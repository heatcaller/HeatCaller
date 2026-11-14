namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 10 (23 Oct 2026), the pieces wired across files:
/// "Check my transponder" reports the pass just made; a lap taken off by
/// hand stops being the race's bar (PaceWatch.Forget); the qualifying
/// figure carries its penalty share; a personal-best reset moves the
/// store's revision; and the budget exposes the month's allowance.
/// </summary>
public sealed class BugHunt10_IntegrationCTests : IDisposable
{
    // Declared first: the stores below load from wherever it points.
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // ------------------------------------------------------------------
    // TransponderWatch: the last pass
    // ------------------------------------------------------------------

    private static DirectLapEvent Pass(string id, int hits, int strength) =>
        new() { TransponderId = id, SignalHits = hits, SignalStrength = strength };

    [Fact]
    public void A_fixed_transponder_reads_good_on_its_next_pass_while_the_window_still_says_weak()
    {
        var watch = new TransponderWatch();

        for (var i = 0; i < 10; i++)
        {
            watch.Record(Pass("T1", 1, 10));
        }

        watch.Record(Pass("T1", 6, 100));

        var sighting = watch.Of("T1");
        Assert.NotNull(sighting);
        Assert.Equal(6, sighting!.LastHits);
        Assert.True(sighting.LastSignalKnown);
        Assert.False(sighting.LastPassWeak);

        // The readiness check and Mission Control still see the window.
        Assert.True(sighting.Weak);
    }

    [Fact]
    public void The_last_pass_decides_weak_and_unknown()
    {
        var watch = new TransponderWatch();

        watch.Record(Pass("T2", 8, 100));
        watch.Record(Pass("T2", 1, 10));
        Assert.True(watch.Of("T2")!.LastPassWeak);

        // A pass with no signal reading (an I-Lap) is unknown, not weak.
        watch.Record(Pass("T2", 0, 0));
        Assert.False(watch.Of("T2")!.LastSignalKnown);
    }

    // ------------------------------------------------------------------
    // PaceWatch.Forget
    // ------------------------------------------------------------------

    [Fact]
    public void A_lap_taken_off_by_hand_is_no_longer_the_fastest_lap_of_the_race()
    {
        var watch = new PaceWatch();

        for (var i = 0; i < 4; i++)
        {
            watch.Saw(new LapSeen($"warm-{i}", 20.0 + i));
        }

        // A double read: half a lap, the race's fastest by a mile.
        Assert.Equal(PaceNoteKind.SessionBest, watch.Saw(new LapSeen("Sam", 15.0))?.Kind);

        // Taken off by hand - Sam's real lap is what is left.
        watch.Forget("Sam", new[] { 19.0 });

        var jen = watch.Saw(new LapSeen("Jen", 18.5));

        Assert.NotNull(jen);
        Assert.Equal(PaceNoteKind.SessionBest, jen!.Kind);
        Assert.Equal(19.0, jen.Beaten!.Value, 3);
        Assert.Equal("Sam", jen.PreviousHolder);
    }

    [Fact]
    public void Forgetting_every_lap_of_a_driver_hands_the_race_best_to_the_next_quickest()
    {
        var watch = new PaceWatch();

        for (var i = 0; i < 4; i++)
        {
            watch.Saw(new LapSeen($"warm-{i}", 20.0 + i));
        }

        watch.Saw(new LapSeen("Sam", 15.0));
        watch.Forget("Sam", Array.Empty<double>());

        // 19.9 beats warm-0's 20.0, which is the race's best again.
        var jen = watch.Saw(new LapSeen("Jen", 19.9));

        Assert.NotNull(jen);
        Assert.Equal(PaceNoteKind.SessionBest, jen!.Kind);
        Assert.Equal("warm-0", jen.PreviousHolder);
    }

    // ------------------------------------------------------------------
    // QualifyingCalculator: the penalty share
    // ------------------------------------------------------------------

    [Fact]
    public void A_penalised_drivers_laps_and_penalty_add_up_to_the_ranked_figure()
    {
        var rounds = RoundDefinitionStore.Load();
        var profiles = ProfileStore.Load();
        var sessions = new List<SessionRecord>();
        var rule = new RaceClass
        {
            Name = "Stock",
            QualifyingMethod = QualifyingMethod.BestConsecutiveLaps,
            ConsecutiveLapCount = 3,
            CountBestRounds = 1,
        };

        var penalised = new SessionResultEntry
        {
            Transponder = "A",
            DriverName = "A",
            Laps = 4,
            ElapsedMs = 51_600,
            LapTimesMs = new List<long> { 1_400, 11_700, 11_700, 11_800 },
            FastLapMs = 11_700,
            RacePenaltyMs = 15_000,
        };
        var clean = new SessionResultEntry
        {
            Transponder = "B",
            DriverName = "B",
            Laps = 4,
            ElapsedMs = 36_800,
            LapTimesMs = new List<long> { 1_400, 11_800, 11_800, 11_800 },
            FastLapMs = 11_800,
        };

        var round = new RoundDefinition { ClassName = "Stock", RoundLabel = "Q1", Kind = RoundKind.Qualifier };
        rounds.Add(round);
        var session = new SessionRecord
        {
            Label = "Q1",
            SessionType = RaceSessionType.Race,
            RoundId = round.Id,
            StartedAtUtc = DateTime.UtcNow,
            Results = new List<SessionResultEntry> { penalised, clean },
        };
        sessions.Add(session);
        Assert.True(rounds.MarkRecorded(round.Id, session.Id));

        var entries = QualifyingCalculator.Compute("Stock", rounds, SessionHistoryStore.FromSessions(sessions), profiles, rule, null, null);

        var a = entries.Single(e => e.Transponder == "A");
        Assert.Equal(15_000, a.ConsecutivePenaltyMs);
        Assert.Equal(35_200, a.BestConsecutiveMs - a.ConsecutivePenaltyMs);
        Assert.Equal(0, entries.Single(e => e.Transponder == "B").ConsecutivePenaltyMs);
    }

    // ------------------------------------------------------------------
    // PersonalBestResetStore.Revision and CommentaryBudget.MonthAllowance
    // ------------------------------------------------------------------

    [Fact]
    public void Every_reset_and_undo_moves_the_reset_revision()
    {
        var store = PersonalBestResetStore.Load();
        var start = store.Revision;
        var before = store.Snapshot();

        store.SetCutoff("Sam", "Club Oval", DateTime.UtcNow);
        store.ClearCutoff("Sam", "Club Oval");
        store.ClearAllForDriver("Sam");
        store.Restore(before);
        store.RestoreForDriver("Sam", before);

        Assert.Equal(start + 5, store.Revision);
    }

    [Fact]
    public void The_budget_reports_the_months_allowance_once_the_relay_has_said()
    {
        var budget = new CommentaryBudget();
        Assert.Equal(0, budget.MonthAllowance);

        budget.MonthlyUsage(100, 1_000);

        Assert.Equal(1_000, budget.MonthAllowance);
        Assert.Equal(900, budget.RemainingThisMonth);
    }
}
