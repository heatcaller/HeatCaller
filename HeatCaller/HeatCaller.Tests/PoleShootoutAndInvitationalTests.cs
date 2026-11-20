using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// 19 Nov 2026, race-night build plan row 14b: the Pole Shootout and the
/// Invitational (Reedy-style format) - the pure pieces (PoleShootout,
/// Invitational, and AMainLegs.Overall's new scale and field), the build
/// question's numbers, and old saved classes and rounds still loading.
/// </summary>
public sealed class PoleShootoutAndInvitationalTests
{
    private static readonly string[] Qualifying = { "q1", "q2", "q3", "q4", "q5", "q6", "q7", "q8" };

    // ---------------------------------------------------------------- Pole Shootout

    [Fact]
    public void The_shootout_field_is_the_top_n_and_the_slowest_of_them_runs_first()
    {
        var field = PoleShootout.Field(Qualifying, 5);
        var runs = PoleShootout.BuildRuns("Stock", field, 1, null, "Club");

        Assert.Equal(new[] { "q1", "q2", "q3", "q4", "q5" }, field);
        Assert.Equal(5, runs.Count);
        Assert.Equal(new[] { "q5", "q4", "q3", "q2", "q1" }, runs.Select(r => r.AssignedTransponders.Single()));
        Assert.Equal(new[] { 5, 4, 3, 2, 1 }, runs.Select(r => r.ShootoutQualifyingPlace));
        Assert.All(runs, r => Assert.Equal(RoundKind.Shootout, r.Kind));
        Assert.Equal("Pole Shootout Run 1", runs[0].RoundLabel);
    }

    [Fact]
    public void A_run_is_an_out_lap_and_the_timed_laps_on_its_own_clock()
    {
        var format = PoleShootout.RunFormat(new RaceFormat { TimeLimitMinutes = 8, StartType = RaceStartType.StaggeredGrid, WarmUpSeconds = 60 }, 1);

        Assert.Equal(RaceStartType.Qualifier, format.StartType);

        // The first crossing is counted as a lap (the start), so one timed lap is a limit of two.
        Assert.Equal(2, format.LapLimit);
        Assert.Equal(0, format.WarmUpSeconds);
        Assert.Equal(4, PoleShootout.RunFormat(null, 3).LapLimit);
    }

    [Fact]
    public void Fewer_than_two_qualifiers_is_no_shootout()
    {
        Assert.Empty(PoleShootout.BuildRuns("Stock", new[] { "q1" }, 1, null, "Club"));
    }

    [Fact]
    public void The_shootout_size_is_kept_to_its_limits_and_the_a_main()
    {
        Assert.Equal(5, PoleShootout.DriversFor(new RaceClass()));
        Assert.Equal(4, PoleShootout.DriversFor(new RaceClass { PoleShootoutDrivers = 6, MainSize = 4 }));
        Assert.Equal(PoleShootout.MaxDrivers, PoleShootout.DriversFor(new RaceClass { PoleShootoutDrivers = 99, MainSize = 40 }));
        Assert.Equal(1, PoleShootout.TimedLapsFor(new RaceClass { PoleShootoutLaps = 0 }));
        Assert.Equal(3, PoleShootout.TimedLapsFor(new RaceClass { PoleShootoutLaps = 9 }));
    }

    [Fact]
    public void Unrun_runs_follow_a_changed_qualifying_order()
    {
        var runs = PoleShootout.BuildRuns("Stock", new[] { "q1", "q2", "q3" }, 1, null, "Club");

        Assert.Equal(PoleShootout.RefreshResult.Unchanged, PoleShootout.Refresh(runs, new[] { "q1", "q2", "q3" }));
        Assert.Equal(PoleShootout.RefreshResult.Updated, PoleShootout.Refresh(runs, new[] { "q2", "q1", "q3" }));

        // The top qualifier still goes last.
        Assert.Equal("q2", runs.Single(r => r.ShootoutRun == 3).AssignedTransponders.Single());
        Assert.Equal("q1", runs.Single(r => r.ShootoutRun == 2).AssignedTransponders.Single());
        Assert.Equal(PoleShootout.RefreshResult.Rebuild, PoleShootout.Refresh(runs, new[] { "q2", "q1" }));
    }

    [Fact]
    public void Once_a_run_has_been_raced_the_shootout_is_left_alone()
    {
        var runs = PoleShootout.BuildRuns("Stock", new[] { "q1", "q2", "q3" }, 1, null, "Club");
        runs[0].LinkedSessionId = "s1";

        Assert.Equal(PoleShootout.RefreshResult.Frozen, PoleShootout.Refresh(runs, new[] { "q3", "q2", "q1" }));
        Assert.Equal("q3", runs[0].AssignedTransponders.Single());
    }

    [Fact]
    public void A_runs_time_is_its_best_timed_lap_not_the_start()
    {
        // The start crossing is the 0, then the out lap is not in the list at all.
        var row = new SessionResultEntry { Transponder = "q1", Laps = 3, LapTimesMs = new List<long> { 0, 12_500, 12_100 } };

        Assert.Equal(12_500, PoleShootout.RunTimeMs(row, 1));
        Assert.Equal(12_100, PoleShootout.RunTimeMs(row, 2));
        Assert.Equal(0, PoleShootout.RunTimeMs(new SessionResultEntry { Transponder = "q1", Laps = 1, LapTimesMs = new List<long> { 0 } }, 1));
        Assert.Equal(0, PoleShootout.RunTimeMs(new SessionResultEntry { Transponder = "q1", Laps = 2, LapTimesMs = new List<long> { 0, 12_000 }, Outcome = ResultEditor.Dq }, 1));
        Assert.Equal(13_000, PoleShootout.RunTimeMs(new SessionResultEntry { Transponder = "q1", Laps = 2, LapTimesMs = new List<long> { 0, 12_000 }, TimePenaltyMs = 1_000 }, 1));
    }

    [Fact]
    public void Fastest_run_takes_pole_and_a_run_with_no_time_keeps_its_qualifying_place_at_the_back()
    {
        var runs = PoleShootout.BuildRuns("Stock", new[] { "q1", "q2", "q3", "q4" }, 1, null, "Club");
        var sessions = new Dictionary<string, SessionRecord>
        {
            ["r1"] = Run("q4", 12_900),
            ["r2"] = Run("q3", 0),
            ["r3"] = Run("q2", 12_300),
            ["r4"] = Run("q1", 0),
        };

        for (var i = 0; i < runs.Count; i++)
        {
            runs[i].LinkedSessionId = $"r{i + 1}";
        }

        var standing = PoleShootout.Standings(runs, id => sessions.GetValueOrDefault(id), 1);

        Assert.True(standing.Complete);
        Assert.Equal(new[] { "q2", "q4", "q1", "q3" }, standing.Order.Select(t => t.Transponder));
        Assert.Equal("q2", standing.Pole!.Transponder);

        // The A-main: the shootout's four at the front in its order, the rest as they were.
        var grid = PoleShootout.Grid(new[] { "q1", "q2", "q3", "q4", "q5", "q6" }, standing);
        Assert.Equal(new[] { "q2", "q4", "q1", "q3", "q5", "q6" }, grid);
    }

    [Fact]
    public void The_run_just_finished_counts_before_it_is_linked()
    {
        var runs = PoleShootout.BuildRuns("Stock", new[] { "q1", "q2" }, 1, null, "Club");
        runs[0].LinkedSessionId = "r1";
        var sessions = new Dictionary<string, SessionRecord> { ["r1"] = Run("q2", 13_000) };

        var standing = PoleShootout.Standings(runs, id => sessions.GetValueOrDefault(id), 1, justRun: (runs[1], Run("q1", 12_000)));

        Assert.True(standing.Complete);
        Assert.Equal("q1", standing.Pole!.Transponder);
        Assert.Equal("Sam takes pole in the Pole Shootout, 12.000.", PoleShootout.PoleCall("Sam", standing.Pole.TimeMs));
    }

    [Fact]
    public void The_run_call_is_short_and_says_where_it_puts_the_driver()
    {
        var runs = PoleShootout.BuildRuns("Stock", new[] { "q1", "q2", "q3" }, 1, null, "Club");
        runs[0].LinkedSessionId = "r1";
        var sessions = new Dictionary<string, SessionRecord> { ["r1"] = Run("q3", 12_000) };

        var standing = PoleShootout.Standings(runs, id => sessions.GetValueOrDefault(id), 1, justRun: (runs[1], Run("q2", 12_500)));

        Assert.Equal("Max, 12.500 - 2nd so far.", PoleShootout.RunCall("Max", standing, "q2"));
        Assert.Equal("Bo, 12.000 - quickest so far.", PoleShootout.RunCall("Bo", standing, "q3"));
        Assert.Equal("Al, no time.", PoleShootout.RunCall("Al", standing, "q1"));
    }

    [Fact]
    public void The_shootout_grids_the_a_main_or_its_legs_or_an_invitationals_first_main()
    {
        var aMain = new RoundDefinition { ClassName = "Stock", Kind = RoundKind.Main, MainTier = 0 };
        var bMain = new RoundDefinition { ClassName = "Stock", Kind = RoundKind.Main, MainTier = 1 };
        Assert.Equal(new[] { aMain }, PoleShootout.GridTargets(new[] { aMain, bMain }, "Stock"));

        var legs = MainsBuilder.BuildRounds("Stock", MainsBuilder.PlanSeeding(Qualifying, 8), "Club", 2, aMainLegs: 3);
        Assert.Equal(3, PoleShootout.GridTargets(legs, "Stock").Count);

        var invitational = Invitational.BuildMains("Stock", Qualifying, 5, "Club");
        Assert.Equal(1, PoleShootout.GridTargets(invitational, "Stock").Single().InvitationalMain);
    }

    // ---------------------------------------------------------------- Invitational

    [Fact]
    public void The_field_is_seeded_by_qualifying_then_the_desks_order_then_the_roster_and_cut_to_the_limit()
    {
        var roster = new[] { "a", "b", "c", "d", "e" };

        Assert.Equal(new[] { "c", "a", "e", "b", "d" }, Invitational.Field(new[] { "c", "a", "gone" }, roster, new[] { "e" }, 0));
        Assert.Equal(new[] { "d", "b", "a", "c", "e" }, Invitational.Field(Array.Empty<string>(), roster, new[] { "d", "b" }, 0));
        Assert.Equal(new[] { "a", "b", "c" }, Invitational.Field(Array.Empty<string>(), roster, null, 3));
    }

    [Fact]
    public void The_mains_are_the_whole_field_every_time()
    {
        var mains = Invitational.BuildMains("Stock", new[] { "a", "b", "c" }, 5, "Club");

        Assert.Equal(5, mains.Count);
        Assert.All(mains, m => Assert.Equal(new[] { "a", "b", "c" }, m.AssignedTransponders));
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, mains.Select(m => m.InvitationalMain));
        Assert.All(mains, m => Assert.Equal(0, m.MainLeg));
        Assert.Equal("Invitational Main 3", mains[2].RoundLabel);
    }

    [Theory]
    [InlineData(5, 4, 4)]
    [InlineData(5, 0, 4)]
    [InlineData(5, 9, 5)]
    [InlineData(12, 10, 10)]
    public void How_many_mains_count(int mains, int counted, int expected) =>
        Assert.Equal(expected, Invitational.Counting(mains, counted));

    [Fact]
    public void Standings_drop_the_worst_main_and_break_ties_on_the_best_finish_then_the_last_main()
    {
        var series = Invitational.BuildMains("Stock", new[] { "a", "b", "c" }, 3, "Club");
        var sessions = new Dictionary<string, SessionRecord>
        {
            ["m1"] = Finish("a", "b", "c"),
            ["m2"] = Finish("b", "c", "a"),
            ["m3"] = Finish("c", "a", "b"),
        };

        for (var i = 0; i < 3; i++)
        {
            series[i].LinkedSessionId = $"m{i + 1}";
        }

        var table = Invitational.Standings(series, id => sessions.GetValueOrDefault(id), new RaceClass { InvitationalMains = 3, InvitationalCounted = 2 })!;

        // Everyone has a 1st and a 2nd counting: 3 points each, and each a
        // win - so the last main decides it: c won it, a was 2nd, b 3rd.
        Assert.True(table.Final);
        Assert.Equal(new[] { "c", "a", "b" }, table.Rows.Select(r => r.Transponder));
        Assert.All(table.Rows, r => Assert.Equal(3, r.Points));
        Assert.Contains("Main 1", table.Rows[0].Detail);
        Assert.Equal("3 pts (best 2 of 3 mains)", table.Rows[0].Words());
    }

    [Fact]
    public void A_driver_who_has_not_started_yet_is_in_the_table_on_the_missed_score()
    {
        var series = Invitational.BuildMains("Stock", new[] { "a", "b", "c" }, 5, "Club");
        series[0].LinkedSessionId = "m1";
        var sessions = new Dictionary<string, SessionRecord> { ["m1"] = Finish("b", "a") };

        var table = Invitational.Standings(series, id => sessions.GetValueOrDefault(id), new RaceClass())!;

        Assert.False(table.Final);
        Assert.Equal(new[] { "b", "a", "c" }, table.Rows.Select(r => r.Transponder));

        // Field of three: a main not started is 4.
        Assert.Equal(4, table.Rows[2].Points);
        Assert.Equal("After main 1 of 5, Bee leads the Invitational on 1 point.", Invitational.Call(table, _ => "Bee"));
    }

    [Fact]
    public void The_class_scale_gives_the_winner_zero()
    {
        var series = Invitational.BuildMains("Stock", new[] { "a", "b" }, 2, "Club");
        series[0].LinkedSessionId = "m1";
        var sessions = new Dictionary<string, SessionRecord> { ["m1"] = Finish("a", "b") };

        var table = Invitational.Standings(series, id => sessions.GetValueOrDefault(id),
            new RaceClass { InvitationalScoring = InvitationalScoring.ClassScale, RoundPointsScale = RoundPointsScale.Ifmar })!;

        Assert.Equal(0, table.Rows[0].Points);
        Assert.Equal(2, table.Rows[1].Points);
    }

    [Fact]
    public void Later_mains_are_gridded_on_points_and_inverted_when_asked()
    {
        var series = Invitational.BuildMains("Stock", new[] { "a", "b", "c" }, 3, "Club");
        series[0].LinkedSessionId = "m1";
        var sessions = new Dictionary<string, SessionRecord> { ["m1"] = Finish("c", "b", "a") };
        var table = Invitational.Standings(series.Take(1).ToList(), id => sessions.GetValueOrDefault(id), new RaceClass())!;

        Assert.Same(series[1], Invitational.NextToGrid(series[0], series));
        Assert.Equal(new[] { "c", "b", "a" }, Invitational.NextGrid(table, series[1].AssignedTransponders, invert: false));
        Assert.Equal(new[] { "a", "b", "c" }, Invitational.NextGrid(table, series[1].AssignedTransponders, invert: true));

        series[1].InvitationalGridSet = true;
        Assert.Null(Invitational.NextToGrid(series[0], series));
    }

    [Fact]
    public void The_final_call_names_the_winner_and_the_count()
    {
        var series = Invitational.BuildMains("Stock", new[] { "a", "b" }, 2, "Club");
        series[0].LinkedSessionId = "m1";
        series[1].LinkedSessionId = "m2";
        var sessions = new Dictionary<string, SessionRecord> { ["m1"] = Finish("a", "b"), ["m2"] = Finish("a", "b") };

        var table = Invitational.Standings(series, id => sessions.GetValueOrDefault(id), new RaceClass { InvitationalMains = 2, InvitationalCounted = 1 })!;

        Assert.Equal("Al wins the Invitational, best one of two mains.", Invitational.Call(table, _ => "Al"));
    }

    [Fact]
    public void The_a_main_legs_are_scored_exactly_as_before()
    {
        var legs = new[] { Finish("a", "b", "c"), Finish("b", "a", "c") };

        var table = AMainLegs.Overall(legs, 2);

        // Level on 3 points, a win each: b won the last leg.
        Assert.Equal(new[] { "b", "a", "c" }, table.Select(r => r.Transponder));
        Assert.Equal("3 pts (2 legs)", table[0].Words());
        Assert.StartsWith("Leg 1 1st (1 pt)", table[1].Detail);
        Assert.Equal(6, table[2].Points);
        Assert.Equal("1 pt (Leg 1)", AMainLegs.Overall(legs.Take(1).ToList(), 1)[0].Words());
    }

    [Fact]
    public void The_names_are_the_ones_settled_for_trademarks()
    {
        Assert.Equal("Pole Shootout", PoleShootout.Name);
        Assert.Equal("Invitational (Reedy-style format)", Invitational.Name);
        Assert.Equal("Invitational", Invitational.ShortName);
    }

    // ---------------------------------------------------------------- lower mains in legs

    private static readonly string[] TwentyQualifiers = Enumerable.Range(1, 20).Select(i => $"q{i}").ToArray();

    [Fact]
    public void Lower_mains_in_legs_are_built_lowest_first_every_leg_of_a_tier_before_the_tier_above()
    {
        var rounds = MainsBuilder.BuildRounds("Stock", MainsBuilder.PlanSeeding(TwentyQualifiers, 8), "Club", 2, aMainLegs: 2, lowerMainsInLegs: true);

        Assert.Equal(
            new[] { "C-Main Leg 1", "C-Main Leg 2", "B-Main Leg 1", "B-Main Leg 2", "A-Main Leg 1", "A-Main Leg 2" },
            rounds.Select(r => r.RoundLabel));
        Assert.Equal(new[] { 2, 2, 1, 1, 0, 0 }, rounds.Select(r => r.MainTier));
        Assert.All(rounds, r => Assert.Equal(2, r.MainLegCount));
        Assert.Equal(TwentyQualifiers.Skip(8).Take(8), rounds[2].AssignedTransponders);
    }

    [Fact]
    public void Off_or_with_a_single_a_main_the_lower_mains_run_once()
    {
        var off = MainsBuilder.BuildRounds("Stock", MainsBuilder.PlanSeeding(TwentyQualifiers, 8), "Club", 2, aMainLegs: 3);
        Assert.Equal(new[] { "C-Main", "B-Main", "A-Main Leg 1", "A-Main Leg 2", "A-Main Leg 3" }, off.Select(r => r.RoundLabel));

        var single = MainsBuilder.BuildRounds("Stock", MainsBuilder.PlanSeeding(TwentyQualifiers, 8), "Club", 2, aMainLegs: 1, lowerMainsInLegs: true);
        Assert.Equal(new[] { "C-Main", "B-Main", "A-Main" }, single.Select(r => r.RoundLabel));
    }

    [Fact]
    public void A_lower_main_bumps_up_on_its_overall_result_into_every_a_leg()
    {
        var rounds = MainsBuilder.BuildRounds("Stock", MainsBuilder.PlanSeeding(TwentyQualifiers.Take(12).ToArray(), 8), "Club", 2, aMainLegs: 2, lowerMainsInLegs: true);

        for (var i = 0; i < rounds.Count; i++)
        {
            rounds[i].Order = i;
        }

        var bLegs = rounds.Where(r => r.MainTier == 1).ToList();
        var aLegs = rounds.Where(r => r.MainTier == 0).ToList();

        // Both legs count: q9 1st + 2nd = 3, q11 3rd + 1st = 4, q10 2nd +
        // 4th = 6, q12 4th + 3rd = 7. q10 was 2nd in leg 1 but is 3rd overall.
        var sessions = new Dictionary<string, SessionRecord>
        {
            ["b1"] = Finish("q9", "q10", "q11", "q12"),
            ["b2"] = Finish("q11", "q9", "q12", "q10"),
        };
        bLegs[0].LinkedSessionId = "b1";
        bLegs[1].LinkedSessionId = "b2";

        var feeder = MainsBuilder.FeederFor(aLegs[0], rounds)!;
        Assert.Same(bLegs[1], feeder);
        Assert.Equal(new[] { bLegs[0], bLegs[1] }, AMainLegs.LegsOf(feeder, rounds));

        var overall = MainsBuilder.FeederResults(feeder, rounds, id => sessions.GetValueOrDefault(id), 2)!;
        Assert.Equal(new[] { "q9", "q11", "q10", "q12" }, overall.Results.Select(r => r.Transponder));

        var plan = MainsBuilder.PlanBumpUp(aLegs[0], overall, null);
        Assert.Equal(new[] { "q9", "q11" }, plan.Movable.Select(p => p.Transponder));

        // Into every A leg at once, as the A legs' bump-ups always went.
        Assert.Equal(aLegs, MainsBuilder.BumpUpTargets(aLegs[0], rounds));

        // With a leg still to run there is no overall to bump from.
        bLegs[1].LinkedSessionId = null;
        Assert.Null(MainsBuilder.FeederResults(feeder, rounds, id => sessions.GetValueOrDefault(id), 2));
    }

    [Fact]
    public void The_last_lower_leg_names_its_own_main_overall()
    {
        var rounds = MainsBuilder.BuildRounds("Stock", MainsBuilder.PlanSeeding(TwentyQualifiers.Take(12).ToArray(), 8), "Club", 2, aMainLegs: 2, lowerMainsInLegs: true);
        var bLegs = rounds.Where(r => r.MainTier == 1).ToList();
        var sessions = new Dictionary<string, SessionRecord> { ["b1"] = Finish("q9", "q10") };
        bLegs[0].LinkedSessionId = "b1";

        var decided = AMainLegs.AfterLastLeg(bLegs[1], Finish("q10", "q9"), rounds, id => sessions.GetValueOrDefault(id), 0)!;

        Assert.Equal(1, decided.Tier);
        Assert.Equal("B", decided.Letter);
        Assert.Equal("Sam wins the B-Main overall, best one of two legs.", AMainLegs.OverallCall("Sam", decided.Counting, decided.Legs, decided.Letter));
        Assert.Contains("last leg of the B-Main", AMainLegs.OverallFact(decided, s => s.DriverName));

        // The A legs are not scored into it.
        Assert.Null(AMainLegs.AfterLastLeg(rounds.First(r => r.MainTier == 0), Finish("q1"), rounds, id => sessions.GetValueOrDefault(id), 0));
    }

    [Fact]
    public void A_class_saved_before_lower_legs_runs_its_lower_mains_once()
    {
        Assert.False(JsonSerializer.Deserialize<RaceClass>("{\"Name\":\"Stock\",\"AMainLegs\":3}")!.LowerMainsInLegs);
        Assert.True(JsonSerializer.Deserialize<RaceClass>(JsonSerializer.Serialize(new RaceClass { LowerMainsInLegs = true }))!.LowerMainsInLegs);
        Assert.Equal("8 per main  ·  top 2 bump up  ·  every main in 3 legs (best 2)",
            RaceClassesControl.DescribeMains(new RaceClass { AMainLegs = 3, LowerMainsInLegs = true }));
    }

    // ---------------------------------------------------------------- Invitational re-grid after a correction

    [Fact]
    public void A_corrected_main_re_grids_the_next_main_while_its_grid_is_as_the_points_gave_it()
    {
        var series = Invitational.BuildMains("Stock", new[] { "a", "b", "c" }, 3, "Club");
        series[0].LinkedSessionId = "m1";

        Assert.True(Invitational.SetGrid(series[1], new[] { "c", "b", "a" }));
        Assert.False(Invitational.SetGrid(series[1], new[] { "c", "b", "a" }));

        // Untouched: offered again, and a new order is taken.
        Assert.Same(series[1], Invitational.NextToGrid(series[0], series));
        Assert.True(Invitational.SetGrid(series[1], new[] { "b", "c", "a" }));
        Assert.Equal(new[] { "b", "c", "a" }, series[1].AssignedTransponders);

        // On track now: left alone.
        Assert.Null(Invitational.NextToGrid(series[0], series, onTrackId: series[1].Id));

        // Changed by hand: the desk's grid stands.
        series[1].AssignedTransponders = new List<string> { "a", "b", "c" };
        Assert.Null(Invitational.NextToGrid(series[0], series));

        // Raced: the main after it is next.
        series[1].LinkedSessionId = "m2";
        Assert.Same(series[2], Invitational.NextToGrid(series[0], series));
    }

    [Fact]
    public void A_grid_set_before_the_grid_was_remembered_is_left_alone()
    {
        var series = Invitational.BuildMains("Stock", new[] { "a", "b" }, 2, "Club");
        series[0].LinkedSessionId = "m1";
        series[1].InvitationalGridSet = true;

        Assert.Null(Invitational.NextToGrid(series[0], series));
        Assert.Null(JsonSerializer.Deserialize<RoundDefinition>("{\"InvitationalMain\":2,\"InvitationalGridSet\":true}")!.InvitationalGridAsSet);
    }

    // ---------------------------------------------------------------- saved files

    [Fact]
    public void A_class_saved_before_this_loads_with_both_formats_off()
    {
        var old = JsonSerializer.Deserialize<RaceClass>("{\"Name\":\"Stock\",\"MainSize\":8,\"AMainLegs\":3}")!;

        Assert.False(old.PoleShootout);
        Assert.False(old.Invitational);
        Assert.Equal(5, old.PoleShootoutDrivers);
        Assert.Equal(1, old.PoleShootoutLaps);
        Assert.Equal(5, old.InvitationalMains);
        Assert.Equal(4, old.InvitationalCounted);
        Assert.Equal(InvitationalScoring.Reedy, old.InvitationalScoring);
        Assert.Null(old.InvitationalSeed);
    }

    [Fact]
    public void A_round_saved_before_this_is_not_a_shootout_run_or_an_invitational_main()
    {
        var old = JsonSerializer.Deserialize<RoundDefinition>("{\"RoundLabel\":\"A-Main\",\"Kind\":1,\"MainTier\":0}")!;

        Assert.Equal(RoundKind.Main, old.Kind);
        Assert.Equal(0, old.ShootoutRun);
        Assert.Equal(0, old.InvitationalMain);
        Assert.False(old.ShootoutGridSet);
        Assert.False(old.InvitationalGridSet);
    }

    [Fact]
    public void The_new_kinds_and_settings_round_trip_and_keep_their_numbers()
    {
        // Saved as numbers: the old values keep theirs, the new ones go at the end.
        Assert.Equal(0, (int)RoundKind.Qualifier);
        Assert.Equal(1, (int)RoundKind.Main);
        Assert.Equal(2, (int)RoundKind.Practice);
        Assert.Equal(3, (int)RoundKind.Shootout);
        Assert.Equal(0, (int)InvitationalScoring.Reedy);
        Assert.Equal(1, (int)InvitationalScoring.ClassScale);

        var run = PoleShootout.BuildRuns("Stock", new[] { "q1", "q2" }, 2, null, "Club")[0];
        var back = JsonSerializer.Deserialize<RoundDefinition>(JsonSerializer.Serialize(run))!;
        Assert.Equal(RoundKind.Shootout, back.Kind);
        Assert.Equal(1, back.ShootoutRun);
        Assert.Equal(2, back.ShootoutRunCount);
        Assert.Equal(3, back.FormatOverride!.LapLimit);

        var raceClass = new RaceClass
        {
            Name = "Stock",
            PoleShootout = true,
            PoleShootoutDrivers = 6,
            Invitational = true,
            InvitationalMains = 7,
            InvitationalCounted = 5,
            InvitationalInvertGrid = true,
            InvitationalScoring = InvitationalScoring.ClassScale,
            InvitationalSeed = new List<string> { "b", "a" },
        };
        var loaded = JsonSerializer.Deserialize<RaceClass>(JsonSerializer.Serialize(raceClass))!;
        Assert.True(loaded.PoleShootout && loaded.Invitational && loaded.InvitationalInvertGrid);
        Assert.Equal(6, loaded.PoleShootoutDrivers);
        Assert.Equal(7, loaded.InvitationalMains);
        Assert.Equal(5, loaded.InvitationalCounted);
        Assert.Equal(InvitationalScoring.ClassScale, loaded.InvitationalScoring);
        Assert.Equal(new[] { "b", "a" }, loaded.InvitationalSeed);
    }

    [Fact]
    public void The_seed_rows_put_the_set_order_first_then_the_rest_of_the_class()
    {
        var rows = FinalsFormatsForm.InvitationalSeedRows(
            new[] { ("a", "Al"), ("b", "Bo"), ("c", "Cy") },
            new[] { "c", "gone" });

        Assert.Equal(new[] { "c", "gone", "a", "b" }, rows.Select(r => r.Transponder));
        Assert.Equal("Cy", rows[0].Name);
        Assert.Equal("gone", rows[1].Name);
    }

    [Fact]
    public void The_classes_page_says_what_the_mains_are()
    {
        Assert.Equal("Pole Shootout (top 5)  ·  Invitational: 5 mains, best 4 count",
            RaceClassesControl.DescribeMains(new RaceClass { PoleShootout = true, Invitational = true }));
        Assert.Equal("8 per main  ·  top 2 bump up", RaceClassesControl.DescribeMains(new RaceClass()));
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>A shootout run's result: the start crossing, then one timed lap - or no time at all.</summary>
    private static SessionRecord Run(string transponder, long lapMs) => new()
    {
        SessionType = RaceSessionType.Race,
        Results = new List<SessionResultEntry>
        {
            new()
            {
                Position = 1,
                Transponder = transponder,
                DriverName = transponder,
                Laps = lapMs > 0 ? 2 : 1,
                LapTimesMs = lapMs > 0 ? new List<long> { 0, lapMs } : new List<long> { 0 },
            },
        },
    };

    // The Seed Order window (look-back, 19 Nov 2026): each row numbered, so
    // names cut short still tell apart, the numbers following a move; and
    // as wide as a long name needs, up to a cap.
    [Fact]
    public void The_seed_order_numbers_its_rows_and_keeps_them_numbered_after_a_move()
    {
        var rows = StaThread.Run(() =>
        {
            using var form = new FinalsFormatsForm.SeedOrderForm("Stock Buggy", new List<(string, string)> { ("T1", "Ann"), ("T2", "Bob"), ("T3", "Cat") });
            var before = form.RowsShown.ToList();
            form.MoveForTests(1);
            return (before, after: form.RowsShown.ToList(), order: form.Order.ToList());
        }, TimeSpan.FromSeconds(60), () => "building the seed order");

        Assert.Equal(new[] { "1. Ann", "2. Bob", "3. Cat" }, rows.before);
        Assert.Equal(new[] { "1. Bob", "2. Ann", "3. Cat" }, rows.after);
        Assert.Equal(new[] { "T2", "T1", "T3" }, rows.order);
    }

    [Fact]
    public void The_seed_order_window_widens_for_long_names_up_to_a_cap()
    {
        var widths = StaThread.Run(() =>
        {
            int Width(string name)
            {
                using var form = new FinalsFormatsForm.SeedOrderForm("Stock Buggy", new List<(string, string)> { ("T1", name) });
                form.Show();
                StaThread.Settle();
                var width = form.ClientSize.Width;
                form.Close();
                return width;
            }

            return (shortName: Width("Ann"), longName: Width("Alexandria Montgomery-Fitzwilliam the Third"), huge: Width(new string('W', 200)));
        }, TimeSpan.FromSeconds(60), () => "sizing the seed order");

        Assert.True(widths.longName > widths.shortName, $"{widths.longName} vs {widths.shortName}");
        Assert.True(widths.huge <= UiLayout.AtTextSize(640), $"{widths.huge}");
    }

    /// <summary>A main's finishing order, every car with laps.</summary>
    private static SessionRecord Finish(params string[] order) => new()
    {
        SessionType = RaceSessionType.Race,
        Results = order.Select((t, i) => new SessionResultEntry
        {
            Position = i + 1,
            Transponder = t,
            DriverName = t,
            Laps = 20 - i,
            ElapsedMs = 300_000,
        }).ToList(),
    };
}
