namespace HeatCaller;

/// <summary>
/// How loud an imaging piece (a jingle, sweeper, bumper or stinger) plays
/// beside the announcer.
///
/// WHY (25 Nov 2026, Asher: "the levels for the imaging and the voice are
/// out of whack. the imaging is way too soft"): every spoken line is
/// brought to one level (VoiceLevel.TargetDb), but a piece played at
/// whatever level its file was made at, times the Mixer's Imaging pieces
/// number - nothing tied the two together, and nothing showed how far
/// apart they were. Asher's was at 20, which put every piece about 14 dB
/// under the voice with nothing on screen to say so.
///
/// Now each piece is measured the way a line is (how loud it sounds, in
/// 50 ms windows, leaving out the quiet ones - see Loudness)
/// and brought to the voice's level, so Imaging pieces means the same on
/// every pack and every club file: 100 is as loud as the announcer, the
/// default 80 just under, and 20 well under.
/// </summary>
internal static class ImagingLevel
{
    /// <summary>Never raised by more than this, so a nearly silent file is not turned into noise.</summary>
    internal const double MostRaiseDb = 12;

    /// <summary>Never turned down by more than this.</summary>
    internal const double MostCutDb = 20;

    /// <summary>
    /// The level of a piece's sound, in dBFS, over every channel; null when
    /// nothing in it is above the floor.
    /// </summary>
    internal static double? Db(float[] samples, int channels, int sampleRate) =>
        Loudness.Db(samples, sampleRate, channels);

    /// <summary>
    /// What a piece measured at <paramref name="measuredDb"/> is multiplied
    /// by to sound as loud as the announcer. 1 when it couldn't be measured.
    /// </summary>
    internal static float GainFor(double? measuredDb)
    {
        if (measuredDb is not { } measured || double.IsNaN(measured))
        {
            return 1f;
        }

        var db = Math.Clamp(VoiceLevel.TargetDb - measured, -MostCutDb, MostRaiseDb);
        return (float)Math.Pow(10, db / 20);
    }

    /// <summary>
    /// How loud a piece plays: the Mixer's Imaging pieces number (0 to 100)
    /// times the piece's own gain, times the lift the announcer is getting
    /// over a loud crowd (1 to 2 - see BroadcastImaging.CrowdBoost), so a
    /// piece stays where the Mixer says beside the voice. Above 1 for a
    /// quiet piece at a high number - the bus rounds its peaks off rather
    /// than clipping them.
    /// </summary>
    internal static float Volume(int level, float pieceGain, float crowdBoost = 1f) =>
        Math.Clamp(level, 0, 100) / 100f * pieceGain * (float.IsNaN(crowdBoost) ? 1f : Math.Clamp(crowdBoost, 1f, 2f));

    /// <summary>How many dB under the announcer the pieces sit at this Imaging pieces number. Infinity at 0.</summary>
    internal static double UnderVoiceDb(int level) =>
        level <= 0 ? double.PositiveInfinity : -20 * Math.Log10(Math.Clamp(level, 1, 100) / 100.0);

    /// <summary>The Mixer's line under Imaging pieces, in plain words.</summary>
    internal static string BalanceWords(int level)
    {
        var under = UnderVoiceDb(level);

        return under switch
        {
            double.PositiveInfinity => "Off - no jingles, sweepers, bumpers or stingers play.",
            <= 1 => "The pieces play as loud as the announcer.",
            <= 3 => "The pieces play just under the announcer.",
            <= 7 => "The pieces play quieter than the announcer.",
            _ => "The pieces play well under the announcer - 80 puts them just under.",
        };
    }
}
