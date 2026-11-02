using NAudio.MediaFoundation;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace HeatCaller.Tests;

/// <summary>
/// The front of a line (1 Nov 2026, Asher: "when the after race call ups
/// fire the very beginning of the audio sounds clipped").
///
/// DecodeMp3 threw away a flat 80 ms from the start of every decoded line,
/// to lose an encoder header frame's static. Most ElevenLabs takes do not
/// start speaking until 85-120 ms in, so that only ever took silence - but
/// the between-races call-ups open on a hard word ("Q2 Heat 1...",
/// "A-Main...") and their saved takes are speaking from 35-50 ms, so the
/// first word lost its start. The cut now stops where the words begin.
/// </summary>
public sealed class LineStartClippingTests
{
    private static readonly WaveFormat Mono = new(44100, 16, 1);

    /// <summary>16-bit mono: this much silence, then a loud tone to the end.</summary>
    private static byte[] SilenceThenTone(double silentSeconds, double totalSeconds)
    {
        var samples = (int)(Mono.SampleRate * totalSeconds);
        var silent = (int)(Mono.SampleRate * silentSeconds);
        var pcm = new byte[samples * 2];

        for (var i = silent; i < samples; i++)
        {
            var value = (short)(12000 * Math.Sin(2 * Math.PI * 220 * i / Mono.SampleRate));
            BitConverter.TryWriteBytes(pcm.AsSpan(i * 2), value);
        }

        return pcm;
    }

    [Fact]
    public void A_line_that_speaks_straight_away_keeps_its_first_word()
    {
        // The call-up's shape: decoder silence, then the words from 35 ms.
        var pcm = SilenceThenTone(0.035, 0.5);

        var skip = AnnouncementService.QuietStartToSkip(pcm, Mono);

        Assert.True(skip <= (int)(Mono.AverageBytesPerSecond * 0.035),
            $"Skipped {skip * 1000.0 / Mono.AverageBytesPerSecond:F0} ms - into the words, which start at 35 ms.");
        Assert.Equal(0, skip % Mono.BlockAlign);
    }

    [Fact]
    public void A_line_with_a_quiet_start_still_loses_the_whole_slice()
    {
        // The usual take: nothing until 100 ms, so the full 80 ms goes, as before.
        var pcm = SilenceThenTone(0.1, 0.5);

        var skip = AnnouncementService.QuietStartToSkip(pcm, Mono);

        Assert.Equal((int)(Mono.AverageBytesPerSecond * AnnouncementService.DecodeSkipSeconds), skip);
    }

    [Fact]
    public void A_breath_before_the_words_is_not_taken_for_them()
    {
        // A take that peaks at a few hundred before speaking is still quiet.
        var pcm = SilenceThenTone(0.1, 0.5);
        BitConverter.TryWriteBytes(pcm.AsSpan((int)(Mono.SampleRate * 0.025) * 2), (short)330);

        var skip = AnnouncementService.QuietStartToSkip(pcm, Mono);

        Assert.Equal((int)(Mono.AverageBytesPerSecond * AnnouncementService.DecodeSkipSeconds), skip);
    }

    /// <summary>
    /// End to end, through a real MP3: whatever the decoder hands back that
    /// is loud must still be there after DecodeMp3. Failed before the fix -
    /// a tone from the first sample came out with its start cut off.
    /// </summary>
    [Fact]
    public void Decoding_an_mp3_that_starts_loud_cuts_none_of_it()
    {
        var mp3 = EncodeMp3(SilenceThenTone(0, 1.0));

        if (mp3 is null)
        {
            // No Windows Media Foundation MP3 encoder on this PC - the
            // QuietStartToSkip tests above still cover the rule.
            return;
        }

        var raw = DecodeUntrimmed(mp3, out var rawFormat);
        var (trimmed, format) = AnnouncementService.DecodeMp3(mp3);

        Assert.Equal(rawFormat.AverageBytesPerSecond, format.AverageBytesPerSecond);

        var lostMs = (LoudSamples(raw) - LoudSamples(trimmed)) * 1000.0 / format.SampleRate / format.Channels;

        Assert.True(lostMs < 1, $"DecodeMp3 cut {lostMs:F0} ms of loud audio off the front of the line.");
    }

    private static int LoudSamples(byte[] pcm)
    {
        var loud = 0;

        for (var i = 0; i + 1 < pcm.Length; i += 2)
        {
            if (Math.Abs((int)BitConverter.ToInt16(pcm, i)) >= AnnouncementService.SpeechOnsetLevel)
            {
                loud++;
            }
        }

        return loud;
    }

    private static byte[] DecodeUntrimmed(byte[] mp3, out WaveFormat format)
    {
        using var reader = new Mp3FileReader(new MemoryStream(mp3));
        format = reader.WaveFormat;

        using var pcm = new MemoryStream();
        reader.CopyTo(pcm);
        return pcm.ToArray();
    }

    /// <summary>The PCM as an MP3, by Windows' own encoder - stereo, as it only takes what it supports. Null where there is none.</summary>
    private static byte[]? EncodeMp3(byte[] monoPcm)
    {
        var path = Path.Combine(Path.GetTempPath(), $"heatcaller-linestart-{Guid.NewGuid():N}.mp3");

        try
        {
            MediaFoundationApi.Startup();

            var mono = new RawSourceWaveStream(new MemoryStream(monoPcm), Mono);
            var stereo = new SampleToWaveProvider16(new MonoToStereoSampleProvider(mono.ToSampleProvider()));
            MediaFoundationEncoder.EncodeToMp3(stereo, path, 128_000);

            return File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is not Xunit.Sdk.XunitException)
        {
            return null;
        }
        finally
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                // A temp file left behind is no failure.
            }
        }
    }
}
