namespace TravelJournal.Core.MapRendering.Caching;

public interface ITileCache
{
    Task<byte[]?> TryGetAsync(string cacheKey, CancellationToken ct);
    Task PutAsync(string cacheKey, byte[] data, CancellationToken ct);
}
