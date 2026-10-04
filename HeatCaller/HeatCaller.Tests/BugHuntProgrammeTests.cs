namespace HeatCaller.Tests;

/// <summary>
/// Pace Analysis's three-lap figures for a driver who never completed
/// three laps. They used to be the plain average of the one or two laps
/// there were, printed under "Best 3 in a row" and used as the table's sort
/// key - so a driver who crashed out early topped the pace table.
/// </summary>
public sealed class BugHuntProgrammeTests
{
    private static SessionResultEntry Driver(string name, params long[] lapTimes) => new()
    {
        Position = 1,
        Transponder = "T-" + name,
        DriverName = name,
        Laps = lapTimes.Length,
        LapTimesMs = lapTimes.ToList(),
    };

    [Fact]
    public void Fewer_laps_than_the_window_give_no_figure()
    {
        var laps = new List<long> { 11000, 11000 };

        Assert.Equal(0, PaceAnalysis.BestConsecutiveAverage(laps, PaceAnalysis.Window));
        Assert.Equal(0, PaceAnalysis.QuickestAverage(laps, PaceAnalysis.Window));
    }

    [Fact]
    public void A_full_window_still_gives_the_average()
    {
        var laps = new List<long> { 12000, 11000, 11500, 11200 };

        Assert.Equal((11000 + 11500 + 11200) / 3, PaceAnalysis.BestConsecutiveAverage(laps, 3));
        Assert.Equal((11000 + 11200 + 11500) / 3, PaceAnalysis.QuickestAverage(laps, 3));
    }

    [Fact]
    public void A_driver_who_crashed_out_has_no_three_lap_figure()
    {
        // The first entry is the run from the green to the first crossing
        // and does not count, so this is two counted laps.
        var row = PaceAnalysis.For(Driver("B", 1400, 11000, 11000));

        Assert.True(row.Thin);
        Assert.Equal(0, row.BestConsecutiveAverageMs);
        Assert.Equal(0, row.QuickestAverageMs);
        Assert.Equal("-", PaceAnalysis.Time(row.BestConsecutiveAverageMs));

        var full = PaceAnalysis.For(Driver("A", 1400, 11500, 11500, 11500, 11500, 11500));

        Assert.Equal(11500, full.BestConsecutiveAverageMs);
    }
}
