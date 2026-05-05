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
                return (ImageSource)bmp;
            });
        }
        catch
        {
            return Placeholder;
        }
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
