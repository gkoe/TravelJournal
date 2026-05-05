using TravelJournal.Core.MapRendering.Caching;
using TravelJournal.Core.MapRendering.Models;
using TravelJournal.Core.MapRendering.TileSources;
using TravelJournal.Core.Models;
using SixLabors.Fonts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using IoPath = System.IO.Path;

namespace TravelJournal.Core.MapRendering;

public sealed class TileMapRenderer : IMapRenderer
{
    private readonly ITileSource          _tileSource;
    private readonly ITileCache           _cache;
    private readonly MapRenderingOptions  _options;
    private readonly StopDetector         _stopDetector = new();

    public TileMapRenderer(ITileSource tileSource, ITileCache cache, MapRenderingOptions options)
    {
        _tileSource = tileSource;
        _cache      = cache;
        _options    = options;
    }

    public async Task<int> RenderAllAsync(
        IReadOnlyList<Photo> allPhotosWithGps,
        string outputFolder,
        IProgress<MapRenderProgress>? progress = null,
        CancellationToken ct = default)
    {
        // Caller already ensures only GPS photos are passed, but we normalise here too
        var gpsPhotos = allPhotosWithGps
            .Where(p => p.Latitude.HasValue && p.Longitude.HasValue && p.DateTime.HasValue)
            .OrderBy(p => p.DateTime)
            .ToList();

        if (gpsPhotos.Count < 2) return 0;

        // Bounds and zoom derived from ALL GPS photos
        var rawBounds = MapBounds.FromPhotos(gpsPhotos);
        var bounds    = rawBounds.WithPadding(_options.BoundsPaddingFraction);   ///!
        var zoom      = WebMercator.CalculateZoom(bounds, _options.TargetWidthPx, _options.TargetHeightPx);

        // Tile range — expand to always cover the full target output size
        var (tlGx, tlGy) = WebMercator.LatLonToGlobalPixel(bounds.MaxLat, bounds.MinLon, zoom);
        var (brGx, brGy) = WebMercator.LatLonToGlobalPixel(bounds.MinLat, bounds.MaxLon, zoom);
        double cenGx = (tlGx + brGx) / 2.0;
        double cenGy = (tlGy + brGy) / 2.0;
        double rangeMinGx = Math.Min(tlGx, cenGx - _options.TargetWidthPx  / 2.0);
        double rangeMinGy = Math.Min(tlGy, cenGy - _options.TargetHeightPx / 2.0);
        double rangeMaxGx = Math.Max(brGx, cenGx + _options.TargetWidthPx  / 2.0);
        double rangeMaxGy = Math.Max(brGy, cenGy + _options.TargetHeightPx / 2.0);
        var (tileXMin, tileYMin) = WebMercator.GlobalPixelToTile(rangeMinGx, rangeMinGy);
        var (tileXMax, tileYMax) = WebMercator.GlobalPixelToTile(rangeMaxGx, rangeMaxGy);

        var tiles = new List<MapTile>();
        for (int tx = tileXMin; tx <= tileXMax; tx++)
            for (int ty = tileYMin; ty <= tileYMax; ty++)
                tiles.Add(new MapTile(zoom, tx, ty));

        // Download tiles (parallel, with cache)
        var tileData   = new Dictionary<MapTile, byte[]>(tiles.Count);
        int downloaded = 0;
        var semaphore  = new SemaphoreSlim(_options.MaxParallelTileDownloads);

        await Task.WhenAll(tiles.Select(async tile =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                ct.ThrowIfCancellationRequested();
                var key  = tile.CacheKey(_tileSource.ProviderId);
                var data = await _cache.TryGetAsync(key, ct)
                           ?? await DownloadAndCacheAsync(tile, key, ct);
                lock (tileData) tileData[tile] = data;
                var n = Interlocked.Increment(ref downloaded);
                progress?.Report(new MapRenderProgress("tiles", n, tiles.Count, null));
            }
            finally { semaphore.Release(); }
        }));

        ct.ThrowIfCancellationRequested();
        progress?.Report(new MapRenderProgress("compose", 0, 1, null));

        // Compose mosaic
        int mosaicW = (tileXMax - tileXMin + 1) * WebMercator.TileSize;
        int mosaicH = (tileYMax - tileYMin + 1) * WebMercator.TileSize;

        using var mosaic = new Image<Rgba32>(mosaicW, mosaicH, Color.LightGray);
        foreach (var (tile, data) in tileData)
        {
            int px = (tile.X - tileXMin) * WebMercator.TileSize;
            int py = (tile.Y - tileYMin) * WebMercator.TileSize;
            using var tileImg = Image.Load<Rgba32>(data);
            mosaic.Mutate(ctx => ctx.DrawImage(tileImg, new Point(px, py), 1.0f));
        }

        // Crop centered on bounds
        double moOriGx = tileXMin * (double)WebMercator.TileSize;
        double moOriGy = tileYMin * (double)WebMercator.TileSize;

        int cropX = Clamp((int)(cenGx - moOriGx - _options.TargetWidthPx  / 2.0), 0, mosaicW - _options.TargetWidthPx);
        int cropY = Clamp((int)(cenGy - moOriGy - _options.TargetHeightPx / 2.0), 0, mosaicH - _options.TargetHeightPx);

        using var baseImage = mosaic.Clone(ctx =>
            ctx.Crop(new Rectangle(cropX, cropY, _options.TargetWidthPx, _options.TargetHeightPx)));

        DrawAttribution(baseImage, _tileSource.AttributionText);

        // Coordinate converter: lat/lon → image pixel
        double offsetGx = moOriGx + cropX;
        double offsetGy = moOriGy + cropY;
        PointF ToImg(double lat, double lon)
        {
            var (gx, gy) = WebMercator.LatLonToGlobalPixel(lat, lon, zoom);
            return new PointF((float)(gx - offsetGx), (float)(gy - offsetGy));
        }

        // Stops derived only from SELECTED photos
        var selectedSorted = gpsPhotos
            .Where(p => p.State == PhotoState.Selected)
            .ToList();

        var stops = _stopDetector.DetectStops(selectedSorted, _options.StopThreshold, _options.AddFinalSummaryMap);
        if (stops.Count == 0) return 0;

        Directory.CreateDirectory(outputFolder);
        int rendered = 0;

        for (int s = 0; s < stops.Count; s++)
        {
            ct.ThrowIfCancellationRequested();
            var stop      = stops[s];
            using var img = baseImage.Clone();

            // Polyline: ALL GPS photos up to this stop's timestamp (not just selected ones)
            var routePixels = gpsPhotos
                .Where(p => p.DateTime!.Value <= stop.Timestamp)
                .Select(p => ToImg(p.Latitude!.Value, p.Longitude!.Value))
                .ToList();

            var simplified = SimplifyByPixelDistance(routePixels);
            if (simplified.Length >= 2)
            {
                img.Mutate(ctx =>
                {
                    ctx.DrawLine(new SolidPen(MapStyle.RouteOuter, MapStyle.RouteOuterWidth), simplified);
                    ctx.DrawLine(new SolidPen(MapStyle.RouteInner, MapStyle.RouteInnerWidth), simplified);
                });
            }

            // Past stop markers (blue)
            for (int p = 0; p < s; p++)
                DrawDot(img, ToImg(stops[p].Latitude, stops[p].Longitude),
                    MapStyle.PastStopRadius, MapStyle.PastStopFill,
                    MapStyle.PastStopBorder, MapStyle.PastStopBorderWidth);

            // Current stop marker (red)
            DrawShadowedDot(img, ToImg(stop.Latitude, stop.Longitude),
                MapStyle.CurrentStopRadius, MapStyle.CurrentStopFill,
                MapStyle.CurrentStopBorder, MapStyle.CurrentStopBorderWidth);

            var filename   = $"map_{stop.Timestamp:yyyy-MM-ddTHH-mm-ss}.png";
            var outputPath = IoPath.Combine(outputFolder, filename);
            await img.SaveAsPngAsync(outputPath, ct);
            File.SetLastWriteTime(outputPath, stop.Timestamp);
            File.SetCreationTime(outputPath, stop.Timestamp);

            rendered++;
            progress?.Report(new MapRenderProgress("render", rendered, stops.Count, $"Karte {rendered}/{stops.Count}"));
        }

        return rendered;
    }

    // ── Helpers ───────────────────────────────────────────────────────

    private static PointF[] SimplifyByPixelDistance(IReadOnlyList<PointF> points, float minDistancePx = 2.0f)
    {
        if (points.Count < 2) return points.ToArray();
        var result = new List<PointF> { points[0] };
        for (int i = 1; i < points.Count - 1; i++)
        {
            var last = result[^1];
            var curr = points[i];
            float dx = curr.X - last.X;
            float dy = curr.Y - last.Y;
            if (dx * dx + dy * dy >= minDistancePx * minDistancePx)
                result.Add(curr);
        }
        result.Add(points[^1]);
        return result.ToArray();
    }

    private async Task<byte[]> DownloadAndCacheAsync(MapTile tile, string key, CancellationToken ct)
    {
        var data = await _tileSource.GetTileAsync(tile, ct);
        await _cache.PutAsync(key, data, ct);
        return data;
    }

    private static void DrawDot(Image<Rgba32> image, PointF center,
        float radius, Color fill, Color border, float borderWidth)
    {
        var circle = new EllipsePolygon(center, new SizeF(radius * 2, radius * 2));
        image.Mutate(ctx =>
        {
            ctx.Fill(new SolidBrush(fill), circle);
            ctx.Draw(new SolidPen(border, borderWidth), circle);
        });
    }

    private static void DrawShadowedDot(Image<Rgba32> image, PointF center,
        float radius, Color fill, Color border, float borderWidth)
    {
        var shadow = new Color(new Rgba32(0, 0, 0, 60));
        for (int i = 1; i <= 2; i++)
        {
            var sc = new PointF(center.X, center.Y + i * 2);
            image.Mutate(ctx => ctx.Fill(new SolidBrush(shadow),
                new EllipsePolygon(sc, new SizeF(radius * 2, radius * 2))));
        }
        DrawDot(image, center, radius, fill, border, borderWidth);
    }

    private static void DrawAttribution(Image<Rgba32> image, string text)
    {
        var font = TryGetFont();
        if (font is null) return;

        var measured = TextMeasurer.MeasureSize(text, new TextOptions(font));
        const float pad = 8f;
        float rw = measured.Width  + pad * 2;
        float rh = measured.Height + pad * 2;
        float rx = image.Width  - rw - 4f;
        float ry = image.Height - rh - 4f;

        image.Mutate(ctx =>
        {
            ctx.Fill(new SolidBrush(MapStyle.AttributionBackground),
                new RectangularPolygon(rx, ry, rw, rh));
            ctx.DrawText(text, font, MapStyle.AttributionText, new PointF(rx + pad, ry + pad));
        });
    }

    private static Font? TryGetFont()
    {
        foreach (var name in new[] { "Segoe UI", "Arial", "Helvetica", "DejaVu Sans" })
        {
            if (SystemFonts.TryGet(name, out var fam))
                return fam.CreateFont(MapStyle.AttributionFontSize);
        }
        var families = SystemFonts.Families.ToList();
        return families.Count > 0 ? families[0].CreateFont(MapStyle.AttributionFontSize) : null;
    }

    private static int Clamp(int value, int min, int max)
        => Math.Max(min, Math.Min(max, value));
}
