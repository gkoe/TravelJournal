using TravelJournal.Core.Services;
using System.IO;
using System.Net.Http;

namespace TravelJournal.Wpf.Services;

public class ReverseGeocoderFactory : IReverseGeocoderFactory
{
    private readonly IHttpClientFactory _httpClientFactory;

    public ReverseGeocoderFactory(IHttpClientFactory httpClientFactory)
        => _httpClientFactory = httpClientFactory;

    public IReverseGeocoder CreateForFolder(string folderPath)
    {
        var cachePath = Path.Combine(folderPath, "geocache-v2.json");
        var httpClient = _httpClientFactory.CreateClient("geocoder");
        return new JsonGeocodeCache(new NominatimReverseGeocoder(httpClient), cachePath);
    }
}
