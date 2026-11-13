namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17: the label strip in LlmText.CleanLine took the start off any
/// line whose first word held a colon, so "2:30 left on the clock" was said
/// as "30 left on the clock". Only a word of letters is a label.
/// </summary>
public sealed class BugHunt17_LlmTextTests
{
    [Theory]
    [InlineData("2:30 left on the clock and Hargrove still leads.")]
    [InlineData("1:05 is the gap now, and it is coming down.")]
    [InlineData("12:45 on the clock, and nobody is giving an inch.")]
    public void A_line_that_opens_with_a_time_keeps_it(string line)
    {
        Assert.Equal(line, LlmText.CleanLine(line, maxTokens: 64));
    }

    [Theory]
    [InlineData("Commentary: Hargrove still leads by a second.")]
    [InlineData("Announcer: Hargrove still leads by a second.")]
    public void A_label_is_still_taken_off(string line)
    {
        Assert.Equal("Hargrove still leads by a second.", LlmText.CleanLine(line, maxTokens: 64));
    }

    /// <summary>
    /// The first fix took only a word of letters, so a label the model had
    /// wrapped in quotes or punctuation - the local model quotes its whole
    /// answer often - was no longer taken off, and was spoken.
    /// </summary>
    [Theory]
    [InlineData("\"Commentary: Hargrove still leads by a second.\"")]
    [InlineData("“Commentary: Hargrove still leads by a second.”")]
    [InlineData("[Announcer]: Hargrove still leads by a second.")]
    [InlineData("Play-by-play: Hargrove still leads by a second.")]
    [InlineData("Co-host: Hargrove still leads by a second.")]
    public void A_quoted_or_punctuated_label_is_still_taken_off(string line)
    {
        Assert.Equal("Hargrove still leads by a second.", LlmText.CleanLine(line, maxTokens: 64));
    }

    [Fact]
    public void A_quoted_line_that_opens_with_a_time_keeps_it()
    {
        Assert.Equal(
            "2:30 left on the clock and Hargrove still leads.",
            LlmText.CleanLine("\"2:30 left on the clock and Hargrove still leads.\"", maxTokens: 64));
    }

    [Fact]
    public void A_label_in_front_of_a_time_goes_and_the_time_stays()
    {
        Assert.Equal(
            "2:30 left on the clock.",
            LlmText.CleanLine("Commentary: 2:30 left on the clock.", maxTokens: 64));
    }
}
