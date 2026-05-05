using TravelJournal.Core.Services;

namespace TravelJournal.Wpf.Services;

public interface IReverseGeocoderFactory
{
    IReverseGeocoder CreateForFolder(string folderPath);
}
