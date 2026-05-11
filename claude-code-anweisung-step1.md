# Claude Code Anweisung — TravelJournal Build-Tool Skeleton

## Ziel

Erzeuge das Solution-Skeleton für ein .NET-Build-Tool, das aus einem Ordner getaggter Fotos eine `tour.csv` generiert und wieder einliest. Diese erste Iteration enthält **nur die Klassenbibliothek `TravelJournal.Core` plus Tests**. Die WPF-UI folgt in einem späteren Schritt — bitte noch nicht anlegen.

## Kontext

Das Tool ist Teil eines größeren Projekts: aus den Fotos einer 6-tägigen Radreise (mit EXIF-GPS-Tags) soll später eine Web-TravelJournal mit Routenverlauf entstehen. Dieser Build-Tool-Teil ist verantwortlich für das Einlesen der Fotos, das Erfassen der Auswahlentscheidung des Nutzers und das Schreiben einer hand-editierbaren `tour.csv` als Vertrag zur Web-Schicht.

## Tech-Stack

- **.NET 8** (LTS)
- **C# 12**, file-scoped namespaces, `Nullable` enabled, `ImplicitUsings` enabled
- **NuGet-Pakete:**
  - `MetadataExtractor` (EXIF/GPS auslesen)
  - `CsvHelper` (CSV lesen und schreiben)
  - **Tests:** `xUnit`, `FluentAssertions`, `Microsoft.NET.Test.Sdk`

## Solution-Struktur

```
TravelJournal.sln
├── src/
│   └── TravelJournal.Core/
│       ├── TravelJournal.Core.csproj
│       ├── Models/
│       │   ├── Photo.cs
│       │   └── PhotoState.cs
│       └── Services/
│           ├── ExifReaderService.cs
│           ├── TourCsvReader.cs
│           ├── TourCsvWriter.cs
│           └── PhotoFolderScanner.cs
└── tests/
    └── TravelJournal.Core.Tests/
        ├── TravelJournal.Core.Tests.csproj
        ├── ExifReaderServiceTests.cs
        ├── TourCsvWriterTests.cs
        ├── TourCsvReaderTests.cs
        ├── PhotoFolderScannerTests.cs
        └── TestData/
            └── (leer, Tests legen Fixtures programmatisch an)
```

## Datenmodell

### `PhotoState` (enum, int-basiert)

```csharp
public enum PhotoState
{
    None = 0,
    Selected = 1,
    Deselected = 2
}
```

Die Integer-Werte sind Teil des CSV-Vertrags und müssen stabil bleiben.

### `Photo` (Klasse, mutabel — wird später von der UI gebunden)

Eigenschaften:

- `string Filename` (nur Dateiname, kein Pfad)
- `DateTime? DateTime` (lokale Zeit aus EXIF; nullable für Fotos ohne EXIF-Datum)
- `double? Latitude`
- `double? Longitude`
- `double? Altitude`
- `PhotoState State` (Default `None`)
- `string? Title`
- `string? Description`

Keine `INotifyPropertyChanged`-Implementierung in `TravelJournal.Core` — das passiert später in der UI-Schicht über ein ViewModel-Wrapper.

## CSV-Schema

- Trennzeichen: **Semikolon** (`;`)
- Dezimaltrenner: **Punkt** (`.`)
- DateTime-Format: **ISO 8601 ohne Zeitzone** (`yyyy-MM-ddTHH:mm:ss`)
- Encoding: **UTF-8 mit BOM** (damit Excel Umlaute korrekt anzeigt)
- Zeilenende: `\r\n`
- Leere Felder bleiben leer (nicht `"null"`, nicht `""` mit Anführungszeichen, sondern wirklich leer)
- Header in der ersten Zeile

Spalten in dieser Reihenfolge:

```
Filename;DateTime;Latitude;Longitude;Altitude;State;Title;Description
```

Beispiel:

```
Filename;DateTime;Latitude;Longitude;Altitude;State;Title;Description
DSC_0123.jpg;2026-04-12T09:14:33;47.376541;8.541234;412;1;Start in Zürich;Früh los bei Sonnenaufgang
DSC_0145.jpg;2026-04-12T11:02:18;47.412112;8.602441;520;0;;
DSC_0156.jpg;2026-04-12T11:23:01;47.413421;8.621112;525;2;;Verwackelt – aussortiert
DSC_0163.jpg;2026-04-12T12:05:44;47.421005;8.640221;510;1;Kaffeepause;Kleine Bäckerei am See
```

Konventioneller Dateiname für die CSV: **`tour.csv`**, abgelegt im gleichen Ordner wie die Fotos.

## Services

### `ExifReaderService`

```csharp
public class ExifReaderService
{
    public Photo ReadMetadata(string filePath);
}
```

- Liest mit `MetadataExtractor` die EXIF-Daten und befüllt ein neues `Photo`-Objekt.
- `Filename` = `Path.GetFileName(filePath)`.
- `DateTime` aus `ExifSubIfdDirectory.TagDateTimeOriginal`, fallback `ExifIfd0Directory.TagDateTime`. Bei Fehlen → `null`.
- `Latitude`/`Longitude` aus `GpsDirectory.GetGeoLocation()`. Bei Fehlen → `null`.
- `Altitude` aus `GpsDirectory.TagAltitude`. Bei Fehlen → `null`.
- `State` initial `None`, `Title` und `Description` initial `null`.
- Wirft keine Exception bei fehlenden EXIF-Tags — gibt einfach ein Photo mit `null`-Feldern zurück.
- Wirft `FileNotFoundException`, wenn die Datei nicht existiert.

### `TourCsvWriter`

```csharp
public class TourCsvWriter
{
    public void Write(string csvPath, IEnumerable<Photo> photos);
}
```

- Schreibt alle übergebenen Fotos in eine CSV gemäß Schema oben.
- Sortiert nach `DateTime` aufsteigend, Fotos ohne `DateTime` ans Ende.
- Überschreibt eine bestehende Datei.

### `TourCsvReader`

```csharp
public class TourCsvReader
{
    public List<Photo> Read(string csvPath);
}
```

- Liest die CSV ein und gibt eine Liste von `Photo`-Objekten zurück.
- Tolerant: leere `State`-Spalte → `PhotoState.None`. Unbekannte Integer-Werte → `PhotoState.None`.
- Leere `DateTime`/`Latitude`/`Longitude`/`Altitude` → `null` im Modell.
- Wirft `FileNotFoundException`, wenn die Datei nicht existiert.

### `PhotoFolderScanner`

```csharp
public class PhotoFolderScanner
{
    public PhotoFolderScanner(ExifReaderService exifReader, TourCsvReader csvReader);

    public ScanResult Scan(string folderPath);
}

public record ScanResult(
    List<Photo> Photos,
    List<string> NewFilenames,
    List<string> MissingFilenames
);
```

Verhalten:

1. Findet alle `*.jpg` und `*.jpeg` (case-insensitive) im `folderPath`, nicht rekursiv.
2. Falls `tour.csv` im selben Ordner existiert: einlesen.
3. Mergt per Dateiname (case-insensitive):
   - **In Ordner und CSV** → CSV-Eintrag verwenden. Falls Felder in der CSV leer sind, Werte aus EXIF nachladen (so überleben hand-editierte CSV-Werte unverändert).
   - **Nur im Ordner** → neuer `Photo`-Eintrag aus EXIF, `State = None`. Dateiname kommt zusätzlich in `NewFilenames`.
   - **Nur in CSV** → Eintrag wird trotzdem in `Photos` aufgenommen (damit nichts verloren geht), Dateiname kommt zusätzlich in `MissingFilenames`.
4. Sortierung der zurückgegebenen Liste: nach `DateTime` aufsteigend, fehlende Daten ans Ende.

## Unit-Tests

Tests verwenden xUnit + FluentAssertions. Test-Fixtures werden programmatisch im Test-Setup angelegt (temporäre Verzeichnisse, kleine in-Code generierte JPG-Dateien sind nicht nötig — fokussiere die Tests auf CSV und Scanner-Logik; `ExifReaderServiceTests` darf einen einfachen Smoke-Test mit einem im Code generierten 1×1-JPG ohne EXIF haben, der prüft, dass die Methode kein Throw macht und `Filename` korrekt gesetzt ist).

### `TourCsvWriterTests` — Pflicht-Cases

- Schreibt korrekten Header.
- Schreibt mehrere Photos in der korrekten Spaltenreihenfolge.
- Sortiert nach `DateTime` aufsteigend.
- `null`-Felder werden als leere Spalten geschrieben (kein `"null"`).
- Umlaute in Title/Description bleiben korrekt erhalten (UTF-8 mit BOM).
- State-Werte werden als Integer (`0`/`1`/`2`) geschrieben.

### `TourCsvReaderTests` — Pflicht-Cases

- Liest die vom Writer geschriebene Datei und gibt äquivalente Photos zurück (Roundtrip-Test).
- Leere `State`-Spalte wird zu `PhotoState.None`.
- Unbekannter `State`-Wert (z.B. `99`) wird zu `PhotoState.None`.
- Leere Koordinaten/Datum werden zu `null`.
- `FileNotFoundException` bei nicht existierender Datei.

### `PhotoFolderScannerTests` — Pflicht-Cases

- Leerer Ordner → leere `Photos`-Liste, keine Errors.
- Ordner mit Fotos ohne CSV → alle Fotos haben `State = None`, alle Dateinamen in `NewFilenames`.
- Ordner mit Fotos und passender CSV → Status/Title/Description werden aus der CSV übernommen.
- Neues Foto im Ordner, das nicht in der CSV steht → erscheint in `Photos` mit `State = None` und in `NewFilenames`.
- Eintrag in CSV, dessen Datei fehlt → erscheint in `Photos` und in `MissingFilenames`.
- Hand-editierter Title in der CSV überlebt einen Re-Scan unverändert.

(Für Scanner-Tests, die echte JPG-Dateien brauchen, kannst du leere oder minimale Dateien mit der Endung `.jpg` anlegen — der Scanner muss tolerieren, dass `ExifReaderService` `null`-Felder zurückgibt.)

## Coding-Konventionen

- File-scoped namespaces (`namespace TravelJournal.Core.Models;`)
- `var` wo der Typ aus dem Kontext klar ist, sonst expliziter Typ
- Keine statischen Service-Klassen — alle Services sind instanziierbar (auch wenn aktuell zustandslos), damit DI später einfach ist
- XML-Doc-Kommentare auf öffentlichen Methoden der Services (1–3 Zeilen, deutsch oder englisch konsistent)
- Keine `Console.WriteLine`-Aufrufe in `TravelJournal.Core`

## Akzeptanzkriterien

- `dotnet build` läuft ohne Warnings durch.
- `dotnet test` läuft grün.
- Die Solution lässt sich in Visual Studio 2022 und Rider öffnen.
- `tour.csv` lässt sich in deutschem Excel doppelklicken und wird korrekt mit Spalten dargestellt.

## Was bewusst NICHT teil dieser Iteration ist

- WPF-UI (kommt im nächsten Schritt)
- Thumbnail-Generierung
- Bildoptimierung für die Web-Schicht
- Web-Präsentation
- Day/Order-Spalten in der CSV (werden später ergänzt, falls nötig)
- Logging-Framework (Serilog o.ä.)
- Dependency-Injection-Container (Services werden im Test direkt instanziiert)
