namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 10 (23 Oct 2026): "Days at the track" on the driver card counts
/// race nights (6am to 6am, TonightAtTheTrack.NightOf), so one club night
/// whose A-main starts after midnight is still one day.
/// </summary>
public sealed class BugHunt10_DriverStatsTests
{
    private static readonly DriverStatsQuery Sam = new()
    {
        Transponder = "T3",
        Transponders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "T3" },
        DriverName = "Sam",
    };

    private static SessionRecord RaceAtLocal(DateTime local, string label)
    {
        var startUtc = DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime();
        return new SessionRecord
        {
            SessionType = RaceSessionType.Race,
            Label = label,
            TrackLayoutName = "Drift 1.0",
            StartedAtUtc = startUtc,
            EndedAtUtc = startUtc.AddMinutes(5),
            Results = new List<SessionResultEntry>
            {
                new() { Transponder = "T1", DriverName = "Asher", Position = 1, Laps = 14, FastLapMs = 6800 },
                new() { Transponder = "T3", DriverName = "Sam", Position = 2, Laps = 13, FastLapMs = 7000 },
            },
        };
    }

    private static DriverStats Compute(params SessionRecord[] races) => DriverStats.Compute(Sam, new DriverStatsSnapshot
    {
        Sessions = races,
        RoundClasses = new Dictionary<string, string>(),
        Rivalries = Array.Empty<(string A, string B)>(),
        Championships = Array.Empty<(string Season, IReadOnlyList<SeasonStandingEntry> Table)>(),
    });

    [Fact]
    public void A_night_that_runs_past_midnight_is_one_day_at_the_track()
    {
        var stats = Compute(
            RaceAtLocal(new DateTime(2026, 10, 14, 20, 0, 0), "Heat 1"),
            RaceAtLocal(new DateTime(2026, 10, 14, 23, 30, 0), "Heat 2"),
            RaceAtLocal(new DateTime(2026, 10, 15, 0, 20, 0), "A-Main"));

        Assert.Equal(1, stats.DaysOnTrack);
    }

    [Fact]
    public void Two_separate_evenings_are_still_two_days()
    {
        var stats = Compute(
            RaceAtLocal(new DateTime(2026, 10, 14, 20, 0, 0), "Heat 1"),
            RaceAtLocal(new DateTime(2026, 10, 15, 20, 0, 0), "Heat 1"));

        Assert.Equal(2, stats.DaysOnTrack);
    }
}
