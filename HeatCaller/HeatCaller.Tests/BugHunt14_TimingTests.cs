namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 14, timing: a black-flagged car at the lap limit no longer puts
/// the checkered out on a restart after a red, or when a lap is added to it
/// by hand. Bug hunt 13 covered only the crossing that reaches the limit.
/// </summary>
public sealed class BugHunt14_TimingTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_black_flagged_car_at_the_limit_does_not_end_the_race_on_the_restart(bool finishOnNextCrossing)
    {
        var race = Racing(lapLimit: 3);
        race.FinishOnNextCrossing = finishOnNextCrossing;
        race.WarningLadder = new List<WarningStep> { new(WarningAction.BlackFlag) };

        Cross(race, "A", 5_000);
        Cross(race, "B", 6_000);
        Cross(race, "A", 15_000);
        Cross(race, "B", 17_000);

        race.IssueWarning("A", "contact");
        Cross(race, "A", 25_000);
        Assert.Equal(3, Standing(race, "A").Laps);

        race.RedFlag();
        race.GreenFlag(40_000);

        Assert.False(race.IsFinishing);
        Assert.Equal(RaceState.Racing, race.State);
        Assert.False(race.HasTakenFlag("A"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void A_lap_added_by_hand_to_a_black_flagged_car_does_not_end_the_race(bool finishOnNextCrossing)
    {
        var race = Racing(lapLimit: 3);
        race.FinishOnNextCrossing = finishOnNextCrossing;
        race.WarningLadder = new List<WarningStep> { new(WarningAction.BlackFlag) };

        Cross(race, "A", 5_000);
        Cross(race, "B", 6_000);
        Cross(race, "A", 15_000);
        Cross(race, "B", 17_000);

        race.IssueWarning("A", "contact");
        race.AdjustLaps("A", 1);

        Assert.Equal(3, Standing(race, "A").Laps);
        Assert.False(race.IsFinishing);
        Assert.Equal(RaceState.Racing, race.State);
    }

    // A clean car at the limit still ends it on the restart: BugHunt4SessionTests.

    private static void Cross(RaceControlSession race, string car, uint raceTimeMs) =>
        race.ProcessLapEvent(new DirectLapEvent
        {
            TransponderId = car,
            DeviceTimeMs = raceTimeMs,
            SignalHits = 1,
            SignalStrength = 100,
            IsSynthetic = true,
        });

    private static RaceControlSession Racing(int? lapLimit = null)
    {
        var race = new RaceControlSession { LapLimit = lapLimit };
        race.Stage();
        race.GreenFlag(0);
        return race;
    }

    private static RacerStanding Standing(RaceControlSession race, string car) =>
        race.GetOrderedStandings().Single(s => s.TransponderId == car);
}
