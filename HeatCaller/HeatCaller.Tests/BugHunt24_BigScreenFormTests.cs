namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): the Big Screen as a commercial break ends.
/// </summary>
public sealed class BugHunt24_BigScreenFormTests
{
    // #118 (F063): a break ending during a paid read's last seconds took the
    // read's picture down, and the read put it back half a second later.

    [Fact]
    public void BugHunt24_A_break_ending_during_a_paid_read_leaves_the_read_on_screen()
    {
        Assert.False(BigScreenForm.BreakHandBackTakesTheSlideDown(
            showingSponsor: true, videoUp: false, showingRead: true));
    }

    [Fact]
    public void BugHunt24_A_break_ending_on_a_still_with_no_read_still_takes_it_down()
    {
        Assert.True(BigScreenForm.BreakHandBackTakesTheSlideDown(
            showingSponsor: true, videoUp: false, showingRead: false));
    }

    [Fact]
    public void BugHunt24_A_break_ending_on_a_video_still_lets_it_run()
    {
        Assert.False(BigScreenForm.BreakHandBackTakesTheSlideDown(
            showingSponsor: true, videoUp: true, showingRead: false));
    }

    [Fact]
    public void BugHunt24_A_break_ending_with_no_slide_up_takes_nothing_down()
    {
        Assert.False(BigScreenForm.BreakHandBackTakesTheSlideDown(
            showingSponsor: false, videoUp: false, showingRead: false));
    }
}
