using TravelJournal.Core.MapRendering.Models;
using System.Net;
using System.Net.Http;

namespace TravelJournal.Core.MapRendering.TileSources;

public sealed class MapTilerTileSource : ITileSource
{
    private readonly HttpClient _client;
    private readonly MapRenderingOptions _options;

    public string ProviderId =>
        !string.IsNullOrEmpty(_options.CustomTileUrlTemplate)
            ? "maptiler-custom"
            : $"maptiler-{_options.StyleId}-{_options.Language}";

    public string AttributionText => "© MapTiler © OpenStreetMap contributors";

    public MapTilerTileSource(HttpClient client, MapRenderingOptions options)
    {
        if (string.IsNullOrEmpty(options.CustomTileUrlTemplate)
            && string.IsNullOrEmpty(options.MapTilerApiKey))
            throw new ArgumentException("MapTilerApiKey is required when CustomTileUrlTemplate is not set.");

        _client  = client;
        _options = options;
    }

    private string BuildUrl(MapTile tile)
    {
        if (!string.IsNullOrEmpty(_options.CustomTileUrlTemplate))
        {
            return _options.CustomTileUrlTemplate
                .Replace("{z}", tile.Z.ToString())
                .Replace("{x}", tile.X.ToString())
                .Replace("{y}", tile.Y.ToString())
                .Replace("{key}",  _options.MapTilerApiKey ?? "")
                .Replace("{lang}", _options.Language);
        }

        var url = $"https://api.maptiler.com/maps/{_options.StyleId}/256/{tile.Z}/{tile.X}/{tile.Y}.png?key={_options.MapTilerApiKey}";
        if (!string.IsNullOrEmpty(_options.Language))
            url += $"&lang={_options.Language}";
        return url;
    }

    public async Task<byte[]> GetTileAsync(MapTile tile, CancellationToken ct)
    {
        var url      = BuildUrl(tile);
        var response = await _client.GetAsync(url, ct);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            await Task.Delay(1000, ct);
            response = await _client.GetAsync(url, ct);
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
            throw new InvalidOperationException(
                "MapTiler API-Key ungültig oder nicht berechtigt (403 Forbidden). " +
                "Bitte prüfen: Key in appsettings.json korrekt eingetragen? " +
                "Im MapTiler-Dashboard unter Account → Keys: keine URL-Einschränkungen gesetzt?");

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct);
    }
}
