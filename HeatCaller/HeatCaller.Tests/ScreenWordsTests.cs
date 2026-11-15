namespace HeatCaller.Tests;

/// <summary>
/// The two word lists that came in with Sailor-mouth (15 Nov 2026): hate
/// terms, which drop a line, and swears, which are starred on screens only.
/// </summary>
public sealed class ScreenWordsTests
{
    [Theory]
    [InlineData("Sam leads that kike by a tenth.")]
    [InlineData("What a K1KE move.")]
    [InlineData("Bill drives like a f.a.g on lap twelve.")]
    [InlineData("Faggots, the lot of them.")]
    [InlineData("Some n*gger in the pits.")]
    [InlineData("Sieg heil, Sam wins.")]
    [InlineData("He's retarded on the brakes.")]
    public void A_hate_term_is_found_through_simple_disguises(string line)
    {
        Assert.NotNull(ScreenWords.HateTermIn(line));
    }

    [Theory]
    [InlineData("Sam's four fucking tenths clear of Bill, and the shit's hit the fan.")]
    [InlineData("A chink in his armor at the hairpin.")]
    [InlineData("Poof - he's gone.")]
    [InlineData("Pakistan's Ali takes the A-Main.")]
    [InlineData("Fire retardant on the marshal post.")]
    [InlineData("Step on the gas, the whole lap.")]
    [InlineData("Sam's Wankel-powered car? Never.")]
    [InlineData("")]
    public void Ordinary_and_sweary_lines_are_not_hate(string line)
    {
        Assert.Null(ScreenWords.HateTermIn(line));
    }

    [Fact]
    public void A_name_the_club_typed_is_not_counted_against_the_line()
    {
        Assert.Null(ScreenWords.HateTermIn("Mong takes the win.", "Drivers: Mong, Sam"));
        Assert.NotNull(ScreenWords.HateTermIn("Mong takes the win.", "Drivers: Sam"));
    }

    [Theory]
    [InlineData("Four fucking tenths!", "Four f****** tenths!")]
    [InlineData("Bullshit, that's a shitty line.", "B*******, that's a s***** line.")]
    [InlineData("Pissed off and a bastard of a gap.", "P***** off and a b****** of a gap.")]
    [InlineData("What an ass.", "What an a**.")]
    [InlineData("Damn, he passed the class.", "D***, he passed the class.")]
    [InlineData("Pissarro and Shitara on the Wankel.", "Pissarro and Shitara on the Wankel.")]
    [InlineData("What the hell, a crappy pass and a dick move.", "What the h***, a c***** pass and a d*** move.")]
    [InlineData("Prick of a gap, cock-up at the hairpin, damn it.", "P**** of a gap, c***-up at the hairpin, d*** it.")]
    [InlineData("Hello Dickson, Cockburn and Shellie.", "Hello Dickson, Cockburn and Shellie.")]
    public void Swears_are_starred_for_screens_and_names_are_left_alone(string said, string shown)
    {
        Assert.Equal(shown, ScreenWords.Masked(said));
    }

    [Fact]
    public void A_driver_whose_name_is_a_swear_shows_as_himself()
    {
        var keep = ScreenWords.NameWords(new[] { "Dick Hell", null, "Sam O'Neil" });

        Assert.Equal("Dick Hell leads, a d*** clear.", ScreenWords.Masked("Dick Hell leads, a damn clear.", keep));
        Assert.Equal("dick leads.", ScreenWords.Masked("dick leads.", keep));
    }
}

/// <summary>The Sailor-mouth dial (15 Nov 2026): ScreenWords.WithinSwears.</summary>
public sealed class SwearDialTests
{
    private const string Six =
        "Fucking hell, Sam is fucking flying, Bill is shit out of luck, what a damn mess, holy shit.";

    [Fact]
    public void A_line_within_the_dial_is_left_as_it_is()
    {
        Assert.Equal("Sam is fucking clear.", ScreenWords.WithinSwears("Sam is fucking clear.", 2));
    }

    [Fact]
    public void Spare_intensifiers_come_out_last_first()
    {
        Assert.Equal(
            "Sam is fucking clear and Bill is stuck.",
            ScreenWords.WithinSwears("Sam is fucking clear and Bill is fucking stuck.", 1));
    }

    [Fact]
    public void A_sentence_that_loses_its_first_word_starts_with_a_capital()
    {
        Assert.Equal("Brilliant lap, Sam.", ScreenWords.WithinSwears("Fucking brilliant lap, Sam.", 0));
    }

    [Fact]
    public void Damn_it_is_not_an_intensifier_so_a_short_line_still_over_is_dropped()
    {
        Assert.Null(ScreenWords.WithinSwears("Damn it, Bill.", 0));
    }

    [Fact]
    public void Six_in_one_short_line_comes_down_to_the_dial_or_is_dropped()
    {
        var two = ScreenWords.WithinSwears(Six, 2);

        Assert.True(two is null || ScreenWords.SwearCount(two) <= 2);
        Assert.Equal(6, ScreenWords.SwearCount(Six));
    }

    [Fact]
    public void A_long_piece_gets_the_dial_for_every_forty_words()
    {
        var filler = string.Join(" ", Enumerable.Repeat("Sam leads on lap twelve.", 10));
        var piece = filler + " Shit. Shit. Shit.";

        // About 53 words: two lines' worth, so two a line allows four.
        Assert.Equal(piece, ScreenWords.WithinSwears(piece, 2));
    }

    [Fact]
    public void A_drivers_name_is_not_counted()
    {
        Assert.Equal("Dick is fucking clear.", ScreenWords.WithinSwears("Dick is fucking clear.", 1, new[] { "Dick" }));
        Assert.Null(ScreenWords.WithinSwears("Dick is fucking clear, damn it.", 1));
    }
}
