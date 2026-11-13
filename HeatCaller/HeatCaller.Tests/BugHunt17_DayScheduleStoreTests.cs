namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17, the run sheet's auto-tick and a programme that could not be
/// read.
///
/// WHY. AutoCompleteRecorded was asked only "is this round recorded?", and a
/// round the programme could not find answered no. With race-rounds.json
/// locked at launch the programme loads empty, so tonight's whole sheet was
/// unticked, its start times wiped, and the result saved. A tick is now only
/// taken back when the round is there to say it is unrun.
/// </summary>
public sealed class BugHunt17_DayScheduleStoreTests : IDisposable
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
    public void A_round_the_programme_cannot_find_keeps_its_tick_and_its_start()
    {
        var started = new DateTime(2026, 11, 13, 19, 42, 0, DateTimeKind.Utc);
        var store = TwoHeats();

        store.MarkStarted("heat-1", started);
        store.AutoCompleteRecorded(id => id == "heat-1");

        // After a restart with the programme unreadable: nothing is recorded,
        // because nothing is known.
        var reloaded = DayScheduleStore.Load();
        Assert.Equal(0, reloaded.AutoCompleteRecorded(_ => false, _ => false));

        Assert.True(Line(reloaded, "heat-1").IsComplete);
        Assert.Equal(started, Line(reloaded, "heat-1").ActualStartUtc);
        Assert.Equal(started, Line(DayScheduleStore.Load(), "heat-1").ActualStartUtc);
    }

    [Fact]
    public void A_round_that_is_there_and_unrun_still_gives_its_tick_back()
    {
        var store = TwoHeats();
        store.AutoCompleteRecorded(id => id == "heat-1");

        Assert.Equal(1, store.AutoCompleteRecorded(_ => false, _ => true));
        Assert.False(Line(store, "heat-1").IsComplete);
    }

    [Fact]
    public void An_unreadable_programme_does_not_untick_tonight_s_sheet()
    {
        // The same thing end to end, through the call a saved race makes.
        var rounds = RoundDefinitionStore.Load();
        var heat = new RoundDefinition { ClassName = "Stock", RoundLabel = "Q1 Heat 1" };
        rounds.Add(heat);

        var store = DayScheduleStore.Load();
        store.SyncToProgram(new List<DayScheduleStore.ProgramLine> { new(heat.Id, "Stock", "Q1 Heat 1") });
        store.MarkStarted(heat.Id, new DateTime(2026, 11, 13, 19, 42, 0, DateTimeKind.Utc));
        store.AutoCompleteRecorded(_ => true);

        File.WriteAllText(_data.PathOf("race-rounds.json"), "null");
        var unreadable = RoundDefinitionStore.Load();
        Assert.True(unreadable.LoadedUnreadable);

        Assert.Equal(0, MainForm.TickOffRecordedHeats(store, unreadable));
        Assert.True(Line(store, heat.Id).IsComplete);
        Assert.NotNull(Line(store, heat.Id).ActualStartUtc);
    }
}
