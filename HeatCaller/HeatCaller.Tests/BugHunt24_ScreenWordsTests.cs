namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, the word lists (ScreenWords).
///
/// "Spic and span" without the k still read as a slur, so the line was
/// dropped. And a sponsor or team with 's ("Dick's Sporting Goods") was
/// still starred and counted as a swear when no other name held the bare word.
/// </summary>
public sealed class BugHunt24_ScreenWordsTests
{
    [Theory]
    [InlineData("The car is spic and span after the rebuild.")]
    [InlineData("Spic-and-span and ready for the A-main!")]
    [InlineData("Spic 'n' span tonight.")]
    [InlineData("Spick and span, as ever.")]
    public void Spic_and_span_without_the_k_is_an_everyday_phrase(string line)
    {
        Assert.Null(ScreenWords.HateTermIn(line));
    }

    [Fact]
    public void Spic_on_its_own_is_still_caught_next_to_the_phrase()
    {
        Assert.Equal("spic", ScreenWords.HateTermIn("That spic."));
        Assert.Equal("spic", ScreenWords.HateTermIn("Spic and span, that spic."));
        Assert.Equal("spick", ScreenWords.HateTermIn("What a spick."));
    }

    [Fact]
    public void A_sponsor_with_an_apostrophe_s_is_kept_on_its_own()
    {
        var driver = new DriverProfile
        {
            Transponder = "101",
            DriverName = "Sam Jones",
            Team = "Hell's Kitchen Racing",
            Sponsors = "Dick's Sporting Goods",
        };

        var keep = ScreenWords.ProperNameWords(new[] { driver }).ToList();

        Assert.Equal(
            "Thanks to Dick's Sporting Goods and Hell's Kitchen Racing, d*** good.",
            ScreenWords.Masked("Thanks to Dick's Sporting Goods and Hell's Kitchen Racing, damn good.", keep));
        Assert.Equal(0, ScreenWords.SwearCount("Dick's Sporting Goods", keep));
        Assert.Equal(
            "Dick's Sporting Goods is fucking quick.",
            ScreenWords.WithinSwears("Dick's Sporting Goods is fucking quick.", 1, keep));
    }

    [Fact]
    public void A_possessive_name_word_keeps_both_forms_and_a_plain_swear_is_still_starred()
    {
        var keep = ScreenWords.NameWords(new[] { "Dick's Sporting Goods", "The Dicks' Team", "Sam O'Neil" }).ToList();

        Assert.Contains("Dick's", keep);
        Assert.Contains("Dick", keep);
        Assert.Contains("Dicks", keep);
        Assert.Contains("O'Neil", keep);
        // The first letter stays and the rest is starred, as everywhere else
        // (ScreenWordsTests: "shit" -> "s***").
        Assert.Equal("What a s*** lap.", ScreenWords.Masked("What a shit lap.", keep));
    }
}
