namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 24 (23 Nov 2026), RaceTiming. With timing on its own thread, a
/// real line pass goes to the timing thread through PostLinePass, which
/// judged it without noting its decoder-mapped crossing - so the UI was told
/// of its lap with no crossing time and the announcer timed the call from
/// when the pass arrived, not from when the car crossed.
/// </summary>
public sealed class BugHunt24_Timing_RaceTimingTests : IDisposable
{
    private readonly TimingHost _host = new();
    private readonly QueueingSynchronizationContext _ui = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public void Threaded_a_line_pass_posted_to_the_timing_thread_carries_its_crossing_time_to_the_ui()
    {
        var race = new RaceTiming(new RaceControlSession(), _host, _ui, tickOnHost: false);
        var seen = new List<long?>();
        race.LapProcessed += (_, _, _) => seen.Add(race.DeliveringLapCrossedAtMs);

        race.Stage();
        race.GreenFlag(10_000);
        _ui.Pump();

        race.PostLinePass(Pass("101", 21_000, crossedMs: 1_234), _ => { });
        race.PostLinePass(Pass("101", 33_000, crossedMs: 5_678), _ => { });
        _host.Invoke(() => { });
        _ui.Pump();

        Assert.Contains((long?)1_234, seen);
        Assert.Contains((long?)5_678, seen);
        Assert.DoesNotContain((long?)null, seen);
        Assert.Null(race.DeliveringLapCrossedAtMs);
    }

    [Fact]
    public void Threaded_a_line_pass_with_no_mapped_crossing_hands_over_none()
    {
        var race = new RaceTiming(new RaceControlSession(), _host, _ui, tickOnHost: false);
        var seen = new List<long?>();
        race.LapProcessed += (_, _, _) => seen.Add(race.DeliveringLapCrossedAtMs);

        race.Stage();
        race.GreenFlag(10_000);
        _ui.Pump();

        race.PostLinePass(Pass("101", 21_000, crossedMs: 1_234), _ => { });
        race.PostLinePass(Pass("101", 33_000, crossedMs: 0), _ => { });
        _host.Invoke(() => { });
        _ui.Pump();

        // The second pass must not inherit the first one's crossing.
        Assert.NotEmpty(seen);
        Assert.Equal((long?)1_234, seen[0]);
        Assert.Null(seen[^1]);
    }

    private static DirectLapEvent Pass(string car, uint deviceTimeMs, long crossedMs) => new()
    {
        TransponderId = car,
        DeviceTimeMs = deviceTimeMs,
        SignalHits = 1,
        SignalStrength = 100,
        Decoder = DecoderConnection.EasyLap,
        CrossedMs = crossedMs,
    };
}
