namespace HeatCaller.Tests;

/// <summary>
/// Race-session fixes from the 4 Oct 2026 bug hunt: a car read on the line
/// in the countdown but behind it at the green starting a lap up on the
/// field, and lap corrections at the end of a lap race that never moved the
/// finish - +1 Lap bringing the leader to the limit left the checkered in,
/// and -1 Lap on the phantom lap that brought it out locked that driver's
/// real last lap out.
/// </summary>
public sealed class BugHunt4SessionTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    /// <summary>A pass from the decoder itself - not synthetic, so the start checks see it.</summary>
    private static void Seen(RaceControlSession race, string car, uint deviceTimeMs) =>
        race.ProcessLapEvent(new DirectLapEvent
        {
            TransponderId = car,
            DeviceTimeMs = deviceTimeMs,
            SignalHits = 60,
            SignalStrength = 247,
        });

    private static RacerStanding StandingOf(RaceControlSession race, string car) =>
        race.GetOrderedStandings().Single(s => string.Equals(s.TransponderId, car, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// A three-lap race under way, A and B both on two laps. The first
    /// crossing of the race comes half a second after the green, so the start
    /// reference is never re-anchored.
    /// </summary>
    private static RaceControlSession ThreeLapRaceOnTwoLaps(bool finishOnNextCrossing = true, params string[] alsoRacing)
    {
        var race = new RaceControlSession { LapLimit = 3, FinishOnNextCrossing = finishOnNextCrossing };

        race.Stage();
        race.StartCountdown();
        race.GreenFlag(100_000);

        Seen(race, "A", 100_500);
        Seen(race, "B", 100_700);

        foreach (var car in alsoRacing)
        {
            Seen(race, car, 100_900);
        }

        Seen(race, "A", 110_000);
        Seen(race, "B", 110_500);

        foreach (var car in alsoRacing)
        {
            Seen(race, car, 111_000);
        }

        Assert.Equal(2, StandingOf(race, "A").Laps);
        Assert.Equal(2, StandingOf(race, "B").Laps);
        Assert.False(race.IsFinishing);
        return race;
    }

    // --- session-1: read in the countdown, behind the line at the green ---

    [Fact]
    public void A_car_read_in_the_countdown_but_behind_the_line_at_the_green_starts_level_with_the_field()
    {
        var race = new RaceControlSession();

        race.Stage();
        race.StartCountdown();

        // Rolled over the loop 4.5 s before the green, then backed off it.
        Seen(race, "P", 95_500);
        race.GreenFlag(100_000);

        // Credited at the green, as a car past the line would be...
        Assert.Equal(1, StandingOf(race, "P").Laps);

        // ...but it crosses with the rest of the grid.
        Seen(race, "P", 100_600);
        Seen(race, "Q", 100_700);

        var p = StandingOf(race, "P");
        var q = StandingOf(race, "Q");

        Assert.Equal(q.Laps, p.Laps);
        Assert.Equal(1, p.Laps);
        Assert.Equal(new long[] { 600 }, p.LapTimesMs);
        Assert.False(p.StartLapCredited);
        Assert.Equal(new[] { 1 }, p.StreakAtLap);
        Assert.Equal(p.LapTimesMs.Count, p.SectorsAtLap.Count);
        Assert.Equal(0, p.RejectedLaps);

        // The dash from the countdown read to the line is not a flying lap.
        Assert.Empty(RaceGaps.PaceLaps(p));
        Assert.Equal(0L, p.FastestLapMs);

        // Its next lap is an ordinary flying lap, level with the field.
        Seen(race, "P", 110_600);
        Seen(race, "Q", 110_900);

        p = StandingOf(race, "P");
        Assert.Equal(2, p.Laps);
        Assert.Equal(2, StandingOf(race, "Q").Laps);
        Assert.Equal(new long[] { 600, 10_000 }, p.LapTimesMs);
        Assert.Equal(10_000L, p.FastestLapMs);
        Assert.Equal(new[] { 1, 2 }, p.StreakAtLap);
    }

    [Fact]
    public void A_car_behind_the_line_at_the_green_keeps_its_jump_start_and_pays_the_penalty_on_its_start_lap()
    {
        var race = new RaceControlSession { JumpStartPenaltyMs = 5000 };
        var log = new List<string>();
        race.LogMessage += log.Add;

        race.Stage();
        race.StartCountdown();
        Seen(race, "P", 95_500);
        race.GreenFlag(100_000);

        Assert.True(race.HasJumpStarted("P"));
        Assert.Equal(5000L, race.OwedJumpStartPenaltyMs("P"));

        Seen(race, "P", 100_600);

        var p = StandingOf(race, "P");
        Assert.Equal(1, p.Laps);
        Assert.True(race.HasJumpStarted("P"));
        Assert.Equal(5000L, p.PenaltyMs);
        Assert.Equal(5600L, p.ElapsedMs);
        Assert.Equal(0L, race.OwedJumpStartPenaltyMs("P"));
        Assert.Contains(log, l => l.StartsWith("P was behind the line at the green", StringComparison.Ordinal));
    }

    [Fact]
    public void A_car_parked_on_the_loop_through_the_green_keeps_its_credited_start_lap()
    {
        var race = new RaceControlSession();

        race.Stage();

        // Put down on the loop and left there, seen every two seconds.
        Seen(race, "P", 94_000);
        Seen(race, "P", 96_000);
        race.StartCountdown();
        Seen(race, "P", 98_000);

        race.GreenFlag(99_000);

        // Still sitting there after the green: every read two seconds after
        // the one before, so thrown out as it always was.
        Seen(race, "P", 100_000);
        Seen(race, "P", 102_000);

        var p = StandingOf(race, "P");
        Assert.Equal(1, p.Laps);
        Assert.True(p.StartLapCredited);
        Assert.Equal(2, p.RejectedLaps);

        // Then it drives off and completes a lap.
        Seen(race, "P", 113_000);

        p = StandingOf(race, "P");
        Assert.Equal(2, p.Laps);
        Assert.Equal(new long[] { 0, 15_000 }, p.LapTimesMs);
    }

    [Fact]
    public void A_car_that_really_jumped_the_start_still_has_its_start_lap_credited()
    {
        var race = new RaceControlSession();

        race.Stage();
        race.StartCountdown();

        // Goes half a second early.
        Seen(race, "J", 99_500);
        race.GreenFlag(100_000);

        // The rest of the grid crosses for its start lap...
        Seen(race, "Q", 100_800);

        // ...and the jumper comes round a lap after it crossed.
        Seen(race, "J", 110_500);

        var j = StandingOf(race, "J");
        Assert.Equal(2, j.Laps);
        Assert.Equal(new long[] { 0, 11_000 }, j.LapTimesMs);
        Assert.True(j.StartLapCredited);
    }

    [Fact]
    public void A_car_that_jumped_seconds_early_on_a_short_lap_keeps_its_credited_start_lap()
    {
        var race = new RaceControlSession();

        race.Stage();
        race.StartCountdown();

        // Goes on the first light of the countdown, 4.2 s early.
        Seen(race, "J", 95_800);
        race.GreenFlag(100_000);

        Seen(race, "Q", 100_800);

        // A 7 s lap after it crossed - which brings it round under the
        // minimum lap time after the green, but it is a whole lap.
        Seen(race, "J", 102_800);

        var j = StandingOf(race, "J");
        Assert.Equal(2, j.Laps);
        Assert.Equal(new long[] { 0, 7000 }, j.LapTimesMs);
        Assert.True(j.StartLapCredited);

        // And it stays a lap up the road.
        Seen(race, "Q", 108_000);
        Seen(race, "J", 109_800);

        Assert.Equal(3, StandingOf(race, "J").Laps);
        Assert.Equal(2, StandingOf(race, "Q").Laps);
        Assert.Equal("J", race.GetOrderedStandings()[0].TransponderId);
    }

    [Fact]
    public void A_car_that_jumped_with_a_long_minimum_lap_time_keeps_its_credited_start_lap()
    {
        // The minimum set a little under the quickest real lap of a 6 s track.
        var race = new RaceControlSession { MinLapTimeMs = 5000 };

        race.Stage();
        race.StartCountdown();

        // Goes 1.5 s early.
        Seen(race, "J", 98_500);
        race.GreenFlag(100_000);

        Seen(race, "Q", 100_700);

        // A 6 s lap after it crossed: 4.5 s after the green.
        Seen(race, "J", 104_500);

        var j = StandingOf(race, "J");
        Assert.Equal(2, j.Laps);
        Assert.Equal(new long[] { 0, 6000 }, j.LapTimesMs);
        Assert.True(j.StartLapCredited);
    }

    [Fact]
    public void A_normal_start_counts_every_cars_first_crossing_as_its_start_lap()
    {
        var race = new RaceControlSession();

        race.Stage();
        race.StartCountdown();
        race.GreenFlag(100_000);

        Seen(race, "A", 100_400);
        Seen(race, "B", 100_900);
        Seen(race, "A", 110_400);
        Seen(race, "B", 111_000);

        var a = StandingOf(race, "A");
        var b = StandingOf(race, "B");
        Assert.Equal(new long[] { 400, 10_000 }, a.LapTimesMs);
        Assert.Equal(new long[] { 900, 10_100 }, b.LapTimesMs);
        Assert.False(a.StartLapCredited);
        Assert.False(b.StartLapCredited);
    }

    // --- session-2: lap corrections at the lap limit ---

    [Fact]
    public void Adding_the_leaders_missed_last_lap_puts_the_checkered_flag_out()
    {
        var race = ThreeLapRaceOnTwoLaps();

        // The decoder missed A's third crossing.
        Assert.True(race.AdjustLaps("A", +1));

        Assert.Equal(RaceState.Racing, race.State);
        Assert.True(race.IsFinishing);
        Assert.Equal(FlagState.Checkered, race.Flag);
        Assert.True(race.HasTakenFlag("A"));
        Assert.False(race.HasTakenFlag("B"));

        // B takes the flag on its next crossing, and the race is over.
        Seen(race, "B", 121_000);

        Assert.Equal(RaceState.Finished, race.State);
        Assert.Equal(3, StandingOf(race, "A").Laps);
        Assert.Equal(3, StandingOf(race, "B").Laps);
    }

    [Fact]
    public void Adding_the_leaders_missed_last_lap_ends_the_race_when_the_race_ends_at_the_limit()
    {
        var race = ThreeLapRaceOnTwoLaps(finishOnNextCrossing: false);
        var completed = 0;
        race.RaceCompleted += () => completed++;

        race.AdjustLaps("A", +1);

        Assert.Equal(RaceState.Finished, race.State);
        Assert.Equal(1, completed);
    }

    [Fact]
    public void A_lap_added_under_a_red_flag_does_not_put_the_checkered_flag_out()
    {
        var race = ThreeLapRaceOnTwoLaps();
        race.RedFlag();

        race.AdjustLaps("A", +1);

        Assert.False(race.IsFinishing);
        Assert.Equal(FlagState.Red, race.Flag);
        Assert.Equal(RaceState.Racing, race.State);
    }

    [Fact]
    public void A_lap_added_under_a_red_flag_that_reaches_the_limit_brings_the_checkered_out_at_the_restart()
    {
        var race = ThreeLapRaceOnTwoLaps();
        race.RedFlag();

        // A's missed third crossing, found while the field is stopped.
        race.AdjustLaps("A", +1);
        race.GreenFlag(125_000);

        Assert.Equal(RaceState.Racing, race.State);
        Assert.True(race.IsFinishing);
        Assert.Equal(FlagState.Checkered, race.Flag);
        Assert.True(race.HasTakenFlag("A"));
        Assert.False(race.HasTakenFlag("B"));

        // B takes the flag on its next crossing, at the limit - not a lap
        // later behind a leader running a fourth.
        Seen(race, "B", 131_000);

        Assert.Equal(RaceState.Finished, race.State);
        Assert.Equal(3, StandingOf(race, "A").Laps);
        Assert.Equal(3, StandingOf(race, "B").Laps);
    }

    [Fact]
    public void A_lap_added_under_a_red_flag_that_reaches_the_limit_ends_the_race_at_the_restart_when_the_race_ends_at_the_limit()
    {
        var race = ThreeLapRaceOnTwoLaps(finishOnNextCrossing: false);
        var completed = 0;
        race.RaceCompleted += () => completed++;
        race.RedFlag();

        race.AdjustLaps("A", +1);
        Assert.Equal(RaceState.Racing, race.State);

        race.GreenFlag(125_000);

        Assert.Equal(RaceState.Finished, race.State);
        Assert.Equal(1, completed);
    }

    [Fact]
    public void A_lap_added_under_a_red_flag_short_of_the_limit_still_resumes_under_green()
    {
        var race = new RaceControlSession { LapLimit = 5 };
        race.Stage();
        race.StartCountdown();
        race.GreenFlag(100_000);
        Seen(race, "A", 100_500);
        Seen(race, "A", 110_000);
        race.RedFlag();

        race.AdjustLaps("A", +1);
        race.GreenFlag(125_000);

        Assert.False(race.IsFinishing);
        Assert.Equal(FlagState.Green, race.Flag);
        Assert.Equal(RaceState.Racing, race.State);
    }

    [Fact]
    public void A_lap_added_short_of_the_limit_changes_nothing_about_the_finish()
    {
        var race = new RaceControlSession { LapLimit = 5 };
        race.Stage();
        race.StartCountdown();
        race.GreenFlag(100_000);
        Seen(race, "A", 100_500);
        Seen(race, "A", 110_000);

        race.AdjustLaps("A", +1);

        Assert.Equal(3, StandingOf(race, "A").Laps);
        Assert.False(race.IsFinishing);
        Assert.Equal(FlagState.Green, race.Flag);
    }

    [Fact]
    public void Taking_off_the_phantom_lap_that_brought_the_checkered_out_lets_the_leaders_real_last_lap_take_it()
    {
        var race = ThreeLapRaceOnTwoLaps();

        // A crashes just past the line, and is carried back across the loop
        // four seconds later: counted as its third lap, and the checkered
        // comes out.
        Seen(race, "A", 114_000);
        Assert.True(race.IsFinishing);
        Assert.True(race.HasTakenFlag("A"));

        race.AdjustLaps("A", -1);

        var a = StandingOf(race, "A");
        Assert.Equal(2, a.Laps);
        Assert.False(race.HasTakenFlag("A"));

        // The flag stays out for everybody else.
        Assert.True(race.IsFinishing);
        Assert.Equal(FlagState.Checkered, race.Flag);

        // A's real third crossing counts, and takes the flag.
        Seen(race, "A", 121_000);

        a = StandingOf(race, "A");
        Assert.Equal(3, a.Laps);
        Assert.True(race.HasTakenFlag("A"));

        Seen(race, "B", 122_000);

        Assert.Equal(RaceState.Finished, race.State);
        Assert.Equal("A", race.GetOrderedStandings()[0].TransponderId);
    }

    [Fact]
    public void Taking_off_the_leaders_phantom_lap_still_lets_its_real_last_lap_count_after_another_car_has_finished()
    {
        var race = ThreeLapRaceOnTwoLaps(alsoRacing: "C");

        // A's phantom third lap brings the flag out; B then completes its
        // third lap and takes it.
        Seen(race, "A", 114_000);
        Seen(race, "B", 120_500);
        Assert.True(race.HasTakenFlag("B"));

        race.AdjustLaps("A", -1);
        Assert.False(race.HasTakenFlag("A"));

        Seen(race, "A", 121_000);

        Assert.Equal(3, StandingOf(race, "A").Laps);
        Assert.True(race.HasTakenFlag("A"));
    }

    [Fact]
    public void Taking_a_lap_off_a_car_that_took_the_flag_after_the_leader_leaves_its_finish_alone()
    {
        var race = ThreeLapRaceOnTwoLaps(alsoRacing: "C");

        // A brings the flag out; B takes it on its next crossing.
        Seen(race, "A", 120_000);
        Seen(race, "B", 120_500);
        Assert.True(race.HasTakenFlag("B"));

        // A double read earlier in B's race, corrected after the flag.
        race.AdjustLaps("B", -1);

        Assert.Equal(2, StandingOf(race, "B").Laps);
        Assert.True(race.HasTakenFlag("B"));

        // B's race is over: its next crossing is not another lap.
        Seen(race, "B", 131_000);
        Assert.Equal(2, StandingOf(race, "B").Laps);
    }

    [Fact]
    public void Taking_a_lap_off_a_lapped_car_that_has_taken_the_flag_leaves_its_finish_alone()
    {
        var race = new RaceControlSession { LapLimit = 3 };
        race.Stage();
        race.StartCountdown();
        race.GreenFlag(100_000);

        Seen(race, "A", 100_500);
        Seen(race, "B", 100_700);
        Seen(race, "C", 100_900);
        Seen(race, "A", 110_000);
        Seen(race, "B", 110_500);
        Seen(race, "A", 120_000);

        // C, stopped on track for a lap, takes the flag a lap down.
        Seen(race, "C", 120_200);
        Assert.True(race.HasTakenFlag("C"));
        Assert.Equal(2, StandingOf(race, "C").Laps);

        race.AdjustLaps("C", -1);

        Assert.True(race.HasTakenFlag("C"));
        Seen(race, "C", 131_000);
        Assert.Equal(1, StandingOf(race, "C").Laps);
    }

    [Fact]
    public void Undoing_a_lap_taken_off_the_leader_puts_its_finish_back()
    {
        var race = ThreeLapRaceOnTwoLaps(alsoRacing: "C");
        Seen(race, "A", 114_000);

        var edit = race.EditStanding("A", () => race.AdjustLaps("A", -1));
        Assert.NotNull(edit);
        Assert.False(race.HasTakenFlag("A"));

        race.UndoStandingEdit(edit!);

        Assert.Equal(3, StandingOf(race, "A").Laps);
        Assert.True(race.HasTakenFlag("A"));
        Assert.True(race.IsFinishing);
    }

    [Fact]
    public void Undoing_a_lap_added_to_the_leader_lets_its_real_last_lap_take_the_flag()
    {
        var race = ThreeLapRaceOnTwoLaps();

        var edit = race.EditStanding("A", () => race.AdjustLaps("A", +1));
        Assert.NotNull(edit);
        Assert.True(race.HasTakenFlag("A"));

        race.UndoStandingEdit(edit!);

        var a = StandingOf(race, "A");
        Assert.Equal(2, a.Laps);
        Assert.False(race.HasTakenFlag("A"));

        Seen(race, "A", 120_000);
        Assert.Equal(3, StandingOf(race, "A").Laps);
        Assert.True(race.HasTakenFlag("A"));
    }
}
