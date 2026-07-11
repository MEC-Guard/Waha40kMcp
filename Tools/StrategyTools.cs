using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using Waha40kMcp.Data;
using Waha40kMcp.Models;

namespace Waha40kMcp.Tools;

/// <summary>
/// Strategie-Wissensbasis: speichert taktische Tipps, die Claude aus Webartikeln,
/// Tournament-Reports oder eigenen Battle-Report-Notizen extrahiert hat.
///
/// Zwei Workflows:
/// 1. Proaktiv: request_strategy_research() liefert Claude eine Anleitung, automatisch
///    mehrere aktuelle Quellen (web_search + web_fetch) zu einer Fraktion/einem Matchup
///    zu finden und auszuwerten — Claude führt die eigentliche Suche selbst aus, da der
///    Server keinen Internetzugriff hat.
/// 2. Reaktiv: Nutzer schickt einen Link, Claude liest ihn (web_fetch) und speichert direkt.
///
/// In beiden Fällen gilt: Claude liest die Quelle selbst, fasst paraphrasiert zusammen
/// (NIE wörtliche Zitate) und ruft dann save_strategy_note mit den strukturierten Feldern auf.
/// </summary>
[McpServerToolType]
public class StrategyTools(StrategyRepository strategyRepo)
{
    [McpServerTool, Description(
        "Speichert einen taktischen Tipp in der Strategie-Wissensbasis. " +
        "Claude sollte dies nach dem Lesen eines Artikels/Battle-Reports mit eigenen, " +
        "paraphrasierten Worten aufrufen (NICHT wörtliche Zitate aus der Quelle). " +
        "Beispiel: save_strategy_note(title: 'Hekaton früh vorrücken', faction: 'Leagues of Votann', " +
        "opponent_faction: 'Space Marines', tip: 'Gegen Gunline-Listen lohnt es sich, den Hekaton " +
        "in Runde 1 aggressiv vorzurücken um Deckung zu erzwingen.', source: 'https://...', " +
        "published_date: '2026-05-01')")]
    public string save_strategy_note(
        [Description("Kurzer, prägnanter Titel der Notiz")] string title,
        [Description("Der taktische Tipp in eigenen Worten (paraphrasiert, keine wörtlichen Zitate)")] string tip,
        [Description("Eigene Fraktion auf die sich der Tipp bezieht (optional, leer = allgemein)")] string faction = "",
        [Description("Gegnerische Fraktion im Matchup (optional, leer = fraktionsübergreifend)")] string opponent_faction = "",
        [Description("Missionstyp/Format falls relevant, z.B. 'Crucible of Battle' (optional)")] string mission = "",
        [Description("Betroffene Einheit falls einheitenspezifisch (optional)")] string unit = "",
        [Description("Kommagetrennte Tags zum späteren Filtern, z.B. 'deployment,alpha-strike'")] string tags = "",
        [Description("Quelle: URL oder Bezeichnung (z.B. Artikel-Titel + Autor)")] string source = "",
        [Description("Veröffentlichungsdatum der Quelle im Format YYYY-MM-DD, falls bekannt (optional, aber empfohlen für Aktualitäts-Filterung)")] string? published_date = null)
    {
        DateTime? parsedDate = null;
        if (!string.IsNullOrWhiteSpace(published_date) &&
            DateTime.TryParse(published_date, out var d))
            parsedDate = d;

        var note = new StrategyNote
        {
            Title = title,
            Tip = tip,
            Faction = faction,
            OpponentFaction = opponent_faction,
            Mission = mission,
            Unit = unit,
            Tags = tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
            Source = source,
            PublishedDate = parsedDate,
        };

        strategyRepo.Add(note);

        return $"✅ Strategie-Notiz **{title}** gespeichert (ID: {note.Id}).";
    }

    [McpServerTool, Description(
        "Bereitet eine automatische Strategie-Recherche vor. Gibt Claude eine strukturierte " +
        "Checkliste zurück, der Claude unmittelbar danach folgen MUSS: mehrere aktuelle Quellen " +
        "(web_search, dann web_fetch je Treffer) zu Matchup/Fraktion suchen, NUR Quellen jünger als " +
        "den angegebenen Stichtag verwenden, daraus paraphrasierte Tipps extrahieren und jeden " +
        "einzeln per save_strategy_note() inkl. published_date speichern. " +
        "Dies ist KEIN Web-Suche-Tool selbst — es liefert nur die Anleitung/Parameter, die Recherche " +
        "führt Claude direkt im Anschluss mit seinen eigenen Such-Tools aus. " +
        "Beispiel: request_strategy_research('Leagues of Votann', 'Space Marines')")]
    public string request_strategy_research(
        [Description("Eigene Fraktion, zu der recherchiert werden soll")] string faction,
        [Description("Gegnerische Fraktion für ein spezifisches Matchup (optional, leer = allgemeine Taktik)")] string opponent_faction = "",
        [Description("Maximales Alter der Quellen in Tagen (Standard 60 = ca. 2 Monate)")] int max_age_days = 60,
        [Description("Wie viele unterschiedliche Quellen mindestens ausgewertet werden sollen (Standard 3)")] int min_sources = 3)
    {
        var cutoff = DateTime.UtcNow.AddDays(-max_age_days);
        var matchupText = string.IsNullOrWhiteSpace(opponent_faction)
            ? $"allgemeine Taktik/Listenbau für {faction}"
            : $"das Matchup {faction} gegen {opponent_faction}";

        var sb = new StringBuilder();
        sb.AppendLine($"# 🔍 Strategie-Recherche-Auftrag: {matchupText}");
        sb.AppendLine();
        sb.AppendLine($"**Stichtag:** nur Quellen veröffentlicht nach **{cutoff:yyyy-MM-dd}** verwenden " +
                       $"(max. {max_age_days} Tage alt).");
        sb.AppendLine($"**Mindestanzahl Quellen:** {min_sources}");
        sb.AppendLine();
        sb.AppendLine("## Auszuführende Schritte (für Claude, jetzt direkt im Anschluss):");
        sb.AppendLine($"1. Mehrere `web_search` Anfragen zu {matchupText} (z.B. Tournament-Reports, " +
                       "Tactics-Artikel, Meta-Analysen, Reddit/Discord-Zusammenfassungen, YouTube-Battle-Report-Transkripte).");
        sb.AppendLine("2. Suchergebnisse nach Veröffentlichungsdatum filtern — alles vor dem Stichtag verwerfen.");
        sb.AppendLine($"3. Mindestens {min_sources} unterschiedliche, aktuelle Quellen mit `web_fetch` öffnen und lesen.");
        sb.AppendLine("4. Aus jeder Quelle die wichtigsten taktischen Erkenntnisse paraphrasieren " +
                       "(NIEMALS wörtlich zitieren) — z.B. Deployment-Tipps, starke/schwache Matchup-Einheiten, " +
                       "Mission-spezifische Hinweise, empfohlene Listen-Anpassungen.");
        sb.AppendLine($"5. Jede Erkenntnis einzeln per `save_strategy_note(faction: \"{faction}\", " +
                       $"opponent_faction: \"{opponent_faction}\", ..., published_date: \"<Datum der Quelle>\")` speichern.");
        sb.AppendLine("6. Am Ende dem Nutzer eine kurze Zusammenfassung der wichtigsten Punkte geben " +
                       "(nicht nur 'gespeichert', sondern die Kernaussagen).");
        sb.AppendLine();
        sb.AppendLine("⚠️ Falls keine ausreichend aktuellen Quellen gefunden werden: dem Nutzer ehrlich sagen, " +
                       "dass die Datenlage für diesen Zeitraum dünn ist, statt veraltete Quellen als aktuell auszugeben.");

        return sb.ToString();
    }

    [McpServerTool, Description(
        "Durchsucht die Strategie-Wissensbasis nach taktischen Tipps. " +
        "Filter sind alle optional und kombinierbar — leer lassen für alle Notizen. " +
        "Beispiel: search_strategy_notes(faction: 'Leagues of Votann', opponent_faction: 'Space Marines')")]
    public string search_strategy_notes(
        [Description("Eigene Fraktion filtern (optional)")] string? faction = null,
        [Description("Gegnerische Fraktion filtern (optional)")] string? opponent_faction = null,
        [Description("Mission/Format filtern (optional)")] string? mission = null,
        [Description("Einheit filtern (optional)")] string? unit = null,
        [Description("Freitext-Suche in Titel/Tipp (optional)")] string? keyword = null,
        [Description("Tag filtern (optional)")] string? tag = null,
        [Description("Nur Notizen aus Quellen, die maximal N Tage alt sind (optional, leer = alle)")] int? max_age_days = null)
    {
        var results = strategyRepo.Search(faction, opponent_faction, mission, unit, keyword, tag);

        if (max_age_days.HasValue)
        {
            var cutoff = DateTime.UtcNow.AddDays(-max_age_days.Value);
            results = results.Where(n => n.PublishedDate == null || n.PublishedDate >= cutoff).ToList();
        }

        if (results.Count == 0)
            return "Keine passenden Strategie-Notizen gefunden. " +
                   "Nutze request_strategy_research() um automatisch aktuelle Quellen zu finden, " +
                   "oder schick mir direkt einen Link zu einem Artikel/Battle-Report.";

        var sb = new StringBuilder();
        sb.AppendLine($"# 📚 Strategie-Wissensbasis — {results.Count} Treffer");
        sb.AppendLine();

        foreach (var n in results)
        {
            sb.AppendLine($"## {n.Title}");
            var meta = new List<string>();
            if (!string.IsNullOrEmpty(n.Faction)) meta.Add($"**Fraktion:** {n.Faction}");
            if (!string.IsNullOrEmpty(n.OpponentFaction)) meta.Add($"**Gegen:** {n.OpponentFaction}");
            if (!string.IsNullOrEmpty(n.Mission)) meta.Add($"**Mission:** {n.Mission}");
            if (!string.IsNullOrEmpty(n.Unit)) meta.Add($"**Einheit:** {n.Unit}");
            if (meta.Count > 0) sb.AppendLine(string.Join("  |  ", meta));
            sb.AppendLine();
            sb.AppendLine(n.Tip);
            if (n.Tags.Count > 0) sb.AppendLine($"*Tags: {string.Join(", ", n.Tags)}*");
            if (!string.IsNullOrEmpty(n.Source)) sb.AppendLine($"*Quelle: {n.Source}*");
            var dateInfo = n.PublishedDate.HasValue
                ? $"veröffentlicht: {n.PublishedDate:yyyy-MM-dd}"
                : "Veröffentlichungsdatum unbekannt";
            sb.AppendLine($"*ID: {n.Id} | {dateInfo} | hinzugefügt: {n.AddedUtc:yyyy-MM-dd}*");
            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    [McpServerTool, Description(
        "Löscht eine Strategie-Notiz anhand ihrer ID. " +
        "Beispiel: delete_strategy_note('a1b2c3d4')")]
    public string delete_strategy_note(
        [Description("ID der zu löschenden Notiz (siehe search_strategy_notes)")] string id)
    {
        var removed = strategyRepo.Remove(id);
        return removed
            ? $"✅ Notiz {id} gelöscht."
            : $"Keine Notiz mit ID '{id}' gefunden.";
    }

    [McpServerTool, Description(
        "Zeigt eine Übersicht der gesamten Strategie-Wissensbasis (Anzahl Notizen, " +
        "abgedeckte Fraktionen). Beispiel: list_strategy_overview()")]
    public string list_strategy_overview()
    {
        var all = strategyRepo.All();
        if (all.Count == 0)
            return "Die Strategie-Wissensbasis ist noch leer. " +
                   "Schick mir einen Link zu einem Tabletop-Artikel oder Battle-Report, dann lese ich ihn ein.";

        var sb = new StringBuilder();
        sb.AppendLine($"# 📚 Strategie-Wissensbasis — Übersicht");
        sb.AppendLine($"**Gesamt: {all.Count} Notizen**");
        sb.AppendLine();

        var byFaction = all
            .Where(n => !string.IsNullOrEmpty(n.Faction))
            .GroupBy(n => n.Faction)
            .OrderByDescending(g => g.Count());

        sb.AppendLine("## Nach Fraktion");
        foreach (var g in byFaction)
            sb.AppendLine($"- **{g.Key}**: {g.Count()} Notizen");

        var generalCount = all.Count(n => string.IsNullOrEmpty(n.Faction));
        if (generalCount > 0)
            sb.AppendLine($"- **Allgemein**: {generalCount} Notizen");

        sb.AppendLine();
        sb.AppendLine("## Neueste Einträge");
        foreach (var n in all.Take(5))
            sb.AppendLine($"- {n.Title} ({n.AddedUtc:yyyy-MM-dd})");

        return sb.ToString();
    }
}
