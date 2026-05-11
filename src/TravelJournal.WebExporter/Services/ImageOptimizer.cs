using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace TravelJournal.WebExporter.Services;

public sealed class ImageOptimizer
{
    public async Task OptimizeAsync(
        string sourcePath,
        string destPath,
        int    maxWidthPx,
        int    jpegQuality,
        CancellationToken ct = default)
    {
        using var image = await Image.LoadAsync(sourcePath, ct);

        image.Mutate(ctx => ctx.AutoOrient());

        if (image.Width > maxWidthPx || image.Height > maxWidthPx)
        {
            image.Mutate(ctx => ctx.Resize(new ResizeOptions
            {
                Size = new Size(maxWidthPx, maxWidthPx),
                Mode = ResizeMode.Max
            }));
        }

        image.Metadata.ExifProfile = null;

        await image.SaveAsJpegAsync(destPath,
            new JpegEncoder { Quality = jpegQuality }, ct);
    }
}
