using NAudio.Wave;

namespace HeatCaller.Tests;

/// <summary>
/// Hitting the post (17 Oct 2026): where the voice in a rendered line really
/// ends, and when a piece has to start for its own voice to land there. See
/// TalkUp. Nothing here plays anything.
/// </summary>
public sealed class TalkUpTests
{
    private const int Rate = 8000;

    /// <summary>16-bit mono PCM: <paramref name="loud"/> seconds of tone, then <paramref name="quiet"/> seconds of silence.</summary>
    private static byte[] Pcm16(double loud, double quiet, short level = 12000)
    {
        var loudFrames = (int)(loud * Rate);
        var frames = loudFrames + (int)(quiet * Rate);
        var bytes = new byte[frames * 2];

        for (var i = 0; i < loudFrames; i++)
        {
            var sample = (short)(i % 2 == 0 ? level : -level);
            BitConverter.GetBytes(sample).CopyTo(bytes, i * 2);
        }

        return bytes;
    }

    [Fact]
    public void The_engines_trailing_silence_is_not_counted_as_voice()
    {
        var pcm = Pcm16(loud: 2.0, quiet: 0.6);

        var voiced = TalkUp.VoicedLength(pcm, new WaveFormat(Rate, 16, 1));

        Assert.InRange(voiced.TotalSeconds, 1.99, 2.01);
    }

    [Fact]
    public void Hiss_under_the_threshold_in_the_tail_is_still_silence()
    {
        var pcm = Pcm16(loud: 1.0, quiet: 0.0);
        var hiss = Pcm16(loud: 0.5, quiet: 0.0, level: 200);

        var voiced = TalkUp.VoicedLength(pcm.Concat(hiss).ToArray(), new WaveFormat(Rate, 16, 1));

        Assert.InRange(voiced.TotalSeconds, 0.99, 1.01);
    }

    [Fact]
    public void Silent_audio_counts_in_full_so_a_post_is_never_early()
    {
        var pcm = Pcm16(loud: 0.0, quiet: 1.5);

        var voiced = TalkUp.VoicedLength(pcm, new WaveFormat(Rate, 16, 1));

        Assert.InRange(voiced.TotalSeconds, 1.49, 1.51);
    }

    [Fact]
    public void Float_audio_is_read_as_well()
    {
        var frames = Rate;
        var bytes = new byte[(frames + (Rate / 2)) * 4];

        for (var i = 0; i < frames; i++)
        {
            BitConverter.GetBytes(i % 2 == 0 ? 0.5f : -0.5f).CopyTo(bytes, i * 4);
        }

        var voiced = TalkUp.VoicedLength(bytes, WaveFormat.CreateIeeeFloatWaveFormat(Rate, 1));

        Assert.InRange(voiced.TotalSeconds, 0.99, 1.01);
    }

    [Fact]
    public void A_piece_fires_its_post_ahead_of_the_last_word()
    {
        var now = new DateTime(2026, 10, 17, 20, 0, 0, DateTimeKind.Utc);
        var lastWord = now.AddSeconds(5);

        var wait = TalkUp.FireIn(now, lastWord, TimeSpan.FromSeconds(2));

        Assert.Equal(TimeSpan.FromSeconds(3), wait);
    }

    [Fact]
    public void An_intro_longer_than_what_is_left_fires_at_once_and_never_waits_negative()
    {
        var now = new DateTime(2026, 10, 17, 20, 0, 0, DateTimeKind.Utc);

        Assert.Equal(TimeSpan.Zero, TalkUp.FireIn(now, now.AddSeconds(1), TimeSpan.FromSeconds(4)));
        Assert.Equal(TimeSpan.Zero, TalkUp.FireIn(now, now.AddSeconds(-1), TimeSpan.Zero));
    }

    [Fact]
    public void A_piece_with_no_post_fires_on_the_last_word()
    {
        var now = new DateTime(2026, 10, 17, 20, 0, 0, DateTimeKind.Utc);

        Assert.Equal(TimeSpan.FromSeconds(1.5), TalkUp.FireIn(now, now.AddSeconds(1.5), TimeSpan.Zero));
    }
}
