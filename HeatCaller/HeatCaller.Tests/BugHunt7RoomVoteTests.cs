using System.Text.Json.Nodes;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 7: the room's vote, and the two small pages and stores beside it.
///
/// The vote was never settled (the winner was looked for by an empty Status,
/// which no saved row has), a demo could settle it with an invented winner,
/// the phone page served each driver's transponder beside their name with no
/// PIN, and a page left open carried the last race's pick into the next vote.
/// </summary>
public sealed class BugHunt7RoomVoteTests : IDisposable
{
    /// <summary>Letters in it, so it cannot turn up by chance inside a number or a hex id.</summary>
    private const string Transponder = "A4821B";

    private const string OtherTransponder = "B9930C";

    // Declared first: everything below reads and writes where it points.
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- Settling the vote ------------------------------------------------------------------

    [Fact]
    public void The_winner_of_an_ordinary_race_is_found_although_every_row_is_marked_active()
    {
        var record = Race(Row(1, Transponder, laps: 12), Row(2, OtherTransponder, laps: 11));

        var won = RaceControlControl.RoomVoteWinner(record);

        Assert.NotNull(won);
        Assert.Equal(Transponder, won!.Transponder);
    }

    [Fact]
    public void A_driver_placed_first_with_a_DNF_is_not_the_winner()
    {
        var first = Row(1, Transponder, laps: 12);
        first.Outcome = "DNF";

        Assert.Null(RaceControlControl.RoomVoteWinner(Race(first, Row(2, OtherTransponder, laps: 11))));
    }

    [Fact]
    public void A_race_nobody_completed_a_lap_in_has_no_winner()
    {
        Assert.Null(RaceControlControl.RoomVoteWinner(Race(Row(1, Transponder, laps: 0))));
    }

    [Fact]
    public void A_demo_race_never_settles_the_room_s_vote()
    {
        var demo = Race(Row(1, Transponder, laps: 12), Row(2, OtherTransponder, laps: 11));
        demo.IsDemo = true;

        Assert.Null(RaceControlControl.RoomVoteWinner(demo));

        var olderDemo = Race(Row(1, Transponder, laps: 12));
        olderDemo.Label = SessionRecord.DemoLabelPrefix + " Stock A-Main";

        Assert.Null(RaceControlControl.RoomVoteWinner(olderDemo));
    }

    [Fact]
    public void Reopening_a_vote_on_the_same_round_gives_it_a_new_poll_id()
    {
        var vote = Tonight();

        Assert.Null(vote.OnPollId);

        Assert.Null(vote.OpenFor("round-a", "Stock A-Main", "Stock", Grid()));
        var first = vote.OnPollId;

        Assert.Null(vote.OpenFor("round-a", "Stock A-Main", "Stock", Grid()));

        Assert.False(string.IsNullOrEmpty(first));
        Assert.NotEqual(first, vote.OnPollId);
    }

    // --- The phone's ballot -----------------------------------------------------------------

    [Fact]
    public void The_ballot_any_phone_can_fetch_never_carries_a_driver_s_transponder()
    {
        var vote = Tonight();
        Assert.Null(vote.OpenFor("round-a", "Stock A-Main", "Stock", Grid()));

        var json = Server().BuildVoteBallotJson(vote, "phone-1");

        Assert.DoesNotContain(Transponder, json);
        Assert.DoesNotContain(OtherTransponder, json);

        var drivers = JsonNode.Parse(json)!["drivers"]!.AsArray();

        Assert.Equal(2, drivers.Count);
        Assert.All(drivers, one => Assert.Equal(16, one!["id"]!.GetValue<string>().Length));
    }

    [Fact]
    public void The_id_a_phone_was_given_maps_back_to_the_driver_it_was_given_for()
    {
        var server = Server();
        var vote = Tonight();
        Assert.Null(vote.OpenFor("round-a", "Stock A-Main", "Stock", Grid()));

        var drivers = JsonNode.Parse(server.BuildVoteBallotJson(vote, "phone-1"))!["drivers"]!.AsArray();
        var bill = drivers.Single(one => one!["name"]!.GetValue<string>() == "Bill Hargrove")!;

        Assert.Equal(Transponder, server.VoteOptionFor(vote, bill["id"]!.GetValue<string>()));
        Assert.Null(server.VoteOptionFor(vote, "0123456789abcdef"));
        Assert.Null(server.VoteOptionFor(vote, null));
    }

    [Fact]
    public void The_ballot_tells_a_phone_its_own_pick_and_a_new_vote_starts_without_it()
    {
        var server = Server();
        var vote = Tonight();
        Assert.Null(vote.OpenFor("round-b", "Stock B-Main", "Stock", Grid()));

        Assert.Null(JsonNode.Parse(server.BuildVoteBallotJson(vote, "phone-1"))!["pick"]);

        Assert.Null(vote.Cast("phone-1", Transponder));

        var ballot = JsonNode.Parse(server.BuildVoteBallotJson(vote, "phone-1"))!;
        var bill = ballot["drivers"]!.AsArray().Single(one => one!["name"]!.GetValue<string>() == "Bill Hargrove")!;

        // The pick is named by the same opaque id as the button it goes with.
        Assert.Equal(bill["id"]!.GetValue<string>(), ballot["pick"]!.GetValue<string>());

        // Another phone has picked nobody.
        Assert.Null(JsonNode.Parse(server.BuildVoteBallotJson(vote, "phone-2"))!["pick"]);

        // Bill is on the next race's grid too, so the same id would light up
        // his button - but nobody has voted in this poll yet.
        Assert.Null(vote.OpenFor("round-a", "Stock A-Main", "Stock", Grid()));

        Assert.Null(JsonNode.Parse(server.BuildVoteBallotJson(vote, "phone-1"))!["pick"]);
    }

    // --- The look-watch script on the two phone pages ---------------------------------------

    [Fact]
    public void The_vote_and_shout_out_pages_run_the_look_watch_rather_than_showing_it()
    {
        var server = Server();

        Assert.Contains("<script>(function(){", server.BuildVotePage());
        Assert.Contains("<script>(function(){", server.BuildNotePage());
    }

    // --- A shout-out's sender ---------------------------------------------------------------

    [Fact]
    public void A_sender_s_name_from_the_room_is_held_to_forty_characters()
    {
        var notes = ClubNotes.Load();
        notes.NewMeeting("night-1");

        Assert.Null(notes.AddFromTheRoom("Happy birthday Dad", new string('x', 15_000)));

        var note = Assert.Single(notes.All);

        Assert.Equal(ClubNotes.MostNameChars, note.From.Length);
    }

    [Fact]
    public void A_short_sender_s_name_is_kept_as_it_was_given()
    {
        var notes = ClubNotes.Load();
        notes.NewMeeting("night-1");

        Assert.Null(notes.AddFromTheRoom("Happy birthday Dad", "  Jess  "));

        Assert.Equal("Jess", Assert.Single(notes.All).From);
    }

    // --- Room fit ---------------------------------------------------------------------------

    [Fact]
    public void Tight_straight_on_but_roomy_turned_says_to_turn_it()
    {
        // 3000 x 3700 into 6000 x 4000: 300 mm front to back straight on,
        // but 2300 and 1000 turned.
        var fit = RoomFit.Check(3000, 3700, 6000, 4000);

        Assert.Equal(RoomFit.Verdict.Comfortable, fit.Verdict);
        Assert.Contains("quarter turn", fit.Says);
        Assert.Equal(2300d, fit.SpareWidthMm);
        Assert.Equal(1000d, fit.SpareDepthMm);
    }

    [Fact]
    public void Which_way_round_the_room_is_typed_does_not_change_the_verdict()
    {
        Assert.Equal(
            RoomFit.Check(3000, 3700, 4000, 6000).Verdict,
            RoomFit.Check(3000, 3700, 6000, 4000).Verdict);
    }

    [Fact]
    public void Tight_both_ways_is_still_tight()
    {
        // 12ft 4in x 9ft 8in into 13ft x 10ft: tight straight on, and does
        // not go in at all turned.
        var fit = RoomFit.Check(3759, 2946, 3962, 3048);

        Assert.Equal(RoomFit.Verdict.Tight, fit.Verdict);
    }

    // --- Helpers ----------------------------------------------------------------------------

    private static RoomVote Tonight()
    {
        var vote = RoomVote.Load();

        vote.NewMeeting("night-1");

        return vote;
    }

    private static VoteOption[] Grid() => new[]
    {
        new VoteOption(Transponder, "Bill Hargrove"),
        new VoteOption(OtherTransponder, "Sam Teague"),
    };

    /// <summary>A server on one of the app's own looks, for the reason BugHunt5WebPrivacyTests gives.</summary>
    private static WebServerService Server() =>
        new(new RaceTiming(new RaceControlSession()), ProfileStore.Load(), settings: new AppSettings { DisplayLook = Looks.MidnightKey });

    /// <summary>A row as RaceFiling.ResultsFrom saves it - Status "active" on every one.</summary>
    private static SessionResultEntry Row(int position, string transponder, int laps) => new()
    {
        Position = position,
        Transponder = transponder,
        DriverName = transponder,
        Laps = laps,
        Status = "active",
    };

    private static SessionRecord Race(params SessionResultEntry[] rows)
    {
        var record = new SessionRecord { SessionType = RaceSessionType.Race, Label = "Stock A-Main" };
        record.Results.AddRange(rows);

        return record;
    }
}
