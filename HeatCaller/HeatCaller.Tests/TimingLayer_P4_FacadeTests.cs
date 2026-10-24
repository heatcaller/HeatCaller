using System.Collections;
using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// P4 of the timing layer (23 Oct 2026): RaceTiming in threaded mode. The
/// engine lives on the timing thread; what leaves it is a copy; events
/// reach the UI context in the order they were raised, and only there;
/// a command the UI thread issues has delivered its own events by the time
/// it returns (as inline); and the start gate runs on the UI without the
/// timing thread ever waiting for it.
/// </summary>
public sealed class TimingLayer_P4_FacadeTests : IDisposable
{
    private readonly TimingHost _host = new();
    private readonly QueueingSynchronizationContext _ui = new();

    public void Dispose() => _host.Dispose();

    /// <summary>A threaded facade made on this test's thread - which is then its "UI thread".</summary>
    private (RaceTiming Race, RaceControlSession Engine) Threaded()
    {
        var engine = new RaceControlSession();
        return (new RaceTiming(engine, _host, _ui, tickOnHost: false), engine);
    }

    private static DirectLapEvent Pass(string car, uint deviceTimeMs) => new()
    {
        TransponderId = car,
        DeviceTimeMs = deviceTimeMs,
        SignalHits = 1,
        SignalStrength = 100,
        Decoder = DecoderConnection.EasyLap,
    };

    /// <summary>Runs <paramref name="action"/> on a thread that is not the UI thread, and waits.</summary>
    private static void OffTheUi(Action action) => OnAnotherThread(action);

    private static void Racing(RaceTiming race)
    {
        race.Stage();
        race.GreenFlag(10_000);
    }

    // --- Copies, not live objects -------------------------------------------------

    [Fact]
    public void Standings_the_leader_and_the_grid_are_copies_made_on_the_timing_thread()
    {
        var (race, engine) = Threaded();
        race.SetGrid(new[] { "101", "202" });
        Racing(race);
        race.ProcessLapEvent(Pass("101", 21_000));
        race.ProcessLapEvent(Pass("101", 33_000));
        race.ProcessLapEvent(Pass("202", 22_000));

        var live = _host.Invoke(() => engine.GetOrderedStandings());
        var copies = race.GetOrderedStandings();

        Assert.Equal(live.Select(s => s.TransponderId), copies.Select(s => s.TransponderId));
        Assert.Equal(live.Select(s => s.Laps), copies.Select(s => s.Laps));

        for (var i = 0; i < live.Count; i++)
        {
            Assert.NotSame(live[i], copies[i]);
            Assert.NotSame(live[i].LapTimesMs, copies[i].LapTimesMs);
            Assert.Equal(live[i].LapTimesMs, copies[i].LapTimesMs);
        }

        // A copy changed changes nothing in the race.
        copies[0].LapTimesMs.Clear();
        copies[0].Laps = 99;
        Assert.NotEqual(99, race.GetOrderedStandings()[0].Laps);

        var leader = race.RaceLeader();
        Assert.NotNull(leader);
        Assert.NotSame(_host.Invoke(() => engine.RaceLeader()), leader);
        Assert.Equal("101", leader!.TransponderId);

        Assert.Equal(new[] { "101", "202" }, race.Grid);
        Assert.NotSame(_host.Invoke(() => engine.Grid), race.Grid);
    }

    [Fact]
    public void A_warning_given_or_taken_back_is_a_copy_and_the_ladder_is_copied_both_ways()
    {
        var (race, engine) = Threaded();
        var ladder = new List<WarningStep> { new(WarningAction.TimePenalty, 5) };
        race.WarningLadder = ladder;
        Racing(race);
        race.ProcessLapEvent(Pass("101", 21_000));

        // The settings page editing its ladder afterwards reaches no race.
        ladder[0].PenaltySeconds = 30;
        Assert.Equal(5, _host.Invoke(() => engine.WarningLadder![0].PenaltySeconds));
        Assert.NotSame(_host.Invoke(() => engine.WarningLadder), race.WarningLadder);
        Assert.Equal(5, race.WarningLadder![0].PenaltySeconds);

        var given = race.IssueWarning("101", "Blocking");
        Assert.NotNull(given);
        Assert.Equal(5_000, given!.PenaltyMs);
        Assert.NotSame(_host.Invoke(() => engine.GetOrderedStandings()[0].Warnings[0]), given);

        var removed = race.RemoveLastWarning("101");
        Assert.NotNull(removed);
        Assert.Equal("Blocking", removed!.Reason);
        Assert.Empty(race.GetOrderedStandings()[0].Warnings);
    }

    [Fact]
    public void A_standing_clone_copies_every_property_deeply()
    {
        var original = new RacerStanding { TransponderId = "101" };
        var number = 1L;

        // Every settable property given a value no default has.
        foreach (var property in typeof(RacerStanding).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.SetMethod is not { IsPublic: true } || property.Name == nameof(RacerStanding.TransponderId))
            {
                continue;
            }

            object? value = property.PropertyType switch
            {
                var t when t == typeof(int) => (int)++number,
                var t when t == typeof(long) => ++number * 1000,
                var t when t == typeof(bool) => true,
                var t when t == typeof(List<long>[]) => new[] { new List<long> { ++number }, new List<long> { ++number } },
                var t when t == typeof(long[]) => new[] { ++number, ++number },
                var t when t == typeof((long StartMs, long EndMs, long?[] Splits)?) => ((long, long, long?[])?)(++number, ++number, new long?[] { ++number, null }),
                _ => throw new InvalidOperationException($"RacerStanding.{property.Name} ({property.PropertyType}) is not covered here or by Clone - add it to both."),
            };

            property.SetValue(original, value);
        }

        original.LapTimesMs.AddRange(new long[] { 1_000, 8_000 });
        original.StreakAtLap.AddRange(new[] { 0, 1 });
        original.RecoveryLapIndexes.Add(1);
        original.SectorsAtLap.AddRange(new long[]?[] { null, new long[] { 3_000, 5_000 } });
        original.Warnings.Add(new RaceWarning { Number = 1, Reason = "Blocking", PenaltyMs = 5_000, RaceTimeMs = 12_000 });

        var copy = original.Clone();

        foreach (var property in typeof(RacerStanding).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var a = property.GetValue(original);
            var b = property.GetValue(copy);

            if (a is IEnumerable sequence and not string)
            {
                Assert.NotSame(a, b);
                Assert.Equal(Flatten(sequence), Flatten((IEnumerable)b!));
            }
            else if (property.PropertyType == typeof((long StartMs, long EndMs, long?[] Splits)?))
            {
                var (startA, endA, splitsA) = original.OpenLastLap!.Value;
                var (startB, endB, splitsB) = copy.OpenLastLap!.Value;
                Assert.Equal((startA, endA), (startB, endB));
                Assert.NotSame(splitsA, splitsB);
                Assert.Equal(splitsA, splitsB);
            }
            else
            {
                Assert.Equal(a, b);
            }
        }

        // The arrays inside the lists are copies too, and so are the warnings.
        Assert.NotSame(original.SectorsAtLap[1], copy.SectorsAtLap[1]);
        Assert.NotSame(original.SplitReadsMs[0], copy.SplitReadsMs[0]);
        Assert.NotSame(original.Warnings[0], copy.Warnings[0]);
        Assert.Equal(original.Warnings[0].Summary(), copy.Warnings[0].Summary());
    }

    private static List<string> Flatten(IEnumerable sequence) =>
        sequence.Cast<object?>()
            .Select(item => item switch
            {
                null => "null",
                RaceWarning warning => warning.Summary(),
                IEnumerable inner and not string => "[" + string.Join(",", Flatten(inner)) + "]",
                _ => item.ToString() ?? string.Empty,
            })
            .ToList();

    // --- Events to the UI ---------------------------------------------------------

    [Fact]
    public void Events_from_another_thread_wait_for_the_ui_and_arrive_there_in_order_as_copies()
    {
        var (race, engine) = Threaded();
        var heard = new List<string>();
        var standings = new List<RacerStanding>();
        var threads = new HashSet<int>();

        race.StateChanged += state => { heard.Add($"state {state}"); threads.Add(Environment.CurrentManagedThreadId); };
        race.FlagChanged += flag => heard.Add($"flag {flag}");
        race.LapProcessed += (standing, valid, _) =>
        {
            heard.Add($"lap {standing.TransponderId} {standing.Laps} {valid}");
            standings.Add(standing);
            threads.Add(Environment.CurrentManagedThreadId);
        };

        // From a thread that is not the UI: nothing is heard until the UI
        // gets round to it.
        OffTheUi(() =>
        {
            race.Stage();
            race.GreenFlag(10_000);
            race.ProcessLapEvent(Pass("101", 21_000));
            race.ProcessLapEvent(Pass("101", 33_000));
            race.ProcessLapEvent(Pass("101", 34_000)); // too soon - not a lap
        });

        Assert.Empty(heard);
        Assert.True(_ui.Pending > 0);

        // One waiting message for all of it, not one per event.
        Assert.Equal(1, _ui.Pending);

        _ui.Pump();

        Assert.Equal(
            new[]
            {
                "state Staging",
                "state Racing",
                "flag Green",
                "lap 101 1 True",
                "lap 101 2 True",
                "lap 101 2 False",
            },
            heard.Where(h => !h.StartsWith("flag None", StringComparison.Ordinal)));

        Assert.Equal(new[] { Environment.CurrentManagedThreadId }, threads);

        // Frozen as they were at each lap, and none of them the race's own.
        Assert.Equal(new[] { 1, 2, 2 }, standings.Select(s => s.Laps));
        var live = _host.Invoke(() => engine.GetOrderedStandings().Single());
        Assert.DoesNotContain(standings, s => ReferenceEquals(s, live));
    }

    [Fact]
    public void A_command_the_ui_issues_has_delivered_its_events_before_it_returns()
    {
        var (race, _) = Threaded();
        var driving = false;
        var heardWhileDriving = new List<RaceState>();
        var heardOtherwise = new List<RaceState>();

        race.StateChanged += state => (driving ? heardWhileDriving : heardOtherwise).Add(state);

        // The demo's DriveSession: a flag set round a call, read by the
        // handler. Inline the handler runs inside the call; it must here too.
        driving = true;
        race.Stage();
        race.GreenFlag(10_000);
        driving = false;

        Assert.Equal(new[] { RaceState.Staging, RaceState.Racing }, heardWhileDriving);
        Assert.Empty(heardOtherwise);
    }

    [Fact]
    public void Events_waiting_for_the_ui_are_delivered_before_a_ui_command_runs()
    {
        var (race, _) = Threaded();
        var heard = new List<string>();
        race.StateChanged += state => heard.Add($"state {state}");
        race.LapProcessed += (standing, _, _) => heard.Add($"lap {standing.Laps}");

        OffTheUi(() =>
        {
            race.Stage();
            race.GreenFlag(10_000);
            race.ProcessLapEvent(Pass("101", 21_000));
        });

        // Not pumped: the UI's own Reset still hears the race's earlier news
        // first, then its own - the order they happened in.
        race.Reset();

        Assert.Equal(new[] { "state Staging", "state Racing", "lap 1", "state Idle" }, heard);
    }

    [Fact]
    public void A_failing_handler_is_logged_and_skipped_and_the_others_still_hear()
    {
        var (race, _) = Threaded();
        var log = new List<string>();
        var second = 0;

        race.LogMessage += log.Add;
        race.StateChanged += _ => throw new InvalidOperationException("screen fault");
        race.StateChanged += _ => second++;

        race.Stage();

        Assert.Equal(1, second);
        Assert.Contains(log, line => line.Contains("StateChanged handler failed", StringComparison.Ordinal)
                                     && line.Contains("screen fault", StringComparison.Ordinal));
    }

    [Fact]
    public void A_handler_taken_off_hears_nothing_more_and_null_handlers_are_ignored()
    {
        var (race, _) = Threaded();
        var heard = 0;
        Action<RaceState> onState = _ => heard++;

        race.StateChanged += onState;
        race.StateChanged += null;
        race.StateChanged -= null;
        race.Stage();
        Assert.Equal(1, heard);

        race.StateChanged -= onState;
        race.Reset();
        _ui.Pump();
        Assert.Equal(1, heard);
    }

    [Fact]
    public void A_timing_side_handler_hears_the_live_race_on_the_timing_thread()
    {
        var (race, engine) = Threaded();
        var onTiming = new List<bool>();
        RacerStanding? heard = null;
        Action<RacerStanding, bool, bool> handler = (standing, _, _) =>
        {
            onTiming.Add(_host.IsTimingThread);
            heard = standing;
        };

        race.AddTimingSideHandler(e => e.LapProcessed += handler);
        Racing(race);
        OffTheUi(() => race.ProcessLapEvent(Pass("101", 21_000)));

        Assert.Equal(new[] { true }, onTiming);
        Assert.Same(_host.Invoke(() => engine.GetOrderedStandings().Single()), heard);

        race.RemoveTimingSideHandler(e => e.LapProcessed -= handler);
        race.ProcessLapEvent(Pass("101", 33_000));
        Assert.Single(onTiming);
    }

    // --- Re-entrancy and undo -------------------------------------------------------

    [Fact]
    public void A_correction_made_through_edit_standing_calls_back_in_and_undoes()
    {
        var (race, _) = Threaded();
        race.WarningLadder = new List<WarningStep> { new(WarningAction.TimePenalty, 5) };
        Racing(race);
        race.ProcessLapEvent(Pass("101", 21_000));
        race.IssueWarning("101", "Blocking");

        // The Clear Penalties button: the change calls the facade from
        // inside the engine (on the timing thread), where it runs inline.
        RaceWarning? removed = null;
        var edit = race.EditStanding("101", () =>
        {
            race.ClearPenalties("101");
            removed = race.RemoveLastWarning("101");
            return true;
        });

        Assert.NotNull(edit);
        Assert.NotNull(removed);
        Assert.Empty(race.GetOrderedStandings()[0].Warnings);
        Assert.Equal(0, race.GetOrderedStandings()[0].PenaltyMs);

        // The undo puts the warning back by reference - ONE warning, not two.
        race.UndoStandingEdit(edit!);
        var after = race.GetOrderedStandings()[0];
        Assert.Single(after.Warnings);
        Assert.Equal(5_000, after.PenaltyMs);
    }

    // --- The start gate -------------------------------------------------------------

    // WHY (23 Oct 2026, timing layer, P5): this test changed on purpose. In
    // P4a the gate was posted to the UI thread (a stopgap); P5 runs it on a
    // pool thread, never the UI's or the timing thread's, so a frozen window
    // cannot hold the green. The screen's part of the start moved to
    // StartRulesOnUi - see TimingLayer_P5_StartTests.
    [Fact]
    public async Task The_start_gate_runs_off_the_ui_and_the_green_waits_for_it_without_blocking_timing()
    {
        var (race, engine) = Threaded();
        var ranOnTiming = true;
        var ranOnUi = true;
        var uiThread = Environment.CurrentManagedThreadId;
        var audio = new TaskCompletionSource();
        Func<Task> gate = async () =>
        {
            ranOnTiming = _host.IsTimingThread;
            ranOnUi = Environment.CurrentManagedThreadId == uiThread;
            await audio.Task;
        };

        race.BeforeGreenFlagAsync = gate;

        // Read back as set (no start rules) - the demo awaits it.
        Assert.Same(gate, race.BeforeGreenFlagAsync);
        Assert.NotSame(gate, _host.Invoke(() => engine.BeforeGreenFlagAsync));

        race.Stage();
        race.StartCountdown();
        var green = race.GreenFlagAfterAudioAsync(30_000);

        // Waiting for the audio: no green yet, and the timing thread is free.
        Assert.False(green.IsCompleted);
        Assert.Equal(RaceState.Countdown, race.State);

        // The audio finishes. The UI is never pumped: the green drops on the
        // timing thread all the same.
        audio.SetResult();
        await green.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(ranOnTiming);
        Assert.False(ranOnUi);
        Assert.Equal(RaceState.Racing, race.State);
        Assert.Equal(FlagState.Green, race.Flag);
    }

    [Fact]
    public async Task A_gate_that_throws_still_lets_the_race_start()
    {
        var (race, _) = Threaded();
        race.BeforeGreenFlagAsync = () => throw new InvalidOperationException("no sound card");
        race.Stage();
        race.StartCountdown();

        var green = race.GreenFlagAfterAudioAsync(30_000);
        _ui.Pump();
        await green.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(RaceState.Racing, race.State);
    }

    // --- Passes straight from the decoder thread ------------------------------------

    // WHY (24 Oct 2026, timing layer, P7): a pass at Idle no longer goes
    // round by the UI (practice starts on the timing side - see
    // TimingLayer_P7_PracticeAutoStartTests), so the two tests that held the
    // passes behind it to that road, and kept it out of a UI command's
    // delivery, went with it; this one now checks an idle pass too goes
    // straight to the timing thread.
    [Fact]
    public void A_line_pass_is_counted_without_the_ui_whether_the_race_is_idle_or_running()
    {
        var (race, _) = Threaded();
        var uiSide = new List<string>();

        // Idle, no practice armed: on the timing thread, where the race
        // ignores it - nothing waits for the UI.
        race.PostLinePass(Pass("101", 9_000), p => uiSide.Add(p.TransponderId));
        _host.Invoke(() => { });
        Assert.Empty(race.GetOrderedStandings());
        Assert.Equal(RaceState.Idle, race.State);

        // Racing: counted on the timing thread with the UI never pumped.
        _ui.Pump();
        Racing(race);
        race.PostLinePass(Pass("101", 21_000), p => uiSide.Add(p.TransponderId));
        race.PostLinePass(Pass("101", 33_000), p => uiSide.Add(p.TransponderId));
        _host.Invoke(() => { });

        Assert.Equal(2, race.GetOrderedStandings().Single().Laps);

        _ui.Pump();
        Assert.Equal(new[] { "101", "101", "101" }, uiSide);
    }

    [Fact]
    public void A_command_issued_inside_a_delivery_delivers_only_its_own_raises_not_news_the_race_made_meanwhile()
    {
        // Review, 23 Oct 2026: what the timing thread raised between the UI
        // starting a nested command and the command running is not the
        // command's own, and waits its turn behind older news.
        var (race, engine) = Threaded();
        var heard = new List<string>();
        var once = false;

        race.Stage();

        race.StateChanged += state =>
        {
            heard.Add($"state {state}");

            if (state == RaceState.Racing && !once)
            {
                once = true;

                // Queued on the timing thread ahead of the command below, and
                // raising while the UI waits for it.
                _host.Post(() =>
                {
                    Thread.Sleep(30);
                    engine.IssueWarning("101", "meanwhile");
                });

                race.YellowFlag();
                heard.Add("yellow returned");
            }
        };
        race.FlagChanged += flag => heard.Add($"flag {flag}");
        race.LapProcessed += (standing, _, _) => heard.Add($"lap {standing.Laps}");
        race.WarningIssued += (_, warning) => heard.Add($"warning {warning.Reason}");

        OffTheUi(() =>
        {
            race.GreenFlag(10_000);
            race.ProcessLapEvent(Pass("101", 21_000));
        });

        _ui.Pump();

        var lap = heard.IndexOf("lap 1");
        Assert.True(lap >= 0, string.Join(" | ", heard));
        Assert.True(heard.IndexOf("flag Yellow") < heard.IndexOf("yellow returned"), string.Join(" | ", heard));
        Assert.True(heard.IndexOf("warning meanwhile") > lap, string.Join(" | ", heard));
    }

    [Fact]
    public void Post_line_pass_is_for_threaded_mode_only()
    {
        var inline = new RaceTiming(new RaceControlSession());

        Assert.False(inline.IsThreaded);
        Assert.Null(inline.Host);
        Assert.Throws<InvalidOperationException>(() => inline.PostLinePass(Pass("101", 1), _ => { }));
    }

    [Fact]
    public void Inline_mode_still_hands_out_the_live_objects_and_fires_events_inside_the_call()
    {
        var engine = new RaceControlSession();
        var race = new RaceTiming(engine);
        var states = new List<RaceState>();
        race.StateChanged += states.Add;
        Racing(race);
        race.ProcessLapEvent(Pass("101", 21_000));

        Assert.Same(engine.GetOrderedStandings().Single(), race.GetOrderedStandings().Single());
        Assert.Same(engine.Grid, race.Grid);
        Assert.Equal(new[] { RaceState.Staging, RaceState.Racing }, states);
        Assert.Null(race.DeliveringEventRaisedAtMs);
    }

    [Fact]
    public void The_moment_an_event_was_raised_is_known_while_it_is_delivered()
    {
        var (race, _) = Threaded();
        long? raisedAt = null;
        race.StateChanged += _ => raisedAt = race.DeliveringEventRaisedAtMs;

        var before = TimingClock.NowMs;
        OffTheUi(race.Stage);
        Thread.Sleep(20);
        _ui.Pump();

        Assert.NotNull(raisedAt);
        Assert.InRange(raisedAt!.Value, before, TimingClock.NowMs - 15);
        Assert.Null(race.DeliveringEventRaisedAtMs);
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
