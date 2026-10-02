namespace HeatCaller.Tests;

/// <summary>
/// What a live ghost starts at before the session has laps (2 Oct bug hunt,
/// second pass). The lap-time box always holds a value, 7.000s by default, so
/// the driver's best was never used and a ghost left on the default started
/// at 7s instead of the driver's PB.
/// </summary>
public sealed class BugHuntH2GhostTests
{
    [Fact]
    public void Untouched_default_gives_way_to_the_drivers_best()
    {
        Assert.Equal(10000, GhostService.LiveStartLapMs(new GhostCar().FixedLapMs, 10000));
    }

    [Fact]
    public void A_typed_time_comes_before_the_drivers_best()
    {
        Assert.Equal(8000, GhostService.LiveStartLapMs(8000, 6500));
    }

    [Fact]
    public void Default_is_used_when_there_is_no_best()
    {
        Assert.Equal(7000, GhostService.LiveStartLapMs(7000, null));
    }

    [Fact]
    public void No_time_and_no_best_resolves_to_nothing()
    {
        Assert.Null(GhostService.LiveStartLapMs(0, null));
        Assert.Equal(9000, GhostService.LiveStartLapMs(0, 9000));
    }
}
