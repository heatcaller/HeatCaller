namespace HeatCaller.Tests;

/// <summary>
/// Race Control fix from the 5 Nov 2026 bug hunt (11): the due time the gap
/// is fed (DueFor) went null the moment the race was due, so the automatic
/// countdown's "Time's up" mark could never be said - see
/// RaceControlControl.DueStill.
/// </summary>
public sealed class BugHunt11_RaceControlMiscTests
{
    private static readonly DateTime Due = new(2026, 11, 5, 19, 45, 0, DateTimeKind.Utc);

    private static GapFacts Facts(DateTime? due) => new()
    {
        NextId = "r3",
        Round = "Heat 3",
        ClassName = "Box Stock",
        Drivers = new[] { "Sam", "Bill" },
        DueUtc = due,
    };

    [Fact]
    public void Times_up_is_said_when_the_race_comes_due_through_the_real_due_time_feed()
    {
        var rules = GapRules.Default with { AutoCountdown = true, CountdownMarks = new[] { 5, 1 }, CountdownTimesUp = true };
        var gap = new GapState();
        gap.Open("race-2", Due.AddMinutes(-8));
        gap.NextRace("r3");
        gap.FloorFree(Due.AddMinutes(-8));

        // The 5- and 1-minute marks, fed the way the live tick feeds them.
        var five = Due.AddMinutes(-5).AddSeconds(1);
        Assert.Equal(5, gap.MarkDue(rules, five, Facts(RaceControlControl.DueStill(Due, five)), false));
        gap.SaidMark(5, five);

        var one = Due.AddMinutes(-1).AddSeconds(1);
        Assert.Equal(1, gap.MarkDue(rules, one, Facts(RaceControlControl.DueStill(Due, one)), false));
        gap.SaidMark(1, one);

        // At the due time and just after it, "Time's up" is due.
        Assert.Equal(0, gap.MarkDue(rules, Due, Facts(RaceControlControl.DueStill(Due, Due)), false));

        var justAfter = Due.AddSeconds(2);
        Assert.Equal(0, gap.MarkDue(rules, justAfter, Facts(RaceControlControl.DueStill(Due, justAfter)), false));
    }

    [Fact]
    public void The_due_time_is_kept_through_the_marks_window_and_then_let_go()
    {
        Assert.Equal(Due, RaceControlControl.DueStill(Due, Due.AddMinutes(-10)));
        Assert.Equal(Due, RaceControlControl.DueStill(Due, Due));
        Assert.Equal(Due, RaceControlControl.DueStill(Due, Due + GapState.MarkWindow - TimeSpan.FromSeconds(1)));
        Assert.Null(RaceControlControl.DueStill(Due, Due + GapState.MarkWindow));
        Assert.Null(RaceControlControl.DueStill(Due, Due.AddMinutes(5)));
    }

    [Fact]
    public void A_just_passed_due_time_does_not_change_the_call_up_or_the_minutes()
    {
        var justAfter = Due.AddSeconds(10);
        var facts = Facts(RaceControlControl.DueStill(Due, justAfter));
        var gap = new GapState();
        gap.Open("race-2", Due.AddMinutes(-8));
        gap.NextRace("r3");
        gap.FloorFree(Due.AddMinutes(-8));

        // Not inside the quiet before the call, not a long break, no {minutes}.
        Assert.Null(gap.WhyNot(GapLine.CallUp, GapRules.Default, justAfter, facts, false, false));
        Assert.Equal("this is not a long break", gap.WhyNot(GapLine.LongBreak, GapRules.Default, justAfter, facts, false, false));
        Assert.Equal(string.Empty, GapWords.Fill("Racing resumes in about {minutes} minutes.", facts, justAfter));
    }
}
