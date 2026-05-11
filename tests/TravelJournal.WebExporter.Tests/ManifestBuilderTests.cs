using TravelJournal.Core.Models;
using TravelJournal.WebExporter.Models;
using TravelJournal.WebExporter.Services;

namespace TravelJournal.WebExporter.Tests;

public class ManifestBuilderTests
{
    private readonly ManifestBuilder _sut = new();

    private static Photo Photo(string filename, PhotoState state, DateTime? dt = null,
        string? location = null, string? title = null, string? desc = null) => new()
    {
        Filename = filename, State = state, DateTime = dt,
        Location = location, Title = title, Description = desc
    };

    private static Photo Map(string filename, DateTime dt, string? title = null) => new()
    {
        EntryType = EntryType.Map,
        Filename  = filename,
        DateTime  = dt,
        Title     = title,
        State     = PhotoState.Selected
    };

    [Fact]
    public void Build_ProducesCorrectOrder_And_Count()
    {
        var start  = new[] { Photo("start.jpg", PhotoState.Start) };
        var middle = new[]
        {
            Photo("a.jpg", PhotoState.Selected, new DateTime(2026, 4, 26, 12, 0, 0)),
            Photo("b.jpg", PhotoState.Selected, new DateTime(2026, 4, 26, 14, 0, 0)),
            Photo("c.jpg", PhotoState.Selected, new DateTime(2026, 4, 27,  9, 0, 0)),
        };
        var maps = new[]
        {
            Map("map_2026-04-26T17-23-55.png", new DateTime(2026, 4, 26, 17, 23, 55)),
            Map("map_2026-04-27T18-00-00.png", new DateTime(2026, 4, 27, 18,  0,  0)),
        };
        var end = new[] { Photo("end.jpg", PhotoState.End) };

        var manifest = _sut.Build(start, middle, maps, end, "Test", 5000, 2000);

        manifest.Items.Should().HaveCount(7);
        manifest.Items[0].Role.Should().Be("start");
        manifest.Items.Skip(1).Take(5).Should().OnlyContain(i => i.Role == "middle");
        manifest.Items[6].Role.Should().Be("end");
    }

    [Fact]
    public void Build_MiddleBlock_IsSortedChronologically()
    {
        var middle = new[]
        {
            Photo("late.jpg",  PhotoState.Selected, new DateTime(2026, 4, 26, 18, 0, 0)),
            Photo("early.jpg", PhotoState.Selected, new DateTime(2026, 4, 26,  8, 0, 0)),
        };
        var maps = new[] { Map("map_2026-04-26T12-00-00.png", new DateTime(2026, 4, 26, 12, 0, 0)) };

        var manifest = _sut.Build([], middle, maps, [], "T", 5000, 2000);

        manifest.Items[0].Src.Should().Contain("early");
        manifest.Items[1].Type.Should().Be("map");
        manifest.Items[2].Src.Should().Contain("late");
    }

    [Fact]
    public void Build_MiddlePhotoWithoutDateTime_IsExcluded()
    {
        var middle = new[]
        {
            Photo("with_date.jpg",    PhotoState.Selected, new DateTime(2026, 4, 26, 10, 0, 0)),
            Photo("without_date.jpg", PhotoState.Selected, null),
        };

        var manifest = _sut.Build([], middle, [], [], "T", 5000, 2000);

        manifest.Items.Should().HaveCount(1);
        manifest.Items[0].Src.Should().Contain("with_date");
    }

    [Fact]
    public void Build_StartWithoutDateTime_IsIncluded_WithNullDateTime()
    {
        var start = new[] { Photo("title.jpg", PhotoState.Start, null) };

        var manifest = _sut.Build(start, [], [], [], "T", 5000, 2000);

        manifest.Items.Should().HaveCount(1);
        manifest.Items[0].Role.Should().Be("start");
        manifest.Items[0].DateTime.Should().BeNull();
    }

    [Fact]
    public void Build_End_IsAlwaysLast()
    {
        var middle = new[] { Photo("m.jpg", PhotoState.Selected, new DateTime(2026, 5, 1, 10, 0, 0)) };
        var end    = new[] { Photo("end.jpg", PhotoState.End, new DateTime(2026, 1, 1)) };

        var manifest = _sut.Build([], middle, [], end, "T", 5000, 2000);

        manifest.Items.Last().Role.Should().Be("end");
    }

    [Fact]
    public void Build_RoleField_IsCorrectForAllTypes()
    {
        var start  = new[] { Photo("s.jpg", PhotoState.Start) };
        var middle = new[] { Photo("m.jpg", PhotoState.Selected, new DateTime(2026, 4, 26, 10, 0, 0)) };
        var maps   = new[] { Map("map_2026-04-26T12-00-00.png", new DateTime(2026, 4, 26, 12, 0, 0)) };
        var end    = new[] { Photo("e.jpg", PhotoState.End) };

        var manifest = _sut.Build(start, middle, maps, end, "T", 5000, 2000);

        manifest.Items[0].Role.Should().Be("start");
        manifest.Items[1].Role.Should().Be("middle");
        manifest.Items[2].Role.Should().Be("middle");
        manifest.Items[3].Role.Should().Be("end");
    }

    [Fact]
    public void Build_PhotoFields_AreCopiedFromPhoto()
    {
        var middle = new[]
        {
            Photo("p.jpg", PhotoState.Selected,
                new DateTime(2026, 4, 26, 10, 0, 0),
                location: "Villach", title: "Titel", desc: "Beschreibung")
        };

        var manifest = _sut.Build([], middle, [], [], "T", 5000, 2000);

        var item = manifest.Items[0];
        item.Location.Should().Be("Villach");
        item.Title.Should().Be("Titel");
        item.Description.Should().Be("Beschreibung");
    }

    [Fact]
    public void Build_MapWithTitle_TitleInManifest()
    {
        var maps = new[] { Map("map_2026-04-26T12-00-00.png", new DateTime(2026, 4, 26, 12, 0, 0), "Inntal-Abschnitt") };

        var manifest = _sut.Build([], [], maps, [], "T", 5000, 2000);

        manifest.Items.Should().HaveCount(1);
        manifest.Items[0].Title.Should().Be("Inntal-Abschnitt");
        manifest.Items[0].Type.Should().Be("map");
    }
}
