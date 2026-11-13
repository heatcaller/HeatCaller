namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 17 (13 Nov 2026), points qualifying: a night built with one
/// round ("Heat 1", "Heat 2") and then given more ("Q2 Heat 1") still scores
/// the unnumbered heats together as round 1, as the build counts them - not
/// as a round each from the moment the first Q2 heat is saved.
/// </summary>
public sealed class BugHunt17_QualifyingCalculatorTests : IDisposable
{
    // Declared first: the stores below load from wherever it points.
    private readonly TempAppData _data = new();

    private readonly RoundDefinitionStore _rounds = RoundDefinitionStore.Load();
    private readonly ProfileStore _profiles = ProfileStore.Load();
    private readonly List<SessionRecord> _sessions = new();
    private DateTime _clock = new(2026, 11, 13, 18, 0, 0, DateTimeKind.Utc);

    private const string Stock = "Stock";

    public void Dispose() => _data.Dispose();

    private static RaceClass Points(int countBest) => new()
    {
        Name = Stock,
        QualifyingMethod = QualifyingMethod.RoundPoints,
        CountBestRounds = countBest,
    };

    [Fact]
    public void Unnumbered_first_round_heats_stay_one_round_once_a_numbered_round_is_recorded()
    {
        // Round 1, built when the night had one round: C (heat 2) is the
        // quickest of all four, D the slowest.
        RunHeat("Heat 1", R("A", 14, 300_000), R("B", 13, 300_000));
        RunHeat("Heat 2", R("C", 15, 300_000), R("D", 12, 300_000));

        // The round count was raised and Build added Q2.
        RunHeat("Q2 Heat 1", R("A", 14, 300_000), R("B", 13, 300_000), R("C", 12, 300_000), R("D", 11, 300_000));

        var entries = Compute(Points(countBest: 2));

        // Two rounds, not three, and nobody has a missed round.
        Assert.All(entries, e => Assert.Equal(2, e.PointsRoundsRun));
        Assert.All(entries, e => Assert.DoesNotContain("DNS", e.PointsDetail, StringComparison.Ordinal));

        // Round 1 across both heats: C, A, B, D.
        Assert.Equal("Q1 1st (1 pt), Q2 3rd (3 pts)", entries.Single(e => e.Transponder == "C").PointsDetail);
        Assert.Equal("Q1 2nd (2 pts), Q2 1st (1 pt)", entries.Single(e => e.Transponder == "A").PointsDetail);
        Assert.Equal("Q1 4th (4 pts), Q2 4th (4 pts)", entries.Single(e => e.Transponder == "D").PointsDetail);
    }

    [Fact]
    public void They_are_one_round_while_the_first_numbered_heat_is_still_running_too()
    {
        RunHeat("Heat 1", R("A", 14, 300_000));
        RunHeat("Heat 2", R("B", 15, 300_000));

        var q2 = new RoundDefinition { ClassName = Stock, RoundLabel = "Q2 Heat 1", Kind = RoundKind.Qualifier };
        _rounds.Add(q2);

        var live = new QualifyingCalculator.LiveHeat(q2.Id, q2.RoundLabel, _clock.AddHours(1), new[]
        {
            new QualifyingCalculator.LiveResult("A", "A", string.Empty, 10, 300_000, 0, Array.Empty<long>()),
            new QualifyingCalculator.LiveResult("B", "B", string.Empty, 9, 300_000, 0, Array.Empty<long>()),
        });

        var entries = Compute(Points(countBest: 2), live);

        Assert.Equal("Q1 2nd (2 pts), Q2 1st (1 pt)", entries.Single(e => e.Transponder == "A").PointsDetail);
        Assert.Equal("Q1 1st (1 pt), Q2 2nd (2 pts)", entries.Single(e => e.Transponder == "B").PointsDetail);
    }

    [Fact]
    public void A_hand_added_heat_beside_a_numbered_first_round_is_still_a_round_of_its_own()
    {
        // Q1 Heat 2 is on the programme but not run yet: the class HAS a
        // numbered first round, so the extra heat is not it.
        RunHeat("Q2 Heat 1", R("A", 14, 300_000), R("B", 13, 300_000));
        _rounds.Add(new RoundDefinition { ClassName = Stock, RoundLabel = "Q1 Heat 1", Kind = RoundKind.Qualifier });
        RunHeat("Extra heat", R("B", 20, 300_000));

        var b = Compute(Points(countBest: 2)).Single(e => e.Transponder == "B");

        Assert.Equal("Q2 2nd (2 pts), Extra heat 1st (1 pt)", b.PointsDetail);
    }

    private static SessionResultEntry R(string transponder, int laps, long elapsedMs) => new()
    {
        Transponder = transponder,
        DriverName = transponder,
        Laps = laps,
        ElapsedMs = elapsedMs,
        FastLapMs = laps > 0 ? elapsedMs / laps : 0,
    };

    private RoundDefinition RunHeat(string label, params SessionResultEntry[] results)
    {
        var round = new RoundDefinition { ClassName = Stock, RoundLabel = label, Kind = RoundKind.Qualifier };
        _rounds.Add(round);

        _clock = _clock.AddMinutes(10);

        var session = new SessionRecord
        {
            Label = label,
            SessionType = RaceSessionType.Race,
            RoundId = round.Id,
            StartedAtUtc = _clock,
            Results = results.ToList(),
        };

        _sessions.Add(session);
        Assert.True(_rounds.MarkRecorded(round.Id, session.Id));
        return round;
    }

    private List<QualifyingCalculator.Entry> Compute(RaceClass raceClass, QualifyingCalculator.LiveHeat? live = null) =>
        QualifyingCalculator.Compute(Stock, _rounds, SessionHistoryStore.FromSessions(_sessions), _profiles, raceClass, null, live);
}
