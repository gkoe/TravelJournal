using TravelJournal.Core.Models;

namespace TravelJournal.Core.MapRendering.Models;

public sealed record MapBounds(double MinLat, double MinLon, double MaxLat, double MaxLon)
{
    public static MapBounds FromPhotos(IEnumerable<Photo> photos)
    {
        var pts = photos.Where(p => p.Latitude.HasValue && p.Longitude.HasValue).ToList();
        if (pts.Count == 0)
            throw new InvalidOperationException("Keine Fotos mit GPS-Daten.");

        return new MapBounds(
            pts.Min(p => p.Latitude!.Value),
            pts.Min(p => p.Longitude!.Value),
            pts.Max(p => p.Latitude!.Value),
            pts.Max(p => p.Longitude!.Value));
    }

    public MapBounds WithPadding(double fraction)
    {
        var latRange = MaxLat - MinLat;
        var lonRange = MaxLon - MinLon;
        const double minRange = 0.001;
        var latPad = Math.Max(latRange, minRange) * fraction;
        var lonPad = Math.Max(lonRange, minRange) * fraction;
        return new MapBounds(MinLat - latPad, MinLon - lonPad, MaxLat + latPad, MaxLon + lonPad);
    }
}
