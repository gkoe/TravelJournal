namespace TravelJournal.Core.MapRendering.Caching;

public sealed class FileTileCache : ITileCache
{
    private readonly string _root;

    public FileTileCache(string rootPath) => _root = rootPath;

    public async Task<byte[]?> TryGetAsync(string cacheKey, CancellationToken ct)
    {
        var path = FullPath(cacheKey);
        if (!File.Exists(path)) return null;
        return await File.ReadAllBytesAsync(path, ct);
    }

    public async Task PutAsync(string cacheKey, byte[] data, CancellationToken ct)
    {
        var path = FullPath(cacheKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        await File.WriteAllBytesAsync(tmp, data, ct);
        File.Move(tmp, path, overwrite: true);
    }

    private string FullPath(string cacheKey) =>
        Path.Combine(_root, cacheKey.Replace('/', Path.DirectorySeparatorChar));
}
