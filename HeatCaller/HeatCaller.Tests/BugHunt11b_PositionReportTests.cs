using System.Diagnostics;
using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 11b (6 Nov 2026), AnnouncementService.LapCalls.cs.
///
/// The position report reads the running order again off the timing
/// thread - from a timer when its wait runs out, and on the voice thread
/// just before it is spoken - while the timing thread is still writing the
/// lap crossings and the board. Those were plain dictionaries with no lock,
/// so a read that caught one growing could throw IndexOutOfRangeException
/// or ArgumentException, which the InvalidOperationException catch let
/// through. Now both go through LapCrossingBook, under one lock.
///
/// A race cannot be forced, so the test hammers the two sides against each
/// other for a fixed time: cars keep joining (the board grows) and laps keep
/// piling up (each car's crossings grow), while another thread reads the
/// running order as fast as it can. Pass is no exception at all from either
/// side; a fresh announcer every round makes the dictionaries grow again.
/// </summary>
public sealed class BugHunt11b_PositionReportTests : IDisposable
{
    private readonly TempAppData _data = new();

    public void Dispose() => _data.Dispose();

    private static readonly MethodInfo RunningOrderNow = typeof(AnnouncementService)
        .GetMethod("RunningOrderNow", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new MissingMethodException("AnnouncementService.RunningOrderNow is gone - this test needs its name updated.");

    [Fact]
    public void The_running_order_can_be_read_while_the_timing_thread_writes_it()
    {
        var profiles = ProfileStore.Load();

        // Nothing said, nothing played: only the bookkeeping runs.
        var voice = VoiceSettingsStore.Load();
        foreach (var config in voice.Current.Events.Values)
        {
            config.Enabled = false;
            config.EnabledInPractice = false;
        }

        var sounds = SoundEffectsStore.Load();
        foreach (var effect in sounds.Current.Effects.Values)
        {
            effect.Enabled = false;
        }

        AnnouncementService NewAnnouncer() => new(profiles, voice, sounds) { Muted = true };

        var current = NewAnnouncer();
        var failures = new List<Exception>();
        var stop = 0;
        var reads = 0;
        var rounds = 0;

        var writer = new Thread(() =>
        {
            try
            {
                while (Volatile.Read(ref stop) == 0)
                {
                    var announcer = NewAnnouncer();
                    Volatile.Write(ref current, announcer);

                    // 30 cars arriving one by one, each on a lap ahead of the
                    // last: the board and every car's crossings keep growing.
                    for (var lap = 1; lap <= 40 && Volatile.Read(ref stop) == 0; lap++)
                    {
                        for (var car = 0; car < Math.Min(30, lap * 2); car++)
                        {
                            announcer.OnStandingUpdated(new RaceStanding
                            {
                                Transponder = "T" + car,
                                Laps = lap,
                                ElapsedMs = lap * 10_000L + car * 37,
                            });
                        }
                    }

                    // And a car taken off the board, as a ghost switched off is.
                    announcer.OnStandingRemoved("T3");
                    rounds++;
                }
            }
            catch (Exception ex)
            {
                lock (failures)
                {
                    failures.Add(ex);
                }
            }
        }) { IsBackground = true, Name = "timing" };

        var reader = new Thread(() =>
        {
            try
            {
                while (Volatile.Read(ref stop) == 0)
                {
                    var announcer = Volatile.Read(ref current);
                    RunningOrderNow.Invoke(announcer, [10]);
                    reads++;
                }
            }
            catch (TargetInvocationException ex)
            {
                lock (failures)
                {
                    failures.Add(ex.InnerException ?? ex);
                }
            }
            catch (Exception ex)
            {
                lock (failures)
                {
                    failures.Add(ex);
                }
            }
        }) { IsBackground = true, Name = "position report" };

        writer.Start();
        reader.Start();

        var clock = Stopwatch.StartNew();
        while (clock.ElapsedMilliseconds < 1_500 && failures.Count == 0)
        {
            Thread.Sleep(20);
        }

        Volatile.Write(ref stop, 1);
        Assert.True(writer.Join(TimeSpan.FromSeconds(10)), "the timing thread did not stop");
        Assert.True(reader.Join(TimeSpan.FromSeconds(10)), "the reading thread did not stop");

        Assert.True(failures.Count == 0, "Reading the running order off the timing thread threw: " + string.Join("\n", failures));
        Assert.True(rounds > 0 && reads > 0, $"the two sides never overlapped ({rounds} rounds, {reads} reads)");
    }

    [Fact]
    public void The_report_reads_the_copy_of_the_board_not_the_live_standings()
    {
        // The stress test above can pass by luck; this pins the shape of the
        // fix so it cannot quietly come undone.
        var source = File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", "AnnouncementService.LapCalls.cs"));
        var start = source.IndexOf("private List<PositionReportOrder.Row>? RunningOrderNow(", StringComparison.Ordinal);
        var end = source.IndexOf("private RaceGap GapBetween(", start, StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start, "RunningOrderNow or GapBetween moved - this test needs its bounds updated.");

        var body = source[start..end];
        var code = string.Join("\n", body.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

        Assert.DoesNotContain("_standings", code, StringComparison.Ordinal);
        Assert.Contains("_lapCrossings.Board", code, StringComparison.Ordinal);
        Assert.Contains("private readonly LapCrossingBook _lapCrossings", source, StringComparison.Ordinal);
    }

    private static string RepoRoot
    {
        get
        {
            var folder = new DirectoryInfo(AppContext.BaseDirectory);

            while (folder is not null)
            {
                if (File.Exists(Path.Combine(folder.FullName, "check.cmd")))
                {
                    return folder.FullName;
                }

                folder = folder.Parent;
            }

            throw new DirectoryNotFoundException("No folder with check.cmd in it above " + AppContext.BaseDirectory);
        }
    }
}
