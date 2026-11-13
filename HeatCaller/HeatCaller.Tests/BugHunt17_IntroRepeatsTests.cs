namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17: a driver named in somebody else's introduction ("two tenths
/// clear of Bill Jones") counted as introduced, so once the count reached
/// the whole grid their own line was cut as the start of a second pass. A
/// sentence about one driver who has only been named in passing is now
/// kept as their introduction.
/// </summary>
public sealed class BugHunt17_IntroRepeatsTests
{
    private static readonly string[] Grid = { "Sam Smith", "Bill Jones", "Frank Lee", "Pat Kim", "The Flying Dutchman" };

    [Fact]
    public void On_a_two_car_grid_second_place_keeps_their_line_after_the_pole_margin_names_them()
    {
        const string Line = "On pole, Sam Smith, two tenths clear of Bill Jones. Alongside him, Bill Jones.";
        var notes = new List<string>();

        Assert.Equal(Line, IntroRepeats.WithoutSecondPass(Line, new[] { "Sam Smith", "Bill Jones" }, notes));
        Assert.Empty(notes);
    }

    [Fact]
    public void The_last_driver_keeps_their_line_when_the_one_before_mentions_them()
    {
        const string Line = "On pole, Sam Smith. Bill Jones second. Frank Lee third. "
                            + "Pat Kim fourth, just ahead of The Flying Dutchman. And The Flying Dutchman at the back. Let's go racing!";

        Assert.Equal(Line, IntroRepeats.WithoutSecondPass(Line, Grid));
    }

    [Fact]
    public void A_second_pass_after_that_line_is_still_cut()
    {
        var said = IntroRepeats.WithoutSecondPass(
            "On pole, Sam Smith, two tenths clear of Bill Jones. Alongside him, Bill Jones. "
            + "So that's Sam on pole and Bill second. Bill Jones is the one to watch. Let's go racing!",
            new[] { "Sam Smith", "Bill Jones" });

        Assert.Equal(
            "On pole, Sam Smith, two tenths clear of Bill Jones. Alongside him, Bill Jones. Let's go racing!",
            said);
    }
}
