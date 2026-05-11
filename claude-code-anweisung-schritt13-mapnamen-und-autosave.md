# Claude Code Anweisung — Schritt 13: Karten-Namen mit Ort + Auto-Save der CSV

## Ziel

Zwei voneinander unabhängige Verbesserungen, die den Workflow runder machen:

1. **Karten tragen den Ortsnamen im Dateinamen.** Beim Generieren einer Karte wird der Ort des Stopp-Fotos (Photo.Location) in den Filename übernommen. Hat eine Karte denselben Ortsnamen wie ihre Vorgängerin, wird sie übersprungen — sonst gäbe es zwei aufeinanderfolgende Karten am selben Ort, die sich kaum unterscheiden.
2. **Auto-Save der `tour.csv`.** Jede für die CSV relevante Änderung (State, Title, Description, Location) löst eine asynchrone, debounced Speicherung aus. Der manuelle Speicher-Befehl und der Toolbar-Button „CSV speichern" entfallen.

## Kontext

Setzt auf Schritt 1–12 auf. Hauptbetroffene Dateien:

- `TravelJournal.Core/MapRendering/Models/StopPoint.cs` — Erweiterung um Location.
- `TravelJournal.Core/MapRendering/StopDetector.cs` — befüllt Location.
- `TravelJournal.Core/MapRendering/TileMapRenderer.cs` — neue Naming-Logik plus Skip-Regel.
- `TravelJournal.Wpf/ViewModels/MainViewModel.cs` und `PhotoViewModel.cs` — Auto-Save-Mechanik.
- `TravelJournal.Wpf/Views/MainWindow.xaml` — „CSV speichern"-Button entfernt; Auto-Save-Statusanzeige in der Statuszeile.

---

## Änderung 1 — Karten-Dateinamen mit Ort

### Schema

```
map_YYYY-MM-DDTHH-MM-SS_<Ort>.png      (mit Ort)
map_YYYY-MM-DDTHH-MM-SS.png            (ohne Ort)
```

`<Ort>` wird via `FilenameSafeName.FromLocation(...)` aus Schritt 12 erzeugt — ASCII-only, keine Sonderzeichen, PascalCase-artige Zusammenschreibung.

Beispiele aus der vorhandenen Tour:

| Stopp-Zeitpunkt | Photo.Location | Karten-Filename |
|---|---|---|
| 2026-04-26T17:23:55 | `Finkenstein am Faaker See` | `map_2026-04-26T17-23-55_FinkensteinAmFaakerSee.png` |
| 2026-04-28T10:38:55 | `Chiusaforte / Scluse` | `map_2026-04-28T10-38-55_ChiusaforteScluse.png` |
| 2026-04-30T15:00:00 | `null` | `map_2026-04-30T15-00-00.png` |

### `StopPoint` um `Location` erweitern

```csharp
public sealed record StopPoint(
    DateTime Timestamp,
    double Latitude,
    double Longitude,
    int PhotoIndex,
    string? Location          // NEU — Ortsname des Stopp-Anchor-Fotos
);
```

### `StopDetector` befüllt Location

Im `DetectStops`-Aufruf greift der Detektor jeweils auf `photos[i].Location` zu und reicht es in den `StopPoint` durch:

```csharp
result.Add(new StopPoint(
    Timestamp:  photos[i].DateTime!.Value.AddSeconds(1),
    Latitude:   photos[i].Latitude!.Value,
    Longitude:  photos[i].Longitude!.Value,
    PhotoIndex: i,
    Location:   photos[i].Location));
```

Analog für die Final-Summary aus dem letzten Foto.

### Skip-Regel im `TileMapRenderer`

Nach `StopDetector.DetectStops(...)` ein Deduplikations-Schritt **vor** dem Rendern:

```csharp
private static IReadOnlyList<StopPoint> DeduplicateConsecutiveLocations(
    IReadOnlyList<StopPoint> stops)
{
    var result = new List<StopPoint>();
    string? prevSafe = null;
    foreach (var stop in stops)
    {
        var safe = FilenameSafeName.FromLocation(stop.Location);
        // Aufeinanderfolgende identische Ortsnamen → der spätere Stopp wird übersprungen
        if (result.Count > 0 && string.Equals(safe, prevSafe, StringComparison.Ordinal))
            continue;
        result.Add(stop);
        prevSafe = safe;
    }
    return result;
}
```

Die Dedupe-Regel gilt für **alle** aufeinanderfolgenden Stopps, nicht nur für den Final-Summary. Beispiele:

- Zwei Stopps an „Chiusaforte / Scluse" hintereinander → nur der erste bekommt eine Karte.
- Final-Summary an „Grado / Grau", direkt davor Stopp ebenfalls in „Grado / Grau" → Final-Summary wird übersprungen (typischer Fall, wenn das letzte Foto am selben Übernachtungsort wie der letzte Tagespause-Stopp aufgenommen wurde).
- Zwei aufeinanderfolgende Stopps ohne Location (`null`) → ebenfalls dedupliziert (beide haben `safe == ""`).

### Filename-Konstruktion im Renderer

```csharp
var safeOrt = FilenameSafeName.FromLocation(stop.Location);
var basename = string.IsNullOrEmpty(safeOrt)
    ? $"map_{stop.Timestamp:yyyy-MM-ddTHH-mm-ss}"
    : $"map_{stop.Timestamp:yyyy-MM-ddTHH-mm-ss}_{safeOrt}";
var path = Path.Combine(outputFolder, $"{basename}.png");
```

Falls die Datei bereits existiert (z.B. weil der Generator bereits einmal lief und die CSV unverändert ist), wird sie überschrieben — gleiches Verhalten wie bisher.

### Tests anpassen

In `TravelJournal.Core.Tests/MapRendering/`:

- `StopDetectorTests`: `Location`-Property wird korrekt aus dem Photo durchgereicht.
- `TileMapRendererTests`: ein neuer Case mit drei Stopps, davon zwei aufeinanderfolgende mit identischem Location-String → es werden nur zwei Karten erzeugt, nicht drei. Filenames enthalten den `_<Ort>`-Suffix.
- `TileMapRendererTests`: ein Case mit Final-Summary, deren Location identisch zum vorherigen Stopp ist → Final-Summary wird übersprungen. Akzeptanzkriterium hierfür auch in der UI nachvollziehbar (eine Karte weniger als erwartet).

### Akzeptanzkriterien Änderung 1

- Karten-Filenames im Foto-Ordner enthalten den Ortsnamen, sofern bekannt: `map_2026-04-28T13-58-43_Tarvis.png`.
- Bei aufeinanderfolgenden Stopps am selben Ort (gleicher `FilenameSafeName.FromLocation`-Output) entsteht nur eine Karte; die zweite wird stillschweigend übersprungen.
- Wenn Final-Summary aktiviert ist und das letzte Foto am selben Ort wie der vorherige Stopp aufgenommen wurde, wird die Summary-Karte nicht erzeugt.
- Karten ohne bekannten Ort haben weiterhin den reinen Timestamp-Filename.

---

## Änderung 2 — Auto-Save der CSV bei jeder relevanten Änderung

### Konzept

Jede Änderung an einem CSV-relevanten Feld (`State`, `Title`, `Description`, `Location`) eines `PhotoViewModel` löst eine **debounced** Speicherung der `tour.csv` aus:

- `RequestAutoSave()` startet einen `DispatcherTimer` von 1 Sekunde neu.
- Während der Sekunde weitere Änderungen → Timer wird zurückgesetzt (typisches Schreibverhalten in TextBoxen wird so gebündelt).
- Bei Timer-Tick: asynchroner CSV-Write im Hintergrund über `TourCsvWriter`.
- Statuszeile zeigt kurz „Gespeichert · 14:23:11" als Bestätigung.

### Implementation in `PhotoViewModel`

`PhotoViewModel` erbt bereits von `ObservableObject` (CommunityToolkit.Mvvm). Wir ergänzen kein extra Event — `MainViewModel` hängt sich an das vorhandene `PropertyChanged` jedes `PhotoViewModel` und filtert auf die vier CSV-relevanten Properties.

### Implementation in `MainViewModel`

Neue Felder und Methoden:

```csharp
private readonly DispatcherTimer _autoSaveTimer;
private readonly SemaphoreSlim _saveLock = new(1, 1);
private DateTime? _lastAutoSavedAt;

public MainViewModel(/* … */)
{
    // … bestehende Initialisierung …
    _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
    _autoSaveTimer.Tick += async (_, _) =>
    {
        _autoSaveTimer.Stop();
        await DoAutoSaveAsync();
    };
}

private static readonly HashSet<string> CsvRelevantProperties = new(StringComparer.Ordinal)
{
    nameof(PhotoViewModel.State),
    nameof(PhotoViewModel.Title),
    nameof(PhotoViewModel.Description),
    nameof(PhotoViewModel.Location),
};

private void OnPhotoPropertyChanged(object? sender, PropertyChangedEventArgs e)
{
    if (e.PropertyName != null && CsvRelevantProperties.Contains(e.PropertyName))
        RequestAutoSave();
}

private void RequestAutoSave()
{
    if (string.IsNullOrEmpty(CurrentFolder)) return;
    _autoSaveTimer.Stop();
    _autoSaveTimer.Start();
}

private async Task DoAutoSaveAsync()
{
    if (string.IsNullOrEmpty(CurrentFolder)) return;
    if (!await _saveLock.WaitAsync(0)) return;   // läuft schon — neuer Tick verworfen
    try
    {
        var csvPath = Path.Combine(CurrentFolder, "tour.csv");
        var photos = Photos.Select(vm => vm.UnderlyingPhoto).ToList();
        await Task.Run(() => _csvWriter.Write(csvPath, photos));
        _lastAutoSavedAt = DateTime.Now;
        BackgroundActivityText = $"Gespeichert · {_lastAutoSavedAt:HH:mm:ss}";
    }
    catch (Exception ex)
    {
        BackgroundActivityText = $"Speichern fehlgeschlagen: {ex.Message}";
    }
    finally
    {
        _saveLock.Release();
    }
}
```

### Subscriben/Unsubscriben in der Scan-Logik

In dem Pfad, in dem `Photos` neu befüllt oder ergänzt wird (`HandleScanResult` oder vergleichbarer Ort):

```csharp
foreach (var oldVm in _subscribedPhotos)
    oldVm.PropertyChanged -= OnPhotoPropertyChanged;
_subscribedPhotos.Clear();

foreach (var vm in Photos)
{
    vm.PropertyChanged += OnPhotoPropertyChanged;
    _subscribedPhotos.Add(vm);
}
```

`_subscribedPhotos` ist eine private `List<PhotoViewModel>`, damit die Unsubscribe-Schleife nicht versehentlich auf die Live-Collection läuft.

### Sicheres Schließen des Fensters

In `MainWindow.xaml.cs` `Window_Closing`-Handler ergänzen:

```csharp
private async void Window_Closing(object sender, CancelEventArgs e)
{
    if (DataContext is MainViewModel vm && vm.HasPendingAutoSave)
    {
        e.Cancel = true;
        await vm.FlushAutoSaveAsync();
        Close();
    }
}
```

`HasPendingAutoSave` ist `true`, wenn `_autoSaveTimer.IsEnabled`. `FlushAutoSaveAsync` führt sofort `DoAutoSaveAsync` aus.

### Statusanzeige

In der Statuszeile wird `BackgroundActivityText` (aus Schritt 7) wiederverwendet. Nach erfolgreichem Speichern erscheint dort kurz `"Gespeichert · 14:23:11"`. Die Anzeige bleibt bis zur nächsten Hintergrund-Aktivität (Geocoding, Ortsermittlung, weiteres Speichern).

Die normale `StatusText`-Zeile zählt weiterhin Fotos und Zustände — unverändert.

### Was wegfällt

- **Toolbar-Button** „CSV speichern" wird entfernt.
- **Command** `SaveCsvCommand` und Methode `SaveCsvAsync` werden entfernt (nicht mehr aufgerufen).
- **KeyBinding** `Strg+S` wird vom `Window` entfernt (oder optional auf eine manuelle „Sofort-Speichern"-Funktion gemappt — siehe „Was bewusst NICHT teil dieser Iteration ist").

### Andere CSV-schreibende Pfade bleiben unverändert

Diese Pfade haben weiterhin ihre eigene Speicher-Logik, sind nicht Teil der Auto-Save-Mechanik:

- `PhotoRenamer` (Schritt 12) schreibt die CSV als Teil der Rename-Transaktion.
- `HeicConverter` (Schritt 11) ändert keine CSV-Inhalte direkt — nach dem Re-Scan greift Auto-Save bei eventuellen State-Setzungen.

### Tests

Eine schlanke Test-Schicht für `MainViewModel` ist sinnvoll, falls noch keine existiert. Realistisch testbar:

- Property-Änderung an einem `PhotoViewModel.State` löst einen Save innerhalb von ~1.2 s aus (mit `Microsoft.Reactive.Testing` oder einfach `await Task.Delay(1200)` in einem Integration-Test).
- Mehrere Änderungen innerhalb 100 ms ergeben **einen** Save (Debounce-Verifikation).
- Save fehlschlägt → `BackgroundActivityText` enthält den Fehler-Hinweis, In-Memory-Daten bleiben unverändert.
- Window-Closing bei pending Save führt zu finalem Save vor dem Schließen (Lock-File / Timestamp-Vergleich der CSV).

### Akzeptanzkriterien Änderung 2

- Toolbar enthält **keinen** „CSV speichern"-Button mehr.
- Jede Änderung an `State`, `Title`, `Description` oder `Location` eines Fotos führt nach max. ~1.5 Sekunden zu einem aktualisierten `tour.csv` auf der Platte.
- Während mehrere schnelle Änderungen kommen (z.B. zügiges Tippen in der Description), wird nur einmal gespeichert (debounced).
- Statuszeile zeigt nach jedem Speichern kurz `"Gespeichert · HH:MM:SS"`.
- Bei Speicher-Fehler (z.B. Datei in Excel offen) erscheint die Fehlermeldung in der Statuszeile, ohne dass die UI hängt.
- Beim Schließen des Fensters mit ausstehendem Auto-Save wird die Speicherung sauber zu Ende geführt — nichts geht verloren.
- Geocoding (Schritt 7) löst beim Setzen von `Location` ebenfalls Auto-Save aus, sodass die ermittelten Orte ohne weiteres Zutun persistiert sind.

---

## Migration / Was zu entfernen ist

Beim Implementieren dieses Schritts sollten folgende Codestellen aus den vorherigen Schritten entfernt werden:

| Aus Schritt | Was wird entfernt |
|---|---|
| Schritt 2 | `SaveCsvAsync`-Command in `MainViewModel`, „CSV speichern"-Button in `MainWindow.xaml`, ggf. `Strg+S`-`KeyBinding` am Window |
| Schritt 6 (StopPoint) | Wird ergänzt um `Location` (Pflicht-Wechsel — alle Aufrufer mitziehen) |

Die in Schritt 12 dokumentierte Tatsache, dass der Renamer die CSV transaktional mit-aktualisiert, bleibt unverändert — beide Schreibpfade existieren parallel. Nach einem Renamer-Lauf läuft kein Auto-Save für diese Änderung an, da die Filename-Property nicht in `CsvRelevantProperties` enthalten ist (sie wird ja schon vom Renamer selbst geschrieben).

---

## Was bewusst NICHT teil dieser Iteration ist

- **Manueller Sofort-Speichern-Shortcut.** Falls sich später herausstellt, dass eine Sekunde Wartezeit zu lang ist, kann `Strg+S` als „flush jetzt"-Hotkey neu eingeführt werden.
- **Versions-Historie der CSV.** Nur die letzte Version wird gespeichert; Backups gibt es nur beim Renamer (Schritt 12).
- **Konflikt-Erkennung** falls die CSV extern (z.B. in Excel) parallel geändert wurde — das Tool überschreibt seinen In-Memory-Stand.
- **Auto-Save-Pause-Funktion.** Der Auto-Save lässt sich nicht abschalten. Falls jemand die CSV im Texteditor inspizieren will, ohne dass sie sich währenddessen ändert: Toolbar zumachen reicht (keine Property-Änderungen → kein Save).
- **Kompaktierung** der CSV (z.B. abgewählte Fotos auslassen) — die CSV bleibt vollständig, jeder bekannte Filename steht drin.
- **Konfigurierbarkeit der Debounce-Zeit** in der UI (Konstante `1 s` im Code).
