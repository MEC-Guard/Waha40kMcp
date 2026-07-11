using Waha40kMcp.Data;
using Waha40kMcp.Models;

namespace Waha40kMcp.Tests;

/// <summary>
/// Test-Double für <see cref="IMfmScraper"/> — liefert vorab konfigurierte Punktekosten/Wargear
/// ohne echten Browser-/Netzwerkzugriff, damit ArmyBuilderTools offline getestet werden kann.
/// </summary>
public class FakeMfmScraper : IMfmScraper
{
    private readonly Dictionary<string, Dictionary<string, List<PointsCostEntry>>> _pointsBySlug =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, Dictionary<string, List<WargearOptionCost>>> _wargearBySlug =
        new(StringComparer.OrdinalIgnoreCase);

    public int ScrapeCallCount { get; private set; }

    public void SetUnitPoints(string factionSlug, string unitName, List<PointsCostEntry> entries)
    {
        if (!_pointsBySlug.TryGetValue(factionSlug, out var units))
            _pointsBySlug[factionSlug] = units = new Dictionary<string, List<PointsCostEntry>>(StringComparer.OrdinalIgnoreCase);
        units[unitName] = entries;
    }

    public void SetWargearOptions(string factionSlug, string unitName, List<WargearOptionCost> options)
    {
        if (!_wargearBySlug.TryGetValue(factionSlug, out var units))
            _wargearBySlug[factionSlug] = units = new Dictionary<string, List<WargearOptionCost>>(StringComparer.OrdinalIgnoreCase);
        units[unitName] = options;
    }

    public Task<Dictionary<string, List<PointsCostEntry>>> ScrapeFactionsAsync(string factionSlug, bool forceRefresh = false)
    {
        ScrapeCallCount++;
        var result = _pointsBySlug.TryGetValue(factionSlug, out var units)
            ? units
            : new Dictionary<string, List<PointsCostEntry>>(StringComparer.OrdinalIgnoreCase);
        return Task.FromResult(result);
    }

    public Dictionary<string, List<WargearOptionCost>> GetWargearOptions(string factionSlug)
    {
        return _wargearBySlug.TryGetValue(factionSlug, out var units)
            ? units
            : new Dictionary<string, List<WargearOptionCost>>(StringComparer.OrdinalIgnoreCase);
    }
}
