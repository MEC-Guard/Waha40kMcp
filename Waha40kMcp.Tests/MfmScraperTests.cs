using Waha40kMcp.Data;
using Xunit;

namespace Waha40kMcp.Tests;

public class MfmScraperTests
{
    // ── Regressionstest: FactionSlugs hatte früher doppelte Dictionary-Keys
    // (z.B. "LoV", "SM", "DG" ...), was beim ersten Zugriff auf die Klasse eine
    // ArgumentException zur Laufzeit auslöste und jeden Army-Builder-Aufruf
    // (add_unit, refresh_mfm_points, get_wargear_options) crashen ließ.
    // Dieser Test stellt sicher, dass genau das nie wieder passiert.

    [Theory]
    [InlineData("LoV", "leagues-of-votann")]
    [InlineData("SM", "space-marines")]
    [InlineData("DG", "death-guard")]
    [InlineData("CSM", "chaos-space-marines")]
    [InlineData("NEC", "necrons")]
    [InlineData("GSC", "genestealer-cults")]
    [InlineData("SpaceMarines", "space-marines")]
    [InlineData("LeaguesOfVotann", "leagues-of-votann")]
    public void GetSlugForFaction_ResolvesKnownAbbreviationsWithoutThrowing(string factionId, string expectedSlug)
    {
        var slug = MfmScraper.GetSlugForFaction(factionId);
        Assert.Equal(expectedSlug, slug);
    }

    [Fact]
    public void GetSlugForFaction_FuzzyMatchesNameWithSpaces()
    {
        Assert.Equal("space-marines", MfmScraper.GetSlugForFaction("space marines"));
        Assert.Equal("leagues-of-votann", MfmScraper.GetSlugForFaction("Leagues of Votann"));
    }

    [Fact]
    public void GetSlugForFaction_ReturnsNullForUnknownFaction()
    {
        Assert.Null(MfmScraper.GetSlugForFaction("Xyzzy12345"));
    }

    // ── Regressionstest: der letzte Fuzzy-Fallback matchte früher auch über die
    // sehr kurzen 2-3-Buchstaben-Abkürzungen ("AC", "CD", "AE" ...) per Teilstring-Suche.
    // Dadurch matchte z.B. "TotallyUnknownFactionXyz" fälschlich auf "adeptus-custodes",
    // weil das Wort "FACtion" zufällig die Buchstabenfolge "AC" enthält. Seit dem Fix
    // dürfen nur noch Keys/Eingaben ab 4 Zeichen an der Fuzzy-Suche teilnehmen.

    [Theory]
    [InlineData("TotallyUnknownFactionXyz")]  // enthält "AC" aus "FACtion"
    [InlineData("UNITTEST1")]                  // enthält kein Kurz-Key, aber ähnliche Fälle taten es früher
    [InlineData("Background")]                 // enthält "AC" aus "bACkground"
    [InlineData("Impact")]                     // enthält "AC" aus "imp-AC-t"
    public void GetSlugForFaction_DoesNotFalsePositiveOnShortAbbreviationSubstrings(string input)
    {
        Assert.Null(MfmScraper.GetSlugForFaction(input));
    }

    // ── TryParsePoints ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("70 pts", 70)]
    [InlineData("70pt", 70)]
    [InlineData("150", 150)]
    [InlineData("5", null)]      // unterhalb der Mindestgrenze (10)
    [InlineData("5000", null)]   // oberhalb der Höchstgrenze (3000)
    [InlineData("abc", null)]
    public void TryParsePoints_ParsesValidPointValuesOnly(string line, int? expected)
    {
        Assert.Equal(expected, MfmScraper.TryParsePoints(line));
    }

    // ── IsUnitName ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("WOLF GUARD TERMINATORS", true)]
    [InlineData("150", false)]
    [InlineData("YOUR 1ST UNIT COSTS", false)]
    [InlineData("WARGEAR OPTIONS", false)]
    [InlineData("ab", false)]
    public void IsUnitName_ClassifiesLinesCorrectly(string line, bool expected)
    {
        Assert.Equal(expected, MfmScraper.IsUnitName(line));
    }

    // ── ParseMfmTextFull: End-to-End-Parsing eines synthetischen MFM-Layouts ─

    [Fact]
    public void ParseMfmTextFull_ParsesTieredUnitCostsAndWargearOptions()
    {
        var text = string.Join('\n',
            "WOLF GUARD TERMINATORS",
            "YOUR 1ST UNIT COSTS",
            "5 models",
            "150 pts",
            "10 models",
            "300 pts",
            "YOUR 2ND + UNIT COSTS",
            "5 models",
            "160 pts",
            "10 models",
            "310 pts",
            "WARGEAR OPTIONS",
            "per Storm Shield",
            "5 pts");

        var (units, wargear) = MfmScraper.ParseMfmTextFull(text);

        Assert.True(units.TryGetValue("wolf guard terminators", out var entries));
        Assert.Equal(4, entries!.Count);

        Assert.Equal("5 models", entries[0].Description);
        Assert.Equal(150, entries[0].Cost);
        Assert.Equal(1, entries[0].CopyTier);

        Assert.Equal("10 models", entries[1].Description);
        Assert.Equal(300, entries[1].Cost);
        Assert.Equal(1, entries[1].CopyTier);

        Assert.Equal("5 models", entries[2].Description);
        Assert.Equal(160, entries[2].Cost);
        Assert.Equal(2, entries[2].CopyTier);

        Assert.Equal("10 models", entries[3].Description);
        Assert.Equal(310, entries[3].Cost);
        Assert.Equal(2, entries[3].CopyTier);

        Assert.True(wargear.TryGetValue("wolf guard terminators", out var wargearEntries));
        var stormShield = Assert.Single(wargearEntries!);
        Assert.Equal("Storm Shield", stormShield.Name);
        Assert.Equal(5, stormShield.Cost);
    }

    [Fact]
    public void ParseMfmTextFull_HandlesMultipleUnitsInOneDocument()
    {
        var text = string.Join('\n',
            "INTERCESSOR SQUAD",
            "YOUR UNIT COSTS",
            "5 models",
            "80 pts",
            "TERMINATOR SQUAD",
            "YOUR UNIT COSTS",
            "5 models",
            "200 pts");

        var (units, _) = MfmScraper.ParseMfmTextFull(text);

        Assert.True(units.ContainsKey("intercessor squad"));
        Assert.True(units.ContainsKey("terminator squad"));
        Assert.Equal(80, units["intercessor squad"][0].Cost);
        Assert.Equal(200, units["terminator squad"][0].Cost);
    }
}
