namespace HeatCaller.Tests;

/// <summary>
/// Ghost pace adjustment (2 Oct bug hunt): a live-pace ghost used to apply
/// its pace percent only at the green, then run at the driver's raw pace
/// from the first re-pace on. Both paths now share one helper, so these
/// pin down what that helper does.
/// </summary>
public sealed class BugHuntGhostsTests
{
    [Fact]
    public void Quicker_percent_shortens_the_lap()
    {
        // 98% of a 7.000s lap is 6.860s - the "stay just ahead" ghost.
        Assert.Equal(6860, GhostService.ApplyPaceAdjustment(7000, 98));
    }

    [Fact]
    public void Slower_percent_lengthens_the_lap()
    {
        Assert.Equal(7700, GhostService.ApplyPaceAdjustment(7000, 110));
    }

    [Fact]
    public void Hundred_percent_leaves_the_lap_alone()
    {
        Assert.Equal(7000, GhostService.ApplyPaceAdjustment(7000, 100));
    }

    [Fact]
    public void Percent_is_clamped_to_the_edit_form_range()
    {
        Assert.Equal(3500, GhostService.ApplyPaceAdjustment(7000, 10));
        Assert.Equal(14000, GhostService.ApplyPaceAdjustment(7000, 500));
    }

    [Fact]
    public void Never_under_half_a_second()
    {
        Assert.Equal(500, GhostService.ApplyPaceAdjustment(600, 50));
    }
}
