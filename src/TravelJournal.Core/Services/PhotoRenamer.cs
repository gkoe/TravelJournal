using TravelJournal.Core.Models;

namespace TravelJournal.Core.Services;

public sealed class PhotoRenamer : IPhotoRenamer
{
    private readonly TourCsvWriter _csvWriter;

    public PhotoRenamer(TourCsvWriter csvWriter) => _csvWriter = csvWriter;

    public async Task<RenameResult> RenameAsync(
        string                     folderPath,
        IReadOnlyList<Photo>       currentEntries,
        RenameOptions              options,
        IProgress<RenameProgress>? progress = null,
        CancellationToken          ct       = default)
    {
        // 1) CSV-Backup
        var csvPath = Path.Combine(folderPath, "tour.csv");
        if (File.Exists(csvPath))
        {
            var backup = Path.Combine(folderPath,
                $"tour.csv.backup-{DateTime.Now:yyyyMMdd-HHmmss}");
            File.Copy(csvPath, backup, overwrite: false);
        }

        // 2) Rename-Plan (nur Foto-Einträge, nicht Karten)
        var plan = BuildRenamingPlan(currentEntries, options);

        var renamed           = new List<RenameOperation>();
        var skippedAlready    = new List<string>();
        var skippedNoDateTime = new List<string>();
        var errors            = new List<RenameError>();

        // Fotos ohne DateTime merken
        foreach (var p in currentEntries.Where(p => p.EntryType == EntryType.Photo && !p.DateTime.HasValue))
            skippedNoDateTime.Add(p.Filename);

        // 3) Bereits korrekt benannte Dateien filtern + Konflikt-Check
        var toRename = new List<(Photo photo, string targetName)>();
        foreach (var (photo, targetName) in plan)
        {
            ct.ThrowIfCancellationRequested();

            if (string.Equals(photo.Filename, targetName, StringComparison.OrdinalIgnoreCase))
            {
                skippedAlready.Add(photo.Filename);
                continue;
            }

            // Konflikt: Zieldatei existiert und wird nicht durch die Umbenennung freigegeben
            var targetPath = Path.Combine(folderPath, targetName);
            if (File.Exists(targetPath))
            {
                bool willBeFreed = plan.Any(kvp =>
                    string.Equals(kvp.Key.Filename, targetName, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(kvp.Value, targetName, StringComparison.OrdinalIgnoreCase));

                if (!willBeFreed)
                {
                    errors.Add(new RenameError(photo.Filename,
                        $"Zieldatei '{targetName}' existiert bereits."));
                    continue;
                }
            }

            toRename.Add((photo, targetName));
        }

        if (toRename.Count == 0)
            return new RenameResult(renamed, skippedAlready, skippedNoDateTime, errors);

        int total = toRename.Count;

        // 4a) Pass 1: alle Dateien auf temp-Namen verschieben
        var tempMapping = new Dictionary<Photo, string>(toRename.Count);
        var pass1Done   = new List<(Photo photo, string tempName, string origName)>();

        foreach (var (photo, _) in toRename)
        {
            ct.ThrowIfCancellationRequested();
            var srcPath  = Path.Combine(folderPath, photo.Filename);
            var tempName = photo.Filename + ".renaming." + Guid.NewGuid().ToString("N");
            var tempPath = Path.Combine(folderPath, tempName);

            try
            {
                File.Move(srcPath, tempPath);
                pass1Done.Add((photo, tempName, photo.Filename));
                tempMapping[photo] = tempName;
            }
            catch (Exception ex)
            {
                // Rollback aller bisherigen Pass-1-Umbenennungen
                foreach (var (_, doneTempName, doneOrigName) in pass1Done)
                {
                    try { File.Move(Path.Combine(folderPath, doneTempName),
                                    Path.Combine(folderPath, doneOrigName)); }
                    catch { /* best effort */ }
                }
                errors.Add(new RenameError(photo.Filename, $"Pass 1 fehlgeschlagen: {ex.Message}"));
                return new RenameResult(renamed, skippedAlready, skippedNoDateTime, errors);
            }
        }

        // 4b) Pass 2: temp-Namen auf finale Ziel-Namen verschieben
        int current   = 0;
        var logLines  = new List<string>();

        foreach (var (photo, targetName) in toRename)
        {
            ct.ThrowIfCancellationRequested();
            current++;
            progress?.Report(new RenameProgress(current, total, $"{photo.Filename} → {targetName}"));

            var tempPath  = Path.Combine(folderPath, tempMapping[photo]);
            var finalPath = Path.Combine(folderPath, targetName);

            try
            {
                File.Move(tempPath, finalPath);
                logLines.Add($"{DateTime.Now:yyyy-MM-ddTHH:mm:ss} {photo.Filename} → {targetName}");
                renamed.Add(new RenameOperation(photo.Filename, targetName));
                photo.Filename = targetName;
            }
            catch (Exception ex)
            {
                errors.Add(new RenameError(photo.Filename,
                    $"Pass 2 fehlgeschlagen (liegt als '{Path.GetFileName(tempPath)}' vor): {ex.Message}"));
            }
        }

        // 5) CSV mit allen Einträgen (Fotos + Karten) neu schreiben
        if (renamed.Count > 0)
            _csvWriter.Write(csvPath, currentEntries);

        // 6) renames.log anhängen
        if (logLines.Count > 0)
        {
            var logPath = Path.Combine(folderPath, "renames.log");
            await File.AppendAllLinesAsync(logPath, logLines, CancellationToken.None);
        }

        return new RenameResult(renamed, skippedAlready, skippedNoDateTime, errors);
    }

    private static Dictionary<Photo, string> BuildRenamingPlan(
        IReadOnlyList<Photo> entries,
        RenameOptions        options)
    {
        var plan = new Dictionary<Photo, string>();

        var sorted = entries
            .Where(p => p.EntryType == EntryType.Photo && p.DateTime.HasValue)
            .OrderBy(p => p.DateTime!.Value)
            .ThenBy(p => p.Filename, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var basenameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var photo in sorted)
        {
            var dt       = photo.DateTime!.Value;
            var ext      = Path.GetExtension(photo.Filename).ToLowerInvariant();
            var basename = options.BuildBaseName(dt, photo.Location);

            var n = basenameCounts.GetValueOrDefault(basename, 0) + 1;
            basenameCounts[basename] = n;

            plan[photo] = n == 1
                ? $"{basename}{ext}"
                : $"{basename}_{n}{ext}";
        }

        return plan;
    }
}
