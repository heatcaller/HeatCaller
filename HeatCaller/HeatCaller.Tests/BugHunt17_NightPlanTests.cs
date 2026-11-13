namespace HeatCaller.Tests;

/// <summary>
/// A break already taken is not break time ahead (13 Nov 2026, bug hunt 17).
///
/// The break before the next race was counted in full whether or not any
/// of it was still to come, so twenty minutes after a 10-minute break the
/// Next Steps card still said "(with 10 min of breaks)" - time that was in
/// no start and not in the finish.
/// </summary>
public sealed class BugHunt17_NightPlanTests
{
    private static readonly DateTime Now = new(2026, 11, 11, 20, 20, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Five = TimeSpan.FromMinutes(5);

    private static RoundDefinition Heat(string label, bool run = false) => new()
    {
        RoundLabel = label,
        ClassName = "Stock",
        Kind = RoundKind.Qualifier,
        LinkedSessionId = run ? Guid.NewGuid().ToString("N") : null,
    };

    private static PlannedBreak After(int round, int minutes = 10) =>
        new() { AfterRound = round, Minutes = minutes };

    private static RoundDefinition[] AfterQ2() =>
        new[] { Heat("Q1 Heat 1", run: true), Heat("Q2 Heat 1", run: true), Heat("Q3 Heat 1"), Heat("Q3 Heat 2") };

    [Fact]
    public void A_break_already_taken_is_not_counted_as_still_ahead()
    {
        var rounds = AfterQ2();

        // Q2 finished 20 minutes ago: the 3-minute gap and the break are long gone.
        var plan = NightPlan.For(rounds, _ => Five, null, null, Now.AddMinutes(-20), null, null, Now, breaks: new[] { After(2) })!;

        Assert.Equal(Now, plan.StartOf(rounds[2].Id));
        Assert.Equal(TimeSpan.Zero, plan.BreakTime);
        Assert.DoesNotContain("breaks", NightPlan.Note(plan));
    }

    [Fact]
    public void A_break_part_way_through_counts_only_what_is_left_of_it()
    {
        var rounds = AfterQ2();

        // Finished 9 minutes ago: 3 of gap, then 6 of the 10-minute break gone.
        var plan = NightPlan.For(rounds, _ => Five, null, null, Now.AddMinutes(-9), null, null, Now, breaks: new[] { After(2) })!;

        Assert.Equal(Now.AddMinutes(4), plan.StartOf(rounds[2].Id));
        Assert.Equal(TimeSpan.FromMinutes(4), plan.BreakTime);
    }

    [Fact]
    public void A_break_still_to_start_counts_in_full()
    {
        var rounds = AfterQ2();

        var plan = NightPlan.For(rounds, _ => Five, null, null, Now.AddMinutes(-1), null, null, Now, breaks: new[] { After(2) })!;

        Assert.Equal(Now.AddMinutes(12), plan.StartOf(rounds[2].Id));
        Assert.Equal(TimeSpan.FromMinutes(10), plan.BreakTime);
    }

    [Fact]
    public void A_known_next_start_leaves_no_more_break_than_the_wait_for_it()
    {
        var rounds = AfterQ2();
        var startsIn2 = new RaceControlControl.NextStartFact(rounds[2].Id, Now.AddMinutes(2), 0);

        // The break has 9 minutes on paper, but the desk starts the race in 2.
        var plan = NightPlan.For(rounds, _ => Five, null, null, Now.AddMinutes(-4), null, null, Now,
            breaks: new[] { After(2) }, nextStart: startsIn2)!;

        Assert.Equal(Now.AddMinutes(2), plan.StartOf(rounds[2].Id));
        Assert.Equal(TimeSpan.FromMinutes(2), plan.BreakTime);
    }

    [Fact]
    public void A_later_break_is_still_counted_with_one_already_taken()
    {
        var rounds = new[] { Heat("Q1 Heat 1", run: true), Heat("Q2 Heat 1"), Heat("Q3 Heat 1") };

        var plan = NightPlan.For(rounds, _ => Five, null, null, Now.AddMinutes(-30), null, null, Now,
            breaks: new[] { After(1), After(2, 15) })!;

        Assert.Equal(TimeSpan.FromMinutes(15), plan.BreakTime);
    }
}
