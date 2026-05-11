using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TravelJournal.Core.Utilities;

public static class FilenameSafeName
{
    public static string FromLocation(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return string.Empty;

        var folded = location
            .Replace("ä", "ae").Replace("Ä", "Ae")
            .Replace("ö", "oe").Replace("Ö", "Oe")
            .Replace("ü", "ue").Replace("Ü", "Ue")
            .Replace("ß", "ss");

        var normalized = folded.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        var chunks = Regex.Split(sb.ToString(), "[^A-Za-z0-9]+")
            .Where(s => s.Length > 0)
            .Select(CapitalizeFirst);

        return string.Concat(chunks);
    }

    private static string CapitalizeFirst(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
