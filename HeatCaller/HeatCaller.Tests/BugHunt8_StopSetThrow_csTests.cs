namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 8 (20 Oct 2026): on a night with no break the throw prompt's own
/// wording ("straight after this") tripped the check's "after this", so every
/// AI line was thrown away. The prompt's wording now comes from StopSetThrow.
/// </summary>
public sealed class BugHunt8_StopSetThrow_csTests
{
    private static readonly StopSetThrow.Names All = StopSetThrow.Names.Of("Friday Night Thunder", "Jess Cole", "Dave Marsh");

    [Fact]
    public void The_no_break_wording_does_not_promise_a_break()
    {
        Assert.False(StopSetThrow.PromisesBreak(StopSetThrow.WhenNoBreak));
        Assert.False(StopSetThrow.PromisesBreak(StopSetThrow.When(breakFollows: false)));
    }

    [Fact]
    public void The_break_wording_does_promise_a_break()
    {
        Assert.True(StopSetThrow.PromisesBreak(StopSetThrow.WhenBreak));
        Assert.True(StopSetThrow.PromisesBreak(StopSetThrow.When(breakFollows: true)));
    }

    [Fact]
    public void A_no_break_line_written_from_the_prompts_own_words_is_used()
    {
        var line = $"Friday Night Thunder rolls on - the race recap with Jess Cole is {StopSetThrow.When(false)}. Dave Marsh here, stay with us.";

        Assert.NotNull(StopSetThrow.Checked(line, All, breakFollows: false));
    }

    [Fact]
    public void A_break_line_written_from_the_prompts_own_words_is_used()
    {
        var line = $"Friday Night Thunder rolls on - the race recap with Jess Cole is {StopSetThrow.When(true)}. Dave Marsh here, stay with us.";

        Assert.NotNull(StopSetThrow.Checked(line, All, breakFollows: true));
    }

    [Theory]
    [InlineData("Friday Night Thunder rolls on - the race recap with Jess Cole is coming up next, straight after this. Dave Marsh here, stay with us.")]
    [InlineData("Friday Night Thunder rolls on - Jess Cole has the race recap, we'll be right back. Dave Marsh here.")]
    public void A_no_break_line_that_implies_a_break_is_still_not_used(string line)
    {
        Assert.Null(StopSetThrow.Checked(line, All, breakFollows: false));
    }
}
