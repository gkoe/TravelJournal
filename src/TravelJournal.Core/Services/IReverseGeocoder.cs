namespace TravelJournal.Core.Services;

public interface IReverseGeocoder
{
    Task<string?> ResolveAsync(double latitude, double longitude, CancellationToken ct = default);
}
