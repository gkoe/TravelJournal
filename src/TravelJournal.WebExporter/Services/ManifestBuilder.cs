using TravelJournal.Core.Models;
using TravelJournal.WebExporter.Models;

namespace TravelJournal.WebExporter.Services;

public sealed class ManifestBuilder
{
    public PresentationManifest Build(
        IReadOnlyList<Photo> startPhotos,
        IReadOnlyList<Photo> middlePhotos,   // nur Photos MIT DateTime
        IReadOnlyList<Photo> maps,           // EntryType = Map
        IReadOnlyList<Photo> endPhotos,
        string               title,
        int                  photoDurationMs,
        int                  overlayVisibleMs)
    {
        var items = new List<PresentationItem>();

        // Start (unabhängig von DateTime, sortiert nach DateTime ?? MinValue, dann Filename)
        foreach (var p in startPhotos
            .OrderBy(p => p.DateTime ?? System.DateTime.MinValue)
            .ThenBy(p => p.Filename))
        {
            items.Add(PhotoItem(p, "start"));
        }

        // Middle: Photos (nur mit DateTime) + Karten, chronologisch sortiert
        var middle = new List<PresentationItem>();
        foreach (var p in middlePhotos.Where(p => p.DateTime.HasValue))
            middle.Add(PhotoItem(p, "middle"));
        foreach (var m in maps)
            middle.Add(MapItem(m));
        items.AddRange(middle.OrderBy(i => i.DateTime));

        // End (unabhängig von DateTime, sortiert nach DateTime ?? MaxValue, dann Filename)
        foreach (var p in endPhotos
            .OrderBy(p => p.DateTime ?? System.DateTime.MaxValue)
            .ThenBy(p => p.Filename))
        {
            items.Add(PhotoItem(p, "end"));
        }

        return new PresentationManifest
        {
            Title            = title,
            GeneratedAt      = System.DateTime.UtcNow.ToString("O"),
            PhotoDurationMs  = photoDurationMs,
            OverlayVisibleMs = overlayVisibleMs,
            Items            = items
        };
    }

    private static PresentationItem PhotoItem(Photo p, string role) => new()
    {
        Type        = "photo",
        Role        = role,
        Src         = $"web/photos/{System.IO.Path.GetFileNameWithoutExtension(p.Filename)}.jpg",
        DateTime    = p.DateTime?.ToString("yyyy-MM-ddTHH:mm:ss"),
        Location    = p.Location,
        Title       = p.Title,
        Description = p.Description
    };

    private static PresentationItem MapItem(Photo m) => new()
    {
        Type     = "map",
        Role     = "middle",
        Src      = $"web/maps/{m.Filename}",
        DateTime = m.DateTime?.ToString("yyyy-MM-ddTHH:mm:ss"),
        Location = m.Location,
        Title    = m.Title
    };
}
