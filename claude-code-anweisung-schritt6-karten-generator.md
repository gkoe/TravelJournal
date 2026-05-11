# Claude Code Anweisung — Schritt 6: Karten-Generator

## Ziel

Erweitere die Anwendung um einen Karten-Generator, der aus den ausgewählten Fotos pro echtem Stopp (Pause > 30 Minuten zwischen aufeinanderfolgenden Fotos) ein Karten-PNG erzeugt. Alle Karten haben identische Größe, identischen Kartenausschnitt und identischen Zoomlevel — abgeleitet aus der Bounding-Box aller ausgewählten Fotos. Pro Karte unterscheidet sich nur die gezeichnete Polyline (von Foto 1 bis zum jeweiligen Stopp) und die Position des aktiven Markers. Die Kartenoptik ist nahe an Google Maps (MapTiler Streets als Tile-Quelle).

## Kontext

Setzt auf Schritt 1–5 auf. Die Logik liegt in `TravelJournal.Core` (testbar, plattformneutral). Die WPF-UI bekommt einen neuen Toolbar-Button „Karten generieren" mit Fortschrittsanzeige. `SixLabors.ImageSharp` ist seit Schritt 4 schon Dependency und wird hier intensiv genutzt.

## Tech-Stack

- **.NET 10**, C# 13
- **MapTiler Cloud API** (kostenloser API-Key, Kunde erstellt sich ein Konto auf maptiler.com). Tile-Endpoint: `https://api.maptiler.com/maps/streets-v2/256/{z}/{x}/{y}.png?key={apiKey}`. Style „streets-v2" sieht Google-Maps-ähnlich aus.
- **OSM Standard** als Fallback ohne API-Key: `https://tile.openstreetmap.org/{z}/{x}/{y}.png` (mit aussagekräftigem User-Agent gemäß OSM-Tile-Usage-Policy).
- **NuGet (in `TravelJournal.Core` neu):** `Microsoft.Extensions.Http` (für `IHttpClientFactory`).
- **NuGet (in `TravelJournal.Wpf` neu):** `Microsoft.Extensions.Configuration.Json` (zum Lesen der `appsettings.json`).

## Konfiguration und API-Key-Handling

**Niemals den API-Key in Quellcode oder in die `tour.csv` schreiben.** Drei Bezugsquellen in absteigender Priorität:

1. Environment-Variable `MAPTILER_API_KEY`.
2. `appsettings.json` neben der Exe mit `{ "MapTiler": { "ApiKey": "..." } }`. Diese Datei wird in `.gitignore` aufgenommen, eine Vorlage `appsettings.example.json` ohne Key liegt im Repo.
3. Kein Key gefunden → Renderer fällt automatisch auf `OsmTileSource` zurück. UI zeigt einen Hinweis-Toast „Kein MapTiler-Key konfiguriert — verwende OpenStreetMap-Standard".

`MapRenderingOptions` (POCO in `TravelJournal.Core/MapRendering/`) bündelt alle einstellbaren Werte:

```csharp
public sealed class MapRenderingOptions
{
    public int TargetWidthPx { get; init; } = 1600;
    public int TargetHeightPx { get; init; } = 1200;
    public TimeSpan StopThreshold { get; init; } = TimeSpan.FromMinutes(30);
    public double BoundsPaddingFraction { get; init; } = 0.12;
    public int MaxParallelTileDownloads { get; init; } = 4;
    public bool AddFinalSummaryMap { get; init; } = true;
    public string? MapTilerApiKey { get; init; }
}
```

## Solution-Erweiterung

Neuer Bereich in `TravelJournal.Core`:

```
src/TravelJournal.Core/
└── MapRendering/
    ├── IMapRenderer.cs
    ├── TileMapRenderer.cs
    ├── MapRenderingOptions.cs
    ├── MapRenderProgress.cs
    ├── StopDetector.cs
    ├── WebMercator.cs
    ├── MapStyle.cs
    ├── Models/
    │   ├── MapBounds.cs
    │   ├── StopPoint.cs
    │   └── MapTile.cs
    ├── TileSources/
    │   ├── ITileSource.cs
    │   ├── MapTilerTileSource.cs
    │   └── OsmTileSource.cs
    └── Caching/
        ├── ITileCache.cs
        └── FileTileCache.cs
```

Tests in `tests/TravelJournal.Core.Tests/MapRendering/`.

## Datenmodelle

```csharp
public sealed record MapBounds(double MinLat, double MinLon, double MaxLat, double MaxLon)
{
    public static MapBounds FromPhotos(IEnumerable<Photo> photos);
    public MapBounds WithPadding(double fraction);
}

public sealed record StopPoint(
    DateTime Timestamp,
    double Latitude,
    double Longitude,
    int PhotoIndex // Index in der sortierten ausgewählten Foto-Liste
);

public sealed record MapTile(int Z, int X, int Y)
{
    public string CacheKey(string providerId) => $"{providerId}/{Z}/{X}/{Y}.png";
}

public sealed record MapRenderProgress(
    string Stage,        // "tiles" | "compose" | "render"
    int Current,
    int Total,
    string? Message
);
```

## `WebMercator` (statische Mathematik)

```csharp
public static class WebMercator
{
    public const int TileSize = 256;

    public static (double X, double Y) LatLonToGlobalPixel(double lat, double lon, int zoom);
    public static (int TileX, int TileY) GlobalPixelToTile(double x, double y);
    public static int CalculateZoom(MapBounds bounds, int targetWidthPx, int targetHeightPx);
}
```

Formeln:

- `x = (lon + 180) / 360 * 2^zoom * TileSize`
- `y = (1 - ln(tan(latRad) + 1/cos(latRad)) / π) / 2 * 2^zoom * TileSize`

`CalculateZoom`: probiert Zoom 18 absteigend bis 0; wählt den höchsten, bei dem die in Pixel projizierte Bounding-Box noch komplett ins Zielformat passt.

## `StopDetector`

```csharp
public sealed class StopDetector
{
    public IReadOnlyList<StopPoint> DetectStops(
        IReadOnlyList<Photo> photosSortedByDateTime,
        TimeSpan threshold,
        bool addFinalSummary);
}
```

Algorithmus:

```
result = []
for i in 0..count-2:
    if photos[i+1].DateTime - photos[i].DateTime > threshold:
        result.add(StopPoint(
            timestamp = photos[i].DateTime + 1 second,
            lat = photos[i].Latitude,
            lon = photos[i].Longitude,
            photoIndex = i
        ))
if addFinalSummary and count > 0:
    last = photos[count-1]
    result.add(StopPoint(last.DateTime + 1 second, last.Lat, last.Lon, count-1))
return result
```

Fotos ohne GPS-Daten werden bei der Stopp-Erkennung übersprungen (sie sollen den Algorithmus aber nicht crashen lassen).

## `ITileSource` und Implementierungen

```csharp
public interface ITileSource
{
    string ProviderId { get; }       // z.B. "maptiler-streets" oder "osm"
    string AttributionText { get; }  // wird unten rechts auf jede Karte gerendert
    Task<byte[]> GetTileAsync(MapTile tile, CancellationToken ct);
}
```

`MapTilerTileSource` baut die URL mit dem API-Key, setzt User-Agent `TravelJournal/1.0`, behandelt HTTP 429 (Rate Limit) mit kurzem Backoff. Attribution: `"© MapTiler © OpenStreetMap contributors"`.

`OsmTileSource`: URL ohne Key, User-Agent muss eine Kontaktangabe enthalten (Konstruktor-Parameter `string contactEmail`). Attribution: `"© OpenStreetMap contributors"`.

Beide bekommen ihren `HttpClient` per `IHttpClientFactory`.

## `ITileCache` und `FileTileCache`

```csharp
public interface ITileCache
{
    Task<byte[]?> TryGetAsync(string cacheKey, CancellationToken ct);
    Task PutAsync(string cacheKey, byte[] data, CancellationToken ct);
}
```

`FileTileCache` speichert unter einem konfigurierbaren Wurzelpfad (Default: `<photofolder>/.tile-cache/`). Verzeichnisse werden bei Bedarf angelegt. Atomar via Tempdatei + Move.

## `MapStyle` (Konstanten)

```csharp
public static class MapStyle
{
    public static readonly Color RouteOuter = Color.White;
    public static readonly Color RouteInner = Color.ParseHex("4285F4"); // Google-Blau
    public const float RouteOuterWidth = 8f;
    public const float RouteInnerWidth = 5f;

    public static readonly Color PastStopFill = Color.White;
    public static readonly Color PastStopBorder = Color.ParseHex("4285F4");
    public const float PastStopRadius = 3f;
    public const float PastStopBorderWidth = 1.5f;

    public static readonly Color CurrentStopFill = Color.ParseHex("EA4335");
    public static readonly Color CurrentStopBorder = Color.White;
    public const float CurrentStopRadius = 9f;
    public const float CurrentStopBorderWidth = 3f;

    public static readonly Color AttributionBackground = Color.FromRgba(255, 255, 255, 200);
    public static readonly Color AttributionText = Color.FromRgb(60, 60, 60);
    public const float AttributionFontSize = 11f;
}
```

## `IMapRenderer` und `TileMapRenderer`

```csharp
public interface IMapRenderer
{
    Task<int> RenderAllAsync(
        IReadOnlyList<Photo> selectedPhotos,
        string outputFolder,
        IProgress<MapRenderProgress>? progress = null,
        CancellationToken ct = default);
}
```

Ablauf in `TileMapRenderer.RenderAllAsync`:

1. Fotos nach `DateTime` sortieren, Fotos ohne GPS herausfiltern.
2. Wenn weniger als 2 Fotos mit GPS: 0 zurück, kein Throw.
3. Bounding-Box aus allen verbliebenen Fotos berechnen, Padding addieren.
4. Zoom mit `WebMercator.CalculateZoom` ermitteln.
5. Globale Pixel-Koordinaten der Box-Ecken berechnen → Tile-Range bestimmen.
6. Alle benötigten Tiles parallel laden (max `MaxParallelTileDownloads`); Progress-Report `("tiles", x, total, ...)`. Cache zuerst, sonst HTTP.
7. Basis-Bitmap zusammenbauen: leeres `Image<Rgba32>` der Tile-Mosaik-Größe, jedes Tile an die richtige Pixel-Position drucken (`image.Mutate(x => x.DrawImage(tile, position, 1f))`).
8. Auf Zielauflösung zuschneiden: Crop-Origin so wählen, dass die Bounding-Box mittig sitzt; Output exakt `TargetWidthPx × TargetHeightPx`.
9. Attributions-Text unten rechts mit halbtransparentem weißen Hintergrund einbrennen.
10. Stopps mit `StopDetector` ermitteln; falls keine Stopps: 0 zurück.
11. Pro Stopp: Basis klonen, Polyline (alle Fotos bis `PhotoIndex` einschließlich) und Marker zeichnen, als PNG speichern.
12. Dateiname: `map_{Timestamp:yyyy-MM-ddTHH-mm-ss}.png`.
13. `File.SetLastWriteTime` und `File.SetCreationTime` auf den Stopp-Timestamp setzen.
14. Progress-Report `("render", x, total, $"Karte {x}/{total}")`.
15. Zurückgeben: Anzahl erzeugter Karten.

### Polyline zeichnen

Foto-Koordinaten in Bildpixel umrechnen (gleicher `WebMercator`-Zoom, dann auf den Crop-Ausschnitt verschoben). Mit ImageSharp:

```csharp
var pen = new SolidPen(MapStyle.RouteOuter, MapStyle.RouteOuterWidth);
image.Mutate(x => x.DrawLine(pen, points));
pen = new SolidPen(MapStyle.RouteInner, MapStyle.RouteInnerWidth);
image.Mutate(x => x.DrawLine(pen, points));
```

Die zwei Lagen ergeben die helle Aura außen + blaue Linie innen. Joins/Caps sind in ImageSharp standardmäßig rund.

### Marker zeichnen

Vergangene Stopps (alle Fotos zwischen Index 0 und `PhotoIndex - 1`, die keine eigenen `StopPoint`s sind, plus alle vorherigen Stopps): kleine weiße Kreise mit blauem Rand.

Aktueller Stopp (Foto am `PhotoIndex`): großer roter Kreis mit weißem Rand. Dezenter Schatten durch zwei vorgelagerte halbtransparente schwarze Kreise mit leichtem Y-Offset.

Hilfs-Methoden in `TileMapRenderer`:

```csharp
private void DrawDot(Image image, PointF center, float radius, Color fill, Color border, float borderWidth);
private void DrawShadowedDot(Image image, PointF center, float radius, Color fill, Color border, float borderWidth);
```

## WPF-Integration

### Neuer Service in `TravelJournal.Wpf/Services/MapRendererFactory.cs`

```csharp
public interface IMapRendererFactory
{
    IMapRenderer Create(string photoFolder);
}
```

Liest Konfiguration (Env oder `appsettings.json`), entscheidet ob `MapTilerTileSource` oder `OsmTileSource`, baut `FileTileCache` mit Pfad `<photoFolder>/.tile-cache/`, instanziiert `TileMapRenderer`.

### `MainViewModel` erweitern

```csharp
[RelayCommand(CanExecute = nameof(CanGenerateMaps))]
private async Task GenerateMapsAsync()
{
    var selected = Photos.Where(p => p.State == PhotoState.Selected)
                        .OrderBy(p => p.DateTime)
                        .Select(p => p.UnderlyingPhoto)
                        .ToList();
    if (selected.Count < 2) return;

    var renderer = _mapRendererFactory.Create(CurrentFolder!);
    var outputFolder = Path.Combine(CurrentFolder!, "maps");
    Directory.CreateDirectory(outputFolder);

    IsBusy = true;
    _mapRenderCts = new CancellationTokenSource();
    var progress = new Progress<MapRenderProgress>(p =>
    {
        StatusText = p.Stage switch
        {
            "tiles"   => $"Tiles laden {p.Current}/{p.Total}",
            "compose" => "Karte komponieren …",
            "render"  => $"Karte {p.Current}/{p.Total}",
            _ => p.Message ?? ""
        };
    });

    try
    {
        var count = await renderer.RenderAllAsync(selected, outputFolder, progress, _mapRenderCts.Token);
        StatusText = count == 0
            ? "Keine Stopps erkannt — keine Karten erzeugt."
            : $"{count} Karten in 'maps/' erzeugt.";
    }
    catch (OperationCanceledException)
    {
        StatusText = "Karten-Generierung abgebrochen.";
    }
    finally
    {
        IsBusy = false;
        _mapRenderCts = null;
    }
}

private bool CanGenerateMaps() =>
    !IsBusy && !string.IsNullOrEmpty(CurrentFolder)
    && Photos.Count(p => p.State == PhotoState.Selected) >= 2;
```

`SelectedPhoto`/`Photos`-Änderungen müssen `GenerateMapsCommand.NotifyCanExecuteChanged()` triggern.

### Toolbar-Button (`MainWindow.xaml`)

In der linken Toolbar in einer eigenen Sektion „Export":

```xml
<TextBlock Style="{StaticResource DetailLabelText}" Text="EXPORT"/>
<Button Content="Karten generieren"
        Command="{Binding GenerateMapsCommand}"
        ToolTip="Erzeugt für jeden erkannten Stopp (Pause > 30 Min) eine Karte mit der Route bis dahin.
Ergebnis liegt im Unterordner 'maps/'."/>
```

`Strg+M` als Shortcut wäre charmant — optional via `KeyBinding` am Window (außerhalb der Galerie-Liste, weil hier kein Konflikt mit `1`/`2`/`L`/`R`/`S` besteht).

### App-Setup (DI)

In `App.xaml.cs`:

```csharp
services.AddHttpClient("tiles", c =>
{
    c.DefaultRequestHeaders.UserAgent.ParseAdd("TravelJournal/1.0 (kontakt@example.org)");
    c.Timeout = TimeSpan.FromSeconds(15);
});
services.AddSingleton<IMapRendererFactory, MapRendererFactory>();
```

## Tests in `TravelJournal.Core.Tests/MapRendering/`

### `WebMercatorTests`

- Brandenburger Tor (52.5163, 13.3777) bei Zoom 12 liefert erwartete Pixel-Werte (Referenzwerte aus OSM-Wiki ableiten und im Test fest verdrahten).
- `CalculateZoom` für eine bekannte Bounding-Box (z.B. 50 × 30 km) bei 1600×1200 liefert einen plausiblen Zoom (8–10).

### `StopDetectorTests`

- Drei Fotos, alle innerhalb 5 Min → 0 Stopps (außer Final-Summary aktiviert).
- Drei Fotos, zweite Lücke 45 Min → 1 Stopp am 2. Foto.
- Mit `AddFinalSummary = true` → genau ein zusätzlicher Stopp am letzten Foto.
- Fotos ohne GPS dazwischen werden ignoriert, lösen aber keinen Crash aus.

### `FileTileCacheTests`

- `Put` schreibt Datei am erwarteten Pfad, `TryGet` liest zurück, atomare Schreibweise (kein halbgeschriebenes File bei Throw).
- `TryGet` für nicht existierenden Schlüssel liefert `null`.

### `TileMapRendererTests` mit Fake `ITileSource`

- Fake liefert einfarbige 256×256-Tiles in unterscheidbarer Farbe pro `(x,y)`.
- 5 Fotos mit zwei erkannten Stopps + Final-Summary → 3 PNGs im Output-Ordner.
- Jedes PNG ist exakt 1600×1200.
- Dateinamen folgen `map_yyyy-MM-ddTHH-mm-ss.png`.
- `LastWriteTime` jedes PNGs entspricht dem Stopp-Timestamp (mit Toleranz ±2 s).

## Akzeptanzkriterien

- `dotnet build` warningfrei, alle Tests grün.
- WPF-Button „Karten generieren" startet einen Lauf mit Live-Fortschritt.
- Nach erfolgreichem Lauf liegt im Foto-Ordner `maps/` mit den PNGs.
- Alle PNGs haben dieselbe Größe (1600×1200) und denselben Kartenausschnitt — die Polyline wächst von Bild zu Bild.
- Sortiert man `<photofolder>` und `<photofolder>/maps/` zusammengelegt nach Datei-LastWriteTime, fügen sich die Karten chronologisch korrekt zwischen die Fotos.
- Ohne `MAPTILER_API_KEY` läuft alles trotzdem mit OSM-Tiles, mit Hinweis-Toast in der UI.
- Wiederholter Lauf für denselben Foto-Ordner ist deutlich schneller (Tile-Cache greift; nur PNG-Komposition läuft neu).
- Cancel-Button (oder erneuter Klick auf den Button bei `IsBusy`) bricht den Lauf sauber ab.

## Was bewusst NICHT teil dieser Iteration ist

- Animierte Polyline-Übergänge (statische PNGs reichen, die Animation passiert später im Web-Layer)
- Marker für Start-/Endpunkt mit „A"/„B"-Beschriftung (nice-to-have, später)
- Höhenprofil als Inset oder Sidebar
- Unterstützung für extreme Strecken über die Datumsgrenze hinweg
- Vektor-basierte Karten (MVT) statt PNG-Tiles
- Zwischenspeichern der Basis-Bitmap auf Platte (wird pro Lauf neu komponiert; ist mit Tile-Cache ausreichend schnell)
- Konfigurations-UI für `MapRenderingOptions` (vorerst Code-Konstanten + `appsettings.json`)
