using TravelJournal.Core.Models;
using TravelJournal.Core.Services;
using FluentAssertions;

namespace TravelJournal.Core.Tests.Services;

public class PhotoRenamerTests : IDisposable
{
    private readonly TourCsvWriter   _csvWriter = new();
    private readonly TourCsvReader   _csvReader = new();
    private readonly string          _tempFolder;
    private readonly PhotoRenamer    _sut;

    public PhotoRenamerTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempFolder);
        _sut = new PhotoRenamer(_csvWriter);
    }

    private Task<RenameResult> Rename(IReadOnlyList<Photo> photos, RenameOptions? options = null)
        => _sut.RenameAsync(_tempFolder, photos, options ?? RenameOptions.Default);

    [Fact]
    public async Task RenameAsync_NoDateTime_SkippedInNoDateTime()
    {
        CreateJpeg("Start.jpg");
        var photos = new List<Photo>
        {
            new() { Filename = "Start.jpg", DateTime = null }
        };

        var result = await Rename(photos);

        result.SkippedNoDateTime.Should().ContainSingle().Which.Should().Be("Start.jpg");
        result.Renamed.Should().BeEmpty();
        File.Exists(Path.Combine(_tempFolder, "Start.jpg")).Should().BeTrue();
    }

    [Fact]
    public async Task RenameAsync_NoLocation_UsesDateTimeOnlySchema()
    {
        CreateJpeg("img.jpg");
        var photos = new List<Photo>
        {
            new() { Filename = "img.jpg", DateTime = new DateTime(2026, 4, 27, 9, 49, 24) }
        };

        var result = await Rename(photos);

        result.Renamed.Should().ContainSingle();
        result.Renamed[0].NewFilename.Should().Be("260427_094924.jpg");
        File.Exists(Path.Combine(_tempFolder, "260427_094924.jpg")).Should().BeTrue();
    }

    [Fact]
    public async Task RenameAsync_WithLocation_LocationAppendedToName()
    {
        CreateJpeg("20260427_122257.jpg");
        var photos = new List<Photo>
        {
            new()
            {
                Filename = "20260427_122257.jpg",
                DateTime = new DateTime(2026, 4, 27, 12, 22, 57),
                Location = "Tarvis"
            }
        };

        var result = await Rename(photos);

        result.Renamed.Should().ContainSingle();
        result.Renamed[0].NewFilename.Should().Be("260427_122257_Tarvis.jpg");
        File.Exists(Path.Combine(_tempFolder, "260427_122257_Tarvis.jpg")).Should().BeTrue();
    }

    [Fact]
    public async Task RenameAsync_WithPrefix_PrefixPrependedToName()
    {
        CreateJpeg("img.jpg");
        var photos = new List<Photo>
        {
            new()
            {
                Filename = "img.jpg",
                DateTime = new DateTime(2026, 4, 27, 12, 22, 57),
                Location = "Tarvis"
            }
        };

        var options = new RenameOptions("rhodos", "{prefix}_{datetime}_{ort}");
        var result  = await Rename(photos, options);

        result.Renamed.Should().ContainSingle();
        result.Renamed[0].NewFilename.Should().Be("rhodos_260427_122257_Tarvis.jpg");
    }

    [Fact]
    public async Task RenameAsync_PrefixWithDefaultTemplate_PrefixAutoPrepended()
    {
        CreateJpeg("img.jpg");
        var photos = new List<Photo>
        {
            new()
            {
                Filename = "img.jpg",
                DateTime = new DateTime(2026, 4, 27, 12, 22, 57),
                Location = "Tarvis"
            }
        };

        // Standardvorlage enthält kein {prefix} – der Präfix muss trotzdem greifen.
        var options = new RenameOptions("rhodos", RenameOptions.DefaultTemplate);
        var result  = await Rename(photos, options);

        result.Renamed.Should().ContainSingle();
        result.Renamed[0].NewFilename.Should().Be("rhodos_260427_122257_Tarvis.jpg");
    }

    [Fact]
    public async Task RenameAsync_CustomTemplate_OrtAndPrefixOnly()
    {
        CreateJpeg("img.jpg");
        var photos = new List<Photo>
        {
            new()
            {
                Filename = "img.jpg",
                DateTime = new DateTime(2026, 4, 27, 12, 22, 57),
                Location = "Tarvis"
            }
        };

        var options = new RenameOptions("Rhodos", "{prefix}_{ort}");
        var result  = await Rename(photos, options);

        result.Renamed.Should().ContainSingle();
        result.Renamed[0].NewFilename.Should().Be("Rhodos_Tarvis.jpg");
    }

    [Fact]
    public async Task RenameAsync_EmptyLocation_NoTrailingSeparator()
    {
        CreateJpeg("img.jpg");
        var photos = new List<Photo>
        {
            new() { Filename = "img.jpg", DateTime = new DateTime(2026, 4, 27, 12, 22, 57) }
        };

        var options = new RenameOptions("rhodos", "{prefix}_{datetime}_{ort}");
        var result  = await Rename(photos, options);

        result.Renamed.Should().ContainSingle();
        result.Renamed[0].NewFilename.Should().Be("rhodos_260427_122257.jpg");
    }

    [Fact]
    public async Task RenameAsync_SameSecondSameLocation_DisambiguatesWithSuffix()
    {
        CreateJpeg("img1.jpg");
        CreateJpeg("img2.jpg");
        CreateJpeg("img3.jpg");
        var dt = new DateTime(2026, 4, 28, 10, 34, 6);
        var photos = new List<Photo>
        {
            new() { Filename = "img1.jpg", DateTime = dt, Location = "Chiusaforte / Scluse" },
            new() { Filename = "img2.jpg", DateTime = dt, Location = "Chiusaforte / Scluse" },
            new() { Filename = "img3.jpg", DateTime = dt, Location = "Chiusaforte / Scluse" },
        };

        var result = await Rename(photos);

        result.Renamed.Should().HaveCount(3);
        result.Renamed.Select(r => r.NewFilename).Should().BeEquivalentTo([
            "260428_103406_ChiusaforteScluse.jpg",
            "260428_103406_ChiusaforteScluse_2.jpg",
            "260428_103406_ChiusaforteScluse_3.jpg"
        ]);
    }

    [Fact]
    public async Task RenameAsync_AlreadyMatchingFilename_Skipped()
    {
        CreateJpeg("260427_122200_Tarvis.jpg");
        var photos = new List<Photo>
        {
            new()
            {
                Filename = "260427_122200_Tarvis.jpg",
                DateTime = new DateTime(2026, 4, 27, 12, 22, 0),
                Location = "Tarvis"
            }
        };

        var result = await Rename(photos);

        result.SkippedAlreadyMatching.Should().ContainSingle().Which
            .Should().Be("260427_122200_Tarvis.jpg");
        result.Renamed.Should().BeEmpty();
    }

    [Fact]
    public async Task RenameAsync_TwoPassChain_BothRenamedCorrectly()
    {
        // A wünscht den Namen, den B aktuell trägt → echte Kette, die nur über
        // temporäre Zwischennamen kollisionsfrei aufzulösen ist.
        CreateJpeg("260101_100100.jpg"); // photoA trägt B's Zielnamen
        CreateJpeg("B.jpg");
        var photos = new List<Photo>
        {
            new() { Filename = "260101_100100.jpg", DateTime = new DateTime(2026, 1, 1, 10, 0, 0) },
            new() { Filename = "B.jpg",             DateTime = new DateTime(2026, 1, 1, 10, 1, 0) },
        };

        var result = await Rename(photos);

        result.Errors.Should().BeEmpty();
        result.Renamed.Should().HaveCount(2);
        File.Exists(Path.Combine(_tempFolder, "260101_100000.jpg")).Should().BeTrue();
        File.Exists(Path.Combine(_tempFolder, "260101_100100.jpg")).Should().BeTrue();
        File.Exists(Path.Combine(_tempFolder, "B.jpg")).Should().BeFalse();
    }

    [Fact]
    public async Task RenameAsync_CreatesBackupAndLog()
    {
        CreateJpeg("img.jpg");
        var csvPath = Path.Combine(_tempFolder, "tour.csv");
        _csvWriter.Write(csvPath, [new Photo { Filename = "img.jpg" }]);

        var photos = new List<Photo>
        {
            new() { Filename = "img.jpg", DateTime = new DateTime(2026, 5, 1, 8, 0, 0) }
        };

        await Rename(photos);

        Directory.EnumerateFiles(_tempFolder, "tour.csv.backup-*")
            .Should().HaveCount(1);
        File.Exists(Path.Combine(_tempFolder, "renames.log")).Should().BeTrue();
    }

    [Fact]
    public async Task RenameAsync_CsvUpdatedWithNewFilenames()
    {
        CreateJpeg("original.jpg");
        var csvPath = Path.Combine(_tempFolder, "tour.csv");
        var original = new Photo
        {
            Filename    = "original.jpg",
            DateTime    = new DateTime(2026, 5, 9, 14, 30, 0),
            Location    = "Villach",
            State       = PhotoState.Selected,
            Title       = "Mein Titel",
            Description = "Meine Beschreibung",
        };
        _csvWriter.Write(csvPath, [original]);

        var result = await Rename([original]);

        result.Renamed.Should().ContainSingle();

        // CSV neu lesen
        var csvPhotos = _csvReader.Read(csvPath).ToList();
        csvPhotos.Should().HaveCount(1);
        csvPhotos[0].Filename.Should().Be("260509_143000_Villach.jpg");
        csvPhotos[0].State.Should().Be(PhotoState.Selected);
        csvPhotos[0].Title.Should().Be("Mein Titel");
        csvPhotos[0].Description.Should().Be("Meine Beschreibung");
        csvPhotos[0].Location.Should().Be("Villach");
    }

    [Fact]
    public async Task RenameAsync_RepeatedRun_Idempotent()
    {
        CreateJpeg("img.jpg");
        var photo = new Photo
        {
            Filename = "img.jpg",
            DateTime = new DateTime(2026, 4, 27, 9, 0, 0),
            Location = "Wien"
        };

        // Erster Lauf
        var r1 = await Rename([photo]);
        r1.Renamed.Should().HaveCount(1);

        // Zweiter Lauf mit demselben Photo-Objekt (Filename wurde in-memory aktualisiert)
        var r2 = await Rename([photo]);
        r2.SkippedAlreadyMatching.Should().ContainSingle();
        r2.Renamed.Should().BeEmpty();
    }

    [Fact]
    public async Task RenameAsync_HeicBackupFolder_NotTouched()
    {
        CreateJpeg("photo.jpg");
        var backupDir = Path.Combine(_tempFolder, "heic-original");
        Directory.CreateDirectory(backupDir);
        File.WriteAllBytes(Path.Combine(backupDir, "original.heic"), [0x00]);

        var photos = new List<Photo>
        {
            new() { Filename = "photo.jpg", DateTime = new DateTime(2026, 5, 1, 9, 0, 0) }
        };

        await Rename(photos);

        File.Exists(Path.Combine(backupDir, "original.heic")).Should().BeTrue("HEIC-Backup darf nicht angerührt werden");
    }

    private void CreateJpeg(string filename)
        => File.WriteAllBytes(Path.Combine(_tempFolder, filename), [0xFF, 0xD8, 0xFF, 0xD9]);

    public void Dispose()
    {
        if (Directory.Exists(_tempFolder))
            Directory.Delete(_tempFolder, recursive: true);
    }
}
