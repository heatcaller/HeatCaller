namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 16 (10 Nov 2026): a demo glitch "lap" (0.3-0.7s, line noise the
/// session rejects) still booked the car's split reads inside it, a third of
/// a second after the last counted crossing. The session then recorded a
/// bogus ~0.35s sector best, flagged a "possible missed lap", or dropped the
/// car's real split as a repeat. A glitch must leave the bookings alone.
/// </summary>
public sealed class BugHunt16_DemoGlitchSplitTests
{
    [Fact]
    public void A_glitch_pass_books_no_split_reads_inside_itself()
    {
        // Every split already fired on the lap before, so nothing is booked.
        var due = new double[1];

        DemoRaceSimulator.BookSplits(due, crossingMs: 60_000, lapSeconds: 0.65, glitch: true, new Random(1));

        Assert.Equal(0, due[0]);
    }

    [Fact]
    public void A_glitch_pass_leaves_an_existing_booking_where_it_was()
    {
        var due = new double[] { 63_100 };

        DemoRaceSimulator.BookSplits(due, crossingMs: 60_000, lapSeconds: 0.5, glitch: true, new Random(1));

        Assert.Equal(63_100, due[0]);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void A_real_lap_books_each_loop_round_its_share_of_the_lap(int loops)
    {
        var due = new double[loops];

        DemoRaceSimulator.BookSplits(due, crossingMs: 60_000, lapSeconds: 6.0, glitch: false, new Random(7));

        for (var slot = 0; slot < loops; slot++)
        {
            var evenly = 60_000 + 6_000 * (slot + 1.0) / (loops + 1.0);
            Assert.InRange(due[slot], evenly - 6_000 * 0.04, evenly + 6_000 * 0.04);
        }
    }
}
