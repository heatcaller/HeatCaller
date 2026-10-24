using System.Text.Json;

namespace HeatCaller.Tests;

/// <summary>
/// P2 of the timing layer (23 Oct 2026): the crash journal is written by
/// RaceJournalRecorder, driven by the session's own events at MainForm level,
/// so it is written whether or not Race Control is there. The screen's
/// details (track, round, typed name, re-run) are read from the page at each
/// write while it is there, and the last ones read stand once it is gone.
/// </summary>
public sealed class TimingLayer_P2_JournalRecorderTests : IDisposable
{
    private readonly TempAppData _data = new();
    private readonly RaceControlSession _race = new();
    private readonly RaceJournalRecorder _recorder;

    public TimingLayer_P2_JournalRecorderTests()
    {
        _recorder = new RaceJournalRecorder(new RaceTiming(_race));
    }

    public void Dispose() => _data.Dispose();

    private string JournalPath => _data.PathOf("race-in-progress.json");

    private RaceJournal.Entry Journalled() =>
        JsonSerializer.Deserialize<RaceJournal.Entry>(File.ReadAllText(JournalPath))!;

    [Fact]
    public void The_journal_is_written_at_the_green_with_no_page_listening()
    {
        // MainForm's part: the session's events, straight through (the test
        // is its own UI thread). Nobody else is subscribed - no Race Control.
        _recorder.Listen(moment => moment());

        _race.Stage();
        _race.GreenFlag(0);

        Assert.True(File.Exists(JournalPath));
        var entry = Journalled();
        Assert.Equal(RaceState.Racing, entry.State);

        // Stamped at the green, as Race Control stamps it.
        Assert.NotNull(entry.SessionStartedAtUtc);
        Assert.Equal(_recorder.SessionStartedAtUtc, entry.SessionStartedAtUtc);
    }

    [Fact]
    public void The_screens_details_are_captured_as_the_race_is_staged_and_started()
    {
        var details = new RaceJournalDetails("Oval", "round-1", "Stock - Q1 Heat 1", IsRerun: true);
        _recorder.DetailsSource = () => details;
        _recorder.Listen(moment => moment());

        _race.Stage();
        _race.GreenFlag(0);

        var entry = Journalled();
        Assert.Equal("Oval", entry.TrackLayoutName);
        Assert.Equal("round-1", entry.RoundId);
        Assert.Equal("Stock - Q1 Heat 1", entry.RaceLabel);
        Assert.True(entry.IsRerun);

        // Changed on the screen: read again at the very next write, as
        // WriteJournal read the screen at every write (23 Oct 2026, timing
        // layer - a capture-only round could go stale).
        details = new RaceJournalDetails("Figure 8", "round-2", "Renamed", IsRerun: false);
        _recorder.Write(force: true);
        Assert.Equal("Renamed", Journalled().RaceLabel);
        Assert.Equal("Figure 8", Journalled().TrackLayoutName);
        Assert.Equal("round-2", Journalled().RoundId);
        Assert.False(Journalled().IsRerun);
    }

    [Fact]
    public void With_the_page_gone_the_last_details_captured_stand()
    {
        _recorder.DetailsSource = () => new RaceJournalDetails("Oval", null, "Friday Main", false);
        _recorder.Listen(moment => moment());
        _race.Stage();

        // Race Control torn down: it takes its source away (UnwireRaceControl).
        _recorder.DetailsSource = null;
        _race.GreenFlag(0);

        Assert.Equal("Friday Main", Journalled().RaceLabel);
    }

    [Fact]
    public void Every_crossing_flag_and_correction_asks_for_a_write()
    {
        var writes = new List<string>();
        _recorder.BeforeEachWrite = () => writes.Add(_race.State.ToString());
        _recorder.Listen(moment => moment());

        _race.Stage();
        _race.GreenFlag(0);
        var afterGreen = writes.Count;
        Assert.True(afterGreen >= 1, "The green flag is a forced write.");

        _race.ProcessLapEvent(Crossing("AAAAAA", 5_000));
        Assert.True(writes.Count > afterGreen, "A crossing asks for a write.");
    }

    [Fact]
    public void Each_write_runs_the_retry_first_and_tells_the_recording_after_even_when_nothing_is_written()
    {
        var order = new List<string>();
        _recorder.BeforeEachWrite = () => order.Add("retry");
        _recorder.Written += () => order.Add("written:" + File.Exists(JournalPath));

        // Idle: Record writes nothing, but the recording still follows - as
        // WriteJournal always called FollowRaceAudio after Record.
        _recorder.Write(force: true);

        Assert.Equal(new[] { "retry", "written:False" }, order);
    }

    [Fact]
    public void Stage_lets_go_of_the_last_races_start_and_the_green_keeps_one_already_stamped()
    {
        _recorder.SessionStartedAtUtc = new DateTime(2026, 10, 23, 18, 0, 0, DateTimeKind.Utc);
        _recorder.OnStateChanged(RaceState.Staging);
        Assert.Null(_recorder.SessionStartedAtUtc);

        // The Green button stamps its own before the green; the state change
        // keeps it ("??=").
        var pressed = new DateTime(2026, 10, 23, 19, 0, 0, DateTimeKind.Utc);
        _recorder.SessionStartedAtUtc = pressed;
        _recorder.OnStateChanged(RaceState.Racing);
        Assert.Equal(pressed, _recorder.SessionStartedAtUtc);

        // A start that stamps nothing (a demo's green) is stamped here.
        _recorder.SessionStartedAtUtc = null;
        _recorder.OnStateChanged(RaceState.Racing);
        Assert.NotNull(_recorder.SessionStartedAtUtc);
    }

    [Fact]
    public void A_crossing_held_back_by_the_throttle_is_written_on_the_clock_tick()
    {
        _recorder.Listen(moment => moment());
        _race.Stage();
        _race.GreenFlag(0);

        _race.ProcessLapEvent(Crossing("AAAAAA", 5_000));
        _recorder.Write(force: true);
        _race.ProcessLapEvent(Crossing("BBBBBB", 5_300));
        Assert.True(_recorder.Journal.IsBehind);

        Thread.Sleep(500);
        _recorder.OnClockTick();

        Assert.False(_recorder.Journal.IsBehind);
        Assert.Contains(Journalled().Standings, s => s.TransponderId == "BBBBBB");
    }

    [Fact]
    public void Practice_and_demo_are_never_journaled()
    {
        _recorder.Listen(moment => moment());
        _race.IsPracticeMode = true;
        _race.Stage();
        _race.GreenFlag(0);

        Assert.False(File.Exists(JournalPath));
    }

    private static DirectLapEvent Crossing(string car, uint atMs) => new()
    {
        TransponderId = car,
        DeviceTimeMs = atMs,
        SignalHits = 1,
        SignalStrength = 100,
        IsSynthetic = true,
    };
}
