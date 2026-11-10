namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 15 (9 Nov 2026), B38: the driver card's top rival counted the
/// 0-lap rows a hand-released grid saves for cars that never moved, so its
/// ahead/behind record disagreed with the Head to Head page (A30-3).
/// </summary>
public sealed class BugHunt15_DriverStatsTests
{
    private static readonly DateTime Day = new(2026, 11, 1, 18, 0, 0, DateTimeKind.Utc);

    private static readonly DriverStatsQuery Sam = new()
    {
        Transponder = "T3",
        Transponders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "T3" },
        DriverName = "Sam",
    };

    private static SessionRecord Heat(int n, int samPos, int samLaps, int alexPos, int alexLaps) => new()
    {
        SessionType = RaceSessionType.Race,
        Label = $"Heat {n}",
        TrackLayoutName = "Main",
        StartedAtUtc = Day.AddMinutes(10 * n),
        EndedAtUtc = Day.AddMinutes(10 * n + 5),
        Results = new List<SessionResultEntry>
        {
            new() { Transponder = "T3", DriverName = "Sam", Position = samPos, Laps = samLaps, FastLapMs = samLaps > 0 ? 9800 : 0 },
            new() { Transponder = "T1", DriverName = "Alex", Position = alexPos, Laps = alexLaps, FastLapMs = alexLaps > 0 ? 9900 : 0 },
        },
    };

    private static DriverStats Compute(IReadOnlyList<SessionRecord> sessions) =>
        DriverStats.Compute(Sam, new DriverStatsSnapshot
        {
            Sessions = sessions,
            RoundClasses = new Dictionary<string, string>(),
            Rivalries = new[] { (A: "Sam", B: "Alex") },
            Championships = Array.Empty<(string Season, IReadOnlyList<SeasonStandingEntry> Table)>(),
            TrackRecords = Array.Empty<(string, long, string, DateTime?)>(),
        });

    [Fact]
    public void The_rival_record_ignores_cars_that_never_turned_a_lap()
    {
        var sessions = new List<SessionRecord>
        {
            // Alex never left the grid in three heats.
            Heat(1, 1, 10, 2, 0),
            Heat(2, 1, 10, 2, 0),
            Heat(3, 1, 10, 2, 0),
            // Sam never left the grid once; both sat on the line once.
            Heat(4, 2, 0, 1, 10),
            Heat(5, 1, 0, 2, 0),
            // They both raced four times; Alex won three.
            Heat(6, 2, 10, 1, 10),
            Heat(7, 2, 10, 1, 10),
            Heat(8, 2, 10, 1, 10),
            Heat(9, 1, 10, 2, 10),
        };

        var rival = Compute(sessions).Rival;

        Assert.NotNull(rival);
        Assert.Equal("Alex", rival!.Name);
        Assert.Equal(1, rival.Ahead);
        Assert.Equal(3, rival.Behind);
    }
}
