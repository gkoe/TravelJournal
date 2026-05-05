using System.Globalization;
using System.Net.Http;
using System.Text.Json;

namespace TravelJournal.Core.Services;

public class NominatimReverseGeocoder : IReverseGeocoder
{
    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _rateLimiter = new(1, 1);

    public NominatimReverseGeocoder(HttpClient httpClient, string contactEmail = "kontakt@example.org")
    {
        _httpClient = httpClient;
        // Nur setzen wenn der Client (z.B. via IHttpClientFactory) noch keinen User-Agent hat.
        // TryAddWithoutValidation würde sonst einen zweiten Wert anhängen, da User-Agent
        // ein Multi-Value-Header ist — Nominatim blockiert dann wegen example.org.
        if (!_httpClient.DefaultRequestHeaders.Contains("User-Agent"))
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent", $"TravelJournal/1.0 ({contactEmail})");
    }

    public async Task<string?> ResolveAsync(double latitude, double longitude, CancellationToken ct = default)
    {
        await _rateLimiter.WaitAsync(ct);
        try
        {
            var lat = latitude.ToString("F6", CultureInfo.InvariantCulture);
            var lon = longitude.ToString("F6", CultureInfo.InvariantCulture);
            var url = $"https://nominatim.openstreetmap.org/reverse?format=jsonv2&lat={lat}&lon={lon}&zoom=10&accept-language=de";

            var response = await _httpClient.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(ct);
            return ParseLocation(json);
        }
        catch (OperationCanceledException) { return null; }
        catch { return null; }
        finally
        {
            // Rate-Limit einhalten: 1 Request/Sekunde
            _ = Task.Delay(1100, CancellationToken.None)
                    .ContinueWith(_ => _rateLimiter.Release());
        }
    }

    private static string? ParseLocation(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? GetProp(string name) =>
                root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
                    ? el.GetString() : null;

            string? GetAddr(string name)
            {
                if (!root.TryGetProperty("address", out var addr)) return null;
                return addr.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String
                    ? el.GetString() : null;
            }

            var place = GetAddr("village")
                     ?? GetAddr("town")
                     ?? GetAddr("city")
                     ?? GetAddr("municipality")
                     ?? GetProp("name")
                     ?? GetProp("display_name")?.Split(',').FirstOrDefault()?.Trim();

            return place;
        }
        catch { return null; }
    }
}
