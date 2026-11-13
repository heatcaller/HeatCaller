namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026), the noun "point" in front of a number word.
///
/// "At this point three drivers can still win the title" was read as 0.3,
/// and "trails by one point five rounds from the end" as 1.5. Neither is in
/// any fact, so a correct sentence was cut as an invented figure. A word in
/// front that only a noun takes, or a count of whole things after the
/// number, now says the point is not a decimal point.
/// </summary>
public sealed class BugHunt17_CommentaryNumberCheckTests
{
    private static List<double> Values(string text) =>
        CommentaryNumberCheck.FiguresIn(text, false).Select(f => Math.Round(f.Value, 3)).ToList();

    [Theory]
    [InlineData("At this point three drivers can still win the title", 3)]
    [InlineData("at that point twenty cars were on the lead lap", 20)]
    [InlineData("the turning point four laps in", 4)]
    [InlineData("Bill trails by one point five rounds from the end", 5)]
    public void The_noun_point_before_a_count_is_not_a_decimal(string text, double count)
    {
        Assert.Equal(new[] { count }, Values(text));
    }

    [Theory]
    [InlineData("point three back", 0.3)]
    [InlineData("the gap is point three", 0.3)]
    [InlineData("one point five seconds clear", 1.5)]
    [InlineData("one point five back", 1.5)]
    [InlineData("four point nine", 4.9)]
    public void A_real_decimal_is_still_read(string text, double expected)
    {
        Assert.Equal(new[] { expected }, Values(text));
    }

    [Fact]
    public void A_championship_sentence_with_the_noun_point_is_not_cut()
    {
        Assert.Null(CommentaryNumberCheck.Unsupported(
            "At this point three drivers can still win the title.",
            "Three drivers can still win the title tonight."));

        Assert.Null(CommentaryNumberCheck.Unsupported(
            "Bill trails by one point five rounds from the end.",
            "Bill is 1 point behind Sam. 5 rounds remain."));
    }
}
