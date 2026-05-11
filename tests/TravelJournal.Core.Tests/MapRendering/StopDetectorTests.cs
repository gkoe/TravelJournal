using TravelJournal.Core.MapRendering;
using TravelJournal.Core.Models;
using FluentAssertions;

namespace TravelJournal.Core.Tests.MapRendering;

public class StopDetectorTests
{
    private static readonly TimeSpan Threshold = TimeSpan.FromMinutes(30);
    private readonly StopDetector _sut = new();

    private static Photo MakePhoto(DateTime dt, double? lat = 48.0, double? lon = 16.0, string? location = null)
        => new() { DateTime = dt, Latitude = lat, Longitude = lon, Location = location };

    [Fact]
    public void AllWithin5Min_NoFinalSummary_ReturnsEmpty()
    {
        var t0 = new DateTime(2024, 7, 1, 10, 0, 0);
        var photos = new[]
        {
            MakePhoto(t0),
            MakePhoto(t0.AddMinutes(2)),
            MakePhoto(t0.AddMinutes(4)),
        };

        var stops = _sut.DetectStops(photos, Threshold, addFinalSummary: false);

        stops.Should().BeEmpty();
    }

    [Fact]
    public void AllWithin5Min_WithFinalSummary_ReturnsOneStop()
    {
        var t0 = new DateTime(2024, 7, 1, 10, 0, 0);
        var photos = new[]
        {
            MakePhoto(t0),
            MakePhoto(t0.AddMinutes(2)),
            MakePhoto(t0.AddMinutes(4)),
        };

        var stops = _sut.DetectStops(photos, Threshold, addFinalSummary: true);

        stops.Should().HaveCount(1);
        stops[0].PhotoIndex.Should().Be(2);
    }

    [Fact]
    public void Gap45Min_ReturnsOneStop()
    {
        var t0 = new DateTime(2024, 7, 1, 10, 0, 0);
        var photos = new[]
        {
            MakePhoto(t0),
            MakePhoto(t0.AddMinutes(5)),
            MakePhoto(t0.AddMinutes(5 + 45)), // gap of 45 min after index 1
        };

        var stops = _sut.DetectStops(photos, Threshold, addFinalSummary: false);

        stops.Should().HaveCount(1);
        stops[0].PhotoIndex.Should().Be(1);
    }

    [Fact]
    public void Gap45Min_WithFinalSummary_ReturnsTwoStops()
    {
        var t0 = new DateTime(2024, 7, 1, 10, 0, 0);
        var photos = new[]
        {
            MakePhoto(t0),
            MakePhoto(t0.AddMinutes(5)),
            MakePhoto(t0.AddMinutes(50)),
        };

        var stops = _sut.DetectStops(photos, Threshold, addFinalSummary: true);

        stops.Should().HaveCount(2);
        stops[0].PhotoIndex.Should().Be(1);
        stops[1].PhotoIndex.Should().Be(2);
    }

    [Fact]
    public void PhotoWithoutGps_IsSkippedWithoutCrash()
    {
        var t0 = new DateTime(2024, 7, 1, 10, 0, 0);
        var photos = new[]
        {
            MakePhoto(t0),
            MakePhoto(t0.AddMinutes(5), lat: null, lon: null), // no GPS
            MakePhoto(t0.AddMinutes(50)),                       // big gap but GPS-less photo is in between
        };

        var act = () => _sut.DetectStops(photos, Threshold, addFinalSummary: false);

        act.Should().NotThrow();
    }

    [Fact]
    public void StopTimestamp_IsPhotoTimeplus1Second()
    {
        var t0 = new DateTime(2024, 7, 1, 10, 0, 0);
        var photos = new[]
        {
            MakePhoto(t0),
            MakePhoto(t0.AddMinutes(60)),
        };

        var stops = _sut.DetectStops(photos, Threshold, addFinalSummary: false);

        stops[0].Timestamp.Should().Be(t0.AddSeconds(1));
    }

    [Fact]
    public void Location_IsPassedThroughFromPhoto()
    {
        var t0 = new DateTime(2024, 7, 1, 10, 0, 0);
        var photos = new[]
        {
            MakePhoto(t0,               location: "Villach"),
            MakePhoto(t0.AddMinutes(60), location: "Graz"),
        };

        var stops = _sut.DetectStops(photos, Threshold, addFinalSummary: true);

        // Gap stop uses photo[0].Location; Final-Summary uses photo[1].Location
        stops[0].Location.Should().Be("Villach");
        stops[1].Location.Should().Be("Graz");
    }
}
