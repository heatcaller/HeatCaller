namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), qualifying:
///
///  - best consecutive laps leaves out the dash after a credited start lap,
///    in a saved heat and in the running one, so a car read just before the
///    green is not put on TQ off a run nobody drove;
///  - a driver whose only heat the marshal rule took away is not given that
///    lost heat's start time as where their figures came from.
/// </summary>
public sealed class BugHunt24_QualifyingCalculatorTests : IDisposable
{
    // Declared first: the stores below load from wherever it points.
    private readonly TempAppData _data = new();

    private readonly RoundDefinitionStore _rounds = RoundDefinitionStore.Load();
    private readonly ProfileStore _profiles = ProfileStore.Load();
    private readonly List<SessionRecord> _sessions = new();

    private const string Stock = "Stock";

    private static readonly DateTime T0 = new(2026, 11, 23, 20, 0, 0, DateTimeKind.Utc);

    // A credited start: the zero, the rest of the start, then real laps.
    private static readonly long[] CreditedLaps = { 0, 5_500, 12_100, 11_900, 11_850 };

    // An ordinary start: the run to the line, then real laps.
    private static readonly long[] OrdinaryLaps = { 1_400, 11_900, 11_900, 11_900, 11_900 };

    public void Dispose() => _data.Dispose();

    private static RaceClass BestThree() => new()
    {
        Name = Stock,
        QualifyingMethod = QualifyingMethod.BestConsecutiveLaps,
        ConsecutiveLapCount = 3,
        CountBestRounds = 1,
    };

    // ------------------------------------------------ the credited start lap

    [Fact]
    public void The_window_skips_both_start_entries_of_a_credited_start()
    {
        // 12.1 + 11.9 + 11.85, from the third entry - not 5.5 + 12.1 + 11.9.
        Assert.Equal((35_850L, 3), QualifyingCalculator.BestFlyingWindow(CreditedLaps, 3, 2));

        // One flying lap wanted: the dash is not it.
        Assert.Equal((11_850L, 5), QualifyingCalculator.BestFlyingWindow(CreditedLaps, 1, 2));

        // Nothing but the start: no window.
        Assert.Equal((0L, 0), QualifyingCalculator.BestFlyingWindow(new long[] { 0, 5_500 }, 1, 2));

        // An ordinary start scores as it always has, either way it is asked.
        Assert.Equal((35_700L, 2), QualifyingCalculator.BestFlyingWindow(OrdinaryLaps, 3));
        Assert.Equal((35_700L, 2), QualifyingCalculator.BestFlyingWindow(OrdinaryLaps, 3, 1));
    }

    [Fact]
    public void A_saved_heat_does_not_count_the_dash_after_a_credited_start_lap()
    {
        RunHeat("Q1 Heat 1", T0,
            Laps("A", CreditedLaps, credited: true),
            Laps("B", OrdinaryLaps, credited: false));

        var entries = Standings(BestThree());
        var a = entries.Single(e => e.Transponder == "A");

        // The old code scored A 29.5s and put them on top.
        Assert.Equal(35_850L, a.BestConsecutiveMs);
        Assert.Equal(3, a.ConsecutiveStartLap);
        Assert.Equal(35_700L, entries.Single(e => e.Transponder == "B").BestConsecutiveMs);
        Assert.Equal("B", entries[0].Transponder);
    }

    [Fact]
    public void A_zero_first_entry_without_the_credit_is_still_one_start_entry()
    {
        // A qualifying start saves a zero too, and the lap after it is real.
        RunHeat("Q1 Heat 1", T0, Laps("A", new long[] { 0, 11_000, 12_000, 12_000 }, credited: false));

        var a = Standings(BestThree()).Single();

        Assert.Equal(35_000L, a.BestConsecutiveMs);
        Assert.Equal(2, a.ConsecutiveStartLap);
    }

    [Fact]
    public void The_running_heat_does_not_count_the_dash_either()
    {
        var round = new RoundDefinition { ClassName = Stock, RoundLabel = "Q1 Heat 1", Kind = RoundKind.Qualifier };
        _rounds.Add(round);

        var live = new QualifyingCalculator.LiveHeat(round.Id, round.RoundLabel, T0, new[]
        {
            new QualifyingCalculator.LiveResult("A", "A", string.Empty, 5, 41_350, 11_850, CreditedLaps, StartLapCredited: true),
            new QualifyingCalculator.LiveResult("B", "B", string.Empty, 5, 49_000, 11_900, OrdinaryLaps),
        });

        var entries = QualifyingCalculator.Compute(Stock, _rounds, SessionHistoryStore.FromSessions(_sessions), _profiles,
            BestThree(), null, live);

        Assert.Equal(35_850L, entries.Single(e => e.Transponder == "A").BestConsecutiveMs);
        Assert.Equal("B", entries[0].Transponder);
    }

    // ------------------------------------------------ the marshal rule's lost result

    [Fact]
    public void A_driver_whose_only_heat_was_lost_is_not_pointed_at_that_heats_time()
    {
        var q1 = RunHeat("Q1 Heat 1", T0, Counted("A", 14), Counted("B", 13));
        LogNoShow(q1, "A");

        var a = Standings(new RaceClass { Name = Stock, CountBestRounds = 1 }).Single(e => e.Transponder == "A");

        Assert.StartsWith("Lost Q1 Heat 1 (14 laps)", a.Forfeit);
        Assert.Equal(0, a.BestLaps);
        Assert.Equal("result lost", a.SourceRoundLabel);
        Assert.Null(a.SourceSessionId);

        // The old code gave the lost heat's own start time here.
        Assert.Null(a.SourceSessionStartedUtc);
    }

    [Fact]
    public void With_another_heat_run_the_next_best_still_says_where_it_came_from()
    {
        var q1 = RunHeat("Q1 Heat 1", T0, Counted("A", 14), Counted("B", 13));
        RunHeat("Q2 Heat 1", T0.AddMinutes(10), Counted("A", 12), Counted("B", 13));
        LogNoShow(q1, "A");

        var a = Standings(new RaceClass { Name = Stock, CountBestRounds = 1 }).Single(e => e.Transponder == "A");

        Assert.Equal(12, a.BestLaps);
        Assert.Equal("Q2 Heat 1", a.SourceRoundLabel);
        Assert.Equal<DateTime?>(T0.AddMinutes(10), a.SourceSessionStartedUtc);
    }

    // ------------------------------------------------ helpers

    private static SessionResultEntry Laps(string transponder, long[] laps, bool credited) => new()
    {
        Transponder = transponder,
        DriverName = transponder,
        Laps = laps.Length,
        ElapsedMs = laps.Sum(),
        FastLapMs = laps.Skip(credited ? 2 : 1).Min(),
        LapTimesMs = laps.ToList(),
        StartLapCredited = credited,
    };

    private static SessionResultEntry Counted(string transponder, int laps) => new()
    {
        Transponder = transponder,
        DriverName = transponder,
        Laps = laps,
        ElapsedMs = 300_000,
    };

    private RoundDefinition RunHeat(string label, DateTime started, params SessionResultEntry[] results)
    {
        var round = new RoundDefinition { ClassName = Stock, RoundLabel = label, Kind = RoundKind.Qualifier };
        _rounds.Add(round);

        var session = new SessionRecord
        {
            Label = label,
            SessionType = RaceSessionType.Race,
            RoundId = round.Id,
            StartedAtUtc = started,
            EndedAtUtc = started.AddMinutes(5),
            Results = results.ToList(),
        };

        _sessions.Add(session);
        Assert.True(_rounds.MarkRecorded(round.Id, session.Id));
        return round;
    }

    private void LogNoShow(RoundDefinition round, string transponder)
    {
        round.NoShowMarshals = new List<MarshalNoShow>
        {
            MarshalRule.Log($"{transponder} (Turn 1)", transponder, Stock, MarshalNoShowRule.LosesBestQualifier, 10, T0),
        };
        round.MarshalNoShows = new List<string> { $"{transponder} (Turn 1)" };
        _rounds.Update(round);
    }

    private List<QualifyingCalculator.Entry> Standings(RaceClass raceClass) =>
        QualifyingCalculator.Compute(Stock, _rounds, SessionHistoryStore.FromSessions(_sessions), _profiles, raceClass);
}
