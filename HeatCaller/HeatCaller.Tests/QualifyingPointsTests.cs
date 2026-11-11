namespace HeatCaller.Tests;

/// <summary>
/// Points qualifying (11 Nov 2026, race-night build plan, Phase 5.1):
/// QualifyingMethod.RoundPoints ranks every heat of a round together, scores
/// place points, a missed or DQ'd round scores field + 1, best N rounds count,
/// fewest points wins, ties to the best single-round finish then the best heat.
/// </summary>
public sealed class QualifyingPointsTests : IDisposable
{
    // Declared first: the stores below load from wherever it points.
    private readonly TempAppData _data = new();

    private readonly RoundDefinitionStore _rounds = RoundDefinitionStore.Load();
    private readonly ProfileStore _profiles = ProfileStore.Load();
    private readonly List<SessionRecord> _sessions = new();
    private DateTime _clock = new(2026, 11, 11, 18, 0, 0, DateTimeKind.Utc);

    private const string Stock = "Stock";

    public void Dispose() => _data.Dispose();

    private static RaceClass Points(int countBest = 1) => new()
    {
        Name = Stock,
        QualifyingMethod = QualifyingMethod.RoundPoints,
        CountBestRounds = countBest,
    };

    [Fact]
    public void A_round_ranks_drivers_across_all_of_its_heats()
    {
        // Q1 in two heats: C (heat 2) beats everybody in heat 1.
        RunHeat("Q1 Heat 1", R("A", 14, 300_000), R("B", 13, 300_000));
        RunHeat("Q1 Heat 2", R("C", 15, 301_000), R("D", 12, 300_000));

        var entries = Compute(Points());

        Assert.Equal(new[] { "C", "A", "B", "D" }, Ids(entries));
        Assert.Equal(new[] { 1, 2, 3, 4 }, entries.Select(e => e.Points).ToArray());
        Assert.Equal("Q1 1st (1 pt)", entries[0].PointsDetail);
        Assert.Equal("1 pt (Q1)", entries[0].PointsWords());
    }

    [Fact]
    public void Points_total_across_rounds_and_fewest_wins()
    {
        RunHeat("Q1 Heat 1", R("A", 14, 300_000), R("B", 13, 300_000), R("C", 12, 300_000));
        RunHeat("Q2 Heat 1", R("B", 14, 300_000), R("C", 13, 300_000), R("A", 12, 300_000));

        var entries = Compute(Points(countBest: 2));

        // A 1+3=4, B 2+1=3, C 3+2=5.
        Assert.Equal(new[] { "B", "A", "C" }, Ids(entries));
        Assert.Equal(new[] { 3, 4, 5 }, entries.Select(e => e.Points).ToArray());
        Assert.Equal("3 pts (2 rounds)", entries[0].PointsWords());
        Assert.Equal("Q1 + Q2", entries[0].SourceRoundLabel);
    }

    [Fact]
    public void A_missed_round_scores_the_field_plus_one_and_best_n_drops_it()
    {
        RunHeat("Q1 Heat 1", R("A", 14, 300_000), R("B", 13, 300_000), R("C", 12, 300_000));
        RunHeat("Q2 Heat 1", R("A", 14, 300_000), R("C", 13, 300_000));          // B missed Q2
        RunHeat("Q3 Heat 1", R("B", 14, 300_000), R("A", 13, 300_000), R("C", 12, 300_000));

        // Every round counted: B scores 2 + 4 (field of 3, plus one) + 1.
        var all = Compute(Points(countBest: 3));
        var b = all.Single(e => e.Transponder == "B");
        Assert.Equal(7, b.Points);
        Assert.Equal("Q1 2nd (2 pts), Q2 DNS (4 pts), Q3 1st (1 pt)", b.PointsDetail);

        // Best 2 of 3: the DNS is the round dropped.
        var best2 = Compute(Points(countBest: 2));
        b = best2.Single(e => e.Transponder == "B");
        Assert.Equal(3, b.Points);
        Assert.Equal("Q2 (DNS, 4 pts)", b.DroppedSummary);
        Assert.Equal("3 pts (best 2 of 3)", b.PointsWords());

        // A: 1, 1, 2 -> best two = 2. B: 2, 1 -> 3.
        Assert.Equal(new[] { "A", "B", "C" }, Ids(best2));
    }

    [Fact]
    public void Missing_a_round_can_never_beat_running_it_last()
    {
        RunHeat("Q1 Heat 1", R("A", 14, 300_000), R("B", 13, 300_000));
        RunHeat("Q2 Heat 1", R("A", 14, 300_000), R("B", 2, 300_000));
        RunHeat("Q2 Heat 2", R("C", 15, 300_000));                                  // C missed Q1

        var entries = Compute(Points(countBest: 2));

        // B: 2 + 3 = 5. C: DNS (4) + 1 = 5 - level, and C's best finish (1st) beats B's (2nd).
        Assert.Equal(5, entries.Single(e => e.Transponder == "B").Points);
        Assert.Equal(5, entries.Single(e => e.Transponder == "C").Points);
        Assert.Equal(new[] { "A", "C", "B" }, Ids(entries));
    }

    [Fact]
    public void A_disqualified_heat_scores_as_a_missed_round_and_says_DQ()
    {
        var dq = R("A", 15, 300_000);
        dq.Outcome = ResultEditor.Dq;
        RunHeat("Q1 Heat 1", dq, R("B", 13, 300_000), R("C", 12, 300_000));
        RunHeat("Q2 Heat 1", R("A", 14, 300_000), R("B", 13, 300_000), R("C", 12, 300_000));

        var entries = Compute(Points(countBest: 2));
        var a = entries.Single(e => e.Transponder == "A");

        // Field of 3: the DQ scores 4, and B is 1st in Q1 with A out of it.
        Assert.Equal(4 + 1, a.Points);
        Assert.Contains("Q1 DQ (4 pts)", a.PointsDetail);
        Assert.Equal(1 + 2, entries.Single(e => e.Transponder == "B").Points);
        Assert.Equal(new[] { "B", "A", "C" }, Ids(entries));
    }

    [Fact]
    public void A_penalty_already_in_the_laps_and_time_moves_the_driver_down_the_round()
    {
        // Penalties reach Laps and ElapsedMs (ResultEditor, RaceFiling) - the
        // figures a round is ranked on - so A's +10s puts them behind B.
        var a = R("A", 14, 310_000);
        a.TimePenaltyMs = 10_000;
        RunHeat("Q1 Heat 1", a, R("B", 14, 305_000));

        Assert.Equal(new[] { "B", "A" }, Ids(Compute(Points())));
    }

    [Fact]
    public void A_dead_heat_shares_the_place_and_the_next_driver_skips_one()
    {
        RunHeat("Q1 Heat 1", R("A", 14, 300_000), R("B", 13, 300_000));
        RunHeat("Q1 Heat 2", R("C", 14, 300_000), R("D", 12, 300_000));

        var entries = Compute(Points());

        Assert.Equal(1, entries.Single(e => e.Transponder == "A").Points);
        Assert.Equal(1, entries.Single(e => e.Transponder == "C").Points);
        Assert.Equal(3, entries.Single(e => e.Transponder == "B").Points);
        Assert.Equal(4, entries.Single(e => e.Transponder == "D").Points);
    }

    [Fact]
    public void Level_on_points_and_best_finish_goes_to_the_best_single_heat()
    {
        RunHeat("Q1 Heat 1", R("A", 14, 300_000), R("B", 13, 300_000));
        RunHeat("Q2 Heat 1", R("B", 15, 300_000), R("A", 13, 300_000));

        var entries = Compute(Points(countBest: 2));

        // Both 1 + 2 = 3, both have a win: B's 15-lap heat beats A's 14.
        Assert.Equal(new[] { "B", "A" }, Ids(entries));
        Assert.Equal(15, entries[0].BestLaps);
    }

    [Fact]
    public void Fewer_rounds_run_than_counted_counts_the_rounds_there_are()
    {
        RunHeat("Q1 Heat 1", R("A", 14, 300_000), R("B", 13, 300_000));

        var entries = Compute(Points(countBest: 3));

        Assert.Equal(1, entries[0].PointsRoundsCounted);
        Assert.Equal(1, entries[0].PointsRoundsRun);
        Assert.Equal(new[] { 1, 2 }, entries.Select(e => e.Points).ToArray());
    }

    [Fact]
    public void An_alternate_transponder_is_the_same_driver_and_two_cars_in_one_round_count_once()
    {
        _profiles.Upsert(new DriverProfile { Transponder = "100", DriverName = "Alice", AlternateTransponder = "200" });

        // Alice ran both cars in Q1 (rows 100 and 200); the better one stands.
        RunHeat("Q1 Heat 1", R("100", 12, 300_000), R("200", 14, 300_000), R("B", 13, 300_000));
        RunHeat("Q2 Heat 1", R("200", 14, 300_000), R("B", 13, 300_000));

        var entries = Compute(Points(countBest: 2));

        Assert.Equal(2, entries.Count);
        var alice = entries.Single(e => e.Transponder == "100");
        Assert.Equal("Alice", alice.DriverName);
        Assert.Equal(2, alice.Points);
        Assert.Equal("Q1 1st (1 pt), Q2 1st (1 pt)", alice.PointsDetail);

        // Field of two drivers, not three rows: B is 2nd in each round.
        Assert.Equal(4, entries.Single(e => e.Transponder == "B").Points);
    }

    [Fact]
    public void The_live_heat_is_ranked_with_its_rounds_finished_heats()
    {
        RunHeat("Q1 Heat 1", R("A", 14, 300_000), R("B", 13, 300_000));
        var heat2 = new RoundDefinition { ClassName = Stock, RoundLabel = "Q1 Heat 2", Kind = RoundKind.Qualifier };
        _rounds.Add(heat2);

        var live = new QualifyingCalculator.LiveHeat(heat2.Id, heat2.RoundLabel, _clock.AddMinutes(30), new[]
        {
            new QualifyingCalculator.LiveResult("C", "C", string.Empty, 15, 300_000, 0, Array.Empty<long>()),
        });

        var entries = Compute(Points(), live);

        // One round, not two: C's live 15 laps takes 1st off A.
        Assert.Equal(new[] { "C", "A", "B" }, Ids(entries));
        Assert.Equal(new[] { 1, 2, 3 }, entries.Select(e => e.Points).ToArray());
        Assert.Equal(1, entries[0].PointsRoundsRun);
    }

    [Fact]
    public void A_heat_being_rerun_replaces_its_recorded_result()
    {
        var q1 = RunHeat("Q1 Heat 1", R("A", 14, 300_000), R("B", 13, 300_000));

        var live = new QualifyingCalculator.LiveHeat(q1.Id, q1.RoundLabel, _clock, new[]
        {
            new QualifyingCalculator.LiveResult("A", "A", string.Empty, 10, 300_000, 0, Array.Empty<long>()),
            new QualifyingCalculator.LiveResult("B", "B", string.Empty, 11, 300_000, 0, Array.Empty<long>()),
        });

        var entries = Compute(Points(), live);

        Assert.Equal(new[] { "B", "A" }, Ids(entries));
        Assert.All(entries, e => Assert.Equal(1, e.HeatsRun));
    }

    [Fact]
    public void A_live_heat_of_a_new_round_gives_the_drivers_not_in_it_a_DNS_for_now()
    {
        RunHeat("Q1 Heat 1", R("A", 14, 300_000), R("B", 13, 300_000));
        var q2 = new RoundDefinition { ClassName = Stock, RoundLabel = "Q2 Heat 1", Kind = RoundKind.Qualifier };
        _rounds.Add(q2);

        var live = new QualifyingCalculator.LiveHeat(q2.Id, q2.RoundLabel, _clock.AddHours(1), new[]
        {
            new QualifyingCalculator.LiveResult("B", "B", string.Empty, 15, 300_000, 0, Array.Empty<long>()),
        });

        var entries = Compute(Points(countBest: 2), live);

        // A: 1 + DNS (field of 2, plus one = 3) = 4. B: 2 + 1 = 3.
        Assert.Equal(new[] { "B", "A" }, Ids(entries));
        Assert.Equal(new[] { 3, 4 }, entries.Select(e => e.Points).ToArray());
        Assert.Equal("Q1 1st (1 pt), Q2 DNS (3 pts)", entries[1].PointsDetail);
    }

    [Fact]
    public void A_hand_added_unnumbered_heat_beside_numbered_rounds_is_a_round_of_its_own()
    {
        RunHeat("Q1 Heat 1", R("A", 14, 300_000), R("B", 13, 300_000));
        RunHeat("Extra heat", R("B", 20, 300_000));

        var entries = Compute(Points(countBest: 2));
        var b = entries.Single(e => e.Transponder == "B");

        // Not folded into Q1 (where it would beat A): its own round.
        Assert.Equal(2, b.PointsRoundsRun);
        Assert.Equal("Q1 2nd (2 pts), Extra heat 1st (1 pt)", b.PointsDetail);
    }

    [Fact]
    public void A_one_round_night_of_unnumbered_heats_is_one_round()
    {
        RunHeat("Heat 1", R("A", 14, 300_000));
        RunHeat("Heat 2", R("B", 15, 300_000));

        var entries = Compute(Points());

        Assert.Equal(new[] { "B", "A" }, Ids(entries));
        Assert.Equal(1, entries[0].PointsRoundsRun);
        Assert.Equal("Qualifying 1st (1 pt)", entries[0].PointsDetail);
    }

    [Fact]
    public void Club_night_races_are_named_as_races()
    {
        RunHeat("Race 1", R("A", 14, 300_000));
        RunHeat("Race 2", R("A", 14, 300_000));

        var a = Assert.Single(Compute(Points(countBest: 2)));

        Assert.Equal("Race 1 1st (1 pt), Race 2 1st (1 pt)", a.PointsDetail);
    }

    [Fact]
    public void The_other_methods_are_untouched()
    {
        RunHeat("Q1 Heat 1", R("A", 14, 300_000), R("B", 13, 300_000));

        var entries = Compute(new RaceClass { Name = Stock, QualifyingMethod = QualifyingMethod.TotalLaps });

        Assert.All(entries, e => Assert.Equal(0, e.Points));
        Assert.Equal(string.Empty, entries[0].PointsDetail);
    }

    [Fact]
    public void Mains_preview_states_the_points()
    {
        RunHeat("Q1 Heat 1", R("A", 14, 300_000), R("B", 13, 300_000));
        RunHeat("Q2 Heat 1", R("A", 14, 300_000), R("B", 13, 300_000));
        RunHeat("Q3 Heat 1", R("B", 14, 300_000), R("A", 13, 300_000));

        var entries = Compute(Points(countBest: 2));
        var a = entries.Single(e => e.Transponder == "A");

        Assert.Equal("2 pts (best 2 of 3)", MainsPreview.ResultWords(a, QualifyingMethod.RoundPoints, 3));

        var plan = MainsBuilder.PlanSeeding(Ids(entries), 8);
        var text = MainsPreview.Text(plan, entries.ToDictionary(e => e.Transponder), QualifyingMethod.RoundPoints, 3, 0);
        Assert.Contains("1. A - 2 pts (best 2 of 3)", text);
    }

    [Fact]
    public void The_recap_reads_points_and_the_gap_in_points()
    {
        var cars = new List<RecapCar>
        {
            new("Alice", "Alice", 14, 300_000, 0, 11_000, 2, Points: 3),
            new("Bob", "Bob", 15, 300_000, 0, 11_000, 2, Points: 5),
        };

        var (_, screen) = DriverIntros.Recap(cars, QualifyingMethod.RoundPoints, 3);

        Assert.Contains("with 3 points", screen);
        Assert.Contains("2 points back", screen);
        Assert.DoesNotContain("laps", screen);
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

    private static string[] Ids(IEnumerable<QualifyingCalculator.Entry> entries) => entries.Select(e => e.Transponder).ToArray();

    private List<QualifyingCalculator.Entry> Compute(RaceClass raceClass, QualifyingCalculator.LiveHeat? live = null) =>
        QualifyingCalculator.Compute(Stock, _rounds, SessionHistoryStore.FromSessions(_sessions), _profiles, raceClass, null, live);
}
