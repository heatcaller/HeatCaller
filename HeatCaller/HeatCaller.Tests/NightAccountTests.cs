namespace HeatCaller.Tests;

/// <summary>
/// THE ACCOUNT THE NIGHT GIVES OF ITSELF (12 Oct 2026) - see
/// <see cref="NightAccount"/>.
///
/// Two questions a club could not get an answer to: why was it so quiet,
/// and why did it say that. The first was unanswerable rather than merely
/// unanswered - the two commonest reasons a segment does not go out were
/// written down nowhere, so a club reading the log could not tell whether
/// the quiet was three segments or thirty.
///
/// WHAT THIS FILE IS MOSTLY ABOUT is the thing a tally gets wrong: counting
/// the same refusal twice, or not at all. The console deliberately says a
/// budget refusal once per reason so it does not repeat itself all evening,
/// and a tally that inherited that would have counted one refusal in ten -
/// see <see cref="Every_refusal_is_counted_even_though_the_console_says_it_once"/>.
/// </summary>
public sealed class NightAccountTests
{
    private static PackedFacts Pack(string text, int dropped = 0, int heldBack = 0) =>
        new(text, new[] { "key" }, dropped) { HeldBack = heldBack };

    // ------------------------------------------------------------------
    //  Why it was quiet
    // ------------------------------------------------------------------

    [Fact]
    public void An_account_of_a_night_nobody_asked_anything_of_is_nothing_rather_than_noughts()
    {
        Assert.Null(new NightAccount().Report());
    }

    [Fact]
    public void The_denominator_is_what_was_weighed_up_rather_than_what_was_nearly_said()
    {
        var account = new NightAccount();

        for (var at = 0; at < 5; at++)
        {
            account.Weighed(SegmentKind.BattleWatch);
        }

        account.Spoke(SegmentKind.BattleWatch, Pack("Bill is all over the back of Sam."));
        account.NothingToSay();
        account.NothingToSay();
        account.EveryFactSpent();
        account.Refused(BudgetRefusal.TheAirtimeCap);

        Assert.Equal(5, account.WeighedUp);
        Assert.Equal(1, account.Spoken);

        var report = account.Report();

        Assert.NotNull(report);
        Assert.Contains("weighed up 5 segments and said 1", report!, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_refusal_is_counted_even_though_the_console_says_it_once()
    {
        // The console says a budget refusal once per reason per segment, on
        // purpose - it must not repeat itself all evening. The tally must
        // not inherit that, or "four held" would be "one held" and a club
        // would be told its quiet night was a quiet pass.
        var account = new NightAccount();

        for (var at = 0; at < 4; at++)
        {
            account.Weighed(SegmentKind.PaceNote);
            account.Refused(BudgetRefusal.TheAirtimeCap);
        }

        var report = account.Report();

        Assert.NotNull(report);
        Assert.Contains("4 held", report!, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_to_say_and_every_fact_spent_are_different_answers()
    {
        // A club hearing one of these all evening has thin results; a club
        // hearing the other has a variety rule biting. Rolling them
        // together would hide which.
        var account = new NightAccount();

        account.Weighed(SegmentKind.GridHistory);
        account.NothingToSay();
        account.Weighed(SegmentKind.GridHistory);
        account.EveryFactSpent();

        var report = account.Report();

        Assert.NotNull(report);
        Assert.Contains("nothing worked out to build one from", report!, StringComparison.Ordinal);
        Assert.Contains("already said tonight, or had just been talked about", report!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_reason_that_did_not_happen_is_not_in_the_report()
    {
        // A report listing nine kinds of nought is a report nobody reads
        // twice.
        var account = new NightAccount();

        account.Weighed(SegmentKind.ColdOpen);
        account.Refused(BudgetRefusal.SwitchedOff);

        var report = account.Report();

        Assert.NotNull(report);
        Assert.Contains("switched off", report!, StringComparison.Ordinal);
        Assert.DoesNotContain("relay", report!, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("allowance", report!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_biggest_reason_is_the_one_a_club_reads_first()
    {
        var account = new NightAccount();

        account.Weighed(SegmentKind.PaceNote);
        account.Refused(BudgetRefusal.SwitchedOff);

        for (var at = 0; at < 6; at++)
        {
            account.Weighed(SegmentKind.PaceNote);
            account.Refused(BudgetRefusal.TheAirtimeCap);
        }

        var report = account.Report();

        Assert.NotNull(report);

        var held = report!.Split('\n').Where(one => one.Contains("held -", StringComparison.Ordinal)).ToList();

        Assert.Equal(2, held.Count);
        Assert.Contains("6 held", held[0], StringComparison.Ordinal);
    }

    [Fact]
    public void A_refusal_with_a_setting_behind_it_names_the_setting()
    {
        var account = new NightAccount();

        account.Weighed(SegmentKind.PaceNote);
        account.Refused(BudgetRefusal.TheAirtimeCap);

        var report = account.Report();

        Assert.NotNull(report);
        Assert.Contains("chattiness dial", report!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AI Commentary tab", report!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refusal_that_is_the_feature_working_says_so_rather_than_sending_a_club_hunting()
    {
        var account = new NightAccount();

        account.Weighed(SegmentKind.GridHistory);
        account.Refused(BudgetRefusal.ItsCapForTheNight);

        var report = account.Report();

        Assert.NotNull(report);
        Assert.Contains("variety rule working", report!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_refusal_of_none_is_not_a_refusal()
    {
        var account = new NightAccount();

        account.Weighed(SegmentKind.BattleWatch);
        account.Refused(BudgetRefusal.None);
        account.Spoke(SegmentKind.BattleWatch, Pack("Something happened."));

        var report = account.Report();

        Assert.NotNull(report);
        Assert.DoesNotContain("held -", report!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_segment_written_and_then_dropped_is_not_counted_as_said()
    {
        var account = new NightAccount();

        account.Weighed(SegmentKind.BattleWatch);
        account.DroppedAfterWriting();

        Assert.Equal(0, account.Spoken);

        var report = account.Report();

        Assert.NotNull(report);
        Assert.Contains("the race moved on", report!, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    //  Why it said that
    // ------------------------------------------------------------------

    [Fact]
    public void Nothing_has_been_said_yet_is_no_proof_rather_than_an_empty_one()
    {
        var account = new NightAccount();

        Assert.Null(account.Proof());

        // And a segment that was weighed up and refused has no proof
        // either: there is no line to account for.
        account.Weighed(SegmentKind.ChampionshipCheck);
        account.Refused(BudgetRefusal.TheNightsHeadroom);

        Assert.Null(account.Proof());
    }

    [Fact]
    public void The_proof_is_the_facts_in_the_words_they_were_given_in()
    {
        var account = new NightAccount();

        account.Weighed(SegmentKind.ChampionshipCheck);
        account.Spoke(
            SegmentKind.ChampionshipCheck,
            Pack("There are 2 rounds of the Winter Series left, this one included.\n"
                 + "Bill Hargrove has already won it on 148 points."));

        var proof = account.Proof();

        Assert.NotNull(proof);
        Assert.Contains("2 rounds of the Winter Series", proof!, StringComparison.Ordinal);
        Assert.Contains("148 points", proof!, StringComparison.Ordinal);

        // Named as the club knows the segment, not as the code knows it.
        Assert.Contains(CommentaryBudget.Spec(SegmentKind.ChampionshipCheck).Title, proof!, StringComparison.Ordinal);
    }

    [Fact]
    public void The_proof_is_of_the_last_thing_said_rather_than_the_first()
    {
        var account = new NightAccount();

        account.Weighed(SegmentKind.ColdOpen);
        account.Spoke(SegmentKind.ColdOpen, Pack("Eight drivers out tonight."));

        account.Weighed(SegmentKind.BattleWatch);
        account.Spoke(SegmentKind.BattleWatch, Pack("Three tenths between them."));

        var proof = account.Proof();

        Assert.NotNull(proof);
        Assert.Contains("Three tenths", proof!, StringComparison.Ordinal);
        Assert.DoesNotContain("Eight drivers", proof!, StringComparison.Ordinal);
    }

    [Fact]
    public void The_two_kinds_of_left_out_are_told_apart()
    {
        // PackedFacts keeps them apart and says why: a thin segment should
        // be accountable as a thin segment rather than looking like a cap
        // that bit.
        var account = new NightAccount();

        account.Weighed(SegmentKind.BattleWatch);
        account.Spoke(SegmentKind.BattleWatch, Pack("One fact went in.", dropped: 2, heldBack: 3));

        var proof = account.Proof();

        Assert.NotNull(proof);
        Assert.Contains("2 more facts did not fit the room", proof!, StringComparison.Ordinal);
        Assert.Contains("3 more facts held back", proof!, StringComparison.Ordinal);
    }

    [Fact]
    public void A_pack_with_nothing_in_it_is_no_proof()
    {
        var account = new NightAccount();

        account.Weighed(SegmentKind.BattleWatch);
        account.Spoke(SegmentKind.BattleWatch, Pack(string.Empty));

        Assert.Equal(1, account.Spoken);
        Assert.Null(account.Proof());
    }

    // ------------------------------------------------------------------
    //  One night
    // ------------------------------------------------------------------

    [Fact]
    public void A_new_race_day_takes_last_nights_account_with_it()
    {
        var account = new NightAccount();

        account.NewMeeting("night-1");
        account.Weighed(SegmentKind.BattleWatch);
        account.Spoke(SegmentKind.BattleWatch, Pack("Something happened."));
        account.Refused(BudgetRefusal.TheAirtimeCap);

        Assert.NotNull(account.Report());
        Assert.NotNull(account.Proof());

        account.NewMeeting("night-2");

        Assert.Equal("night-2", account.DayId);
        Assert.Equal(0, account.WeighedUp);
        Assert.Equal(0, account.Spoken);
        Assert.Null(account.Report());
        Assert.Null(account.Proof());
    }

    [Fact]
    public void The_same_race_day_twice_a_second_keeps_the_account()
    {
        // NewMeeting runs on the host's half-second tick, so anything
        // cleared unguarded is cleared twice a second - the trap the
        // director's own NewMeeting documents at length.
        var account = new NightAccount();

        account.NewMeeting("night-1");
        account.Weighed(SegmentKind.BattleWatch);
        account.Spoke(SegmentKind.BattleWatch, Pack("Something happened."));

        for (var tick = 0; tick < 50; tick++)
        {
            account.NewMeeting("night-1");
        }

        Assert.Equal(1, account.Spoken);
        Assert.NotNull(account.Proof());
    }

    [Fact]
    public async Task Counting_from_two_threads_at_once_loses_nothing()
    {
        // The gather happens on the UI tick and what became of the segment
        // is known on a pool thread after the model answers, so both write
        // here - see NightAccount's note on its gate.
        var account = new NightAccount();

        account.NewMeeting("night-1");

        var ui = Task.Run(() =>
        {
            for (var at = 0; at < 500; at++)
            {
                account.Weighed(SegmentKind.BattleWatch);
            }
        });

        var pool = Task.Run(() =>
        {
            for (var at = 0; at < 500; at++)
            {
                account.Refused(BudgetRefusal.TheAirtimeCap);
                _ = account.Report();
            }
        });

        await Task.WhenAll(ui, pool);

        Assert.Equal(500, account.WeighedUp);
        Assert.Contains("500 held", account.Report()!, StringComparison.Ordinal);
    }
}
