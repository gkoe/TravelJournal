using System.Text.RegularExpressions;
using TravelJournal.Core.Models;
using TravelJournal.Core.Utilities;

namespace TravelJournal.Core.Services;

public interface IPhotoRenamer
{
    Task<RenameResult> RenameAsync(
        string                      folderPath,
        IReadOnlyList<Photo>        currentEntries,
        RenameOptions               options,
        IProgress<RenameProgress>?  progress = null,
        CancellationToken           ct       = default);
}

/// <summary>
/// Konfiguriert die Namensgebung beim Umbenennen. Die <see cref="Template"/>
/// darf die Platzhalter <c>{prefix}</c>, <c>{datetime}</c> und <c>{ort}</c>
/// (Alias <c>{location}</c>) enthalten. <c>{datetime}</c> wird als
/// <c>YYMMDD_hhmmss</c> ausgegeben.
/// </summary>
public sealed record RenameOptions(string Prefix, string Template)
{
    public const string DefaultTemplate = "{datetime}_{ort}";

    /// <summary>Datums-/Uhrzeitformat für den {datetime}-Platzhalter (YYMMDD_hhmmss).</summary>
    public const string DateTimeFormat = "yyMMdd_HHmmss";

    public static readonly RenameOptions Default = new(string.Empty, DefaultTemplate);

    private static readonly Regex CollapseSeparators = new("[_\\-]{2,}", RegexOptions.Compiled);

    /// <summary>
    /// Erzeugt den Basisnamen (ohne Endung und ohne Disambiguierungs-Suffix)
    /// für ein Foto anhand der konfigurierten Vorlage.
    /// </summary>
    public string BuildBaseName(DateTime dateTime, string? location)
    {
        var datePart   = dateTime.ToString(DateTimeFormat);
        var ortPart    = FilenameSafeName.FromLocation(location);
        var prefixPart = FilenameSafeName.Sanitize(Prefix);

        var template = string.IsNullOrWhiteSpace(Template) ? DefaultTemplate : Template;

        // Ist ein Präfix gesetzt, die Vorlage referenziert ihn aber nicht,
        // wird er automatisch vorangestellt – sonst bliebe das Präfix-Feld wirkungslos.
        if (!string.IsNullOrEmpty(prefixPart) && !template.Contains("{prefix}"))
            template = "{prefix}_" + template;

        var name = template
            .Replace("{prefix}",   prefixPart)
            .Replace("{datetime}", datePart)
            .Replace("{ort}",      ortPart)
            .Replace("{location}", ortPart);

        // Trennzeichen-Artefakte leerer Platzhalter zusammenfassen und trimmen.
        name = CollapseSeparators.Replace(name, "_").Trim('_', '-', ' ');

        // Fallback: ergibt die Vorlage nichts Verwertbares, zumindest Datum/Zeit verwenden.
        return string.IsNullOrEmpty(name) ? datePart : name;
    }
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
