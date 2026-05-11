# Claude Code Anweisung — Schritt 11: HEIC/HEIF-Konvertierung in JPEG

## Ziel

Fotos im HEIC/HEIF-Format (iPhone-Standard seit iOS 11) sollen sich nahtlos in den TravelJournal-Workflow einfügen, indem sie auf Knopfdruck nach JPEG konvertiert werden. **EXIF-Metadaten — insbesondere `DateTimeOriginal`, GPS-Koordinaten und Orientation — bleiben dabei vollständig erhalten**. Die Originaldateien werden nicht gelöscht, sondern in einen Backup-Unterordner verschoben.

## Kontext

Setzt auf Schritt 1–10 auf. Hauptbetroffene Dateien:

- Neuer Service `IHeicConverter` und Implementation in `TravelJournal.Core/Services/`.
- `PhotoFolderScanner` wird erweitert, sodass HEIC/HEIF-Dateien als eigene Item-Klasse erkannt werden (analog zu den `MapItem`s aus Schritt 7).
- `MainViewModel` und `MainWindow` bekommen einen neuen Toolbar-Button „HEIC konvertieren" plus visuelle Markierung in der Galerie.

Die Konvertierung wird **bewusst nicht automatisch** beim Scan ausgelöst — der Nutzer behält die Kontrolle über Dateioperationen im Foto-Ordner.

## Tech-Stack

- **.NET 10**, C# 13.
- **NuGet (in `TravelJournal.Core` neu):** `Magick.NET-Q8-AnyCPU` (aktuelle Version, ≥ 13.x). Q8 ist ausreichend (8 Bit pro Channel) und spart Speicher gegenüber Q16. Magick.NET bringt `libheif` mit — keine separate native Abhängigkeit nötig.
- Lizenz: ImageMagick (Apache-2.0-kompatibel).

## Datenmodell-Erweiterung

### Neuer Item-Typ `HeicItem` in `TravelJournal.Core/Models/`

Analog zu `MapItem` aus Schritt 7:

```csharp
public sealed class HeicItem
{
    public required string Filename { get; init; }       // z.B. "IMG_4523.heic"
    public required string FullPath { get; init; }
    public required DateTime? DateTime { get; init; }    // aus EXIF, falls schon ermittelt
}
```

`PhotoFolderScanner.ScanResult` wird um eine zusätzliche Liste erweitert:

```csharp
public sealed record ScanResult(
    IReadOnlyList<Photo> Photos,
    IReadOnlyList<MapItem> Maps,
    IReadOnlyList<HeicItem> HeicCandidates,    // NEU
    IReadOnlyList<string> NewFilenames,
    IReadOnlyList<string> MissingFilenames
);
```

Erkennungs-Pattern im Scanner: `*.heic`, `*.heif` (case-insensitive). Diese Dateien werden **nicht** als `Photo` aufgenommen (sonst würden sie im normalen Workflow fehlerhaft behandelt) und auch nicht in `MissingFilenames` gewertet.

## Service in `TravelJournal.Core/Services/`

```csharp
public interface IHeicConverter
{
    /// <summary>
    /// Konvertiert eine einzelne HEIC/HEIF-Datei nach JPEG und verschiebt das Original
    /// in den Backup-Unterordner.
    /// </summary>
    Task<HeicConversionResult> ConvertAsync(
        string heicFilePath,
        HeicConversionOptions options,
        CancellationToken ct = default);
}

public sealed class HeicConversionOptions
{
    public int JpegQuality { get; init; } = 90;
    public string BackupSubfolderName { get; init; } = "heic-original";
}

public sealed record HeicConversionResult(
    string SourcePath,
    string OutputJpegPath,
    string BackupPath,
    bool ExifPreserved,
    long OriginalSizeBytes,
    long ConvertedSizeBytes
);
```

### Implementierung `MagickHeicConverter`

```csharp
public sealed class MagickHeicConverter : IHeicConverter
{
    public async Task<HeicConversionResult> ConvertAsync(
        string heicFilePath,
        HeicConversionOptions options,
        CancellationToken ct = default)
    {
        if (!File.Exists(heicFilePath))
            throw new FileNotFoundException("HEIC-Datei nicht gefunden", heicFilePath);

        var dir = Path.GetDirectoryName(heicFilePath)!;
        var nameNoExt = Path.GetFileNameWithoutExtension(heicFilePath);
        var jpegPath = Path.Combine(dir, nameNoExt + ".jpg");

        // Konflikt-Vermeidung: falls "<name>.jpg" schon existiert, hänge Suffix an
        if (File.Exists(jpegPath))
            jpegPath = Path.Combine(dir, $"{nameNoExt}_converted.jpg");

        var originalSize = new FileInfo(heicFilePath).Length;

        // Konvertierung — EXIF/IPTC/XMP bleiben durch Magick.NET-Default erhalten
        var exifPreserved = false;
        await Task.Run(() =>
        {
            using var image = new MagickImage(heicFilePath);
            image.Quality = (uint)options.JpegQuality;
            image.Format = MagickFormat.Jpeg;
            image.Write(jpegPath);
            exifPreserved = image.GetExifProfile() is not null;
        }, ct);

        // Datei-Zeitstempel der JPG auf Original-DateCreated setzen,
        // damit Datei-Explorer-Sortierung weiter stimmt
        var heicInfo = new FileInfo(heicFilePath);
        File.SetLastWriteTime(jpegPath, heicInfo.LastWriteTime);
        File.SetCreationTime(jpegPath, heicInfo.CreationTime);

        // Original in Backup-Subfolder verschieben
        var backupDir = Path.Combine(dir, options.BackupSubfolderName);
        Directory.CreateDirectory(backupDir);
        var backupPath = Path.Combine(backupDir, Path.GetFileName(heicFilePath));
        if (File.Exists(backupPath))
            backupPath = Path.Combine(backupDir,
                $"{nameNoExt}_{DateTime.Now:yyyyMMdd-HHmmss}{Path.GetExtension(heicFilePath)}");
        File.Move(heicFilePath, backupPath);

        var convertedSize = new FileInfo(jpegPath).Length;

        return new HeicConversionResult(
            SourcePath: heicFilePath,
            OutputJpegPath: jpegPath,
            BackupPath: backupPath,
            ExifPreserved: exifPreserved,
            OriginalSizeBytes: originalSize,
            ConvertedSizeBytes: convertedSize);
    }
}
```

Wichtig zur EXIF-Erhaltung: Magick.NET kopiert beim Schreiben standardmäßig alle Metadaten-Profile (EXIF, IPTC, XMP, ICC) aus dem Quellbild ins Zielbild. Es ist **kein** explizites `image.GetExifProfile()`/`SetExifProfile()` notwendig. Der `exifPreserved`-Wert im Rückgabewert dient nur der Verifikation für das UI/Logging.

## WPF-Integration

### `MainViewModel`-Erweiterung

```csharp
public ObservableCollection<HeicItemViewModel> HeicCandidates { get; } = new();

[RelayCommand(CanExecute = nameof(CanConvertHeic))]
private async Task ConvertHeicAsync()
{
    if (CurrentFolder is null || HeicCandidates.Count == 0) return;

    var converter = _serviceProvider.GetRequiredService<IHeicConverter>();
    var options = new HeicConversionOptions { JpegQuality = 90 };

    IsBusy = true;
    var cts = new CancellationTokenSource();
    var converted = 0;

    try
    {
        foreach (var item in HeicCandidates.ToList())   // Kopie wegen Modifikation
        {
            cts.Token.ThrowIfCancellationRequested();
            BackgroundActivityText = $"Konvertiere {item.Filename} ({converted + 1}/{HeicCandidates.Count})";

            try
            {
                var result = await converter.ConvertAsync(item.FullPath, options, cts.Token);
                converted++;
            }
            catch (Exception ex)
            {
                BackgroundActivityText = $"Fehler bei {item.Filename}: {ex.Message}";
                // Mit nächster Datei weitermachen, nicht abbrechen
            }
        }

        BackgroundActivityText = $"{converted} HEIC-Datei(en) konvertiert. Originale in 'heic-original/'.";

        // Re-Scan, damit die neuen JPGs in der Galerie auftauchen
        await RescanAsync();
    }
    finally
    {
        IsBusy = false;
    }
}

private bool CanConvertHeic() => !IsBusy && HeicCandidates.Count > 0;
```

`HeicItemViewModel` ist ein simpler Wrapper analog zu `MapItemViewModel` — `Filename`, `FullPath`, `DateTime`, optional ein generisches Thumbnail (z.B. ein „HEIC"-Platzhalter-Icon, weil HEIC-Thumbnails ohne Decode nicht trivial sind).

### Galerie-Anzeige

Die HEIC-Items erscheinen in der Galerie wie `MapItem`s als eigene Kategorie:

- `IGalleryItem`-Interface aus Schritt 7 wird auch von `HeicItemViewModel` implementiert.
- `MatchesFilter`: HEIC-Items zeigt nur der neue Filter „HEIC" oder „Alle" an.
- Im `DataTemplateSelector` ein neues `HeicTemplate` mit Platzhalter-Icon und „HEIC"-Badge in Akzentfarbe.
- Klick auf HEIC-Item zeigt rechts einen Hinweistext: „Diese Datei muss zuerst nach JPEG konvertiert werden, bevor sie in die Diashow aufgenommen werden kann. Klick auf „HEIC konvertieren" in der Toolbar."

### Filter-Erweiterung

```csharp
public enum PhotoFilter { All, Open, Selected, Deselected, New, Maps, Heic }
```

### Toolbar-Button

In der „BULK"-Sektion (über oder unter „Alle offenen abwählen"):

```xml
<Button Content="HEIC konvertieren"
        Command="{Binding ConvertHeicCommand}"
        ToolTip="Konvertiert alle HEIC/HEIF-Dateien im Foto-Ordner in JPEG.
EXIF-Daten (Datum, GPS) bleiben erhalten.
Originale werden in den Unterordner 'heic-original/' verschoben."
        Margin="0,4,0,0"/>
```

Anzahl-Hinweis im Button-Text wäre charmant — z.B. via Multi-Binding `Content="{Binding HeicCandidates.Count, StringFormat='HEIC konvertieren ({0})'}"` (vereinfacht via Converter), aber optional.

### DI-Registrierung in `App.xaml.cs`

```csharp
services.AddSingleton<IHeicConverter, MagickHeicConverter>();
```

## Tests in `tests/TravelJournal.Core.Tests/Services/`

Realistisch testbar (benötigt eine Test-HEIC-Datei als Embedded Resource oder im `TestData/`-Ordner):

- `MagickHeicConverter.ConvertAsync` mit einem Test-HEIC erzeugt eine `.jpg`-Datei am erwarteten Pfad.
- Die JPG-Datei ist mit `MetadataExtractor` lesbar; `DateTimeOriginal` aus dem JPG entspricht dem aus dem HEIC.
- GPS-Koordinaten aus EXIF stimmen vor und nach Konvertierung überein (mit angemessener Float-Toleranz).
- Original-HEIC ist nach Konvertierung im `heic-original/`-Unterordner.
- Bei Konflikt (Ziel-JPG existiert bereits): Suffix `_converted` wird angehängt, kein Überschreiben.
- `JpegQuality = 90` ergibt eine plausibel große Datei (z.B. > 100 KB für ein typisches iPhone-Foto).

Für die Test-Fixture eignet sich eine kleine HEIC-Datei mit bekannten EXIF-Werten — z.B. ein iPhone-Schnappschuss eines beliebigen Motivs, ~1–2 MB groß, einmal commited unter `tests/TravelJournal.Core.Tests/TestData/sample.heic`.

`PhotoFolderScannerTests` bekommt einen zusätzlichen Case:

- Ordner mit gemischtem Inhalt (JPG + HEIC + Karten-PNG) → `Photos`, `Maps` und `HeicCandidates` werden korrekt befüllt, keine Überschneidungen.

## Akzeptanzkriterien

- `dotnet build` warningfrei, alle Tests grün.
- HEIC-Dateien im Foto-Ordner erscheinen in der Galerie als eigene Kategorie mit „HEIC"-Badge.
- Filter „HEIC" zeigt ausschließlich HEIC-Items.
- Toolbar-Button „HEIC konvertieren" konvertiert alle vorhandenen HEIC/HEIF-Dateien sequentiell. Fortschritt erscheint live in der Statuszeile (`BackgroundActivityText`).
- Nach Abschluss: Re-Scan läuft automatisch, neue JPGs erscheinen in der Galerie als reguläre `Photo`s mit ihren Metadaten (Datum, GPS, Höhe, Pixelmaße, Dateigröße).
- Original-HEIC-Dateien liegen im Unterordner `heic-original/` neben den Fotos und sind aus der Galerie verschwunden.
- `EXIF-DateTimeOriginal` und GPS-Koordinaten der konvertierten JPGs sind identisch zu denen der HEIC-Originale (per `ExifReaderService` verifizierbar).
- Bestehende `tour.csv`-Einträge bleiben unverändert; falls eine HEIC-Datei vorher (rein hypothetisch) in der CSV stand, taucht sie nach Konvertierung als JPG mit gleichen Metadaten und Status `None` (= neu) auf.
- Konvertierung blockiert die UI nicht hart — `IsBusy=true` ist gesetzt, aber Fehler bei einzelnen Dateien führen nicht zum Abbruch der gesamten Batch.

## Was bewusst NICHT teil dieser Iteration ist

- PNG-Output (JPEG ist für Fotos die natürliche Wahl; PNG würde 5–10× größere Dateien ohne Qualitätsgewinn erzeugen)
- Konfigurierbare JPEG-Qualität in der UI (über `HeicConversionOptions` im Code änderbar)
- Auto-Konvertierung beim Scan (bewusst manueller Trigger für Datei-Sicherheit)
- Konvertierung anderer Formate (RAW, TIFF, WebP) — Muster ist aber leicht erweiterbar
- Parallele Konvertierung mehrerer Dateien (sequentiell ist robust und CPU-bound; eine Datei nach der anderen)
- Wiederherstellung aus dem Backup-Ordner per UI (manuell durch den Nutzer machbar)
- HEIC-Vorschau im Detail-Bereich vor der Konvertierung (würde einen separaten Decode-Pfad in der UI brauchen)
