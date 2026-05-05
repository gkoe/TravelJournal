using TravelJournal.Core.Presentation;
using FluentAssertions;

namespace TravelJournal.Core.Tests.Presentation;

public class PresentationPlaylistTests
{
    private static readonly DateTime Base = new(2026, 4, 12, 9, 14, 0); // Sonntag

    // ── Playlist ordering ─────────────────────────────────────

    [Fact]
    public void Playlist_MixOfPhotosAndMaps_SortedChronologically()
    {
        var items = new List<IPresentationItem>
        {
            new PhotoPresentationItem(Base.AddHours(3),  "/f3.jpg", null),
            new MapPresentationItem  (Base.AddHours(1),  "/m1.png"),
            new PhotoPresentationItem(Base,              "/f0.jpg", null),
            new MapPresentationItem  (Base.AddHours(5),  "/m5.png"),
            new PhotoPresentationItem(Base.AddHours(2),  "/f2.jpg", null),
        };

        var playlist = items.OrderBy(i => i.EffectiveDateTime).ToList();

        playlist.Should().HaveCount(5);
        playlist.Select(i => i.FullPath).Should()
            .ContainInOrder("/f0.jpg", "/m1.png", "/f2.jpg", "/f3.jpg", "/m5.png");
    }

    [Fact]
    public void Playlist_OnlySelectedPhotosWithDateTime_ExcludesOthers()
    {
        // Simulate what MainViewModel does: only include photos with DateTime
        var dateTimes = new DateTime?[] { Base, null, Base.AddHours(1) };
        var playlist  = dateTimes
            .Where(dt => dt.HasValue)
            .Select((dt, i) => new PhotoPresentationItem(dt!.Value, $"/f{i}.jpg", null))
            .Cast<IPresentationItem>()
            .ToList();

        playlist.Should().HaveCount(2);
    }

    // ── PhotoPresentationItem overlay ─────────────────────────

    [Fact]
    public void PhotoItem_Overlay_ContainsCorrectDayOfWeek()
    {
        // 12.04.2026 ist ein Sonntag
        var item = new PhotoPresentationItem(Base, "/foto.jpg", "Linz");

        item.Overlay.DayOfWeek.Should().Be(DayOfWeek.Sunday);
    }

    [Fact]
    public void PhotoItem_Overlay_CorrectDateTimeFormat()
    {
        var item = new PhotoPresentationItem(Base, "/foto.jpg", "Stein am Rhein");

        var de       = new System.Globalization.CultureInfo("de-DE");
        var formatted = item.Overlay.LocalDateTime.ToString("dddd, d. MMMM yyyy · HH:mm", de);

        formatted.Should().Be("Sonntag, 12. April 2026 · 09:14");
    }

    [Fact]
    public void PhotoItem_Overlay_WithLocation_LocationSet()
    {
        var item = new PhotoPresentationItem(Base, "/foto.jpg", "Stein am Rhein");

        item.Overlay.Location.Should().Be("Stein am Rhein");
    }

    [Fact]
    public void PhotoItem_Overlay_WithoutLocation_LocationIsNull()
    {
        var item = new PhotoPresentationItem(Base, "/foto.jpg", null);

        item.Overlay.Location.Should().BeNull();
    }

    // ── MapPresentationItem overlay ───────────────────────────

    [Fact]
    public void MapItem_Overlay_IsNull()
    {
        var item = new MapPresentationItem(Base, "/map.png");

        item.Overlay.Should().BeNull();
    }

    [Fact]
    public void MapItem_EffectiveDateTime_MatchesConstructorArgument()
    {
        var item = new MapPresentationItem(Base, "/map.png");

        item.EffectiveDateTime.Should().Be(Base);
    }
}
