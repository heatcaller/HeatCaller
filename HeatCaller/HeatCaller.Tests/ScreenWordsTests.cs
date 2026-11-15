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
