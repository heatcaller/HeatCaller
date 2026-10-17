namespace HeatCaller.Tests;

/// <summary>
/// The show and Race Control (bug hunt 8, 17 Oct 2026).
///
/// A recap show opened on a token already cancelled blipped its opener
/// over the next race's staging call; a muted announcer still got the
/// show's music and stings; a club's own sting played with show music
/// switched off, and at the soundscape's level when it stood in for a
/// stinger that would not load; a soundscape stinger in a block ignored its
/// Out; the recap's caption was lost behind a nameless jingle still counted
/// on air; and a Play again's Release freed the floor out from under the
/// recap sequence.
///
/// Nothing here plays anything - AudioBus is silent for the whole test run
/// (TestSetup).
/// </summary>
public sealed class BugHunt8FATests : IDisposable
{
    private readonly TempAppData _data = new();

    private readonly List<Action<bool>> _watching = new();

    /// <summary>Low on purpose, as in ShowAudioTests: every piece below is seconds long.</summary>
    private const int Rate = 8000;

    public BugHunt8FATests()
    {
        ShowOnAir.ResetForTests();
        ShowFloor.ResetForTests();
        ShowMusic.Reload();
    }

    public void Dispose()
    {
        foreach (var watcher in _watching)
        {
            ShowOnAir.PlayingChanged -= watcher;
        }

        ShowOnAir.ResetForTests();
        ShowFloor.ResetForTests();
        ShowMusic.Reload();
        _data.Dispose();
    }

    // --- A04-1: a show called off before it began ----------------------------------------------

    [Fact]
    public async Task A_show_started_on_a_cancelled_token_plays_nothing_at_all()
    {
        var treatment = Treatment(theme: true, withSting: false);
        var raised = Watch();

        var run = await treatment.StartAsync(SegmentKind.RaceRecap, new CancellationToken(canceled: true));

        Assert.Null(run);
        Assert.Empty(raised);
        Assert.False(ShowOnAir.Playing);
        Assert.Null(treatment.LastBedForTests);
    }

    // --- A05-2: mute is silence -----------------------------------------------------------------

    [Fact]
    public async Task A_muted_announcer_gets_no_show_music()
    {
        var announcer = Announcer();
        announcer.Muted = true;

        var treatment = Treatment(theme: true, withSting: true, announcer);
        var raised = Watch();

        var run = await treatment.StartAsync(SegmentKind.RaceRecap);

        Assert.Null(run);
        Assert.Empty(raised);
        Assert.False(ShowOnAir.Playing);
        Assert.Null(treatment.LastBedForTests);
    }

    [Fact]
    public void A_muted_announcer_gets_no_sting_at_a_boundary()
    {
        var announcer = Announcer();
        announcer.Muted = true;

        var treatment = Treatment(theme: true, withSting: true, announcer);

        Assert.Null(treatment.StingForABoundary());
    }

    // --- A05-6 and A05-3: the club's own sting ---------------------------------------------------

    [Fact]
    public void A_club_with_show_music_off_hears_no_sting_of_its_own()
    {
        var treatment = Treatment(theme: false, withSting: true);

        Assert.Null(treatment.StingForABoundary());
    }

    [Fact]
    public void A_club_sting_plays_at_the_club_s_own_sting_level()
    {
        var treatment = Treatment(theme: true, withSting: true, stingLevel: 65);

        var sting = treatment.StingForABoundary();

        Assert.NotNull(sting);
        Assert.Equal(0.65f, sting!.Level, 3);

        // The club's sting holds for its own length - only a soundscape
        // piece brings an Out.
        Assert.Null(sting.Post);
    }

    // --- A05-5: a mark held to its Out -----------------------------------------------------------

    [Fact]
    public async Task A_mark_with_an_out_is_held_to_its_out_and_not_its_length()
    {
        var treatment = Treatment(theme: true, withSting: false);
        var clip = Clip("long.wav", Music(8));

        var timer = System.Diagnostics.Stopwatch.StartNew();

        await treatment.MarkOnceAsync(clip, 1f, "test", CancellationToken.None, post: TimeSpan.FromSeconds(0.3));

        timer.Stop();

        // Without the Out it would hold for ShowBlock.LongestSting (4s).
        Assert.True(
            timer.Elapsed < TimeSpan.FromSeconds(2),
            $"the mark held for {timer.Elapsed.TotalSeconds:0.0}s - past its Out");
    }

    // --- A05-1: the recap's caption --------------------------------------------------------------

    [Fact]
    public void A_nameless_piece_on_air_gives_way_to_the_first_named_show()
    {
        // The back-from-break jingle, still winding down, then the recap.
        ShowOnAir.Begin();
        ShowOnAir.Begin("Race Recap");

        Assert.Equal("Race Recap", ShowOnAir.Caption);

        ShowOnAir.End();
        ShowOnAir.End();

        Assert.Null(ShowOnAir.Caption);
    }

    [Fact]
    public void A_named_show_on_air_keeps_its_name_when_another_begins()
    {
        ShowOnAir.Begin("Cold Open");
        ShowOnAir.Begin("Race Recap");

        Assert.Equal("Cold Open", ShowOnAir.Caption);

        ShowOnAir.End();
        ShowOnAir.End();
    }

    // --- A04-4: the programme takes the floor ----------------------------------------------------

    [Fact]
    public void The_programme_takes_the_floor_and_the_replay_s_release_does_not_free_it()
    {
        Assert.True(ShowFloor.TryClaim("a segment being played again"));

        ShowFloor.TakeOver("the race recap sequence", TimeSpan.FromMinutes(15));
        ShowFloor.Release("a segment being played again");

        Assert.Equal("the race recap sequence", ShowFloor.HeldBy);

        ShowFloor.Release("the race recap sequence");

        Assert.Null(ShowFloor.HeldBy);
    }

    [Fact]
    public void Taking_over_a_free_floor_is_an_ordinary_claim()
    {
        ShowFloor.TakeOver("the race recap sequence", TimeSpan.FromMinutes(15));

        Assert.True(ShowFloor.Busy);
        Assert.False(ShowFloor.TryClaim("a director segment"));
    }

    // --- Helpers ---------------------------------------------------------------------------------

    private List<bool> Watch()
    {
        var seen = new List<bool>();
        Action<bool> watcher = playing =>
        {
            lock (seen)
            {
                seen.Add(playing);
            }
        };

        ShowOnAir.PlayingChanged += watcher;
        _watching.Add(watcher);

        return seen;
    }

    /// <summary>A treatment with an opener and a bed in the club's folder, and a sting when asked.</summary>
    private ShowTreatment Treatment(bool theme, bool withSting, AnnouncementService? announcer = null, int stingLevel = 80)
    {
        ShowMusicFile("opener.wav", Join(Music(8), FadeOut(Music(2))));
        ShowMusicFile("bed.wav", Join(Music(8), FadeOut(Music(2))));

        if (withSting)
        {
            ShowMusicFile("sting.wav", Music(1.5));
        }

        ShowMusic.Reload();

        return new ShowTreatment(
            new AppSettings
            {
                ShowThemeEnabled = theme,
                ShowStingLevel = stingLevel,
                ShowVoiceEntersMs = 200,
            },
            announcer ?? Announcer())
        {
            Pacing = ShowPacing.ForTests,
        };
    }

    private static AnnouncementService Announcer() =>
        new(ProfileStore.Load(), VoiceSettingsStore.Load(), SoundEffectsStore.Load());

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

    private static float[] FadeOut(float[] piece)
    {
        var faded = new float[piece.Length];

        for (var i = 0; i < piece.Length; i++)
        {
            faded[i] = piece[i] * (1f - (float)i / piece.Length);
        }

        return faded;
    }

    private static float[] Join(params float[][] parts) => parts.SelectMany(p => p).ToArray();

    private static ShowClip Clip(string name, float[] samples)
    {
        var clip = ShowClip.Load(Wav(Path.Combine(AppPaths.Root, name), samples), out var problem);

        Assert.Null(problem);

        return clip!;
    }

    private static string ShowMusicFile(string name, float[] samples)
    {
        Directory.CreateDirectory(ShowMusic.Folder);

        return Wav(Path.Combine(ShowMusic.Folder, name), samples);
    }

    /// <summary>A 16-bit mono PCM wav, written by hand as ShowBlockTests does it.</summary>
    private static string Wav(string path, float[] samples)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

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

        return path;
    }
}
