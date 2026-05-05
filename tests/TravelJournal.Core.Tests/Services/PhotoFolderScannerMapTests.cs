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
    public void Scan_FolderWithTwoPhotosAndOneMapPng_MapsCount1PhotosCount2()
    {
        CreateJpeg("img1.jpg");
        CreateJpeg("img2.jpg");
        CreateMapPng("map_2026-04-12T13-47-22.png");

        var result = _scanner.Scan(_tempFolder);

        result.Photos.Should().HaveCount(2);
        result.Maps.Should().HaveCount(1);
    }

    [Fact]
    public void Scan_MapPngWithValidFilename_DateTimeParsedFromFilename()
    {
        CreateMapPng("map_2026-04-12T13-47-22.png");

        var result = _scanner.Scan(_tempFolder);

        result.Maps.Should().HaveCount(1);
        var map = result.Maps[0];
        map.DateTime.Should().Be(new DateTime(2026, 4, 12, 13, 47, 22));
        map.Filename.Should().Be("map_2026-04-12T13-47-22.png");
    }

    [Fact]
    public void Scan_PngWithoutMapPrefix_NotInMaps()
    {
        CreateMapPng("screenshot.png");  // kein map_-Präfix

        var result = _scanner.Scan(_tempFolder);

        result.Maps.Should().BeEmpty();
    }

    [Fact]
    public void Scan_MapPngIsNotAddedToPhotos()
    {
        CreateJpeg("img1.jpg");
        CreateMapPng("map_2026-04-12T13-47-22.png");

        var result = _scanner.Scan(_tempFolder);

        result.Photos.Should().HaveCount(1);
        result.Photos[0].Filename.Should().Be("img1.jpg");
    }

    [Fact]
    public void Scan_EmptyFolder_MapsEmpty()
    {
        var result = _scanner.Scan(_tempFolder);

        result.Maps.Should().BeEmpty();
    }

    private void CreateJpeg(string filename)
        => File.WriteAllBytes(Path.Combine(_tempFolder, filename), [0xFF, 0xD8, 0xFF, 0xD9]);

    private void CreateMapPng(string filename)
        => File.WriteAllBytes(Path.Combine(_tempFolder, filename), CreateMinimalPng());

    private static byte[] CreateMinimalPng()
    {
        // Minimal 1×1 PNG (89 bytes, transparent pixel)
        return Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempFolder))
            Directory.Delete(_tempFolder, recursive: true);
    }
}
