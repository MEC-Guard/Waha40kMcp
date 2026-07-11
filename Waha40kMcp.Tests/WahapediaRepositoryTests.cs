using Waha40kMcp.Data;
using Waha40kMcp.Models;
using Xunit;

namespace Waha40kMcp.Tests;

public class WahapediaRepositoryTests
{
    /// <summary>
    /// Baut ein WahapediaRepository mit direkt befüllten In-Memory-Daten auf,
    /// ohne InitializeAsync() (das würde echte Netzwerkzugriffe auf wahapedia.ru machen).
    /// </summary>
    private static WahapediaRepository MakeRepo()
    {
        var repo = new WahapediaRepository();

        repo.Factions["SM"] = new Faction { Id = "SM", Name = "Space Marines" };
        repo.Factions["NEC"] = new Faction { Id = "NEC", Name = "Necrons" };

        repo.Datasheets["ds1"] = new Datasheet
        {
            Id = "ds1", Name = "Intercessor Squad", FactionId = "SM", FactionName = "Space Marines",
            Keywords = "Infantry, Battleline", Datasheettype = "Battleline",
        };
        repo.Datasheets["ds2"] = new Datasheet
        {
            Id = "ds2", Name = "Assault Intercessors", FactionId = "SM", FactionName = "Space Marines",
            Keywords = "Infantry", Datasheettype = "Battleline",
        };
        repo.Datasheets["ds3"] = new Datasheet
        {
            Id = "ds3", Name = "Necron Warriors", FactionId = "NEC", FactionName = "Necrons",
            Keywords = "Infantry, Battleline", Datasheettype = "Battleline",
        };

        repo.Stratagems["s1"] = new Stratagem
        {
            Id = "s1", FactionId = "SM", Name = "Rapid Fire", Phase = "Shooting", CpCost = "1",
            Description = "Re-roll hit rolls.",
        };
        repo.Stratagems["s2"] = new Stratagem
        {
            Id = "s2", FactionId = "SM", Name = "Heroic Intervention", Phase = "Movement", CpCost = "2",
            Description = "Charge in the opponent's turn.",
        };
        repo.Stratagems["s3"] = new Stratagem
        {
            Id = "s3", FactionId = "NEC", Name = "Rapid Fire", Phase = "Shooting", CpCost = "1",
            Description = "Necron version.",
        };

        return repo;
    }

    // ── SearchDatasheets ─────────────────────────────────────────────────────

    [Fact]
    public void SearchDatasheets_ExactNameMatch_IsRankedFirst()
    {
        var repo = MakeRepo();

        var results = repo.SearchDatasheets("Intercessor Squad").ToList();

        Assert.Equal("Intercessor Squad", results[0].Name);
    }

    [Fact]
    public void SearchDatasheets_PrefixMatch_RanksBeforeKeywordOnlyMatch()
    {
        var repo = MakeRepo();

        // "Assault" matcht den Namen von "Assault Intercessors" per StartsWith,
        // während andere Treffer nur über Keywords laufen würden.
        var results = repo.SearchDatasheets("Assault").ToList();

        Assert.Equal("Assault Intercessors", results[0].Name);
    }

    [Fact]
    public void SearchDatasheets_FiltersByFaction()
    {
        var repo = MakeRepo();

        var results = repo.SearchDatasheets("Infantry", factionId: "NEC").ToList();

        Assert.All(results, d => Assert.Equal("NEC", d.FactionId));
        Assert.Contains(results, d => d.Name == "Necron Warriors");
    }

    [Fact]
    public void SearchDatasheets_NoMatch_ReturnsEmpty()
    {
        var repo = MakeRepo();

        var results = repo.SearchDatasheets("CompletelyUnknownUnitXyz").ToList();

        Assert.Empty(results);
    }

    // ── SearchStratagems ─────────────────────────────────────────────────────

    [Fact]
    public void SearchStratagems_FiltersByFactionAndPhase()
    {
        var repo = MakeRepo();

        var results = repo.SearchStratagems(factionId: "SM", phase: "Shooting").ToList();

        var strat = Assert.Single(results);
        Assert.Equal("Rapid Fire", strat.Name);
        Assert.Equal("SM", strat.FactionId);
    }

    [Fact]
    public void SearchStratagems_SameNameDifferentFactions_AreDistinguished()
    {
        var repo = MakeRepo();

        var smResults = repo.SearchStratagems(factionId: "SM", query: "Rapid Fire").ToList();
        var necResults = repo.SearchStratagems(factionId: "NEC", query: "Rapid Fire").ToList();

        Assert.Single(smResults);
        Assert.Single(necResults);
        Assert.NotEqual(smResults[0].Description, necResults[0].Description);
    }

    // ── FindFaction ──────────────────────────────────────────────────────────

    [Fact]
    public void FindFaction_MatchesById()
    {
        var repo = MakeRepo();
        var faction = repo.FindFaction("SM");
        Assert.NotNull(faction);
        Assert.Equal("Space Marines", faction!.Name);
    }

    [Fact]
    public void FindFaction_MatchesByPartialName()
    {
        var repo = MakeRepo();
        var faction = repo.FindFaction("Necron");
        Assert.NotNull(faction);
        Assert.Equal("NEC", faction!.Id);
    }

    [Fact]
    public void FindFaction_ReturnsNullForUnknownFaction()
    {
        var repo = MakeRepo();
        Assert.Null(repo.FindFaction("Tyranids"));
    }
}
