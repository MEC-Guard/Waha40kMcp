using System.Net;
using System.Text;
using Microsoft.Playwright;
using Waha40kMcp.Models;

namespace Waha40kMcp.Data;

/// <summary>
/// Exportiert eine Army-Liste als PDF, angelehnt an das Layout von New Recruit
/// (https://www.newrecruit.eu): eine Roster-Übersichtsseite gefolgt von einer
/// Datasheet-Detailseite pro Einheitentyp.
///
/// Hinweis zur Genauigkeit: Anders als New Recruit trackt unser Army Builder keine
/// individuellen Wargear-/Waffenauswahlen pro Modell und auch nicht, welcher Character
/// welche Einheit anführt. Die "Options"-Spalte im Roster zeigt daher nur die angehängte
/// Enhancement (falls vorhanden), und Detailseiten werden nicht für angeführte Einheiten
/// zusammengeführt oder bei mehreren Kopien dedupliziert — jede Einheit bekommt ihre eigene
/// Seite mit dem vollständigen Datasheet-Regeltext (Stats, Fähigkeiten, Waffen) als Referenz.
/// </summary>
public static class ArmyPdfExporter
{
    /// <summary>Baut das HTML-Dokument für die Army. Reine, testbare Funktion ohne Browser-Abhängigkeit.</summary>
    internal static string BuildHtml(ArmyList army, WahapediaRepository repo)
    {
        var sb = new StringBuilder();
        sb.Append("""
            <!DOCTYPE html>
            <html><head><meta charset="utf-8"><style>
            body { font-family: Arial, Helvetica, sans-serif; font-size: 11px; color: #1a1a1a; margin: 0; }
            .page { padding: 24px 28px; }
            h1 { font-size: 18px; margin: 0 0 4px 0; }
            .meta { font-size: 12px; color: #333; margin-bottom: 14px; }
            table { width: 100%; border-collapse: collapse; margin-bottom: 10px; break-inside: avoid; }
            th, td { border: 1px solid #999; padding: 4px 7px; text-align: left; vertical-align: top; }
            thead.bar th { background: #5c7a94; color: #fff; font-size: 13px; padding: 6px 8px; }
            .section-label { background: #e4e4e4; font-weight: bold; width: 110px; }
            .roster th { background: #e4e4e4; }
            .unit-block { page-break-after: always; }
            .unit-block:last-child { page-break-after: auto; }
            .unit-header { background: #5c7a94; color: #fff; padding: 6px 8px; font-weight: bold;
                           font-size: 13px; display: flex; justify-content: space-between; }
            .subhead { background: #e4e4e4; font-weight: bold; padding: 3px 7px; border: 1px solid #999;
                       border-bottom: none; }
            .copies-note { font-size: 10px; color: #555; font-style: italic; margin: 4px 0 10px 0; }
            </style></head><body>
            """);

        // ── Seite 1: Roster-Übersicht ────────────────────────────────────────
        sb.Append("<div class=\"page\">");
        sb.Append($"<h1>{Html(army.Name)}</h1>");
        sb.Append($"<div class=\"meta\"><b>{Html(army.FactionName)}</b>");
        if (!string.IsNullOrEmpty(army.Detachment)) sb.Append($" — {Html(army.Detachment)}");
        sb.Append($" — {army.TotalPoints} / {army.PointsLimit} pts</div>");

        sb.Append("<table class=\"roster\"><tr><th>NAME</th><th>ROLE</th><th>PTS</th><th>OPTIONS</th></tr>");
        foreach (var u in army.Units)
        {
            var ds = repo.Datasheets.GetValueOrDefault(u.DatasheetId);
            var role = ds?.Datasheettype ?? "";
            var options = string.IsNullOrEmpty(u.EnhancementName) ? "" : $"{u.EnhancementName} (+{u.EnhancementCost})";
            sb.Append("<tr>");
            sb.Append($"<td>({u.ModelCount}) {Html(u.Name)}</td>");
            sb.Append($"<td>{Html(role)}</td>");
            sb.Append($"<td>{u.Points}</td>");
            sb.Append($"<td>{Html(options)}</td>");
            sb.Append("</tr>");
        }
        sb.Append("</table>");
        sb.Append("</div>");

        // ── Detailseiten: eine pro Einheitentyp (Datasheet), dedupliziert ────
        var unitsByDatasheet = army.Units
            .GroupBy(u => u.DatasheetId)
            .Select(g => new { First = g.First(), Count = g.Count() });

        foreach (var group in unitsByDatasheet)
        {
            var ds = repo.Datasheets.GetValueOrDefault(group.First.DatasheetId);
            if (ds == null) continue; // Datasheet nicht (mehr) in der geladenen Wahapedia-Datenbank

            sb.Append("<div class=\"page unit-block\">");
            sb.Append($"<div class=\"unit-header\"><span>{group.First.Points} PTS</span>" +
                      $"<span>{Html(ds.Name.ToUpperInvariant())}</span></div>");
            if (group.Count > 1)
                sb.Append($"<p class=\"copies-note\">{group.Count}x in dieser Liste enthalten " +
                          $"(je eigene Punktekosten/Enhancement, siehe Roster-Seite).</p>");

            // Unit-Stats
            if (ds.Models.Count > 0)
            {
                sb.Append("<div class=\"subhead\">Unit</div>");
                sb.Append("<table><tr><th>Model</th><th>M</th><th>T</th><th>Sv</th><th>W</th><th>LD</th><th>OC</th><th>InvSv</th></tr>");
                foreach (var m in ds.Models)
                    sb.Append($"<tr><td>{Html(m.Name)}</td><td>{Html(m.M)}</td><td>{Html(m.T)}</td>" +
                              $"<td>{Html(m.Sv)}</td><td>{Html(m.W)}</td><td>{Html(m.Ld)}</td>" +
                              $"<td>{Html(m.Oc)}</td><td>{Html(m.InvSv)}</td></tr>");
                sb.Append("</table>");
            }

            // Fähigkeiten
            var abilities = ds.Abilities.Where(a => !string.IsNullOrEmpty(a.Name)).ToList();
            if (abilities.Count > 0)
            {
                sb.Append("<div class=\"subhead\">Abilities</div>");
                sb.Append("<table><tr><th style=\"width:140px\">Name</th><th>Description</th></tr>");
                foreach (var a in abilities)
                    sb.Append($"<tr><td>{Html(a.Name)}</td><td>{Html(a.Description)}</td></tr>");
                sb.Append("</table>");
            }

            // Nahkampfwaffen
            var melee = ds.Weapons.Where(w => w.Type.Equals("melee", StringComparison.OrdinalIgnoreCase)).ToList();
            if (melee.Count > 0)
            {
                sb.Append("<div class=\"subhead\">Melee Weapons</div>");
                sb.Append("<table><tr><th>Name</th><th>A</th><th>WS</th><th>S</th><th>AP</th><th>D</th><th>Keywords</th></tr>");
                foreach (var w in melee)
                    sb.Append($"<tr><td>{Html(w.Name)}</td><td>{Html(w.A)}</td><td>{Html(w.BsWs)}</td>" +
                              $"<td>{Html(w.S)}</td><td>{Html(w.Ap)}</td><td>{Html(w.D)}</td><td>{Html(w.Keywords)}</td></tr>");
                sb.Append("</table>");
            }

            // Fernkampfwaffen
            var ranged = ds.Weapons.Where(w => w.Type.Equals("ranged", StringComparison.OrdinalIgnoreCase)).ToList();
            if (ranged.Count > 0)
            {
                sb.Append("<div class=\"subhead\">Ranged Weapons</div>");
                sb.Append("<table><tr><th>Name</th><th>Range</th><th>A</th><th>BS</th><th>S</th><th>AP</th><th>D</th><th>Keywords</th></tr>");
                foreach (var w in ranged)
                    sb.Append($"<tr><td>{Html(w.Name)}</td><td>{Html(w.Range)}</td><td>{Html(w.A)}</td>" +
                              $"<td>{Html(w.BsWs)}</td><td>{Html(w.S)}</td><td>{Html(w.Ap)}</td><td>{Html(w.D)}</td><td>{Html(w.Keywords)}</td></tr>");
                sb.Append("</table>");
            }

            // Categories (Datasheettype + Keywords + Faction, dedupliziert — Wahapedia-Daten
            // überlappen sich hier häufig, z.B. Datasheettype "Battleline" steckt oft schon in Keywords)
            var categoryTokens = new[] { ds.Datasheettype }
                .Concat(ds.Keywords.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                .Where(t => !string.IsNullOrEmpty(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Append($"Faction: {ds.FactionName}");
            sb.Append("<table><tr><td class=\"section-label\">Categories</td><td>" +
                      $"{Html(string.Join(", ", categoryTokens))}</td></tr></table>");

            sb.Append("</div>");
        }

        sb.Append("</body></html>");
        return sb.ToString();
    }

    private static string Html(string? s) => WebUtility.HtmlEncode(s ?? "");

    /// <summary>Rendert HTML per Playwright/Chromium zu einer PDF-Datei (druckt Hintergrundfarben mit).</summary>
    internal static async Task RenderToPdfAsync(string html, string outputPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync();
        await page.SetContentAsync(html, new() { WaitUntil = WaitUntilState.NetworkIdle });
        await page.PdfAsync(new PagePdfOptions
        {
            Path = outputPath,
            Format = "A4",
            PrintBackground = true,
            Margin = new Margin { Top = "12mm", Bottom = "12mm", Left = "10mm", Right = "10mm" },
        });
    }
}
