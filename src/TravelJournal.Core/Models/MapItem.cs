namespace TravelJournal.Core.Models;

public sealed class MapItem
{
    public required string   Filename { get; init; }
    public required DateTime DateTime { get; init; }
    public required string   FullPath { get; init; }
}
