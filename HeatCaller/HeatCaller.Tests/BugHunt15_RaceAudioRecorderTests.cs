using NAudio.Wave;

namespace HeatCaller.Tests;

/// <summary>
/// Bug hunt 15 (9 Nov 2026), the race recorder.
///
/// B14: the drift padding measured the recording's length on the wall
/// clock, so a Windows time sync mid-race wrote seconds of silence that
/// never happened and put every later clip out of step with its marks.
///
/// B15: Stop flushed and closed the wav itself even when its wait for the
/// writer thread had run out, so a stalled disk could have the header
/// rewrite and a pending write fight over one stream position.
/// </summary>
public sealed class BugHunt15_RaceAudioRecorderTests : IDisposable
{
    private readonly TempAppData _data = new();

    public BugHunt15_RaceAudioRecorderTests() => RaceAudioStore.OffForTests = false;

    public void Dispose()
    {
        RaceAudioStore.OffForTests = true;
        _data.Dispose();
    }

    [Fact]
    public void The_drift_padding_and_the_captions_are_timed_on_the_stopwatch()
    {
        var source = Source("RaceAudioRecorder.cs");
        var pad = Code(Between(source, "private void PadForDrift()", "public static long SilenceSamplesNeeded("));
        var line = Code(Between(source, "public void NoteLine(string text)", "public void Mixed("));

        Assert.DoesNotContain("DateTime.UtcNow", pad, StringComparison.Ordinal);
        Assert.Contains("ElapsedMs", pad, StringComparison.Ordinal);
        Assert.DoesNotContain("DateTime.UtcNow", line, StringComparison.Ordinal);
        Assert.Contains("ElapsedMs", line, StringComparison.Ordinal);

        var elapsed = Code(Between(source, "private long ElapsedMs", ";"));
        Assert.Contains("Stopwatch", elapsed, StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_writer_thread_flushes_and_closes_the_file()
    {
        var source = Source("RaceAudioRecorder.cs");
        var loop = Code(Between(source, "private void WriteLoop()", "private void FlushAndCheck()"));
        var stop = Code(Between(source, "public RaceAudioRecording Stop()", "private RaceAudioRecording Describe()"));

        var closedInLoop = loop.IndexOf("finally", StringComparison.Ordinal);
        Assert.True(closedInLoop >= 0, "the writer thread no longer closes the file as it ends");
        Assert.Contains("_wav.Dispose();", loop[closedInLoop..], StringComparison.Ordinal);
        Assert.Contains("_wav.Flush();", loop[closedInLoop..], StringComparison.Ordinal);

        // Stop never flushes (a seek to the header) from its own thread, and
        // closes the file only once the writer is known to be finished.
        Assert.DoesNotContain("_wav.Flush()", stop, StringComparison.Ordinal);

        var guard = stop.IndexOf("if (!writerStuck)", StringComparison.Ordinal);
        Assert.True(guard >= 0, "Stop no longer leaves a stuck writer's file alone");
        Assert.True(stop.IndexOf("_wav.Dispose();", StringComparison.Ordinal) > guard);
        Assert.True(stop.IndexOf("_pending.Dispose();", StringComparison.Ordinal) > guard);
    }

    [Fact]
    public void A_recording_that_started_a_moment_ago_is_still_caught_up_and_closed_playable()
    {
        // The Stopwatch anchor is worked out from startedUtc, so a recording
        // whose clock zeroed a few seconds before the file opened is still
        // padded to where it should be - and the file the writer thread
        // closes opens and plays.
        var store = RaceAudioStore.Load();
        store.FolderSetting = Path.Combine(_data.Folder, "audio");

        var said = new List<string>();
        var recorder = RaceAudioRecorder.StartFor(store, "Heat 2", DateTime.UtcNow.AddSeconds(-4), said.Add);

        Assert.NotNull(recorder);

        try
        {
            var mix = Tenth();

            // Two and a half seconds of mix, so the 2-second flush (and its
            // drift check) runs at least once.
            for (var chunk = 0; chunk < 25; chunk++)
            {
                recorder!.Mixed(mix, 0, mix.Length);
                Thread.Sleep(100);
            }

            recorder!.NoteLine("Into turn one");
            var recording = recorder.Stop();

            Assert.Null(recording.Problem);

            // At least the four seconds before the file opened, padded in.
            Assert.True(recording.LengthMs >= 5_500L, $"The file runs {recording.LengthMs}ms.");
            Assert.InRange(recording.Heard!.Single().AtMs, 6_000L, 30_000L);

            using var reader = new WaveFileReader(recording.WavPath);
            Assert.InRange(reader.TotalTime.TotalMilliseconds, recording.LengthMs - 50, recording.LengthMs + 50);
        }
        finally
        {
            recorder?.Dispose();
        }
    }

    private static float[] Tenth()
    {
        var frames = RaceAudioRecorder.SampleRate / 10;
        var buffer = new float[frames * AudioBus.MixChannels];

        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = 0.25f * MathF.Sin(i * 0.05f);
        }

        return buffer;
    }

    // ------------------------------------------------------------------
    //  Reading the source

    private static string Code(string source) =>
        string.Join("\n", source.Split('\n').Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

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

    private static string Source(string fileName) =>
        File.ReadAllText(Path.Combine(RepoRoot, "HeatCaller", "HeatCaller", fileName));

    private static string Between(string source, string from, string until)
    {
        var start = source.IndexOf(from, StringComparison.Ordinal);

        Assert.True(start >= 0, $"\"{from}\" is not in the file any more - this test needs its name updated.");

        var end = source.IndexOf(until, start + from.Length, StringComparison.Ordinal);

        Assert.True(end > start, $"\"{until}\" no longer follows \"{from}\" - this test needs its bounds updated.");

        return source[start..end];
    }
}
