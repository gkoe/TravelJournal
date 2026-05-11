# Claude Code Anweisung — Schritt 2: WPF-UI für das Build-Tool

## Ziel

Erweitere die bestehende Solution um ein WPF-Frontend `TravelJournal.Wpf`, das die Funktionen aus `TravelJournal.Core` über eine bedienbare Oberfläche zugänglich macht. Nutzer können einen Foto-Ordner öffnen, die enthaltenen Fotos mit Thumbnails und Metadaten sehen, jede Aufnahme als `Selected`/`Deselected`/`None` markieren, optional Title und Description eingeben, den Ordner neu scannen und die `tour.csv` schreiben.

## Kontext

Diese Iteration setzt auf der bestehenden Klassenbibliothek `TravelJournal.Core` auf (Modelle `Photo`/`PhotoState`, Services `ExifReaderService`, `TourCsvReader`, `TourCsvWriter`, `PhotoFolderScanner`). Es werden **keine Änderungen** an `TravelJournal.Core` vorgenommen außer den unten unter „Optionale Anpassungen in `TravelJournal.Core`" beschriebenen.

## Tech-Stack

- **.NET 10** (Target Framework `net10.0-windows`)
- **WPF** (`UseWPF=true`)
- **C# 13**, file-scoped namespaces, `Nullable` enabled, `ImplicitUsings` enabled
- **NuGet-Pakete:**
  - `CommunityToolkit.Mvvm` (`[ObservableProperty]`, `[RelayCommand]`)
  - `Microsoft.Extensions.DependencyInjection` (für die Service-Registrierung)
  - `Ookii.Dialogs.Wpf` (komfortabler Folder-Picker)

## Solution-Erweiterung

Lege im bestehenden `src/`-Verzeichnis ein neues Projekt an und füge es der Solution hinzu:

```
TravelJournal.sln
├── src/
│   ├── TravelJournal.Core/                  (existiert)
│   └── TravelJournal.Wpf/                   (NEU)
│       ├── TravelJournal.Wpf.csproj
│       ├── App.xaml / App.xaml.cs
│       ├── Views/
│       │   ├── MainWindow.xaml
│       │   └── MainWindow.xaml.cs
│       ├── ViewModels/
│       │   ├── MainViewModel.cs
│       │   └── PhotoViewModel.cs
│       ├── Services/
│       │   ├── IFolderDialogService.cs
│       │   ├── FolderDialogService.cs
│       │   ├── IThumbnailLoader.cs
│       │   └── ThumbnailLoader.cs
│       ├── Converters/
│       │   ├── PhotoStateToBrushConverter.cs
│       │   └── NullableDoubleToStringConverter.cs
│       └── Resources/
│           └── Styles.xaml
└── tests/
    └── TravelJournal.Core.Tests/            (existiert)
```

`TravelJournal.Wpf` referenziert `TravelJournal.Core` per Project-Reference.

## App-Setup (DI)

In `App.xaml.cs` einen `ServiceCollection`-Container aufbauen und im `OnStartup` befüllen:

- `ExifReaderService` — Singleton
- `TourCsvReader` — Singleton
- `TourCsvWriter` — Singleton
- `PhotoFolderScanner` — Singleton
- `IFolderDialogService` → `FolderDialogService` — Singleton
- `IThumbnailLoader` → `ThumbnailLoader` — Singleton
- `MainViewModel` — Transient
- `MainWindow` — Transient (bekommt `MainViewModel` per Konstruktor injiziert)

Im `OnStartup`: `MainWindow` aus dem Container auflösen und anzeigen. Kein `StartupUri` in `App.xaml`.

## Datenmodell der UI-Schicht

### `PhotoViewModel`

Wrapper um `Photo` mit `[ObservableProperty]` aus `CommunityToolkit.Mvvm`:

- Gebundene Eigenschaften: `State` (PhotoState), `Title` (string?), `Description` (string?)
- Read-only-Projektionen (Properties ohne Setter, aus dem zugrunde liegenden `Photo`): `Filename`, `DateTime`, `Latitude`, `Longitude`, `Altitude`
- Zusätzlich UI-Marker: `bool IsNew` (true wenn Foto im letzten Scan in `NewFilenames` war), `bool IsMissing` (true wenn in `MissingFilenames`)
- `ImageSource? Thumbnail` — wird vom `MainViewModel` über den `IThumbnailLoader` asynchron geladen und gesetzt
- `string FullPath` (intern, für Thumbnail-Loading)

Beim Setzen von `State`, `Title`, `Description` wird das umschlossene `Photo` synchron aktualisiert, damit der Save-Vorgang konsistent ist.

### `MainViewModel`

Eigenschaften:

- `ObservableCollection<PhotoViewModel> Photos` — Quelle der Galerie
- `ICollectionView FilteredPhotos` — gefilterte/sortierte View über `Photos` (siehe Filter unten)
- `PhotoViewModel? SelectedPhoto` — aktuell im Detail-Panel angezeigtes Foto
- `string? CurrentFolder` — Pfad des geöffneten Ordners (`null` wenn keiner)
- `PhotoFilter ActiveFilter` — Enum (siehe Filter)
- `string StatusText` — kompakte Status-Zeile
- `bool IsBusy` — Spinner-Anzeige während Scan/Save

Commands (alle als `[RelayCommand]`):

- `OpenFolderAsync` — Folder-Dialog → Ordner wählen → Scan starten
- `RescanAsync` — `PhotoFolderScanner.Scan(CurrentFolder)` erneut aufrufen, Status-Erhalt für bestehende Fotos
- `SaveCsvAsync` — `TourCsvWriter.Write` aufrufen, Pfad ist `Path.Combine(CurrentFolder, "tour.csv")`
- `SetStateSelected` (Parameter: `PhotoViewModel`) — setzt State auf `Selected`
- `SetStateDeselected` — setzt State auf `Deselected`
- `SetStateNone` — setzt State auf `None`
- `SetFilter` (Parameter: `PhotoFilter`) — wechselt aktiven Filter

Scan-Logik in der UI:

1. `IsBusy = true`, dann `Scanner.Scan(folder)` auf einem Background-Thread (`Task.Run`).
2. Ergebnis (`ScanResult`) auf den UI-Thread zurückbringen.
3. `Photos`-Collection neu befüllen (oder bei Re-Scan: bestehende `PhotoViewModel`-Instanzen mit gleichem Filename behalten und Properties aktualisieren, damit Bindings/Detail-Panel-Auswahl nicht springen).
4. `IsNew`/`IsMissing` aus `NewFilenames`/`MissingFilenames` setzen.
5. Thumbnails asynchron im Hintergrund nachladen (Galerie soll sofort responsiv sein).
6. `StatusText` aktualisieren: z.B. `"42 Fotos · 12 ausgewählt · 8 abgewählt · 22 offen · 4 neu · ~ 87,3 km"`.
7. `IsBusy = false`.

### `PhotoFilter` (enum)

```csharp
public enum PhotoFilter { All, Open, Selected, Deselected, New }
```

Anwendung in `FilteredPhotos.Filter`:

- `All` → alles zeigen
- `Open` → `State == None`
- `Selected` → `State == Selected`
- `Deselected` → `State == Deselected`
- `New` → `IsNew == true`

## Services

### `IFolderDialogService` / `FolderDialogService`

```csharp
public interface IFolderDialogService
{
    string? PickFolder(string? initialFolder = null);
}
```

Implementierung mit `Ookii.Dialogs.Wpf.VistaFolderBrowserDialog`.

### `IThumbnailLoader` / `ThumbnailLoader`

```csharp
public interface IThumbnailLoader
{
    Task<ImageSource> LoadAsync(string filePath, int decodePixelWidth = 240);
}
```

Implementierung mit `BitmapImage` und `DecodePixelWidth` (effizient, kein extra ImageSharp nötig). Bilder werden gefroren (`Freeze()`) damit sie über Threads nutzbar sind. Bei Lade-Fehlern wird ein Platzhalter (z.B. ein leeres `DrawingImage` oder eine Placeholder-PNG-Resource) zurückgegeben — keine Exception.

## Distanzberechnung

Im `MainViewModel` (oder einem kleinen Helper `RouteStatistics` in `TravelJournal.Wpf/Services/`) eine Methode, die aus den ausgewählten Fotos mit GPS-Daten die Gesamtstrecke per Haversine-Formel berechnet (Erdradius 6371 km, Ergebnis in km mit einer Nachkommastelle). Wird in `StatusText` eingebunden.

## Layout `MainWindow.xaml`

Drei-Spalten-`Grid` mit `ColumnDefinitions="320,*,360"`. Optional: Splitter zwischen den Spalten.

### Linke Spalte (320px)

`StackPanel` mit:

- `Image` Logo/Header (optional, kann später kommen)
- Toolbar als vertikales `StackPanel` mit Buttons:
  - `Ordner öffnen…` → `OpenFolderCommand`
  - `Neu scannen` → `RescanCommand` (disabled wenn `CurrentFolder == null`)
  - `CSV speichern` → `SaveCsvCommand` (disabled wenn `CurrentFolder == null`)
- Trenner
- Filter-Buttons als `ToggleButton`-Gruppe (oder `ListBox` mit `ItemTemplate`):
  - Alle / Offen / Ausgewählt / Abgewählt / Neu
  - Aktiver Filter visuell hervorgehoben
- Trenner
- `TextBlock` `CurrentFolder` (mehrzeilig, mit Tooltip auf vollständigen Pfad)

### Mittlere Spalte (Galerie)

`ItemsControl` mit `ItemsSource="{Binding FilteredPhotos}"`, `ItemsPanel` ist ein `WrapPanel`. `ScrollViewer` außen herum, `VerticalScrollBarVisibility="Auto"`.

`ItemTemplate` pro `PhotoViewModel`:

- `Border` mit Rand (Farbe abhängig vom State per `PhotoStateToBrushConverter`):
  - `None` → hellgrau
  - `Selected` → grün
  - `Deselected` → rot
- `Grid` mit:
  - `Image` (Thumbnail, ~200×150)
  - Overlay rechts oben: kleine `TextBlock`-Badges für „NEU" (wenn `IsNew`) und „FEHLT" (wenn `IsMissing`)
  - Overlay links unten: kleines State-Icon (Häkchen/X/Kreis)
  - Overlay rechts unten: kompaktes DateTime (`HH:mm`)
- `MouseLeftButtonDown` setzt `SelectedPhoto = this` (per Behavior oder Command)
- `MouseRightButtonDown` öffnet ein `ContextMenu` mit „Auswählen / Abwählen / Offen"

### Rechte Spalte (Detail-Panel)

Zeigt `SelectedPhoto`. Wenn `null`: Hinweis-Text „Foto in der Galerie auswählen". Sonst:

- Großes Vorschaubild (gebunden an dasselbe Thumbnail oder optional eine größere Variante)
- Metadaten als read-only `TextBlock`-Block: Dateiname, Datum/Uhrzeit, Lat/Lon (formatiert auf 6 Nachkommastellen), Höhe in m
- Drei `RadioButton`s für `State` (mit `EnumToBoolConverter` oder ähnlichem Pattern)
- `TextBox Title` (single-line)
- `TextBox Description` (multi-line, `AcceptsReturn=True`, `TextWrapping=Wrap`, `MinHeight=80`)
- Tastatur-Hinweise als kleine graue Hilfe-Zeile

### Statusleiste unten

`StatusBar` über alle drei Spalten mit `StatusText`.

## Tastatur-Shortcuts

In `MainWindow.xaml.cs` über `InputBindings` registrieren — solange das Detail-Panel **nicht** den Fokus hat (z.B. `KeyDown`-Event auf der Galerie):

- `1` → `SetStateSelected` für `SelectedPhoto`
- `2` → `SetStateDeselected`
- `0` oder `Backspace` → `SetStateNone`
- `Pfeil links/rechts` → vorheriges/nächstes Foto in `FilteredPhotos` selektieren
- `Strg+S` → `SaveCsvCommand`
- `F5` → `RescanCommand`

## Style / Resources

`Resources/Styles.xaml` mit dezenten Farben:

- Hintergrund hellgrau (#F5F5F5)
- Akzentfarbe Bahnblau o.ä. (#1E5F8E)
- State-Farben: Grün (#2E7D32), Rot (#C62828), Grau (#9E9E9E)
- Schrift: Segoe UI

`MergeDictionaries` in `App.xaml`. Konsistente Padding-/Margin-Werte (z.B. 8/16/24).

## Optionale Anpassungen in `TravelJournal.Core`

Falls beim Implementieren auffällt, dass `PhotoFolderScanner.ScanResult.Photos` keine stabile Referenzgleichheit beim Re-Scan bietet (was die UI-Auswahl springen lassen könnte), darf der Scanner um eine Überladung erweitert werden:

```csharp
ScanResult Scan(string folderPath, IEnumerable<Photo>? existing = null);
```

— die bei gleichen Dateinamen die bestehenden `Photo`-Instanzen wiederverwendet und nur die Felder aktualisiert. Tests für diese Überladung in `TravelJournal.Core.Tests` ergänzen.

## Akzeptanzkriterien

- `dotnet build` läuft warningfrei.
- `dotnet run --project src/TravelJournal.Wpf` startet das Fenster.
- Folder-Picker funktioniert; ein Test-Ordner mit JPGs (mit oder ohne EXIF) wird ohne Crash eingelesen.
- Thumbnails erscheinen in der Galerie.
- Klick auf ein Foto zeigt Details rechts.
- State-Buttons und Tastatur-Shortcuts ändern den Status sichtbar (Rahmenfarbe).
- `CSV speichern` legt eine `tour.csv` im Ordner an, die in Excel sauber öffnet.
- `Neu scannen` nach Hinzufügen eines neuen JPGs zeigt dieses mit „NEU"-Badge an, ohne bestehende Status zu verlieren.
- Filter-Buttons reduzieren die Galerie korrekt.
- Statuszeile zeigt Counts und Gesamtdistanz.

## Was bewusst NICHT teil dieser Iteration ist

- Web-Präsentation (Schritt 3)
- Bildoptimierung
- Day/Order-Override-UI
- Drag&Drop-Sortierung
- Lokalisierung über Resource-Files (UI-Texte sind hartkodiert deutsch)
- Tests für die WPF-UI (kommen erst wenn die UI stabil ist)
