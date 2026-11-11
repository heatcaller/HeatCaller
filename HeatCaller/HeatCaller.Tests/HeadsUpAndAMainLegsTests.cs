namespace HeatCaller.Tests;

/// <summary>
/// 11 Nov 2026, race-night build plan, Phase 5.2 (heads-up / HRQF heats) and
/// 5.3 (double and triple A-mains): the pure pieces - HeadsUpQualifying and
/// AMainLegs - and MainsBuilder building the legs.
/// </summary>
public sealed class HeadsUpAndAMainLegsTests
{
    private static readonly string[] EightSeeds = { "s1", "s2", "s3", "s4", "s5", "s6", "s7", "s8" };

    [Fact]
    public void Round_one_spreads_every_other_seed_across_two_heats()
    {
        var heats = HeadsUpQualifying.Round(0, EightSeeds, 2, null);

        Assert.Equal(new[] { "s1", "s3", "s5", "s7" }, heats[0]);
        Assert.Equal(new[] { "s2", "s4", "s6", "s8" }, heats[1]);
    }

    [Fact]
    public void Round_two_runs_the_same_heats_with_the_grid_reversed()
    {
        var heats = HeadsUpQualifying.Round(1, EightSeeds, 2, null);

        Assert.Equal(new[] { "s7", "s5", "s3", "s1" }, heats[0]);
        Assert.Equal(new[] { "s8", "s6", "s4", "s2" }, heats[1]);
    }

    [Fact]
    public void Round_three_reseeds_on_points_and_spreads_again()
    {
        var points = new[] { "s8", "s7", "s6", "s5", "s4", "s3", "s2", "s1" };

        var heats = HeadsUpQualifying.Round(2, EightSeeds, 2, points);

        Assert.Equal(new[] { "s8", "s6", "s4", "s2" }, heats[0]);
        Assert.Equal(new[] { "s7", "s5", "s3", "s1" }, heats[1]);
    }

    [Fact]
    public void Round_three_falls_back_to_the_practice_seed_before_points_are_known()
    {
        Assert.Equal(HeadsUpQualifying.Round(0, EightSeeds, 2, null), HeadsUpQualifying.Round(2, EightSeeds, 2, null));
    }

    [Fact]
    public void The_reseed_keeps_a_late_entry_and_drops_a_driver_who_left()
    {
        var order = HeadsUpQualifying.WithEveryone(new[] { "B", "gone", "A" }, new[] { "A", "B", "late" });

        Assert.Equal(new[] { "B", "A", "late" }, order);
    }

    [Fact]
    public void Three_heats_take_every_third_seed_and_stay_level()
    {
        var seeds = Enumerable.Range(1, 10).Select(i => $"s{i}").ToList();

        var heats = HeadsUpQualifying.Spread(seeds, 3);

        Assert.Equal(new[] { "s1", "s4", "s7", "s10" }, heats[0]);
        Assert.Equal(new[] { "s2", "s5", "s8" }, heats[1]);
        Assert.True(heats.Max(h => h.Count) - heats.Min(h => h.Count) <= 1);
    }

    [Fact]
    public void Seeding_uses_practice_then_the_recent_best_lap_then_roster_order()
    {
        var practice = new Dictionary<string, long> { ["C"] = 33_000, ["A"] = 34_000 };
        var pace = new Dictionary<string, long> { ["D"] = 10_500, ["B"] = 12_000 };

        var seed = HeadsUpQualifying.Seed(new[] { "A", "B", "C", "D", "E" }, practice, pace);

        Assert.Equal(new[] { "C", "A", "D", "B", "E" }, seed);
    }

    [Fact]
    public void Practice_runs_are_three_flying_laps_from_this_classes_practice_only()
    {
        var stockPractice = new RoundDefinition { ClassName = "Stock", Kind = RoundKind.Practice };
        var modPractice = new RoundDefinition { ClassName = "Mod", Kind = RoundKind.Practice };
        var stockHeat = new RoundDefinition { ClassName = "Stock", Kind = RoundKind.Qualifier };
        var rounds = new[] { stockPractice, modPractice, stockHeat }.ToDictionary(r => r.Id);
        var since = new DateTime(2026, 11, 11, 0, 0, 0, DateTimeKind.Utc);

        SessionRecord Session(RoundDefinition? round, RaceSessionType type, params SessionResultEntry[] results) => new()
        {
            RoundId = round?.Id,
            SessionType = type,
            StartedAtUtc = since.AddHours(18),
            Results = results.ToList(),
        };

        SessionResultEntry Laps(string t, params long[] laps) => new() { Transponder = t, DriverName = t, LapTimesMs = laps.ToList(), Laps = laps.Length };

        var history = new[]
        {
            // Lap 0 is the run to the line and never counts: A's best three are 10+10+10.
            Session(stockPractice, RaceSessionType.Practice, Laps("A", 1_000, 10_000, 10_000, 10_000, 12_000)),
            // Open practice (no round) counts too.
            Session(null, RaceSessionType.Practice, Laps("B", 1_000, 11_000, 11_000, 11_000)),
            // Another class's practice and a qualifier don't.
            Session(modPractice, RaceSessionType.Practice, Laps("A", 1_000, 5_000, 5_000, 5_000)),
            Session(stockHeat, RaceSessionType.Race, Laps("C", 1_000, 9_000, 9_000, 9_000)),
        };

        var runs = HeadsUpQualifying.PracticeRuns("Stock", history, id => rounds.GetValueOrDefault(id), since);

        Assert.Equal(30_000, runs["A"]);
        Assert.Equal(33_000, runs["B"]);
        Assert.False(runs.ContainsKey("C"));
    }

    [Fact]
    public void Heads_up_is_the_last_first_round_choice_so_saved_days_keep_theirs()
    {
        Assert.Equal(3, (int)FirstRoundSeeding.HeadsUp);
        Assert.Contains("HRQF", FirstRound.Words(FirstRoundSeeding.HeadsUp));
    }

    // --- 5.3 A-main legs ---

    [Fact]
    public void A_triple_A_main_is_built_as_three_legs_run_last()
    {
        var plan = MainsBuilder.PlanSeeding(new[] { "1", "2", "3", "4", "5" }, 3);

        var rounds = MainsBuilder.BuildRounds("Stock", plan, "Club", 2, aMainLegs: 3);

        Assert.Equal(new[] { "B-Main", "A-Main Leg 1", "A-Main Leg 2", "A-Main Leg 3" }, rounds.Select(r => r.RoundLabel).ToArray());
        Assert.All(rounds.Skip(1), r =>
        {
            Assert.Equal(0, r.MainTier);
            Assert.Equal(3, r.MainLegCount);
            Assert.Equal(new[] { "1", "2", "3" }, r.AssignedTransponders);
            Assert.Equal(2, r.BumpUpCount);
        });
        Assert.Equal(new[] { 1, 2, 3 }, rounds.Skip(1).Select(r => r.MainLeg).ToArray());

        // Each leg's grid is its own list, so a bump-up into one leg doesn't land in another by accident.
        rounds[1].AssignedTransponders.Add("x");
        Assert.DoesNotContain("x", rounds[2].AssignedTransponders);
    }

    [Fact]
    public void A_single_A_main_is_built_as_before()
    {
        var plan = MainsBuilder.PlanSeeding(new[] { "1", "2" }, 8);

        var main = Assert.Single(MainsBuilder.BuildRounds("Stock", plan, "Club", 2));

        Assert.Equal("A-Main", main.RoundLabel);
        Assert.Equal(0, main.MainLeg);
    }

    [Fact]
    public void Every_leg_feeds_from_the_same_B_main()
    {
        var plan = MainsBuilder.PlanSeeding(new[] { "1", "2", "3", "4" }, 2);
        var rounds = MainsBuilder.BuildRounds("Stock", plan, "Club", 1, aMainLegs: 2);

        for (var i = 0; i < rounds.Count; i++)
        {
            rounds[i].Order = i;
        }

        var b = rounds.Single(r => r.MainTier == 1);
        Assert.All(rounds.Where(r => r.MainLeg > 0), leg => Assert.Same(b, MainsBuilder.FeederFor(leg, rounds)));
    }

    [Fact]
    public void Legs_count_best_two_of_three_by_default_and_best_one_of_two()
    {
        Assert.Equal(2, AMainLegs.LegsCounting(3, 0));
        Assert.Equal(1, AMainLegs.LegsCounting(2, 0));
        Assert.Equal(1, AMainLegs.LegsCounting(1, 0));
        Assert.Equal(3, AMainLegs.LegsCounting(3, 3));
        Assert.Equal(2, AMainLegs.LegsCounting(2, 5));
    }

    [Fact]
    public void Overall_is_points_over_the_best_legs_fewest_wins()
    {
        var legs = new[]
        {
            Leg(("A", 1), ("B", 2), ("C", 3)),
            Leg(("B", 1), ("C", 2), ("A", 3)),
            Leg(("A", 1), ("C", 2), ("B", 3)),
        };

        var overall = AMainLegs.Overall(legs, 2);

        // A 1+1 (3 dropped), B 1+2, C 2+2.
        Assert.Equal(new[] { "A", "B", "C" }, overall.Select(s => s.Transponder).ToArray());
        Assert.Equal(new[] { 2, 3, 4 }, overall.Select(s => s.Points).ToArray());
        Assert.Equal("2 pts (best 2 of 3 legs)", overall[0].Words());
        Assert.Equal("Leg 1 1st (1 pt), Leg 2 3rd (3 pts) dropped, Leg 3 1st (1 pt)", overall[0].Detail);
    }

    [Fact]
    public void Level_on_points_goes_to_best_finish_then_the_last_leg()
    {
        // Double A, both count: A 1+3 = 4, B 2+2 = 4, C 3+1 = 4.
        var legs = new[]
        {
            Leg(("A", 1), ("B", 2), ("C", 3)),
            Leg(("C", 1), ("B", 2), ("A", 3)),
        };

        var overall = AMainLegs.Overall(legs, 2);

        // A and C both have a win; C won the last leg. B never won.
        Assert.Equal(new[] { "C", "A", "B" }, overall.Select(s => s.Transponder).ToArray());
    }

    [Fact]
    public void A_leg_not_started_or_DQd_scores_the_field_plus_one()
    {
        var dq = new SessionResultEntry { Transponder = "B", DriverName = "B", Position = 1, Laps = 20, Outcome = ResultEditor.Dq };
        var legs = new[]
        {
            Leg(("A", 1), ("B", 2), ("C", 3)),
            new SessionRecord { Results = new List<SessionResultEntry> { dq, R("A", 2), R("C", 3) } },
            Leg(("A", 1), ("B", 2)),
        };

        var overall = AMainLegs.Overall(legs, 3);

        // Field of 3 -> 4 points. B: 2 + DQ 4 + 2. C: 3 + 2 + DNS 4. A: 1 + 1 + 1.
        Assert.Equal(new[] { 3, 8, 9 }, overall.Select(s => s.Points).ToArray());
        Assert.Contains("Leg 2 DQ (4 pts)", overall[1].Detail);
        Assert.Contains("Leg 3 DNS (4 pts)", overall[2].Detail);
    }

    [Fact]
    public void After_one_leg_the_overall_is_that_leg()
    {
        var overall = AMainLegs.Overall(new[] { Leg(("A", 1), ("B", 2)) }, 2);

        Assert.Equal("1 pt (Leg 1)", overall[0].Words());
        Assert.Equal(1, overall[0].LegsCounted);
    }

    [Fact]
    public void An_alternate_transponder_is_the_same_driver_across_legs()
    {
        var legs = new[]
        {
            Leg(("A", 1), ("B", 2)),
            Leg(("A-spare", 1), ("B", 2)),
        };

        var overall = AMainLegs.Overall(legs, 2, t => t == "A-spare" ? "A" : null);

        Assert.Equal(2, overall.Count);
        Assert.Equal(2, overall[0].Points);
        Assert.Equal("A", overall[0].Transponder);
    }

    private static SessionResultEntry R(string transponder, int position) => new()
    {
        Transponder = transponder,
        DriverName = transponder,
        Position = position,
        Laps = 20 - position,
        ElapsedMs = 300_000,
    };

    private static SessionRecord Leg(params (string Transponder, int Position)[] finishers) => new()
    {
        Results = finishers.Select(f => R(f.Transponder, f.Position)).ToList(),
    };
}
