namespace TravelJournal.Core.Presentation;

public sealed class MapPresentationItem : IPresentationItem
{
    public DateTime             EffectiveDateTime { get; }
    public string               FullPath          { get; }
    public PresentationOverlay? Overlay           => null;
    public TimeSpan?            OverrideDuration  => null;
    public bool                 IsEndSlide        => false;

    public MapPresentationItem(DateTime dateTime, string fullPath)
    {
        EffectiveDateTime = dateTime;
        FullPath          = fullPath;
    }
}
