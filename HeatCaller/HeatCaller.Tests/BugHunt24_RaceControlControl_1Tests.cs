namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), Race Control: the new-driver welcome, the tech
/// warning in a shared heat, the Big Screen's championship card, the gap's
/// "race just run", the lap strip for a credited start, and putting skipped
/// drivers back on the grid.
/// </summary>
public sealed class BugHunt24_RaceControlControl_1Tests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- F226: a new driver on their second transponder ---------------------

    private static readonly DriverProfile Max = new() { Transponder = "AAA1111", DriverName = "Max Power" };

    private static DriverProfile? MaxByEither(string t) =>
        t is "AAA1111" or "BBB2222" ? Max : null;

    [Fact]
    public void BugHunt24_A_new_driver_on_their_alternate_transponder_is_not_welcomed_twice()
    {
        var welcomed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var first = RaceControlControl.NewcomersToWelcome(new[] { "AAA1111" }, MaxByEither, _ => true, "2026-11-23", welcomed);
        Assert.Single(first);

        // Later tonight, their second class, on the other car's transponder.
        var second = RaceControlControl.NewcomersToWelcome(new[] { "BBB2222" }, MaxByEither, _ => true, "2026-11-23", welcomed);
        Assert.Empty(second);
    }

    [Fact]
    public void BugHunt24_A_new_driver_seated_on_both_transponders_in_one_race_is_named_once()
    {
        var welcomed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var rows = RaceControlControl.NewcomersToWelcome(new[] { "AAA1111", "BBB2222" }, MaxByEither, _ => true, "2026-11-23", welcomed);

        Assert.Single(rows);
    }

    [Fact]
    public void BugHunt24_A_new_driver_is_welcomed_again_on_another_night()
    {
        var welcomed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Assert.Single(RaceControlControl.NewcomersToWelcome(new[] { "AAA1111" }, MaxByEither, _ => true, "2026-11-23", welcomed));
        Assert.Single(RaceControlControl.NewcomersToWelcome(new[] { "BBB2222" }, MaxByEither, _ => true, "2026-11-24", welcomed));
    }

    // --- F227: the tech warning in a shared heat ----------------------------

    private static RaceClass ClassWithTech(string name, bool warn = true) =>
        new() { Name = name, TechCheckItems = new List<string> { "Ride height" }, WarnOnUninspected = warn };

    private static RoundDefinition StockAndMod() => new()
    {
        ClassName = "Stock",
        RoundLabel = "Q1 Heat 1",
        AssignedTransponders = new List<string> { "A1", "B2" },
        SharedClasses = new List<string> { "Mod" },
        SharedClassOf = new Dictionary<string, string> { ["A1"] = "Stock", ["B2"] = "Mod" },
    };

    private static List<string> Uninspected(RoundDefinition round, IEnumerable<RaceClass> classes, TechInspectionStore tech) =>
        RaceControlControl.UninspectedDriversOf(round, classes, tech, t => MixedHeats.ClassOfDriver(round, t), _ => null);

    [Fact]
    public void BugHunt24_A_Mod_car_passed_under_Mod_is_not_listed_in_a_Stock_and_Mod_heat()
    {
        var tech = TechInspectionStore.Load();
        tech.Upsert(new TechInspection { Transponder = "A1", ClassName = "Stock", Result = TechResult.Passed });
        tech.Upsert(new TechInspection { Transponder = "B2", ClassName = "Mod", Result = TechResult.Passed });

        var lines = Uninspected(StockAndMod(), new[] { ClassWithTech("Stock"), ClassWithTech("Mod") }, tech);

        Assert.Empty(lines);
    }

    [Fact]
    public void BugHunt24_The_Mod_cars_are_still_checked_when_Stock_has_the_warning_off()
    {
        var tech = TechInspectionStore.Load();

        var lines = Uninspected(StockAndMod(), new[] { ClassWithTech("Stock", warn: false), ClassWithTech("Mod") }, tech);

        Assert.Equal(new[] { "B2 - not inspected" }, lines);
    }

    [Fact]
    public void BugHunt24_A_one_class_heat_is_judged_as_before()
    {
        var tech = TechInspectionStore.Load();
        var round = new RoundDefinition { ClassName = "Stock", AssignedTransponders = new List<string> { "A1", "C3" } };
        tech.Upsert(new TechInspection { Transponder = "A1", ClassName = "Stock", Result = TechResult.Passed });

        var lines = Uninspected(round, new[] { ClassWithTech("Stock") }, tech);

        Assert.Equal(new[] { "C3 - not inspected" }, lines);
    }

    // --- F234: the Big Screen's championship card ---------------------------

    private static (Season Season, List<SessionRecord> Sessions) Champ(string name, string className, DateTime lastRaced) =>
        (new Season { Name = name, ClassName = className }, new List<SessionRecord> { new() { StartedAtUtc = lastRaced } });

    [Fact]
    public void BugHunt24_The_card_shows_this_years_series_not_last_years_for_the_same_class()
    {
        var tonight = new DateTime(2026, 11, 23, 20, 0, 0, DateTimeKind.Utc);
        var candidates = new[]
        {
            Champ("2025 Buggy Series", "Buggy", tonight.AddYears(-1)),
            Champ("2026 Buggy Series", "Buggy", tonight),
        };

        Assert.Equal("2026 Buggy Series", RaceControlControl.PickChampionship(candidates, " buggy ").Season.Name);
    }

    [Fact]
    public void BugHunt24_With_no_series_for_the_class_the_card_shows_the_one_raced_last()
    {
        var tonight = new DateTime(2026, 11, 23, 20, 0, 0, DateTimeKind.Utc);
        var candidates = new[]
        {
            Champ("A Truck Series", "Truck", tonight.AddDays(-7)),
            Champ("B Buggy Series", "Buggy", tonight),
        };

        Assert.Equal("B Buggy Series", RaceControlControl.PickChampionship(candidates, "Touring").Season.Name);
        Assert.Equal("A Truck Series", RaceControlControl.PickChampionship(candidates, "Truck").Season.Name);
    }

    // --- F220: a round picked by hand in the gap ----------------------------

    [Fact]
    public void BugHunt24_A_round_picked_by_hand_after_filing_is_not_passed_over_as_the_race_just_run()
    {
        Assert.Null(RaceControlControl.RoundJustRun("heat3", ("heat2", 7), raceWaitingToBeFiled: false));
    }

    [Fact]
    public void BugHunt24_The_race_just_run_is_still_passed_over()
    {
        // The round it was filed under, still in the box.
        Assert.Equal("heat2", RaceControlControl.RoundJustRun("heat2", ("heat2", 7), raceWaitingToBeFiled: false));

        // Not filed yet - its result can take a moment to be linked.
        Assert.Equal("heat2", RaceControlControl.RoundJustRun("heat2", null, raceWaitingToBeFiled: false));

        // A race waiting to be filed may be this round's.
        Assert.Equal("heat3", RaceControlControl.RoundJustRun("heat3", ("heat2", 7), raceWaitingToBeFiled: true));

        Assert.Null(RaceControlControl.RoundJustRun(null, ("heat2", 7), raceWaitingToBeFiled: false));
    }

    // --- F224: the lap strip for a credited start ---------------------------

    private static RacerStanding Standing(bool credited, params long[] laps)
    {
        var standing = new RacerStanding { TransponderId = "T1", StartLapCredited = credited, FastestLapMs = 10_900 };
        standing.LapTimesMs.AddRange(laps);
        return standing;
    }

    [Fact]
    public void BugHunt24_A_credited_start_reads_credited_and_the_next_pass_is_the_start()
    {
        var standing = Standing(true, 0, 2_800, 11_000, 10_900);

        var (zeroEntry, zeroDetail) = RaceControlControl.LapStripEntry(standing, 0, 10_900, 5);
        Assert.DoesNotContain("0.000", zeroEntry, StringComparison.Ordinal);
        Assert.Contains("credited", zeroEntry, StringComparison.Ordinal);
        Assert.DoesNotContain("0.000", zeroDetail, StringComparison.Ordinal);

        var (startEntry, startDetail) = RaceControlControl.LapStripEntry(standing, 1, 10_900, 5);
        Assert.StartsWith("start ", startEntry, StringComparison.Ordinal);
        Assert.DoesNotContain("L2", startEntry, StringComparison.Ordinal);
        Assert.DoesNotContain("their best", startDetail, StringComparison.Ordinal);

        var (lapEntry, lapDetail) = RaceControlControl.LapStripEntry(standing, 2, 10_900, 5);
        Assert.StartsWith("L3 ", lapEntry, StringComparison.Ordinal);
        Assert.Contains("off their best", lapDetail, StringComparison.Ordinal);
    }

    [Fact]
    public void BugHunt24_An_ordinary_start_is_labelled_as_before()
    {
        var standing = Standing(false, 1_400, 10_900);

        Assert.StartsWith("start ", RaceControlControl.LapStripEntry(standing, 0, 10_900, 5).Entry, StringComparison.Ordinal);
        Assert.StartsWith("L2 ", RaceControlControl.LapStripEntry(standing, 1, 10_900, 5).Entry, StringComparison.Ordinal);
        Assert.Contains("their best", RaceControlControl.LapStripEntry(standing, 1, 10_900, 5).Detail, StringComparison.Ordinal);
    }

    // --- F233: two neighbours skipped and put back --------------------------

    [Fact]
    public void BugHunt24_Two_neighbours_skipped_and_put_back_in_skip_order_keep_their_order()
    {
        // Skip A (pole), then B.
        IReadOnlyList<string> rememberedA = new List<string> { "A", "B", "C" };
        var rememberedB = RaceControlControl.GridWithEarlierSkips(
            new List<string> { "B", "C" },
            new (string, IReadOnlyList<string>?)[] { ("A", rememberedA) });

        // Put A back, then B.
        var grid = RaceControlControl.BackInTheirSlot(new[] { "C" }, rememberedA, "A");
        grid = RaceControlControl.BackInTheirSlot(grid, rememberedB, "B");

        Assert.Equal(new[] { "A", "B", "C" }, grid);
    }

    [Fact]
    public void BugHunt24_Two_neighbours_put_back_in_reverse_order_keep_their_order()
    {
        IReadOnlyList<string> rememberedA = new List<string> { "A", "B", "C" };
        var rememberedB = RaceControlControl.GridWithEarlierSkips(
            new List<string> { "B", "C" },
            new (string, IReadOnlyList<string>?)[] { ("A", rememberedA) });

        var grid = RaceControlControl.BackInTheirSlot(new[] { "C" }, rememberedB, "B");
        grid = RaceControlControl.BackInTheirSlot(grid, rememberedA, "A");

        Assert.Equal(new[] { "A", "B", "C" }, grid);
    }
}
