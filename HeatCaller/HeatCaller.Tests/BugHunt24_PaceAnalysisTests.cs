namespace HeatCaller.Tests;

/// <summary>
/// Bug Hunt 24 (23 Nov 2026): Pace Analysis cut a driver's mean lap down to
/// the millisecond while the phone's results page rounds the same mean, so
/// the PC could read a thousandth under the phone for the same race.
/// </summary>
public sealed class BugHunt24_PaceAnalysisTests
{
    [Fact]
    public void Average_lap_with_a_half_millisecond_rounds_up_like_the_phone()
    {
        // The first entry is the start, not a lap; the flying laps average 5123.5.
        var entry = new SessionResultEntry { Transponder = "7", LapTimesMs = { 3_000, 5_123, 5_124 } };

        Assert.Equal(5_124, PaceAnalysis.For(entry).AverageLapMs);
    }

    [Fact]
    public void Average_lap_under_a_half_millisecond_still_rounds_down()
    {
        // 5123 + 5123 + 5124 = 15370 over 3 = 5123.33.
        var entry = new SessionResultEntry { Transponder = "7", LapTimesMs = { 3_000, 5_123, 5_123, 5_124 } };

        Assert.Equal(5_123, PaceAnalysis.For(entry).AverageLapMs);
    }

    [Fact]
    public void Quickest_average_rounds_rather_than_cuts()
    {
        Assert.Equal(5_124, PaceAnalysis.QuickestAverage(new List<long> { 5_123, 5_124, 9_000 }, 2));

        // 1000 + 1000 + 1002 = 3002 over 3 = 1000.67.
        Assert.Equal(1_001, PaceAnalysis.QuickestAverage(new List<long> { 1_000, 1_002, 1_000 }, 3));
    }
}
