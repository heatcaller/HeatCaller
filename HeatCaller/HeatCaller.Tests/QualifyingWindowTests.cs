namespace HeatCaller.Tests;

/// <summary>
/// Best-N-consecutive qualifying: the sliding window that ranks drivers for
/// TQ, and so seeds the mains.
/// </summary>
public sealed class QualifyingWindowTests
{
    [Fact]
    public void Finds_the_fastest_run_of_N_laps()
    {
        long[] laps = { 12_300, 12_100, 11_900, 11_850, 11_900 };

        var (total, startLap) = QualifyingCalculator.BestConsecutiveWindow(laps, 3);

        Assert.Equal(35_650, total);
        Assert.Equal(3, startLap); // laps 3, 4 and 5, counted from 1
    }

    [Fact]
    public void Fewer_laps_than_the_window_scores_nothing()
    {
        long[] laps = { 12_300, 12_100 };

        Assert.Equal((0L, 0), QualifyingCalculator.BestConsecutiveWindow(laps, 3));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_window_of_zero_or_less_scores_nothing(int n)
    {
        long[] laps = { 12_300, 12_100, 11_900 };

        Assert.Equal((0L, 0), QualifyingCalculator.BestConsecutiveWindow(laps, n));
    }

    [Fact]
    public void The_dash_from_the_green_to_the_first_crossing_is_never_counted()
    {
        // The example in BestFlyingWindow's own notes: the 1.40s first entry
        // is the run to the line, not a lap. Counting it scored 25.80s; the
        // real best three is 35.65s.
        long[] laps = { 1_400, 12_300, 12_100, 11_900, 11_850, 11_900 };

        var (total, startLap) = QualifyingCalculator.BestFlyingWindow(laps, 3);

        Assert.Equal(35_650, total);
        Assert.Equal(4, startLap); // numbered as the lap list numbers them
    }

    [Theory]
    [InlineData(new long[0])]
    [InlineData(new long[] { 1_400 })]
    public void No_flying_laps_scores_nothing(long[] laps)
    {
        Assert.Equal((0L, 0), QualifyingCalculator.BestFlyingWindow(laps, 3));
    }
}
