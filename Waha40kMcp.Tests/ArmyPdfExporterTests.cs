using Waha40kMcp.Data;
using Waha40kMcp.Models;
using Xunit;

namespace Waha40kMcp.Tests;

/// <summary>
/// Testet nur ArmyPdfExporter.BuildHtml (reine String-Erzeugung, kein Browser nötig).
/// Das eigentliche Playwright-Rendering (RenderToPdfAsync) wird hier bewusst nicht getestet —
/// analog zu MfmScraper.ScrapeFactionsAsync bräuchte das einen echten Chromium-Prozess, den wir
/// in CI nicht vorhalten wollen (siehe MfmScraperTests: nur die reine Parse-Logik wird getestet).
/// </summary>
public class ArmyPdfExporterTests
{
    private static (WahapediaRepository Repo, ArmyList Army) MakeFixture()
    {
        var repo = new WahapediaRepository();
        repo.Factions["SM"] = new Faction { Id = "SM", Name = "Space Marines" };

        var ds = new Datasheet
        {
            Id = "ds1", Name = "Intercessor Squad", FactionId = "SM", FactionName = "Space Marines",
            Keywords = "Infantry, Battleline", Datasheettype = "Battleline",
        };
        ds.Models.Add(new DatasheetModel { Name = "Intercessor", M = "6\"", T = "4", Sv = "3+", W = "2", Ld = "6+", Oc = "2", InvSv = "" });
        ds.Weapons.Add(new DatasheetWeapon { Name = "Bolt Rifle", Type = "ranged", Range = "24", A = "2", BsWs = "3+", S = "4", Ap = "-1", D = "1", Keywords = "Assault, Heavy" });
        ds.Weapons.Add(new DatasheetWeapon { Name = "Close Combat Weapon", Type = "melee", A = "3", BsWs = "3+", S = "4", Ap = "0", D = "1" });
        ds.Abilities.Add(new DatasheetAbility { Name = "Oath of Moment", Description = "Re-roll hit & wound rolls against the target.", Type = "Core" });
        repo.Datasheets[ds.Id] = ds;

        var army = new ArmyList
        {
            Name = "My & <Test> Army", FactionId = "SM", FactionName = "Space Marines",
            Detachment = "Gladius Task Force", PointsLimit = 2000,
        };
        army.Units.Add(new ArmyUnit { DatasheetId = "ds1", Name = "Intercessor Squad", Points = 80, ModelCount = 5 });
        army.Units.Add(new ArmyUnit { DatasheetId = "ds1", Name = "Intercessor Squad", Points = 80, ModelCount = 5 });
        army.Units.Add(new ArmyUnit
        {
            DatasheetId = "ds1", Name = "Intercessor Squad", Points = 95, ModelCount = 5,
            EnhancementName = "Adept of the Codicium", EnhancementCost = 15,
        });

        return (repo, army);
    }

    [Fact]
    public void BuildHtml_IncludesArmyMetadata()
    {
        var (repo, army) = MakeFixture();

        var html = ArmyPdfExporter.BuildHtml(army, repo);

        Assert.Contains("Space Marines", html);
        Assert.Contains("Gladius Task Force", html);
        Assert.Contains("2000", html);
    }

    [Fact]
    public void BuildHtml_EscapesHtmlSpecialCharactersInArmyName()
    {
        var (repo, army) = MakeFixture();

        var html = ArmyPdfExporter.BuildHtml(army, repo);

        Assert.DoesNotContain("<Test>", html);
        Assert.Contains("&amp;", html);
        Assert.Contains("&lt;Test&gt;", html);
    }

    [Fact]
    public void BuildHtml_RosterListsEveryUnitInstanceWithEnhancement()
    {
        var (repo, army) = MakeFixture();

        var html = ArmyPdfExporter.BuildHtml(army, repo);

        // 3 separate Kaufeinträge -> 3 Roster-Zeilen mit "(5) Intercessor Squad"
        var occurrences = html.Split("(5) Intercessor Squad").Length - 1;
        Assert.Equal(3, occurrences);
        Assert.Contains("Adept of the Codicium (+15)", html);
    }

    [Fact]
    public void BuildHtml_DeduplicatesDetailPagesByDatasheetAndNotesCopyCount()
    {
        var (repo, army) = MakeFixture();

        var html = ArmyPdfExporter.BuildHtml(army, repo);

        // Nur EIN Datasheet-Detailblock trotz 3 Käufen desselben Datasheets.
        var detailBlocks = html.Split("class=\"page unit-block\"").Length - 1;
        Assert.Equal(1, detailBlocks);
        Assert.Contains("3x in dieser Liste enthalten", html);
    }

    [Fact]
    public void BuildHtml_IncludesStatsAbilitiesAndWeapons()
    {
        var (repo, army) = MakeFixture();

        var html = ArmyPdfExporter.BuildHtml(army, repo);

        Assert.Contains("INTERCESSOR SQUAD", html); // Unit-Header (Großbuchstaben)
        Assert.Contains("Intercessor", html);        // Modellname in der Stats-Tabelle
        Assert.Contains("Oath of Moment", html);
        Assert.Contains("Re-roll hit &amp; wound rolls against the target.", html);
        Assert.Contains("Bolt Rifle", html);
        Assert.Contains("Close Combat Weapon", html);
    }

    [Fact]
    public void BuildHtml_SkipsUnitsWhoseDatasheetIsNoLongerInTheRepository()
    {
        var repo = new WahapediaRepository();
        repo.Factions["SM"] = new Faction { Id = "SM", Name = "Space Marines" };
        var army = new ArmyList { Name = "Orphan Army", FactionId = "SM", FactionName = "Space Marines" };
        army.Units.Add(new ArmyUnit { DatasheetId = "missing-ds", Name = "Ghost Unit", Points = 100, ModelCount = 1 });

        var html = ArmyPdfExporter.BuildHtml(army, repo);

        // Roster-Zeile bleibt (aus den ArmyUnit-Daten), aber keine Detailseite (kein Datasheet gefunden).
        Assert.Contains("Ghost Unit", html);
        Assert.DoesNotContain("class=\"page unit-block\"", html);
    }
}
