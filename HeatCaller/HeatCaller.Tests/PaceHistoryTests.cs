namespace HeatCaller.Tests;

/// <summary>
/// The pace detector's history, and the rules that make it comparable.
///
/// WHY IT MATTERS THAT THESE ARE TESTED. PaceWatch's three history
/// callbacks were wired only in its own tests and never in the app, so
/// until 15 Oct 2026 the commentary could never say what a lap beat or who
/// held it. Wiring them introduces a new way to be wrong, which is the
/// point of this file: a figure from a DIFFERENT layout, or from a demo
/// race, or from a ghost, would have the announcer comparing a lap to
/// something it cannot be compared to - in front of a room that watched
/// it happen.
/// </summary>
public sealed class PaceHistoryTests
{
    /// <summary>
    /// Plain layouts rather than a store: TrackLayoutStore's constructor is
    /// private and its Upsert writes to %AppData%, so a test that built one
    /// would be writing into the club's own file.
    /// </summary>
    private static IReadOnlyList<TrackLayout> Layouts(params (string Name, long RecordMs, string Holder)[] layouts) =>
        layouts.Select(one => new TrackLayout
        {
            Name = one.Name,
            RecordLapMs = one.RecordMs,
            RecordHolderName = one.Holder,
        }).ToList();

    private static SessionRecord Race(string layout, bool demo = false, params (string Transponder, string Name, long FastMs)[] results)
    {
        var record = new SessionRecord { TrackLayoutName = layout, IsDemo = demo };

        foreach (var (transponder, name, fast) in results)
        {
            record.Results.Add(new SessionResultEntry
            {
                Transponder = transponder,
                DriverName = name,
                FastLapMs = fast,
            });
        }

        return record;
    }

    // ------------------------------------------------------------------
    //  The layout's own record
    // ------------------------------------------------------------------

    [Fact]
    public void The_record_for_the_selected_layout_is_the_one_that_comes_back()
    {
        var found = PaceHistory.LayoutRecordFor(
            Layouts(("Infield", 5040, "Dave"), ("Full circuit", 9100, "Jen")),
            "Full circuit");

        Assert.NotNull(found);
        Assert.Equal(9.100, found!.Value.Seconds, 3);
        Assert.Equal("Jen", found.Value.Holder);
    }

    [Fact]
    public void A_name_is_matched_whatever_case_and_spacing_it_was_stored_in()
    {
        // The name comes off a combo box on one page and a settings file
        // on another, and a club renaming a layout re-saves it.
        var layouts = Layouts(("Infield", 5040, "Dave"));

        Assert.NotNull(PaceHistory.LayoutRecordFor(layouts, "  infield  "));
        Assert.NotNull(PaceHistory.LayoutRecordFor(layouts, "INFIELD"));
    }

    [Fact]
    public void No_layout_selected_claims_no_record()
    {
        // Null means no record is claimed rather than a guess at one - the
        // rule NightMoments.ForRace states for the same figure.
        var layouts = Layouts(("Infield", 5040, "Dave"));

        Assert.Null(PaceHistory.LayoutRecordFor(layouts, null));
        Assert.Null(PaceHistory.LayoutRecordFor(layouts, "   "));
        Assert.Null(PaceHistory.LayoutRecordFor(layouts, "A layout nobody has made"));
        Assert.Null(PaceHistory.LayoutRecordFor(null, "Infield"));
    }

    [Fact]
    public void A_layout_with_no_mark_standing_on_it_claims_no_record()
    {
        Assert.Null(PaceHistory.LayoutRecordFor(Layouts(("Infield", 0, string.Empty)), "Infield"));
    }

    [Fact]
    public void A_record_nobody_is_recorded_as_holding_still_comes_back()
    {
        // The figure is the useful half; the holder is the story. One
        // without the other is still worth saying.
        var found = PaceHistory.LayoutRecordFor(Layouts(("Infield", 5040, string.Empty)), "Infield");

        Assert.NotNull(found);
        Assert.Equal(string.Empty, found!.Value.Holder);
    }

    // ------------------------------------------------------------------
    //  The night's best
    // ------------------------------------------------------------------

    [Fact]
    public void The_quickest_lap_of_the_night_on_this_layout_comes_back_with_who_turned_it()
    {
        var found = PaceHistory.NightBestFor(
            new[]
            {
                Race("Infield", false, ("A1", "Carl", 5300), ("A2", "Dave", 5180)),
                Race("Infield", false, ("A1", "Carl", 5120), ("A3", "Eve", 5400)),
            },
            "Infield");

        Assert.NotNull(found);
        Assert.Equal(5.120, found!.Value.Seconds, 3);
        Assert.Equal("Carl", found.Value.Holder);
    }

    [Fact]
    public void A_lap_from_a_different_layout_is_not_the_nights_best()
    {
        // THE RULE WORTH THE TEST. A club that moves the track between
        // classes, or runs two configurations in an evening, would
        // otherwise have the announcer calling a lap "the fastest of the
        // night" against a different track - wrong about something the
        // room watched.
        var found = PaceHistory.NightBestFor(
            new[]
            {
                Race("Full circuit", false, ("A1", "Carl", 4100)),
                Race("Infield", false, ("A2", "Dave", 5300)),
            },
            "Infield");

        Assert.NotNull(found);
        Assert.Equal(5.300, found!.Value.Seconds, 3);
        Assert.Equal("Dave", found.Value.Holder);
    }

    [Fact]
    public void A_demo_race_never_holds_the_nights_best()
    {
        // Demo Mode produces invented lap times, and a club that has
        // ticked "save demo races to history" would otherwise have one
        // holding the night's best for every real race after it.
        var found = PaceHistory.NightBestFor(
            new[]
            {
                Race("Infield", true, ("A1", "Carl", 3000)),
                Race("Infield", false, ("A2", "Dave", 5300)),
            },
            "Infield");

        Assert.NotNull(found);
        Assert.Equal(5.300, found!.Value.Seconds, 3);
        Assert.Equal("Dave", found.Value.Holder);
    }

    [Fact]
    public void A_ghost_never_holds_the_nights_best()
    {
        // A ghost is a replay of an old lap put on the track for somebody
        // to race - the same exclusion RacingField makes for the running
        // order.
        var ghost = Ghosts.Prefix + "9999";

        var found = PaceHistory.NightBestFor(
            new[] { Race("Infield", false, (ghost, "Ghost of Carl", 3000), ("A2", "Dave", 5300)) },
            "Infield");

        Assert.NotNull(found);
        Assert.Equal(5.300, found!.Value.Seconds, 3);
        Assert.Equal("Dave", found.Value.Holder);
    }

    [Fact]
    public void A_driver_with_no_recorded_best_lap_is_skipped_rather_than_counted_as_nought()
    {
        // A nought would be the quickest lap of the night by a mile, and
        // every retirement has one.
        var found = PaceHistory.NightBestFor(
            new[] { Race("Infield", false, ("A1", "Carl", 0), ("A2", "Dave", 5300)) },
            "Infield");

        Assert.NotNull(found);
        Assert.Equal(5.300, found!.Value.Seconds, 3);
    }

    [Fact]
    public void A_night_with_nothing_run_on_this_layout_claims_nothing()
    {
        Assert.Null(PaceHistory.NightBestFor(Array.Empty<SessionRecord>(), "Infield"));
        Assert.Null(PaceHistory.NightBestFor(null, "Infield"));
        Assert.Null(PaceHistory.NightBestFor(new[] { Race("Infield", false) }, "Infield"));
        Assert.Null(PaceHistory.NightBestFor(new[] { Race("Full circuit", false, ("A1", "Carl", 5000)) }, "Infield"));
    }

    [Fact]
    public void No_layout_selected_claims_no_nights_best_either()
    {
        var tonight = new[] { Race("Infield", false, ("A1", "Carl", 5300)) };

        Assert.Null(PaceHistory.NightBestFor(tonight, null));
        Assert.Null(PaceHistory.NightBestFor(tonight, "  "));
    }

    // ------------------------------------------------------------------
    //  What the detector then does with them
    // ------------------------------------------------------------------

    [Fact]
    public void A_wired_detector_can_finally_say_what_a_lap_beat()
    {
        // The whole point of the fix, end to end: before this the only
        // note PaceWatch could ever produce in the field was SessionBest.
        var watch = new PaceWatch
        {
            LayoutRecord = () => PaceHistory.LayoutRecordFor(Layouts(("Infield", 5040, "Jen")), "Infield"),
            NightBest = () => PaceHistory.NightBestFor(
                new[] { Race("Infield", false, ("A2", "Dave", 5180)) },
                "Infield"),
        };

        var note = watch.Saw(new LapSeen("A1", 5.000));

        Assert.NotNull(note);
        Assert.Equal(PaceNoteKind.LayoutRecord, note!.Kind);
        Assert.Equal(5.040, note.Beaten!.Value, 3);
        Assert.Equal("Jen", note.PreviousHolder);
    }

    [Fact]
    public void The_nights_best_is_reported_when_the_layout_record_still_stands()
    {
        var watch = new PaceWatch
        {
            LayoutRecord = () => PaceHistory.LayoutRecordFor(Layouts(("Infield", 4500, "Jen")), "Infield"),
            NightBest = () => PaceHistory.NightBestFor(
                new[] { Race("Infield", false, ("A2", "Dave", 5180)) },
                "Infield"),
        };

        var note = watch.Saw(new LapSeen("A1", 5.000));

        Assert.NotNull(note);
        Assert.Equal(PaceNoteKind.NightBest, note!.Kind);
        Assert.Equal("Dave", note.PreviousHolder);
    }
}
