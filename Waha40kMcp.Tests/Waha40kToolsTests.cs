using Waha40kMcp.Data;
using Waha40kMcp.Models;
using Waha40kMcp.Tools;
using Xunit;

namespace Waha40kMcp.Tests;

public class Waha40kToolsTests
{
    private static (WahapediaRepository Repo, Waha40kTools Tools) MakeFixture()
    {
        var repo = new WahapediaRepository();

        repo.Factions["SM"] = new Faction { Id = "SM", Name = "Space Marines" };

        var ds = new Datasheet
        {
            Id = "ds1", Name = "Intercessor Squad", FactionId = "SM", FactionName = "Space Marines",
            Keywords = "Infantry, Battleline", Datasheettype = "Battleline",
        };
        ds.Models.Add(new DatasheetModel { Name = "Intercessor", M = "6\"", T = "4", Sv = "3+", W = "2", Ld = "6+", Oc = "2" });
        ds.Weapons.Add(new DatasheetWeapon { Name = "Bolt Rifle", Type = "ranged", Range = "24", A = "2", BsWs = "3+", S = "4", Ap = "-1", D = "1" });
        ds.PointsCosts.Add(new PointsCostEntry { Description = "5 models", Cost = 80 });
        ds.PointsCosts.Add(new PointsCostEntry { Description = "10 models", Cost = 160 });
        repo.Datasheets[ds.Id] = ds;

        var ds2 = new Datasheet
        {
            Id = "ds2", Name = "Captain", FactionId = "SM", FactionName = "Space Marines",
            Keywords = "Character, Infantry", Datasheettype = "Character",
        };
        ds2.Models.Add(new DatasheetModel { Name = "Captain", M = "6\"", T = "4", Sv = "3+", W = "5", Ld = "6+", Oc = "1" });
        ds2.PointsCosts.Add(new PointsCostEntry { Description = "1 model", Cost = 75 });
        repo.Datasheets[ds2.Id] = ds2;

        repo.DetachmentAbilities["da1"] = new DetachmentAbility
        {
            Id = "da1", FactionId = "SM", Detachment = "Gladius Task Force",
            Name = "Combat Doctrines", Description = "Grants bonuses depending on battle round.",
        };

        repo.Enhancements["e1"] = new Enhancement
        {
            Id = "e1", FactionId = "SM", Detachment = "Gladius Task Force",
            Name = "Adept of the Codicium", Cost = 15, Description = "Grants a 4+ invulnerable save.",
        };

        return (repo, new Waha40kTools(repo));
    }

    [Fact]
    public void GetDatasheet_ReturnsStatsWeaponsAndPoints()
    {
        var (_, tools) = MakeFixture();

        var result = tools.get_datasheet("Intercessor Squad");

        Assert.Contains("Intercessor Squad", result);
        Assert.Contains("Space Marines", result);
        Assert.Contains("Bolt Rifle", result);
        Assert.Contains("80", result);
    }

    [Fact]
    public void GetDatasheet_UnknownUnit_ReturnsNotFoundMessage()
    {
        var (_, tools) = MakeFixture();

        var result = tools.get_datasheet("Nonexistent Unit Xyz");

        Assert.Contains("Keine Einheit gefunden", result);
    }

    [Fact]
    public void ListFactionUnits_FiltersByKeyword()
    {
        var (_, tools) = MakeFixture();

        var result = tools.list_faction_units("Space Marines", "Character");

        Assert.Contains("Captain", result);
        Assert.DoesNotContain("Intercessor Squad", result);
    }

    [Fact]
    public void ListFactionUnits_PartialFactionName_StillMatches()
    {
        var (_, tools) = MakeFixture();

        // "Space Marine" (ohne s) ist ein Teilstring von "Space Marines" -> FindFaction matcht per Contains.
        var result = tools.list_faction_units("Space Marine");

        Assert.Contains("Space Marines", result);
        Assert.Contains("Captain", result);
    }

    [Fact]
    public void ListFactionUnits_UnknownFaction_ReturnsNotFoundMessage()
    {
        var (_, tools) = MakeFixture();

        var result = tools.list_faction_units("Completely Unknown Faction Xyz");

        Assert.Contains("nicht gefunden", result);
    }

    [Fact]
    public void CalculateArmyPoints_SumsCostsAndFlagsOverLimit()
    {
        var (_, tools) = MakeFixture();

        var result = tools.calculate_army_points("Intercessor Squad, Captain", "Space Marines", points_limit: 100);

        // Intercessor Squad nimmt den letzten (höchsten) Punktewert: 160. + Captain 75 = 235.
        Assert.Contains("235", result);
        Assert.Contains("ÜBERSCHRITTEN", result);
    }

    [Fact]
    public void CalculateArmyPoints_UnknownUnit_IsListedAsNotFound()
    {
        var (_, tools) = MakeFixture();

        var result = tools.calculate_army_points("Intercessor Squad, Nonexistent Unit", "Space Marines");

        Assert.Contains("Nicht gefunden", result);
        Assert.Contains("Nonexistent Unit", result);
    }

    [Fact]
    public void CompareUnits_ShowsBothUnitsStatsAndPoints()
    {
        var (_, tools) = MakeFixture();

        var result = tools.compare_units("Intercessor Squad", "Captain");

        Assert.Contains("Intercessor Squad", result);
        Assert.Contains("Captain", result);
        Assert.Contains("160", result); // höchster Punktewert Intercessor Squad
        Assert.Contains("75", result);  // Punktewert Captain
    }

    [Fact]
    public void ListFactions_ListsAllKnownFactions()
    {
        var (_, tools) = MakeFixture();

        var result = tools.list_factions();

        Assert.Contains("Space Marines", result);
    }

    [Fact]
    public void ListDetachments_ReturnsAbilityText()
    {
        var (_, tools) = MakeFixture();

        var result = tools.list_detachments("Space Marines");

        Assert.Contains("Gladius Task Force", result);
        Assert.Contains("Combat Doctrines", result);
        Assert.Contains("Grants bonuses depending on battle round.", result);
    }

    [Fact]
    public void ListDetachments_UnknownFaction_ReturnsNotFoundMessage()
    {
        var (_, tools) = MakeFixture();

        var result = tools.list_detachments("Completely Unknown Faction Xyz");

        Assert.Contains("nicht gefunden", result);
    }

    [Fact]
    public void ListEnhancements_ReturnsCostAndDescription()
    {
        var (_, tools) = MakeFixture();

        var result = tools.list_enhancements("Space Marines");

        Assert.Contains("Adept of the Codicium", result);
        Assert.Contains("15", result);
        Assert.Contains("Grants a 4+ invulnerable save.", result);
    }

    [Fact]
    public void ListEnhancements_FiltersByDetachment()
    {
        var (_, tools) = MakeFixture();

        var matching = tools.list_enhancements("Space Marines", "Gladius Task Force");
        var nonMatching = tools.list_enhancements("Space Marines", "Firestorm Assault Force");

        Assert.Contains("Adept of the Codicium", matching);
        Assert.Contains("Keine Enhancements", nonMatching);
    }
}
