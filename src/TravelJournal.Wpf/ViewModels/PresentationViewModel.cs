using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TravelJournal.Core.Presentation;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace TravelJournal.Wpf.ViewModels;

public sealed partial class PresentationViewModel : ObservableObject
{
    private readonly IReadOnlyList<IPresentationItem> _playlist;
    private readonly DispatcherTimer _itemTimer;
    private readonly DispatcherTimer _overlayTimer;
    private readonly DispatcherTimer _hintTimer;

    private static readonly TimeSpan ItemDuration           = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan OverlayVisibleDuration = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan HintVisibleDuration    = TimeSpan.FromSeconds(3);

    [ObservableProperty] private int          _currentIndex;
    [ObservableProperty] private ImageSource? _currentImageSource;
    [ObservableProperty] private string?      _overlayLocation;
    [ObservableProperty] private string?      _overlayDateTime;
    [ObservableProperty] private string?      _overlayDescription;
    [ObservableProperty] private bool         _isPaused;

    public Visibility OverlayLocationVisibility =>
        string.IsNullOrEmpty(OverlayLocation) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility OverlayDescriptionVisibility =>
        string.IsNullOrEmpty(OverlayDescription) ? Visibility.Collapsed : Visibility.Visible;

    public Visibility OverlayDateTimeVisibility =>
        string.IsNullOrEmpty(OverlayDateTime) ? Visibility.Collapsed : Visibility.Visible;

    partial void OnOverlayLocationChanged(string? value) =>
        OnPropertyChanged(nameof(OverlayLocationVisibility));

    partial void OnOverlayDescriptionChanged(string? value) =>
        OnPropertyChanged(nameof(OverlayDescriptionVisibility));

    partial void OnOverlayDateTimeChanged(string? value) =>
        OnPropertyChanged(nameof(OverlayDateTimeVisibility));

    public event EventHandler?   RequestClose;
    public event Action<double>? OverlayAnimationRequested;
    public event Action<double>? HintAnimationRequested;

    public PresentationViewModel(IReadOnlyList<IPresentationItem> playlist)
    {
        _playlist = playlist;

        _itemTimer = new DispatcherTimer { Interval = ItemDuration };
        _itemTimer.Tick += (_, _) => MoveNext();

        _overlayTimer = new DispatcherTimer { Interval = OverlayVisibleDuration };
        _overlayTimer.Tick += (_, _) =>
        {
            _overlayTimer.Stop();
            OverlayAnimationRequested?.Invoke(0.0);
        };

        _hintTimer = new DispatcherTimer { Interval = HintVisibleDuration };
        _hintTimer.Tick += (_, _) =>
        {
            _hintTimer.Stop();
            HintAnimationRequested?.Invoke(0.0);
        };
    }

    public async Task StartAsync()
    {
        await ShowItemAsync(0);
        _hintTimer.Start();
    }

    private async Task ShowItemAsync(int index)
    {
        if (index >= _playlist.Count)
        {
            RequestClose?.Invoke(this, EventArgs.Empty);
            return;
        }
        if (index < 0) index = 0;

        _itemTimer.Stop();

        CurrentIndex = index;
        var item = _playlist[index];

        CurrentImageSource = await LoadImageAsync(item.FullPath);
        UpdateOverlay(item);

        if (!item.IsEndSlide && !IsPaused)
        {
            _itemTimer.Interval = item.OverrideDuration ?? ItemDuration;
            _itemTimer.Start();
        }
    }

    private void UpdateOverlay(IPresentationItem item)
    {
        _overlayTimer.Stop();

        if (item.Overlay is null || item.OverrideDuration.HasValue || item.IsEndSlide)
        {
            OverlayDescription = null;
            OverlayLocation    = null;
            OverlayDateTime    = null;
            OverlayAnimationRequested?.Invoke(0.0);
            return;
        }

        if (!string.IsNullOrEmpty(item.Overlay.Title))
        {
            OverlayDescription = item.Overlay.Title;
            OverlayLocation    = null;
            OverlayDateTime    = null;
        }
        else
        {
            var de = new CultureInfo("de-DE");
            OverlayDescription = null;
            OverlayLocation    = item.Overlay.Location;
            OverlayDateTime    = item.Overlay.LocalDateTime.ToString("dddd, d. MMMM yyyy · HH:mm", de);
        }

        OverlayAnimationRequested?.Invoke(1.0);
        _overlayTimer.Start();
    }

    private void MoveNext()
    {
        var next = CurrentIndex + 1;
        if (next >= _playlist.Count) { Stop(); return; }
        _ = ShowItemAsync(next);
    }

    [RelayCommand]
    private void Next() => _ = ShowItemAsync(CurrentIndex + 1);

    [RelayCommand]
    private void Previous() => _ = ShowItemAsync(Math.Max(0, CurrentIndex - 1));

    [RelayCommand]
    private void TogglePause()
    {
        if (IsPaused)
        {
            IsPaused = false;
            var item = _playlist[CurrentIndex];
            if (!item.IsEndSlide)
            {
                _itemTimer.Interval = item.OverrideDuration ?? ItemDuration;
                _itemTimer.Start();
            }
        }
        else
        {
            _itemTimer.Stop();
            IsPaused = true;
        }
    }

    [RelayCommand]
    private void Stop()
    {
        _itemTimer.Stop();
        _overlayTimer.Stop();
        _hintTimer.Stop();
        RequestClose?.Invoke(this, EventArgs.Empty);
    }

    private static async Task<ImageSource?> LoadImageAsync(string path)
    {
        try
        {
            return await Task.Run(() =>
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption      = BitmapCacheOption.OnLoad;
                bmp.UriSource        = new Uri(path);
                bmp.DecodePixelWidth = 1920;
                bmp.EndInit();
                bmp.Freeze();
                return (ImageSource)bmp;
            });
        }
        catch
        {
            return null;
        }
    }
}
