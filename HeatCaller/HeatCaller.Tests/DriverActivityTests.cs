namespace HeatCaller.Tests;

/// <summary>
/// A driver's recent activity on the Driver Hub card (27 Sep 2026):
/// their races with place and best lap, their personal bests, and the
/// setups logged for them with what each changed - newest first.
/// </summary>
public sealed class DriverActivityTests
{
    private static readonly DateTime Day = new(2026, 9, 20, 18, 0, 0, DateTimeKind.Utc);

    private static SessionRecord Race(string label, double hours, string? roundId, params (string Transponder, int Position, int Laps, long BestMs)[] rows) => new()
    {
        SessionType = RaceSessionType.Race,
        Label = label,
        RoundId = roundId,
        TrackLayoutName = "Drift 1.0",
        StartedAtUtc = Day.AddHours(hours),
        EndedAtUtc = Day.AddHours(hours).AddMinutes(5),
        Results = rows.Select(r => new SessionResultEntry
        {
            Transponder = r.Transponder,
            DriverName = r.Transponder == "T1" ? "Asher" : "Driver " + r.Transponder,
            Position = r.Position,
            Laps = r.Laps,
            FastLapMs = r.BestMs,
        }).ToList(),
    };

    private static readonly DriverStatsQuery Asher = new()
    {
        Transponder = "T1",
        Transponders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "T1" },
        DriverName = "Asher",
    };

    private static DriverStatsSnapshot Snapshot(IReadOnlyList<SessionRecord> newestFirst, params SetupEntry[] setups) => new()
    {
        Sessions = newestFirst,
        RoundClasses = new Dictionary<string, string> { ["r1"] = "Stock Buggy" },
        Rivalries = Array.Empty<(string A, string B)>(),
        Championships = Array.Empty<(string Season, IReadOnlyList<SeasonStandingEntry> Table)>(),
        Setups = setups.OrderByDescending(s => s.RecordedAtUtc).ToList(),
        SetupSheets = new Dictionary<string, ChassisTemplate> { [TestSheet.Sheet.Id] = TestSheet.Sheet },
    };

    private static string Lap(long ms) => (ms / 1000.0).ToString("0.000");

    [Fact]
    public void Races_setups_and_personal_bests_come_newest_first()
    {
        // A ghost in the heat is not counted in the field.
        var heat = Race("Q1 Heat 1", 0, null, ("T1", 2, 10, 6935), ("T2", 1, 10, 6900), ("GHOST-1", 3, 9, 7100), ("T3", 4, 9, 7200));
        var main = Race("A-Main", 2, "r1", ("T1", 1, 12, 6800), ("T2", 2, 12, 6850));

        var firstSetup = TestSheet.Setup(Day.AddHours(1), ("pinion", "22"));
        var secondSetup = TestSheet.Setup(Day.AddHours(1.5), ("pinion", "23"));

        var stats = DriverStats.Compute(Asher, Snapshot(new[] { main, heat }, firstSetup, secondSetup));
        stats.ApplyPersonalBests(null);

        Assert.Equal(
            new[]
            {
                $"Personal best  ·  Drift 1.0  ·  {Lap(6800)}s",
                $"A-Main (Stock Buggy)  ·  P1 of 2  ·  best {Lap(6800)}s",
                "Setup on Kyosho MR-03  ·  Pinion 22T → 23T",
                "Setup on Kyosho MR-03  ·  the first one logged",
                $"Q1 Heat 1  ·  P2 of 3  ·  best {Lap(6935)}s",
            },
            stats.RecentActivity.Select(a => a.Text));

        Assert.Equal(
            new[] { ActivityKind.PersonalBest, ActivityKind.Race, ActivityKind.Setup, ActivityKind.Setup, ActivityKind.Race },
            stats.RecentActivity.Select(a => a.Kind));
    }

    [Fact]
    public void A_race_with_no_laps_did_not_finish()
    {
        var stats = DriverStats.Compute(Asher, Snapshot(new[] { Race("Heat 2", 0, null, ("T1", 2, 0, 0), ("T2", 1, 8, 7000)) }));

        Assert.Equal("Heat 2  ·  did not finish", Assert.Single(stats.RecentActivity).Text);
    }

    [Fact]
    public void Only_the_latest_lines_are_kept_and_other_drivers_setups_are_not_theirs()
    {
        var races = Enumerable.Range(0, 12)
            .Select(i => Race($"Heat {i}", i, null, ("T1", 1, 5, 7000)))
            .Reverse()
            .ToList();

        var somebodyElses = TestSheet.Setup(Day.AddHours(20), ("pinion", "30"));
        somebodyElses.DriverName = "Frank";

        var stats = DriverStats.Compute(Asher, Snapshot(races, somebodyElses));

        Assert.Equal(DriverStats.ActivityLength, stats.RecentActivity.Count);
        Assert.StartsWith("Heat 11", stats.RecentActivity[0].Text);
        Assert.DoesNotContain(stats.RecentActivity, a => a.Kind == ActivityKind.Setup);
    }

    [Fact]
    public void A_demo_race_is_not_activity()
    {
        var demo = Race("[DEMO] A-Main", 0, null, ("T1", 1, 10, 6000));
        demo.IsDemo = true;

        var stats = DriverStats.Compute(Asher, Snapshot(new[] { demo }));
        stats.ApplyPersonalBests(null);

        Assert.Empty(stats.RecentActivity);
    }
}
