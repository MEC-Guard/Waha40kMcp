using Waha40kMcp.Data;
using Waha40kMcp.Models;
using Xunit;

namespace Waha40kMcp.Tests;

/// <summary>
/// Jeder Test bekommt sein eigenes temporäres Verzeichnis, damit Tests sich nicht
/// gegenseitig beeinflussen und die echte %LocalAppData%\Waha40kMcp\strategy\notes.json
/// des Nutzers nicht angerührt wird.
/// </summary>
public class StrategyRepositoryTests : IDisposable
{
    private readonly string _tempDir;

    public StrategyRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Waha40kMcpTests_" + Guid.NewGuid().ToString("N"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private StrategyRepository MakeRepo() => new(_tempDir);

    [Fact]
    public void Add_ThenSearch_FindsTheNote()
    {
        var repo = MakeRepo();
        repo.Add(new StrategyNote { Title = "Alpha Strike", Faction = "Leagues of Votann", Tip = "Vorrücken." });

        var results = repo.Search(faction: "Leagues of Votann");

        var note = Assert.Single(results);
        Assert.Equal("Alpha Strike", note.Title);
    }

    [Fact]
    public void Search_FiltersByOpponentFactionAndKeyword()
    {
        var repo = MakeRepo();
        repo.Add(new StrategyNote
        {
            Title = "Gegen Gunlines", Faction = "LoV", OpponentFaction = "Space Marines",
            Tip = "Deckung nutzen.",
        });
        repo.Add(new StrategyNote
        {
            Title = "Gegen Horden", Faction = "LoV", OpponentFaction = "Orks",
            Tip = "Objectives halten.",
        });

        var results = repo.Search(opponentFaction: "Space Marines");

        var note = Assert.Single(results);
        Assert.Equal("Gegen Gunlines", note.Title);
    }

    [Fact]
    public void Search_KeywordMatchesTitleOrTip()
    {
        var repo = MakeRepo();
        repo.Add(new StrategyNote { Title = "Deployment-Tipp", Tip = "Immer zuerst die Flanke sichern." });
        repo.Add(new StrategyNote { Title = "Anderer Tipp", Tip = "Nichts mit dem Suchwort." });

        var results = repo.Search(keyword: "Flanke");

        var note = Assert.Single(results);
        Assert.Equal("Deployment-Tipp", note.Title);
    }

    [Fact]
    public void Search_TagFilterMatchesPartialTag()
    {
        var repo = MakeRepo();
        repo.Add(new StrategyNote { Title = "Mit Tag", Tags = ["alpha-strike", "deployment"] });
        repo.Add(new StrategyNote { Title = "Ohne Tag", Tags = ["objective-control"] });

        var results = repo.Search(tag: "alpha");

        var note = Assert.Single(results);
        Assert.Equal("Mit Tag", note.Title);
    }

    [Fact]
    public void Remove_DeletesNoteById()
    {
        var repo = MakeRepo();
        var note = repo.Add(new StrategyNote { Title = "Zu löschen" });

        var removed = repo.Remove(note.Id);

        Assert.True(removed);
        Assert.Equal(0, repo.Count());
    }

    [Fact]
    public void Remove_UnknownId_ReturnsFalse()
    {
        var repo = MakeRepo();
        Assert.False(repo.Remove("does-not-exist"));
    }

    [Fact]
    public void All_ReturnsNotesNewestFirst()
    {
        var repo = MakeRepo();
        var first = repo.Add(new StrategyNote { Title = "Zuerst", AddedUtc = DateTime.UtcNow.AddMinutes(-10) });
        var second = repo.Add(new StrategyNote { Title = "Zuletzt", AddedUtc = DateTime.UtcNow });

        var all = repo.All();

        Assert.Equal(2, all.Count);
        Assert.Equal("Zuletzt", all[0].Title);
        Assert.Equal("Zuerst", all[1].Title);
    }

    [Fact]
    public void Persistence_SurvivesAcrossRepositoryInstances()
    {
        // Simuliert einen Neustart des Servers: neue Instanz, gleiches Verzeichnis.
        var repo1 = MakeRepo();
        repo1.Add(new StrategyNote { Title = "Persistiert" });

        var repo2 = MakeRepo();
        var all = repo2.All();

        var note = Assert.Single(all);
        Assert.Equal("Persistiert", note.Title);
    }
}
