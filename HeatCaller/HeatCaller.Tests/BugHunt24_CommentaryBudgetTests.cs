namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, the commentary budget: a night with no rounds on the
/// programme was held at the opening share of its lines all evening.
/// </summary>
public sealed class BugHunt24_CommentaryBudgetTests
{
    private sealed class Clock
    {
        public DateTime Now { get; private set; } = new(2026, 11, 23, 19, 0, 0, DateTimeKind.Unspecified);

        public void Advance(TimeSpan by) => Now += by;

        public Func<DateTime> Read => () => Now;
    }

    /// <summary>400 lines over four nights: a hundred tonight.</summary>
    private static CommentaryBudget Opened(Clock clock)
    {
        var budget = new CommentaryBudget(clock.Read);

        budget.NewMeeting("day-1");
        budget.MonthlyUsage(0, 400);
        budget.NightsLeftThisMonth = 4;

        return budget;
    }

    [Fact]
    public void BugHunt24_No_programme_counts_as_the_whole_night_released()
    {
        Assert.Equal(1.0, CommentaryBudget.ProgressThroughNight(0, 0, programmeCounted: true));
        Assert.Equal(0.0, CommentaryBudget.ProgressThroughNight(0, 0, programmeCounted: false));
        Assert.Equal(0.5, CommentaryBudget.ProgressThroughNight(15, 30, programmeCounted: true));
        Assert.Equal(0.0, CommentaryBudget.ProgressThroughNight(0, 30, programmeCounted: true));
    }

    [Fact]
    public void BugHunt24_A_night_with_no_rounds_is_not_held_at_the_opening_float()
    {
        var clock = new Clock();
        var budget = Opened(clock);

        budget.NightShape(0, 0);

        Assert.Equal(100.0, budget.NightAllotment, 3);
        Assert.Equal(budget.NightAllotment, budget.Entitled, 3);
    }

    [Fact]
    public void BugHunt24_A_night_with_no_rounds_keeps_its_live_segments_after_the_first_races()
    {
        var clock = new Clock();
        var budget = Opened(clock);

        budget.NightShape(0, 0);

        // Twenty intros - more than the opening 15 lines the old code
        // released - and the airtime window left to empty.
        for (var i = 0; i < 20; i++)
        {
            var intro = budget.Reserve(SegmentKind.RaceIntro);
            Assert.True(intro.Allowed, $"intro {i} was refused");
            budget.Settle(intro.Ticket);
        }

        clock.Advance(TimeSpan.FromMinutes(11));

        var verdict = budget.Peek(SegmentKind.BattleWatch);

        Assert.True(verdict.Allowed, verdict.Reason);
    }

    [Fact]
    public void BugHunt24_Rounds_added_later_still_save_for_the_mains()
    {
        var clock = new Clock();
        var budget = Opened(clock);

        // The day opened before its heats were built, then the programme
        // arrived: the no-programme release must not have been kept.
        budget.NightShape(0, 0);
        budget.NightShape(0, 30);

        Assert.Equal(budget.NightAllotment * CommentaryBudget.OpeningFloat, budget.Entitled, 3);
    }

    [Fact]
    public void BugHunt24_A_programme_not_yet_counted_still_starts_at_the_opening_float()
    {
        var clock = new Clock();
        var budget = Opened(clock);

        Assert.Equal(budget.NightAllotment * CommentaryBudget.OpeningFloat, budget.Entitled, 3);
    }

    [Fact]
    public void BugHunt24_A_new_day_forgets_that_the_last_one_had_no_programme()
    {
        var clock = new Clock();
        var budget = Opened(clock);

        budget.NightShape(0, 0);
        budget.NewMeeting("day-2");

        Assert.Equal(budget.NightAllotment * CommentaryBudget.OpeningFloat, budget.Entitled, 3);
    }
}
