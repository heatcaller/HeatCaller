namespace HeatCaller.Tests;

/// <summary>
/// A race whose filing failed is still listed for the evening (All), but
/// nothing that publishes may treat it as saved: the relay's periodic passes
/// read the history through Saved and SavedView, which leave it out until a
/// retry of Add files it.
/// </summary>
public sealed class BugHuntH2FilingTests : IDisposable
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
    public void A_race_that_failed_to_file_is_listed_but_not_in_the_saved_view()
    {
        var store = SessionHistoryStore.Load();
        var heat = new SessionRecord { Label = "Q1 Heat 1" };
        Assert.True(store.Add(heat));

        var aMain = new SessionRecord { Label = "A-Main" };
        Assert.False(AddWhileLocked(store, aMain));

        Assert.Contains(store.All, s => s.Id == aMain.Id);
        Assert.True(store.IsUnsaved(aMain));
        Assert.False(store.IsUnsaved(heat));

        Assert.DoesNotContain(store.Saved, s => s.Id == aMain.Id);
        Assert.Contains(store.Saved, s => s.Id == heat.Id);

        var view = store.SavedView();
        Assert.Null(view.Get(aMain.Id));
        Assert.NotNull(view.Get(heat.Id));
    }

    [Fact]
    public void Once_the_retry_files_it_the_race_is_in_the_saved_view()
    {
        var store = SessionHistoryStore.Load();
        Assert.True(store.Add(new SessionRecord { Label = "Q1 Heat 1" }));

        var aMain = new SessionRecord { Label = "A-Main" };
        Assert.False(AddWhileLocked(store, aMain));

        Assert.True(store.Add(aMain));

        Assert.False(store.IsUnsaved(aMain));
        Assert.Single(store.Saved, s => s.Id == aMain.Id);
        Assert.Same(store, store.SavedView());
    }
}
