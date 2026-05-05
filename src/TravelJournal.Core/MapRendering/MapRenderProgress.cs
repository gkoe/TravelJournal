namespace TravelJournal.Core.MapRendering;

public sealed record MapRenderProgress(
    string Stage,
    int Current,
    int Total,
    string? Message);
