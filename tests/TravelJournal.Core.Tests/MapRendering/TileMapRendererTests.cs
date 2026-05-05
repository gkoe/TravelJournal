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

    private static Photo MakePhoto(DateTime dt, double lat, double lon)
        => new() { DateTime = dt, Latitude = lat, Longitude = lon, State = PhotoState.Selected };

    // ── Tests ─────────────────────────────────────────────────────────

    [Fact]
    public async Task FewerThan2GpsPhotos_Returns0()
    {
        var renderer = BuildRenderer();
        var photos   = new[] { MakePhoto(DateTime.Now, 48.0, 16.0) };

        var count = await renderer.RenderAllAsync(photos, _outputDir);

        count.Should().Be(0);
        Directory.Exists(_outputDir).Should().BeFalse();
    }

    [Fact]
    public async Task FivePhotosWithTwoStopsPlusFinalSummary_Produces3Pngs()
    {
        var renderer = BuildRenderer(stopThresholdMinutes: 30);
        var t0       = new DateTime(2024, 7, 1, 8, 0, 0);
        var photos   = new[]
        {
            MakePhoto(t0,                  48.200, 16.370),  // 0
            MakePhoto(t0.AddMinutes(10),   48.210, 16.380),  // 1
            MakePhoto(t0.AddMinutes(50),   48.220, 16.390),  // 2  ← gap 40 min → stop after 1
            MakePhoto(t0.AddMinutes(60),   48.230, 16.400),  // 3
            MakePhoto(t0.AddMinutes(110),  48.240, 16.410),  // 4  ← gap 50 min → stop after 3  + final summary
        };

        var count = await renderer.RenderAllAsync(photos, _outputDir);

        count.Should().Be(3);
        Directory.GetFiles(_outputDir, "map_*.png").Should().HaveCount(3);
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
            Path.GetFileName(file).Should().MatchRegex(@"^map_\d{4}-\d{2}-\d{2}T\d{2}-\d{2}-\d{2}\.png$");
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

        var count = await renderer.RenderAllAsync(photos, _outputDir);

        count.Should().Be(0);
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
            new Photo { DateTime = t0.AddMinutes(4),  Latitude = 48.202, Longitude = 16.372, State = PhotoState.Selected },
            new Photo { DateTime = t0.AddMinutes(6),  Latitude = 48.203, Longitude = 16.373, State = PhotoState.None },
            new Photo { DateTime = t0.AddMinutes(8),  Latitude = 48.204, Longitude = 16.374, State = PhotoState.None },
            new Photo { DateTime = t0.AddMinutes(10), Latitude = 48.205, Longitude = 16.375, State = PhotoState.Selected },
        };

        var count = await renderer.RenderAllAsync(photos, _outputDir);

        // 1 Zwischen-Stopp (Lücke 6 min zwischen den 2 selected Fotos) + 1 Final-Summary = 2
        count.Should().Be(2);
        Directory.GetFiles(_outputDir, "map_*.png").Should().HaveCount(2);
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
