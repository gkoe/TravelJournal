namespace TravelJournal.WebExporter.Models;

public sealed class PresentationItem
{
    public required string Type        { get; init; }  // "photo" | "map"
    public required string Role        { get; init; }  // "start" | "middle" | "end"
    public required string Src         { get; init; }
    public string?         DateTime    { get; init; }  // ISO 8601 oder null
    public string?         Location    { get; init; }
    public string?         Title       { get; init; }
    public string?         Description { get; init; }
}
