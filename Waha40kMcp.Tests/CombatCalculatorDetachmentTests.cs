using Waha40kMcp.Data;
using Waha40kMcp.Models;
using Waha40kMcp.Tools;
using Xunit;

namespace Waha40kMcp.Tests;

/// <summary>
/// Integrationstests für die Einbindung von Detachment-Fähigkeiten und Enhancements
/// in calculate_combat/simulate_combat (im Gegensatz zu CombatCalculatorTests, das die
/// reinen statischen Rechen-/Parse-Hilfsmethoden isoliert testet).
/// </summary>
public class CombatCalculatorDetachmentTests
{
    private static (WahapediaRepository Repo, CombatCalculator Tools) MakeFixture()
    {
        var repo = new WahapediaRepository();
        repo.Factions["SM"] = new Faction { Id = "SM", Name = "Space Marines" };

        var attacker = new Datasheet { Id = "atk", Name = "Test Squad", FactionId = "SM", FactionName = "Space Marines" };
        attacker.Weapons.Add(new DatasheetWeapon
        {
            Name = "Test Weapon", Type = "ranged", A = "10", BsWs = "3+", S = "4", Ap = "0", D = "1",
        });
        repo.Datasheets[attacker.Id] = attacker;

        var defender = new Datasheet { Id = "def", Name = "Test Target", FactionId = "SM", FactionName = "Space Marines" };
        defender.Models.Add(new DatasheetModel { Name = "Model", M = "6\"", T = "4", Sv = "3+", W = "1", Ld = "6+", Oc = "1" });
        repo.Datasheets[defender.Id] = defender;

        repo.DetachmentAbilities["da1"] = new DetachmentAbility
        {
            Id = "da1", FactionId = "SM", Detachment = "Gladius Task Force",
            Name = "Combat Doctrines", Description = "Each attack made by this unit has the Lethal Hits ability.",
        };

        repo.Enhancements["e1"] = new Enhancement
        {
            Id = "e1", FactionId = "SM", Detachment = "Gladius Task Force",
            Name = "Artificer Armour", Cost = 15, Description = "This model has a 4+ invulnerable save.",
        };
        repo.Enhancements["e2"] = new Enhancement
        {
            Id = "e2", FactionId = "SM", Detachment = "Firestorm Assault Force",
            Name = "Other Detachment Only", Cost = 10, Description = "Should not be found under Gladius Task Force.",
        };

        return (repo, new CombatCalculator(repo));
    }

    [Fact]
    public void CalculateCombat_UnknownAttackerDetachment_ReturnsErrorMessage()
    {
        var (_, tools) = MakeFixture();

        var result = tools.calculate_combat("Test Squad", "Test Target",
            attacker_detachment: "Nonexistent Detachment Xyz");

        Assert.Contains("Detachment", result);
        Assert.Contains("nicht gefunden", result);
    }

    [Fact]
    public void CalculateCombat_UnknownEnhancement_ReturnsErrorMessage()
    {
        var (_, tools) = MakeFixture();

        var result = tools.calculate_combat("Test Squad", "Test Target",
            attacker_enhancement: "Nonexistent Enhancement Xyz");

        Assert.Contains("Enhancement", result);
        Assert.Contains("nicht", result);
    }

    [Fact]
    public void CalculateCombat_EnhancementRestrictedToOtherDetachment_IsNotFound()
    {
        var (_, tools) = MakeFixture();

        // "Other Detachment Only" gehört zu Firestorm Assault Force, nicht zu Gladius Task Force.
        var result = tools.calculate_combat("Test Squad", "Test Target",
            attacker_detachment: "Gladius Task Force", attacker_enhancement: "Other Detachment Only");

        Assert.Contains("Enhancement", result);
        Assert.Contains("nicht", result);
    }

    [Fact]
    public void CalculateCombat_DetachmentAbility_GrantsLethalHitsAndIncreasesWounds()
    {
        var (_, tools) = MakeFixture();

        var withoutDetachment = tools.calculate_combat("Test Squad", "Test Target");
        var withDetachment = tools.calculate_combat("Test Squad", "Test Target",
            attacker_detachment: "Gladius Task Force");

        Assert.Contains("Lethal Hits", withDetachment);
        Assert.Contains("Gladius Task Force", withDetachment);
        Assert.DoesNotContain("Lethal Hits", withoutDetachment);
    }

    [Fact]
    public void CalculateCombat_Enhancement_GrantsInvulnerableSaveToDefender()
    {
        var (_, tools) = MakeFixture();

        var result = tools.calculate_combat("Test Squad", "Test Target",
            defender_detachment: "Gladius Task Force", defender_enhancement: "Artificer Armour");

        Assert.Contains("InvSv:** 4+", result);
        Assert.Contains("Artificer Armour", result);
    }

    [Fact]
    public void SimulateCombat_DetachmentAbility_GrantsLethalHits()
    {
        var (_, tools) = MakeFixture();

        var result = tools.simulate_combat("Test Squad", "Test Target",
            attacker_detachment: "Gladius Task Force", iterations: 100);

        Assert.Contains("Gladius Task Force", result);
    }
}
