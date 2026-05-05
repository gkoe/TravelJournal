namespace TravelJournal.Core.MapRendering;

public sealed record MapRenderingOptions
{
    public int      TargetWidthPx            { get; init; } = 1600;
    public int      TargetHeightPx           { get; init; } = 1200;
    public TimeSpan StopThreshold            { get; init; } = TimeSpan.FromMinutes(30);
    public double   BoundsPaddingFraction    { get; init; } = 0.12;
    public int      MaxParallelTileDownloads { get; init; } = 4;
    public bool     AddFinalSummaryMap       { get; init; } = true;
    public string?  MapTilerApiKey           { get; init; }
    public string   StyleId                  { get; init; } = "outdoor-v2";
    public string   Language                 { get; init; } = "de";
    public string?  CustomTileUrlTemplate    { get; init; }
}
