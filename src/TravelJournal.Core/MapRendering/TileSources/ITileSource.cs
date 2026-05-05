using TravelJournal.Core.MapRendering.Models;

namespace TravelJournal.Core.MapRendering.TileSources;

public interface ITileSource
{
    string ProviderId      { get; }
    string AttributionText { get; }
    Task<byte[]> GetTileAsync(MapTile tile, CancellationToken ct);
}
