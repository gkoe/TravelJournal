namespace TravelJournal.Core.Services;

public interface IHeicConverter
{
    Task<HeicConversionResult> ConvertAsync(
        string               heicFilePath,
        HeicConversionOptions options,
        CancellationToken    ct = default);
}

public sealed class HeicConversionOptions
{
    public int JpegQuality { get; init; } = 90;
}

public sealed record HeicConversionResult(
    string  SourcePath,
    string  OutputJpegPath,
    string? BackupPath,
    bool    ExifPreserved,
    long    OriginalSizeBytes,
    long    ConvertedSizeBytes);
