using System.Collections.Concurrent;

namespace HeatCaller.Tests;

/// <summary>
/// Calls timed from the crossing itself (21 Nov 2026). A lap call's say-by
/// was counted from when the timing engine raised the lap, which leaves out
/// how long the pass took to come down the decoder link. The decoder's own
/// clock, mapped onto the app's (DecoderClockMap), says when the car really
/// crossed; that moment now travels with the pass (DirectLapEvent.CrossedMs)
/// through RaceTiming to the announcer (AnnouncementService.LapCrossedAtMs),
/// which uses it when it is plausible and the raise time otherwise.
/// </summary>
public sealed class CrossingTimeAnchorTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    // --- Which decoder-mapped times are kept ------------------------------------

    [Fact]
    public void A_mapped_crossing_just_before_the_pass_arrived_is_kept()
    {
        Assert.Equal(9_400, DirectLapEvent.CrossingOnAppClock(9_400, 10_000));
        Assert.Equal(10_000, DirectLapEvent.CrossingOnAppClock(10_000, 10_000));
    }

    [Fact]
    public void A_mapped_crossing_that_cannot_be_right_is_not_kept()
    {
        // After it arrived, further back than the map's own Jump, or no map at all.
        Assert.Equal(0, DirectLapEvent.CrossingOnAppClock(10_001, 10_000));
        Assert.Equal(0, DirectLapEvent.CrossingOnAppClock(10_000 - DecoderClockMap.Jump - 1, 10_000));
        Assert.Equal(0, DirectLapEvent.CrossingOnAppClock(null, 10_000));
        Assert.Equal(0, DirectLapEvent.CrossingOnAppClock(0, 10_000));
    }

    [Fact]
    public void Every_copy_of_a_pass_keeps_its_crossing_time()
    {
        var lap = new DirectLapEvent { TransponderId = "101", DeviceTimeMs = 5_000 }
            .Stamped(DecoderConnection.EasyLap, 10_000, TimingPoint.Line, 9_700);

        Assert.Equal(9_700, lap.CrossedMs);
        Assert.Equal(9_700, lap.WithTransponder("202").CrossedMs);
        Assert.Equal(9_700, lap.AsSimulated().CrossedMs);

        // Stamped again (the split loop does) without a new crossing: kept.
        Assert.Equal(9_700, lap.Stamped(lap.Decoder, lap.ReceivedMs, TimingPoint.Split1).CrossedMs);
    }

    [Fact]
    public void The_easylap_reader_stamps_each_pass_with_its_mapped_crossing()
    {
        // Source check: the EasyLap HID reader is a live device loop no test
        // can drive, so the line that maps the crossing is checked in place.
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "HeatCaller", "HeatCaller", "DirectDecoderClient.cs"));

        Assert.Contains("DirectLapEvent.CrossingOnAppClock(Clock.ToApp(lap.DeviceTimeMs, arrived), arrived)", source, StringComparison.Ordinal);
    }

    // --- Through the timing layer ------------------------------------------------

    [Fact]
    public void Inline_the_crossing_time_is_known_while_the_lap_is_delivered()
    {
        var clock = 1_000_000L;
        var engine = new RaceControlSession { NowMsSource = () => clock };
        var race = new RaceTiming(engine);
        var seen = new List<long?>();
        race.LapProcessed += (_, _, _) => seen.Add(race.DeliveringLapCrossedAtMs);

        race.Stage();
        race.GreenFlag(10_000);

        const long crossed = 1_234;
        race.ProcessLapEvent(Pass("101", 21_000, receivedMs: clock, crossedMs: crossed));

        Assert.NotEmpty(seen);
        Assert.Equal(crossed, seen[^1]);
        Assert.Null(race.DeliveringLapCrossedAtMs);
    }

    [Fact]
    public void Inline_a_pass_with_no_mapped_crossing_hands_over_none()
    {
        var clock = 1_000_000L;
        var engine = new RaceControlSession { NowMsSource = () => clock };
        var race = new RaceTiming(engine);
        var seen = new List<long?>();
        race.LapProcessed += (_, _, _) => seen.Add(race.DeliveringLapCrossedAtMs);

        race.Stage();
        race.GreenFlag(10_000);
        race.ProcessLapEvent(Pass("101", 21_000, receivedMs: clock, crossedMs: 0));

        Assert.NotEmpty(seen);
        Assert.All(seen, Assert.Null);
    }

    [Fact]
    public void Race_control_hands_the_crossing_to_the_announcer_with_the_raise_time()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "HeatCaller", "HeatCaller", "RaceControlControl.RaceEvents.cs"));

        Assert.Contains("_announcer.LapCrossedAtMs = _race.DeliveringLapCrossedAtMs;", source, StringComparison.Ordinal);
        Assert.Contains("_announcer.LapCrossedAtMs = null;", source, StringComparison.Ordinal);
    }

    // --- The announcer times its calls from it -----------------------------------

    [Fact]
    public void A_call_is_timed_from_the_decoder_crossing_when_it_is_plausible()
    {
        // Raised 5 s ago, crossed 6.5 s ago by the decoder's clock: 6 s from
        // the crossing has already gone, so the split call is not said. From
        // the raise alone it had a second left.
        var (announcer, _, log) = Announcer(AnnouncementEventType.SplitTime);

        announcer.LapRaisedAtMs = TimingClock.NowMs - 5_000;
        announcer.LapCrossedAtMs = TimingClock.NowMs - 6_500;
        announcer.NotifySplitTime(Car("101", 3, 16_000), "the split", 7_000);
        announcer.LapRaisedAtMs = null;
        announcer.LapCrossedAtMs = null;
        announcer.Muted = true;

        Assert.Contains(log, line => line.Contains("Dropped SplitTime", StringComparison.Ordinal));
    }

    [Fact]
    public void A_decoder_crossing_too_far_before_the_raise_is_not_trusted()
    {
        // 2.5 s before the raise is further than DecoderClockMap.Jump: a map
        // that far out is ignored, and the raise time used - still fresh.
        var (announcer, shown, log) = Announcer(AnnouncementEventType.SplitTime);

        announcer.LapRaisedAtMs = TimingClock.NowMs - 5_000;
        announcer.LapCrossedAtMs = TimingClock.NowMs - 7_500;
        announcer.NotifySplitTime(Car("101", 3, 16_000), "the split", 7_000);
        announcer.LapRaisedAtMs = null;
        announcer.LapCrossedAtMs = null;
        announcer.Muted = true;

        Assert.DoesNotContain(log, line => line.Contains("Dropped SplitTime", StringComparison.Ordinal));
        Assert.NotEmpty(shown);
    }

    // --- Helpers ---------------------------------------------------------------

    private static DirectLapEvent Pass(string car, uint deviceTimeMs, long receivedMs, long crossedMs) => new()
    {
        TransponderId = car,
        DeviceTimeMs = deviceTimeMs,
        SignalHits = 1,
        SignalStrength = 100,
        Decoder = DecoderConnection.EasyLap,
        ReceivedMs = receivedMs,
        CrossedMs = crossedMs,
    };

    private static RaceStanding Car(string transponder, int laps, long elapsedMs) => new()
    {
        Transponder = transponder,
        Laps = laps,
        ElapsedMs = elapsedMs,
        Id = transponder,
    };

    private static (AnnouncementService Announcer, ConcurrentQueue<string> Shown, ConcurrentQueue<string> Log) Announcer(params AnnouncementEventType[] on)
    {
        var profiles = ProfileStore.Load();
        profiles.Upsert(new DriverProfile { Transponder = "101", DriverName = "Terry" });
        profiles.Upsert(new DriverProfile { Transponder = "102", DriverName = "Dee" });

        var voice = VoiceSettingsStore.Load();
        foreach (var config in voice.Current.Events.Values)
        {
            config.Enabled = false;
            config.EnabledInPractice = false;
        }

        foreach (var type in on)
        {
            voice.Current.Events[type.ToString()].Enabled = true;
            voice.Current.Events[type.ToString()].EnabledInPractice = true;
        }

        var sounds = SoundEffectsStore.Load();
        foreach (var effect in sounds.Current.Effects.Values)
        {
            effect.Enabled = false;
        }

        var announcer = new AnnouncementService(profiles, voice, sounds);
        var shown = new ConcurrentQueue<string>();
        var log = new ConcurrentQueue<string>();
        announcer.AnnouncementTextReady += shown.Enqueue;
        announcer.StatusMessage += log.Enqueue;

        return (announcer, shown, log);
    }

    /// <summary>The repo root: the folder holding check.cmd, walking up from the test build.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("check.cmd not found above the test build.");
    }
}
