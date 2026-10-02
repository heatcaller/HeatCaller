namespace HeatCaller.Tests;

/// <summary>
/// The heat sheet and run sheet print every round and every line, over as
/// many pages as they need. Both used to stop at the foot of one page and
/// tell the club to print "one class at a time", which the app already did,
/// so whatever did not fit was never printed anywhere.
/// </summary>
public class BugHuntPrintoutsTests
{
    [Fact]
    public void SixRoundsOfEightGoOnTwoPagesWithNoneLeftOut()
    {
        // 16 drivers in two heats of 8 over three qualifying rounds - the
        // night the report describes. Four rounds of 8 fit on a page.
        var plan = HeatSheetRenderer.PlanPages(new[] { 8, 8, 8, 8, 8, 8 });

        Assert.Equal(new[] { 4, 2 }, plan.ToArray());
    }

    [Fact]
    public void EveryRoundIsOnSomePage()
    {
        var sizes = Enumerable.Range(0, 40).Select(i => i % 11).ToArray();

        var plan = HeatSheetRenderer.PlanPages(sizes);

        Assert.Equal(sizes.Length, plan.Sum());
        Assert.All(plan, count => Assert.True(count > 0));
    }

    [Fact]
    public void AnEmptyClassStillGetsOnePage()
    {
        Assert.Equal(new[] { 0 }, HeatSheetRenderer.PlanPages(Array.Empty<int>()).ToArray());
    }

    [Fact]
    public void ARoundTooTallForAnyPageStillMovesThePagingOn()
    {
        // Fifty drivers in one heat cannot fit a page; each still gets one
        // rather than the paging never getting past it.
        var plan = HeatSheetRenderer.PlanPages(new[] { 50, 50, 2 });

        Assert.Equal(new[] { 1, 1, 1 }, plan.ToArray());
    }

    [Fact]
    public void TheRunSheetFitsAsManyLinesAsTheOnePageDidBefore()
    {
        // The single page drew lines from y = 188 in 42 px steps while a
        // 40 px line still ended by y = 1580: 33 of them.
        Assert.Equal(33, RunSheetRenderer.RowsPerPage);
    }
}
