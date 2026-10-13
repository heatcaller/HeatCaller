namespace HeatCaller.Tests;

/// <summary>
/// A line written before it was needed (12 Oct 2026) - see
/// <see cref="WrittenAhead"/>.
///
/// WHAT THIS FILE IS ABOUT
///
/// One question, asked six ways: is a line that was true when it was
/// written still true now? Everything else in the class is bookkeeping.
///
/// The test worth reading first is
/// <see cref="A_fact_said_by_something_else_throws_the_words_away"/>. That
/// is the rule the plan settled on and the one a reader will not guess
/// from the code: every key is unspent at the moment a line is held,
/// because marking happens on speaking - so a key that is spent by the
/// time the line is wanted means another segment said that fact in
/// between, and the held words would say it twice.
///
/// AND THE ONE THESE TESTS CANNOT COVER, stated here so nobody thinks
/// they do: a held intro's championship figures going stale because the
/// race it was written during was then won. No check in this class can
/// catch that - a points total is not a key that gets spent, it is just a
/// different number - so it is prevented by WHEN the caller writes
/// (after a result is filed, never during a race) rather than by anything
/// testable here. See the class summary on WrittenAhead.
/// </summary>
public sealed class WrittenAheadTests
{
    private static readonly DateTime At = new(2026, 10, 12, 20, 0, 0, DateTimeKind.Local);

    private const string Round = "r5";

    private static readonly string[] Grid = { "111", "222", "333" };

    /// <summary>The two facts a held intro was built from.</summary>
    private static PackedFacts Facts(params string[] keys) =>
        new("the facts, as the prompt carried them", keys, 0);

    private static PackedFacts TwoFacts() => Facts("hist:111:debut", "venue:oval:record");

    /// <summary>What has gone out tonight, as the ledger would know it.</summary>
    private static SaidTonight Said(params string[] spent)
    {
        var said = new SaidTonight();
        said.NewMeeting("2026-10-12");

        foreach (var key in spent)
        {
            said.Mark(key);
        }

        return said;
    }

    // ------------------------------------------------------------------
    //  It saves the wait
    // ------------------------------------------------------------------

    [Fact]
    public void A_line_still_true_is_handed_over_with_its_facts()
    {
        var ahead = new WrittenAhead();
        var pack = TwoFacts();

        ahead.Hold(Round, Grid, "Here comes the A-main.", pack, At);

        var answer = ahead.TakeFor(Round, Grid, Said(), At.AddMinutes(4));

        Assert.Equal(WrittenAhead.Why.Used, answer.Why);
        Assert.Equal("Here comes the A-main.", answer.Line);

        // The pack comes back so the CALLER marks it once the line has
        // actually been spoken - never in here. A held line dropped by a
        // reset must cost the night nothing.
        Assert.Same(pack, answer.Facts);
        Assert.Equal(1, ahead.Used);
        Assert.Equal(0, ahead.Wasted);
    }

    [Fact]
    public void Nothing_written_ahead_is_not_a_waste()
    {
        var ahead = new WrittenAhead();

        var answer = ahead.TakeFor(Round, Grid, Said(), At);

        Assert.Equal(WrittenAhead.Why.NothingHeld, answer.Why);
        Assert.Null(answer.Line);

        // Counted nowhere: nothing was written, so nothing was thrown away.
        Assert.Equal(0, ahead.Wasted);
        Assert.Equal(0, ahead.Written);
        Assert.Null(ahead.Report());
    }

    [Fact]
    public void A_line_is_used_once()
    {
        var ahead = new WrittenAhead();

        ahead.Hold(Round, Grid, "line", TwoFacts(), At);

        Assert.Equal(WrittenAhead.Why.Used, ahead.TakeFor(Round, Grid, Said(), At.AddMinutes(1)).Why);
        Assert.Equal(WrittenAhead.Why.NothingHeld, ahead.TakeFor(Round, Grid, Said(), At.AddMinutes(2)).Why);
    }

    // ------------------------------------------------------------------
    //  The four ways it stops being true
    // ------------------------------------------------------------------

    [Fact]
    public void The_programme_moving_on_throws_the_words_away()
    {
        var ahead = new WrittenAhead();

        ahead.Hold(Round, Grid, "line", TwoFacts(), At);

        var answer = ahead.TakeFor("r6", Grid, Said(), At.AddMinutes(2));

        Assert.Equal(WrittenAhead.Why.NotThatRound, answer.Why);
        Assert.Equal(1, ahead.Wasted);

        // And the slot is empty, so those words cannot be offered again.
        Assert.Equal(WrittenAhead.Why.NothingHeld, ahead.TakeFor(Round, Grid, Said(), At.AddMinutes(3)).Why);
    }

    [Fact]
    public void A_driver_scratching_throws_the_words_away()
    {
        var ahead = new WrittenAhead();

        ahead.Hold(Round, Grid, "line", TwoFacts(), At);

        var answer = ahead.TakeFor(Round, new[] { "111", "222" }, Said(), At.AddMinutes(2));

        Assert.Equal(WrittenAhead.Why.GridChanged, answer.Why);
    }

    /// <summary>
    /// The same people in a different order is a different intro, because
    /// an intro names the drivers off the front of the entry list. A
    /// fingerprint that sorted them would have called these the same and
    /// read out a front row that was not there.
    /// </summary>
    [Fact]
    public void The_grid_order_moving_throws_the_words_away()
    {
        var ahead = new WrittenAhead();

        ahead.Hold(Round, Grid, "line", TwoFacts(), At);

        var answer = ahead.TakeFor(Round, new[] { "222", "111", "333" }, Said(), At.AddMinutes(2));

        Assert.Equal(WrittenAhead.Why.GridChanged, answer.Why);
    }

    [Fact]
    public void A_transponder_typed_with_a_space_is_the_same_grid()
    {
        var ahead = new WrittenAhead();

        ahead.Hold(Round, new[] { " 111", "222 ", "333" }, "line", TwoFacts(), At);

        var answer = ahead.TakeFor(Round, new[] { "111", "222", "333" }, Said(), At.AddMinutes(1));

        Assert.Equal(WrittenAhead.Why.Used, answer.Why);
    }

    /// <summary>THE ONE. See the class summary.</summary>
    [Fact]
    public void A_fact_said_by_something_else_throws_the_words_away()
    {
        var ahead = new WrittenAhead();

        ahead.Hold(Round, Grid, "line", TwoFacts(), At);

        // Between the writing and the wanting, some other segment went out
        // carrying the venue's record.
        var answer = ahead.TakeFor(Round, Grid, Said("venue:oval:record"), At.AddMinutes(3));

        Assert.Equal(WrittenAhead.Why.AFactWasSaid, answer.Why);
        Assert.Null(answer.Line);
    }

    [Fact]
    public void Somebody_elses_fact_being_said_changes_nothing()
    {
        var ahead = new WrittenAhead();

        ahead.Hold(Round, Grid, "line", TwoFacts(), At);

        var answer = ahead.TakeFor(Round, Grid, Said("pb:sam:2026-10-12-r4"), At.AddMinutes(3));

        Assert.Equal(WrittenAhead.Why.Used, answer.Why);
    }

    /// <summary>
    /// An intro that carried no worked-out facts cannot have been
    /// superseded by one, so the fact check is vacuous rather than a gap.
    /// </summary>
    [Fact]
    public void A_line_with_no_facts_is_never_stale_for_want_of_them()
    {
        var ahead = new WrittenAhead();

        ahead.Hold(Round, Grid, "line", facts: null, At);

        var answer = ahead.TakeFor(Round, Grid, Said("anything", "and:another"), At.AddMinutes(3));

        Assert.Equal(WrittenAhead.Why.Used, answer.Why);
        Assert.Null(answer.Facts);
    }

    [Fact]
    public void A_caller_with_no_ledger_skips_the_fact_check()
    {
        var ahead = new WrittenAhead();

        ahead.Hold(Round, Grid, "line", TwoFacts(), At);

        Assert.Equal(WrittenAhead.Why.Used, ahead.TakeFor(Round, Grid, said: null, At.AddMinutes(1)).Why);
    }

    [Fact]
    public void Words_waiting_too_long_are_not_about_tonight_any_more()
    {
        var ahead = new WrittenAhead();

        ahead.Hold(Round, Grid, "line", TwoFacts(), At);

        var late = At + WrittenAhead.StaysFresh + TimeSpan.FromMinutes(1);

        Assert.Equal(WrittenAhead.Why.TooOld, ahead.TakeFor(Round, Grid, Said(), late).Why);
        Assert.Equal(1, ahead.Wasted);
    }

    // ------------------------------------------------------------------
    //  Holding
    // ------------------------------------------------------------------

    [Fact]
    public void A_model_that_wrote_nothing_is_not_held_and_not_counted()
    {
        var ahead = new WrittenAhead();

        ahead.Hold(Round, Grid, line: null, TwoFacts(), At);
        ahead.Hold(Round, Grid, line: "   ", TwoFacts(), At);
        ahead.Hold(forWhat: null, Grid, "a line with no round", TwoFacts(), At);
        ahead.Hold(forWhat: string.Empty, Grid, "likewise", TwoFacts(), At);

        Assert.Equal(0, ahead.Written);
        Assert.Equal(string.Empty, ahead.HoldingFor);
        Assert.Equal(WrittenAhead.Why.NothingHeld, ahead.TakeFor(Round, Grid, Said(), At).Why);
    }

    [Fact]
    public void Holding_for_names_the_round_so_nothing_is_written_twice()
    {
        var ahead = new WrittenAhead();

        Assert.Equal(string.Empty, ahead.HoldingFor);

        ahead.Hold(Round, Grid, "line", TwoFacts(), At);

        Assert.Equal(Round, ahead.HoldingFor);

        ahead.TakeFor(Round, Grid, Said(), At.AddMinutes(1));

        Assert.Equal(string.Empty, ahead.HoldingFor);
    }

    /// <summary>
    /// The commonest way a held line goes: the club reorders the
    /// programme between races.
    /// </summary>
    [Fact]
    public void The_newest_round_replaces_the_one_before_it()
    {
        var ahead = new WrittenAhead();

        ahead.Hold(Round, Grid, "five", TwoFacts(), At);
        ahead.Hold("r6", Grid, "six", TwoFacts(), At.AddMinutes(1));

        Assert.Equal(2, ahead.Written);
        Assert.Equal(1, ahead.Wasted);
        Assert.Equal("r6", ahead.HoldingFor);

        var answer = ahead.TakeFor("r6", Grid, Said(), At.AddMinutes(2));

        Assert.Equal("six", answer.Line);
    }

    /// <summary>
    /// Writing twice for ONE round is the producer's guard having a hole
    /// in it, so it is counted under a reason of its own - a club should
    /// never see a figure here.
    /// </summary>
    [Fact]
    public void Writing_the_same_round_twice_is_counted_as_its_own_fault()
    {
        var ahead = new WrittenAhead();

        ahead.Hold(Round, Grid, "first words", TwoFacts(), At);
        ahead.Hold(Round, Grid, "second words", TwoFacts(), At.AddSeconds(30));

        Assert.Equal(2, ahead.Written);
        Assert.Equal(1, ahead.Wasted);

        // The newer words win - they were written against a fresher read.
        Assert.Equal("second words", ahead.TakeFor(Round, Grid, Said(), At.AddMinutes(1)).Line);

        Assert.Contains("written ahead twice", ahead.Report()!);
    }

    /// <summary>
    /// A caller that named no round is not evidence the held line is
    /// wrong, so this is the one refusal that keeps the words.
    /// </summary>
    [Fact]
    public void Asking_about_no_round_at_all_keeps_the_words()
    {
        var ahead = new WrittenAhead();

        ahead.Hold(Round, Grid, "line", TwoFacts(), At);

        Assert.Equal(WrittenAhead.Why.NotThatRound, ahead.TakeFor(null, Grid, Said(), At.AddMinutes(1)).Why);
        Assert.Equal(0, ahead.Wasted);
        Assert.Equal(Round, ahead.HoldingFor);

        // And the real call still gets them.
        Assert.Equal(WrittenAhead.Why.Used, ahead.TakeFor(Round, Grid, Said(), At.AddMinutes(1)).Why);
    }

    // ------------------------------------------------------------------
    //  The night
    // ------------------------------------------------------------------

    [Fact]
    public void A_new_race_day_keeps_nothing()
    {
        var ahead = new WrittenAhead();

        ahead.NewMeeting("2026-10-12");
        ahead.Hold(Round, Grid, "line", TwoFacts(), At);

        ahead.NewMeeting("2026-10-19");

        Assert.Equal(string.Empty, ahead.HoldingFor);
        Assert.Equal(0, ahead.Written);
        Assert.Equal(0, ahead.Wasted);
        Assert.Null(ahead.Report());
    }

    [Fact]
    public void The_same_race_day_twice_keeps_what_is_waiting()
    {
        var ahead = new WrittenAhead();

        ahead.NewMeeting("2026-10-12");
        ahead.Hold(Round, Grid, "line", TwoFacts(), At);
        ahead.NewMeeting("2026-10-12");

        Assert.Equal(Round, ahead.HoldingFor);
    }

    // ------------------------------------------------------------------
    //  The report
    // ------------------------------------------------------------------

    [Fact]
    public void A_night_that_wrote_nothing_ahead_reports_nothing()
    {
        Assert.Null(new WrittenAhead().Report());
    }

    [Fact]
    public void A_night_that_wasted_nothing_does_not_list_reasons()
    {
        var ahead = new WrittenAhead();

        ahead.Hold(Round, Grid, "line", TwoFacts(), At);
        ahead.TakeFor(Round, Grid, Said(), At.AddMinutes(1));

        var report = ahead.Report();

        Assert.NotNull(report);
        Assert.Contains("1 intro", report!);
        Assert.Contains("used 1", report);
        Assert.DoesNotContain("Threw", report);
    }

    [Fact]
    public void The_report_says_what_was_thrown_away_and_why()
    {
        var ahead = new WrittenAhead();

        // One used.
        ahead.Hold(Round, Grid, "line", TwoFacts(), At);
        ahead.TakeFor(Round, Grid, Said(), At.AddMinutes(1));

        // One lost to a scratch.
        ahead.Hold("r6", Grid, "line", TwoFacts(), At.AddMinutes(2));
        ahead.TakeFor("r6", new[] { "111" }, Said(), At.AddMinutes(3));

        // One lost because something else said its fact.
        ahead.Hold("r7", Grid, "line", TwoFacts(), At.AddMinutes(4));
        ahead.TakeFor("r7", Grid, Said("venue:oval:record"), At.AddMinutes(5));

        var report = ahead.Report();

        Assert.NotNull(report);
        Assert.Contains("3 intros", report!);
        Assert.Contains("used 1", report);
        Assert.Contains("Threw 2 away", report);
        Assert.Contains("the grid changed after it was written", report);
        Assert.Contains("another segment had already said one of its facts", report);
    }

    [Fact]
    public void Every_reason_has_words_of_its_own()
    {
        foreach (var why in Enum.GetValues<WrittenAhead.Why>())
        {
            var said = WrittenAhead.Because(why);

            Assert.False(string.IsNullOrWhiteSpace(said));
            Assert.DoesNotContain("does not have a word for", said);
        }
    }

    // ------------------------------------------------------------------
    //  The fingerprint
    // ------------------------------------------------------------------

    [Fact]
    public void A_grid_fingerprint_keeps_the_order_and_drops_the_noise()
    {
        Assert.Equal("111|222", WrittenAhead.Fingerprint(new[] { "111", "222" }));
        Assert.Equal("222|111", WrittenAhead.Fingerprint(new[] { "222", "111" }));
        Assert.Equal("111|222", WrittenAhead.Fingerprint(new[] { " 111 ", string.Empty, "222", "   " }));
        Assert.Equal("ABC", WrittenAhead.Fingerprint(new[] { "abc" }));
        Assert.Equal(string.Empty, WrittenAhead.Fingerprint(null));
        Assert.Equal(string.Empty, WrittenAhead.Fingerprint(Array.Empty<string>()));
    }

    /// <summary>
    /// An empty grid matches an empty grid, so a round with no entry list
    /// is not refused on that account - the round and fact checks still
    /// apply to it.
    /// </summary>
    [Fact]
    public void A_round_with_no_entry_list_is_not_refused_for_its_grid()
    {
        var ahead = new WrittenAhead();

        ahead.Hold(Round, Array.Empty<string>(), "line", TwoFacts(), At);

        Assert.Equal(WrittenAhead.Why.Used, ahead.TakeFor(Round, Array.Empty<string>(), Said(), At.AddMinutes(1)).Why);
    }
}
