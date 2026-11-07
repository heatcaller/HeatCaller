namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 11 (5 Nov 2026), Achievements area:
///  - achievements.json with a null driver or null list is repaired on load;
///  - the pace detector judges a lap against the layout record as it stood
///    at the green, not the one Race Control has just rewritten to that lap;
///  - a disqualified car's fast lap is not the race's fastest for badges;
///  - nor is it the night's best for the pace detector.
/// </summary>
public sealed class BugHunt11_AchievementsTests : IDisposable
{
    // Declared first: everything below reads and writes where it points.
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- 1. A hand-edited achievements.json -----------------------------------

    [Fact]
    public void A_null_driver_or_null_lists_in_achievements_json_are_repaired_on_load()
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(_data.PathOf("achievements.json"), """
            [
              null,
              { "Transponder": "100", "Unlocked": null, "TrackRecords": null, "RacesFinished": 4 },
              { "Transponder": null, "Unlocked": [ null, { "Id": "first-race" } ], "TrackRecords": [ null, "Infield" ] }
            ]
            """);

        var store = AchievementStore.Load();

        Assert.False(store.LoadedUnreadable);
        Assert.Equal(2, store.All.Count);

        // Every one of these threw a NullReferenceException before.
        var driver = store.For("100");
        Assert.Equal(4, driver.RacesFinished);
        Assert.False(driver.Has(AchievementCatalogue.FirstRace));
        Assert.True(store.Award("100", AchievementCatalogue.FirstRace, "s1"));
        Assert.Empty(driver.TrackRecords);

        var nameless = store.All.Single(d => d.Transponder == string.Empty);
        Assert.Single(nameless.Unlocked);
        Assert.Equal(new[] { "Infield" }, nameless.TrackRecords);

        // And a whole race's badges can be worked out against it.
        var race = Race(Entry("100", 1, 9_800), Entry("200", 2, 9_900));
        var unlocks = AchievementEvaluator.EvaluateFinishedRace(race, Array.Empty<SessionRecord>(), store);
        Assert.Contains(unlocks, u => u.Transponder == "200" && u.Badge.Id == AchievementCatalogue.FirstRace);
    }

    // --- 2. The layout record moves under the pace detector -------------------

    [Fact]
    public void A_lap_that_breaks_the_layout_record_is_called_against_the_old_holder()
    {
        // Race Control writes the record onto the shared layout as the lap
        // crosses the line - before the announcer feeds PaceWatch the lap.
        var layout = new TrackLayout { Name = "Infield", RecordLapMs = 10_000, RecordHolderName = "Jen" };
        var watch = new PaceWatch
        {
            LayoutRecord = () => PaceHistory.LayoutRecordFor(new[] { layout }, "Infield"),
        };
        watch.Reset();

        watch.Saw(new LapSeen("DAVE", 10.400));

        layout.RecordLapMs = 9_950;
        layout.RecordHolderName = "Sam";

        var note = watch.Saw(new LapSeen("SAM", 9.950));

        Assert.NotNull(note);
        Assert.Equal(PaceNoteKind.LayoutRecord, note!.Kind);
        Assert.Equal("Jen", note.PreviousHolder);
        Assert.Equal(10.000, note.Beaten!.Value, 3);
    }

    [Fact]
    public void A_rental_under_the_record_is_not_called_the_fastest_lap_ever_on_the_layout()
    {
        // Race Control refuses a rental's record, so the store is unchanged.
        var watch = new PaceWatch
        {
            LayoutRecord = () => (10.000, "Jen"),
            CanHoldRecord = transponder => transponder != "RENTAL",
        };
        watch.Reset();

        var rental = watch.Saw(new LapSeen("RENTAL", 9.950));
        Assert.NotEqual(PaceNoteKind.LayoutRecord, rental?.Kind);

        // And the rental's lap is not the bar a real driver has to beat for it.
        var sam = watch.Saw(new LapSeen("SAM", 9.970));
        Assert.NotNull(sam);
        Assert.Equal(PaceNoteKind.LayoutRecord, sam!.Kind);
        Assert.Equal("Jen", sam.PreviousHolder);
    }

    [Fact]
    public void The_record_watch_counts_the_breaking_lap_as_the_record_gone()
    {
        var layout = new TrackLayout { Name = "Infield", RecordLapMs = 10_000, RecordHolderName = "Jen" };
        var watch = new RecordWatch
        {
            LayoutRecord = () => PaceHistory.LayoutRecordFor(new[] { layout }, "Infield"),
        };
        watch.Reset();

        foreach (var lap in new[] { 10.300, 10.200, 10.100 })
        {
            watch.Saw(new LapSeen("SAM", lap));
        }

        // Sam breaks it; Race Control has already written it by the time
        // the watch sees the lap.
        layout.RecordLapMs = 9_950;
        layout.RecordHolderName = "Sam";
        Assert.Null(watch.Saw(new LapSeen("SAM", 9.950)));

        // A near lap afterwards must not say his own record is in danger.
        Assert.Null(watch.Saw(new LapSeen("SAM", 9.960)));
        Assert.Null(watch.Saw(new LapSeen("SAM", 9.955)));
    }

    [Fact]
    public void The_record_watch_counts_the_first_record_on_a_new_layout_as_gone()
    {
        // No record stands at the green; Sam's first timed lap is the
        // quickest and Race Control writes it as the layout's first record.
        var layout = new TrackLayout { Name = "Infield" };
        var watch = new RecordWatch
        {
            LayoutRecord = () => PaceHistory.LayoutRecordFor(new[] { layout }, "Infield"),
        };
        watch.Reset();

        layout.RecordLapMs = 12_000;
        layout.RecordHolderName = "Sam";
        Assert.Null(watch.Saw(new LapSeen("SAM", 12.000)));

        Assert.Null(watch.Saw(new LapSeen("SAM", 12.020)));
        Assert.Null(watch.Saw(new LapSeen("SAM", 12.010)));
        Assert.Null(watch.Saw(new LapSeen("SAM", 12.005)));
    }

    [Fact]
    public void The_record_watch_never_reset_still_counts_the_lap_that_set_the_record()
    {
        // The console makes its watch on the first lap fed, so Reset has not
        // run when the first mark it ever reads is the lap that set it.
        var layout = new TrackLayout { Name = "Infield", RecordLapMs = 12_000, RecordHolderName = "Sam" };
        var watch = new RecordWatch
        {
            LayoutRecord = () => PaceHistory.LayoutRecordFor(new[] { layout }, "Infield"),
        };

        Assert.Null(watch.Saw(new LapSeen("SAM", 12.000)));

        Assert.Null(watch.Saw(new LapSeen("SAM", 12.020)));
        Assert.Null(watch.Saw(new LapSeen("SAM", 12.010)));
        Assert.Null(watch.Saw(new LapSeen("SAM", 12.005)));
    }

    // --- 3. A disqualified car's fast lap in the badges -----------------------

    [Fact]
    public void A_disqualified_cars_fast_lap_does_not_take_Fastest_Lap_off_the_best_legal_lap()
    {
        var jen = Entry("JEN", 4, 9_700);
        jen.Outcome = ResultEditor.Dq;
        jen.Laps = 0;

        var race = Race(
            Entry("SAM", 1, 9_800),
            Entry("DAVE", 2, 9_900),
            Entry("EVE", 3, 10_000),
            jen);

        var unlocks = AchievementEvaluator.EvaluateFinishedRace(race, Array.Empty<SessionRecord>(), AchievementStore.Load());

        Assert.Contains(unlocks, u => u.Transponder == "SAM" && u.Badge.Id == AchievementCatalogue.FastestLap);
        Assert.DoesNotContain(unlocks, u => u.Transponder == "JEN");
    }

    [Fact]
    public void A_disqualified_cars_getaway_does_not_keep_Rocket_Start_from_everyone()
    {
        var jen = Entry("JEN", 4, 9_700, opening: 2_000);
        jen.Outcome = ResultEditor.Dq;
        jen.Laps = 0;

        var race = Race(
            Entry("SAM", 1, 9_800, opening: 2_800),
            Entry("DAVE", 2, 9_900, opening: 2_900),
            Entry("EVE", 3, 10_000, opening: 2_500),
            jen);

        var unlocks = AchievementEvaluator.EvaluateFinishedRace(race, Array.Empty<SessionRecord>(), AchievementStore.Load());

        Assert.Contains(unlocks, u => u.Transponder == "EVE" && u.Badge.Id == AchievementCatalogue.RocketStart);
    }

    [Fact]
    public void A_disqualified_cars_fast_lap_is_not_a_quicker_rival_for_Slow_and_Steady()
    {
        // 6 Nov 2026: only Dave's legal lap was quicker than Sam's.
        var jen = Entry("JEN", 3, 9_700);
        jen.Outcome = ResultEditor.Dq;
        jen.Laps = 0;

        var race = Race(
            Entry("SAM", 1, 10_200),
            Entry("DAVE", 2, 10_000),
            jen);

        var unlocks = AchievementEvaluator.EvaluateFinishedRace(race, Array.Empty<SessionRecord>(), AchievementStore.Load());

        Assert.DoesNotContain(unlocks, u => u.Transponder == "SAM" && u.Badge.Id == AchievementCatalogue.SlowAndSteady);
    }

    // --- 4. A disqualified car's fast lap in the night's best -----------------

    [Fact]
    public void A_disqualified_result_does_not_hold_the_nights_best()
    {
        var heat = new SessionRecord { TrackLayoutName = "Infield" };
        heat.Results.Add(new SessionResultEntry { Transponder = "JEN", DriverName = "Jen", FastLapMs = 9_700, Outcome = ResultEditor.Dq });
        heat.Results.Add(new SessionResultEntry { Transponder = "DAVE", DriverName = "Dave", FastLapMs = 9_900, Laps = 12 });

        var best = PaceHistory.NightBestFor(new[] { heat }, "Infield");

        Assert.NotNull(best);
        Assert.Equal(9.900, best!.Value.Seconds, 3);
        Assert.Equal("Dave", best.Value.Holder);
    }

    // --- Helpers ---------------------------------------------------------------

    private static SessionRecord Race(params SessionResultEntry[] results) => new()
    {
        Label = "A-Main",
        SessionType = RaceSessionType.Race,
        TrackLayoutName = "Infield",
        StartedAtUtc = DateTime.UtcNow.AddMinutes(-6),
        EndedAtUtc = DateTime.UtcNow,
        Results = results.ToList(),
    };

    private static SessionResultEntry Entry(string transponder, int position, long fast, long opening = 3_000) => new()
    {
        Transponder = transponder,
        DriverName = transponder,
        Position = position,
        StartPosition = position,
        Laps = 12,
        ElapsedMs = 300_000 + position * 1_000,
        FastLapMs = fast,
        LapTimesMs = Enumerable.Repeat(fast + 500, 11).Append(fast).Prepend(opening).ToList(),
    };
}
