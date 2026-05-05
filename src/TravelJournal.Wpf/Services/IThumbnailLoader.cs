using System.Windows.Media;

namespace TravelJournal.Wpf.Services;

public interface IThumbnailLoader
{
    Task<ImageSource> LoadAsync(string filePath, int decodePixelWidth = 240);
}
