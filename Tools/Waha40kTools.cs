using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using Waha40kMcp.Data;
using Waha40kMcp.Models;

namespace Waha40kMcp.Tools;

[McpServerToolType]
public class Waha40kTools(WahapediaRepository repo)
{
    // ── 1. Datasheet nachschlagen ─────────────────────────────────────────────

    [McpServerTool, Description(
        "Sucht eine Einheit (Datasheet) nach Name. Gibt Stats, Waffen und Fähigkeiten zurück. " +
        "Beispiel: get_datasheet('Space Marine Intercessors')")]
    public string get_datasheet(
        [Description("Name der Einheit, z.B. 'Intercessor Squad', 'Ork Boyz', 'Necron Warriors'")] string unit_name,
        [Description("Optional: Fraktion eingrenzen, z.B. 'Space Marines', 'Necrons'")] string? faction = null)
    {
        string? factionId = null;
        if (faction != null)
        {
            var f = repo.FindFaction(faction);
            if (f != null) factionId = f.Id;
        }

        var results = repo.SearchDatasheets(unit_name, factionId).Take(3).ToList();

        if (results.Count == 0)
            return $"Keine Einheit gefunden für '{unit_name}'" +
                   (faction != null ? $" in Fraktion '{faction}'" : "") + ".";

        var sb = new StringBuilder();
        foreach (var ds in results)
        {
            sb.AppendLine($"# {ds.Name}");
            sb.AppendLine($"**Fraktion:** {ds.FactionName}  |  **Typ:** {ds.Datasheettype}");
            if (!string.IsNullOrEmpty(ds.Keywords))
                sb.AppendLine($"**Keywords:** {ds.Keywords}");
            if (ds.PointsCosts.Count > 0)
            {
                var pts = string.Join(" / ", ds.PointsCosts.Select(p =>
                    string.IsNullOrEmpty(p.Description) ? $"{p.Cost}" : $"{p.Description}: {p.Cost}"));
                sb.AppendLine($"**Punkte:** {pts}");
            }
            sb.AppendLine();

            // Stats
            if (ds.Models.Count > 0)
            {
                sb.AppendLine("## Stats");
                sb.AppendLine("| Modell | M | T | SV | W | LD | OC | InvSv |");
                sb.AppendLine("|--------|---|---|----|---|----|----|-------|");
                foreach (var m in ds.Models)
                    sb.AppendLine($"| {m.Name} | {m.M} | {m.T} | {m.Sv} | {m.W} | {m.Ld} | {m.Oc} | {m.InvSv} |");
                sb.AppendLine();
            }

            // Waffen
            if (ds.Weapons.Count > 0)
            {
                sb.AppendLine("## Waffen");
                foreach (var type in new[] { "ranged", "melee" })
                {
                    var weapons = ds.Weapons.Where(w => w.Type.Equals(type, StringComparison.OrdinalIgnoreCase)).ToList();
                    if (weapons.Count == 0) continue;
                    sb.AppendLine(type == "ranged" ? "### Fernkampf" : "### Nahkampf");
                    sb.AppendLine("| Name | Range | A | BS/WS | S | AP | D | Keywords |");
                    sb.AppendLine("|------|-------|---|-------|---|----|---|----------|");
                    foreach (var w in weapons)
                        sb.AppendLine($"| {w.Name} | {w.Range} | {w.A} | {w.BsWs} | {w.S} | {w.Ap} | {w.D} | {w.Keywords} |");
                    sb.AppendLine();
                }
            }

            // Fähigkeiten
            if (ds.Abilities.Count > 0)
            {
                sb.AppendLine("## Fähigkeiten");
                foreach (var a in ds.Abilities.Where(a => !string.IsNullOrEmpty(a.Name)))
                    sb.AppendLine($"- **{a.Name}** ({a.Type}): {a.Description}");
                sb.AppendLine();
            }

            // Optionen
            if (ds.Options.Count > 0)
            {
                sb.AppendLine("## Ausrüstungsoptionen");
                foreach (var o in ds.Options)
                    sb.AppendLine($"- {o}");
                sb.AppendLine();
            }

            sb.AppendLine($"[Wahapedia]({ds.Link})");
            sb.AppendLine("---");
        }

        return sb.ToString();
    }

    // ── 2. Einheiten einer Fraktion auflisten ─────────────────────────────────

    [McpServerTool, Description(
        "Listet alle Einheiten einer Fraktion auf, optional gefiltert nach Keyword. " +
        "Beispiel: list_faction_units('Space Marines')")]
    public string list_faction_units(
        [Description("Fraktionsname, z.B. 'Space Marines', 'Necrons', 'Orks', 'Tyranids'")] string faction,
        [Description("Optional: Keyword-Filter, z.B. 'Infantry', 'Vehicle', 'Character'")] string? keyword_filter = null)
    {
        var f = repo.FindFaction(faction);
        if (f == null)
        {
            var similar = repo.Factions.Values
                .Where(x => x.Name.Contains(faction, StringComparison.OrdinalIgnoreCase))
                .Select(x => x.Name).Take(5);
            return $"Fraktion '{faction}' nicht gefunden. Ähnliche: {string.Join(", ", similar)}";
        }

        var units = repo.Datasheets.Values
            .Where(d => d.FactionId == f.Id &&
                        (keyword_filter == null ||
                         d.Keywords.Contains(keyword_filter, StringComparison.OrdinalIgnoreCase) ||
                         d.Datasheettype.Contains(keyword_filter, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(d => d.Name)
            .ToList();

        if (units.Count == 0)
            return $"Keine Einheiten gefunden für '{faction}'" +
                   (keyword_filter != null ? $" mit Keyword '{keyword_filter}'" : "") + ".";

        var sb = new StringBuilder();
        sb.AppendLine($"# {f.Name} — {units.Count} Einheiten");
        if (keyword_filter != null) sb.AppendLine($"*Filter: {keyword_filter}*");
        sb.AppendLine();
        sb.AppendLine("| Einheit | Typ | Punkte |");
        sb.AppendLine("|---------|-----|--------|");
        foreach (var d in units)
        {
            var ptsDisplay = d.PointsCosts.Count switch
            {
                0 => "–",
                1 => d.PointsCosts[0].Cost.ToString(),
                _ => string.Join(" / ", d.PointsCosts.Select(p => p.Cost))
            };
            sb.AppendLine($"| {d.Name} | {d.Datasheettype} | {ptsDisplay} |");
        }

        return sb.ToString();
    }

    // ── 3. Stratagems nachschlagen ────────────────────────────────────────────

    [McpServerTool, Description(
        "Sucht Stratagems einer Fraktion, optional nach Phase oder Keyword gefiltert. " +
        "Beispiel: search_stratagems('Space Marines', phase: 'Shooting')")]
    public string search_stratagems(
        [Description("Fraktionsname, z.B. 'Space Marines', 'Necrons'")] string faction,
        [Description("Optional: Suchbegriff im Namen/Text des Stratagems")] string? query = null,
        [Description("Optional: Phase filtern — 'Shooting', 'Fight', 'Movement', 'Command'")] string? phase = null,
        [Description("Optional: Detachment-Name filtern")] string? detachment = null)
    {
        var f = repo.FindFaction(faction);
        if (f == null)
            return $"Fraktion '{faction}' nicht gefunden.";

        var results = repo.SearchStratagems(f.Id, query, phase)
            .Where(s => detachment == null ||
                        s.Detachment.Contains(detachment, StringComparison.OrdinalIgnoreCase))
            .Take(20)
            .ToList();

        if (results.Count == 0)
            return $"Keine Stratagems für '{f.Name}' gefunden" +
                   (query != null ? $" mit Suchbegriff '{query}'" : "") +
                   (phase != null ? $" in Phase '{phase}'" : "") + ".";

        var sb = new StringBuilder();
        sb.AppendLine($"# {f.Name} — Stratagems ({results.Count})");
        if (phase != null) sb.AppendLine($"*Phase: {phase}*");
        if (detachment != null) sb.AppendLine($"*Detachment: {detachment}*");
        sb.AppendLine();

        foreach (var s in results)
        {
            sb.AppendLine($"## {s.Name} [{s.CpCost} CP]");
            sb.AppendLine($"*{s.Type} — {s.Phase} — {s.Turn}*");
            if (!string.IsNullOrEmpty(s.Detachment))
                sb.AppendLine($"*Detachment: {s.Detachment}*");
            if (!string.IsNullOrEmpty(s.Legend))
                sb.AppendLine($"> {s.Legend}");
            sb.AppendLine();
            sb.AppendLine(s.Description);
            sb.AppendLine();
            sb.AppendLine("---");
        }

        return sb.ToString();
    }

    // ── 4. Alle Fraktionen auflisten ──────────────────────────────────────────

    [McpServerTool, Description("Listet alle verfügbaren Fraktionen auf.")]
    public string list_factions()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Warhammer 40K — Fraktionen");
        sb.AppendLine();
        foreach (var f in repo.Factions.Values.OrderBy(f => f.Name))
            sb.AppendLine($"- **{f.Name}** (ID: `{f.Id}`)");
        return sb.ToString();
    }

    // ── 5. Army Builder ───────────────────────────────────────────────────────

    [McpServerTool, Description(
        "Berechnet die Gesamtpunkte einer Army-Liste und prüft das Punktelimit. " +
        "Übergib eine kommagetrennte Liste von Einheitennamen.")]
    public string calculate_army_points(
        [Description("Kommagetrennte Einheitennamen, z.B. 'Intercessor Squad, Predator, Captain'")] string unit_names,
        [Description("Fraktion, z.B. 'Space Marines'")] string faction,
        [Description("Punktelimit (Standard: 2000)")] int points_limit = 2000)
    {
        var f = repo.FindFaction(faction);
        if (f == null) return $"Fraktion '{faction}' nicht gefunden.";

        var names = unit_names.Split(',').Select(n => n.Trim()).ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"# Army-Liste: {f.Name}");
        sb.AppendLine($"**Limit:** {points_limit} Punkte");
        sb.AppendLine();
        sb.AppendLine("| Einheit | Punkte |");
        sb.AppendLine("|---------|--------|");

        int total = 0;
        var notFound = new List<string>();

        foreach (var name in names)
        {
            var ds = repo.SearchDatasheets(name, f.Id).FirstOrDefault();
            if (ds == null)
            {
                notFound.Add(name);
                sb.AppendLine($"| ❓ {name} | ? |");
                continue;
            }

            var pts = ds.PointsCost ?? 0;
            var ptsSuffix = ds.PointsCosts.Count > 1
                ? $" ⚠️ (Staffelung: {string.Join("/", ds.PointsCosts.Select(p => p.Cost))})"
                : "";
            total += pts;
            sb.AppendLine($"| {ds.Name} | {pts}{ptsSuffix} |");
        }

        sb.AppendLine();
        var status = total <= points_limit ? "✅" : "❌ ÜBERSCHRITTEN";
        sb.AppendLine($"**Gesamt: {total} / {points_limit} Punkte {status}**");

        if (notFound.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"⚠️ Nicht gefunden: {string.Join(", ", notFound)}");
            sb.AppendLine("Tipp: Versuche `get_datasheet()` für genaue Namen.");
        }

        return sb.ToString();
    }

    // ── 6. Einheiten vergleichen ──────────────────────────────────────────────

    [McpServerTool, Description(
        "Vergleicht zwei Einheiten hinsichtlich Stats und Waffen. " +
        "Nützlich für Entscheidungen beim Army-Building.")]
    public string compare_units(
        [Description("Name der ersten Einheit")] string unit_a,
        [Description("Name der zweiten Einheit")] string unit_b)
    {
        var dsA = repo.SearchDatasheets(unit_a).FirstOrDefault();
        var dsB = repo.SearchDatasheets(unit_b).FirstOrDefault();

        if (dsA == null) return $"Einheit '{unit_a}' nicht gefunden.";
        if (dsB == null) return $"Einheit '{unit_b}' nicht gefunden.";

        var sb = new StringBuilder();
        sb.AppendLine($"# Vergleich: {dsA.Name} vs {dsB.Name}");
        sb.AppendLine();

        sb.AppendLine("## Stats");
        sb.AppendLine($"| Stat | {dsA.Name} | {dsB.Name} |");
        sb.AppendLine("|------|-----------|-----------|");

        var mA = dsA.Models.FirstOrDefault();
        var mB = dsB.Models.FirstOrDefault();
        if (mA != null && mB != null)
        {
            sb.AppendLine($"| M  | {mA.M}  | {mB.M}  |");
            sb.AppendLine($"| T  | {mA.T}  | {mB.T}  |");
            sb.AppendLine($"| SV | {mA.Sv} | {mB.Sv} |");
            sb.AppendLine($"| W  | {mA.W}  | {mB.W}  |");
            sb.AppendLine($"| LD | {mA.Ld} | {mB.Ld} |");
            sb.AppendLine($"| OC | {mA.Oc} | {mB.Oc} |");
            if (!string.IsNullOrEmpty(mA.InvSv) || !string.IsNullOrEmpty(mB.InvSv))
                sb.AppendLine($"| Inv| {mA.InvSv} | {mB.InvSv} |");
        }

        sb.AppendLine();
        sb.AppendLine($"**Punkte:** {dsA.Name} = {dsA.PointsCost ?? 0} pts | " +
                      $"{dsB.Name} = {dsB.PointsCost ?? 0} pts");

        return sb.ToString();
    }
}