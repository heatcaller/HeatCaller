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
    [InlineData("Damn, he passed the class.", "Damn, he passed the class.")]
    [InlineData("Pissarro and Shitara on the Wankel.", "Pissarro and Shitara on the Wankel.")]
    public void Swears_are_starred_for_screens_and_names_are_left_alone(string said, string shown)
    {
        Assert.Equal(shown, ScreenWords.Masked(said));
    }
}
