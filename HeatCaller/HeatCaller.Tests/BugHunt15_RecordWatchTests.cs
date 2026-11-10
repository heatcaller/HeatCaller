namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 15 (B23): the record watch called record danger on laps that
/// could never become the record - a rental, a guest excluded from records,
/// a ghost, and anyone in a demo or practice run. It now asks the same two
/// questions PaceWatch does, wired here the way the announcer wires them.
/// </summary>
public sealed class BugHunt15_RecordWatchTests : IDisposable
{
    // Declared first: every store below loads from wherever it points.
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private const string Ghost = "GHOST-AAAAAA";

    private static long Ms(double seconds) => (long)Math.Round(seconds * 1000);

    private static RaceTiming Race() => new(new RaceControlSession());

    private ProfileStore Profiles()
    {
        var profiles = ProfileStore.Load();

        profiles.Upsert(new DriverProfile { Transponder = "SAM", DriverName = "Sam" });
        profiles.Upsert(new DriverProfile { Transponder = "RENT", DriverName = "Rental One", IsRental = true });
        profiles.Upsert(new DriverProfile { Transponder = "GUEST", DriverName = "Guest", ExcludeFromRecords = true });

        return profiles;
    }

    private static RecordWatch Watch(RaceTiming race, ProfileStore profiles)
    {
        var watch = new RecordWatch
        {
            LayoutRecord = () => (9.800, "Dave"),
            CanHoldRecord = transponder => AchievementEvaluator.CanHoldTrackRecord(transponder, profiles),
            RecordWouldStand = (transponder, lap, record) => RecordPlausibility.WouldTake(race, transponder, Ms(lap), Ms(record)),
        };

        watch.Reset();
        return watch;
    }

    /// <summary>Two warm-up laps, then two within a quarter of a percent: "one more like that".</summary>
    private static List<RecordThreat> RunNearTheRecord(RecordWatch watch, string driver)
    {
        var said = new List<RecordThreat>();

        foreach (var lap in new[] { 10.6, 10.5, 9.810, 9.815, 9.812 })
        {
            if (watch.Saw(new LapSeen(driver, lap)) is { } threat)
            {
                said.Add(threat);
            }
        }

        return said;
    }

    [Fact]
    public void A_driver_who_can_hold_the_record_is_still_told_it_is_in_danger()
    {
        var said = RunNearTheRecord(Watch(Race(), Profiles()), "SAM");

        Assert.Contains(said, t => t.Level == RecordThreatLevel.OneMoreLikeThat);
    }

    [Theory]
    [InlineData("RENT")]
    [InlineData("GUEST")]
    [InlineData(Ghost)]
    public void A_driver_who_can_never_hold_the_record_is_never_told_it_is_in_danger(string driver)
    {
        Assert.Empty(RunNearTheRecord(Watch(Race(), Profiles()), driver));
    }

    [Fact]
    public void A_demo_run_never_calls_record_danger()
    {
        var race = Race();
        race.IsDemoMode = true;

        Assert.Empty(RunNearTheRecord(Watch(race, Profiles()), "SAM"));
    }

    [Fact]
    public void A_practice_run_never_calls_record_danger()
    {
        var race = Race();
        race.IsPracticeMode = true;

        Assert.Empty(RunNearTheRecord(Watch(race, Profiles()), "SAM"));
    }
}
