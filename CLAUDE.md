# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build, Test, Run

```powershell
# Build (alle Projekte)
dotnet build

# Tests ausführen
dotnet test

# Einzelnen Test ausführen
dotnet test --filter "FullyQualifiedName~StopDetectorTests"

# WPF-App starten (Debug)
dotnet run --project src/TravelJournal.Wpf/TravelJournal.Wpf.csproj

# Release-Build (für Deployment)
dotnet build src/TravelJournal.Wpf/TravelJournal.Wpf.csproj -c Release
```

## Projektstruktur

```
src/
  TravelJournal.Core/        # Domänenlogik, kein WPF
  TravelJournal.WebExporter/ # HTML/CSS/JS-Export, kein WPF
  TravelJournal.Wpf/         # UI: ViewModels, Views, Services, App-Bootstrap
tests/
  TravelJournal.Core.Tests/
  TravelJournal.WebExporter.Tests/
```

`TravelJournal.Core` und `TravelJournal.WebExporter` sind reine .NET-Bibliotheken ohne WPF-Abhängigkeit — alle UI-spezifischen Services leben ausschließlich in `TravelJournal.Wpf`.

## Architektur-Überblick

### Datenmodell
`Photo` (Core/Models) ist das zentrale Modell. `PhotoState` (None/Selected/Deselected/Start/End) steuert, welche Fotos in Präsentation und Export erscheinen. `EntryType` unterscheidet echte Fotos von generierten Karten-PNGs. Der Zustand wird in `tour.csv` neben den Fotos gespeichert (kein AppData, kein Registry).

### WPF-Schicht (MVVM mit CommunityToolkit.Mvvm)
- `MainViewModel` ist der zentrale ViewModel (~1000 Zeilen). Alle Commands sind `[RelayCommand]`-Attribute.
- Die Galerie-Liste ist ein `ICollectionView` über `GalleryItems` (`ObservableCollection<IGalleryItem>`). Nach jeder Statusänderung muss `GalleryItemsView.Refresh()` aufgerufen werden.
- Wenn Commands aktiviert/deaktiviert werden sollen, nach jeder relevanten Zustandsänderung **explizit** `XyzCommand.NotifyCanExecuteChanged()` aufrufen — insbesondere in `AfterStateChange()` und `CycleSelectedPhotoState()`.
- Nach programmatischer Selektion (z.B. nach Löschen) `ScrollSelectedIntoViewRequested?.Invoke()` aufrufen, damit das neue `ListBoxItem` Tastatur-Fokus erhält.

### _wpftmp.csproj-Einschränkung (kritisch)
Der WPF-BAML-Compiler erzeugt intern `TravelJournal.Wpf_xxx_wpftmp.csproj`, das alle `.cs`-Dateien kompiliert, aber **keine transitive NuGet-Referenz auf `Wpf.Ui.dll`** besitzt. Daher gilt:
- **`Wpf.Ui.Controls.*` niemals in C#-Code verwenden** — nur in XAML
- `System.Windows.MessageBox` statt `Wpf.Ui.Controls.MessageBox`
- `System.IO.Path`, `System.IO.File`, `System.IO.Directory` **vollständig qualifizieren** in `App.xaml.cs`

### Karten-Pipeline
`MapRendererFactory` (Wpf/Services) lädt die Konfiguration aus `appsettings.json` und entscheidet, ob MapTiler oder OSM-Tiles verwendet werden. `TileMapRenderer` (Core) lädt Tiles parallel, cached sie in `.tile-cache/` neben den Fotos, und rendert via ImageSharp. `StopDetector` findet Stopps anhand von Zeitlücken zwischen GPS-Fotos (Standard: 30 Min.).

### Logging
Serilog, konfiguriert über `appsettings.json` (`Serilog`-Sektion). Tägliches Rolling-File in `%LocalAppData%\TravelJournal\logs\`. Globale Handler in `App.xaml.cs` für unbehandelte UI-, Background- und Task-Exceptions.

### appsettings.json
Liegt neben der EXE und ist **nicht** in der Repository-Konfiguration für Releases — das Release-ZIP enthält `appsettings.example.json` (umbenannt zu `appsettings.json`). Der API-Key kann auch über `MAPTILER_API_KEY`-Umgebungsvariable gesetzt werden.

## Deployment

GitHub Actions (`.github/workflows/release.yml`) baut bei jedem `v*`-Tag einen self-contained win-x64-Release und stellt ihn als GitHub-Release-Asset bereit:

```powershell
git tag v1.2.3
git push origin v1.2.3
```
