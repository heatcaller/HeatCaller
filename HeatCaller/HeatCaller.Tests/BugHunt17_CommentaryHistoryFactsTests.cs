namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026), the ordinal in "at the 112th attempt".
///
/// Every heat and main is a start, so a regular passes a hundred of them in
/// a season or two. The ordinal helper only knew 11, 12 and 13 as "th", so
/// a first win at the 112th try was handed to the recap as "112nd", and a
/// 111th win as "111st".
/// </summary>
public sealed class BugHunt17_CommentaryHistoryFactsTests
{
    [Theory]
    [InlineData(111, "111th")]
    [InlineData(112, "112th")]
    [InlineData(113, "113th")]
    [InlineData(213, "213th")]
    [InlineData(11, "11th")]
    [InlineData(101, "101st")]
    [InlineData(122, "122nd")]
    [InlineData(23, "23rd")]
    public void An_ordinal_past_a_hundred_keeps_its_teens(int number, string said)
    {
        Assert.Equal(said, CommentaryFacts.Ordinal(number));
    }

    [Fact]
    public void A_first_win_at_the_112th_attempt_is_said_properly()
    {
        var block = CommentaryHistoryFacts.ResultInContext(
            new DriverForm("sam", "Sam", 111, 0, 30, null, null), 1, "race-1");

        Assert.NotNull(block);
        Assert.Contains("112th attempt", block!.Text);
    }

    [Fact]
    public void A_111th_win_is_said_properly()
    {
        var block = CommentaryHistoryFacts.ResultInContext(
            new DriverForm("sam", "Sam", 400, 110, 200, null, null), 1, "race-1");

        Assert.NotNull(block);
        Assert.Contains("111th win", block!.Text);
    }
}
