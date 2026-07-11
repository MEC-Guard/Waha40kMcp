using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using Waha40kMcp.Data;
using Waha40kMcp.Models;

namespace Waha40kMcp.Tools;

[McpServerToolType]
public class ArmyBuilderTools(WahapediaRepository repo, IMfmScraper mfmScraper)
{
    // In-memory Army Lists pro Session (Key = army name).
    // ConcurrentDictionary, da im --http Modus mehrere Requests parallel zugreifen können.
    private static readonly ConcurrentDictionary<string, ArmyList> Armies = new();

    // MFM Punkte Cache pro Fraktion
    private static readonly ConcurrentDictionary<string, Dictionary<string, List<PointsCostEntry>>> MfmCache = new();

    // ── MFM Punkte laden ──────────────────────────────────────────────────────

    private async Task<Dictionary<string, List<PointsCostEntry>>> GetMfmPointsAsync(string factionId, string factionName = "")
    {
        if (MfmCache.TryGetValue(factionId, out var cached)) return cached;

        // Versuche zuerst mit factionId, dann mit factionName
        var slug = MfmScraper.GetSlugForFaction(factionId)
                ?? MfmScraper.GetSlugForFaction(factionName);

        if (slug == null)
        {
            Console.Error.WriteLine($"[ArmyBuilder] Kein MFM-Slug für Fraktion '{factionId}' / '{factionName}'");
            return [];
        }

        var points = await mfmScraper.ScrapeFactionsAsync(slug);
        MfmCache[factionId] = points;
        return points;
    }

    private async Task<List<PointsCostEntry>> GetUnitPointsAsync(Datasheet ds)
    {
        var mfmPoints = await GetMfmPointsAsync(ds.FactionId, ds.FactionName);

        // Suche nach Einheitenname im MFM (case-insensitive)
        var key = ds.Name.ToLowerInvariant();
        if (mfmPoints.TryGetValue(key, out var pts)) return pts;

        // Fuzzy-Suche: MFM-Namen enthalten oft Umlaute oder andere Schreibweisen
        var fuzzy = mfmPoints.FirstOrDefault(kv =>
            kv.Key.Contains(key, StringComparison.OrdinalIgnoreCase) ||
            key.Contains(kv.Key, StringComparison.OrdinalIgnoreCase));

        if (fuzzy.Value != null) return fuzzy.Value;

        // Fallback: Wahapedia Punkte
        return ds.PointsCosts;
    }

    // ── Tool: Neue Army erstellen ─────────────────────────────────────────────

    [McpServerTool, Description(
        "Erstellt eine neue Army-Liste. " +
        "Beispiel: create_army('Meine Votann', 'Leagues of Votann', 2000)")]
    public string create_army(
        [Description("Name der Army, z.B. 'Meine Votann'")] string army_name,
        [Description("Fraktion, z.B. 'Leagues of Votann'")] string faction,
        [Description("Punktelimit (Standard: 2000)")] int points_limit = 2000)
    {
        var f = repo.FindFaction(faction);
        if (f == null) return $"Fraktion '{faction}' nicht gefunden.";

        Armies[army_name] = new ArmyList
        {
            Name        = army_name,
            FactionId   = f.Id,
            FactionName = f.Name,
            PointsLimit = points_limit,
            Units       = []
        };

        return $"✅ Army **{army_name}** ({f.Name}, {points_limit} Punkte) erstellt!\n\n" +
               $"Füge Einheiten hinzu mit: `add_unit('{army_name}', 'Einheitenname', Modellanzahl)`";
    }

    // ── Tool: Einheit hinzufügen ──────────────────────────────────────────────

    [McpServerTool, Description(
        "Fügt eine Einheit zur Army-Liste hinzu. Punktekosten werden automatisch vom MFM geladen, " +
        "inklusive Staffelung wenn weitere Kopien derselben Einheit mehr kosten. " +
        "Beispiel: add_unit('Meine Votann', 'Einhyr Hearthguard', 5)")]
    public async Task<string> add_unit(
        [Description("Name der Army")] string army_name,
        [Description("Name der Einheit")] string unit_name,
        [Description("Anzahl Modelle in der Einheit")] int model_count = 0)
    {
        if (!Armies.TryGetValue(army_name, out var army))
            return $"Army '{army_name}' nicht gefunden. Erstelle sie mit `create_army()`.";

        var ds = repo.SearchDatasheets(unit_name, army.FactionId).FirstOrDefault();
        if (ds == null) return $"Einheit '{unit_name}' nicht gefunden.";

        // Punkte vom MFM laden
        var pointsEntries = await GetUnitPointsAsync(ds);

        if (pointsEntries.Count == 0)
            return $"Keine Punktekosten für '{ds.Name}' gefunden.";

        // Wie viele Kopien dieser Einheit sind bereits in der Liste? (für Staffelung "1st-2nd" vs "3rd+")
        var existingCopies = army.Units.Count(u => u.DatasheetId == ds.Id);
        var copyNumber = existingCopies + 1; // diese wäre die Nte Kopie

        // Falls Tier-Informationen vorhanden sind (CopyTier > 0), passende Tier-Gruppe wählen
        var hasTiers = pointsEntries.Any(p => p.CopyTier > 0);
        List<PointsCostEntry> candidateEntries;
        bool usedHigherTier = false;

        if (hasTiers)
        {
            // Tier 1 = erste Kopien, Tier 2 = weitere Kopien.
            // Wahapedia gibt evtl. keine Tier-Angabe; MFM schon.
            var tier1 = pointsEntries.Where(p => p.CopyTier == 1).ToList();
            var tier2 = pointsEntries.Where(p => p.CopyTier == 2).ToList();

            // Standardmäßig nehmen wir an Tier 1 deckt die ersten 2 Kopien ab (häufigstes Muster),
            // ab der 3. Kopie greift Tier 2 falls vorhanden. Falls nur 2 Tiers ohne genauere Angabe,
            // ist das eine Annäherung — bei Unsicherheit lieber den höheren (teureren) Tier nehmen
            // um die Punkte nicht zu unterschätzen.
            if (copyNumber <= 2 || tier2.Count == 0)
            {
                candidateEntries = tier1.Count > 0 ? tier1 : pointsEntries;
            }
            else
            {
                candidateEntries = tier2;
                usedHigherTier = true;
            }
        }
        else
        {
            candidateEntries = pointsEntries;
        }

        // Modellanzahl → passende Punktekosten finden
        PointsCostEntry? selectedEntry;
        if (model_count <= 0 || candidateEntries.Count == 1)
        {
            selectedEntry = candidateEntries.First();
            model_count = ParseModelCount(selectedEntry.Description);
        }
        else
        {
            selectedEntry = candidateEntries
                .OrderByDescending(p => ParseModelCount(p.Description))
                .FirstOrDefault(p => ParseModelCount(p.Description) <= model_count)
                ?? candidateEntries.First();
        }

        var unit = new ArmyUnit
        {
            DatasheetId = ds.Id,
            Name        = ds.Name,
            Points      = selectedEntry.Cost,
            ModelCount  = model_count > 0 ? model_count : ParseModelCount(selectedEntry.Description),
        };

        army.Units.Add(unit);

        var sb = new StringBuilder();
        sb.AppendLine($"✅ **{ds.Name}** ({unit.ModelCount} Modelle, {unit.Points} Punkte) hinzugefügt.");
        if (usedHigherTier)
            sb.AppendLine($"⚠️ *Dies ist deine {copyNumber}. Kopie von {ds.Name} — teurere Staffelung angewendet.*");
        sb.AppendLine();
        sb.AppendLine($"**{army.Name}:** {army.TotalPoints} / {army.PointsLimit} Punkte " +
                      $"({army.PointsLimit - army.TotalPoints} verbleibend)");

        if (pointsEntries.Count > 1)
        {
            sb.AppendLine();
            sb.AppendLine($"*Verfügbare Staffelungen für {ds.Name}:*");
            foreach (var e in pointsEntries)
            {
                var tierLabel = e.CopyTier == 1 ? " (1.-2. Kopie)" : e.CopyTier == 2 ? " (3.+ Kopie)" : "";
                sb.AppendLine($"- {e.Description}: {e.Cost} Punkte{tierLabel}");
            }
        }

        return sb.ToString();
    }

    // ── Tool: Einheit entfernen ───────────────────────────────────────────────

    [McpServerTool, Description("Entfernt eine Einheit aus der Army-Liste per Index (1-basiert).")]
    public string remove_unit(
        [Description("Name der Army")] string army_name,
        [Description("Index der Einheit (1 = erste Einheit)")] int unit_index)
    {
        if (!Armies.TryGetValue(army_name, out var army))
            return $"Army '{army_name}' nicht gefunden.";

        if (unit_index < 1 || unit_index > army.Units.Count)
            return $"Ungültiger Index {unit_index}. Die Army hat {army.Units.Count} Einheiten.";

        var removed = army.Units[unit_index - 1];
        army.Units.RemoveAt(unit_index - 1);

        return $"✅ **{removed.Name}** entfernt.\n\n" +
               $"**{army.Name}:** {army.TotalPoints} / {army.PointsLimit} Punkte";
    }

    // ── Tool: Army anzeigen ───────────────────────────────────────────────────

    [McpServerTool, Description(
        "Zeigt die aktuelle Army-Liste mit allen Einheiten und Punkten. " +
        "Beispiel: show_army('Meine Votann')")]
    public string show_army(
        [Description("Name der Army")] string army_name)
    {
        if (!Armies.TryGetValue(army_name, out var army))
            return $"Army '{army_name}' nicht gefunden. Verfügbare Armies: {string.Join(", ", Armies.Keys)}";

        var sb = new StringBuilder();
        sb.AppendLine($"# ⚒️ {army.Name}");
        sb.AppendLine($"**Fraktion:** {army.FactionName}  |  " +
                      $"**Punkte:** {army.TotalPoints} / {army.PointsLimit}");
        sb.AppendLine();

        var remaining = army.PointsLimit - army.TotalPoints;
        var bar = GenerateProgressBar(army.TotalPoints, army.PointsLimit);
        sb.AppendLine($"{bar} {army.TotalPoints}/{army.PointsLimit}");
        sb.AppendLine();

        if (army.Units.Count == 0)
        {
            sb.AppendLine("*Noch keine Einheiten hinzugefügt.*");
            sb.AppendLine($"Nutze `add_unit('{army_name}', 'Einheitenname', Modellanzahl)`");
            return sb.ToString();
        }

        sb.AppendLine("| # | Einheit | Modelle | Punkte |");
        sb.AppendLine("|---|---------|---------|--------|");
        for (int i = 0; i < army.Units.Count; i++)
        {
            var u = army.Units[i];
            sb.AppendLine($"| {i + 1} | {u.Name} | {u.ModelCount} | {u.Points} |");
        }

        sb.AppendLine();
        sb.AppendLine($"**Gesamt: {army.TotalPoints} Punkte** " +
                      (remaining >= 0
                          ? $"({remaining} Punkte verbleibend ✅)"
                          : $"(**{-remaining} Punkte ÜBERSCHRITTEN ❌**)"));

        return sb.ToString();
    }

    // ── Tool: Alle Armies auflisten ───────────────────────────────────────────

    [McpServerTool, Description("Listet alle gespeicherten Army-Listen auf.")]
    public string list_armies()
    {
        if (Armies.Count == 0)
            return "Noch keine Armies erstellt. Nutze `create_army()`.";

        var sb = new StringBuilder();
        sb.AppendLine("# Gespeicherte Armies");
        sb.AppendLine();
        foreach (var (name, army) in Armies)
        {
            var status = army.TotalPoints <= army.PointsLimit ? "✅" : "❌";
            sb.AppendLine($"- **{name}** — {army.FactionName} — " +
                          $"{army.TotalPoints}/{army.PointsLimit} Punkte {status} — " +
                          $"{army.Units.Count} Einheiten");
        }
        return sb.ToString();
    }

    // ── Tool: MFM Punkte aktualisieren ────────────────────────────────────────

    [McpServerTool, Description(
        "Aktualisiert die Punktekosten einer Fraktion frisch vom MFM (löscht den Cache). " +
        "Nutzen wenn Games Workshop neue Punkte veröffentlicht hat.")]
    public async Task<string> refresh_mfm_points(
        [Description("Fraktionsname, z.B. 'Leagues of Votann'")] string faction)
    {
        var f = repo.FindFaction(faction);
        if (f == null) return $"Fraktion '{faction}' nicht gefunden.";

        var slug = MfmScraper.GetSlugForFaction(f.Id)
                ?? MfmScraper.GetSlugForFaction(f.Name);
        if (slug == null) return $"Kein MFM-Eintrag für '{f.Name}' gefunden.";

        // Cache leeren
        MfmCache.TryRemove(f.Id, out _);

        var points = await mfmScraper.ScrapeFactionsAsync(slug, forceRefresh: true);
        MfmCache[f.Id] = points;

        var wargearCount = mfmScraper.GetWargearOptions(slug).Count;
        return $"✅ Punkte für **{f.Name}** aktualisiert — {points.Count} Einheiten geladen" +
               (wargearCount > 0 ? $", {wargearCount} mit Wargear-Aufpreisen." : ".");
    }

    // ── Tool: Wargear-Aufpreise anzeigen ──────────────────────────────────────

    [McpServerTool, Description(
        "Zeigt Wargear-Punktaufpreise einer Einheit (z.B. 'per Storm Shield: 5 pts'). " +
        "Diese Kosten kommen zusätzlich zu den normalen Unit-Punkten dazu, pro Modell das " +
        "die Option trägt. Beispiel: get_wargear_options('Wolf Guard Terminators')")]
    public async Task<string> get_wargear_options(
        [Description("Name der Einheit")] string unit_name,
        [Description("Fraktion (optional)")] string? faction = null)
    {
        var ds = repo.SearchDatasheets(unit_name, faction != null ? repo.FindFaction(faction)?.Id : null)
                      .FirstOrDefault();
        if (ds == null) return $"Einheit '{unit_name}' nicht gefunden.";

        var mfmPoints = await GetMfmPointsAsync(ds.FactionId, ds.FactionName); // stellt sicher Fraktion ist gescraped
        var slug = MfmScraper.GetSlugForFaction(ds.FactionId) ?? MfmScraper.GetSlugForFaction(ds.FactionName);
        if (slug == null) return $"Kein MFM-Eintrag für Fraktion '{ds.FactionName}' gefunden.";

        var wargearByUnit = mfmScraper.GetWargearOptions(slug);
        var key = ds.Name.ToLowerInvariant();

        if (!wargearByUnit.TryGetValue(key, out var options) || options.Count == 0)
        {
            var fuzzy = wargearByUnit.FirstOrDefault(kv =>
                kv.Key.Contains(key, StringComparison.OrdinalIgnoreCase) ||
                key.Contains(kv.Key, StringComparison.OrdinalIgnoreCase));
            options = fuzzy.Value;
        }

        if (options == null || options.Count == 0)
            return $"Keine kostenpflichtigen Wargear-Optionen für **{ds.Name}** im MFM gefunden " +
                   $"(Wargear ist hier vermutlich im Grundpreis enthalten).";

        var sb = new StringBuilder();
        sb.AppendLine($"# ⚙️ Wargear-Aufpreise: {ds.Name}");
        sb.AppendLine();
        sb.AppendLine("| Option | Punkte pro Modell |");
        sb.AppendLine("|--------|--------------------|");
        foreach (var o in options)
            sb.AppendLine($"| {o.Name} | +{o.Cost} |");
        sb.AppendLine();
        sb.AppendLine("*Diese Punkte kommen zusätzlich zum Grundpreis der Einheit, pro Modell das die Option trägt.*");

        return sb.ToString();
    }

    // ── Hilfsmethoden ─────────────────────────────────────────────────────────

    internal static int ParseModelCount(string description)
    {
        var match = System.Text.RegularExpressions.Regex.Match(description, @"(\d+)");
        return match.Success ? int.Parse(match.Groups[1].Value) : 1;
    }

    internal static string GenerateProgressBar(int current, int max)
    {
        int filled = (int)Math.Round(20.0 * Math.Min(current, max) / max);
        var over = current > max;
        var filledChar = over ? 'X' : '#';
        var bar = new string(filledChar, filled) + new string('-', 20 - filled);
        return $"[{bar}]";
    }
}