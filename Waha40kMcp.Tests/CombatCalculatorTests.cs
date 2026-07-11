using Waha40kMcp.Models;
using Waha40kMcp.Tools;
using Xunit;

namespace Waha40kMcp.Tests;

public class CombatCalculatorTests
{
    // ── ParseDice ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("", 0)]
    [InlineData("N/A", 0)]
    [InlineData("-", 0)]
    [InlineData("3", 3)]
    [InlineData("D6", 3.5)]
    [InlineData("2D6", 7)]
    [InlineData("D6+1", 4.5)]
    [InlineData("2D6+3", 10)]
    [InlineData("d3", 2)]
    public void ParseDice_ReturnsExpectedAverage(string expr, double expected)
    {
        Assert.Equal(expected, CombatCalculator.ParseDice(expr), 3);
    }

    // ── ParseSave ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("", 0)]
    [InlineData("-", 0)]
    [InlineData("3+", 3)]
    [InlineData("2+", 2)]
    public void ParseSave_ReturnsExpectedTarget(string save, int expected)
    {
        Assert.Equal(expected, CombatCalculator.ParseSave(save));
    }

    // ── WoundProb (10th-Ed Wundtabelle) ─────────────────────────────────────

    [Theory]
    [InlineData(8, 4, 5.0 / 6)]  // S >= 2x T
    [InlineData(6, 4, 4.0 / 6)]  // S > T
    [InlineData(4, 4, 3.0 / 6)]  // S == T
    [InlineData(3, 4, 2.0 / 6)]  // 2x S > T
    [InlineData(1, 4, 1.0 / 6)]  // sonst
    public void WoundProb_MatchesCoreRules(int s, int t, double expected)
    {
        Assert.Equal(expected, CombatCalculator.WoundProb(s, t), 5);
    }

    // ── SaveProb ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(3, 0, 0, 4.0 / 6)]   // 3+ Save, kein AP, kein Invuln
    [InlineData(3, 2, 0, 2.0 / 6)]   // 3+ Save, AP-2 -> effektiv 5+
    [InlineData(6, 3, 0, 0)]         // Save durch AP unmöglich, kein Invuln
    [InlineData(6, 3, 4, 3.0 / 6)]   // Save unmöglich, aber 4+ Invuln greift
    public void SaveProb_MatchesCoreRules(int save, int ap, int invSave, double expected)
    {
        Assert.Equal(expected, CombatCalculator.SaveProb(save, ap, invSave), 5);
    }

    // ── ParseSustainedHitsFromKeywords (Regressionstest für den Fix) ────────

    [Theory]
    [InlineData("", 0)]
    [InlineData("Assault, Pistol", 0)]
    [InlineData("Sustained Hits 1", 1)]
    [InlineData("SUSTAINED HITS 2", 2)]
    [InlineData("Lethal Hits, Sustained Hits 3, Twin-linked", 3)]
    public void ParseSustainedHitsFromKeywords_ExtractsCount(string keywords, int expected)
    {
        Assert.Equal(expected, CombatCalculator.ParseSustainedHitsFromKeywords(keywords));
    }

    // ── ParseAbilities ───────────────────────────────────────────────────────

    private static Datasheet WithAbility(string description)
    {
        var ds = new Datasheet { Id = "x", Name = "Test Unit" };
        ds.Abilities.Add(new DatasheetAbility { Name = "", Description = description, Type = "Datasheet" });
        return ds;
    }

    [Fact]
    public void ParseAbilities_DetectsFeelNoPain()
    {
        var ds = WithAbility("This model has the Feel No Pain 5+ ability.");
        var abilities = CombatCalculator.ParseAbilities(ds);
        Assert.Equal(5, abilities.FnpValue);
    }

    [Fact]
    public void ParseAbilities_DetectsLethalHits()
    {
        var ds = WithAbility("Each Critical Hit for this weapon automatically wounds the target (Lethal Hits).");
        var abilities = CombatCalculator.ParseAbilities(ds);
        Assert.True(abilities.LethalHits);
    }

    [Fact]
    public void ParseAbilities_DetectsSustainedHitsCount()
    {
        var ds = WithAbility("This weapon has the Sustained Hits 2 ability.");
        var abilities = CombatCalculator.ParseAbilities(ds);
        Assert.Equal(2, abilities.SustainedHitsCount);
    }

    [Fact]
    public void ParseAbilities_DetectsDevastatingWounds()
    {
        var ds = WithAbility("This weapon has the Devastating Wounds ability.");
        var abilities = CombatCalculator.ParseAbilities(ds);
        Assert.True(abilities.DevastatingWounds);
    }

    [Fact]
    public void ParseAbilities_DetectsRerollHitsOf1()
    {
        var ds = WithAbility("You can re-roll a Hit roll of 1.");
        var abilities = CombatCalculator.ParseAbilities(ds);
        Assert.True(abilities.RerollHitsOf1);
        Assert.False(abilities.RerollAllHits);
    }

    [Fact]
    public void ParseAbilities_DetectsRerollAllHits()
    {
        var ds = WithAbility("You can re-roll all Hit rolls.");
        var abilities = CombatCalculator.ParseAbilities(ds);
        Assert.True(abilities.RerollAllHits);
    }

    [Fact]
    public void ParseAbilities_DetectsRerollAllWounds()
    {
        var ds = WithAbility("You can re-roll all Wound rolls.");
        var abilities = CombatCalculator.ParseAbilities(ds);
        Assert.True(abilities.RerollAllWounds);
    }

    [Fact]
    public void ParseAbilities_DetectsStealth()
    {
        var ds = WithAbility("This unit benefits from the Stealth ability.");
        var abilities = CombatCalculator.ParseAbilities(ds);
        Assert.True(abilities.MinusOneToHit);
    }

    [Fact]
    public void ParseAbilities_DetectsInvulnerableSave()
    {
        var ds = WithAbility("This model has a 4+ invulnerable save.");
        var abilities = CombatCalculator.ParseAbilities(ds);
        Assert.Equal(4, abilities.AdditionalInvSave);
    }

    [Fact]
    public void ParseAbilities_DetectsDamageReduction()
    {
        var ds = WithAbility("This model reduces damage by 1.");
        var abilities = CombatCalculator.ParseAbilities(ds);
        Assert.Equal(1, abilities.DamageReduction);
    }

    [Fact]
    public void ParseAbilities_OwnUnit_ExcludesAbilitiesMeantForOtherCharacters()
    {
        var ds = WithAbility("That Character model has the Feel No Pain 5+ ability while leading this unit.");
        var abilities = CombatCalculator.ParseAbilities(ds, isLeader: false);
        Assert.Equal(0, abilities.FnpValue);
    }

    [Fact]
    public void ParseAbilities_Leader_IncludesAbilitiesGrantedToLedUnit()
    {
        var ds = WithAbility("While this model is leading a unit, models in that unit have the Feel No Pain 5+ ability.");
        var abilities = CombatCalculator.ParseAbilities(ds, isLeader: true);
        Assert.Equal(5, abilities.FnpValue);
    }

    [Fact]
    public void ParseAbilities_Leader_IgnoresAbilitiesNotGrantedToUnit()
    {
        // Eine Fähigkeit, die nur für den Leader selbst gilt (kein "grants to unit"-Muster),
        // darf beim Parsen als Leader NICHT auf die geführte Einheit übertragen werden.
        var ds = WithAbility("This model has the Feel No Pain 5+ ability.");
        var abilities = CombatCalculator.ParseAbilities(ds, isLeader: true);
        Assert.Equal(0, abilities.FnpValue);
    }

    // ── CombatAbilities.MergeWith ───────────────────────────────────────────

    [Fact]
    public void MergeWith_CombinesFlagsAndTakesBetterFnp()
    {
        var unit = new CombatAbilities { FnpValue = 6, LethalHits = true };
        var leader = new CombatAbilities { FnpValue = 5, RerollAllHits = true };

        unit.MergeWith(leader);

        Assert.True(unit.LethalHits);
        Assert.True(unit.RerollAllHits);
        Assert.Equal(5, unit.FnpValue); // besserer (niedrigerer) FNP-Wert gewinnt
    }

    // ── CalculateWeapon (Erwartungswert-Rechnung, per Hand nachgerechnet) ───

    [Fact]
    public void CalculateWeapon_BasicBoltRifleVsMarine_MatchesHandCalculation()
    {
        var weapon = new DatasheetWeapon
        {
            Name = "Bolt Rifle", Type = "ranged",
            A = "2", BsWs = "3+", S = "4", Ap = "0", D = "1", Keywords = "",
        };
        var attacker = new CombatAbilities();
        var defender = new CombatAbilities();

        // 5 Angreifer-Modelle, Verteidiger: T4, Sv3+, kein Invuln, W1, kein FNP, 5 Modelle, kein Cover
        var result = CombatCalculator.CalculateWeapon(
            weapon, models: 5,
            defT: 4, defSv: 3, defInv: 0, defW: 1, defFnp: 0, defenderModels: 5, defenderCover: false,
            attacker, defender);

        Assert.Equal(10, result.TotalAttacks, 3);
        Assert.Equal(6.667, result.AvgHits, 2);
        Assert.Equal(3.333, result.AvgWounds, 2);
        Assert.Equal(1.111, result.AvgFailedSaves, 2);
        Assert.Equal(1.111, result.AvgDamage, 2);
        Assert.Equal(1.111, result.AvgModelsKilled, 2);
    }

    [Fact]
    public void CalculateWeapon_SustainedHitsFromWeaponKeyword_IncreasesAvgHits()
    {
        // Regressionstest für den Sustained-Hits-Fix: "SUSTAINED HITS 2" steht nur im
        // Waffen-Keyword (wie bei Wahapedia üblich), keine Ability-Beschreibung dazu.
        var weaponWithoutSustained = new DatasheetWeapon
        {
            Name = "Plain", Type = "ranged", A = "10", BsWs = "3+", S = "4", Ap = "0", D = "1", Keywords = "",
        };
        var weaponWithSustained2 = new DatasheetWeapon
        {
            Name = "Sustained", Type = "ranged", A = "10", BsWs = "3+", S = "4", Ap = "0", D = "1",
            Keywords = "Sustained Hits 2",
        };
        var attacker = new CombatAbilities();
        var defender = new CombatAbilities();

        var baseline = CombatCalculator.CalculateWeapon(weaponWithoutSustained, 1, 4, 3, 0, 1, 0, 5, false, attacker, defender);
        var withSustained = CombatCalculator.CalculateWeapon(weaponWithSustained2, 1, 4, 3, 0, 1, 0, 5, false, attacker, defender);

        // Ohne Sustained Hits: AvgHits == totalAttacks * hitChance.
        // Mit Sustained Hits 2: jeder Crit (1/6 der Attacken) zählt als 2 zusätzliche Treffer statt 1 Wundwurf.
        Assert.True(withSustained.AvgHits > baseline.AvgHits);

        double critHits = 10 * (1.0 / 6.0);
        double expectedAvgHits = baseline.AvgHits + critHits * 2; // +2 statt +1 pro Crit
        Assert.Equal(expectedAvgHits, withSustained.AvgHits, 3);
    }

    // ── BLAST ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(5, 0)]   // unter 6 Modellen: kein Bonus
    [InlineData(6, 1)]   // 6-10 Modelle: +1 Attacke
    [InlineData(10, 1)]
    [InlineData(11, 3)]  // 11+ Modelle: +3 Attacken
    [InlineData(20, 3)]
    public void CalculateWeapon_Blast_AddsAttacksBasedOnDefenderModelCount(int defenderModels, int expectedBonus)
    {
        var weapon = new DatasheetWeapon
        {
            Name = "Frag Missile", Type = "ranged",
            A = "3", BsWs = "3+", S = "4", Ap = "0", D = "1", Keywords = "Blast",
        };
        var noAbilities = new CombatAbilities();

        var result = CombatCalculator.CalculateWeapon(
            weapon, models: 1,
            defT: 4, defSv: 3, defInv: 0, defW: 1, defFnp: 0, defenderModels: defenderModels, defenderCover: false,
            noAbilities, noAbilities);

        Assert.Equal(3 + expectedBonus, result.TotalAttacks, 3);
    }

    [Fact]
    public void CalculateWeapon_NonBlastWeapon_IgnoresDefenderModelCount()
    {
        var weapon = new DatasheetWeapon
        {
            Name = "Bolt Rifle", Type = "ranged",
            A = "2", BsWs = "3+", S = "4", Ap = "0", D = "1", Keywords = "",
        };
        var noAbilities = new CombatAbilities();

        var vs5 = CombatCalculator.CalculateWeapon(weapon, 1, 4, 3, 0, 1, 0, 5, false, noAbilities, noAbilities);
        var vs20 = CombatCalculator.CalculateWeapon(weapon, 1, 4, 3, 0, 1, 0, 20, false, noAbilities, noAbilities);

        Assert.Equal(vs5.TotalAttacks, vs20.TotalAttacks, 3);
    }

    // ── Benefit of Cover ─────────────────────────────────────────────────────

    [Fact]
    public void CalculateWeapon_Cover_NeutralizesApMinus1()
    {
        var weapon = new DatasheetWeapon
        {
            Name = "Bolt Rifle", Type = "ranged",
            A = "10", BsWs = "3+", S = "4", Ap = "-1", D = "1", Keywords = "",
        };
        var noAbilities = new CombatAbilities();

        var withoutCover = CombatCalculator.CalculateWeapon(weapon, 1, 4, 3, 0, 1, 0, 5, defenderCover: false, noAbilities, noAbilities);
        var withCover = CombatCalculator.CalculateWeapon(weapon, 1, 4, 3, 0, 1, 0, 5, defenderCover: true, noAbilities, noAbilities);

        // Mit Cover wird AP-1 zu AP0 -> besserer Save -> weniger fehlgeschlagene Saves -> weniger Schaden.
        Assert.True(withCover.AvgFailedSaves < withoutCover.AvgFailedSaves);
    }

    [Fact]
    public void CalculateWeapon_Cover_DoesNotAffectApMinus2OrWorse()
    {
        var weapon = new DatasheetWeapon
        {
            Name = "Plasma Gun", Type = "ranged",
            A = "10", BsWs = "3+", S = "7", Ap = "-2", D = "1", Keywords = "",
        };
        var noAbilities = new CombatAbilities();

        var withoutCover = CombatCalculator.CalculateWeapon(weapon, 1, 4, 3, 0, 1, 0, 5, defenderCover: false, noAbilities, noAbilities);
        var withCover = CombatCalculator.CalculateWeapon(weapon, 1, 4, 3, 0, 1, 0, 5, defenderCover: true, noAbilities, noAbilities);

        Assert.Equal(withoutCover.AvgFailedSaves, withCover.AvgFailedSaves, 5);
    }

    // ── Kritische Treffer/Wunden ab abweichender Schwelle ───────────────────

    [Fact]
    public void ParseAbilities_DetectsLoweredCriticalHitThreshold()
    {
        var ds = WithAbility("Critical Hits for this weapon are scored on a 5+ instead of a 6.");
        var abilities = CombatCalculator.ParseAbilities(ds);
        Assert.Equal(5, abilities.CritHitThreshold);
    }

    [Fact]
    public void ParseAbilities_DetectsLoweredCriticalWoundThreshold()
    {
        var ds = WithAbility("Critical Wounds for this weapon are scored on a 5+ instead of a 6.");
        var abilities = CombatCalculator.ParseAbilities(ds);
        Assert.Equal(5, abilities.CritWoundThreshold);
    }

    [Fact]
    public void CalculateWeapon_LethalHitsWithLoweredCritThreshold_WoundsMoreThanDefaultThreshold()
    {
        var weapon = new DatasheetWeapon
        {
            Name = "Test Weapon", Type = "ranged",
            A = "20", BsWs = "3+", S = "4", Ap = "0", D = "1", Keywords = "",
        };
        var defender = new CombatAbilities();
        var defaultThreshold = new CombatAbilities { LethalHits = true };
        var loweredThreshold = new CombatAbilities { LethalHits = true, CritHitThreshold = 5 };

        var resultDefault = CombatCalculator.CalculateWeapon(weapon, 1, 4, 3, 0, 1, 0, 5, false, defaultThreshold, defender);
        var resultLowered = CombatCalculator.CalculateWeapon(weapon, 1, 4, 3, 0, 1, 0, 5, false, loweredThreshold, defender);

        // Mehr Crits -> mehr automatische Wunden (Lethal Hits) statt unsicherer Wundwürfe -> mehr Gesamtwunden.
        Assert.True(resultLowered.AvgWounds > resultDefault.AvgWounds);
    }

    [Fact]
    public void CalculateWeapon_DevastatingWoundsWithLoweredCritThreshold_DealsMoreDamageThanDefaultThreshold()
    {
        var weapon = new DatasheetWeapon
        {
            Name = "Test Weapon", Type = "ranged",
            A = "20", BsWs = "3+", S = "4", Ap = "0", D = "1", Keywords = "",
        };
        var defender = new CombatAbilities();
        var defaultThreshold = new CombatAbilities { DevastatingWounds = true };
        var loweredThreshold = new CombatAbilities { DevastatingWounds = true, CritWoundThreshold = 5 };

        var resultDefault = CombatCalculator.CalculateWeapon(weapon, 1, 4, 3, 0, 1, 0, 5, false, defaultThreshold, defender);
        var resultLowered = CombatCalculator.CalculateWeapon(weapon, 1, 4, 3, 0, 1, 0, 5, false, loweredThreshold, defender);

        // Mehr kritische Wunden -> mehr Mortal Wounds die Saves umgehen -> mehr Schaden.
        Assert.True(resultLowered.AvgDamage > resultDefault.AvgDamage);
    }

    [Fact]
    public void MergeWith_TakesLowerCritThresholds()
    {
        var unit = new CombatAbilities { CritHitThreshold = 6, CritWoundThreshold = 6 };
        var leader = new CombatAbilities { CritHitThreshold = 5, CritWoundThreshold = 6 };

        unit.MergeWith(leader);

        Assert.Equal(5, unit.CritHitThreshold);
        Assert.Equal(6, unit.CritWoundThreshold);
    }
}
