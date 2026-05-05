using CommunityToolkit.Mvvm.ComponentModel;
using TravelJournal.Core.Models;
using TravelJournal.Wpf.Services;
using System.Windows.Media;

namespace TravelJournal.Wpf.ViewModels;

public partial class MapItemViewModel : ObservableObject, IGalleryItem
{
    private readonly MapItem _item;

    [ObservableProperty] private ImageSource? _thumbnail;
    [ObservableProperty] private ImageSource? _largeImage;

    public string   Filename          => _item.Filename;
    public DateTime EffectiveDateTime => _item.DateTime;
    public string   FullPath          => _item.FullPath;
    public string   DisplayTitle      => $"Karte {_item.DateTime:dd.MM.yyyy · HH:mm}";

    public ImageSource? DetailImage => LargeImage ?? Thumbnail;

    public bool MatchesFilter(PhotoFilter filter) =>
        filter == PhotoFilter.All || filter == PhotoFilter.Maps;

    public MapItemViewModel(MapItem item) => _item = item;

    partial void OnLargeImageChanged(ImageSource? value) => OnPropertyChanged(nameof(DetailImage));
    partial void OnThumbnailChanged(ImageSource? value)  => OnPropertyChanged(nameof(DetailImage));

    public async Task LoadThumbnailAsync(IThumbnailLoader loader)
        => Thumbnail = await loader.LoadAsync(FullPath);

    public async Task LoadLargeImageAsync(IThumbnailLoader loader)
        => LargeImage = await loader.LoadAsync(FullPath, decodePixelWidth: 1600);
}
