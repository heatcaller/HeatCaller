namespace HeatCaller.Tests;

/// <summary>
/// Golden races for the timing layer (23 Oct 2026, P0): whole heats played
/// through a fresh RaceControlSession by the replay harness, on the replay
/// clock, with every pass on time. Each answer is worked out by hand in the
/// comments, from the crossing times in the script.
///
/// WHY. These are the results the timing layer must never change. P1 judges
/// each pass by its arrival, P4 moves the engine onto its own thread - both
/// are proved against these, and the freeze tests (TimingLayer_P0_FreezeTests)
/// play the same races with passes held up and expect the same answers.
///
/// The clock: the green is at 20,000 on the replay clock (Start stages at
/// 10,000 and counts down from 15,000). A car "crossing at 21,000" is seen by
/// the decoder at 21,000 on its own clock (plus a fixed offset - see
/// ReplayScript) and reaches the app 3 ms later. Elapsed time is decoder
/// time since the green, so a crossing at 53,000 is 33,000 into the race.
/// The 500 ms tick runs on every multiple of 500.
/// </summary>
public sealed class TimingLayer_P0_GoldenTests
{
    internal const long Green = 20_000;

    private static ReplayResult OnTime(ReplayScript script, Action<RaceControlSession>? setup = null, long untilMs = 200_000) =>
        ReplayHarness.Run(ReplayDelivery.OnTime(script.Inputs, untilMs), setup);

    // --- Alternate transponders --------------------------------------------------

    /// <summary>
    /// 100 owns 200 (spare) and is out on the spare alone; 400 owns 500 and
    /// both cars are out, 400 first. Five laps, finish on the next crossing.
    /// </summary>
    internal static ReplayScript AlternatesHeat() => new ReplayScript()
        .Start(Green)
        .Laps("200", 21_000, 8_000, 5)  // 21,000 ... 53,000 - under 100
        .Laps("300", 21_500, 8_200, 5)  // 21,500 ... 54,300
        .Laps("400", 22_000, 9_000, 5)  // 22,000 ... 58,000
        .Laps("500", 22_500, 9_500, 5); // 22,500 ... 60,500 - its own car

    internal static void AlternatesRules(RaceControlSession race)
    {
        race.LapLimit = 5;
        race.AlternateTransponderOwner = id => id switch
        {
            "200" => "100",
            "500" => "400",
            _ => null,
        };
    }

    [Fact]
    public void A_spare_alone_is_merged_into_its_owner_and_a_spare_out_with_its_owner_is_its_own_car()
    {
        var result = OnTime(AlternatesHeat(), AlternatesRules);

        // 100's fifth crossing (53,000) reaches the limit and takes the flag;
        // the others take it on their next crossing - each its fifth - and
        // the last one in (500 at 60,500, arriving 60,503) ends the race.
        Assert.Equal(RaceState.Finished, result.State);
        Assert.Equal(new[] { "100", "300", "400", "500" }, result.Rows.Select(r => r.Transponder));
        Assert.DoesNotContain(result.Rows, r => r.Transponder == "200");
        Assert.Equal("200", result.Race.HeardAs("100"));

        Assert.Equal(new ReplayRow("100", 1, 5, 8_000, 33_000, 0, 0, true, "1000 8000 8000 8000 8000"), result.Row("100"));
        Assert.Equal(new ReplayRow("300", 2, 5, 8_200, 34_300, 0, 0, true, "1500 8200 8200 8200 8200"), result.Row("300"));
        Assert.Equal(new ReplayRow("400", 3, 5, 9_000, 38_000, 0, 0, true, "2000 9000 9000 9000 9000"), result.Row("400"));
        Assert.Equal(new ReplayRow("500", 4, 5, 9_500, 40_500, 0, 0, true, "2500 9500 9500 9500 9500"), result.Row("500"));

        Assert.Equal(new[] { "100", "300", "400", "500" }, result.FlagOrder);
        Assert.Equal(40_503, result.ElapsedMs);
    }

    // --- A jump start -------------------------------------------------------------

    /// <summary>
    /// 300 goes at 18,500, in the countdown, 1.5 s before the green; the class
    /// gives 5 s. It is past the line at the green, so it is credited with the
    /// start lap there, and pays the 5 s on its first counted lap. Four laps.
    /// </summary>
    internal static ReplayScript JumpStartHeat() => new ReplayScript()
        .Start(Green)
        .Pass(18_500, "300")
        .Laps("100", 21_000, 8_000, 4)  // 21,000 ... 45,000
        .Laps("300", 27_500, 9_000, 3); // 27,500, 36,500, 45,500

    internal static void JumpStartRules(RaceControlSession race)
    {
        race.LapLimit = 4;
        race.JumpStartPenaltyMs = 5_000;
    }

    [Fact]
    public void A_jump_start_is_credited_with_the_start_lap_and_pays_its_penalty()
    {
        var result = OnTime(JumpStartHeat(), JumpStartRules);

        Assert.Equal(RaceState.Finished, result.State);
        Assert.True(result.Race.HasJumpStarted("300"));
        Assert.False(result.Race.HasJumpStarted("100"));

        // 100 reaches four laps at 45,000 and takes the flag.
        Assert.Equal(new ReplayRow("100", 1, 4, 8_000, 25_000, 0, 0, true, "1000 8000 8000 8000"), result.Row("100"));

        // 300: the credit (0), then 9 s laps measured from 18,500. The first
        // of them runs from the grid and does not compete for best. Elapsed
        // 25,500 since the green plus the 5 s.
        Assert.Equal(new ReplayRow("300", 2, 4, 9_000, 30_500, 5_000, 0, true, "0 9000 9000 9000"), result.Row("300"));
        Assert.Equal(new[] { "100", "300" }, result.FlagOrder);
    }

    // --- A red flag ---------------------------------------------------------------

    /// <summary>
    /// A red at 32,000 and the restart at 50,000. A crossing under the red is
    /// ignored; the first one after it is a long lap through the stoppage.
    /// Five laps.
    /// </summary>
    internal static ReplayScript RedFlagHeat() => new ReplayScript()
        .Start(Green)
        .Pass(21_000, "100").Pass(29_000, "100").Pass(34_000, "100")
        .Pass(52_000, "100").Pass(60_000, "100").Pass(68_000, "100")
        .Pass(21_500, "300").Pass(30_000, "300").Pass(35_000, "300")
        .Pass(53_000, "300").Pass(61_500, "300").Pass(70_000, "300")
        .Red(32_000)
        .Resume(50_000);

    internal static void FiveLaps(RaceControlSession race) => race.LapLimit = 5;

    [Fact]
    public void A_red_flag_stops_the_count_and_the_clock_and_the_race_runs_on_after_it()
    {
        var result = OnTime(RedFlagHeat(), FiveLaps);

        Assert.Equal(RaceState.Finished, result.State);

        // 34,000 and 35,000 came under the red and count for nothing; the lap
        // through the stoppage is 23 s for both.
        Assert.Equal(new ReplayRow("100", 1, 5, 8_000, 48_000, 0, 0, true, "1000 8000 23000 8000 8000"), result.Row("100"));
        Assert.Equal(new ReplayRow("300", 2, 5, 8_500, 50_000, 0, 0, true, "1500 8500 23000 8500 8500"), result.Row("300"));

        // The race clock leaves the 18 s red out: 300's last crossing arrived
        // at 70,003, 50,003 after the green.
        Assert.Equal(32_003, result.ElapsedMs);
    }

    // --- Finish on the next crossing, on a time limit -------------------------------

    /// <summary>
    /// A 60 s race: the limit falls at 80,000. 300 crosses at 79,600 - four
    /// tenths before it - so that is an ordinary lap and 300 runs one more.
    /// Everyone takes the flag on their first crossing after 80,000.
    /// </summary>
    internal static ReplayScript TimeLimitHeat() => new ReplayScript()
        .Start(Green)
        .Laps("100", 21_000, 8_000, 9)  // ... 77,000 (8), 85,000 (9: flag)
        .Laps("300", 23_600, 8_000, 9)  // ... 79,600 (8), 87,600 (9: flag)
        .Laps("400", 22_500, 9_000, 8); // ... 76,500 (7), 85,500 (8: flag)

    internal static void SixtySeconds(RaceControlSession race) => race.TimeLimit = TimeSpan.FromSeconds(60);

    [Fact]
    public void On_a_time_limit_each_car_takes_the_flag_on_its_first_crossing_after_the_limit()
    {
        var result = OnTime(TimeLimitHeat(), SixtySeconds);

        Assert.Equal(RaceState.Finished, result.State);
        Assert.Equal(new ReplayRow("100", 1, 9, 8_000, 65_000, 0, 0, true, "1000 8000 8000 8000 8000 8000 8000 8000 8000"), result.Row("100"));
        Assert.Equal(new ReplayRow("300", 2, 9, 8_000, 67_600, 0, 0, true, "3600 8000 8000 8000 8000 8000 8000 8000 8000"), result.Row("300"));
        Assert.Equal(new ReplayRow("400", 3, 8, 9_000, 65_500, 0, 0, true, "2500 9000 9000 9000 9000 9000 9000 9000"), result.Row("400"));
        Assert.Equal(new[] { "100", "400", "300" }, result.FlagOrder);
        Assert.Equal(67_603, result.ElapsedMs);
    }

    // --- A staggered start, each driver on their own clock --------------------------

    /// <summary>
    /// A staggered grid on OwnRelease: 100 goes on the green, 300 five
    /// seconds later, and each gets 30 s of their own. 300 crosses at 23,000,
    /// before its release - a jump - and is timed from its release after that.
    /// </summary>
    internal static ReplayScript StaggeredHeat() => new ReplayScript()
        .Command(12_000, "SetReleaseSchedule", race => race.SetReleaseSchedule(new Dictionary<string, long> { ["100"] = 0, ["300"] = 5_000 }))
        .Start(Green)
        .Pass(23_000, "300")
        .Laps("100", 21_000, 8_000, 5)  // ... 45,000 (4), 53,000 (5: flag)
        .Laps("300", 26_000, 8_100, 5); // ... 50,300 (4: still in its window), 58,400 (5: flag)

    internal static void StaggeredRules(RaceControlSession race)
    {
        race.TimeLimit = TimeSpan.FromSeconds(30);
        race.StartType = RaceStartType.StaggeredGrid;
        race.StaggerClockMode = StaggerClock.OwnRelease;
    }

    [Fact]
    public void A_staggered_car_gets_its_own_full_window_and_a_crossing_before_its_release_is_a_jump()
    {
        var result = OnTime(StaggeredHeat(), StaggeredRules);

        Assert.Equal(RaceState.Finished, result.State);
        Assert.True(result.Race.HasJumpStarted("300"));

        // The checkered comes out at 50,000. 100 is out of time there and
        // takes it at 53,000. 300's window runs to 55,000: its 50,300 crossing
        // is an ordinary lap and 58,400 takes the flag. 300 is timed from its
        // release: 58,400 - 20,000 - 5,000.
        Assert.Equal(new ReplayRow("100", 1, 5, 8_000, 33_000, 0, 0, true, "1000 8000 8000 8000 8000"), result.Row("100"));
        Assert.Equal(new ReplayRow("300", 2, 5, 8_100, 33_400, 0, 0, true, "1000 8100 8100 8100 8100"), result.Row("300"));
        Assert.Equal(new[] { "100", "300" }, result.FlagOrder);
        Assert.Equal(38_403, result.ElapsedMs);
    }

    // --- Warnings and a time penalty -------------------------------------------------

    /// <summary>
    /// The ladder: a warning, then +5 s, then the black flag. 300 is warned
    /// twice (+5 s); 100 is given +10 s from the button; 400 is warned three
    /// times, black-flagged, and comes in after four laps. Five laps.
    /// </summary>
    internal static ReplayScript WarningsHeat() => new ReplayScript()
        .Start(Green)
        .Laps("100", 21_000, 8_000, 5)  // ... 53,000
        .Laps("300", 21_500, 8_200, 5)  // ... 54,300
        .Laps("400", 22_000, 9_000, 4)  // ... 49,000, then in
        .Command(25_000, "Warning", race => race.IssueWarning("300", "Blocking"))
        .Command(26_000, "Warning", race => race.IssueWarning("300", "Blocking again"))
        .Command(27_000, "Penalty", race => race.AdjustPenaltyTime("100", 10_000))
        .Command(32_000, "Warning", race => race.IssueWarning("400", "Contact"))
        .Command(32_100, "Warning", race => race.IssueWarning("400", "Contact"))
        .Command(32_200, "Warning", race => race.IssueWarning("400", "Contact"));

    internal static void WarningsRules(RaceControlSession race)
    {
        race.LapLimit = 5;
        race.WarningLadder = new List<WarningStep>
        {
            new(WarningAction.Warning),
            new(WarningAction.TimePenalty, 5),
            new(WarningAction.BlackFlag),
        };
    }

    [Fact]
    public void Warnings_and_penalties_move_cars_down_and_a_black_flagged_car_is_not_waited_for()
    {
        var result = OnTime(WarningsHeat(), WarningsRules);

        Assert.Equal(RaceState.Finished, result.State);

        // 100 is first to five laps but carries 10 s; 300 carries 5 s.
        // 33,000 + 10,000 against 34,300 + 5,000: 300 wins. The race ends on
        // 300's flag - 400 is black-flagged and not waited for.
        Assert.Equal(new ReplayRow("300", 1, 5, 8_200, 39_300, 5_000, 2, true, "1500 8200 8200 8200 8200"), result.Row("300"));
        Assert.Equal(new ReplayRow("100", 2, 5, 8_000, 43_000, 10_000, 0, true, "1000 8000 8000 8000 8000"), result.Row("100"));
        Assert.Equal(new ReplayRow("400", 3, 4, 9_000, 34_000, 5_000, 3, false, "2000 9000 9000 9000"), result.Row("400"));
        Assert.Equal(new[] { "100", "300" }, result.FlagOrder);
        Assert.Equal(34_303, result.ElapsedMs);
    }

    // --- An undo -----------------------------------------------------------------------

    /// <summary>
    /// Reset pressed by mistake at 32,000, undone at 40,000 (Race Control's
    /// undo: CreateSnapshot before the reset, RestoreSnapshotAfterGap after),
    /// and the restart at 42,000. The cars kept driving all along. Five laps.
    /// </summary>
    internal static ReplayScript UndoHeat()
    {
        DateTime takenUtc = default;

        return new ReplayScript()
            .Start(Green)
            .Pass(21_000, "100").Pass(29_000, "100").Pass(37_000, "100")
            .Pass(45_000, "100").Pass(53_000, "100").Pass(61_000, "100")
            .Pass(21_500, "300").Pass(30_000, "300").Pass(38_500, "300")
            .Pass(47_000, "300").Pass(55_500, "300").Pass(64_000, "300")
            .Command(32_000, "Reset (undoable)", (race, context) =>
            {
                context.Snapshots[1] = race.CreateSnapshot();
                takenUtc = context.Clock.UtcNow;
                race.Reset();
            })
            .Command(40_000, "Undo", (race, context) => race.RestoreSnapshotAfterGap(context.Snapshots[1], takenUtc))
            .Resume(42_000);
    }

    [Fact]
    public void A_reset_undone_puts_the_race_back_with_its_laps_and_its_clock()
    {
        var result = OnTime(UndoHeat(), FiveLaps);

        Assert.Equal(RaceState.Finished, result.State);

        // Two laps each before the reset; the crossings at 37,000 and 38,500
        // came while there was no race. The undo moved the start and every
        // reading on by the 8 s gap, so the next laps are measured as if the
        // race had been stopped: 45,000 - (29,000 + 8,000) = 8,000 for 100,
        // 47,000 - (30,000 + 8,000) = 9,000 for 300. Elapsed is from the
        // moved start, 28,000.
        Assert.Equal(new ReplayRow("100", 1, 5, 8_000, 33_000, 0, 0, true, "1000 8000 8000 8000 8000"), result.Row("100"));
        Assert.Equal(new ReplayRow("300", 2, 5, 8_500, 36_000, 0, 0, true, "1500 8500 9000 8500 8500"), result.Row("300"));

        // 12 s before the reset, then 42,000 to 64,003 after the restart.
        Assert.Equal(34_003, result.ElapsedMs);
    }
}
