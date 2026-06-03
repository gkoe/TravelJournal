using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TravelJournal.Core.MapRendering;
using TravelJournal.Core.Models;
using TravelJournal.Core.Presentation;
using TravelJournal.Core.Services;
using TravelJournal.Wpf.Services;
using TravelJournal.Wpf.Views;
using Microsoft.Extensions.Logging;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using Int32Rect = System.Windows.Int32Rect;

namespace TravelJournal.Wpf.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ILogger<MainViewModel>  _log;
    private readonly IFolderDialogService    _folderDialog;
    private readonly IThumbnailLoader        _thumbnailLoader;
    private readonly PhotoFolderScanner      _scanner;
    private readonly TourCsvWriter           _csvWriter;
    private readonly IReverseGeocoderFactory _geoFactory;
    private readonly IConfirmDialogService   _confirmDialog;
    private readonly IImageRotator           _imageRotator;
    private readonly ExifReaderService       _exifReader;
    private readonly IMapRendererFactory     _mapRendererFactory;
    private readonly UserSettingsService     _userSettings;
    private readonly ImageCropService        _cropService;
    private readonly IWebExportService       _webExport;
    private readonly IHeicConverter          _heicConverter;
    private readonly IPhotoRenamer           _photoRenamer;

    private readonly DispatcherTimer    _autoSaveTimer;
    private readonly SemaphoreSlim      _saveLock         = new(1, 1);
    private readonly List<PhotoViewModel> _subscribedPhotos = new();
    private DateTime?                   _lastAutoSavedAt;

    private static readonly HashSet<string> CsvRelevantProperties = new(StringComparer.Ordinal)
    {
        nameof(PhotoViewModel.State),
        nameof(PhotoViewModel.Title),
        nameof(PhotoViewModel.Description),
        nameof(PhotoViewModel.Location),
    };

    private MapRenderingOptions _baseOptions = new();

    private CancellationTokenSource? _geocodingCts;
    private CancellationTokenSource? _mapRenderCts;

    // ── Selected item ─────────────────────────────────────────

    private IGalleryItem? _selectedGalleryItem;
    public IGalleryItem? SelectedGalleryItem
    {
        get => _selectedGalleryItem;
        set
        {
            if (_selectedGalleryItem == value) return;
            if (_selectedGalleryItem is PhotoViewModel { PendingRotation: not 0 } prev)
            {
                var decision = _confirmDialog.AskRotationSaveDecision(prev.Filename);
                switch (decision)
                {
                    case RotationSaveDecision.Cancel:
                        OnPropertyChanged(nameof(SelectedGalleryItem));
                        return;
                    case RotationSaveDecision.Save:
                        _ = SaveRotationAsync();
                        break;
                    case RotationSaveDecision.Discard:
                        prev.PendingRotation = 0;
                        break;
                }
            }
            _selectedGalleryItem = value;
            CropRect = Int32Rect.Empty;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedPhoto));
            CycleSelectedPhotoStateCommand.NotifyCanExecuteChanged();
            RotateLeftCommand.NotifyCanExecuteChanged();
            RotateRightCommand.NotifyCanExecuteChanged();
            SaveRotationCommand.NotifyCanExecuteChanged();
            GenerateMapsCommand.NotifyCanExecuteChanged();
            if (value is PhotoViewModel pvm && pvm.LargeImage == null && !pvm.IsMissing)
                _ = LoadLargeImageAsync(pvm);
        }
    }

    public PhotoViewModel? SelectedPhoto => _selectedGalleryItem as PhotoViewModel;

    // ── Observable properties ─────────────────────────────────

    [ObservableProperty] private string?     _currentFolder;
    [ObservableProperty] private PhotoFilter _activeFilter = PhotoFilter.All;
    [ObservableProperty] private string      _statusText   = "Ordner öffnen um zu beginnen";
    [ObservableProperty] private bool        _isBusy;
    [ObservableProperty] private bool        _isGeneratingMaps;
    [ObservableProperty] private bool        _isGeocodingRunning;
    [ObservableProperty] private string?     _backgroundActivityText;

    [ObservableProperty] private string    _selectedMapStyleId   = "outdoor-v2";
    [ObservableProperty] private string    _selectedLanguage     = "de";
    [ObservableProperty] private int       _boundsPaddingPercent = 12;
    [ObservableProperty] private Int32Rect _cropRect             = Int32Rect.Empty;

    // ── Umbenennen-Konfiguration ──────────────────────────────
    [ObservableProperty] private string _renamePrefix   = string.Empty;
    [ObservableProperty] private string _renameTemplate = RenameOptions.DefaultTemplate;

    partial void OnRenamePrefixChanged(string value)
    {
        SaveUserSettings();
        OnPropertyChanged(nameof(RenamePreview));
    }

    partial void OnRenameTemplateChanged(string value)
    {
        SaveUserSettings();
        OnPropertyChanged(nameof(RenamePreview));
    }

    /// <summary>Beispielhafter Dateiname für die aktuelle Vorlage/den Präfix.</summary>
    public string RenamePreview
    {
        get
        {
            var sample = new DateTime(2026, 4, 27, 12, 22, 57);
            return CurrentRenameOptions.BuildBaseName(sample, "Rhodos-Stadt") + ".jpg";
        }
    }

    private RenameOptions CurrentRenameOptions => new(RenamePrefix ?? string.Empty, RenameTemplate);

    partial void OnCropRectChanged(Int32Rect value) => ConfirmCropCommand.NotifyCanExecuteChanged();

    public IReadOnlyList<MapStyleInfo> AvailableMapStyles  => MapTilerStyles.Curated;
    public IReadOnlyList<string>       AvailableLanguages  => MapTilerStyles.SupportedLanguages;

    partial void OnSelectedMapStyleIdChanged(string value)   => SaveUserSettings();
    partial void OnSelectedLanguageChanged(string value)     => SaveUserSettings();
    partial void OnBoundsPaddingPercentChanged(int value)    => SaveUserSettings();

    public string GeocodingButtonText =>
        IsGeocodingRunning ? "Abbruch Ortsermittlung" : "Orte ermitteln";

    partial void OnIsGeocodingRunningChanged(bool value) =>
        OnPropertyChanged(nameof(GeocodingButtonText));

    // ── Collections ───────────────────────────────────────────

    public ObservableCollection<PhotoViewModel>    Photos        { get; } = new();
    public ObservableCollection<HeicItemViewModel> HeicCandidates { get; } = new();
    public ObservableCollection<IGalleryItem>      GalleryItems  { get; } = new();
    public ICollectionView GalleryItemsView { get; }

    public event Action? ScrollSelectedIntoViewRequested;

    public MainViewModel(
        ILogger<MainViewModel>  log,
        IFolderDialogService    folderDialog,
        IThumbnailLoader        thumbnailLoader,
        PhotoFolderScanner      scanner,
        TourCsvWriter           csvWriter,
        IReverseGeocoderFactory geoFactory,
        IConfirmDialogService   confirmDialog,
        IImageRotator           imageRotator,
        ExifReaderService       exifReader,
        IMapRendererFactory     mapRendererFactory,
        UserSettingsService     userSettings,
        ImageCropService        cropService,
        IWebExportService       webExport,
        IHeicConverter          heicConverter,
        IPhotoRenamer           photoRenamer)
    {
        _log                = log;
        _folderDialog       = folderDialog;
        _thumbnailLoader    = thumbnailLoader;
        _scanner            = scanner;
        _csvWriter          = csvWriter;
        _geoFactory         = geoFactory;
        _confirmDialog      = confirmDialog;
        _imageRotator       = imageRotator;
        _exifReader         = exifReader;
        _mapRendererFactory = mapRendererFactory;
        _userSettings       = userSettings;
        _cropService        = cropService;
        _webExport          = webExport;
        _heicConverter      = heicConverter;
        _photoRenamer       = photoRenamer;

        _autoSaveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _autoSaveTimer.Tick += async (_, _) =>
        {
            _autoSaveTimer.Stop();
            await DoAutoSaveAsync();
        };

        _baseOptions = _mapRendererFactory.LoadBaseOptions();

        var saved = _userSettings.Load();
        _selectedMapStyleId   = saved.MapStyleId;
        _selectedLanguage     = saved.Language;
        _boundsPaddingPercent = saved.BoundsPaddingPercent;
        _renamePrefix         = saved.RenamePrefix   ?? string.Empty;
        _renameTemplate       = string.IsNullOrWhiteSpace(saved.RenameTemplate)
            ? RenameOptions.DefaultTemplate
            : saved.RenameTemplate;

        GalleryItemsView = CollectionViewSource.GetDefaultView(GalleryItems);
        GalleryItemsView.Filter = item => item is IGalleryItem gi && gi.MatchesFilter(ActiveFilter);
        GalleryItemsView.SortDescriptions.Add(
            new SortDescription(nameof(IGalleryItem.EffectiveDateTime), ListSortDirection.Ascending));
    }

    partial void OnActiveFilterChanged(PhotoFilter value) => GalleryItemsView.Refresh();

    partial void OnIsBusyChanged(bool value)
    {
        GenerateMapsCommand.NotifyCanExecuteChanged();
        ExportWebPresentationCommand.NotifyCanExecuteChanged();
        ExportPhotosCommand.NotifyCanExecuteChanged();
        ConvertHeicCommand.NotifyCanExecuteChanged();
        RenamePhotosCommand.NotifyCanExecuteChanged();
        ClearAllLocationsCommand.NotifyCanExecuteChanged();
    }

    // ── Scan / Folder ──────────────────────────────────────────

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        var folder = _folderDialog.PickFolder(CurrentFolder);
        if (folder == null) return;
        CurrentFolder = folder;
        RescanCommand.NotifyCanExecuteChanged();
        ToggleGeocodingCommand.NotifyCanExecuteChanged();
        GenerateMapsCommand.NotifyCanExecuteChanged();
        await ScanInternalAsync(folder);
    }

    [RelayCommand(CanExecute = nameof(HasFolder))]
    private async Task RescanAsync()
    {
        if (CurrentFolder == null) return;
        await ScanInternalAsync(CurrentFolder);
    }

    // ── Auto-Save ────────────────────────────────────────────

    public bool HasPendingAutoSave => _autoSaveTimer.IsEnabled;

    public async Task FlushAutoSaveAsync()
    {
        _autoSaveTimer.Stop();
        await DoAutoSaveAsync();
    }

    private void RequestAutoSave()
    {
        if (string.IsNullOrEmpty(CurrentFolder)) return;
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
    }

    private void OnPhotoPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != null && CsvRelevantProperties.Contains(e.PropertyName))
        {
            RequestAutoSave();
            if (e.PropertyName == nameof(PhotoViewModel.Location))
            {
                GenerateMapsCommand.NotifyCanExecuteChanged();
                ClearAllLocationsCommand.NotifyCanExecuteChanged();
            }
        }
    }

    private async Task DoAutoSaveAsync()
    {
        if (string.IsNullOrEmpty(CurrentFolder)) return;
        if (!await _saveLock.WaitAsync(0)) return;
        try
        {
            var csvPath = System.IO.Path.Combine(CurrentFolder, "tour.csv");
            var entries = AllCsvEntries().ToList();
            await Task.Run(() => _csvWriter.Write(csvPath, entries));
            _lastAutoSavedAt       = DateTime.Now;
            BackgroundActivityText = $"Gespeichert · {_lastAutoSavedAt:HH:mm:ss}";
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Auto-Save fehlgeschlagen");
            BackgroundActivityText = $"Speichern fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            _saveLock.Release();
        }
    }

    // ── State-Commands ────────────────────────────────────────

    [RelayCommand]
    private void SetStateSelected(PhotoViewModel? vm)
    {
        var target = vm ?? SelectedPhoto;
        if (target == null) return;
        target.State = PhotoState.Selected;
        AfterStateChange();
    }

    [RelayCommand]
    private void SetStateDeselected(PhotoViewModel? vm)
    {
        var target = vm ?? SelectedPhoto;
        if (target == null) return;
        target.State = PhotoState.Deselected;
        AfterStateChange();
    }

    [RelayCommand]
    private void SetStateNone(PhotoViewModel? vm)
    {
        var target = vm ?? SelectedPhoto;
        if (target == null) return;
        target.State = PhotoState.None;
        AfterStateChange();
    }

    [RelayCommand]
    private void SetStateStart(PhotoViewModel? vm)
    {
        var target = vm ?? SelectedPhoto;
        if (target == null) return;
        target.State = PhotoState.Start;
        AfterStateChange();
    }

    [RelayCommand]
    private void SetStateEnd(PhotoViewModel? vm)
    {
        var target = vm ?? SelectedPhoto;
        if (target == null) return;
        target.State = PhotoState.End;
        AfterStateChange();
    }

    // ── Bulk Deselect / Select ────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanDeselectAllOpen))]
    private void DeselectAllOpen()
    {
        foreach (var photo in Photos.Where(p => p.State == PhotoState.None))
            photo.State = PhotoState.Deselected;
        AfterStateChange();
    }

    private bool CanDeselectAllOpen() => Photos.Any(p => p.State == PhotoState.None);

    [RelayCommand(CanExecute = nameof(CanSelectAllOpen))]
    private void SelectAllOpen()
    {
        foreach (var photo in Photos.Where(p => p.State == PhotoState.None))
            photo.State = PhotoState.Selected;
        AfterStateChange();
    }

    private bool CanSelectAllOpen() => Photos.Any(p => p.State == PhotoState.None);

    // ── Cycle State ───────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(HasSelectedPhoto))]
    private void CycleSelectedPhotoState()
    {
        if (SelectedPhoto is null) return;

        var itemsBefore = GalleryItemsView.Cast<IGalleryItem>().ToList();
        var indexBefore = itemsBefore.IndexOf(SelectedPhoto);

        SelectedPhoto.State = SelectedPhoto.State switch
        {
            PhotoState.None       => PhotoState.Selected,
            PhotoState.Selected   => PhotoState.Deselected,
            PhotoState.Deselected => PhotoState.None,
            _                     => PhotoState.None
        };

        GalleryItemsView.Refresh();

        var itemsAfter = GalleryItemsView.Cast<IGalleryItem>().ToList();
        if (itemsAfter.Contains(SelectedPhoto))
        {
            ScrollSelectedIntoViewRequested?.Invoke();
        }
        else if (itemsAfter.Count == 0)
        {
            SelectedGalleryItem = null;
        }
        else
        {
            var newIndex = Math.Min(indexBefore, itemsAfter.Count - 1);
            SelectedGalleryItem = itemsAfter[newIndex];
            ScrollSelectedIntoViewRequested?.Invoke();
        }

        UpdateStatusText();
        DeselectAllOpenCommand.NotifyCanExecuteChanged();
        SelectAllOpenCommand.NotifyCanExecuteChanged();
        GenerateMapsCommand.NotifyCanExecuteChanged();
        StartPresentationCommand.NotifyCanExecuteChanged();
        ExportWebPresentationCommand.NotifyCanExecuteChanged();
        ExportPhotosCommand.NotifyCanExecuteChanged();
    }

    // ── Rotation ──────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(HasSelectedPhoto))]
    private void RotateLeft()
    {
        if (SelectedPhoto is null) return;
        SelectedPhoto.PendingRotation = NormalizeAngle(SelectedPhoto.PendingRotation - 90);
        SaveRotationCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedPhoto))]
    private void RotateRight()
    {
        if (SelectedPhoto is null) return;
        SelectedPhoto.PendingRotation = NormalizeAngle(SelectedPhoto.PendingRotation + 90);
        SaveRotationCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSaveRotation))]
    private async Task SaveRotationAsync()
    {
        if (SelectedPhoto is null || SelectedPhoto.PendingRotation == 0) return;

        IsBusy = true;
        try
        {
            await _imageRotator.RotateAsync(SelectedPhoto.FullPath, SelectedPhoto.PendingRotation);
            SelectedPhoto.PendingRotation = 0;
            await SelectedPhoto.ReloadImagesAsync(_thumbnailLoader);

            var updatedMeta = await Task.Run(() => _exifReader.ReadMetadata(SelectedPhoto.FullPath));
            SelectedPhoto.RefreshDimensions(updatedMeta.PixelWidth, updatedMeta.PixelHeight);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSaveRotation() => SelectedPhoto is { PendingRotation: not 0 };

    // ── Geocoding (asynchron, nicht-blockierend) ──────────────

    [RelayCommand(CanExecute = nameof(HasFolder))]
    private async Task ToggleGeocodingAsync()
    {
        if (IsGeocodingRunning)
        {
            _geocodingCts?.Cancel();
            return;
        }

        if (CurrentFolder == null) return;

        _geocodingCts = new CancellationTokenSource();
        var ct        = _geocodingCts.Token;
        IsGeocodingRunning = true;

        try
        {
            var geocoder = _geoFactory.CreateForFolder(CurrentFolder);
            var queue    = Photos
                .Where(p => p.State == PhotoState.Selected
                         && p.Latitude.HasValue
                         && p.Longitude.HasValue
                         && string.IsNullOrEmpty(p.Location))
                .ToList();

            if (queue.Count == 0)
            {
                BackgroundActivityText = "Alle ausgewählten Fotos haben bereits einen Ort.";
                return;
            }

            int resolved = 0;
            foreach (var photo in queue)
            {
                ct.ThrowIfCancellationRequested();
                var location = await geocoder.ResolveAsync(photo.Latitude!.Value, photo.Longitude!.Value, ct);
                if (!string.IsNullOrEmpty(location))
                {
                    photo.Location         = location;
                    BackgroundActivityText = $"Ort ermittelt: {location}";
                    resolved++;
                }
            }

            BackgroundActivityText = $"Ortsermittlung abgeschlossen ({resolved} von {queue.Count} Fotos).";

            var csvPath = System.IO.Path.Combine(CurrentFolder, "tour.csv");
            await Task.Run(() => _csvWriter.Write(csvPath, AllCsvEntries()), CancellationToken.None);
            BackgroundActivityText += " CSV gespeichert.";
        }
        catch (OperationCanceledException)
        {
            BackgroundActivityText = "Ortsermittlung abgebrochen.";
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Ortsermittlung fehlgeschlagen");
            BackgroundActivityText = $"Ortsermittlung fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            IsGeocodingRunning = false;
            _geocodingCts?.Dispose();
            _geocodingCts = null;
        }
    }

    [RelayCommand(CanExecute = nameof(CanClearAllLocations))]
    private async Task ClearAllLocationsAsync()
    {
        if (CurrentFolder is null) return;

        var affected = Photos.Where(p => !p.IsMapPhoto && !string.IsNullOrEmpty(p.Location)).ToList();
        if (affected.Count == 0)
        {
            BackgroundActivityText = "Es sind keine Orte gesetzt.";
            return;
        }

        var confirm = System.Windows.MessageBox.Show(
            $"Bei {affected.Count} Foto(s) wird der Ort gelöscht.\n\n" +
            "Karten bleiben unangetastet. Fortfahren?",
            "Alle Orte löschen",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        foreach (var photo in affected)
            photo.Location = null;

        var csvPath = System.IO.Path.Combine(CurrentFolder, "tour.csv");
        await Task.Run(() => _csvWriter.Write(csvPath, AllCsvEntries()), CancellationToken.None);

        GenerateMapsCommand.NotifyCanExecuteChanged();
        BackgroundActivityText = $"Orte gelöscht ({affected.Count} Foto(s)). CSV gespeichert.";
    }

    private bool CanClearAllLocations() =>
        !IsBusy && !string.IsNullOrEmpty(CurrentFolder)
        && Photos.Any(p => !p.IsMapPhoto && !string.IsNullOrEmpty(p.Location));

    // ── Foto löschen ─────────────────────────────────────────

    [RelayCommand]
    private async Task DeleteSelectedPhotoAsync()
    {
        if (SelectedGalleryItem is not IGalleryItem item) return;

        var viewItems  = GalleryItemsView.Cast<IGalleryItem>().ToList();
        var currentIdx = viewItems.IndexOf(item);
        var isMissing  = item is PhotoViewModel { IsMissing: true };

        if (!isMissing)
        {
            try { System.IO.File.Delete(item.FullPath); }
            catch (Exception ex)
            {
                _log.LogError(ex, "Foto konnte nicht gelöscht werden: {Path}", item.FullPath);
                StatusText = $"Löschen fehlgeschlagen: {ex.Message}";
                return;
            }
        }

        if (item is PhotoViewModel photo)
        {
            Photos.Remove(photo);
            // Unsubscribe from property change events
            photo.PropertyChanged -= OnPhotoPropertyChanged;
            _subscribedPhotos.Remove(photo);
        }

        GalleryItems.Remove(item);
        GalleryItemsView.Refresh();

        var newItems = GalleryItemsView.Cast<IGalleryItem>().ToList();
        SelectedGalleryItem = newItems.Count > 0
            ? newItems[Math.Min(currentIdx, newItems.Count - 1)]
            : null;
        ScrollSelectedIntoViewRequested?.Invoke();

        if (CurrentFolder != null && isMissing)
        {
            var csvPath = System.IO.Path.Combine(CurrentFolder, "tour.csv");
            await Task.Run(() => _csvWriter.Write(csvPath, AllCsvEntries()));
        }

        UpdateStatusText();
        GenerateMapsCommand.NotifyCanExecuteChanged();
        StartPresentationCommand.NotifyCanExecuteChanged();
    }

    // ── Zuschneiden ──────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanConfirmCrop))]
    private async Task ConfirmCropAsync()
    {
        if (SelectedPhoto == null || CropRect.IsEmpty) return;

        var path = SelectedPhoto.FullPath;
        var rect = CropRect;
        CropRect = Int32Rect.Empty;

        IsBusy = true;
        try
        {
            await _cropService.CropAndSaveAsync(path, rect.X, rect.Y, rect.Width, rect.Height);
            await SelectedPhoto.ReloadImagesAsync(_thumbnailLoader);
            var meta = await Task.Run(() => _exifReader.ReadMetadata(path));
            SelectedPhoto.RefreshDimensions(meta.PixelWidth, meta.PixelHeight);
            StatusText = "Zuschnitt gespeichert.";
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Zuschneiden fehlgeschlagen");
            StatusText = $"Zuschneiden fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanConfirmCrop() => !CropRect.IsEmpty && SelectedPhoto != null && !IsBusy;

    // ── Präsentation starten ─────────────────────────────────

    private static readonly TimeSpan StartSlideDuration = TimeSpan.FromSeconds(10);

    [RelayCommand(CanExecute = nameof(CanStartPresentation))]
    private void StartPresentation()
    {
        // ── Startfolie(n) ─────────────────────────────────────
        var startSlides = Photos
            .Where(p => p.State == PhotoState.Start)
            .OrderBy(p => p.DateTime)
            .Select(p => (IPresentationItem)new PhotoPresentationItem(
                p.DateTime ?? DateTime.MinValue, p.FullPath, p.Location, p.Title)
                { OverrideDuration = StartSlideDuration });

        // ── Hauptteil: ausgewählte Fotos + Karten ─────────────
        var mainItems = new List<IPresentationItem>();
        mainItems.AddRange(Photos
            .Where(p => p.State == PhotoState.Selected && p.DateTime.HasValue && !p.IsMapPhoto)
            .Select(p => (IPresentationItem)new PhotoPresentationItem(
                p.DateTime!.Value, p.FullPath, p.Location, p.Title)));
        mainItems.AddRange(Photos
            .Where(p => p.IsMapPhoto)
            .Select(m => (IPresentationItem)new PhotoPresentationItem(
                m.EffectiveDateTime, m.FullPath, m.Location, m.Title)));
        var sortedMain = mainItems.OrderBy(i => i.EffectiveDateTime);

        // ── Schlussfolie(n) ───────────────────────────────────
        var endSlides = Photos
            .Where(p => p.State == PhotoState.End)
            .OrderBy(p => p.DateTime)
            .Select(p => (IPresentationItem)new PhotoPresentationItem(
                p.DateTime ?? DateTime.MaxValue, p.FullPath, p.Location, p.Title)
                { IsEndSlide = true });

        var playlist = startSlides.Concat(sortedMain).Concat(endSlides).ToList();
        if (playlist.Count == 0) return;

        var presentationVm = new PresentationViewModel(playlist);
        var window         = new PresentationWindow(presentationVm);
        window.Owner = Application.Current.MainWindow;
        window.ShowDialog();
    }

    private bool CanStartPresentation() =>
        Photos.Any(p => p.State is PhotoState.Selected or PhotoState.Start or PhotoState.End)
        || Photos.Any(p => p.IsMapPhoto);

    // ── Web-Präsentation exportieren ─────────────────────────

    [RelayCommand(CanExecute = nameof(CanExportWebPresentation))]
    private async Task ExportWebPresentationAsync()
    {
        if (CurrentFolder is null) return;

        var outputFolder = System.IO.Path.Combine(CurrentFolder, "webpresentation");

        if (System.IO.Directory.Exists(outputFolder))
            System.IO.Directory.Delete(outputFolder, recursive: true);

        IsBusy = true;
        try
        {
            var progress = new Progress<TravelJournal.WebExporter.WebExportProgress>(p =>
            {
                StatusText = p.Stage switch
                {
                    "photos"    => $"Fotos optimieren {p.Current}/{p.Total} …",
                    "maps"      => $"Karten kopieren {p.Current}/{p.Total} …",
                    "templates" => "Templates schreiben …",
                    _           => p.Message ?? ""
                };
            });

            var count = await _webExport.ExportAsync(CurrentFolder, outputFolder, progress, CancellationToken.None);
            StatusText = count == 0
                ? "Keine Inhalte zum Exportieren gefunden."
                : $"Web-Präsentation erstellt ({count} Items).";

            if (count > 0)
            {
                var indexPath = System.IO.Path.Combine(outputFolder, "index.html");
                System.Diagnostics.Process.Start(
                    new System.Diagnostics.ProcessStartInfo(indexPath) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Web-Export fehlgeschlagen");
            StatusText = $"Web-Export fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanExportWebPresentation() =>
        !IsBusy && !string.IsNullOrEmpty(CurrentFolder)
        && (Photos.Any(p => p.State == PhotoState.Selected) || Photos.Any(p => p.IsMapPhoto));

    // ── Fotos exportieren ────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanExportPhotos))]
    private async Task ExportPhotosAsync()
    {
        var selected = Photos.Where(p => p.State == PhotoState.Selected).ToList();
        if (selected.Count == 0) return;

        var dest = _folderDialog.PickFolder(null);
        if (dest is null) return;

        IsBusy = true;
        var copied  = 0;
        var skipped = 0;
        try
        {
            for (var i = 0; i < selected.Count; i++)
            {
                var photo = selected[i];
                BackgroundActivityText = $"Exportiere {i + 1}/{selected.Count}: {photo.Filename} …";

                var target = System.IO.Path.Combine(dest, photo.Filename);
                if (System.IO.File.Exists(target))
                {
                    skipped++;
                    continue;
                }

                await Task.Run(() => System.IO.File.Copy(photo.FullPath, target));
                copied++;
            }

            StatusText = skipped == 0
                ? $"{copied} Foto(s) exportiert."
                : $"{copied} Foto(s) exportiert, {skipped} bereits vorhanden übersprungen.";
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Foto-Export fehlgeschlagen");
            StatusText = $"Export fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            BackgroundActivityText = null;
            IsBusy = false;
        }
    }

    private bool CanExportPhotos() =>
        !IsBusy && Photos.Any(p => p.State == PhotoState.Selected);

    // ── HEIC konvertieren ────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanConvertHeic))]
    private async Task ConvertHeicAsync()
    {
        if (CurrentFolder is null || HeicCandidates.Count == 0) return;

        var total = HeicCandidates.Count;

        var confirm = System.Windows.MessageBox.Show(
            $"Es werden {total} HEIC-Datei(en) nach JPEG konvertiert.\nDie HEIC-Originale werden anschließend gelöscht.\nFortfahren?",
            "HEIC importieren",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);
        if (confirm != System.Windows.MessageBoxResult.Yes) return;

        var options   = new HeicConversionOptions { JpegQuality = 90 };
        var converted = 0;

        IsBusy = true;
        try
        {
            foreach (var item in HeicCandidates.ToList())
            {
                BackgroundActivityText =
                    $"Konvertiere {item.Filename} ({converted + 1}/{total}) …";
                try
                {
                    await _heicConverter.ConvertAsync(item.FullPath, options);
                    converted++;
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "HEIC-Konvertierung fehlgeschlagen: {File}", item.Filename);
                    BackgroundActivityText = $"Fehler bei {item.Filename}: {ex.Message}";
                }
            }

            BackgroundActivityText = $"{converted} HEIC-Datei(en) konvertiert.";

            await ScanInternalAsync(CurrentFolder);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanConvertHeic() => !IsBusy && HeicCandidates.Count > 0;

    // ── Fotos umbenennen ─────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanRenamePhotos))]
    private async Task RenamePhotosAsync()
    {
        if (CurrentFolder is null) return;

        var renameableCount = Photos.Count(p => p.DateTime is not null);
        var options = CurrentRenameOptions;

        var msg = $"Es werden bis zu {renameableCount} Fotos im Ordner umbenannt.\n\n" +
                  $"• Vorlage: {options.Template}\n" +
                  (string.IsNullOrWhiteSpace(options.Prefix) ? "" : $"• Präfix: {options.Prefix}\n") +
                  $"• Beispiel: {RenamePreview}\n" +
                  $"• tour.csv wird automatisch angepasst (Backup wird angelegt)\n" +
                  $"• Karten bleiben unangetastet\n\n" +
                  $"Fortfahren?";

        var renameConfirm = System.Windows.MessageBox.Show(
            msg,
            "Fotos umbenennen",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);
        if (renameConfirm != System.Windows.MessageBoxResult.Yes) return;

        IsBusy = true;
        var progress = new Progress<TravelJournal.Core.Services.RenameProgress>(p =>
        {
            BackgroundActivityText = $"Umbenennen {p.Current}/{p.Total}: {p.Message}";
        });

        try
        {
            var allEntries = Photos.Select(vm => vm.Photo).ToList();

            var result = await _photoRenamer.RenameAsync(CurrentFolder, allEntries, options, progress);

            BackgroundActivityText =
                $"{result.Renamed.Count} umbenannt, " +
                $"{result.SkippedAlreadyMatching.Count} bereits korrekt, " +
                $"{result.SkippedNoDateTime.Count} ohne Datum übersprungen" +
                (result.Errors.Count > 0 ? $", {result.Errors.Count} Fehler" : "") + ".";

            await ScanInternalAsync(CurrentFolder);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanRenamePhotos() =>
        !IsBusy && !string.IsNullOrEmpty(CurrentFolder)
        && Photos.Any(p => p.DateTime is not null);

    // ── Karten generieren ─────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanGenerateMaps))]
    private async Task GenerateMapsAsync()
    {
        if (_mapRenderCts != null)
        {
            _mapRenderCts.Cancel();
            return;
        }

        if (CurrentFolder == null) return;

        // Bestätigung wenn bereits Karten vorhanden
        var existingMapCount = Photos.Count(p => p.IsMapPhoto);
        if (existingMapCount > 0)
        {
            var mapConfirm = System.Windows.MessageBox.Show(
                $"Es werden {existingMapCount} bestehende Karte(n) gelöscht und durch frisch generierte ersetzt. Fortfahren?",
                "Karten neu erzeugen",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);
            if (mapConfirm != System.Windows.MessageBoxResult.Yes) return;
        }

        _mapRenderCts = new CancellationTokenSource();
        var ct        = _mapRenderCts.Token;

        // Bestehende Karten bereinigen
        var deleted = await CleanupExistingMapsAsync(ct);
        if (deleted > 0)
            BackgroundActivityText = $"{deleted} alte Karte(n) entfernt — Generierung läuft …";

        // Pass only real GPS photos (exclude existing map PNGs)
        var allWithGps = Photos
            .Select(vm => vm.Photo)
            .Where(p => p.EntryType != EntryType.Map
                     && p.Latitude.HasValue && p.Longitude.HasValue && p.DateTime.HasValue)
            .OrderBy(p => p.DateTime)
            .ToList();

        if (allWithGps.Count < 2) return;

        var options = _baseOptions with
        {
            StyleId               = SelectedMapStyleId,
            Language              = SelectedLanguage,
            BoundsPaddingFraction = BoundsPaddingPercent / 100.0,
        };
        var renderer     = _mapRendererFactory.Create(CurrentFolder, options, msg => StatusText = msg);
        var outputFolder = CurrentFolder; // Karten direkt neben den Fotos

        IsGeneratingMaps = true;
        IsBusy           = true;
        var progressReporter = new Progress<MapRenderProgress>(p =>
        {
            StatusText = p.Stage switch
            {
                "tiles"   => $"Tiles laden {p.Current}/{p.Total} …",
                "compose" => "Karte komponieren …",
                "render"  => $"Karte {p.Current}/{p.Total} …",
                _         => p.Message ?? ""
            };
        });

        MapRenderResult? mapResult = null;
        try
        {
            mapResult  = await renderer.RenderAllAsync(allWithGps, outputFolder, progressReporter, ct);
            StatusText = mapResult.RenderedCount == 0
                ? "Keine Stopps erkannt — keine Karten erzeugt."
                : $"{mapResult.RenderedCount} Karten erzeugt.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Karten-Generierung abgebrochen.";
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Karten-Generierung fehlgeschlagen");
            StatusText = $"Karten-Generierung fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            IsBusy           = false;
            IsGeneratingMaps = false;
            _mapRenderCts    = null;
            GenerateMapsCommand.NotifyCanExecuteChanged();
        }

        if (mapResult?.RenderedCount > 0 && CurrentFolder != null)
        {
            // Write map photos (with GPS + Location from stops) to CSV before re-scan,
            // so the scanner picks up the correct data immediately.
            var csvPath           = System.IO.Path.Combine(CurrentFolder, "tour.csv");
            var byFilename        = Photos.Select(vm => vm.Photo)
                .ToDictionary(p => p.Filename, StringComparer.OrdinalIgnoreCase);
            foreach (var mapPhoto in mapResult.MapPhotos)
            {
                if (byFilename.TryGetValue(mapPhoto.Filename, out var existing))
                {
                    existing.DateTime  = mapPhoto.DateTime;
                    existing.Latitude  = mapPhoto.Latitude;
                    existing.Longitude = mapPhoto.Longitude;
                    existing.Location  = mapPhoto.Location;
                    // State/Title/Description are intentionally preserved
                }
                else
                {
                    byFilename[mapPhoto.Filename] = mapPhoto;
                }
            }
            await Task.Run(() => _csvWriter.Write(csvPath, byFilename.Values.ToList()));
            await ScanInternalAsync(CurrentFolder);
        }
    }

    private bool CanGenerateMaps() =>
        !IsBusy
        && !string.IsNullOrEmpty(CurrentFolder)
        && Photos.Any(p =>
            p.State == PhotoState.Selected
            && p.Latitude is not null
            && p.Longitude is not null
            && !string.IsNullOrEmpty(p.Location));

    private async Task<int> CleanupExistingMapsAsync(CancellationToken ct)
    {
        if (CurrentFolder is null) return 0;

        var mapPhotos = Photos.Where(p => p.IsMapPhoto).ToList();
        if (mapPhotos.Count == 0) return 0;

        foreach (var p in mapPhotos)
        {
            Photos.Remove(p);
            p.PropertyChanged -= OnPhotoPropertyChanged;
            _subscribedPhotos.Remove(p);
        }

        var deleted = 0;
        foreach (var p in mapPhotos)
        {
            try
            {
                var path = System.IO.Path.Combine(CurrentFolder, p.Filename);
                if (System.IO.File.Exists(path))
                {
                    System.IO.File.Delete(path);
                    deleted++;
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Kartendatei konnte nicht gelöscht werden: {File}", p.Filename);
                BackgroundActivityText = $"Fehler beim Löschen einer Karte: {ex.Message}";
            }
            ct.ThrowIfCancellationRequested();
        }

        RebuildGalleryItems();
        return deleted;
    }

    // ── Filter ────────────────────────────────────────────────

    [RelayCommand]
    private void SetFilter(PhotoFilter filter) => ActiveFilter = filter;

    // ── Window Closing ────────────────────────────────────────

    public bool HandleWindowClosing()
    {
        _geocodingCts?.Cancel();

        if (SelectedPhoto is not { PendingRotation: not 0 }) return true;

        var decision = _confirmDialog.AskRotationSaveDecision(SelectedPhoto.Filename);
        switch (decision)
        {
            case RotationSaveDecision.Save:
                _ = SaveRotationAsync();
                return true;
            case RotationSaveDecision.Discard:
                SelectedPhoto.PendingRotation = 0;
                return true;
            default:
                return false;
        }
    }

    // ── Helpers ───────────────────────────────────────────────

    private void SaveUserSettings() =>
        _userSettings.Save(new UserSettings(
            SelectedMapStyleId, SelectedLanguage, BoundsPaddingPercent,
            RenamePrefix ?? string.Empty, RenameTemplate ?? RenameOptions.DefaultTemplate));

    private IEnumerable<Photo> AllCsvEntries() =>
        Photos.Select(vm => vm.Photo);

    private bool HasFolder()        => CurrentFolder != null;
    private bool HasSelectedPhoto() => SelectedPhoto != null && !SelectedPhoto.IsMapPhoto;

    private static readonly System.Text.RegularExpressions.Regex _mapFilenameRegex =
        new(@"^map_\d{4}-\d{2}-\d{2}T\d{2}-\d{2}-\d{2}(_[A-Za-z0-9]+)?\.png$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static bool IsMapFilename(string name) => _mapFilenameRegex.IsMatch(name);

    private static int NormalizeAngle(int deg) => ((deg % 360) + 360) % 360;

    private void AfterStateChange()
    {
        GalleryItemsView.Refresh();
        UpdateStatusText();
        DeselectAllOpenCommand.NotifyCanExecuteChanged();
        SelectAllOpenCommand.NotifyCanExecuteChanged();
        GenerateMapsCommand.NotifyCanExecuteChanged();
        StartPresentationCommand.NotifyCanExecuteChanged();
        ExportWebPresentationCommand.NotifyCanExecuteChanged();
        ExportPhotosCommand.NotifyCanExecuteChanged();
    }

    private async Task ScanInternalAsync(string folder)
    {
        IsBusy = true;
        try
        {
            var existingPhotos = Photos.Select(vm => vm.Photo).ToList();
            var result = await Task.Run(() => _scanner.Scan(folder, existingPhotos));

            MergeCollections(result, folder);

            var missingPhotos = Photos.Where(p => p.IsMissing).ToList();
            bool needsCsvWrite = missingPhotos.Count > 0 || result.NewFilenames.Any(n => IsMapFilename(n));

            if (missingPhotos.Count > 0)
            {
                foreach (var missing in missingPhotos)
                {
                    Photos.Remove(missing);
                    GalleryItems.Remove(missing);
                }
                GalleryItemsView.Refresh();

                if (SelectedGalleryItem != null && !GalleryItems.Contains(SelectedGalleryItem))
                    SelectedGalleryItem = GalleryItems.OfType<PhotoViewModel>().FirstOrDefault();
            }

            if (needsCsvWrite)
            {
                var csvPath = System.IO.Path.Combine(folder, "tour.csv");
                await Task.Run(() => _csvWriter.Write(csvPath, AllCsvEntries()));
            }

            UpdateStatusText();
            DeselectAllOpenCommand.NotifyCanExecuteChanged();
            SelectAllOpenCommand.NotifyCanExecuteChanged();
            GenerateMapsCommand.NotifyCanExecuteChanged();
            StartPresentationCommand.NotifyCanExecuteChanged();

            _ = LoadThumbnailsAsync();
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void MergeCollections(ScanResult result, string folder)
    {
        // ── Photos ──
        var existing   = Photos.ToDictionary(vm => vm.Filename, StringComparer.OrdinalIgnoreCase);
        var newSet     = new HashSet<string>(result.NewFilenames,     StringComparer.OrdinalIgnoreCase);
        var missingSet = new HashSet<string>(result.MissingFilenames, StringComparer.OrdinalIgnoreCase);

        Photos.Clear();

        foreach (var photo in result.Photos)
        {
            if (existing.TryGetValue(photo.Filename, out var vm))
            {
                vm.IsNew     = newSet.Contains(photo.Filename);
                vm.IsMissing = missingSet.Contains(photo.Filename);
                vm.Refresh();
                Photos.Add(vm);
            }
            else
            {
                Photos.Add(new PhotoViewModel(photo, folder)
                {
                    IsNew     = newSet.Contains(photo.Filename),
                    IsMissing = missingSet.Contains(photo.Filename)
                });
            }
        }

        // ── PropertyChanged-Subscriptions ──
        foreach (var old in _subscribedPhotos)
            old.PropertyChanged -= OnPhotoPropertyChanged;
        _subscribedPhotos.Clear();
        foreach (var vm in Photos)
        {
            vm.PropertyChanged += OnPhotoPropertyChanged;
            _subscribedPhotos.Add(vm);
        }


        // ── HEIC ──
        HeicCandidates.Clear();
        foreach (var heic in result.HeicCandidates)
            HeicCandidates.Add(new HeicItemViewModel(heic));
        ConvertHeicCommand.NotifyCanExecuteChanged();

        // ── Combined gallery ──
        RebuildGalleryItems();

        if (SelectedGalleryItem != null && !GalleryItems.Contains(SelectedGalleryItem))
            SelectedGalleryItem = GalleryItems.OfType<PhotoViewModel>().FirstOrDefault();
    }

    private void RebuildGalleryItems()
    {
        GalleryItems.Clear();
        foreach (var p in Photos)         GalleryItems.Add(p);
        foreach (var h in HeicCandidates) GalleryItems.Add(h);
        GalleryItemsView.Refresh();
    }

    private async Task LoadThumbnailsAsync()
    {
        var photos = Photos.Where(vm => vm.Thumbnail == null && !vm.IsMissing).ToList();
        foreach (var vm in photos) vm.Thumbnail = await _thumbnailLoader.LoadAsync(vm.FullPath);
    }

    private async Task LoadLargeImageAsync(PhotoViewModel vm)
    {
        var img = await _thumbnailLoader.LoadAsync(vm.FullPath, decodePixelWidth: 1600);
        vm.LargeImage = img;
    }

    private void UpdateStatusText()
    {
        var total      = Photos.Count;
        var startCount = Photos.Count(p => p.State == PhotoState.Start);
        var selected   = Photos.Count(p => p.State == PhotoState.Selected);
        var deselected = Photos.Count(p => p.State == PhotoState.Deselected);
        var open       = Photos.Count(p => p.State == PhotoState.None);
        var endCount   = Photos.Count(p => p.State == PhotoState.End);
        var newCount   = Photos.Count(p => p.IsNew);
        var km = RouteStatistics.CalculateDistance(
            Photos.Where(p => p.State == PhotoState.Selected).OrderBy(p => p.DateTime));

        var parts = new List<string> { $"{total} Fotos" };
        if (startCount > 0) parts.Add($"{startCount} Start");
        parts.Add($"{selected} ausgewählt");
        parts.Add($"{deselected} abgewählt");
        parts.Add($"{open} offen");
        if (endCount > 0) parts.Add($"{endCount} Ende");
        parts.Add($"{newCount} neu");
        parts.Add($"~ {km:F1} km");
        StatusText = string.Join(" · ", parts);
    }
}
