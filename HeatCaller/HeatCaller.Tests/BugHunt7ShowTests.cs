namespace HeatCaller.Tests;

/// <summary>
/// The show (bug hunt 7, 13 Oct 2026).
///
/// A look-back bridge in front of the first segment let that segment's
/// overrun allowance stack on top of the bridge, past the eight seconds
/// ShowBlock.Overrun promises; a show cut short rode the bed down but left
/// a long opener ringing over the countdown; a 5.1 file was accepted as
/// show music and then could not be played at the show; the template tidy
/// cut an author's ellipsis to a full stop and "?!" to "!"; and Skip
/// reported a stop whether or not there was a block to stop.
///
/// Nothing here plays anything - AudioBus is silent for the whole test run
/// (TestSetup).
/// </summary>
public sealed class BugHunt7ShowTests : IDisposable
{
    private readonly TempAppData _data = new();

    /// <summary>Low on purpose, as in ShowAudioTests: every piece below is seconds long.</summary>
    private const int Rate = 8000;

    public BugHunt7ShowTests()
    {
        ShowOnAir.ResetForTests();
        ShowMusic.Reload();
    }

    public void Dispose()
    {
        ShowOnAir.ResetForTests();
        ShowMusic.Reload();
        _data.Dispose();
    }

    // --- The first segment's overrun ---------------------------------------------------------

    [Fact]
    public void A_bridge_in_front_cannot_push_the_first_segment_past_the_overrun_bound()
    {
        // Room is 30 - 3 headroom = 27s. The segment alone is 34s, inside
        // the 8s allowance; with the 2.5s bridge in front it would be 36.5s.
        // The bridge comes off and the segment still goes out.
        var bridge = Bridge(2.5);
        var segment = Item(34);

        var fit = ShowBlock.Fit(new[] { bridge, segment }, TimeSpan.FromSeconds(30), ShowBlockCost.None);

        Assert.Same(segment, Assert.Single(fit.Items));
        Assert.Same(bridge, Assert.Single(fit.Dropped));
        Assert.True(
            fit.Spent <= TimeSpan.FromSeconds(27) + ShowBlock.Overrun,
            $"the block was reckoned at {fit.Spent.TotalSeconds:0.0}s, past room plus the overrun allowance");
    }

    [Fact]
    public void A_bridge_that_still_leaves_the_segment_inside_the_bound_stays_in()
    {
        // 2 + 30 = 32s against 27s of room: an overrun of 5s, inside the
        // allowance, so nothing changes from before.
        var bridge = Bridge(2);
        var segment = Item(30);

        var fit = ShowBlock.Fit(new[] { bridge, segment }, TimeSpan.FromSeconds(30), ShowBlockCost.None);

        Assert.Equal(2, fit.Items.Count);
        Assert.Same(bridge, fit.Items[0]);
        Assert.Same(segment, fit.Items[1]);
        Assert.Empty(fit.Dropped);
    }

    // --- A show cut short ----------------------------------------------------------------------

    [Fact]
    public async Task A_show_cut_short_rides_the_opener_down_too()
    {
        var opener = Clip("opener.wav", Music(4));
        var openerGain = new SmoothGainSampleProvider(new ShowOneShot(opener), 1f) { Target = 0.3f };

        ShowOnAir.Begin();

        var run = new ShowRun(
            Announcer(), _ => { }, new ShowSpeaking(), null, null, Task.CompletedTask, TimeSpan.Zero, 0.22f, null,
            null, openerGain);

        await run.FinishAsync(cutShort: true);

        Assert.Equal(0f, openerGain.Target, 3);
        Assert.False(ShowOnAir.Playing);
    }

    [Fact]
    public async Task A_show_that_ends_normally_leaves_the_opener_to_play_out()
    {
        var opener = Clip("opener.wav", Music(4));
        var openerGain = new SmoothGainSampleProvider(new ShowOneShot(opener), 1f) { Target = 0.3f };

        ShowOnAir.Begin();

        var run = new ShowRun(
            Announcer(), _ => { }, new ShowSpeaking(), null, null, Task.CompletedTask, TimeSpan.Zero, 0.22f, null,
            null, openerGain);

        await run.FinishAsync();

        Assert.Equal(0.3f, openerGain.Target, 3);
        Assert.False(ShowOnAir.Playing);
    }

    // --- Show music the output cannot play -----------------------------------------------------

    [Fact]
    public void A_file_with_more_than_two_channels_is_refused_with_a_reason()
    {
        var path = Wav("surround.wav", new float[Rate * 6], channels: 6);

        var clip = ShowClip.Load(path, out var problem);

        Assert.Null(clip);
        Assert.NotNull(problem);
        Assert.Contains("surround.wav", problem);
        Assert.Contains("6 channels", problem);
    }

    [Fact]
    public void A_stereo_file_still_loads()
    {
        var mono = Music(2);
        var stereo = new float[mono.Length * 2];

        for (var i = 0; i < mono.Length; i++)
        {
            stereo[2 * i] = mono[i];
            stereo[2 * i + 1] = mono[i];
        }

        var clip = ShowClip.Load(Wav("stereo.wav", stereo, channels: 2), out var problem);

        Assert.Null(problem);
        Assert.NotNull(clip);
        Assert.Equal(2, clip!.Channels);
    }

    // --- Skip ----------------------------------------------------------------------------------

    [Fact]
    public void Abandoning_with_no_block_on_air_says_there_was_nothing()
    {
        var treatment = new ShowTreatment(new AppSettings(), Announcer());

        Assert.False(treatment.AbandonBlock());
        Assert.False(treatment.AbandonBlock());
    }

    // --- The template tidy ---------------------------------------------------------------------

    [Theory]
    [InlineData("[excited] And they're off... GO!", "[excited] And they're off... GO!")]
    [InlineData("Sam for the win?!", "Sam for the win?!")]
    [InlineData("Loading race... almost ready!", "Loading race... almost ready!")]
    [InlineData("Great race..", "Great race.")]
    [InlineData("Great race. !", "Great race!")]
    [InlineData("GO!!", "GO!")]
    public void The_tidy_keeps_the_punctuation_an_author_meant(string line, string expected)
    {
        Assert.Equal(expected, AnnouncementText.TidyTemplate(line));
    }

    // --- Helpers -------------------------------------------------------------------------------

    private static AnnouncementService Announcer() =>
        new(ProfileStore.Load(), VoiceSettingsStore.Load(), SoundEffectsStore.Load());

    private static ShowItem Item(double seconds) =>
        ShowItem.Spoken("filler", () => { }, estimate: TimeSpan.FromSeconds(seconds));

    private static ShowItem Bridge(double seconds) =>
        ShowItem.Bridge("still to come", () => { }, estimate: TimeSpan.FromSeconds(seconds));

    private ShowClip Clip(string name, float[] samples)
    {
        var clip = ShowClip.Load(Wav(name, samples), out var problem);

        Assert.Null(problem);

        return clip!;
    }

    /// <summary>A tone with a little level variation, as ShowAudioTests makes it.</summary>
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

    /// <summary>A 16-bit PCM wav in the test's own folder, as ShowAudioTests writes it.</summary>
    private static string Wav(string name, float[] samples, int channels = 1)
    {
        var path = Path.Combine(AppPaths.Root, name);
        Directory.CreateDirectory(AppPaths.Root);

        using var file = File.Create(path);
        using var write = new BinaryWriter(file);

        var bytes = samples.Length * 2;

        write.Write("RIFF".ToCharArray());
        write.Write(36 + bytes);
        write.Write("WAVE".ToCharArray());
        write.Write("fmt ".ToCharArray());
        write.Write(16);
        write.Write((short)1);
        write.Write((short)channels);
        write.Write(Rate);
        write.Write(Rate * channels * 2);
        write.Write((short)(channels * 2));
        write.Write((short)16);
        write.Write("data".ToCharArray());
        write.Write(bytes);

        foreach (var sample in samples)
        {
            write.Write((short)Math.Clamp(sample * short.MaxValue, short.MinValue, short.MaxValue));
        }

        return path;
    }
}
