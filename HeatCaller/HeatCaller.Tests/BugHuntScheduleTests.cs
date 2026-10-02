namespace HeatCaller.Tests;

/// <summary>
/// Undoing "Fill in the times" on the run sheet.
///
/// WHY. The fill changes lines in place, and its undo used to go through
/// Snapshot and Restore, which hold the very same line objects - so the
/// undo put back the race day's start time and left every line restamped.
/// DayScheduleStore.RestoreTimes is the replacement; these pin what it does.
/// </summary>
public sealed class BugHuntScheduleTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void RestoreTimes_puts_back_the_old_times_and_saves_them()
    {
        var store = DayScheduleStore.Load();
        var blank = new ScheduleEntry { Description = "Doors open", ScheduledTime = "" };
        var typed = new ScheduleEntry { Description = "Practice", ScheduledTime = "19:00" };
        store.Add(blank);
        store.Add(typed);

        // What the fill does: stamp in place, then write the pass once.
        var changes = new List<DayScheduleStore.TimeChange>
        {
            new(blank.Id, "", "18:00"),
            new(typed.Id, "19:00", "18:10"),
        };
        blank.ScheduledTime = "18:00";
        typed.ScheduledTime = "18:10";
        store.UpdateMany(new[] { blank, typed });

        Assert.Equal(2, store.RestoreTimes(changes));

        var reloaded = DayScheduleStore.Load().Everything.ToDictionary(e => e.Id);
        Assert.Equal("", reloaded[blank.Id].ScheduledTime);
        Assert.Equal("19:00", reloaded[typed.Id].ScheduledTime);
    }

    [Fact]
    public void RestoreTimes_leaves_a_time_typed_over_since_and_skips_a_deleted_line()
    {
        var store = DayScheduleStore.Load();
        var edited = new ScheduleEntry { Description = "Lunch", ScheduledTime = "" };
        var gone = new ScheduleEntry { Description = "Raffle", ScheduledTime = "" };
        store.Add(edited);
        store.Add(gone);

        var changes = new List<DayScheduleStore.TimeChange>
        {
            new(edited.Id, "", "12:00"),
            new(gone.Id, "", "12:30"),
        };

        // Typed over by hand after the fill, and a line deleted after it.
        edited.ScheduledTime = "13:00";
        store.Update(edited);
        store.Delete(gone.Id);

        Assert.Equal(0, store.RestoreTimes(changes));
        Assert.Equal("13:00", store.Everything.Single(e => e.Id == edited.Id).ScheduledTime);
    }
}
