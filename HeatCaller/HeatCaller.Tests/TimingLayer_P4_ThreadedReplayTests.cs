namespace HeatCaller.Tests;

/// <summary>
/// P4 of the timing layer (23 Oct 2026): the golden races played through a
/// THREADED RaceTiming - passes and ticks through the facade, the race on the
/// timing thread - must end exactly as they do inline, log and all. And a
/// frozen window (a UI context that is never pumped) must not stop laps
/// counting, the time limit or the checkered flag.
/// </summary>
public sealed class TimingLayer_P4_ThreadedReplayTests
{
    private const long Green = TimingLayer_P0_GoldenTests.Green;

    /// <summary>
    /// ReplayHarness.Run, threaded: the same inputs in the same order, a pass
    /// and the tick through the facade (from this thread, its "UI thread"),
    /// a scripted command on the timing thread, and every read through the
    /// facade - copies.
    /// </summary>
    private static ReplayResult RunThreaded(IEnumerable<ReplayInput> delivered, Action<RaceControlSession>? setup)
    {
        var clock = new ReplayClock();
        var context = new ReplayContext(clock);
        var engine = new RaceControlSession();
        clock.Bind(engine);
        setup?.Invoke(engine);

        using var host = new TimingHost();
        var ui = new QueueingSynchronizationContext();
        var race = new RaceTiming(engine, host, ui, tickOnHost: false);

        var log = new List<string>();
        race.LogMessage += log.Add;

        var flagOrder = new List<string>();

        var ordered = delivered
            .OrderBy(i => i.ProcessedMs)
            .ThenBy(i => i.Kind == ReplayKind.Tick ? 1 : 0)
            .ThenBy(i => i.Order)
            .ToList();

        foreach (var input in ordered)
        {
            // Moved on here; read there, after the hand-over to the queue.
            clock.AdvanceTo(input.ProcessedMs);

            switch (input.Kind)
            {
                case ReplayKind.Pass:
                    race.ProcessLapEvent(input.Pass!);
                    break;

                case ReplayKind.Tick:
                    race.Tick();
                    race.PollReleases();
                    break;

                case ReplayKind.Command:
                    host.Invoke(() => input.Apply!(engine, context));
                    break;
            }

            foreach (var standing in race.GetOrderedStandings())
            {
                if (race.HasTakenFlag(standing.TransponderId) && !flagOrder.Contains(standing.TransponderId, StringComparer.OrdinalIgnoreCase))
                {
                    flagOrder.Add(standing.TransponderId);
                }
            }
        }

        // Anything a scripted command raised (it went straight to the engine).
        ui.Pump();

        var rows = race.GetOrderedStandings()
            .Select((s, index) => new ReplayRow(
                s.TransponderId,
                index + 1,
                s.Laps,
                s.FastestLapMs,
                s.ElapsedMs,
                s.PenaltyMs,
                s.Warnings.Count,
                race.HasTakenFlag(s.TransponderId),
                string.Join(" ", s.LapTimesMs)))
            .ToList();

        return new ReplayResult
        {
            Race = engine,
            State = race.State,
            Flag = race.Flag,
            ElapsedMs = (long)race.ElapsedRaceTime.TotalMilliseconds,
            Rows = rows,
            FlagOrder = flagOrder,
            Log = log,
            Skipped = context.Skipped,
        };
    }

    public static IEnumerable<object[]> GoldenRaces()
    {
        yield return new object[] { "alternates" };
        yield return new object[] { "jump start" };
        yield return new object[] { "red flag" };
        yield return new object[] { "time limit" };
        yield return new object[] { "staggered" };
        yield return new object[] { "warnings" };
        yield return new object[] { "undo" };
    }

    private static (ReplayScript Script, Action<RaceControlSession> Rules) Golden(string name) => name switch
    {
        "alternates" => (TimingLayer_P0_GoldenTests.AlternatesHeat(), TimingLayer_P0_GoldenTests.AlternatesRules),
        "jump start" => (TimingLayer_P0_GoldenTests.JumpStartHeat(), TimingLayer_P0_GoldenTests.JumpStartRules),
        "red flag" => (TimingLayer_P0_GoldenTests.RedFlagHeat(), TimingLayer_P0_GoldenTests.FiveLaps),
        "time limit" => (TimingLayer_P0_GoldenTests.TimeLimitHeat(), TimingLayer_P0_GoldenTests.SixtySeconds),
        "staggered" => (TimingLayer_P0_GoldenTests.StaggeredHeat(), TimingLayer_P0_GoldenTests.StaggeredRules),
        "warnings" => (TimingLayer_P0_GoldenTests.WarningsHeat(), TimingLayer_P0_GoldenTests.WarningsRules),
        "undo" => (TimingLayer_P0_GoldenTests.UndoHeat(), TimingLayer_P0_GoldenTests.FiveLaps),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [MemberData(nameof(GoldenRaces))]
    public void A_golden_race_ends_the_same_threaded_as_inline(string name)
    {
        var (script, rules) = Golden(name);
        var inputs = ReplayDelivery.OnTime(script.Inputs, 200_000);

        var inline = ReplayHarness.Run(inputs, rules);

        // A fresh script: the undo heat keeps its reset's moment in itself.
        var (again, _) = Golden(name);
        var threaded = RunThreaded(ReplayDelivery.OnTime(again.Inputs, 200_000), rules);

        Assert.Equal(inline.Describe(), threaded.Describe());

        // The race log too, line for line: every LogMessage reached the UI
        // side, in the order the engine wrote them.
        Assert.Equal(inline.Log, threaded.Log);
    }

    [Fact]
    public void A_golden_race_held_up_by_a_frozen_window_still_ends_the_same_threaded()
    {
        var (script, rules) = Golden("time limit");
        var onTime = ReplayDelivery.OnTime(script.Inputs, 200_000);

        var inline = ReplayHarness.Run(onTime, rules);
        var (again, _) = Golden("time limit");
        var threaded = RunThreaded(ReplayDelivery.WindowFrozen(ReplayDelivery.OnTime(again.Inputs, 200_000), 75_000, 85_000), rules);

        Assert.Equal(inline.Describe(withRaceClock: false), threaded.Describe(withRaceClock: false));
    }

    // --- A frozen window ------------------------------------------------------------

    /// <summary>A clock the test moves and the timing thread reads.</summary>
    private sealed class SharedClock
    {
        private long _nowMs;

        public long NowMs => Interlocked.Read(ref _nowMs);

        public DateTime UtcNow => ReplayClock.DefaultEpoch.AddMilliseconds(NowMs);

        public void Set(long ms) => Interlocked.Exchange(ref _nowMs, ms);
    }

    [Fact]
    public void A_frozen_window_does_not_stop_laps_the_time_limit_or_the_checkered()
    {
        var (script, rules) = Golden("time limit");
        var inline = ReplayHarness.Run(ReplayDelivery.OnTime(script.Inputs, 200_000), rules);

        var clock = new SharedClock();
        var engine = new RaceControlSession
        {
            NowMsSource = () => clock.NowMs,
            UtcNowSource = () => clock.UtcNow,
        };
        rules(engine);

        using var host = new TimingHost();
        var ui = new QueueingSynchronizationContext();

        // Made on a "UI thread" that then stops answering - and the context
        // is never pumped. The clock ticks on the host, in real time.
        RaceTiming race = null!;
        var made = new ManualResetEventSlim();
        var thaw = new ManualResetEventSlim();
        var window = new Thread(() =>
        {
            race = new RaceTiming(engine, host, ui);
            made.Set();
            thaw.Wait();
        }) { IsBackground = true };
        window.Start();
        Assert.True(made.Wait(TimeSpan.FromSeconds(5)));

        var lapsHeard = 0;
        race.LapProcessed += (_, _, _) => lapsHeard++;
        var context = new ReplayContext(new ReplayClock());

        try
        {
            var checkeredSeen = false;

            foreach (var input in script.Inputs)
            {
                // Past the limit (80,000): the host's own tick, and nothing
                // else, must put the checkered out before the next crossing.
                if (!checkeredSeen && input.ArrivedMs > 80_000)
                {
                    clock.Set(80_500);
                    Assert.True(WaitFor(() => race.Flag == FlagState.Checkered), "the checkered never came out");
                    Assert.True(race.IsFinishing);
                    checkeredSeen = true;
                }

                clock.Set(input.ArrivedMs);

                if (input.Kind == ReplayKind.Pass)
                {
                    // Straight from the decoder's thread, as the app does.
                    race.PostLinePass(input.Pass!, _ => { });
                }
                else
                {
                    host.Invoke(() => input.Apply!(engine, context));
                }

                // Counted before the clock moves on, as on the night.
                host.Invoke(() => { });
            }

            Assert.True(WaitFor(() => race.State == RaceState.Finished), "the race never finished");

            var rows = race.GetOrderedStandings()
                .Select((s, index) => new ReplayRow(s.TransponderId, index + 1, s.Laps, s.FastestLapMs, s.ElapsedMs, s.PenaltyMs,
                    s.Warnings.Count, race.HasTakenFlag(s.TransponderId), string.Join(" ", s.LapTimesMs)))
                .ToList();

            Assert.Equal(inline.Rows, rows);

            // And the window heard none of it - it is frozen.
            Assert.Equal(0, lapsHeard);
            Assert.True(ui.Pending > 0);
        }
        finally
        {
            thaw.Set();
        }
    }

    private static bool WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);

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
}
