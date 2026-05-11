using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using TravelJournal.WebExporter.Services;

namespace TravelJournal.WebExporter.Tests;

public class ImageOptimizerTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
    private readonly ImageOptimizer _sut = new();

    public ImageOptimizerTests() => Directory.CreateDirectory(_tmp);

    public void Dispose() => Directory.Delete(_tmp, recursive: true);

    [Fact]
    public async Task Optimize_ResizesLargeImage_ToMaxWidth()
    {
        var src  = Path.Combine(_tmp, "big.jpg");
        var dest = Path.Combine(_tmp, "out.jpg");

        using (var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgb24>(4000, 3000))
            await img.SaveAsJpegAsync(src);

        await _sut.OptimizeAsync(src, dest, maxWidthPx: 1920, jpegQuality: 85);

        using var result = await Image.LoadAsync(dest);
        Math.Max(result.Width, result.Height).Should().BeLessThanOrEqualTo(1920);
    }

    [Fact]
    public async Task Optimize_SmallImage_IsNotUpscaled()
    {
        var src  = Path.Combine(_tmp, "small.jpg");
        var dest = Path.Combine(_tmp, "out.jpg");

        using (var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgb24>(800, 600))
            await img.SaveAsJpegAsync(src);

        await _sut.OptimizeAsync(src, dest, maxWidthPx: 1920, jpegQuality: 85);

        using var result = await Image.LoadAsync(dest);
        result.Width.Should().Be(800);
        result.Height.Should().Be(600);
    }

    [Fact]
    public async Task Optimize_StripsExifMetadata()
    {
        var src  = Path.Combine(_tmp, "exif.jpg");
        var dest = Path.Combine(_tmp, "out.jpg");

        using (var img = new SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgb24>(400, 300))
        {
            img.Metadata.ExifProfile = new ExifProfile();
            img.Metadata.ExifProfile.SetValue(ExifTag.Make, "TestCamera");
            await img.SaveAsJpegAsync(src);
        }

        await _sut.OptimizeAsync(src, dest, maxWidthPx: 1920, jpegQuality: 85);

        using var result = await Image.LoadAsync(dest);
        result.Metadata.ExifProfile.Should().BeNull();
    }
}
