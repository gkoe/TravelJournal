namespace TravelJournal.Core.MapRendering.Models;

public sealed record MapTile(int Z, int X, int Y)
{
    public string CacheKey(string providerId) => $"{providerId}/{Z}/{X}/{Y}.png";
}
