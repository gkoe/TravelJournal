# Claude Code Anweisung — Schritt 10: Statische Web-Präsentation

## Ziel

Eine zur Bildschirm-Präsentation aus Schritt 9 analoge **statische, lokal lauffähige Web-Präsentation** als Export-Funktion. Auf Knopfdruck im WPF-Tool wird ein vollständig in sich geschlossener Ordner erzeugt, der per Doppelklick auf `index.html` im Browser läuft und keine zusätzliche Software, keinen Server und keine Netzwerk-Verbindung braucht. Inhaltlich identisch zur Schritt-9-Präsentation:

- **Startbild** (Foto mit `State=Start` / Wert 3) — wird immer als erstes Item gezeigt, unabhängig von seinem `DateTime`. Typisch ein Titel-Bild ohne EXIF-Metadaten.
- Danach **alle ausgewählten Fotos** (`State=Selected` / Wert 1) **und alle generierten Karten** in chronologischer Reihenfolge.
- **Endbild** (Foto mit `State=End` / Wert 4) — wird immer als letztes Item gezeigt.
- **5 Sekunden pro Item**, bei Fotos **2 Sekunden Info-Einblendung** (Ort, Wochentag, Datum, Uhrzeit), bei Karten keine Einblendung. Fotos ohne `DateTime` (typisches Startbild) zeigen kein Overlay.
- Tastatur-Steuerung: `Esc` Ende, `Space` Pause, `←`/`→` Navigation, `F` Vollbild (Browser-nativ).

### State-Werte

`PhotoState` ist um `Start = 3` und `End = 4` erweitert. Der Exporter behandelt diese als Sonderrollen mit fester Position in der Playlist (vorne bzw. hinten), sortiert sie nicht in den chronologischen Mittelblock ein.

Die Web-Präsentation ist ein Export-Artefakt — sie kann auf USB-Stick mitgenommen, per E-Mail verschickt oder auf einen statischen Webspace gelegt werden.

## Kontext

Setzt auf Schritt 1–9 auf. Nutzt `TravelJournal.Core` (insbesondere `TourCsvReader` und `PhotoFolderScanner`). Die WPF-UI bekommt einen neuen Toolbar-Button „Web-Präsentation exportieren". Vor dem Export sollten Karten generiert sein (Schritt 6/7), sonst besteht die Präsentation nur aus Fotos.

## Tech-Stack

- **.NET 10** für den Exporter
- **C# 13**, file-scoped namespaces, `Nullable` enabled
- **NuGet (neu in `TravelJournal.WebExporter`):** `SixLabors.ImageSharp` (Bildoptimierung; im Repo schon vorhanden)
- **Web-Frontend:** Vanilla HTML/CSS/JavaScript (ES Modules), **kein Build-Schritt**, keine Frameworks. Begründung: maximale Einfachheit beim lokalen Ausführen, perfekte Kontrolle über Animationen, keine Toolchain-Abhängigkeiten.

## Solution-Erweiterung

Neues Projekt `TravelJournal.WebExporter` (Klassenbibliothek mit optionalem Console-Einstiegspunkt) parallel zu `TravelJournal.Wpf`:

```
src/
├── TravelJournal.Core/           (existiert)
├── TravelJournal.Wpf/            (existiert)
└── TravelJournal.WebExporter/    (NEU)
    ├── TravelJournal.WebExporter.csproj
    ├── WebPresentationExporter.cs       Hauptservice
    ├── Models/
    │   ├── PresentationManifest.cs
    │   └── PresentationItem.cs
    ├── Services/
    │   ├── ImageOptimizer.cs            ImageSharp-basiert
    │   └── ManifestBuilder.cs
    ├── Templates/                       eingebettete Resources
    │   ├── index.html
    │   ├── style.css
    │   └── app.js
    └── Program.cs                       optionaler CLI-Einstieg
```

`TravelJournal.WebExporter` referenziert `TravelJournal.Core`. `TravelJournal.Wpf` referenziert `TravelJournal.WebExporter`, damit der WPF-Toolbar-Button den Export direkt anstoßen kann.

`Templates/index.html`, `style.css` und `app.js` werden in der `.csproj` als `EmbeddedResource` deklariert:

```xml
<ItemGroup>
  <EmbeddedResource Include="Templates\index.html" />
  <EmbeddedResource Include="Templates\style.css" />
  <EmbeddedResource Include="Templates\app.js" />
</ItemGroup>
```

## Service-API

```csharp
public interface IWebPresentationExporter
{
    Task<int> ExportAsync(
        WebExportRequest request,
        IProgress<WebExportProgress>? progress = null,
        CancellationToken ct = default);
}

public sealed class WebExportRequest
{
    public required string SourcePhotoFolder { get; init; }
    public required string OutputFolder { get; init; }
    public string Title { get; init; } = "Reise-Präsentation";
    public int MaxImageWidthPx { get; init; } = 1920;
    public int JpegQuality { get; init; } = 82;
    public int PhotoDurationMs { get; init; } = 5000;
    public int OverlayVisibleMs { get; init; } = 2000;
}

public sealed record WebExportProgress(string Stage, int Current, int Total, string? Message);
```

Rückgabewert: Anzahl der exportierten Items.

## Pipeline `WebPresentationExporter.ExportAsync`

1. **Eingabe lesen**: `tour.csv` aus `SourcePhotoFolder` mit `TourCsvReader`. Drei Foto-Gruppen filtern:
   - **Start-Fotos**: `State == Start`
   - **Mittelblock-Fotos**: `State == Selected` **und** `DateTime != null`
   - **End-Fotos**: `State == End`

   Karten per Filename-Pattern `map_*.png` aus dem Foto-Ordner einsammeln, Timestamp aus dem Filename parsen (analog zum `PhotoFolderScanner` aus Schritt 7).
2. **Validieren**: bei `start.Count + middle.Count + end.Count + maps.Count == 0` → Fehlermeldung „Keine Inhalte für die Präsentation gefunden", Rückgabe `0`.
3. **Output-Ordner anlegen**: `OutputFolder`, sowie Unterordner `photos/` und `maps/`.
4. **Fotos optimieren**: jedes Foto (Start, Middle, End) in `photos/<filename>.jpg` mit `ImageOptimizer` schreiben. Auto-Rotate via EXIF-Orientation, Resize auf `MaxImageWidthPx`, JPEG mit konfigurierter Qualität. EXIF-Metadaten dürfen entfernt werden (Datenschutz beim Verteilen). Progress-Report `("photos", x, total, ...)`.
5. **Karten kopieren**: jede Karte als `maps/<filename>.png` 1:1 kopieren (sind bereits 1600×1200 und web-tauglich groß, kein erneutes Encoding).
6. **Manifest bauen**: Item-Liste in fester Reihenfolge `Start → Middle (chronologisch) → End`. Mittelblock vereint Selected-Fotos und Karten und sortiert nach `dateTime`. Start- und End-Items behalten ihre Eingabe-Reihenfolge (sortiert nach `DateTime ?? DateTime.MinValue` bzw. `DateTime.MaxValue`, dann `Filename`).
7. **Manifest schreiben**: `tour.json` mit `System.Text.Json`, eingerückt für Lesbarkeit (`WriteIndented = true`).
8. **Templates ausspielen**: `index.html`, `style.css`, `app.js` aus den eingebetteten Resources in den Output-Ordner schreiben. Bei `index.html` vorher Platzhalter ersetzen (siehe Templating).
9. **Fertig**: Anzahl Items zurückgeben.

## Manifest-Format `tour.json`

```jsonc
{
  "title": "Radtour Alpe Adria 2026",
  "generatedAt": "2026-05-02T10:15:00Z",
  "photoDurationMs": 5000,
  "overlayVisibleMs": 2000,
  "items": [
    {
      "type": "photo",
      "role": "start",
      "src": "photos/Start.jpg",
      "dateTime": null,
      "location": null,
      "title": null,
      "description": null
    },
    {
      "type": "photo",
      "role": "middle",
      "src": "photos/20260426_134157.jpg",
      "dateTime": "2026-04-26T13:41:58",
      "location": "Villach",
      "title": "Radtour Alpe Adria vom 26.4. - 15.5.2026",
      "description": "Walter Kehrer, Ulrich Knell, Herbert Lackinger, Rupert Obermüller, Ewald Feilmeir, Gerald Köck"
    },
    {
      "type": "map",
      "role": "middle",
      "src": "maps/map_2026-04-26T17-23-55.png",
      "dateTime": "2026-04-26T17:23:55"
    },
    {
      "type": "photo",
      "role": "end",
      "src": "photos/20260501_090554.jpg",
      "dateTime": "2026-05-01T09:05:54",
      "location": null,
      "title": null,
      "description": null
    }
  ]
}
```

Felder:

- `type`: `"photo"` oder `"map"`.
- `role`: `"start"`, `"middle"` oder `"end"`. Macht die Sonderrollen explizit für das Frontend (z.B. um Start-Items optisch anders zu behandeln, falls gewünscht).
- `dateTime`: ISO 8601, kann `null` sein (typisch beim Startbild).
- `location`, `title`, `description`: optional, können `null` sein. Das Frontend handhabt fehlende Felder transparent.

Items werden bereits beim Schreiben in der finalen Reihenfolge geliefert (`start → middle → end`); das Frontend rendert sie 1:1 in dieser Reihenfolge.

## Frontend `index.html`

Schlanker, semantisch klarer Aufbau:

```html
<!DOCTYPE html>
<html lang="de">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
  <title>{{TITLE}}</title>
  <link rel="stylesheet" href="style.css">
</head>
<body>
  <main id="stage">
    <img id="current" alt="" />
    <img id="next" alt="" aria-hidden="true" />

    <section id="overlay" aria-hidden="true">
      <h1 id="overlay-location"></h1>
      <p id="overlay-datetime"></p>
    </section>

    <div id="hint">Esc beendet · Space pausiert · ← →</div>
  </main>
  <script type="module" src="app.js"></script>
</body>
</html>
```

`{{TITLE}}` wird vom `WebPresentationExporter` mit `WebExportRequest.Title` ersetzt.

## Frontend `style.css`

Gestaltungsprinzipien:

- Vollflächiger schwarzer Hintergrund (`background:#000`).
- `<main>` per Grid/Flex auf `100vw × 100vh`, kein Scrolling.
- `#current` und `#next` absolut positioniert, `object-fit: contain`, Übergang per `opacity`-Transition (300 ms ease).
- `#overlay` unten links, Padding `48px 64px 56px`, Hintergrund linearer Gradient von transparent (oben) auf 80 % schwarz (unten). Linksbündige Schrift in Weiß, FontFamily Systemschrift (`-apple-system, "Segoe UI", Roboto, sans-serif`).
- `#overlay-location` 44 px, `font-weight: 600`. Bei fehlendem Ort wird das Element ausgeblendet, dann wächst die Datum/Zeit-Zeile auf 38 px.
- `#overlay-datetime` 26 px, leicht heller (`#eee`).
- Overlay hat eine eigene `opacity`-Transition (400 ms) zwischen 0 und 1, gesteuert durch eine `.visible`-Klasse.
- `#hint` oben rechts, sehr dezent (Opacity 0.5), blendet nach 4 s aus.
- Cursor wird nach 2 s Inaktivität ausgeblendet (`cursor: none` über JS-Timer-gesteuerte Klasse auf `<body>`).

## Frontend `app.js` (ES Module)

Verantwortlichkeiten in klaren Funktions-Blöcken:

```js
const state = {
  manifest: null,
  index: 0,
  paused: false,
  itemTimer: null,
  overlayTimer: null,
};

async function init() {
  state.manifest = await fetch('tour.json').then(r => r.json());
  document.title = state.manifest.title;
  if (!state.manifest.items?.length) {
    showError('Keine Inhalte gefunden.');
    return;
  }
  preloadImage(state.manifest.items[0].src);
  show(0);
  scheduleHintFade();
  setupKeyboard();
  setupCursorHide();
}

function show(index) {
  if (index < 0 || index >= state.manifest.items.length) {
    finish();
    return;
  }
  state.index = index;
  const item = state.manifest.items[index];

  crossfadeTo(item.src);
  updateOverlay(item);
  scheduleNext();
  preloadImage(state.manifest.items[index + 1]?.src);
}

function updateOverlay(item) {
  clearTimeout(state.overlayTimer);
  const overlay = document.getElementById('overlay');

  // Karten und Fotos ohne dateTime (typisch: Startbild) → kein Overlay
  if (item.type !== 'photo' || !item.dateTime) {
    overlay.classList.remove('visible');
    return;
  }

  const dt = new Date(item.dateTime);
  const formatter = new Intl.DateTimeFormat('de-DE', {
    weekday: 'long', day: 'numeric', month: 'long', year: 'numeric',
    hour: '2-digit', minute: '2-digit'
  });
  const datetime = formatter.format(dt).replace(',', ' ·');

  document.getElementById('overlay-location').textContent = item.location ?? '';
  document.getElementById('overlay-location').style.display = item.location ? '' : 'none';
  document.getElementById('overlay-datetime').textContent = datetime;

  overlay.classList.add('visible');
  state.overlayTimer = setTimeout(
    () => overlay.classList.remove('visible'),
    state.manifest.overlayVisibleMs ?? 2000
  );
}

function scheduleNext() {
  clearTimeout(state.itemTimer);
  if (state.paused) return;
  state.itemTimer = setTimeout(
    () => show(state.index + 1),
    state.manifest.photoDurationMs ?? 5000
  );
}

function crossfadeTo(src) { /* swap #current ↔ #next, animate opacity */ }
function preloadImage(src) { if (!src) return; const i = new Image(); i.src = src; }
function setupKeyboard() { /* Esc, Space, ArrowLeft, ArrowRight, f for fullscreen */ }
function setupCursorHide() { /* mouse-move shows cursor, idle 2s hides */ }
function scheduleHintFade() { setTimeout(() => document.getElementById('hint').classList.add('hidden'), 4000); }
function finish() { /* zeige finalen Hinweis "Ende — F5 zum Wiederholen" */ }
function showError(msg) { /* großer roter Hinweis-Text */ }

init();
```

Tastatur-Bindings:

- `Esc` → Pause + Hinweis „Beendet — F5 zum Wiederholen". Browser-Fenster nicht schließen (von JS aus nicht möglich); Nutzer schließt selbst.
- `Space` → `togglePause()`: Item-Timer pausieren oder fortsetzen.
- `→` / `ArrowRight` → `show(state.index + 1)` (überspringt Restzeit).
- `←` / `ArrowLeft` → `show(Math.max(0, state.index - 1))`.
- `F` → `document.documentElement.requestFullscreen()`. Esc verlässt automatisch (Browser-Standardverhalten).

## Templating beim Schreiben von `index.html`

`WebPresentationExporter` ersetzt vor dem Schreiben den Platzhalter `{{TITLE}}` mit `HtmlEncoder.Default.Encode(request.Title)`. Optional weitere Platzhalter:

- `{{TITLE}}` → Seitentitel und `<title>`-Element
- `{{GENERATED_AT}}` → ISO-Datum, kann in einem Footer-Hinweis genutzt werden

Ein einfacher `string.Replace`-Pass reicht — kein vollwertiger Template-Engine nötig.

## WPF-Integration

### Neuer Service in `TravelJournal.Wpf/Services/`

```csharp
public interface IWebExportService
{
    Task<int> ExportAsync(string sourceFolder, string outputFolder,
        IProgress<WebExportProgress>? progress, CancellationToken ct);
}
```

Die Implementation wrappt `IWebPresentationExporter` aus `TravelJournal.WebExporter`. So bleibt die WPF-Schicht von der konkreten Exporter-Klasse entkoppelt und in Tests mockbar.

### `MainViewModel`-Erweiterung

```csharp
[RelayCommand(CanExecute = nameof(CanExportWebPresentation))]
private async Task ExportWebPresentationAsync()
{
    if (CurrentFolder is null) return;

    var picked = _folderDialog.PickFolder(initialFolder: null);
    if (picked is null) return;

    IsBusy = true;
    var cts = new CancellationTokenSource();
    var progress = new Progress<WebExportProgress>(p =>
    {
        StatusText = p.Stage switch
        {
            "photos" => $"Fotos optimieren {p.Current}/{p.Total}",
            "maps"   => $"Karten kopieren {p.Current}/{p.Total}",
            "templates" => "Templates schreiben …",
            _ => p.Message ?? ""
        };
    });

    try
    {
        var count = await _webExport.ExportAsync(CurrentFolder, picked, progress, cts.Token);
        StatusText = count == 0
            ? "Keine Inhalte zum Exportieren gefunden."
            : $"Web-Präsentation erstellt ({count} Items). Ordner geöffnet.";

        if (count > 0)
        {
            // index.html im Default-Browser öffnen
            var indexPath = Path.Combine(picked, "index.html");
            Process.Start(new ProcessStartInfo(indexPath) { UseShellExecute = true });
        }
    }
    finally
    {
        IsBusy = false;
    }
}

private bool CanExportWebPresentation() =>
    !IsBusy && !string.IsNullOrEmpty(CurrentFolder)
    && (Photos.Any(p => p.State == PhotoState.Selected) || Maps.Any());
```

### Toolbar-Button

In der „EXPORT"-Sektion, unter „Karten generieren" und „Präsentation starten":

```xml
<Button Content="Web-Präsentation exportieren"
        Command="{Binding ExportWebPresentationCommand}"
        ToolTip="Erzeugt einen lokal lauffähigen Web-Präsentationsordner mit allen ausgewählten Fotos und Karten.
Auswahl des Zielordners im nächsten Schritt."/>
```

### DI-Registrierung in `App.xaml.cs`

```csharp
services.AddSingleton<IWebPresentationExporter, WebPresentationExporter>();
services.AddSingleton<IWebExportService, WebExportService>();
```

## Optionaler Console-Einstiegspunkt

`TravelJournal.WebExporter/Program.cs` — kleiner CLI-Einstieg, damit der Exporter auch ohne WPF aufrufbar ist (für Skripte, CI):

```
TravelJournal.WebExporter.exe --source <photo-folder> --output <web-folder> [--title "Reise"]
```

Implementiert mit einem schlanken Argument-Parser (z.B. mehrere `args.IndexOf("--source")`-Suchen — kein `System.CommandLine` nötig, nur sechs Zeilen).

## Tests in `tests/TravelJournal.WebExporter.Tests/`

Realistisch testbar:

- `ManifestBuilder`: gegebene Liste aus 1 Start, 3 Selected-Fotos, 2 Karten und 1 End ergibt ein Manifest mit 7 Items in der Reihenfolge `start, middle×5 (chronologisch), end`.
- `ManifestBuilder`: Mittelblock-Foto ohne `DateTime` wird ausgeschlossen.
- `ManifestBuilder`: Startbild ohne `DateTime` (typisches `Start.jpg`) wird **trotzdem** als erstes Item aufgenommen — `dateTime` im Manifest ist `null`.
- `ManifestBuilder`: Endbild mit oder ohne DateTime steht immer am Ende.
- `ManifestBuilder`: `role`-Feld ist korrekt gesetzt (`"start"`/`"middle"`/`"end"`).
- `ManifestBuilder`: Felder `location`, `title`, `description` werden korrekt aus dem `Photo` übernommen oder sind `null`.
- `ImageOptimizer`: ein Test-JPG (z.B. 4000×3000, Hochformat-Orientation) ergibt nach Optimierung Maße `≤ 1920` an der längsten Kante und ist Auto-rotiert (kein EXIF-Orientation-Tag mehr in der Output-Datei).
- `WebPresentationExporter`: End-to-End mit gemockten Services schreibt eine vorhersagbare Datei-Struktur in einen Temp-Ordner, inklusive `tour.json`, `index.html`, `style.css`, `app.js`, optimierten Fotos und kopierten Karten.

Keine Browser-Tests notwendig — das Frontend wird manuell verifiziert.

## Akzeptanzkriterien

- `dotnet build` warningfrei, alle Tests grün.
- WPF-Button „Web-Präsentation exportieren" erscheint in der Export-Sektion und ist bei mindestens einem Selected-Foto oder einer Karte aktiv.
- Klick auf den Button öffnet einen Folder-Picker. Nach Auswahl läuft der Export mit Live-Status.
- Im Output-Ordner liegen `index.html`, `style.css`, `app.js`, `tour.json`, `photos/*.jpg`, `maps/*.png`.
- Doppelklick auf `index.html` öffnet die Präsentation im Default-Browser. Die Slideshow startet automatisch, schwarzer Hintergrund.
- **Erstes Item ist das Startbild** (`State=Start`), unabhängig von dessen `DateTime`. Falls kein Startbild markiert ist, beginnt die Slideshow mit dem chronologisch ersten Mittelblock-Item.
- **Letztes Item ist das Endbild** (`State=End`), unabhängig von dessen `DateTime`.
- Dazwischen: ausgewählte Fotos und Karten in chronologischer Reihenfolge.
- Pro Item 5 Sekunden, bei Fotos mit `dateTime` die ersten 2 Sekunden mit Overlay (Ort + „Montag, 12. April 2026 · 09:14"), danach Overlay sanft ausgeblendet.
- **Startbild ohne `dateTime`** zeigt **kein** Overlay — wirkt als Titel-Karte.
- Karten zeigen kein Overlay.
- `Esc` stoppt, `Space` pausiert/setzt fort, `→`/`←` springen, `F` Vollbild.
- Auch ohne Internet-Verbindung läuft alles (alle Resources sind lokal im Output-Ordner).
- Auch beim Hosten unter `python -m http.server` oder einem statischen Webhost funktioniert die Präsentation ohne CORS-Probleme.

## Was bewusst NICHT teil dieser Iteration ist

- Begleitmusik
- Mehrsprachigkeit der UI-Hinweise (statisch deutsch)
- Responsive Layout-Optimierungen für sehr schmale Mobil-Viewports (funktioniert, aber nicht poliert)
- Lazy-Loading-Optimierungen über Browser-Standard hinaus
- Aufzeichnung der Präsentation als MP4
- Server-Komponente mit Live-Aktualisierung der Inhalte
- Konfigurierbare Item- oder Overlay-Dauer in der WPF-UI (Werte werden über `WebExportRequest`-Defaults gesetzt; in der UI kann später ergänzt werden)
- Build-Toolchain (Vite/TypeScript) — bewusst Vanilla, damit keine `node_modules` und kein `npm install` nötig sind
