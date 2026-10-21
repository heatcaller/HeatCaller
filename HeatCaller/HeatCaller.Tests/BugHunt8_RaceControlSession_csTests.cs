namespace HeatCaller.Tests;

/// <summary>
/// 20 Oct 2026 bug hunt: removing a lap by hand worked a driver's clean run
/// out again from the last StreakAtLap entry, which a rejected pass never
/// touches - so -1 Lap after a rejection brought the broken run back.
///
/// The rejection itself marks the run broken in RaceControlSession.Laps.cs;
/// these tests set RacerStanding.RunBrokenSinceLastLap directly so they
/// check the recompute on its own.
/// </summary>
public sealed class BugHunt8_RaceControlSession_csTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

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

    /// <summary>A race with A on a start lap and four even 10 s laps - a clean run.</summary>
    private static RaceControlSession RaceWithACleanRun()
    {
        var race = new RaceControlSession();

        race.Stage();
        race.StartCountdown();
        race.GreenFlag(100_000);

        Seen(race, "A", 100_500);
        Seen(race, "A", 110_500);
        Seen(race, "A", 120_500);
        Seen(race, "A", 130_500);
        Seen(race, "A", 140_500);

        Assert.True(StandingOf(race, "A").ConsecutiveValidLaps > 1);
        return race;
    }

    [Fact]
    public void Removing_a_lap_after_the_run_was_broken_leaves_it_broken()
    {
        var race = RaceWithACleanRun();
        var a = StandingOf(race, "A");

        // As a rejected pass leaves it.
        a.ConsecutiveValidLaps = 0;
        a.RunBrokenSinceLastLap = true;

        Assert.True(race.AdjustLaps("A", -1));

        Assert.Equal(0, StandingOf(race, "A").ConsecutiveValidLaps);
    }

    [Fact]
    public void Removing_a_lap_from_an_unbroken_run_keeps_the_run_it_had()
    {
        var race = RaceWithACleanRun();
        var a = StandingOf(race, "A");
        var expected = a.StreakAtLap[^2];

        Assert.True(race.AdjustLaps("A", -1));

        Assert.Equal(expected, StandingOf(race, "A").ConsecutiveValidLaps);
    }

    [Fact]
    public void Undoing_a_lap_removal_after_the_run_was_broken_leaves_it_broken()
    {
        var race = RaceWithACleanRun();

        var edit = race.EditStanding("A", () => race.AdjustLaps("A", -1));
        Assert.NotNull(edit);

        // A pass rejected after the correction, with nothing counted since.
        var a = StandingOf(race, "A");
        a.ConsecutiveValidLaps = 0;
        a.RunBrokenSinceLastLap = true;

        race.UndoStandingEdit(edit!);

        a = StandingOf(race, "A");
        Assert.Equal(5, a.LapTimesMs.Count);
        Assert.Equal(0, a.ConsecutiveValidLaps);
    }

    [Fact]
    public void A_snapshot_keeps_the_broken_run()
    {
        var race = RaceWithACleanRun();
        StandingOf(race, "A").RunBrokenSinceLastLap = true;

        var snapshot = race.CreateSnapshot();

        Assert.True(snapshot.Standings.Single(s => s.TransponderId == "A").RunBrokenSinceLastLap);
    }
}
