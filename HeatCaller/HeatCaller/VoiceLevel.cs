using NAudio.Wave;

namespace HeatCaller;

/// <summary>
/// Every spoken line brought to one level, whoever says it (8 Nov 2026).
/// Asher: "the principal and tech lead's level is always noticeably lower
/// than the main characters. even that out."
/// </summary>
/// <remarks>
/// MEASURED, 8 Nov 2026: the speech level (see KokoroLoudness.SpeechDb) of
/// up to 60 saved ElevenLabs takes per voice and model. On eleven_v4, the
/// model the commentary and the pit lane are rendered on:
///
///   Daniel - Steady Broadcaster (the announcer)   -17.7 dBFS
///   Liam - Energetic (the co-host)                -24.1 dBFS
///   Charlie - Deep, Confident (Team Principal)    -26.9 dBFS
///
/// and on v3, Harry - Fierce Warrior (the Technical Lead) at -15.5. Nothing
/// in the app turned the guests down: ElevenLabs voices simply come out of
/// the API at very different levels, up to 9 dB apart on one model. So
/// every line is levelled as it is decoded - raised, soft-limited, the way
/// KokoroLoudness raises Kokoro, or lowered when it is hotter than the rest.
///
/// One level for every voice and every model, the v3 family's measured
/// level. Flash race calls (about -19.8) come up 3 dB with it, and Kokoro
/// is matched to the same level, so a stand-in never drops out of the mix.
/// </remarks>
internal static class VoiceLevel
{
    /// <summary>The level every line is brought to - see the remarks.</summary>
    internal const double TargetDb = KokoroLoudness.ExpressiveDb;

    /// <summary>Within this of the target, a line is left exactly as it came.</summary>
    internal const double ToleranceDb = 0.5;

    /// <summary>
    /// <paramref name="pcm"/> at <see cref="TargetDb"/>. Only 16-bit PCM is
    /// touched - anything else comes back as it was, as does silence and a
    /// line already at the level. A new array when anything changed.
    /// </summary>
    internal static byte[] Level(byte[] pcm, WaveFormat format)
    {
        if (format.Encoding != WaveFormatEncoding.Pcm || format.BitsPerSample != 16 || pcm.Length < 2)
        {
            return pcm;
        }

        // Interleaved stereo measures as one stream at twice the rate: the
        // 50 ms windows stay 50 ms long.
        var rate = format.SampleRate * Math.Max(1, format.Channels);

        if (KokoroLoudness.SpeechDb(pcm, rate) is not { } level || Math.Abs(level - TargetDb) <= ToleranceDb)
        {
            return pcm;
        }

        if (level < TargetDb)
        {
            return KokoroLoudness.Match(pcm, rate, TargetDb);
        }

        var gain = Math.Pow(10, (TargetDb - level) / 20);
        var quieter = new byte[pcm.Length];

        for (var i = 0; i + 1 < pcm.Length; i += 2)
        {
            var value = (short)Math.Round(BitConverter.ToInt16(pcm, i) * gain);
            quieter[i] = (byte)(value & 0xFF);
            quieter[i + 1] = (byte)((value >> 8) & 0xFF);
        }

        return quieter;
    }
}
