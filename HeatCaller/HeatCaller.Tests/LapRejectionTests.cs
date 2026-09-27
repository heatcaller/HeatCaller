namespace HeatCaller.Tests;

/// <summary>
/// The race log's reason for throwing a pass out
/// (RaceControlSession.RejectionReason). Rehearsal 3's A-Main had cars
/// parked on the loop, seen every two seconds, and logged them as "too soon,
/// 4000ms &lt; 3000ms minimum" - the minimum is checked against the previous
/// pass, but the figure printed was the time since the last counted lap, so
/// a correct rejection read as a broken check.
/// </summary>
public sealed class LapRejectionTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static void Cross(RaceControlSession race, string car, uint raceTimeMs) =>
        race.ProcessLapEvent(new DirectLapEvent
        {
            TransponderId = car,
            DeviceTimeMs = raceTimeMs,
            SignalHits = 1,
            SignalStrength = 100,
            IsSynthetic = true,
        });

    [Fact]
    public void A_car_parked_on_the_loop_is_rejected_on_the_gap_since_its_previous_pass()
    {
        var race = new RaceControlSession();
        var log = new List<string>();
        race.LogMessage += log.Add;

        race.Stage();
        race.GreenFlag(0);

        // A lap at 5s, then seen every 2s while it sits on the loop.
        Cross(race, "P1", 5_000);
        Cross(race, "P1", 7_000);
        Cross(race, "P1", 9_000);

        var rejected = log.Where(l => l.StartsWith("Lap rejected", StringComparison.Ordinal)).ToList();

        Assert.Equal(2, rejected.Count);

        // The second rejection: 2s after the pass before it (itself
        // rejected), 4s after the lap that counted.
        Assert.Contains("2000ms after the previous pass", rejected[1]);
        Assert.Contains("3000ms minimum", rejected[1]);
        Assert.Contains("4000ms since the last counted lap", rejected[1]);

        // The old wording put the 4000 against the minimum as if it were
        // the figure being judged.
        Assert.DoesNotContain("4000ms < 3000ms", rejected[1]);
    }

    [Fact]
    public void The_lap_figure_says_when_it_runs_from_the_start()
    {
        var race = new RaceControlSession();

        var text = race.RejectionReason(gapFromLastSeenMs: 1_500, lapTimeMs: 1_500, measuredFromCountedLap: false);

        Assert.Contains("1500ms after the previous pass", text);
        Assert.Contains("1500ms since the start", text);
        Assert.DoesNotContain("counted lap", text);
    }

    [Fact]
    public void A_first_pass_is_rejected_for_the_start_not_for_being_too_soon()
    {
        var race = new RaceControlSession();

        var text = race.RejectionReason(gapFromLastSeenMs: null, lapTimeMs: 0, measuredFromCountedLap: false);

        Assert.StartsWith("first pass", text);
        Assert.DoesNotContain("too soon", text);
    }
}
