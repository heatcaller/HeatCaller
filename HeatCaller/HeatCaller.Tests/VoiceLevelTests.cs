using NAudio.Wave;

namespace HeatCaller.Tests;

/// <summary>
/// Every spoken line brought to one level as it is decoded (8 Nov 2026), so
/// the Team Principal and the Technical Lead are not heard quieter than the
/// announcer. See VoiceLevel.
/// </summary>
public sealed class VoiceLevelTests
{
    private static readonly WaveFormat Mono = new(24_000, 16, 1);

    /// <summary>A second of a 220 Hz tone at <paramref name="amplitude"/> of full scale, 16-bit.</summary>
    private static byte[] Tone(double amplitude, WaveFormat format)
    {
        var frames = format.SampleRate;
        var pcm = new byte[frames * 2 * format.Channels];

        for (var i = 0; i < frames; i++)
        {
            var value = (short)Math.Round(Math.Sin(2 * Math.PI * 220 * i / format.SampleRate) * amplitude * 32767);

            for (var c = 0; c < format.Channels; c++)
            {
                var at = ((i * format.Channels) + c) * 2;
                pcm[at] = (byte)(value & 0xFF);
                pcm[at + 1] = (byte)((value >> 8) & 0xFF);
            }
        }

        return pcm;
    }

    // By how loud the line sounds since 25 Nov 2026 (see Loudness), the
    // measure VoiceLevel now levels by. A 220 Hz tone reads about 0.7 dB
    // under its flat level on it.
    private static double LevelOf(byte[] pcm, WaveFormat format) =>
        Loudness.Db(pcm, format.SampleRate, format.Channels)!.Value;

    [Fact]
    public void A_nearly_silent_line_is_raised_no_more_than_12_dB()
    {
        // About -37 dBFS: 20 dB short, so it gets the most, not the target.
        var quiet = Tone(0.02, Mono);

        var raised = LevelOf(VoiceLevel.Level(quiet, Mono), Mono) - LevelOf(quiet, Mono);

        Assert.InRange(raised, 11.5, 12.1);
    }

    [Theory]
    [InlineData(0.06)] // about -27 dBFS: the Team Principal on v4
    [InlineData(0.6)]  // about -7 dBFS: hotter than anyone
    public void A_quiet_or_a_hot_line_is_brought_to_the_one_level(double amplitude)
    {
        var levelled = VoiceLevel.Level(Tone(amplitude, Mono), Mono);

        Assert.InRange(LevelOf(levelled, Mono), VoiceLevel.TargetDb - 0.6, VoiceLevel.TargetDb + 0.6);
    }

    [Fact]
    public void Two_voices_9_dB_apart_come_out_together()
    {
        var announcer = VoiceLevel.Level(Tone(0.18, Mono), Mono);
        var principal = VoiceLevel.Level(Tone(0.064, Mono), Mono);

        Assert.InRange(LevelOf(announcer, Mono) - LevelOf(principal, Mono), -1.0, 1.0);
    }

    [Fact]
    public void A_line_already_at_the_level_is_the_same_array()
    {
        var probe = Tone(0.1, Mono);
        var amplitude = 0.1 * Math.Pow(10, (VoiceLevel.TargetDb - LevelOf(probe, Mono)) / 20);
        var line = Tone(amplitude, Mono);

        Assert.Same(line, VoiceLevel.Level(line, Mono));
    }

    [Fact]
    public void Silence_and_other_formats_are_left_alone()
    {
        var silence = new byte[48_000];
        Assert.Same(silence, VoiceLevel.Level(silence, Mono));

        var floats = WaveFormat.CreateIeeeFloatWaveFormat(24_000, 1);
        var data = new byte[4_000];
        Assert.Same(data, VoiceLevel.Level(data, floats));
    }

    [Fact]
    public void Stereo_is_levelled_too_and_stays_the_same_length()
    {
        var stereo = new WaveFormat(44_100, 16, 2);
        // 0.07, not 0.05: by how it sounds (25 Nov 2026) 0.05 is just over
        // 12 dB short, and the most a line is raised is 12.
        var quiet = Tone(0.07, stereo);

        var levelled = VoiceLevel.Level(quiet, stereo);

        Assert.Equal(quiet.Length, levelled.Length);
        Assert.InRange(LevelOf(levelled, stereo), VoiceLevel.TargetDb - 0.6, VoiceLevel.TargetDb + 0.6);
    }

    [Fact]
    public void Kokoro_is_matched_to_the_same_level()
    {
        Assert.Equal(VoiceLevel.TargetDb, KokoroLoudness.TargetDbFor(VoiceProfile.CreateDefault()));
    }
}
