namespace HeatCaller.Tests;

/// <summary>
/// The room votes on who wins the main (12 Oct 2026), and afterwards the
/// announcer says what they reckoned.
///
/// This is the only segment in the app that can be WRONG, and that is the
/// point of it - so most of this file is about the cases where the honest
/// answer is to say nothing at all: a thin vote, a split room, a vote about
/// a different race. A claim about what "the room" predicted, made off four
/// people's guesses, would make everything else the announcer says sound
/// invented.
/// </summary>
public sealed class RoomVoteTests : IDisposable
{
    /// <summary>Its own %AppData%, like every other store test here.</summary>
    private readonly TempAppData _appData = new();

    public void Dispose() => _appData.Dispose();

    private static readonly VoteOption Bill = new("t1", "Bill Hargrove");
    private static readonly VoteOption Sam = new("t2", "Sam Teague");
    private static readonly VoteOption Dave = new("t3", "Dave Bellamy");

    /// <summary>
    /// The ROUND, by its own id. Everything here matches on this rather
    /// than on the label, because a club running two classes has two
    /// rounds both labelled "A-Main".
    /// </summary>
    private const string TheMainId = "round-stock-a";

    private const string TheMain = "Stock 17.5 A-Main";

    private const string TheBMainId = "round-stock-b";

    private static RoomVote Tonight(string day = "night-1")
    {
        var vote = RoomVote.Load();

        vote.NewMeeting(day);

        return vote;
    }

    /// <summary>A poll on the three of them, open and ready to take votes.</summary>
    private static RoomVote Open(string raceKey = TheMainId, string race = TheMain)
    {
        var vote = Tonight();

        Assert.Null(vote.OpenFor(raceKey, race, "Stock 17.5", new[] { Bill, Sam, Dave }));

        return vote;
    }

    /// <summary>Casts <paramref name="many"/> votes for one driver, each from its own phone.</summary>
    private static void Votes(RoomVote vote, VoteOption forWhom, int many)
    {
        for (var at = 0; at < many; at++)
        {
            Assert.Null(vote.Cast($"{forWhom.Id}-phone-{at}", forWhom.Id));
        }
    }

    // ------------------------------------------------------------------
    //  When it says nothing - the half that matters
    // ------------------------------------------------------------------

    [Fact]
    public void Four_peoples_guesses_are_not_the_room()
    {
        var vote = Open();

        Votes(vote, Bill, 3);
        Votes(vote, Sam, 1);

        Assert.True(vote.Votes < RoomVote.LeastVotes);
        Assert.Null(vote.CallFor(TheMainId, Bill.Id));
    }

    [Fact]
    public void The_fewest_votes_it_will_speak_on_is_enough_to_speak_on()
    {
        // The boundary from the other side: LeastVotes must itself be
        // sayable, or the constant is off by one and nothing is ever said
        // on a small club's night.
        var vote = Open();

        Votes(vote, Bill, 3);
        Votes(vote, Sam, 2);

        Assert.Equal(RoomVote.LeastVotes, vote.Votes);
        Assert.NotNull(vote.CallFor(TheMainId, Bill.Id));
    }

    [Fact]
    public void A_room_that_split_evenly_picked_nobody()
    {
        // "The room had Bill" off a four-all tie is a claim about the room
        // that is not true.
        var vote = Open();

        Votes(vote, Bill, 4);
        Votes(vote, Sam, 4);

        Assert.Null(vote.CallFor(TheMainId, Bill.Id));
    }

    [Fact]
    public void A_verdict_is_never_read_out_over_a_different_race()
    {
        // A club that voted on the A-main and then ran a tie-break heat
        // must not hear the room's A-main prediction over the heat.
        var vote = Open();

        Votes(vote, Bill, 9);

        Assert.Null(vote.CallFor(TheBMainId, Bill.Id));
        Assert.NotNull(vote.CallFor(TheMainId, Bill.Id));
    }

    [Fact]
    public void Two_classes_both_running_an_A_main_do_not_collide()
    {
        // THE REASON A ROUND ID IS CARRIED AT ALL. A club running Stock
        // and Mod has two rounds both labelled "A-Main". Matched on the
        // label, the room's Stock prediction would be read out over the
        // Mod A-main - a verdict about the wrong race, said with complete
        // confidence.
        var vote = Tonight();

        Assert.Null(vote.OpenFor("round-stock-a", "A-Main", "Stock 17.5", new[] { Bill, Sam, Dave }));

        Votes(vote, Bill, 9);

        // Same label, different round.
        Assert.Null(vote.CallFor("round-mod-a", Bill.Id));
        Assert.NotNull(vote.CallFor("round-stock-a", Bill.Id));
    }

    [Fact]
    public void A_vote_needs_a_round_to_be_keyed_on()
    {
        var vote = Tonight();

        Assert.NotNull(vote.OpenFor(null, TheMain, "Stock 17.5", new[] { Bill, Sam }));
        Assert.NotNull(vote.OpenFor("   ", TheMain, "Stock 17.5", new[] { Bill, Sam }));

        Assert.False(vote.Taking);
    }

    [Fact]
    public void A_verdict_is_said_once()
    {
        var vote = Open();

        Votes(vote, Bill, 9);

        Assert.NotNull(vote.CallFor(TheMainId, Bill.Id));

        vote.Said(TheMainId);

        Assert.Null(vote.CallFor(TheMainId, Bill.Id));
    }

    [Fact]
    public void Settling_one_race_cannot_mark_the_vote_opened_for_the_next()
    {
        var vote = Open();

        Votes(vote, Bill, 9);

        // The race director settles the wrong race - nothing happens.
        vote.Said(TheBMainId);

        Assert.NotNull(vote.CallFor(TheMainId, Bill.Id));
    }

    [Fact]
    public void With_no_vote_at_all_there_is_nothing_to_say()
    {
        var vote = Tonight();

        Assert.False(vote.Taking);
        Assert.Null(vote.CallFor(TheMainId, Bill.Id));
        Assert.Empty(vote.Options);
        Assert.Equal(0, vote.Votes);
    }

    // ------------------------------------------------------------------
    //  What it says when it does
    // ------------------------------------------------------------------

    [Fact]
    public void The_room_was_right()
    {
        var vote = Open();

        Votes(vote, Bill, 12);
        Votes(vote, Sam, 8);
        Votes(vote, Dave, 5);

        var call = vote.CallFor(TheMainId, Bill.Id);

        Assert.NotNull(call);
        Assert.True(call!.RoomWasRight);
        Assert.Equal(Bill.Name, call.PickName);
        Assert.Equal(25, call.Votes);
        Assert.Equal(48, call.PickShare);
        Assert.Equal(48, call.WinnerShare);
        Assert.False(call.NobodyHadTheWinner);
    }

    [Fact]
    public void The_room_was_wrong_and_it_says_what_the_winner_had()
    {
        var vote = Open();

        Votes(vote, Bill, 12);
        Votes(vote, Sam, 8);
        Votes(vote, Dave, 5);

        var call = vote.CallFor(TheMainId, Sam.Id);

        Assert.NotNull(call);
        Assert.False(call!.RoomWasRight);
        Assert.Equal(Bill.Name, call.PickName);
        Assert.Equal(32, call.WinnerShare);
        Assert.False(call.NobodyHadTheWinner);
    }

    [Fact]
    public void Not_one_person_in_this_room_picked_him()
    {
        // The best line the whole feature produces, and the only one no
        // amount of timing data could ever get to.
        var vote = Open();

        Votes(vote, Bill, 12);
        Votes(vote, Sam, 8);

        var call = vote.CallFor(TheMainId, Dave.Id);

        Assert.NotNull(call);
        Assert.True(call!.NobodyHadTheWinner);
        Assert.Equal(0, call.WinnerShare);
        Assert.False(call.RoomWasRight);
    }

    [Fact]
    public void A_winner_who_was_never_on_the_ballot_is_not_a_room_that_missed_him()
    {
        // Nobody picked him because nobody COULD - the grid changed, or
        // the vote was opened before he was in it. That is this app's
        // doing, not the room's, so the announcer is never handed the
        // "nobody had him" line for it.
        var vote = Open();

        Votes(vote, Bill, 9);

        var call = vote.CallFor(TheMainId, "a-transponder-never-offered");

        Assert.NotNull(call);
        Assert.False(call!.NobodyHadTheWinner);
        Assert.False(call.RoomWasRight);
        Assert.Equal(0, call.WinnerShare);
    }

    // ------------------------------------------------------------------
    //  One vote to one voter
    // ------------------------------------------------------------------

    [Fact]
    public void A_second_vote_from_the_same_phone_replaces_the_first()
    {
        // Somebody who picked the wrong name on a phone screen gets to fix
        // it. Nobody gets two.
        var vote = Open();

        Assert.Null(vote.Cast("one-phone", Bill.Id));
        Assert.Null(vote.Cast("one-phone", Sam.Id));

        Assert.Equal(1, vote.Votes);

        var sam = vote.Standing().Single(one => one.Name == Sam.Name);
        var bill = vote.Standing().Single(one => one.Name == Bill.Name);

        Assert.Equal(1, sam.Votes);
        Assert.Equal(0, bill.Votes);
    }

    [Fact]
    public void Changing_your_mind_still_works_on_a_poll_that_is_full()
    {
        // The ceiling is about how many PEOPLE have voted. Turning
        // somebody away for it when they already have a vote in would be
        // the limit stopping the one thing it is not about.
        var vote = Open();

        for (var at = 0; at < RoomVote.MostVoters; at++)
        {
            Assert.Null(vote.Cast($"phone-{at}", Bill.Id));
        }

        Assert.Equal(RoomVote.MostVoters, vote.Votes);

        // A new voter is turned away...
        Assert.NotNull(vote.Cast("one-too-many", Bill.Id));

        // ...but somebody already in it can change their pick.
        Assert.Null(vote.Cast("phone-0", Sam.Id));
        Assert.Equal(RoomVote.MostVoters, vote.Votes);
    }

    [Fact]
    public void A_vote_for_somebody_not_on_the_grid_is_refused()
    {
        var vote = Open();

        Assert.NotNull(vote.Cast("a-phone", "not-racing-tonight"));
        Assert.Equal(0, vote.Votes);
    }

    [Fact]
    public void An_empty_vote_is_refused_rather_than_counted()
    {
        var vote = Open();

        Assert.NotNull(vote.Cast(null, Bill.Id));
        Assert.NotNull(vote.Cast("a-phone", null));
        Assert.NotNull(vote.Cast("   ", Bill.Id));

        Assert.Equal(0, vote.Votes);
    }

    // ------------------------------------------------------------------
    //  Opening and closing
    // ------------------------------------------------------------------

    [Fact]
    public void One_name_is_not_a_question()
    {
        var vote = Tonight();

        Assert.NotNull(vote.OpenFor(TheMainId, TheMain, "Stock 17.5", new[] { Bill }));
        Assert.NotNull(vote.OpenFor(TheMainId, TheMain, "Stock 17.5", Array.Empty<VoteOption>()));
        Assert.NotNull(vote.OpenFor(TheMainId, TheMain, "Stock 17.5", null));

        Assert.False(vote.Taking);
    }

    [Fact]
    public void A_vote_needs_a_race_to_be_about()
    {
        var vote = Tonight();

        Assert.NotNull(vote.OpenFor(TheMainId, null, "Stock 17.5", new[] { Bill, Sam }));
        Assert.NotNull(vote.OpenFor(TheMainId, "   ", "Stock 17.5", new[] { Bill, Sam }));

        Assert.False(vote.Taking);
    }

    [Fact]
    public void The_same_driver_twice_on_the_grid_is_one_option()
    {
        var vote = Tonight();

        Assert.Null(vote.OpenFor(TheMainId, TheMain, "Stock 17.5", new[] { Bill, Bill, Sam }));

        Assert.Equal(2, vote.Options.Count);
    }

    [Fact]
    public void A_grid_longer_than_the_ceiling_is_cut_rather_than_refused()
    {
        var vote = Tonight();

        var big = Enumerable.Range(0, RoomVote.MostOptions + 6)
            .Select(at => new VoteOption($"t{at}", $"Driver {at}"))
            .ToArray();

        Assert.Null(vote.OpenFor(TheMainId, TheMain, "Stock 17.5", big));

        Assert.Equal(RoomVote.MostOptions, vote.Options.Count);
    }

    [Fact]
    public void Opening_a_vote_on_the_next_race_closes_the_one_before_it()
    {
        // A club can never have two running, so the announcer can never
        // read a verdict about the wrong race.
        var vote = Open();

        Votes(vote, Bill, 9);

        Assert.Null(vote.OpenFor(TheBMainId, "Stock 17.5 B-Main", "Stock 17.5", new[] { Bill, Sam }));

        Assert.Equal("Stock 17.5 B-Main", vote.OnRace);
        Assert.Equal(TheBMainId, vote.OnRaceKey);
        Assert.Equal(0, vote.Votes);
        Assert.Null(vote.CallFor(TheMainId, Bill.Id));
    }

    [Fact]
    public void Closing_it_stops_it_taking_votes()
    {
        var vote = Open();

        Assert.True(vote.Taking);

        vote.Close();

        Assert.False(vote.Taking);
        Assert.NotNull(vote.Cast("a-late-phone", Bill.Id));
    }

    [Fact]
    public void Closing_it_twice_is_safe_and_so_is_closing_nothing()
    {
        var vote = Tonight();

        vote.Close();

        var open = Open();

        open.Close();
        open.Close();

        Assert.False(open.Taking);
    }

    [Fact]
    public void A_closed_vote_still_has_its_verdict_to_give()
    {
        // Closing is what the race director does before the start. The
        // verdict is read AFTER the race, so closing must not throw it
        // away.
        var vote = Open();

        Votes(vote, Bill, 9);

        vote.Close();

        Assert.NotNull(vote.CallFor(TheMainId, Bill.Id));
    }

    [Fact]
    public void A_vote_left_open_stops_taking_them_on_its_own()
    {
        // THE ONE THAT KEEPS IT HONEST. A poll left open through two more
        // heats would collect votes for a race already run and then tell
        // the room what it "predicted".
        var vote = Open();

        var justInside = DateTime.UtcNow + RoomVote.LongestOpen - TimeSpan.FromMinutes(1);
        var justOutside = DateTime.UtcNow + RoomVote.LongestOpen + TimeSpan.FromMinutes(1);

        Assert.True(vote.TakingAt(justInside));
        Assert.False(vote.TakingAt(justOutside));
    }

    [Fact]
    public void The_window_is_long_enough_to_run_a_main_in()
    {
        Assert.True(
            RoomVote.LongestOpen >= TimeSpan.FromMinutes(10),
            "A vote that times out before the race has been run is a vote nobody can use.");
    }

    // ------------------------------------------------------------------
    //  The tally
    // ------------------------------------------------------------------

    [Fact]
    public void The_tally_is_best_first_and_keeps_the_drivers_nobody_picked()
    {
        // An empty row is the interesting one before the race and the
        // whole story after it.
        var vote = Open();

        Votes(vote, Sam, 4);
        Votes(vote, Bill, 7);

        var standing = vote.Standing();

        Assert.Equal(3, standing.Count);
        Assert.Equal(Bill.Name, standing[0].Name);
        Assert.Equal(Sam.Name, standing[1].Name);
        Assert.Equal(Dave.Name, standing[2].Name);
        Assert.Equal(0, standing[2].Votes);
    }

    [Fact]
    public void Shares_are_of_the_votes_cast()
    {
        var vote = Open();

        Votes(vote, Bill, 1);
        Votes(vote, Sam, 1);

        Assert.All(
            vote.Standing().Where(one => one.Votes > 0),
            one => Assert.Equal(50, one.Share));
    }

    [Fact]
    public void A_share_of_nothing_is_nothing_rather_than_a_crash()
    {
        Assert.Equal(0, RoomVote.ShareOf(0, 0));
        Assert.Equal(0, RoomVote.ShareOf(3, 0));
    }

    [Fact]
    public void A_share_is_rounded_the_way_a_person_would_round_it()
    {
        Assert.Equal(50, RoomVote.ShareOf(1, 2));
        Assert.Equal(33, RoomVote.ShareOf(1, 3));
        Assert.Equal(67, RoomVote.ShareOf(2, 3));
        Assert.Equal(100, RoomVote.ShareOf(4, 4));
    }

    // ------------------------------------------------------------------
    //  The result, and the verdict that waits for it
    // ------------------------------------------------------------------

    [Fact]
    public void There_is_no_verdict_until_the_race_has_been_run()
    {
        // A verdict offered before the result would be the announcer
        // telling the room what it predicted about something that has not
        // happened.
        var vote = Open();

        Votes(vote, Bill, 9);

        Assert.Null(vote.CallWaiting());

        vote.Finished(TheMainId, Bill.Id, Bill.Name);

        Assert.NotNull(vote.CallWaiting());
    }

    [Fact]
    public void The_verdict_carries_the_winners_name_for_the_announcer_to_say()
    {
        var vote = Open();

        Votes(vote, Bill, 6);
        Votes(vote, Sam, 4);

        vote.Finished(TheMainId, Sam.Id, Sam.Name);

        var waiting = vote.CallWaiting();

        Assert.NotNull(waiting);
        Assert.Equal(Sam.Name, waiting!.WinnerName);
        Assert.False(waiting.Call.RoomWasRight);
        Assert.Equal(Bill.Name, waiting.Call.PickName);
    }

    [Fact]
    public void A_result_for_a_different_race_is_not_filed_against_this_vote()
    {
        var vote = Open();

        Votes(vote, Bill, 9);

        vote.Finished(TheBMainId, Bill.Id, Bill.Name);

        Assert.Null(vote.CallWaiting());
    }

    [Fact]
    public void The_first_result_filed_is_the_one_the_room_is_told_about()
    {
        // A correction after the fact must not change a line that has
        // already gone out - or, worse, change one that is about to.
        var vote = Open();

        Votes(vote, Bill, 9);

        vote.Finished(TheMainId, Bill.Id, Bill.Name);
        vote.Finished(TheMainId, Sam.Id, Sam.Name);

        var waiting = vote.CallWaiting();

        Assert.NotNull(waiting);
        Assert.Equal(Bill.Name, waiting!.WinnerName);
        Assert.True(waiting.Call.RoomWasRight);
    }

    [Fact]
    public void The_race_being_run_closes_the_vote_whatever_anybody_pressed()
    {
        // Otherwise a club that never pressed Close would go on taking
        // votes while the result was being read out.
        var vote = Open();

        Votes(vote, Bill, 9);

        Assert.True(vote.Taking);

        vote.Finished(TheMainId, Bill.Id, Bill.Name);

        Assert.False(vote.Taking);
        Assert.NotNull(vote.Cast("a-very-late-phone", Sam.Id));
    }

    [Fact]
    public void A_thin_vote_has_no_verdict_even_with_a_result_in()
    {
        var vote = Open();

        Votes(vote, Bill, 2);

        vote.Finished(TheMainId, Bill.Id, Bill.Name);

        Assert.Null(vote.CallWaiting());
    }

    [Fact]
    public void A_verdict_waiting_is_gone_once_it_has_been_said()
    {
        var vote = Open();

        Votes(vote, Bill, 9);

        vote.Finished(TheMainId, Bill.Id, Bill.Name);

        Assert.NotNull(vote.CallWaiting());

        vote.Said(TheMainId);

        Assert.Null(vote.CallWaiting());
    }

    [Fact]
    public void Filing_a_result_with_no_vote_open_is_safe()
    {
        var vote = Tonight();

        vote.Finished(TheMainId, Bill.Id, Bill.Name);

        Assert.Null(vote.CallWaiting());
    }

    // ------------------------------------------------------------------
    //  What the relay's collector needs
    // ------------------------------------------------------------------

    [Fact]
    public void A_voters_pick_can_be_read_back()
    {
        // The relay is not drained by being read, so its collector reads
        // the whole held set every poll. Without this it would rewrite the
        // file with the votes it already had, every fifteen seconds, all
        // evening.
        var vote = Open();

        Assert.Null(vote.PickOf("a-phone"));

        Assert.Null(vote.Cast("a-phone", Bill.Id));

        Assert.Equal(Bill.Id, vote.PickOf("a-phone"));

        Assert.Null(vote.Cast("a-phone", Sam.Id));

        Assert.Equal(Sam.Id, vote.PickOf("a-phone"));
    }

    [Fact]
    public void Reading_back_a_voter_who_never_voted_is_null_rather_than_a_throw()
    {
        var vote = Open();

        Assert.Null(vote.PickOf(null));
        Assert.Null(vote.PickOf("   "));
        Assert.Null(vote.PickOf("never-seen"));

        Assert.Null(Tonight("night-9").PickOf("a-phone"));
    }

    [Fact]
    public void A_voter_from_the_relay_and_one_from_the_hall_are_two_people()
    {
        // Both routes write into one list of votes. The relay's keys are
        // marked, so a hashed relay voter can never collide with a key the
        // Wi-Fi route made from a client address - a collision would be
        // one person's vote replacing another's.
        var vote = Open();

        Assert.Null(vote.Cast("192.168.1.40", Bill.Id));
        Assert.Null(vote.Cast("relay:192.168.1.40", Sam.Id));

        Assert.Equal(2, vote.Votes);
        Assert.Equal(Bill.Id, vote.PickOf("192.168.1.40"));
        Assert.Equal(Sam.Id, vote.PickOf("relay:192.168.1.40"));
    }

    // ------------------------------------------------------------------
    //  The night
    // ------------------------------------------------------------------

    [Fact]
    public void Last_weeks_vote_is_gone()
    {
        // A verdict about a race run last Thursday, read out to this
        // week's room, is worse than no verdict.
        var vote = Open();

        Votes(vote, Bill, 9);

        vote.NewMeeting("night-2");

        Assert.False(vote.Taking);
        Assert.Null(vote.OnRace);
        Assert.Null(vote.CallFor(TheMainId, Bill.Id));
    }

    [Fact]
    public void Being_told_the_same_day_again_changes_nothing()
    {
        // It is called from a tick, so it has to be cheap and idempotent.
        var vote = Open();

        Votes(vote, Bill, 9);

        vote.NewMeeting("night-1");
        vote.NewMeeting("night-1");

        Assert.True(vote.Taking);
        Assert.Equal(9, vote.Votes);
    }

    [Fact]
    public void A_vote_survives_the_app_being_restarted()
    {
        // It is a file for exactly this reason: a club PC that fell over
        // between the grid and the flag must not lose the room's vote.
        var vote = Open();

        Votes(vote, Bill, 7);
        Votes(vote, Sam, 2);

        var reopened = RoomVote.Load();

        Assert.Equal(TheMain, reopened.OnRace);
        Assert.Equal(9, reopened.Votes);

        var call = reopened.CallFor(TheMainId, Bill.Id);

        Assert.NotNull(call);
        Assert.True(call!.RoomWasRight);
    }
}
