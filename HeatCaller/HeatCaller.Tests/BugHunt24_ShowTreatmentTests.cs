using System.Reflection;

namespace HeatCaller.Tests;

/// <summary>
/// The show treatment opened while the announcer is part-way through a line
/// (bug hunt 24, 23 Nov 2026).
///
/// SpeakingChanged only fires on a change, and each show's view of whether
/// the announcer is talking started false. So a show opened mid-line - Play
/// again pressed over a line still being read - thought he was silent: its
/// bed sat at full level over the rest of that line, and the wait for quiet
/// took a long line for silence and handed the music back (or freed the show
/// floor) before the replay had been read.
///
/// The announcer is put "mid-line" through his own private BeginSpeaking, as
/// other bug hunt tests reach his internals; nothing is synthesised or
/// played - AudioBus is silent for the whole run (TestSetup).
/// </summary>
public sealed class BugHunt24_ShowTreatmentTests : IDisposable
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    private readonly TempAppData _data = new();

    /// <summary>Low on purpose, as in the other show tests: keeps the arrays small.</summary>
    private const int Rate = 8000;

    public BugHunt24_ShowTreatmentTests()
    {
        ShowOnAir.ResetForTests();
        AdBreak.ResetForTests();
        ShowMusic.Reload();
    }

    public void Dispose()
    {
        ShowOnAir.ResetForTests();
        AdBreak.ResetForTests();
        ShowMusic.Reload();
        _data.Dispose();
    }

    [Fact]
    public async Task BugHunt24_A_show_opened_mid_line_starts_with_its_bed_ducked()
    {
        var (treatment, announcer) = Treatment();

        // What the bed reads at with nobody talking, for the comparison.
        var quiet = await treatment.StartAsync();
        Assert.NotNull(quiet);
        Assert.False(quiet!.Speaking.Now);
        var fullLevel = quiet.BedTargetForTests;
        Assert.True(fullLevel > 0, "the bed should be up at its own level for the read");
        await quiet.FinishAsync(cutShort: true);

        // A line is being read when the next show opens - the edge has gone.
        Begin(announcer);
        Assert.True(announcer.IsTalking);

        var show = await treatment.StartAsync();
        Assert.NotNull(show);

        try
        {
            Assert.True(
                show!.Speaking.Now,
                "the show opened over a line still being read and thought the announcer was silent");

            var duck = Math.Clamp(new AppSettings().ShowBedDuckPercent / 100f, 0.05f, 1f);

            Assert.Equal(fullLevel * duck, show.BedTargetForTests, 3);
        }
        finally
        {
            End(announcer);
            await show!.FinishAsync(cutShort: true);
        }

        Assert.False(ShowOnAir.Playing);
    }

    [Fact]
    public async Task BugHunt24_Waiting_for_the_read_started_mid_line_does_not_return_while_that_line_is_read()
    {
        var (treatment, announcer) = Treatment();

        Begin(announcer);

        using var cut = new CancellationTokenSource();

        var waiting = treatment.WaitForTheReadAsync(ct: cut.Token);

        // Past the 2.5 s grace and the 2.5 s of quiet the old flag would have
        // counted from the start, with the line still going.
        await Task.Delay(TimeSpan.FromSeconds(6.5));

        var returnedEarly = waiting.IsCompleted;

        End(announcer);
        cut.Cancel();

        try
        {
            await waiting;
        }
        catch (OperationCanceledException)
        {
            // Expected: the test stops the wait rather than sitting out the quiet.
        }

        Assert.False(
            returnedEarly,
            "the wait took a line still being read for silence, and the show floor was freed under it");
    }

    private static void Begin(AnnouncementService announcer) =>
        typeof(AnnouncementService).GetMethod("BeginSpeaking", Private)!.Invoke(announcer, new object?[] { null, null });

    private static void End(AnnouncementService announcer) =>
        typeof(AnnouncementService).GetMethod("EndSpeaking", Private)!.Invoke(announcer, null);

    private (ShowTreatment Treatment, AnnouncementService Announcer) Treatment()
    {
        ShowMusicFile("bed.wav", Music(10));

        var announcer = new AnnouncementService(ProfileStore.Load(), VoiceSettingsStore.Load(), SoundEffectsStore.Load());

        return (new ShowTreatment(new AppSettings(), announcer), announcer);
    }

    /// <summary>A tone with a little level variation, as the other show tests make it.</summary>
    private static float[] Music(double seconds)
    {
        var frames = (int)(seconds * Rate);
        var samples = new float[frames];

        var wobble = new Random(7);

        for (var i = 0; i < frames; i++)
        {
            var level = 0.45 * (0.9 + 0.2 * wobble.NextDouble());
            samples[i] = (float)(level * Math.Sin(2 * Math.PI * 220 * i / Rate) * Math.Sqrt(2));
        }

        return samples;
    }

    /// <summary>A 16-bit PCM wav in the club's show music folder, written by hand.</summary>
    private static void ShowMusicFile(string name, float[] samples)
    {
        Directory.CreateDirectory(ShowMusic.Folder);

        var path = Path.Combine(ShowMusic.Folder, name);

        using var file = File.Create(path);
        using var write = new BinaryWriter(file);

        var bytes = samples.Length * 2;

        write.Write("RIFF".ToCharArray());
        write.Write(36 + bytes);
        write.Write("WAVE".ToCharArray());
        write.Write("fmt ".ToCharArray());
        write.Write(16);
        write.Write((short)1);
        write.Write((short)1);
        write.Write(Rate);
        write.Write(Rate * 2);
        write.Write((short)2);
        write.Write((short)16);
        write.Write("data".ToCharArray());
        write.Write(bytes);

        foreach (var sample in samples)
        {
            write.Write((short)Math.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue));
        }
    }
}
