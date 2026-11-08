namespace HeatCaller.Tests;

/// <summary>
/// Kokoro raised to the level of the ElevenLabs voice beside it, and "A-Main"
/// said as the letter (8 Nov 2026). See KokoroLoudness and
/// RaceNumbers.SpokenRoundNames.
/// </summary>
public sealed class KokoroLoudnessTests
{
    /// <summary>A second of a 220 Hz tone at <paramref name="amplitude"/> of full scale, 16-bit mono.</summary>
    private static byte[] Tone(double amplitude, int sampleRate = 24_000)
    {
        var pcm = new byte[sampleRate * 2];

        for (var i = 0; i < sampleRate; i++)
        {
            var value = (short)Math.Round(Math.Sin(2 * Math.PI * 220 * i / sampleRate) * amplitude * 32767);
            pcm[i * 2] = (byte)(value & 0xFF);
            pcm[(i * 2) + 1] = (byte)((value >> 8) & 0xFF);
        }

        return pcm;
    }

    private static double Peak(byte[] pcm)
    {
        var peak = 0;

        for (var i = 0; i + 1 < pcm.Length; i += 2)
        {
            peak = Math.Max(peak, Math.Abs((int)BitConverter.ToInt16(pcm, i)));
        }

        return peak / 32768.0;
    }

    [Fact]
    public void A_quiet_line_is_raised_to_the_target()
    {
        // A sine at 0.08 is about -24.9 dBFS RMS.
        var louder = KokoroLoudness.Match(Tone(0.08), 24_000, KokoroLoudness.StandardDb);

        Assert.InRange(KokoroLoudness.SpeechDb(louder, 24_000)!.Value, KokoroLoudness.StandardDb - 0.3, KokoroLoudness.StandardDb + 0.3);
    }

    [Fact]
    public void A_line_already_at_the_level_is_left_alone()
    {
        var loud = Tone(0.3);

        Assert.Same(loud, KokoroLoudness.Match(loud, 24_000, KokoroLoudness.StandardDb));
    }

    [Fact]
    public void Raising_a_line_never_takes_it_past_full_scale()
    {
        // Peaky enough that the straight gain would clip.
        var louder = KokoroLoudness.Match(Tone(0.3), 24_000, -3);

        Assert.True(Peak(louder) <= 1.0);
    }

    [Theory]
    [InlineData(0.5, 0.5)]
    [InlineData(-0.6, -0.6)]
    public void Below_the_knee_the_limiter_changes_nothing(double sample, double expected)
    {
        Assert.Equal(expected, KokoroLoudness.Limit(sample), 9);
    }

    [Theory]
    [InlineData(1.4)]
    [InlineData(-3.0)]
    [InlineData(10.0)]
    public void Above_the_knee_the_limiter_stays_under_full_scale(double sample)
    {
        Assert.True(Math.Abs(KokoroLoudness.Limit(sample)) <= 1.0);
    }

    [Fact]
    public void Silence_is_left_as_it_is()
    {
        var silence = new byte[48_000];

        Assert.Null(KokoroLoudness.SpeechDb(silence, 24_000));
        Assert.Same(silence, KokoroLoudness.Match(silence, 24_000, KokoroLoudness.StandardDb));
    }

    [Fact]
    public void Kokoro_matches_the_model_it_stands_in_for()
    {
        var v3 = VoiceProfile.CreateDefault();
        v3.Engine = VoiceEngine.ElevenLabs;
        v3.ModelId = "eleven_v3";
        Assert.Equal(KokoroLoudness.ExpressiveDb, KokoroLoudness.TargetDbFor(v3));

        var flash = VoiceProfile.CreateDefault();
        flash.Engine = VoiceEngine.ElevenLabs;
        flash.ModelId = "eleven_flash_v2_5";
        Assert.Equal(KokoroLoudness.StandardDb, KokoroLoudness.TargetDbFor(flash));

        var kokoro = VoiceProfile.CreateDefault();
        kokoro.Engine = VoiceEngine.Kokoro;
        kokoro.ModelId = "eleven_v3";
        Assert.Equal(KokoroLoudness.StandardDb, KokoroLoudness.TargetDbFor(kokoro));
    }

    [Fact]
    public void The_measured_levels_keep_kokoro_below_v3_and_above_its_own_old_level()
    {
        // -21.9 dBFS was Kokoro bm_daniel as rendered; both targets raise it.
        Assert.True(KokoroLoudness.StandardDb > -21.9);
        Assert.True(KokoroLoudness.ExpressiveDb > KokoroLoudness.StandardDb);
    }

    // --- A-Main --------------------------------------------------------------

    [Theory]
    [InlineData("The A-Main for Stock is next.", "The Eh-Main for Stock is next.")]
    [InlineData("A Main, Stock - drivers to the stand.", "Eh-Main, Stock - drivers to the stand.")]
    [InlineData("Three A-Mains tonight.", "Three Eh-Mains tonight.")]
    [InlineData("into the A-main", "into the Eh-main")]
    public void An_a_main_is_said_with_the_letter(string written, string said)
    {
        Assert.Equal(said, RaceNumbers.SpokenRoundNames(written));
    }

    [Theory]
    [InlineData("It's a main event tonight.")]
    [InlineData("The B-Main is next.")]
    [InlineData("Stock A-Mainline")]
    [InlineData("TA-Main")]
    public void Anything_else_is_left_alone(string written)
    {
        Assert.Equal(written, RaceNumbers.SpokenRoundNames(written));
    }

    [Fact]
    public void The_letter_is_given_to_every_voice_after_the_recordings()
    {
        var synthesis = File.ReadAllText(Path.Combine(RepoRoot(), "HeatCaller", "HeatCaller", "AnnouncementService.Synthesis.cs"));

        var recordings = synthesis.IndexOf("StingerLibrary.Shared.TryLoadWithEntry(text)", StringComparison.Ordinal);
        var letter = synthesis.IndexOf("text = RaceNumbers.SpokenRoundNames(text);", StringComparison.Ordinal);
        var kokoro = synthesis.IndexOf("return await RenderWithKokoroAsync(profile, text);", StringComparison.Ordinal);

        Assert.True(recordings > 0 && letter > recordings && kokoro > letter);
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
