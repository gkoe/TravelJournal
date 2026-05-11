# Claude Code Anweisung — Schritt 8: Karten-Style, Sprache und Padding konfigurierbar

## Ziel

Drei Karten-Eigenschaften, die bisher hartkodiert waren, werden konfigurierbar — sowohl persistent über `appsettings.json` als auch dynamisch über die WPF-Oberfläche, damit verschiedene Stile schnell ausprobiert werden können, ohne neu zu kompilieren:

1. **Style-ID** des MapTiler-Maps (z.B. `outdoor-v2`, `streets-v2`, `topo-v2`).
2. **Sprache** der Ortsbeschriftungen (z.B. `de`, `en`, `fr`).
3. **BoundsPaddingFraction** — der relative Rand um die Bounding-Box (z.B. `0.12` = 12 %).

Zusätzlich wird der Default-Style von `streets-v2` auf **`outdoor-v2`** gewechselt — als sinnvolle Vorbelegung für Radreisen.

## Kontext

Setzt auf Schritt 1–7 auf. Hauptbetroffene Dateien: `TravelJournal.Core/MapRendering/MapRenderingOptions.cs`, `MapTilerTileSource.cs`, neue Klasse `MapTilerStyles.cs`, `TravelJournal.Wpf/Services/MapRendererFactory.cs`, `TravelJournal.Wpf/ViewModels/MainViewModel.cs`, `TravelJournal.Wpf/Views/MainWindow.xaml`.

---

## Änderung 1 — `MapRenderingOptions` erweitern

```csharp
public sealed class MapRenderingOptions
{
    // bestehend
    public int TargetWidthPx { get; init; } = 1600;
    public int TargetHeightPx { get; init; } = 1200;
    public TimeSpan StopThreshold { get; init; } = TimeSpan.FromMinutes(30);
    public int MaxParallelTileDownloads { get; init; } = 4;
    public bool AddFinalSummaryMap { get; init; } = true;
    public string? MapTilerApiKey { get; init; }

    // angepasst
    public double BoundsPaddingFraction { get; init; } = 0.12;

    // neu
    public string StyleId { get; init; } = "outdoor-v2";
    public string Language { get; init; } = "de";
    public string? CustomTileUrlTemplate { get; init; }   // optional, für Power-User
}
```

`CustomTileUrlTemplate` erlaubt fortgeschrittenen Nutzern, einen vollständigen URL-Bauplan einzugeben (mit Platzhaltern `{z}`, `{x}`, `{y}` und optional `{key}`/`{lang}`). Wenn gesetzt, wird `StyleId`/`Language`/`MapTilerApiKey` für die URL-Konstruktion ignoriert. Default ist `null` — der normale MapTiler-Pfad.

---

## Änderung 2 — Bekannte Styles als Konstanten

Neue Datei `TravelJournal.Core/MapRendering/MapTilerStyles.cs`:

```csharp
public static class MapTilerStyles
{
    /// <summary>
    /// Kuratierte Liste der für Reise- und Tour-Karten am besten geeigneten MapTiler-Styles.
    /// Schlüssel ist die Style-ID, die in der Tile-URL eingesetzt wird.
    /// </summary>
    public static readonly IReadOnlyList<MapStyleInfo> Curated = new List<MapStyleInfo>
    {
        new("outdoor-v2",  "Outdoor",  "Wandern/Radfahren, Höhenlinien, sichtbare Wege"),
        new("streets-v2",  "Streets",  "Generischer Google-Maps-Look, gut in Städten"),
        new("topo-v2",     "Topo",     "Topografisch, prominente Höhenangaben"),
        new("voyager",     "Voyager",  "Klar und reduziert, hebt Polyline gut hervor"),
        new("bright",      "Bright",   "Kontraststark mit hellen Farben"),
        new("basic-v2",    "Basic",    "Minimalistisch"),
        new("backdrop",    "Backdrop", "Sehr dezent, ideal als Daten-Hintergrund"),
    };

    public static readonly IReadOnlyList<string> SupportedLanguages =
        new[] { "de", "en", "fr", "it", "es", "nl" };
}

public sealed record MapStyleInfo(string Id, string Name, string Description)
{
    public string DisplayLabel => $"{Name} — {Description}";
}
```

Diese Liste ist kuratiert (nicht erschöpfend) — wer eine andere Style-ID kennt (z.B. einen eigenen Style aus MapTiler Cloud Studio), kann sie direkt in `appsettings.json` eintragen oder über `CustomTileUrlTemplate` einsetzen.

---

## Änderung 3 — `MapTilerTileSource` baut URL aus Optionen

```csharp
public sealed class MapTilerTileSource : ITileSource
{
    private readonly HttpClient _http;
    private readonly MapRenderingOptions _options;

    public MapTilerTileSource(HttpClient http, MapRenderingOptions options)
    {
        _http = http;
        _options = options;
        if (string.IsNullOrEmpty(options.CustomTileUrlTemplate)
            && string.IsNullOrEmpty(options.MapTilerApiKey))
        {
            throw new ArgumentException("MapTilerApiKey is required when CustomTileUrlTemplate is not set.");
        }
    }

    public string ProviderId =>
        !string.IsNullOrEmpty(_options.CustomTileUrlTemplate)
            ? "maptiler-custom"
            : $"maptiler-{_options.StyleId}-{_options.Language}";

    public string AttributionText => "© MapTiler © OpenStreetMap contributors";

    private string BuildUrl(MapTile tile)
    {
        if (!string.IsNullOrEmpty(_options.CustomTileUrlTemplate))
        {
            return _options.CustomTileUrlTemplate
                .Replace("{z}", tile.Z.ToString())
                .Replace("{x}", tile.X.ToString())
                .Replace("{y}", tile.Y.ToString())
                .Replace("{key}", _options.MapTilerApiKey ?? "")
                .Replace("{lang}", _options.Language ?? "");
        }

        var url = $"https://api.maptiler.com/maps/{_options.StyleId}/256/{tile.Z}/{tile.X}/{tile.Y}.png?key={_options.MapTilerApiKey}";
        if (!string.IsNullOrEmpty(_options.Language))
            url += $"&lang={_options.Language}";
        return url;
    }

    public async Task<byte[]> GetTileAsync(MapTile tile, CancellationToken ct)
    {
        // … BuildUrl + HTTP GET + Retry-Behandlung wie in Schritt 6 …
    }
}
```

Wichtig: `ProviderId` enthält jetzt `StyleId` und `Language` — damit speichert der `FileTileCache` die Tiles je Style/Sprache **getrennt** ab. Ein Wechsel auf einen anderen Style invalidiert den bisherigen Cache nicht und führt nicht zu falsch gemischten Bildern.

---

## Änderung 4 — `appsettings.json` erweitern

```jsonc
{
  "MapTiler": {
    "ApiKey": "DEIN_KEY_HIER"
  },
  "MapRendering": {
    "StyleId": "outdoor-v2",
    "Language": "de",
    "BoundsPaddingFraction": 0.12,
    "TargetWidthPx": 1600,
    "TargetHeightPx": 1200,
    "StopThresholdMinutes": 30,
    "AddFinalSummaryMap": true,
    "CustomTileUrlTemplate": null
  }
}
```

`appsettings.example.json` (im Repo, ohne Key) entsprechend ergänzen, damit andere Mitwirkende sehen, was konfigurierbar ist.

`MapRendererFactory` (aus Schritt 6) liest die Sektion `MapRendering` und befüllt `MapRenderingOptions` daraus, mit den Defaults aus der Klasse als Fallback.

---

## Änderung 5 — UI-Konfiguration in der WPF-Toolbar

In der „EXPORT"-Sektion der linken Toolbar (über dem „Karten generieren"-Button) eine kompakte Konfigurationszone:

```xml
<TextBlock Style="{StaticResource DetailLabelText}" Text="KARTEN-STIL"/>
<ComboBox ItemsSource="{Binding AvailableMapStyles}"
          SelectedValuePath="Id"
          DisplayMemberPath="DisplayLabel"
          SelectedValue="{Binding SelectedMapStyleId, Mode=TwoWay}"
          ToolTip="Wirkt sich auf neu generierte Karten aus."
          Margin="0,2,0,8"/>

<TextBlock Style="{StaticResource DetailLabelText}" Text="SPRACHE"/>
<ComboBox ItemsSource="{Binding AvailableLanguages}"
          SelectedItem="{Binding SelectedLanguage, Mode=TwoWay}"
          ToolTip="Sprache der Ortsbeschriftungen auf der Karte."
          Margin="0,2,0,8"/>

<TextBlock Style="{StaticResource DetailLabelText}">
  <Run Text="RAND"/>
  <Run Text="{Binding BoundsPaddingPercent, StringFormat='({0} %)'}"/>
</TextBlock>
<Slider Minimum="0" Maximum="40" TickFrequency="2" IsSnapToTickEnabled="True"
        Value="{Binding BoundsPaddingPercent, Mode=TwoWay}"
        ToolTip="Wie viel freier Rand um die Route herum dargestellt wird."
        Margin="0,2,0,12"/>

<Button Content="Karten generieren"
        Command="{Binding GenerateMapsCommand}"
        ToolTip="Erzeugt für jeden erkannten Stopp eine Karte mit den aktuellen Einstellungen."/>
```

Sowohl die Style- als auch die Sprach-Auswahl wirken **erst beim nächsten Generieren-Lauf** — bestehende Karten im Foto-Ordner werden nicht automatisch neu erzeugt. Optional einen Tooltip-Hinweis darauf, oder die Buttontext-Variante „Karten neu generieren" wenn schon Karten im Ordner liegen.

---

## Änderung 6 — `MainViewModel` erweitern

```csharp
public IReadOnlyList<MapStyleInfo> AvailableMapStyles => MapTilerStyles.Curated;
public IReadOnlyList<string> AvailableLanguages => MapTilerStyles.SupportedLanguages;

[ObservableProperty]
private string selectedMapStyleId = "outdoor-v2";

[ObservableProperty]
private string selectedLanguage = "de";

[ObservableProperty]
private int boundsPaddingPercent = 12;
```

Beim Konstruktor-Aufruf werden diese drei Werte aus den `MapRenderingOptions` (die ihrerseits aus `appsettings.json` und User-Settings gespeist sind) initialisiert — so spiegelt die UI immer die aktuelle Konfiguration.

In `GenerateMapsAsync` wird vor dem Renderer-Call die Konfiguration aus den UI-Werten gebaut:

```csharp
var options = _baseOptions with
{
    StyleId = SelectedMapStyleId,
    Language = SelectedLanguage,
    BoundsPaddingFraction = BoundsPaddingPercent / 100.0,
};
var renderer = _mapRendererFactory.Create(CurrentFolder!, options);
```

`MapRendererFactory.Create` bekommt also eine zweite Überladung mit explizitem `MapRenderingOptions`-Parameter, die der UI-Pfad nutzt; die parameterlose Variante verwendet weiterhin die Default-Konfiguration.

`MapRenderingOptions` muss dafür `record` sein oder einen Copy-Constructor haben, damit `with` funktioniert. Empfehlung: in `record class` umwandeln.

---

## Änderung 7 — User-Settings persistieren

Damit die UI-Auswahl zwischen Sitzungen erhalten bleibt, ohne `appsettings.json` (versionierbarer Default) zu überschreiben:

Neue Datei `TravelJournal.Wpf/Services/UserSettingsService.cs`:

```csharp
public sealed class UserSettingsService
{
    private readonly string _path;

    public UserSettingsService()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var dir = Path.Combine(appData, "TravelJournal");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "user-settings.json");
    }

    public UserSettings Load();
    public void Save(UserSettings settings);
}

public sealed record UserSettings(
    string MapStyleId,
    string Language,
    int BoundsPaddingPercent
);
```

`MainViewModel` ruft `Save(...)` jeweils nach Änderung einer der drei Werte auf (`partial void OnSelectedMapStyleIdChanged` etc.) und beim Konstruktor `Load()` für die Initialwerte. Falls `user-settings.json` fehlt, fallback auf die Werte aus `appsettings.json`.

---

## Änderung 8 — Tile-Cache je Style/Sprache trennen

Bereits durch die Änderung in `ProviderId` automatisch gelöst — jeder Style/Sprache-Wechsel landet in einem eigenen Unterordner unter `<photofolder>/.tile-cache/`. Beispiel:

```
.tile-cache/
├── maptiler-outdoor-v2-de/
│   └── 11/1234/567.png
├── maptiler-outdoor-v2-en/
├── maptiler-streets-v2-de/
└── maptiler-topo-v2-de/
```

Alte Cache-Daten bleiben gültig. Nutzer können den Cache-Ordner gefahrlos manuell löschen, wenn er zu groß wird; beim nächsten Lauf werden die nötigen Tiles neu gezogen.

---

## Tests

In `TravelJournal.Core.Tests/MapRendering/MapTilerTileSourceTests.cs` (mit gemocktem `HttpClient` via `HttpMessageHandler`):

- URL-Bau für `streets-v2` + `de` ergibt `https://api.maptiler.com/maps/streets-v2/256/{z}/{x}/{y}.png?key=...&lang=de`.
- URL-Bau ohne `Language` lässt `&lang=` weg.
- URL-Bau mit `CustomTileUrlTemplate` ersetzt alle Platzhalter korrekt und ignoriert `StyleId`/`Language`.
- `ProviderId` ändert sich beim Wechsel von `streets-v2` auf `outdoor-v2`.
- `ProviderId` für Custom Template ist `"maptiler-custom"`.

In `TravelJournal.Wpf` (manuell verifizierbar, keine Auto-Tests nötig):

- ComboBox-Auswahl wird in `user-settings.json` gespeichert und beim nächsten App-Start wieder geladen.

---

## Akzeptanzkriterien

- `appsettings.json` enthält die neue `MapRendering`-Sektion und wird beim Start korrekt eingelesen.
- Der Default-Style ist `outdoor-v2` mit `lang=de`. Erste generierte Karte zeigt deutsche Ortsnamen und ein Outdoor-typisches Erscheinungsbild (Höhenlinien, Wege).
- In der WPF-Toolbar gibt es eine ComboBox für den Style, eine ComboBox für die Sprache und einen Slider für das Padding (0–40 %).
- Wechsel des Styles in der UI und anschließendes „Karten generieren" erzeugt PNGs mit dem neuen Style; bestehende PNGs werden überschrieben.
- Der Tile-Cache hat getrennte Unterordner pro Style/Sprache — nach Wechsel und erneutem Generieren ist ein neuer Unterordner sichtbar, der bisherige bleibt erhalten.
- `user-settings.json` im AppData-Ordner persistiert die letzte Auswahl; nach Neustart sind die Werte wieder vorbelegt.
- Custom Style URL via `CustomTileUrlTemplate` in `appsettings.json` funktioniert (z.B. mit einem in MapTiler Cloud Studio erstellten Style).

---

## Was bewusst NICHT teil dieser Iteration ist

- Live-Vorschau des Karten-Stils, ohne Generieren-Lauf
- Eingabefeld in der UI für eine eigene Style-ID (geht über `appsettings.json` per Hand)
- Weitere MapTiler-Optionen (z.B. `tileSize`, `marker_overlay`)
- Validierung der Style-ID gegen die MapTiler-API zur Laufzeit (falsche IDs liefern HTTP 404, Fehlermeldung in der UI ist ausreichend)
- Cache-Größen-Begrenzung oder automatische Bereinigung
