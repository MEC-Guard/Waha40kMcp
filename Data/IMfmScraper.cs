using Waha40kMcp.Models;

namespace Waha40kMcp.Data;

/// <summary>
/// Abstraktion über <see cref="MfmScraper"/>, damit Konsumenten (z.B. ArmyBuilderTools)
/// ohne echten Browser-/Netzwerkzugriff getestet werden können.
/// </summary>
public interface IMfmScraper
{
    Task<Dictionary<string, List<PointsCostEntry>>> ScrapeFactionsAsync(string factionSlug, bool forceRefresh = false);

    Dictionary<string, List<WargearOptionCost>> GetWargearOptions(string factionSlug);
}
