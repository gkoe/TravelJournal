using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace TravelJournal.Core.Services;

public class ImageSharpImageRotator : IImageRotator
{
    public async Task RotateAsync(string filePath, int degreesClockwise, CancellationToken ct = default)
    {
        if (degreesClockwise % 90 != 0)
            throw new ArgumentException("Rotation muss ein Vielfaches von 90 Grad sein.", nameof(degreesClockwise));

        int normalized = ((degreesClockwise % 360) + 360) % 360;
        if (normalized == 0) return;

        var rotateMode = normalized switch
        {
            90  => RotateMode.Rotate90,
            180 => RotateMode.Rotate180,
            270 => RotateMode.Rotate270,
            _   => RotateMode.None
        };

        var tempPath = filePath + ".tmp";
        try
        {
            using var image = await Image.LoadAsync(filePath, ct);
            image.Mutate(x => x.Rotate(rotateMode));
            await image.SaveAsJpegAsync(tempPath, new JpegEncoder { Quality = 92 }, ct);
        }
        catch
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            throw;
        }

        File.Move(tempPath, filePath, overwrite: true);
    }
}
