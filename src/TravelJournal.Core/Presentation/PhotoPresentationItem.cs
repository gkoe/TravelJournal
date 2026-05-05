namespace TravelJournal.Core.Presentation;

public sealed class PhotoPresentationItem : IPresentationItem
{
    public DateTime            EffectiveDateTime { get; }
    public string              FullPath          { get; }
    public PresentationOverlay Overlay           { get; }
    public TimeSpan?           OverrideDuration  { get; init; }
    public bool                IsEndSlide        { get; init; }

    PresentationOverlay? IPresentationItem.Overlay => Overlay;

    public PhotoPresentationItem(DateTime dateTime, string fullPath, string? location, string? title = null)
    {
        EffectiveDateTime = dateTime;
        FullPath          = fullPath;
        Overlay           = new PresentationOverlay(location, dateTime.DayOfWeek, dateTime, title);
    }
}
