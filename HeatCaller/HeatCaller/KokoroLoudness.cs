namespace HeatCaller;

/// <summary>
/// Brings a Kokoro line up to the level of the ElevenLabs voice it is heard
/// beside (8 Nov 2026). Asher: "the kokoro voice isn't hot enough."
/// </summary>
/// <remarks>
/// MEASURED, 8 Nov 2026: the speech level (RMS over the 50 ms windows above
/// -45 dBFS) of 60 saved takes each of Daniel - Steady Broadcaster, against
/// eight race calls rendered in Kokoro's bm_daniel:
///
///   ElevenLabs eleven_v3          -16.7 dBFS   peaks about -1.3
///   ElevenLabs eleven_flash_v2_5  -19.8 dBFS   peaks about -2.6
///   Kokoro bm_daniel              -21.9 dBFS   peaks about -5.4
///
/// So Kokoro standing in for v3 was 5 dB down - the drop the room heard when
/// the ElevenLabs credits ran out mid-heat - and 2 dB down even on Flash.
/// Each Kokoro line is now raised to the level of the model it stands in for
/// (the expressive v3/v4 family, or the rest), and to the Flash level when
/// Kokoro is the voice the club chose. A straight +5 dB would put Kokoro's
/// peaks past full scale, so the top is rounded off by a soft limiter
/// instead of clipping.
/// </remarks>
internal static class KokoroLoudness
{
    /// <summary>The v3 / v4 family's speech level, measured.</summary>
    internal const double ExpressiveDb = -16.7;

    /// <summary>Flash's (and the other ElevenLabs models') speech level, measured.</summary>
    internal const double StandardDb = -19.8;

    /// <summary>Speech is the windows above this, so pauses do not pull the level down.</summary>
    private const double SpeechFloorDb = -45;

    /// <summary>Where the limiter starts to round the top off, as a fraction of full scale.</summary>
    private const double Knee = 0.7;

    /// <summary>Never more than this - a line that was nearly silent is not turned into noise.</summary>
    private const double MostGainDb = 12;

    /// <summary>
    /// The level a Kokoro line should be brought to. One level for every
    /// profile since 8 Nov 2026: every ElevenLabs line is now brought to it
    /// too (see VoiceLevel), so Kokoro matches it whatever it stands in for.
    /// Flash's own level used to be the target beside Flash.
    /// </summary>
    internal static double TargetDbFor(VoiceProfile profile) => VoiceLevel.TargetDb;

    /// <summary>The speech level of 16-bit mono PCM, in dBFS, or null when there is no speech in it.</summary>
    internal static double? SpeechDb(byte[] pcm, int sampleRate)
    {
        var window = Math.Max(1, sampleRate / 20);
        double power = 0;
        long counted = 0;

        for (var start = 0; start + 1 < pcm.Length; start += window * 2)
        {
            double sum = 0;
            var n = 0;

            for (var i = start; i + 1 < pcm.Length && n < window; i += 2, n++)
            {
                var sample = BitConverter.ToInt16(pcm, i) / 32768.0;
                sum += sample * sample;
            }

            if (n > 0 && 10 * Math.Log10((sum / n) + 1e-12) > SpeechFloorDb)
            {
                power += sum;
                counted += n;
            }
        }

        return counted == 0 ? null : 10 * Math.Log10((power / counted) + 1e-12);
    }

    /// <summary>
    /// <paramref name="pcm"/> (16-bit mono) raised to <paramref name="targetDb"/>,
    /// its peaks soft-limited below full scale. Never made quieter: a line
    /// already at the level is left as it is. A new array.
    /// </summary>
    internal static byte[] Match(byte[] pcm, int sampleRate, double targetDb)
    {
        if (SpeechDb(pcm, sampleRate) is not { } level || level >= targetDb)
        {
            return pcm;
        }

        return Raise(pcm, targetDb - level);
    }

    /// <summary>
    /// <paramref name="pcm"/> (16-bit) raised by <paramref name="db"/>, at
    /// most MostGainDb, its peaks soft-limited below full scale. A new array.
    /// For a caller that has measured the line its own way (VoiceLevel).
    /// </summary>
    internal static byte[] Raise(byte[] pcm, double db)
    {
        var gain = Math.Pow(10, Math.Clamp(db, 0, MostGainDb) / 20);
        var louder = new byte[pcm.Length];

        for (var i = 0; i + 1 < pcm.Length; i += 2)
        {
            var sample = Limit(BitConverter.ToInt16(pcm, i) / 32768.0 * gain);
            var value = (short)Math.Clamp(Math.Round(sample * 32767.0), short.MinValue, short.MaxValue);
            louder[i] = (byte)(value & 0xFF);
            louder[i + 1] = (byte)((value >> 8) & 0xFF);
        }

        return louder;
    }

    /// <summary>Unchanged below the knee; above it, rounded off towards full scale and never past it.</summary>
    internal static double Limit(double sample)
    {
        var size = Math.Abs(sample);

        if (size <= Knee)
        {
            return sample;
        }

        var over = (size - Knee) / (1 - Knee);
        var rounded = Knee + ((1 - Knee) * Math.Tanh(over));

        return Math.Sign(sample) * rounded;
    }
}
