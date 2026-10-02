namespace HeatCaller.Tests;

/// <summary>
/// Bump-ups and disqualifications: a driver DQ'd in Edit Result keeps a
/// numbered place at the bottom of the feeder, and must not be promoted
/// from it into the main above.
/// </summary>
public sealed class BugHuntMainsTests : IDisposable
{
    private readonly TempAppData _data = new();
    private readonly ProfileStore _profiles = ProfileStore.Load();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void A_disqualified_driver_in_a_small_feeder_is_not_moved_up()
    {
        var target = Target(bumpUpCount: 3);
        var feederResults = Finished("C1", "C2", "C3");
        feederResults.Results[2].Outcome = ResultEditor.Dq;

        var plan = MainsBuilder.PlanBumpUp(target, feederResults, _profiles);

        Assert.Equal(new[] { "C1", "C2" }, plan.Movable.Select(p => p.Transponder));
        Assert.Empty(plan.AlreadyThere);
    }

    [Fact]
    public void A_disqualified_winner_gives_his_place_to_the_next_finisher()
    {
        var target = Target(bumpUpCount: 2);
        var feederResults = Finished("C1", "C2", "C3", "C4");
        feederResults.Results[0].Outcome = ResultEditor.Dq;

        var plan = MainsBuilder.PlanBumpUp(target, feederResults, _profiles);

        Assert.Equal(new[] { "C2", "C3" }, plan.Movable.Select(p => p.Transponder));
    }

    [Fact]
    public void A_driver_who_did_not_finish_still_counts_in_his_place()
    {
        var target = Target(bumpUpCount: 3);
        var feederResults = Finished("C1", "C2", "C3");
        feederResults.Results[2].Outcome = ResultEditor.Dnf;

        var plan = MainsBuilder.PlanBumpUp(target, feederResults, _profiles);

        Assert.Equal(new[] { "C1", "C2", "C3" }, plan.Movable.Select(p => p.Transponder));
    }

    private static RoundDefinition Target(int bumpUpCount) => new()
    {
        ClassName = "Stock",
        RoundLabel = "B-Main",
        Kind = RoundKind.Main,
        MainTier = 1,
        BumpUpCount = bumpUpCount,
        AssignedTransponders = new List<string> { "B1", "B2", "B3", "B4" },
    };

    private static SessionRecord Finished(params string[] finishingOrder) => new()
    {
        Label = "C-Main",
        SessionType = RaceSessionType.Race,
        StartedAtUtc = DateTime.UtcNow,
        Results = finishingOrder
            .Select((t, i) => new SessionResultEntry { Transponder = t, DriverName = t, Position = i + 1, Laps = 20 - i })
            .ToList(),
    };
}
