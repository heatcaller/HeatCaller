namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), F196: a Pole Shootout run for a class that
/// follows the app default (Format null) used the built-in values - a 3 s
/// min lap and an automatic 15 s + 5 s start - instead of the club's own
/// default, so at a club with a lower min lap no timed lap counted.
/// </summary>
public sealed class BugHunt24_PoleShootoutTests
{
    private static RaceFormat ClubDefault() => new()
    {
        MinLapTimeMs = 1500,
        AutoAdvance = false,
        StagingSeconds = 30,
        CountdownSeconds = 10,
        FinishOnNextCrossing = false,
    };

    [Fact]
    public void A_run_for_a_class_following_the_app_default_takes_the_app_defaults_min_lap_and_start()
    {
        var runs = PoleShootout.BuildRuns("Stock", new[] { "q1", "q2", "q3" }, 2, null, "Club", ClubDefault());

        Assert.Equal(3, runs.Count);
        Assert.All(runs, r =>
        {
            var f = r.FormatOverride!;
            Assert.Equal(1500, f.MinLapTimeMs);
            Assert.False(f.AutoAdvance);
            Assert.Equal(30, f.StagingSeconds);
            Assert.Equal(10, f.CountdownSeconds);
            Assert.False(f.FinishOnNextCrossing);

            // The run's own length and start still apply on top.
            Assert.Equal(RaceStartType.Qualifier, f.StartType);
            Assert.Equal(3, f.LapLimit);
            Assert.Equal(0, f.WarmUpSeconds);
        });
    }

    [Fact]
    public void A_class_with_its_own_format_still_wins_over_the_app_default()
    {
        var own = new RaceFormat { MinLapTimeMs = 5000, StagingSeconds = 20 };

        var format = PoleShootout.RunFormat(own, 1, ClubDefault());

        Assert.Equal(5000, format.MinLapTimeMs);
        Assert.Equal(20, format.StagingSeconds);
        Assert.Equal(2, format.LapLimit);
    }

    [Fact]
    public void The_app_default_handed_in_is_not_changed_by_building_a_run()
    {
        var appDefault = ClubDefault();

        PoleShootout.RunFormat(null, 1, appDefault);

        Assert.Equal(0, appDefault.LapLimit);
        Assert.Equal(RaceStartType.Standard, appDefault.StartType);
    }
}
