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
///  - [excited]: passes and battles, personal bests and streaks, and the
///    last lap and final seconds.
///  - everything else is read plain.
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
        AnnouncementEventType.PersonalBestLap,

        // A personal best of a bigger kind, and several said as one (10 Nov 2026) -
        // a driver's own, not the track's, so lifted rather than shouted.
        AnnouncementEventType.BestEverHere,
        AnnouncementEventType.PersonalBestsTogether,
        AnnouncementEventType.Streak,
        AnnouncementEventType.StreakLead,
        AnnouncementEventType.QualifyingImprovement,
        AnnouncementEventType.AchievementUnlocked,
        AnnouncementEventType.LastLap,
        AnnouncementEventType.FinalMinute,
        AnnouncementEventType.FinalSeconds,
    };

    /// <summary>The loud tag this call's lines take: [shouting], [excited], or null for a plain read.</summary>
    public static string? For(AnnouncementEventType type) =>
        Shouted.Contains(type) ? Shouting : Lifted.Contains(type) ? Excited : null;
}
