namespace HeatCaller.Tests;

/// <summary>
/// Race-session fixes from the second 2 Oct 2026 bug hunt: an undo of Reset
/// (or of a Stage or Countdown that abandoned a race) charging the time the
/// race was gone to the race clock, and a car that was not on a
/// hand-released grid being held forever and called as a jump start.
/// </summary>
public sealed class BugHuntH2SessionTests : IDisposable
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

    private static RaceControlSession RunningRace(TimeSpan? timeLimit = null)
    {
        var race = new RaceControlSession { TimeLimit = timeLimit };
        race.Stage();
        race.StartCountdown();
        race.GreenFlag(100_000);
        return race;
    }

    private static void AssertClose(TimeSpan expected, TimeSpan actual) =>
        Assert.InRange(actual.TotalSeconds, expected.TotalSeconds - 2, expected.TotalSeconds + 2);

    [Fact]
    public void A_race_put_back_a_minute_after_it_was_reset_keeps_its_clock_and_comes_back_under_red()
    {
        var race = RunningRace(TimeSpan.FromMinutes(5));
        var now = DateTime.UtcNow;

        // At 4:10 of a 5:00 race when it was reset, a minute ago.
        var takenUtc = now - TimeSpan.FromSeconds(60);
        var snapshot = race.CreateSnapshot() with
        {
            RaceStartUtc = takenUtc - TimeSpan.FromSeconds(250),
            AccumulatedRedTime = TimeSpan.Zero,
            RedFlagStartedUtc = null,
        };

        race.Reset();
        var held = race.RestoreSnapshotAfterGap(snapshot, takenUtc);

        Assert.True(held);
        Assert.Equal(RaceState.Racing, race.State);
        Assert.Equal(FlagState.Red, race.Flag);
        AssertClose(TimeSpan.FromSeconds(250), race.ElapsedRaceTime);

        // The time limit is 50 seconds off, not already passed.
        race.Tick();
        Assert.False(race.IsFinishing);
        Assert.Equal(RaceState.Racing, race.State);
    }

    [Fact]
    public void A_race_reset_under_red_does_not_lose_the_red_time_before_the_reset()
    {
        var race = RunningRace(TimeSpan.FromMinutes(5));
        var now = DateTime.UtcNow;

        // 4:10 since the green, the last 30 seconds of it under red, so the
        // clock read 3:40 when it was reset a minute ago.
        var takenUtc = now - TimeSpan.FromSeconds(60);
        var snapshot = race.CreateSnapshot() with
        {
            Flag = FlagState.Red,
            RaceStartUtc = takenUtc - TimeSpan.FromSeconds(250),
            AccumulatedRedTime = TimeSpan.Zero,
            RedFlagStartedUtc = takenUtc - TimeSpan.FromSeconds(30),
        };

        race.Reset();
        race.RestoreSnapshotAfterGap(snapshot, takenUtc);

        Assert.Equal(FlagState.Red, race.Flag);
        AssertClose(TimeSpan.FromSeconds(220), race.ElapsedRaceTime);
    }

    [Fact]
    public void A_race_that_was_not_running_is_put_back_as_it_was()
    {
        var race = new RaceControlSession();
        var snapshot = race.CreateSnapshot();

        var held = race.RestoreSnapshotAfterGap(snapshot, DateTime.UtcNow - TimeSpan.FromSeconds(60));

        Assert.False(held);
        Assert.Equal(RaceState.Idle, race.State);
    }

    [Fact]
    public void A_car_not_on_a_hand_released_grid_is_timed_and_not_called_as_a_jump_start()
    {
        var race = new RaceControlSession
        {
            StartType = RaceStartType.HandReleased,
            JumpStartPenaltyMs = 5000,
        };
        race.SetGrid(new[] { "A", "B" });

        race.Stage();
        race.StartCountdown();
        race.GreenFlag(100_000);

        // A driver who did not run in the race the grid came from.
        Seen(race, "NEW", 101_000);
        Seen(race, "NEW", 113_000);

        Assert.False(race.HasJumpStarted("NEW"));
        Assert.True(race.IsReleased("NEW"));

        var newcomer = race.GetOrderedStandings().Single(s => s.TransponderId == "NEW");
        Assert.True(newcomer.Laps >= 1);

        // The grid itself is still held until the director lets it go.
        Assert.False(race.IsReleased("A"));
        Assert.False(race.IsReleased("b"));
    }
}
