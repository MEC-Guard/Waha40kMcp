using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using Waha40kMcp.Models;

namespace Waha40kMcp.Data;

/// <summary>
/// Lädt alle CSV-Dateien von Wahapedia und hält sie im Speicher.
/// Basis-URL: https://wahapedia.ru/wh40k10ed/
/// </summary>
public class WahapediaRepository
{
    private static readonly string BaseUrl = "https://wahapedia.ru/wh40k10ed/";
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Waha40kMcp", "cache");

    // In-memory Daten
    public Dictionary<string, Datasheet> Datasheets { get; private set; } = [];
    public Dictionary<string, Stratagem> Stratagems { get; private set; } = [];
    public Dictionary<string, Faction> Factions { get; private set; } = [];
    public Dictionary<string, DetachmentAbility> DetachmentAbilities { get; private set; } = [];
    public Dictionary<string, Enhancement> Enhancements { get; private set; } = [];

    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(30) };

    // ── Initialisierung ───────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(CacheDir);
        Console.Error.WriteLine("[Waha40k] Lade Daten von Wahapedia...");

        var factionRows      = await LoadCsvAsync("Factions.csv");
        var sourceRows       = await LoadCsvAsync("Source.csv");
        var sheetRows        = await LoadCsvAsync("Datasheets.csv");
        var keywordRows      = await LoadCsvAsync("Datasheets_keywords.csv");
        var modelRows        = await LoadCsvAsync("Datasheets_models.csv");
        var weaponRows       = await LoadCsvAsync("Datasheets_wargear.csv");
        var abilityRows      = await LoadCsvAsync("Datasheets_abilities.csv");
        var optionRows       = await LoadCsvAsync("Datasheets_options.csv");
        var stratagemRows    = await LoadCsvAsync("Stratagems.csv");
        var pointsRows       = await TryLoadCsvAsync("Datasheets_models_cost.csv", "Datasheets_points.csv");
        var detachmentRows   = await LoadCsvAsync("Detachment_abilities.csv");
        var enhancementRows  = await LoadCsvAsync("Enhancements.csv");

        BuildFactions(factionRows);
        BuildDatasheets(sheetRows, modelRows, weaponRows, abilityRows, optionRows, pointsRows, sourceRows, keywordRows);
        BuildStratagems(stratagemRows);
        BuildDetachmentAbilities(detachmentRows);
        BuildEnhancements(enhancementRows);

        Console.Error.WriteLine($"[Waha40k] Geladen: {Datasheets.Count} Datasheets, " +
                                $"{Stratagems.Count} Stratagems, {Factions.Count} Fraktionen, " +
                                $"{DetachmentAbilities.Count} Detachments, {Enhancements.Count} Enhancements.");

    }

    // ── CSV laden (mit lokalem Cache, 24h TTL) ────────────────────────────────

    private async Task<List<Dictionary<string, string>>> LoadCsvAsync(string filename)
    {
        var cachePath = Path.Combine(CacheDir, filename);
        string csv;

        if (File.Exists(cachePath) &&
            (DateTime.UtcNow - File.GetLastWriteTimeUtc(cachePath)).TotalHours < 24)
        {
            Console.Error.WriteLine($"[Waha40k] Cache: {filename}");
            csv = await File.ReadAllTextAsync(cachePath);
        }
        else
        {
            var url = BaseUrl + filename;
            Console.Error.WriteLine($"[Waha40k] Download: {url}");
            csv = await _http.GetStringAsync(url);
            await File.WriteAllTextAsync(cachePath, csv);
        }

        return ParseCsv(csv);
    }

    private async Task<List<Dictionary<string, string>>> TryLoadCsvAsync(params string[] filenames)
    {
        foreach (var filename in filenames)
        {
            try
            {
                var rows = await LoadCsvAsync(filename);
                if (rows.Count > 0)
                {
                    Console.Error.WriteLine($"[Waha40k] Punkte geladen aus: {filename} ({rows.Count} Einträge)");
                    return rows;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Waha40k] {filename} nicht gefunden: {ex.Message}");
            }
        }
        Console.Error.WriteLine("[Waha40k] Keine Punkte-CSV gefunden, Punkte werden aus Datasheets.csv genommen.");
        return [];
    }

    internal static List<Dictionary<string, string>> ParseCsv(string csv)
    {
        var result = new List<Dictionary<string, string>>();
        // Wahapedia nutzt | als Trennzeichen
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = "|",
            HasHeaderRecord = true,
            BadDataFound = null,
            MissingFieldFound = null,
        };

        using var reader = new StringReader(csv);
        using var csvReader = new CsvReader(reader, config);

        csvReader.Read();
        csvReader.ReadHeader();
        var headers = csvReader.HeaderRecord ?? [];

        while (csvReader.Read())
        {
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in headers)
                row[h] = csvReader.GetField(h) ?? "";
            result.Add(row);
        }

        return result;
    }

    // ── Builder-Methoden ──────────────────────────────────────────────────────

    internal void BuildFactions(List<Dictionary<string, string>> rows)
    {
        foreach (var r in rows)
        {
            var f = new Faction
            {
                Id   = r.GetValueOrDefault("id", ""),
                Name = r.GetValueOrDefault("name", ""),
                Link = r.GetValueOrDefault("link", ""),
            };
            if (!string.IsNullOrEmpty(f.Id))
                Factions[f.Id] = f;
        }
    }

    internal void BuildDatasheets(
        List<Dictionary<string, string>> sheetRows,
        List<Dictionary<string, string>> modelRows,
        List<Dictionary<string, string>> weaponRows,
        List<Dictionary<string, string>> abilityRows,
        List<Dictionary<string, string>> optionRows,
        List<Dictionary<string, string>> pointsRows,
        List<Dictionary<string, string>> sourceRows,
        List<Dictionary<string, string>> keywordRows)
    {
        // Echte Legends-Quellen ermitteln: Source.csv verzeichnet z.B. "Space Marines (Warhammer
        // Legends)" als eigene Quelle mit eigener id. Datasheets, deren source_id auf so eine
        // Quelle zeigt, sind echte (nicht im Matched Play erlaubte) Legends-Einheiten.
        // WICHTIG: Das Datasheets.csv-Feld "legend" ist dagegen KEIN Status-Flag, sondern schlicht
        // der Fluff-/Hintergrundtext der Einheit — den hat praktisch jedes reguläre Datasheet auch
        // (z.B. "Intercessor Squad"). Früher wurde fälschlich danach gefiltert, wodurch fast alle
        // Einheiten mit Fluff-Text verschwanden, siehe LegendText unten für die korrekte Nutzung.
        var legendsSourceIds = sourceRows
            .Where(r => r.GetValueOrDefault("name", "").Contains("(Warhammer Legends)", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.GetValueOrDefault("id", ""))
            .Where(sourceId => !string.IsNullOrEmpty(sourceId))
            .ToHashSet();

        // Basis-Datasheets
        foreach (var r in sheetRows)
        {
            var id = r.GetValueOrDefault("id", "");
            if (string.IsNullOrEmpty(id)) continue;

            // Legends-Einheiten komplett ignorieren — nicht im Matched Play.
            var sourceId = r.GetValueOrDefault("source_id", "");
            if (legendsSourceIds.Contains(sourceId)) continue;

            var factionId = r.GetValueOrDefault("faction_id", "");
            Factions.TryGetValue(factionId, out var faction);

            Datasheets[id] = new Datasheet
            {
                Id               = id,
                Name             = r.GetValueOrDefault("name", ""),
                FactionId        = factionId,
                FactionName      = faction?.Name ?? factionId,
                Link             = r.GetValueOrDefault("link", ""),
                // Keywords stehen nicht in Datasheets.csv, sondern in der separaten
                // Datasheets_keywords.csv — werden weiter unten befüllt.
                Datasheettype    = r.GetValueOrDefault("role", ""), // Spalte heißt "role", nicht "datasheettype"
                LegendText       = r.GetValueOrDefault("legend", ""), // Fluff-/Hintergrundtext
            };
        }

        // Keywords aus der separaten Datasheets_keywords.csv (datasheet_id, keyword, model, is_faction_keyword).
        // Modellspezifische Keywords (model-Feld gesetzt) werden ignoriert — wir zeigen nur
        // die für das ganze Datasheet geltenden Keywords.
        foreach (var r in keywordRows)
        {
            var dsId = r.GetValueOrDefault("datasheet_id", "");
            if (!Datasheets.TryGetValue(dsId, out var ds)) continue;
            if (!string.IsNullOrEmpty(r.GetValueOrDefault("model", ""))) continue;

            var keyword = r.GetValueOrDefault("keyword", "");
            if (string.IsNullOrEmpty(keyword)) continue;

            var isFactionKeyword = r.GetValueOrDefault("is_faction_keyword", "")
                .Equals("true", StringComparison.OrdinalIgnoreCase);

            if (isFactionKeyword)
                ds.FactionKeywords = ds.FactionKeywords.Length == 0 ? keyword : $"{ds.FactionKeywords}, {keyword}";
            else
                ds.Keywords = ds.Keywords.Length == 0 ? keyword : $"{ds.Keywords}, {keyword}";
        }

        // Models (Stats)
        foreach (var r in modelRows)
        {
            var dsId = r.GetValueOrDefault("datasheet_id", "");
            if (!Datasheets.TryGetValue(dsId, out var ds)) continue;

            ds.Models.Add(new DatasheetModel
            {
                DatasheetId = dsId,
                Name  = r.GetValueOrDefault("name", ""),
                M     = r.GetValueOrDefault("m", ""),
                T     = r.GetValueOrDefault("t", ""),
                Sv    = r.GetValueOrDefault("sv", ""),
                W     = r.GetValueOrDefault("w", ""),
                Ld    = r.GetValueOrDefault("ld", ""),
                Oc    = r.GetValueOrDefault("oc", ""),
                InvSv = r.GetValueOrDefault("inv_sv", ""),
            });
        }

        // Weapons
        foreach (var r in weaponRows)
        {
            var dsId = r.GetValueOrDefault("datasheet_id", "");
            if (!Datasheets.TryGetValue(dsId, out var ds)) continue;

            ds.Weapons.Add(new DatasheetWeapon
            {
                DatasheetId = dsId,
                WeaponId    = r.GetValueOrDefault("id", ""),
                Name        = r.GetValueOrDefault("name", ""),
                Type        = r.GetValueOrDefault("type", ""),
                Range       = r.GetValueOrDefault("range", ""),
                A           = r.GetValueOrDefault("a", ""),
                BsWs        = r.GetValueOrDefault("bs_ws", ""),
                S           = r.GetValueOrDefault("s", ""),
                Ap          = r.GetValueOrDefault("ap", ""),
                D           = r.GetValueOrDefault("d", ""),
                // Kein eigenes "keywords"-Feld in Datasheets_wargear.csv — die Waffen-Keywords
                // (z.B. "rapid fire 2") stehen im "description"-Feld.
                Keywords    = r.GetValueOrDefault("description", ""),
            });
        }

        // Abilities
        foreach (var r in abilityRows)
        {
            var dsId = r.GetValueOrDefault("datasheet_id", "");
            if (!Datasheets.TryGetValue(dsId, out var ds)) continue;

            var name = r.GetValueOrDefault("name", "");
            var desc = r.GetValueOrDefault("description", "");
            if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(desc)) continue;

            ds.Abilities.Add(new DatasheetAbility
            {
                DatasheetId = dsId,
                AbilityId   = r.GetValueOrDefault("ability_id", ""),
                Name        = name,
                Description = desc,
                Type        = r.GetValueOrDefault("type", ""),
            });
        }

        // Options
        foreach (var r in optionRows)
        {
            var dsId = r.GetValueOrDefault("datasheet_id", "");
            if (!Datasheets.TryGetValue(dsId, out var ds)) continue;
            var desc = r.GetValueOrDefault("description", "");
            if (!string.IsNullOrEmpty(desc))
                ds.Options.Add(desc);
        }

        // Points aus Datasheets_models_cost.csv (fields: datasheet_id, line, description, cost)
        // Pro Einheit können mehrere Zeilen existieren (Staffelung nach Modellanzahl)
        foreach (var r in pointsRows.OrderBy(r => r.GetValueOrDefault("line", "0")))
        {
            var dsId = r.GetValueOrDefault("datasheet_id", "");
            var cost = r.GetValueOrDefault("cost", "");
            var desc = r.GetValueOrDefault("description", "");
            if (!Datasheets.TryGetValue(dsId, out var ds)) continue;
            if (int.TryParse(cost, out var pts) && pts > 0)
                ds.PointsCosts.Add(new PointsCostEntry { Description = desc, Cost = pts });
        }
    }

    private void BuildStratagems(List<Dictionary<string, string>> rows)
    {
        foreach (var r in rows)
        {
            var id = r.GetValueOrDefault("id", "");
            if (string.IsNullOrEmpty(id)) continue;

            Stratagems[id] = new Stratagem
            {
                Id          = id,
                FactionId   = r.GetValueOrDefault("faction_id", ""),
                Name        = r.GetValueOrDefault("name", ""),
                Type        = r.GetValueOrDefault("type", ""),
                CpCost      = r.GetValueOrDefault("cp_cost", ""),
                Legend      = r.GetValueOrDefault("legend", ""),
                Turn        = r.GetValueOrDefault("turn", ""),
                Phase       = r.GetValueOrDefault("phase", ""),
                Description = r.GetValueOrDefault("description", ""),
                Detachment  = r.GetValueOrDefault("detachment", ""),
            };
        }
    }

    private void BuildDetachmentAbilities(List<Dictionary<string, string>> rows)
    {
        foreach (var r in rows)
        {
            var id = r.GetValueOrDefault("id", "");
            if (string.IsNullOrEmpty(id)) continue;

            DetachmentAbilities[id] = new DetachmentAbility
            {
                Id           = id,
                FactionId    = r.GetValueOrDefault("faction_id", ""),
                DetachmentId = r.GetValueOrDefault("detachment_id", ""),
                Detachment   = r.GetValueOrDefault("detachment", ""),
                Name         = r.GetValueOrDefault("name", ""),
                Legend       = r.GetValueOrDefault("legend", ""),
                Description  = r.GetValueOrDefault("description", ""),
            };
        }
    }

    private void BuildEnhancements(List<Dictionary<string, string>> rows)
    {
        foreach (var r in rows)
        {
            var id = r.GetValueOrDefault("id", "");
            if (string.IsNullOrEmpty(id)) continue;

            var cost = r.GetValueOrDefault("cost", "");

            Enhancements[id] = new Enhancement
            {
                Id           = id,
                FactionId    = r.GetValueOrDefault("faction_id", ""),
                DetachmentId = r.GetValueOrDefault("detachment_id", ""),
                Detachment   = r.GetValueOrDefault("detachment", ""),
                Name         = r.GetValueOrDefault("name", ""),
                Cost         = int.TryParse(cost, out var c) ? c : 0,
                Legend       = r.GetValueOrDefault("legend", ""),
                Description  = r.GetValueOrDefault("description", ""),
            };
        }
    }

    // ── Suche-Hilfsmethoden ───────────────────────────────────────────────────

    public IEnumerable<Datasheet> SearchDatasheets(string query, string? factionId = null)
    {
        var q = query.ToLowerInvariant();
        return Datasheets.Values
            .Where(d =>
                (factionId == null || d.FactionId.Equals(factionId, StringComparison.OrdinalIgnoreCase)) &&
                (d.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                 d.Keywords.Contains(q, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(d =>
            {
                // Exakter Treffer zuerst, dann Starts-With, dann Contains, dann Keyword-Treffer
                if (d.Name.Equals(query, StringComparison.OrdinalIgnoreCase)) return 0;
                if (d.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 1;
                if (d.Name.Contains(q, StringComparison.OrdinalIgnoreCase)) return 2;
                return 3; // Keyword-Treffer
            })
            .ThenBy(d => d.Name);
    }

    public IEnumerable<Stratagem> SearchStratagems(string? factionId = null, string? query = null, string? phase = null)
    {
        return Stratagems.Values
            .Where(s =>
                (factionId == null || s.FactionId.Equals(factionId, StringComparison.OrdinalIgnoreCase)) &&
                (query == null     || s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                      s.Description.Contains(query, StringComparison.OrdinalIgnoreCase)) &&
                (phase == null     || s.Phase.Contains(phase, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(s => s.Name);
    }

    public Faction? FindFaction(string nameOrId)
    {
        return Factions.Values.FirstOrDefault(f =>
            f.Id.Equals(nameOrId, StringComparison.OrdinalIgnoreCase) ||
            f.Name.Contains(nameOrId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Alle Detachment-Fähigkeiten einer Fraktion, optional nach Detachment-Name gefiltert.</summary>
    public IEnumerable<DetachmentAbility> GetDetachmentAbilities(string factionId, string? detachment = null)
    {
        return DetachmentAbilities.Values
            .Where(d => d.FactionId.Equals(factionId, StringComparison.OrdinalIgnoreCase) &&
                        (detachment == null || d.Detachment.Contains(detachment, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(d => d.Detachment);
    }

    /// <summary>Namen aller Detachments einer Fraktion (eindeutig, alphabetisch).</summary>
    public IEnumerable<string> GetDetachmentNames(string factionId)
    {
        return DetachmentAbilities.Values
            .Where(d => d.FactionId.Equals(factionId, StringComparison.OrdinalIgnoreCase))
            .Select(d => d.Detachment)
            .Where(d => !string.IsNullOrEmpty(d))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(d => d);
    }

    /// <summary>Findet ein Detachment einer Fraktion per exaktem oder Teilstring-Namen (case-insensitive).</summary>
    public DetachmentAbility? FindDetachment(string factionId, string detachmentName)
    {
        var matches = DetachmentAbilities.Values
            .Where(d => d.FactionId.Equals(factionId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return matches.FirstOrDefault(d => d.Detachment.Equals(detachmentName, StringComparison.OrdinalIgnoreCase))
            ?? matches.FirstOrDefault(d => d.Detachment.Contains(detachmentName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Enhancements einer Fraktion, optional nach Detachment gefiltert.</summary>
    public IEnumerable<Enhancement> SearchEnhancements(string factionId, string? detachment = null)
    {
        return Enhancements.Values
            .Where(e => e.FactionId.Equals(factionId, StringComparison.OrdinalIgnoreCase) &&
                        (detachment == null || e.Detachment.Contains(detachment, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(e => e.Detachment).ThenBy(e => e.Name);
    }

    /// <summary>Findet eine Enhancement einer Fraktion per exaktem oder Teilstring-Namen (case-insensitive).</summary>
    public Enhancement? FindEnhancement(string factionId, string enhancementName, string? detachment = null)
    {
        var matches = Enhancements.Values
            .Where(e => e.FactionId.Equals(factionId, StringComparison.OrdinalIgnoreCase) &&
                        (detachment == null || e.Detachment.Contains(detachment, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return matches.FirstOrDefault(e => e.Name.Equals(enhancementName, StringComparison.OrdinalIgnoreCase))
            ?? matches.FirstOrDefault(e => e.Name.Contains(enhancementName, StringComparison.OrdinalIgnoreCase));
    }
}