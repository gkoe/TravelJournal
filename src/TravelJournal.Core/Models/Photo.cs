namespace TravelJournal.Core.Models;

public class Photo
{
    public string Filename { get; set; } = string.Empty;
    public DateTime? DateTime { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? Altitude { get; set; }
    public PhotoState State { get; set; } = PhotoState.None;
    public string? Title { get; set; }
    public string? Description { get; set; }
    public string? Location { get; set; }
    public long?   FileSizeBytes { get; set; }
    public int?    PixelWidth    { get; set; }
    public int?    PixelHeight   { get; set; }
}
