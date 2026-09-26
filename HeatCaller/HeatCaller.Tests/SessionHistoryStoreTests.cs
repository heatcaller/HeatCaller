namespace HeatCaller.Tests;

/// <summary>
/// The race history is the one store whose loss nobody can re-type. From
/// the 25 Sep audit: a result that failed to save must not be treated as
/// saved - Add has to say so, and nothing may hear it was added.
/// </summary>
public sealed class SessionHistoryStoreTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void A_saved_result_is_there_after_a_restart()
    {
        var store = SessionHistoryStore.Load();
        var record = new SessionRecord { Label = "A-Main", StartedAtUtc = DateTime.UtcNow };

        Assert.True(store.Add(record));

        var reloaded = SessionHistoryStore.Load();

        Assert.NotNull(reloaded.Get(record.Id));
        Assert.Equal("A-Main", reloaded.Get(record.Id)!.Label);
    }

    [Fact]
    public void A_result_that_could_not_be_written_is_reported_and_not_announced()
    {
        var store = SessionHistoryStore.Load();
        Assert.True(store.Add(new SessionRecord { Label = "Q1 Heat 1" }));

        var announced = false;
        store.SessionAdded += _ => announced = true;

        bool saved;
        using (new FileStream(_data.PathOf("race-history.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            saved = store.Add(new SessionRecord { Label = "Q1 Heat 2" });
        }

        Assert.False(saved);
        Assert.False(announced);
    }

    [Fact]
    public void No_file_yet_means_an_empty_history_and_nothing_written()
    {
        var store = SessionHistoryStore.Load();

        Assert.Empty(store.All);
        Assert.False(File.Exists(_data.PathOf("race-history.json")));
    }
}
