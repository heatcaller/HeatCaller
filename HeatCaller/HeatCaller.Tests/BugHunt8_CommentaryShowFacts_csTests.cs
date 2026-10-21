namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 8 (20 Oct 2026): the room verdict must not tell the room that
/// nobody backed a winner who was never on the ballot.
/// </summary>
public sealed class BugHunt8_CommentaryShowFacts_csTests
{
    [Fact]
    public void A_winner_who_was_never_on_the_ballot_is_not_given_a_zero_share()
    {
        var call = new RoomCall(
            RoundName: "A-main",
            Votes: 9,
            PickName: "Bill",
            PickShare: 67,
            RoomWasRight: false,
            WinnerShare: 0,
            NobodyHadTheWinner: false);

        var fact = CommentaryShowFacts.RoomVerdict(call, "Sam", "race-1");

        Assert.NotNull(fact);
        Assert.Contains("Sam won the race instead.", fact!.Text);
        Assert.DoesNotContain("per cent of the room's vote", fact.Text);
        Assert.DoesNotContain("not one", fact.Text);
    }

    [Fact]
    public void A_winner_with_a_real_share_still_has_it_said()
    {
        var call = new RoomCall("A-main", 9, "Bill", 67, false, 22, false);

        var fact = CommentaryShowFacts.RoomVerdict(call, "Sam", "race-1");

        Assert.NotNull(fact);
        Assert.Contains("with 22 per cent of the room's vote", fact!.Text);
    }

    [Fact]
    public void A_winner_nobody_picked_from_the_ballot_is_still_called_out()
    {
        var call = new RoomCall("A-main", 9, "Bill", 67, false, 0, true);

        var fact = CommentaryShowFacts.RoomVerdict(call, "Sam", "race-1");

        Assert.NotNull(fact);
        Assert.Contains("not one of the 9 people", fact!.Text);
    }
}
