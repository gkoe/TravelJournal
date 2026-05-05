using System.Text.Json;

namespace TravelJournal.Core.Services;

public sealed class JsonGeocodeCache : IReverseGeocoder
{
    private readonly IReverseGeocoder _inner;
    private readonly string _cacheFilePath;
    private readonly Dictionary<string, string?> _cache = new();
    private readonly SemaphoreSlim _lock = new(1, 1);

    public JsonGeocodeCache(IReverseGeocoder inner, string cacheFilePath)
    {
        _inner = inner;
        _cacheFilePath = cacheFilePath;
        LoadCache();
    }

    public async Task<string?> ResolveAsync(double latitude, double longitude, CancellationToken ct = default)
    {
        var key = $"{Math.Round(latitude, 4)},{Math.Round(longitude, 4)}";

        await _lock.WaitAsync(ct);
        try
        {
            if (_cache.TryGetValue(key, out var cached)) return cached;
        }
        finally { _lock.Release(); }

        var result = await _inner.ResolveAsync(latitude, longitude, ct);

        await _lock.WaitAsync(CancellationToken.None);
        try
        {
            _cache[key] = result;
            SaveCache();
        }
        finally { _lock.Release(); }

        return result;
    }

    private void LoadCache()
    {
        if (!File.Exists(_cacheFilePath)) return;
        try
        {
            var json = File.ReadAllText(_cacheFilePath);
            var dict = JsonSerializer.Deserialize<Dictionary<string, string?>>(json);
            if (dict != null)
                foreach (var (k, v) in dict)
                    _cache[k] = v;
        }
        catch { }
    }

    private void SaveCache()
    {
        try
        {
            var tmp = _cacheFilePath + ".tmp";
            var json = JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(tmp, json);
            File.Move(tmp, _cacheFilePath, overwrite: true);
        }
        catch { }
    }
}
