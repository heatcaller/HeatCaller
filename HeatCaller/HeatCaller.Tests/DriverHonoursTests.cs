namespace HeatCaller.Tests;

/// <summary>
/// The lap records a driver holds now, as the Profile tab shows them
/// (DriverStats.RecordsHeld): matched by the record lap among their own
/// results on that layout, with the name announced deciding a tie.
/// </summary>
public sealed class DriverHonoursTests
{
    private static readonly DateTime Day = new(2026, 9, 20, 18, 0, 0, DateTimeKind.Utc);

    private static SessionRecord Race(string layout, double hours, params (string Transponder, long BestMs)[] rows) => new()
    {
        SessionType = RaceSessionType.Race,
        Label = "Heat",
        TrackLayoutName = layout,
        StartedAtUtc = Day.AddHours(hours),
        EndedAtUtc = Day.AddHours(hours).AddMinutes(5),
        Results = rows.Select((r, i) => new SessionResultEntry
        {
            Transponder = r.Transponder,
            DriverName = r.Transponder == "T1" ? "Asher" : "Driver " + r.Transponder,
            Position = i + 1,
            Laps = 10,
            FastLapMs = r.BestMs,
        }).ToList(),
    };

    private static readonly DriverStatsQuery Asher = new()
    {
        Transponder = "T1",
        Transponders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "T1" },
        DriverName = "Asher",
    };

    private static DriverStats StatsFor(IReadOnlyList<SessionRecord> sessions, params (string Layout, long LapMs, string Holder)[] records) =>
        DriverStats.Compute(Asher, new DriverStatsSnapshot
        {
            Sessions = sessions,
            RoundClasses = new Dictionary<string, string>(),
            Rivalries = Array.Empty<(string A, string B)>(),
            Championships = Array.Empty<(string Season, IReadOnlyList<SeasonStandingEntry> Table)>(),
            TrackRecords = records.Select(r => (r.Layout, r.LapMs, r.Holder, (DateTime?)Day)).ToList(),
        });

    [Fact]
    public void A_record_set_on_their_lap_is_theirs_and_one_beaten_since_is_not()
    {
        var sessions = new[]
        {
            Race("Drift", 2, ("T2", 6700), ("T1", 6800)),
            Race("Drift", 1, ("T1", 6750)),
            Race("Oval", 0, ("T1", 5100), ("T2", 5200)),
        };

        // Oval's record is Asher's lap; Drift's was, until T2 beat it.
        var stats = StatsFor(sessions, ("Drift", 6700, "Driver T2"), ("Oval", 5100, "Asher"));

        var held = Assert.Single(stats.RecordsHeld);
        Assert.Equal("Oval", held.Layout);
        Assert.Equal(5100, held.LapMs);
    }

    [Fact]
    public void A_tie_goes_to_the_name_announced_and_a_rename_keeps_a_record_nobody_else_could_hold()
    {
        var tie = new[] { Race("Drift", 0, ("T2", 6700), ("T1", 6700)) };

        Assert.Empty(StatsFor(tie, ("Drift", 6700, "Driver T2")).RecordsHeld);
        Assert.Single(StatsFor(tie, ("Drift", 6700, "Asher")).RecordsHeld);

        // Announced under a name Asher no longer has, but nobody else ran it.
        var alone = new[] { Race("Drift", 0, ("T1", 6700), ("T2", 6900)) };
        Assert.Single(StatsFor(alone, ("Drift", 6700, "Ash")).RecordsHeld);
    }

    [Fact]
    public void A_demo_race_never_makes_anyone_a_record_holder()
    {
        var demo = Race("Drift", 0, ("T1", 6000));
        demo.IsDemo = true;

        Assert.Empty(StatsFor(new[] { demo }, ("Drift", 6000, "Asher")).RecordsHeld);
    }
}
