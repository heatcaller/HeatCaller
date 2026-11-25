using NAudio.Wave;

namespace HeatCaller.Tests;

/// <summary>
/// The Classic pack's new sounds (25 Nov 2026, Asher picked them from the
/// ElevenLabs set on the listening page). See SoundPacks.
/// </summary>
public sealed class ClassicPackSoundsTests
{
    private static readonly SoundEffectKind[] Replaced =
    {
        SoundEffectKind.GoodLap,
        SoundEffectKind.InvalidLap,
        SoundEffectKind.PersonalBest,
        SoundEffectKind.FastLap,
        SoundEffectKind.Streak,
        SoundEffectKind.LeaderChange,
        SoundEffectKind.JumpStart,
        SoundEffectKind.CountdownStarted,
        SoundEffectKind.CountdownTicks,
    };

    private static readonly SoundEffectKind[] Countdowns = { SoundEffectKind.CountdownStarted, SoundEffectKind.CountdownTicks };

    private static WaveFileReader Open(SoundEffectKind kind)
    {
        var path = SoundPacks.FileFor("Synthwave", kind);
        Assert.NotNull(path);
        return new WaveFileReader(path);
    }

    // --- Classic is the only pack (25 Nov 2026, Asher: "only the classic
    // pack should in the app. get ride of all other sound effect packs") ---

    [Theory]
    [InlineData("Podium")]
    [InlineData("Arcade")]
    [InlineData("Broadcast")]
    [InlineData("PitLane")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Gone")]
    public void Every_pack_a_setting_names_plays_the_classic_sound(string? packKey)
    {
        // Before, a row saved on Pit Lane played Pit Lane, and an empty one
        // the generated tones.
        foreach (var kind in Enum.GetValues<SoundEffectKind>())
        {
            Assert.Equal(SoundPacks.FileFor(SoundPacks.EffectsKey, kind), SoundPacks.FileFor(packKey, kind));
        }

        Assert.NotNull(SoundPacks.FileFor(packKey, SoundEffectKind.GoodLap));
    }

    [Fact]
    public void The_pack_lists_offer_classic_alone()
    {
        var only = Assert.Single(SoundPacks.EffectPacks);

        Assert.Equal("Classic", only.Name);
        Assert.Equal(SoundPacks.EffectsKey, only.Key);
    }

    [Fact]
    public void A_row_saved_on_another_pack_is_read_as_classic()
    {
        var older = new SoundEffectsProfile();
        older.Effects[nameof(SoundEffectKind.PersonalBest)] = new SoundEffectConfig { Enabled = true, BuiltInSound = "PitLane" };
        older.Effects[nameof(SoundEffectKind.GoodLap)] = new SoundEffectConfig { Enabled = true, BuiltInSound = SoundPacks.ClassicKey };
        older.Effects[nameof(SoundEffectKind.FastLap)] = new SoundEffectConfig { Enabled = true, BuiltInSound = "Podium", CustomFilePath = @"C:\club\horn.wav" };

        SoundEffectsStore.FillMissing(older);

        Assert.All(older.Effects.Values, config => Assert.Equal(SoundPacks.EffectsKey, config.BuiltInSound));
        Assert.Equal(@"C:\club\horn.wav", older.Effects[nameof(SoundEffectKind.FastLap)].CustomFilePath);
    }

    [Fact]
    public void Every_soundscape_plays_the_classic_sounds()
    {
        foreach (var theme in Theme.CreateBuiltInDefaults())
        {
            Assert.All(theme.SoundEffects.Effects.Values, config => Assert.Equal(SoundPacks.EffectsKey, config.BuiltInSound));
        }
    }

    [Fact]
    public void The_other_packs_keep_their_crowds_and_nothing_else()
    {
        foreach (var pack in SoundPacks.All.Where(p => p.Key != SoundPacks.EffectsKey))
        {
            var files = Directory.GetFiles(Path.Combine(SoundPacks.Folder, pack.Key), "*.wav").Select(Path.GetFileName).Order().ToArray();

            Assert.Equal(new[] { "Ambience.wav", "CrowdRoar.wav" }, files);
            Assert.NotNull(SoundPacks.AmbienceFileFor(pack.Key));
            Assert.NotNull(SoundPacks.CrowdRoarFileFor(pack.Key));
        }
    }

    [Fact]
    public void Each_new_sound_is_in_the_format_the_other_pack_files_use()
    {
        foreach (var kind in Replaced)
        {
            using var reader = Open(kind);

            Assert.Equal(44_100, reader.WaveFormat.SampleRate);
            Assert.Equal(2, reader.WaveFormat.Channels);
            Assert.Equal(16, reader.WaveFormat.BitsPerSample);
        }
    }

    // WHY (25 Nov 2026, Asher's picks): the ElevenLabs files came with up
    // to two seconds of silence after the sound. Left in, a good lap took
    // 1.7 s to finish when it is heard for 1 s, and a sound that still
    // "plays" holds one of the 8 places for sounds at once. They're trimmed
    // to where the sound ends, as they played on the listening page.
    [Theory]
    [InlineData(SoundEffectKind.GoodLap, 1.2)]
    [InlineData(SoundEffectKind.InvalidLap, 0.7)]
    [InlineData(SoundEffectKind.PersonalBest, 1.6)]
    [InlineData(SoundEffectKind.FastLap, 1.4)]
    [InlineData(SoundEffectKind.Streak, 1.5)]
    [InlineData(SoundEffectKind.LeaderChange, 1.9)]
    [InlineData(SoundEffectKind.JumpStart, 0.7)]
    public void The_lap_sounds_end_where_the_sound_does(SoundEffectKind kind, double longestSeconds)
    {
        using var reader = Open(kind);

        Assert.InRange(reader.TotalTime.TotalSeconds, 0.3, longestSeconds);
    }

    [Fact]
    public void The_countdown_rises_for_about_four_seconds()
    {
        using var reader = Open(SoundEffectKind.CountdownStarted);

        Assert.InRange(reader.TotalTime.TotalSeconds, 3.8, 4.3);
    }

    // WHY (25 Nov 2026, Asher: "use the ticks"): the countdown ticks are on
    // at his club, and while they are, Countdown started is skipped - so
    // the rising tone he picked for the countdown never played. It's the
    // ticks too now. The ticks start CountdownTicksLength before the green,
    // so the file is exactly that long, with the quiet first and the rise
    // last: a 4-second file there would have ended a second before the
    // green.
    [Fact]
    public void The_ticks_are_the_rise_ending_on_the_green()
    {
        using var reader = Open(SoundEffectKind.CountdownTicks);
        var rate = reader.WaveFormat.SampleRate;
        var channels = reader.WaveFormat.Channels;
        var all = new float[(int)(reader.SampleCount * channels)];
        var read = reader.ToSampleProvider().Read(all, 0, all.Length);

        float PeakBetween(double fromSeconds, double toSeconds) =>
            all.Skip((int)(fromSeconds * rate) * channels)
               .Take((int)((toSeconds - fromSeconds) * rate) * channels)
               .Take(read)
               .Max(Math.Abs);

        Assert.Equal(AnnouncementService.CountdownTicksLength.TotalSeconds, reader.TotalTime.TotalSeconds, 2);
        Assert.True(PeakBetween(0, 0.8) < 0.001f, "the ticks should be quiet until the rise");
        Assert.True(PeakBetween(4.0, 4.5) > 0.3f, "the rise should still be going half a second before the green");
    }

    [Fact]
    public void Each_new_lap_sound_is_heard_from_its_first_moment()
    {
        // A lap sound has to start when the car crosses, not a beat later.
        // The countdown's rise starts 55 ms in, which a four-second rise
        // into the start can afford, and the ticks wait for theirs.
        foreach (var kind in Replaced.Except(Countdowns))
        {
            using var reader = Open(kind);
            var samples = reader.ToSampleProvider();
            var first = new float[reader.WaveFormat.SampleRate / 20 * reader.WaveFormat.Channels];
            var read = samples.Read(first, 0, first.Length);

            Assert.True(first.Take(read).Max(Math.Abs) > 0.01f, kind + " is silent for its first 50 ms");
        }
    }

    // WHY (25 Nov 2026, Asher: "replace the crowd sound with this one"):
    // Classic's crowd roar is the ElevenLabs cheer. As it came it was
    // -5.7 LUFS with peaks past full scale, 10 dB over the old roar, and
    // 30 s that the crowd layer loops with a jump where the end meets the
    // start. It's levelled to the old roar's -16 LUFS and its last three
    // seconds crossfaded over its first, so the loop has no seam.
    [Fact]
    public void The_crowd_roar_is_at_the_old_level_and_loops_without_a_seam()
    {
        var path = SoundPacks.CrowdRoarFileFor(SoundPacks.EffectsKey);
        Assert.NotNull(path);
        using var reader = new WaveFileReader(path);
        var channels = reader.WaveFormat.Channels;
        var all = new float[(int)(reader.SampleCount * channels)];
        var read = reader.ToSampleProvider().Read(all, 0, all.Length);
        var half = reader.WaveFormat.SampleRate / 2 * channels;

        static double Rms(IEnumerable<float> samples) => Math.Sqrt(samples.Average(x => (double)x * x));

        var start = Rms(all.Take(half));
        var end = Rms(all.Skip(read - half).Take(half));
        var peak = all.Take(read).Max(Math.Abs);

        Assert.InRange(reader.TotalTime.TotalSeconds, 25, 30);
        Assert.InRange(20 * Math.Log10(end / start), -1.5, 1.5);
        Assert.InRange(peak, 0.1f, 0.5f);
    }
}
