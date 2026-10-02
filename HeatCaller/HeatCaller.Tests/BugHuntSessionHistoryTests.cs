namespace HeatCaller.Tests;

/// <summary>
/// A race whose filing failed must not reach race-history.json on the back
/// of some later, unrelated save: its crash journal is still there, so the
/// next launch offers it back and it gets filed a second time.
/// </summary>
public sealed class BugHuntSessionHistoryTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private bool AddWhileLocked(SessionHistoryStore store, SessionRecord record)
    {
        using (new FileStream(_data.PathOf("race-history.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            return store.Add(record);
        }
    }

    [Fact]
    public void A_race_that_failed_to_file_is_not_written_by_the_next_unrelated_save()
    {
        var store = SessionHistoryStore.Load();
        var earlier = new SessionRecord { Label = "Q1 Heat 1" };
        Assert.True(store.Add(earlier));

        var aMain = new SessionRecord { Label = "A-Main" };
        Assert.False(AddWhileLocked(store, aMain));

        // Still shown for the rest of the evening.
        Assert.NotNull(store.Get(aMain.Id));

        // A note typed on another race, once the lock has cleared.
        earlier.Notes = "Marshal short";
        Assert.True(store.SaveChanges());

        var reloaded = SessionHistoryStore.Load();
        Assert.NotNull(reloaded.Get(earlier.Id));
        Assert.Null(reloaded.Get(aMain.Id));
    }

    [Fact]
    public void Adding_the_same_race_again_files_it_once()
    {
        var store = SessionHistoryStore.Load();
        Assert.True(store.Add(new SessionRecord { Label = "Q1 Heat 1" }));

        var aMain = new SessionRecord { Label = "A-Main" };
        Assert.False(AddWhileLocked(store, aMain));

        var announced = 0;
        store.SessionAdded += _ => announced++;

        Assert.True(store.Add(aMain));

        Assert.Equal(1, announced);
        Assert.Single(store.All, s => s.Id == aMain.Id);
        Assert.Single(SessionHistoryStore.Load().All, s => s.Id == aMain.Id);
    }

    [Fact]
    public void A_correction_to_a_race_that_failed_to_file_is_not_announced()
    {
        var store = SessionHistoryStore.Load();
        Assert.True(store.Add(new SessionRecord { Label = "Q1 Heat 1" }));

        var aMain = new SessionRecord { Label = "A-Main" };
        Assert.False(AddWhileLocked(store, aMain));

        var announced = false;
        store.SessionEdited += _ => announced = true;

        Assert.False(store.SaveEdited(aMain));
        Assert.False(announced);
        Assert.Null(SessionHistoryStore.Load().Get(aMain.Id));
    }
}
