using SixLabors.ImageSharp;

namespace TravelJournal.Core.MapRendering;

public static class MapStyle
{
    public static readonly Color RouteOuter = Color.White;
    public static readonly Color RouteInner = Color.ParseHex("4285F4");
    public const float RouteOuterWidth = 8f;
    public const float RouteInnerWidth = 5f;

    public static readonly Color PastStopFill   = Color.ParseHex("4285F4");
    public static readonly Color PastStopBorder = Color.White;
    public const float PastStopRadius      = 4f;
    public const float PastStopBorderWidth = 1.5f;

    public static readonly Color CurrentStopFill   = Color.ParseHex("EA4335");
    public static readonly Color CurrentStopBorder = Color.White;
    public const float CurrentStopRadius      = 9f;
    public const float CurrentStopBorderWidth = 3f;

    // White 78% opacity → #C8, then RRGGBBAA → FFFFFFC8
    public static readonly Color AttributionBackground = Color.Parse("#FFFFFFC8");
    public static readonly Color AttributionText       = Color.Parse("#3C3C3C");
    public const float AttributionFontSize = 11f;
}
