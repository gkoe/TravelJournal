# Claude Code Anweisung — Schritt 4: Fokus-Fix, Bild-Rotation, Speicher-Warnung

## Ziel

Drei zusammenhängende Verbesserungen am Review-Flow der `TravelJournal.Wpf`-Anwendung:

1. **Fokus-Fix nach Enter** — nach dem State-Zyklus mit `Enter` darf die Auswahl nicht an den Listenanfang springen. Stattdessen bleibt entweder die aktuelle Auswahl bestehen, oder es wird automatisch zum nächsten passenden Foto in der gefilterten Liste gewechselt (üblicher Review-Workflow).
2. **Bild-Rotation per Tastatur** — `L` dreht das aktuell angezeigte Foto um 90° nach links, `R` um 90° nach rechts. Die Drehung ist zunächst nur eine vorgemerkte Anzeige-Transformation; die Originaldatei bleibt unangetastet.
3. **Rotation speichern + Warnung bei Navigation** — `S` schreibt die vorgemerkte Rotation in die JPG-Datei (verlustarmes Re-Encoding mit ImageSharp). Wechselt der Nutzer das aktive Foto, ohne gespeichert zu haben, erscheint eine Warn-Abfrage (Speichern/Verwerfen/Abbrechen).

## Kontext

Setzt auf Schritt 1–3 auf. Es werden keine CSV-Schema-Änderungen vorgenommen — die Rotation modifiziert direkt die Bilddatei.

## Neue NuGet-Abhängigkeit

`SixLabors.ImageSharp` in **`TravelJournal.Core`** ergänzen — dort liegt der Rotation-Service, damit er testbar und in späteren Tools (z.B. Web-Exporter) wiederverwendbar ist.

---

## Feature 1 — Fokus-Verhalten nach Enter korrigieren

### Diagnose

Der bisherige `CycleSelectedPhotoState`-Command ändert `SelectedPhoto.State`. Wenn der aktive Filter (`Open`/`Selected`/`Deselected`/`New`) den neuen State nicht mehr einschließt, fällt das Foto aus der `FilteredPhotos`-View heraus. WPF setzt die ListBox-Selektion daraufhin zurück und springt visuell an den Anfang der sichtbaren Liste.

### Lösung

Vor dem State-Wechsel die Position des aktuellen Fotos in der gefilterten Liste merken; nach dem Wechsel prüfen, ob das Foto noch in der View enthalten ist:

- **Ja, noch enthalten** → `SelectedPhoto` bleibt, Fokus stabil. Lediglich `ScrollIntoView` aufrufen, falls die Sortierung sich verschoben hat.
- **Nein, nicht mehr enthalten** → den **nächsten** Eintrag an derselben Index-Position selektieren (also das, was rückt nun an die Stelle des verschwundenen Items). Falls keiner mehr nachkommt: das letzte verbliebene Element selektieren. Falls die View leer ist: `SelectedPhoto = null`.

Das ergibt das übliche Verhalten in Review-Werkzeugen: man arbeitet eine Liste offener Fotos von oben nach unten ab, drückt `Enter` zum Markieren, und die Liste „verschiebt sich" automatisch zum nächsten unbearbeiteten Foto.

### Implementation in `MainViewModel`

```csharp
[RelayCommand(CanExecute = nameof(HasSelectedPhoto))]
private void CycleSelectedPhotoState()
{
    if (SelectedPhoto is null) return;

    var view = (CollectionView)FilteredPhotos;
    var photosBefore = view.Cast<PhotoViewModel>().ToList();
    var indexBefore = photosBefore.IndexOf(SelectedPhoto);

    SelectedPhoto.State = SelectedPhoto.State switch
    {
        PhotoState.None => PhotoState.Selected,
        PhotoState.Selected => PhotoState.Deselected,
        PhotoState.Deselected => PhotoState.None,
        _ => PhotoState.None
    };

    view.Refresh();

    var photosAfter = view.Cast<PhotoViewModel>().ToList();
    if (photosAfter.Contains(SelectedPhoto))
    {
        // Foto ist noch sichtbar — Fokus bleibt, nur ggf. nachscrollen
        ScrollSelectedIntoViewRequested?.Invoke();
    }
    else if (photosAfter.Count == 0)
    {
        SelectedPhoto = null;
    }
    else
    {
        // Nachfolger an derselben Position wählen, sonst letztes Item
        var newIndex = Math.Min(indexBefore, photosAfter.Count - 1);
        SelectedPhoto = photosAfter[newIndex];
        ScrollSelectedIntoViewRequested?.Invoke();
    }

    UpdateStatusText();
    DeselectAllOpenCommand.NotifyCanExecuteChanged();
}

public event Action? ScrollSelectedIntoViewRequested;
```

Im Code-Behind von `MainWindow.xaml.cs` an das Event hängen:

```csharp
public MainWindow(MainViewModel vm)
{
    InitializeComponent();
    DataContext = vm;
    vm.ScrollSelectedIntoViewRequested += () =>
    {
        if (PhotoList.SelectedItem is not null)
            PhotoList.ScrollIntoView(PhotoList.SelectedItem);
    };
}
```

### Akzeptanzkriterien Feature 1

- Filter steht auf „Offen", Liste enthält 20 offene Fotos. Cursor auf dem 5. Foto. `Enter` setzt es auf `Selected`. Die Liste enthält danach nur noch 19 Einträge, die Selektion springt automatisch auf das **neue** 5. Element (also das ehemalige 6.). Nicht an den Anfang.
- Filter steht auf „Alle". `Enter` ändert den State; das Foto bleibt sichtbar und selektiert; die Detailansicht zeigt unverändert dasselbe Foto.
- Filter steht auf „Offen", letztes verbliebenes offenes Foto wird per `Enter` zugewiesen → die Liste ist leer, Detailpanel zeigt den Hinweis „Kein Foto ausgewählt".

---

## Feature 2 — Bild-Rotation per L/R (vorgemerkt, in Anzeige sichtbar)

### Tastenbelegung

Solange die Galerie-`ListBox` den Fokus hat (gleicher Mechanismus wie Enter):

- `L` → Rotation nach links: `PendingRotation -= 90`
- `R` → Rotation nach rechts: `PendingRotation += 90`

`PendingRotation` wird auf den Bereich `[0, 360)` normalisiert. Mehrfaches Drücken kumuliert. `0` bedeutet keine ungespeicherte Änderung.

### Erweiterung in `PhotoViewModel`

```csharp
[ObservableProperty]
private int pendingRotation;

public bool HasPendingRotation => PendingRotation != 0;

partial void OnPendingRotationChanged(int value)
{
    OnPropertyChanged(nameof(HasPendingRotation));
}
```

### Erweiterung in `MainViewModel`

```csharp
[RelayCommand(CanExecute = nameof(HasSelectedPhoto))]
private void RotateLeft()
{
    if (SelectedPhoto is null) return;
    SelectedPhoto.PendingRotation = NormalizeAngle(SelectedPhoto.PendingRotation - 90);
}

[RelayCommand(CanExecute = nameof(HasSelectedPhoto))]
private void RotateRight()
{
    if (SelectedPhoto is null) return;
    SelectedPhoto.PendingRotation = NormalizeAngle(SelectedPhoto.PendingRotation + 90);
}

private static int NormalizeAngle(int deg) => ((deg % 360) + 360) % 360;
```

### KeyBindings an der `ListBox` ergänzen

```xml
<ListBox.InputBindings>
  <KeyBinding Key="Enter" Command="{Binding CycleSelectedPhotoStateCommand}" />
  <KeyBinding Key="L" Command="{Binding RotateLeftCommand}" />
  <KeyBinding Key="R" Command="{Binding RotateRightCommand}" />
  <KeyBinding Key="S" Command="{Binding SaveRotationCommand}" />
  <!-- bestehende 1/2/0-KeyBindings bleiben -->
</ListBox.InputBindings>
```

### Anzeige der vorgemerkten Rotation im Detail-Bereich

Auf das große `Image`-Element rechts eine `LayoutTransform` legen, die an `SelectedPhoto.PendingRotation` gebunden ist:

```xml
<Image Source="{Binding SelectedPhoto.LargeImage}" Stretch="Uniform">
  <Image.LayoutTransform>
    <RotateTransform Angle="{Binding SelectedPhoto.PendingRotation}" />
  </Image.LayoutTransform>
</Image>
```

`LayoutTransform` (nicht `RenderTransform`) ist wichtig, weil das Layout die rotierten Bounds berücksichtigen muss — sonst wird die Bildkante abgeschnitten.

Im Thumbnail in der linken Liste die Rotation **nicht** anwenden (Thumbnails sind nur Navigationshilfe, das vermeidet teures Neu-Rendern beim Tippen). Stattdessen ein kleines Rotation-Badge anzeigen, wenn `HasPendingRotation == true`, z.B. ein dezentes Symbol „↻" mit der Akzentfarbe.

### Akzeptanzkriterien Feature 2

- `R` im Galerie-Fokus rotiert das große Bild rechts sofort um 90° im Uhrzeigersinn. Vier mal `R` ist visuell wieder die Ausgangslage, `PendingRotation` ist `0`.
- `L` rotiert gegen den Uhrzeigersinn, sonst analog.
- Tastendruck in Title- oder Description-`TextBox` löst keine Rotation aus (KeyBinding sitzt nur an der `ListBox`).
- Wechsel auf ein anderes Foto per Pfeiltaste → siehe Feature 3 (Warnung), nach Bestätigung wird die Rotation des **vorherigen** Fotos verworfen oder gespeichert je nach Wahl.

---

## Feature 3 — `S` zum Speichern der Rotation + Warnung bei Navigation mit ungespeicherten Änderungen

### Speichern mit `S`

`S` ist explizit **nicht** dasselbe wie `Strg+S`. Bedeutung:

- `S` (im Galerie-Fokus) → schreibt die vorgemerkte Rotation des aktiven Fotos in die JPG-Datei und setzt `PendingRotation = 0`.
- `Strg+S` → speichert die `tour.csv` (bleibt aus Schritt 2 unverändert).

Wenn `PendingRotation == 0` ist `S` ein No-Op.

### Neuer Service `IImageRotator` in `TravelJournal.Core`

```csharp
public interface IImageRotator
{
    Task RotateAsync(string filePath, int degreesClockwise, CancellationToken ct = default);
}
```

Implementierung `ImageSharpImageRotator` mit `SixLabors.ImageSharp`:

- Datei laden (`Image.LoadAsync`).
- `image.Mutate(x => x.Rotate(degreesClockwise))` mit `RotateMode.Rotate90/180/270` für 90er-Schritte (verlustarm gegenüber freier Rotation).
- Re-Encoding als JPEG mit Qualität 92, EXIF-Metadaten erhalten (`EncoderOptions` und Original-Metadaten kopieren — ImageSharp übernimmt das standardmäßig, prüfen).
- Schreiben in eine temporäre Datei daneben, dann `File.Move` mit `overwrite: true` → atomar, kein halb geschriebenes JPG bei Crash.
- Wirft `ArgumentException` bei nicht durch 90 teilbaren Winkeln, `IOException` bei Schreibfehlern.

In `TravelJournal.Core.Tests`:

- Test, dass nach `RotateAsync(file, 90)` die Bildbreite/-höhe getauscht sind.
- Test, dass mehrfaches Rotieren um 90° insgesamt 360° wieder das Originalbild liefert (Pixel-Vergleich nicht nötig — Maße reichen).
- Test, dass eine ungültige Gradzahl (z.B. 45) eine `ArgumentException` wirft.

### Verkabelung in `MainViewModel`

```csharp
[RelayCommand(CanExecute = nameof(CanSaveRotation))]
private async Task SaveRotationAsync()
{
    if (SelectedPhoto is null || SelectedPhoto.PendingRotation == 0) return;

    IsBusy = true;
    try
    {
        await _imageRotator.RotateAsync(SelectedPhoto.FullPath, SelectedPhoto.PendingRotation);
        SelectedPhoto.PendingRotation = 0;
        // Thumbnail und LargeImage neu laden, damit die UI das gespeicherte Bild zeigt
        await SelectedPhoto.ReloadImagesAsync(_thumbnailLoader);
    }
    finally
    {
        IsBusy = false;
    }
}

private bool CanSaveRotation() => SelectedPhoto is { PendingRotation: not 0 };
```

`PhotoViewModel.ReloadImagesAsync` lädt Thumbnail (240px) und `LargeImage` (1600px) neu.

### Warnung bei Navigation mit ungespeicherter Rotation

Sobald der Nutzer das aktive Foto wechselt (Pfeiltasten, Mausklick auf anderes Thumbnail, Filter-Wechsel der das Foto verbirgt) und das aktuelle `SelectedPhoto.PendingRotation != 0` ist, soll vor dem Wechsel ein modaler Dialog erscheinen:

```
Titel: Rotation nicht gespeichert
Text:  Das Foto „DSC_0123.jpg" wurde gedreht, aber noch nicht gespeichert.
       
       [ Speichern ]   [ Verwerfen ]   [ Abbrechen ]
```

- **Speichern** → `SaveRotationAsync()` ausführen, dann zum neuen Foto wechseln.
- **Verwerfen** → `PendingRotation = 0`, dann zum neuen Foto wechseln. Kein Dateischreiben.
- **Abbrechen** → Auswahl bleibt auf dem alten Foto, Navigation wird verworfen.

### Implementation der Warnung

`SelectedPhoto`-Setter in `MainViewModel` zum Gatekeeper machen:

```csharp
private PhotoViewModel? _selectedPhoto;
public PhotoViewModel? SelectedPhoto
{
    get => _selectedPhoto;
    set
    {
        if (_selectedPhoto == value) return;
        if (_selectedPhoto is { PendingRotation: not 0 })
        {
            var decision = _confirmDialog.AskRotationSaveDecision(_selectedPhoto.Filename);
            switch (decision)
            {
                case RotationSaveDecision.Cancel:
                    OnPropertyChanged(nameof(SelectedPhoto)); // erzwingt UI-Reset auf altes Item
                    return;
                case RotationSaveDecision.Save:
                    _ = SaveRotationAsync(); // bewusst fire-and-forget, aber UI sperrt via IsBusy
                    break;
                case RotationSaveDecision.Discard:
                    _selectedPhoto.PendingRotation = 0;
                    break;
            }
        }
        _selectedPhoto = value;
        OnPropertyChanged();
        SaveRotationCommand.NotifyCanExecuteChanged();
        CycleSelectedPhotoStateCommand.NotifyCanExecuteChanged();
        RotateLeftCommand.NotifyCanExecuteChanged();
        RotateRightCommand.NotifyCanExecuteChanged();
    }
}
```

Neuer Service in `TravelJournal.Wpf/Services/`:

```csharp
public enum RotationSaveDecision { Save, Discard, Cancel }

public interface IConfirmDialogService
{
    RotationSaveDecision AskRotationSaveDecision(string filename);
}
```

Implementation `ConfirmDialogService` nutzt `MessageBox.Show(..., MessageBoxButton.YesNoCancel, MessageBoxImage.Warning)`:

- `Yes` → `Save`
- `No` → `Discard`
- `Cancel` → `Cancel`

Buttons im Dialog beschriften nicht über Standard-MessageBox — wenn die deutsche Beschriftung wichtig ist, ein eigenes kleines `Window` als Custom-Dialog mit drei `Button`s anlegen. Empfehlung: zunächst Standard-MessageBox, Custom-Dialog später.

In der DI-Registrierung von `App.xaml.cs`:

```csharp
services.AddSingleton<IConfirmDialogService, ConfirmDialogService>();
services.AddSingleton<IImageRotator, ImageSharpImageRotator>();
```

### Akzeptanzkriterien Feature 3

- `R` zwei mal drücken → großes Bild ist um 180° gedreht, Thumbnail-Badge „↻" sichtbar.
- `S` drücken → Originaldatei wird umgeschrieben (Maße tauschen sich nach 90°/270° physisch in der Datei), `PendingRotation` ist wieder `0`, Badge verschwindet.
- Datei-Encoding bleibt JPEG, EXIF-Daten (insbesondere DateTimeOriginal und GPS) bleiben erhalten — `ExifReaderService` liefert nach dem Speichern weiterhin die korrekten Metadaten.
- `R` drücken, dann Pfeiltaste runter → Warn-Dialog erscheint. „Speichern" speichert und navigiert. „Verwerfen" navigiert ohne Speichern. „Abbrechen" lässt Selektion stehen.
- Dialog erscheint **nicht**, wenn `PendingRotation == 0` ist (also kein unnötiges Pop-up bei normaler Navigation).
- Beim Schließen des Fensters mit ungespeicherter Rotation erscheint derselbe Dialog (im `Window.Closing`-Event prüfen, bei `Cancel` `e.Cancel = true`).

---

## Was bewusst NICHT teil dieser Iteration ist

- Verlustfreie JPEG-Rotation per `jpegtran` (würde externe Tool-Abhängigkeit bedeuten — ImageSharp re-encodet stattdessen mit Qualität 92, was für eine einmalige 90°-Drehung optisch unauffällig ist)
- Modifikation der EXIF-`Orientation` als Alternative zum echten Pixel-Rotieren
- Undo-Stack für Rotationen
- Rotation in beliebigen Winkeln
- Speichern aller offenen Rotationen mehrerer Fotos in einem Bulk-Vorgang
