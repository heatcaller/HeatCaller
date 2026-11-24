namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24: the words on a line held back by the pacing dial. The old
/// reason put both the gap and the time since the last call through
/// AnnouncerDensity.Gap, which rounds to the nearest half second, so a call
/// 7.8 seconds after the last one on an 8 second gap read "leaves 8 seconds
/// between calls, and the last one was only 8 seconds before".
/// </summary>
public sealed class BugHunt24_AnnouncerDensityTests
{
    [Theory]
    [InlineData(7.76)]
    [InlineData(7.8)]
    [InlineData(7.99)]
    public void BugHunt24_Time_since_just_under_the_gap_never_reads_as_the_gap(double since)
    {
        var words = AnnouncerDensity.GapSince(since, 8);

        Assert.NotEqual(AnnouncerDensity.Gap(8), words);
        Assert.Equal("7 and a half seconds", words);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.1)]
    [InlineData(0.24)]
    [InlineData(0.49)]
    public void BugHunt24_Two_calls_in_one_burst_never_read_as_no_time_at_all(double since)
    {
        var words = AnnouncerDensity.GapSince(since, 8);

        Assert.NotEqual("no time at all", words);
        Assert.Equal("under half a second", words);
    }

    [Fact]
    public void BugHunt24_Time_since_rounds_down_not_to_the_nearest_half_second()
    {
        Assert.Equal("3 seconds", AnnouncerDensity.GapSince(3.4, 8));
        Assert.Equal("3 and a half seconds", AnnouncerDensity.GapSince(3.9, 8));
        Assert.Equal("half a second", AnnouncerDensity.GapSince(0.6, 8));
    }

    [Fact]
    public void BugHunt24_On_every_pacing_stop_the_time_since_reads_shorter_than_the_gap()
    {
        var statures = new[]
        {
            new RaceStature(RaceBilling.Practice, false),
            new RaceStature(RaceBilling.Heat, false),
            new RaceStature(RaceBilling.TopMain, false),
        };

        foreach (var level in AnnouncerDensity.All)
        {
            if (level.Scale <= 0)
            {
                continue;
            }

            foreach (var stature in statures)
            {
                var gap = AnnouncerDensity.GapSeconds(stature, level.Key);

                foreach (var short_ in new[] { 0.01, 0.1, 0.24, 0.26, 0.49 })
                {
                    var since = gap - short_;

                    Assert.NotEqual(AnnouncerDensity.Gap(gap), AnnouncerDensity.GapSince(since, gap));
                }
            }
        }
    }
}
