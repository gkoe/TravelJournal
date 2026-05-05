using CommunityToolkit.Mvvm.ComponentModel;
using TravelJournal.Core.Models;
using TravelJournal.Wpf.Services;
using System.Globalization;
using System.Windows.Media;

namespace TravelJournal.Wpf.ViewModels;

public partial class PhotoViewModel : ObservableObject, IGalleryItem
{
    private readonly Photo _photo;

    [ObservableProperty] private PhotoState  _state;
    [ObservableProperty] private string?     _title;
    [ObservableProperty] private string?     _description;
    [ObservableProperty] private string?     _location;
    [ObservableProperty] private bool        _isNew;
    [ObservableProperty] private bool        _isMissing;
    [ObservableProperty] private ImageSource? _thumbnail;
    [ObservableProperty] private ImageSource? _largeImage;
    [ObservableProperty] private int          _pendingRotation;

    public bool HasPendingRotation => PendingRotation != 0;

    // ── IGalleryItem ─────────────────────────────────────────
    public DateTime EffectiveDateTime => _photo.DateTime ?? System.DateTime.MinValue;

    public bool MatchesFilter(PhotoFilter filter) => filter switch
    {
        PhotoFilter.Open       => State == PhotoState.None,
        PhotoFilter.Selected   => State == PhotoState.Selected,
        PhotoFilter.Deselected => State == PhotoState.Deselected,
        PhotoFilter.New        => IsNew,
        PhotoFilter.Maps       => false,
        _                      => true
    };

    // ── Read-only Projektionen ────────────────────────────────
    public string    Filename    => _photo.Filename;
    public DateTime? DateTime   => _photo.DateTime;
    public double?   Latitude   => _photo.Latitude;
    public double?   Longitude  => _photo.Longitude;
    public double?   Altitude   => _photo.Altitude;
    public string    FullPath   { get; }
    public int?      PixelWidth  => _photo.PixelWidth;
    public int?      PixelHeight => _photo.PixelHeight;

    public ImageSource? DetailImage => LargeImage ?? Thumbnail;

    // ── Formatierte Metadaten ─────────────────────────────────
    public string DateTimeFormatted =>
        _photo.DateTime.HasValue
            ? _photo.DateTime.Value.ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture)
            : "—";

    public string CoordinatesFormatted =>
        _photo.Latitude.HasValue && _photo.Longitude.HasValue
            ? $"{_photo.Latitude:F6}, {_photo.Longitude:F6}"
            : "—";

    public string AltitudeFormatted =>
        _photo.Altitude.HasValue
            ? $"{_photo.Altitude:F0} m"
            : "—";

    public string FileSizeFormatted =>
        _photo.FileSizeBytes is { } bytes
            ? $"{bytes / 1024d / 1024d:F1} MB"
            : "—";

    public string PixelDimensionsFormatted =>
        _photo.PixelWidth is { } w && _photo.PixelHeight is { } h
            ? $"{w} × {h} px"
            : "—";

    /// <summary>Einzeiliger Metadaten-String (legacy, wird intern weitergeführt).</summary>
    public string MetaLine
    {
        get
        {
            var parts = new List<string>();
            if (DateTime.HasValue)
                parts.Add(DateTime.Value.ToString("dd.MM.yyyy · HH:mm"));
            if (Latitude.HasValue && Longitude.HasValue)
                parts.Add($"{Latitude:F6}, {Longitude:F6}");
            if (Altitude.HasValue)
                parts.Add($"{Altitude:F0} m");
            if (!string.IsNullOrEmpty(Location))
                parts.Add(Location);
            return string.Join(" · ", parts);
        }
    }

    internal Photo Photo => _photo;

    public PhotoViewModel(Photo photo, string folderPath)
    {
        _photo       = photo;
        FullPath     = System.IO.Path.Combine(folderPath, photo.Filename);
        _state       = photo.State;
        _title       = photo.Title;
        _description = photo.Description;
        _location    = photo.Location;
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(Filename));
        OnPropertyChanged(nameof(DateTime));
        OnPropertyChanged(nameof(Latitude));
        OnPropertyChanged(nameof(Longitude));
        OnPropertyChanged(nameof(Altitude));
        OnPropertyChanged(nameof(MetaLine));
        OnPropertyChanged(nameof(DateTimeFormatted));
        OnPropertyChanged(nameof(CoordinatesFormatted));
        OnPropertyChanged(nameof(AltitudeFormatted));
        OnPropertyChanged(nameof(FileSizeFormatted));
        OnPropertyChanged(nameof(PixelDimensionsFormatted));
    }

    public void RefreshDimensions(int? width, int? height)
    {
        _photo.PixelWidth  = width;
        _photo.PixelHeight = height;
        OnPropertyChanged(nameof(PixelDimensionsFormatted));
        OnPropertyChanged(nameof(PixelWidth));
        OnPropertyChanged(nameof(PixelHeight));
    }

    public async Task ReloadImagesAsync(IThumbnailLoader loader)
    {
        Thumbnail  = await loader.LoadAsync(FullPath);
        LargeImage = await loader.LoadAsync(FullPath, decodePixelWidth: 1600);
    }

    // ── Partial Hooks ─────────────────────────────────────────
    partial void OnStateChanged(PhotoState value)     => _photo.State       = value;
    partial void OnTitleChanged(string? value)        => _photo.Title       = value;
    partial void OnDescriptionChanged(string? value)  => _photo.Description = value;

    partial void OnLocationChanged(string? value)
    {
        _photo.Location = value;
        OnPropertyChanged(nameof(MetaLine));
    }

    partial void OnLargeImageChanged(ImageSource? value)  => OnPropertyChanged(nameof(DetailImage));
    partial void OnThumbnailChanged(ImageSource? value)   => OnPropertyChanged(nameof(DetailImage));
    partial void OnPendingRotationChanged(int value)      => OnPropertyChanged(nameof(HasPendingRotation));
}
