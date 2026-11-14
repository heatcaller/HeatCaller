namespace HeatCaller.Tests;

/// <summary>
/// A break is crossed once (14 Nov 2026, bug hunt 18).
///
/// With a heat of Q1 held and Q2 Heat 1 run first, the walk went Q2 Heat 1,
/// Q1 Heat 4, Q2 Heat 2 - and the dip back to Q1 brought the after-Q1 break
/// round again before Q2 Heat 2, so every start after it, the finish and
/// "(with N min of breaks)" ran late by the break.
/// </summary>
public sealed class BugHunt18_NightPlanHeldHeatBreakTests
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

    private static readonly PlannedBreak[] AfterQ1 = { new() { AfterRound = 1, Minutes = 10 } };

    [Fact]
    public void A_held_heat_run_after_the_next_round_starts_does_not_bring_the_break_round_again()
    {
        // Running order: Q1 Heat 4 held, Q2 Heat 1 moved in front of it.
        var rounds = new[]
        {
            Heat("Q1 Heat 1", run: true), Heat("Q1 Heat 2", run: true), Heat("Q1 Heat 3", run: true),
            Heat("Q2 Heat 1"), Heat("Q1 Heat 4"), Heat("Q2 Heat 2"),
        };

        var plan = NightPlan.For(rounds, _ => Five, null, null, Now.AddMinutes(-1), null, null, Now, breaks: AfterQ1)!;

        // 3 min gap + 10 min break before Q2 Heat 1, then 5 + 3 a race.
        Assert.Equal(Now.AddMinutes(12), plan.StartOf(rounds[3].Id));
        Assert.Equal(Now.AddMinutes(20), plan.StartOf(rounds[4].Id));
        Assert.Equal(Now.AddMinutes(28), plan.StartOf(rounds[5].Id));
        Assert.Equal(TimeSpan.FromMinutes(10), plan.BreakTime);
    }

    [Fact]
    public void A_break_taken_before_the_next_round_is_not_taken_again_after_a_held_heat()
    {
        // Q2 Heat 1 has run (after the break); Q1 Heat 4 is still held.
        var rounds = new[]
        {
            Heat("Q1 Heat 1", run: true), Heat("Q1 Heat 2", run: true), Heat("Q1 Heat 3", run: true),
            Heat("Q1 Heat 4"), Heat("Q2 Heat 1", run: true), Heat("Q2 Heat 2"),
        };

        var plan = NightPlan.For(rounds, _ => Five, null, null, Now.AddMinutes(-20), null, null, Now, breaks: AfterQ1)!;

        Assert.Equal(Now, plan.StartOf(rounds[3].Id));
        Assert.Equal(Now.AddMinutes(8), plan.StartOf(rounds[5].Id));
        Assert.Equal(TimeSpan.Zero, plan.BreakTime);
    }

    [Fact]
    public void A_held_heat_on_track_does_not_bring_the_break_round_again()
    {
        var rounds = new[]
        {
            Heat("Q1 Heat 1", run: true), Heat("Q1 Heat 2", run: true), Heat("Q1 Heat 3", run: true),
            Heat("Q1 Heat 4"), Heat("Q2 Heat 1", run: true), Heat("Q2 Heat 2"),
        };

        var plan = NightPlan.For(rounds, _ => Five, null, rounds[3].Id, Now.AddMinutes(-20), null, null, Now, breaks: AfterQ1)!;

        // On track since 2.5 minutes ago: done at +2.5, then the 3 min gap.
        Assert.Equal(Now.AddMinutes(5.5), plan.StartOf(rounds[5].Id));
        Assert.Equal(TimeSpan.Zero, plan.BreakTime);
    }

    [Fact]
    public void The_break_still_falls_once_in_program_order()
    {
        var rounds = new[]
        {
            Heat("Q1 Heat 1", run: true), Heat("Q1 Heat 2"), Heat("Q2 Heat 1"), Heat("Q2 Heat 2"),
        };

        var plan = NightPlan.For(rounds, _ => Five, null, null, Now.AddMinutes(-1), null, null, Now, breaks: AfterQ1)!;

        Assert.Equal(Now.AddMinutes(2), plan.StartOf(rounds[1].Id));
        Assert.Equal(Now.AddMinutes(20), plan.StartOf(rounds[2].Id));
        Assert.Equal(TimeSpan.FromMinutes(10), plan.BreakTime);
    }
}
