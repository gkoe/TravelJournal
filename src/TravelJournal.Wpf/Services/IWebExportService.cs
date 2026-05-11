using TravelJournal.WebExporter;

namespace TravelJournal.Wpf.Services;

public interface IWebExportService
{
    Task<int> ExportAsync(
        string                        sourceFolder,
        string                        outputFolder,
        IProgress<WebExportProgress>? progress,
        CancellationToken             ct);
}
