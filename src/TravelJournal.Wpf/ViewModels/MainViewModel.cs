using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TravelJournal.Core.MapRendering;
using TravelJournal.Core.Models;
using TravelJournal.Core.Presentation;
using TravelJournal.Core.Services;
using TravelJournal.Wpf.Services;
using TravelJournal.Wpf.Views;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Int32Rect = System.Windows.Int32Rect;

namespace TravelJournal.Wpf.ViewModels;

public partial class MainViewModel : ObservableObject
{
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
            if (value is MapItemViewModel mvm && mvm.LargeImage == null)
                _ = LoadMapLargeImageAsync(mvm);
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

    public ObservableCollection<PhotoViewModel>   Photos     { get; } = new();
    public ObservableCollection<MapItemViewModel> Maps       { get; } = new();
    public ObservableCollection<IGalleryItem>     GalleryItems { get; } = new();
    public ICollectionView GalleryItemsView { get; }

    public event Action? ScrollSelectedIntoViewRequested;

    public MainViewModel(
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
        ImageCropService        cropService)
    {
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

        _baseOptions = _mapRendererFactory.LoadBaseOptions();

        var saved = _userSettings.Load();
        _selectedMapStyleId   = saved.MapStyleId;
        _selectedLanguage     = saved.Language;
        _boundsPaddingPercent = saved.BoundsPaddingPercent;

        GalleryItemsView = CollectionViewSource.GetDefaultView(GalleryItems);
        GalleryItemsView.Filter = item => item is IGalleryItem gi && gi.MatchesFilter(ActiveFilter);
        GalleryItemsView.SortDescriptions.Add(
            new SortDescription(nameof(IGalleryItem.EffectiveDateTime), ListSortDirection.Ascending));
    }

    partial void OnActiveFilterChanged(PhotoFilter value) => GalleryItemsView.Refresh();

    partial void OnIsBusyChanged(bool value) => GenerateMapsCommand.NotifyCanExecuteChanged();

    // ── Scan / Folder ──────────────────────────────────────────

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        var folder = _folderDialog.PickFolder(CurrentFolder);
        if (folder == null) return;
        CurrentFolder = folder;
        RescanCommand.NotifyCanExecuteChanged();
        SaveCsvCommand.NotifyCanExecuteChanged();
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

    [RelayCommand(CanExecute = nameof(HasFolder))]
    private async Task SaveCsvAsync()
    {
        if (CurrentFolder == null) return;
        IsBusy = true;
        try
        {
            var csvPath = System.IO.Path.Combine(CurrentFolder, "tour.csv");
            var photos  = Photos.Select(vm => vm.Photo).ToList();
            await Task.Run(() => _csvWriter.Write(csvPath, photos));
            StatusText = $"Gespeichert: {csvPath}";
        }
        finally
        {
            IsBusy = false;
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

    // ── Bulk Deselect ─────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanDeselectAllOpen))]
    private void DeselectAllOpen()
    {
        foreach (var photo in Photos.Where(p => p.State == PhotoState.None))
            photo.State = PhotoState.Deselected;
        AfterStateChange();
    }

    private bool CanDeselectAllOpen() => Photos.Any(p => p.State == PhotoState.None);

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
        GenerateMapsCommand.NotifyCanExecuteChanged();
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
            var photos  = Photos.Select(vm => vm.Photo).ToList();
            await Task.Run(() => _csvWriter.Write(csvPath, photos), CancellationToken.None);
            BackgroundActivityText += " CSV gespeichert.";
        }
        catch (OperationCanceledException)
        {
            BackgroundActivityText = "Ortsermittlung abgebrochen.";
        }
        catch (Exception ex)
        {
            BackgroundActivityText = $"Ortsermittlung fehlgeschlagen: {ex.Message}";
        }
        finally
        {
            IsGeocodingRunning = false;
            _geocodingCts?.Dispose();
            _geocodingCts = null;
        }
    }

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
                StatusText = $"Löschen fehlgeschlagen: {ex.Message}";
                return;
            }
        }

        if (item is PhotoViewModel photo)
            Photos.Remove(photo);
        else if (item is MapItemViewModel map)
            Maps.Remove(map);

        GalleryItems.Remove(item);
        GalleryItemsView.Refresh();

        var newItems = GalleryItemsView.Cast<IGalleryItem>().ToList();
        SelectedGalleryItem = newItems.Count > 0
            ? newItems[Math.Min(currentIdx, newItems.Count - 1)]
            : null;

        if (isMissing && CurrentFolder != null)
        {
            var csvPath = System.IO.Path.Combine(CurrentFolder, "tour.csv");
            var photos  = Photos.Select(vm => vm.Photo).ToList();
            await Task.Run(() => _csvWriter.Write(csvPath, photos));
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
        var selectedPhotoTimes = Photos
            .Where(p => p.State == PhotoState.Selected && p.DateTime.HasValue)
            .Select(p => p.DateTime!.Value)
            .OrderBy(t => t)
            .ToList();

        var mainItems = new List<IPresentationItem>();
        mainItems.AddRange(Photos
            .Where(p => p.State == PhotoState.Selected && p.DateTime.HasValue)
            .Select(p => (IPresentationItem)new PhotoPresentationItem(
                p.DateTime!.Value, p.FullPath, p.Location, p.Title)));
        mainItems.AddRange(Maps
            .Select(m => (IPresentationItem)new MapPresentationItem(
                GetMapSortKey(m.EffectiveDateTime, selectedPhotoTimes), m.FullPath)));
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
        || Maps.Any();

    private DateTime GetMapSortKey(DateTime mapTime, IReadOnlyList<DateTime> sortedPhotoTimes)
    {
        var before = sortedPhotoTimes.TakeWhile(t => t < mapTime).ToList();
        if (before.Count == 0) return mapTime;

        int clusterStart = before.Count - 1;
        while (clusterStart > 0 &&
               before[clusterStart] - before[clusterStart - 1] < _baseOptions.StopThreshold)
        {
            clusterStart--;
        }

        return before[clusterStart].AddTicks(-1);
    }

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

        // Pass ALL GPS photos — renderer uses State to determine stops
        var allWithGps = Photos
            .Select(vm => vm.Photo)
            .Where(p => p.Latitude.HasValue && p.Longitude.HasValue && p.DateTime.HasValue)
            .OrderBy(p => p.DateTime)
            .ToList();

        if (allWithGps.Count < 2) return;

        _mapRenderCts = new CancellationTokenSource();
        var ct        = _mapRenderCts.Token;

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

        bool rescan = false;
        try
        {
            var count = await renderer.RenderAllAsync(allWithGps, outputFolder, progressReporter, ct);
            StatusText = count == 0
                ? "Keine Stopps erkannt — keine Karten erzeugt."
                : $"{count} Karten erzeugt.";
            rescan = count > 0;
        }
        catch (OperationCanceledException)
        {
            StatusText = "Karten-Generierung abgebrochen.";
        }
        finally
        {
            IsBusy           = false;
            IsGeneratingMaps = false;
            _mapRenderCts    = null;
            GenerateMapsCommand.NotifyCanExecuteChanged();
        }

        if (rescan && CurrentFolder != null)
            await ScanInternalAsync(CurrentFolder);
    }

    private bool CanGenerateMaps() =>
        !IsBusy
        && !string.IsNullOrEmpty(CurrentFolder)
        && Photos.Count(p => p.State == PhotoState.Selected) >= 2;

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
        _userSettings.Save(new UserSettings(SelectedMapStyleId, SelectedLanguage, BoundsPaddingPercent));

    private bool HasFolder()        => CurrentFolder != null;
    private bool HasSelectedPhoto() => SelectedPhoto != null;

    private static int NormalizeAngle(int deg) => ((deg % 360) + 360) % 360;

    private void AfterStateChange()
    {
        GalleryItemsView.Refresh();
        UpdateStatusText();
        DeselectAllOpenCommand.NotifyCanExecuteChanged();
        GenerateMapsCommand.NotifyCanExecuteChanged();
        StartPresentationCommand.NotifyCanExecuteChanged();
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

                var csvPath = System.IO.Path.Combine(folder, "tour.csv");
                var photos  = Photos.Select(vm => vm.Photo).ToList();
                await Task.Run(() => _csvWriter.Write(csvPath, photos));
            }

            UpdateStatusText();
            DeselectAllOpenCommand.NotifyCanExecuteChanged();
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

        // ── Maps ──
        Maps.Clear();
        foreach (var mapItem in result.Maps)
            Maps.Add(new MapItemViewModel(mapItem));

        // ── Combined gallery ──
        RebuildGalleryItems();

        if (SelectedGalleryItem != null && !GalleryItems.Contains(SelectedGalleryItem))
            SelectedGalleryItem = GalleryItems.OfType<PhotoViewModel>().FirstOrDefault();
    }

    private void RebuildGalleryItems()
    {
        GalleryItems.Clear();
        foreach (var p in Photos) GalleryItems.Add(p);
        foreach (var m in Maps)   GalleryItems.Add(m);
        GalleryItemsView.Refresh();
    }

    private async Task LoadThumbnailsAsync()
    {
        var photos = Photos.Where(vm => vm.Thumbnail == null && !vm.IsMissing).ToList();
        var maps   = Maps.Where(vm => vm.Thumbnail == null).ToList();
        foreach (var vm in photos) vm.Thumbnail = await _thumbnailLoader.LoadAsync(vm.FullPath);
        foreach (var vm in maps)   await vm.LoadThumbnailAsync(_thumbnailLoader);
    }

    private async Task LoadLargeImageAsync(PhotoViewModel vm)
    {
        var img = await _thumbnailLoader.LoadAsync(vm.FullPath, decodePixelWidth: 1600);
        vm.LargeImage = img;
    }

    private async Task LoadMapLargeImageAsync(MapItemViewModel vm)
        => await vm.LoadLargeImageAsync(_thumbnailLoader);

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
