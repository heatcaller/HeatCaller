namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 11, the programme and its run sheet.
///
/// WHY. Deleting a round from the programme made SyncToProgram drop its
/// timetable line; undoing the delete (or undoing a heats build that replaced
/// it) brought the round back under the same id to a brand new blank line, so
/// the typed time, the tick and "started 19:42 · 3 min late" were all lost.
/// The line the sync took off is now kept aside and given back when its round
/// returns.
/// </summary>
public sealed class BugHunt11_ProgramScheduleTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static readonly DayScheduleStore.ProgramLine Heat2 = new("heat-2", "Stock", "Q2 Heat 2");
    private static readonly DayScheduleStore.ProgramLine Heat3 = new("heat-3", "Stock", "Q2 Heat 3");

    private static ScheduleEntry Line(DayScheduleStore store, string roundId) =>
        store.Everything.Single(e => e.RoundId == roundId);

    [Fact]
    public void Undoing_a_round_delete_gives_the_round_its_own_timetable_line_back()
    {
        var store = DayScheduleStore.Load();
        store.SyncToProgram(new[] { Heat2, Heat3 });

        var line = Line(store, "heat-3");
        var started = new DateTime(2026, 11, 5, 19, 42, 0, DateTimeKind.Utc);
        line.ScheduledTime = "19:40";
        store.Update(line);
        store.MarkStarted("heat-3", started);
        store.SetComplete(line.Id, true);
        var id = line.Id;

        // Deleted from the programme: its line goes.
        store.SyncToProgram(new[] { Heat2 });
        Assert.DoesNotContain(store.Everything, e => e.RoundId == "heat-3");

        // Undo: the round is back under the same id, and so is its line.
        store.SyncToProgram(new[] { Heat2, Heat3 });

        var back = Line(store, "heat-3");
        Assert.Equal(id, back.Id);
        Assert.Equal("19:40", back.ScheduledTime);
        Assert.Equal(started, back.ActualStartUtc);
        Assert.True(back.IsComplete);

        // And it was written down, not just held in memory.
        var reloaded = Line(DayScheduleStore.Load(), "heat-3");
        Assert.Equal(id, reloaded.Id);
        Assert.Equal("19:40", reloaded.ScheduledTime);
    }

    [Fact]
    public void A_returning_round_takes_the_programmes_current_wording()
    {
        var store = DayScheduleStore.Load();
        store.SyncToProgram(new[] { Heat2, Heat3 });

        var line = Line(store, "heat-3");
        line.ScheduledTime = "19:40";
        store.Update(line);

        store.SyncToProgram(new[] { Heat2 });
        store.SyncToProgram(new[] { Heat2, Heat3 with { ClassName = "Stock 2WD" } });

        var back = Line(store, "heat-3");
        Assert.Equal("19:40", back.ScheduledTime);
        Assert.Equal("Stock 2WD", back.ClassName);
    }

    [Fact]
    public void A_cleared_sheet_does_not_bring_an_old_line_back()
    {
        var store = DayScheduleStore.Load();
        store.SyncToProgram(new[] { Heat2, Heat3 });

        var line = Line(store, "heat-3");
        line.ScheduledTime = "19:40";
        store.Update(line);

        store.SyncToProgram(new[] { Heat2 });
        store.ClearAll();
        store.SyncToProgram(new[] { Heat2, Heat3 });

        Assert.Equal(string.Empty, Line(store, "heat-3").ScheduledTime);
    }

    [Fact]
    public void A_line_parked_on_one_race_day_is_not_given_to_another()
    {
        var store = DayScheduleStore.Load();
        store.CurrentEventId = "day-a";
        store.SyncToProgram(new[] { Heat3 });
        var line = Line(store, "heat-3");
        line.ScheduledTime = "19:40";
        store.Update(line);
        store.SyncToProgram(Array.Empty<DayScheduleStore.ProgramLine>());

        store.CurrentEventId = "day-b";
        store.SyncToProgram(new[] { Heat3 });

        var other = Line(store, "heat-3");
        Assert.Equal("day-b", other.EventId);
        Assert.NotEqual(line.Id, other.Id);
    }
}
