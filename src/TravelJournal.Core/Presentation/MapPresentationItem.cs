namespace TravelJournal.Core.Presentation;

public sealed class MapPresentationItem : IPresentationItem
{
    public DateTime             EffectiveDateTime { get; }
    public string               FullPath          { get; }
    public PresentationOverlay? Overlay           { get; }
    public TimeSpan?            OverrideDuration  => null;
    public bool                 IsEndSlide        => false;

    public MapPresentationItem(DateTime dateTime, string fullPath, string? title = null)
    {
        EffectiveDateTime = dateTime;
        FullPath          = fullPath;
        Overlay           = string.IsNullOrWhiteSpace(title)
            ? null
            : new PresentationOverlay(null, dateTime.DayOfWeek, dateTime, title);
    }
}
