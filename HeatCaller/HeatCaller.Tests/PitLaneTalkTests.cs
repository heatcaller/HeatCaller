namespace HeatCaller.Tests;

/// <summary>
/// The booth and pit lane talking like people who can hear each other
/// (19 Oct 2026): the hand back uses the announcer's first name, and a car
/// with no profile is still named.
/// </summary>
public sealed class PitLaneTalkTests
{
    [Theory]
    [InlineData("Rick Marshal", "Rick")]
    [InlineData("Rick", "Rick")]
    [InlineData("Sir Higginbottom Butt Cheese", "Sir Higginbottom Butt Cheese")]
    [InlineData("Dr. Dave Marsh", "Dr. Dave Marsh")]
    [InlineData("  ", "")]
    [InlineData(null, "")]
    public void First_name_is_the_first_word_unless_it_is_a_title(string? name, string expected)
    {
        Assert.Equal(expected, CommentaryService.FirstName(name));
    }

    [Fact]
    public void The_hand_back_uses_his_first_name()
    {
        Assert.Equal("That's the word from pit lane - back to you, Rick.", RaceControlControl.FlagHandBack("Rick Marshal"));
        Assert.Equal("That's the word from pit lane - back up to the booth.", RaceControlControl.FlagHandBack(""));
    }

    [Theory]
    [InlineData("Terry Byte", true)]
    [InlineData("12345", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void A_name_needs_a_letter(string? name, bool expected)
    {
        Assert.Equal(expected, DriverIntros.LooksLikeAName(name));
    }
}
