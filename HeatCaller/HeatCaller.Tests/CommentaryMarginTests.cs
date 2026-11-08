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

    // ------------------------------------------------------------------
    //  8 Nov 2026 - a margin and a fastest lap only from classified cars
    // ------------------------------------------------------------------

    private static SessionResultEntry Out(SessionResultEntry entry, string outcome)
    {
        entry.Outcome = outcome;
        return entry;
    }

    [Theory]
    [InlineData(ResultEditor.Dq)]
    [InlineData(ResultEditor.Dnf)]
    public void A_margin_is_not_measured_to_a_car_that_was_not_classified(string outcome)
    {
        // A DQ a hundredth behind the winner was "a photo finish".
        var text = CommentaryService.DescribeMargin(new List<SessionResultEntry>
        {
            Finisher(1, 6_000),
            Out(Finisher(2, 6_010), outcome),
        });

        Assert.DoesNotContain("photo finish", text);
        Assert.DoesNotContain("0.010s", text);
        Assert.Contains("only one classified finisher", text);
    }

    [Fact]
    public void A_margin_skips_an_unclassified_car_to_the_next_finisher()
    {
        var text = CommentaryService.DescribeMargin(new List<SessionResultEntry>
        {
            Finisher(1, 6_000),
            Out(Finisher(2, 6_010), ResultEditor.Dq),
            Finisher(3, 14_000),
        });

        Assert.StartsWith("comfortable", text);
        Assert.Contains("8.000s", text);
    }

    [Fact]
    public void The_fastest_lap_of_the_race_is_never_a_disqualified_driver_s()
    {
        var winner = Finisher(1, 6_000);
        winner.Transponder = "win";
        winner.FastLapMs = 5_400;

        var thrownOut = Out(Finisher(2, 6_100), "dsq");
        thrownOut.Transponder = "dq";
        thrownOut.FastLapMs = 5_100;

        var retired = Out(Finisher(3, 6_200), ResultEditor.Dnf);
        retired.Transponder = "dnf";
        retired.FastLapMs = 5_300;

        Assert.Equal("dnf", CommentaryService.FastestLapOf(new[] { winner, thrownOut, retired })!.Transponder);
        Assert.Equal("win", CommentaryService.FastestLapOf(new[] { winner, thrownOut })!.Transponder);
        Assert.Null(CommentaryService.FastestLapOf(new[] { thrownOut }));
    }
}
