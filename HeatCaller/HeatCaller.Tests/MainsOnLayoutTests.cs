namespace HeatCaller.Tests;

/// <summary>
/// The run of mains on a layout, counted in meetings (11 Oct 2026) - the
/// one claim in the broadcast no human announcer could make.
///
/// Nobody standing at a club track holds "who won the main on this layout
/// at each of the last fourteen meetings" in their head, across three
/// classes and four layouts and two years of Thursday nights. The app does,
/// because it filed every one of them.
///
/// As CommentaryRaceFacts' own note says, the tests here are mostly about
/// the nulls: a club in its first weeks must get silence rather than one
/// evening dressed up as a dynasty.
/// </summary>
public sealed class MainsOnLayoutTests
{
    private const string Layout = "Clubhouse Loop";
    private const string Cls = "Stock 17.5";

    private static MainsOnLayout Mains(string holderId, string holderName, int run, int meetings) =>
        new(Layout, Cls, meetings, holderName, holderId, run);

    // ------------------------------------------------------------------
    //  The run that just ended - the half that did not exist before
    // ------------------------------------------------------------------

    [Fact]
    public void A_run_that_ends_tonight_names_both_drivers_and_the_count_the_run_actually_covered()
    {
        var fact = CommentaryRaceFacts.RunOfMainsHere(
            Mains("T-BILL", "Bill Hargrove", run: 14, meetings: 31), "T-SAM", "Sam Teague", "race-1");

        Assert.NotNull(fact);

        // The run counted is the one BEFORE tonight, so the number said is
        // exactly what the reader found. Tonight is the meeting that ended
        // it and is not counted into it.
        // Through the same speller the line uses - Spell only words the
        // small counts, and a literal here would pin the wrong one.
        Assert.Contains($"{CommentaryHistoryFacts.Spell(14)} club meetings", fact!.Text, StringComparison.Ordinal);
        Assert.Contains("Bill Hargrove", fact.Text, StringComparison.Ordinal);
        Assert.Contains("Sam Teague", fact.Text, StringComparison.Ordinal);
        Assert.Contains("ended that run", fact.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_run_that_ends_is_themed_on_the_track_rather_than_either_driver()
    {
        // Two drivers are named in it and neither is what it is about. On
        // the winner it would be held back because a form guide mentioned
        // them, which is the wrong line to lose.
        var fact = CommentaryRaceFacts.RunOfMainsHere(
            Mains("T-BILL", "Bill", 14, 31), "T-SAM", "Sam", "race-1");

        Assert.Equal(FactTheme.Venue(Layout), fact!.Theme);
    }

    // ------------------------------------------------------------------
    //  The run that carries on
    // ------------------------------------------------------------------

    [Fact]
    public void A_run_the_winner_extends_counts_tonight_in()
    {
        var fact = CommentaryRaceFacts.RunOfMainsHere(
            Mains("T-BILL", "Bill Hargrove", run: 4, meetings: 20), "T-BILL", "Bill Hargrove", "race-1");

        Assert.NotNull(fact);

        // Four before tonight, and tonight makes five. Nothing is
        // embroidered: the reader's four is not said as five of its own.
        Assert.Contains($"{CommentaryHistoryFacts.Spell(5)} club meetings in a row", fact!.Text, StringComparison.Ordinal);
        Assert.Contains("Bill Hargrove", fact.Text, StringComparison.Ordinal);

        // Said about the person, so themed on them.
        Assert.Equal(FactTheme.Driver("T-BILL"), fact.Theme);
    }

    [Fact]
    public void The_two_halves_are_never_both_true_of_one_race()
    {
        // A run extended and a run ended are the same sentence from two
        // sides, so the aggregator carries at most one line.
        var extended = CommentaryRaceFacts.ForTheMainsHere(
            Mains("T-BILL", "Bill", 5, 20), "T-BILL", "Bill", "race-1");

        var ended = CommentaryRaceFacts.ForTheMainsHere(
            Mains("T-BILL", "Bill", 5, 20), "T-SAM", "Sam", "race-1");

        Assert.Single(extended);
        Assert.Single(ended);
        Assert.NotEqual(extended[0].Text, ended[0].Text);
    }

    // ------------------------------------------------------------------
    //  The nulls
    // ------------------------------------------------------------------

    [Fact]
    public void A_club_in_its_first_weeks_on_a_layout_says_nothing()
    {
        // A run that IS the whole history of the layout is not a run - it
        // is the layout being new.
        Assert.Empty(CommentaryRaceFacts.ForTheMainsHere(
            Mains("T-BILL", "Bill", run: 3, meetings: CommentaryRaceFacts.LeastMeetingsHere - 1),
            "T-SAM", "Sam", "race-1"));
    }

    [Fact]
    public void Two_in_a_row_is_a_coincidence_rather_than_a_run()
    {
        Assert.Empty(CommentaryRaceFacts.ForTheMainsHere(
            Mains("T-BILL", "Bill", run: CommentaryRaceFacts.LeastRunToCall - 1, meetings: 20),
            "T-SAM", "Sam", "race-1"));
    }

    [Fact]
    public void The_thresholds_are_the_right_way_round()
    {
        // The run is the claim, so the layout must have more meetings on
        // it than the run is long before the run is worth saying.
        Assert.True(
            CommentaryRaceFacts.LeastMeetingsHere > CommentaryRaceFacts.LeastRunToCall,
            "A run as long as the layout's whole history would be reported as a dynasty.");
    }

    [Theory]
    [InlineData("", "Sam")]
    [InlineData("T-SAM", "")]
    [InlineData(null, "Sam")]
    [InlineData("T-SAM", null)]
    public void A_winner_with_no_name_or_no_transponder_is_no_fact(string? winnerId, string? winnerName)
    {
        // An anonymous line is not a weaker version of this fact; it is a
        // sentence the app cannot stand behind.
        Assert.Null(CommentaryRaceFacts.RunOfMainsHere(
            Mains("T-BILL", "Bill", 14, 31), winnerId, winnerName, "race-1"));
    }

    [Fact]
    public void A_run_with_no_holder_name_is_no_fact()
    {
        Assert.Null(CommentaryRaceFacts.RunOfMainsHere(
            Mains("T-BILL", string.Empty, 14, 31), "T-SAM", "Sam", "race-1"));
    }

    [Fact]
    public void A_reader_that_found_nothing_is_nothing_rather_than_a_blank_line()
    {
        Assert.Empty(CommentaryRaceFacts.ForTheMainsHere(null, "T-SAM", "Sam", "race-1"));
    }

    // ------------------------------------------------------------------
    //  The scope is in the line
    // ------------------------------------------------------------------

    [Fact]
    public void Both_the_layout_and_the_class_are_named_in_whichever_line_goes_out()
    {
        // "He has won the main here every time" means nothing without the
        // layout, and at a club with three classes it means nothing
        // without the class either - there are three mains a night.
        foreach (var (winnerId, winnerName) in new[] { ("T-BILL", "Bill"), ("T-SAM", "Sam") })
        {
            var fact = CommentaryRaceFacts.RunOfMainsHere(
                Mains("T-BILL", "Bill", 14, 31), winnerId, winnerName, "race-1");

            Assert.NotNull(fact);
            Assert.Contains(Layout, fact!.Text, StringComparison.Ordinal);
            Assert.Contains(Cls, fact.Text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_key_is_this_races_rather_than_the_nights()
    {
        // A run ending is news about THIS result, and the next main of the
        // evening is a different class with a different run - so two mains
        // on one night must not share a key and silence the second.
        var first = CommentaryRaceFacts.RunOfMainsHere(Mains("T-BILL", "Bill", 14, 31), "T-SAM", "Sam", "race-1");
        var second = CommentaryRaceFacts.RunOfMainsHere(Mains("T-BILL", "Bill", 14, 31), "T-SAM", "Sam", "race-2");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first!.Key, second!.Key);
    }

    [Fact]
    public void Two_classes_on_one_night_do_not_share_a_key_either()
    {
        var stock = CommentaryRaceFacts.RunOfMainsHere(
            new MainsOnLayout(Layout, "Stock 17.5", 31, "Bill", "T-BILL", 14), "T-SAM", "Sam", "race-1");

        var open = CommentaryRaceFacts.RunOfMainsHere(
            new MainsOnLayout(Layout, "Open", 31, "Bill", "T-BILL", 14), "T-SAM", "Sam", "race-1");

        Assert.NotEqual(stock!.Key, open!.Key);
    }
}
