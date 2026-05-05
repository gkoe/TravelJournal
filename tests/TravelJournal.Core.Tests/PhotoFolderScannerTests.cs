using TravelJournal.Core.Models;
using TravelJournal.Core.Services;
using FluentAssertions;

namespace TravelJournal.Core.Tests;

public class PhotoFolderScannerTests : IDisposable
{
    private readonly ExifReaderService _exifReader = new();
    private readonly TourCsvReader _csvReader = new();
    private readonly TourCsvWriter _csvWriter = new();
    private readonly string _tempFolder;

    public PhotoFolderScannerTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempFolder);
    }

    private PhotoFolderScanner CreateScanner() => new(_exifReader, _csvReader);

    [Fact]
    public void Scan_EmptyFolder_ReturnsEmptyResult()
    {
        var result = CreateScanner().Scan(_tempFolder);

        result.Photos.Should().BeEmpty();
        result.NewFilenames.Should().BeEmpty();
        result.MissingFilenames.Should().BeEmpty();
    }

    [Fact]
    public void Scan_FolderWithPhotos_NoCsv_AllNewAndStateNone()
    {
        CreateJpeg("img1.jpg");
        CreateJpeg("img2.jpg");

        var result = CreateScanner().Scan(_tempFolder);

        result.Photos.Should().HaveCount(2);
        result.Photos.Should().AllSatisfy(p => p.State.Should().Be(PhotoState.None));
        result.NewFilenames.Should().BeEquivalentTo(["img1.jpg", "img2.jpg"]);
        result.MissingFilenames.Should().BeEmpty();
    }

    [Fact]
    public void Scan_FolderWithMatchingCsv_StateFromCsv()
    {
        CreateJpeg("img1.jpg");
        var csvPhotos = new List<Photo>
        {
            new() { Filename = "img1.jpg", State = PhotoState.Selected, Title = "Gipfel" }
        };
        _csvWriter.Write(Path.Combine(_tempFolder, "tour.csv"), csvPhotos);

        var result = CreateScanner().Scan(_tempFolder);

        result.Photos.Should().HaveCount(1);
        result.Photos[0].State.Should().Be(PhotoState.Selected);
        result.Photos[0].Title.Should().Be("Gipfel");
        result.NewFilenames.Should().BeEmpty();
    }

    [Fact]
    public void Scan_NewPhotoNotInCsv_AppearsInPhotosAndNewFilenames()
    {
        CreateJpeg("old.jpg");
        CreateJpeg("new.jpg");
        var csvPhotos = new List<Photo>
        {
            new() { Filename = "old.jpg", State = PhotoState.Selected }
        };
        _csvWriter.Write(Path.Combine(_tempFolder, "tour.csv"), csvPhotos);

        var result = CreateScanner().Scan(_tempFolder);

        result.Photos.Should().HaveCount(2);
        result.NewFilenames.Should().ContainSingle().Which.Should().Be("new.jpg");
        result.MissingFilenames.Should().BeEmpty();

        var newPhoto = result.Photos.First(p => p.Filename == "new.jpg");
        newPhoto.State.Should().Be(PhotoState.None);
    }

    [Fact]
    public void Scan_CsvEntryWithoutFile_AppearsInPhotosAndMissingFilenames()
    {
        CreateJpeg("exists.jpg");
        var csvPhotos = new List<Photo>
        {
            new() { Filename = "exists.jpg" },
            new() { Filename = "missing.jpg", State = PhotoState.Selected }
        };
        _csvWriter.Write(Path.Combine(_tempFolder, "tour.csv"), csvPhotos);

        var result = CreateScanner().Scan(_tempFolder);

        result.Photos.Should().HaveCount(2);
        result.MissingFilenames.Should().ContainSingle().Which.Should().Be("missing.jpg");
        result.Photos.Should().Contain(p => p.Filename == "missing.jpg");
    }

    [Fact]
    public void Scan_HandEditedTitleInCsv_SurvivesRescan()
    {
        CreateJpeg("photo.jpg");
        var csvPhotos = new List<Photo>
        {
            new() { Filename = "photo.jpg", Title = "Hand-editierter Titel", State = PhotoState.Selected }
        };
        _csvWriter.Write(Path.Combine(_tempFolder, "tour.csv"), csvPhotos);

        var result = CreateScanner().Scan(_tempFolder);

        result.Photos.Single().Title.Should().Be("Hand-editierter Titel");
    }

    [Fact]
    public void Scan_WithExistingPhotos_PreservesUnsavedStateOnRescan()
    {
        CreateJpeg("img1.jpg");
        CreateJpeg("img2.jpg");

        // Erster Scan ohne CSV
        var firstResult = CreateScanner().Scan(_tempFolder);
        var img1 = firstResult.Photos.First(p => p.Filename == "img1.jpg");
        img1.State = PhotoState.Selected;
        img1.Title = "Ungespeicherter Titel";

        // Re-Scan mit existing → State muss erhalten bleiben
        var secondResult = CreateScanner().Scan(_tempFolder, firstResult.Photos);

        var img1After = secondResult.Photos.First(p => p.Filename == "img1.jpg");
        img1After.Should().BeSameAs(img1);
        img1After.State.Should().Be(PhotoState.Selected);
        img1After.Title.Should().Be("Ungespeicherter Titel");
    }

    [Fact]
    public void Scan_WithExistingPhotos_NewFileAppearsInNewFilenames()
    {
        CreateJpeg("old.jpg");
        var firstResult = CreateScanner().Scan(_tempFolder);

        CreateJpeg("new.jpg");
        var secondResult = CreateScanner().Scan(_tempFolder, firstResult.Photos);

        secondResult.NewFilenames.Should().ContainSingle().Which.Should().Be("new.jpg");
        secondResult.Photos.Should().HaveCount(2);
    }

    private void CreateJpeg(string filename)
        => File.WriteAllBytes(Path.Combine(_tempFolder, filename), [0xFF, 0xD8, 0xFF, 0xD9]);

    public void Dispose()
    {
        if (Directory.Exists(_tempFolder))
            Directory.Delete(_tempFolder, recursive: true);
    }
}
