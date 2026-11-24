namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): the sign-off's closest finish and the Tonight
/// Open's thread question.
/// </summary>
public sealed class BugHunt24_CommentaryShowFactsTests
{
    private static NightThread Beatable(
        int startedTonight = 0,
        int wonTonight = 0,
        string? beatenBy = null,
        bool nightOver = false) =>
        new(
            SubjectId: "dave",
            Name: "Dave",
            DaysWon: 3,
            DaysOnFile: 8,
            Rivals: 6,
            StartedTonight: startedTonight,
            WonTonight: wonTonight,
            BeatenBy: beatenBy,
            NightOver: nightOver);

    private static string Closest(double seconds) =>
        string.Join(" ", CommentaryShowFacts.SignOff(
            new NightDone(Races: 8, Laps: 100, ClosestFinishSeconds: seconds, ClosestFinishWinner: "Ann"),
            "day-1").Select(b => b.Text));

    [Fact]
    public void BugHunt24_SignOff_words_a_win_by_thousandths_as_thousandths_not_a_dead_heat()
    {
        var text = Closest(0.004);

        Assert.Contains("by 0.004s", text);
        Assert.DoesNotContain("by 0.00s", text);
    }

    [Fact]
    public void BugHunt24_SignOff_keeps_hundredths_for_a_margin_of_a_hundredth_or_more()
    {
        Assert.Contains("by 0.06s", Closest(0.06));
        Assert.Contains("by 0.01s", Closest(0.01));
    }

    [Fact]
    public void BugHunt24_ThreadAhead_does_not_ask_who_can_stop_a_driver_already_beaten_tonight()
    {
        Assert.Null(CommentaryShowFacts.ThreadAhead(
            Beatable(startedTonight: 1, wonTonight: 0, beatenBy: "Bill"), "night-1"));

        // Beaten by a result saved with no name on it is still beaten.
        Assert.Null(CommentaryShowFacts.ThreadAhead(
            Beatable(startedTonight: 1, wonTonight: 0, beatenBy: ""), "night-1"));
    }

    [Fact]
    public void BugHunt24_ThreadAhead_does_not_ask_once_the_night_is_over_or_a_race_went_unwon()
    {
        Assert.Null(CommentaryShowFacts.ThreadAhead(
            Beatable(startedTonight: 2, wonTonight: 2, nightOver: true), "night-1"));

        Assert.Null(CommentaryShowFacts.ThreadAhead(
            Beatable(startedTonight: 2, wonTonight: 1), "night-1"));
    }

    [Fact]
    public void BugHunt24_ThreadAhead_still_asks_while_the_question_is_open()
    {
        var before = CommentaryShowFacts.ThreadAhead(Beatable(), "night-1");
        Assert.NotNull(before);
        Assert.Contains("Can anybody stop that run tonight?", before!.Text);

        Assert.NotNull(CommentaryShowFacts.ThreadAhead(
            Beatable(startedTonight: 2, wonTonight: 2), "night-1"));
    }
}
