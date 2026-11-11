namespace HeatCaller;

/// <summary>
/// Which calls earn the loud audio tags (10 Nov 2026, asked for by Asher).
///
/// Eleven v4 plays a tag much harder than v3 did, and almost every built-in
/// line opened on [excited] or [shouting] - the lap times, the position
/// report, staging, the countdown - so on v4 the whole night came out hyped
/// and nothing stood out. Now the two loud tags are kept for the moments
/// that should:
///
///  - [shouting]: the start and the checkered flag, a new leader, records,
///    and the flags and jump starts (urgency, not hype).
///  - [excited]: passes and battles, and the last lap and final seconds.
///  - everything else is read plain, a driver's own bests included (see
///    Unlifted).
///
/// Every line of one call takes the same tag, so the same moment is never
/// shouted one time and flat the next. Character tags ([cheerful], [awe],
/// [laughs]) are not about loudness and are left where the lines have them.
/// The built-in lines and every built-in soundscape follow this; the tests
/// hold them to it, so a new line can't drift back to [shouting].
/// </summary>
internal static class AudioTagTiers
{
    public const string Shouting = "[shouting]";
    public const string Excited = "[excited]";

    private static readonly HashSet<AnnouncementEventType> Shouted = new()
    {
        AnnouncementEventType.RaceStarted,
        AnnouncementEventType.RaceTimeOver,
        AnnouncementEventType.RaceCompleted,

        // The checkered flag's own moment, said straight after its call (10 Nov 2026).
        AnnouncementEventType.WinnerCalled,
        AnnouncementEventType.LeaderChange,
        AnnouncementEventType.TrackRecord,
        AnnouncementEventType.FastLap,
        AnnouncementEventType.ProvisionalPole,
        AnnouncementEventType.RecordPace,
        AnnouncementEventType.SplitRecordPace,
        AnnouncementEventType.YellowFlag,
        AnnouncementEventType.RedFlag,
        AnnouncementEventType.GreenFlagResumed,
        AnnouncementEventType.JumpStart,
    };

    private static readonly HashSet<AnnouncementEventType> Lifted = new()
    {
        AnnouncementEventType.CloseBattle,

        // The same fight, called each lap at the end (11 Nov 2026).
        AnnouncementEventType.FinalLapsGap,
        AnnouncementEventType.GhostPassed,
        AnnouncementEventType.PassedByGhost,
        AnnouncementEventType.Lapped,
        AnnouncementEventType.LastLap,
        AnnouncementEventType.FinalMinute,
        AnnouncementEventType.FinalSeconds,
    };

    /// <summary>
    /// A driver's own bests (11 Nov 2026, Asher): personal bests, a best ever
    /// here, several personal bests said as one, streaks, qualifying
    /// improvements and achievements. No [excited] or [shouting] - the
    /// night's lift stays with the race itself - but the character tags
    /// ([cheerful], [awe]) they have stay. VoiceSettingsStore takes the loud
    /// tags out of these calls' saved lines too (template version 5).
    /// </summary>
    internal static readonly IReadOnlySet<AnnouncementEventType> Unlifted = new HashSet<AnnouncementEventType>
    {
        AnnouncementEventType.PersonalBestLap,
        AnnouncementEventType.BestEverHere,

        // What their saved races say (11 Nov 2026) - their own news, so lifted.
        AnnouncementEventType.BeatLastTimeOut,
        AnnouncementEventType.BackOnForm,
        AnnouncementEventType.Milestone,
        AnnouncementEventType.RivalsSideBySide,
        AnnouncementEventType.PersonalBestsTogether,
        AnnouncementEventType.Streak,
        AnnouncementEventType.StreakLead,
        AnnouncementEventType.QualifyingImprovement,
        AnnouncementEventType.AchievementUnlocked,
    };

    /// <summary>The loud tag this call's lines take: [shouting], [excited], or null for a plain read.</summary>
    public static string? For(AnnouncementEventType type) =>
        Shouted.Contains(type) ? Shouting : Lifted.Contains(type) ? Excited : null;
}
