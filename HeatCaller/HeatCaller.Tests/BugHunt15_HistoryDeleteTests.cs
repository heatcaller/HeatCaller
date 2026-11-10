namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 15, B24: a delete from Race History that did not reach disk
/// (race-history.json held open) still took the session out of memory, and
/// Race History unlinked its round anyway. The file kept the race, so after a
/// restart it was back with its heat reading as unrun - or, if any later save
/// landed first, the delete went through after all behind the operator's
/// back. SessionHistoryStore.Delete now changes nothing when the write fails,
/// so Race History can keep the round linked and say so.
/// </summary>
public sealed class BugHunt15_HistoryDeleteTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private FileStream Lock() =>
        new(_data.PathOf("race-history.json"), FileMode.Open, FileAccess.Read, FileShare.None);

    [Fact]
    public void A_delete_that_cannot_be_written_leaves_the_session_listed()
    {
        var history = SessionHistoryStore.Load();
        var race = new SessionRecord { Label = "Q1 Heat 1" };
        Assert.True(history.Add(race));

        var raised = 0;
        history.SessionDeleted += _ => raised++;

        using (Lock())
        {
            Assert.False(history.Delete(race.Id));
        }

        Assert.Same(race, history.Get(race.Id));
        Assert.False(history.IsUnsaved(race));
        Assert.Equal(0, raised);
    }

    [Fact]
    public void A_later_save_does_not_quietly_finish_a_delete_that_failed()
    {
        var history = SessionHistoryStore.Load();
        var race = new SessionRecord { Label = "Q1 Heat 1" };
        Assert.True(history.Add(race));

        using (Lock())
        {
            Assert.False(history.Delete(race.Id));
        }

        Assert.True(history.Add(new SessionRecord { Label = "Q1 Heat 2" }));

        Assert.NotNull(SessionHistoryStore.Load().Get(race.Id));
    }

    [Fact]
    public void A_session_that_was_never_filed_still_goes_when_the_write_fails()
    {
        var history = SessionHistoryStore.Load();
        Assert.True(history.Add(new SessionRecord { Label = "Q1 Heat 1" }));

        var unfiled = new SessionRecord { Label = "A-Main" };

        using (Lock())
        {
            Assert.False(history.Add(unfiled));
            Assert.False(history.Delete(unfiled.Id));
        }

        Assert.Null(history.Get(unfiled.Id));
    }
}
