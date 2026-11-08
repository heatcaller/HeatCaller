namespace HeatCaller;

/// <summary>
/// What the race looked like when an AI line's facts were taken - so the
/// line can be checked against the race as it is when it is about to be
/// heard. See <see cref="AnnouncementService.WhyNoLongerTrue"/>.
/// </summary>
/// <param name="Race">Which race (bumped at each green and each reset).</param>
/// <param name="FastestLaps">How many times the race's fastest lap had been set.</param>
/// <param name="LeadChanges">How many times the lead had changed hands.</param>
/// <param name="LeaderLaps">The leader's lap count.</param>
/// <param name="AtMs">When, on TimingClock.</param>
/// <param name="SayByMs">The segment's own deadline on TimingClock (its NeedsBy), or null for none.</param>
/// <param name="LeaderLapsAllowed">
/// How many more laps the leader may complete before the line is out of
/// date: 1 for a line about the lead or the gaps and laps to go, more for a
/// fight further back, null for a line no lap by the leader can make untrue
/// (a record lap, a record in danger). 8 Nov 2026 - see WhyNoLongerTrue.
/// </param>
/// <param name="LeadMatters">False for a line a lead change cannot make untrue - a fight for fifth, a record lap.</param>
public sealed record RaceFactsStamp(long Race, long FastestLaps, long LeadChanges, int LeaderLaps, long AtMs, long? SayByMs = null,
    int? LeaderLapsAllowed = 1, bool LeadMatters = true);

/// <summary>
/// Lines that are no longer true by the time they would be heard (8 Nov
/// 2026). Asked for by Asher after a demo heat on a 5.5-second track:
/// "I thought I heard some times called inaccurately ... it's very
/// important to people trusting the timing."
/// </summary>
/// <remarks>
/// WHAT HAPPENED. Every lap was timed right; what was said about them was
/// not. With ElevenLabs on eleven_v3 and AI commentary on Non-stop, the
/// voice queue ran five or six lines deep for the whole race, and three
/// things let a line out after the race had moved past it:
///
/// 1. A lap call had no age limit. The chimes did (PerLapSoundMaxAge, 3s)
///    but the words did not, so "Best lap of the race so far - Terry Byte,
///    five four twenty-five" was queued at 1:11:22 and started at 1:11:37,
///    three laps later. Only the clock calls had a say-by (28 Oct).
///
/// 2. An AI line was checked against its deadline once, when the relay's
///    answer came back - never when it was about to be heard - and never
///    against the race. "Terry Byte absolutely RIPS it with a five
///    forty-two" was written about lap 5 and heard twenty seconds later,
///    a second after Terry's 4.936 on lap 9; "out front by just over a
///    second ... NINE laps left" was heard a lap after both had changed.
///
/// 3. An AI line is queued as a Reminder, which is a priority call: it
///    binned the lap calls waiting ahead of it and waited in front of the
///    ones queued after it. Terry's 4.936 - the fastest lap of the race -
///    sat behind two AI lines and was still in the queue at the flag.
///
/// So: a lap call is said within <see cref="LapCallSayWithinMs"/> of its lap
/// or not at all, checked when it comes off the queue AND again once it is
/// rendered (a v3 render can take five seconds). An AI line written during a
/// race carries a <see cref="RaceFactsStamp"/>, and is dropped - when it comes
/// off the queue, once it is rendered, and the moment the race changes under
/// it - if a quicker lap has been set, the lead has changed hands, the leader
/// has started another lap, or its own deadline has passed. And it no
/// longer bins lap calls or waits in front of them.
/// </remarks>
public sealed partial class AnnouncementService
{
    /// <summary>
    /// How long after its lap a call about it may still START. Past this the
    /// room has seen the next lap, and the call is dropped unsaid. Checked
    /// as it comes off the queue and again just before the speaker.
    /// </summary>
    internal const long LapCallSayWithinMs = 6_000;

    /// <summary>
    /// When each car last crossed the line, on TimingClock - so a call about
    /// that lap is timed from the crossing, not from when it reached the
    /// queue (the per-lap batch, the field window and a busy window all sit
    /// in between). Any thread.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> _lapSeenAtMs = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// While a lap is being handed over: when the timing engine raised it
    /// (threaded mode), set by the screen delivering it. Null inline, where
    /// the lap is being handled as it happens. UI thread.
    /// </summary>
    public long? LapRaisedAtMs { get; set; }

    /// <summary>
    /// A standing that comes from a penalty, a lap added or removed by hand -
    /// Race Control marks these "adjusted" (OnStandingAdjusted). Not a
    /// crossing: nobody passed anybody on track.
    /// </summary>
    internal static bool IsHandAdjustment(RaceStanding standing) =>
        string.Equals(standing.Status, "adjusted", StringComparison.OrdinalIgnoreCase);

    /// <summary>Notes a car's crossing - see _lapSeenAtMs.</summary>
    private void NoteLapSeen(string transponder) =>
        _lapSeenAtMs[transponder] = LapRaisedAtMs is { } raised && raised <= TimingClock.NowMs ? raised : TimingClock.NowMs;

    /// <summary>
    /// When the lap a call about this car is about was crossed: its last
    /// crossing, when that was recent, otherwise now.
    /// </summary>
    private long LapSeenAt(string? transponder)
    {
        var now = TimingClock.NowMs;

        return transponder is not null
            && _lapSeenAtMs.TryGetValue(transponder, out var seen)
            && now - seen is >= 0 and < 2 * LapCallSayWithinMs
            ? seen
            : now;
    }

    private long _factsRace;
    private long _factsFastestLaps;
    private long _factsLeadChanges;
    private int _factsLeaderLaps;

    /// <summary>
    /// The calls that are about the lap just completed - its time, its
    /// place, the pass on it - and so only true for a few seconds. Not the
    /// clock calls (they have a say-by of their own), not instructions or
    /// rulings, not a badge or a track record (facts that stay true), and
    /// not the running order (it is worded again as it comes off the queue
    /// - see the position report).
    /// </summary>
    internal static bool IsLapBoundCall(AnnouncementEventType type) => type is
        AnnouncementEventType.FastLap or
        AnnouncementEventType.PersonalBestLap or
        AnnouncementEventType.LeaderChange or
        AnnouncementEventType.LapCompleted or
        AnnouncementEventType.LapTime or
        AnnouncementEventType.Streak or
        AnnouncementEventType.StreakLead or
        AnnouncementEventType.CloseBattle or
        AnnouncementEventType.Lapped or
        AnnouncementEventType.GhostPassed or
        AnnouncementEventType.PassedByGhost or
        AnnouncementEventType.RecordPace or
        AnnouncementEventType.LastLap or
        AnnouncementEventType.QualifyingImprovement or
        AnnouncementEventType.FastestSector or
        AnnouncementEventType.SplitPace or
        AnnouncementEventType.SplitRecordPace or
        AnnouncementEventType.SplitTime or
        AnnouncementEventType.SplitDelta or
        AnnouncementEventType.SplitBattle or
        AnnouncementEventType.SectorsRecap or
        AnnouncementEventType.TargetPace or
        AnnouncementEventType.ConsistencyCoach;

    /// <summary>
    /// Where a lap call goes in the queue: in front of the first AI line
    /// still waiting (true in <paramref name="isAiLine"/>), else at the back.
    /// Everything ahead of that line - a flag, a release, the call before it
    /// - keeps its place.
    /// </summary>
    internal static int LapCallSlot(IReadOnlyList<bool> isAiLine)
    {
        for (var i = 0; i < isAiLine.Count; i++)
        {
            if (isAiLine[i])
            {
                return i;
            }
        }

        return isAiLine.Count;
    }

    /// <summary>The race as it is now, for an AI line about to be written. Any thread.</summary>
    public RaceFactsStamp RaceFactsNow() => new(
        Interlocked.Read(ref _factsRace),
        Interlocked.Read(ref _factsFastestLaps),
        Interlocked.Read(ref _factsLeadChanges),
        Volatile.Read(ref _factsLeaderLaps),
        TimingClock.NowMs);

    /// <summary>
    /// Why a line written against <paramref name="then"/> is no longer true
    /// in the race as it is <paramref name="now"/>, or null while it still is.
    /// </summary>
    internal static string? WhyNoLongerTrue(RaceFactsStamp then, RaceFactsStamp now)
    {
        if (now.Race != then.Race || now.LeaderLaps < then.LeaderLaps)
        {
            return "the race it was about is over";
        }

        if (now.FastestLaps != then.FastestLaps)
        {
            return "a quicker lap has been set since it was written";
        }

        if (then.LeadMatters && now.LeadChanges != then.LeadChanges)
        {
            return "the lead has changed hands since it was written";
        }

        // Its gaps, its laps-to-go and its "last time by" were all read off
        // the lap before. One lap on, the board says something else - for a
        // line about the front. A fight further back is given longer, and a
        // record lap is not made untrue by a lap at all (8 Nov 2026: the
        // first cut dropped nearly every AI line on a 5.5 s track).
        if (then.LeaderLapsAllowed is { } allowed && now.LeaderLaps - then.LeaderLaps >= allowed)
        {
            return now.LeaderLaps - then.LeaderLaps == 1
                ? "the leader has started another lap since it was written"
                : $"the leader has done {now.LeaderLaps - then.LeaderLaps} more laps since it was written";
        }

        if (then.SayByMs is { } sayBy && now.AtMs > sayBy)
        {
            return $"its moment passed {Seconds(now.AtMs - sayBy)} ago";
        }

        return null;
    }

    /// <summary>Why a queued line is no longer worth saying now, or null. Any thread.</summary>
    private string? WhyStale(long? sayByMs, RaceFactsStamp? facts)
    {
        if (facts is not null && WhyNoLongerTrue(facts, RaceFactsNow()) is { } untrue)
        {
            return untrue;
        }

        if (sayByMs is { } sayBy && TimingClock.NowMs is var now && now > sayBy)
        {
            return $"it was {Seconds(now - sayBy)} past the latest it could still be said";
        }

        return null;
    }

    /// <summary>The line being said's deadline and facts - set by the voice loop, read once the line is rendered.</summary>
    private readonly AsyncLocal<(long? SayByMs, RaceFactsStamp? Facts, string? Shown)> _dequeuedFreshness = new();

    /// <summary>
    /// Why the line this voice-loop iteration took off the queue has gone
    /// stale while it was rendered or waited for the speaker, or null.
    /// </summary>
    private string? WhyDequeuedLineIsStale()
    {
        var (sayBy, facts, _) = _dequeuedFreshness.Value;
        return sayBy is null && facts is null ? null : WhyStale(sayBy, facts);
    }

    /// <summary>A new race: AI lines from the last one are no longer about this one.</summary>
    private void NoteNewRaceForFacts()
    {
        Interlocked.Increment(ref _factsRace);
        Volatile.Write(ref _factsLeaderLaps, 0);
        BinStaleCommentary();
    }

    /// <summary>A new fastest lap of the race was just set.</summary>
    private void NoteRaceFastestLap()
    {
        Interlocked.Increment(ref _factsFastestLaps);
        BinStaleCommentary();
    }

    /// <summary>The lead just changed hands.</summary>
    private void NoteLeadChangeForFacts()
    {
        Interlocked.Increment(ref _factsLeadChanges);
        BinStaleCommentary();
    }

    /// <summary>The leader's lap count, as of this crossing.</summary>
    private void NoteLeaderLapsForFacts(int laps)
    {
        if (laps == Volatile.Read(ref _factsLeaderLaps))
        {
            return;
        }

        Volatile.Write(ref _factsLeaderLaps, laps);
        BinStaleCommentary();
    }

    /// <summary>
    /// Throws away any AI line still waiting that the race has just made
    /// untrue - so the room never hears it, and the lap call it was in
    /// front of moves up.
    /// </summary>
    private void BinStaleCommentary()
    {
        List<Action>? giveBack = null;
        List<string>? why = null;
        List<string?>? shown = null;
        int binned;

        var now = RaceFactsNow();

        lock (_voiceQueueLock)
        {
            binned = BinQueuedLocked(item =>
            {
                if (item.Facts is null || WhyNoLongerTrue(item.Facts, now) is not { } reason)
                {
                    return false;
                }

                (why ??= new List<string>()).Add(reason);
                (shown ??= new List<string?>()).Add(item.ShownText);
                return true;
            }, ref giveBack);
        }

        GiveBackLost(giveBack);

        foreach (var text in shown ?? [])
        {
            WithdrawAnnouncementText(text);
        }

        if (binned > 0)
        {
            StatusMessage?.Invoke(
                $"[voice] Dropped {Plural.Of(binned, "waiting AI line")} unheard - {why![0]}.");
        }
    }

    private static string Seconds(long ms) =>
        (ms / 1000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "s";
}
