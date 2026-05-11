using TravelJournal.Core.Models;

namespace TravelJournal.Core.MapRendering;

public sealed record MapRenderResult(
    int RenderedCount,
    IReadOnlyList<Photo> MapPhotos);

public interface IMapRenderer
{
    Task<MapRenderResult> RenderAllAsync(
        IReadOnlyList<Photo> allPhotosWithGps,
        string outputFolder,
        IProgress<MapRenderProgress>? progress = null,
        CancellationToken ct = default);
}
