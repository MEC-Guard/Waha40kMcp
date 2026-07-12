using System.Text.Json;
using Waha40kMcp.Models;

namespace Waha40kMcp.Data;

/// <summary>
/// Persistiert Army-Listen als JSON-Datei, damit sie einen Server-Neustart überleben
/// (im Gegensatz zu einem rein In-Memory-Dictionary).
/// </summary>
public class ArmyRepository
{
    private readonly string _storeDir;
    private readonly string _storeFile;

    // Schützt _armies und die Store-Datei vor gleichzeitigem Zugriff mehrerer Requests (--http Modus).
    private readonly Lock _lock = new();

    private Dictionary<string, ArmyList> _armies = new(StringComparer.OrdinalIgnoreCase);
    private bool _loaded;

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    /// <param name="storeDir">
    /// Verzeichnis für die armies.json. Standardmäßig %LocalAppData%\Waha40kMcp\armies —
    /// überschreibbar (z.B. in Tests) um ein isoliertes Verzeichnis zu nutzen.
    /// </param>
    public ArmyRepository(string? storeDir = null)
    {
        _storeDir = storeDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Waha40kMcp", "armies");
        _storeFile = Path.Combine(_storeDir, "armies.json");
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
                _armies = JsonSerializer.Deserialize<Dictionary<string, ArmyList>>(json, JsonOptions)
                          ?? new(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                _armies = new(StringComparer.OrdinalIgnoreCase);
            }
        }

        _loaded = true;
    }

    private void Save()
    {
        Directory.CreateDirectory(_storeDir);
        var json = JsonSerializer.Serialize(_armies, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_storeFile, json);
    }

    public ArmyList? Get(string name)
    {
        lock (_lock)
        {
            EnsureLoaded();
            return _armies.TryGetValue(name, out var army) ? army : null;
        }
    }

    /// <summary>Erstellt oder ersetzt eine Army-Liste und persistiert sofort.</summary>
    public void Set(string name, ArmyList army)
    {
        lock (_lock)
        {
            EnsureLoaded();
            _armies[name] = army;
            Save();
        }
    }

    public bool Remove(string name)
    {
        lock (_lock)
        {
            EnsureLoaded();
            var removed = _armies.Remove(name);
            if (removed) Save();
            return removed;
        }
    }

    /// <summary>
    /// Persistiert Änderungen, die direkt an einem per <see cref="Get"/> geholten
    /// <see cref="ArmyList"/>-Objekt vorgenommen wurden (z.B. Units/Detachment mutiert).
    /// </summary>
    public void SaveChanges()
    {
        lock (_lock)
        {
            Save();
        }
    }

    /// <summary>Snapshot aller gespeicherten Armies (Name → Liste).</summary>
    public Dictionary<string, ArmyList> All()
    {
        lock (_lock)
        {
            EnsureLoaded();
            return new Dictionary<string, ArmyList>(_armies, StringComparer.OrdinalIgnoreCase);
        }
    }

    public int Count()
    {
        lock (_lock)
        {
            EnsureLoaded();
            return _armies.Count;
        }
    }
}
