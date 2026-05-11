using TravelJournal.Core.MapRendering.Models;
using TravelJournal.Core.Models;

namespace TravelJournal.Core.MapRendering;

public sealed class StopDetector
{
    public IReadOnlyList<StopPoint> DetectStops(
        IReadOnlyList<Photo> photosSortedByDateTime,
        TimeSpan threshold,
        bool addFinalSummary)
    {
        // Only photos with GPS and DateTime can produce meaningful stop points
        var gps = photosSortedByDateTime
            .Where(p => p.Latitude.HasValue && p.Longitude.HasValue && p.DateTime.HasValue)
            .ToList();

        var result = new List<StopPoint>();

        for (int i = 0; i < gps.Count - 1; i++)
        {
            var gap = gps[i + 1].DateTime!.Value - gps[i].DateTime!.Value;
            if (gap > threshold)
            {
                result.Add(new StopPoint(
                    Timestamp:  gps[i].DateTime!.Value.AddSeconds(1),
                    Latitude:   gps[i].Latitude!.Value,
                    Longitude:  gps[i].Longitude!.Value,
                    PhotoIndex: i,
                    Location:   gps[i].Location));
            }
        }

        if (addFinalSummary && gps.Count > 0)
        {
            var last = gps[^1];
            result.Add(new StopPoint(
                Timestamp:     last.DateTime!.Value.AddSeconds(1),
                Latitude:      last.Latitude!.Value,
                Longitude:     last.Longitude!.Value,
                PhotoIndex:    gps.Count - 1,
                Location:      last.Location,
                IsFinalSummary: true));
        }

        return result;
    }
}
