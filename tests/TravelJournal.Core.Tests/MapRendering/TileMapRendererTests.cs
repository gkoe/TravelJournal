using TravelJournal.Core.MapRendering;
using TravelJournal.Core.MapRendering.Caching;
using TravelJournal.Core.MapRendering.Models;
using TravelJournal.Core.MapRendering.TileSources;
using TravelJournal.Core.Models;
using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace TravelJournal.Core.Tests.MapRendering;

public class TileMapRendererTests : IDisposable
{
    private readonly string _outputDir = Path.Combine(Path.GetTempPath(), $"map-renderer-test-{Guid.NewGuid()}");
    private readonly string _cacheDir  = Path.Combine(Path.GetTempPath(), $"map-cache-test-{Guid.NewGuid()}");

    public void Dispose()
    {
        TryDelete(_outputDir);
        TryDelete(_cacheDir);
    }

    private static void TryDelete(string dir)
    {
        if (Directory.Exists(dir))
            try { Directory.Delete(dir, recursive: true); } catch { /* best-effort */ }
    }

    private TileMapRenderer BuildRenderer(int stopThresholdMinutes = 30) => new(
        new FakeTileSource(),
        new FileTileCache(_cacheDir),
        new MapRenderingOptions
        {
            TargetWidthPx            = 1600,
            TargetHeightPx           = 1200,
            StopThreshold            = TimeSpan.FromMinutes(stopThresholdMinutes),
            BoundsPaddingFraction    = 0.12,
            MaxParallelTileDownloads = 4,
            AddFinalSummaryMap       = true,
        });

    private static Photo MakePhoto(DateTime dt, double lat, double lon, string? location = null)
        => new() { DateTime = dt, Latitude = lat, Longitude = lon, State = PhotoState.Selected, Location = location };

    // ── Tests ─────────────────────────────────────────────────────────

    [Fact]
    public async Task FewerThan2GpsPhotos_Returns0()
    {
        var renderer = BuildRenderer();
        var photos   = new[] { MakePhoto(DateTime.Now, 48.0, 16.0) };

        var result = await renderer.RenderAllAsync(photos, _outputDir);

        result.RenderedCount.Should().Be(0);
        result.MapPhotos.Should().BeEmpty();
        Directory.Exists(_outputDir).Should().BeFalse();
    }

    [Fact]
    public async Task FivePhotosWithTwoStopsPlusFinalSummary_Produces3Pngs()
    {
        var renderer = BuildRenderer(stopThresholdMinutes: 30);
        var t0       = new DateTime(2024, 7, 1, 8, 0, 0);
        var photos   = new[]
        {
            MakePhoto(t0,                  48.200, 16.370, "Wien"),    // 0
            MakePhoto(t0.AddMinutes(10),   48.210, 16.380, "Wien"),    // 1  ← stop after 1: Location="Wien"
            MakePhoto(t0.AddMinutes(50),   48.220, 16.390, "Graz"),    // 2  gap 40 min
            MakePhoto(t0.AddMinutes(60),   48.230, 16.400, "Graz"),    // 3  ← stop after 3: Location="Graz"
            MakePhoto(t0.AddMinutes(110),  48.240, 16.410, "Linz"),    // 4  gap 50 min; final summary: Location="Linz"
        };

        var result = await renderer.RenderAllAsync(photos, _outputDir);

        result.RenderedCount.Should().Be(3);
        Directory.GetFiles(_outputDir, "map_*.png").Should().HaveCount(3);
        result.MapPhotos.Should().HaveCount(3);
    }

    [Fact]
    public async Task EachPng_IsExactly1600x1200()
    {
        var renderer = BuildRenderer(stopThresholdMinutes: 5);
        var t0       = new DateTime(2024, 7, 1, 8, 0, 0);
        var photos   = new[]
        {
            MakePhoto(t0,                48.200, 16.370),
            MakePhoto(t0.AddMinutes(10), 48.210, 16.380),
        };

        await renderer.RenderAllAsync(photos, _outputDir);

        foreach (var file in Directory.GetFiles(_outputDir, "map_*.png"))
        {
            using var img = Image.Load<Rgba32>(file);
            img.Width .Should().Be(1600);
            img.Height.Should().Be(1200);
        }
    }

    [Fact]
    public async Task FileNames_MatchPattern()
    {
        var renderer = BuildRenderer(stopThresholdMinutes: 5);
        var t0       = new DateTime(2024, 7, 1, 8, 0, 0);
        var photos   = new[]
        {
            MakePhoto(t0,                48.200, 16.370),
            MakePhoto(t0.AddMinutes(10), 48.210, 16.380),
        };

        await renderer.RenderAllAsync(photos, _outputDir);

        foreach (var file in Directory.GetFiles(_outputDir, "*.png"))
            Path.GetFileName(file).Should().MatchRegex(
                @"^map_\d{4}-\d{2}-\d{2}T\d{2}-\d{2}-\d{2}(_[A-Za-z0-9]+)?\.png$");
    }

    [Fact]
    public async Task LastWriteTime_ApproximatesStopTimestamp()
    {
        // threshold=30 min, gap=10 min → only the final-summary stop is generated
        var renderer = BuildRenderer(stopThresholdMinutes: 30);
        var t0       = new DateTime(2024, 7, 1, 8, 0, 0, DateTimeKind.Local);
        var photos   = new[]
        {
            MakePhoto(t0,                48.200, 16.370),
            MakePhoto(t0.AddMinutes(10), 48.210, 16.380),
        };

        await renderer.RenderAllAsync(photos, _outputDir);

        var files = Directory.GetFiles(_outputDir, "map_*.png");
        files.Should().HaveCount(1);

        var lwt = File.GetLastWriteTime(files[0]);
        lwt.Should().BeCloseTo(t0.AddMinutes(10).AddSeconds(1), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task NoSelectedPhotos_Returns0EvenWithManyGpsPhotos()
    {
        var renderer = BuildRenderer(stopThresholdMinutes: 5);
        var t0 = new DateTime(2024, 7, 1, 8, 0, 0);
        var photos = Enumerable.Range(0, 6).Select(i => new Photo
        {
            DateTime  = t0.AddMinutes(i * 5),
            Latitude  = 48.2 + i * 0.001,
            Longitude = 16.37 + i * 0.001,
            State     = PhotoState.None  // keines ausgewählt
        }).ToArray();

        var result = await renderer.RenderAllAsync(photos, _outputDir);

        result.RenderedCount.Should().Be(0);
    }

    [Fact]
    public async Task AllGpsPhotosUsedForRoute_OnlySelectedDetermineStops()
    {
        // 6 GPS-Fotos, davon 2 ausgewählt — Stopp-Erkennung basiert nur auf den Selected-Fotos.
        // Ausgewählte Fotos bei t0+4 und t0+10, Abstand = 6 min > 5 min Schwelle → 1 Zwischen-Stopp + 1 Final = 2 Karten.
        var renderer = BuildRenderer(stopThresholdMinutes: 5);
        var t0 = new DateTime(2024, 7, 1, 8, 0, 0);
        var photos = new[]
        {
            new Photo { DateTime = t0,                Latitude = 48.200, Longitude = 16.370, State = PhotoState.None },
            new Photo { DateTime = t0.AddMinutes(2),  Latitude = 48.201, Longitude = 16.371, State = PhotoState.None },
            new Photo { DateTime = t0.AddMinutes(4),  Latitude = 48.202, Longitude = 16.372, State = PhotoState.Selected, Location = "Ort-A" },
            new Photo { DateTime = t0.AddMinutes(6),  Latitude = 48.203, Longitude = 16.373, State = PhotoState.None },
            new Photo { DateTime = t0.AddMinutes(8),  Latitude = 48.204, Longitude = 16.374, State = PhotoState.None },
            new Photo { DateTime = t0.AddMinutes(10), Latitude = 48.205, Longitude = 16.375, State = PhotoState.Selected, Location = "Ort-B" },
        };

        var result = await renderer.RenderAllAsync(photos, _outputDir);

        // 1 Zwischen-Stopp (Lücke 6 min zwischen den 2 selected Fotos) + 1 Final-Summary = 2
        // Beide Stopps haben verschiedene Locations → kein Dedup
        result.RenderedCount.Should().Be(2);
        Directory.GetFiles(_outputDir, "map_*.png").Should().HaveCount(2);
    }

    [Fact]
    public async Task LocationInFilename_WhenPhotoHasLocation()
    {
        var renderer = BuildRenderer(stopThresholdMinutes: 5);
        var t0       = new DateTime(2026, 4, 28, 10, 34, 0);
        var photos   = new[]
        {
            MakePhoto(t0,                48.200, 13.370, "Chiusaforte / Scluse"),
            MakePhoto(t0.AddMinutes(10), 48.210, 13.380, "Chiusaforte / Scluse"),
        };

        await renderer.RenderAllAsync(photos, _outputDir);

        var files = Directory.GetFiles(_outputDir, "map_*.png");
        files.Should().HaveCount(1);
        Path.GetFileName(files[0]).Should().Contain("ChiusaforteScluse");
    }

    [Fact]
    public async Task ConsecutiveSameLocation_ProducesOnlyOneMap()
    {
        var renderer = BuildRenderer(stopThresholdMinutes: 30);
        var t0       = new DateTime(2026, 4, 28, 10, 0, 0);
        // Three photos: gap >30 min after index 0 and after index 1
        // Both stops → same location → second stop deduplicated → only 1 map
        var photos   = new[]
        {
            MakePhoto(t0,                48.200, 13.370, "Villach"),
            MakePhoto(t0.AddMinutes(35), 48.210, 13.380, "Villach"),
            MakePhoto(t0.AddMinutes(80), 48.220, 13.390, "Villach"),
        };

        var result = await renderer.RenderAllAsync(photos, _outputDir);

        // Stop after index 0 + Stop after index 1, both "Villach" → dedup to 1
        // Final summary also "Villach" → also deduped → only 1 total
        result.RenderedCount.Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task FinalSummary_SameLocationAsPreviousStop_IsSkipped()
    {
        var renderer = BuildRenderer(stopThresholdMinutes: 30);
        var t0       = new DateTime(2026, 4, 28, 8, 0, 0);
        // One stop (gap >30 min), then final summary at same location
        var photos   = new[]
        {
            MakePhoto(t0,                48.200, 13.370, "Grado"),
            MakePhoto(t0.AddMinutes(35), 48.200, 13.370, "Grado"),  // creates stop after index 0
        };
        // With AddFinalSummaryMap=true, final summary would be index 1 ("Grado")
        // which equals the previous stop → deduplicated → 1 map only

        var result = await renderer.RenderAllAsync(photos, _outputDir);

        result.RenderedCount.Should().Be(1);
        var files = Directory.GetFiles(_outputDir, "map_*.png");
        Path.GetFileName(files[0]).Should().Contain("Grado");
    }

    [Fact]
    public async Task MapPhotos_HaveEntryTypeMap()
    {
        var renderer = BuildRenderer(stopThresholdMinutes: 5);
        var t0       = new DateTime(2024, 7, 1, 8, 0, 0);
        var photos   = new[]
        {
            MakePhoto(t0,                48.200, 16.370, "Wien"),
            MakePhoto(t0.AddMinutes(10), 48.210, 16.380, "Wien"),
        };

        var result = await renderer.RenderAllAsync(photos, _outputDir);

        result.MapPhotos.Should().AllSatisfy(p => p.EntryType.Should().Be(EntryType.Map));
        result.MapPhotos.Should().AllSatisfy(p => p.State.Should().Be(PhotoState.Selected));
    }

    [Fact]
    public async Task MapPhoto_Timestamp_IsBeforeFirstPhotoAtLocation()
    {
        // threshold=5min, gap=10min → 1 intermediate stop (gap) + final-summary → dedup keeps gap stop
        var renderer = BuildRenderer(stopThresholdMinutes: 5);
        var t0       = new DateTime(2024, 7, 1, 8, 0, 0);
        var photos   = new[]
        {
            MakePhoto(t0,                48.200, 16.370, "Wien"),
            MakePhoto(t0.AddMinutes(10), 48.210, 16.380, "Wien"),
        };

        var result = await renderer.RenderAllAsync(photos, _outputDir);

        result.MapPhotos.Should().HaveCount(1);
        // Intermediate stop: 1 second before first photo at "Wien" (= t0)
        result.MapPhotos[0].DateTime.Should().Be(t0.AddSeconds(-1));
    }

    [Fact]
    public async Task FinalSummary_Timestamp_IsAfterLastPhoto()
    {
        // threshold=30min, gap=10min → no intermediate stop; only final-summary (IsFinalSummary=true)
        // Final-summary keeps stop.Timestamp (= last photo + 1s), NOT before first photo at location
        var renderer = BuildRenderer(stopThresholdMinutes: 30);
        var t0       = new DateTime(2024, 7, 1, 8, 0, 0);
        var photos   = new[]
        {
            MakePhoto(t0,                48.200, 16.370, "Graz"),
            MakePhoto(t0.AddMinutes(10), 48.210, 16.380, "Graz"),
        };

        var result = await renderer.RenderAllAsync(photos, _outputDir);

        result.MapPhotos.Should().HaveCount(1);
        // Final-summary: stop.Timestamp = lastPhoto.DateTime + 1s = t0+10min+1s
        result.MapPhotos[0].DateTime.Should().Be(t0.AddMinutes(10).AddSeconds(1));
    }

    [Fact]
    public async Task IntermediateStop_LocationInMapPhoto_IsFullOriginalString()
    {
        var renderer = BuildRenderer(stopThresholdMinutes: 5);
        var t0       = new DateTime(2024, 7, 1, 8, 0, 0);
        var photos   = new[]
        {
            MakePhoto(t0,                48.200, 16.370, "Chiusaforte / Scluse"),
            MakePhoto(t0.AddMinutes(10), 48.210, 16.380, "Linz"),
        };

        var result = await renderer.RenderAllAsync(photos, _outputDir);

        // Intermediate stop should have full location string (not sanitized FilenameSafeName)
        var stopMap = result.MapPhotos.FirstOrDefault(p =>
            p.Filename.Contains("ChiusaforteScluse"));
        stopMap.Should().NotBeNull();
        stopMap!.Location.Should().Be("Chiusaforte / Scluse");
    }

    // ── Fake tile source ─────────────────────────────────────────────

    private sealed class FakeTileSource : ITileSource
    {
        public string ProviderId      => "fake";
        public string AttributionText => "Fake tiles";

        public Task<byte[]> GetTileAsync(MapTile tile, CancellationToken ct)
        {
            // Generate a solid-color 256×256 PNG; color varies by (x,y) to be distinguishable
            byte r = (byte)((tile.X * 37) & 0xFF);
            byte g = (byte)((tile.Y * 71) & 0xFF);
            using var img = new Image<Rgba32>(256, 256, new Rgba32(r, g, 180));
            using var ms  = new MemoryStream();
            img.SaveAsPng(ms);
            return Task.FromResult(ms.ToArray());
        }
    }
}
