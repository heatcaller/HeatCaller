namespace HeatCaller.Tests;

/// <summary>
/// The winning margin as the recap prompt describes it
/// (CommentaryService.DescribeMargin). Rehearsal 1's Q2 recap called a race
/// with a clear winner a tie: a win by a few thousandths was written as
/// "0.00s", and the model read that as a dead heat.
/// </summary>
public sealed class CommentaryMarginTests
{
    /// <summary>A finisher on the same number of laps as everyone else, differing only in their last lap.</summary>
    private static SessionResultEntry Finisher(int position, long lastLapMs, int laps = 10, long lapMs = 6_000)
    {
        var times = Enumerable.Repeat(lapMs, laps - 1).Append(lastLapMs).ToList();

        return new SessionResultEntry
        {
            Position = position,
            Laps = laps,
            LapTimesMs = times,
            ElapsedMs = times.Sum(),
        };
    }

    [Fact]
    public void A_win_by_thousandths_is_given_in_thousandths_and_is_not_a_tie()
    {
        var text = CommentaryService.DescribeMargin(new List<SessionResultEntry> { Finisher(1, 6_000), Finisher(2, 6_004) });

        Assert.Contains("0.004s", text);
        Assert.Contains("NOT a tie", text);
        Assert.DoesNotContain("0.00s", text);
    }

    // Every margin to the thousandth since 1 Nov 2026, as the board's Gap
    // to P1 shows it - it was hundredths and tenths above a tenth.
    [Fact]
    public void A_close_finish_over_a_tenth_is_given_in_thousandths()
    {
        var text = CommentaryService.DescribeMargin(new List<SessionResultEntry> { Finisher(1, 6_000), Finisher(2, 6_300) });

        Assert.StartsWith("a photo finish", text);
        Assert.Contains("0.300s", text);
    }

    [Fact]
    public void A_comfortable_win_is_still_called_comfortable()
    {
        var text = CommentaryService.DescribeMargin(new List<SessionResultEntry> { Finisher(1, 6_000), Finisher(2, 14_000) });

        Assert.StartsWith("comfortable", text);
        Assert.Contains("8.000s", text);
    }
}
