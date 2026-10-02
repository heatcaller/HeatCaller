namespace HeatCaller.Tests;

/// <summary>
/// The window a live "recent average" ghost averages over (2 Oct bug hunt).
/// It used to be a fixed 20 laps for every ghost, and the ghost's own
/// LiveAverageLaps setting (5 by default) was never read, so the ghost was
/// slow to follow a driver who found pace.
/// </summary>
public sealed class BugHuntGhostPaceTests
{
    [Fact]
    public void Averages_only_the_last_laps_in_the_window()
    {
        // Six slow laps, then five quick ones: a five-lap window sees only
        // the quick ones.
        var laps = new long[] { 8000, 8000, 8000, 8000, 8000, 8000, 6000, 6000, 6000, 6000, 6000 };

        Assert.Equal(6000, GhostService.RecentAverageMs(laps, 5));
    }

    [Fact]
    public void A_window_longer_than_the_laps_averages_them_all()
    {
        var laps = new long[] { 6000, 7000, 8000 };

        Assert.Equal(7000, GhostService.RecentAverageMs(laps, 5));
    }

    [Fact]
    public void A_window_under_one_follows_the_latest_lap()
    {
        var laps = new long[] { 8000, 6500 };

        Assert.Equal(6500, GhostService.RecentAverageMs(laps, 0));
        Assert.Equal(6500, GhostService.RecentAverageMs(laps, -3));
    }

    [Fact]
    public void No_laps_is_zero()
    {
        Assert.Equal(0, GhostService.RecentAverageMs(Array.Empty<long>(), 5));
    }
}
