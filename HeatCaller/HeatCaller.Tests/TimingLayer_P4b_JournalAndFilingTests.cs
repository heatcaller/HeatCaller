namespace HeatCaller.Tests;

/// <summary>
/// P4b of the timing layer (23 Oct 2026): with "Run timing on its own
/// thread" on, the crash journal is written on the timing side - on every lap
/// and flag, with the window frozen - and the unfiled-race retry it must wait
/// for runs on the UI; journal writes and Clear never overlap; a finished race
/// is filed as it finished, whatever the race has done since; and a lap the
/// window is only told of seconds late is known to be late.
/// </summary>
public sealed class TimingLayer_P4b_JournalAndFilingTests : IDisposable
{
    private readonly TimingHost _host = new();
    private readonly QueueingSynchronizationContext _ui = new();
    private readonly TempAppData _data = new();

    public void Dispose()
    {
        _host.Dispose();
        _data.Dispose();
    }

    /// <summary>A threaded facade made on this test's thread - which is then its "UI thread".</summary>
    private RaceTiming Threaded(bool tickOnHost = false) =>
        new(new RaceControlSession(), _host, _ui, tickOnHost);

    private static DirectLapEvent Pass(string car, uint deviceTimeMs) => new()
    {
        TransponderId = car,
        DeviceTimeMs = deviceTimeMs,
        SignalHits = 1,
        SignalStrength = 100,
        Decoder = DecoderConnection.EasyLap,
    };

    /// <summary>A post to the test's UI context that is never pumped unless the test says so - a frozen window.</summary>
    private bool ToFrozenUi(Action work)
    {
        _ui.Post(_ => work(), null);
        return true;
    }

    private static void OffTheUi(Action action) => OnAnotherThread(action);

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

    private static string JournalPath => Path.Combine(AppPaths.Root, "race-in-progress.json");

    /// <summary>What the journal file holds now, read as it is (RaceJournal.Unfinished passes over a race with no laps yet); null when there is none.</summary>
    private static RaceJournal.Entry? Journaled()
    {
        try
        {
            return File.Exists(JournalPath)
                ? System.Text.Json.JsonSerializer.Deserialize<RaceJournal.Entry>(ReadShared(JournalPath))
                : null;
        }
        catch (IOException)
        {
            return null; // mid-swap; asked again
        }
    }

    // WHY (24 Oct 2026, timing bug hunt): read with ReadWrite and Delete
    // sharing - File.ReadAllText's handle made the timing thread's File.Replace
    // fail on Windows, and the journal then waited 5 s before trying again.
    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static int JournalLaps() => Journaled()?.LapCount ?? -1;

    // --- The journal on the timing side ---------------------------------------------

    [Fact]
    public void The_journal_is_written_on_every_lap_and_flag_with_the_ui_frozen()
    {
        var race = Threaded(tickOnHost: true);
        var recorder = new RaceJournalRecorder(race);
        recorder.UseDetails(new RaceJournalDetails("Club oval", "round-1", "Heat 1", false));
        recorder.ListenOnTimingSide(ToFrozenUi);

        // The race is run from off the UI thread, which never pumps: a frozen window.
        OffTheUi(() =>
        {
            race.Stage();
            race.GreenFlag(10_000);
        });

        // The green is a forced write.
        Assert.True(WaitFor(() => Journaled() is { State: RaceState.Racing }), "the green was not journaled");
        var atGreen = Journaled()!;
        Assert.Equal("Club oval", atGreen.TrackLayoutName);
        Assert.Equal("round-1", atGreen.RoundId);
        Assert.Equal("Heat 1", atGreen.RaceLabel);

        // Two crossings 300 ms apart, then a lap: the throttle holds some
        // back, and the timing tick catches them up - nothing on the UI.
        OffTheUi(() => race.ProcessLapEvent(Pass("101", 21_000)));
        OffTheUi(() => race.ProcessLapEvent(Pass("202", 21_300)));
        OffTheUi(() => race.ProcessLapEvent(Pass("101", 33_000)));

        var expected = race.GetOrderedStandings().Sum(s => s.Laps);
        Assert.True(expected > 0);
        Assert.True(WaitFor(() => JournalLaps() == expected), $"journal has {JournalLaps()} laps, the race {expected}");
        Assert.False(recorder.Journal.IsBehind);

        // The start was stamped on the timing side at the green.
        Assert.NotNull(recorder.SessionStartedAtUtc);

        // Nothing of this needed the window: what it was sent is still waiting.
        Assert.True(_ui.Pending > 0);
    }

    [Fact]
    public void Written_and_the_details_capture_reach_the_ui_by_a_post_never_on_the_timing_thread()
    {
        var race = Threaded();
        var recorder = new RaceJournalRecorder(race);
        var detailsReadOn = new List<bool>();
        var writtenOn = new List<bool>();
        recorder.DetailsSource = () =>
        {
            detailsReadOn.Add(_host.IsTimingThread);
            return new RaceJournalDetails("Track", null, null, false);
        };
        recorder.Written += () => writtenOn.Add(_host.IsTimingThread);
        recorder.ListenOnTimingSide(ToFrozenUi);

        OffTheUi(() =>
        {
            race.Stage();
            race.GreenFlag(10_000);
            race.ProcessLapEvent(Pass("101", 21_000));
        });

        Assert.Empty(detailsReadOn);
        Assert.Empty(writtenOn);

        _ui.Pump();

        Assert.NotEmpty(detailsReadOn);
        Assert.All(detailsReadOn, onTiming => Assert.False(onTiming));

        // One Written waiting at a time, however many writes were made.
        Assert.Single(writtenOn);
        Assert.False(writtenOn[0]);
    }

    [Fact]
    public void A_write_waits_for_the_unfiled_race_retry_on_the_ui_then_is_made()
    {
        var race = Threaded();
        var recorder = new RaceJournalRecorder(race);
        var retried = false;
        var retryRanOnTiming = true;
        recorder.RetryDueBeforeWrite = () => !retried;
        recorder.BeforeEachWrite = () =>
        {
            retryRanOnTiming = _host.IsTimingThread;
            retried = true;
        };
        recorder.ListenOnTimingSide(ToFrozenUi);

        OffTheUi(() =>
        {
            race.Stage();
            race.GreenFlag(10_000);
        });

        // Held: the journal still holds whatever it held (here, nothing) -
        // the next race does not replace the unfiled one before its retry.
        Assert.Null(Journaled());
        Assert.False(retried);

        _ui.Pump();
        Assert.True(retried);
        Assert.False(retryRanOnTiming);

        // Then the write is made, forced, on the timing side.
        Assert.True(WaitFor(() => Journaled() is { State: RaceState.Racing }), "the held write was never made");
    }

    [Fact]
    public async Task A_write_the_ui_asks_for_is_made_on_the_timing_thread_and_never_deadlocks_against_timing_side_writes()
    {
        var race = Threaded(tickOnHost: true);
        var recorder = new RaceJournalRecorder(race);
        var writtenOnUi = 0;
        recorder.Written += () => writtenOnUi++;
        recorder.ListenOnTimingSide(ToFrozenUi);

        race.Stage();
        race.GreenFlag(10_000);

        using var stop = new CancellationTokenSource();
        var laps = Task.Factory.StartNew(() =>
        {
            var t = 21_000u;
            while (!stop.IsCancellationRequested)
            {
                race.ProcessLapEvent(Pass("101", t));
                t += 7_000;
            }
        }, TaskCreationOptions.LongRunning); // its own thread, never run inline (24 Oct 2026)

        // The UI's own writes - restore, undo, closing - while the timing
        // side is writing on every lap.
        for (var i = 0; i < 50; i++)
        {
            recorder.Write(force: true);
        }

        stop.Cancel();
        await laps.WaitAsync(TimeSpan.FromSeconds(5)); // throws if it never stops
        Assert.Equal(50, writtenOnUi);
        Assert.NotNull(Journaled());
    }

    [Fact]
    public async Task Clear_is_safe_against_writes_on_the_timing_thread()
    {
        var race = Threaded(tickOnHost: true);
        var recorder = new RaceJournalRecorder(race);
        recorder.ListenOnTimingSide(ToFrozenUi);

        race.Stage();
        race.GreenFlag(10_000);

        using var stop = new CancellationTokenSource();
        var laps = Task.Factory.StartNew(() =>
        {
            var t = 21_000u;
            while (!stop.IsCancellationRequested)
            {
                race.ProcessLapEvent(Pass("101", t));
                race.AdjustPenaltyTime("101", 1); // forced writes, as fast as they come
                t += 7_000;
            }
        }, TaskCreationOptions.LongRunning); // its own thread, never run inline (24 Oct 2026)

        for (var i = 0; i < 200; i++)
        {
            recorder.Journal.Clear();
        }

        stop.Cancel();
        await laps.WaitAsync(TimeSpan.FromSeconds(5)); // throws if it never stops

        // Once the race stops and the journal is cleared, it stays cleared:
        // a write still in flight cannot land after the Clear it overlapped.
        race.Reset();
        _host.Invoke(() => { });
        recorder.Journal.Clear();
        Thread.Sleep(300);
        Assert.False(File.Exists(JournalPath));
        Assert.False(recorder.Journal.IsBehind);
    }

    [Fact]
    public void Listening_on_the_timing_side_is_for_threaded_mode_only()
    {
        var recorder = new RaceJournalRecorder(new RaceTiming(new RaceControlSession()));
        Assert.Throws<InvalidOperationException>(() => recorder.ListenOnTimingSide(_ => true));
    }

    // --- A finished race, filed as it finished --------------------------------------

    [Fact]
    public void Race_completed_hands_the_ui_the_race_as_it_finished_not_as_it_is_now()
    {
        var race = Threaded();
        FinishedRace? seen = null;
        int? lapsNowAtDelivery = null;
        race.RaceCompleted += () =>
        {
            seen = race.DeliveringFinishedRace;
            lapsNowAtDelivery = race.GetOrderedStandings().Single(s => s.TransponderId == "101").Laps;
        };

        OffTheUi(() =>
        {
            race.LapLimit = 2;
            race.Stage();
            race.GreenFlag(10_000);
            race.ProcessLapEvent(Pass("101", 21_000));
            race.ProcessLapEvent(Pass("101", 33_000));
            race.ProcessLapEvent(Pass("101", 45_000));
        });

        Assert.Equal(RaceState.Finished, race.State);
        var lapsAtFinish = race.GetOrderedStandings().Single().Laps;
        var startAtFinish = race.RaceStartUtc;

        // The window is frozen; meanwhile the race moves on - a lap added by
        // hand after the flag, from the phones.
        OffTheUi(() => race.AdjustLaps("101", 1));
        Assert.Equal(lapsAtFinish + 1, race.GetOrderedStandings().Single().Laps);

        _ui.Pump();

        Assert.NotNull(seen);
        Assert.Equal(lapsAtFinish, seen!.Standings.Single().Laps);
        Assert.Equal(lapsAtFinish + 1, lapsNowAtDelivery);
        Assert.Equal(startAtFinish, seen.RaceStartUtc);
        Assert.False(seen.IsPracticeMode);
        Assert.Equal(race.SessionGeneration, seen.SessionGeneration);

        // Only while it is being delivered.
        Assert.Null(race.DeliveringFinishedRace);
    }

    [Fact]
    public void The_finished_race_is_a_copy_and_knows_who_jumped_the_start()
    {
        var engine = new RaceControlSession();
        engine.Stage();
        engine.GreenFlag(10_000);
        engine.ProcessLapEvent(Pass("101", 21_000));

        var finished = FinishedRace.Of(engine);
        var live = engine.GetOrderedStandings().Single();

        Assert.NotSame(live, finished.Standings.Single());
        Assert.Equal(live.Laps, finished.Standings.Single().Laps);
        Assert.Equal(engine.HasJumpStarted("101"), finished.HasJumpStarted("101"));
        Assert.False(finished.HasJumpStarted("not-in-the-race"));
    }

    [Fact]
    public void Inline_there_is_no_frozen_race_and_no_stale_delivery()
    {
        var race = new RaceTiming(new RaceControlSession());
        FinishedRace? seen = null;
        bool? stale = null;
        race.RaceCompleted += () =>
        {
            seen = race.DeliveringFinishedRace;
            stale = race.DeliveringIsStale;
        };

        race.LapLimit = 1;
        race.Stage();
        race.GreenFlag(10_000);
        race.ProcessLapEvent(Pass("101", 21_000));
        race.ProcessLapEvent(Pass("101", 33_000));

        Assert.Equal(RaceState.Finished, race.State);
        Assert.Null(seen);
        Assert.True(stale == false);
        Assert.Null(race.DeliveringAgeMs);
    }

    // --- Lap calls the window hears of late -----------------------------------------

    [Fact]
    public void A_lap_the_window_is_told_of_more_than_three_seconds_late_is_stale_and_one_told_at_once_is_not()
    {
        var race = Threaded();
        var staleness = new List<(bool Stale, long? AgeMs)>();
        race.LapProcessed += (_, _, _) => staleness.Add((race.DeliveringIsStale, race.DeliveringAgeMs));

        OffTheUi(() =>
        {
            race.Stage();
            race.GreenFlag(10_000);
            race.ProcessLapEvent(Pass("101", 21_000));
        });

        // The window frozen for longer than the rule allows.
        Thread.Sleep((int)RaceTiming.StaleLapCallMs + 200);
        OffTheUi(() => race.ProcessLapEvent(Pass("101", 33_000)));
        _ui.Pump();

        Assert.Equal(2, staleness.Count);
        Assert.True(staleness[0].Stale);
        Assert.True(staleness[0].AgeMs > RaceTiming.StaleLapCallMs);
        Assert.False(staleness[1].Stale);
        Assert.False(race.DeliveringIsStale);
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
