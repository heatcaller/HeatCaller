namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): the championship stakes sentence.
/// F105 - a contender said by the same name as the leader was dropped with
/// the leader, and the "level on points" line could name the wrong driver.
/// F106 - the last-round sentence said "today", which is wrong in a preview
/// written the evening before.
/// </summary>
public sealed class BugHunt24_CommentaryStakesFactsTests
{
    private static TitleStakes Stakes(int roundsLeft, int leadBy, string leader, params string[] stillInIt) =>
        new(
            "Winter Series",
            RoundsCounted: 4,
            RoundsLeft: roundsLeft,
            LeaderName: leader,
            LeaderId: "111",
            LeaderPoints: 148,
            LeadBy: leadBy,
            CannotBeCaught: false,
            StillInIt: stillInIt);

    [Fact]
    public void BugHunt24_TwoContendersSaidByTheLeadersName_AndAThird_SaysNothing()
    {
        // Old code: removed both Sams and said "Sam and Bill are level".
        var said = CommentaryStakesFacts.ForTheTitle(Stakes(2, 0, "Sam", "Sam", "Sam", "Bill"), "race-1");

        Assert.Null(said);
    }

    [Fact]
    public void BugHunt24_TwoContendersSaidByOneName_SaysNothing()
    {
        var said = CommentaryStakesFacts.ForTheTitle(Stakes(2, 0, "Sam", "Sam", "sam"), "race-1");

        Assert.Null(said);
    }

    [Fact]
    public void BugHunt24_LeaderIsTakenOutOnceAndTheRestAreStillNamed()
    {
        var said = CommentaryStakesFacts.ForTheTitle(Stakes(2, 0, "Sam Teague", "Sam Teague", "Bill Hargrove"), "race-1");

        Assert.NotNull(said);
        Assert.Contains("Only Sam Teague and Bill Hargrove can still win it.", said!.Text, StringComparison.Ordinal);
        Assert.Contains("Sam Teague and Bill Hargrove are level on 148 points.", said.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void BugHunt24_TheLastRoundSentenceNamesNoDay()
    {
        var said = CommentaryStakesFacts.ForTheTitle(Stakes(1, 12, "Bill Hargrove", "Bill Hargrove", "Sam Teague"), "race-1");

        Assert.NotNull(said);
        Assert.Contains("It is between Bill Hargrove and Sam Teague, and nobody else can take it.", said!.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("today", said.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BugHunt24_ANightBeforePreviewForTheLastRoundSaysNoToday()
    {
        var when = new DateTime(2026, 10, 17, 10, 0, 0, DateTimeKind.Local);
        var round = new RoundDefinition
        {
            ClassName = "Box Stock",
            RoundLabel = "Heat 1",
            Kind = RoundKind.Qualifier,
            AssignedTransponders = new List<string> { "111", "222" },
        };

        var facts = NightBefore.FactsFor(
            "Winter Round 6",
            when,
            "10:15",
            new[] { round },
            venue: null,
            stakes: Stakes(1, 12, "Bill Hargrove", "Bill Hargrove", "Sam Teague"));

        Assert.Contains("It is between Bill Hargrove and Sam Teague, and nobody else can take it.", facts.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("between Bill Hargrove and Sam Teague today", facts.Text, StringComparison.Ordinal);
    }
}
