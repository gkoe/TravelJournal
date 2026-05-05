using TravelJournal.Core.MapRendering;
using TravelJournal.Core.MapRendering.Models;
using FluentAssertions;

namespace TravelJournal.Core.Tests.MapRendering;

public class WebMercatorTests
{
    // Reference: Brandenburger Tor ≈ (52.5163 N, 13.3777 E)
    // OSM tile calculator at zoom 12: tile (2200, 1340)
    [Fact]
    public void LatLonToGlobalPixel_BrandenburgerTor_Zoom12_ApproximatesExpected()
    {
        var (x, y) = WebMercator.LatLonToGlobalPixel(52.5163, 13.3777, 12);

        // Brandenburger Tor is in tile approx (2200, 1343) at zoom 12
        // Accept ±1500 px tolerance (≈ 6 tile-widths) — we test formula direction, not exact rounding
        x.Should().BeApproximately(563015, 1500);
        y.Should().BeApproximately(343900, 1500);
    }

    [Fact]
    public void GlobalPixelToTile_ReturnsCorrectTileIndex()
    {
        var (tx, ty) = WebMercator.GlobalPixelToTile(563200.0, 343040.0);

        tx.Should().Be(2200);
        ty.Should().Be(1340);
    }

    [Theory]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    public void CalculateZoom_50x30kmBoundingBox_ReturnsPlausibleZoom(int expectedMin)
    {
        // ~50 km longitude span at ~51° N, ~30 km latitude span
        var bounds = new MapBounds(51.0, 13.0, 51.27, 13.7);
        var zoom   = WebMercator.CalculateZoom(bounds, 1600, 1200);

        zoom.Should().BeGreaterThanOrEqualTo(expectedMin);
        zoom.Should().BeLessThanOrEqualTo(12);
    }

    [Fact]
    public void CalculateZoom_BoundsFitTargetAtReturnedZoom()
    {
        var bounds = new MapBounds(51.0, 13.0, 51.27, 13.7);
        var zoom   = WebMercator.CalculateZoom(bounds, 1600, 1200);

        var (x1, y1) = WebMercator.LatLonToGlobalPixel(bounds.MaxLat, bounds.MinLon, zoom);
        var (x2, y2) = WebMercator.LatLonToGlobalPixel(bounds.MinLat, bounds.MaxLon, zoom);

        Math.Abs(x2 - x1).Should().BeLessThanOrEqualTo(1600);
        Math.Abs(y2 - y1).Should().BeLessThanOrEqualTo(1200);
    }
}
