using TravelJournal.Core.MapRendering.Models;
using System.Net.Http;

namespace TravelJournal.Core.MapRendering.TileSources;

public sealed class OsmTileSource : ITileSource
{
    private readonly HttpClient _client;

    public string ProviderId      => "osm";
    public string AttributionText => "© OpenStreetMap contributors";

    public OsmTileSource(IHttpClientFactory httpClientFactory, string contactEmail)
    {
        _client = httpClientFactory.CreateClient("tiles");
        // OSM tile usage policy requires a contact address in the User-Agent
        _client.DefaultRequestHeaders.UserAgent.Clear();
        _client.DefaultRequestHeaders.UserAgent.ParseAdd($"TravelJournal/1.0 ({contactEmail})");
    }

    public async Task<byte[]> GetTileAsync(MapTile tile, CancellationToken ct)
    {
        var url      = $"https://tile.openstreetmap.org/{tile.Z}/{tile.X}/{tile.Y}.png";
        var response = await _client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(ct);
    }
}
