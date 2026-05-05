using TravelJournal.Wpf.ViewModels;

namespace TravelJournal.Wpf.Services;

public static class RouteStatistics
{
    private const double EarthRadiusKm = 6371.0;

    public static double CalculateDistance(IEnumerable<PhotoViewModel> photos)
    {
        var points = photos
            .Where(p => p.Latitude.HasValue && p.Longitude.HasValue)
            .Select(p => (lat: p.Latitude!.Value, lon: p.Longitude!.Value))
            .ToList();

        double total = 0;
        for (int i = 1; i < points.Count; i++)
            total += Haversine(points[i - 1].lat, points[i - 1].lon, points[i].lat, points[i].lon);

        return total;
    }

    private static double Haversine(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = ToRad(lat2 - lat1);
        var dLon = ToRad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
              + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2))
              * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return EarthRadiusKm * 2 * Math.Asin(Math.Sqrt(a));
    }

    private static double ToRad(double deg) => deg * Math.PI / 180.0;
}
