namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, an abandoned heat's start stamp.
///
/// WHY. MarkStarted keeps only the first stamp per line, and nothing took it
/// off when a heat went green and was then abandoned unsaved, so the heat read
/// "on track - started 7:42 PM" until it was run again, and its real run kept
/// the abandoned start. ClearStarted takes it off (for an unticked line only)
/// and hands it back so an undo can restore it.
/// </summary>
public sealed class BugHunt24_DayScheduleStoreTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static DayScheduleStore TwoHeats()
    {
        var store = DayScheduleStore.Load();
        store.SyncToProgram(new List<DayScheduleStore.ProgramLine>
        {
            new("heat-1", "Stock", "Q1 Heat 1"),
            new("heat-2", "Stock", "Q1 Heat 2"),
        });
        return store;
    }

    private static ScheduleEntry Line(DayScheduleStore store, string roundId) =>
        store.Everything.Single(e => e.RoundId == roundId);

    [Fact]
    public void An_abandoned_heat_loses_its_start_and_takes_the_real_one_when_run_again()
    {
        var store = TwoHeats();
        var abandoned = new DateTime(2026, 11, 23, 19, 42, 0, DateTimeKind.Utc);
        var real = new DateTime(2026, 11, 23, 20, 30, 0, DateTimeKind.Utc);

        store.MarkStarted("heat-1", abandoned);
        Assert.Equal(abandoned, store.ClearStarted("heat-1"));
        Assert.Null(Line(store, "heat-1").ActualStartUtc);
        Assert.Null(Line(DayScheduleStore.Load(), "heat-1").ActualStartUtc);

        store.MarkStarted("heat-2", abandoned.AddMinutes(8));
        store.MarkStarted("heat-1", real);
        Assert.Equal(real, Line(store, "heat-1").ActualStartUtc);
    }

    [Fact]
    public void Clearing_leaves_a_done_line_and_a_resume_alone()
    {
        var store = TwoHeats();
        var first = new DateTime(2026, 11, 23, 19, 0, 0, DateTimeKind.Utc);

        // A resume in the same race: still first stamp wins.
        store.MarkStarted("heat-1", first);
        store.MarkStarted("heat-1", first.AddMinutes(3));
        Assert.Equal(first, Line(store, "heat-1").ActualStartUtc);

        // A line already ticked done keeps its start.
        store.AutoCompleteRecorded(id => id == "heat-1");
        Assert.Null(store.ClearStarted("heat-1"));
        Assert.Equal(first, Line(store, "heat-1").ActualStartUtc);

        // A line never started, or a round not on the sheet: nothing to give back.
        Assert.Null(store.ClearStarted("heat-2"));
        Assert.Null(store.ClearStarted("no-such-round"));
    }
}
