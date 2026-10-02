namespace HeatCaller.Tests;

/// <summary>
/// A lap credit cannot lift a car that never turned a lap above a DNF who
/// drove some - both sit in the DNF group, and the result editor ranks the
/// driver with laps first.
/// </summary>
public sealed class BugHuntH2HandicapTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    [Fact]
    public void A_lap_credit_does_not_lift_a_non_starter_above_a_DNF_with_laps()
    {
        var handicaps = HandicapStore.Load();
        handicaps.Upsert(new DriverHandicap { Transponder = "A", LapCredit = 1 });

        var results = new List<SessionResultEntry>
        {
            new() { Position = 1, Transponder = "B", Laps = 1, ElapsedMs = 20_000, Outcome = ResultEditor.Dnf },
            new() { Position = 2, Transponder = "A", Laps = 0, ElapsedMs = 0 },
        };

        var adjusted = HandicapCalculator.Apply(results, handicaps, HandicapMode.LapCredit);

        Assert.Equal(new[] { "B", "A" }, adjusted.Select(a => a.Result.Transponder));
    }

    [Fact]
    public void A_bigger_lap_credit_still_does_not_lift_a_non_starter_above_a_DNF_with_laps()
    {
        var handicaps = HandicapStore.Load();
        handicaps.Upsert(new DriverHandicap { Transponder = "A", LapCredit = 3 });

        var results = new List<SessionResultEntry>
        {
            new() { Position = 1, Transponder = "B", Laps = 1, ElapsedMs = 20_000, Outcome = ResultEditor.Dnf },
            new() { Position = 2, Transponder = "A", Laps = 0, ElapsedMs = 0 },
        };

        var adjusted = HandicapCalculator.Apply(results, handicaps, HandicapMode.LapCredit);

        Assert.Equal(new[] { "B", "A" }, adjusted.Select(a => a.Result.Transponder));
    }

    [Fact]
    public void A_lap_credit_still_reorders_DNFs_who_drove_laps()
    {
        var handicaps = HandicapStore.Load();
        handicaps.Upsert(new DriverHandicap { Transponder = "B", LapCredit = 2 });

        var results = new List<SessionResultEntry>
        {
            new() { Position = 1, Transponder = "A", Laps = 5, ElapsedMs = 50_000, Outcome = ResultEditor.Dnf },
            new() { Position = 2, Transponder = "B", Laps = 4, ElapsedMs = 45_000, Outcome = ResultEditor.Dnf },
        };

        var adjusted = HandicapCalculator.Apply(results, handicaps, HandicapMode.LapCredit);

        Assert.Equal(new[] { "B", "A" }, adjusted.Select(a => a.Result.Transponder));
    }
}
