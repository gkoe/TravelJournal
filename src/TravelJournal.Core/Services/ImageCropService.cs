using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.Processing;

namespace TravelJournal.Core.Services;

public class ImageCropService
{
    public async Task CropAndSaveAsync(string filePath, int x, int y, int width, int height,
                                        CancellationToken ct = default)
    {
        var rect = new Rectangle(x, y, width, height);
        var tmp  = filePath + ".crop.tmp";
        try
        {
            using var image = await Image.LoadAsync(filePath, ct);

            rect = new Rectangle(
                Math.Max(0, x),
                Math.Max(0, y),
                Math.Min(width,  image.Width  - Math.Max(0, x)),
                Math.Min(height, image.Height - Math.Max(0, y)));

            image.Mutate(ctx => ctx.Crop(rect));

            var exif = image.Metadata.ExifProfile;
            if (exif != null)
            {
                exif.SetValue(ExifTag.PixelXDimension, (uint)image.Width);
                exif.SetValue(ExifTag.PixelYDimension, (uint)image.Height);
            }

            await image.SaveAsJpegAsync(tmp, new JpegEncoder { Quality = 92 }, ct);
        }
        catch
        {
            if (File.Exists(tmp)) File.Delete(tmp);
            throw;
        }

        File.Move(tmp, filePath, overwrite: true);
    }
}
