namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 15, timing: taking a car's only timed lap off by hand no longer
/// leaves the removed crossing as the point its next lap is timed from, so
/// the start lap that follows is timed from the green and the lap list still
/// adds up to the elapsed time.
/// </summary>
public sealed class BugHunt15_TimingTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void Taking_off_the_only_timed_lap_times_the_next_start_lap_from_the_green()
    {
        var race = Racing();

        Cross(race, "X", 4_000);
        Assert.True(race.AdjustLaps("X", -1));

        var cleared = Standing(race, "X");
        Assert.Equal(0, cleared.Laps);
        Assert.Empty(cleared.LapTimesMs);

        Cross(race, "X", 12_000);

        var x = Standing(race, "X");
        Assert.Equal(1, x.Laps);
        Assert.Equal(12_000, x.LapTimesMs[0]);
        Assert.Equal(x.ElapsedMs, x.LapTimesMs.Sum());

        Cross(race, "X", 22_000);
        x = Standing(race, "X");
        Assert.Equal(10_000, x.LapTimesMs[1]);
        Assert.Equal(x.ElapsedMs, x.LapTimesMs.Sum());
    }

    [Fact]
    public void Undoing_the_removal_puts_the_lap_reference_back()
    {
        var race = Racing();

        Cross(race, "X", 4_000);
        var edit = race.EditStanding("X", () => race.AdjustLaps("X", -1));
        Assert.NotNull(edit);
        race.UndoStandingEdit(edit!);

        Cross(race, "X", 12_000);

        var x = Standing(race, "X");
        Assert.Equal(2, x.Laps);
        Assert.Equal(8_000, x.LapTimesMs[1]);
    }

    [Fact]
    public void Undoing_the_removal_after_a_lap_since_keeps_the_laps_adding_up()
    {
        var race = Racing();

        Cross(race, "X", 4_000);
        var edit = race.EditStanding("X", () => race.AdjustLaps("X", -1));
        Assert.NotNull(edit);

        Cross(race, "X", 12_000);
        race.UndoStandingEdit(edit!);

        var x = Standing(race, "X");
        Assert.Equal(2, x.Laps);
        Assert.Equal(new long[] { 4_000, 8_000 }, x.LapTimesMs);
        Assert.Equal(x.ElapsedMs, x.LapTimesMs.Sum());
    }

    private static void Cross(RaceControlSession race, string car, uint raceTimeMs) =>
        race.ProcessLapEvent(new DirectLapEvent
        {
            TransponderId = car,
            DeviceTimeMs = raceTimeMs,
            SignalHits = 1,
            SignalStrength = 100,
            IsSynthetic = true,
        });

    private static RaceControlSession Racing()
    {
        var race = new RaceControlSession();
        race.Stage();
        race.GreenFlag(0);
        return race;
    }

    private static RacerStanding Standing(RaceControlSession race, string car) =>
        race.GetOrderedStandings().Single(s => s.TransponderId == car);
}
