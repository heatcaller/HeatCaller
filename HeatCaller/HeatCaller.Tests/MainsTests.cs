namespace HeatCaller.Tests;

/// <summary>
/// MainsBuilder: splitting qualifying into A/B/C mains, the order they run
/// in, and moving the top finishers of one main up into the next.
/// </summary>
public sealed class MainsTests : IDisposable
{
    private readonly TempAppData _data = new();
    private readonly ProfileStore _profiles = ProfileStore.Load();

    public void Dispose() => _data.Dispose();

    // --- Seeding --------------------------------------------------------------

    [Fact]
    public void The_fastest_fill_the_A_main_and_the_last_main_is_the_short_one()
    {
        var ranked = Enumerable.Range(1, 10).Select(i => $"D{i}").ToList();

        var plan = MainsBuilder.PlanSeeding(ranked, mainSize: 4);

        Assert.Equal(3, plan.TierCount);
        Assert.Equal(new[] { "D1", "D2", "D3", "D4" }, plan.Tiers[0]);
        Assert.Equal(new[] { "D5", "D6", "D7", "D8" }, plan.Tiers[1]);
        Assert.Equal(new[] { "D9", "D10" }, plan.Tiers[2]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void A_main_is_never_smaller_than_two(int mainSize)
    {
        var plan = MainsBuilder.PlanSeeding(new[] { "D1", "D2", "D3" }, mainSize);

        Assert.Equal(2, plan.MainSize);
        Assert.Equal(new[] { "D1", "D2" }, plan.Tiers[0]);
    }

    [Fact]
    public void Nobody_qualified_still_gives_one_empty_A_main_to_fill_by_hand()
    {
        var plan = MainsBuilder.PlanSeeding(Array.Empty<string>(), mainSize: 8);

        var only = Assert.Single(plan.Tiers);
        Assert.Empty(only);
    }

    [Fact]
    public void The_lowest_main_runs_first_so_its_finishers_can_move_up()
    {
        var plan = MainsBuilder.PlanSeeding(Enumerable.Range(1, 10).Select(i => $"D{i}").ToList(), mainSize: 4);

        var rounds = MainsBuilder.BuildRounds("Stock", plan, "Club layout", bumpUpCount: 2);

        Assert.Equal(new[] { "C-Main", "B-Main", "A-Main" }, rounds.Select(r => r.RoundLabel));
        Assert.Equal(new[] { 2, 1, 0 }, rounds.Select(r => r.MainTier));
        Assert.All(rounds, r => Assert.Equal(RoundKind.Main, r.Kind));

        // Every main is set up to receive, the bottom one included: a main
        // added by hand below it later (late arrivals) must be able to move
        // its finishers up. While nothing is below, the blocker stops first
        // at "no main below this one".
        Assert.Equal(new[] { 2, 2, 2 }, rounds.Select(r => r.BumpUpCount));
        Assert.Equal(new[] { "D1", "D2", "D3", "D4" }, rounds.Single(r => r.MainTier == 0).AssignedTransponders);
    }

    // --- Who moves up -------------------------------------------------------------

    [Fact]
    public void The_top_finishers_below_move_up()
    {
        var (target, _) = Mains(bumpUpCount: 2);
        var feederResults = Finished("B5", "B6", "B7", "B8");

        var plan = MainsBuilder.PlanBumpUp(target, feederResults, _profiles);

        Assert.Equal(new[] { "B5", "B6" }, plan.Movable.Select(p => p.Transponder));
        Assert.Empty(plan.AlreadyThere);
    }

    [Fact]
    public void A_ghost_that_finished_near_the_front_does_not_take_a_real_driver_s_place()
    {
        var (target, _) = Mains(bumpUpCount: 2);
        var feederResults = Finished("GHOST-pace", "B5", "B6", "B7");

        var plan = MainsBuilder.PlanBumpUp(target, feederResults, _profiles);

        Assert.Equal(new[] { "B5", "B6" }, plan.Movable.Select(p => p.Transponder));
    }

    [Fact]
    public void A_row_with_no_known_place_does_not_jump_ahead_of_the_winner()
    {
        var (target, _) = Mains(bumpUpCount: 2);
        var feederResults = Finished("B5", "B6", "B7");
        feederResults.Results.Add(new SessionResultEntry { Transponder = "B8", DriverName = "B8", Position = 0 });

        var plan = MainsBuilder.PlanBumpUp(target, feederResults, _profiles);

        Assert.Equal(new[] { "B5", "B6" }, plan.Movable.Select(p => p.Transponder));
    }

    [Fact]
    public void A_driver_on_both_his_cars_takes_one_place_under_his_main_transponder()
    {
        _profiles.Upsert(new DriverProfile { Transponder = "100", DriverName = "Alice", AlternateTransponder = "200" });

        var (target, _) = Mains(bumpUpCount: 2);
        var feederResults = Finished("200", "100", "B6", "B7");

        var plan = MainsBuilder.PlanBumpUp(target, feederResults, _profiles);

        Assert.Equal(new[] { "100", "B6" }, plan.Movable.Select(p => p.Transponder));
    }

    [Fact]
    public void A_driver_already_moved_up_by_hand_is_left_where_he_is()
    {
        var (target, _) = Mains(bumpUpCount: 2);
        target.AssignedTransponders.Add("B5");

        var plan = MainsBuilder.PlanBumpUp(target, Finished("B5", "B6", "B7"), _profiles);

        Assert.Equal(new[] { "B6" }, plan.Movable.Select(p => p.Transponder));
        Assert.Equal(new[] { "B5" }, plan.AlreadyThere.Select(p => p.Transponder));
    }

    [Fact]
    public void Applying_grows_the_main_above_and_moves_nobody_down()
    {
        var (target, feeder) = Mains(bumpUpCount: 2);
        var before = target.AssignedTransponders.ToList();

        var result = MainsBuilder.ApplyBumpUp(target, feeder, Finished("B5", "B6", "B7", "B8"), _profiles);

        Assert.Equal(new[] { "B5", "B6" }, result.Promoted);
        Assert.Empty(result.Demoted);

        // At the back: they came up from below.
        Assert.Equal(before.Concat(new[] { "B5", "B6" }), target.AssignedTransponders);
        Assert.Equal(new[] { "B7", "B8" }, feeder.AssignedTransponders);
        Assert.True(target.BumpUpsApplied);
    }

    // --- When a bump-up can happen -----------------------------------------------------

    [Fact]
    public void Bump_ups_wait_for_the_main_below_to_be_run()
    {
        var (target, feeder) = Mains(bumpUpCount: 2);
        var none = SessionHistoryStore.FromSessions(Array.Empty<SessionRecord>());

        Assert.Contains("hasn't been run", MainsBuilder.DescribeBumpUpBlocker(target, new[] { target, feeder }, none));
    }

    [Fact]
    public void Bump_ups_are_ready_once_the_main_below_has_its_result()
    {
        var (target, feeder) = Mains(bumpUpCount: 2);
        var session = Finished("B5", "B6");
        feeder.LinkedSessionId = session.Id;

        var history = SessionHistoryStore.FromSessions(new[] { session });

        Assert.Null(MainsBuilder.DescribeBumpUpBlocker(target, new[] { target, feeder }, history));
    }

    [Fact]
    public void Bump_ups_are_refused_once_applied_or_once_the_main_has_run()
    {
        var (target, feeder) = Mains(bumpUpCount: 2);
        var session = Finished("B5", "B6");
        feeder.LinkedSessionId = session.Id;
        var history = SessionHistoryStore.FromSessions(new[] { session });

        target.BumpUpsApplied = true;
        Assert.NotNull(MainsBuilder.DescribeBumpUpBlocker(target, new[] { target, feeder }, history));

        target.BumpUpsApplied = false;
        target.LinkedSessionId = "already-run";
        Assert.NotNull(MainsBuilder.DescribeBumpUpBlocker(target, new[] { target, feeder }, history));
    }

    // --- Helpers ---------------------------------------------------------------------

    /// <summary>An A-main seeded A1-A4 taking bump-ups, over a B-main seeded B5-B8.</summary>
    private static (RoundDefinition Target, RoundDefinition Feeder) Mains(int bumpUpCount)
    {
        var target = new RoundDefinition
        {
            ClassName = "Stock",
            RoundLabel = "A-Main",
            Kind = RoundKind.Main,
            MainTier = 0,
            BumpUpCount = bumpUpCount,
            AssignedTransponders = new List<string> { "A1", "A2", "A3", "A4" },
        };

        var feeder = new RoundDefinition
        {
            ClassName = "Stock",
            RoundLabel = "B-Main",
            Kind = RoundKind.Main,
            MainTier = 1,
            AssignedTransponders = new List<string> { "B5", "B6", "B7", "B8" },
        };

        return (target, feeder);
    }

    /// <summary>A saved result with these drivers in finishing order, P1 first.</summary>
    private static SessionRecord Finished(params string[] finishingOrder) => new()
    {
        Label = "B-Main",
        SessionType = RaceSessionType.Race,
        StartedAtUtc = DateTime.UtcNow,
        Results = finishingOrder
            .Select((t, i) => new SessionResultEntry { Transponder = t, DriverName = t, Position = i + 1, Laps = 20 - i })
            .ToList(),
    };
}
