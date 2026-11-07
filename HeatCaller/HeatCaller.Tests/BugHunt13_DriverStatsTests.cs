using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 13 (7 Nov 2026), the driver card. A record the setter still
/// held went missing from it when a phantom he ran later became his best
/// lap; "P1 of N" counted cars that never turned a lap; and a result
/// corrected or deleted while the Driver Hub was open left the card on the
/// old numbers.
/// </summary>
public sealed class BugHunt13_DriverStatsTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static readonly DateTime Day = new(2026, 11, 1, 18, 0, 0, DateTimeKind.Utc);

    private static readonly DriverStatsQuery Sam = new()
    {
        Transponder = "T3",
        Transponders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "T3" },
        DriverName = "Sam",
    };

    private static DriverStats Compute(IReadOnlyList<SessionRecord> sessions, params (string Layout, long LapMs, string Holder)[] records) =>
        DriverStats.Compute(Sam, new DriverStatsSnapshot
        {
            Sessions = sessions,
            RoundClasses = new Dictionary<string, string>(),
            Rivalries = Array.Empty<(string A, string B)>(),
            Championships = Array.Empty<(string Season, IReadOnlyList<SeasonStandingEntry> Table)>(),
            TrackRecords = records.Select(r => (r.Layout, r.LapMs, r.Holder, (DateTime?)Day)).ToList(),
        });

    // ------------------------------------------------------------------
    //  The record held, with a refused phantom as the setter's best
    // ------------------------------------------------------------------

    [Fact]
    public void A_record_is_still_the_setters_when_a_later_phantom_of_his_is_his_best_lap()
    {
        // Sam set 9.800 on Main; a retriggering transponder later in the
        // same race gave him a 5.100 that Race Control refused as a record
        // but which stays his best lap.
        var race = new SessionRecord
        {
            SessionType = RaceSessionType.Race,
            Label = "Heat 1",
            TrackLayoutName = "Main",
            StartedAtUtc = Day,
            EndedAtUtc = Day.AddMinutes(5),
            Results = new List<SessionResultEntry>
            {
                new() { Transponder = "T3", DriverName = "Sam", Position = 1, Laps = 4, FastLapMs = 5100, LapTimesMs = { 10_200, 9_800, 5_100, 10_000 } },
                new() { Transponder = "T1", DriverName = "Alex", Position = 2, Laps = 4, FastLapMs = 9900, LapTimesMs = { 10_100, 9_900, 10_000, 10_050 } },
            },
        };

        var stats = Compute(new[] { race }, ("Main", 9800, "Sam"));

        var held = Assert.Single(stats.RecordsHeld);
        Assert.Equal("Main", held.Layout);
        Assert.Equal(9800, held.LapMs);
    }

    [Fact]
    public void A_record_beaten_since_is_still_not_theirs()
    {
        var race = new SessionRecord
        {
            SessionType = RaceSessionType.Race,
            Label = "Heat 1",
            TrackLayoutName = "Main",
            StartedAtUtc = Day,
            EndedAtUtc = Day.AddMinutes(5),
            Results = new List<SessionResultEntry>
            {
                new() { Transponder = "T3", DriverName = "Sam", Position = 1, Laps = 2, FastLapMs = 9800, LapTimesMs = { 10_200, 9_800 } },
            },
        };

        Assert.Empty(Compute(new[] { race }, ("Main", 9500, "Alex")).RecordsHeld);
    }

    // ------------------------------------------------------------------
    //  "P1 of N" counts the cars that raced
    // ------------------------------------------------------------------

    [Fact]
    public void The_field_on_the_activity_line_counts_only_cars_that_turned_a_lap()
    {
        // A hand-released heat filed all six rostered drivers at the green;
        // only Sam and Alex ran.
        var race = new SessionRecord
        {
            SessionType = RaceSessionType.Race,
            Label = "Heat 1",
            TrackLayoutName = "Main",
            StartedAtUtc = Day,
            EndedAtUtc = Day.AddMinutes(5),
            Results = new List<SessionResultEntry>
            {
                new() { Transponder = "T3", DriverName = "Sam", Position = 1, Laps = 12, FastLapMs = 9812 },
                new() { Transponder = "T1", DriverName = "Alex", Position = 2, Laps = 11, FastLapMs = 9950 },
                new() { Transponder = "T4", DriverName = "Bo", Position = 3 },
                new() { Transponder = "T5", DriverName = "Cy", Position = 4 },
                new() { Transponder = "T6", DriverName = "Di", Position = 5 },
                new() { Transponder = "T7", DriverName = "Ed", Position = 6 },
            },
        };

        var line = Assert.Single(Compute(new[] { race }).RecentActivity);

        Assert.StartsWith("Heat 1  ·  P1 of 2", line.Text);
    }

    // ------------------------------------------------------------------
    //  A correction or a deletion starts the card's stats again
    // ------------------------------------------------------------------

    [Fact]
    public void Driver_Hub_starts_its_stats_again_when_a_result_is_corrected_or_a_race_deleted()
    {
        var race = new SessionRecord
        {
            SessionType = RaceSessionType.Race,
            Label = "Heat 1",
            TrackLayoutName = "Main",
            StartedAtUtc = Day,
            EndedAtUtc = Day.AddMinutes(5),
            Results = new List<SessionResultEntry>
            {
                new() { Transponder = "T3", DriverName = "Sam", Position = 1, Laps = 12, FastLapMs = 9812 },
            },
        };

        var history = SessionHistoryStore.FromSessions(new[] { race });
        var versionField = typeof(ProfileManagerControl).GetField("_statsVersion", BindingFlags.Instance | BindingFlags.NonPublic)!;

        var (afterEdit, afterDelete, start) = StaThread.Run(() =>
        {
            using var page = new ProfileManagerControl(
                ProfileStore.Load(),
                RaceClassStore.Load(),
                new UndoService(),
                new DriverStatsSources { History = history });

            _ = page.Handle;
            StaThread.Settle();

            var start = (int)versionField.GetValue(page)!;

            race.Results[0].Outcome = ResultEditor.Dq;
            Assert.True(history.SaveEdited(race));
            StaThread.Settle();
            var afterEdit = (int)versionField.GetValue(page)!;

            Assert.True(history.Delete(race.Id));
            StaThread.Settle();
            var afterDelete = (int)versionField.GetValue(page)!;

            return (afterEdit, afterDelete, start);
        }, TimeSpan.FromSeconds(30), () => "correcting a race under the Driver Hub");

        Assert.True(afterEdit > start, "a corrected result left the card's stats as they were");
        Assert.True(afterDelete > afterEdit, "a deleted race left the card's stats as they were");
    }
}
