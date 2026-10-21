namespace HeatCaller.Tests;

/// <summary>
/// 20 Oct 2026: a penalty race control gave during a SAVED heat (kept as
/// SessionResultEntry.RacePenaltyMs) counts on its best-consecutive-laps
/// window, so a penalized driver no longer takes pole and seeds the mains.
/// </summary>
public sealed class QualifyingCalculatorTests : IDisposable
{
    // Declared first: the stores below load from wherever it points.
    private readonly TempAppData _data = new();

    private readonly RoundDefinitionStore _rounds = RoundDefinitionStore.Load();
    private readonly ProfileStore _profiles = ProfileStore.Load();
    private readonly List<SessionRecord> _sessions = new();

    private const string Stock = "Stock";

    public void Dispose() => _data.Dispose();

    private static readonly RaceClass BestThree = new()
    {
        Name = Stock,
        QualifyingMethod = QualifyingMethod.BestConsecutiveLaps,
        ConsecutiveLapCount = 3,
        CountBestRounds = 1,
    };

    [Fact]
    public void A_race_penalty_on_a_saved_heat_is_added_to_the_window()
    {
        // A's window is 35.2s but carries a 15s penalty; B's is a clean 35.4s.
        var a = Result("A", 4, 51_600, 1_400, 11_700, 11_700, 11_800);
        a.RacePenaltyMs = 15_000;
        RunHeat("Q1", a, Result("B", 4, 36_800, 1_400, 11_800, 11_800, 11_800));

        var entries = Compute();

        Assert.Equal(new[] { "B", "A" }, entries.Select(e => e.Transponder).ToArray());
        Assert.Equal(35_200 + 15_000, entries.Single(e => e.Transponder == "A").BestConsecutiveMs);
    }

    [Fact]
    public void A_race_penalty_does_not_invent_a_window_and_a_negative_one_is_ignored()
    {
        var noWindow = Result("A", 1, 20_000, 1_400, 11_700);
        noWindow.RacePenaltyMs = 5_000;
        var negative = Result("B", 4, 36_600, 1_400, 11_700, 11_700, 11_800);
        negative.RacePenaltyMs = -5_000;
        RunHeat("Q1", noWindow, negative);

        var entries = Compute();

        Assert.Equal(0, entries.Single(e => e.Transponder == "A").BestConsecutiveMs);
        Assert.Equal(35_200, entries.Single(e => e.Transponder == "B").BestConsecutiveMs);
    }

    private static SessionResultEntry Result(string transponder, int laps, long elapsedMs, params long[] lapTimes) => new()
    {
        Transponder = transponder,
        DriverName = transponder,
        Laps = laps,
        ElapsedMs = elapsedMs,
        LapTimesMs = lapTimes.ToList(),
        FastLapMs = lapTimes.Skip(1).DefaultIfEmpty(0).Min(),
    };

    private void RunHeat(string label, params SessionResultEntry[] results)
    {
        var round = new RoundDefinition { ClassName = Stock, RoundLabel = label, Kind = RoundKind.Qualifier };
        _rounds.Add(round);

        var session = new SessionRecord
        {
            Label = label,
            SessionType = RaceSessionType.Race,
            RoundId = round.Id,
            StartedAtUtc = DateTime.UtcNow,
            Results = results.ToList(),
        };

        _sessions.Add(session);
        Assert.True(_rounds.MarkRecorded(round.Id, session.Id));
    }

    private List<QualifyingCalculator.Entry> Compute() =>
        QualifyingCalculator.Compute(Stock, _rounds, SessionHistoryStore.FromSessions(_sessions), _profiles, BestThree, null, null);
}
