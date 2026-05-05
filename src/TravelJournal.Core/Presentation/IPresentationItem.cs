namespace TravelJournal.Core.Presentation;

public interface IPresentationItem
{
    DateTime             EffectiveDateTime { get; }
    string               FullPath          { get; }
    PresentationOverlay? Overlay           { get; }
    TimeSpan?            OverrideDuration  { get; }
    bool                 IsEndSlide        { get; }
}

public sealed record PresentationOverlay(
    string?   Location,
    DayOfWeek DayOfWeek,
    DateTime  LocalDateTime,
    string?   Title = null);
