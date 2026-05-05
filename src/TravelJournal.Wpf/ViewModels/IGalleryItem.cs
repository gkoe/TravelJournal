namespace TravelJournal.Wpf.ViewModels;

public interface IGalleryItem
{
    DateTime EffectiveDateTime { get; }
    string   Filename          { get; }
    string   FullPath          { get; }
    bool     MatchesFilter(PhotoFilter filter);
}
