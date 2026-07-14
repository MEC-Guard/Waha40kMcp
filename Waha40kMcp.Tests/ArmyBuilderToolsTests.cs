using Waha40kMcp.Data;
using Waha40kMcp.Models;
using Waha40kMcp.Tools;
using Xunit;

namespace Waha40kMcp.Tests;

/// <summary>
/// ArmyBuilderTools nutzt ein statisches Dictionary für den MFM-Cache (siehe ArmyBuilderTools.cs),
/// das über den gesamten Testlauf hinweg geteilt wird. Damit sich Tests nicht gegenseitig stören,
/// bekommt jeder Test einen eigenen, per Zähler eindeutigen Army- und Faction-Namen.
/// Die Army-Persistenz (ArmyRepository) bekommt zusätzlich pro Test ein eigenes, isoliertes
/// Temp-Verzeichnis (analog StrategyRepositoryTests), damit Tests nicht dieselbe armies.json teilen.
/// </summary>
public class ArmyBuilderToolsTests : IDisposable
{
    private readonly string _tempDir;

    public ArmyBuilderToolsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Waha40kMcpTests_Army_" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private sealed record Fixture(WahapediaRepository Repo, ArmyBuilderTools Tools, FakeMfmScraper Scraper, string FactionId, string ArmyName);

    private static int _fixtureCounter;

    private Fixture MakeFixture()
    {
        // Bewusst KEIN Guid-Hex-Suffix und KEIN Wort wie "FACTION": MfmScraper.GetSlugForFaction()
        // matcht Kurz-Abkürzungen (z.B. "AC", "CD", "AE" — alles gültige Hex-Zeichen bzw. in
        // "faCtion" enthalten) per Teilstring-Suche. Ein rein numerisches Suffix auf einem
        // dafür geprüften Wortstamm umgeht das zuverlässig.
        var suffix = System.Threading.Interlocked.Increment(ref _fixtureCounter);
        var factionId = $"UNITTEST{suffix}";
        var armyName = $"TestArmy{suffix}";

        var repo = new WahapediaRepository();
        repo.Factions[factionId] = new Faction { Id = factionId, Name = "Leagues of Votann" };

        var scraper = new FakeMfmScraper();
        var armyRepo = new ArmyRepository(_tempDir);
        var tools = new ArmyBuilderTools(repo, scraper, armyRepo);

        return new Fixture(repo, tools, scraper, factionId, armyName);
    }

    private static Datasheet AddDatasheet(WahapediaRepository repo, string factionId, string name, string id)
    {
        var ds = new Datasheet { Id = id, Name = name, FactionId = factionId, FactionName = "Leagues of Votann" };
        repo.Datasheets[id] = ds;
        return ds;
    }

    private static void AddDetachment(WahapediaRepository repo, string factionId, string detachmentName)
    {
        var id = Guid.NewGuid().ToString("N");
        repo.DetachmentAbilities[id] = new DetachmentAbility
        {
            Id = id, FactionId = factionId, Detachment = detachmentName,
            Name = "Test Ability", Description = "Test detachment ability description.",
        };
    }

    private static void AddEnhancement(WahapediaRepository repo, string factionId, string detachmentName, string name, int cost)
    {
        var id = Guid.NewGuid().ToString("N");
        repo.Enhancements[id] = new Enhancement
        {
            Id = id, FactionId = factionId, Detachment = detachmentName,
            Name = name, Cost = cost, Description = "Test enhancement description.",
        };
    }

    [Fact]
    public void CreateArmy_ThenShowArmy_ReportsEmptyArmy()
    {
        var fx = MakeFixture();

        var created = fx.Tools.create_army(fx.ArmyName, "Leagues of Votann", 2000);
        Assert.Contains("erstellt", created);

        var shown = fx.Tools.show_army(fx.ArmyName);
        Assert.Contains("Noch keine Einheiten", shown);
    }

    [Fact]
    public async Task AddUnit_UsesTier1PricingForFirstTwoCopiesAndTier2ForThirdCopy()
    {
        var fx = MakeFixture();
        AddDatasheet(fx.Repo, fx.FactionId, "Einhyr Hearthguard", "ds1");

        var slug = MfmScraper.GetSlugForFaction("Leagues of Votann")!;
        fx.Scraper.SetUnitPoints(slug, "einhyr hearthguard",
        [
            new PointsCostEntry { Description = "5 models", Cost = 150, CopyTier = 1 },
            new PointsCostEntry { Description = "5 models", Cost = 160, CopyTier = 2 },
        ]);

        fx.Tools.create_army(fx.ArmyName, "Leagues of Votann", 2000);

        var first = await fx.Tools.add_unit(fx.ArmyName, "Einhyr Hearthguard", 5);
        Assert.Contains("150", first);

        var second = await fx.Tools.add_unit(fx.ArmyName, "Einhyr Hearthguard", 5);
        Assert.Contains("150", second);

        var third = await fx.Tools.add_unit(fx.ArmyName, "Einhyr Hearthguard", 5);
        Assert.Contains("160", third);
        Assert.Contains("teurere Staffelung", third);
    }

    [Fact]
    public async Task AddUnit_PicksPointsEntryMatchingRequestedModelCount()
    {
        var fx = MakeFixture();
        AddDatasheet(fx.Repo, fx.FactionId, "Einhyr Hearthguard", "ds1");

        var slug = MfmScraper.GetSlugForFaction("Leagues of Votann")!;
        fx.Scraper.SetUnitPoints(slug, "einhyr hearthguard",
        [
            new PointsCostEntry { Description = "5 models", Cost = 150 },
            new PointsCostEntry { Description = "10 models", Cost = 300 },
        ]);

        fx.Tools.create_army(fx.ArmyName, "Leagues of Votann", 2000);

        var result = await fx.Tools.add_unit(fx.ArmyName, "Einhyr Hearthguard", 10);

        Assert.Contains("300", result);
    }

    [Fact]
    public async Task AddUnit_FallsBackToWahapediaPointsWhenMfmHasNoData()
    {
        var fx = MakeFixture();
        var ds = AddDatasheet(fx.Repo, fx.FactionId, "Hekaton Land Fortress", "ds2");
        ds.PointsCosts.Add(new PointsCostEntry { Description = "1 model", Cost = 220 });

        fx.Tools.create_army(fx.ArmyName, "Leagues of Votann", 2000);

        var result = await fx.Tools.add_unit(fx.ArmyName, "Hekaton Land Fortress");

        Assert.Contains("220", result);
    }

    [Fact]
    public async Task RemoveUnit_RemovesEntryAndRecalculatesTotal()
    {
        var fx = MakeFixture();
        AddDatasheet(fx.Repo, fx.FactionId, "Einhyr Hearthguard", "ds1");
        var slug = MfmScraper.GetSlugForFaction("Leagues of Votann")!;
        fx.Scraper.SetUnitPoints(slug, "einhyr hearthguard", [new PointsCostEntry { Description = "5 models", Cost = 150 }]);

        fx.Tools.create_army(fx.ArmyName, "Leagues of Votann", 2000);
        await fx.Tools.add_unit(fx.ArmyName, "Einhyr Hearthguard", 5);

        var removed = fx.Tools.remove_unit(fx.ArmyName, 1);

        Assert.Contains("entfernt", removed);
        var shown = fx.Tools.show_army(fx.ArmyName);
        Assert.Contains("0 / 2000", shown);
    }

    [Fact]
    public async Task ShowArmy_FlagsPointsLimitExceeded()
    {
        var fx = MakeFixture();
        AddDatasheet(fx.Repo, fx.FactionId, "Einhyr Hearthguard", "ds1");
        var slug = MfmScraper.GetSlugForFaction("Leagues of Votann")!;
        fx.Scraper.SetUnitPoints(slug, "einhyr hearthguard", [new PointsCostEntry { Description = "5 models", Cost = 150 }]);

        fx.Tools.create_army(fx.ArmyName, "Leagues of Votann", 100); // absichtlich niedriges Limit
        await fx.Tools.add_unit(fx.ArmyName, "Einhyr Hearthguard", 5);

        var shown = fx.Tools.show_army(fx.ArmyName);

        Assert.Contains("ÜBERSCHRITTEN", shown);
    }

    [Fact]
    public async Task GetWargearOptions_ReturnsConfiguredOptions()
    {
        var fx = MakeFixture();
        AddDatasheet(fx.Repo, fx.FactionId, "Wolf Guard Terminators", "ds3");

        var slug = MfmScraper.GetSlugForFaction("Leagues of Votann")!;
        fx.Scraper.SetUnitPoints(slug, "wolf guard terminators", [new PointsCostEntry { Description = "5 models", Cost = 150 }]);
        fx.Scraper.SetWargearOptions(slug, "wolf guard terminators", [new WargearOptionCost { Name = "Storm Shield", Cost = 5 }]);

        var result = await fx.Tools.get_wargear_options("Wolf Guard Terminators");

        Assert.Contains("Storm Shield", result);
        Assert.Contains("+5", result);
    }

    [Fact]
    public void RemoveUnit_InvalidIndex_ReturnsErrorMessage()
    {
        var fx = MakeFixture();
        fx.Tools.create_army(fx.ArmyName, "Leagues of Votann", 2000);

        var result = fx.Tools.remove_unit(fx.ArmyName, 99);

        Assert.Contains("Ungültiger Index", result);
    }

    // ── Detachments & Enhancements ───────────────────────────────────────────

    [Fact]
    public void CreateArmy_WithValidDetachment_SetsDetachmentAndShowsInShowArmy()
    {
        var fx = MakeFixture();
        AddDetachment(fx.Repo, fx.FactionId, "Oathband");

        var created = fx.Tools.create_army(fx.ArmyName, "Leagues of Votann", 2000, detachment: "Oathband");
        Assert.Contains("Oathband", created);

        var shown = fx.Tools.show_army(fx.ArmyName);
        Assert.Contains("Oathband", shown);
    }

    [Fact]
    public void CreateArmy_WithUnknownDetachment_ReturnsErrorListingAvailable()
    {
        var fx = MakeFixture();
        AddDetachment(fx.Repo, fx.FactionId, "Oathband");

        var result = fx.Tools.create_army(fx.ArmyName, "Leagues of Votann", 2000, detachment: "Nonexistent");

        Assert.Contains("nicht gefunden", result);
        Assert.Contains("Oathband", result);
    }

    [Fact]
    public void SetDetachment_ChangesArmyDetachment()
    {
        var fx = MakeFixture();
        AddDetachment(fx.Repo, fx.FactionId, "Oathband");
        fx.Tools.create_army(fx.ArmyName, "Leagues of Votann", 2000);

        var result = fx.Tools.set_detachment(fx.ArmyName, "Oathband");

        Assert.Contains("Oathband", result);
        var shown = fx.Tools.show_army(fx.ArmyName);
        Assert.Contains("Oathband", shown);
    }

    [Fact]
    public void SetDetachment_UnknownDetachment_ReturnsError()
    {
        var fx = MakeFixture();
        fx.Tools.create_army(fx.ArmyName, "Leagues of Votann", 2000);

        var result = fx.Tools.set_detachment(fx.ArmyName, "Nonexistent");

        Assert.Contains("nicht gefunden", result);
    }

    [Fact]
    public async Task AddEnhancement_WithoutDetachmentSet_ReturnsError()
    {
        var fx = MakeFixture();
        AddDatasheet(fx.Repo, fx.FactionId, "Einhyr Hearthguard", "ds1");
        var slug = MfmScraper.GetSlugForFaction("Leagues of Votann")!;
        fx.Scraper.SetUnitPoints(slug, "einhyr hearthguard", [new PointsCostEntry { Description = "5 models", Cost = 150 }]);

        fx.Tools.create_army(fx.ArmyName, "Leagues of Votann", 2000); // kein Detachment gesetzt
        await fx.Tools.add_unit(fx.ArmyName, "Einhyr Hearthguard", 5);

        var result = fx.Tools.add_enhancement(fx.ArmyName, 1, "Voidstrider");

        Assert.Contains("kein Detachment", result);
    }

    [Fact]
    public async Task AddEnhancement_AttachesToUnitAndAddsCostToTotal()
    {
        var fx = MakeFixture();
        AddDetachment(fx.Repo, fx.FactionId, "Oathband");
        AddEnhancement(fx.Repo, fx.FactionId, "Oathband", "Voidstrider", 15);
        AddDatasheet(fx.Repo, fx.FactionId, "Einhyr Hearthguard", "ds1");
        var slug = MfmScraper.GetSlugForFaction("Leagues of Votann")!;
        fx.Scraper.SetUnitPoints(slug, "einhyr hearthguard", [new PointsCostEntry { Description = "5 models", Cost = 150 }]);

        fx.Tools.create_army(fx.ArmyName, "Leagues of Votann", 2000, detachment: "Oathband");
        await fx.Tools.add_unit(fx.ArmyName, "Einhyr Hearthguard", 5);

        var result = fx.Tools.add_enhancement(fx.ArmyName, 1, "Voidstrider");

        Assert.Contains("Voidstrider", result);
        var shown = fx.Tools.show_army(fx.ArmyName);
        Assert.Contains("Voidstrider (+15)", shown);
        Assert.Contains("165 / 2000", shown); // 150 Grundpreis + 15 Enhancement
    }

    [Fact]
    public async Task AddEnhancement_SecondEnhancementOnSameUnit_IsRejected()
    {
        var fx = MakeFixture();
        AddDetachment(fx.Repo, fx.FactionId, "Oathband");
        AddEnhancement(fx.Repo, fx.FactionId, "Oathband", "Voidstrider", 15);
        AddEnhancement(fx.Repo, fx.FactionId, "Oathband", "Second Enhancement", 20);
        AddDatasheet(fx.Repo, fx.FactionId, "Einhyr Hearthguard", "ds1");
        var slug = MfmScraper.GetSlugForFaction("Leagues of Votann")!;
        fx.Scraper.SetUnitPoints(slug, "einhyr hearthguard", [new PointsCostEntry { Description = "5 models", Cost = 150 }]);

        fx.Tools.create_army(fx.ArmyName, "Leagues of Votann", 2000, detachment: "Oathband");
        await fx.Tools.add_unit(fx.ArmyName, "Einhyr Hearthguard", 5);
        fx.Tools.add_enhancement(fx.ArmyName, 1, "Voidstrider");

        var result = fx.Tools.add_enhancement(fx.ArmyName, 1, "Second Enhancement");

        Assert.Contains("bereits", result);
    }

    [Fact]
    public async Task RemoveEnhancement_RemovesAndSubtractsCost()
    {
        var fx = MakeFixture();
        AddDetachment(fx.Repo, fx.FactionId, "Oathband");
        AddEnhancement(fx.Repo, fx.FactionId, "Oathband", "Voidstrider", 15);
        AddDatasheet(fx.Repo, fx.FactionId, "Einhyr Hearthguard", "ds1");
        var slug = MfmScraper.GetSlugForFaction("Leagues of Votann")!;
        fx.Scraper.SetUnitPoints(slug, "einhyr hearthguard", [new PointsCostEntry { Description = "5 models", Cost = 150 }]);

        fx.Tools.create_army(fx.ArmyName, "Leagues of Votann", 2000, detachment: "Oathband");
        await fx.Tools.add_unit(fx.ArmyName, "Einhyr Hearthguard", 5);
        fx.Tools.add_enhancement(fx.ArmyName, 1, "Voidstrider");

        var result = fx.Tools.remove_enhancement(fx.ArmyName, 1);

        Assert.Contains("Voidstrider", result);
        var shown = fx.Tools.show_army(fx.ArmyName);
        Assert.Contains("150 / 2000", shown);
    }

    [Fact]
    public async Task RemoveEnhancement_UnitWithoutEnhancement_ReturnsError()
    {
        var fx = MakeFixture();
        AddDatasheet(fx.Repo, fx.FactionId, "Einhyr Hearthguard", "ds1");
        var slug = MfmScraper.GetSlugForFaction("Leagues of Votann")!;
        fx.Scraper.SetUnitPoints(slug, "einhyr hearthguard", [new PointsCostEntry { Description = "5 models", Cost = 150 }]);

        fx.Tools.create_army(fx.ArmyName, "Leagues of Votann", 2000);
        await fx.Tools.add_unit(fx.ArmyName, "Einhyr Hearthguard", 5);

        var result = fx.Tools.remove_enhancement(fx.ArmyName, 1);

        Assert.Contains("keine Enhancement", result);
    }

    [Fact]
    public void RemoveEnhancement_InvalidIndex_ReturnsError()
    {
        var fx = MakeFixture();
        fx.Tools.create_army(fx.ArmyName, "Leagues of Votann", 2000);

        var result = fx.Tools.remove_enhancement(fx.ArmyName, 1);

        Assert.Contains("Ungültiger Index", result);
    }

    // ── Leader-Zuordnung ─────────────────────────────────────────────────────

    [Fact]
    public async Task AttachLeader_SetsAttachmentAndShowsInShowArmy()
    {
        var fx = MakeFixture();
        AddDatasheet(fx.Repo, fx.FactionId, "Hearthkyn Chief", "ds1");
        AddDatasheet(fx.Repo, fx.FactionId, "Einhyr Hearthguard", "ds2");
        var slug = MfmScraper.GetSlugForFaction("Leagues of Votann")!;
        fx.Scraper.SetUnitPoints(slug, "hearthkyn chief", [new PointsCostEntry { Description = "1 model", Cost = 70 }]);
        fx.Scraper.SetUnitPoints(slug, "einhyr hearthguard", [new PointsCostEntry { Description = "5 models", Cost = 150 }]);

        fx.Tools.create_army(fx.ArmyName, "Leagues of Votann", 2000);
        await fx.Tools.add_unit(fx.ArmyName, "Hearthkyn Chief", 1);
        await fx.Tools.add_unit(fx.ArmyName, "Einhyr Hearthguard", 5);

        var result = fx.Tools.attach_leader(fx.ArmyName, 1, 2);

        Assert.Contains("Hearthkyn Chief", result);
        Assert.Contains("Einhyr Hearthguard", result);
        var shown = fx.Tools.show_army(fx.ArmyName);
        Assert.Contains("#2", shown); // "Führt an"-Spalte zeigt auf Index 2
    }

    [Fact]
    public async Task AttachLeader_InvalidIndices_ReturnsError()
    {
        var fx = MakeFixture();
        AddDatasheet(fx.Repo, fx.FactionId, "Hearthkyn Chief", "ds1");
        var slug = MfmScraper.GetSlugForFaction("Leagues of Votann")!;
        fx.Scraper.SetUnitPoints(slug, "hearthkyn chief", [new PointsCostEntry { Description = "1 model", Cost = 70 }]);

        fx.Tools.create_army(fx.ArmyName, "Leagues of Votann", 2000);
        await fx.Tools.add_unit(fx.ArmyName, "Hearthkyn Chief", 1);

        var result = fx.Tools.attach_leader(fx.ArmyName, 1, 99);

        Assert.Contains("Ungültiger Ziel-Index", result);
    }

    [Fact]
    public async Task AttachLeader_SelfAttachment_ReturnsError()
    {
        var fx = MakeFixture();
        AddDatasheet(fx.Repo, fx.FactionId, "Hearthkyn Chief", "ds1");
        var slug = MfmScraper.GetSlugForFaction("Leagues of Votann")!;
        fx.Scraper.SetUnitPoints(slug, "hearthkyn chief", [new PointsCostEntry { Description = "1 model", Cost = 70 }]);

        fx.Tools.create_army(fx.ArmyName, "Leagues of Votann", 2000);
        await fx.Tools.add_unit(fx.ArmyName, "Hearthkyn Chief", 1);

        var result = fx.Tools.attach_leader(fx.ArmyName, 1, 1);

        Assert.Contains("nicht sich selbst", result);
    }

    [Fact]
    public async Task AttachLeader_TargetAlreadyLeadsAnotherUnit_IsRejected()
    {
        var fx = MakeFixture();
        AddDatasheet(fx.Repo, fx.FactionId, "Leader A", "ds1");
        AddDatasheet(fx.Repo, fx.FactionId, "Leader B", "ds2");
        AddDatasheet(fx.Repo, fx.FactionId, "Squad", "ds3");
        var slug = MfmScraper.GetSlugForFaction("Leagues of Votann")!;
        fx.Scraper.SetUnitPoints(slug, "leader a", [new PointsCostEntry { Description = "1 model", Cost = 70 }]);
        fx.Scraper.SetUnitPoints(slug, "leader b", [new PointsCostEntry { Description = "1 model", Cost = 70 }]);
        fx.Scraper.SetUnitPoints(slug, "squad", [new PointsCostEntry { Description = "5 models", Cost = 150 }]);

        fx.Tools.create_army(fx.ArmyName, "Leagues of Votann", 2000);
        await fx.Tools.add_unit(fx.ArmyName, "Leader A", 1);
        await fx.Tools.add_unit(fx.ArmyName, "Leader B", 1);
        await fx.Tools.add_unit(fx.ArmyName, "Squad", 5);
        fx.Tools.attach_leader(fx.ArmyName, 1, 3); // Leader A -> Squad

        // Leader B soll nicht an "Leader A" andocken können, da Leader A bereits selbst führt.
        var result = fx.Tools.attach_leader(fx.ArmyName, 2, 1);

        Assert.Contains("bereits", result);
    }

    [Fact]
    public async Task DetachLeader_RemovesAttachment()
    {
        var fx = MakeFixture();
        AddDatasheet(fx.Repo, fx.FactionId, "Hearthkyn Chief", "ds1");
        AddDatasheet(fx.Repo, fx.FactionId, "Einhyr Hearthguard", "ds2");
        var slug = MfmScraper.GetSlugForFaction("Leagues of Votann")!;
        fx.Scraper.SetUnitPoints(slug, "hearthkyn chief", [new PointsCostEntry { Description = "1 model", Cost = 70 }]);
        fx.Scraper.SetUnitPoints(slug, "einhyr hearthguard", [new PointsCostEntry { Description = "5 models", Cost = 150 }]);

        fx.Tools.create_army(fx.ArmyName, "Leagues of Votann", 2000);
        await fx.Tools.add_unit(fx.ArmyName, "Hearthkyn Chief", 1);
        await fx.Tools.add_unit(fx.ArmyName, "Einhyr Hearthguard", 5);
        fx.Tools.attach_leader(fx.ArmyName, 1, 2);

        var result = fx.Tools.detach_leader(fx.ArmyName, 1);

        Assert.Contains("führt keine Einheit mehr an", result);
        var shown = fx.Tools.show_army(fx.ArmyName);
        // "Führt an"-Spalte für Zeile 1 soll wieder "–" sein statt "#2".
        Assert.DoesNotContain("#2", shown);
    }

    [Fact]
    public void DetachLeader_UnitNotLeading_ReturnsError()
    {
        var fx = MakeFixture();
        fx.Tools.create_army(fx.ArmyName, "Leagues of Votann", 2000);

        var result = fx.Tools.detach_leader(fx.ArmyName, 1);

        Assert.Contains("Ungültiger Index", result); // leere Army, Index 1 existiert nicht
    }

    [Fact]
    public async Task RemoveUnit_ClearsDanglingAttachmentOnRemainingUnits()
    {
        var fx = MakeFixture();
        AddDatasheet(fx.Repo, fx.FactionId, "Hearthkyn Chief", "ds1");
        AddDatasheet(fx.Repo, fx.FactionId, "Einhyr Hearthguard", "ds2");
        var slug = MfmScraper.GetSlugForFaction("Leagues of Votann")!;
        fx.Scraper.SetUnitPoints(slug, "hearthkyn chief", [new PointsCostEntry { Description = "1 model", Cost = 70 }]);
        fx.Scraper.SetUnitPoints(slug, "einhyr hearthguard", [new PointsCostEntry { Description = "5 models", Cost = 150 }]);

        fx.Tools.create_army(fx.ArmyName, "Leagues of Votann", 2000);
        await fx.Tools.add_unit(fx.ArmyName, "Hearthkyn Chief", 1);   // #1
        await fx.Tools.add_unit(fx.ArmyName, "Einhyr Hearthguard", 5); // #2
        fx.Tools.attach_leader(fx.ArmyName, 1, 2);

        // Die geführte Einheit (#2) entfernen -> die Zuordnung des Leaders darf nicht auf eine
        // nicht mehr existierende Id zeigen (sonst potenziell falsche Zuordnung nach Re-Indizierung).
        fx.Tools.remove_unit(fx.ArmyName, 2);

        var shown = fx.Tools.show_army(fx.ArmyName);
        Assert.DoesNotContain("#2", shown);
        Assert.Contains("| 1 | Hearthkyn Chief | 1 | 70 | – | – |", shown);
    }

    // ── Persistenz ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Army_SurvivesAcrossArmyBuilderToolsInstances_SimulatingServerRestart()
    {
        var fx1 = MakeFixture();
        AddDatasheet(fx1.Repo, fx1.FactionId, "Einhyr Hearthguard", "ds1");
        var slug = MfmScraper.GetSlugForFaction("Leagues of Votann")!;
        fx1.Scraper.SetUnitPoints(slug, "einhyr hearthguard", [new PointsCostEntry { Description = "5 models", Cost = 150 }]);

        fx1.Tools.create_army(fx1.ArmyName, "Leagues of Votann", 2000);
        await fx1.Tools.add_unit(fx1.ArmyName, "Einhyr Hearthguard", 5);

        // Simuliert einen Serverneustart: neues ArmyRepository (gleiches Verzeichnis), neue ArmyBuilderTools-Instanz.
        var armyRepoAfterRestart = new ArmyRepository(_tempDir);
        var toolsAfterRestart = new ArmyBuilderTools(fx1.Repo, fx1.Scraper, armyRepoAfterRestart);

        var shown = toolsAfterRestart.show_army(fx1.ArmyName);

        Assert.Contains("Einhyr Hearthguard", shown);
        Assert.Contains("150 / 2000", shown);
    }

    // ── Reine Hilfsmethoden ──────────────────────────────────────────────────

    [Theory]
    [InlineData("5 models", 5)]
    [InlineData("10 models", 10)]
    [InlineData("1 model", 1)]
    [InlineData("no number here", 1)]
    public void ParseModelCount_ExtractsNumberOrDefaultsToOne(string description, int expected)
    {
        Assert.Equal(expected, ArmyBuilderTools.ParseModelCount(description));
    }

    [Fact]
    public void GenerateProgressBar_MarksOverLimitWithX()
    {
        var underLimit = ArmyBuilderTools.GenerateProgressBar(1000, 2000);
        var overLimit = ArmyBuilderTools.GenerateProgressBar(2500, 2000);

        Assert.Contains('#', underLimit);
        Assert.DoesNotContain('X', underLimit);
        Assert.Contains('X', overLimit);
    }
}
