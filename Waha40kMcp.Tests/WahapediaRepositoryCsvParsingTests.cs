using Waha40kMcp.Data;
using Xunit;

namespace Waha40kMcp.Tests;

/// <summary>
/// Regressionstests gegen das ECHTE Wahapedia-CSV-Schema (per curl gegen wahapedia.ru
/// verifiziert, siehe Kommentare). WahapediaRepositoryTests.cs testet SearchDatasheets & Co.
/// gegen direkt befüllte Fixtures und würde falsche Spaltennamen in BuildDatasheets NIE
/// bemerken — genau das ist hier vorher passiert: "datasheettype"/"keywords" existierten in den
/// echten CSVs schlicht nicht, und das "legend"-Feld wurde fälschlich als Legends-Status-Flag
/// verwendet statt als reiner Fluff-Text. Diese Tests fahren stattdessen echte BuildXyz()-Methoden
/// mit literalen CSV-Zeilen im echten Format, damit ein falscher Spaltenname wieder auffällt.
/// </summary>
public class WahapediaRepositoryCsvParsingTests
{
    private const string FactionsCsv = "id|name|link|\nSM|Space Marines|https://wahapedia.ru/wh40k10ed/factions/space-marines|\n";

    private const string SourceCsv =
        "id|name|type|edition|version|errata_date|errata_link|\n" +
        "000000139|Space Marines|Faction Pack|10|1.8|06.05.2026 0:00:00|https://example.com/sm.pdf|\n" +
        "000000356|Space Marines (Warhammer Legends)|Faction Pack|0||01.01.0001 0:00:00|\n";

    // Reales Feldlayout: id|name|faction_id|source_id|legend|role|loadout|transport|virtual|leader_head|leader_footer|damaged_w|damaged_description|link|
    private const string DatasheetsCsv =
        "id|name|faction_id|source_id|legend|role|loadout|transport|virtual|leader_head|leader_footer|damaged_w|damaged_description|link|\n" +
        "000000001|Intercessor Squad|SM|000000139|Intercessor Squads are capable of laying down punishing fire.|Battleline|<b>Equipped with:</b> bolt rifle.||false|||||https://example.com/intercessor|\n" +
        "000000002|Land Speeder Tempest|SM|000000356||Other|<b>Equipped with:</b> assault cannon.||false|||||https://example.com/tempest|\n";

    // Reales Feldlayout: datasheet_id|keyword|model|is_faction_keyword|
    private const string KeywordsCsv =
        "datasheet_id|keyword|model|is_faction_keyword|\n" +
        "000000001|Infantry||false|\n" +
        "000000001|Battleline||false|\n" +
        "000000001|Adeptus Astartes||true|\n" +
        "000000001|Some Model-Only Keyword|Sergeant|false|\n"; // sollte ignoriert werden (model-spezifisch)

    // Reales Feldlayout: datasheet_id|line|line_in_wargear|dice|name|description|range|type|A|BS_WS|S|AP|D|
    private const string WargearCsv =
        "datasheet_id|line|line_in_wargear|dice|name|description|range|type|A|BS_WS|S|AP|D|\n" +
        "000000001|1|1||Bolt rifle|rapid fire 1|24|Ranged|2|3|4|-1|1|\n";

    private static WahapediaRepository BuildRepoFromCsv()
    {
        var repo = new WahapediaRepository();
        repo.BuildFactions(WahapediaRepository.ParseCsv(FactionsCsv));
        repo.BuildDatasheets(
            sheetRows: WahapediaRepository.ParseCsv(DatasheetsCsv),
            modelRows: [],
            weaponRows: WahapediaRepository.ParseCsv(WargearCsv),
            abilityRows: [],
            optionRows: [],
            pointsRows: [],
            sourceRows: WahapediaRepository.ParseCsv(SourceCsv),
            keywordRows: WahapediaRepository.ParseCsv(KeywordsCsv));
        return repo;
    }

    [Fact]
    public void BuildDatasheets_DoesNotExcludeUnitsWithFluffText()
    {
        // Regression: "legend" ist Fluff-Text, kein Legends-Status-Flag. Intercessor Squad hat
        // nicht-leeren Fluff-Text und ist trotzdem eine ganz normale, aktuelle Matched-Play-Einheit.
        var repo = BuildRepoFromCsv();

        Assert.True(repo.Datasheets.ContainsKey("000000001"));
    }

    [Fact]
    public void BuildDatasheets_ExcludesUnitsFromLegendsSource()
    {
        // "Land Speeder Tempest" zeigt via source_id auf "Space Marines (Warhammer Legends)"
        // in Source.csv -> das ist eine echte Legends-Einheit und muss ausgeschlossen werden.
        var repo = BuildRepoFromCsv();

        Assert.False(repo.Datasheets.ContainsKey("000000002"));
    }

    [Fact]
    public void BuildDatasheets_ReadsDatasheettypeFromRoleColumn()
    {
        var repo = BuildRepoFromCsv();

        Assert.Equal("Battleline", repo.Datasheets["000000001"].Datasheettype);
    }

    [Fact]
    public void BuildDatasheets_PopulatesLegendTextFromLegendColumn()
    {
        var repo = BuildRepoFromCsv();

        Assert.Equal("Intercessor Squads are capable of laying down punishing fire.",
            repo.Datasheets["000000001"].LegendText);
    }

    [Fact]
    public void BuildDatasheets_PopulatesKeywordsFromSeparateKeywordsCsv_ExcludingModelSpecific()
    {
        var repo = BuildRepoFromCsv();
        var ds = repo.Datasheets["000000001"];

        Assert.Contains("Infantry", ds.Keywords);
        Assert.Contains("Battleline", ds.Keywords);
        Assert.DoesNotContain("Some Model-Only Keyword", ds.Keywords);
        Assert.Equal("Adeptus Astartes", ds.FactionKeywords);
    }

    [Fact]
    public void BuildDatasheets_PopulatesWeaponKeywordsFromWargearDescriptionColumn()
    {
        var repo = BuildRepoFromCsv();
        var weapon = Assert.Single(repo.Datasheets["000000001"].Weapons);

        Assert.Equal("rapid fire 1", weapon.Keywords);
    }
}
