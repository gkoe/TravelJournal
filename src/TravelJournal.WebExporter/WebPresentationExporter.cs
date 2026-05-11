using System.Text.Json;
using System.Text.Json.Serialization;
using TravelJournal.Core.Models;
using TravelJournal.Core.Services;
using TravelJournal.WebExporter.Models;
using TravelJournal.WebExporter.Services;

namespace TravelJournal.WebExporter;

public interface IWebPresentationExporter
{
    Task<int> ExportAsync(
        WebExportRequest           request,
        IProgress<WebExportProgress>? progress = null,
        CancellationToken          ct       = default);
}

public sealed class WebExportRequest
{
    public required string SourcePhotoFolder { get; init; }
    public required string OutputFolder      { get; init; }
    public string          Title             { get; init; } = "Reise-Präsentation";
    public int             MaxImageWidthPx   { get; init; } = 1920;
    public int             JpegQuality       { get; init; } = 82;
    public int             PhotoDurationMs   { get; init; } = 5000;
    public int             OverlayVisibleMs  { get; init; } = 2000;
}

public sealed record WebExportProgress(string Stage, int Current, int Total, string? Message = null);

public sealed class WebPresentationExporter : IWebPresentationExporter
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented          = true,
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly TourCsvReader    _csvReader;
    private readonly ManifestBuilder  _manifestBuilder;
    private readonly ImageOptimizer   _imageOptimizer;

    public WebPresentationExporter(
        TourCsvReader   csvReader,
        ManifestBuilder manifestBuilder,
        ImageOptimizer  imageOptimizer)
    {
        _csvReader        = csvReader;
        _manifestBuilder  = manifestBuilder;
        _imageOptimizer   = imageOptimizer;
    }

    public async Task<int> ExportAsync(
        WebExportRequest              request,
        IProgress<WebExportProgress>? progress = null,
        CancellationToken             ct       = default)
    {
        // 1. CSV lesen — enthält Fotos (EntryType=Photo) und Karten (EntryType=Map)
        var csvPath = Path.Combine(request.SourcePhotoFolder, "tour.csv");
        var entries = File.Exists(csvPath) ? _csvReader.Read(csvPath) : new List<Photo>();

        var startPhotos  = entries.Where(p => p.EntryType == EntryType.Photo && p.State == PhotoState.Start).ToList();
        var middlePhotos = entries.Where(p => p.EntryType == EntryType.Photo && p.State == PhotoState.Selected).ToList();
        var endPhotos    = entries.Where(p => p.EntryType == EntryType.Photo && p.State == PhotoState.End).ToList();
        var maps         = entries.Where(p => p.EntryType == EntryType.Map).ToList();

        // 2. Validieren
        if (startPhotos.Count + middlePhotos.Count + endPhotos.Count + maps.Count == 0)
            return 0;

        // 3. Output-Verzeichnisse anlegen
        var webDir    = Path.Combine(request.OutputFolder, "web");
        var photosDir = Path.Combine(webDir, "photos");
        var mapsDir   = Path.Combine(webDir, "maps");
        Directory.CreateDirectory(photosDir);
        Directory.CreateDirectory(mapsDir);

        // 4. Fotos optimieren
        var allPhotos = startPhotos.Concat(middlePhotos).Concat(endPhotos).ToList();
        for (int i = 0; i < allPhotos.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var p       = allPhotos[i];
            var srcPath = Path.Combine(request.SourcePhotoFolder, p.Filename);
            if (!File.Exists(srcPath)) continue;

            var destName = Path.GetFileNameWithoutExtension(p.Filename) + ".jpg";
            progress?.Report(new WebExportProgress("photos", i + 1, allPhotos.Count, p.Filename));
            await _imageOptimizer.OptimizeAsync(
                srcPath, Path.Combine(photosDir, destName),
                request.MaxImageWidthPx, request.JpegQuality, ct);
        }

        // 5. Karten kopieren
        for (int i = 0; i < maps.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new WebExportProgress("maps", i + 1, maps.Count, maps[i].Filename));
            var srcMap  = Path.Combine(request.SourcePhotoFolder, maps[i].Filename);
            var destMap = Path.Combine(mapsDir, maps[i].Filename);
            if (File.Exists(srcMap))
                File.Copy(srcMap, destMap, overwrite: true);
        }

        // 6. Manifest bauen
        var manifest = _manifestBuilder.Build(
            startPhotos,
            middlePhotos.Where(p => p.DateTime.HasValue).ToList(),
            maps,      // Photo-Einträge mit EntryType=Map
            endPhotos,
            request.Title,
            request.PhotoDurationMs,
            request.OverlayVisibleMs);

        // 7. tour.json schreiben
        var json = JsonSerializer.Serialize(manifest, JsonOpts);
        await File.WriteAllTextAsync(Path.Combine(request.OutputFolder, "tour.json"), json, ct);

        // 8. Templates schreiben
        progress?.Report(new WebExportProgress("templates", 0, 3, "Templates schreiben …"));

        var replacements = new Dictionary<string, string>
        {
            ["{{TITLE}}"]        = System.Text.Encodings.Web.HtmlEncoder.Default.Encode(request.Title),
            ["{{GENERATED_AT}}"] = DateTimeOffset.UtcNow.ToString("O"),
            ["{{TOUR_JSON}}"]    = json
        };
        await WriteTemplateAsync("index.html", request.OutputFolder, replacements, ct);
        await WriteTemplateAsync("style.css",  webDir, null, ct);
        await WriteTemplateAsync("app.js",     webDir, null, ct);

        return manifest.Items.Count;
    }

    private static async Task WriteTemplateAsync(
        string                       filename,
        string                       outputFolder,
        Dictionary<string, string>?  replacements,
        CancellationToken            ct)
    {
        var assembly = typeof(WebPresentationExporter).Assembly;
        var resName  = $"TravelJournal.WebExporter.Templates.{filename}";
        using var stream = assembly.GetManifestResourceStream(resName)
            ?? throw new InvalidOperationException($"Embedded resource not found: {resName}");
        using var reader = new StreamReader(stream);
        var content = await reader.ReadToEndAsync(ct);

        if (replacements != null)
            foreach (var (k, v) in replacements)
                content = content.Replace(k, v);

        await File.WriteAllTextAsync(Path.Combine(outputFolder, filename), content, ct);
    }
}
