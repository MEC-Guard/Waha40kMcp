using Waha40kMcp.Data;
using Waha40kMcp.Models;
using Xunit;

namespace Waha40kMcp.Tests;

/// <summary>
/// Jeder Test bekommt sein eigenes temporäres Verzeichnis, damit Tests sich nicht
/// gegenseitig beeinflussen und die echte %LocalAppData%\Waha40kMcp\armies\armies.json
/// des Nutzers nicht angerührt wird.
/// </summary>
public class ArmyRepositoryTests : IDisposable
{
    private readonly string _tempDir;

    public ArmyRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Waha40kMcpTests_ArmyRepo_" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private ArmyRepository MakeRepo() => new(_tempDir);

    [Fact]
    public void Get_UnknownArmy_ReturnsNull()
    {
        var repo = MakeRepo();
        Assert.Null(repo.Get("Does Not Exist"));
    }

    [Fact]
    public void Set_ThenGet_ReturnsTheArmy()
    {
        var repo = MakeRepo();
        repo.Set("My Army", new ArmyList { Name = "My Army", FactionId = "SM", FactionName = "Space Marines" });

        var army = repo.Get("My Army");

        Assert.NotNull(army);
        Assert.Equal("Space Marines", army!.FactionName);
    }

    [Fact]
    public void Remove_DeletesArmy()
    {
        var repo = MakeRepo();
        repo.Set("My Army", new ArmyList { Name = "My Army" });

        var removed = repo.Remove("My Army");

        Assert.True(removed);
        Assert.Null(repo.Get("My Army"));
    }

    [Fact]
    public void Remove_UnknownArmy_ReturnsFalse()
    {
        var repo = MakeRepo();
        Assert.False(repo.Remove("Does Not Exist"));
    }

    [Fact]
    public void All_ReturnsSnapshotOfAllArmies()
    {
        var repo = MakeRepo();
        repo.Set("Army A", new ArmyList { Name = "Army A" });
        repo.Set("Army B", new ArmyList { Name = "Army B" });

        var all = repo.All();

        Assert.Equal(2, all.Count);
        Assert.Contains("Army A", all.Keys);
        Assert.Contains("Army B", all.Keys);
    }

    [Fact]
    public void Count_ReflectsNumberOfStoredArmies()
    {
        var repo = MakeRepo();
        Assert.Equal(0, repo.Count());

        repo.Set("Army A", new ArmyList { Name = "Army A" });

        Assert.Equal(1, repo.Count());
    }

    [Fact]
    public void SaveChanges_PersistsInPlaceMutationsToRetrievedArmy()
    {
        var repo = MakeRepo();
        repo.Set("My Army", new ArmyList { Name = "My Army", PointsLimit = 2000 });

        var army = repo.Get("My Army")!;
        army.Units.Add(new ArmyUnit { Name = "Intercessor Squad", Points = 80, ModelCount = 5 });
        repo.SaveChanges();

        // Neue Repository-Instanz simuliert einen Neustart -> liest von Disk statt aus dem Speicher.
        var repoAfterRestart = new ArmyRepository(_tempDir);
        var reloaded = repoAfterRestart.Get("My Army");

        Assert.NotNull(reloaded);
        var unit = Assert.Single(reloaded!.Units);
        Assert.Equal("Intercessor Squad", unit.Name);
        Assert.Equal(80, unit.Points);
    }

    [Fact]
    public void Persistence_SurvivesAcrossRepositoryInstances()
    {
        var repo1 = MakeRepo();
        repo1.Set("My Army", new ArmyList { Name = "My Army", FactionName = "Necrons", PointsLimit = 1000 });

        var repo2 = new ArmyRepository(_tempDir);
        var army = repo2.Get("My Army");

        Assert.NotNull(army);
        Assert.Equal("Necrons", army!.FactionName);
        Assert.Equal(1000, army.PointsLimit);
    }
}
