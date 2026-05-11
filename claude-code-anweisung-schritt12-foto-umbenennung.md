# Claude Code Anweisung — Schritt 12: Einheitliche Foto-Umbenennung

## Ziel

Da Fotos aus verschiedenen Quellen stammen (eigene Kamera, Smartphone, Gruppen-Bilder von Freunden) liegen sie mit ganz unterschiedlichen Dateinamen-Konventionen vor. Eine neue Funktion benennt **alle Fotos im Foto-Ordner** auf ein einheitliches, sortier­freundliches Schema um:

```
YYYY_MM_DD_hh_mm_<Ort>.jpg     (mit Ort, falls bekannt)
YYYY_MM_DD_hh_mm.jpg           (ohne Ort)
```

Sonderzeichen im Ortsnamen werden bereinigt, Umlaute ASCII-gefoldet, Mehrfach-Treffer in derselben Minute mit `_2`, `_3` usw. disambiguiert. Die `tour.csv` wird im selben Vorgang transaktional mit-aktualisiert, sodass alle Status, Title, Description und Location der jeweiligen Fotos erhalten bleiben.

## Kontext

Setzt auf Schritt 1–11 auf. Hauptbetroffene Dateien:

- Neuer Service `IPhotoRenamer` und Implementation in `TravelJournal.Core/Services/`.
- Neuer Helper `FilenameSafeName` in `TravelJournal.Core/Utilities/` für Ortsnamen-Bereinigung.
- `MainViewModel` bekommt neuen Command `RenamePhotosCommand` mit Bestätigungs-Dialog und Live-Fortschritt.
- Toolbar-Button „Fotos umbenennen" in der „BULK"-Sektion.

Die Operation ist destruktiv (Dateien werden umbenannt, CSV überschrieben). Vor dem Ausführen werden Sicherheitsmaßnahmen getroffen: Backup der CSV und Logging aller Umbenennungen.

## Schema im Detail

Komponenten in der Reihenfolge:

| Position | Quelle | Format | Beispiel |
|---|---|---|---|
| Datum | `Photo.DateTime` (Datum-Anteil) | `yyyy_MM_dd` | `2026_04_27` |
| Uhrzeit | `Photo.DateTime` (Stunde + Minute) | `HH_mm` | `12_22` |
| Ort | `Photo.Location` (bereinigt) | `<safe>` | `Tarvis` |
| Disambiguierungs-Suffix | (nur bei Konflikt) | `_<N>` (N≥2) | `_2`, `_3`, … |
| Endung | unverändert | `.jpg` / `.jpeg` | `.jpg` |

Beispiele aus der vorhandenen `tour.csv`:

| Original | Neu |
|---|---|
| `20260427_122257.jpg` (Tarvis, 12:22) | `2026_04_27_12_22_Tarvis.jpg` |
| `20260427_122319.jpg` (Tarvis, 12:23) | `2026_04_27_12_23_Tarvis.jpg` |
| `20260428_103406.jpg` (Chiusaforte / Scluse, 10:34) | `2026_04_28_10_34_ChiusaforteScluse.jpg` |
| `20260428_103419.jpg` (Chiusaforte / Scluse, 10:34) | `2026_04_28_10_34_ChiusaforteScluse_2.jpg` |
| `20260428_103445.jpg` (Chiusaforte / Scluse, 10:34) | `2026_04_28_10_34_ChiusaforteScluse_3.jpg` |
| `20260427_094924(0).jpg` (kein Ort, 09:49) | `2026_04_27_09_49.jpg` |
| `Start.jpg` (kein DateTime, kein Ort) | bleibt unverändert |

**Regeln für Sonderfälle:**

- Foto **ohne `DateTime`** → wird **nicht umbenannt** und in einer Warn-Liste zurückgegeben (Beispiel: `Start.jpg`).
- Foto **ohne `Location`** → Schema ohne Orts-Teil: `YYYY_MM_DD_hh_mm.jpg`.
- **Konflikt** in derselben Minute (gleicher Ort oder beide ohne Ort) → Suffix `_2`, `_3`, … in der Reihenfolge der `Photo.DateTime`-Sortierung (Sekunden-Bruchteile entscheiden).
- **Bereits korrekt benannte Datei** (Regex-Match auf das Schema) → übersprungen, kein No-Op-Rename.
- **Karten-Dateien** (`map_*.png`) und **HEIC-Backup-Ordner** (`heic-original/`) bleiben unangetastet.

## Ortsnamen-Bereinigung — `FilenameSafeName`

Neue Datei `TravelJournal.Core/Utilities/FilenameSafeName.cs`:

```csharp
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

public static class FilenameSafeName
{
    /// <summary>
    /// Wandelt einen Ortsnamen in eine dateinamen-taugliche Form: ASCII-only,
    /// keine Sonderzeichen, PascalCase-artige Zusammenschreibung.
    /// Beispiele:
    ///   "Villach"                  → "Villach"
    ///   "Stein am Rhein"           → "SteinAmRhein"
    ///   "Chiusaforte / Scluse"     → "ChiusaforteScluse"
    ///   "Grado / Grau"             → "GradoGrau"
    ///   "São Paulo"                → "SaoPaulo"
    ///   "  "                       → ""
    /// </summary>
    public static string FromLocation(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return string.Empty;

        // 1. Deutsche Umlaute und ß explizit foldern (vor der Unicode-Decomposition,
        //    weil das Standard-NFKD ä → a + Combining Diaeresis macht und
        //    aus a wird kein ae)
        var folded = location
            .Replace("ä", "ae").Replace("Ä", "Ae")
            .Replace("ö", "oe").Replace("Ö", "Oe")
            .Replace("ü", "ue").Replace("Ü", "Ue")
            .Replace("ß", "ss");

        // 2. Unicode-Normalization: Akzente, Tildes etc. abspalten
        var normalized = folded.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        // 3. An jedem nicht-alphanumerischen Zeichen splitten,
        //    leere Chunks verwerfen, jeden Chunk PascalCase-isieren
        var chunks = Regex.Split(sb.ToString(), "[^A-Za-z0-9]+")
            .Where(s => s.Length > 0)
            .Select(CapitalizeFirst);

        return string.Concat(chunks);
    }

    private static string CapitalizeFirst(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
```

Tests in `TravelJournal.Core.Tests/Utilities/FilenameSafeNameTests.cs`:

- `"Villach"` → `"Villach"`
- `"Stein am Rhein"` → `"SteinAmRhein"`
- `"Chiusaforte / Scluse"` → `"ChiusaforteScluse"`
- `"Grado / Grau"` → `"GradoGrau"`
- `"São Paulo"` → `"SaoPaulo"`
- `"München"` → `"Muenchen"`
- `"Bad Ischl-Lauffen"` → `"BadIschlLauffen"`
- `null` → `""`
- `"   "` → `""`
- `"!@#$%"` → `""`
- `"123 Foo"` → `"123Foo"`

## Service `IPhotoRenamer` in `TravelJournal.Core/Services/`

```csharp
public interface IPhotoRenamer
{
    Task<RenameResult> RenameAsync(
        string folderPath,
        IReadOnlyList<Photo> currentPhotos,
        IProgress<RenameProgress>? progress = null,
        CancellationToken ct = default);
}

public sealed record RenameProgress(int Current, int Total, string? Message);

public sealed record RenameResult(
    IReadOnlyList<RenameOperation> Renamed,
    IReadOnlyList<string> SkippedAlreadyMatching,
    IReadOnlyList<string> SkippedNoDateTime,
    IReadOnlyList<RenameError> Errors
);

public sealed record RenameOperation(string OldFilename, string NewFilename);

public sealed record RenameError(string Filename, string Reason);
```

### Algorithmus

1. **Eingabe-Validierung**: Foto-Liste muss zur `tour.csv` im Ordner passen (sonst Fehler — sicherer als blind zu arbeiten).
2. **Backup der CSV**: `tour.csv` wird vor jeder Aktion nach `tour.csv.backup-<timestamp>` kopiert.
3. **Plan erstellen**: für jede `Photo` mit `DateTime != null`:
   - Ziel-Basisnamen aus `DateTime` und `Location` (via `FilenameSafeName`) bauen.
   - Kollisionen über alle geplanten Ziele auflösen: bei Konflikt der zweite/dritte/… `_2`, `_3` anhängen, Reihenfolge bestimmt durch `Photo.DateTime` aufsteigend (deterministisch und reproduzierbar).
4. **Already-matched filtern**: Fotos, deren aktueller Filename schon dem geplanten Ziel entspricht, werden in `SkippedAlreadyMatching` aufgenommen — kein Datei-Move, keine CSV-Änderung.
5. **Konflikt-Check**: existiert für ein geplantes Ziel bereits eine Datei im Ordner, die nicht selbst umbenannt wird? Falls ja → Fehler in `Errors`, dieses Foto wird nicht umbenannt.
6. **Zwei-Pass-Umbenennung** (verhindert Selbst-Kollisionen, wenn `A → B` und `B → C`):
   - **Pass 1**: alle umzubenennenden Dateien in einen temporären Namen wandern lassen: `<oldname>.renaming.<guid>`. Bei Fehler abbrechen und alle Temp-Dateien zurückbenennen.
   - **Pass 2**: alle Temp-Dateien auf ihren finalen Zielnamen wandern. Bei Fehler den temporären Namen behalten und in `Errors` aufnehmen.
7. **CSV aktualisieren**: für jede erfolgreich umbenannte Datei wird in der in-Memory-`Photo`-Liste das `Filename`-Feld umgesetzt; danach wird die `tour.csv` neu geschrieben (über `TourCsvWriter`). Title, Description, Location, State bleiben unverändert.
8. **Log schreiben**: eine `renames.log` (im selben Ordner, append) dokumentiert jede Umbenennung mit Zeitstempel — `2026-05-09T17:42:11 OldName.jpg → NewName.jpg`. Hilft dem Nutzer, Änderungen zu verfolgen oder bei Bedarf manuell rückgängig zu machen.
9. **Rückgabewert**: `RenameResult` mit Listen für Renamed, Skipped, Errors.

### Beispiel `RenameAsync`-Skelett

```csharp
public sealed class PhotoRenamer : IPhotoRenamer
{
    private static readonly Regex AlreadyMatchingPattern = new(
        @"^\d{4}_\d{2}_\d{2}_\d{2}_\d{2}(_[A-Za-z0-9]+)?(_\d+)?\.jpe?g$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public async Task<RenameResult> RenameAsync(
        string folderPath,
        IReadOnlyList<Photo> currentPhotos,
        IProgress<RenameProgress>? progress = null,
        CancellationToken ct = default)
    {
        // 1) Backup der CSV
        var csvPath = Path.Combine(folderPath, "tour.csv");
        if (File.Exists(csvPath))
        {
            var backup = Path.Combine(folderPath,
                $"tour.csv.backup-{DateTime.Now:yyyyMMdd-HHmmss}");
            File.Copy(csvPath, backup, overwrite: false);
        }

        // 2) Plan: Photo → geplanter neuer Filename
        var plan = BuildRenamingPlan(currentPhotos);

        // 3) Skip-Listen + Konflikt-Erkennung
        // …

        // 4) Two-pass rename (siehe oben)
        // …

        // 5) CSV neu schreiben
        // …

        return new RenameResult(...);
    }

    private static Dictionary<Photo, string> BuildRenamingPlan(IReadOnlyList<Photo> photos)
    {
        var plan = new Dictionary<Photo, string>();
        // Nach DateTime sortieren für deterministische Disambiguierung
        var sorted = photos
            .Where(p => p.DateTime.HasValue)
            .OrderBy(p => p.DateTime!.Value)
            .ThenBy(p => p.Filename, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var basenameCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var photo in sorted)
        {
            var dt = photo.DateTime!.Value;
            var ext = Path.GetExtension(photo.Filename);
            var ortPart = FilenameSafeName.FromLocation(photo.Location);
            var basename = string.IsNullOrEmpty(ortPart)
                ? $"{dt:yyyy_MM_dd_HH_mm}"
                : $"{dt:yyyy_MM_dd_HH_mm}_{ortPart}";

            var n = basenameCounts.GetValueOrDefault(basename, 0) + 1;
            basenameCounts[basename] = n;

            var finalName = n == 1
                ? $"{basename}{ext}"
                : $"{basename}_{n}{ext}";

            plan[photo] = finalName;
        }
        return plan;
    }
}
```

### Tests in `TravelJournal.Core.Tests/Services/PhotoRenamerTests.cs`

Realistisch testbar mit einem Temp-Ordner und in-Code erzeugten Mini-JPGs:

- Drei Fotos in derselben Minute mit gleichem Ort → bekommen Suffixe `_2`, `_3` in DateTime-Reihenfolge.
- Foto ohne `DateTime` wird nicht umbenannt, taucht in `SkippedNoDateTime` auf.
- Foto ohne `Location` wird zu `YYYY_MM_DD_hh_mm.jpg`.
- Foto, dessen Filename bereits dem Schema entspricht, wird übersprungen (in `SkippedAlreadyMatching`).
- Foto-Kette `A.jpg → B.jpg`, `B.jpg → C.jpg`: Two-Pass funktioniert ohne Datenverlust.
- Nach erfolgreichem Lauf existiert eine `tour.csv.backup-*` und `renames.log`.
- `tour.csv` enthält die neuen Dateinamen, alle anderen Felder (State, Title, Description, Location) bleiben für jedes Foto identisch.
- HEIC-Backup-Ordner wird ignoriert (Dateien dort werden nicht angerührt).

## WPF-Integration

### `MainViewModel`-Erweiterung

```csharp
[RelayCommand(CanExecute = nameof(CanRenamePhotos))]
private async Task RenamePhotosAsync()
{
    if (CurrentFolder is null) return;

    var renameableCount = Photos.Count(p =>
        p.DateTime is not null
        && !PhotoRenamer.AlreadyMatchingPattern.IsMatch(p.Filename));

    var msg = $"Es werden bis zu {renameableCount} Fotos im Ordner umbenannt.\n\n" +
              $"• Schema: YYYY_MM_DD_hh_mm_Ort.jpg\n" +
              $"• tour.csv wird automatisch angepasst (Backup wird angelegt)\n" +
              $"• Karten und HEIC-Originale bleiben unangetastet\n\n" +
              $"Fortfahren?";

    var decision = MessageBox.Show(msg, "Fotos umbenennen",
        MessageBoxButton.YesNo, MessageBoxImage.Warning);
    if (decision != MessageBoxResult.Yes) return;

    IsBusy = true;
    var cts = new CancellationTokenSource();
    var progress = new Progress<RenameProgress>(p =>
    {
        BackgroundActivityText = $"Umbenennen {p.Current}/{p.Total}: {p.Message}";
    });

    try
    {
        var photoModels = Photos.Select(vm => vm.UnderlyingPhoto).ToList();
        var result = await _photoRenamer.RenameAsync(
            CurrentFolder, photoModels, progress, cts.Token);

        BackgroundActivityText =
            $"{result.Renamed.Count} umbenannt, " +
            $"{result.SkippedAlreadyMatching.Count} bereits korrekt, " +
            $"{result.SkippedNoDateTime.Count} ohne Datum übersprungen, " +
            $"{result.Errors.Count} Fehler.";

        // Re-Scan, damit Galerie die neuen Filenames widerspiegelt
        await RescanAsync();
    }
    finally
    {
        IsBusy = false;
    }
}

private bool CanRenamePhotos() =>
    !IsBusy && !string.IsNullOrEmpty(CurrentFolder)
    && Photos.Any(p => p.DateTime is not null);
```

### Toolbar-Button (BULK-Sektion)

```xml
<Button Content="Fotos umbenennen"
        Command="{Binding RenamePhotosCommand}"
        ToolTip="Benennt alle Fotos im Ordner einheitlich um:
YYYY_MM_DD_hh_mm_Ort.jpg
tour.csv wird automatisch mit-aktualisiert (Backup wird angelegt)."
        Margin="0,4,0,0"/>
```

### DI-Registrierung

```csharp
services.AddSingleton<IPhotoRenamer, PhotoRenamer>();
```

## Akzeptanzkriterien

- `dotnet build` warningfrei, alle Tests grün — insbesondere `FilenameSafeNameTests` und `PhotoRenamerTests`.
- Klick auf „Fotos umbenennen" zeigt Bestätigungs-Dialog mit Anzahl betroffener Fotos.
- Nach Bestätigung: alle Fotos im Foto-Ordner werden gemäß Schema umbenannt, Live-Fortschritt in der Statuszeile.
- Nach Abschluss erscheint eine Zusammenfassung (umbenannt / bereits korrekt / übersprungen / Fehler), und ein automatischer Re-Scan zeigt die neuen Namen in der Galerie.
- `tour.csv` enthält die neuen Filenames, alle anderen Spalten (State, Title, Description, Location, DateTime, GPS) sind unverändert für jedes Foto.
- Eine `tour.csv.backup-yyyyMMdd-HHmmss` liegt im Foto-Ordner.
- Eine `renames.log` mit allen `Old → New`-Mappings (zeitgestempelt) liegt im Foto-Ordner; bei wiederholten Läufen wird angehängt, nicht überschrieben.
- Konkrete Beispiele aus der vorhandenen Tour:
  - `20260427_122257.jpg` (Tarvis, 12:22:57) → `2026_04_27_12_22_Tarvis.jpg`
  - Drei `20260428_10_34*.jpg` mit Ort „Chiusaforte / Scluse" → `2026_04_28_10_34_ChiusaforteScluse.jpg`, `..._2.jpg`, `..._3.jpg`
  - `Start.jpg` (kein DateTime) bleibt `Start.jpg`
- Karten (`map_*.png`) und Dateien im `heic-original/`-Unterordner werden nicht angerührt.
- Wiederholtes Ausführen ist idempotent: alle Dateien sind beim zweiten Lauf in `SkippedAlreadyMatching`, kein Datei-Move.

## Was bewusst NICHT teil dieser Iteration ist

- Konfigurierbares Schema in der UI (Format ist als Konstante im Renamer fix)
- Trockenlauf-Modus („Vorschau" ohne Schreibvorgang) — falls gewünscht, einfach via zusätzlichem `bool dryRun`-Parameter ergänzbar
- Rückgängig-Funktion über die UI — die `renames.log` ermöglicht manuelles Zurückbenennen
- Sekunden-Genauigkeit im Schema (auch nicht bei Konflikten — stattdessen `_N`-Suffix)
- Umbenennung anderer Bildformate als JPG/JPEG (PNG bleibt für Karten reserviert)
- Live-Synchronisation mit umliegenden Tools (z.B. wenn die Bilder auf einem geteilten Laufwerk liegen und parallel woanders genutzt werden)
