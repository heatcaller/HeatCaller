namespace HeatCaller;

/// <summary>
/// How loud a line or a piece sounds, rather than how big its signal is.
///
/// WHY (25 Nov 2026, Asher, step 4 of plans/imaging-voice-levels-plan.md):
/// lines were levelled on their flat signal level (KokoroLoudness.SpeechDb).
/// The ear hears the middle and top of a voice far more than its low end,
/// so two lines at one flat level can sound a dB or more apart - Finley
/// landed about 1 dB above Charlie after levelling - and a jingle full of
/// bass measured louder than it sounds. This weights the sound the way the
/// broadcast loudness standard does (ITU-R BS.1770's K-weighting: a lift
/// of about 4 dB above 2 kHz and a roll-off under about 40 Hz) before it is
/// measured, in the same 50 ms windows with the same −45 dB floor as
/// before, so pauses still don't pull a line down.
///
/// The number comes out on the old scale: CalibrationDb is added so a
/// typical announcer line measures what it did, and the level every line
/// is brought to (VoiceLevel.TargetDb) is unchanged. What changes is the
/// voices against each other, and the imaging against the voices.
/// </summary>
internal static class Loudness
{
    /// <summary>The windows above this count - see the class summary.</summary>
    private const double FloorDb = -45;

    /// <summary>
    /// Added to the weighted level so it reads on the flat scale the target
    /// was set on: the median of (flat − weighted) over the club's saved
    /// ElevenLabs takes (25 Nov 2026: 1,570 lines across 19 voices and
    /// models). The weighted level reads 0.64 dB above the flat one on a
    /// typical line - more on a bright voice (Daniel on v4, 1.2), almost
    /// nothing on a deep one (Charlie, 0.1) - which is the difference the
    /// flat level could not hear.
    /// </summary>
    internal const double CalibrationDb = -0.64;

    /// <summary>The sounding level of 16-bit PCM, interleaved, in dB on the flat scale; null when nothing is above the floor.</summary>
    internal static double? Db(byte[] pcm, int sampleRate, int channels)
    {
        channels = Math.Max(1, channels);
        var frames = pcm.Length / 2 / channels;
        var samples = new float[frames * channels];

        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = BitConverter.ToInt16(pcm, i * 2) / 32768f;
        }

        return Db(samples, sampleRate, channels);
    }

    /// <summary>The sounding level of float samples, interleaved, in dB on the flat scale; null when nothing is above the floor.</summary>
    internal static double? Db(float[] samples, int sampleRate, int channels)
    {
        channels = Math.Max(1, channels);
        sampleRate = Math.Max(1, sampleRate);
        var frames = samples.Length / channels;
        if (frames == 0)
        {
            return null;
        }

        var weighted = new double[frames * channels];

        for (var c = 0; c < channels; c++)
        {
            var shelf = Biquad.HighShelf(sampleRate);
            var highPass = Biquad.HighPass(sampleRate);

            for (var f = 0; f < frames; f++)
            {
                var i = (f * channels) + c;
                weighted[i] = highPass.Next(shelf.Next(samples[i]));
            }
        }

        var window = Math.Max(1, sampleRate / 20) * channels;
        double power = 0;
        long counted = 0;

        for (var start = 0; start < weighted.Length; start += window)
        {
            var end = Math.Min(weighted.Length, start + window);
            double sum = 0;

            for (var i = start; i < end; i++)
            {
                sum += weighted[i] * weighted[i];
            }

            var n = end - start;
            if (n > 0 && 10 * Math.Log10((sum / n) + 1e-12) > FloorDb)
            {
                power += sum;
                counted += n;
            }
        }

        return counted == 0 ? null : (10 * Math.Log10((power / counted) + 1e-12)) + CalibrationDb;
    }

    /// <summary>One stage of the weighting, worked out for the file's own sample rate (the standard gives it at 48 kHz).</summary>
    private sealed class Biquad
    {
        private readonly double _b0, _b1, _b2, _a1, _a2;
        private double _x1, _x2, _y1, _y2;

        private Biquad(double b0, double b1, double b2, double a1, double a2)
        {
            (_b0, _b1, _b2, _a1, _a2) = (b0, b1, b2, a1, a2);
        }

        /// <summary>The head's lift of about 4 dB above 2 kHz.</summary>
        internal static Biquad HighShelf(int rate)
        {
            const double f0 = 1681.974450955533, gainDb = 3.999843853973347, q = 0.7071752369554196;
            var k = Math.Tan(Math.PI * f0 / rate);
            var vh = Math.Pow(10, gainDb / 20);
            var vb = Math.Pow(vh, 0.4996667741545416);
            var a0 = 1 + (k / q) + (k * k);

            return new Biquad(
                (vh + (vb * k / q) + (k * k)) / a0,
                2 * ((k * k) - vh) / a0,
                (vh - (vb * k / q) + (k * k)) / a0,
                2 * ((k * k) - 1) / a0,
                (1 - (k / q) + (k * k)) / a0);
        }

        /// <summary>The roll-off under about 40 Hz.</summary>
        internal static Biquad HighPass(int rate)
        {
            const double f0 = 38.13547087602444, q = 0.5003270373238773;
            var k = Math.Tan(Math.PI * f0 / rate);
            var a0 = 1 + (k / q) + (k * k);

            return new Biquad(1, -2, 1, 2 * ((k * k) - 1) / a0, (1 - (k / q) + (k * k)) / a0);
        }

        internal double Next(double x)
        {
            var y = (_b0 * x) + (_b1 * _x1) + (_b2 * _x2) - (_a1 * _y1) - (_a2 * _y2);
            (_x2, _x1, _y2, _y1) = (_x1, x, _y1, y);
            return y;
        }
    }
}
