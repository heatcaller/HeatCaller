namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24, #283: a car nobody had named that took a lap record in its
/// first race went onto the public Tracks page under its bare transponder
/// number, because the page only recognised numbers it could match to a
/// profile or a saved result. A holder with a digit and no letter is now
/// treated as a number and goes out as "Unnamed driver".
/// </summary>
public sealed class BugHunt24_RelayDocumentsTests
{
    [Theory]
    [InlineData("4012345")]
    [InlineData(" 4012345 ")]
    [InlineData("40-12345")]
    public void A_bare_transponder_holder_is_recognised_as_a_number(string holder)
    {
        Assert.True(RelayDocuments.LooksLikeBareNumber(holder));
    }

    [Theory]
    [InlineData("Sam Parker")]
    [InlineData("Car 7")]
    [InlineData("Rental 4012345")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("-")]
    public void A_real_name_or_blank_is_not_a_bare_number(string? holder)
    {
        Assert.False(RelayDocuments.LooksLikeBareNumber(holder));
    }
}
