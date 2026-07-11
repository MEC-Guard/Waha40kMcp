using System.ComponentModel;
using System.Text;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;
using Waha40kMcp.Data;
using Waha40kMcp.Models;

namespace Waha40kMcp.Tools;

/// <summary>
/// MathHammer Combat Calculator für Warhammer 40K 10th Edition.
/// Berücksichtigt Waffen-Keywords UND Datasheet-Fähigkeiten automatisch.
/// </summary>
[McpServerToolType]
public class CombatCalculator(WahapediaRepository repo)
{
    // ── Ability-Parser ────────────────────────────────────────────────────────

    /// <summary>
    /// Parst alle relevanten Fähigkeiten aus dem Freitext der Datasheet-Abilities.
    /// Filtert Fähigkeiten aus die sich auf andere Einheiten/Characters beziehen.
    /// </summary>
    internal static CombatAbilities ParseAbilities(Datasheet ds, bool isLeader = false)
    {
        var a = new CombatAbilities();

        // Phrasen die anzeigen dass eine Fähigkeit für einen anderen Character gilt
        // (nur relevant wenn wir die eigene Einheit parsen, nicht den Leader)
        var excludePatterns = new[]
        {
            @"that\s+\w*\s*character\s+model\s+has",         // "that CHARACTER model has the Feel No Pain"
            @"while\s+a\s+character\s+model\s+is\s+leading", // "while a Character model is leading this unit"
            @"while\s+this\s+unit\s+is\s+leading",           // "while this unit is leading [another unit]"
        };

        // Phrasen die anzeigen dass eine Leader-Fähigkeit für die geführte Einheit gilt
        var leaderGrantsPatterns = new[]
        {
            @"models?\s+in\s+(the\s+)?bearer'?s?\s+unit\s+(have|gain|get)",           // "models in the bearer's unit have"
            @"models?\s+in\s+that\s+unit\s+(have|gain|get)",                           // "models in that unit have"
            @"while\s+this\s+model\s+is\s+leading.{0,80}models?\s+in\s+that\s+unit",  // "while this model is leading ... models in that unit"
            @"while\s+the\s+bearer\s+is\s+leading.{0,80}models?\s+in\s+that\s+unit",  // "while the bearer is leading ... models in that unit"
            @"weapons?\s+equipped\s+by\s+models?\s+in\s+that\s+unit",                  // "weapons equipped by models in that unit"
            // Breitere Patterns für Ork-Anführer und andere Formulierungen
            @"while\s+this\s+model\s+is\s+leading\s+a?\s*unit",                        // "while this model is leading a unit"
            @"each\s+time\s+a\s+model\s+in\s+that\s+unit\s+makes?\s+an?\s+attack",    // "each time a model in that unit makes an attack"
            @"unit\s+(that\s+)?this\s+(model|character)\s+is\s+leading",                // "unit that this model is leading"
            @"(the\s+)?unit\s+(they\s+are|it\s+is)\s+leading",                         // "unit they are leading"
            @"this\s+model\s+is\s+attached\s+to",                                       // "this model is attached to"
            @"models?\s+in\s+the\s+same\s+unit\s+(have|gain|get|can)",                 // "models in the same unit have/gain"
        };

        foreach (var ability in ds.Abilities)
        {
            var text = ability.Name + " " + ability.Description;

            if (!isLeader)
            {
                // Eigene Einheit: Fähigkeiten ausschließen die für andere gelten
                bool isForOther = excludePatterns.Any(p =>
                    Regex.IsMatch(text, p, RegexOptions.IgnoreCase));
                if (isForOther) continue;
            }
            else
            {
                // Leader: nur Fähigkeiten einschließen die für die geführte Einheit gelten
                bool grantsToUnit = leaderGrantsPatterns.Any(p =>
                    Regex.IsMatch(text, p, RegexOptions.IgnoreCase));
                if (!grantsToUnit) continue;
            }

            ApplyAbilityText(a, text);
        }

        return a;
    }

    /// <summary>
    /// Erkennt regelrelevante Schlüsselwörter/Formulierungen in einem beliebigen Fähigkeitstext
    /// (Datasheet-Ability, Detachment-Ability oder Enhancement-Beschreibung) und trägt sie in
    /// <paramref name="a"/> ein. Wird von <see cref="ParseAbilities"/> pro Datasheet-Ability
    /// aufgerufen, aber auch direkt für Detachment-/Enhancement-Text genutzt (dort gibt es keine
    /// Leader/Exclude-Filterung, der Effekt gilt ja unmittelbar für die eigene Einheit).
    /// </summary>
    internal static void ApplyAbilityText(CombatAbilities a, string text)
    {
        // Feel No Pain
        var fnpMatch = Regex.Match(text, @"Feel No Pain (\d)\+", RegexOptions.IgnoreCase);
        if (fnpMatch.Success && a.FnpValue == 0)
            a.FnpValue = int.Parse(fnpMatch.Groups[1].Value);

        // Lethal Hits
        if (text.Contains("Lethal Hits", StringComparison.OrdinalIgnoreCase))
            a.LethalHits = true;

        // Sustained Hits N
        var sustainedMatch = Regex.Match(text, @"Sustained Hits (\d)", RegexOptions.IgnoreCase);
        if (sustainedMatch.Success)
            a.SustainedHitsCount = Math.Max(a.SustainedHitsCount,
                int.Parse(sustainedMatch.Groups[1].Value));

        // Devastating Wounds
        if (text.Contains("Devastating Wounds", StringComparison.OrdinalIgnoreCase))
            a.DevastatingWounds = true;

        // Re-roll Hit rolls of 1
        if (Regex.IsMatch(text, @"re-?roll.{0,40}hit roll.{0,15}of 1", RegexOptions.IgnoreCase))
            a.RerollHitsOf1 = true;

        // Re-roll all Hit rolls
        if (Regex.IsMatch(text, @"re-?roll.{0,10}all.{0,10}hit roll", RegexOptions.IgnoreCase))
            a.RerollAllHits = true;

        // Re-roll Wound rolls of 1
        if (Regex.IsMatch(text, @"re-?roll.{0,40}wound roll.{0,15}of 1", RegexOptions.IgnoreCase))
            a.RerollWoundsOf1 = true;

        // Re-roll all Wound rolls
        if (Regex.IsMatch(text, @"re-?roll.{0,10}all.{0,10}wound roll", RegexOptions.IgnoreCase))
            a.RerollAllWounds = true;

        // Stealth / -1 to hit
        if (text.Contains("Stealth", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(text, @"[–\-]1.{0,20}hit roll", RegexOptions.IgnoreCase))
            a.MinusOneToHit = true;

        // Invulnerable Save
        var invMatch = Regex.Match(text, @"(\d)\+\s+invulnerable save", RegexOptions.IgnoreCase);
        if (invMatch.Success)
        {
            int inv = int.Parse(invMatch.Groups[1].Value);
            a.AdditionalInvSave = a.AdditionalInvSave == 0
                ? inv : Math.Min(a.AdditionalInvSave, inv);
        }

        // Damage reduction
        if (Regex.IsMatch(text, @"reduc.{0,30}damage.{0,10}by 1", RegexOptions.IgnoreCase))
            a.DamageReduction = 1;

        // Kritischer Treffer/Wunde schon ab niedrigerer Zahl als der Standard-6
        // (z.B. "Critical Hits on a 5+", "Critical Hits and Critical Wounds happen on a 5+")
        var critHitMatch = Regex.Match(text, @"critical hits?.{0,60}(\d)\+", RegexOptions.IgnoreCase);
        if (critHitMatch.Success)
            a.CritHitThreshold = Math.Min(a.CritHitThreshold, int.Parse(critHitMatch.Groups[1].Value));

        var critWoundMatch = Regex.Match(text, @"critical wounds?.{0,60}(\d)\+", RegexOptions.IgnoreCase);
        if (critWoundMatch.Success)
            a.CritWoundThreshold = Math.Min(a.CritWoundThreshold, int.Parse(critWoundMatch.Groups[1].Value));
    }

    // ── Würfel-Parser ─────────────────────────────────────────────────────────

    internal static double ParseDice(string expr)
    {
        if (string.IsNullOrWhiteSpace(expr)) return 0;
        expr = expr.Trim().ToUpperInvariant();
        if (expr is "N/A" or "-" or "") return 0;
        if (double.TryParse(expr, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var n)) return n;

        var match = Regex.Match(expr, @"^(\d*)[Dd](\d+)(?:\+(\d+))?$");
        if (match.Success)
        {
            var count = match.Groups[1].Value != "" ? int.Parse(match.Groups[1].Value) : 1;
            var sides = int.Parse(match.Groups[2].Value);
            var bonus = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;
            return count * (sides + 1) / 2.0 + bonus;
        }
        return 0;
    }

    internal static int ParseSave(string save)
    {
        if (string.IsNullOrEmpty(save) || save == "-") return 0;
        var match = Regex.Match(save, @"(\d+)\+?");
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    /// <summary>
    /// Liest die Sustained-Hits-Zahl direkt aus dem Waffen-Keyword (z.B. "SUSTAINED HITS 2"),
    /// da Wahapedia diese meist als Waffen-Keyword statt als separate Ability-Beschreibung führt.
    /// </summary>
    internal static int ParseSustainedHitsFromKeywords(string keywords)
    {
        if (string.IsNullOrEmpty(keywords)) return 0;
        var match = Regex.Match(keywords, @"SUSTAINED HITS (\d)", RegexOptions.IgnoreCase);
        return match.Success ? int.Parse(match.Groups[1].Value) : 0;
    }

    // ── Wund/Save Tabellen ────────────────────────────────────────────────────

    internal static double WoundProb(int s, int t)
    {
        if (s >= t * 2) return 5.0 / 6;
        if (s > t)      return 4.0 / 6;
        if (s == t)     return 3.0 / 6;
        if (s * 2 > t)  return 2.0 / 6;
        return 1.0 / 6;
    }

    internal static double SaveProb(int save, int ap, int invSave)
    {
        int effectiveSave = save + ap;
        double normalSaveProb = effectiveSave <= 6
            ? Math.Max(0, (7 - effectiveSave) / 6.0) : 0;
        double invSaveProb = invSave > 0 && invSave <= 6
            ? (7 - invSave) / 6.0 : 0;
        return Math.Min(1.0, Math.Max(normalSaveProb, invSaveProb));
    }

    /// <summary>
    /// Sucht Detachment-Fähigkeit und/oder Enhancement einer Fraktion und trägt ihren Effekt in
    /// <paramref name="abilities"/> ein. Gibt eine Fehlermeldung zurück falls angegeben, aber nicht
    /// gefunden — sonst null.
    /// </summary>
    private string? ApplyDetachmentAndEnhancement(
        CombatAbilities abilities, string factionId,
        string? detachmentName, string? enhancementName, string sideLabel)
    {
        if (detachmentName != null)
        {
            var detachmentAbility = repo.FindDetachment(factionId, detachmentName);
            if (detachmentAbility == null) return $"{sideLabel}-Detachment '{detachmentName}' nicht gefunden.";
            ApplyAbilityText(abilities, detachmentAbility.Name + " " + detachmentAbility.Description);
        }
        if (enhancementName != null)
        {
            var enhancement = repo.FindEnhancement(factionId, enhancementName, detachmentName);
            if (enhancement == null) return $"{sideLabel}-Enhancement '{enhancementName}' nicht gefunden.";
            ApplyAbilityText(abilities, enhancement.Name + " " + enhancement.Description);
        }
        return null;
    }

    // ── Haupt-Tool ────────────────────────────────────────────────────────────

    [McpServerTool, Description(
        "Berechnet den Durchschnittsschaden (MathHammer) wenn eine Einheit auf eine andere schießt oder kämpft. " +
        "Liest Fähigkeiten automatisch aus den Datasheets (Re-rolls, FNP, Lethal Hits, Stealth etc.). " +
        "Unterstützt Leader UND Support Charaktere gleichzeitig (neue Regel). " +
        "Beispiel: calculate_combat('Einhyr Hearthguard', 'Deathshroud Terminators', mode: 'ranged')")]
    public string calculate_combat(
        [Description("Name der angreifenden Einheit")] string attacker_name,
        [Description("Name der verteidigenden Einheit")] string defender_name,
        [Description("'ranged' für Fernkampf, 'melee' für Nahkampf")] string mode = "ranged",
        [Description("Fraktion des Angreifers (optional)")] string? attacker_faction = null,
        [Description("Fraktion des Verteidigers (optional)")] string? defender_faction = null,
        [Description("Anzahl Modelle im Angreifer-Trupp")] int attacker_models = 5,
        [Description("Nur bestimmte Waffen berechnen, kommagetrennt. Leer = alle Waffen")] string? weapons_filter = null,
        [Description("Name des Leaders der den Angreifer führt (optional), z.B. 'Kâhl'")] string? attacker_leader = null,
        [Description("Name des Leaders der den Verteidiger führt (optional), z.B. 'Lord of Contagion'")] string? defender_leader = null,
        [Description("Name des Support-Charakters beim Angreifer (optional), z.B. 'Apothecary'")] string? attacker_support = null,
        [Description("Name des Support-Charakters beim Verteidiger (optional), z.B. 'Painboy'")] string? defender_support = null,
        [Description("Anzahl Modelle im Verteidiger-Trupp (relevant für BLAST-Waffen: +1 Attacke bei 6-10, +3 bei 11+ Modellen)")] int defender_models = 5,
        [Description("Hat der Verteidiger Benefit of Cover? Waffen mit AP -1 werden dadurch zu AP 0 (gilt nicht für AP -2 oder schlechter)")] bool defender_cover = false,
        [Description("Detachment des Angreifers (optional), dessen Detachment-Fähigkeit einfließt, z.B. 'Shield Host'")] string? attacker_detachment = null,
        [Description("Name der Enhancement, die der Angreifer(-Leader) trägt (optional), z.B. 'Aegis Projector'")] string? attacker_enhancement = null,
        [Description("Detachment des Verteidigers (optional), dessen Detachment-Fähigkeit einfließt")] string? defender_detachment = null,
        [Description("Name der Enhancement, die der Verteidiger(-Leader) trägt (optional)")] string? defender_enhancement = null)
    {
        var attackerDs = repo.SearchDatasheets(attacker_name, attacker_faction).FirstOrDefault();
        var defenderDs = repo.SearchDatasheets(defender_name, defender_faction).FirstOrDefault();

        if (attackerDs == null) return $"Angreifer '{attacker_name}' nicht gefunden.";
        if (defenderDs == null) return $"Verteidiger '{defender_name}' nicht gefunden.";

        // Leader & Support suchen wenn angegeben
        Datasheet? attackerLeaderDs = null;
        Datasheet? defenderLeaderDs = null;
        Datasheet? attackerSupportDs = null;
        Datasheet? defenderSupportDs = null;
        if (attacker_leader != null)
        {
            attackerLeaderDs = repo.SearchDatasheets(attacker_leader, attacker_faction).FirstOrDefault();
            if (attackerLeaderDs == null) return $"Angreifer-Leader '{attacker_leader}' nicht gefunden.";
        }
        if (defender_leader != null)
        {
            defenderLeaderDs = repo.SearchDatasheets(defender_leader, defender_faction).FirstOrDefault();
            if (defenderLeaderDs == null) return $"Verteidiger-Leader '{defender_leader}' nicht gefunden.";
        }
        if (attacker_support != null)
        {
            attackerSupportDs = repo.SearchDatasheets(attacker_support, attacker_faction).FirstOrDefault();
            if (attackerSupportDs == null) return $"Angreifer-Support '{attacker_support}' nicht gefunden.";
        }
        if (defender_support != null)
        {
            defenderSupportDs = repo.SearchDatasheets(defender_support, defender_faction).FirstOrDefault();
            if (defenderSupportDs == null) return $"Verteidiger-Support '{defender_support}' nicht gefunden.";
        }

        // Fähigkeiten parsen: eigene Einheit + Leader + Support
        var attackerAbilities = ParseAbilities(attackerDs, isLeader: false);
        if (attackerLeaderDs != null)
            attackerAbilities.MergeWith(ParseAbilities(attackerLeaderDs, isLeader: true));
        if (attackerSupportDs != null)
            attackerAbilities.MergeWith(ParseAbilities(attackerSupportDs, isLeader: true));

        var defenderAbilities = ParseAbilities(defenderDs, isLeader: false);
        if (defenderLeaderDs != null)
            defenderAbilities.MergeWith(ParseAbilities(defenderLeaderDs, isLeader: true));
        if (defenderSupportDs != null)
            defenderAbilities.MergeWith(ParseAbilities(defenderSupportDs, isLeader: true));

        // Detachment-Fähigkeit & Enhancement einbeziehen
        var attachError = ApplyDetachmentAndEnhancement(
            attackerAbilities, attackerDs.FactionId, attacker_detachment, attacker_enhancement, "Angreifer");
        if (attachError != null) return attachError;
        var defendError = ApplyDetachmentAndEnhancement(
            defenderAbilities, defenderDs.FactionId, defender_detachment, defender_enhancement, "Verteidiger");
        if (defendError != null) return defendError;

        // Defender Stats
        var defModel = defenderDs.Models.FirstOrDefault();
        if (defModel == null) return $"Keine Stats für '{defender_name}' gefunden.";

        int defT   = int.TryParse(defModel.T, out var t) ? t : 4;
        int defSv  = ParseSave(defModel.Sv);
        int defW   = int.TryParse(defModel.W, out var w) ? w : 1;

        // InvSave: aus Stats ODER aus Abilities (z.B. Weavefield Crest)
        int defInv = ParseSave(defModel.InvSv);
        if (defenderAbilities.AdditionalInvSave > 0)
            defInv = defInv > 0
                ? Math.Min(defInv, defenderAbilities.AdditionalInvSave)
                : defenderAbilities.AdditionalInvSave;

        int defFnp = defenderAbilities.FnpValue;

        // Waffen filtern
        var weapons = attackerDs.Weapons
            .Where(w => mode == "ranged"
                ? w.Type.Equals("ranged", StringComparison.OrdinalIgnoreCase)
                : w.Type.Equals("melee", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (weapons_filter != null)
        {
            var filter = weapons_filter.Split(',').Select(f => f.Trim()).ToList();
            weapons = weapons.Where(w => filter.Any(f =>
                w.Name.Contains(f, StringComparison.OrdinalIgnoreCase))).ToList();
        }

        if (weapons.Count == 0)
            return $"{attackerDs.Name} hat keine {(mode == "ranged" ? "Fernkampf" : "Nahkampf")}waffen.";

        var sb = new StringBuilder();
        sb.AppendLine($"# ⚔️ MathHammer: {attackerDs.Name} → {defenderDs.Name}");
        sb.AppendLine($"**Modus:** {(mode == "ranged" ? "Fernkampf 🔫" : "Nahkampf ⚔️")}  " +
                      $"**Angreifer-Modelle:** {attacker_models}");
        if (attackerLeaderDs != null) sb.AppendLine($"**Angreifer-Leader:** {attackerLeaderDs.Name}");
        if (attackerSupportDs != null) sb.AppendLine($"**Angreifer-Support:** {attackerSupportDs.Name}");
        if (attacker_detachment != null) sb.AppendLine($"**Angreifer-Detachment:** {attacker_detachment}");
        if (attacker_enhancement != null) sb.AppendLine($"**Angreifer-Enhancement:** {attacker_enhancement}");
        if (defenderLeaderDs != null) sb.AppendLine($"**Verteidiger-Leader:** {defenderLeaderDs.Name}");
        if (defenderSupportDs != null) sb.AppendLine($"**Verteidiger-Support:** {defenderSupportDs.Name}");
        if (defender_detachment != null) sb.AppendLine($"**Verteidiger-Detachment:** {defender_detachment}");
        if (defender_enhancement != null) sb.AppendLine($"**Verteidiger-Enhancement:** {defender_enhancement}");
        sb.AppendLine();

        // Verteidiger Stats
        sb.AppendLine("## Verteidiger Stats");
        sb.AppendLine($"**T:** {defT}  **SV:** {defModel.Sv}  " +
                      $"**InvSv:** {(defInv > 0 ? defInv + "+" : "–")}  " +
                      $"**W:** {defW}  **Modelle:** {defender_models}" +
                      (defFnp > 0 ? $"  **FNP:** {defFnp}+" : "") +
                      (defender_cover ? "  **🛡️ Benefit of Cover**" : ""));
        sb.AppendLine();

        // Erkannte Fähigkeiten ausgeben
        var attackerMods = attackerAbilities.GetSummary();
        var defenderMods = defenderAbilities.GetDefenderSummary();

        if (attackerMods.Count > 0 || defenderMods.Count > 0)
        {
            sb.AppendLine("## 🎯 Erkannte Fähigkeiten");
            if (attackerMods.Count > 0)
            {
                sb.AppendLine($"**{attackerDs.Name} (Angreifer):**");
                foreach (var m in attackerMods) sb.AppendLine($"- {m}");
            }
            if (defenderMods.Count > 0)
            {
                sb.AppendLine($"**{defenderDs.Name} (Verteidiger):**");
                foreach (var m in defenderMods) sb.AppendLine($"- {m}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("## Waffen-Analyse");
        sb.AppendLine();

        double totalDamage = 0;
        double totalModelsKilled = 0;

        foreach (var weapon in weapons)
        {
            var result = CalculateWeapon(
                weapon, attacker_models,
                defT, defSv, defInv, defW, defFnp, defender_models, defender_cover,
                attackerAbilities, defenderAbilities);

            sb.AppendLine($"### {weapon.Name}");
            sb.AppendLine($"*{weapon.Range}\" Range  |  S{weapon.S}  AP{weapon.Ap}  D{weapon.D}*");
            if (!string.IsNullOrEmpty(weapon.Keywords))
                sb.AppendLine($"*Keywords: {weapon.Keywords}*");
            sb.AppendLine();
            sb.AppendLine($"| Schritt | Ergebnis |");
            sb.AppendLine($"|---------|----------|");
            sb.AppendLine($"| Attacken gesamt | {result.TotalAttacks:F1} |");
            sb.AppendLine($"| Ø Treffer ({result.HitChance:P0} pro Att.) | {result.AvgHits:F2} |");
            sb.AppendLine($"| Ø Wunden ({result.WoundChance:P0} pro Treffer) | {result.AvgWounds:F2} |");
            sb.AppendLine($"| Ø fehlgesch. Saves ({result.FailSaveChance:P0} pro Wunde) | {result.AvgFailedSaves:F2} |");
            sb.AppendLine($"| **Ø Schaden** | **{result.AvgDamage:F2}** |");
            if (defFnp > 0)
                sb.AppendLine($"| Ø Schaden nach FNP {defFnp}+ | {result.AvgDamageAfterFnp:F2} |");
            sb.AppendLine($"| **Ø Modelle getötet** | **{result.AvgModelsKilled:F2}** |");
            sb.AppendLine();

            totalDamage += defFnp > 0 ? result.AvgDamageAfterFnp : result.AvgDamage;
            totalModelsKilled += result.AvgModelsKilled;
        }

        sb.AppendLine("---");
        sb.AppendLine("## 📊 Gesamtergebnis");
        sb.AppendLine($"| | |");
        sb.AppendLine($"|--|--|");
        sb.AppendLine($"| **Ø Gesamtschaden** | **{totalDamage:F2}** |");
        sb.AppendLine($"| **Ø Modelle getötet** | **{totalModelsKilled:F2}** |");
        sb.AppendLine();

        if (totalModelsKilled >= 0.5)
            sb.AppendLine($"💡 Du tötest im Durchschnitt **{totalModelsKilled:F1} Modelle** pro Schussphase.");
        else if (totalModelsKilled > 0)
        {
            double rounds = 1.0 / totalModelsKilled;
            sb.AppendLine($"💡 Du brauchst im Schnitt **{rounds:F1} Schussrunden** um 1 Modell zu töten.");
        }

        return sb.ToString();
    }

    // ── Monte-Carlo-Simulation ──────────────────────────────────────────────────

    [McpServerTool, Description(
        "Simuliert tausende echte Würfelwürfe (Monte-Carlo) statt nur den Erwartungswert zu berechnen. " +
        "Zeigt dir die tatsächliche Streuung: wie oft du 0 Modelle tötest, wahrscheinlichste Ergebnisse, " +
        "Median, Worst-/Best-Case. Realistischer als calculate_combat für Entscheidungen am Spieltisch. " +
        "Unterstützt Leader UND Support Charaktere gleichzeitig (neue Regel). " +
        "Beispiel: simulate_combat('Einhyr Hearthguard', 'Deathshroud Terminators', mode: 'ranged')")]
    public string simulate_combat(
        [Description("Name der angreifenden Einheit")] string attacker_name,
        [Description("Name der verteidigenden Einheit")] string defender_name,
        [Description("'ranged' für Fernkampf, 'melee' für Nahkampf")] string mode = "ranged",
        [Description("Fraktion des Angreifers (optional)")] string? attacker_faction = null,
        [Description("Fraktion des Verteidigers (optional)")] string? defender_faction = null,
        [Description("Anzahl Modelle im Angreifer-Trupp")] int attacker_models = 5,
        [Description("Nur bestimmte Waffen berechnen, kommagetrennt. Leer = alle Waffen")] string? weapons_filter = null,
        [Description("Name des Leaders der den Angreifer führt (optional)")] string? attacker_leader = null,
        [Description("Name des Leaders der den Verteidiger führt (optional)")] string? defender_leader = null,
        [Description("Name des Support-Charakters beim Angreifer (optional), z.B. 'Apothecary'")] string? attacker_support = null,
        [Description("Name des Support-Charakters beim Verteidiger (optional), z.B. 'Painboy'")] string? defender_support = null,
        [Description("Anzahl Modelle im Verteidiger-Trupp (relevant für BLAST-Waffen: +1 Attacke bei 6-10, +3 bei 11+ Modellen)")] int defender_models = 5,
        [Description("Hat der Verteidiger Benefit of Cover? Waffen mit AP -1 werden dadurch zu AP 0 (gilt nicht für AP -2 oder schlechter)")] bool defender_cover = false,
        [Description("Detachment des Angreifers (optional), dessen Detachment-Fähigkeit einfließt, z.B. 'Shield Host'")] string? attacker_detachment = null,
        [Description("Name der Enhancement, die der Angreifer(-Leader) trägt (optional), z.B. 'Aegis Projector'")] string? attacker_enhancement = null,
        [Description("Detachment des Verteidigers (optional), dessen Detachment-Fähigkeit einfließt")] string? defender_detachment = null,
        [Description("Name der Enhancement, die der Verteidiger(-Leader) trägt (optional)")] string? defender_enhancement = null,
        [Description("Anzahl simulierter Durchläufe (Standard 10000, mehr = genauer aber langsamer)")] int iterations = 10000)
    {
        var attackerDs = repo.SearchDatasheets(attacker_name, attacker_faction).FirstOrDefault();
        var defenderDs = repo.SearchDatasheets(defender_name, defender_faction).FirstOrDefault();

        if (attackerDs == null) return $"Angreifer '{attacker_name}' nicht gefunden.";
        if (defenderDs == null) return $"Verteidiger '{defender_name}' nicht gefunden.";

        Datasheet? attackerLeaderDs = null;
        Datasheet? defenderLeaderDs = null;
        Datasheet? attackerSupportDs = null;
        Datasheet? defenderSupportDs = null;
        if (attacker_leader != null)
        {
            attackerLeaderDs = repo.SearchDatasheets(attacker_leader, attacker_faction).FirstOrDefault();
            if (attackerLeaderDs == null) return $"Angreifer-Leader '{attacker_leader}' nicht gefunden.";
        }
        if (defender_leader != null)
        {
            defenderLeaderDs = repo.SearchDatasheets(defender_leader, defender_faction).FirstOrDefault();
            if (defenderLeaderDs == null) return $"Verteidiger-Leader '{defender_leader}' nicht gefunden.";
        }
        if (attacker_support != null)
        {
            attackerSupportDs = repo.SearchDatasheets(attacker_support, attacker_faction).FirstOrDefault();
            if (attackerSupportDs == null) return $"Angreifer-Support '{attacker_support}' nicht gefunden.";
        }
        if (defender_support != null)
        {
            defenderSupportDs = repo.SearchDatasheets(defender_support, defender_faction).FirstOrDefault();
            if (defenderSupportDs == null) return $"Verteidiger-Support '{defender_support}' nicht gefunden.";
        }

        var attackerAbilities = ParseAbilities(attackerDs, isLeader: false);
        if (attackerLeaderDs != null)
            attackerAbilities.MergeWith(ParseAbilities(attackerLeaderDs, isLeader: true));
        if (attackerSupportDs != null)
            attackerAbilities.MergeWith(ParseAbilities(attackerSupportDs, isLeader: true));

        var defenderAbilities = ParseAbilities(defenderDs, isLeader: false);
        if (defenderLeaderDs != null)
            defenderAbilities.MergeWith(ParseAbilities(defenderLeaderDs, isLeader: true));
        if (defenderSupportDs != null)
            defenderAbilities.MergeWith(ParseAbilities(defenderSupportDs, isLeader: true));

        var attachError = ApplyDetachmentAndEnhancement(
            attackerAbilities, attackerDs.FactionId, attacker_detachment, attacker_enhancement, "Angreifer");
        if (attachError != null) return attachError;
        var defendError = ApplyDetachmentAndEnhancement(
            defenderAbilities, defenderDs.FactionId, defender_detachment, defender_enhancement, "Verteidiger");
        if (defendError != null) return defendError;

        var defModel = defenderDs.Models.FirstOrDefault();
        if (defModel == null) return $"Keine Stats für '{defender_name}' gefunden.";

        int defT  = int.TryParse(defModel.T, out var t) ? t : 4;
        int defSv = ParseSave(defModel.Sv);
        int defW  = int.TryParse(defModel.W, out var w) ? w : 1;

        int defInv = ParseSave(defModel.InvSv);
        if (defenderAbilities.AdditionalInvSave > 0)
            defInv = defInv > 0
                ? Math.Min(defInv, defenderAbilities.AdditionalInvSave)
                : defenderAbilities.AdditionalInvSave;

        int defFnp = defenderAbilities.FnpValue;

        var weapons = attackerDs.Weapons
            .Where(w => mode == "ranged"
                ? w.Type.Equals("ranged", StringComparison.OrdinalIgnoreCase)
                : w.Type.Equals("melee", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (weapons_filter != null)
        {
            var filter = weapons_filter.Split(',').Select(f => f.Trim()).ToList();
            weapons = weapons.Where(w => filter.Any(f =>
                w.Name.Contains(f, StringComparison.OrdinalIgnoreCase))).ToList();
        }

        if (weapons.Count == 0)
            return $"{attackerDs.Name} hat keine {(mode == "ranged" ? "Fernkampf" : "Nahkampf")}waffen.";

        iterations = Math.Clamp(iterations, 100, 100_000);

        var rng = Random.Shared;
        var totalModelsKilledResults = new int[iterations];
        var totalDamageResults = new double[iterations];

        // Initiales (unverwundetes) Modell-Wundlimit für "tote Modelle zählen"
        // Wir simulieren auf Wunden-Pool-Basis: Schaden wird auf das aktuelle Modell "gestapelt"
        // bis es stirbt (W erreicht), Rest-Schaden geht aufs nächste Modell (vereinfachtes, aber
        // realistischeres Modell als reines avgDamage/W).
        for (int iter = 0; iter < iterations; iter++)
        {
            double remainingWoundsOnCurrentModel = defW;
            int modelsKilled = 0;
            double totalDamageThisRun = 0;

            foreach (var weapon in weapons)
            {
                var simResult = SimulateWeapon(weapon, attacker_models, defT, defSv, defInv, defFnp,
                    defender_models, defender_cover, attackerAbilities, defenderAbilities, rng);

                totalDamageThisRun += simResult;

                // Schaden auf Modelle verteilen
                double dmgLeft = simResult;
                while (dmgLeft > 0 && modelsKilled < attacker_models * 50) // Sicherheitslimit
                {
                    if (dmgLeft >= remainingWoundsOnCurrentModel)
                    {
                        dmgLeft -= remainingWoundsOnCurrentModel;
                        modelsKilled++;
                        remainingWoundsOnCurrentModel = defW;
                    }
                    else
                    {
                        remainingWoundsOnCurrentModel -= dmgLeft;
                        dmgLeft = 0;
                    }
                }
            }

            totalModelsKilledResults[iter] = modelsKilled;
            totalDamageResults[iter] = totalDamageThisRun;
        }

        Array.Sort(totalModelsKilledResults);
        Array.Sort(totalDamageResults);

        double avgKilled = totalModelsKilledResults.Average();
        double avgDamage = totalDamageResults.Average();
        int medianKilled = totalModelsKilledResults[iterations / 2];
        double medianDamage = totalDamageResults[iterations / 2];
        int p10Killed = totalModelsKilledResults[(int)(iterations * 0.10)];
        int p90Killed = totalModelsKilledResults[(int)(iterations * 0.90)];
        int minKilled = totalModelsKilledResults[0];
        int maxKilled = totalModelsKilledResults[^1];

        int zeroKillCount = totalModelsKilledResults.Count(x => x == 0);
        double zeroKillChance = (double)zeroKillCount / iterations;

        // Histogramm der Modelle-getötet-Verteilung (0 bis max, gekappt bei 10 für Lesbarkeit)
        int histMax = Math.Min(maxKilled, 10);
        var histogram = new int[histMax + 1];
        foreach (var v in totalModelsKilledResults)
        {
            var bucket = Math.Min(v, histMax);
            histogram[bucket]++;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"# 🎲 Monte-Carlo-Simulation: {attackerDs.Name} → {defenderDs.Name}");
        sb.AppendLine($"**Modus:** {(mode == "ranged" ? "Fernkampf 🔫" : "Nahkampf ⚔️")}  " +
                      $"**Angreifer-Modelle:** {attacker_models}  **Durchläufe:** {iterations:N0}");
        if (attackerLeaderDs != null) sb.AppendLine($"**Angreifer-Leader:** {attackerLeaderDs.Name}");
        if (attackerSupportDs != null) sb.AppendLine($"**Angreifer-Support:** {attackerSupportDs.Name}");
        if (attacker_detachment != null) sb.AppendLine($"**Angreifer-Detachment:** {attacker_detachment}");
        if (attacker_enhancement != null) sb.AppendLine($"**Angreifer-Enhancement:** {attacker_enhancement}");
        if (defenderLeaderDs != null) sb.AppendLine($"**Verteidiger-Leader:** {defenderLeaderDs.Name}");
        if (defenderSupportDs != null) sb.AppendLine($"**Verteidiger-Support:** {defenderSupportDs.Name}");
        if (defender_detachment != null) sb.AppendLine($"**Verteidiger-Detachment:** {defender_detachment}");
        if (defender_enhancement != null) sb.AppendLine($"**Verteidiger-Enhancement:** {defender_enhancement}");
        sb.AppendLine();

        sb.AppendLine("## Verteidiger Stats");
        sb.AppendLine($"**T:** {defT}  **SV:** {defModel.Sv}  " +
                      $"**InvSv:** {(defInv > 0 ? defInv + "+" : "–")}  " +
                      $"**W:** {defW}  **Modelle:** {defender_models}" +
                      (defFnp > 0 ? $"  **FNP:** {defFnp}+" : "") +
                      (defender_cover ? "  **🛡️ Benefit of Cover**" : ""));
        sb.AppendLine();

        sb.AppendLine("## 📊 Ergebnisverteilung über alle Durchläufe");
        sb.AppendLine();
        sb.AppendLine("| Kennzahl | Schaden | Modelle getötet |");
        sb.AppendLine("|----------|---------|------------------|");
        sb.AppendLine($"| Durchschnitt (Ø) | {avgDamage:F2} | {avgKilled:F2} |");
        sb.AppendLine($"| Median | {medianDamage:F2} | {medianKilled} |");
        sb.AppendLine($"| Min | {totalDamageResults[0]:F2} | {minKilled} |");
        sb.AppendLine($"| Max | {totalDamageResults[^1]:F2} | {maxKilled} |");
        sb.AppendLine($"| 10. Perzentil (schlechte Würfe) | – | {p10Killed} |");
        sb.AppendLine($"| 90. Perzentil (gute Würfe) | – | {p90Killed} |");
        sb.AppendLine();

        sb.AppendLine($"⚠️ **Wahrscheinlichkeit für 0 getötete Modelle: {zeroKillChance:P1}**");
        sb.AppendLine();

        sb.AppendLine("## 📈 Verteilung (Modelle getötet)");
        sb.AppendLine();
        int maxBarWidth = 30;
        int maxCount = histogram.Max();
        for (int i = 0; i <= histMax; i++)
        {
            var pct = (double)histogram[i] / iterations;
            var barLen = maxCount > 0 ? (int)(histogram[i] / (double)maxCount * maxBarWidth) : 0;
            var bar = new string('█', barLen);
            var label = (i == histMax && maxKilled > histMax) ? $"{i}+" : $"{i}";
            sb.AppendLine($"`{label,3}` {bar} {pct:P1}");
        }
        sb.AppendLine();

        sb.AppendLine("---");
        sb.AppendLine($"💡 In **80%** der Fälle tötest du zwischen **{p10Killed}** und **{p90Killed}** Modelle. " +
                      $"Der Erwartungswert (Ø) allein ({avgKilled:F1}) verschleiert oft, dass Würfelglück stark schwankt — " +
                      $"plane lieber mit dem 10. Perzentil ({p10Killed}) für Worst-Case-Entscheidungen am Tisch.");

        return sb.ToString();
    }

    /// <summary>Simuliert eine Waffe für EINEN Durchlauf mit echtem Würfeln (W6 pro Attacke/Wund/Save).</summary>
    internal static double SimulateWeapon(
        DatasheetWeapon weapon, int models,
        int defT, int defSv, int defInv, int defFnp, int defenderModels, bool defenderCover,
        CombatAbilities attacker, CombatAbilities defender, Random rng)
    {
        bool isTorrent    = weapon.Keywords.Contains("TORRENT", StringComparison.OrdinalIgnoreCase);
        bool isLethal     = attacker.LethalHits || weapon.Keywords.Contains("LETHAL HITS", StringComparison.OrdinalIgnoreCase);
        int keywordSustainedCount = ParseSustainedHitsFromKeywords(weapon.Keywords);
        bool isSustained  = attacker.SustainedHitsCount > 0 || keywordSustainedCount > 0;
        int sustainedCount = Math.Max(attacker.SustainedHitsCount, keywordSustainedCount);
        if (sustainedCount == 0) sustainedCount = 1;
        bool isDevWounds  = attacker.DevastatingWounds || weapon.Keywords.Contains("DEVASTATING WOUNDS", StringComparison.OrdinalIgnoreCase);
        bool isTwinLinked = weapon.Keywords.Contains("TWIN-LINKED", StringComparison.OrdinalIgnoreCase);
        bool isBlast      = weapon.Keywords.Contains("BLAST", StringComparison.OrdinalIgnoreCase);

        int attacksPerModel = (int)Math.Round(RollDice(weapon.A, rng));
        int totalAttacks = attacksPerModel * models + BlastBonusAttacks(isBlast, defenderModels);

        int weaponS  = int.TryParse(weapon.S, out var s) ? s : 4;
        int weaponAp = int.TryParse(weapon.Ap, out var apRaw) ? Math.Abs(apRaw) : 0;
        // Benefit of Cover: eine Attacke mit AP -1 wird dadurch zu AP 0 (nicht bei AP -2 oder schlechter).
        if (defenderCover && weaponAp == 1) weaponAp = 0;
        int bsWs = ParseSave(weapon.BsWs);

        int hits = 0;
        int lethalWoundsDirect = 0;

        for (int i = 0; i < totalAttacks; i++)
        {
            int roll = isTorrent ? 6 : RollD6(rng);

            // Re-roll Hits of 1
            if (!isTorrent && roll == 1 && attacker.RerollHitsOf1)
                roll = RollD6(rng);
            else if (!isTorrent && attacker.RerollAllHits && roll < bsWs)
                roll = RollD6(rng);

            int effectiveBs = bsWs + (defender.MinusOneToHit ? 1 : 0);
            bool isHit = isTorrent || roll >= Math.Min(effectiveBs, 6);
            if (!isHit) continue;

            bool isCrit = roll >= attacker.CritHitThreshold;

            if (isCrit && isSustained)
                hits += sustainedCount; // Sustained Hits: extra Treffer statt Wundwurf
            else if (isCrit && isLethal)
                lethalWoundsDirect++; // Lethal Hits: auto-wound, geht direkt in Wundpool
            else
                hits++;
        }

        // ── Wund-Phase (für normale Treffer) ────────────────────────────────────
        int totalFailedSaves = 0;
        int mortalWoundsFromDevWounds = 0;

        void ResolveWound(bool skipWoundRoll)
        {
            bool isCritWound;
            if (skipWoundRoll)
            {
                isCritWound = false; // Lethal Hits selbst zählen nicht als kritische Wunde
            }
            else
            {
                int woundTarget = WoundTarget(weaponS, defT);
                int wRoll = RollD6(rng);
                if (wRoll == 1 && attacker.RerollWoundsOf1) wRoll = RollD6(rng);
                else if (attacker.RerollAllWounds || isTwinLinked)
                {
                    if (wRoll < woundTarget) wRoll = RollD6(rng);
                }
                if (wRoll < woundTarget) return; // Wundwurf fehlgeschlagen
                isCritWound = wRoll >= attacker.CritWoundThreshold;
            }

            if (isCritWound && isDevWounds)
            {
                mortalWoundsFromDevWounds++; // Devastating Wounds: ignoriert Saves komplett
                return;
            }

            // Save-Wurf
            int effectiveSave = defSv + weaponAp;
            int saveTarget = effectiveSave <= 6 ? Math.Max(effectiveSave, 2) : 7; // 7 = unmöglich zu schaffen
            if (defInv > 0) saveTarget = Math.Min(saveTarget, defInv);

            int saveRoll = RollD6(rng);
            bool saved = saveRoll >= saveTarget && saveTarget <= 6;
            if (!saved) totalFailedSaves++;
        }

        for (int i = 0; i < hits; i++) ResolveWound(skipWoundRoll: false);
        for (int i = 0; i < lethalWoundsDirect; i++) ResolveWound(skipWoundRoll: true);

        // ── Schaden würfeln ──────────────────────────────────────────────────────
        double totalDamage = 0;
        for (int i = 0; i < totalFailedSaves; i++)
        {
            double dmg = RollDice(weapon.D, rng);
            if (defender.DamageReduction > 0) dmg = Math.Max(1, dmg - defender.DamageReduction);
            totalDamage += ApplyFnp(dmg, defFnp, rng);
        }
        for (int i = 0; i < mortalWoundsFromDevWounds; i++)
        {
            double dmg = RollDice(weapon.D, rng);
            totalDamage += ApplyFnp(dmg, defFnp, rng);
        }

        return totalDamage;
    }

    private static double ApplyFnp(double damage, int fnp, Random rng)
    {
        if (fnp <= 0) return damage;
        // FNP wird pro Schadenspunkt gewürfelt (vereinfachte, gängige Annäherung)
        double survived = 0;
        int wholeDamage = (int)Math.Round(damage);
        for (int i = 0; i < wholeDamage; i++)
        {
            if (RollD6(rng) < fnp) survived += 1;
        }
        return survived;
    }

    /// <summary>
    /// BLAST: erhöht die Attacken-Charakteristik einmalig (nicht pro Modell) um +1 bei 6-10
    /// Modellen im Zieltrupp bzw. +3 bei 11+ Modellen (10th-Edition-Kernregel).
    /// </summary>
    private static int BlastBonusAttacks(bool isBlast, int defenderModels)
    {
        if (!isBlast) return 0;
        if (defenderModels >= 11) return 3;
        if (defenderModels >= 6) return 1;
        return 0;
    }

    private static int WoundTarget(int s, int t)
    {
        if (s >= t * 2) return 2;
        if (s > t)      return 3;
        if (s == t)     return 4;
        if (s * 2 > t)  return 5;
        return 6;
    }

    private static int RollD6(Random rng) => rng.Next(1, 7);

    /// <summary>Würfelt einen Dice-Ausdruck (z.B. "D6", "2D6+1", "3") tatsächlich aus, statt Erwartungswert.</summary>
    private static double RollDice(string expr, Random rng)
    {
        if (string.IsNullOrWhiteSpace(expr)) return 0;
        expr = expr.Trim().ToUpperInvariant();
        if (expr is "N/A" or "-" or "") return 0;
        if (double.TryParse(expr, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var n)) return n;

        var match = Regex.Match(expr, @"^(\d*)[Dd](\d+)(?:\+(\d+))?$");
        if (match.Success)
        {
            var count = match.Groups[1].Value != "" ? int.Parse(match.Groups[1].Value) : 1;
            var sides = int.Parse(match.Groups[2].Value);
            var bonus = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;
            double total = bonus;
            for (int i = 0; i < count; i++) total += rng.Next(1, sides + 1);
            return total;
        }
        return 0;
    }

    internal static WeaponResult CalculateWeapon(
        DatasheetWeapon weapon, int models,
        int defT, int defSv, int defInv, int defW, int defFnp, int defenderModels, bool defenderCover,
        CombatAbilities attacker, CombatAbilities defender)
    {
        bool isTorrent       = weapon.Keywords.Contains("TORRENT", StringComparison.OrdinalIgnoreCase);
        bool isLethal        = attacker.LethalHits || weapon.Keywords.Contains("LETHAL HITS", StringComparison.OrdinalIgnoreCase);
        int  keywordSustainedCount = ParseSustainedHitsFromKeywords(weapon.Keywords);
        bool isSustained     = attacker.SustainedHitsCount > 0 || keywordSustainedCount > 0;
        int  sustainedCount  = Math.Max(attacker.SustainedHitsCount, keywordSustainedCount);
        if (sustainedCount == 0) sustainedCount = 1;
        bool isDevWounds     = attacker.DevastatingWounds || weapon.Keywords.Contains("DEVASTATING WOUNDS", StringComparison.OrdinalIgnoreCase);
        bool isTwinLinked    = weapon.Keywords.Contains("TWIN-LINKED", StringComparison.OrdinalIgnoreCase);
        bool isBlast         = weapon.Keywords.Contains("BLAST", StringComparison.OrdinalIgnoreCase);

        double attacksPerModel = ParseDice(weapon.A);
        double totalAttacks    = attacksPerModel * models + BlastBonusAttacks(isBlast, defenderModels);

        int weaponS  = int.TryParse(weapon.S, out var s) ? s : 4;
        int weaponAp = int.TryParse(weapon.Ap, out var apRaw) ? Math.Abs(apRaw) : 0;
        // Benefit of Cover: eine Attacke mit AP -1 wird dadurch zu AP 0 (nicht bei AP -2 oder schlechter).
        if (defenderCover && weaponAp == 1) weaponAp = 0;
        double avgDmg = ParseDice(weapon.D);
        if (defender.DamageReduction > 0)
            avgDmg = Math.Max(1, avgDmg - defender.DamageReduction);

        int bsWs = ParseSave(weapon.BsWs);

        // ── Treffer-Phase ─────────────────────────────────────────────────────
        double rawHitChance;
        if (isTorrent)
        {
            rawHitChance = 1.0;
        }
        else
        {
            int effectiveBs = bsWs + (defender.MinusOneToHit ? 1 : 0);
            effectiveBs = Math.Min(effectiveBs, 6);
            rawHitChance = effectiveBs > 0 ? (7.0 - effectiveBs) / 6.0 : 0.5;
        }

        // Re-rolls
        double hitChance = rawHitChance;
        if (!isTorrent)
        {
            if (attacker.RerollAllHits)
                hitChance = rawHitChance + (1 - rawHitChance) * rawHitChance;
            else if (attacker.RerollHitsOf1)
                hitChance = rawHitChance + (1.0 / 6.0) * rawHitChance;
        }

        double critChance    = (7.0 - attacker.CritHitThreshold) / 6.0;
        double critHits      = totalAttacks * critChance;
        double normalHits    = totalAttacks * (hitChance - critChance);

        if (isSustained) normalHits += critHits * sustainedCount;

        double lethalWounds  = isLethal ? critHits : 0;
        double hitsToWound   = isLethal ? normalHits : normalHits + critHits;
        double avgHits       = normalHits + critHits;

        // ── Wund-Phase ────────────────────────────────────────────────────────
        double baseWoundChance = WoundProb(weaponS, defT);
        double woundChance     = baseWoundChance;

        if (isTwinLinked || attacker.RerollAllWounds)
            woundChance = woundChance + (1 - woundChance) * woundChance;
        else if (attacker.RerollWoundsOf1)
            woundChance = woundChance + (1.0 / 6.0) * woundChance;

        double critWoundChance = (7.0 - attacker.CritWoundThreshold) / 6.0;
        double critWounds      = hitsToWound * critWoundChance;
        double normalWounds    = hitsToWound * (woundChance - critWoundChance);
        double totalWounds     = lethalWounds + critWounds + normalWounds;

        double mortalWounds    = isDevWounds ? critWounds : 0;
        double regularWounds   = isDevWounds ? lethalWounds + normalWounds : totalWounds;

        // ── Save-Phase ────────────────────────────────────────────────────────
        double saveChance    = SaveProb(defSv, weaponAp, defInv);
        double failSaveChance = 1.0 - saveChance;

        double failedSaves   = regularWounds * failSaveChance + mortalWounds;

        // ── Schaden ───────────────────────────────────────────────────────────
        double totalDamage   = failedSaves * avgDmg;
        double fnpChance     = defFnp > 0 ? (7.0 - defFnp) / 6.0 : 0;
        double damageAfterFnp = defFnp > 0 ? totalDamage * (1.0 - fnpChance) : totalDamage;

        double effectiveDamage = defFnp > 0 ? damageAfterFnp : totalDamage;
        double modelsKilled    = defW > 1 ? effectiveDamage / defW : effectiveDamage;

        return new WeaponResult
        {
            TotalAttacks      = totalAttacks,
            AvgHits           = avgHits,
            AvgWounds         = totalWounds,
            AvgFailedSaves    = failedSaves,
            AvgDamage         = totalDamage,
            AvgDamageAfterFnp = damageAfterFnp,
            AvgModelsKilled   = modelsKilled,
            HitChance         = hitChance,
            WoundChance       = woundChance,
            FailSaveChance    = failSaveChance,
        };
    }

    internal record WeaponResult
    {
        public double TotalAttacks      { get; init; }
        public double AvgHits           { get; init; }
        public double AvgWounds         { get; init; }
        public double AvgFailedSaves    { get; init; }
        public double AvgDamage         { get; init; }
        public double AvgDamageAfterFnp { get; init; }
        public double AvgModelsKilled   { get; init; }
        public double HitChance         { get; init; }
        public double WoundChance       { get; init; }
        public double FailSaveChance    { get; init; }
    }
}

// ── Fähigkeiten-Container ─────────────────────────────────────────────────────

public class CombatAbilities
{
    public bool LethalHits         { get; set; }
    public int  SustainedHitsCount { get; set; }
    public bool DevastatingWounds  { get; set; }
    public bool RerollHitsOf1      { get; set; }
    public bool RerollAllHits      { get; set; }
    public bool RerollWoundsOf1    { get; set; }
    public bool RerollAllWounds    { get; set; }
    public bool MinusOneToHit      { get; set; }
    public int  FnpValue           { get; set; }
    public int  AdditionalInvSave  { get; set; }
    public int  DamageReduction    { get; set; }

    // Standard-Schwelle für kritische Treffer/Wunden ist 6 (unmodifizierte 6).
    // Manche Einheiten/Waffen senken das ab (z.B. "Critical Hits on a 5+").
    public int  CritHitThreshold   { get; set; } = 6;
    public int  CritWoundThreshold { get; set; } = 6;

    public List<string> GetSummary()
    {
        var result = new List<string>();
        if (LethalHits)            result.Add("✅ Lethal Hits");
        if (SustainedHitsCount > 0) result.Add($"✅ Sustained Hits {SustainedHitsCount}");
        if (DevastatingWounds)     result.Add("✅ Devastating Wounds");
        if (RerollAllHits)         result.Add("🎲 Re-roll alle Treffer");
        if (RerollHitsOf1)         result.Add("🎲 Re-roll Treffer von 1");
        if (RerollAllWounds)       result.Add("🎲 Re-roll alle Wunden");
        if (RerollWoundsOf1)       result.Add("🎲 Re-roll Wunden von 1");
        if (CritHitThreshold < 6)   result.Add($"💥 Kritischer Treffer bereits ab {CritHitThreshold}+");
        if (CritWoundThreshold < 6) result.Add($"💥 Kritische Wunde bereits ab {CritWoundThreshold}+");
        return result;
    }

    public List<string> GetDefenderSummary()
    {
        var result = new List<string>();
        if (FnpValue > 0)          result.Add($"🛡️ Feel No Pain {FnpValue}+");
        if (MinusOneToHit)         result.Add("🛡️ -1 auf Treffer (Stealth o.ä.)");
        if (AdditionalInvSave > 0) result.Add($"🛡️ Invulnerable Save {AdditionalInvSave}+");
        if (DamageReduction > 0)   result.Add($"🛡️ Schadensreduzierung -{DamageReduction}");
        return result;
    }

    public void MergeWith(CombatAbilities other)
    {
        if (other.LethalHits)            LethalHits = true;
        if (other.SustainedHitsCount > 0) SustainedHitsCount = Math.Max(SustainedHitsCount, other.SustainedHitsCount);
        if (other.DevastatingWounds)     DevastatingWounds = true;
        if (other.RerollAllHits)         RerollAllHits = true;
        if (other.RerollHitsOf1)         RerollHitsOf1 = true;
        if (other.RerollAllWounds)       RerollAllWounds = true;
        if (other.RerollWoundsOf1)       RerollWoundsOf1 = true;
        if (other.MinusOneToHit)         MinusOneToHit = true;
        if (other.FnpValue > 0)          FnpValue = FnpValue == 0 ? other.FnpValue : Math.Min(FnpValue, other.FnpValue);
        if (other.AdditionalInvSave > 0) AdditionalInvSave = AdditionalInvSave == 0 ? other.AdditionalInvSave : Math.Min(AdditionalInvSave, other.AdditionalInvSave);
        if (other.DamageReduction > 0)   DamageReduction = Math.Max(DamageReduction, other.DamageReduction);
        CritHitThreshold   = Math.Min(CritHitThreshold, other.CritHitThreshold);
        CritWoundThreshold = Math.Min(CritWoundThreshold, other.CritWoundThreshold);
    }
}