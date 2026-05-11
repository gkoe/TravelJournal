using ImageMagick;

namespace TravelJournal.Core.Services;

public sealed class MagickHeicConverter : IHeicConverter
{
    public async Task<HeicConversionResult> ConvertAsync(
        string                heicFilePath,
        HeicConversionOptions options,
        CancellationToken     ct = default)
    {
        if (!File.Exists(heicFilePath))
            throw new FileNotFoundException("HEIC-Datei nicht gefunden", heicFilePath);

        var dir       = Path.GetDirectoryName(heicFilePath)!;
        var nameNoExt = Path.GetFileNameWithoutExtension(heicFilePath);
        var jpegPath  = Path.Combine(dir, nameNoExt + ".jpg");

        if (File.Exists(jpegPath))
            jpegPath = Path.Combine(dir, $"{nameNoExt}_converted.jpg");

        var originalSize = new FileInfo(heicFilePath).Length;

        bool exifPreserved = false;
        await Task.Run(() =>
        {
            using var image = new MagickImage(heicFilePath);
            image.Quality = (uint)options.JpegQuality;
            image.Format  = MagickFormat.Jpeg;
            image.Write(jpegPath);
            exifPreserved = image.GetExifProfile() is not null;
        }, ct);

        // Datei-Zeitstempel übertragen, damit Explorer-Sortierung stimmt
        var heicInfo = new FileInfo(heicFilePath);
        File.SetLastWriteTime(jpegPath, heicInfo.LastWriteTime);
        File.SetCreationTime(jpegPath, heicInfo.CreationTime);

        // Original direkt löschen
        File.Delete(heicFilePath);

        var convertedSize = new FileInfo(jpegPath).Length;

        return new HeicConversionResult(
            SourcePath:         heicFilePath,
            OutputJpegPath:     jpegPath,
            BackupPath:         null,
            ExifPreserved:      exifPreserved,
            OriginalSizeBytes:  originalSize,
            ConvertedSizeBytes: convertedSize);
    }
}
