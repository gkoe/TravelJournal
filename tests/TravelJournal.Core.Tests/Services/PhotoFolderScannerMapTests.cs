using TravelJournal.Core.Models;
using TravelJournal.Core.Services;
using FluentAssertions;

namespace TravelJournal.Core.Tests.Services;

public class PhotoFolderScannerMapTests : IDisposable
{
    private readonly string _tempFolder;
    private readonly PhotoFolderScanner _scanner;

    public PhotoFolderScannerMapTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempFolder);
        _scanner = new PhotoFolderScanner(new ExifReaderService(), new TourCsvReader());
    }

    [Fact]
    public void Scan_FolderWithTwoPhotosAndOneMapPng_PhotosCount3()
    {
        CreateJpeg("img1.jpg");
        CreateJpeg("img2.jpg");
        CreateMapPng("map_2026-04-12T13-47-22.png");

        var result = _scanner.Scan(_tempFolder);

        result.Photos.Should().HaveCount(3);
    }

    [Fact]
    public void Scan_MapPngWithValidFilename_DateTimeParsedFromFilename()
    {
        CreateMapPng("map_2026-04-12T13-47-22.png");

        var result = _scanner.Scan(_tempFolder);

        var map = result.Photos.Should().ContainSingle(p => p.Filename == "map_2026-04-12T13-47-22.png").Subject;
        map.DateTime.Should().Be(new DateTime(2026, 4, 12, 13, 47, 22));
        map.EntryType.Should().Be(EntryType.Map);
    }

    [Fact]
    public void Scan_MapPngWithLocationSuffix_DateTimeParsedCorrectly()
    {
        CreateMapPng("map_2026-04-12T13-47-22_Wien.png");

        var result = _scanner.Scan(_tempFolder);

        var map = result.Photos.Should().ContainSingle(p => p.Filename == "map_2026-04-12T13-47-22_Wien.png").Subject;
        map.DateTime.Should().Be(new DateTime(2026, 4, 12, 13, 47, 22));
        map.EntryType.Should().Be(EntryType.Map);
    }

    [Fact]
    public void Scan_PngWithoutMapPrefix_NotInPhotos()
    {
        CreateMapPng("screenshot.png");

        var result = _scanner.Scan(_tempFolder);

        result.Photos.Should().BeEmpty();
    }

    [Fact]
    public void Scan_MapPngInPhotos_HasEntryTypeMap()
    {
        CreateJpeg("img1.jpg");
        CreateMapPng("map_2026-04-12T13-47-22.png");

        var result = _scanner.Scan(_tempFolder);

        result.Photos.Should().Contain(p => p.EntryType == EntryType.Map && p.Filename == "map_2026-04-12T13-47-22.png");
        result.Photos.Should().Contain(p => p.EntryType != EntryType.Map && p.Filename == "img1.jpg");
    }

    [Fact]
    public void Scan_EmptyFolder_PhotosEmpty()
    {
        var result = _scanner.Scan(_tempFolder);

        result.Photos.Should().BeEmpty();
    }

    [Fact]
    public void Scan_MapPngWithoutCsvEntry_StateIsNone()
    {
        CreateMapPng("map_2026-04-12T13-47-22.png");

        var result = _scanner.Scan(_tempFolder);

        var map = result.Photos.Single(p => p.EntryType == EntryType.Map);
        map.State.Should().Be(PhotoState.None);
    }

    private void CreateJpeg(string filename)
        => File.WriteAllBytes(Path.Combine(_tempFolder, filename), [0xFF, 0xD8, 0xFF, 0xD9]);

    private void CreateMapPng(string filename)
        => File.WriteAllBytes(Path.Combine(_tempFolder, filename), CreateMinimalPng());

    private static byte[] CreateMinimalPng()
    {
        // Minimal 1×1 PNG (transparent pixel)
        return Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempFolder))
            Directory.Delete(_tempFolder, recursive: true);
    }
}
