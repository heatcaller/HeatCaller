namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 8 (20 Oct 2026): a penalty given during the running heat must
/// count on the best-consecutive-laps window the live projection ranks on.
/// </summary>
public sealed class BugHunt8_QualifyingCalculator_csTests : IDisposable
{
    // Declared first: the stores below load from wherever it points.
    private readonly TempAppData _data = new();

    private readonly RoundDefinitionStore _rounds = RoundDefinitionStore.Load();
    private readonly ProfileStore _profiles = ProfileStore.Load();

    private const string Stock = "Stock";

    public void Dispose() => _data.Dispose();

    private static readonly RaceClass BestThree = new()
    {
        Name = Stock,
        QualifyingMethod = QualifyingMethod.BestConsecutiveLaps,
        ConsecutiveLapCount = 3,
        CountBestRounds = 1,
    };

    private List<QualifyingCalculator.Entry> ComputeLive(params QualifyingCalculator.LiveResult[] results) =>
        QualifyingCalculator.Compute(Stock, _rounds, SessionHistoryStore.FromSessions(new List<SessionRecord>()), _profiles,
            BestThree, null, new QualifyingCalculator.LiveHeat("live-q1", "Q1", DateTime.UtcNow, results));

    [Fact]
    public void A_penalty_in_the_running_heat_is_added_to_the_window()
    {
        // A's window is 35.2s but carries 15s of penalties; B's is a clean 35.4s.
        var entries = ComputeLive(
            new QualifyingCalculator.LiveResult("A", "A", string.Empty, 4, 51_600, 11_700,
                new long[] { 1_400, 11_700, 11_700, 11_800 }, 15_000),
            new QualifyingCalculator.LiveResult("B", "B", string.Empty, 4, 36_800, 11_800,
                new long[] { 1_400, 11_800, 11_800, 11_800 }));

        Assert.Equal(new[] { "B", "A" }, entries.Select(e => e.Transponder).ToArray());
        Assert.Equal(35_200 + 15_000, entries.Single(e => e.Transponder == "A").BestConsecutiveMs);
        Assert.Equal(35_400, entries.Single(e => e.Transponder == "B").BestConsecutiveMs);
    }

    [Fact]
    public void A_penalty_does_not_invent_a_window_for_a_driver_without_one()
    {
        var a = Assert.Single(ComputeLive(
            new QualifyingCalculator.LiveResult("A", "A", string.Empty, 1, 20_000, 11_700,
                new long[] { 1_400, 11_700 }, 5_000)));

        Assert.Equal(0, a.BestConsecutiveMs);
    }

    [Fact]
    public void A_negative_penalty_is_ignored()
    {
        var a = Assert.Single(ComputeLive(
            new QualifyingCalculator.LiveResult("A", "A", string.Empty, 4, 36_600, 11_700,
                new long[] { 1_400, 11_700, 11_700, 11_800 }, -5_000)));

        Assert.Equal(35_200, a.BestConsecutiveMs);
    }
}
