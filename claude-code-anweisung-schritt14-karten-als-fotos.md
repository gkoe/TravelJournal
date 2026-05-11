# Claude Code Anweisung — Schritt 14: Karten als vollwertige Fotos in CSV und Galerie

## Ziel

Drei zusammenhängende Änderungen, die das Karten-Konzept grundlegend umbauen — von einem reinen Output-Artefakt zu einem **vollwertigen, in der CSV verwalteten Element der Diashow**:

1. **Bug-Fix**: Karten tragen tatsächlich den Ortsnamen im Filename. Die Skip-Regel aus Schritt 13 funktioniert (gleiche Orte werden übersprungen), aber der Filename-Bestandteil `_<Ort>` wird derzeit nicht geschrieben — Karten heißen weiterhin nur `map_<timestamp>.png`. Das ist zu beheben.
2. **Timestamp-Positionierung**: Eine Karte für einen Ort wird so einsortiert, dass sie **chronologisch unmittelbar vor dem ersten Foto dieses Ortes** liegt. Die bisherige Logik „Karte = Stopp + 1 Sekunde" (also nach dem Stopp-Anker-Foto) wird ersetzt.
3. **Karten in der CSV**: Jede generierte Karte wird als regulärer Eintrag in der `tour.csv` geführt — mit DateTime, GPS, State=Selected, Location und (optional) Title/Description. Die Karte wird im WPF-Tool wie ein normales ausgewähltes Foto angezeigt und bearbeitet.

Damit verschwindet die Sonder-Behandlung von Karten als „MapItem" aus Schritt 7. Karten werden zu Fotos im weiten Sinn: andere Datei-Endung (`.png` statt `.jpg`), aber gleiches CSV-Schema, gleiche Galerie-Darstellung, gleiche Bearbeitungs-Möglichkeiten.

## Kontext

Setzt auf Schritt 1–13 auf. Hauptbetroffene Dateien:

- `TravelJournal.Core/MapRendering/TileMapRenderer.cs` — Filename und Timestamp-Berechnung, plus CSV-Update
- `TravelJournal.Core/Services/PhotoFolderScanner.cs` — erkennt PNG-Maps als reguläre Fotos
- `TravelJournal.Core/Services/ExifReaderService.cs` — toleriert PNG-Dateien (kein EXIF erwartbar)
- `TravelJournal.Wpf/ViewModels/MainViewModel.cs` — Maps-Collection und MapItemViewModel werden entfernt
- `TravelJournal.Wpf/Views/MainWindow.xaml` — separater Map-`DataTemplate` und Filter „Karten" entfallen

---

## Änderung 1 — Filename mit Ort (Bug-Fix)

Die Spec aus Schritt 13 ist umzusetzen, falls noch nicht geschehen. Konkret:

```csharp
// im TileMapRenderer
private static string BuildMapFilename(StopPoint stop)
{
    var safeOrt = FilenameSafeName.FromLocation(stop.Location);
    var ts = stop.Timestamp.ToString("yyyy-MM-ddTHH-mm-ss");
    return string.IsNullOrEmpty(safeOrt)
        ? $"map_{ts}.png"
        : $"map_{ts}_{safeOrt}.png";
}
```

Häufige Stolperfallen, falls die Skip-Regel funktioniert aber der Suffix fehlt:

- `StopPoint.Location` wird im `StopDetector` nicht befüllt → in jedem `StopPoint`-Konstruktor-Aufruf den vierten Parameter `photo.Location` ergänzen.
- `BuildMapFilename` benutzt noch die alte Variante ohne `safeOrt` → ersetzen.
- `FilenameSafeName.FromLocation(null)` muss `""` liefern (siehe Schritt 12, ist dort Pflicht-Test).

Akzeptanz-Beispiel: Karte am Stopp in „Tarvis" → `map_2026-04-27T14-30-00_Tarvis.png` im Foto-Ordner.

---

## Änderung 2 — Timestamp = vor erstem Foto des Ortes

### Konzept

Statt `stop.PhotoDateTime + 1 Sekunde` (Karte nach dem Anker-Foto) wird der Karten-Timestamp so gesetzt, dass die Karte chronologisch **unmittelbar vor dem ersten Foto dieses Ortes** in der Tour liegt. Im Effekt:

- Im Galerie- und Diashow-Ablauf erscheint zuerst die Karte („Wir kommen in Tarvis an, hier ist die Route bis hierher"), dann die Fotos aus Tarvis.
- Wird die Karte in der Listen-Sortierung (nach DateTime) korrekt einsortiert.

### Algorithmus

Der `TileMapRenderer` braucht Zugriff auf die ganze Foto-Liste, um „erstes Foto am Ort X" zu finden:

```csharp
private static DateTime ComputeMapTimestamp(
    StopPoint stop,
    IReadOnlyList<Photo> allPhotosWithGps)
{
    var ortKey = FilenameSafeName.FromLocation(stop.Location);

    // Erstes Foto in chronologischer Reihenfolge, dessen Location nach
    // FilenameSafeName-Bereinigung mit dem Stopp-Ort übereinstimmt
    var firstAtOrt = allPhotosWithGps
        .Where(p => p.DateTime.HasValue
                 && string.Equals(
                        FilenameSafeName.FromLocation(p.Location),
                        ortKey,
                        StringComparison.Ordinal))
        .OrderBy(p => p.DateTime!.Value)
        .FirstOrDefault();

    if (firstAtOrt?.DateTime is { } firstDt)
        return firstDt.AddSeconds(-1);

    // Fallback: kein Foto mit diesem Ort gefunden — bleib am Stopp
    return stop.Timestamp;
}
```

Damit liegt die Karte 1 Sekunde **vor** dem ersten Foto des Ortes. Der Sekunden-Offset reicht für die Sortierung; ein Konflikt mit anderen Fotos zur selben Sekunde ist extrem unwahrscheinlich, im Härtefall wird beim Speichern des Files schlicht überschrieben.

### Spezialfall: Final-Summary

Die in `MapRenderingOptions.AddFinalSummaryMap` konfigurierte Schluss-Karte folgt einer anderen Logik — sie ist ein **Wrap-up am Tour-Ende**, kein Ortswechsel-Übergang. Sie behält ihre bisherige Positionierung **am Ende** (Timestamp = letztes Foto + 1 Sekunde). Falls die Skip-Regel aus Schritt 13 sie wegen identischen Ortsnamens entfernt, geschieht das wie gehabt.

### Spezialfall: Karte ohne Ort

Stopp ohne `Location` → fallback wie oben: `stop.Timestamp` unverändert verwenden. Diese Karten kollidieren nicht mit anderen Maps (jeder Stop hat einen einzigartigen Zeitpunkt).

---

## Änderung 3 — Karten als reguläre CSV-Einträge

### Schema-Mapping

Eine Karte erzeugt beim Generieren einen regulären CSV-Eintrag mit folgenden Werten:

| Spalte | Wert |
|---|---|
| `Filename` | `map_<timestamp>_<Ort-bereinigt>.png` (oder ohne `_<Ort>`) |
| `DateTime` | berechneter Timestamp (siehe Änderung 2) |
| `Latitude` | `stop.Latitude` |
| `Longitude` | `stop.Longitude` |
| `Altitude` | `null` (Karten haben keine Höhe) |
| `State` | `1` (Selected) — Karten gehören zur Diashow |
| `Title` | `null` (kann der Nutzer nachträglich befüllen) |
| `Description` | `null` |
| `Location` | **`stop.Location` im Original — voller, unbereinigter Name** |

### Wichtig: zwei verschiedene Schreibweisen des Ortsnamens

Pro Karte existieren **zwei** Repräsentationen des Ortsnamens, die sich klar voneinander unterscheiden:

- **Im Dateinamen**: bereinigte Form via `FilenameSafeName.FromLocation(stop.Location)` — ASCII-only, ohne Sonderzeichen, PascalCase. Notwendig, weil Dateisysteme Slashes, Leerzeichen, Umlaute schlecht verarbeiten und der Filename idealerweise sortier- und tippfreundlich ist.
- **In der CSV-Spalte `Location`**: der **volle Originaltext** wie er im Anker-Foto steht — mit Leerzeichen, Sonderzeichen, Umlauten. Genau so, wie der Nutzer (oder das Reverse-Geocoding) ihn ursprünglich gesetzt hat.

Konkrete Beispiele aus der vorhandenen Tour:

| `Photo.Location` (Anker) | Filename (bereinigt) | CSV-`Location` (voll) |
|---|---|---|
| `Tarvis` | `map_2026-04-27T12-22-56_Tarvis.png` | `Tarvis` |
| `Chiusaforte / Scluse` | `map_2026-04-28T10-23-19_ChiusaforteScluse.png` | `Chiusaforte / Scluse` |
| `Finkenstein am Faaker See` | `map_2026-04-26T16-52-55_FinkensteinAmFaakerSee.png` | `Finkenstein am Faaker See` |
| `Grado / Grau` | `map_2026-04-30T14-29-07_GradoGrau.png` | `Grado / Grau` |
| `null` | `map_2026-04-30T14-29-07.png` | `` (leer) |

Konsequenz für die UI: das Detail-Panel rechts zeigt für die Karte die **volle** Location-Zeile in Akzentfarbe, genau wie für ein normales Foto desselben Ortes — niemand sieht jemals den bereinigten String.

Konsequenz für die Konsistenz: wenn alle Fotos eines Ortes die identische Location-Zeichenkette tragen (und nach dem Geocoding ist das die Regel), trägt die zugehörige Karte denselben String, und die UI-Darstellung „diese Karte gehört zu Tarvis" ist visuell sofort klar.

### Update-Logik

Beim Generieren der Karten gilt: **bestehende Einträge mit demselben Filename werden überschrieben, andere bleiben erhalten**. Konkret:

1. Für jeden tatsächlich erzeugten Stopp (nach Skip-Regel):
   - PNG-Datei schreiben.
   - `File.SetLastWriteTime`/`SetCreationTime` auf den berechneten Timestamp setzen.
   - In der in-Memory-Foto-Liste prüfen, ob bereits ein `Photo` mit diesem Filename existiert:
     - **Ja**: bestehende Felder aktualisieren (DateTime, Lat, Lon, Location), Title/Description/State unverändert lassen (der Nutzer könnte sie editiert haben).
     - **Nein**: neuen `Photo`-Eintrag anlegen mit allen Werten wie oben.
2. Nach Abschluss aller Karten: `tour.csv` per `TourCsvWriter` neu schreiben.

Da die WPF-UI eine ObservableCollection von `PhotoViewModel`s führt, müssen neue Karten dort eingefügt werden. Cleanster Weg: nach dem Karten-Generator-Lauf einen Re-Scan triggern, der die neue/aktualisierte CSV einliest und die Galerie spiegelt.

### Schnittstellen-Änderung im `IMapRenderer`

Der Renderer braucht jetzt Schreibzugriff auf die CSV (oder zumindest die in-Memory-Foto-Liste muss ihm gegeben werden, damit er `Photo`-Einträge zurückgeben kann). Saubere Variante:

```csharp
public interface IMapRenderer
{
    Task<MapRenderResult> RenderAllAsync(
        IReadOnlyList<Photo> allPhotosWithGps,
        string outputFolder,
        IProgress<MapRenderProgress>? progress = null,
        CancellationToken ct = default);
}

public sealed record MapRenderResult(
    int RenderedCount,
    IReadOnlyList<Photo> MapPhotos     // Photo-Einträge für die erzeugten Karten
);
```

`MapPhotos` enthält für jede tatsächlich erzeugte Karte einen fertigen `Photo`-Datensatz mit allen Feldern aus der Tabelle oben. Der Aufrufer (`MainViewModel.GenerateMapsAsync`) verschmilzt diese Liste mit der bestehenden Foto-Liste:

```csharp
var result = await renderer.RenderAllAsync(allWithGps, outputFolder, progress, ct);

// In-Memory-Liste aktualisieren
foreach (var mapPhoto in result.MapPhotos)
{
    var existing = Photos.FirstOrDefault(p =>
        string.Equals(p.UnderlyingPhoto.Filename, mapPhoto.Filename, StringComparison.OrdinalIgnoreCase));

    if (existing is not null)
    {
        existing.UnderlyingPhoto.DateTime = mapPhoto.DateTime;
        existing.UnderlyingPhoto.Latitude = mapPhoto.Latitude;
        existing.UnderlyingPhoto.Longitude = mapPhoto.Longitude;
        existing.UnderlyingPhoto.Location = mapPhoto.Location;
        // State/Title/Description nicht überschreiben
    }
    else
    {
        Photos.Add(new PhotoViewModel(mapPhoto, /* ... */));
    }
}

// Auto-Save (Schritt 13) feuert von selbst, sobald sich Photo-Properties ändern
// — bei Add explizit triggern, falls keine Property-Änderung erfolgt:
RequestAutoSave();
```

---

## Änderung 4 — Scanner: PNG-Maps als reguläre Fotos

### `PhotoFolderScanner` erweitern

Die separate Erkennung von `MapItem`s aus Schritt 7 entfällt. Stattdessen:

- Such-Pattern um `*.png` mit Filename-Match `map_*.png` erweitert.
- Diese PNGs werden wie JPGs behandelt: in die `Photo`-Liste aufgenommen.
- Metadaten kommen aus der CSV (falls vorhanden) oder aus dem File-Timestamp (`File.GetLastWriteTime`) als Fallback für DateTime; GPS und Location aus CSV.
- Wenn keine CSV-Zeile existiert (z.B. Karte aus alter Version): neuer Eintrag mit `DateTime = File.LastWriteTime`, `State = None`, `Location = null`. Der Nutzer kann es nachträglich fixen oder die Karte per neuem Render-Lauf regenerieren.

### `ExifReaderService` toleriert PNGs

PNGs haben kein EXIF im klassischen Sinn. Service muss bei `.png`-Endung:

- `DateTime`, `Latitude`, `Longitude`, `Altitude` auf `null` setzen.
- `FileSizeBytes` aus `FileInfo.Length` setzen.
- `PixelWidth`/`PixelHeight` über die PNG-Header lesen (mit MetadataExtractor → `PngDirectory.TagImageWidth`/`TagImageHeight`).
- Keine Exception werfen.

### `ScanResult` schrumpft

`ScanResult` aus Schritt 7 wird vereinfacht — die `Maps`-Liste entfällt:

```csharp
public sealed record ScanResult(
    IReadOnlyList<Photo> Photos,
    IReadOnlyList<HeicItem> HeicCandidates,    // bleibt aus Schritt 11
    IReadOnlyList<string> NewFilenames,
    IReadOnlyList<string> MissingFilenames
);
```

### `MapItem` und `MapItemViewModel` entfernen

`TravelJournal.Core/Models/MapItem.cs` und `TravelJournal.Wpf/ViewModels/MapItemViewModel.cs` werden gelöscht. Alle Referenzen darauf (insbesondere im `IGalleryItem`-Pattern aus Schritt 7) werden bereinigt:

- `IGalleryItem` kann komplett verschwinden — die Galerie zeigt nur noch `PhotoViewModel`s.
- Der `DataTemplateSelector` in `MainWindow.xaml` zeigt nur noch das Foto-Template.
- `PhotoFilter.Maps` wird aus dem Enum entfernt; falls Karten gezielt sichtbar sein sollen, kann ein zusätzlicher Filter „Karten" über Filename-Pattern (`StartsWith("map_") && EndsWith(".png")`) ergänzt werden — aber als optionaler Filter, kein eigener Item-Typ.

### Optional: visuelle Markierung von Karten in der Galerie

Damit man Karten in der Liste schnell als solche erkennt, kann das Thumbnail-Template die Endung prüfen:

- PNG mit `map_`-Präfix → kleines Karten-Symbol (z.B. „🗺" oder ein dezentes „MAP"-Badge in Akzentfarbe) in der Ecke des Thumbnails.
- Sonst kein Badge.

---

## Änderung 5 — Bearbeitbarkeit von Karten

Da Karten jetzt `Photo`-Einträge sind, gelten automatisch:

- **Title** und **Description** lassen sich im Detail-Bereich rechts editieren.
- **State**-Toggle (1/2/0) funktioniert (typisch bleibt es bei `Selected = 1`, aber der Nutzer kann eine Karte abwählen, falls sie nicht in die Diashow soll).
- **Location** wird gesetzt und kann nachträglich geändert werden.
- **Auto-Save** (Schritt 13) greift bei jeder dieser Änderungen.
- **Reverse-Geocoding** (Schritt 7) wird übersprungen, falls `Location` bereits gesetzt ist (was beim Karten-Generieren immer der Fall ist).

Aktionen, die für Karten **keinen Sinn machen** und entsprechend No-Ops sein sollten:

- **Bild-Rotation** (Schritt 4) — Karten sind bereits korrekt orientiert. Implementierung: in `RotateLeft`/`RotateRight`-Commands prüfen, ob die Datei mit `map_` beginnt und `.png` endet; falls ja, Status-Hinweis „Karten werden nicht rotiert" und kein State-Change.
- **HEIC-Konvertierung** — irrelevant für PNGs.

---

## Tests

Anpassungen in `TravelJournal.Core.Tests/MapRendering/TileMapRendererTests.cs`:

- Karte für Stopp in „Tarvis" mit erstem Tarvis-Foto um `2026-04-27T12:22:57` → Karten-Timestamp ist `2026-04-27T12:22:56`.
- Karten-Filename enthält den Ort: `map_2026-04-27T12-22-56_Tarvis.png`.
- `MapRenderResult.MapPhotos` enthält für jede erzeugte Karte einen `Photo` mit `State == Selected`, `Location` gesetzt (**voller Originalstring**, nicht die `FilenameSafeName`-Variante), Lat/Lon vom Stopp, `Altitude == null`.
- Test-Beispiel: ein Stopp mit `stop.Location == "Chiusaforte / Scluse"` ergibt einen `Photo` mit `Filename == "map_<timestamp>_ChiusaforteScluse.png"` **und** `Location == "Chiusaforte / Scluse"` (mit Leerzeichen und Slash).
- Zweimal hintereinander rendern: Filenames identisch, in-Memory-Photo-Liste hat keine Duplikate, die zweite Ausführung überschreibt die existierenden Einträge.
- Karte für Stopp ohne `Location`: Timestamp = `stop.Timestamp` (Fallback), Filename ohne `_<Ort>`.

In `TravelJournal.Core.Tests/Services/PhotoFolderScannerTests.cs`:

- Ordner mit JPG + PNG mit `map_`-Präfix → Scanner liefert beide in `Photos`, kein separater `Maps`-Bucket mehr.
- PNG ohne CSV-Eintrag bekommt `DateTime` aus `LastWriteTime`, `State == None`.
- PNG mit CSV-Eintrag bekommt alle Daten aus der CSV.

In `TravelJournal.Core.Tests/Services/ExifReaderServiceTests.cs`:

- PNG-Datei wird ohne Exception gelesen, `DateTime` ist `null`, `PixelWidth`/`PixelHeight` aus PNG-Header korrekt.

---

## Akzeptanzkriterien

- `dotnet build` warningfrei, alle Tests grün.
- Karten im Foto-Ordner heißen `map_<timestamp>_<Ort>.png` (mit Ort) bzw. `map_<timestamp>.png` (ohne Ort).
- Eine Karte für Ort „Tarvis" hat einen Timestamp 1 Sekunde vor dem ersten Tarvis-Foto in der Tour. Wenn die Galerie nach `DateTime` sortiert ist, erscheint die Karte unmittelbar vor diesem Foto.
- `tour.csv` enthält für jede generierte Karte eine Zeile mit `State=1`, gesetzten Koordinaten, leerer `Altitude` und der `Location`-Spalte mit dem **vollen Originaltext** des Anker-Foto-Ortes (z.B. `Chiusaforte / Scluse`, nicht `ChiusaforteScluse`).
- Im WPF-Tool erscheint die Karte als regulärer Galerie-Eintrag, links in der Liste mit dezentem Karten-Badge, rechts mit großem Bild und editierbaren Title/Description.
- Tippen in das Title- oder Description-Feld einer Karte triggert den Auto-Save aus Schritt 13.
- Filter „Karten" (falls beibehalten) zeigt alle PNG-Einträge mit `map_`-Präfix.
- Tasten `L`/`R` auf einer Karte erzeugen keine Rotation, sondern einen Hinweis in der Statuszeile.
- Skip-Regel aus Schritt 13 funktioniert weiterhin: aufeinanderfolgende Stopps mit gleichem `FilenameSafeName.FromLocation`-Output ergeben nur eine Karte.
- Wiederholtes Generieren ist idempotent: bestehende Karten-PNGs werden überschrieben, CSV-Zeilen aktualisiert (DateTime/Lat/Lon/Location), aber Title/Description/State bleiben erhalten.

---

## Migration / Aufräumen

Folgendes ist im Code zu entfernen oder umzubauen:

| Aus Schritt | Was wird entfernt / geändert |
|---|---|
| Schritt 6 | `IMapRenderer.RenderAllAsync` Rückgabewert: war `Task<int>`, wird `Task<MapRenderResult>`. Aufrufer ziehen mit. |
| Schritt 6 | StopPoint-Timestamp-Berechnung in `StopDetector` → bleibt als Default, wird aber im Renderer durch `ComputeMapTimestamp` überschrieben. |
| Schritt 7 | `MapItem`-Modell, `MapItemViewModel`, `Maps`-ObservableCollection, separater Map-`DataTemplate`, `IGalleryItem`-Interface (alle entfallen — Karten sind Photos) |
| Schritt 7 | `ScanResult.Maps` aus dem Record entfernen, alle Aufrufer anpassen |
| Schritt 7 | `PhotoFilter.Maps` Enum-Wert: optional behalten als Filename-basierter Filter, oder entfernen |
| Schritt 13 | „Skip wenn Filename gleich wie vorher" — bleibt; aber zusätzlich vom neuen Bug-Fix unterstützt, dass die Filenames jetzt überhaupt unterschiedlich sind |

`HeicItem` (Schritt 11) und der dortige Workflow bleiben unverändert — HEIC-Dateien sind weiterhin ein eigener vorgelagerter Schritt vor der Konvertierung in JPG.

---

## Was bewusst NICHT teil dieser Iteration ist

- Karten regenerieren auf Knopfdruck einzeln pro Foto (Bulk-Regen läuft weiterhin über „Karten generieren")
- Manuelles Hinzufügen einer Karte ohne Stopp (z.B. „hier ist der Höhepunkt der Tour, ich hätte gern eine Karte dazu")
- Karten-Vorschau im Detail-Bereich mit interaktivem Zoom
- Veränderte Foto-Karten-Verhältnisse je Tag (Tages-Übersichtskarten)
- Drag&Drop zur Reposition einer Karte in der Liste (ergibt sich automatisch durch DateTime-Sortierung — wer eine Karte verschieben will, ändert deren `DateTime`-Feld in der CSV manuell oder per UI)
- Eigene Bearbeitungs-Aktionen wie „Karte neu rendern mit anderem Style" pro Karte
