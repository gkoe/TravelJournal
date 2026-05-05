namespace TravelJournal.Core.Services;

public interface IImageRotator
{
    Task RotateAsync(string filePath, int degreesClockwise, CancellationToken ct = default);
}
