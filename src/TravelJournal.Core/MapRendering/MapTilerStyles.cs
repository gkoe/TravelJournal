namespace TravelJournal.Core.MapRendering;

public static class MapTilerStyles
{
    public static readonly IReadOnlyList<MapStyleInfo> Curated = new List<MapStyleInfo>
    {
        new("outdoor-v2",  "Outdoor",  "Wandern/Radfahren, Höhenlinien, sichtbare Wege"),
        new("streets-v2",  "Streets",  "Generischer Google-Maps-Look, gut in Städten"),
        new("topo-v2",     "Topo",     "Topografisch, prominente Höhenangaben"),
        new("voyager",     "Voyager",  "Klar und reduziert, hebt Polyline gut hervor"),
        new("bright",      "Bright",   "Kontraststark mit hellen Farben"),
        new("basic-v2",    "Basic",    "Minimalistisch"),
        new("backdrop",    "Backdrop", "Sehr dezent, ideal als Daten-Hintergrund"),
    };

    public static readonly IReadOnlyList<string> SupportedLanguages =
        new[] { "de", "en", "fr", "it", "es", "nl" };
}

public sealed record MapStyleInfo(string Id, string Name, string Description)
{
    public string DisplayLabel => $"{Name} — {Description}";
}
