namespace TravelJournal.WebExporter.Models;

public sealed class PresentationManifest
{
    public required string                          Title            { get; init; }
    public required string                          GeneratedAt      { get; init; }
    public required int                             PhotoDurationMs  { get; init; }
    public required int                             OverlayVisibleMs { get; init; }
    public required IReadOnlyList<PresentationItem> Items            { get; init; }
}
