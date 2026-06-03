using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace TravelJournal.Core.Utilities;

public static class FilenameSafeName
{
    public static string FromLocation(string? location)
    {
        if (string.IsNullOrWhiteSpace(location)) return string.Empty;

        var ascii = ToAscii(location);

        var chunks = Regex.Split(ascii, "[^A-Za-z0-9]+")
            .Where(s => s.Length > 0)
            .Select(CapitalizeFirst);

        return string.Concat(chunks);
    }

    /// <summary>
    /// Macht einen frei eingegebenen Text (z.B. einen Präfix wie "rhodos")
    /// dateinamenstauglich: Umlaute werden gefaltet, Diakritika entfernt und
    /// alle übrigen ungültigen Zeichen verworfen. Im Unterschied zu
    /// <see cref="FromLocation"/> bleibt die ursprüngliche Groß-/Kleinschreibung erhalten.
    /// </summary>
    public static string Sanitize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        return Regex.Replace(ToAscii(text), "[^A-Za-z0-9]+", "");
    }

    private static string ToAscii(string input)
    {
        var folded = input
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
        return sb.ToString();
    }

    private static string CapitalizeFirst(string s) =>
        s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
}
