namespace HeatCaller.Tests;

/// <summary>
/// Race-session fixes from the 2 Oct 2026 bug hunt: the run count of a car
/// credited with the start lap at the green, and cars parked on the loop
/// called as jump starts on paths the parked check did not cover (a
/// staggered car waiting for its release, and a countdown started without
/// Stage).
/// </summary>
public sealed class BugHuntRaceSessionTests : IDisposable
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

    [Fact]
    public void A_jumper_credited_with_the_start_lap_starts_a_run_of_one_in_step_with_its_laps()
    {
        var race = new RaceControlSession();

        race.Stage();
        race.StartCountdown();

        // Goes half a second early, and is credited with the start lap.
        Seen(race, "J", 100_000);
        race.GreenFlag(100_500);

        var jumper = StandingOf(race, "J");
        Assert.Equal(1, jumper.Laps);
        Assert.Equal(1, jumper.ConsecutiveValidLaps);
        Assert.Equal(new[] { 1 }, jumper.StreakAtLap);
        Assert.Equal(jumper.LapTimesMs.Count, jumper.StreakAtLap.Count);

        // Their first flying lap is the second of their run, as it would be
        // for a car that started cleanly.
        Seen(race, "J", 112_000);

        jumper = StandingOf(race, "J");
        Assert.Equal(2, jumper.Laps);
        Assert.Equal(2, jumper.ConsecutiveValidLaps);
        Assert.Equal(new[] { 1, 2 }, jumper.StreakAtLap);
        Assert.Equal(jumper.LapTimesMs.Count, jumper.StreakAtLap.Count);
    }

    [Fact]
    public void A_staggered_car_parked_on_the_loop_is_not_a_jump_start_while_it_waits_for_its_release()
    {
        var race = new RaceControlSession { JumpStartPenaltyMs = 5000 };
        race.SetReleaseSchedule(new Dictionary<string, long> { ["P4"] = 60_000 });

        race.Stage();

        // Put down on the loop and left there, seen every two seconds.
        Seen(race, "P4", 100_000);
        Seen(race, "P4", 102_000);
        Seen(race, "P4", 104_000);

        race.StartCountdown();
        Seen(race, "P4", 106_000);

        race.GreenFlag(107_000);

        // Still sitting there, a minute before its release.
        Seen(race, "P4", 108_000);
        Seen(race, "P4", 110_000);

        Assert.False(race.HasJumpStarted("P4"));
        Assert.Equal(0L, race.OwedJumpStartPenaltyMs("P4"));
    }

    [Fact]
    public void A_staggered_car_that_crosses_before_its_release_is_still_a_jump_start()
    {
        var race = new RaceControlSession();
        race.SetReleaseSchedule(new Dictionary<string, long> { ["P4"] = 60_000 });

        race.Stage();
        race.StartCountdown();
        race.GreenFlag(100_000);

        // Not seen at all before - it drove across the line early.
        Seen(race, "P4", 101_000);

        Assert.True(race.HasJumpStarted("P4"));
    }

    [Fact]
    public void A_car_left_on_the_loop_after_a_race_is_not_a_jump_start_when_the_countdown_skips_stage()
    {
        var race = new RaceControlSession();
        var log = new List<string>();
        race.LogMessage += log.Add;

        // Idle, between races: the car is sitting on the loop.
        Seen(race, "P1", 100_000);
        Seen(race, "P1", 102_000);
        Seen(race, "P1", 104_000);

        // Countdown pressed straight away, without Stage.
        race.StartCountdown();
        Seen(race, "P1", 106_000);

        Assert.False(race.HasJumpStarted("P1"));
        Assert.Contains(log, l => l.Contains("P1 is sitting on the loop", StringComparison.Ordinal));
        Assert.DoesNotContain(log, l => l.StartsWith("JUMP START", StringComparison.Ordinal));
    }

    [Fact]
    public void A_car_seen_long_before_the_countdown_skips_stage_is_still_a_jump_start()
    {
        var race = new RaceControlSession();

        // Rolled across the loop between races, and moved off it.
        Seen(race, "P1", 100_000);
        Seen(race, "P1", 102_000);

        race.StartCountdown();

        // Crosses on the lights, long after.
        Seen(race, "P1", 120_000);

        Assert.True(race.HasJumpStarted("P1"));
    }
}
