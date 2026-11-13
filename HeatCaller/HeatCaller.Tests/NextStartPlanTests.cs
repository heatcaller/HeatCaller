namespace HeatCaller.Tests;

/// <summary>
/// The schedule follows the next race's real start (12 Nov 2026): an approved
/// "Need 5 minutes", the desk's +1 min or a Hold moves every "about" time
/// after it - see RaceControlControl.NextStart.
/// </summary>
public sealed class NextStartPlanTests
{
    private static readonly DateTime T0 = new(2026, 11, 12, 20, 0, 0, DateTimeKind.Utc);
    private static readonly TimeSpan Five = TimeSpan.FromMinutes(5);

    private static (RoundDefinition Run, RoundDefinition Next, RoundDefinition After) Night() => (
        new RoundDefinition { Kind = RoundKind.Qualifier, RoundLabel = "Q1 Heat 1", LinkedSessionId = "s1" },
        new RoundDefinition { Kind = RoundKind.Qualifier, RoundLabel = "Q1 Heat 2" },
        new RoundDefinition { Kind = RoundKind.Qualifier, RoundLabel = "Q1 Heat 3" });

    private static Func<string, string?> Plan(RoundDefinition[] rounds, RaceControlControl.NextStartFact? next) =>
        NightPlan.AboutOf(NightPlan.For(rounds, _ => Five, null, null, T0.AddMinutes(-1), null, null, T0, nextStart: next));

    [Fact]
    public void Auto_start_s_time_for_the_next_race_moves_it_and_everything_after_it()
    {
        var (run, next, after) = Night();
        var startsAt = T0.AddMinutes(9);

        var about = Plan(new[] { run, next, after }, new RaceControlControl.NextStartFact(next.Id, startsAt, PushedBackMinutes: 5));

        Assert.Equal(NightPlan.About(startsAt), about(next.Id));
        Assert.Equal(NightPlan.About(startsAt + Five + NightPlan.DefaultGap), about(after.Id));
    }

    [Fact]
    public void With_auto_start_off_the_desk_s_push_back_moves_the_next_race()
    {
        var (run, next, after) = Night();
        var before = Plan(new[] { run, next, after }, null);
        var pushed = Plan(new[] { run, next, after }, new RaceControlControl.NextStartFact(next.Id, null, PushedBackMinutes: 10));

        var expected = T0.AddMinutes(-1) + NightPlan.DefaultGap + TimeSpan.FromMinutes(10);
        Assert.Equal(NightPlan.About(expected), pushed(next.Id));
        Assert.NotEqual(before(next.Id), pushed(next.Id));
        Assert.Equal(NightPlan.About(expected + Five + NightPlan.DefaultGap), pushed(after.Id));
    }

    [Fact]
    public void A_start_for_some_other_race_changes_nothing()
    {
        var (run, next, after) = Night();
        var before = Plan(new[] { run, next, after }, null);
        var other = Plan(new[] { run, next, after }, new RaceControlControl.NextStartFact(after.Id, T0.AddMinutes(30), PushedBackMinutes: 5));

        Assert.Equal(before(next.Id), other(next.Id));
    }

    [Fact]
    public void Only_a_real_move_redraws_the_program()
    {
        var at = T0.AddMinutes(4);
        var fact = new RaceControlControl.NextStartFact("r", at, 0);

        Assert.False(RaceControlControl.Moved(fact, fact with { StartsAtUtc = at.AddSeconds(2) }));
        Assert.True(RaceControlControl.Moved(fact, fact with { StartsAtUtc = at.AddMinutes(5) }));
        Assert.True(RaceControlControl.Moved(fact, fact with { PushedBackMinutes = 5 }));
        Assert.True(RaceControlControl.Moved(fact, fact with { RoundId = "s" }));
        Assert.True(RaceControlControl.Moved(null, fact));
        Assert.False(RaceControlControl.Moved(null, null));
    }
}
