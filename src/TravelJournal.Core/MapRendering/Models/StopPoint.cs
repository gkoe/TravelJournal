namespace TravelJournal.Core.MapRendering.Models;

public sealed record StopPoint(
    DateTime Timestamp,
    double Latitude,
    double Longitude,
    int PhotoIndex);
