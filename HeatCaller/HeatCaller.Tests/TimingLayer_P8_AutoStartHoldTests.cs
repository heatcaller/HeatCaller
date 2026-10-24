namespace HeatCaller.Tests;

/// <summary>
/// P8 of the timing layer, B1 (24 Oct 2026): a race start that spends time at
/// Idle first (On Air) holds practice auto-start on the timing thread for as
/// long as it keeps the hold - RaceTiming.HoldPracticeAutoStart. A car
/// warming up during On Air's opening used to start practice at the gate,
/// and On Air was then called off.
/// </summary>
public sealed class TimingLayer_P8_AutoStartHoldTests : IDisposable
{
    private readonly TimingHost _host = new();
    private readonly QueueingSynchronizationContext _ui = new();

    public void Dispose() => _host.Dispose();

    /// <summary>A threaded facade made on this test's thread - which is then its "UI thread".</summary>
    private RaceTiming Threaded() => new(new RaceControlSession(), _host, _ui, tickOnHost: false);

    private static DirectLapEvent Pass(string car, uint deviceTimeMs) => new()
    {
        TransponderId = car,
        DeviceTimeMs = deviceTimeMs,
        SignalHits = 1,
        SignalStrength = 100,
        Decoder = DecoderConnection.EasyLap,
    };

    /// <summary>Waits until everything already handed to the timing thread has run.</summary>
    private void Drain() => _host.Invoke(() => { });

    private static PracticeAutoStartPlan Plan() => new(20, TimeSpan.FromMinutes(5), _ => { });

    [Fact]
    public void A_pass_at_idle_with_an_armed_plan_and_a_hold_does_not_start_practice()
    {
        var race = Threaded();
        race.PracticeAutoStart = Plan();
        using var hold = race.HoldPracticeAutoStart();

        race.PostLinePass(Pass("101", 9_000), _ => { });
        Drain();

        Assert.Equal(RaceState.Idle, race.State);
        Assert.False(race.IsPracticeMode);

        // Left armed for when it is allowed again.
        Assert.NotNull(race.PracticeAutoStart);
    }

    [Fact]
    public void After_the_hold_is_disposed_the_next_pass_starts_practice()
    {
        var race = Threaded();
        race.PracticeAutoStart = Plan();
        var hold = race.HoldPracticeAutoStart();

        race.PostLinePass(Pass("101", 9_000), _ => { });
        Drain();
        Assert.Equal(RaceState.Idle, race.State);

        hold.Dispose();

        race.PostLinePass(Pass("101", 21_000), _ => { });
        Drain();

        Assert.Equal(RaceState.Racing, race.State);
        Assert.True(race.IsPracticeMode);
        Assert.Null(race.PracticeAutoStart);
    }

    [Fact]
    public void Disposing_a_hold_twice_lets_go_once()
    {
        var race = Threaded();
        race.PracticeAutoStart = Plan();
        var first = race.HoldPracticeAutoStart();
        var second = race.HoldPracticeAutoStart();

        // Twice: had it let go twice, the second hold would count for nothing.
        first.Dispose();
        first.Dispose();

        race.PostLinePass(Pass("101", 9_000), _ => { });
        Drain();
        Assert.Equal(RaceState.Idle, race.State);
        Assert.False(race.IsPracticeMode);

        second.Dispose();

        race.PostLinePass(Pass("101", 21_000), _ => { });
        Drain();
        Assert.Equal(RaceState.Racing, race.State);
        Assert.True(race.IsPracticeMode);
    }

    [Fact]
    public void Inline_the_hold_is_a_no_op()
    {
        var engine = new RaceControlSession();
        var race = new RaceTiming(engine);

        var hold = race.HoldPracticeAutoStart();
        Assert.NotNull(hold);
        Assert.Same(hold, race.HoldPracticeAutoStart());

        hold.Dispose();
        hold.Dispose();

        // Nothing about the race was touched.
        Assert.Equal(RaceState.Idle, engine.State);
        Assert.False(engine.IsPracticeMode);
    }
}
