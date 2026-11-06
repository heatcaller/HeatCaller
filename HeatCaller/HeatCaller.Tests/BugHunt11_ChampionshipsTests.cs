namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 11 (5 Nov 2026), Championships: the live qualifying projection
/// kept ranking a driver race control had black-flagged. Saving the race DQs
/// him (RaceControlWarnings.ApplyBlackFlags) and a DQ'd heat does not count,
/// so the projected places were wrong for the rest of the heat - and as a
/// black flag adds no lap and no penalty, the cached projection was served
/// unchanged until the next crossing.
/// </summary>
public sealed class BugHunt11_ChampionshipsTests : IDisposable
{
    // Declared first: the stores below load from wherever it points.
    private readonly TempAppData _data = new();

    private const string Stock = "Stock";

    public void Dispose() => _data.Dispose();

    private static void Cross(RaceControlSession race, string transponder, uint atMs) =>
        race.ProcessLapEvent(new DirectLapEvent
        {
            TransponderId = transponder,
            DeviceTimeMs = atMs,
            SignalHits = 1,
            SignalStrength = 100,
            IsSynthetic = true,
        });

    /// <summary>A qualifier on track: A four laps in, B three, A to be black-flagged on his first warning.</summary>
    private static (RaceControlSession Race, QualifyingProjector Projector) Qualifier()
    {
        var race = new RaceControlSession();
        race.Stage();
        race.GreenFlag(0);
        race.WarningLadder = new List<WarningStep> { new(WarningAction.BlackFlag) };

        Cross(race, "A", 1_000);
        Cross(race, "B", 1_500);
        Cross(race, "A", 11_000);
        Cross(race, "B", 12_500);
        Cross(race, "A", 21_000);
        Cross(race, "B", 23_500);
        Cross(race, "A", 31_000);

        var round = new RoundDefinition
        {
            ClassName = Stock,
            RoundLabel = "Qualifier 1 Heat 1",
            Kind = RoundKind.Qualifier,
            AssignedTransponders = new List<string> { "A", "B" },
        };

        var projector = new QualifyingProjector(
            new RaceTiming(race),
            ProfileStore.Load(),
            RaceClassStore.Load(),
            SessionHistoryStore.FromSessions(new List<SessionRecord>()),
            RoundDefinitionStore.Load(),
            events: null,
            activeRound: () => round);

        return (race, projector);
    }

    [Fact]
    public void A_black_flagged_driver_is_not_ranked_from_the_running_heat()
    {
        var (race, projector) = Qualifier();

        Assert.Equal(WarningAction.BlackFlag, race.IssueWarning("A", "rough driving")!.Action);

        var projection = projector.Current();

        Assert.NotNull(projection);
        Assert.Equal(new[] { "B" }, projection!.Rows.Select(r => r.Transponder).ToArray());
        Assert.Equal(1, projection.Slots["B"].Position);
        Assert.False(projection.Slots.ContainsKey("A"));
    }

    [Fact]
    public void A_black_flag_redraws_the_projection_without_waiting_for_a_crossing()
    {
        var (race, projector) = Qualifier();

        var before = projector.Current();
        Assert.Equal("A", before!.Rows[0].Transponder);
        Assert.Equal(2, before.Slots["B"].Position);

        race.IssueWarning("A", "rough driving");

        // No crossing since: the flag alone must change the answer.
        var after = projector.Current();

        Assert.NotSame(before, after);
        Assert.Equal(1, after!.Slots["B"].Position);
        Assert.DoesNotContain(after.Rows, r => r.Transponder == "A");
    }
}
