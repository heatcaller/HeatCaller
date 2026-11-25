namespace HeatCaller;

/// <summary>
/// The rules for sound effects that play over each other.
///
/// WHY (24 Nov 2026, asked for by Asher): every effect used to wait in one
/// line for the one before it to end. Two cars crossing together heard one
/// Good lap chime and then, a beat later, the other - or not at all, since a
/// chime that waits more than 3 seconds is thrown away. A fast lap that was
/// also a streak played its two sounds in turn. "You should be able to hear
/// multiple cars hitting a good lap." The speaker could always play them
/// together (AudioBus mixes); only the one line kept them apart.
///
/// So a "moment" sound - one that belongs to a car and an instant - plays
/// when it fires, beside whatever else is playing, up to MaxAtOnce. The race
/// sequence sounds (staging, countdown, ticks, start, time up, finish, the
/// flags) keep their own line, because their order is the point. What any
/// sound does with the VOICE is unchanged: see AnnouncementService.PlaySound.
///
/// Kept here, away from the announcer, so it can be tested without one.
/// </summary>
internal static class SoundOverlap
{
    /// <summary>
    /// Most moment sounds playing at once; the next waits for one to end.
    /// Was 3 (Asher, 24 Nov 2026: "3 is a good limit for now"), raised to 8
    /// the same day at his asking, so a full heat crossing together is all
    /// heard. Each one is a short stream in a mix that already carries the
    /// voice, music and crowd, with a 300 ms output buffer - eight of them
    /// cost next to nothing. Loudness is the real limit, which StackGain
    /// takes care of.
    /// </summary>
    public const int MaxAtOnce = 8;

    /// <summary>
    /// How far apart two of the SAME sound start. Two Good lap chimes from
    /// one file starting in the same instant are heard as one louder chime,
    /// not two cars; this little gap is what makes them two. Different
    /// sounds get no gap at all, so each plays as close to its moment as it
    /// can.
    /// </summary>
    public static readonly TimeSpan SameSoundStagger = TimeSpan.FromMilliseconds(120);

    /// <summary>The sounds that may play over each other: a car's lap sounds, and the one-off moments that belong to a car.</summary>
    public static readonly IReadOnlySet<SoundEffectKind> MomentKinds = new HashSet<SoundEffectKind>
    {
        SoundEffectKind.GoodLap,
        SoundEffectKind.InvalidLap,
        SoundEffectKind.PersonalBest,
        SoundEffectKind.FastLap,
        SoundEffectKind.Streak,
        SoundEffectKind.LeaderChange,
        SoundEffectKind.TrackRecord,
        SoundEffectKind.JumpStart,
        SoundEffectKind.Warning,
    };

    /// <summary>
    /// Whether this sound plays as it fires, beside others. False puts it in
    /// the line it always had: every sound when the switch is off, and the
    /// race sequence sounds always.
    /// </summary>
    public static bool PlaysAtOnce(SoundEffectKind kind, bool overlapOn) =>
        overlapOn && MomentKinds.Contains(kind);

    /// <summary>
    /// How loud a sound starts when others are already playing. Three
    /// full-level stings summed squash the mix even with AudioBus's soft
    /// limiter, so each one that joins is a little quieter. 1 when it plays
    /// alone, as every sound always did. With eight allowed at once
    /// (24 Nov 2026) it keeps going down, to half from the fifth on: eight
    /// at full level would sum to eight times one, and the limiter would
    /// flatten them into one loud smear.
    /// </summary>
    public static float StackGain(int othersPlaying) => othersPlaying switch
    {
        <= 0 => 1f,
        1 => 0.8f,
        2 => 0.7f,
        3 => 0.6f,
        _ => 0.5f,
    };

    /// <summary>
    /// How long to hold a sound so it starts SameSoundStagger after the last
    /// one of its kind. Zero when there was none, or it was long enough ago.
    /// </summary>
    public static TimeSpan StaggerFor(DateTime? lastStartUtc, DateTime nowUtc)
    {
        if (lastStartUtc is not { } last)
        {
            return TimeSpan.Zero;
        }

        var due = last + SameSoundStagger;

        return due > nowUtc ? due - nowUtc : TimeSpan.Zero;
    }
}
