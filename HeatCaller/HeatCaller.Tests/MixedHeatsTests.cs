using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// Mixed-class heats (11 Nov 2026, race-night build plan, Phase 4.1): small
/// classes sharing a race and scored apart - qualifying, points qualifying,
/// mains seeding, championships, the event report's class tables, the
/// announcer's class winners, Run Together and its undo, the build's option,
/// the curfew planner's suggestion, and old files loading unchanged.
/// </summary>
public sealed class MixedHeatsTests : IDisposable
{
    // Declared first: the stores below load from wherever it points.
    private readonly TempAppData _data = new();

    private readonly RoundDefinitionStore _rounds = RoundDefinitionStore.Load();
    private readonly ProfileStore _profiles = ProfileStore.Load();
    private readonly List<SessionRecord> _sessions = new();
    private DateTime _clock = new(2026, 11, 11, 18, 0, 0, DateTimeKind.Utc);

    private const string Stock = "Stock";
    private const string Mod = "Mod";

    public void Dispose() => _data.Dispose();

    // ------------------------------------------------------------------
    // The model, and old files.
    // ------------------------------------------------------------------

    [Fact]
    public void An_old_round_file_loads_as_one_class_rounds_and_saves_back_the_same()
    {
        // Written by a build from before 11 Nov 2026: no SharedClasses, no SharedClassOf.
        const string old = """
            [
              { "Id": "r1", "Order": 0, "ClassName": "Stock", "RoundLabel": "Q1 Heat 1", "Kind": 0,
                "TrackLayoutName": "", "AssignedTransponders": ["A", "B"], "LinkedSessionId": null,
                "MainTier": -1, "BumpUpCount": 0, "BumpUpsApplied": false }
            ]
            """;
        File.WriteAllText(_data.PathOf("race-rounds.json"), old);

        var store = RoundDefinitionStore.Load();
        var round = Assert.Single(store.All);

        Assert.Null(round.SharedClasses);
        Assert.Null(round.SharedClassOf);
        Assert.False(MixedHeats.IsShared(round));
        Assert.Equal(new[] { Stock }, MixedHeats.ClassesOf(round));
        Assert.Equal(Stock, MixedHeats.ClassLabel(round));
        Assert.Single(store.ForClass(Stock));
        Assert.Empty(store.ForClass(Mod));

        // Saved and read back: still one class, still the same roster.
        store.MarkRecorded("r1", "s1");
        var again = Assert.Single(RoundDefinitionStore.Load().All);
        Assert.False(MixedHeats.IsShared(again));
        Assert.Equal(new[] { "A", "B" }, again.AssignedTransponders);
        Assert.Equal("s1", again.LinkedSessionId);
    }

    [Fact]
    public void An_old_result_has_no_class_and_scores_as_one_field()
    {
        const string old = """{ "Position": 1, "Transponder": "A", "DriverName": "A", "Laps": 10 }""";
        var result = JsonSerializer.Deserialize<SessionResultEntry>(old)!;

        Assert.Null(result.ClassName);
        var fields = MixedHeats.ScoringFields(new[] { result }, Stock);
        Assert.Single(fields);
        Assert.Single(fields[0]);
    }

    [Fact]
    public void A_shared_round_round_trips_through_the_store()
    {
        var round = SharedHeat("Q1 Heat 1", stock: new[] { "S1", "S2" }, mod: new[] { "M1" });

        var loaded = RoundDefinitionStore.Load().Get(round.Id)!;

        Assert.True(MixedHeats.IsShared(loaded));
        Assert.Equal("Stock + Mod", MixedHeats.ClassLabel(loaded));
        Assert.Equal("Stock and Mod", MixedHeats.SpokenClasses(loaded));
        Assert.Equal(Mod, MixedHeats.ClassOfDriver(loaded, "M1"));
        Assert.Equal(Stock, MixedHeats.ClassOfDriver(loaded, "S2"));
    }

    [Fact]
    public void A_shared_heat_is_one_of_each_of_its_classes_rounds()
    {
        var shared = SharedHeat("Q1 Heat 1", stock: new[] { "S1" }, mod: new[] { "M1" });
        var stockOnly = Heat(Stock, "Q1 Heat 2", "S2");

        Assert.Equal(new[] { shared.Id, stockOnly.Id }, _rounds.ForClass(Stock).Select(r => r.Id));
        Assert.Equal(new[] { shared.Id }, _rounds.ForClass(Mod).Select(r => r.Id));
        Assert.True(MixedHeats.RunsClass(shared, "mod"));
        Assert.False(MixedHeats.RunsClass(stockOnly, Mod));
    }

    [Fact]
    public void A_driver_with_no_record_is_placed_by_their_profile_then_the_main_class()
    {
        var round = new RoundDefinition { ClassName = Stock, SharedClasses = new() { Mod }, AssignedTransponders = new() { "P", "Q" } };
        _profiles.Upsert(new DriverProfile { Transponder = "P", DriverName = "Pat", ClassName = Mod });

        Assert.Equal(Mod, MixedHeats.ClassOfDriver(round, "P", _profiles));
        Assert.Equal(Stock, MixedHeats.ClassOfDriver(round, "Q", _profiles)); // no profile: the main class
        Assert.Equal(Stock, MixedHeats.ClassOfDriver(round, "P"));            // no profiles to hand: the main class
    }

    [Fact]
    public void A_driver_in_both_classes_counts_only_for_the_class_they_ran_in()
    {
        // Pat races Mod and Stock on their profile, and tonight ran the Stock car in the shared heat.
        _profiles.Upsert(new DriverProfile { Transponder = "P", DriverName = "Pat", ClassName = Mod, Classes = new() { Stock } });

        var round = SharedHeat("Q1 Heat 1", stock: new[] { "P", "S1" }, mod: new[] { "M1" });
        Record(round, R("M1", 15, 300_000), R("P", 14, 300_000), R("S1", 13, 300_000));

        Assert.Equal(new[] { "P", "S1" }, Ids(Compute(Stock)));
        Assert.Equal(new[] { "M1" }, Ids(Compute(Mod)));

        // Without the heat's record, their profile's main class would have put them in Mod.
        round.SharedClassOf!.Remove("P");
        Assert.Equal(Mod, MixedHeats.ClassOfDriver(round, "P", _profiles));
    }

    // ------------------------------------------------------------------
    // Scoring.
    // ------------------------------------------------------------------

    [Fact]
    public void Each_class_is_qualified_from_its_own_drivers_in_a_shared_heat()
    {
        var shared = SharedHeat("Q1 Heat 1", stock: new[] { "S1", "S2", "S3" }, mod: new[] { "M1", "M2", "M3", "M4" });
        Record(shared,
            R("M1", 18, 300_000), R("M2", 17, 300_000), R("S1", 16, 300_000), R("M3", 15, 300_000),
            R("S2", 14, 300_000), R("M4", 13, 300_000), R("S3", 12, 300_000));

        var stock = Compute(Stock);
        var mod = Compute(Mod);

        Assert.Equal(new[] { "S1", "S2", "S3" }, Ids(stock));
        Assert.Equal(new[] { "M1", "M2", "M3", "M4" }, Ids(mod));
        Assert.All(stock, e => Assert.Equal(1, e.HeatsRun));
    }

    [Fact]
    public void A_one_class_heat_still_counts_every_result()
    {
        var heat = Heat(Stock, "Q1 Heat 1", "S1", "S2");
        Record(heat, R("S1", 10, 300_000), R("X", 11, 300_000), R("S2", 9, 300_000));

        // "X" is not on the roster - it never was a filter - and still counts, as always.
        Assert.Equal(new[] { "X", "S1", "S2" }, Ids(Compute(Stock)));
    }

    [Fact]
    public void Points_qualifying_ranks_each_class_within_itself_with_its_own_field_size()
    {
        var points = new RaceClass { Name = Stock, QualifyingMethod = QualifyingMethod.RoundPoints, CountBestRounds = 2 };

        var q1 = SharedHeat("Q1 Heat 1", stock: new[] { "S1", "S2" }, mod: new[] { "M1", "M2", "M3" });
        Record(q1, R("M1", 18, 300_000), R("S2", 17, 300_000), R("M2", 16, 300_000), R("S1", 15, 300_000), R("M3", 14, 300_000));

        var q2 = SharedHeat("Q2 Heat 1", stock: new[] { "S1" }, mod: new[] { "M1" });     // S2 missed Q2
        Record(q2, R("M1", 18, 300_000), R("S1", 17, 300_000));

        var stock = Compute(Stock, points);

        // Stock's field is 2, so a missed round is 3 points - not 6 from the whole shared grid.
        var s1 = stock.Single(e => e.Transponder == "S1");
        var s2 = stock.Single(e => e.Transponder == "S2");
        Assert.Equal(2 + 1, s1.Points);
        Assert.Equal(1 + 3, s2.Points);
        Assert.Equal(new[] { "S1", "S2" }, Ids(stock));
    }

    [Fact]
    public void Live_projection_of_a_shared_heat_counts_only_the_class_asked_for()
    {
        var shared = SharedHeat("Q1 Heat 1", stock: new[] { "S1" }, mod: new[] { "M1" });

        var live = new QualifyingCalculator.LiveHeat(shared.Id, shared.RoundLabel, _clock, new[]
        {
            new QualifyingCalculator.LiveResult("M1", "M1", "", 12, 300_000, 20_000, Array.Empty<long>()),
            new QualifyingCalculator.LiveResult("S1", "S1", "", 11, 300_000, 21_000, Array.Empty<long>()),
        });

        Assert.Equal(new[] { "S1" }, Ids(QualifyingCalculator.Compute(Stock, _rounds, History(), _profiles, null, null, live)));
        Assert.Equal(new[] { "M1" }, Ids(QualifyingCalculator.Compute(Mod, _rounds, History(), _profiles, null, null, live)));
    }

    [Fact]
    public void Mains_are_seeded_per_class_from_a_shared_qualifier()
    {
        var shared = SharedHeat("Q1 Heat 1", stock: new[] { "S1", "S2", "S3" }, mod: new[] { "M1", "M2" });
        Record(shared, R("M1", 18, 300_000), R("S3", 17, 300_000), R("M2", 16, 300_000), R("S1", 15, 300_000), R("S2", 14, 300_000));

        var stockMains = MainsBuilder.BuildRounds(Stock, MainsBuilder.PlanSeeding(Ids(Compute(Stock)), 10), "", 0);
        var modMains = MainsBuilder.BuildRounds(Mod, MainsBuilder.PlanSeeding(Ids(Compute(Mod)), 10), "", 0);

        var stockA = Assert.Single(stockMains);
        Assert.Equal(Stock, stockA.ClassName);
        Assert.False(MixedHeats.IsShared(stockA));
        Assert.Equal(new[] { "S3", "S1", "S2" }, stockA.AssignedTransponders);
        Assert.Equal(new[] { "M1", "M2" }, Assert.Single(modMains).AssignedTransponders);
    }

    [Fact]
    public void Results_are_stamped_with_each_drivers_class_and_only_for_a_shared_heat()
    {
        var shared = SharedHeat("Q1 Heat 1", stock: new[] { "S1" }, mod: new[] { "M1" });
        var results = new List<SessionResultEntry> { R("M1", 10, 1), R("S1", 9, 1) };
        MixedHeats.StampClasses(results, shared);
        Assert.Equal(new[] { Mod, Stock }, results.Select(r => r.ClassName));

        var plain = new List<SessionResultEntry> { R("S1", 9, 1) };
        MixedHeats.StampClasses(plain, Heat(Stock, "Q1 Heat 2", "S1"));
        Assert.Null(plain[0].ClassName);
    }

    [Fact]
    public void Class_results_close_up_and_renumber_within_the_class()
    {
        var shared = SharedHeat("Q1 Heat 1", stock: new[] { "S1", "S2" }, mod: new[] { "M1", "M2" });
        var results = new[] { R("M1", 10, 1, 1), R("S1", 9, 1, 2), R("M2", 8, 1, 3), R("S2", 7, 1, 4) };

        var stock = MixedHeats.ClassResults(results, shared, Stock);

        Assert.Equal(new[] { "S1", "S2" }, stock.Select(r => r.Transponder));
        Assert.Equal(new[] { 1, 2 }, stock.Select(r => r.Position));
        Assert.Equal(2, results[1].Position); // the saved result is untouched
    }

    // ------------------------------------------------------------------
    // Championships.
    // ------------------------------------------------------------------

    [Fact]
    public void A_class_championship_scores_only_its_own_drivers_from_a_shared_race()
    {
        var shared = SharedHeat("Q1 Heat 1", stock: new[] { "S1", "S2" }, mod: new[] { "M1", "M2" });
        var session = Record(shared, R("M1", 18, 1, 1), R("S1", 17, 1, 2), R("M2", 16, 1, 3), R("S2", 15, 1, 4));
        MixedHeats.StampClasses(session.Results, shared);

        var season = new Season { Name = "Stock series", ClassName = Stock, AutoInclude = true };
        var table = SeasonStore.ComputeStandings(season, new[] { session });

        Assert.Equal(new[] { "S1", "S2" }, table.Select(e => e.DriverName));
        Assert.Equal(10, table[0].TotalPoints); // first in Stock, not second overall
        Assert.Equal(9, table[1].TotalPoints);
        Assert.Equal(1, table[0].Wins);
    }

    [Fact]
    public void An_every_class_championship_scores_each_class_of_a_shared_race_on_its_own()
    {
        var shared = SharedHeat("Q1 Heat 1", stock: new[] { "S1" }, mod: new[] { "M1", "M2" });
        var session = Record(shared, R("M1", 18, 1, 1), R("M2", 17, 1, 2), R("S1", 16, 1, 3));
        MixedHeats.StampClasses(session.Results, shared);

        var table = SeasonStore.ComputeStandings(new Season { Name = "Club", AutoInclude = true }, new[] { session });

        Assert.Equal(10, table.Single(e => e.DriverName == "M1").TotalPoints);
        Assert.Equal(9, table.Single(e => e.DriverName == "M2").TotalPoints);
        Assert.Equal(10, table.Single(e => e.DriverName == "S1").TotalPoints); // won Stock
    }

    [Fact]
    public void An_unshared_race_scores_exactly_as_before()
    {
        var heat = Heat(Stock, "Q1 Heat 1", "S1", "S2");
        var session = Record(heat, R("S1", 18, 1, 1), R("S2", 17, 1, 2));

        var table = SeasonStore.ComputeStandings(new Season { Name = "Stock", ClassName = Stock }, new[] { session });

        Assert.Equal(new[] { 10, 9 }, table.Select(e => e.TotalPoints));
    }

    [Fact]
    public void A_class_championship_takes_a_race_its_class_shared()
    {
        var shared = SharedHeat("Q1 Heat 1", stock: new[] { "S1" }, mod: new[] { "M1" });
        var session = Record(shared, R("M1", 18, 1, 1), R("S1", 17, 1, 2));

        Assert.True(EventSpine.RuleAccepts(new Season { ClassName = Mod, AutoInclude = true, MainsOnly = false }, session, _rounds, null));
        Assert.True(EventSpine.RuleAccepts(new Season { ClassName = Stock, AutoInclude = true, MainsOnly = false }, session, _rounds, null));
        Assert.False(EventSpine.RuleAccepts(new Season { ClassName = "Buggy", AutoInclude = true, MainsOnly = false }, session, _rounds, null));
    }

    // ------------------------------------------------------------------
    // The announcer and the recap.
    // ------------------------------------------------------------------

    [Fact]
    public void The_flag_names_each_classes_winner_in_a_shared_race_only()
    {
        var shared = SharedHeat("Q1 Heat 1", stock: new[] { "S1", "S2" }, mod: new[] { "M1" });
        var map = MixedHeats.ClassMapFor(shared);

        Assert.Equal("Mick wins Mod, Sam wins Stock.",
            MixedHeats.ClassWinnersLine(new[] { "M1", "S1", "S2" }, map, t => t == "M1" ? "Mick" : "Sam"));

        Assert.Null(MixedHeats.ClassMapFor(Heat(Stock, "Q1 Heat 2", "S1")));
        Assert.Null(MixedHeats.ClassWinnersLine(new[] { "S1" }, null, t => t));
    }

    [Fact]
    public void Class_places_are_counted_within_each_class()
    {
        var shared = SharedHeat("Q1 Heat 1", stock: new[] { "S1", "S2" }, mod: new[] { "M1" });

        var places = MixedHeats.ClassPlaces(new[] { ("M1", 1), ("S1", 2), ("S2", 3) }, shared);

        Assert.Equal(1, places["M1"]);
        Assert.Equal(1, places["S1"]);
        Assert.Equal(2, places["S2"]);
        Assert.Empty(MixedHeats.ClassPlaces(new[] { ("S1", 1) }, Heat(Stock, "Q1 Heat 2", "S1")));
    }

    [Fact]
    public void The_recap_is_told_each_classes_top_three_only_for_a_stamped_race()
    {
        var results = new[] { R("M1", 10, 1, 1), R("S1", 9, 1, 2) };
        results[0].ClassName = Mod;
        results[1].ClassName = Stock;

        var fact = MixedHeats.ClassWinnersFact(results, t => t);

        Assert.Contains("Mod: 1st M1.", fact);
        Assert.Contains("Stock: 1st S1.", fact);
        Assert.Null(MixedHeats.ClassWinnersFact(new[] { R("S1", 9, 1, 1) }, t => t));
    }

    // ------------------------------------------------------------------
    // Run Together, and its undo.
    // ------------------------------------------------------------------

    [Fact]
    public void Run_together_makes_one_shared_heat_and_undo_puts_both_back_exactly()
    {
        var stock = Heat(Stock, "Q1 Heat 1", "S1", "S2", "S3");
        var mod = Heat(Mod, "Q1 Heat 1", "M1", "M2", "M3", "M4");
        var stockBefore = JsonSerializer.Serialize(stock);
        var modBefore = JsonSerializer.Serialize(mod);

        var undo = MixedHeats.RunTogetherIn(_rounds, mod, stock, _profiles, null, out var refusal);

        Assert.Null(refusal);
        Assert.NotNull(undo);
        var merged = Assert.Single(_rounds.All);
        Assert.Equal(stock.Id, merged.Id);                // the earlier heat keeps its slot and name
        Assert.Equal("Stock + Mod", MixedHeats.ClassLabel(merged));
        Assert.Equal(7, merged.AssignedTransponders.Count);
        Assert.Equal(Mod, MixedHeats.ClassOfDriver(merged, "M3"));
        Assert.Equal(Stock, MixedHeats.ClassOfDriver(merged, "S3"));

        Assert.True(MixedHeats.UndoRunTogether(_rounds, undo!, out var notUndone));
        Assert.Null(notUndone);

        Assert.Equal(2, _rounds.All.Count);
        Assert.Equal(stockBefore, JsonSerializer.Serialize(_rounds.Get(stock.Id)));
        Assert.Equal(modBefore, JsonSerializer.Serialize(_rounds.Get(mod.Id)));
        Assert.Equal(new[] { stock.Id, mod.Id }, _rounds.All.Select(r => r.Id));
    }

    [Fact]
    public void Undo_of_run_together_is_refused_once_the_shared_race_has_run()
    {
        var stock = Heat(Stock, "Q1 Heat 1", "S1");
        var mod = Heat(Mod, "Q1 Heat 1", "M1");
        var undo = MixedHeats.RunTogetherIn(_rounds, stock, mod, null, null, out _)!;

        _rounds.MarkRecorded(stock.Id, "race");

        Assert.False(MixedHeats.UndoRunTogether(_rounds, undo, out var refusal));
        Assert.Contains("cannot be split", refusal);
        Assert.Single(_rounds.All);
    }

    [Fact]
    public void Run_together_refuses_what_cannot_share_a_race()
    {
        var stock = Heat(Stock, "Q1 Heat 1", "S1");
        var stock2 = Heat(Stock, "Q1 Heat 2", "S2");
        var modQ2 = Heat(Mod, "Q2 Heat 1", "M1");
        var modSameDriver = Heat(Mod, "Q1 Heat 1", "S1");
        var run = Heat(Mod, "Q1 Heat 1", "M2");
        _rounds.MarkRecorded(run.Id, "x");
        var main = new RoundDefinition { ClassName = Mod, RoundLabel = "A-Main", Kind = RoundKind.Main, AssignedTransponders = new() { "M3" } };
        var onTrack = Heat(Mod, "Q1 Heat 1", "M4");

        Assert.Contains("share a class", MixedHeats.WhyNotTogether(stock, stock2));
        Assert.Contains("different qualifying rounds", MixedHeats.WhyNotTogether(stock, modQ2));
        Assert.Contains("in both heats", MixedHeats.WhyNotTogether(stock, modSameDriver));
        Assert.Contains("already been run", MixedHeats.WhyNotTogether(stock, _rounds.Get(run.Id)!));
        Assert.Contains("Mains", MixedHeats.WhyNotTogether(stock, main));
        Assert.Contains("on track", MixedHeats.WhyNotTogether(stock, onTrack, onTrack.Id));

        var count = _rounds.All.Count;
        Assert.Null(MixedHeats.RunTogetherIn(_rounds, stock, stock2, null, null, out var refusal));
        Assert.NotNull(refusal);
        Assert.Equal(count, _rounds.All.Count);
    }

    [Fact]
    public void A_shared_heat_can_take_a_third_class()
    {
        var stock = Heat(Stock, "Q1 Heat 1", "S1");
        var mod = Heat(Mod, "Q1 Heat 1", "M1");
        var buggy = Heat("Buggy", "Q1 Heat 1", "B1");

        MixedHeats.RunTogetherIn(_rounds, stock, mod, null, null, out _);
        MixedHeats.RunTogetherIn(_rounds, _rounds.Get(stock.Id)!, buggy, null, null, out var refusal);

        Assert.Null(refusal);
        var merged = Assert.Single(_rounds.All);
        Assert.Equal("Stock + Mod + Buggy", MixedHeats.ClassLabel(merged));
        Assert.Equal("Stock, Mod and Buggy", MixedHeats.SpokenClasses(merged));
        Assert.Equal("Buggy", MixedHeats.ClassOfDriver(merged, "B1"));
    }

    [Fact]
    public void Renaming_a_class_renames_it_where_it_shares_a_heat_and_undo_puts_it_back()
    {
        var shared = SharedHeat("Q1 Heat 1", stock: new[] { "S1" }, mod: new[] { "M1" });
        var leave = _rounds.WithClassName("Modified");

        _rounds.RenameClass(Mod, "Modified");
        var renamed = _rounds.Get(shared.Id)!;
        Assert.Equal("Stock + Modified", MixedHeats.ClassLabel(renamed));
        Assert.Equal("Modified", MixedHeats.ClassOfDriver(renamed, "M1"));
        Assert.Single(_rounds.ForClass("Modified"));

        _rounds.UndoRenameClass("Modified", Mod, leave);
        Assert.Equal("Stock + Mod", MixedHeats.ClassLabel(_rounds.Get(shared.Id)!));
        Assert.Equal(Mod, MixedHeats.ClassOfDriver(_rounds.Get(shared.Id)!, "M1"));
    }

    [Fact]
    public void A_rebuild_of_either_class_clears_an_unrun_shared_heat()
    {
        var shared = SharedHeat("Q1 Heat 1", stock: new[] { "S1" }, mod: new[] { "M1" });

        _rounds.ClearUnrecordedQualifiers(Mod);

        Assert.Null(_rounds.Get(shared.Id));
    }

    // ------------------------------------------------------------------
    // The build, and the curfew planner.
    // ------------------------------------------------------------------

    [Fact]
    public void The_build_puts_small_classes_together_when_asked_and_not_otherwise()
    {
        var heats = new List<RoundDefinition>
        {
            Built(Stock, "S1", "S2", "S3"),
            Built("Buggy", "B1", "B2", "B3", "B4", "B5", "B6", "B7", "B8"),
            Built(Mod, "M1", "M2", "M3", "M4"),
        };

        Assert.Equal(3, MixedHeats.CombineSmallClasses(heats, 0, 8).Count); // off

        var combined = MixedHeats.CombineSmallClasses(heats.Select(MixedHeats.Snapshot).ToList(), 5, 8);

        Assert.Equal(2, combined.Count);
        Assert.Equal("Stock + Mod", MixedHeats.ClassLabel(combined[0]));
        Assert.Equal(7, combined[0].AssignedTransponders.Count);
        Assert.Equal(Mod, MixedHeats.ClassOfDriver(combined[0], "M4"));
        Assert.False(MixedHeats.IsShared(combined[1]));
    }

    [Fact]
    public void The_build_never_combines_past_the_heat_size_or_a_class_split_over_two_heats_or_a_shared_driver()
    {
        // 4 + 4 does not fit a heat of 6.
        Assert.Equal(2, MixedHeats.CombineSmallClasses(new List<RoundDefinition> { Built(Stock, "S1", "S2", "S3", "S4"), Built(Mod, "M1", "M2", "M3", "M4") }, 5, 6).Count);

        // Stock already needs two heats this round.
        Assert.Equal(3, MixedHeats.CombineSmallClasses(new List<RoundDefinition> { Built(Stock, "S1", "S2"), Built(Stock, "S3", "S4"), Built(Mod, "M1") }, 5, 8).Count);

        // One driver entered in both small classes.
        Assert.Equal(2, MixedHeats.CombineSmallClasses(new List<RoundDefinition> { Built(Stock, "P", "S1"), Built(Mod, "P", "M1") }, 5, 8).Count);
    }

    [Fact]
    public void The_curfew_planner_suggests_running_two_small_classes_together()
    {
        var rounds = new List<RoundDefinition>
        {
            Built(Stock, "S1", "S2", "S3"), Built(Mod, "M1", "M2", "M3", "M4"),
        };
        rounds[0].RoundLabel = "Q1 Heat 1";
        rounds[1].RoundLabel = "Q1 Heat 1";
        rounds.Add(Relabel(Built(Stock, "S1", "S2", "S3"), "Q2 Heat 1"));
        rounds.Add(Relabel(Built(Mod, "M1", "M2", "M3", "M4"), "Q2 Heat 1"));

        var pairs = MixedHeats.ShareableClasses(rounds, 8);
        var (a, b, saved) = Assert.Single(pairs);
        Assert.Equal(Stock, a);
        Assert.Equal(Mod, b);
        Assert.Equal(2, saved);

        // On the plan: well past the out-by time, the fix is offered.
        var now = new DateTime(2026, 11, 11, 19, 0, 0, DateTimeKind.Utc);
        var plan = NightPlan.For(rounds, _ => TimeSpan.FromMinutes(5), null, null, null, null, now.AddMinutes(1).ToLocalTime().TimeOfDay, now)!;
        Assert.Contains(plan.Fixes, f => f.Text.StartsWith("run Stock and Mod together", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------

    private RoundDefinition Heat(string className, string label, params string[] transponders)
    {
        var round = new RoundDefinition { ClassName = className, RoundLabel = label, Kind = RoundKind.Qualifier, AssignedTransponders = transponders.ToList() };
        _rounds.Add(round);
        return round;
    }

    private static RoundDefinition Built(string className, params string[] transponders) =>
        new() { ClassName = className, RoundLabel = "Q1 Heat 1", Kind = RoundKind.Qualifier, AssignedTransponders = transponders.ToList() };

    private static RoundDefinition Relabel(RoundDefinition round, string label)
    {
        round.RoundLabel = label;
        return round;
    }

    private RoundDefinition SharedHeat(string label, string[] stock, string[] mod)
    {
        var round = new RoundDefinition
        {
            ClassName = Stock,
            SharedClasses = new() { Mod },
            SharedClassOf = stock.Select(t => (t, Stock)).Concat(mod.Select(t => (t, Mod)))
                .ToDictionary(x => x.t, x => x.Item2, StringComparer.OrdinalIgnoreCase),
            RoundLabel = label,
            Kind = RoundKind.Qualifier,
            AssignedTransponders = stock.Concat(mod).ToList(),
        };
        _rounds.Add(round);
        return round;
    }

    private SessionRecord Record(RoundDefinition round, params SessionResultEntry[] results)
    {
        _clock = _clock.AddMinutes(10);

        var session = new SessionRecord
        {
            Label = round.RoundLabel,
            SessionType = RaceSessionType.Race,
            RoundId = round.Id,
            StartedAtUtc = _clock,
            Results = results.ToList(),
        };

        _sessions.Add(session);
        Assert.True(_rounds.MarkRecorded(round.Id, session.Id));
        return session;
    }

    private static SessionResultEntry R(string transponder, int laps, long elapsedMs, int position = 0) => new()
    {
        Position = position,
        Transponder = transponder,
        DriverName = transponder,
        Laps = laps,
        ElapsedMs = elapsedMs,
        FastLapMs = laps > 0 ? Math.Max(1, elapsedMs / laps) : 0,
    };

    private SessionHistoryStore History() => SessionHistoryStore.FromSessions(_sessions);

    private static string[] Ids(IEnumerable<QualifyingCalculator.Entry> entries) => entries.Select(e => e.Transponder).ToArray();

    private List<QualifyingCalculator.Entry> Compute(string className, RaceClass? raceClass = null) =>
        QualifyingCalculator.Compute(className, _rounds, History(), _profiles, raceClass);
}
