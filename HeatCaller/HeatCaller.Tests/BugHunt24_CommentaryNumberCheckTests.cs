namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): the number check read the ordinal in "a
/// tenth-place finish" or "a tenth win" as 0.1 of a second, and cut a true
/// sentence about the driver who came tenth. See CommentaryNumberCheck.
/// </summary>
public sealed class BugHunt24_CommentaryNumberCheckTests
{
    private static List<double> Values(string text, bool forFacts = false) =>
        CommentaryNumberCheck.FiguresIn(text, forFacts).Select(f => Math.Round(f.Value, 3)).ToList();

    [Theory]
    [InlineData("a tenth-place finish for Dee")]
    [InlineData("a tenth win here for Sam")]
    [InlineData("a hundredth start")]
    [InlineData("a tenth straight podium")]
    [InlineData("an eighth win and a tenth podium")]
    public void BugHunt24_ATenthAsAPlaceIsNoFigure(string text)
    {
        Assert.Empty(Values(text));
    }

    [Fact]
    public void BugHunt24_TenthPlaceSentenceIsNotCut()
    {
        Assert.Null(CommentaryNumberCheck.Unsupported("Dee Ploy comes home with a tenth-place finish.", "P10 Dee Ploy, 21 laps"));
    }

    [Theory]
    [InlineData("a tenth")]
    [InlineData("a tenth clear of Bill")]
    [InlineData("a tenth a lap")]
    [InlineData("a tenth of a second")]
    [InlineData("a tenth-of-a-second gap")]
    public void BugHunt24_ATenthOfASecondStillReads(string text)
    {
        Assert.Contains(0.1, Values(text));
    }

    [Fact]
    public void BugHunt24_AnUnbackedTenthOfASecondIsStillCaught()
    {
        Assert.Equal("a tenth", CommentaryNumberCheck.Unsupported("Sam wins by a tenth.", "P1 Sam, 22 laps"));
    }
}
