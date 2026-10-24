namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 10 (23 Oct 2026): the Qualifying Standings double-click detail
/// told the director the counted laps "should total" the ranked consecutive
/// figure, which has the driver's penalties added in - so for a penalized
/// driver the Lap Times figures could never match. ConsecutiveDetail now
/// says a penalty may be inside the figure, or itemizes it when known.
/// </summary>
public sealed class BugHunt10_QualifyingStandingsFormTests
{
    [Fact]
    public void Unknown_penalty_says_the_figure_may_include_one()
    {
        var text = QualifyingStandingsForm.ConsecutiveDetail(2, 3, 40_650, penaltyMs: null);

        Assert.Contains("Check laps 2 to 4", text);
        Assert.Contains("40.650s", text);
        Assert.Contains("penalty", text);
        Assert.DoesNotContain("should total 40.650s", text);
    }

    [Fact]
    public void Known_penalty_is_itemized_so_the_laps_add_up()
    {
        var text = QualifyingStandingsForm.ConsecutiveDetail(2, 3, 40_650, penaltyMs: 5_000);

        Assert.Contains("Check laps 2 to 4", text);
        Assert.Contains("should total 35.650s", text);
        Assert.Contains("plus 5.000s in penalties = 40.650s", text);
    }

    [Fact]
    public void No_penalty_keeps_the_plain_total()
    {
        var text = QualifyingStandingsForm.ConsecutiveDetail(1, 3, 35_650, penaltyMs: 0);

        Assert.Contains("Check laps 1 to 3 - they should total 35.650s.", text);
        Assert.DoesNotContain("penalt", text);
    }
}
