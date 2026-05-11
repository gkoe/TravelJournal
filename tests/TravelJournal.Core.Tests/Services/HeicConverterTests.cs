using TravelJournal.Core.Services;
using FluentAssertions;

namespace TravelJournal.Core.Tests.Services;

public class HeicConverterTests : IDisposable
{
    private readonly MagickHeicConverter _sut = new();
    private readonly string _tempFolder;

    public HeicConverterTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempFolder);
    }

    [Fact]
    public async Task ConvertAsync_FileNotFound_ThrowsFileNotFoundException()
    {
        var path = Path.Combine(_tempFolder, "notexistent.heic");

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            _sut.ConvertAsync(path, new HeicConversionOptions()));
    }

    // Die nachfolgenden Tests benötigen echte HEIC-Dateien.
    // Magick.NET-Q8-AnyCPU enthält keinen HEIC-Encoder (nur den Decoder via libheif),
    // daher können HEIC-Testdateien nicht programmatisch erstellt werden.
    // Für einen Volltest wäre eine echte .heic-Datei als embedded resource nötig.

    [Fact(Skip = "Benötigt echte HEIC-Datei — HEIC-Encoder in Testumgebung nicht verfügbar")]
    public async Task ConvertAsync_ValidHeic_ProducesJpegFile()
    {
        var heicPath = Path.Combine(_tempFolder, "sample.heic");
        // heicPath müsste eine echte HEIC-Datei sein
        var result = await _sut.ConvertAsync(heicPath, new HeicConversionOptions());
        File.Exists(result.OutputJpegPath).Should().BeTrue();
        result.ConvertedSizeBytes.Should().BeGreaterThan(0);
    }

    [Fact(Skip = "Benötigt echte HEIC-Datei — HEIC-Encoder in Testumgebung nicht verfügbar")]
    public async Task ConvertAsync_ValidHeic_OriginalDeleted()
    {
        var heicPath = Path.Combine(_tempFolder, "delete_test.heic");
        var result = await _sut.ConvertAsync(heicPath, new HeicConversionOptions());
        File.Exists(heicPath).Should().BeFalse();
        result.BackupPath.Should().BeNull();
    }

    [Fact(Skip = "Benötigt echte HEIC-Datei — HEIC-Encoder in Testumgebung nicht verfügbar")]
    public async Task ConvertAsync_ValidHeic_NoBackupFolderCreated()
    {
        var heicPath = Path.Combine(_tempFolder, "test.heic");
        await _sut.ConvertAsync(heicPath, new HeicConversionOptions());
        Directory.Exists(Path.Combine(_tempFolder, "heic-original")).Should().BeFalse();
    }

    [Fact(Skip = "Benötigt echte HEIC-Datei — HEIC-Encoder in Testumgebung nicht verfügbar")]
    public async Task ConvertAsync_ConvertedJpegExists_AppendsSuffix()
    {
        var heicPath = Path.Combine(_tempFolder, "photo.heic");
        File.WriteAllBytes(Path.Combine(_tempFolder, "photo.jpg"), [0xFF, 0xD8, 0xFF, 0xD9]);
        var result = await _sut.ConvertAsync(heicPath, new HeicConversionOptions());
        result.OutputJpegPath.Should().EndWith("_converted.jpg");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempFolder))
            Directory.Delete(_tempFolder, recursive: true);
    }
}
