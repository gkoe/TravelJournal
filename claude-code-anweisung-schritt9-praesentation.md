# Claude Code Anweisung — Schritt 9: Präsentation direkt aus dem WPF-Tool

## Ziel

Eine erste, vollwertige Vorführung der TravelJournal direkt aus dem WPF-Build-Tool — ohne separaten Web-Layer, ohne Export. Auf Knopfdruck öffnet sich ein Vollbild-Fenster, das **mit dem Startbild beginnt**, dann in chronologischer Reihenfolge die ausgewählten Fotos und die zuvor generierten Karten abspielt und **mit dem Endbild abschließt**:

- **Pro Item 5 Sekunden** Anzeigedauer.
- **Bei Fotos** wird in den ersten 2 Sekunden am unteren Bildrand eine gut lesbare Info-Einblendung gezeigt: Ortsname, Wochentag, Datum, Uhrzeit. Danach blendet sie aus, das Foto bleibt allein stehen. Fotos ohne `DateTime` (z.B. ein synthetisches Startbild) zeigen kein Overlay.
- **Bei Karten** keine Überlagerung — die Karte spricht für sich.
- **`Esc` bricht** die Präsentation jederzeit ab.

### Rolle der State-Werte

`PhotoState` (in `TravelJournal.Core`) ist um zwei Werte erweitert worden:

```
0 = None         (offen)
1 = Selected     (Teil der Diashow im chronologischen Block)
2 = Deselected   (nicht in der Diashow)
3 = Start        (Startbild — wird immer als erstes Item gezeigt)
4 = End          (Endbild — wird immer als letztes Item gezeigt)
```

Start- und End-Fotos sind „Sonderrollen". Sie tauchen in der Präsentations-Playlist unabhängig von ihrer `DateTime` an festen Positionen auf — das Startbild zuerst, das Endbild zuletzt — und werden **nicht** chronologisch zwischen die anderen Items eingeordnet.

## Kontext

Setzt auf Schritt 1–8 auf. Nutzt die ausgewählten `Photo`-Objekte und die `MapItem`-Einträge, die `PhotoFolderScanner` aus dem Foto-Ordner liest. Vor dem Start muss „Karten generieren" mindestens einmal gelaufen sein, sonst besteht die Präsentation nur aus Fotos — was zulässig ist.

## Architektur-Überblick

Eine kleine, in sich geschlossene Erweiterung:

```
src/TravelJournal.Wpf/
├── Views/
│   └── PresentationWindow.xaml          (NEU, Vollbild-Fenster)
├── ViewModels/
│   └── PresentationViewModel.cs         (NEU)
└── Models/
    ├── IPresentationItem.cs             (NEU)
    ├── PhotoPresentationItem.cs         (NEU)
    └── MapPresentationItem.cs           (NEU)
```

`MainViewModel` bekommt einen neuen Command `StartPresentationCommand`, der die Playlist baut und das `PresentationWindow` als modales Vollbild-Fenster öffnet.

---

## Datenmodelle

### `IPresentationItem`

```csharp
public interface IPresentationItem
{
    DateTime EffectiveDateTime { get; }
    string FullPath { get; }
    PresentationOverlay? Overlay { get; }   // null bei Karten
}

public sealed record PresentationOverlay(
    string? Location,        // optional
    DayOfWeek DayOfWeek,
    DateTime LocalDateTime
);
```

### `PhotoPresentationItem`

Wrapper um `Photo`. `EffectiveDateTime` = `Photo.DateTime ?? DateTime.MinValue` (Fotos ohne DateTime werden trotzdem aufgenommen, da sie als Start- oder End-Fotos dienen können — die Sortierung umgeht sie über die Sonderrollen-Logik).

`Overlay` ist **nur dann** befüllt, wenn `Photo.DateTime` einen Wert hat — also `Location`, `DayOfWeek` und `LocalDateTime`. Bei fehlendem DateTime ist `Overlay` `null` (kein Overlay angezeigt). So funktioniert ein Startbild ohne Metadaten als reine Titel-Karte.

### `MapPresentationItem`

Wrapper um `MapItem`. `EffectiveDateTime` = `MapItem.DateTime`. `Overlay` ist immer `null`.

---

## Playlist bauen

In `MainViewModel.StartPresentationAsync`:

```csharp
[RelayCommand(CanExecute = nameof(CanStartPresentation))]
private void StartPresentation()
{
    // 1) Startbilder (State == Start, typisch genau eins) — kommen immer ganz vorne
    var startItems = Photos
        .Where(p => p.State == PhotoState.Start)
        .OrderBy(p => p.DateTime ?? DateTime.MinValue)
        .ThenBy(p => p.Filename)
        .Select(p => new PhotoPresentationItem(p.UnderlyingPhoto))
        .Cast<IPresentationItem>()
        .ToList();

    // 2) Hauptblock: chronologisch ausgewählte Fotos und alle Karten
    var middleItems = new List<IPresentationItem>();
    middleItems.AddRange(Photos
        .Where(p => p.State == PhotoState.Selected && p.DateTime is not null)
        .Select(p => new PhotoPresentationItem(p.UnderlyingPhoto)));
    middleItems.AddRange(Maps
        .Select(m => new MapPresentationItem(m.UnderlyingMap)));
    middleItems = middleItems.OrderBy(i => i.EffectiveDateTime).ToList();

    // 3) Endbilder (State == End, typisch genau eins) — kommen immer ganz hinten
    var endItems = Photos
        .Where(p => p.State == PhotoState.End)
        .OrderBy(p => p.DateTime ?? DateTime.MaxValue)
        .ThenBy(p => p.Filename)
        .Select(p => new PhotoPresentationItem(p.UnderlyingPhoto))
        .Cast<IPresentationItem>()
        .ToList();

    var playlist = startItems.Concat(middleItems).Concat(endItems).ToList();
    if (playlist.Count == 0) return;

    var presentationVm = new PresentationViewModel(playlist);
    var window = new PresentationWindow(presentationVm);
    window.Owner = Application.Current.MainWindow;
    window.ShowDialog();   // modal — Hauptfenster bleibt im Hintergrund
}

private bool CanStartPresentation() =>
    Photos.Any(p => p.State is PhotoState.Selected or PhotoState.Start or PhotoState.End)
    || Maps.Any();
```

Wichtig: die drei Listen werden **nicht** wieder zusammen sortiert — die Reihenfolge `Start → Middle → End` bleibt durch die `Concat`-Reihenfolge zwingend erhalten, auch wenn ein Endbild zeitlich früher liegen sollte als das letzte Item im Mittelblock (was selten, aber denkbar ist).

Toolbar-Button (in der Sektion „EXPORT", über oder unter „Karten generieren"):

```xml
<Button Content="Präsentation starten"
        Command="{Binding StartPresentationCommand}"
        ToolTip="Spielt ausgewählte Fotos und generierte Karten in chronologischer Reihenfolge im Vollbild ab.
Esc beendet die Präsentation."/>
```

Tastatur-Shortcut `F5` wäre intuitiv für „Präsentation starten" — kollidiert aber mit dem `Rescan`-Shortcut aus Schritt 2. Vorschlag: stattdessen `F11` (Vollbild-Idiom) oder `Strg+P`.

---

## `PresentationWindow.xaml`

Vollbild-Fenster ohne Chrome:

```xml
<Window x:Class="TravelJournal.Wpf.Views.PresentationWindow"
        WindowStyle="None"
        WindowState="Maximized"
        ResizeMode="NoResize"
        Background="Black"
        Cursor="None"
        ShowInTaskbar="False"
        Topmost="True">
  <Window.InputBindings>
    <KeyBinding Key="Escape" Command="{Binding StopCommand}"/>
    <KeyBinding Key="Space" Command="{Binding TogglePauseCommand}"/>
    <KeyBinding Key="Right" Command="{Binding NextCommand}"/>
    <KeyBinding Key="Left" Command="{Binding PreviousCommand}"/>
  </Window.InputBindings>

  <Grid>
    <!-- Aktuelles Bild, voll skaliert -->
    <Image x:Name="CurrentImage"
           Source="{Binding CurrentImageSource}"
           Stretch="Uniform"/>

    <!-- Überblend-Image für Cross-Fade (kommt im Vordergrund mit Opacity-Animation) -->
    <Image x:Name="NextImage"
           Source="{Binding NextImageSource}"
           Stretch="Uniform"
           Opacity="0"/>

    <!-- Info-Overlay unten -->
    <Border x:Name="OverlayPanel"
            VerticalAlignment="Bottom"
            HorizontalAlignment="Stretch"
            Padding="48,32"
            Opacity="{Binding OverlayOpacity}">
      <Border.Background>
        <LinearGradientBrush StartPoint="0,0" EndPoint="0,1">
          <GradientStop Color="#00000000" Offset="0"/>
          <GradientStop Color="#CC000000" Offset="1"/>
        </LinearGradientBrush>
      </Border.Background>
      <StackPanel>
        <TextBlock Text="{Binding OverlayLocation}"
                   FontSize="44"
                   FontWeight="SemiBold"
                   Foreground="White"
                   Visibility="{Binding OverlayLocationVisibility}"
                   Margin="0,0,0,8"/>
        <TextBlock Text="{Binding OverlayDateTime}"
                   FontSize="26"
                   Foreground="#EEEEEE"/>
      </StackPanel>
    </Border>

    <!-- Sehr dezenter Stop-Hinweis oben (ein paar Sekunden lang) -->
    <TextBlock Text="Esc: Präsentation beenden"
               VerticalAlignment="Top"
               HorizontalAlignment="Right"
               Margin="32"
               FontSize="14"
               Foreground="#80FFFFFF"
               Opacity="{Binding HintOpacity}"/>
  </Grid>
</Window>
```

`Cursor="None"` versteckt den Mauszeiger im Vollbild — eine kleine Politur, die viel ausmacht. Bei Mausbewegung könnte er kurz wieder erscheinen (optional, nicht zwingend).

---

## `PresentationViewModel`

Verantwortlichkeiten:

- Playlist halten und Index führen.
- Zwei Timer steuern: einen für Item-Wechsel (5 s), einen für das Ausblenden des Overlays (2 s).
- Bilder asynchron laden, damit es keine Hänger gibt.
- Nächstes Bild vorab laden für nahtlose Übergänge.
- ESC/Space/Pfeil-Navigation.

```csharp
public sealed partial class PresentationViewModel : ObservableObject
{
    private readonly IReadOnlyList<IPresentationItem> _playlist;
    private readonly DispatcherTimer _itemTimer;
    private readonly DispatcherTimer _overlayTimer;

    private static readonly TimeSpan ItemDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan OverlayVisibleDuration = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan HintFadeDuration = TimeSpan.FromSeconds(3);

    [ObservableProperty] private int currentIndex;
    [ObservableProperty] private ImageSource? currentImageSource;
    [ObservableProperty] private ImageSource? nextImageSource;
    [ObservableProperty] private string? overlayLocation;
    [ObservableProperty] private string? overlayDateTime;
    [ObservableProperty] private double overlayOpacity;
    [ObservableProperty] private double hintOpacity = 1.0;
    [ObservableProperty] private bool isPaused;

    public Visibility OverlayLocationVisibility =>
        string.IsNullOrEmpty(OverlayLocation) ? Visibility.Collapsed : Visibility.Visible;

    public event EventHandler? RequestClose;

    public PresentationViewModel(IReadOnlyList<IPresentationItem> playlist)
    {
        _playlist = playlist;
        _itemTimer = new DispatcherTimer { Interval = ItemDuration };
        _itemTimer.Tick += (_, _) => Next();
        _overlayTimer = new DispatcherTimer { Interval = OverlayVisibleDuration };
        _overlayTimer.Tick += (_, _) => HideOverlay();
    }

    public async Task StartAsync()
    {
        await ShowItemAsync(0);
        _itemTimer.Start();
        FadeOutHint();
    }

    private async Task ShowItemAsync(int index)
    {
        if (index < 0 || index >= _playlist.Count)
        {
            RequestClose?.Invoke(this, EventArgs.Empty);
            return;
        }

        CurrentIndex = index;
        var item = _playlist[index];
        CurrentImageSource = await LoadImageAsync(item.FullPath);
        UpdateOverlay(item);
        await PreloadNextAsync();
    }

    private void UpdateOverlay(IPresentationItem item)
    {
        if (item.Overlay is null)
        {
            OverlayOpacity = 0;
            OverlayLocation = null;
            OverlayDateTime = null;
            return;
        }

        var de = new CultureInfo("de-DE");
        OverlayLocation = item.Overlay.Location;
        OverlayDateTime = item.Overlay.LocalDateTime.ToString(
            "dddd, d. MMMM yyyy · HH:mm", de);
        OnPropertyChanged(nameof(OverlayLocationVisibility));
        OverlayOpacity = 1.0;
        _overlayTimer.Stop();
        _overlayTimer.Start();
    }

    private void HideOverlay()
    {
        _overlayTimer.Stop();
        // Sanftes Ausblenden via DoubleAnimation (siehe unten)
        AnimateOverlayTo(0.0);
    }

    [RelayCommand]
    private void Next() => _ = ShowItemAsync(CurrentIndex + 1);

    [RelayCommand]
    private void Previous() => _ = ShowItemAsync(Math.Max(0, CurrentIndex - 1));

    [RelayCommand]
    private void TogglePause()
    {
        if (IsPaused) { _itemTimer.Start(); IsPaused = false; }
        else          { _itemTimer.Stop();  IsPaused = true;  }
    }

    [RelayCommand]
    private void Stop()
    {
        _itemTimer.Stop();
        _overlayTimer.Stop();
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    private static async Task<ImageSource> LoadImageAsync(string path)
    {
        return await Task.Run(() =>
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(path);
            bmp.DecodePixelWidth = 1920;     // Vollbild-tauglich, spart Speicher
            bmp.EndInit();
            bmp.Freeze();
            return (ImageSource)bmp;
        });
    }

    private async Task PreloadNextAsync()
    {
        var nextIndex = CurrentIndex + 1;
        if (nextIndex >= _playlist.Count) { NextImageSource = null; return; }
        NextImageSource = await LoadImageAsync(_playlist[nextIndex].FullPath);
    }

    private void FadeOutHint() { /* DoubleAnimation auf HintOpacity 0, Duration HintFadeDuration */ }
    private void AnimateOverlayTo(double targetOpacity) { /* DoubleAnimation auf OverlayOpacity */ }
}
```

`AnimateOverlayTo` und `FadeOutHint` lassen sich am elegantesten direkt im Code-Behind des `PresentationWindow` als Storyboard-Animationen umsetzen, weil `DoubleAnimation` ein UIElement-Ziel braucht. Alternativ direkt im XAML als `Storyboard` mit `BeginStoryboard`-Trigger auf einer geänderten Property — aber das ViewModel-direkt-Ansprechen ist hier einfacher.

Empfehlung: im Code-Behind von `PresentationWindow.xaml.cs`:

```csharp
public partial class PresentationWindow : Window
{
    public PresentationWindow(PresentationViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        vm.RequestClose += (_, _) => Close();
        vm.PropertyChanged += OnVmPropertyChanged;
        Loaded += async (_, _) => await vm.StartAsync();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PresentationViewModel.OverlayOpacity))
            AnimateOpacity(OverlayPanel, ((PresentationViewModel)DataContext).OverlayOpacity);
        // analog für HintOpacity
    }

    private static void AnimateOpacity(UIElement target, double to, double durationMs = 400)
    {
        var anim = new DoubleAnimation(to, TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseInOut }
        };
        target.BeginAnimation(UIElement.OpacityProperty, anim);
    }
}
```

---

## Cross-Fade zwischen Items (optional, sehr empfohlen)

Statt eines harten Wechsels:

- Beim `ShowItemAsync(index)` setze `NextImageSource = aktuelle Quelle`, schiebe das `NextImage` mit Opacity 0 vor das `CurrentImage`.
- Setze `CurrentImageSource = neue Quelle` für den Hintergrund.
- Animiere `NextImage.Opacity` sanft auf 0 (alte Bild verblasst, neue erscheint darunter).

Eleganter und einfacher: **eine** Image-Control mit Opacity-Storyboard:

1. Beim Wechsel: Animiere `CurrentImage.Opacity` 1 → 0 in 300 ms.
2. Wenn die Animation endet (`Completed`-Event): setze die neue `Source`, animiere `Opacity` 0 → 1 in 300 ms.
3. Während der 600 ms Übergangs-Zeit pausiert der Overlay-Timer.

Für die erste Version: **harter Wechsel ohne Cross-Fade** ist akzeptabel. Polierung kann später kommen — Cross-Fade ist explizit als „nice-to-have" markiert.

---

## Overlay-Format

Ortsname (große Zeile, optional):

```
Stein am Rhein
```

Datum/Zeit-Zeile darunter, immer:

```
Montag, 12. April 2026 · 09:14
```

Falls `Location` `null` ist: nur die Datum/Zeit-Zeile, dafür größer (FontSize 38 statt 26).

Format-String: `"dddd, d. MMMM yyyy · HH:mm"` mit `CultureInfo("de-DE")`.

Layout: linksbündig, `Padding="48,32"` vom unteren Bildrand, Hintergrund-Gradient von transparent oben auf 80 % schwarz unten — sorgt für Lesbarkeit auch über hellen Bildbereichen, ohne das Foto unterhalb hart abzuschneiden.

Gut lesbar bei 1920×1080-Notebook-Bildschirm aus normaler Sitzdistanz.

---

## Tastatur-Steuerung im Präsentations-Fenster

| Taste | Wirkung |
|---|---|
| `Esc` | Präsentation beenden, Fenster schließen |
| `Space` | Pause/Weiter |
| `→` | Nächstes Item (überspringt verbleibende Restzeit) |
| `←` | Vorheriges Item |

Maus wird ausgeblendet (`Cursor="None"`). Bei Mausbewegung optional einen kleinen Restzeit-Indikator einblenden — nicht Teil dieser Iteration.

---

## Edge-Cases

- **Keine ausgewählten Fotos, keine Karten, kein Start/End** → Button ist deaktiviert (`CanStartPresentation`).
- **Nur Karten, keine Fotos** → läuft, jede Karte 5 s, kein Overlay.
- **Foto im Mittelblock ohne `DateTime`** → wird aus dem Mittelblock ausgenommen, da chronologisch nicht einordnbar. Start/End-Fotos sind davon ausgenommen — sie kommen unabhängig von DateTime in die Playlist.
- **Foto mit `DateTime`, aber ohne `Location`** → Overlay zeigt nur die Datum/Zeit-Zeile (etwas größer).
- **Startbild ohne jegliche Metadaten** (typisches Startbild wie `Start.jpg`) → wird ohne Overlay 5 s gezeigt, fungiert als Titel-Karte.
- **Mehrere Startbilder** (mehrere mit State=Start) → alle in der Reihenfolge `DateTime` aufsteigend, dann `Filename`. Selten, aber unterstützt.
- **Mehrere Endbilder** → analog am Ende, alle in derselben Reihenfolge.
- **Karte konnte nicht geladen werden** → überspringe Item, blende kurz schwarz, gehe zum nächsten.
- **Letztes Item erreicht** → Fenster schließt sich automatisch.

---

## Tests

Sinnvoll automatisierbar (in `TravelJournal.Core.Tests` oder einem neuen `TravelJournal.Wpf.Tests`-Projekt):

- Playlist-Builder: Mix aus 3 Selected-Photos und 2 Maps → Playlist hat 5 Einträge in korrekter chronologischer Reihenfolge.
- Foto ohne `DateTime` wird ausgenommen.
- Overlay-Format: ein Test-Photo am 12.04.2026 09:14 ergibt `OverlayDateTime == "Sonntag, 12. April 2026 · 09:14"` (12.04.2026 war ein Sonntag).

UI-Verhalten (Timing, Animationen, ESC) wird manuell verifiziert.

---

## Akzeptanzkriterien

- Toolbar-Button „Präsentation starten" startet bei mindestens 1 Item (Selected-Foto, Karte, Start-Foto oder End-Foto).
- Vollbild-Fenster öffnet sich, schwarzer Hintergrund, kein Mauszeiger sichtbar.
- **Erstes Item ist das Startbild** (State=3), falls vorhanden — auch wenn es keinen DateTime hat und seine `Filename` chronologisch nicht zuerst käme.
- **Letztes Item ist das Endbild** (State=4), falls vorhanden — unabhängig von dessen `DateTime`.
- Dazwischen: ausgewählte Fotos (State=1) und Karten in chronologischer Reihenfolge.
- Items wechseln im 5-Sekunden-Takt.
- Bei Foto-Items mit `DateTime` erscheint die Info-Einblendung sofort sichtbar, blendet nach 2 s sanft aus.
- Einblendung enthält bei vorhandener `Location` zwei Zeilen (Ort groß, dann Datum/Zeit), sonst eine Zeile (Datum/Zeit).
- **Startbild ohne Metadaten** (DateTime null) zeigt **kein** Overlay, nur das Bild auf schwarzem Grund.
- Beispiel-Format der Datum/Zeit-Zeile: `Montag, 12. April 2026 · 09:14`.
- Karten werden ohne Overlay 5 s lang gezeigt.
- `Esc` schließt das Präsentationsfenster sofort, Hauptfenster ist wieder bedienbar.
- `Space` pausiert/setzt fort, `→`/`←` springen zwischen Items.
- Bei großer Foto-Zahl bleiben Übergänge ruckelfrei (Pre-Loading des nächsten Bildes via `Task.Run` + `Freeze()`).

---

## Was bewusst NICHT teil dieser Iteration ist

- Cross-Fade-Animation zwischen Items (kann als nächste Politur ergänzt werden)
- Fortschrittsbalken oder Restzeit-Indikator
- Begleitmusik
- Mehrmonitor-Auswahl (Präsentation startet auf dem Hauptbildschirm)
- Konfigurierbare Item-Dauer in der UI (5 s sind hartkodiert; Konstanten oben in der ViewModel-Klasse änderbar)
- Konfigurierbare Overlay-Dauer (2 s analog hartkodiert)
- Untertitel/Description einblenden (Title und Description bleiben für die spätere Web-Präsentation reserviert)
- Aufzeichnung als Video
- Vollbild-Wechsel auf einen externen Beamer per Hotkey
