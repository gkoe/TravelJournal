using System.IO;
using TravelJournal.WebExporter;

namespace TravelJournal.Wpf.Services;

public sealed class WebExportService : IWebExportService
{
    private readonly IWebPresentationExporter _exporter;

    public WebExportService(IWebPresentationExporter exporter) => _exporter = exporter;

    public Task<int> ExportAsync(
        string                        sourceFolder,
        string                        outputFolder,
        IProgress<WebExportProgress>? progress,
        CancellationToken             ct)
    {
        var title = Path.GetFileName(sourceFolder.TrimEnd(Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar)) ?? "Reise-Präsentation";

        return _exporter.ExportAsync(new WebExportRequest
        {
            SourcePhotoFolder = sourceFolder,
            OutputFolder      = outputFolder,
            Title             = title
        }, progress, ct);
    }
}
