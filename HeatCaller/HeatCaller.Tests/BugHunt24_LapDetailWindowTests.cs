namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), finding F152: a driver credited with the start
/// lap at the green has a zero at index 0 and the rest of their start at
/// index 1. The lap window only treated index 0 as the start, so the rest of
/// the start went into its Average tile (unlike the leaderboard's Average)
/// and could read "their best".
/// </summary>
public sealed class BugHunt24_LapDetailWindowTests
{
    private static RacerStanding CreditedStarter()
    {
        var car = new RacerStanding { TransponderId = "T1", Laps = 5, StartLapCredited = true, FastestLapMs = 20_000 };
        car.LapTimesMs.AddRange(new long[] { 0, 3_000, 20_000, 20_000, 20_000 });
        return car;
    }

    [Fact]
    public void Average_leaves_out_the_lap_after_a_credited_start_like_the_leaderboard()
    {
        var car = CreditedStarter();

        var average = LapDetailFigures.AverageFlyingMs(car.LapTimesMs, new HashSet<int>(), car.StartLapCredited);

        Assert.Equal(20_000, average);
        Assert.Equal(RaceGaps.PaceLaps(car).Average(), average);
    }

    [Fact]
    public void Without_a_credited_start_the_second_entry_is_a_flying_lap_as_before()
    {
        var laps = new List<long> { 3_000, 18_000, 20_000, 22_000 };

        Assert.Equal(20_000, LapDetailFigures.AverageFlyingMs(laps, new HashSet<int>(), startLapCredited: false));
        Assert.False(LapDetailFigures.IsStartRow(laps, 1, startLapCredited: false));
    }

    [Fact]
    public void A_credit_whose_zero_was_edited_away_counts_the_second_entry_again()
    {
        var laps = new List<long> { 3_000, 18_000, 22_000 };

        Assert.False(LapDetailFigures.IsStartRow(laps, 1, startLapCredited: true));
        Assert.Equal(20_000, LapDetailFigures.AverageFlyingMs(laps, new HashSet<int>(), startLapCredited: true));
    }

    [Fact]
    public void The_rest_of_a_credited_start_is_a_start_row_not_a_lap()
    {
        var car = CreditedStarter();

        Assert.True(LapDetailFigures.IsStartRow(car.LapTimesMs, 0, true));
        Assert.True(LapDetailFigures.IsStartRow(car.LapTimesMs, 1, true));
        Assert.False(LapDetailFigures.IsStartRow(car.LapTimesMs, 2, true));
    }

    [Fact]
    public void Recovery_laps_are_still_left_out_beside_a_credited_start()
    {
        var car = CreditedStarter();
        car.LapTimesMs.Add(90_000);

        Assert.Equal(20_000, LapDetailFigures.AverageFlyingMs(car.LapTimesMs, new HashSet<int> { 5 }, true));
    }
}
