# Claude Code Anweisung — Schritt 5: Detail-Bereich aufwerten

## Ziel

Den rechten Detail-Bereich der `TravelJournal.Wpf`-Anwendung so umgestalten, dass das aktuelle Foto deutlich größer wirkt und die Begleit­informationen kompakt darunter sitzen. Außerdem zwei zusätzliche technische Metadaten (Dateigröße in MB, Pixelmaße) und der per Reverse-Geocoding ermittelte Ortsname als eigenständige, gut lesbare Einträge im Detail-Bereich.

## Kontext

Setzt auf Schritt 1–4 auf. Die in Schritt 3 eingeführte `Location`-Eigenschaft existiert bereits — sie wird hier prominenter platziert. Neue Metadaten kommen in `TravelJournal.Core/Photo` und werden vom `ExifReaderService` befüllt; sie werden **nicht** in die `tour.csv` geschrieben (sind aus der Datei jederzeit wieder ableitbar).

---

## Änderung 1 — Layout: Bild groß, Begleitinfo kompakt

### Neues Grundgerüst der rechten Spalte

Statt fünf Rows mit zwei konkurrierenden `*`-Werten nur noch zwei Rows: Bild oben (gewinnt allen Platz), kompakter Info-Block unten (`Auto`).

```xml
<Grid Grid.Column="1" Margin="0">
  <Grid.RowDefinitions>
    <RowDefinition Height="*" />        <!-- großes Foto -->
    <RowDefinition Height="Auto" />     <!-- Info-Block, so klein wie nötig -->
  </Grid.RowDefinitions>

  <!-- Foto füllt sämtlichen verfügbaren Platz -->
  <Border Grid.Row="0" Background="#111" Padding="8">
    <Image Source="{Binding SelectedPhoto.LargeImage}" Stretch="Uniform">
      <Image.LayoutTransform>
        <RotateTransform Angle="{Binding SelectedPhoto.PendingRotation}" />
      </Image.LayoutTransform>
    </Image>
  </Border>

  <!-- Info-Block: kompakt, Auto-Höhe, deutlich kleinere Schriften -->
  <Border Grid.Row="1" Padding="16,12" Background="{StaticResource InfoPanelBackgroundBrush}">
    <StackPanel>
      <!-- Inhalt siehe Änderung 2 / 3 -->
    </StackPanel>
  </Border>
</Grid>
```

Der dunkle `#111`-Hintergrund hinter dem Bild wirkt wie ein Passepartout, lenkt nicht vom Foto ab und harmoniert mit dem späteren dunklen Web-Theme.

### Empfohlene Verteilung

Bei einem 1080p-Bildschirm und Vollbild-Fenster soll das Foto-Element mindestens **75 %** der vertikalen Höhe der rechten Spalte einnehmen. Der Info-Block bleibt unter ~220 px hoch. Bei kleineren Fenstern darf das Foto entsprechend schrumpfen, aber der Info-Block soll **nie** das Foto verdrängen — er hat `Auto`, nicht `*`.

### Akzeptanzkriterien Änderung 1

- Im maximierten Fenster auf einem 1920×1080-Display ist das Foto rechts spürbar dominanter als zuvor (vertikal mindestens 75 % der Detail-Spalte).
- Bei kleinen Fenstern (z.B. 1024×768) bleibt das Foto erkennbar das Hauptelement, der Info-Block scrollt nicht und drückt das Foto nicht weg (Description ist auf maximal 5 Zeilen begrenzt, danach internes Scrollen).
- Foto bleibt mit `Stretch="Uniform"` immer komplett sichtbar (kein Beschnitt), auch nach `LayoutTransform` durch eine vorgemerkte Rotation aus Schritt 4.

---

## Änderung 2 — Texte kleiner und klar gestaffelt

Im Info-Block eine bewusste Typografie-Hierarchie. Vorschlag (in `Resources/Styles.xaml` als wiederverwendbare Styles):

| Zweck | Style-Name | FontSize | FontWeight | Farbe |
|---|---|---|---|---|
| Title (Foto-Titel) | `DetailTitleText` | 16 | SemiBold | Standard |
| Ortsname | `DetailLocationText` | 13 | Normal | Akzent (`AccentBrush`) |
| Description | `DetailDescriptionText` | 12 | Normal | Standard |
| Metadaten-Strip | `DetailMetaText` | 11 | Normal | 70 % Opacity |
| Sektionslabels („Titel", „Beschreibung") | `DetailLabelText` | 10 | SemiBold | 55 % Opacity, Großbuchstaben (`Typography.Capitals`) |

Beispiel `Styles.xaml`-Ausschnitt:

```xml
<Style x:Key="DetailTitleText" TargetType="TextBlock">
  <Setter Property="FontSize" Value="16"/>
  <Setter Property="FontWeight" Value="SemiBold"/>
</Style>

<Style x:Key="DetailMetaText" TargetType="TextBlock">
  <Setter Property="FontSize" Value="11"/>
  <Setter Property="Opacity" Value="0.7"/>
</Style>

<Style x:Key="DetailLabelText" TargetType="TextBlock">
  <Setter Property="FontSize" Value="10"/>
  <Setter Property="FontWeight" Value="SemiBold"/>
  <Setter Property="Opacity" Value="0.55"/>
  <Setter Property="Margin" Value="0,4,0,2"/>
</Style>
```

Title- und Description-`TextBox`es übernehmen die Schriftgröße der zugehörigen Text-Styles per `<Setter Property="FontSize" .../>`.

### Reihenfolge im Info-Block (von oben nach unten)

1. **Title** (große Zeile) — `TextBox` mit `DetailTitleText`-Style. Watermark/Placeholder „Titel eingeben…" wenn leer.
2. **Ortsname** (eine Zeile, Akzentfarbe) — `TextBlock` mit `DetailLocationText`-Style. Wird ausgeblendet, wenn `Location == null` (`Visibility` per Converter).
3. **Description** (mehrzeilig, begrenzt) — `TextBox` mit `DetailDescriptionText`-Style, `MaxHeight="100"`, `VerticalScrollBarVisibility="Auto"`, `AcceptsReturn="True"`, `TextWrapping="Wrap"`. Klein-Label „BESCHREIBUNG" darüber.
4. **Metadaten-Strip** — siehe Änderung 3.
5. **State-Buttons** — drei `RadioButton`s horizontal, kompakt (Padding reduzieren), gleiche Schriftgröße wie der Metadaten-Strip.

Vertikale Abstände zwischen Sektionen: 8 px. Innerhalb einer Sektion (z.B. Label + TextBox) 2 px.

### Akzeptanzkriterien Änderung 2

- Titel ist visuell die dominante Textzeile; Description und Metadaten sind sichtbar kleiner.
- Ortsname hebt sich durch Akzentfarbe vom Rest ab und ist kürzer als die Title-Zeile, also klar als Untertitel erkennbar.
- Bei Foto ohne ermittelten Ort verschwindet die Ortszeile vollständig (kein leerer Platzhalter).

---

## Änderung 3 — Erweiterte Metadaten: Ort, Dateigröße, Pixelmaße

### Neue Eigenschaften in `TravelJournal.Core.Photo`

```csharp
public long? FileSizeBytes { get; set; }
public int? PixelWidth { get; set; }
public int? PixelHeight { get; set; }
```

Bewusst **nicht** in die CSV serialisieren — `TourCsvWriter` ignoriert sie, `TourCsvReader` setzt sie auf `null`. Sie werden bei jedem Scan aus den Dateien neu ermittelt; das hält die `tour.csv` schlank und hand-editierbar.

### Erweiterung von `ExifReaderService`

Im selben Aufruf, in dem die EXIF-Tags ausgelesen werden, zusätzlich:

- `FileSizeBytes`: `new FileInfo(filePath).Length`.
- `PixelWidth` / `PixelHeight`: aus `JpegDirectory.TagImageWidth` und `TagImageHeight` (liefert die tatsächlich kodierten Maße ohne Pixel zu dekodieren — sehr schnell). Fallback: `ExifSubIfdDirectory.TagExifImageWidth` / `TagExifImageHeight`. Wenn beides fehlt → `null`.

Hinweis: nach einer 90°/270°-Rotation aus Schritt 4 tauschen sich `PixelWidth` und `PixelHeight` in der Datei. Beim nächsten Scan werden die neuen Werte automatisch ermittelt; nach `SaveRotationAsync` zusätzlich `Photo.PixelWidth/Height` neu setzen (z.B. durch erneutes Aufrufen des `ExifReaderService` für diese eine Datei).

### Tests in `TravelJournal.Core.Tests`

- `ExifReaderService` setzt `FileSizeBytes > 0` für eine existierende JPG-Datei.
- Für ein minimales 1×1-Test-JPG (z.B. mit ImageSharp im Test-Setup erzeugt) liefert `PixelWidth == 1` und `PixelHeight == 1`.

### Erweiterung in `PhotoViewModel`

Read-only-Projektionen mit Formatierung für die UI:

```csharp
public string? FileSizeFormatted =>
    _photo.FileSizeBytes is { } bytes
        ? $"{bytes / 1024d / 1024d:F1} MB"
        : null;

public string? PixelDimensionsFormatted =>
    _photo.PixelWidth is { } w && _photo.PixelHeight is { } h
        ? $"{w} × {h} px"
        : null;

// Beispiel für die Megapixel-Variante, falls gewünscht:
public string? MegapixelsFormatted =>
    _photo.PixelWidth is { } w && _photo.PixelHeight is { } h
        ? $"{(w * h) / 1_000_000d:F1} MP"
        : null;
```

Format-Beispiele: `4.2 MB`, `4032 × 3024 px`. Dezimaltrenner abhängig von `CultureInfo.CurrentUICulture` — falls deutsche Komma-Trennung gewünscht ist, explizit `ToString("F1", CultureInfo.CurrentCulture)` verwenden.

### Metadaten-Strip im Detail-Bereich

Eine Zeile (mit Wrap, falls die Breite knapp wird), Trenner ` · `:

```xml
<TextBlock Style="{StaticResource DetailMetaText}" TextWrapping="Wrap">
  <Run Text="{Binding SelectedPhoto.DateTimeFormatted}"/>
  <Run Text=" · "/>
  <Run Text="{Binding SelectedPhoto.CoordinatesFormatted}"/>
  <Run Text=" · "/>
  <Run Text="{Binding SelectedPhoto.AltitudeFormatted}"/>
  <Run Text=" · "/>
  <Run Text="{Binding SelectedPhoto.PixelDimensionsFormatted}"/>
  <Run Text=" · "/>
  <Run Text="{Binding SelectedPhoto.FileSizeFormatted}"/>
</TextBlock>
```

Hilfs-Properties auf `PhotoViewModel`:

- `DateTimeFormatted` → `"12.04.2026 09:14"` (oder `"—"` falls null).
- `CoordinatesFormatted` → `"47.376541, 8.541234"` (oder `"—"`).
- `AltitudeFormatted` → `"412 m"` (oder `"—"`).

Fehlende Felder als `"—"` (Halbgeviertstrich), nicht weglassen — sonst wirken die Trennzeichen unsauber.

### Ortsname (separate Zeile, prominent)

Wie in Änderung 2 beschrieben — eine eigenständige Zeile in Akzentfarbe, **direkt unter dem Title**. Nicht im Metadaten-Strip versteckt, weil der Ortsname für den Nutzer beim Beschreiben der Reise die wichtigste Zusatzinformation ist.

### Akzeptanzkriterien Änderung 3

- Ein Test-Foto mit bekannter Dateigröße und Auflösung zeigt im Metadaten-Strip die korrekten Werte (z.B. `4.2 MB`, `4032 × 3024 px`).
- Ein Foto ohne EXIF-Pixelmaße zeigt `—` an dieser Stelle, ohne UI-Fehler.
- Nach dem Speichern einer 90°-Rotation (Schritt 4) zeigt der Strip die getauschten Pixelmaße an (Re-Scan oder gezielter Refresh des einzelnen Fotos).
- Der per Geocoding ermittelte Ortsname erscheint klar erkennbar als eigene Zeile im Detail-Bereich, nicht erst irgendwo im Metadaten-Strip.
- Fotos ohne ermittelten Ortsnamen zeigen die Ortszeile gar nicht an (kein Leereintrag).

---

## Was bewusst NICHT teil dieser Iteration ist

- Persistierung der neuen Metadaten in der CSV (sind aus den Dateien jederzeit ableitbar)
- Kameramodell, Blende, Belichtungszeit als zusätzliche Metadaten (kann später ergänzt werden, ähnliches Muster)
- Skalierbare Schriftgrößen über System-DPI hinaus (WPF macht das automatisch ausreichend gut)
- Vollbild-Modus für das Foto (Doppelklick auf Bild → Vollbild) — guter Kandidat für eine spätere Iteration
