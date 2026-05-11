# Claude Code Anweisung — Schritt 3: Feature-Erweiterungen WPF

## Ziel

Erweitere die bestehende `TravelJournal.Wpf`-Anwendung um vier aufeinander abgestimmte Features:

1. **Bulk-Aktion „Alle offenen abwählen"** — ein Klick setzt alle Fotos mit `State=None` auf `State=Deselected`. Damit fallen nach einem Re-Scan neu hinzugekommene Fotos (die wieder als `None` erscheinen) sofort visuell auf.
2. **Layout-Umstellung** — die Thumbnail-Liste wird einspaltig und schmal; der gesamte rechte Bereich zeigt das aktuell gewählte Foto in deutlich größerer Ansicht.
3. **Reverse Geocoding** — zu den GPS-Koordinaten jedes Fotos wird per Nominatim (OpenStreetMap) der Ortsname ermittelt, lokal gecached, im Detail-Panel angezeigt und in der CSV persistiert.
4. **Tastatur-Toggle und visuelle Hervorhebung des aktiven Thumbnails** — die Enter-Taste schaltet den State des aktuell fokussierten Fotos um (Selektion/Deselektion); das in der Liste fokussierte Thumbnail ist deutlich sichtbar vom Rest abgehoben.

## Kontext

Setzt auf Schritt 1 (`TravelJournal.Core` mit Modellen, Services, Tests) und Schritt 2 (`TravelJournal.Wpf` mit MVVM-UI) auf. Alle Änderungen sind rückwärtskompatibel zur bisherigen `tour.csv`.

---

## Feature 1 — Button „Alle offenen abwählen"

### Änderungen in `MainViewModel`

Neuen Command per `[RelayCommand]` ergänzen:

```csharp
[RelayCommand(CanExecute = nameof(CanDeselectAllOpen))]
private void DeselectAllOpen()
{
    foreach (var photo in Photos.Where(p => p.State == PhotoState.None))
    {
        photo.State = PhotoState.Deselected;
    }
    UpdateStatusText();
}

private bool CanDeselectAllOpen() => Photos.Any(p => p.State == PhotoState.None);
```

Der Command muss seinen `CanExecute`-Status neu auswerten, sobald sich der State eines Fotos ändert. Lösung: in `PhotoViewModel` löst `OnStateChanged` ein Event aus, auf das `MainViewModel` lauscht und `DeselectAllOpenCommand.NotifyCanExecuteChanged()` aufruft. Alternativ pragmatisch: nach jedem State-Toggle aus den Setze-Commands `DeselectAllOpenCommand.NotifyCanExecuteChanged()` aufrufen.

### Änderung in der Toolbar (`MainWindow.xaml`)

Neuer Button in der linken Toolbar, optisch klar von den Speichern/Scannen-Buttons abgesetzt (z.B. eigene Gruppe „Bulk-Aktionen"):

```xml
<Button Content="Alle offenen abwählen"
        Command="{Binding DeselectAllOpenCommand}"
        ToolTip="Setzt alle Fotos mit Status 'Offen' auf 'Abgewählt'.
Damit fallen neu eingelesene Fotos nach dem nächsten Scan sofort auf."
        Margin="0,16,0,0"/>
```

Tooltip ist wichtig — der Mehrwert ist erklärungsbedürftig.

### Akzeptanzkriterien Feature 1

- Button ist deaktiviert, wenn keine Fotos mit `State=None` existieren.
- Klick setzt alle offenen Fotos auf `Deselected`, sichtbare Rahmenfarben aktualisieren sich sofort.
- Statuszeile aktualisiert die Zähler korrekt.
- Nach einem Re-Scan mit neu hinzugekommenen Fotos: die neuen Fotos sind die einzigen mit `State=None` und damit sofort sichtbar (zusätzlich zur „NEU"-Badge).

---

## Feature 2 — Layout-Umstellung

### Neues Grundlayout in `MainWindow.xaml`

Wechsel von Drei- zu Zwei-Spalten-Layout:

```xml
<Grid>
  <Grid.ColumnDefinitions>
    <ColumnDefinition Width="280" MinWidth="220" />
    <ColumnDefinition Width="*" />
  </Grid.ColumnDefinitions>

  <!-- Spalte 0: Toolbar + Filter (oben) + einspaltige Thumbnail-Liste (unten, scrollbar) -->
  <DockPanel Grid.Column="0">
    <StackPanel DockPanel.Dock="Top">
      <!-- Bisherige Toolbar und Filter-Buttons -->
    </StackPanel>
    <ListBox ItemsSource="{Binding FilteredPhotos}"
             SelectedItem="{Binding SelectedPhoto, Mode=TwoWay}"
             ScrollViewer.HorizontalScrollBarVisibility="Disabled"
             ScrollViewer.VerticalScrollBarVisibility="Auto"
             VirtualizingStackPanel.IsVirtualizing="True"
             VirtualizingStackPanel.VirtualizationMode="Recycling">
      <ListBox.ItemContainerStyle>
        <Style TargetType="ListBoxItem">
          <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
          <Setter Property="Padding" Value="0"/>
        </Style>
      </ListBox.ItemContainerStyle>
      <ListBox.ItemTemplate>
        <DataTemplate>
          <!-- Siehe „Item-Template" unten -->
        </DataTemplate>
      </ListBox.ItemTemplate>
    </ListBox>
  </DockPanel>

  <!-- Spalte 1: große Bildansicht + Metadaten + State-Steuerung + Title/Description -->
  <Grid Grid.Column="1">
    <Grid.RowDefinitions>
      <RowDefinition Height="*" />          <!-- großes Bild füllt -->
      <RowDefinition Height="Auto" />       <!-- Metadaten-Strip -->
      <RowDefinition Height="Auto" />       <!-- State-Buttons -->
      <RowDefinition Height="Auto" />       <!-- Title -->
      <RowDefinition Height="*" MinHeight="120"/> <!-- Description -->
    </Grid.RowDefinitions>
    <!-- Inhalt siehe „Detail-Bereich" unten -->
  </Grid>
</Grid>
```

`ListBox` statt des bisherigen `ItemsControl`+`WrapPanel`, weil wir nun eine echte Auswahl-Semantik wollen (`SelectedItem`-Binding ersetzt den manuellen MouseDown-Handler) und Virtualisierung für viele Fotos brauchen.

### Item-Template (einspaltige Thumbnail-Zeile, ~260px breit)

Pro Eintrag eine kompakte Zeile mit Thumbnail links, Metadaten rechts, State-Rahmen außen herum:

```xml
<Border BorderThickness="3" CornerRadius="4" Margin="4"
        BorderBrush="{Binding State, Converter={StaticResource PhotoStateToBrushConverter}}">
  <Grid>
    <Grid.ColumnDefinitions>
      <ColumnDefinition Width="96"/>
      <ColumnDefinition Width="*"/>
    </Grid.ColumnDefinitions>
    <Image Grid.Column="0" Source="{Binding Thumbnail}" Stretch="UniformToFill" Width="96" Height="72"/>
    <StackPanel Grid.Column="1" Margin="8,4">
      <TextBlock Text="{Binding Filename}" TextTrimming="CharacterEllipsis" FontWeight="SemiBold"/>
      <TextBlock Text="{Binding DateTime, StringFormat='{}{0:dd.MM. HH:mm}'}" FontSize="11" Opacity="0.75"/>
      <TextBlock Text="{Binding Location}" FontSize="11" Opacity="0.75" TextTrimming="CharacterEllipsis"/>
    </StackPanel>
    <!-- Badges/Icons als Overlay: NEU, FEHLT, State-Icon -->
  </Grid>
</Border>
```

Thumbnail-Zielgröße in der einspaltigen Liste: `96×72` (vorher `200×150`). `IThumbnailLoader.LoadAsync` kann dieselbe Decode-Größe weiter verwenden — die kleinere Anzeige ist optisch ausreichend scharf, der Speicherverbrauch sinkt deutlich.

### Detail-Bereich (rechte Spalte)

- **Bild-Bereich (Row 0)**: `Image` mit `Stretch="Uniform"` füllt die gesamte verfügbare Fläche. Quelle ist eine größere Variante des Fotos. Empfehlung: zweite Property `LargeImage` auf `PhotoViewModel` ergänzen, die per `IThumbnailLoader.LoadAsync(filePath, decodePixelWidth: 1600)` lazy nachlädt, sobald das Foto selektiert wird. So bleibt die Galerie schnell, das Detail wird in voller Pracht angezeigt. Während des Ladens fallback auf `Thumbnail`.
- **Metadaten-Strip (Row 1)**: einzeiliger horizontaler Bereich mit Datum, Uhrzeit, Lat/Lon, Höhe, Ortsname. Trennzeichen `·`. Z.B. `12.04.2026 · 09:14 · 47.376541, 8.541234 · 412 m · Zürich, Kreis 1`.
- **State-Buttons (Row 2)**: Drei `RadioButton`s (Auswählen / Abwählen / Offen) horizontal, mit denselben Farben wie der Galerie-Rahmen.
- **Title (Row 3)**: einzeilige `TextBox` mit Label „Titel".
- **Description (Row 4)**: mehrzeilige `TextBox`, `AcceptsReturn=True`, `TextWrapping=Wrap`, `VerticalScrollBarVisibility=Auto`.

Wenn `SelectedPhoto == null`: gesamte Detail-Spalte zeigt einen zentrierten Hinweistext „Kein Foto ausgewählt" und sonst nichts (per `DataTrigger` oder `Visibility`).

### Akzeptanzkriterien Feature 2

- Thumbnail-Liste füllt eine schmale linke Spalte und scrollt vertikal.
- Auswahl in der Liste aktualisiert das große Bild rechts ohne spürbare Verzögerung.
- Das große Bild nutzt den verfügbaren Platz aus, ohne zu pixeln (1600px-Decode).
- Tastatur-Shortcuts (Pfeil hoch/runter über die `ListBox`-Standard-Navigation, sowie 1/2/0 für State) funktionieren weiterhin.
- Layout bleibt bei kleinen Fenstergrößen (`MinWidth=220` linke Spalte) noch lesbar.

---

## Feature 3 — Reverse Geocoding (Koordinaten → Ortsname)

### Antwort vorab: ja, das geht — und kostenlos

Es gibt mehrere etablierte APIs. Empfohlen für dieses Projekt: **Nominatim** (offizieller Reverse-Geocoder von OpenStreetMap). Frei nutzbar, kein API-Key, gute Datenqualität in Mitteleuropa, klare Nutzungsbedingungen. Wichtige Auflagen:

- Maximal **1 Request pro Sekunde** auf den öffentlichen Endpunkt `https://nominatim.openstreetmap.org`.
- Aussagekräftiger `User-Agent`-Header mit Kontaktmöglichkeit ist Pflicht.
- Ergebnisse sollen lokal gecached werden — wir schicken nicht zweimal denselben Request.

Alternativen, falls später nötig: LocationIQ, Geoapify, Photon (komoot) — alle mit ähnlicher API-Form.

### Datenmodell-Erweiterung in `TravelJournal.Core`

`Photo` bekommt eine zusätzliche Eigenschaft:

```csharp
public string? Location { get; set; }
```

CSV-Schema wird **am Ende** ergänzt — die bisherigen Spalten bleiben in Position und Reihenfolge unverändert:

```
Filename;DateTime;Latitude;Longitude;Altitude;State;Title;Description;Location
```

`TourCsvReader` muss tolerant sein: fehlende `Location`-Spalte (alte CSVs) → `Location = null`. `TourCsvWriter` schreibt die neue Spalte immer mit, leer bei `null`.

Ein zusätzlicher Test in `TravelJournal.Core.Tests`: alte CSV ohne `Location`-Spalte wird gelesen ohne Exception, `Location` ist `null`. Roundtrip mit gesetztem `Location` bleibt erhalten.

### Neue Abstraktion `IReverseGeocoder` in `TravelJournal.Core`

```csharp
public interface IReverseGeocoder
{
    Task<string?> ResolveAsync(double latitude, double longitude, CancellationToken ct = default);
}
```

Implementierung in `TravelJournal.Core/Services/NominatimReverseGeocoder.cs`:

- `HttpClient` per Konstruktor (DI-freundlich), `User-Agent` Header `"TravelJournal/1.0 (kontakt@example.org)"` — Adresse als Konstruktor-Parameter konfigurierbar.
- Endpoint: `https://nominatim.openstreetmap.org/reverse?format=jsonv2&lat={lat}&lon={lon}&zoom=14&accept-language=de`.
- Antwort als JSON parsen (z.B. mit `System.Text.Json`), Ortsbezeichnung priorisiert aus folgenden Feldern (erste vorhandene gewinnt): `address.village`, `address.town`, `address.city`, `address.municipality`, `name`, `display_name`. Optional Stadtteil ergänzen: `address.suburb`. Beispiel-Format: `"Zürich, Kreis 1"` oder einfach `"Stein am Rhein"`.
- Fehlerbehandlung: HTTP-Fehler, Rate-Limit (HTTP 429) → `null` zurückgeben (nicht werfen), Logging optional.

Rate-Limiting durch einen kleinen `SemaphoreSlim(1,1)` plus `await Task.Delay(1100)` nach jedem Request. Nicht elegant, aber sicher unter dem 1-req/sec-Limit.

### Caching in `TravelJournal.Core`

Eigene Klasse `JsonGeocodeCache` (oder `FileGeocodeCache`) als Decorator/Wrapper, die `IReverseGeocoder` implementiert und einen `IReverseGeocoder` umschließt:

- Cache-Datei `geocache.json` im Foto-Ordner (Pfad wird per Konstruktor übergeben).
- Schlüssel: `$"{Math.Round(lat, 4)},{Math.Round(lon, 4)}"` (≈ 11 m Genauigkeit, ausreichend für Ortsnamen).
- Bei Treffer: sofort zurück, ohne API-Aufruf.
- Bei Miss: API aufrufen, Ergebnis speichern, Cache-Datei sofort schreiben (atomar via Tempdatei + Move).

```csharp
public sealed class JsonGeocodeCache : IReverseGeocoder
{
    public JsonGeocodeCache(IReverseGeocoder inner, string cacheFilePath);
    public Task<string?> ResolveAsync(double latitude, double longitude, CancellationToken ct = default);
}
```

In `TravelJournal.Wpf`/`App.xaml.cs` wird in der DI-Konfiguration `IReverseGeocoder` als `JsonGeocodeCache(new NominatimReverseGeocoder(...), pathBeimOrdnerÖffnenSetzen)` registriert. Da der Cache-Pfad vom aktuellen Foto-Ordner abhängt, ist eine Factory die saubere Lösung:

```csharp
public interface IReverseGeocoderFactory
{
    IReverseGeocoder CreateForFolder(string folderPath);
}
```

### UI-Anbindung in `TravelJournal.Wpf`

Neuer Button in der Toolbar: **„Orte ermitteln"**. Verhalten:

1. `IReverseGeocoderFactory.CreateForFolder(CurrentFolder)` → konkreter Geocoder.
2. Iteriere alle `Photos` mit gesetzten Koordinaten **und** noch leerem `Location`.
3. Pro Foto: `await geocoder.ResolveAsync(lat, lon)`, Ergebnis ins `PhotoViewModel.Location` schreiben.
4. Während des Laufs `IsBusy = true`, in der Statuszeile Fortschritt anzeigen (`"Ortsabfrage 17/42 …"`). Cancel-Token, falls der Nutzer den Vorgang abbrechen möchte (z.B. zweiter Klick auf den Button als Stop).
5. Nach Abschluss: Statuszeile zurücksetzen.

Im Detail-Panel ein neuer Eintrag im Metadaten-Strip: `Location` (siehe Layout oben). Falls `null`, einfach weglassen.

Beim CSV-Speichern wird `Location` automatisch mitgeschrieben — keine Sonderlogik nötig.

### Akzeptanzkriterien Feature 3

- `dotnet build` warningfrei, alle bestehenden Tests grün, neue CSV-Roundtrip-Tests für `Location` grün.
- Klick auf „Orte ermitteln" mit ~10 Test-Fotos füllt nach kurzer Zeit (~10 s wegen Rate-Limit) die Ortsnamen sichtbar in der Liste und im Detail-Panel.
- Zweiter Klick auf „Orte ermitteln" macht **keinen** API-Aufruf mehr — alle Fotos haben `Location` bereits, oder der Cache antwortet direkt.
- Cache-Datei `geocache.json` liegt im Foto-Ordner, ist menschenlesbares JSON.
- Eine alte CSV ohne `Location`-Spalte lässt sich öffnen und neu speichern — die Spalte erscheint im neuen Output, alte Werte bleiben erhalten.
- Beim Beenden während laufender Geocoding-Anfragen entsteht keine Exception (CancellationToken-Pfad).

---

## Feature 4 — Tastatur-Toggle + visuelle Hervorhebung des aktiven Thumbnails

### Tastatur-Bedienung mit Enter

Die `Enter`-Taste schaltet den `State` des in der Liste fokussierten Fotos in einem Drei-Stufen-Zyklus weiter:

```
None  →  Selected  →  Deselected  →  None  →  …
```

Damit ist das schnelle Durchgehen der Galerie nur mit Pfeil ↑/↓ und `Enter` möglich:

- Erstes `Enter` auf einem offenen Foto → Selected.
- Erneutes `Enter` → Deselected.
- Drittes `Enter` → wieder offen.

Die bisherigen Shortcuts `1`/`2`/`0` (direkter Sprung zu einem bestimmten State) bleiben erhalten — `Enter` ist die ergonomische Ergänzung für den Review-Flow „Foto angucken, Daumen hoch oder runter".

### Implementation in `MainViewModel`

Neuer Command per `[RelayCommand]`:

```csharp
[RelayCommand(CanExecute = nameof(HasSelectedPhoto))]
private void CycleSelectedPhotoState()
{
    if (SelectedPhoto is null) return;
    SelectedPhoto.State = SelectedPhoto.State switch
    {
        PhotoState.None => PhotoState.Selected,
        PhotoState.Selected => PhotoState.Deselected,
        PhotoState.Deselected => PhotoState.None,
        _ => PhotoState.None
    };
    UpdateStatusText();
    DeselectAllOpenCommand.NotifyCanExecuteChanged();
}

private bool HasSelectedPhoto() => SelectedPhoto is not null;
```

`SelectedPhoto`-Setter muss `CycleSelectedPhotoStateCommand.NotifyCanExecuteChanged()` triggern.

### Verkabelung in `MainWindow.xaml`

Der `Enter`-Shortcut darf nicht ausgelöst werden, während der Fokus in der `TextBox` für Title oder Description liegt (sonst würde Enter dort einen Zeilenumbruch oder Tab-Sprung erzeugen). Lösung: `KeyBinding` direkt an der Thumbnail-`ListBox`, nicht am Window:

```xml
<ListBox x:Name="PhotoList"
         ItemsSource="{Binding FilteredPhotos}"
         SelectedItem="{Binding SelectedPhoto, Mode=TwoWay}"
         KeyboardNavigation.TabNavigation="Cycle">
  <ListBox.InputBindings>
    <KeyBinding Key="Enter" Command="{Binding CycleSelectedPhotoStateCommand}" />
    <KeyBinding Key="D1" Command="{Binding SetStateSelectedCommand}" CommandParameter="{Binding SelectedItem, ElementName=PhotoList}" />
    <KeyBinding Key="D2" Command="{Binding SetStateDeselectedCommand}" CommandParameter="{Binding SelectedItem, ElementName=PhotoList}" />
    <KeyBinding Key="D0" Command="{Binding SetStateNoneCommand}" CommandParameter="{Binding SelectedItem, ElementName=PhotoList}" />
  </ListBox.InputBindings>
  <!-- ItemTemplate wie in Feature 2 -->
</ListBox>
```

Damit funktioniert `Enter` nur, wenn die `ListBox` (oder eines ihrer Items) den Fokus hat — Tipparbeit in den Detail-Textboxen bleibt unbeeinträchtigt.

Den bisherigen Window-weiten KeyBinding für `1`/`2`/`0` aus Schritt 2 entfernen oder im selben Sinne an die `ListBox` verschieben, damit das Verhalten konsistent ist.

### Visuelle Hervorhebung des aktiven Thumbnails

Bisher: Status (Selected/Deselected/None) wird über die Rahmenfarbe des Thumbnails dargestellt.
Zusätzlich nötig: das in der `ListBox` fokussierte Item — also das Foto, dessen Detail rechts angezeigt wird — muss klar von den anderen Items abgehoben sein, **ohne** die State-Farbe zu überschreiben.

Lösung über einen `Style` für `ListBoxItem` mit Triggern auf `IsSelected`:

```xml
<Style TargetType="ListBoxItem">
  <Setter Property="Padding" Value="0"/>
  <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
  <Setter Property="Background" Value="Transparent"/>
  <Setter Property="Template">
    <Setter.Value>
      <ControlTemplate TargetType="ListBoxItem">
        <Border x:Name="OuterBorder"
                BorderThickness="3"
                BorderBrush="Transparent"
                CornerRadius="6"
                Background="{TemplateBinding Background}"
                Padding="2">
          <ContentPresenter />
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property="IsSelected" Value="True">
            <Setter TargetName="OuterBorder" Property="BorderBrush" Value="{StaticResource AccentBrush}" />
            <Setter TargetName="OuterBorder" Property="Background" Value="{StaticResource SelectionBackgroundBrush}" />
          </Trigger>
          <Trigger Property="IsMouseOver" Value="True">
            <Setter TargetName="OuterBorder" Property="Background" Value="{StaticResource HoverBackgroundBrush}" />
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>
```

Empfohlene Brushes in `Resources/Styles.xaml`:

- `AccentBrush` — kräftiges Bahnblau `#1E5F8E`, 3 px Rahmen.
- `SelectionBackgroundBrush` — leichter Akzentton mit Transparenz, z.B. `#1E5F8E` mit 18 % Opacity.
- `HoverBackgroundBrush` — sehr dezent, z.B. `#000000` mit 6 % Opacity.

Wichtig: der **innere** Rahmen (State-Farbe rund ums Thumbnail aus Feature 2) bleibt unverändert. So entstehen zwei klar unterscheidbare visuelle Achsen:

- **Außenrahmen + Background** = aktuell fokussiertes Item (UI-Selektion in der Liste).
- **Innenrahmen rund ums Thumbnail** = Domain-State (Selected/Deselected/None).

Der Nutzer kann dadurch auf einen Blick beide Informationen unterscheiden, ohne dass eine die andere verdeckt.

Zusätzlich soll die `ListBox` beim Setzen von `SelectedPhoto` automatisch zum aktiven Item scrollen. Standardverhalten von WPF reicht meistens; falls nicht, im Code-Behind eine kleine Hilfsmethode `PhotoList.ScrollIntoView(PhotoList.SelectedItem)` an `SelectionChanged` hängen.

### Akzeptanzkriterien Feature 4

- Pfeil ↓/↑ in der Galerie wechselt das fokussierte Thumbnail; das Detail rechts aktualisiert sich entsprechend.
- `Enter` mit Fokus auf der Galerie zyklusweise: None → Selected → Deselected → None.
- `Enter` mit Fokus in der Title- oder Description-`TextBox` macht keinen State-Wechsel (wirkt dort wie üblich).
- Das aktive Thumbnail ist eindeutig erkennbar: Akzent-Außenrahmen plus dezent eingefärbter Hintergrund, gleichzeitig bleibt der State-Innenrahmen sichtbar.
- Beim Wechsel des Filters bleibt — falls möglich — die Auswahl auf demselben Foto, sonst springt sie auf das erste Element der gefilterten Liste.

---

## Was bewusst NICHT teil dieser Iteration ist

- Multi-Provider-Auswahl (LocationIQ/Mapbox als Alternative)
- Höhenkorrektur via API
- Offline-Geocoding (z.B. mit GeoNames-Dump)
- Web-Präsentation (kommt später)
- Automatisches Geocoding direkt beim Scan — bewusst manueller Trigger, um die API zu schonen
- Drag&Drop-Sortierung der Thumbnail-Liste
