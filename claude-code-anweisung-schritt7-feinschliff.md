# Claude Code Anweisung — Schritt 7: Karten-Ablage, detaillierte Route, asynchrones Geocoding

## Ziel

Drei Verfeinerungen, die die Anwendung deutlich angenehmer in der Bedienung machen:

1. **Karten direkt im Foto-Ordner** ablegen (nicht im `maps/`-Unterordner) und vom WPF-Scanner als eigene Listen-Einträge erfassen, damit die Karten in der Galerie chronologisch zwischen den Fotos erscheinen.
2. **Detailliertere Route** auf den Karten — die Polyline verläuft durch **jedes** ausgewählte Foto mit GPS-Daten, nicht nur durch die Stopp-Punkte. So zeichnet sie die tatsächlich gefahrene Strecke realistisch nach.
3. **Asynchrones Geocoding mit Abbruch-Möglichkeit** — Reverse-Geocoding läuft nur für ausgewählte Fotos, blockiert die UI nicht, der Toolbar-Button schaltet zwischen „Orte ermitteln" und „Abbruch Ortsermittlung" um, und der zuletzt ermittelte Ort wird in der Statuszeile angezeigt.

## Kontext

Setzt auf Schritt 1–6 auf. Die Änderungen modifizieren bestehenden Code an drei Stellen: `TravelJournal.Core/MapRendering/TileMapRenderer`, `TravelJournal.Core/Services/PhotoFolderScanner`, `TravelJournal.Wpf/ViewModels/MainViewModel`. Es kommt ein zusätzlicher Item-Typ `MapItem` hinzu, der parallel zu `Photo` in der Galerie dargestellt wird.

---

## Änderung 1 — Karten im Foto-Ordner, in der Liste sichtbar

### 1a) Output-Pfad anpassen

In `MainViewModel.GenerateMapsAsync` (aus Schritt 6) den Output-Ordner ändern:

```csharp
// alt:
var outputFolder = Path.Combine(CurrentFolder!, "maps");
Directory.CreateDirectory(outputFolder);

// neu:
var outputFolder = CurrentFolder!;   // Karten landen direkt neben den Fotos
```

Der Tile-Cache bleibt im versteckten `<photofolder>/.tile-cache/` — der gehört nicht zu den TravelJournal-Inhalten.

Die in Schritt 6 vergebenen Dateinamen `map_yyyy-MM-ddTHH-mm-ss.png` und das Setzen von `LastWriteTime`/`CreationTime` auf den Stopp-Timestamp bleiben unverändert. Damit liegt jede Karte im Foto-Ordner an der zeitlich korrekten Position.

### 1b) `PhotoFolderScanner` erfasst Karten zusätzlich

Neuer Item-Typ `MapItem` in `TravelJournal.Core/Models/`:

```csharp
public sealed class MapItem
{
    public required string Filename { get; init; }       // z.B. "map_2026-04-12T13-47-22.png"
    public required DateTime DateTime { get; init; }     // aus Filename geparst, fallback File.GetLastWriteTime
    public required string FullPath { get; init; }
}
```

`PhotoFolderScanner.ScanResult` wird erweitert:

```csharp
public sealed record ScanResult(
    IReadOnlyList<Photo> Photos,
    IReadOnlyList<MapItem> Maps,
    IReadOnlyList<string> NewFilenames,
    IReadOnlyList<string> MissingFilenames
);
```

Im `Scan(folderPath)`-Aufruf zusätzlich `*.png` durchsuchen, deren Name dem Muster `map_yyyy-MM-ddTHH-mm-ss.png` entspricht (Regex). Aus dem Filename den Zeitstempel parsen; gelingt das nicht, `File.GetLastWriteTime` als Fallback nutzen. Karten werden **nicht** in `Photos` aufgenommen und **nicht** in die `tour.csv` geschrieben — sie sind reine Anzeige-Items.

Tests in `TravelJournal.Core.Tests`:

- Ordner mit zwei Fotos und einer Karten-PNG → `Maps.Count == 1`, `Photos.Count == 2`.
- Karten-PNG mit valide geparstem Timestamp → `MapItem.DateTime` entspricht dem Filename-Wert.
- PNG ohne `map_`-Präfix → wird ignoriert (nicht in `Maps`).

### 1c) Galerie-Ansicht erweitert

In `MainViewModel` neben `ObservableCollection<PhotoViewModel> Photos` eine zweite Collection:

```csharp
public ObservableCollection<MapItemViewModel> Maps { get; } = new();
```

Dazu eine kombinierte View `GalleryItems`, die beide Collections vereint und nach `DateTime` sortiert:

```csharp
public ICollectionView GalleryItems { get; }   // CompositeCollection oder eigene CollectionView
```

Empfehlung: eine eigene `ObservableCollection<IGalleryItem>` mit Marker-Interface `IGalleryItem`, das `DateTime` und `Filter`-Eigenschaften kennt. `PhotoViewModel` und `MapItemViewModel` implementieren beide `IGalleryItem`. So bleibt das Filter-/Sortier-Handling konsistent.

```csharp
public interface IGalleryItem
{
    DateTime EffectiveDateTime { get; }
    string Filename { get; }
    bool MatchesFilter(PhotoFilter filter);
}
```

`MapItemViewModel.MatchesFilter`: gibt `true` für `PhotoFilter.All` zurück, sonst `false` (Maps tauchen in den State-Filtern „Offen/Selected/Deselected/New" nicht auf — sie haben keinen State).

### 1d) Visuelle Darstellung der Karten in der Liste

Im `ListBox.ItemTemplate` über einen `DataTemplateSelector` zwischen `PhotoTemplate` und `MapTemplate` unterscheiden, je nach Item-Typ.

`MapTemplate` zeigt:

- Thumbnail des Karten-PNGs (kann der `IThumbnailLoader` direkt liefern).
- Filename in einer Zeile (z.B. „Karte 12.04. 13:47").
- Statt State-Innenrahmen einen dezenten **Akzent-Rahmen mit Label „KARTE"** (kleine Badge oben rechts).
- Keine State-Buttons, keine Tristate-Logik.
- Bei Selektion in der ListBox wird der rechte Detail-Bereich auf das Karten-Bild umgeschaltet (siehe 1e).

`MapItemViewModel` braucht eine `Thumbnail`- und eine `LargeImage`-Property analog zu `PhotoViewModel`.

### 1e) Detail-Bereich für Karten

Wenn `SelectedItem` ein `MapItemViewModel` ist:

- Großes Karten-Bild rechts (gleicher `Image`-Slot wie für Fotos).
- Info-Block reduziert: nur Title-Pendant „Karte zu Stopp am 12.04.2026 13:47", Anzahl Fotos im aktuellen Routenausschnitt, Hinweistext, dass diese Karte automatisch generiert wurde.
- Keine Title-/Description-Eingabefelder, keine State-Buttons.

Implementiert über zwei verschiedene `DataTemplate`s im rechten Bereich, gesteuert durch denselben `DataTemplateSelector`.

### 1f) Filter „Karten" als zusätzliche Option

`PhotoFilter` um einen Wert erweitern:

```csharp
public enum PhotoFilter { All, Open, Selected, Deselected, New, Maps }
```

Filter `Maps` zeigt ausschließlich `MapItemViewModel`-Einträge. Toolbar-Filter-Sektion entsprechend ergänzen.

### Akzeptanzkriterien Änderung 1

- Nach „Karten generieren" liegen die PNGs **direkt** im Foto-Ordner, kein `maps/`-Unterordner mehr.
- Im WPF-Tool erscheinen die Karten in der Galerie an der zeitlich passenden Stelle zwischen den Fotos.
- Karten haben ein klar erkennbares „KARTE"-Badge und keine State-Buttons.
- Filter „Karten" zeigt ausschließlich Karten-Einträge, alle anderen Filter ignorieren Karten.
- Karten erscheinen weder in der `tour.csv` noch in der Statuszeile-Zählung „X ausgewählt / Y abgewählt".

---

## Änderung 2 — Route folgt allen GPS-Fotos (nicht nur den ausgewählten)

### Trennung zweier Datenquellen

Für eine genaue Routendarstellung gilt jetzt: **die Polyline und die Bounding-Box berücksichtigen alle Fotos mit GPS-Daten** — auch die offenen und abgewählten. Der `State` filtert nur die *narrative* Auswahl für die TravelJournal, nicht die *technische* Spur, die der Reisende tatsächlich zurückgelegt hat.

Daraus ergibt sich eine saubere Trennung:

| Zweck | Datenquelle |
|---|---|
| Bounding-Box des Karten-Ausschnitts | alle Fotos mit GPS |
| Polyline (gefahrene Route) | alle Fotos mit GPS, in chronologischer Reihenfolge |
| Stopp-Erkennung | nur Fotos mit `State == Selected` und GPS |
| Marker-Position des aktiven Stopps | das ausgewählte Foto am Stopp |
| Vergangene-Stopps-Marker | nur ausgewählte Stopp-Fotos |

So zeichnet eine Karte für „Stopp Nr. 5" eine fein detaillierte Route durch z.B. 200 GPS-Punkte, die Stopps sind aber weiterhin nur die 5 narrativ wichtigen Pausen, die der Nutzer per Auswahl markiert hat.

### Anpassung der `IMapRenderer`-Signatur

Bisher (Schritt 6/7-Entwurf): eine Liste, die als „selected" interpretiert wurde. Neu: die Auswahl wird **nicht** mehr außerhalb gefiltert, sondern alle Fotos werden hineingegeben — der Renderer filtert intern für die jeweilige Verwendung.

```csharp
public interface IMapRenderer
{
    Task<int> RenderAllAsync(
        IReadOnlyList<Photo> allPhotosWithGps,
        string outputFolder,
        IProgress<MapRenderProgress>? progress = null,
        CancellationToken ct = default);
}
```

Im `MainViewModel.GenerateMapsAsync` entsprechend:

```csharp
var allWithGps = Photos
    .Select(p => p.UnderlyingPhoto)
    .Where(p => p.Latitude is not null && p.Longitude is not null)
    .OrderBy(p => p.DateTime)
    .ToList();

await renderer.RenderAllAsync(allWithGps, outputFolder, progress, ct);
```

CanExecute des Buttons wird angepasst auf „mindestens 2 ausgewählte Fotos mit GPS **und** insgesamt mindestens 2 Fotos mit GPS" — wobei die zweite Bedingung in der Praxis durch die erste impliziert ist.

### Anpassung im `TileMapRenderer`

```csharp
public async Task<int> RenderAllAsync(
    IReadOnlyList<Photo> allPhotosWithGps,
    string outputFolder,
    IProgress<MapRenderProgress>? progress,
    CancellationToken ct)
{
    if (allPhotosWithGps.Count < 2) return 0;

    var bounds = MapBounds.FromPhotos(allPhotosWithGps).WithPadding(_options.BoundsPaddingFraction);
    var zoom = WebMercator.CalculateZoom(bounds, _options.TargetWidthPx, _options.TargetHeightPx);
    // … Tiles laden, Basis komponieren …

    var selectedSorted = allPhotosWithGps
        .Where(p => p.State == PhotoState.Selected)
        .ToList();

    var stops = _stopDetector.DetectStops(selectedSorted, _options.StopThreshold, _options.AddFinalSummaryMap);
    if (stops.Count == 0) return 0;

    foreach (var stop in stops)
    {
        ct.ThrowIfCancellationRequested();
        using var map = baseMap.Clone(_ => { });

        // Polyline: alle Fotos mit GPS bis einschließlich des Zeitpunkts dieses Stopps
        var routePoints = allPhotosWithGps
            .Where(p => p.DateTime <= stop.Timestamp)
            .Select(p => projection.ToPixel(p.Latitude!.Value, p.Longitude!.Value))
            .ToArray();

        DrawRoute(map, routePoints);
        DrawPastStopMarkers(map, stops.TakeWhile(s => s != stop), projection);
        DrawCurrentStopMarker(map, stop, projection);

        var path = Path.Combine(outputFolder, $"map_{stop.Timestamp:yyyy-MM-ddTHH-mm-ss}.png");
        await map.SaveAsPngAsync(path, ct);
        File.SetLastWriteTime(path, stop.Timestamp);
        File.SetCreationTime(path, stop.Timestamp);

        progress?.Report(new MapRenderProgress("render", ++renderedCount, stops.Count, null));
    }

    return renderedCount;
}
```

Wichtig: der Filter `p.DateTime <= stop.Timestamp` ist robuster als ein Index-Vergleich, weil die Polyline-Quelle (alle GPS-Fotos) und die Stopp-Quelle (nur ausgewählte) unterschiedlich indexiert sind.

### Punkt-Vereinfachung wird wichtiger

Mit allen Fotos statt nur den ausgewählten kann die Punktzahl pro Karte deutlich höher sein (Faktor 5–10 ist realistisch). Der optionale Vereinfachungs-Filter aus dem Vorschlag rentiert sich nun klar:

```csharp
private static IReadOnlyList<PointF> SimplifyByPixelDistance(
    IReadOnlyList<PointF> points, float minDistancePx = 2.0f);
```

Algorithmus: ersten Punkt übernehmen; jeden weiteren nur, wenn er mindestens `minDistancePx` vom zuletzt übernommenen entfernt ist; letzten Punkt immer übernehmen (damit das Linienende exakt am Stopp endet). In Empfehlungs-Default `2.0f` — bei 1600×1200 entspricht das in unserer typischen Reise-Größenordnung etwa 100–300 m. Reduziert die Punktzahl um typischerweise 60–80 % ohne sichtbaren Verlust.

### Marker-Logik präzisiert

- **Aktiver Stopp**: großer roter Kreis am Stopp-Punkt.
- **Vergangene Stopps**: nur die ausgewählten Stopp-Punkte mit Index vor dem aktuellen, als kleine blaue Kreise.
- **Alle übrigen Foto-Positionen** (alle nicht-ausgewählten oder nicht als Stopp erkannten Fotos): **kein** Marker — sie sind nur Polyline-Knoten. Die Karte bleibt aufgeräumt, die Stopps bleiben die einzigen sichtbaren Wegpunkte.

### Tests anpassen

In `TileMapRendererTests`:

- Synthetische Liste: 12 Fotos mit GPS, davon 4 mit `State == Selected`. Stopps an Index 1 und 3 der ausgewählten Liste. Beim Rendern der Karte für den zweiten Stopp enthält die Polyline alle Fotos bis zu dessen Timestamp — also typischerweise 8–10 Punkte, nicht nur die 2 Stopps und nicht nur die 4 Selected.
- Wenn keine ausgewählten Fotos vorhanden sind, gibt der Renderer `0` zurück und schreibt nichts (selbst wenn viele GPS-Fotos vorliegen).

In `StopDetectorTests`: keine Änderungen nötig — der Detektor arbeitet weiterhin auf der vorgefilterten Selected-Liste, nur die Liste der „candidate stops" wird im Renderer gefiltert übergeben.

### Akzeptanzkriterien Änderung 2

- Auf einer Karte mit 50 GPS-Fotos zwischen zwei ausgewählten Stopps ist die Route fein nachgezeichnet — sichtbar mehr Detail als nur eine gerade Linie zwischen den Stopps.
- Stopps und Stopp-Marker werden weiterhin ausschließlich aus den ausgewählten Fotos abgeleitet.
- Bounding-Box umschließt alle GPS-Fotos, nicht nur die ausgewählten — keine Polyline läuft am Kartenrand entlang oder darüber hinaus.
- Performance bleibt akzeptabel (eine Karte mit 200 Punkten unter 1 s Rendering, mit Vereinfachungs-Filter).

---

## Änderung 3 — Asynchrones Reverse-Geocoding mit Abbruch

### Neues Verhalten

| Aspekt | Bisher (Schritt 3) | Neu (Schritt 7) |
|---|---|---|
| Welche Fotos | alle mit Koordinaten und leerem Location | nur `State == Selected` mit Koordinaten und leerem Location |
| UI-Blockade | `IsBusy = true` (UI gesperrt) | UI bleibt voll bedienbar |
| Button-Text | „Orte ermitteln" (statisch) | toggelt zu „Abbruch Ortsermittlung" während Lauf |
| Statuszeile | „Ortsabfrage 17/42 …" | letzter ermittelter Ort: „Ort ermittelt: Stein am Rhein" |
| Abbruch | nicht möglich | jederzeit per zweitem Button-Klick |

### `MainViewModel`-Erweiterung

Neue Felder und Properties:

```csharp
private CancellationTokenSource? _geocodingCts;

[ObservableProperty]
private bool isGeocodingRunning;

[ObservableProperty]
private string? backgroundActivityText;   // wird in der Statuszeile zusätzlich angezeigt

public string GeocodingButtonText =>
    IsGeocodingRunning ? "Abbruch Ortsermittlung" : "Orte ermitteln";

partial void OnIsGeocodingRunningChanged(bool value) =>
    OnPropertyChanged(nameof(GeocodingButtonText));
```

Der bestehende Command (in Schritt 3 als `[RelayCommand]` für „Orte ermitteln") wird ersetzt durch einen Toggle-Command:

```csharp
[RelayCommand]
private async Task ToggleGeocodingAsync()
{
    if (IsGeocodingRunning)
    {
        _geocodingCts?.Cancel();
        return;
    }

    _geocodingCts = new CancellationTokenSource();
    var ct = _geocodingCts.Token;
    IsGeocodingRunning = true;

    try
    {
        var geocoder = _geocoderFactory.CreateForFolder(CurrentFolder!);
        var queue = Photos
            .Where(p => p.State == PhotoState.Selected
                     && p.Latitude is not null
                     && p.Longitude is not null
                     && string.IsNullOrEmpty(p.Location))
            .ToList();

        if (queue.Count == 0)
        {
            BackgroundActivityText = "Alle ausgewählten Fotos haben bereits einen Ort.";
            return;
        }

        var resolved = 0;
        foreach (var photo in queue)
        {
            ct.ThrowIfCancellationRequested();

            var location = await geocoder.ResolveAsync(
                photo.Latitude!.Value, photo.Longitude!.Value, ct);

            if (!string.IsNullOrEmpty(location))
            {
                photo.Location = location;   // setzt PropertyChanged → UI updated sich
                BackgroundActivityText = $"Ort ermittelt: {location}";
                resolved++;
            }
        }

        BackgroundActivityText = $"Ortsermittlung abgeschlossen ({resolved} von {queue.Count} Fotos).";
    }
    catch (OperationCanceledException)
    {
        BackgroundActivityText = "Ortsermittlung abgebrochen.";
    }
    catch (Exception ex)
    {
        BackgroundActivityText = $"Ortsermittlung fehlgeschlagen: {ex.Message}";
    }
    finally
    {
        IsGeocodingRunning = false;
        _geocodingCts?.Dispose();
        _geocodingCts = null;
    }
}
```

Wichtig: **`IsBusy` wird nicht mehr gesetzt** — der globale `IsBusy`-Spinner ist für blockierende Operationen (Scan, Karten-Generierung) reserviert. Geocoding läuft als Hintergrund-Task; alle anderen Aktionen (Foto auswählen, State togglen, Karten generieren) bleiben verfügbar.

### Toolbar-Button anpassen

```xml
<Button Command="{Binding ToggleGeocodingCommand}"
        Content="{Binding GeocodingButtonText}"
        ToolTip="Ermittelt für alle ausgewählten Fotos mit GPS-Daten den Ortsnamen via OpenStreetMap.
Ein zweiter Klick bricht die laufende Abfrage ab."
        Margin="0,8,0,0"/>
```

Der Button bleibt während des Laufs immer aktivierbar — ein zweiter Klick löst den Abbruch aus. Visuell könnte er während des Laufs eine Akzentfarbe annehmen (über `DataTrigger` auf `IsGeocodingRunning`).

### Statuszeile aktualisieren

Die Statuszeile zeigt zusätzlich `BackgroundActivityText`, getrennt durch ` · `:

```xml
<StatusBar>
  <StatusBarItem>
    <TextBlock Text="{Binding StatusText}"/>
  </StatusBarItem>
  <Separator Visibility="{Binding BackgroundActivityText, Converter={StaticResource NullOrEmptyToCollapsedConverter}}"/>
  <StatusBarItem>
    <TextBlock Text="{Binding BackgroundActivityText}"
               Foreground="{StaticResource AccentBrush}"
               Visibility="{Binding BackgroundActivityText, Converter={StaticResource NullOrEmptyToCollapsedConverter}}"/>
  </StatusBarItem>
</StatusBar>
```

So sieht der Nutzer während des Laufs den jeweils zuletzt ermittelten Ort kontinuierlich aktualisiert — eine charmante kleine Live-Feedback-Schleife, die das Gefühl gibt, dass die App arbeitet.

### Verhalten beim Fenster-Schließen

Wenn beim `Window.Closing` `IsGeocodingRunning == true` ist: die laufende Operation per `_geocodingCts.Cancel()` abbrechen, kurz auf Beendigung warten (Task aufbewahren als Field), dann schließen. Kein Dialog nötig — Geocoding ist nicht-destruktiv und kann jederzeit fortgesetzt werden.

### Tests

In `TravelJournal.Core.Tests` (Geocoder-Logik testbar mit Fake `IReverseGeocoder`):

- `JsonGeocodeCache` mit Cancellation-Token: ein Cancel während laufendem Inner-Call wirft `OperationCanceledException`, kein Cache-Eintrag wird geschrieben.
- Cache-Treffer respektiert ebenfalls Cancellation (auch wenn der Pfad sehr schnell ist).

Für die UI-Logik (toggle, status text) keine automatisierten Tests notwendig — manuell verifizierbar.

### Akzeptanzkriterien Änderung 3

- „Orte ermitteln" startet Geocoding **nur** für ausgewählte Fotos mit Koordinaten und leerem `Location`. Abgewählte und offene Fotos werden ignoriert.
- Während des Laufs ist die UI voll bedienbar: Foto-Auswahl, State togglen, Filterwechsel funktionieren ohne spürbare Verzögerung.
- Button-Text wechselt nach Klick auf „Abbruch Ortsermittlung" und zurück nach Abschluss/Abbruch.
- Statuszeile zeigt nach jeder erfolgreichen Auflösung den neuen Ortsnamen an, z.B. „Ort ermittelt: Stein am Rhein". Nach Abschluss erscheint die Zusammenfassung „Ortsermittlung abgeschlossen (8 von 12 Fotos)".
- Beim Klick auf „Abbruch Ortsermittlung" stoppt der Lauf binnen einer Sekunde, Statuszeile zeigt „Ortsermittlung abgebrochen".
- Beim Schließen des Fensters mit laufender Ortsermittlung wird sauber abgebrochen, kein Hang.

---

## Korrekturhinweis zu Schritt 6

Die in Schritt 6 spezifizierte Ablage in `<photofolder>/maps/` wird mit Schritt 7 obsolet. Falls Schritt 6 bereits implementiert ist, gilt:

- `outputFolder` in `GenerateMapsAsync` wird auf `CurrentFolder` umgestellt (siehe 1a).
- Bestehende Karten in `<photofolder>/maps/` einmalig manuell in `<photofolder>/` verschieben oder neu generieren lassen.
- Der Tile-Cache bleibt unverändert in `<photofolder>/.tile-cache/`.

---

## Was bewusst NICHT teil dieser Iteration ist

- Persistierung der Karten-Generierungs-Parameter (Stopp-Schwelle, Auflösung) als CSV-Spalten oder Project-Datei
- Drag&Drop-Sortierung von Karten in der Liste
- Eigener „Karten-Manager"-Tab zur Verwaltung mehrerer Karten-Sets
- Parallele Geocoding-Anfragen (bewusst sequentiell wegen Nominatim-Rate-Limit)
- Geocoding-Provider-Auswahl in der UI (bleibt im Code konfiguriert)
- Wiederholungsversuche (Retry) bei Geocoding-Fehlern
