namespace HeatCaller.Tests;

/// <summary>
/// A result the race director marked DNF or DQ in Edit Result is not a
/// finish on the driver card, even when the driver still has laps on the
/// board (ResultEditor keeps a DNF's laps and places it after the finishers).
/// </summary>
public sealed class BugHuntH2StatsTests
{
    private static readonly DateTime Day = new(2026, 9, 20, 18, 0, 0, DateTimeKind.Utc);

    private static readonly DriverStatsQuery Sam = new()
    {
        Transponder = "T3",
        Transponders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "T3" },
        DriverName = "Sam",
    };

    private static SessionRecord ThreeCarRace(string samOutcome) => new()
    {
        SessionType = RaceSessionType.Race,
        Label = "A-Main",
        TrackLayoutName = "Drift 1.0",
        StartedAtUtc = Day,
        EndedAtUtc = Day.AddMinutes(5),
        Results = new List<SessionResultEntry>
        {
            new() { Transponder = "T1", DriverName = "Asher", Position = 1, Laps = 14, FastLapMs = 6800 },
            new() { Transponder = "T2", DriverName = "Frank", Position = 2, Laps = 13, FastLapMs = 6900 },
            new() { Transponder = "T3", DriverName = "Sam", Position = 3, Laps = 12, FastLapMs = 7000, Outcome = samOutcome, Status = samOutcome },
        },
    };

    private static DriverStats Compute(SessionRecord race) => DriverStats.Compute(Sam, new DriverStatsSnapshot
    {
        Sessions = new[] { race },
        RoundClasses = new Dictionary<string, string>(),
        Rivalries = Array.Empty<(string A, string B)>(),
        Championships = Array.Empty<(string Season, IReadOnlyList<SeasonStandingEntry> Table)>(),
    });

    [Theory]
    [InlineData("DNF")]
    [InlineData("DQ")]
    public void A_marked_dnf_or_dq_with_laps_is_not_a_finish(string outcome)
    {
        var stats = Compute(ThreeCarRace(outcome));

        Assert.Equal(1, stats.Starts);
        Assert.Equal(0, stats.Finishes);
        Assert.Equal(1, stats.Dnfs);
        Assert.Equal(0, stats.Podiums);
        Assert.Equal(0, stats.TopFives);
        Assert.Null(stats.BestFinish);
        Assert.Equal(new[] { 0 }, stats.RecentForm);
        Assert.Equal("A-Main  ·  did not finish", Assert.Single(stats.RecentActivity).Text);
    }

    [Fact]
    public void An_ordinary_third_place_is_still_a_podium()
    {
        var stats = Compute(ThreeCarRace(string.Empty));

        Assert.Equal(1, stats.Finishes);
        Assert.Equal(1, stats.Podiums);
        Assert.Equal(3, stats.BestFinish);
        Assert.Equal(new[] { 3 }, stats.RecentForm);
    }
}
