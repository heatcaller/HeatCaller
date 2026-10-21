namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 8 (20 Oct 2026), PaceWatch: the night's best and the layout
/// record are read from SAVED races only, so they never include the race
/// being run. A lap that beat the old stored mark but not a faster lap
/// already turned in this race was still called "the fastest lap of the
/// night", taken from the old holder.
/// </summary>
public sealed class BugHunt8_PaceWatch_csTests
{
    private static PaceNote? Lap(PaceWatch watch, string driver, double seconds) =>
        watch.Saw(new LapSeen(driver, seconds));

    private static void WarmUp(PaceWatch watch, double seconds = 20.0)
    {
        for (var i = 0; i < 4; i++)
        {
            watch.Saw(new LapSeen($"warm-{i}", seconds + i));
        }
    }

    [Fact]
    public void A_lap_slower_than_this_races_new_night_best_is_not_the_night_best()
    {
        var watch = new PaceWatch { NightBest = () => (11.000, "Dave") };

        WarmUp(watch, 20.0);

        var sam = Lap(watch, "Sam", 10.500);
        Assert.NotNull(sam);
        Assert.Equal(PaceNoteKind.NightBest, sam!.Kind);
        Assert.Equal(11.000, sam.Beaten!.Value, 3);
        Assert.Equal("Dave", sam.PreviousHolder);

        // Under Dave's old mark, but Sam's 10.5 a minute ago was quicker.
        Assert.Null(Lap(watch, "Jen", 10.800));

        var jen = Lap(watch, "Jen", 10.400);
        Assert.NotNull(jen);
        Assert.Equal(PaceNoteKind.NightBest, jen!.Kind);
        Assert.Equal(10.500, jen.Beaten!.Value, 3);
        Assert.Equal("Sam", jen.PreviousHolder);
    }

    [Fact]
    public void A_lap_slower_than_this_races_new_layout_record_is_not_the_layout_record()
    {
        var watch = new PaceWatch { LayoutRecord = () => (11.000, "Jen") };

        WarmUp(watch, 20.0);

        Assert.Equal(PaceNoteKind.LayoutRecord, Lap(watch, "Sam", 10.500)!.Kind);

        // Not a layout record, and not the fastest of the race either.
        Assert.Null(Lap(watch, "Dave", 10.800));

        var dave = Lap(watch, "Dave", 10.300);
        Assert.Equal(PaceNoteKind.LayoutRecord, dave!.Kind);
        Assert.Equal(10.500, dave.Beaten!.Value, 3);
        Assert.Equal("Sam", dave.PreviousHolder);
    }

    [Fact]
    public void A_race_slower_than_the_stored_mark_is_still_judged_against_the_stored_mark()
    {
        var watch = new PaceWatch { NightBest = () => (12.500, "Dave") };

        // Warmed up AT the night's mark, so the race's own best only equals
        // it and the stored figure and holder still stand.
        //
        // WHY (20 Oct 2026, final review): this also ran a 12.497 first and
        // expected Dave's 12.500 to stand after it. It doesn't, and rightly:
        // 12.497 is under 12.500, so it IS the night's fastest lap from then
        // on, even though it was inside the margin and not called. That case
        // is the test below.
        WarmUp(watch, 12.5);

        var note = Lap(watch, "Sam", 12.460);
        Assert.Equal(PaceNoteKind.NightBest, note!.Kind);
        Assert.Equal(12.500, note.Beaten!.Value, 3);
        Assert.Equal("Dave", note.PreviousHolder);
    }

    [Fact]
    public void A_lap_inside_the_margin_of_the_stored_mark_is_the_mark_to_beat_after_it()
    {
        // As PaceWatchTests' margin test: three thousandths under Dave's
        // 12.500 is not called, but it is now the fastest lap of the night,
        // so Sam's next lap is measured against it (the facts then say
        // "better than the previous", since the holder is Sam himself).
        var watch = new PaceWatch { NightBest = () => (12.500, "Dave") };

        WarmUp(watch, 12.5);

        Assert.Null(Lap(watch, "Sam", 12.497));

        var note = Lap(watch, "Sam", 12.460);
        Assert.Equal(PaceNoteKind.NightBest, note!.Kind);
        Assert.Equal(12.497, note.Beaten!.Value, 3);
        Assert.Equal("Sam", note.PreviousHolder);
    }

    [Fact]
    public void A_race_already_under_the_night_best_does_not_hand_out_another_night_best()
    {
        // The old PaceWatchTests scenario: warmed up at 12.0, under Dave's
        // 12.5, so a 12.46 is no longer "the fastest lap of the night".
        var watch = new PaceWatch { NightBest = () => (12.500, "Dave") };

        WarmUp(watch, 12.0);

        Assert.Null(Lap(watch, "Sam", 12.497));

        var note = Lap(watch, "Sam", 12.460);
        Assert.NotEqual(PaceNoteKind.NightBest, note?.Kind);
    }

    [Fact]
    public void The_first_lap_of_a_race_is_judged_against_the_stored_figure()
    {
        var watch = new PaceWatch { LayoutRecord = () => (11.000, "Jen") };

        var note = Lap(watch, "Sam", 10.500);

        Assert.Equal(PaceNoteKind.LayoutRecord, note!.Kind);
        Assert.Equal(11.000, note.Beaten!.Value, 3);
        Assert.Equal("Jen", note.PreviousHolder);
    }
}
