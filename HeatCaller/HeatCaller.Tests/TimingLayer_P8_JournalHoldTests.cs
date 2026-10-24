using System.Diagnostics;

namespace HeatCaller.Tests;

/// <summary>
/// P8 of the timing layer (24 Oct 2026), item E: with "Run timing on its own
/// thread" on, a journal write held back for the unfiled-race retry is held
/// for RaceJournalRecorder.RetryHoldMs at most. A frozen window used to mean
/// the new race's crash journal was not written at all for as long as the
/// freeze lasted. Now: inside the hold, the window's retry runs and the
/// forced write follows, as before; after it, the race running is journalled
/// without the retry, the console is told once, and a retry the window gets
/// to late does not run (its filing would clear the new race's journal).
/// </summary>
public sealed class TimingLayer_P8_JournalHoldTests : IDisposable
{
    private readonly TimingHost _host = new();
    private readonly QueueingSynchronizationContext _ui = new();
    private readonly TempAppData _data = new();

    public void Dispose()
    {
        _host.Dispose();
        _data.Dispose();
    }

    /// <summary>A threaded facade made on this test's thread - which is then its "UI thread" - ticking on the host, as the app's does.</summary>
    private RaceTiming Threaded() => new(new RaceControlSession(), _host, _ui, tickOnHost: true);

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

    /// <summary>
    /// A recorder with an unfiled race waiting: the retry is due until it has
    /// run (as RaceFilingService.UnfiledRetryDue is, for the race running),
    /// and the retry, like RaceFiling.Commit, clears the journal.
    /// </summary>
    private RaceJournalRecorder WithUnfiledRace(RaceTiming race, Func<bool> due, Action retry, List<string> logged)
    {
        var recorder = new RaceJournalRecorder(race);
        recorder.UseDetails(new RaceJournalDetails("Club oval", "round-1", "Heat 1", false));
        recorder.RetryDueBeforeWrite = due;
        recorder.BeforeEachWrite = () =>
        {
            retry();
            recorder.Journal.Clear();
        };
        recorder.Log = logged.Add;
        recorder.ListenOnTimingSide(ToFrozenUi);
        return recorder;
    }

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

    /// <summary>What the journal file holds now, read as it is; null when there is none.</summary>
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

    // Read with ReadWrite and Delete sharing, as P4b's tests do, so this
    // never makes the timing thread's File.Replace fail on Windows.
    private static string ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static int JournalLaps() => Journaled()?.LapCount ?? -1;

    [Fact]
    public void With_the_window_frozen_the_race_is_journalled_once_the_hold_is_over_and_the_console_is_told_once()
    {
        var race = Threaded();
        var retries = 0;
        var logged = new List<string>();
        WithUnfiledRace(race, () => retries == 0, () => retries++, logged);

        var sinceGreen = Stopwatch.StartNew();
        OnAnotherThread(() =>
        {
            race.Stage();
            race.GreenFlag(10_000);
        });

        // Inside the hold the old race's copy on disk is kept.
        Assert.Null(Journaled());

        // Then the green is written - by the timing tick, with no crossing
        // to prompt it - and not before the hold is over.
        Assert.True(WaitFor(() => Journaled() is { State: RaceState.Racing }), "the held green was never written");
        Assert.True(sinceGreen.ElapsedMilliseconds >= RaceJournalRecorder.RetryHoldMs,
            $"written after {sinceGreen.ElapsedMilliseconds} ms, inside the hold");

        // Every lap from here is written straight away: no second hold for this race.
        OnAnotherThread(() => race.ProcessLapEvent(Pass("101", 21_000)));
        OnAnotherThread(() => race.ProcessLapEvent(Pass("101", 33_000)));
        var expected = race.GetOrderedStandings().Sum(s => s.Laps);
        Assert.True(expected > 0);
        Assert.True(WaitFor(() => JournalLaps() == expected, seconds: 1), $"journal has {JournalLaps()} laps, the race {expected}");

        // Nothing needed the window. When it wakes: one line, and no retry.
        Assert.Empty(logged);
        _ui.Pump();
        Assert.Single(logged);
        Assert.Contains("crash journal is written now", logged[0]);
        Assert.Equal(0, retries);
    }

    [Fact]
    public void A_window_that_wakes_inside_the_hold_runs_the_retry_once_and_the_forced_write_follows()
    {
        var race = Threaded();
        var retries = 0;
        var logged = new List<string>();
        WithUnfiledRace(race, () => retries == 0, () => retries++, logged);

        OnAnotherThread(() =>
        {
            race.Stage();
            race.GreenFlag(10_000);
        });

        Assert.Null(Journaled());

        _ui.Pump();
        Assert.Equal(1, retries);

        Assert.True(WaitFor(() => Journaled() is { State: RaceState.Racing }, seconds: 1), "the write after the retry was never made");

        // Long past the hold, nothing more: no give-up, no second retry.
        Thread.Sleep((int)RaceJournalRecorder.RetryHoldMs + 300);
        OnAnotherThread(() => race.ProcessLapEvent(Pass("101", 21_000)));
        Assert.True(WaitFor(() => JournalLaps() == 1, seconds: 1));
        _ui.Pump();
        Assert.Equal(1, retries);
        Assert.Empty(logged);
    }

    [Fact]
    public void A_retry_the_window_gets_to_after_the_hold_never_runs_and_the_new_race_keeps_its_journal()
    {
        var race = Threaded();
        var retries = 0;
        var logged = new List<string>();
        WithUnfiledRace(race, () => retries == 0, () => retries++, logged);

        OnAnotherThread(() =>
        {
            race.Stage();
            race.GreenFlag(10_000);
        });

        Assert.True(WaitFor(() => Journaled() is { State: RaceState.Racing }), "the held green was never written");
        OnAnotherThread(() => race.ProcessLapEvent(Pass("101", 21_000)));
        Assert.True(WaitFor(() => JournalLaps() == 1, seconds: 1));

        // The window wakes now, with the retry still waiting in its queue.
        _ui.Pump();

        // It did not run: had it, its filing would have cleared the journal,
        // which now holds the race running.
        Assert.Equal(0, retries);
        Assert.Single(logged);
        Thread.Sleep(300);
        Assert.Equal(1, JournalLaps());

        // And the journal goes on being written.
        OnAnotherThread(() => race.ProcessLapEvent(Pass("101", 33_000)));
        Assert.True(WaitFor(() => JournalLaps() == 2, seconds: 1));
        _ui.Pump();
        Assert.Equal(0, retries);
    }

    [Fact]
    public void The_next_race_posts_the_retry_again()
    {
        var race = Threaded();
        var retries = 0;
        var logged = new List<string>();
        var recorder = WithUnfiledRace(race, () => retries == 0, () => retries++, logged);

        OnAnotherThread(() =>
        {
            race.Stage();
            race.GreenFlag(10_000);
            race.ProcessLapEvent(Pass("101", 21_000));
        });

        // Race 1: given up on, journalled without the retry.
        Assert.True(WaitFor(() => Journaled() is { RaceLabel: "Heat 1", State: RaceState.Racing }), "race 1 was never journalled");

        // Race 2 (a new generation): the retry is due again, so its writes
        // are held - the journal still holds race 1.
        recorder.UseDetails(new RaceJournalDetails("Club oval", "round-2", "Heat 2", false));
        OnAnotherThread(() =>
        {
            race.Stage();
            race.GreenFlag(60_000);
        });

        Assert.Equal("Heat 1", Journaled()?.RaceLabel);

        // The window wakes inside race 2's hold: the retry runs once (the
        // one left from race 1 is the one that takes it; whichever runs,
        // it runs once), then race 2 is written.
        _ui.Pump();
        Assert.Equal(1, retries);
        Assert.True(WaitFor(() => Journaled() is { RaceLabel: "Heat 2", State: RaceState.Racing }, seconds: 1),
            "race 2 was not written after its retry");

        _ui.Pump();
        Assert.Equal(1, retries);
        Assert.Single(logged); // race 1's give-up only
    }

    /// <summary>
    /// Runs <paramref name="work"/> on a new thread and waits. Not Task.Run,
    /// which can run inline on the waiting thread (see P4b's tests).
    /// </summary>
    private static void OnAnotherThread(Action work)
    {
        Exception? failed = null;
        var thread = new Thread(() =>
        {
            try
            {
                work();
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
    }
}
