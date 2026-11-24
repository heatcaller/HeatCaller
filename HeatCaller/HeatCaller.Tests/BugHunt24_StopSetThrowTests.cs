namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): the built-in throw to the break said
/// "the The Friday Night Show race recap" when the show's name already
/// started with "The".
/// </summary>
public sealed class BugHunt24_StopSetThrowTests
{
    [Theory]
    [InlineData("The Friday Night Show", "Jess Cole", "The Friday Night Show race recap with Jess Cole")]
    [InlineData("The Friday Night Show", "", "The Friday Night Show race recap")]
    [InlineData("the pit lane show", "Jess Cole", "the pit lane show race recap with Jess Cole")]
    [InlineData("Theatre of Speed", "Jess Cole", "the Theatre of Speed race recap with Jess Cole")]
    [InlineData("Friday Night Thunder", "Jess Cole", "the Friday Night Thunder race recap with Jess Cole")]
    public void A_show_name_that_starts_with_the_gets_no_second_the(string show, string host, string expected)
    {
        Assert.Equal(expected, StopSetThrow.RecapPhrase(StopSetThrow.Names.Of(show, host, "")));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    public void The_built_in_throw_never_says_the_the(int pick, bool breakFollows)
    {
        var names = StopSetThrow.Names.Of("The Friday Night Show", "Jess Cole", "Dave Marsh");

        var line = StopSetThrow.Fallback(names, pick, breakFollows);

        Assert.DoesNotContain("the the", line, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("The Friday Night Show", line);
        Assert.NotNull(StopSetThrow.Checked(line, names, breakFollows: breakFollows));
    }
}
