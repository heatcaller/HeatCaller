namespace HeatCaller.Tests;

/// <summary>
/// Imaging pieces levelled to the announcer, and the Mixer saying how they
/// sit beside it (25 Nov 2026, Asher: "the imaging is way too soft"). See
/// ImagingLevel.
/// </summary>
public sealed class ImagingLevelTests
{
    private static float[] Tone(double amplitude, double seconds = 1, int rate = 44_100, int channels = 2)
    {
        var frames = (int)(rate * seconds);
        var samples = new float[frames * channels];

        for (var f = 0; f < frames; f++)
        {
            var value = (float)(Math.Sin(2 * Math.PI * 440 * f / rate) * amplitude);
            for (var c = 0; c < channels; c++)
            {
                samples[(f * channels) + c] = value;
            }
        }

        return samples;
    }

    private static double HeardDb(float[] samples, float volume) =>
        ImagingLevel.Db(samples.Select(s => s * volume).ToArray(), 2, 44_100)!.Value;

    [Fact]
    public void A_quiet_piece_and_a_loud_one_play_equally_loud()
    {
        // Before, each played at its own file's level times the one number:
        // 18 dB apart here.
        // 0.08 rather than quieter, so the quiet one isn't held back by the
        // 12 dB limit on a raise.
        var quiet = Tone(0.08);
        var loud = Tone(0.4);

        var quietHeard = HeardDb(quiet, ImagingLevel.Volume(80, ImagingLevel.GainFor(ImagingLevel.Db(quiet, 2, 44_100))));
        var loudHeard = HeardDb(loud, ImagingLevel.Volume(80, ImagingLevel.GainFor(ImagingLevel.Db(loud, 2, 44_100))));

        Assert.InRange(quietHeard - loudHeard, -0.5, 0.5);
    }

    [Fact]
    public void At_the_default_a_piece_sits_just_under_the_announcer()
    {
        var piece = Tone(0.2);
        var heard = HeardDb(piece, ImagingLevel.Volume(new ImagingSettings().Level, ImagingLevel.GainFor(ImagingLevel.Db(piece, 2, 44_100))));

        Assert.InRange(VoiceLevel.TargetDb - heard, 1, 4);
    }

    [Fact]
    public void A_fade_or_a_gap_in_a_piece_does_not_pull_its_level_down()
    {
        var loud = Tone(0.3, seconds: 1);
        var withGap = loud.Concat(new float[44_100 * 2 * 2]).ToArray();

        // Within 0.3 dB: the weighting filter rings for a moment where the
        // sound stops, which reads as a little more sound (25 Nov 2026).
        Assert.InRange(ImagingLevel.Db(loud, 2, 44_100)!.Value - ImagingLevel.Db(withGap, 2, 44_100)!.Value, -0.3, 0.3);
    }

    [Fact]
    public void Silence_and_near_silence_are_not_turned_into_noise()
    {
        Assert.Equal(1f, ImagingLevel.GainFor(ImagingLevel.Db(new float[44_100], 2, 44_100)));
        Assert.Equal(1f, ImagingLevel.GainFor(null));

        var faint = ImagingLevel.GainFor(-60);
        Assert.Equal(Math.Pow(10, ImagingLevel.MostRaiseDb / 20), faint, 3);
    }

    [Fact]
    public void The_defaults_are_80_for_the_pieces_and_22_for_the_beds()
    {
        // Nothing pinned them before.
        var settings = new ImagingSettings();

        Assert.Equal(80, settings.Level);
        Assert.Equal(22, settings.BedLevel);
        Assert.Equal(55, settings.BedDuckPercent);
    }

    [Theory]
    [InlineData(0, "Off - no jingles, sweepers, bumpers or stingers play.")]
    [InlineData(100, "The pieces play as loud as the announcer.")]
    [InlineData(80, "The pieces play just under the announcer.")]
    [InlineData(50, "The pieces play quieter than the announcer.")]
    [InlineData(20, "The pieces play well under the announcer - 80 puts them just under.")]
    public void The_mixer_says_how_the_pieces_sit_beside_the_announcer(int level, string words)
    {
        Assert.Equal(words, ImagingLevel.BalanceWords(level));
    }

    [Fact]
    public void Asher_s_20_was_about_14_dB_under_the_announcer()
    {
        Assert.Equal(14.0, ImagingLevel.UnderVoiceDb(20), 1);
        Assert.Equal(1.9, ImagingLevel.UnderVoiceDb(80), 1);
    }

    [Fact]
    public void Every_shipped_piece_is_brought_to_the_announcer_without_a_big_jump()
    {
        // The shipped pieces were made near the announcer's level, so
        // levelling them moves each by a few dB, not a lot.
        var pieces = Directory.GetFiles(BroadcastImaging.DefaultsRoot, "*.mp3", SearchOption.AllDirectories)
            .Where(p => !Path.GetFileName(p).Contains(" Bed ", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(pieces);

        foreach (var path in pieces)
        {
            var clip = ShowClip.Load(path, out var problem);
            Assert.True(clip is not null, path + ": " + problem);

            var db = 20 * Math.Log10(clip!.LevelGain);
            Assert.True(Math.Abs(db) <= 6, $"{Path.GetFileName(path)} moves {db:0.0} dB");
        }
    }

    [Fact]
    public void Pieces_play_through_the_levelling()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "HeatCaller", "HeatCaller", "BroadcastImaging.cs"));

        Assert.DoesNotContain("Level / 100f", source);
        Assert.Contains("ImagingLevel.Volume(Level, piece.Clip.LevelGain, CrowdBoostNow())", source);
        Assert.Contains("ImagingLevel.Volume(Level, clip.LevelGain, CrowdBoostNow())", source);

        // The show plays pieces too: the soundscape jingle that opens it and
        // the stingers between recap parts (missed at first, 25 Nov 2026).
        var show = File.ReadAllText(Path.Combine(RepoRoot(), "HeatCaller", "HeatCaller", "ShowTreatment.cs"));
        Assert.DoesNotContain("BroadcastImaging.Level / 100f", show);
        Assert.Contains("ImagingLevel.Volume(BroadcastImaging.Level, imagingOpener.LevelGain, BroadcastImaging.CrowdBoostNow())", show);
        Assert.Contains("ImagingLevel.Volume(BroadcastImaging.Level, piece.Clip.LevelGain, BroadcastImaging.CrowdBoostNow())", show);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "check.cmd")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Couldn't find check.cmd.");
    }
}
