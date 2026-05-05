using TravelJournal.Core.MapRendering;
using TravelJournal.Core.MapRendering.Caching;
using TravelJournal.Core.MapRendering.TileSources;
using Microsoft.Extensions.Configuration;
using System.IO;
using System.Net.Http;

namespace TravelJournal.Wpf.Services;

public sealed class MapRendererFactory : IMapRendererFactory
{
    private readonly IHttpClientFactory _httpClientFactory;

    public MapRendererFactory(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    public IMapRenderer Create(string photoFolder, Action<string>? statusCallback = null)
        => Create(photoFolder, LoadBaseOptions(), statusCallback);

    public IMapRenderer Create(string photoFolder, MapRenderingOptions options, Action<string>? statusCallback = null)
    {
        ITileSource tileSource;
        if (!string.IsNullOrEmpty(options.CustomTileUrlTemplate))
        {
            var http = _httpClientFactory.CreateClient("tiles");
            tileSource = new MapTilerTileSource(http, options);
        }
        else if (!string.IsNullOrEmpty(options.MapTilerApiKey))
        {
            var http = _httpClientFactory.CreateClient("tiles");
            tileSource = new MapTilerTileSource(http, options);
        }
        else
        {
            statusCallback?.Invoke("Kein MapTiler-Key konfiguriert — verwende OpenStreetMap-Standard");
            tileSource = new OsmTileSource(_httpClientFactory, ResolveContactEmail());
        }

        var cacheRoot = Path.Combine(photoFolder, ".tile-cache");
        var cache     = new FileTileCache(cacheRoot);

        return new TileMapRenderer(tileSource, cache, options);
    }

    public MapRenderingOptions LoadBaseOptions()
    {
        var apiKey = ResolveApiKey();

        try
        {
            var section = LoadConfig().GetSection("MapRendering");
            return new MapRenderingOptions
            {
                MapTilerApiKey         = apiKey,
                StyleId                = section["StyleId"]                ?? "outdoor-v2",
                Language               = section["Language"]               ?? "de",
                BoundsPaddingFraction  = TryParseDouble(section["BoundsPaddingFraction"], 0.12),
                TargetWidthPx          = TryParseInt(section["TargetWidthPx"],            1600),
                TargetHeightPx         = TryParseInt(section["TargetHeightPx"],           1200),
                StopThreshold          = TimeSpan.FromMinutes(TryParseInt(section["StopThresholdMinutes"], 30)),
                AddFinalSummaryMap     = TryParseBool(section["AddFinalSummaryMap"],      true),
                CustomTileUrlTemplate  = section["CustomTileUrlTemplate"],
            };
        }
        catch
        {
            return new MapRenderingOptions { MapTilerApiKey = apiKey };
        }
    }

    private static string? ResolveApiKey()
    {
        var key = Environment.GetEnvironmentVariable("MAPTILER_API_KEY");
        if (!string.IsNullOrEmpty(key)) return key;

        try
        {
            key = LoadConfig()["MapTiler:ApiKey"];
        }
        catch { }

        return string.IsNullOrEmpty(key) ? null : key;
    }

    private static string ResolveContactEmail()
    {
        try
        {
            var email = LoadConfig()["Nominatim:ContactEmail"];
            if (!string.IsNullOrEmpty(email)) return email;
        }
        catch { }

        return "kontakt@example.org";
    }

    private static IConfiguration LoadConfig() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .Build();

    private static double TryParseDouble(string? s, double fallback)
        => double.TryParse(s, System.Globalization.NumberStyles.Any,
                           System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;

    private static int TryParseInt(string? s, int fallback)
        => int.TryParse(s, out var v) ? v : fallback;

    private static bool TryParseBool(string? s, bool fallback)
        => bool.TryParse(s, out var v) ? v : fallback;
}
