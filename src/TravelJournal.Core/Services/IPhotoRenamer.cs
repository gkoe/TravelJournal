using TravelJournal.Core.Models;

namespace TravelJournal.Core.Services;

public interface IPhotoRenamer
{
    Task<RenameResult> RenameAsync(
        string                      folderPath,
        IReadOnlyList<Photo>        currentEntries,
        IProgress<RenameProgress>?  progress = null,
        CancellationToken           ct       = default);
}

public sealed record RenameProgress(int Current, int Total, string? Message);

public sealed record RenameResult(
    IReadOnlyList<RenameOperation> Renamed,
    IReadOnlyList<string>          SkippedAlreadyMatching,
    IReadOnlyList<string>          SkippedNoDateTime,
    IReadOnlyList<RenameError>     Errors
);

public sealed record RenameOperation(string OldFilename, string NewFilename);

public sealed record RenameError(string Filename, string Reason);
