namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 10, the run sheet's auto-tick.
///
/// WHY. AutoCompleteRecorded only ever ticked: deleting a heat's race from
/// Race History put the round back to unrun but left its timetable line done,
/// so Up Next and the phones' timetable skipped it. It now takes back its own
/// ticks (and the start stamp with them), never a director's hand tick, and
/// remembers which ticks were its own across a restart.
/// </summary>
public sealed class BugHunt10_DayScheduleStoreTests : IDisposable
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
    public void A_heat_whose_race_is_deleted_goes_back_to_unrun_on_the_timetable()
    {
        var store = TwoHeats();
        var recorded = new HashSet<string> { "heat-1" };

        store.MarkStarted("heat-1", new DateTime(2026, 10, 23, 18, 0, 0, DateTimeKind.Utc));
        Assert.Equal(1, store.AutoCompleteRecorded(recorded.Contains));
        Assert.True(Line(store, "heat-1").IsComplete);
        Assert.Equal("heat-2", store.NextUp?.RoundId);

        // The race is deleted: the round is unrun again.
        recorded.Clear();
        Assert.Equal(1, store.AutoCompleteRecorded(recorded.Contains));

        Assert.False(Line(store, "heat-1").IsComplete);
        Assert.Null(Line(store, "heat-1").ActualStartUtc);
        Assert.Equal("heat-1", store.NextUp?.RoundId);
        Assert.False(Line(DayScheduleStore.Load(), "heat-1").IsComplete);
    }

    [Fact]
    public void Undoing_the_delete_gives_the_line_its_start_back_but_a_rerun_keeps_its_own()
    {
        var store = TwoHeats();
        var rehearsal = new DateTime(2026, 10, 23, 18, 0, 0, DateTimeKind.Utc);

        store.MarkStarted("heat-1", rehearsal);
        store.AutoCompleteRecorded(id => id == "heat-1");
        store.AutoCompleteRecorded(_ => false);
        Assert.Null(Line(store, "heat-1").ActualStartUtc);

        // Race History's undo relinks the race: same line, same start.
        Assert.Equal(1, store.AutoCompleteRecorded(id => id == "heat-1"));
        Assert.True(Line(store, "heat-1").IsComplete);
        Assert.Equal(rehearsal, Line(store, "heat-1").ActualStartUtc);

        // Deleted again and actually re-run: the re-run's own start stands.
        var rerun = rehearsal.AddMinutes(40);
        store.AutoCompleteRecorded(_ => false);
        store.MarkStarted("heat-1", rerun);
        store.AutoCompleteRecorded(id => id == "heat-1");
        Assert.Equal(rerun, Line(store, "heat-1").ActualStartUtc);
    }

    [Fact]
    public void The_auto_tick_is_remembered_across_a_restart()
    {
        var store = TwoHeats();
        store.AutoCompleteRecorded(id => id == "heat-1");

        var reloaded = DayScheduleStore.Load();
        Assert.True(Line(reloaded, "heat-1").IsComplete);
        Assert.Equal(1, reloaded.AutoCompleteRecorded(_ => false));
        Assert.False(Line(reloaded, "heat-1").IsComplete);
    }

    [Fact]
    public void A_hand_tick_is_never_taken_back()
    {
        var store = TwoHeats();

        // A heat skipped tonight, ticked by the director with no race behind it.
        store.SetComplete(Line(store, "heat-2").Id, true);
        Assert.Equal(0, store.AutoCompleteRecorded(_ => false));
        Assert.True(Line(store, "heat-2").IsComplete);

        // And a line the app ticked, then the director ticked again by hand, is theirs.
        store.AutoCompleteRecorded(id => id == "heat-1");
        store.SetComplete(Line(store, "heat-1").Id, false);
        store.SetComplete(Line(store, "heat-1").Id, true);
        Assert.Equal(0, DayScheduleStore.Load().AutoCompleteRecorded(_ => false));
        Assert.True(Line(DayScheduleStore.Load(), "heat-1").IsComplete);
    }

    [Fact]
    public void A_file_without_the_flag_reads_as_no_auto_ticks()
    {
        Assert.Empty(DayScheduleStore.ReadAutoTicked("[{\"Id\":\"a\",\"IsComplete\":true}]"));
        Assert.Empty(DayScheduleStore.ReadAutoTicked("{}"));
        Assert.Empty(DayScheduleStore.ReadAutoTicked("not json"));
        Assert.Equal(new[] { "b" }, DayScheduleStore.ReadAutoTicked("[{\"Id\":\"a\"},{\"Id\":\"b\",\"AutoCompleted\":true}]").ToArray());
    }
}
