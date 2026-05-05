using TravelJournal.Core.Models;

namespace TravelJournal.Core.MapRendering;

public interface IMapRenderer
{
    Task<int> RenderAllAsync(
        IReadOnlyList<Photo> allPhotosWithGps,
        string outputFolder,
        IProgress<MapRenderProgress>? progress = null,
        CancellationToken ct = default);
}
