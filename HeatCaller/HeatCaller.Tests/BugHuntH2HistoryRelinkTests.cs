namespace HeatCaller.Tests;

/// <summary>
/// An undo that could not save a restored session leaves a retry on the Undo
/// list. When that retry does save it, the caller has to hear about it - the
/// session's round was left unlinked by the failed undo, and only the caller
/// knows which round to link back. Without that, the race was in Race
/// History again but its heat still read as unrun.
/// </summary>
public sealed class BugHuntH2HistoryRelinkTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private T WhileLocked<T>(Func<T> action)
    {
        using (new FileStream(_data.PathOf("race-history.json"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            return action();
        }
    }

    [Fact]
    public void The_retry_reports_each_session_it_saves_even_after_failing_again()
    {
        var store = SessionHistoryStore.Load();
        Assert.True(store.Add(new SessionRecord { Label = "Q1 Heat 1" }));

        var aMain = new SessionRecord { Label = "A-Main" };
        Assert.False(WhileLocked(() => store.Add(aMain)));

        var undo = new UndoService();
        var saved = new List<SessionRecord>();

        Assert.Throws<InvalidOperationException>(() => store.KeepRetryForUnsaved(undo, new[] { aMain }, saved.Add));
        Assert.True(undo.CanUndo);

        // Still locked: the retry fails, says so, and leaves another retry
        // that must still carry the callback.
        Assert.Null(WhileLocked(() => undo.Undo()));
        Assert.Empty(saved);
        Assert.True(undo.CanUndo);

        Assert.NotNull(undo.Undo());

        Assert.Same(aMain, Assert.Single(saved));
        Assert.False(undo.CanUndo);
        Assert.NotNull(SessionHistoryStore.Load().Get(aMain.Id));
    }

    [Fact]
    public void A_retry_with_no_callback_still_saves()
    {
        var store = SessionHistoryStore.Load();
        Assert.True(store.Add(new SessionRecord { Label = "Q1 Heat 1" }));

        var aMain = new SessionRecord { Label = "A-Main" };
        Assert.False(WhileLocked(() => store.Add(aMain)));

        var undo = new UndoService();
        Assert.Throws<InvalidOperationException>(() => store.KeepRetryForUnsaved(undo, new[] { aMain }));

        Assert.NotNull(undo.Undo());
        Assert.NotNull(SessionHistoryStore.Load().Get(aMain.Id));
    }

    [Fact]
    public void A_failing_callback_is_reported_after_the_session_is_saved()
    {
        var store = SessionHistoryStore.Load();
        Assert.True(store.Add(new SessionRecord { Label = "Q1 Heat 1" }));

        var aMain = new SessionRecord { Label = "A-Main" };
        Assert.False(WhileLocked(() => store.Add(aMain)));

        var undo = new UndoService();
        Exception? reported = null;
        undo.UndoFailed += (_, ex) => reported = ex;

        Assert.Throws<InvalidOperationException>(() =>
            store.KeepRetryForUnsaved(undo, new[] { aMain }, _ => throw new IOException("rounds file locked")));

        Assert.Null(undo.Undo());

        Assert.IsType<IOException>(Assert.IsType<InvalidOperationException>(reported).InnerException);
        Assert.False(undo.CanUndo);
        Assert.NotNull(SessionHistoryStore.Load().Get(aMain.Id));
    }
}
