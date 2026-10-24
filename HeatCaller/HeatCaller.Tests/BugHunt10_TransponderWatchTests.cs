namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 10 (23 Oct 2026): TransponderWatch judges signal on the passes
/// inside its window only, not on every pass since the app started.
/// </summary>
public sealed class BugHunt10_TransponderWatchTests
{
    private static DirectLapEvent Pass(string id, int hits, int strength = 100) =>
        new() { TransponderId = id, SignalHits = hits, SignalStrength = strength };

    [Fact]
    public void A_transponder_fading_before_the_main_is_called_weak_despite_a_clean_day()
    {
        var watch = new TransponderWatch();
        var now = DateTime.UtcNow;

        // Practice and heats: 120 clean passes, all well over 12 minutes ago.
        for (var i = 0; i < 120; i++)
        {
            watch.Record(Pass("1234", 8), now.AddMinutes(-60).AddSeconds(i));
        }

        // Warm-up before the main: ten passes only just caught.
        for (var i = 0; i < 10; i++)
        {
            watch.Record(Pass("1234", 1), now.AddSeconds(-60 + i));
        }

        var sighting = watch.Of("1234");
        Assert.NotNull(sighting);
        Assert.Equal(10, sighting!.Passes);
        Assert.True(sighting.Weak);
        Assert.Equal(1, sighting.BestHits);
    }

    [Fact]
    public void A_new_battery_clears_a_weak_morning()
    {
        var watch = new TransponderWatch();
        var now = DateTime.UtcNow;

        for (var i = 0; i < 40; i++)
        {
            watch.Record(Pass("77", i < 30 ? 1 : 8), now.AddMinutes(-90).AddSeconds(i));
        }

        for (var i = 0; i < 3; i++)
        {
            watch.Record(Pass("77", 9, 150), now.AddSeconds(-30 + i));
        }

        var sighting = watch.Of("77");
        Assert.NotNull(sighting);
        Assert.False(sighting!.Weak);
        Assert.Equal(3, sighting.Passes);
        Assert.Equal(150, sighting.AverageStrength);
    }

    [Fact]
    public void Passes_without_a_signal_reading_count_as_passes_but_not_readings()
    {
        var watch = new TransponderWatch();
        var now = DateTime.UtcNow;

        watch.Record(Pass("I1", 0, 0), now.AddSeconds(-20));
        watch.Record(Pass("I1", 0, 0), now.AddSeconds(-10));

        var sighting = watch.Of("I1");
        Assert.NotNull(sighting);
        Assert.Equal(2, sighting!.Passes);
        Assert.False(sighting.SignalKnown);
        Assert.False(sighting.Weak);
    }

    [Fact]
    public void Passes_that_age_out_while_the_car_is_quiet_stop_counting()
    {
        var watch = new TransponderWatch();
        var now = DateTime.UtcNow;

        // Weak passes twelve and a half minutes ago, then one clean pass a
        // minute ago after a battery change. When the clean pass came in the
        // weak ones were still inside the window; by now they are not.
        for (var i = 0; i < 10; i++)
        {
            watch.Record(Pass("55", 1), now.AddMinutes(-12.5).AddSeconds(i));
        }

        watch.Record(Pass("55", 8), now.AddMinutes(-1));

        var sighting = watch.Of("55");
        Assert.NotNull(sighting);
        Assert.Equal(1, sighting!.Passes);
        Assert.False(sighting.Weak);

        var listed = Assert.Single(watch.Recent());
        Assert.Equal(1, listed.Passes);
    }
}
