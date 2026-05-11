using TravelJournal.Core.Utilities;
using FluentAssertions;

namespace TravelJournal.Core.Tests.Utilities;

public class FilenameSafeNameTests
{
    [Theory]
    [InlineData("Villach",               "Villach")]
    [InlineData("Stein am Rhein",        "SteinAmRhein")]
    [InlineData("Chiusaforte / Scluse",  "ChiusaforteScluse")]
    [InlineData("Grado / Grau",          "GradoGrau")]
    [InlineData("São Paulo",             "SaoPaulo")]
    [InlineData("München",               "Muenchen")]
    [InlineData("Bad Ischl-Lauffen",     "BadIschlLauffen")]
    [InlineData("123 Foo",               "123Foo")]
    [InlineData("!@#$%",                 "")]
    [InlineData("   ",                   "")]
    [InlineData(null,                    "")]
    public void FromLocation_ReturnsExpected(string? input, string expected)
    {
        FilenameSafeName.FromLocation(input).Should().Be(expected);
    }
}
