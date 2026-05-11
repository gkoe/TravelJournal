using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TravelJournal.Wpf.Services;

public class ThumbnailLoader : IThumbnailLoader
{
    private static readonly DrawingImage Placeholder = CreatePlaceholder();

    public async Task<ImageSource> LoadAsync(string filePath, int decodePixelWidth = 240)
    {
        try
        {
            return await Task.Run(() =>
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(filePath, UriKind.Absolute);
                bmp.DecodePixelWidth = decodePixelWidth;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.EndInit();
                bmp.Freeze();

                var rotation = ReadExifRotation(filePath);
                if (rotation == 0) return (ImageSource)bmp;

                var rotated = new TransformedBitmap(bmp, new RotateTransform(rotation));
                rotated.Freeze();
                return (ImageSource)rotated;
            });
        }
        catch
        {
            return Placeholder;
        }
    }

    // Liest EXIF-Orientation (Tag 274) ohne die Pixel zu dekodieren.
    // Gibt den Winkel zurück, um den das Bild im Uhrzeigersinn gedreht werden muss.
    private static double ReadExifRotation(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            var decoder = BitmapDecoder.Create(stream,
                BitmapCreateOptions.IgnoreColorProfile,
                BitmapCacheOption.Default);
            if (decoder.Frames[0].Metadata is not BitmapMetadata meta) return 0;
            if (meta.GetQuery("/app1/ifd/{ushort=274}") is not ushort ori) return 0;
            return ori switch
            {
                3 => 180,   // 180° gedreht
                6 =>  90,   // 90° CW (Kamera nach links gehalten)
                8 => 270,   // 90° CCW (Kamera nach rechts gehalten)
                _ =>   0
            };
        }
        catch { return 0; }
    }

    private static DrawingImage CreatePlaceholder()
    {
        var drawing = new GeometryDrawing(
            Brushes.LightGray,
            new Pen(Brushes.Gray, 1),
            new RectangleGeometry(new System.Windows.Rect(0, 0, 4, 3)));
        var img = new DrawingImage(drawing);
        img.Freeze();
        return img;
    }
}
