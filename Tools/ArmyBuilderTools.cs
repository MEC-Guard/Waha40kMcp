using System.Collections.Concurrent;
using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using Waha40kMcp.Data;
using Waha40kMcp.Models;

namespace Waha40kMcp.Tools;

[McpServerToolType]
public class ArmyBuilderTools(WahapediaRepository repo, IMfmScraper mfmScraper, ArmyRepository armyRepo)
{
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
        "Erstellt eine neue Army-Liste. Das Detachment kann optional direkt hier oder " +
        "später per set_detachment() gesetzt werden (nötig für add_enhancement). " +
        "Beispiel: create_army('Meine Votann', 'Leagues of Votann', 2000, detachment: 'Oathband')")]
    public string create_army(
        [Description("Name der Army, z.B. 'Meine Votann'")] string army_name,
        [Description("Fraktion, z.B. 'Leagues of Votann'")] string faction,
        [Description("Punktelimit (Standard: 2000)")] int points_limit = 2000,
        [Description("Detachment (optional), siehe list_detachments(). Kann auch später per set_detachment() gesetzt werden.")] string? detachment = null)
    {
        var f = repo.FindFaction(faction);
        if (f == null) return $"Fraktion '{faction}' nicht gefunden.";

        string resolvedDetachment = "";
        if (detachment != null)
        {
            var detachmentAbility = repo.FindDetachment(f.Id, detachment);
            if (detachmentAbility == null)
                return $"Detachment '{detachment}' nicht gefunden für '{f.Name}'. " +
                       $"Verfügbar: {string.Join(", ", repo.GetDetachmentNames(f.Id))}";
            resolvedDetachment = detachmentAbility.Detachment;
        }

        armyRepo.Set(army_name, new ArmyList
        {
            Name        = army_name,
            FactionId   = f.Id,
            FactionName = f.Name,
            PointsLimit = points_limit,
            Detachment  = resolvedDetachment,
            Units       = []
        });

        var sb = new StringBuilder();
        sb.AppendLine($"✅ Army **{army_name}** ({f.Name}, {points_limit} Punkte" +
                      (resolvedDetachment != "" ? $", Detachment: {resolvedDetachment}" : "") + ") erstellt!");
        sb.AppendLine();
        sb.AppendLine($"Füge Einheiten hinzu mit: `add_unit('{army_name}', 'Einheitenname', Modellanzahl)`");
        if (resolvedDetachment == "")
            sb.AppendLine($"Detachment setzen mit: `set_detachment('{army_name}', 'Detachment-Name')` — siehe `list_detachments('{f.Name}')`");
        return sb.ToString();
    }

    // ── Tool: Detachment setzen ────────────────────────────────────────────────

    [McpServerTool, Description(
        "Legt das Detachment einer Army-Liste fest (oder ändert es). Nötig für add_enhancement(). " +
        "Beispiel: set_detachment('Meine Votann', 'Oathband')")]
    public string set_detachment(
        [Description("Name der Army")] string army_name,
        [Description("Detachment-Name, siehe list_detachments()")] string detachment)
    {
        var army = armyRepo.Get(army_name);
        if (army == null) return $"Army '{army_name}' nicht gefunden.";

        var detachmentAbility = repo.FindDetachment(army.FactionId, detachment);
        if (detachmentAbility == null)
            return $"Detachment '{detachment}' nicht gefunden für '{army.FactionName}'. " +
                   $"Verfügbar: {string.Join(", ", repo.GetDetachmentNames(army.FactionId))}";

        army.Detachment = detachmentAbility.Detachment;
        armyRepo.SaveChanges();

        return $"✅ Detachment **{army.Detachment}** für **{army_name}** gesetzt.\n\n" +
               $"**{detachmentAbility.Name}:** {detachmentAbility.Description}";
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
        var army = armyRepo.Get(army_name);
        if (army == null) return $"Army '{army_name}' nicht gefunden. Erstelle sie mit `create_army()`.";

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
        armyRepo.SaveChanges();

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
        var army = armyRepo.Get(army_name);
        if (army == null) return $"Army '{army_name}' nicht gefunden.";

        if (unit_index < 1 || unit_index > army.Units.Count)
            return $"Ungültiger Index {unit_index}. Die Army hat {army.Units.Count} Einheiten.";

        var removed = army.Units[unit_index - 1];
        army.Units.RemoveAt(unit_index - 1);

        // Verwaiste Leader-Zuordnungen aufräumen: falls die entfernte Einheit selbst geführt
        // wurde oder ein Leader war, würde die gespeicherte Id sonst ins Leere zeigen.
        foreach (var u in army.Units)
            if (u.AttachedToUnitId == removed.Id)
                u.AttachedToUnitId = null;

        armyRepo.SaveChanges();

        return $"✅ **{removed.Name}** entfernt.\n\n" +
               $"**{army.Name}:** {army.TotalPoints} / {army.PointsLimit} Punkte";
    }

    // ── Tool: Leader anführen lassen ───────────────────────────────────────────

    [McpServerTool, Description(
        "Ordnet einen Leader/Charakter als Anführer einer anderen Einheit zu (wie 'Attach' in " +
        "New Recruit). Wirkt sich nur auf export_army_pdf aus: Leader und geführte Einheit werden " +
        "dort zu einem gemeinsamen Datasheet-Block zusammengeführt statt zwei getrennte Seiten zu " +
        "erzeugen. Beispiel: attach_leader('Meine Votann', 1, 2) — Einheit 1 führt Einheit 2 an.")]
    public string attach_leader(
        [Description("Name der Army")] string army_name,
        [Description("Index des Leaders (1-basiert, siehe show_army)")] int leader_unit_index,
        [Description("Index der Einheit, die angeführt werden soll (1-basiert)")] int target_unit_index)
    {
        var army = armyRepo.Get(army_name);
        if (army == null) return $"Army '{army_name}' nicht gefunden.";

        if (leader_unit_index < 1 || leader_unit_index > army.Units.Count)
            return $"Ungültiger Leader-Index {leader_unit_index}. Die Army hat {army.Units.Count} Einheiten.";
        if (target_unit_index < 1 || target_unit_index > army.Units.Count)
            return $"Ungültiger Ziel-Index {target_unit_index}. Die Army hat {army.Units.Count} Einheiten.";
        if (leader_unit_index == target_unit_index)
            return "Eine Einheit kann nicht sich selbst anführen.";

        var leader = army.Units[leader_unit_index - 1];
        var target = army.Units[target_unit_index - 1];

        // Ketten vermeiden: die Ziel-Einheit darf nicht selbst schon ein Leader sein, der wiederum
        // eine andere Einheit anführt — New Recruit kennt nur eine Ebene (Leader -> geführte Einheit).
        if (target.AttachedToUnitId != null)
            return $"**{target.Name}** führt bereits selbst eine andere Einheit an und kann daher " +
                   $"nicht gleichzeitig als Ziel dienen.";

        leader.AttachedToUnitId = target.Id;
        armyRepo.SaveChanges();

        return $"✅ **{leader.Name}** (#{leader_unit_index}) führt jetzt **{target.Name}** (#{target_unit_index}) an.\n\n" +
               "Im PDF-Export (`export_army_pdf`) erscheinen beide als ein gemeinsamer Block.";
    }

    // ── Tool: Leader-Zuordnung entfernen ───────────────────────────────────────

    [McpServerTool, Description("Entfernt die Anführer-Zuordnung eines Leaders (siehe attach_leader).")]
    public string detach_leader(
        [Description("Name der Army")] string army_name,
        [Description("Index des Leaders (1-basiert, siehe show_army)")] int leader_unit_index)
    {
        var army = armyRepo.Get(army_name);
        if (army == null) return $"Army '{army_name}' nicht gefunden.";

        if (leader_unit_index < 1 || leader_unit_index > army.Units.Count)
            return $"Ungültiger Index {leader_unit_index}. Die Army hat {army.Units.Count} Einheiten.";

        var leader = army.Units[leader_unit_index - 1];
        if (leader.AttachedToUnitId == null)
            return $"**{leader.Name}** führt aktuell keine Einheit an.";

        leader.AttachedToUnitId = null;
        armyRepo.SaveChanges();

        return $"✅ **{leader.Name}** führt keine Einheit mehr an.";
    }

    // ── Tool: Enhancement anhängen ─────────────────────────────────────────────

    [McpServerTool, Description(
        "Hängt eine Enhancement an eine Einheit in der Army-Liste (max. 1 pro Einheit). " +
        "Die Army braucht dafür ein gesetztes Detachment (siehe set_detachment/create_army). " +
        "Die Punktekosten werden automatisch zum Army-Gesamtpunktestand addiert. " +
        "Beispiel: add_enhancement('Meine Votann', 1, 'Voidstrider')")]
    public string add_enhancement(
        [Description("Name der Army")] string army_name,
        [Description("Index der Einheit, die die Enhancement tragen soll (1-basiert, siehe show_army)")] int unit_index,
        [Description("Name der Enhancement, siehe list_enhancements()")] string enhancement_name)
    {
        var army = armyRepo.Get(army_name);
        if (army == null) return $"Army '{army_name}' nicht gefunden.";

        if (string.IsNullOrEmpty(army.Detachment))
            return $"Für '{army_name}' ist noch kein Detachment gesetzt. " +
                   $"Nutze zuerst `set_detachment('{army_name}', 'Detachment-Name')`.";

        if (unit_index < 1 || unit_index > army.Units.Count)
            return $"Ungültiger Index {unit_index}. Die Army hat {army.Units.Count} Einheiten.";

        var unit = army.Units[unit_index - 1];
        if (!string.IsNullOrEmpty(unit.EnhancementName))
            return $"**{unit.Name}** trägt bereits die Enhancement **{unit.EnhancementName}**. " +
                   $"Erst mit `remove_enhancement('{army_name}', {unit_index})` entfernen.";

        var enhancement = repo.FindEnhancement(army.FactionId, enhancement_name, army.Detachment);
        if (enhancement == null)
            return $"Enhancement '{enhancement_name}' nicht im Detachment '{army.Detachment}' gefunden. " +
                   $"Siehe `list_enhancements('{army.FactionName}', detachment: '{army.Detachment}')`.";

        unit.EnhancementName = enhancement.Name;
        unit.EnhancementCost = enhancement.Cost;
        unit.Points += enhancement.Cost;
        armyRepo.SaveChanges();

        var sb = new StringBuilder();
        sb.AppendLine($"✅ **{enhancement.Name}** ({enhancement.Cost} Punkte) an **{unit.Name}** angehängt.");
        sb.AppendLine();
        sb.AppendLine($"**{army.Name}:** {army.TotalPoints} / {army.PointsLimit} Punkte " +
                      $"({army.PointsLimit - army.TotalPoints} verbleibend)");
        return sb.ToString();
    }

    // ── Tool: Enhancement entfernen ────────────────────────────────────────────

    [McpServerTool, Description("Entfernt die Enhancement einer Einheit in der Army-Liste (falls vorhanden).")]
    public string remove_enhancement(
        [Description("Name der Army")] string army_name,
        [Description("Index der Einheit (1-basiert, siehe show_army)")] int unit_index)
    {
        var army = armyRepo.Get(army_name);
        if (army == null) return $"Army '{army_name}' nicht gefunden.";

        if (unit_index < 1 || unit_index > army.Units.Count)
            return $"Ungültiger Index {unit_index}. Die Army hat {army.Units.Count} Einheiten.";

        var unit = army.Units[unit_index - 1];
        if (string.IsNullOrEmpty(unit.EnhancementName))
            return $"**{unit.Name}** trägt keine Enhancement.";

        var removedName = unit.EnhancementName;
        unit.Points -= unit.EnhancementCost;
        unit.EnhancementName = "";
        unit.EnhancementCost = 0;
        armyRepo.SaveChanges();

        return $"✅ Enhancement **{removedName}** von **{unit.Name}** entfernt.\n\n" +
               $"**{army.Name}:** {army.TotalPoints} / {army.PointsLimit} Punkte";
    }

    // ── Tool: Army anzeigen ───────────────────────────────────────────────────

    [McpServerTool, Description(
        "Zeigt die aktuelle Army-Liste mit allen Einheiten und Punkten. " +
        "Beispiel: show_army('Meine Votann')")]
    public string show_army(
        [Description("Name der Army")] string army_name)
    {
        var army = armyRepo.Get(army_name);
        if (army == null)
            return $"Army '{army_name}' nicht gefunden. Verfügbare Armies: {string.Join(", ", armyRepo.All().Keys)}";

        var sb = new StringBuilder();
        sb.AppendLine($"# ⚒️ {army.Name}");
        sb.AppendLine($"**Fraktion:** {army.FactionName}  |  " +
                      $"**Detachment:** {(string.IsNullOrEmpty(army.Detachment) ? "– (siehe set_detachment())" : army.Detachment)}  |  " +
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

        var indexById = army.Units
            .Select((u, i) => (u.Id, Index: i + 1))
            .ToDictionary(x => x.Id, x => x.Index);

        sb.AppendLine("| # | Einheit | Modelle | Punkte | Enhancement | Führt an |");
        sb.AppendLine("|---|---------|---------|--------|-------------|----------|");
        for (int i = 0; i < army.Units.Count; i++)
        {
            var u = army.Units[i];
            var enhancementCell = string.IsNullOrEmpty(u.EnhancementName)
                ? "–"
                : $"{u.EnhancementName} (+{u.EnhancementCost})";
            var leadsCell = u.AttachedToUnitId != null && indexById.TryGetValue(u.AttachedToUnitId, out var targetIdx)
                ? $"#{targetIdx}"
                : "–";
            sb.AppendLine($"| {i + 1} | {u.Name} | {u.ModelCount} | {u.Points} | {enhancementCell} | {leadsCell} |");
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
        if (armyRepo.Count() == 0)
            return "Noch keine Armies erstellt. Nutze `create_army()`.";

        var sb = new StringBuilder();
        sb.AppendLine("# Gespeicherte Armies");
        sb.AppendLine();
        foreach (var (name, army) in armyRepo.All())
        {
            var status = army.TotalPoints <= army.PointsLimit ? "✅" : "❌";
            sb.AppendLine($"- **{name}** — {army.FactionName} — " +
                          $"{army.TotalPoints}/{army.PointsLimit} Punkte {status} — " +
                          $"{army.Units.Count} Einheiten");
        }
        return sb.ToString();
    }

    // ── Tool: Army als PDF exportieren ────────────────────────────────────────

    [McpServerTool, Description(
        "Exportiert eine Army-Liste als PDF im Stil von New Recruit: Roster-Übersichtsseite " +
        "gefolgt von einer Datasheet-Detailseite (Stats, Fähigkeiten, Waffen) pro Einheitentyp. " +
        "Hinweis: Der Army Builder trackt keine individuellen Wargear-Auswahlen pro Modell und " +
        "keine Leader-Zuordnung — Detailseiten zeigen daher das vollständige Referenz-Datasheet " +
        "statt einer konkreten Ausrüstungsauswahl, und angeführte Einheiten werden nicht zu einem " +
        "kombinierten Block zusammengefasst. Beispiel: export_army_pdf('Meine Votann')")]
    public async Task<string> export_army_pdf(
        [Description("Name der Army")] string army_name,
        [Description("Zielpfad für die PDF-Datei (optional). Standard: <Waha40kMcp-Datenverzeichnis>\\armies\\exports\\<ArmyName>.pdf")] string? output_path = null)
    {
        var army = armyRepo.Get(army_name);
        if (army == null) return $"Army '{army_name}' nicht gefunden.";

        if (army.Units.Count == 0)
            return $"Army '{army_name}' hat noch keine Einheiten. Nutze zuerst `add_unit()`.";

        var path = output_path ?? Path.Combine(armyRepo.ExportsDir, $"{SanitizeFileName(army_name)}.pdf");

        try
        {
            var html = ArmyPdfExporter.BuildHtml(army, repo);
            await ArmyPdfExporter.RenderToPdfAsync(html, path);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[ArmyBuilder] PDF-Export fehlgeschlagen: {ex}");
            return $"❌ PDF-Export fehlgeschlagen: {ex.Message}";
        }

        return $"✅ PDF exportiert: `{path}`\n\n" +
               $"{army.Units.Count} Einheiten, {army.TotalPoints}/{army.PointsLimit} Punkte.";
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

    internal static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return sanitized.Length > 0 ? sanitized : "army";
    }

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