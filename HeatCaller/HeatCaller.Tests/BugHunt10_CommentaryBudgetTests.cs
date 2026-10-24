namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 10 (23 Oct 2026): a line the relay wrote and the app then
/// dropped was refunded in the budget, though the relay keeps the charge
/// for any line it returned. A written drop is now charged; a write that
/// never happened is still given back whole.
/// </summary>
public sealed class BugHunt10_CommentaryBudgetTests
{
    private static readonly DateTime Start = new(2026, 10, 7, 19, 0, 0, DateTimeKind.Unspecified);

    private static CommentaryBudget Mid()
    {
        var budget = new CommentaryBudget(() => Start);

        budget.NewMeeting("day-1");
        budget.MonthlyUsage(0, 400);
        budget.NightsLeftThisMonth = 4;
        budget.NightShape(15, 30);

        return budget;
    }

    [Fact]
    public void A_written_line_that_is_dropped_keeps_its_charge_but_gives_back_its_turn()
    {
        var budget = Mid();
        var before = budget.Headroom;
        var verdict = budget.Reserve(SegmentKind.ColdOpen);

        Assert.True(verdict.Allowed);

        budget.Written(verdict.Ticket);
        budget.Release(verdict.Ticket, written: true);

        Assert.Equal(0, budget.InFlight);
        Assert.Equal(1, budget.SpentTonight);
        Assert.Equal(before - 1, budget.Headroom, 3);
        Assert.True(budget.Peek(SegmentKind.ColdOpen).Allowed);
    }

    [Fact]
    public void A_write_that_never_happened_is_still_given_back_whole()
    {
        var budget = Mid();
        var before = budget.Headroom;
        var verdict = budget.Reserve(SegmentKind.ColdOpen);

        Assert.True(verdict.Allowed);

        budget.Release(verdict.Ticket);

        Assert.Equal(0, budget.InFlight);
        Assert.Equal(0, budget.SpentTonight);
        Assert.Equal(before, budget.Headroom, 3);
        Assert.True(budget.Peek(SegmentKind.ColdOpen).Allowed);
    }

    [Fact]
    public void Releasing_twice_charges_once()
    {
        var budget = Mid();
        var verdict = budget.Reserve(SegmentKind.ColdOpen);

        budget.Release(verdict.Ticket, written: true);
        budget.Release(verdict.Ticket, written: true);

        Assert.Equal(1, budget.SpentTonight);
    }
}
