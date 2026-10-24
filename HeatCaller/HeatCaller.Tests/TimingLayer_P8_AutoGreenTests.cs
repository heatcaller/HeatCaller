namespace HeatCaller.Tests;

/// <summary>
/// P8 of the timing layer, B2 (24 Oct 2026): in threaded mode the automatic
/// start's green after the countdown is asked for on the timing tick
/// (RaceTiming.ArmGreenAfterCountdown), after the countdown audio (capped)
/// and not before its time - never by the window. A frozen window used to
/// hold the green while line passes, reaching the timing thread on time,
/// were booked as jump starts.
/// </summary>
public sealed class TimingLayer_P8_AutoGreenTests : IDisposable
{
    private readonly TimingHost _host = new();

    /// <summary>The window: never pumped unless a test says so.</summary>
    private readonly QueueingSynchronizationContext _ui = new();

    public void Dispose() => _host.Dispose();

    /// <summary>A threaded facade made on this test's thread - which is then its "UI thread" - ticking on the host as the app does.</summary>
    private (RaceTiming Race, RaceControlSession Engine) Threaded()
    {
        var engine = new RaceControlSession();
        var race = new RaceTiming(engine, _host, _ui, tickOnHost: true);

        // The engine's own gate: the countdown audio, already over.
        var gate = new TaskCompletionSource();
        gate.SetResult();
        race.BeforeGreenFlagAsync = () => gate.Task;
        return (race, engine);
    }

    private static DirectLapEvent Pass(string car, uint deviceTimeMs) => new()
    {
        TransponderId = car,
        DeviceTimeMs = deviceTimeMs,
        SignalHits = 1,
        SignalStrength = 100,
        Decoder = DecoderConnection.EasyLap,
    };

    private RaceState StateOnTiming(RaceControlSession engine) => _host.Invoke(() => engine.State);

    private static bool WaitFor(Func<bool> condition, int milliseconds)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(milliseconds);

        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(10);
        }

        return condition();
    }

    [Fact]
    public void An_armed_green_drops_on_the_timing_tick_with_the_ui_never_pumped_and_a_pass_after_it_is_no_jump_start()
    {
        var (race, engine) = Threaded();
        var rulesRan = 0;
        race.StartRulesOnUi = () => rulesRan++;

        race.Stage();
        race.StartCountdown();

        var armedAt = TimingClock.NowMs;
        Assert.True(race.ArmGreenAfterCountdown(TimeSpan.FromMilliseconds(300), _ => Task.CompletedTask, TimeSpan.FromSeconds(5)));

        // The start rules were applied now, on the UI, before the green.
        Assert.Equal(1, rulesRan);
        Assert.Equal(RaceState.Countdown, StateOnTiming(engine));

        // From here the window is frozen: nothing is pumped.
        Assert.True(WaitFor(() => StateOnTiming(engine) == RaceState.Racing, 1_500));
        Assert.True(TimingClock.NowMs - armedAt >= 300);
        Assert.Equal(1, rulesRan);

        race.PostLinePass(Pass("101", 9_000), _ => { });
        _host.Invoke(() =>
        {
            Assert.False(engine.HasJumpStarted("101"));
            Assert.Contains(engine.GetOrderedStandings(), s => s.TransponderId == "101");
        });
    }

    [Fact]
    public void An_audio_wait_that_never_ends_holds_the_green_only_until_its_cap()
    {
        var (race, engine) = Threaded();
        race.Stage();
        race.StartCountdown();

        var armedAt = TimingClock.NowMs;
        Assert.True(race.ArmGreenAfterCountdown(TimeSpan.Zero, _ => new TaskCompletionSource().Task, TimeSpan.FromMilliseconds(500)));

        Assert.True(WaitFor(() => StateOnTiming(engine) == RaceState.Racing, 3_000));
        Assert.InRange(TimingClock.NowMs - armedAt, 450L, 2_500L);
    }

    [Fact]
    public void A_disarmed_green_never_drops_and_its_audio_wait_is_cancelled()
    {
        var (race, engine) = Threaded();
        race.Stage();
        race.StartCountdown();

        var audio = new TaskCompletionSource();
        CancellationToken waitToken = default;
        var started = new ManualResetEventSlim();
        Assert.True(race.ArmGreenAfterCountdown(TimeSpan.FromMilliseconds(300), token =>
        {
            waitToken = token;
            started.Set();
            return audio.Task;
        }, TimeSpan.FromSeconds(10)));

        race.DisarmGreenAfterCountdown();
        audio.SetResult();

        Assert.True(started.Wait(2_000));
        Assert.True(WaitFor(() => waitToken.IsCancellationRequested, 2_000));

        Thread.Sleep(800);
        Assert.Equal(RaceState.Countdown, StateOnTiming(engine));
    }

    [Fact]
    public void A_reset_drops_the_arm_and_the_next_countdown_is_not_greened_by_it()
    {
        var (race, engine) = Threaded();
        race.Stage();
        race.StartCountdown();

        var audio = new TaskCompletionSource();
        Assert.True(race.ArmGreenAfterCountdown(TimeSpan.Zero, _ => audio.Task, TimeSpan.FromSeconds(10)));

        race.Reset();
        Thread.Sleep(300);

        // A new race counting down, then the old arm's audio finishes.
        race.Stage();
        race.StartCountdown();
        audio.SetResult();

        Thread.Sleep(800);
        Assert.Equal(RaceState.Countdown, StateOnTiming(engine));
    }

    [Fact]
    public void A_yellow_before_the_green_is_due_stops_it_with_one_line_in_the_log()
    {
        var (race, engine) = Threaded();
        var log = new List<string>();
        race.LogMessage += log.Add;

        race.Stage();
        race.StartCountdown();
        Assert.True(race.ArmGreenAfterCountdown(TimeSpan.FromMilliseconds(400), _ => Task.CompletedTask, TimeSpan.FromSeconds(5)));
        race.YellowFlag();

        Thread.Sleep(1_000);
        Assert.Equal(RaceState.Countdown, StateOnTiming(engine));
        Assert.Equal(FlagState.Yellow, _host.Invoke(() => engine.Flag));

        // Queued for the window, which hears it when it is next pumped.
        _ui.Pump();
        Assert.Equal(1, log.Count(line => line.Contains("Automatic start stopped at Countdown - the yellow flag is out.")));
    }

    [Fact]
    public void Not_before_is_honored_with_the_audio_already_over()
    {
        var (race, engine) = Threaded();
        race.Stage();
        race.StartCountdown();

        var armedAt = TimingClock.NowMs;
        Assert.True(race.ArmGreenAfterCountdown(TimeSpan.FromMilliseconds(700), null, TimeSpan.FromSeconds(5)));

        Thread.Sleep(350);
        Assert.Equal(RaceState.Countdown, StateOnTiming(engine));

        Assert.True(WaitFor(() => StateOnTiming(engine) == RaceState.Racing, 2_000));
        Assert.True(TimingClock.NowMs - armedAt >= 700);
    }

    [Fact]
    public void Inline_arming_returns_false_and_leaves_the_engine_alone()
    {
        var engine = new RaceControlSession();
        var race = new RaceTiming(engine);
        var asked = false;

        race.Stage();
        race.StartCountdown();
        var generation = engine.SessionGeneration;

        Assert.False(race.ArmGreenAfterCountdown(TimeSpan.Zero, _ =>
        {
            asked = true;
            return Task.CompletedTask;
        }, TimeSpan.FromMilliseconds(100)));
        race.DisarmGreenAfterCountdown();

        Thread.Sleep(300);
        Assert.False(asked);
        Assert.Equal(RaceState.Countdown, engine.State);
        Assert.Equal(generation, engine.SessionGeneration);
    }
}
