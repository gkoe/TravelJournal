using CommunityToolkit.Mvvm.ComponentModel;
using TravelJournal.Core.Models;
using System.Windows.Media;

namespace TravelJournal.Wpf.ViewModels;

public partial class HeicItemViewModel : ObservableObject, IGalleryItem
{
    private readonly HeicItem _item;

    [ObservableProperty] private ImageSource? _thumbnail;

    public string    Filename          => _item.Filename;
    public DateTime  EffectiveDateTime => _item.DateTime ?? System.DateTime.MinValue;
    public string    FullPath          => _item.FullPath;
    public ImageSource? DetailImage   => null;

    public bool MatchesFilter(PhotoFilter filter) =>
        filter == PhotoFilter.All || filter == PhotoFilter.Heic;

    public HeicItemViewModel(HeicItem item) => _item = item;
}
