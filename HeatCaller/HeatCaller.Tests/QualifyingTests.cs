namespace HeatCaller.Tests;

/// <summary>
/// QualifyingCalculator.Compute - the ranking that seeds the mains, so a
/// wrong answer puts the wrong people in the A-main. Each test builds a
/// small class night: rounds in a real RoundDefinitionStore (in the test
/// folder), their results as saved sessions.
/// </summary>
public sealed class QualifyingTests : IDisposable
{
    // Declared first: the stores below load from wherever it points.
    private readonly TempAppData _data = new();

    private readonly RoundDefinitionStore _rounds = RoundDefinitionStore.Load();
    private readonly ProfileStore _profiles = ProfileStore.Load();
    private readonly List<SessionRecord> _sessions = new();

    private const string Stock = "Stock";

    public void Dispose() => _data.Dispose();

    // --- Most laps, then least time ------------------------------------------

    [Fact]
    public void Most_laps_ranks_first_then_the_shorter_time()
    {
        RunHeat("Q1",
            Result("A", 10, 300_000),
            Result("B", 10, 299_000),
            Result("C", 11, 310_000));

        Assert.Equal(new[] { "C", "B", "A" }, Order(Compute()));
    }

    [Fact]
    public void Best_two_of_three_drops_the_worst_heat_and_says_which()
    {
        RunHeat("Q1", Result("A", 10, 300_000), Result("B", 12, 300_000), Result("C", 13, 300_000));
        RunHeat("Q2", Result("A", 12, 300_000), Result("B", 12, 300_000));
        RunHeat("Q3", Result("A", 11, 300_000));

        var entries = Compute(TotalLaps(countBest: 2));

        // B: 12 + 12. A: 12 + 11, Q1 dropped. C ran one heat: 13.
        Assert.Equal(new[] { "B", "A", "C" }, Order(entries));

        var a = entries.Single(e => e.Transponder == "A");
        Assert.Equal(23, a.BestLaps);
        Assert.Equal(3, a.HeatsRun);
        Assert.Equal(2, a.HeatsCounted);
        Assert.Contains("Q1", a.DroppedSummary);
    }

    // --- Best consecutive laps -----------------------------------------------

    [Fact]
    public void Fewer_laps_than_the_window_sorts_to_the_bottom_not_the_top()
    {
        RunHeat("Q1",
            Result("A", 4, 37_400, 1_400, 12_000, 12_000, 12_000),
            Result("B", 3, 23_400, 1_400, 11_000, 11_000), // quick, but only two flying laps
            Result("C", 4, 38_900, 1_400, 12_500, 12_500, 12_500));

        var entries = Compute(Consecutive(laps: 3));

        Assert.Equal(new[] { "A", "C", "B" }, Order(entries));
        Assert.Equal(0, entries.Single(e => e.Transponder == "B").BestConsecutiveMs);
    }

    [Fact]
    public void The_dash_from_the_green_never_counts_as_a_lap()
    {
        RunHeat("Q1",
            Result("A", 4, 37_000, 1_000, 12_000, 12_000, 12_000),
            Result("B", 4, 40_700, 5_000, 11_900, 11_900, 11_900));

        // B's three real laps (35.7s) beat A's (36.0s), whatever the dash.
        Assert.Equal(new[] { "B", "A" }, Order(Compute(Consecutive(laps: 3))));
    }

    [Fact]
    public void Running_both_heats_ranks_above_running_one_even_on_a_bigger_total()
    {
        RunHeat("Q1",
            Result("A", 4, 37_400, 1_400, 12_000, 12_000, 12_000),
            Result("B", 4, 36_400, 1_400, 11_700, 11_700, 11_600));
        RunHeat("Q2",
            Result("A", 4, 37_400, 1_400, 12_000, 12_000, 12_000));

        var entries = Compute(Consecutive(laps: 3, countBest: 2));

        // A totals two runs (72.0s), B one (35.0s). Missing a heat must not
        // be the quickest way to the top.
        Assert.Equal(new[] { "A", "B" }, Order(entries));
        Assert.Equal(2, entries.Single(e => e.Transponder == "A").WindowsCounted);
        Assert.Equal(1, entries.Single(e => e.Transponder == "B").WindowsCounted);
    }

    // --- What does not count --------------------------------------------------

    [Fact]
    public void A_heat_the_driver_was_disqualified_from_does_not_count()
    {
        var dq = Result("A", 4, 31_400, 1_400, 10_000, 10_000, 10_000);
        dq.Outcome = ResultEditor.Dq;
        dq.Laps = 0; // what the result editor does to a DQ

        RunHeat("Q1", dq, Result("B", 4, 37_400, 1_400, 12_000, 12_000, 12_000));
        RunHeat("Q2", Result("A", 4, 40_400, 1_400, 13_000, 13_000, 13_000));

        var entries = Compute(Consecutive(laps: 3));

        // Counting the DQ'd heat would have put A on top with 30.0s.
        Assert.Equal(new[] { "B", "A" }, Order(entries));
        Assert.Equal(39_000, entries.Single(e => e.Transponder == "A").BestConsecutiveMs);
    }

    [Fact]
    public void A_ghost_never_takes_a_qualifying_place()
    {
        RunHeat("Q1",
            Result("GHOST-track-record", 12, 290_000),
            Result("A", 10, 300_000));

        Assert.Equal(new[] { "A" }, Order(Compute()));
    }

    [Fact]
    public void A_demo_heat_seeds_the_mains_of_the_night_it_was_rehearsing()
    {
        // CHANGED 13 Oct 2026, and the old name said what it used to do:
        // "a demo race never seeds a real main". Qualifying skipped any
        // demo-linked heat outright, which was right while nothing linked a
        // demo to a heat on purpose - and it is what made a whole night
        // impossible to rehearse, because the mains then seeded off nothing.
        //
        // A demo only reaches a heat because the club asked for a demo
        // night (AppSettings.DemoFillsTheProgramme), and RaceFiling refuses
        // to let one land on a heat that really ran - so a demo in here
        // means the whole programme is a rehearsal, and seeding it is the
        // point. It still scores nothing: the championship, badges,
        // personal bests and every record check IsDemoSession themselves.
        //
        // The heat that must never be seeded from is covered where it is
        // actually prevented - see
        // RaceFilingTests.A_demo_is_refused_a_heat_that_really_ran.
        RunHeat("Q1", Result("A", 10, 300_000));
        RunHeat("Q2", Result("B", 14, 300_000));
        _sessions.Last().IsDemo = true;

        Assert.Equal(new[] { "B", "A" }, Order(Compute()));
    }

    [Fact]
    public void Only_this_class_s_recorded_qualifiers_count()
    {
        RunHeat("Q1", Result("A", 10, 300_000));
        RunRound(Stock, "A-Main", RoundKind.Main, Result("B", 20, 300_000));
        RunRound("Mod", "Q1", RoundKind.Qualifier, Result("C", 20, 300_000));

        // Scheduled, never run.
        _rounds.Add(new RoundDefinition { ClassName = Stock, RoundLabel = "Q2", Kind = RoundKind.Qualifier });

        Assert.Equal(new[] { "A" }, Order(Compute()));
    }

    [Fact]
    public void Scoped_to_one_race_day_only_that_day_s_heats_count()
    {
        RunHeat("Q1 (last week)", Result("A", 14, 300_000));
        var tonight = RunHeat("Q1", Result("B", 10, 300_000));

        var entries = Compute(filter: new[] { tonight.Id });

        Assert.Equal(new[] { "B" }, Order(entries));
    }

    // --- Two transponders, one driver -------------------------------------------

    [Fact]
    public void Heats_on_the_spare_car_count_for_the_driver_not_a_stranger()
    {
        _profiles.Upsert(new DriverProfile { Transponder = "100", DriverName = "Alice", AlternateTransponder = "200" });

        RunHeat("Q1", Result("200", 12, 300_000));
        RunHeat("Q2", Result("100", 11, 300_000));

        var entries = Compute(TotalLaps(countBest: 2));

        var alice = Assert.Single(entries);
        Assert.Equal("100", alice.Transponder);
        Assert.Equal("Alice", alice.DriverName);
        Assert.Equal(2, alice.HeatsRun);
        Assert.Equal(23, alice.BestLaps);
    }

    [Fact]
    public void Both_cars_in_one_heat_count_once_the_better_one()
    {
        _profiles.Upsert(new DriverProfile { Transponder = "100", DriverName = "Alice", AlternateTransponder = "200" });

        RunHeat("Q1", Result("100", 10, 300_000), Result("200", 12, 300_000));

        var alice = Assert.Single(Compute(TotalLaps(countBest: 2)));
        Assert.Equal(1, alice.HeatsRun);
        Assert.Equal(12, alice.BestLaps);
    }

    // --- The heat running now ------------------------------------------------------

    [Fact]
    public void A_heat_being_re_run_replaces_its_old_result_rather_than_adding_one()
    {
        var q1 = RunHeat("Q1", Result("A", 10, 300_000));

        var live = new QualifyingCalculator.LiveHeat(q1.Id, "Q1", DateTime.UtcNow, new[]
        {
            new QualifyingCalculator.LiveResult("A", "A", string.Empty, 14, 300_000, 0, Array.Empty<long>()),
        });

        var a = Assert.Single(Compute(TotalLaps(countBest: 2), live: live));
        Assert.Equal(1, a.HeatsRun);
        Assert.Equal(14, a.BestLaps);
    }

    // --- Helpers --------------------------------------------------------------------

    private static SessionResultEntry Result(string transponder, int laps, long elapsedMs, params long[] lapTimes) => new()
    {
        Transponder = transponder,
        DriverName = transponder,
        Laps = laps,
        ElapsedMs = elapsedMs,
        LapTimesMs = lapTimes.ToList(),
        FastLapMs = lapTimes.Skip(1).DefaultIfEmpty(0).Min(),
    };

    private RoundDefinition RunHeat(string label, params SessionResultEntry[] results) =>
        RunRound(Stock, label, RoundKind.Qualifier, results);

    /// <summary>A round as the app leaves it once raced: in the store, with its session saved and linked.</summary>
    private RoundDefinition RunRound(string className, string label, RoundKind kind, params SessionResultEntry[] results)
    {
        var round = new RoundDefinition { ClassName = className, RoundLabel = label, Kind = kind };
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
        return round;
    }

    private List<QualifyingCalculator.Entry> Compute(
        RaceClass? raceClass = null,
        IReadOnlyCollection<string>? filter = null,
        QualifyingCalculator.LiveHeat? live = null) =>
        QualifyingCalculator.Compute(Stock, _rounds, SessionHistoryStore.FromSessions(_sessions), _profiles, raceClass, filter, live);

    private static RaceClass Consecutive(int laps, int countBest = 1) => new()
    {
        Name = Stock,
        QualifyingMethod = QualifyingMethod.BestConsecutiveLaps,
        ConsecutiveLapCount = laps,
        CountBestRounds = countBest,
    };

    private static RaceClass TotalLaps(int countBest) => new()
    {
        Name = Stock,
        QualifyingMethod = QualifyingMethod.TotalLaps,
        CountBestRounds = countBest,
    };

    private static string[] Order(IEnumerable<QualifyingCalculator.Entry> entries) =>
        entries.Select(e => e.Transponder).ToArray();
}
