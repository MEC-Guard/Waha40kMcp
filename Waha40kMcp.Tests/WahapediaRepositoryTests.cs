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

        repo.DetachmentAbilities["da1"] = new DetachmentAbility
        {
            Id = "da1", FactionId = "SM", Detachment = "Gladius Task Force", DetachmentId = "det1",
            Name = "Combat Doctrines", Description = "Grants bonuses depending on battle round.",
        };
        repo.DetachmentAbilities["da2"] = new DetachmentAbility
        {
            Id = "da2", FactionId = "SM", Detachment = "Firestorm Assault Force", DetachmentId = "det2",
            Name = "Fast Assault", Description = "Bonus to charging units.",
        };
        repo.DetachmentAbilities["da3"] = new DetachmentAbility
        {
            Id = "da3", FactionId = "NEC", Detachment = "Awakened Dynasty", DetachmentId = "det3",
            Name = "Reanimation Protocols", Description = "Bring back destroyed models.",
        };

        repo.Enhancements["e1"] = new Enhancement
        {
            Id = "e1", FactionId = "SM", Detachment = "Gladius Task Force", DetachmentId = "det1",
            Name = "Adept of the Codicium", Cost = 15, Description = "Grants a 4+ invulnerable save.",
        };
        repo.Enhancements["e2"] = new Enhancement
        {
            Id = "e2", FactionId = "SM", Detachment = "Firestorm Assault Force", DetachmentId = "det2",
            Name = "Iron Resolve", Cost = 10, Description = "Improves Objective Control.",
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

    // ── Detachments ──────────────────────────────────────────────────────────

    [Fact]
    public void GetDetachmentAbilities_FiltersByFaction()
    {
        var repo = MakeRepo();

        var results = repo.GetDetachmentAbilities("SM").ToList();

        Assert.Equal(2, results.Count);
        Assert.All(results, d => Assert.Equal("SM", d.FactionId));
    }

    [Fact]
    public void GetDetachmentNames_ReturnsDistinctSortedNames()
    {
        var repo = MakeRepo();

        var names = repo.GetDetachmentNames("SM").ToList();

        Assert.Equal(["Firestorm Assault Force", "Gladius Task Force"], names);
    }

    [Fact]
    public void FindDetachment_MatchesExactName()
    {
        var repo = MakeRepo();

        var detachment = repo.FindDetachment("SM", "Gladius Task Force");

        Assert.NotNull(detachment);
        Assert.Equal("Combat Doctrines", detachment!.Name);
    }

    [Fact]
    public void FindDetachment_MatchesPartialName()
    {
        var repo = MakeRepo();

        var detachment = repo.FindDetachment("SM", "Gladius");

        Assert.NotNull(detachment);
        Assert.Equal("Gladius Task Force", detachment!.Detachment);
    }

    [Fact]
    public void FindDetachment_DoesNotLeakAcrossFactions()
    {
        var repo = MakeRepo();

        Assert.Null(repo.FindDetachment("NEC", "Gladius Task Force"));
    }

    // ── Enhancements ─────────────────────────────────────────────────────────

    [Fact]
    public void SearchEnhancements_FiltersByDetachment()
    {
        var repo = MakeRepo();

        var results = repo.SearchEnhancements("SM", "Gladius Task Force").ToList();

        var enhancement = Assert.Single(results);
        Assert.Equal("Adept of the Codicium", enhancement.Name);
    }

    [Fact]
    public void FindEnhancement_MatchesWithinCorrectDetachmentOnly()
    {
        var repo = MakeRepo();

        var found = repo.FindEnhancement("SM", "Iron Resolve", "Firestorm Assault Force");
        var notFound = repo.FindEnhancement("SM", "Iron Resolve", "Gladius Task Force");

        Assert.NotNull(found);
        Assert.Equal(10, found!.Cost);
        Assert.Null(notFound);
    }
}
