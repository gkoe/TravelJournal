namespace TravelJournal.Core.Models;

public sealed class HeicItem
{
    public required string    Filename { get; init; }
    public required string    FullPath { get; init; }
    public required DateTime? DateTime { get; init; }
}
