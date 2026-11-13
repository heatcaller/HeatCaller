namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026): the driver card read a shared (mixed-class)
/// heat by the overall order and the heat's main class, so a Mod driver who
/// won Mod behind three Stock cars had no win, "P4 of 7" and "Stock" on a
/// card whose championship line said one win.
/// </summary>
public sealed class BugHunt17_DriverStatsTests
{
    private static readonly DateTime Day = new(2026, 11, 13, 18, 0, 0, DateTimeKind.Utc);

    private static readonly DriverStatsQuery Alex = new()
    {
        Transponder = "M1",
        Transponders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "M1" },
        DriverName = "Alex",
    };

    private static SessionResultEntry Row(string id, string name, int position, string? cls) => new()
    {
        Transponder = id,
        DriverName = name,
        Position = position,
        Laps = 10,
        FastLapMs = 9800 + position,
        ClassName = cls,
    };

    private static DriverStats Compute(SessionRecord session) =>
        DriverStats.Compute(Alex, new DriverStatsSnapshot
        {
            Sessions = new[] { session },
            RoundClasses = new Dictionary<string, string> { ["r1"] = "Stock" },
            Rivalries = Array.Empty<(string A, string B)>(),
            Championships = Array.Empty<(string Season, IReadOnlyList<SeasonStandingEntry> Table)>(),
            TrackRecords = Array.Empty<(string, long, string, DateTime?)>(),
        });

    [Fact]
    public void A_shared_heat_is_read_by_the_driver_s_own_class()
    {
        var stats = Compute(new SessionRecord
        {
            SessionType = RaceSessionType.Race,
            Label = "Q1 Heat 1",
            RoundId = "r1",
            StartedAtUtc = Day,
            EndedAtUtc = Day.AddMinutes(5),
            Results = new List<SessionResultEntry>
            {
                Row("S1", "Sam", 1, "Stock"),
                Row("S2", "Jo", 2, "Stock"),
                Row("S3", "Kim", 3, "Stock"),
                Row("M1", "Alex", 4, "Mod"),
                Row("M2", "Pat", 5, "Mod"),
                Row("M3", "Lee", 6, "Mod"),
                Row("M4", "Ash", 7, "Mod"),
            },
        });

        Assert.Equal(1, stats.Wins);
        Assert.Equal(1, stats.BestFinish);
        Assert.Equal(new[] { 1 }, stats.RecentForm);
        Assert.Equal(new[] { "Mod" }, stats.Classes);

        var line = Assert.Single(stats.RecentActivity, a => a.Kind == ActivityKind.Race);
        Assert.StartsWith("Q1 Heat 1 (Mod)  ·  P1 of 4", line.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void An_ordinary_heat_is_read_overall_with_the_round_s_class_as_before()
    {
        var stats = Compute(new SessionRecord
        {
            SessionType = RaceSessionType.Race,
            Label = "Q1 Heat 1",
            RoundId = "r1",
            StartedAtUtc = Day,
            EndedAtUtc = Day.AddMinutes(5),
            Results = new List<SessionResultEntry>
            {
                Row("S1", "Sam", 1, null),
                Row("S2", "Jo", 2, null),
                Row("S3", "Kim", 3, null),
                Row("M1", "Alex", 4, null),
            },
        });

        Assert.Equal(0, stats.Wins);
        Assert.Equal(4, stats.BestFinish);
        Assert.Equal(new[] { "Stock" }, stats.Classes);

        var line = Assert.Single(stats.RecentActivity, a => a.Kind == ActivityKind.Race);
        Assert.StartsWith("Q1 Heat 1 (Stock)  ·  P4 of 4", line.Text, StringComparison.Ordinal);
    }
}
