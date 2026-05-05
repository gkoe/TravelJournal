using TravelJournal.Core.Models;
using TravelJournal.Core.Services;
using FluentAssertions;

namespace TravelJournal.Core.Tests;

public class TourCsvWriterTests : IDisposable
{
    private readonly TourCsvWriter _writer = new();
    private readonly string _tempPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.csv");

    [Fact]
    public void Write_CreatesCorrectHeader()
    {
        _writer.Write(_tempPath, []);

        var lines = File.ReadAllLines(_tempPath);
        lines[0].Should().Be("Filename;DateTime;Latitude;Longitude;Altitude;State;Title;Description;Location");
    }

    [Fact]
    public void Write_MultiplePhotos_CorrectColumnOrder()
    {
        var photos = new List<Photo>
        {
            new()
            {
                Filename = "A.jpg",
                DateTime = new DateTime(2026, 4, 12, 9, 0, 0),
                Latitude = 47.3,
                Longitude = 8.5,
                Altitude = 400,
                State = PhotoState.Selected,
                Title = "Start",
                Description = "Beschreibung"
            }
        };

        _writer.Write(_tempPath, photos);

        var lines = File.ReadAllLines(_tempPath);
        lines[1].Should().Be("A.jpg;2026-04-12T09:00:00;47.3;8.5;400;1;Start;Beschreibung;");
    }

    [Fact]
    public void Write_SortsByDateTimeAscending()
    {
        var photos = new List<Photo>
        {
            new() { Filename = "B.jpg", DateTime = new DateTime(2026, 4, 12, 12, 0, 0) },
            new() { Filename = "A.jpg", DateTime = new DateTime(2026, 4, 12, 9, 0, 0) }
        };

        _writer.Write(_tempPath, photos);

        var lines = File.ReadAllLines(_tempPath);
        lines[1].Should().StartWith("A.jpg");
        lines[2].Should().StartWith("B.jpg");
    }

    [Fact]
    public void Write_NullFields_WrittenAsEmpty()
    {
        var photos = new List<Photo>
        {
            new() { Filename = "X.jpg", DateTime = null, Latitude = null, Title = null }
        };

        _writer.Write(_tempPath, photos);

        var content = File.ReadAllText(_tempPath);
        content.Should().NotContain("null");

        var lines = File.ReadAllLines(_tempPath);
        var cols = lines[1].Split(';');
        cols[1].Should().BeEmpty();
        cols[2].Should().BeEmpty();
        cols[6].Should().BeEmpty();
    }

    [Fact]
    public void Write_Umlauts_PreservedCorrectly()
    {
        var photos = new List<Photo>
        {
            new() { Filename = "U.jpg", Title = "Über den Berg", Description = "Früh aufgestanden — schön!" }
        };

        _writer.Write(_tempPath, photos);

        var content = File.ReadAllText(_tempPath);
        content.Should().Contain("Über den Berg");
        content.Should().Contain("Früh aufgestanden");
    }

    [Fact]
    public void Write_StateWrittenAsInteger()
    {
        var photos = new List<Photo>
        {
            new() { Filename = "S.jpg", State = PhotoState.Selected },
            new() { Filename = "D.jpg", State = PhotoState.Deselected },
            new() { Filename = "N.jpg", State = PhotoState.None }
        };

        _writer.Write(_tempPath, photos);

        var lines = File.ReadAllLines(_tempPath);
        lines[1].Split(';')[5].Should().Be("1");
        lines[2].Split(';')[5].Should().Be("2");
        lines[3].Split(';')[5].Should().Be("0");
    }

    [Fact]
    public void Write_PhotosWithoutDateTime_SortedToEnd()
    {
        var photos = new List<Photo>
        {
            new() { Filename = "NoDate.jpg", DateTime = null },
            new() { Filename = "WithDate.jpg", DateTime = new DateTime(2026, 4, 12, 8, 0, 0) }
        };

        _writer.Write(_tempPath, photos);

        var lines = File.ReadAllLines(_tempPath);
        lines[1].Should().StartWith("WithDate.jpg");
        lines[2].Should().StartWith("NoDate.jpg");
    }

    public void Dispose() => File.Delete(_tempPath);
}
