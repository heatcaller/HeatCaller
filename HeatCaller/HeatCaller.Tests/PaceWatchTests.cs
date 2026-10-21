namespace HeatCaller.Tests;

/// <summary>
/// The pace detector: which laps are worth a word, and - mostly - which
/// are not.
///
/// A record falling is the cheapest live-feel line there is, and also the
/// easiest to make ridiculous. "Fastest lap of the race!" on lap one is
/// true of every race ever run, and "a personal best!" for a driver whose
/// first ever lap it is says nothing at all. Most of this file is the
/// silence.
/// </summary>
public sealed class PaceWatchTests
{
    private static PaceNote? Lap(PaceWatch watch, string driver, double seconds) =>
        watch.Saw(new LapSeen(driver, seconds));

    /// <summary>Gets a race past the warm-up with laps nobody would mention.</summary>
    private static void WarmUp(PaceWatch watch, double seconds = 20.0)
    {
        for (var i = 0; i < 4; i++)
        {
            watch.Saw(new LapSeen($"warm-{i}", seconds + i));
        }
    }

    // ------------------------------------------------------------------
    // The silence
    // ------------------------------------------------------------------

    [Fact]
    public void The_first_lap_of_a_race_is_not_the_fastest_lap_of_the_race()
    {
        var watch = new PaceWatch();

        Assert.Null(Lap(watch, "Sam", 12.5));
    }

    [Fact]
    public void Nothing_is_called_during_the_warm_up_laps_however_quick_it_is()
    {
        var watch = new PaceWatch();

        Assert.Null(Lap(watch, "Sam", 20.0));
        Assert.Null(Lap(watch, "Bill", 14.0));
        Assert.Null(Lap(watch, "Dave", 13.0));
    }

    [Fact]
    public void A_driver_with_no_history_at_all_has_no_personal_best_to_beat()
    {
        var watch = new PaceWatch { PersonalBestBefore = _ => null };

        WarmUp(watch);

        // Their very first lap. It is their best, and it is not news.
        Assert.Null(Lap(watch, "NewDriver", 30.0));
    }

    [Fact]
    public void A_record_broken_by_a_few_thousandths_is_not_broken()
    {
        var watch = new PaceWatch { NightBest = () => (12.500, "Dave") };

        // Warmed up at the night's own mark, so neither lap below is the
        // fastest of the race - this is about the night's record and nothing
        // else.
        //
        // WHY (20 Oct 2026): this warmed up at 12.0, but the night's best is
        // now judged against this race's best too, so 12.460 came back as a
        // personal best and the test no longer tested the margin. Not 13.0:
        // then 12.497 would be the fastest lap of the race.
        WarmUp(watch, 12.5);

        // Three thousandths. Inside the loop's own timing, not a drive.
        Assert.Null(Lap(watch, "Sam", 12.497));

        // Four hundredths is a real improvement.
        Assert.Equal(PaceNoteKind.NightBest, Lap(watch, "Sam", 12.460)!.Kind);
    }

    [Fact]
    public void A_car_that_stopped_is_not_a_lap()
    {
        var watch = new PaceWatch();

        WarmUp(watch);

        Assert.Null(Lap(watch, "Sam", 0));
        Assert.Null(Lap(watch, "Sam", -4));
        Assert.Null(Lap(watch, "Sam", 9_999));
        Assert.Equal(4, watch.LapsSeen);
    }

    [Fact]
    public void One_driver_chipping_away_at_their_own_best_is_mentioned_once()
    {
        var watch = new PaceWatch { PersonalBestBefore = d => d == "Sam" ? 14.000 : null };

        // Somebody quicker has already been round, so Sam's laps are a
        // personal best and nothing grander.
        WarmUp(watch, 12.0);

        Assert.Equal(PaceNoteKind.PersonalBest, Lap(watch, "Sam", 13.900)!.Kind);

        // Three more, each quicker than the last. All true, none of them
        // worth interrupting the race for a second time.
        Assert.Null(Lap(watch, "Sam", 13.850));
        Assert.Null(Lap(watch, "Sam", 13.800));
        Assert.Null(Lap(watch, "Sam", 13.750));
    }

    // ------------------------------------------------------------------
    // The things worth saying
    // ------------------------------------------------------------------

    [Fact]
    public void The_fastest_lap_of_the_race_is_called_once_the_race_has_some_laps_in_it()
    {
        var watch = new PaceWatch();

        WarmUp(watch, 20.0);

        var note = Lap(watch, "Sam", 12.0);

        Assert.NotNull(note);
        Assert.Equal(PaceNoteKind.SessionBest, note!.Kind);
        Assert.Equal(20.0, note.Beaten!.Value, 3);
        Assert.Equal("warm-0", note.PreviousHolder);
    }

    [Fact]
    public void The_layout_record_is_the_biggest_thing_a_lap_can_be()
    {
        var watch = new PaceWatch
        {
            PersonalBestBefore = _ => 12.000,
            NightBest = () => (11.500, "Dave"),
            LayoutRecord = () => (11.000, "Jen"),
        };

        WarmUp(watch);

        var note = Lap(watch, "Sam", 10.800);

        Assert.NotNull(note);
        Assert.Equal(PaceNoteKind.LayoutRecord, note!.Kind);
        Assert.Equal("Jen", note.PreviousHolder);
        Assert.Equal(11.000, note.Beaten!.Value, 3);
    }

    [Fact]
    public void A_lap_that_breaks_the_layout_record_on_the_first_lap_still_counts()
    {
        // The warm-up rule is about "fastest of the race so far", which
        // needs a race to compare against. A track record is a record
        // whenever it falls.
        var watch = new PaceWatch { LayoutRecord = () => (11.000, "Jen") };

        Assert.Equal(PaceNoteKind.LayoutRecord, Lap(watch, "Sam", 10.500)!.Kind);
    }

    [Fact]
    public void The_fastest_lap_of_the_night_outranks_the_fastest_of_the_race()
    {
        var watch = new PaceWatch { NightBest = () => (12.000, "Dave") };

        WarmUp(watch);

        var note = Lap(watch, "Sam", 11.800);

        Assert.Equal(PaceNoteKind.NightBest, note!.Kind);
        Assert.Equal("Dave", note.PreviousHolder);
    }

    [Fact]
    public void A_driver_beating_their_own_mark_without_leading_the_race_is_still_a_story()
    {
        var watch = new PaceWatch { PersonalBestBefore = d => d == "Sam" ? 15.000 : 9.000 };

        WarmUp(watch, 10.0);

        // Nowhere near the front, but quicker than Sam has ever gone.
        var note = Lap(watch, "Sam", 14.500);

        Assert.NotNull(note);
        Assert.Equal(PaceNoteKind.PersonalBest, note!.Kind);
        Assert.Equal(15.000, note.Beaten!.Value, 3);
    }

    [Fact]
    public void A_driver_with_no_history_can_still_beat_their_own_laps_from_this_race()
    {
        var watch = new PaceWatch { PersonalBestBefore = _ => null };

        WarmUp(watch, 20.0);

        Assert.Null(Lap(watch, "NewDriver", 25.0));

        var note = Lap(watch, "NewDriver", 23.0);

        Assert.NotNull(note);
        Assert.Equal(PaceNoteKind.PersonalBest, note!.Kind);
        Assert.Equal(25.0, note.Beaten!.Value, 3);
    }

    // ------------------------------------------------------------------
    // Between races
    // ------------------------------------------------------------------

    [Fact]
    public void A_new_race_forgets_the_last_ones_laps_but_not_the_nights_record()
    {
        var watch = new PaceWatch { NightBest = () => (11.000, "Dave") };

        WarmUp(watch, 20.0);

        Assert.Equal(PaceNoteKind.SessionBest, Lap(watch, "Sam", 12.0)!.Kind);

        watch.Reset();

        Assert.Equal(0, watch.LapsSeen);

        // The new race starts from nothing again - no session best to
        // beat, and the warm-up to get through.
        Assert.Null(Lap(watch, "Sam", 12.5));

        // But the night's record is read fresh from the store behind it,
        // and still stands.
        WarmUp(watch, 20.0);

        Assert.Equal(PaceNoteKind.NightBest, Lap(watch, "Sam", 10.5)!.Kind);
    }

    [Fact]
    public void A_driver_may_have_a_personal_best_in_each_race_of_the_night()
    {
        var watch = new PaceWatch { PersonalBestBefore = d => d == "Sam" ? 14.000 : null };

        WarmUp(watch, 12.0);

        Assert.Equal(PaceNoteKind.PersonalBest, Lap(watch, "Sam", 13.5)!.Kind);
        Assert.Null(Lap(watch, "Sam", 13.4));

        watch.Reset();
        WarmUp(watch, 12.0);

        // In the main, with the store now holding the quicker mark.
        Assert.Equal(PaceNoteKind.PersonalBest, Lap(watch, "Sam", 13.0)!.Kind);
    }
}
