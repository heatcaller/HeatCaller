using System.Text.Json.Nodes;

namespace HeatCaller.Tests;

/// <summary>
/// (13 Nov 2026, bug hunt 17, #159) The room's vote knew a voter by the
/// phone's address and nothing else, so a phone whose address changed
/// mid-vote counted as a second person. A voter is now known by two keys -
/// the address, and a random token the voting page keeps in its browser -
/// and either one is enough to be the same voter.
/// </summary>
public sealed class BugHunt17_RoomVoteTests : IDisposable
{
    private readonly TempAppData _appData = new();

    public void Dispose() => _appData.Dispose();

    private static readonly VoteOption Bill = new("A4821B", "Bill Hargrove");
    private static readonly VoteOption Sam = new("B9930C", "Sam Teague");

    private const string TokenOne = "aaaaaaaaaaaaaaaa1111";
    private const string TokenTwo = "bbbbbbbbbbbbbbbb2222";

    private static RoomVote Open()
    {
        var vote = RoomVote.Load();

        vote.NewMeeting("night-1");

        Assert.Null(vote.OpenFor("round-a", "Stock A-Main", "Stock", new[] { Bill, Sam }));

        return vote;
    }

    [Fact]
    public void The_same_address_with_a_new_token_is_still_one_voter()
    {
        var vote = Open();

        Assert.Null(vote.Cast("192.168.1.40", Bill.Id, voterToken: TokenOne));

        // A private window: same address, a token nobody has seen.
        Assert.Null(vote.Cast("192.168.1.40", Sam.Id, voterToken: TokenTwo));

        Assert.Equal(1, vote.Votes);
        Assert.Equal(Sam.Id, vote.PickOf("192.168.1.40"));

        // And the second token is that voter's too: it buys no vote from
        // another address either.
        Assert.Null(vote.Cast("192.168.1.41", Bill.Id, voterToken: TokenTwo));

        Assert.Equal(1, vote.Votes);
    }

    [Fact]
    public void A_new_address_with_the_same_token_is_one_voter_and_the_vote_moves()
    {
        var vote = Open();

        Assert.Null(vote.Cast("192.168.1.40", Bill.Id, voterToken: TokenOne));

        // Before it votes again, the phone on its new address is shown the
        // pick it already made.
        Assert.Equal(Bill.Id, vote.PickOf("192.168.1.77", TokenOne));
        Assert.Null(vote.PickOf("192.168.1.77"));

        Assert.Null(vote.Cast("192.168.1.77", Sam.Id, voterToken: TokenOne));

        Assert.Equal(1, vote.Votes);

        var sam = Assert.Single(vote.Standing(), one => one.Name == Sam.Name);
        Assert.Equal(1, sam.Votes);

        // Both addresses are that one voter now, token or no token - so going
        // back to the first address and dropping the token gets nothing.
        Assert.Equal(Sam.Id, vote.PickOf("192.168.1.40"));
        Assert.Equal(Sam.Id, vote.PickOf("192.168.1.77"));

        Assert.Null(vote.Cast("192.168.1.40", Bill.Id));
        Assert.Null(vote.Cast("192.168.1.77", Bill.Id));

        Assert.Equal(1, vote.Votes);
    }

    [Fact]
    public void A_new_address_and_a_new_token_are_two_voters()
    {
        var vote = Open();

        Assert.Null(vote.Cast("192.168.1.40", Bill.Id, voterToken: TokenOne));
        Assert.Null(vote.Cast("192.168.1.41", Bill.Id, voterToken: TokenTwo));

        Assert.Equal(2, vote.Votes);
    }

    [Fact]
    public void A_vote_with_no_token_is_judged_by_its_address_alone_as_before()
    {
        var vote = Open();

        Assert.Null(vote.Cast("192.168.1.40", Bill.Id));
        Assert.Null(vote.Cast("192.168.1.40", Sam.Id));

        Assert.Equal(1, vote.Votes);
        Assert.Equal(Sam.Id, vote.PickOf("192.168.1.40"));

        Assert.Null(vote.Cast("192.168.1.41", Bill.Id));

        Assert.Equal(2, vote.Votes);

        // An older page on an address that voted with a token is that voter.
        Assert.Null(vote.Cast("192.168.1.42", Bill.Id, voterToken: TokenOne));
        Assert.Null(vote.Cast("192.168.1.42", Sam.Id));

        Assert.Equal(3, vote.Votes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("has a space in it 1234")]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void A_token_of_the_wrong_shape_is_ignored_and_the_vote_counts_by_address(string bad)
    {
        Assert.Null(RoomVote.VoterToken(bad));

        var vote = Open();

        Assert.Null(vote.Cast("192.168.1.40", Bill.Id, voterToken: bad));

        // Ignored, not remembered: the same bad token from another address
        // is another voter, as two votes with no token would be.
        Assert.Null(vote.Cast("192.168.1.41", Bill.Id, voterToken: bad));

        Assert.Equal(2, vote.Votes);
        Assert.Null(vote.PickOf("192.168.1.99", bad));
    }

    [Fact]
    public void A_token_of_the_right_shape_is_kept_as_it_came()
    {
        Assert.Equal(TokenOne, RoomVote.VoterToken(TokenOne));
        Assert.Equal("Ab0_-Ab0_-Ab0_-Ab0_-", RoomVote.VoterToken("Ab0_-Ab0_-Ab0_-Ab0_-"));
        Assert.Null(RoomVote.VoterToken(null));
    }

    [Fact]
    public void The_most_voters_still_holds_and_a_known_token_can_still_change_its_mind()
    {
        var vote = Open();

        for (var at = 0; at < RoomVote.MostVoters; at++)
        {
            Assert.Null(vote.Cast($"phone-{at}", Bill.Id, voterToken: $"token-{at:D4}-xxxxxxxxxx"));
        }

        Assert.Equal(RoomVote.MostVoters, vote.Votes);

        // Somebody new, by both keys, is turned away.
        Assert.NotNull(vote.Cast("one-too-many", Bill.Id, voterToken: TokenOne));
        Assert.Equal(RoomVote.MostVoters, vote.Votes);

        // A voter already in it is not - whichever key they are known by.
        Assert.Null(vote.Cast("phone-0", Sam.Id));
        Assert.Null(vote.Cast("a-new-address", Sam.Id, voterToken: "token-0001-xxxxxxxxxx"));

        Assert.Equal(RoomVote.MostVoters, vote.Votes);
        Assert.Equal(Sam.Id, vote.PickOf("phone-1"));
    }

    [Fact]
    public void One_address_sending_token_after_token_does_not_grow_the_file_without_end()
    {
        var vote = Open();

        for (var at = 0; at < RoomVote.MostVoters + 50; at++)
        {
            Assert.Null(vote.Cast("192.168.1.40", Bill.Id, voterToken: $"token-{at:D4}-xxxxxxxxxx"));
        }

        Assert.Equal(1, vote.Votes);

        var poll = JsonNode.Parse(File.ReadAllText(Path.Combine(AppPaths.Root, "room-vote.json")))!["Current"]!;

        Assert.Equal(RoomVote.MostVoters, poll["Tokens"]!.AsObject().Count);
    }

    [Fact]
    public void A_new_vote_starts_with_nobody_s_token_in_it()
    {
        var vote = Open();

        Assert.Null(vote.Cast("192.168.1.40", Bill.Id, voterToken: TokenOne));

        Assert.Null(vote.OpenFor("round-b", "Stock B-Main", "Stock", new[] { Bill, Sam }));

        Assert.Null(vote.PickOf("192.168.1.77", TokenOne));

        // The token is nobody's in this poll, so a different address with it
        // is a first vote here, and the old address is a second voter.
        Assert.Null(vote.Cast("192.168.1.77", Bill.Id, voterToken: TokenOne));
        Assert.Null(vote.Cast("192.168.1.40", Bill.Id));

        Assert.Equal(2, vote.Votes);
    }

    [Fact]
    public void The_two_keys_survive_a_restart_of_the_app()
    {
        var vote = Open();

        Assert.Null(vote.Cast("192.168.1.40", Bill.Id, voterToken: TokenOne));

        var again = RoomVote.Load();

        Assert.Null(again.Cast("192.168.1.77", Sam.Id, voterToken: TokenOne));

        Assert.Equal(1, again.Votes);
    }

    [Fact]
    public void A_file_from_before_the_tokens_or_with_nulls_for_them_still_loads_and_takes_votes()
    {
        Directory.CreateDirectory(AppPaths.Root);
        File.WriteAllText(Path.Combine(AppPaths.Root, "room-vote.json"), $$"""
            {
              "DayId": "night-1",
              "Current": {
                "Id": "poll-1",
                "RaceKey": "round-a",
                "RoundName": "Stock A-Main",
                "ClassName": "Stock",
                "Options": [ { "Id": "A4821B", "Name": "Bill Hargrove" }, { "Id": "B9930C", "Name": "Sam Teague" } ],
                "Votes": { "192.168.1.40": "A4821B" },
                "Tokens": { "{{TokenOne}}": null },
                "Addresses": null,
                "OpenedUtc": "{{DateTime.UtcNow:O}}"
              }
            }
            """);

        var vote = RoomVote.Load();

        Assert.False(vote.LoadedUnreadable);
        Assert.Null(vote.Cast("192.168.1.77", Sam.Id, voterToken: TokenOne));
        Assert.Null(vote.Cast("192.168.1.40", Sam.Id, voterToken: TokenTwo));

        Assert.Equal(2, vote.Votes);
    }

    [Fact]
    public void The_ballot_shows_a_phone_on_a_new_address_the_pick_its_token_made()
    {
        var server = new WebServerService(
            new RaceTiming(new RaceControlSession()),
            ProfileStore.Load(),
            settings: new AppSettings { DisplayLook = Looks.MidnightKey });

        var vote = Open();

        Assert.Null(vote.Cast("192.168.1.40", Bill.Id, voterToken: TokenOne));

        Assert.Null(JsonNode.Parse(server.BuildVoteBallotJson(vote, "192.168.1.77"))!["pick"]);
        Assert.NotNull(JsonNode.Parse(server.BuildVoteBallotJson(vote, "192.168.1.77", TokenOne))!["pick"]);
    }

    [Fact]
    public void The_voting_page_sends_its_token_with_the_ballot_and_with_the_vote()
    {
        var page = new WebServerService(
            new RaceTiming(new RaceControlSession()),
            ProfileStore.Load(),
            settings: new AppSettings { DisplayLook = Looks.MidnightKey }).BuildVotePage();

        Assert.Contains("fetch('/api/vote/ballot', { headers:{ 'X-HeatCaller-Voter':voter() } })", page);
        Assert.Contains("headers:{ 'X-HeatCaller':'1', 'X-HeatCaller-Voter':voter() }", page);
        Assert.Contains("localStorage.getItem('hc-voter')", page);
    }
}
