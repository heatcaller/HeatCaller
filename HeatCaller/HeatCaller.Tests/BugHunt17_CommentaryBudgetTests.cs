namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026): the gap since the last line and the
/// ten-minute airtime window are timed on the laptop's wall clock. When it
/// stepped backwards - Windows correcting it, daylight saving ending - the
/// stamps were in the future, and everything but intros and recaps was
/// refused for the size of the jump. A stamp later than now is now taken
/// as now.
/// </summary>
public sealed class BugHunt17_CommentaryBudgetTests
{
    private sealed class Clock
    {
        public DateTime Now { get; set; } = new(2026, 10, 25, 2, 30, 0, DateTimeKind.Unspecified);

        public Func<DateTime> Read => () => Now;
    }

    private static CommentaryBudget Mid(Clock clock)
    {
        var budget = new CommentaryBudget(clock.Read);

        budget.NewMeeting("day-1");
        budget.MonthlyUsage(0, 400);
        budget.NightsLeftThisMonth = 4;
        budget.NightShape(15, 30);

        return budget;
    }

    private static void Speak(CommentaryBudget budget, SegmentKind kind)
    {
        var verdict = budget.Reserve(kind);

        Assert.True(verdict.Allowed, verdict.Reason);

        budget.Settle(verdict.Ticket);
    }

    [Fact]
    public void A_clock_put_back_an_hour_holds_the_gap_once_and_not_for_the_hour()
    {
        var clock = new Clock();
        var budget = Mid(clock);

        Speak(budget, SegmentKind.ComingUpNext);

        // Daylight saving ends: 2:30 again is 1:30.
        clock.Now -= TimeSpan.FromHours(1);

        // Straight after, the gap still holds - and is not a negative wait.
        var held = budget.Peek(SegmentKind.ComingUpNext);

        Assert.False(held.Allowed);
        Assert.Equal(BudgetRefusal.TooSoonAfterTheLast, held.Why);
        Assert.DoesNotContain("-", held.Reason.Split(" - ")[0]);

        // Five minutes on it is long over, with fifty-five of the hour left.
        clock.Now += TimeSpan.FromMinutes(5);

        Assert.True(budget.Peek(SegmentKind.ComingUpNext).Allowed, budget.Peek(SegmentKind.ComingUpNext).Reason);
    }

    [Fact]
    public void A_clock_put_back_an_hour_empties_the_airtime_window_in_ten_minutes()
    {
        var clock = new Clock();
        var budget = Mid(clock);

        // Fill the ten-minute window.
        for (var i = 0; i < CommentaryBudget.AirtimeCap; i++)
        {
            Speak(budget, SegmentKind.ComingUpNext);
            clock.Now += TimeSpan.FromSeconds(30);
        }

        Assert.Equal(BudgetRefusal.TheAirtimeCap, budget.Peek(SegmentKind.ComingUpNext).Why);

        clock.Now -= TimeSpan.FromHours(1);

        // Still full: the lines were said, whatever the clock now reads.
        Assert.False(budget.Peek(SegmentKind.ComingUpNext).Allowed);

        clock.Now += TimeSpan.FromMinutes(11);

        Assert.True(budget.Peek(SegmentKind.ComingUpNext).Allowed, budget.Peek(SegmentKind.ComingUpNext).Reason);
    }
}
