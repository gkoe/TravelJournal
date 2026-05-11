using TravelJournal.Core.Services;
using TravelJournal.WebExporter;
using TravelJournal.WebExporter.Services;

int Arg(string flag, string[] args)
{
    var i = Array.IndexOf(args, flag);
    return i >= 0 && i + 1 < args.Length ? i + 1 : -1;
}

if (Array.IndexOf(args, "--help") >= 0 || args.Length == 0)
{
    Console.WriteLine("Usage: TravelJournal.WebExporter --source <folder> --output <folder> [--title \"...\"]");
    return 0;
}

var srcIdx    = Arg("--source", args);
var outIdx    = Arg("--output", args);
var titleIdx  = Arg("--title",  args);

if (srcIdx < 0 || outIdx < 0)
{
    Console.Error.WriteLine("--source and --output are required.");
    return 1;
}

var request = new WebExportRequest
{
    SourcePhotoFolder = args[srcIdx],
    OutputFolder      = args[outIdx],
    Title             = titleIdx >= 0 ? args[titleIdx] : "Reise-Präsentation"
};

var progress = new Progress<WebExportProgress>(p =>
    Console.WriteLine($"[{p.Stage}] {p.Current}/{p.Total} {p.Message}"));

var exporter = new WebPresentationExporter(
    new TourCsvReader(),
    new ManifestBuilder(),
    new ImageOptimizer());

var count = await exporter.ExportAsync(request, progress);
Console.WriteLine(count == 0 ? "Keine Inhalte gefunden." : $"Export abgeschlossen: {count} Items.");
return count == 0 ? 1 : 0;
