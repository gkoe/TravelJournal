using TravelJournal.Core.Models;
using TravelJournal.Core.Services;
using FluentAssertions;
using System.Text;

namespace TravelJournal.Core.Tests;

public class TourCsvReaderTests : IDisposable
{
    private readonly TourCsvWriter _writer = new();
    private readonly TourCsvReader _reader = new();
    private readonly string _tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.csv");

    [Fact]
    public void Read_RoundTrip_ReturnsEquivalentPhotos()
    {
        var originals = new List<Photo>
        {
            new()
            {
                Filename = "DSC_001.jpg",
                DateTime = new DateTime(2026, 4, 12, 9, 14, 33),
                Latitude = 47.376541,
                Longitude = 8.541234,
                Altitude = 412,
                State = PhotoState.Selected,
                Title = "Start in Zürich",
                Description = "Früh los"
            },
            new()
            {
                Filename = "DSC_002.jpg",
                DateTime = null,
                Latitude = null,
                Longitude = null,
                Altitude = null,
                State = PhotoState.None,
                Title = null,
                Description = null
            }
        };

        _writer.Write(_tempPath, originals);
        var loaded = _reader.Read(_tempPath);

        loaded.Should().HaveCount(2);
        var first = loaded.First(p => p.Filename == "DSC_001.jpg");
        first.DateTime.Should().Be(originals[0].DateTime);
        first.Latitude.Should().BeApproximately(47.376541, 0.000001);
        first.State.Should().Be(PhotoState.Selected);
        first.Title.Should().Be("Start in Zürich");

        var second = loaded.First(p => p.Filename == "DSC_002.jpg");
        second.DateTime.Should().BeNull();
        second.State.Should().Be(PhotoState.None);
        second.Title.Should().BeNull();
    }

    [Fact]
    public void Read_EmptyStateColumn_ReturnsNone()
    {
        WriteCsvContent("Filename;DateTime;Latitude;Longitude;Altitude;State;Title;Description\r\ntest.jpg;;;;;;;\r\n");

        var photos = _reader.Read(_tempPath);

        photos[0].State.Should().Be(PhotoState.None);
    }

    [Fact]
    public void Read_UnknownStateValue_ReturnsNone()
    {
        WriteCsvContent("Filename;DateTime;Latitude;Longitude;Altitude;State;Title;Description\r\ntest.jpg;;;;99;;\r\n");

        var photos = _reader.Read(_tempPath);

        photos[0].State.Should().Be(PhotoState.None);
    }

    [Fact]
    public void Read_EmptyCoordinates_ReturnsNull()
    {
        WriteCsvContent("Filename;DateTime;Latitude;Longitude;Altitude;State;Title;Description\r\ntest.jpg;;;;;;;\r\n");

        var photos = _reader.Read(_tempPath);

        photos[0].Latitude.Should().BeNull();
        photos[0].Longitude.Should().BeNull();
        photos[0].Altitude.Should().BeNull();
        photos[0].DateTime.Should().BeNull();
    }

    [Fact]
    public void Read_NonExistentFile_ThrowsFileNotFoundException()
    {
        var act = () => _reader.Read(@"C:\does\not\exist.csv");
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void Read_LocationRoundtrip_PreservesValue()
    {
        var originals = new List<Photo>
        {
            new() { Filename = "geo.jpg", Location = "Zürich, Kreis 1" },
            new() { Filename = "nogeo.jpg", Location = null }
        };

        _writer.Write(_tempPath, originals);
        var loaded = _reader.Read(_tempPath);

        loaded.First(p => p.Filename == "geo.jpg").Location.Should().Be("Zürich, Kreis 1");
        loaded.First(p => p.Filename == "nogeo.jpg").Location.Should().BeNull();
    }

    [Fact]
    public void Read_OldCsvWithoutLocationColumn_LocationIsNull()
    {
        // Alte CSV ohne Location-Spalte
        WriteCsvContent("Filename;DateTime;Latitude;Longitude;Altitude;State;Title;Description\r\ntest.jpg;2026-04-12T09:00:00;47.3;8.5;400;1;Titel;Beschreibung\r\n");

        var photos = _reader.Read(_tempPath);

        photos.Should().HaveCount(1);
        photos[0].Location.Should().BeNull();
        photos[0].Title.Should().Be("Titel");
    }

    private void WriteCsvContent(string content)
        => File.WriteAllText(_tempPath, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

    public void Dispose() => File.Delete(_tempPath);
}
