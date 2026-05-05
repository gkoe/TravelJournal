using TravelJournal.Core.Services;
using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace TravelJournal.Core.Tests;

public class ImageSharpImageRotatorTests : IDisposable
{
    private readonly ImageSharpImageRotator _rotator = new();
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"RotatorTests_{Guid.NewGuid()}");

    public ImageSharpImageRotatorTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    private string CreateTestJpeg(int width, int height)
    {
        var path = Path.Combine(_tempDir, $"{Guid.NewGuid()}.jpg");
        using var img = new Image<Rgb24>(width, height);
        img.SaveAsJpeg(path, new JpegEncoder { Quality = 90 });
        return path;
    }

    [Fact]
    public async Task RotateAsync_90Degrees_SwapsWidthAndHeight()
    {
        var path = CreateTestJpeg(width: 200, height: 100);

        await _rotator.RotateAsync(path, 90);

        using var result = Image.Load(path);
        result.Width.Should().Be(100);
        result.Height.Should().Be(200);
    }

    [Fact]
    public async Task RotateAsync_FourTimes90_RestoresOriginalDimensions()
    {
        var path = CreateTestJpeg(width: 300, height: 150);

        for (int i = 0; i < 4; i++)
            await _rotator.RotateAsync(path, 90);

        using var result = Image.Load(path);
        result.Width.Should().Be(300);
        result.Height.Should().Be(150);
    }

    [Fact]
    public async Task RotateAsync_180Degrees_PreservesDimensions()
    {
        var path = CreateTestJpeg(width: 400, height: 300);

        await _rotator.RotateAsync(path, 180);

        using var result = Image.Load(path);
        result.Width.Should().Be(400);
        result.Height.Should().Be(300);
    }

    [Theory]
    [InlineData(45)]
    [InlineData(30)]
    [InlineData(91)]
    public async Task RotateAsync_NonMultipleOf90_ThrowsArgumentException(int degrees)
    {
        var path = CreateTestJpeg(100, 100);

        var act = () => _rotator.RotateAsync(path, degrees);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task RotateAsync_0Degrees_FileUnchanged()
    {
        var path = CreateTestJpeg(100, 80);
        var sizeBefore = new FileInfo(path).Length;

        await _rotator.RotateAsync(path, 0);

        new FileInfo(path).Length.Should().Be(sizeBefore);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
