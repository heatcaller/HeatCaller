namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), the race session's finish: a hand-added car
/// taken off after the checkered ends a race that was only waiting on it; a
/// race abandoned on its last lap no longer stays "finishing" through the
/// next start; the straggler wait on a lap limit is the minute, not the
/// minute plus the stagger; "last lap" is not called for a car taking the
/// checkered; and the race log says the lap limit was reached once.
/// </summary>
public sealed class BugHunt24_Timing_RaceControlSessionTests : IDisposable
{
    private static readonly DateTime Utc0 = new(2026, 11, 23, 19, 0, 0, DateTimeKind.Utc);
    private const long BaseMs = 1_000_000;

    private readonly TempAppData _data = new();
    private long _nowMs = BaseMs;

    public void Dispose() => _data.Dispose();

    /// <summary>A session on a clock this test moves, its race clock in step with the decoder's.</summary>
    private RaceControlSession NewRace(int? lapLimit = null)
    {
        _nowMs = BaseMs;
        return new RaceControlSession
        {
            LapLimit = lapLimit,
            NowMsSource = () => _nowMs,
            UtcNowSource = () => Utc0.AddMilliseconds(_nowMs),
        };
    }

    private RaceControlSession Racing(int? lapLimit = null)
    {
        var race = NewRace(lapLimit);
        race.Stage();
        race.GreenFlag(0);
        return race;
    }

    /// <summary>Moves the clock to this race time.</summary>
    private void At(long raceTimeMs) => _nowMs = BaseMs + raceTimeMs;

    /// <summary>A crossing at this race time, with the clock moved there first.</summary>
    private void Cross(RaceControlSession race, string car, uint raceTimeMs)
    {
        At(raceTimeMs);
        race.ProcessLapEvent(new DirectLapEvent
        {
            TransponderId = car,
            DeviceTimeMs = raceTimeMs,
            SignalHits = 1,
            SignalStrength = 100,
            IsSynthetic = true,
        });
    }

    // --- #65 (F001): a hand-added car taken back off after the checkered ---------

    [Fact]
    public void Taking_a_hand_added_car_off_after_the_checkered_ends_a_race_only_waiting_on_it()
    {
        var race = Racing(lapLimit: 2);

        Cross(race, "A", 5_000);
        Assert.True(race.AddStandingByHand("B"));

        // A reaches the limit: checkered out, A home, only B being waited on.
        Cross(race, "A", 15_000);
        Assert.True(race.IsFinishing);
        Assert.True(race.HasTakenFlag("A"));
        Assert.Equal(RaceState.Racing, race.State);

        Assert.True(race.RemoveHandAddedStanding("B"));

        // At once - no clock moved, no Tick.
        Assert.Equal(RaceState.Finished, race.State);
    }

    [Fact]
    public void Taking_a_hand_added_car_off_after_the_checkered_keeps_waiting_on_a_car_still_out()
    {
        var race = Racing(lapLimit: 2);

        Cross(race, "A", 5_000);
        Cross(race, "C", 6_000);
        Assert.True(race.AddStandingByHand("B"));

        Cross(race, "A", 15_000);
        Assert.True(race.IsFinishing);

        Assert.True(race.RemoveHandAddedStanding("B"));

        // C has not taken the flag yet.
        Assert.Equal(RaceState.Racing, race.State);

        Cross(race, "C", 16_000);
        Assert.Equal(RaceState.Finished, race.State);
    }

    // --- #66 (F002): a race abandoned on its last lap ----------------------------

    /// <summary>A on the lap limit (checkered out, A home), B still to take it.</summary>
    private RaceControlSession OnItsLastLap()
    {
        var race = Racing(lapLimit: 3);

        Cross(race, "A", 5_000);
        Cross(race, "B", 6_000);
        Cross(race, "A", 15_000);
        Cross(race, "A", 25_000);

        Assert.True(race.IsFinishing);
        Assert.True(race.HasTakenFlag("A"));
        Assert.Equal(RaceState.Racing, race.State);
        return race;
    }

    [Fact]
    public void Stage_over_a_race_on_its_last_lap_is_no_longer_finishing()
    {
        var race = OnItsLastLap();

        race.Stage();

        Assert.Equal(RaceState.Staging, race.State);
        Assert.False(race.IsFinishing);
        Assert.False(race.HasTakenFlag("A"));
    }

    [Fact]
    public void A_countdown_over_a_race_on_its_last_lap_is_no_longer_finishing()
    {
        var race = OnItsLastLap();

        race.StartCountdown();

        Assert.Equal(RaceState.Countdown, race.State);
        Assert.False(race.IsFinishing);
        Assert.False(race.HasTakenFlag("A"));
    }

    [Fact]
    public void Undo_after_a_stage_over_a_race_on_its_last_lap_puts_the_finish_back()
    {
        var race = OnItsLastLap();
        var snapshot = race.CreateSnapshot();

        race.Stage();
        race.RestoreSnapshot(snapshot);

        Assert.Equal(RaceState.Racing, race.State);
        Assert.True(race.IsFinishing);
        Assert.True(race.HasTakenFlag("A"));
    }

    // --- #67 (F003): the straggler wait on a lap limit ---------------------------

    [Fact]
    public void The_straggler_grace_adds_the_stagger_only_on_a_finish_on_the_clock()
    {
        var stagger = TimeSpan.FromSeconds(45);

        Assert.Equal(TimeSpan.FromSeconds(60), RaceControlSession.StragglerGrace(false, stagger));
        Assert.Equal(TimeSpan.FromSeconds(105), RaceControlSession.StragglerGrace(true, stagger));
        Assert.Equal(TimeSpan.FromSeconds(60), RaceControlSession.StragglerGrace(true, TimeSpan.Zero));
    }

    /// <summary>A staggered start on each driver's own clock: A at the green, B 20 s later.</summary>
    private RaceControlSession StaggeredRacing(int? lapLimit, TimeSpan? timeLimit)
    {
        var race = NewRace(lapLimit);
        race.StartType = RaceStartType.StaggeredGrid;
        race.StaggerClockMode = StaggerClock.OwnRelease;
        race.FinishOnNextCrossing = true;
        race.TimeLimit = timeLimit;
        race.Stage();
        race.SetReleaseSchedule(new Dictionary<string, long> { ["A"] = 0, ["B"] = 20_000 });
        race.GreenFlag(0);
        return race;
    }

    [Fact]
    public void A_lap_limit_finish_on_a_staggered_start_waits_only_the_minute_for_a_car_that_stopped()
    {
        var race = StaggeredRacing(lapLimit: 3, timeLimit: null);

        Cross(race, "A", 5_000);
        Cross(race, "A", 16_000);
        Cross(race, "B", 25_000);
        Cross(race, "A", 28_000); // the limit: checkered out

        Assert.True(race.IsFinishing);
        Assert.Equal(TimeSpan.FromSeconds(20), race.LastWindowExtension);

        // B has stopped on track and never crosses again. 61 s after the flag
        // the race is called - it used to wait 80 s (the minute plus B's 20 s).
        At(28_000 + 61_000);
        race.Tick();

        Assert.Equal(RaceState.Finished, race.State);
    }

    [Fact]
    public void A_time_limit_finish_on_a_staggered_start_still_waits_out_the_last_window()
    {
        var race = StaggeredRacing(lapLimit: null, timeLimit: TimeSpan.FromSeconds(30));

        Cross(race, "A", 5_000);
        Cross(race, "A", 16_000);
        Cross(race, "B", 25_000);

        At(30_000);
        race.Tick();
        Assert.True(race.IsFinishing);

        Cross(race, "A", 35_000);
        Assert.True(race.HasTakenFlag("A"));

        // Inside the minute plus B's 20 s: still waiting on B.
        At(30_000 + 70_000);
        race.Tick();
        Assert.Equal(RaceState.Racing, race.State);

        At(30_000 + 81_000);
        race.Tick();
        Assert.Equal(RaceState.Finished, race.State);
    }

    // --- #68 (F004): no "last lap" for a car taking the checkered ----------------

    [Fact]
    public void Last_lap_is_not_called_for_a_car_taking_the_checkered_after_the_clock_ran_out()
    {
        var race = Racing(lapLimit: 5);
        race.TimeLimit = TimeSpan.FromSeconds(30);

        var lastLap = new List<string>();
        race.LastLapStarted += s => lastLap.Add(s.TransponderId);

        Cross(race, "A", 5_000);
        Cross(race, "A", 15_000);
        Cross(race, "A", 25_000); // 3 laps - two short of the limit

        At(30_000);
        race.Tick();
        Assert.True(race.IsFinishing);

        // The crossing onto lap 4 is the one that takes the flag.
        Cross(race, "A", 35_000);

        Assert.Empty(lastLap);
        Assert.Equal(RaceState.Finished, race.State);
    }

    [Fact]
    public void Last_lap_is_still_called_once_for_the_leader_before_the_checkered()
    {
        var race = Racing(lapLimit: 5);

        var lastLap = new List<string>();
        race.LastLapStarted += s => lastLap.Add(s.TransponderId);

        Cross(race, "A", 5_000);
        Cross(race, "A", 15_000);
        Cross(race, "A", 25_000);
        Cross(race, "A", 35_000); // onto lap 4

        Assert.Equal(new[] { "A" }, lastLap);
    }

    // --- #69 (F005): "lap limit reached" said once -------------------------------

    [Fact]
    public void The_race_log_says_the_lap_limit_was_reached_only_by_the_car_that_brought_the_checkered_out()
    {
        var race = Racing(lapLimit: 3);

        var log = new List<string>();
        race.LogMessage += log.Add;

        Cross(race, "A", 5_000);
        Cross(race, "B", 6_000);
        Cross(race, "C", 7_000);
        Cross(race, "A", 15_000);
        Cross(race, "B", 16_000);
        Cross(race, "A", 25_000); // A brings the checkered out
        Cross(race, "B", 26_000); // B, on the lead lap, takes it - C still out

        Assert.True(race.HasTakenFlag("B"));
        Assert.Equal(RaceState.Racing, race.State);

        var reached = log.Where(l => l.Contains("Lap limit of 3 reached by", StringComparison.Ordinal)).ToList();
        Assert.Single(reached);
        Assert.Contains("reached by A", reached[0], StringComparison.Ordinal);

        Cross(race, "C", 27_000);
        Assert.Equal(RaceState.Finished, race.State);
    }
}
