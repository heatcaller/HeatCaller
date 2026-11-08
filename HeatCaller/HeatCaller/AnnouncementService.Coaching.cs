namespace HeatCaller;

/// <summary>
/// THE COACHING LINES (22 Oct 2026) - for the one driver Race Control is
/// coaching, chosen on its right-click (Coach > ...):
///
///  - Target lap: each lap against the target set for them, "Two tenths
///    off", every Nth lap.
///  - Consistency: once a block of clean laps, how far apart they were.
///  - Tones instead of words: a short tone at the line in place of either -
///    not a line at all, so it is played here straight away rather than
///    queued behind whatever the announcer is saying.
///
/// Race Control decides when each is due (RaceControlControl.Coaching), as
/// it does for the split coach; these word them and hand them to the queue,
/// so the club's switches, the flags and the pacing gate apply as to any
/// other routine call.
/// </summary>
public sealed partial class AnnouncementService
{
    /// <summary>Laps seen per coached driver since the session reset, for the target line's every-Nth.</summary>
    private readonly Dictionary<string, int> _targetPaceCounts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The coached driver finished a <paramref name="lapMs"/> lap against a
    /// target of <paramref name="targetMs"/> - said when this lap is one of
    /// the every Nth the club asked for.
    /// </summary>
    public void NotifyTargetPace(RaceStanding standing, long lapMs, long targetMs)
    {
        if (lapMs <= 0 || targetMs <= 0)
        {
            return;
        }

        var config = _voiceStore.Current.Events.GetValueOrDefault(nameof(AnnouncementEventType.TargetPace));

        if (config is null || !ShouldSayTargetPace(standing.Transponder, config.TargetPaceEveryNLaps))
        {
            return;
        }

        var spoken = CapitalisedFirst(CoachingWords.PaceSpoken(lapMs, targetMs));
        var shown = CapitalisedFirst(CoachingWords.PaceShown(lapMs, targetMs));

        EnqueueVoice(AnnouncementEventType.TargetPace, standing, null, lapTimeMs: lapMs, extras: new Dictionary<string, (string, string)>
        {
            ["pace"] = (spoken, shown),
            ["target"] = (RaceNumbers.Plainly(targetMs), Sectors.Format(targetMs) + "s"),
        });
    }

    /// <summary>
    /// Whether this lap's pace is said - counted per driver, and counted
    /// even when it is skipped, as the split time's cadence is.
    /// </summary>
    private bool ShouldSayTargetPace(string? transponder, int everyN)
    {
        everyN = Math.Max(1, everyN);

        if (everyN == 1 || string.IsNullOrEmpty(transponder))
        {
            return true;
        }

        lock (_targetPaceCounts)
        {
            var count = _targetPaceCounts.GetValueOrDefault(transponder) + 1;
            _targetPaceCounts[transponder] = count;

            return count % everyN == 0;
        }
    }

    /// <summary>Forgets the target line's cadence - a new session starts the count again.</summary>
    private void ForgetCoachingCounts()
    {
        lock (_targetPaceCounts)
        {
            _targetPaceCounts.Clear();
        }
    }

    /// <summary>
    /// How many clean laps make a consistency block, from the club's
    /// setting - three at the least (two laps' spread is one gap, not a
    /// rhythm), twenty at the most.
    /// </summary>
    public int ConsistencyBlockLaps =>
        Math.Clamp(_voiceStore.Current.Events.GetValueOrDefault(nameof(AnnouncementEventType.ConsistencyCoach))?.ConsistencyEveryLaps ?? 5, 3, 20);

    /// <summary>
    /// The coached driver's last <paramref name="laps"/> clean laps were
    /// <paramref name="spreadMs"/> apart; <paramref name="previousSpreadMs"/>
    /// is the block before's, or null for the first.
    /// </summary>
    public void NotifyConsistency(RaceStanding standing, int laps, long spreadMs, long? previousSpreadMs)
    {
        var words = CapitalisedFirst(CoachingWords.ConsistencySpoken(laps, spreadMs, previousSpreadMs));

        EnqueueVoice(AnnouncementEventType.ConsistencyCoach, standing, null, extras: new Dictionary<string, (string, string)>
        {
            ["consistency"] = (words, words),
            ["spread"] = (CoachingWords.SpreadSpoken(spreadMs), CoachingWords.SpreadShown(spreadMs)),
            ["laps"] = (RaceNumbers.Words(laps), laps.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        });
    }

    /// <summary>
    /// The coached driver crossed the line <paramref name="gapMs"/> behind
    /// their own-best ghost (negative: ahead of it) - said every lap the
    /// gap is asked for (22 Oct 2026). See OwnBestGhost.GapToGhostMs.
    /// </summary>
    public void NotifyGhostGap(RaceStanding standing, long gapMs)
    {
        var spoken = CapitalisedFirst(CoachingWords.GhostGapSpoken(gapMs));
        var shown = CapitalisedFirst(CoachingWords.GhostGapShown(gapMs));

        EnqueueVoice(AnnouncementEventType.GhostGap, standing, null, extras: new Dictionary<string, (string, string)>
        {
            ["ghostgap"] = (spoken, shown),
            // WHY (8 Nov 2026): said to the tenth it is shown to, not
            // GapSpoken's halves ("a second and a half" under "+1.3s").
            ["gap"] = (RaceNumbers.GapInTenths(RaceNumbers.TenthsOf(gapMs)), CoachingWords.GhostGapShown(gapMs)),
        });
    }

    /// <summary>
    /// A coaching tone, now - not queued, because a tone that lands behind
    /// a sentence has lost the lap it was about, and not a line, so it never
    /// holds anything else up. Quiet: at the sound effects' level, and lower
    /// than the effects themselves (ToneGenerator.CoachToneLevel). Nothing
    /// when the announcer is muted.
    /// </summary>
    internal void PlayCoachTone(CoachTone tone)
    {
        // Nor for a lap the window was told of too late (23 Oct 2026, timing
        // layer, P4b) - see WhileLapCallsAreStale.
        if (Muted || SkippedAsStale())
        {
            return;
        }

        _ = ToneGenerator.PlayCoachToneAsync(tone, (float)_soundStore.Current.Volume);
    }
}
