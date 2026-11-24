namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): the band at the bottom of the Big Screen put
/// up a guessed driver in two ways - two drivers announced by the same name
/// (the higher one on the board won), and a driver whose spoken name is an
/// everyday word ("Will") named under every line using that word.
/// </summary>
public sealed class BugHunt24_LowerThirdTests
{
    private static LowerThird.Candidate Driver(string said, string? shown = null) =>
        new(said, shown ?? said);

    private static IReadOnlyList<string> In(string line, params LowerThird.Candidate[] who) =>
        LowerThird.NamesIn(line, who);

    // ------------------------------------------------------------------
    //  F156: two drivers announced by the same name
    // ------------------------------------------------------------------

    [Fact]
    public void Two_drivers_announced_by_the_same_name_put_neither_on_the_band()
    {
        const string line = "Bill is all over the back of Bellamy.";

        Assert.Equal(
            new[] { "Bellamy" },
            In(line, Driver("Bill", "Bill Hargrove"), Driver("Bill", "Bill Fenwick"), Driver("Bellamy")));

        // Whichever order the standings hand them over in.
        Assert.Equal(
            new[] { "Bellamy" },
            In(line, Driver("Bill", "Bill Fenwick"), Driver("Bill", "Bill Hargrove"), Driver("Bellamy")));
    }

    [Fact]
    public void The_same_spoken_name_typed_in_different_case_is_still_two_drivers()
    {
        Assert.Equal(
            new[] { "Bellamy" },
            In("Bill is all over the back of Bellamy.", Driver("bill", "Bill Hargrove"), Driver("Bill", "Bill Fenwick"), Driver("Bellamy")));
    }

    [Fact]
    public void One_driver_on_two_transponders_under_one_spoken_name_is_still_named()
    {
        Assert.Equal(
            new[] { "Bill Hargrove" },
            In("Bill takes the lead.", Driver("Bill", "Bill Hargrove"), Driver("Bill", "Bill Hargrove")));
    }

    [Fact]
    public void A_shared_short_name_does_not_stop_a_full_name_being_found()
    {
        Assert.Equal(
            new[] { "Bill Hargrove" },
            In(
                "Bill Hargrove takes it.",
                Driver("Bill Hargrove"),
                Driver("Bill", "Bill Fenwick"),
                Driver("Bill", "Bill Marsh")));
    }

    // ------------------------------------------------------------------
    //  F157: a spoken name that is an everyday word
    // ------------------------------------------------------------------

    [Fact]
    public void A_driver_called_Will_is_not_named_by_the_word_will()
    {
        Assert.Equal(
            new[] { "Bellamy" },
            In("Bellamy will have to settle for second.", Driver("Bellamy"), Driver("Will", "Will Fenwick")));

        Assert.Equal(
            new[] { "Hargrove" },
            In("Hargrove sets the mark tonight.", Driver("Hargrove"), Driver("Mark", "Mark Doyle")));

        Assert.Empty(In("And the chase is on.", Driver("Chase", "Chase Marsh")));
    }

    [Fact]
    public void A_capitalised_Will_later_in_the_same_line_is_still_found()
    {
        Assert.Equal(
            new[] { "Bellamy", "Will Fenwick" },
            In("Bellamy will not catch Will today.", Driver("Bellamy"), Driver("Will", "Will Fenwick")));
    }

    [Fact]
    public void A_line_all_in_one_case_still_matches_the_name_whatever_its_case()
    {
        // There the case tells us nothing, so it is not used.
        Assert.Equal(new[] { "Hargrove" }, In("HARGROVE takes the lead!", Driver("Hargrove")));
        Assert.Equal(new[] { "Hargrove" }, In("hargrove takes the lead!", Driver("Hargrove")));
        Assert.Equal(new[] { "Will Fenwick" }, In("WILL TAKES THE LEAD!", Driver("Will", "Will Fenwick")));
    }

    [Fact]
    public void The_everyday_word_rule_is_only_for_one_word_names_typed_with_a_capital()
    {
        Assert.True(LowerThird.IsJustTheEverydayWord("Bellamy will win.", 8, "Will"));
        Assert.False(LowerThird.IsJustTheEverydayWord("Bellamy Will win.", 8, "Will"));
        Assert.False(LowerThird.IsJustTheEverydayWord("bellamy will win.", 8, "Will"));
        Assert.False(LowerThird.IsJustTheEverydayWord("Bellamy will win.", 8, "will"));
        Assert.False(LowerThird.IsJustTheEverydayWord("Then billy bob wins.", 5, "Billy Bob"));
    }
}
