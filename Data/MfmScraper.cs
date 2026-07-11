using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Text.Json;
using Microsoft.Playwright;
using Waha40kMcp.Models;

namespace Waha40kMcp.Data;

/// <summary>
/// Scraped Punktekosten vom offiziellen Munitorum Field Manual.
/// Fängt die JSON API-Calls der Next.js Seite ab.
/// </summary>
public class MfmScraper : IMfmScraper
{
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Waha40kMcp", "mfm-cache");

    private static readonly Dictionary<string, string> FactionSlugs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["AdeptaSororitas"]       = "adepta-sororitas",
        ["AdeptusCustodes"]      = "adeptus-custodes",
        ["AdeptusMechanicus"]    = "adeptus-mechanicus",
        ["Aeldari"]              = "aeldari",
        ["AstraMilitarum"]       = "astra-militarum",
        ["BlackTemplars"]        = "black-templars",
        ["BloodAngels"]          = "blood-angels",
        ["ChaosDaemons"]         = "chaos-daemons",
        ["ChaosKnights"]         = "chaos-knights",
        ["ChaosSpaceMarines"]    = "chaos-space-marines",
        ["DarkAngels"]           = "dark-angels",
        ["DeathGuard"]           = "death-guard",
        ["Deathwatch"]           = "deathwatch",
        ["Drukhari"]             = "drukhari",
        ["EmperorsChildren"]     = "emperors-children",
        ["GenestealerCults"]     = "genestealer-cults",
        ["GreyKnights"]          = "grey-knights",
        ["ImperialAgents"]       = "imperial-agents",
        ["ImperialKnights"]      = "imperial-knights",
        ["LeaguesOfVotann"]      = "leagues-of-votann",
        ["LoV"]                  = "leagues-of-votann",
        ["DG"]                   = "death-guard",
        ["CSM"]                  = "chaos-space-marines",
        ["SM"]                   = "space-marines",
        ["NEC"]                  = "necrons",
        ["ORK"]                  = "orks",
        ["TAU"]                  = "tau-empire",
        ["TYR"]                  = "tyranids",
        ["AE"]                   = "aeldari",
        ["AM"]                   = "astra-militarum",
        ["CD"]                   = "chaos-daemons",
        ["GK"]                   = "grey-knights",
        ["DW"]                   = "deathwatch",
        ["BA"]                   = "blood-angels",
        ["DA"]                   = "dark-angels",
        ["SW"]                   = "space-wolves",
        ["BT"]                   = "black-templars",
        ["AC"]                   = "adeptus-custodes",
        ["AdMech"]               = "adeptus-mechanicus",
        ["AS"]                   = "adepta-sororitas",
        ["IK"]                   = "imperial-knights",
        ["CK"]                   = "chaos-knights",
        ["DRU"]                  = "drukhari",
        ["TS"]                   = "thousand-sons",
        ["WE"]                   = "world-eaters",
        ["EC"]                   = "emperors-children",
        ["GSC"]                  = "genestealer-cults",
        ["Necrons"]              = "necrons",
        ["Orks"]                 = "orks",
        ["SpaceMarines"]         = "space-marines",
        ["SpaceWolves"]          = "space-wolves",
        ["TauEmpire"]            = "tau-empire",
        ["ThousandSons"]         = "thousand-sons",
        ["Tyranids"]             = "tyranids",
        ["WorldEaters"]          = "world-eaters",
    };

    public static string? GetSlugForFaction(string factionId)
    {
        if (FactionSlugs.TryGetValue(factionId, out var slug)) return slug;
        var normalized = Regex.Replace(factionId, @"[\s\-_']", "");
        foreach (var kv in FactionSlugs)
        {
            var keyNorm = Regex.Replace(kv.Key, @"[\s\-_']", "");
            if (keyNorm.Equals(normalized, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        }
        var slugNorm = factionId.ToLowerInvariant().Replace(" ", "-");
        if (FactionSlugs.ContainsValue(slugNorm)) return slugNorm;

        // Letzter Fallback: Teilstring-Suche. Nur für Keys/Eingaben ab 4 Zeichen erlaubt —
        // die vielen 2-3-Buchstaben-Abkürzungen ("AC", "CD", "AE", "SM" ...) sind bereits
        // über den exakten Lookup oben abgedeckt und würden hier sonst in x-beliebigen
        // längeren Eingaben (z.B. enthält "...faCtion..." zufällig "AC") falsch anschlagen.
        const int minFuzzyLength = 4;
        if (normalized.Length >= minFuzzyLength)
        {
            foreach (var kv in FactionSlugs)
                if (kv.Key.Length >= minFuzzyLength &&
                    (kv.Key.Contains(normalized, StringComparison.OrdinalIgnoreCase) ||
                     normalized.Contains(kv.Key, StringComparison.OrdinalIgnoreCase)))
                    return kv.Value;
        }
        return null;
    }

    /// <summary>In-Memory Cache der zuletzt gescrapten Wargear-Optionen pro Fraktion.</summary>
    private static readonly ConcurrentDictionary<string, Dictionary<string, List<WargearOptionCost>>> WargearCache = new();

    public Dictionary<string, List<WargearOptionCost>> GetWargearOptions(string factionSlug)
    {
        return WargearCache.TryGetValue(factionSlug, out var w) ? w : [];
    }

    public async Task<Dictionary<string, List<PointsCostEntry>>> ScrapeFactionsAsync(
        string factionSlug, bool forceRefresh = false)
    {
        Directory.CreateDirectory(CacheDir);
        var cacheFile = Path.Combine(CacheDir, $"{factionSlug}.json");
        var wargearCacheFile = Path.Combine(CacheDir, $"{factionSlug}.wargear.json");

        if (!forceRefresh && File.Exists(cacheFile) &&
            (DateTime.UtcNow - File.GetLastWriteTimeUtc(cacheFile)).TotalHours < 24)
        {
            Console.Error.WriteLine($"[MFM] Cache: {factionSlug}");
            var cached = await File.ReadAllTextAsync(cacheFile);
            if (File.Exists(wargearCacheFile))
            {
                var wgCached = await File.ReadAllTextAsync(wargearCacheFile);
                WargearCache[factionSlug] = JsonSerializer.Deserialize<Dictionary<string, List<WargearOptionCost>>>(wgCached) ?? [];
            }
            return JsonSerializer.Deserialize<Dictionary<string, List<PointsCostEntry>>>(cached) ?? [];
        }

        var url = $"https://mfm.warhammer-community.com/en/{factionSlug}";
        Console.Error.WriteLine($"[MFM] Scrape: {url}");

        var result = new Dictionary<string, List<PointsCostEntry>>(StringComparer.OrdinalIgnoreCase);
        var capturedJson = new List<string>();

        try
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
            var page = await browser.NewPageAsync();

            // Network-Requests abfangen — alle JSON-Responses loggen
            page.Response += async (_, response) =>
            {
                var respUrl = response.Url;
                var ct = response.Headers.GetValueOrDefault("content-type", "");
                if (response.Status == 200 && ct.Contains("json"))
                {
                    try
                    {
                        var body = await response.TextAsync();
                        Console.Error.WriteLine($"[MFM] JSON-Response: {respUrl.Substring(0, Math.Min(100, respUrl.Length))} ({body.Length} bytes)");
                        if (body.Length > 100 &&
                            (body.Contains("model", StringComparison.OrdinalIgnoreCase) ||
                             body.Contains("cost", StringComparison.OrdinalIgnoreCase) ||
                             body.Contains("points", StringComparison.OrdinalIgnoreCase)))
                        {
                            capturedJson.Add(body);
                        }
                    }
                    catch { }
                }
            };

            await page.GotoAsync(url);
            await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

            // Cookie-Banner (OneTrust) wegklicken falls vorhanden
            try
            {
                var acceptButton = page.Locator("#onetrust-accept-btn-handler");
                await acceptButton.ClickAsync(new() { Timeout = 5000 });
                Console.Error.WriteLine($"[MFM] Cookie-Banner akzeptiert");
                await page.WaitForTimeoutAsync(1000);
            }
            catch
            {
                Console.Error.WriteLine($"[MFM] Kein Cookie-Banner gefunden oder bereits akzeptiert");
            }

            // Warte bis Punktzahlen erscheinen (JS lädt sie nach)
            try
            {
                await page.WaitForFunctionAsync(@"
                    () => {
                        const lines = document.body.innerText.split('\n').map(l => l.trim());
                        return lines.some(l => /^\d{2,4}$/.test(l) && parseInt(l) >= 10 && parseInt(l) <= 3000);
                    }
                ", null, new() { Timeout = 20000 });
                Console.Error.WriteLine($"[MFM] Punktzahlen geladen!");
            }
            catch
            {
                Console.Error.WriteLine($"[MFM] Timeout - versuche trotzdem zu parsen...");
                await page.WaitForTimeoutAsync(5000);
            }

            // Versuch 1: API-Response parsen
            foreach (var json in capturedJson)
            {
                var parsed = TryParseApiResponse(json);
                foreach (var kv in parsed)
                {
                    if (!result.ContainsKey(kv.Key))
                        result[kv.Key] = kv.Value;
                }
            }

            // Versuch 2: Vollständigen Seitentext parsen
            Dictionary<string, List<WargearOptionCost>> wargearResult = [];
            if (result.Count == 0)
            {
                var text = await page.EvaluateAsync<string>("() => document.body.innerText");
                Console.Error.WriteLine($"[MFM] Fallback Text-Parsing ({text.Length} Zeichen)");
                Console.Error.WriteLine($"[MFM] TEXT-PREVIEW: {text.Substring(0, Math.Min(800, text.Length))}");
                var (parsedUnits, parsedWargear) = ParseMfmTextFull(text);
                result = parsedUnits;
                wargearResult = parsedWargear;
            }

            // Versuch 3: Next.js __NEXT_DATA__ Script-Tag parsen
            if (result.Count == 0)
            {
                var nextData = await page.EvaluateAsync<string>(@"
                    () => {
                        const el = document.getElementById('__NEXT_DATA__');
                        return el ? el.textContent : '';
                    }");
                if (!string.IsNullOrEmpty(nextData))
                {
                    Console.Error.WriteLine($"[MFM] __NEXT_DATA__ gefunden ({nextData.Length} Zeichen)");
                    var parsed = TryParseApiResponse(nextData);
                    foreach (var kv in parsed)
                        if (!result.ContainsKey(kv.Key))
                            result[kv.Key] = kv.Value;
                }
            }

            var json2 = JsonSerializer.Serialize(result);
            await File.WriteAllTextAsync(cacheFile, json2);

            var wgJson = JsonSerializer.Serialize(wargearResult);
            await File.WriteAllTextAsync(wargearCacheFile, wgJson);
            WargearCache[factionSlug] = wargearResult;

            var wargearUnitCount = wargearResult.Count;
            Console.Error.WriteLine($"[MFM] {result.Count} Einheiten gefunden für {factionSlug}" +
                                    (wargearUnitCount > 0 ? $" ({wargearUnitCount} mit Wargear-Optionen)" : ""));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MFM] Fehler: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// Versucht JSON aus der Next.js API zu parsen.
    /// Sucht nach Arrays mit "name" und "cost"/"points" Feldern.
    /// </summary>
    private static Dictionary<string, List<PointsCostEntry>> TryParseApiResponse(string json)
    {
        var result = new Dictionary<string, List<PointsCostEntry>>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var doc = JsonDocument.Parse(json);
            FindUnitsInJson(doc.RootElement, result);
        }
        catch { }
        return result;
    }

    private static void FindUnitsInJson(JsonElement element, Dictionary<string, List<PointsCostEntry>> result)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            // Prüfen ob dieses Objekt eine Einheit ist (hat "name" und Kosten)
            string? name = null;
            var costs = new List<PointsCostEntry>();

            foreach (var prop in element.EnumerateObject())
            {
                if ((prop.Name.Equals("name", StringComparison.OrdinalIgnoreCase) ||
                     prop.Name.Equals("unitName", StringComparison.OrdinalIgnoreCase)) &&
                    prop.Value.ValueKind == JsonValueKind.String)
                {
                    name = prop.Value.GetString();
                }

                if ((prop.Name.Equals("cost", StringComparison.OrdinalIgnoreCase) ||
                     prop.Name.Equals("points", StringComparison.OrdinalIgnoreCase) ||
                     prop.Name.Equals("pointsCost", StringComparison.OrdinalIgnoreCase)) &&
                    prop.Value.ValueKind == JsonValueKind.Number)
                {
                    costs.Add(new PointsCostEntry
                    {
                        Description = "1 model",
                        Cost = prop.Value.GetInt32()
                    });
                }

                // Rekursiv suchen
                FindUnitsInJson(prop.Value, result);
            }

            if (!string.IsNullOrEmpty(name) && costs.Count > 0)
            {
                var key = name!.ToLowerInvariant();
                if (!result.ContainsKey(key))
                    result[key] = costs;
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                FindUnitsInJson(item, result);
        }
    }

    /// <summary>Fallback: Text-Parsing des Seiteninhalts.</summary>
    private static Dictionary<string, List<PointsCostEntry>> ParseMfmText(string text)
    {
        var (units, _) = ParseMfmTextFull(text);
        return units;
    }

    /// <summary>
    /// Vollständiger Parser: erkennt UNIT COSTS Sektionen (mit Copy-Tier-Staffelung)
    /// UND separate WARGEAR OPTIONS Sektionen (Punktaufschlag pro Modell mit dieser Option).
    ///
    /// Beispiel-Layout:
    ///   WOLF GUARD TERMINATORS
    ///   YOUR 1ST UNIT COSTS
    ///   5 models ... 150 pts
    ///   10 models ... 300 pts
    ///   YOUR 2ND + UNIT COSTS
    ///   5 models ... 160 pts
    ///   10 models ... 310 pts
    ///   WARGEAR OPTIONS
    ///   per Storm Shield ... 5 pts
    /// </summary>
    internal static (Dictionary<string, List<PointsCostEntry>> units,
                     Dictionary<string, List<WargearOptionCost>> wargear) ParseMfmTextFull(string text)
    {
        var result = new Dictionary<string, List<PointsCostEntry>>(StringComparer.OrdinalIgnoreCase);
        var wargearResult = new Dictionary<string, List<WargearOptionCost>>(StringComparer.OrdinalIgnoreCase);
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                        .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

        string? currentUnit = null;
        // Sektionstyp: "none", "unit_costs", "wargear"
        string sectionType = "none";
        int currentCopyTier = 0;
        string? pendingModelCount = null;
        string? pendingWargearName = null;
        var costEntries = new List<PointsCostEntry>();
        var wargearEntries = new List<WargearOptionCost>();

        void FlushUnit()
        {
            SaveUnit(result, currentUnit, costEntries);
            if (!string.IsNullOrEmpty(currentUnit) && wargearEntries.Count > 0)
            {
                var key = currentUnit.ToLowerInvariant().Trim();
                if (!wargearResult.ContainsKey(key)) wargearResult[key] = [.. wargearEntries];
                else wargearResult[key].AddRange(wargearEntries);
            }
            costEntries = [];
            wargearEntries = [];
        }

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];

            // ── Sektions-Header erkennen ─────────────────────────────────────
            // "YOUR ... UNIT(S) COST(S)" → unit_costs Sektion
            // Akzeptiert: "YOUR UNIT COSTS", "YOUR 1ST UNIT COSTS", "YOUR 1ST TO 2ND UNITS COST",
            //             "YOUR 2ND + UNIT COSTS", "YOUR 3RD+ UNIT COSTS"
            if (Regex.IsMatch(line, @"YOUR\s+.{0,40}UNITS?\s+COSTS?", RegexOptions.IgnoreCase))
            {
                sectionType = "unit_costs";
                pendingModelCount = null;

                // Tier 2 wenn "2ND", "3RD", "4TH" etc. zusammen mit "+" auftaucht (auch mit Leerzeichen davor)
                if (Regex.IsMatch(line, @"\d(ST|ND|RD|TH)\s*\+", RegexOptions.IgnoreCase) ||
                    Regex.IsMatch(line, @"2ND\s*\+|3RD\s*\+|4TH\s*\+", RegexOptions.IgnoreCase))
                {
                    currentCopyTier = 2;
                }
                else
                {
                    currentCopyTier = 1; // "1ST UNIT COSTS", "1ST TO 2ND...", oder einfach "UNIT COSTS"
                }
                continue;
            }

            // "WARGEAR OPTIONS" → eigene Sektion, kostet pro Modell mit dieser Option
            if (Regex.IsMatch(line, @"^WARGEAR\s+OPTIONS?$", RegexOptions.IgnoreCase))
            {
                sectionType = "wargear";
                pendingWargearName = null;
                continue;
            }

            // ── Neue Einheit erkannt → alles speichern, neu beginnen ─────────
            if (IsUnitName(line))
            {
                FlushUnit();
                currentUnit = line;
                sectionType = "none";
                pendingModelCount = null;
                pendingWargearName = null;
                currentCopyTier = 0;
                continue;
            }

            if (sectionType == "none") continue;

            // ── UNIT COSTS Sektion ────────────────────────────────────────────
            if (sectionType == "unit_costs")
            {
                if (Regex.IsMatch(line, @"^\d+\s+model(s)?$", RegexOptions.IgnoreCase))
                {
                    pendingModelCount = line;
                    continue;
                }

                var pts = TryParsePoints(line);
                if (pts.HasValue)
                {
                    costEntries.Add(new PointsCostEntry
                    {
                        Description = pendingModelCount ?? "1 model",
                        Cost = pts.Value,
                        CopyTier = currentCopyTier
                    });
                    pendingModelCount = null;
                }
                continue;
            }

            // ── WARGEAR OPTIONS Sektion ───────────────────────────────────────
            // Format: "per Storm Shield" dann "5 pts" auf der nächsten Zeile
            // (manchmal auch "per X" ... "Y pts" mit Punkten dazwischen aus dotted leader line,
            //  die wir schon beim Split entfernt haben)
            if (sectionType == "wargear")
            {
                var pts = TryParsePoints(line);
                if (pts.HasValue && pendingWargearName != null)
                {
                    wargearEntries.Add(new WargearOptionCost
                    {
                        Name = pendingWargearName,
                        Cost = pts.Value
                    });
                    pendingWargearName = null;
                    continue;
                }

                // Name der Wargear-Option: "per Storm Shield" → "Storm Shield"
                var nameMatch = Regex.Match(line, @"^per\s+(.+)$", RegexOptions.IgnoreCase);
                if (nameMatch.Success)
                {
                    pendingWargearName = nameMatch.Groups[1].Value.Trim();
                    continue;
                }

                // Falls die Zeile selbst schon Name+Punkte enthält (z.B. "Storm Shield 5 pts")
                var inlineMatch = Regex.Match(line, @"^(.+?)\s+(\d{1,4})\s*pts?$", RegexOptions.IgnoreCase);
                if (inlineMatch.Success)
                {
                    var n = inlineMatch.Groups[1].Value.Trim();
                    n = Regex.Replace(n, @"^per\s+", "", RegexOptions.IgnoreCase);
                    wargearEntries.Add(new WargearOptionCost
                    {
                        Name = n,
                        Cost = int.Parse(inlineMatch.Groups[2].Value)
                    });
                    pendingWargearName = null;
                }
                continue;
            }
        }

        FlushUnit();
        return (result, wargearResult);
    }

    /// <summary>Versucht eine Punktzahl aus einer Zeile zu lesen: "70 pts", "70pt", oder reine Zahl 10-3000.</summary>
    internal static int? TryParsePoints(string line)
    {
        var ptsMatch = Regex.Match(line, @"^(\d{1,4})\s*pts?$", RegexOptions.IgnoreCase);
        if (ptsMatch.Success)
        {
            var v = int.Parse(ptsMatch.Groups[1].Value);
            if (v >= 1 && v <= 3000) return v;
        }
        if (int.TryParse(line, out var v2) && v2 >= 10 && v2 <= 3000) return v2;
        return null;
    }

    internal static bool IsUnitName(string line)
    {
        if (line.Length < 3 || int.TryParse(line, out _)) return false;
        var excludes = new[] { "COSTS", "MODEL", "UNIT", "YOUR", "LEADER", "DETACHMENT",
            "DISRUPTION", "PURGE", "HOLD", "ASSETS", "FACTIONS", "ENHANCEMENTS",
            "UNIQUE", "RECONNAISSANCE", "WELCOME", "MUNITORUM", "WARGEAR", "OPTIONS" };
        if (excludes.Any(e => line.Contains(e, StringComparison.OrdinalIgnoreCase))) return false;
        var letters = line.Where(char.IsLetter).ToList();
        if (letters.Count < 3) return false;
        return letters.Count(char.IsUpper) / (double)letters.Count >= 0.6;
    }

    private static void SaveUnit(Dictionary<string, List<PointsCostEntry>> result,
        string? unitName, List<PointsCostEntry> entries)
    {
        if (string.IsNullOrEmpty(unitName) || entries.Count == 0) return;
        var key = unitName.ToLowerInvariant().Trim();
        if (!result.ContainsKey(key)) result[key] = [.. entries];
        else result[key].AddRange(entries);
    }
}