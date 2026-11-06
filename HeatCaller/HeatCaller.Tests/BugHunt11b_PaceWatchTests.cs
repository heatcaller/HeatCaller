namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 11b review (6 Nov 2026): the pace detector calls a lap "the
/// fastest ever set on this layout" only when Race Control would write it
/// onto the layout - not for a lap Race Control refuses as implausible
/// ("All-time record NOT taken"), and not in a demo run. Both now ask the
/// one rule, RecordPlausibility.
/// </summary>
public sealed class BugHunt11b_PaceWatchTests
{
    private static long Ms(double seconds) => (long)Math.Round(seconds * 1000);

    /// <summary>A watch wired the way the announcer wires it, against a real race.</summary>
    private static PaceWatch Watch(RaceTiming race, double recordSeconds = 9.8)
    {
        var watch = new PaceWatch
        {
            LayoutRecord = () => (recordSeconds, "Old Holder"),
            RecordWouldStand = (driver, lap, record) => RecordPlausibility.WouldTake(race, driver, Ms(lap), Ms(record)),
        };

        watch.Reset();
        return watch;
    }

    private static RaceTiming Race() => new(new RaceControlSession());

    [Fact]
    public void A_misread_lap_far_under_the_record_is_not_called_a_layout_record()
    {
        var watch = Watch(Race());

        // 7.2 s under a 9.8 s record: more than a quarter off, which Race
        // Control refuses and never writes to the layout.
        var note = watch.Saw(new LapSeen("Sam", 7.2));

        Assert.NotEqual(PaceNoteKind.LayoutRecord, note?.Kind);
    }

    [Fact]
    public void A_demo_run_never_calls_a_layout_record()
    {
        var race = Race();
        race.IsDemoMode = true;
        var watch = Watch(race);

        var note = watch.Saw(new LapSeen("Sam", 9.7));

        Assert.NotEqual(PaceNoteKind.LayoutRecord, note?.Kind);
    }

    [Fact]
    public void A_genuine_record_lap_is_still_called_a_layout_record()
    {
        var watch = Watch(Race());

        var note = watch.Saw(new LapSeen("Sam", 9.7));

        Assert.NotNull(note);
        Assert.Equal(PaceNoteKind.LayoutRecord, note!.Kind);
        Assert.Equal(9.8, note.Beaten!.Value, 3);
        Assert.Equal("Old Holder", note.PreviousHolder);
    }

    [Fact]
    public void A_refused_phantom_does_not_become_the_bar_a_real_record_must_beat()
    {
        var watch = Watch(Race());

        watch.Saw(new LapSeen("Sam", 7.2));
        var real = watch.Saw(new LapSeen("Alex", 9.7));

        Assert.NotNull(real);
        Assert.Equal(PaceNoteKind.LayoutRecord, real!.Kind);
        Assert.Equal(9.8, real.Beaten!.Value, 3);
    }

    [Fact]
    public void A_record_taken_this_race_is_the_bar_for_the_next_one()
    {
        var watch = Watch(Race());

        watch.Saw(new LapSeen("Sam", 9.7));
        var next = watch.Saw(new LapSeen("Alex", 9.6));

        Assert.NotNull(next);
        Assert.Equal(PaceNoteKind.LayoutRecord, next!.Kind);
        Assert.Equal(9.7, next.Beaten!.Value, 3);
        Assert.Equal("Sam", next.PreviousHolder);
    }

    // --- The shared rule, as Race Control's log line words it -------------

    [Fact]
    public void A_lap_within_a_tenth_of_the_minimum_lap_time_is_doubted()
    {
        Assert.Contains("minimum lap time", RecordPlausibility.Doubt(3200, 3000, 0, null));
        Assert.Null(RecordPlausibility.Doubt(3400, 3000, 0, null));
    }

    [Fact]
    public void A_lap_more_than_a_quarter_under_the_record_is_doubted()
    {
        Assert.Contains("by more than a quarter", RecordPlausibility.Doubt(7200, 0, 9800, null));
        Assert.Null(RecordPlausibility.Doubt(7400, 0, 9800, null));
    }

    [Fact]
    public void A_lap_under_half_the_drivers_typical_lap_is_doubted_once_there_are_three()
    {
        Assert.Contains("typical", RecordPlausibility.Doubt(4000, 0, 0, [10000, 10100, 9900]));
        Assert.Null(RecordPlausibility.Doubt(4000, 0, 0, [10000, 10100]));
        Assert.Null(RecordPlausibility.Doubt(5100, 0, 0, [10000, 10100, 9900]));
    }
}
