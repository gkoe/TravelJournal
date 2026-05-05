using TravelJournal.Core.MapRendering.Models;

namespace TravelJournal.Core.MapRendering;

public static class WebMercator
{
    public const int TileSize = 256;

    public static (double X, double Y) LatLonToGlobalPixel(double lat, double lon, int zoom)
    {
        double scale = Math.Pow(2, zoom) * TileSize;
        double x = (lon + 180.0) / 360.0 * scale;
        double latRad = lat * Math.PI / 180.0;
        double y = (1.0 - Math.Log(Math.Tan(latRad) + 1.0 / Math.Cos(latRad)) / Math.PI) / 2.0 * scale;
        return (x, y);
    }

    public static (int TileX, int TileY) GlobalPixelToTile(double x, double y)
        => ((int)(x / TileSize), (int)(y / TileSize));

    public static int CalculateZoom(MapBounds bounds, int targetWidthPx, int targetHeightPx)
    {
        for (int zoom = 18; zoom >= 0; zoom--)
        {
            var (x1, y1) = LatLonToGlobalPixel(bounds.MaxLat, bounds.MinLon, zoom);
            var (x2, y2) = LatLonToGlobalPixel(bounds.MinLat, bounds.MaxLon, zoom);
            if (Math.Abs(x2 - x1) <= targetWidthPx && Math.Abs(y2 - y1) <= targetHeightPx)
                return zoom;
        }
        return 0;
    }
}
