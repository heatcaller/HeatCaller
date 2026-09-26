namespace HeatCaller;

/// <summary>
/// Every moment that can have its own local sound effect. GoodLap/InvalidLap
/// are per-lap ("was this pass valid?"); Streak is NLT's own consistency
/// flag counted by us (see AnnouncementService); the rest mirror the race
/// lifecycle events already used for voice announcements.
///
/// The order is the order the Sound Effects page lists them in - the lap
/// sounds, then the race from staging to the finish. Settings files key
/// the effects by NAME, so members can be put where they read best.
/// </summary>
public enum SoundEffectKind
{
    GoodLap,
    InvalidLap,

    /// <summary>A driver beats their own best of the race - not the race's fastest, which has FastLap.</summary>
    PersonalBest,

    FastLap,
    TrackRecord,
    Streak,
    LeaderChange,
    RaceStaging,
    CountdownStarted,

    /// <summary>A beep a second for the last five seconds before the green.</summary>
    CountdownTicks,

    /// <summary>
    /// A car crossing before the green. Played the invalid-lap buzzer until
    /// it had a sound of its own - see SoundEffectsStore.FillMissing, which
    /// keeps a club that heard that buzzer hearing one.
    /// </summary>
    JumpStart,

    RaceStarted,
    YellowFlag,
    RedFlag,

    /// <summary>Green again after a yellow or red. The first green of a race is RaceStarted.</summary>
    GreenFlagResumed,

    RaceTimeOver,
    RaceCompleted,
}

/// <summary>
/// Config for one sound effect: whether it's on, and an optional custom
/// audio file to play instead of the built-in generated tone.
/// </summary>
public sealed class SoundEffectConfig
{
    public bool Enabled { get; set; }

    /// <summary>
    /// Whether this sound plays during PRACTICE. Null means "same as the
    /// race setting", so existing settings files behave exactly as before
    /// until someone deliberately splits the two.
    /// </summary>
    public bool? EnabledInPractice { get; set; }

    /// <summary>Resolves the right flag for the current context.</summary>
    public bool IsEnabledFor(bool practice) =>
        practice ? EnabledInPractice ?? Enabled : Enabled;

    /// <summary>
    /// Whether this sound plays while a yellow or red flag is out. Off by
    /// default: under caution the announcer is giving instructions, and lap
    /// chimes over the top of them are noise.
    /// </summary>
    public bool AllowUnderCaution { get; set; }

    public string CustomFilePath { get; set; } = string.Empty;

    /// <summary>
    /// Which built-in sound pack this effect plays when no custom file is
    /// set - a SoundPacks key ("Podium", "Arcade", ...), or empty for the
    /// generated "Classic tones". Empty by default, so every settings file
    /// written before packs existed keeps sounding exactly as it did.
    /// </summary>
    public string BuiltInSound { get; set; } = string.Empty;

    // How much this effect "matters" relative to the others when several
    // could all fire for the same lap (see SoundEffectsProfile.MaxEffectsPerLap).
    // Higher wins. Only meaningful for the per-lap kinds (GoodLap, InvalidLap,
    // FastLap, Streak, LeaderChange) - lifecycle sounds (race staging,
    // countdown, race started/completed) always play regardless of weight.
    public int Weight { get; set; } = 50;
}

/// <summary>
/// Local sound-effect settings mimicking Next Level Timing's own per-lap and
/// per-race-moment audio feedback. One SoundEffectConfig per SoundEffectKind,
/// plus the streak threshold (shared with the voice Streak announcement) and
/// an overall playback volume.
/// </summary>
public sealed class SoundEffectsProfile
{
    public Dictionary<string, SoundEffectConfig> Effects { get; set; } = new();

    /// <summary>Consecutive streak-flagged laps required before the streak sound/voice kick in.</summary>
    public int StreakThreshold { get; set; } = 3;

    /// <summary>
    /// The same threshold for PRACTICE. Null means "same as the race
    /// value", so existing settings files behave exactly as before.
    ///
    /// Worth splitting because practice runs far longer than a heat and
    /// clean laps come easily when nobody is racing for position - a
    /// threshold that fires nicely in a main can chatter constantly over
    /// an open session.
    /// </summary>
    public int? StreakThresholdInPractice { get; set; }

    /// <summary>
    /// Resolves the right threshold for the current context.
    ///
    /// Floored at TWO, not one. A run of one lap is a lap - there is
    /// nothing consecutive about it - and every streak line, on screen and
    /// spoken, is written in the plural: at a threshold of one the
    /// announcer says "one laps running" for every driver on every single
    /// crossing, which is both wrong and constant.
    /// </summary>
    public int StreakThresholdFor(bool practice) =>
        Math.Max(2, practice ? StreakThresholdInPractice ?? StreakThreshold : StreakThreshold);

    public double Volume { get; set; } = 1.0;

    // Caps how many of the per-lap sound effects (GoodLap, InvalidLap,
    // FastLap, Streak, LeaderChange) actually play for any single lap, when
    // more than that many would otherwise fire together - keeping only the
    // highest-Weight ones. Mirrors VoiceProfile.MaxAnnouncementsPerLap, but
    // tracked independently since chimes are much shorter than spoken
    // announcements and a track might reasonably want a different limit for
    // each.
    public int MaxEffectsPerLap { get; set; } = 3;

    /// <summary>Which kinds are subject to per-lap weighting/limiting - the rest are one-time lifecycle sounds that always play.</summary>
    public static readonly HashSet<SoundEffectKind> PerLapKinds = new()
    {
        SoundEffectKind.GoodLap,
        SoundEffectKind.InvalidLap,
        SoundEffectKind.PersonalBest,
        SoundEffectKind.FastLap,
        SoundEffectKind.Streak,
        SoundEffectKind.LeaderChange,
    };

    /// <summary>
    /// The flag sounds. They are the caution rather than something held
    /// by it, and a newer flag replaces any of them still waiting to play -
    /// see AnnouncementService.PlayFlagSound.
    /// </summary>
    public static readonly HashSet<SoundEffectKind> FlagKinds = new()
    {
        SoundEffectKind.YellowFlag,
        SoundEffectKind.RedFlag,
        SoundEffectKind.GreenFlagResumed,
    };

    public static SoundEffectsProfile CreateDefault()
    {
        var p = new SoundEffectsProfile();
        p.Effects[nameof(SoundEffectKind.GoodLap)] = new SoundEffectConfig { Enabled = true, Weight = 40 };
        p.Effects[nameof(SoundEffectKind.InvalidLap)] = new SoundEffectConfig { Enabled = false, Weight = 40 };
        p.Effects[nameof(SoundEffectKind.FastLap)] = new SoundEffectConfig { Enabled = true, Weight = 90 };
        p.Effects[nameof(SoundEffectKind.TrackRecord)] = new SoundEffectConfig { Enabled = true, Weight = 100 };
        p.Effects[nameof(SoundEffectKind.Streak)] = new SoundEffectConfig { Enabled = true, Weight = 70 };
        p.Effects[nameof(SoundEffectKind.LeaderChange)] = new SoundEffectConfig { Enabled = true, Weight = 100 };
        p.Effects[nameof(SoundEffectKind.RaceStaging)] = new SoundEffectConfig { Enabled = false };
        p.Effects[nameof(SoundEffectKind.CountdownStarted)] = new SoundEffectConfig { Enabled = false };
        p.Effects[nameof(SoundEffectKind.RaceStarted)] = new SoundEffectConfig { Enabled = true };
        p.Effects[nameof(SoundEffectKind.RaceTimeOver)] = new SoundEffectConfig { Enabled = true };
        p.Effects[nameof(SoundEffectKind.RaceCompleted)] = new SoundEffectConfig { Enabled = true };

        // The six added after 0.58 start switched off, here and in every file
        // that predates them (FillMissing takes these), so nobody's race
        // night gains a sound they didn't choose. The one exception is
        // JumpStart for a club that already heard a buzzer - see FillMissing.
        //
        // A personal best sits above a good lap and below a fast one when
        // the per-lap limit has to choose, and stays off in practice even
        // once switched on: an open session is one personal best after
        // another.
        p.Effects[nameof(SoundEffectKind.PersonalBest)] = new SoundEffectConfig { Enabled = false, EnabledInPractice = false, Weight = 60 };
        p.Effects[nameof(SoundEffectKind.CountdownTicks)] = new SoundEffectConfig { Enabled = false };
        p.Effects[nameof(SoundEffectKind.JumpStart)] = new SoundEffectConfig { Enabled = false };
        p.Effects[nameof(SoundEffectKind.YellowFlag)] = new SoundEffectConfig { Enabled = false };
        p.Effects[nameof(SoundEffectKind.RedFlag)] = new SoundEffectConfig { Enabled = false };
        p.Effects[nameof(SoundEffectKind.GreenFlagResumed)] = new SoundEffectConfig { Enabled = false };
        return p;
    }

    /// <summary>
    /// The default settings with every effect switched to one built-in
    /// pack. Used by the built-in soundscapes.
    /// </summary>
    public static SoundEffectsProfile CreateForPack(string packKey)
    {
        var p = CreateDefault();
        foreach (var config in p.Effects.Values)
        {
            config.BuiltInSound = packKey;
        }

        return p;
    }

    public static string Describe(SoundEffectKind kind) => kind switch
    {
        SoundEffectKind.GoodLap => "Good lap (valid, no crash)",
        SoundEffectKind.InvalidLap => "Invalid lap",
        SoundEffectKind.PersonalBest => "Personal best (a driver beats their own best lap)",
        SoundEffectKind.FastLap => "Fast lap",
        SoundEffectKind.TrackRecord => "Track record (all-time best on this track)",
        SoundEffectKind.Streak => "Consistency streak",
        SoundEffectKind.LeaderChange => "Leader change",
        SoundEffectKind.RaceStaging => "Race staging",
        SoundEffectKind.CountdownStarted => "Countdown started",
        SoundEffectKind.CountdownTicks => "Countdown ticks (a beep a second for the last five)",
        SoundEffectKind.JumpStart => "Jump start (crossing before the green)",
        SoundEffectKind.RaceStarted => "Race started",
        SoundEffectKind.YellowFlag => "Yellow flag",
        SoundEffectKind.RedFlag => "Red flag",
        SoundEffectKind.GreenFlagResumed => "Green flag (racing resumes after a yellow or red)",
        SoundEffectKind.RaceTimeOver => "Race time over (last lap)",
        SoundEffectKind.RaceCompleted => "Race completed",
        _ => kind.ToString(),
    };
}
