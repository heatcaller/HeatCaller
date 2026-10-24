namespace HeatCaller.Tests;

/// <summary>
/// P5 of the timing layer (23 Oct 2026): the start, threaded. The screen's
/// start rules are applied on the UI before the green is asked for; the
/// green waits on the timing side for the countdown audio alone, never for
/// the window, and never longer than the ten-second cap; the staggered
/// start's release calls and the ghosts' crossings run off the timing clock.
/// </summary>
public sealed class TimingLayer_P5_StartTests : IDisposable
{
    private readonly TimingHost _host = new();
    private readonly QueueingSynchronizationContext _ui = new();

    public void Dispose() => _host.Dispose();

    /// <summary>A threaded facade made on this test's thread - which is then its "UI thread".</summary>
    private (RaceTiming Race, RaceControlSession Engine) Threaded(bool tickOnHost = false)
    {
        var engine = new RaceControlSession();
        return (new RaceTiming(engine, _host, _ui, tickOnHost), engine);
    }

    private static DirectLapEvent Pass(string car, uint deviceTimeMs) => new()
    {
        TransponderId = car,
        DeviceTimeMs = deviceTimeMs,
        SignalHits = 1,
        SignalStrength = 100,
        Decoder = DecoderConnection.EasyLap,
    };

    private static bool WaitFor(Func<bool> condition, int seconds = 5)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(seconds);

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
    public async Task A_threaded_start_with_a_frozen_ui_goes_green_when_the_audio_ends()
    {
        var (race, _) = Threaded();
        var audio = new TaskCompletionSource();
        race.BeforeGreenFlagAsync = () => audio.Task;

        race.Stage();
        race.StartCountdown();

        // Asked for, then the window freezes: the test never pumps it again.
        var green = race.GreenFlagAfterAudioAsync(30_000);
        Assert.False(green.IsCompleted);

        // The countdown audio finishes, from the sound card's own thread.
        _ = Task.Run(audio.SetResult);

        await green.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(RaceState.Racing, race.State);
        Assert.Equal(FlagState.Green, race.Flag);
    }

    [Fact]
    public async Task A_stuck_sound_card_holds_the_green_no_longer_than_the_cap_with_the_ui_frozen()
    {
        var (race, _) = Threaded();

        // Audio that never ends - and a gate that would ignore its own cap.
        race.BeforeGreenFlagAsync = () => new TaskCompletionSource().Task;

        race.Stage();
        race.StartCountdown();

        var asked = TimingClock.NowMs;
        var green = race.GreenFlagAfterAudioAsync(30_000);

        await green.WaitAsync(RaceTiming.GreenAudioCap + TimeSpan.FromSeconds(3));
        var waited = TimingClock.NowMs - asked;

        Assert.Equal(RaceState.Racing, race.State);
        Assert.InRange(waited, (long)RaceTiming.GreenAudioCap.TotalMilliseconds - 200, (long)RaceTiming.GreenAudioCap.TotalMilliseconds + 2_500);
    }

    [Fact]
    public async Task The_start_rules_run_on_the_ui_before_the_green_is_asked_for_and_the_gate_never_on_timing()
    {
        var (race, engine) = Threaded();
        var order = new List<string>();
        var uiThread = Environment.CurrentManagedThreadId;
        var gateOnTiming = true;

        race.StartRulesOnUi = () =>
        {
            order.Add(Environment.CurrentManagedThreadId == uiThread ? "rules on the ui" : "rules elsewhere");

            // The rules issue commands of their own, as Race Control's do.
            race.SetReleaseSchedule(new Dictionary<string, long> { ["202"] = 2_000 });
        };
        race.BeforeGreenFlagAsync = () =>
        {
            gateOnTiming = _host.IsTimingThread;
            lock (order)
            {
                order.Add("audio wait");
            }

            return Task.CompletedTask;
        };

        race.Stage();
        race.StartCountdown();
        await race.GreenFlagAfterAudioAsync(30_000).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(new[] { "rules on the ui", "audio wait" }, order);
        Assert.False(gateOnTiming);
        Assert.Equal(RaceState.Racing, race.State);
        Assert.Equal(2_000, _host.Invoke(() => engine.ReleaseOffsetMsFor("202")));
    }

    [Fact]
    public async Task A_green_that_resumes_from_a_caution_applies_no_start_rules()
    {
        var (race, _) = Threaded();
        var rulesRan = 0;
        race.StartRulesOnUi = () => rulesRan++;

        race.Stage();
        race.GreenFlag(10_000);
        race.YellowFlag();

        await race.GreenFlagAfterAudioAsync(30_000).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(0, rulesRan);
        Assert.Equal(FlagState.Green, race.Flag);
    }

    [Fact]
    public async Task Start_rules_that_throw_never_stop_the_race_starting()
    {
        var (race, _) = Threaded();
        race.StartRulesOnUi = () => throw new InvalidOperationException("a control has gone");

        race.Stage();
        race.StartCountdown();
        await race.GreenFlagAfterAudioAsync(30_000).WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(RaceState.Racing, race.State);
    }

    [Fact]
    public async Task A_get_of_the_gate_answers_the_start_rules_then_the_audio_as_inline_for_the_demo()
    {
        var (race, _) = Threaded();
        var order = new List<string>();
        Func<Task> audio = () =>
        {
            order.Add("audio");
            return Task.CompletedTask;
        };

        race.BeforeGreenFlagAsync = audio;
        Assert.Same(audio, race.BeforeGreenFlagAsync);

        race.StartRulesOnUi = () => order.Add("rules");
        var gate = race.BeforeGreenFlagAsync;
        Assert.NotNull(gate);
        Assert.NotSame(audio, gate);

        await gate!();
        Assert.Equal(new[] { "rules", "audio" }, order);

        race.StartRulesOnUi = null;
        Assert.Same(audio, race.BeforeGreenFlagAsync);
    }

    [Fact]
    public void Start_rules_and_the_timing_tick_are_threaded_mode_only()
    {
        var inline = new RaceTiming(new RaceControlSession());

        Assert.Throws<InvalidOperationException>(() => inline.StartRulesOnUi = () => { });
        Assert.Throws<InvalidOperationException>(() => inline.AddTimingTick(() => { }));
        Assert.Null(inline.StartRulesOnUi);
    }

    [Fact]
    public void Inline_the_gate_is_the_engines_own_and_a_green_waits_for_it_inside_the_call()
    {
        var engine = new RaceControlSession();
        var race = new RaceTiming(engine);
        var ranOn = -1;
        Func<Task> gate = () =>
        {
            ranOn = Environment.CurrentManagedThreadId;
            return Task.CompletedTask;
        };

        race.BeforeGreenFlagAsync = gate;
        Assert.Same(gate, engine.BeforeGreenFlagAsync);
        Assert.Same(gate, race.BeforeGreenFlagAsync);

        race.Stage();
        race.StartCountdown();
        var green = race.GreenFlagAfterAudioAsync(30_000);

        // Inline, a gate that finishes at once means the green has dropped
        // by the time the call returns, on this thread.
        Assert.True(green.IsCompleted);
        Assert.Equal(Environment.CurrentManagedThreadId, ranOn);
        Assert.Equal(RaceState.Racing, race.State);
    }

    // --- Releases and ghosts off the timing clock -----------------------------------

    [Fact]
    public void A_staggered_release_is_called_on_the_timing_clock_with_the_ui_frozen()
    {
        var (race, _) = Threaded(tickOnHost: true);
        var released = new List<string>();
        var releasedOnTiming = true;
        race.AddTimingSideHandler(e => e.DriverReleased += (transponder, _) =>
        {
            releasedOnTiming &= _host.IsTimingThread;
            lock (released)
            {
                released.Add(transponder);
            }
        });

        race.StartType = RaceStartType.StaggeredGrid;
        race.Stage();
        race.SetGrid(new[] { "101", "202" });
        race.SetReleaseSchedule(new Dictionary<string, long> { ["101"] = 0, ["202"] = 300 });
        race.GreenFlag(10_000);

        // Nothing pumps the UI from here: the release is the timing tick's.
        Assert.True(WaitFor(() => { lock (released) { return released.Contains("202"); } }), "the release was never called");
        Assert.True(releasedOnTiming);

        lock (released)
        {
            // The car on zero goes on the green and is never "released".
            Assert.Equal(new[] { "202" }, released);
        }
    }

    [Fact]
    public void A_ghost_crossing_is_counted_from_its_own_thread_only_while_racing()
    {
        var (race, _) = Threaded();
        var ghost = new DirectLapEvent
        {
            TransponderId = Ghosts.Prefix + "1",
            DeviceTimeMs = 12_000,
            SignalHits = 1,
            SignalStrength = 100,
            IsSynthetic = true,
            ReceivedMs = TimingClock.NowMs,
        };

        // Not racing: refused, as Deliver's State check refuses it.
        Assert.False(OnAnotherThread(() => race.ProcessLapEventWhileRacing(ghost)));

        race.Stage();
        race.GreenFlag(10_000);

        // The ghosts' timer thread; the UI is never pumped.
        Assert.True(OnAnotherThread(() => race.ProcessLapEventWhileRacing(ghost)));
        Assert.Contains(race.GetOrderedStandings(), s => s.TransponderId == ghost.TransponderId);
    }

    /// <summary>
    /// Runs <paramref name="work"/> on a new thread and waits. Not Task.Run:
    /// waited on from a thread-pool thread (where xUnit runs tests with
    /// parallelization off), Task.Run's task can be run inline on the very
    /// same thread, which is then not "another thread" at all (24 Oct 2026,
    /// timing bug hunt).
    /// </summary>
    private static T OnAnotherThread<T>(Func<T> work)
    {
        T result = default!;
        Exception? failed = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = work();
            }
            catch (Exception ex)
            {
                failed = ex;
            }
        });

        thread.Start();
        thread.Join();

        if (failed is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failed).Throw();
        }

        return result;
    }

    private static void OnAnotherThread(Action work) => OnAnotherThread(() => { work(); return 0; });
}
