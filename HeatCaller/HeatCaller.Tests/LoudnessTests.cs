using NAudio.Wave;

namespace HeatCaller.Tests;

/// <summary>
/// Lines and imaging measured by how loud they sound, and the imaging
/// taking the announcer's lift over a loud crowd (25 Nov 2026, Asher,
/// steps 4 and 5 of plans/imaging-voice-levels-plan.md). See Loudness,
/// VoiceLevel, ImagingLevel and BroadcastImaging.CrowdBoost.
/// </summary>
public sealed class LoudnessTests : IDisposable
{
    public void Dispose() => BroadcastImaging.CrowdBoost = null;

    private static float[] Tone(double hz, double amplitude, int rate = 44_100, int channels = 1, double seconds = 1)
    {
        var frames = (int)(rate * seconds);
        var samples = new float[frames * channels];

        for (var f = 0; f < frames; f++)
        {
            var value = (float)(Math.Sin(2 * Math.PI * hz * f / rate) * amplitude);
            for (var c = 0; c < channels; c++)
            {
                samples[(f * channels) + c] = value;
            }
        }

        return samples;
    }

    private static byte[] Pcm(float[] samples)
    {
        var pcm = new byte[samples.Length * 2];
        for (var i = 0; i < samples.Length; i++)
        {
            var value = (short)Math.Round(samples[i] * 32767);
            pcm[i * 2] = (byte)(value & 0xFF);
            pcm[(i * 2) + 1] = (byte)((value >> 8) & 0xFF);
        }

        return pcm;
    }

    private static double FlatDb(float[] samples) =>
        10 * Math.Log10(samples.Average(s => (double)s * s));

    [Fact]
    public void A_bright_voice_measures_louder_than_a_deep_one_at_the_same_signal_level()
    {
        // The flat level called these equal; the ear does not.
        var bright = Tone(3_000, 0.2);
        var deep = Tone(150, 0.2);

        Assert.Equal(FlatDb(bright), FlatDb(deep), 1);
        Assert.InRange(Loudness.Db(bright, 44_100, 1)!.Value - Loudness.Db(deep, 44_100, 1)!.Value, 3.2, 4.6);
    }

    [Fact]
    public void A_typical_voice_measures_on_the_old_scale()
    {
        // The calibration keeps TargetDb meaning what it did: in the middle
        // of the voice, the sounding level is within half a dB of the flat.
        var middle = Tone(1_000, 0.2);

        Assert.InRange(Loudness.Db(middle, 44_100, 1)!.Value - FlatDb(middle), -0.5, 0.5);
    }

    [Theory]
    [InlineData(22_050)]
    [InlineData(24_000)]
    [InlineData(48_000)]
    public void The_measure_is_the_same_at_any_sample_rate(int rate)
    {
        var at44 = Loudness.Db(Tone(1_000, 0.2), 44_100, 1)!.Value;

        Assert.Equal(at44, Loudness.Db(Tone(1_000, 0.2, rate), rate, 1)!.Value, 1);
    }

    [Fact]
    public void Stereo_reads_as_mono_does_and_pauses_do_not_pull_a_line_down()
    {
        var mono = Loudness.Db(Tone(800, 0.2), 44_100, 1)!.Value;
        var stereo = Loudness.Db(Tone(800, 0.2, channels: 2), 44_100, 2)!.Value;
        var withPause = Loudness.Db(Tone(800, 0.2).Concat(new float[44_100]).ToArray(), 44_100, 1)!.Value;

        Assert.Equal(mono, stereo, 1);
        Assert.Equal(mono, withPause, 1);
        Assert.Null(Loudness.Db(new float[44_100], 44_100, 1));
    }

    [Fact]
    public void A_bright_line_and_a_deep_one_come_out_sounding_alike()
    {
        // Before, both came out at one flat level, and the bright one sounded
        // louder - Finley about 0.6 dB over Charlie.
        var format = new WaveFormat(24_000, 16, 1);
        var bright = VoiceLevel.Level(Pcm(Tone(2_500, 0.1, 24_000)), format);
        var deep = VoiceLevel.Level(Pcm(Tone(180, 0.1, 24_000)), format);

        Assert.InRange(Loudness.Db(bright, 24_000, 1)!.Value - Loudness.Db(deep, 24_000, 1)!.Value, -0.6, 0.6);
    }

    [Fact]
    public void The_imaging_is_measured_the_way_the_voice_is()
    {
        var piece = Tone(600, 0.2, channels: 2);

        Assert.Equal(Loudness.Db(piece, 44_100, 2)!.Value, ImagingLevel.Db(piece, 2, 44_100)!.Value, 6);
    }

    // --- Step 5: the crowd lift ---

    [Theory]
    [InlineData(1f, 0.8f)]
    [InlineData(1.8f, 1.44f)]
    [InlineData(0.5f, 0.8f)]   // never turned down
    [InlineData(3f, 1.6f)]     // never past the voice's own ×2
    [InlineData(float.NaN, 0.8f)]
    public void A_piece_takes_the_announcer_s_lift_over_the_crowd(float boost, float expected)
    {
        Assert.Equal(expected, ImagingLevel.Volume(80, 1f, boost), 3);
    }

    [Fact]
    public void The_lift_is_read_from_the_announcer_and_never_breaks_a_piece()
    {
        BroadcastImaging.CrowdBoost = () => 1.8f;
        Assert.Equal(1.8f, BroadcastImaging.CrowdBoostNow(), 3);

        BroadcastImaging.CrowdBoost = () => throw new InvalidOperationException("the crowd is gone");
        Assert.Equal(1f, BroadcastImaging.CrowdBoostNow());

        BroadcastImaging.CrowdBoost = null;
        Assert.Equal(1f, BroadcastImaging.CrowdBoostNow());
    }

    [Fact]
    public void The_window_hands_the_imaging_the_announcer_s_own_lift()
    {
        var mainForm = File.ReadAllText(Path.Combine(RepoRoot(), "HeatCaller", "HeatCaller", "MainForm.cs"));

        Assert.Contains("BroadcastImaging.CrowdBoost = _announcer.VoiceBoostProvider;", mainForm);
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
