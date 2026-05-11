# Claude Code Anweisung — Schritt 15: UI-Modernisierung mit WPF-UI

## Ziel

Größerer optischer und struktureller Aufschlag der WPF-Oberfläche, plus zwei Verhaltens-Korrekturen am Karten-Generator und HEIC-Import:

1. **WPF-UI-Library einsetzen** — moderne Fluent-Design-Controls (Buttons, ComboBoxen, Slider) und vor allem schönere, themen­konforme Dialoge ersetzen die altbacken wirkenden Standard-`MessageBox`-Aufrufe.
2. **Befehlsspalte in drei klar getrennte Bereiche gliedern**: Vorbereitung, Bearbeitung, Ausgabe. Innerhalb der Bearbeitung sinnvolle Untergruppen.
3. **Karten generieren** ist nur aktiv, wenn mindestens ein ausgewähltes Foto eine ermittelte `Location` hat.
4. **Vor dem Generieren neuer Karten** werden alle bestehenden `map_*.png` aus dem Foto-Ordner und die zugehörigen CSV-Einträge gelöscht — kein Vermischen alter und neuer Karten.
5. **HEIC-Import ohne Backup-Unterordner** — die HEIC-Originale werden nach erfolgreicher JPG-Konvertierung direkt gelöscht, nicht mehr nach `heic-original/` verschoben.
6. **Anwendungs-Icon** als vollwertiges Windows-`.ico` mit Mehrgrößen-Set (16, 32, 48, 64, 128, 256 px), in `Diashow.csproj` als `ApplicationIcon` referenziert.

## Kontext

Setzt auf Schritt 1–14 auf. Hauptbetroffene Dateien:

- `TravelJournal.Wpf/TravelJournal.Wpf.csproj` — neues NuGet `WPF-UI`, `ApplicationIcon`-Property
- `TravelJournal.Wpf/App.xaml` — WPF-UI-ResourceDictionaries, Fluent-Theme
- `TravelJournal.Wpf/Views/MainWindow.xaml` — Dreiteilige Toolbar, WPF-UI-Controls
- `TravelJournal.Wpf/Services/ConfirmDialogService.cs` — `Wpf.Ui.Controls.MessageBox` statt System-MessageBox
- `TravelJournal.Wpf/ViewModels/MainViewModel.cs` — `CanGenerateMaps`-Erweiterung, Karten-Cleanup-Logik
- `TravelJournal.Core/Services/MagickHeicConverter.cs` — Backup-Logik entfernen
- `TravelJournal.Core/MapRendering/TileMapRenderer.cs` (oder neuer `MapCleanupService`) — Cleanup-Schritt
- Neuer `TravelJournal.Wpf/Resources/Icon.ico`

---

## Änderung 1 — WPF-UI-Library integrieren

### NuGet-Paket

In `TravelJournal.Wpf.csproj` ergänzen:

```xml
<PackageReference Include="WPF-UI" Version="3.*" />
```

WPF-UI (lepoco/wpfui) ist MIT-lizenziert, aktiv gepflegt, bringt Fluent-Design-Controls, Theme-Manager und einen großen Icon-Set mit. Im Vergleich zu MahApps wirkt es 2026 deutlich frischer.

### App.xaml — ResourceDictionaries einbinden

```xml
<Application x:Class="TravelJournal.Wpf.App"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml">
  <Application.Resources>
    <ResourceDictionary>
      <ResourceDictionary.MergedDictionaries>
        <ui:ThemesDictionary Theme="Light" />
        <ui:ControlsDictionary />
        <ResourceDictionary Source="/Resources/Styles.xaml" />
      </ResourceDictionary.MergedDictionaries>
    </ResourceDictionary>
  </Application.Resources>
</Application>
```

`Theme="Light"` als Default — `Dark` ist eine triviale Anpassung für später, falls gewünscht. Die in Schritt 5 angelegten eigenen Styles (`DetailTitleText`, `DetailMetaText` etc.) bleiben erhalten und überschreiben WPF-UI-Defaults nur dort, wo nötig.

### MainWindow auf `ui:FluentWindow` umstellen

```xml
<ui:FluentWindow x:Class="TravelJournal.Wpf.Views.MainWindow"
                 xmlns:ui="http://schemas.lepo.co/wpfui/2022/xaml"
                 ExtendsContentIntoTitleBar="True"
                 WindowBackdropType="Mica"
                 WindowCornerPreference="Round"
                 Title="TravelJournal" Height="900" Width="1600">
  <Grid>
    <ui:TitleBar Title="TravelJournal" Icon="Resources/Icon.ico" />
    <!-- bestehender Drei-Spalten-Inhalt darunter, mit Top-Margin für die TitleBar -->
  </Grid>
</ui:FluentWindow>
```

Mica-Backdrop und runde Ecken sind auf Windows 11 nativ; auf Windows 10 fallen sie still aufs klassische Verhalten zurück.

### Dialoge ersetzen

Überall, wo bisher `MessageBox.Show(...)` aufgerufen wird (insbesondere in `ConfirmDialogService` aus Schritt 4 und im Renamer aus Schritt 12), wird auf `Wpf.Ui.Controls.MessageBox` umgestellt:

```csharp
public sealed class FluentConfirmDialogService : IConfirmDialogService
{
    public async Task<RotationSaveDecision> AskRotationSaveDecisionAsync(string filename)
    {
        var dialog = new Wpf.Ui.Controls.MessageBox
        {
            Title = "Rotation nicht gespeichert",
            Content = $"Das Foto „{filename}" wurde gedreht, aber noch nicht gespeichert.",
            PrimaryButtonText = "Speichern",
            SecondaryButtonText = "Verwerfen",
            CloseButtonText = "Abbrechen"
        };

        var result = await dialog.ShowDialogAsync();
        return result switch
        {
            Wpf.Ui.Controls.MessageBoxResult.Primary   => RotationSaveDecision.Save,
            Wpf.Ui.Controls.MessageBoxResult.Secondary => RotationSaveDecision.Discard,
            _                                          => RotationSaveDecision.Cancel
        };
    }
}
```

Analog für die Renamer-Bestätigung aus Schritt 12 und für den Map-Cleanup-Hinweis (siehe Änderung 5 unten).

### `Snackbar` für Statusmeldungen

WPF-UI bringt einen `Snackbar`-Mechanismus für unaufdringliche Toasts mit. Sinnvolle Einsatzorte:

- „Gespeichert · 14:23:11" aus Schritt 13 als kurzer Snackbar statt Statusleisten-Text — optional (die Statusleisten-Variante bleibt funktional).
- „Kein MapTiler-Key konfiguriert — verwende OSM" aus Schritt 6 als Snackbar beim ersten Karten-Generieren.

Implementierung: `<ui:SnackbarPresenter />` im `MainWindow`, in `MainViewModel` per Service injiziert.

---

## Änderung 2 — Befehlsspalte: Drei Bereiche, klare Hierarchie

Die linke Toolbar (320 px) wird komplett neu strukturiert. Verwendung von `ui:CardExpander`, `ui:Button` und einer einheitlichen Sektions-Typografie.

### Layout-Skelett

```xml
<ScrollViewer VerticalScrollBarVisibility="Auto">
  <StackPanel Margin="12">

    <!-- ===== VORBEREITUNG ===== -->
    <TextBlock Style="{StaticResource ToolbarSectionHeader}" Text="VORBEREITUNG"/>
    <ui:Button Content="Ordner öffnen…"     Icon="{ui:SymbolIcon FolderOpen24}"   Command="{Binding OpenFolderCommand}"     Appearance="Primary"   HorizontalAlignment="Stretch" Margin="0,0,0,4"/>
    <ui:Button Content="Neu scannen"        Icon="{ui:SymbolIcon ArrowSync24}"    Command="{Binding RescanCommand}"         HorizontalAlignment="Stretch" Margin="0,0,0,4"/>
    <ui:Button Content="HEIC importieren"   Icon="{ui:SymbolIcon ArrowImport24}"  Command="{Binding ConvertHeicCommand}"    HorizontalAlignment="Stretch" Margin="0,0,0,4"/>

    <Separator Margin="0,12"/>

    <!-- ===== BEARBEITUNG ===== -->
    <TextBlock Style="{StaticResource ToolbarSectionHeader}" Text="BEARBEITUNG"/>

    <TextBlock Style="{StaticResource ToolbarSubsectionHeader}" Text="Auswahl"/>
    <ui:Button Content="Alle offenen abwählen" Icon="{ui:SymbolIcon CheckmarkSquare24}"
               Command="{Binding DeselectAllOpenCommand}" HorizontalAlignment="Stretch" Margin="0,0,0,8"/>

    <TextBlock Style="{StaticResource ToolbarSubsectionHeader}" Text="Bild bearbeiten"/>
    <ui:Button Content="Fotos umbenennen" Icon="{ui:SymbolIcon Rename24}"
               Command="{Binding RenamePhotosCommand}" HorizontalAlignment="Stretch" Margin="0,0,0,4"/>
    <TextBlock Style="{StaticResource ToolbarHintText}"
               Text="Tasten in der Galerie: 1/2/0 Status, Enter zyklisch, L/R drehen, S speichern"
               Margin="0,0,0,8"/>

    <TextBlock Style="{StaticResource ToolbarSubsectionHeader}" Text="Filter"/>
    <UniformGrid Columns="2" Margin="0,0,0,8">
      <ui:ToggleButton Content="Alle"        IsChecked="{Binding ActiveFilter, ConverterParameter=All,        Converter={StaticResource EnumToBoolConverter}}" Command="{Binding SetFilterCommand}" CommandParameter="All"/>
      <ui:ToggleButton Content="Offen"       IsChecked="{Binding ActiveFilter, ConverterParameter=Open,       Converter={StaticResource EnumToBoolConverter}}" Command="{Binding SetFilterCommand}" CommandParameter="Open"/>
      <ui:ToggleButton Content="Ausgewählt"  IsChecked="{Binding ActiveFilter, ConverterParameter=Selected,   Converter={StaticResource EnumToBoolConverter}}" Command="{Binding SetFilterCommand}" CommandParameter="Selected"/>
      <ui:ToggleButton Content="Abgewählt"   IsChecked="{Binding ActiveFilter, ConverterParameter=Deselected, Converter={StaticResource EnumToBoolConverter}}" Command="{Binding SetFilterCommand}" CommandParameter="Deselected"/>
      <ui:ToggleButton Content="Neu"         IsChecked="{Binding ActiveFilter, ConverterParameter=New,        Converter={StaticResource EnumToBoolConverter}}" Command="{Binding SetFilterCommand}" CommandParameter="New"/>
      <ui:ToggleButton Content="Karten"      IsChecked="{Binding ActiveFilter, ConverterParameter=Maps,       Converter={StaticResource EnumToBoolConverter}}" Command="{Binding SetFilterCommand}" CommandParameter="Maps"/>
    </UniformGrid>

    <TextBlock Style="{StaticResource ToolbarSubsectionHeader}" Text="Orte"/>
    <ui:Button Content="{Binding GeocodingButtonText}" Icon="{ui:SymbolIcon Location24}"
               Command="{Binding ToggleGeocodingCommand}" HorizontalAlignment="Stretch" Margin="0,0,0,8"/>

    <ui:CardExpander Header="Karten" Icon="{ui:SymbolIcon Map24}" IsExpanded="False" Margin="0,0,0,8">
      <StackPanel>
        <TextBlock Style="{StaticResource ToolbarLabelText}" Text="Stil"/>
        <ComboBox ItemsSource="{Binding AvailableMapStyles}"
                  DisplayMemberPath="DisplayLabel"
                  SelectedValuePath="Id"
                  SelectedValue="{Binding SelectedMapStyleId}"
                  Margin="0,0,0,8"/>

        <TextBlock Style="{StaticResource ToolbarLabelText}" Text="Sprache"/>
        <ComboBox ItemsSource="{Binding AvailableLanguages}"
                  SelectedItem="{Binding SelectedLanguage}"
                  Margin="0,0,0,8"/>

        <TextBlock Style="{StaticResource ToolbarLabelText}">
          <Run Text="Rand"/>
          <Run Text="{Binding BoundsPaddingPercent, StringFormat=' ({0} %)'}"/>
        </TextBlock>
        <Slider Minimum="0" Maximum="40" TickFrequency="2" IsSnapToTickEnabled="True"
                Value="{Binding BoundsPaddingPercent}" Margin="0,0,0,12"/>

        <ui:Button Content="Karten generieren"
                   Icon="{ui:SymbolIcon MapDrive24}"
                   Command="{Binding GenerateMapsCommand}"
                   Appearance="Primary"
                   HorizontalAlignment="Stretch"/>
        <TextBlock Style="{StaticResource ToolbarHintText}"
                   Text="Aktiv, sobald für mindestens ein ausgewähltes Foto ein Ort ermittelt wurde. Bestehende Karten werden ersetzt."
                   TextWrapping="Wrap" Margin="0,8,0,0"/>
      </StackPanel>
    </ui:CardExpander>

    <Separator Margin="0,12"/>

    <!-- ===== AUSGABE ===== -->
    <TextBlock Style="{StaticResource ToolbarSectionHeader}" Text="AUSGABE"/>
    <ui:Button Content="Präsentation starten"        Icon="{ui:SymbolIcon Play24}"      Command="{Binding StartPresentationCommand}"          HorizontalAlignment="Stretch" Margin="0,0,0,4"/>
    <ui:Button Content="Web-Präsentation exportieren" Icon="{ui:SymbolIcon ShareScreenStart24}" Command="{Binding ExportWebPresentationCommand}" HorizontalAlignment="Stretch"/>
  </StackPanel>
</ScrollViewer>
```

### Neue Style-Definitionen in `Resources/Styles.xaml`

```xml
<Style x:Key="ToolbarSectionHeader" TargetType="TextBlock">
  <Setter Property="FontSize" Value="11"/>
  <Setter Property="FontWeight" Value="SemiBold"/>
  <Setter Property="Foreground" Value="{DynamicResource TextFillColorSecondaryBrush}"/>
  <Setter Property="Typography.Capitals" Value="AllSmallCaps"/>
  <Setter Property="Margin" Value="0,0,0,8"/>
</Style>

<Style x:Key="ToolbarSubsectionHeader" TargetType="TextBlock">
  <Setter Property="FontSize" Value="10"/>
  <Setter Property="FontWeight" Value="SemiBold"/>
  <Setter Property="Foreground" Value="{DynamicResource TextFillColorTertiaryBrush}"/>
  <Setter Property="Margin" Value="2,8,0,4"/>
</Style>

<Style x:Key="ToolbarHintText" TargetType="TextBlock">
  <Setter Property="FontSize" Value="10"/>
  <Setter Property="Foreground" Value="{DynamicResource TextFillColorTertiaryBrush}"/>
  <Setter Property="TextWrapping" Value="Wrap"/>
</Style>

<Style x:Key="ToolbarLabelText" TargetType="TextBlock">
  <Setter Property="FontSize" Value="11"/>
  <Setter Property="Margin" Value="0,0,0,2"/>
</Style>
```

`TextFillColorSecondaryBrush` etc. sind WPF-UI-eigene Theme-Brushes, sie passen sich automatisch an Light/Dark-Theme an.

---

## Änderung 3 — „Karten generieren" nur bei vorhandenen Orten

`CanGenerateMaps` in `MainViewModel` wird verschärft:

```csharp
private bool CanGenerateMaps() =>
    !IsBusy
    && !string.IsNullOrEmpty(CurrentFolder)
    && Photos.Any(p =>
        p.State == PhotoState.Selected
        && p.Latitude is not null
        && p.Longitude is not null
        && !string.IsNullOrEmpty(p.Location));
```

Damit ist der Button **inaktiv**, solange für kein ausgewähltes Foto ein Ort ermittelt wurde — der Nutzer wird durch den natürlichen Workflow geleitet (zuerst „Orte ermitteln", dann „Karten generieren").

Damit der Button-Status sich aktualisiert, sobald Geocoding ein `Location`-Feld setzt, wird der Auto-Save-Listener-Filter aus Schritt 13 (`OnPhotoPropertyChanged`) erweitert:

```csharp
private void OnPhotoPropertyChanged(object? sender, PropertyChangedEventArgs e)
{
    if (e.PropertyName != null && CsvRelevantProperties.Contains(e.PropertyName))
    {
        RequestAutoSave();
        // Wenn Location gesetzt wird, kann sich CanGenerateMaps geändert haben
        if (e.PropertyName == nameof(PhotoViewModel.Location))
            GenerateMapsCommand.NotifyCanExecuteChanged();
    }
}
```

Tooltip am Button erklärt den Aktivierungs-Zustand:

```xml
ToolTip="Erzeugt Karten für jeden Stopp.
Aktiv, sobald für mindestens ein ausgewähltes Foto ein Ort ermittelt wurde."
```

---

## Änderung 4 — Bestehende Karten vor Generieren löschen

### Cleanup-Schritt im `MainViewModel.GenerateMapsAsync`

Vor dem Aufruf des Renderers:

```csharp
private async Task<int> CleanupExistingMapsAsync(CancellationToken ct)
{
    if (CurrentFolder is null) return 0;

    // 1) Karten-Photos aus der in-Memory-Liste entfernen
    var mapPhotos = Photos
        .Where(p => p.UnderlyingPhoto.Filename.StartsWith("map_", StringComparison.OrdinalIgnoreCase)
                 && p.UnderlyingPhoto.Filename.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        .ToList();

    foreach (var p in mapPhotos)
        Photos.Remove(p);

    // 2) Dateien löschen
    var deleted = 0;
    foreach (var p in mapPhotos)
    {
        try
        {
            var path = Path.Combine(CurrentFolder, p.UnderlyingPhoto.Filename);
            if (File.Exists(path))
            {
                File.Delete(path);
                deleted++;
            }
        }
        catch (Exception ex)
        {
            // Logging in StatusText, aber nicht abbrechen
            BackgroundActivityText = $"Fehler beim Löschen einer Karte: {ex.Message}";
        }

        ct.ThrowIfCancellationRequested();
    }

    // Auto-Save (Schritt 13) feuert wegen Photos-Mutation;
    // explizit triggern, falls die Collection-Change keinen PropertyChanged auslöst:
    RequestAutoSave();

    return deleted;
}
```

Im `GenerateMapsAsync` aufrufen, **bevor** der Renderer startet:

```csharp
[RelayCommand(CanExecute = nameof(CanGenerateMaps))]
private async Task GenerateMapsAsync()
{
    var cts = new CancellationTokenSource();
    IsBusy = true;
    try
    {
        var deleted = await CleanupExistingMapsAsync(cts.Token);
        if (deleted > 0)
            BackgroundActivityText = $"{deleted} alte Karte(n) entfernt — Generierung läuft …";

        // bisheriger Generator-Aufruf
        // …
    }
    finally { IsBusy = false; }
}
```

### Bestätigungs-Dialog (optional, aber sinnvoll)

Wenn `> 0` Karten gefunden wurden, vor dem Löschen eine kurze Bestätigung per WPF-UI-MessageBox:

```
Titel: Karten neu erzeugen
Inhalt: Es werden 7 bestehende Karten gelöscht und durch frisch generierte ersetzt. Fortfahren?
[Ja] [Abbrechen]
```

Das schützt vor versehentlichem Verlust manuell editierter Karten-Titel/Description (die nach dem Löschen + Re-Erzeugen wieder leer sind). Die Bestätigung kann ausgeblendet werden („Nicht mehr fragen") — wird in `user-settings.json` aus Schritt 8 gemerkt.

---

## Änderung 5 — HEIC-Import ohne Backup-Unterordner

### `MagickHeicConverter` vereinfachen

Im `ConvertAsync` aus Schritt 11 wird der Backup-Pfad entfernt; nach erfolgreicher Konvertierung wird die Original-HEIC-Datei direkt gelöscht:

```csharp
public async Task<HeicConversionResult> ConvertAsync(
    string heicFilePath,
    HeicConversionOptions options,
    CancellationToken ct = default)
{
    // … (Pfad-Berechnung, Konfliktauflösung, eigentliche Konvertierung wie bisher) …

    // NEU: Original direkt löschen, kein Move nach heic-original/
    File.Delete(heicFilePath);

    var convertedSize = new FileInfo(jpegPath).Length;

    return new HeicConversionResult(
        SourcePath: heicFilePath,
        OutputJpegPath: jpegPath,
        BackupPath: null,                // bleibt im Record-Type, ist aber jetzt immer null
        ExifPreserved: exifPreserved,
        OriginalSizeBytes: originalSize,
        ConvertedSizeBytes: convertedSize);
}
```

`HeicConversionResult.BackupPath` bleibt aus Kompatibilitätsgründen im Record, ist aber stets `null`. Optional kann er ganz entfernt werden, dann müssen alle Aufrufer mitziehen.

Der `BackupSubfolderName`-Parameter in `HeicConversionOptions` wird obsolet und entfernt.

### Verhalten beim Konflikt

Falls bereits eine `<name>.jpg` neben der HEIC existiert, gilt weiterhin:

- Ziel-Filename bekommt `_converted`-Suffix.
- Original-HEIC wird trotzdem gelöscht.

### Hinweis-Dialog vor Bulk-Konvertierung

Da die Originale jetzt unwiederbringlich gelöscht werden, erscheint vor dem ersten HEIC-Import ein deutlicher Bestätigungs-Dialog (WPF-UI-MessageBox):

```
Titel: HEIC importieren
Inhalt: Es werden 14 HEIC-Dateien nach JPEG konvertiert.
        Die HEIC-Originale werden anschließend gelöscht.
        Fortfahren?
[Ja] [Abbrechen]
```

Auch hier kann „Nicht mehr fragen" gemerkt werden.

### Tests in `MagickHeicConverterTests`

- Nach erfolgreicher Konvertierung existiert die Quell-HEIC-Datei nicht mehr.
- `HeicConversionResult.BackupPath` ist `null`.
- Kein `heic-original/`-Unterordner wird angelegt.

---

## Änderung 6 — Anwendungs-Icon

### Icon-Datei erzeugen

In `TravelJournal.Wpf/Resources/` eine `Icon.ico` mit folgenden Größen anlegen: 16, 32, 48, 64, 128, 256 px. Aus dem bestehenden `TravelJournal.png` (im Projekt-Root) kann das z.B. mit ImageMagick erzeugt werden:

```
magick TravelJournal.png -define icon:auto-resize=256,128,64,48,32,16 TravelJournal.Wpf/Resources/Icon.ico
```

oder online via [icoconvert.com](https://icoconvert.com/) / [favicon.io](https://favicon.io/). Wichtig ist die Multi-Size-`.ico`, damit Windows je nach Kontext (Taskleiste 24px, Desktop 48px, Start-Menü 96px, …) die passende Auflösung wählt.

Falls das `TravelJournal.png` zu komplex/detailreich für 16- und 32-px-Darstellung ist (typischer Fehler bei Foto-basierten Logos), eine vereinfachte Variante des Logos für die kleinen Größen verwenden — am besten ein klares Symbol mit hohem Kontrast (z.B. ein stilisiertes Karten-Pin-mit-Kamera oder Kompass-Symbol).

### `.csproj` referenzieren

```xml
<PropertyGroup>
  <ApplicationIcon>Resources\Icon.ico</ApplicationIcon>
</PropertyGroup>
```

Damit bekommt die `TravelJournal.Wpf.exe` automatisch das Icon im Datei-Explorer, in der Taskleiste, im Alt-Tab-Wechsler und in der Verknüpfungs-Vorschau.

### Icon im Fenster

In `MainWindow.xaml` (siehe Änderung 1):

```xml
<ui:TitleBar Title="TravelJournal" Icon="Resources/Icon.ico" />
```

Das `Icon` der `ui:TitleBar` zeigt das Logo links neben dem Fenster-Titel — Windows-11-Standardverhalten.

---

## Migration / Aufräumen

| Aus Schritt | Was wird entfernt / geändert |
|---|---|
| Schritt 2 | Klassischer `MessageBox.Show` in der Toolbar — durch `ui:Button` ersetzt |
| Schritt 4 | `ConfirmDialogService` mit System-MessageBox → `FluentConfirmDialogService` |
| Schritt 11 | `BackupSubfolderName`-Parameter aus `HeicConversionOptions`, `BackupPath`-Logik im Converter, Tests für `heic-original/`-Ordner |
| Schritt 12 | Renamer-Bestätigungs-Dialog von System-MessageBox auf WPF-UI-Variante umstellen |
| Schritt 13 | Auto-Save-Status-Anzeige optional als Snackbar (Statuszeile bleibt parallel) |

Der Filter `Maps` aus Schritt 7 (in Schritt 14 als optional erwähnt) bleibt jetzt fest dabei, weil Karten als reguläre Photos in der Liste erscheinen und gezielt filterbar sein sollen.

---

## Akzeptanzkriterien

- `dotnet build` warningfrei. Fenster startet mit Mica-Backdrop (auf Windows 11) bzw. neutralem Acrylic-Fallback (Windows 10).
- TitleBar zeigt das neue Icon links neben dem Titel „TravelJournal". In der Windows-Taskleiste erscheint dasselbe Icon, ebenso im Datei-Explorer auf der `.exe`.
- Linke Toolbar ist klar in drei Bereiche gegliedert (Vorbereitung, Bearbeitung, Ausgabe), mit Section-Headern in Smallcaps und sichtbaren Trennern dazwischen.
- Innerhalb der Bearbeitung sind Auswahl, Bild bearbeiten, Filter, Orte und Karten als eigene Untergruppen erkennbar. Karten-Optionen liegen in einem zusammenklappbaren `CardExpander`.
- Button „Karten generieren" ist deaktiviert, solange für kein ausgewähltes Foto ein Ort gesetzt ist. Sobald „Orte ermitteln" einen Ort liefert, wird er aktiv (innerhalb von 1–2 Sekunden, durch die `NotifyCanExecuteChanged`-Verkettung).
- Klick auf „Karten generieren" mit bereits vorhandenen Karten zeigt den Bestätigungs-Dialog „X Karten werden ersetzt". Nach Bestätigung verschwinden die alten Karten aus dem Foto-Ordner und aus der `tour.csv`, dann läuft die Generierung.
- HEIC-Import zeigt einen Warn-Dialog „Originale werden gelöscht". Nach Bestätigung sind nach Konvertierung keine HEIC-Dateien und kein `heic-original/`-Ordner mehr im Foto-Ordner — nur die neuen JPGs.
- Alle Dialoge (Rotation-Save-Warnung, Renamer-Bestätigung, HEIC-Warnung, Map-Replace-Warnung) sehen modern aus und passen sich dem aktiven Theme an.
- Die in den älteren Schritten definierten Tastatur-Shortcuts in der Galerie (1/2/0, Enter, L/R, S, Arrows) funktionieren unverändert.
- Statusleiste mit `StatusText` und `BackgroundActivityText` aus Schritt 7/13 bleibt am unteren Fensterrand erhalten und korrekt befüllt.

---

## Was bewusst NICHT teil dieser Iteration ist

- Dark-Theme-Toggle in der UI (Theme ist auf `Light` festgelegt; Wechsel via Code möglich)
- Vollständige `NavigationView`-Architektur (Toolbar bleibt — der Aufwand für Navigation lohnt sich für ein einzelnes MainWindow nicht)
- Custom-Designed Icon (das vorhandene `TravelJournal.png` wird nur in eine multi-size `.ico` konvertiert; ein neues Logo-Design ist eigenes Thema)
- Mica-Backdrop-Effekt im Detail-Bereich (rechts) — bleibt klassisch mit dunklem Passepartout aus Schritt 5
- Wiederherstellung gelöschter HEIC-Dateien aus dem Papierkorb (`File.Delete` umgeht den Papierkorb; falls gewünscht, müsste Microsoft.VisualBasic referenziert und `FileSystem.DeleteFile(... RecycleOption.SendToRecycleBin)` verwendet werden — bewusste Entscheidung gegen diese Abhängigkeit)
- Karten-Cleanup für Karten in untergeordneten Ordnern (nur Top-Level des Foto-Ordners wird angerührt)
- Animationen beim Wechsel zwischen Toolbar-Sektionen
