using TravelJournal.Core.MapRendering.Caching;
using FluentAssertions;

namespace TravelJournal.Core.Tests.MapRendering;

public class FileTileCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"tile-cache-test-{Guid.NewGuid()}");
    private readonly FileTileCache _sut;

    public FileTileCacheTests()
        => _sut = new FileTileCache(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task TryGet_NonExistentKey_ReturnsNull()
    {
        var result = await _sut.TryGetAsync("osm/10/100/200.png", default);
        result.Should().BeNull();
    }

    [Fact]
    public async Task Put_ThenTryGet_ReturnsSameData()
    {
        var data = new byte[] { 1, 2, 3, 4, 5 };
        const string key = "osm/10/100/200.png";

        await _sut.PutAsync(key, data, default);
        var result = await _sut.TryGetAsync(key, default);

        result.Should().Equal(data);
    }

    [Fact]
    public async Task Put_WritesFileAtExpectedPath()
    {
        var data = new byte[] { 42 };
        const string key = "maptiler/12/2200/1340.png";

        await _sut.PutAsync(key, data, default);

        var expectedPath = Path.Combine(_root, "maptiler", "12", "2200", "1340.png");
        File.Exists(expectedPath).Should().BeTrue();
    }

    [Fact]
    public async Task Put_OverwritesExistingEntry()
    {
        const string key = "osm/5/10/20.png";
        await _sut.PutAsync(key, new byte[] { 1 }, default);
        await _sut.PutAsync(key, new byte[] { 2, 3 }, default);

        var result = await _sut.TryGetAsync(key, default);
        result.Should().Equal(new byte[] { 2, 3 });
    }

    [Fact]
    public async Task Put_CreatesDirectoriesAsNeeded()
    {
        const string key = "provider/18/123456/789012.png";
        await _sut.PutAsync(key, new byte[] { 0 }, default);

        var dir = Path.Combine(_root, "provider", "18", "123456");
        Directory.Exists(dir).Should().BeTrue();
    }
}
