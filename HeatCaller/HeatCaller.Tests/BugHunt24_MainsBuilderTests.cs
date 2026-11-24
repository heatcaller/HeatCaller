namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026): a car in a single lower main's result with no
/// laps - a grid car put on the board by a hand-released green that never
/// crossed the line - must not take a bump-up place into the main above.
/// </summary>
public sealed class BugHunt24_MainsBuilderTests : IDisposable
{
    private readonly TempAppData _data = new();
    private readonly ProfileStore _profiles = ProfileStore.Load();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void PlanBumpUp_does_not_promote_a_car_that_never_turned_a_lap()
    {
        var (target, _) = Mains(bumpUpCount: 2);
        var feederResults = Result(("B5", 10), ("B6", 0), ("B7", 0));

        var plan = MainsBuilder.PlanBumpUp(target, feederResults, _profiles);

        Assert.Equal(new[] { "B5" }, plan.Movable.Select(p => p.Transponder));
        Assert.Empty(plan.AlreadyThere);
    }

    [Fact]
    public void ApplyBumpUp_adds_only_the_cars_that_raced_to_the_main_above()
    {
        var (target, feeder) = Mains(bumpUpCount: 3);
        var feederResults = Result(("B5", 10), ("B6", 9), ("B7", 0));

        var result = MainsBuilder.ApplyBumpUp(target, feeder, feederResults, _profiles);

        Assert.Equal(new[] { "B5", "B6" }, result.Promoted);
        Assert.Equal(new[] { "A1", "A2", "A3", "A4", "B5", "B6" }, target.AssignedTransponders);
        Assert.Contains("B7", feeder.AssignedTransponders);
    }

    [Fact]
    public void A_driver_who_did_not_finish_but_turned_laps_still_counts_in_his_place()
    {
        var (target, _) = Mains(bumpUpCount: 3);
        var feederResults = Result(("B5", 10), ("B6", 9), ("B7", 3));
        feederResults.Results[2].Outcome = ResultEditor.Dnf;

        var plan = MainsBuilder.PlanBumpUp(target, feederResults, _profiles);

        Assert.Equal(new[] { "B5", "B6", "B7" }, plan.Movable.Select(p => p.Transponder));
    }

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
            AssignedTransponders = new List<string> { "B5", "B6", "B7" },
        };

        return (target, feeder);
    }

    /// <summary>A saved single-race result, P1 first, with each car's laps.</summary>
    private static SessionRecord Result(params (string Transponder, int Laps)[] order) => new()
    {
        Label = "B-Main",
        SessionType = RaceSessionType.Race,
        StartedAtUtc = DateTime.UtcNow,
        Results = order
            .Select((r, i) => new SessionResultEntry { Transponder = r.Transponder, DriverName = r.Transponder, Position = i + 1, Laps = r.Laps })
            .ToList(),
    };
}
