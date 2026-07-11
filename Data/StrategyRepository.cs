using System.Text.Json;
using Waha40kMcp.Models;

namespace Waha40kMcp.Data;

/// <summary>
/// Lokale Wissensbasis für taktische Tipps, die Claude aus Webartikeln/Battle-Reports
/// extrahiert und strukturiert ablegt. Persistiert als JSON-Datei.
/// </summary>
public class StrategyRepository
{
    private readonly string _storeDir;
    private readonly string _storeFile;

    // Schützt _notes und die Store-Datei vor gleichzeitigem Zugriff mehrerer Requests (--http Modus).
    private readonly Lock _lock = new();

    private List<StrategyNote> _notes = [];
    private bool _loaded;

    /// <param name="storeDir">
    /// Verzeichnis für die notes.json. Standardmäßig %LocalAppData%\Waha40kMcp\strategy —
    /// überschreibbar (z.B. in Tests) um ein isoliertes Verzeichnis zu nutzen.
    /// </param>
    public StrategyRepository(string? storeDir = null)
    {
        _storeDir = storeDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Waha40kMcp", "strategy");
        _storeFile = Path.Combine(_storeDir, "notes.json");
    }

    private void EnsureLoaded()
    {
        if (_loaded) return;
        Directory.CreateDirectory(_storeDir);

        if (File.Exists(_storeFile))
        {
            try
            {
                var json = File.ReadAllText(_storeFile);
                _notes = JsonSerializer.Deserialize<List<StrategyNote>>(json) ?? [];
            }
            catch
            {
                _notes = [];
            }
        }

        _loaded = true;
    }

    private void Save()
    {
        Directory.CreateDirectory(_storeDir);
        var json = JsonSerializer.Serialize(_notes, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_storeFile, json);
    }

    public StrategyNote Add(StrategyNote note)
    {
        lock (_lock)
        {
            EnsureLoaded();
            _notes.Add(note);
            Save();
            return note;
        }
    }

    public bool Remove(string id)
    {
        lock (_lock)
        {
            EnsureLoaded();
            var removed = _notes.RemoveAll(n => n.Id == id);
            if (removed > 0) Save();
            return removed > 0;
        }
    }

    public List<StrategyNote> Search(
        string? faction = null,
        string? opponentFaction = null,
        string? mission = null,
        string? unit = null,
        string? keyword = null,
        string? tag = null)
    {
        List<StrategyNote> notes;
        lock (_lock)
        {
            EnsureLoaded();
            notes = _notes;
        }

        IEnumerable<StrategyNote> query = notes;

        if (!string.IsNullOrWhiteSpace(faction))
            query = query.Where(n => string.IsNullOrEmpty(n.Faction) ||
                n.Faction.Contains(faction, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(opponentFaction))
            query = query.Where(n => string.IsNullOrEmpty(n.OpponentFaction) ||
                n.OpponentFaction.Contains(opponentFaction, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(mission))
            query = query.Where(n => string.IsNullOrEmpty(n.Mission) ||
                n.Mission.Contains(mission, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(unit))
            query = query.Where(n => n.Unit.Contains(unit, StringComparison.OrdinalIgnoreCase));

        if (!string.IsNullOrWhiteSpace(tag))
            query = query.Where(n => n.Tags.Any(t => t.Contains(tag, StringComparison.OrdinalIgnoreCase)));

        if (!string.IsNullOrWhiteSpace(keyword))
            query = query.Where(n =>
                n.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                n.Tip.Contains(keyword, StringComparison.OrdinalIgnoreCase));

        return query.OrderByDescending(n => n.AddedUtc).ToList();
    }

    public List<StrategyNote> All()
    {
        lock (_lock)
        {
            EnsureLoaded();
            return [.. _notes.OrderByDescending(n => n.AddedUtc)];
        }
    }

    public int Count()
    {
        lock (_lock)
        {
            EnsureLoaded();
            return _notes.Count;
        }
    }
}
