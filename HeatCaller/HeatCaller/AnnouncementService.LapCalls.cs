using NAudio.Wave;

namespace HeatCaller;

/// <summary>
/// Calls made lap by lap: streaks, positions, gaps, lapped cars, close
/// battles, rivalries and gate passes, and the per-lap batching behind them.
///
/// Moved here unchanged from AnnouncementService.cs on 26 Sep 2026, when the
/// file was split into parts. Fields with initializers stayed there.
/// </summary>
public sealed partial class AnnouncementService
{
    /// <summary>Guards the streak bookkeeping, which queued voice work reads as well as the handlers that update it.</summary>
    private readonly object _streakLock = new();

    /// <summary>The fallback staleness window, used until a lap has been timed.</summary>
    private static readonly TimeSpan StreakGoesStaleAfter = TimeSpan.FromMinutes(2);

    /// <summary>
    /// A rolling sense of how long a lap takes on THIS track.
    ///
    /// Anything measured in "a few laps" has to be derived from this rather
    /// than written as a number of seconds. A flat two-minute window is four
    /// laps on a big outdoor layout and twenty on a short indoor one - and
    /// twenty laps is long enough for a car that stopped to keep a frozen
    /// streak and go on being named as the runner-up for most of a race.
    /// </summary>
    private long _typicalLapMs;

    /// <summary>Folds a real lap into the rolling pace, ignoring anything implausible.</summary>
    private void ObserveLapPace(long lapMs)
    {
        if (lapMs < 1000 || lapMs > 600000)
        {
            return;
        }

        // Weighted to the recent past, so a track change or a caution-slowed
        // stint is absorbed within a handful of laps.
        _typicalLapMs = _typicalLapMs == 0 ? lapMs : ((_typicalLapMs * 3) + lapMs) / 4;
    }

    /// <summary>
    /// How long a run may go untouched before it stops counting as live.
    ///
    /// Four laps of the current pace: long enough to survive a marshalling
    /// stop, short enough that a car which packed up stops being treated as
    /// the driver to beat.
    /// </summary>
    private TimeSpan StreakStaleAfter => _typicalLapMs > 0
        ? TimeSpan.FromMilliseconds(Math.Clamp(_typicalLapMs * 4.0, 15000, 120000))
        : StreakGoesStaleAfter;

    /// <summary>Who holds the longest streak, and how long it is - for the takeover call.</summary>
    private string? _streakLeader;
    private int _streakLeaderCount;
    private string? _currentLeaderTransponder;

    // Neither "standing" nor "racer_passed_gate" carries the time of the lap
    // that was just completed - only the session's cumulative elapsed time
    // and its all-time fastest lap. We derive this specific lap's duration
    // ourselves from the change in elapsed time between consecutive finish
    // passes for the same transponder.
    private readonly Dictionary<string, long> _lastLapElapsedMs = new();

    // Consecutive streak-flagged laps per transponder. NLT tells us per-pass
    // whether it's currently "on a consistency streak" (the streak boolean),
    // but not how long that streak is - we count it ourselves.
    private readonly Dictionary<string, int> _streakCounts = new();

    /// <summary>
    /// Per-lap announcements (FastLap, LeaderChange, LapCompleted, LapTime,
    /// Streak) aren't enqueued immediately for either voice OR sound
    /// effects - they're collected here for a short window per transponder,
    /// then the highest-Weight ones (up to MaxAnnouncementsPerLap /
    /// MaxEffectsPerLap) are actually played and the rest are dropped.
    /// Without this, a single lap that's simultaneously a new fastest lap,
    /// a continuing streak, AND a lead change would fire three full
    /// sentences (or three chimes) for ONE lap - more than a short track's
    /// lap time allows, especially stacked across several laps in a row.
    /// Voice and sound effects are tracked as separate batches (keyed by a
    /// "voice:" or "sound:" prefix) since they have independent limits.
    /// </summary>
    private readonly Dictionary<string, List<(int Weight, Action Enqueue)>> _pendingPerLapBatches = new();
    private readonly Dictionary<string, CancellationTokenSource> _pendingBatchCancellation = new();
    private readonly object _batchLock = new();

    // The window candidates are collected over before the batch is decided
    // and flushed. All of a single lap's candidates (gate-pass-driven and
    // standings-driven alike) are raised within milliseconds of each other
    // in practice - the 10 Nov 2026 log has a lap's calls 1-3 ms apart.
    //
    // 120 ms since 10 Nov 2026 (voice speed plan, phase 1), from 300. This
    // said the delay was small beside "~1s of audio device warm-up" in every
    // line; that pad is 0.15 s now, the render is under a second, and the
    // batch plus the field window were 0.67 s of the 2.63 s from a lap to its
    // call. It also put every lap chime 0.3 s after its lap.
    private static readonly TimeSpan PerLapBatchWindow = TimeSpan.FromMilliseconds(120);

    /// <summary>
    /// Whether a per-lap voice candidate could actually be spoken right now:
    /// it has a configuration entry, it is switched on for this kind of
    /// session, and no flag is holding it.
    ///
    /// Checked BEFORE a candidate goes into the per-lap batch, not only when
    /// the batch is spoken. The batch keeps the highest-weight candidates up
    /// to the per-lap limit and drops the rest, and the switched-off check
    /// used to happen only afterwards, in EnqueueVoice. So a switched-off
    /// event still won a slot by its weight and then said nothing, and the
    /// line that WAS switched on lost out to it. "Lap times only" is the
    /// plain case: it leaves "Lap completed" (40), "Fastest lap" (90) and
    /// "Lead change" (100) in the settings but off, and "Lap time" (30) on,
    /// so the lap time went unspoken on every fast lap and lead change - and
    /// on every lap at all with the limit set to one.
    ///
    /// EnqueueVoice still makes the same checks when the batch is spoken,
    /// because a flag can come out during the batch window.
    /// </summary>
    private bool VoiceCandidateCanSpeak(AnnouncementEventType type, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] AnnouncementEventConfig? config) =>
        config is not null
        && CallOn(config)
        && !HeldByCaution(type, config)
        // The dial too, and for the reason this method exists at all: a
        // call EnqueueVoice is going to hold must not first take a per-lap
        // slot off one that would have been said. That is the same fault
        // the note below records for a switched-off effect, arriving by a
        // new route. See DialLetsItSpeak.
        && DialLetsItSpeak(type, config);

    /// <summary>
    /// The sound-effect side of VoiceCandidateCanSpeak, for the same reason:
    /// a switched-off effect (Personal best is off by default, at weight 60)
    /// used to take a slot from one that is on (Good lap, 40). Mirrors the
    /// checks PlaySound makes, which still runs them again at play time.
    /// </summary>
    private bool SoundCandidateCanPlay(SoundEffectKind kind, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] SoundEffectConfig? config) =>
        config is not null
        && config.IsEnabledFor(IsPracticeMode)
        && !(IsUnderCaution && SoundEffectsProfile.PerLapKinds.Contains(kind) && !config.AllowUnderCaution);

    /// <summary>
    /// Queues a per-lap CANDIDATE action (a voice line or a sound effect)
    /// rather than performing it immediately. All candidates raised under
    /// the same batchNamespace ("voice" or "sound") for the same
    /// transponder within a short window are collected together; once the
    /// window closes, only the highest-Weight ones (up to maxPerLap) are
    /// actually performed, and the rest are silently dropped. See the
    /// class-level remarks on _pendingPerLapBatches for why.
    ///
    /// Only offer a candidate here that could actually be heard - see
    /// VoiceCandidateCanSpeak and SoundCandidateCanPlay.
    /// </summary>
    private void ConsiderPerLapAction(string batchNamespace, string? transponder, int weight, int maxPerLap, Action action)
    {
        // A ghost's lap sounds and calls, while ghosts are not announced.
        if (MutedGhost(transponder))
        {
            return;
        }

        // A lap the window was told of too late to say (23 Oct 2026, timing
        // layer, P4b) - dropped here, as it is offered, because the batch is
        // spoken later on another thread. See WhileLapCallsAreStale.
        if (SkippedAsStale())
        {
            return;
        }

        if (transponder is null)
        {
            action();
            return;
        }

        var key = $"{batchNamespace}:{transponder}";

        CancellationTokenSource cts;
        lock (_batchLock)
        {
            if (!_pendingPerLapBatches.TryGetValue(key, out var list))
            {
                list = new List<(int, Action)>();
                _pendingPerLapBatches[key] = list;
            }
            list.Add((weight, action));

            if (_pendingBatchCancellation.TryGetValue(key, out var previous))
            {
                previous.Cancel();
                previous.Dispose();
            }

            cts = new CancellationTokenSource();
            _pendingBatchCancellation[key] = cts;
        }

        Interlocked.Increment(ref _lapCallsSettling);
        _ = FlushPerLapBatchAfterDelayAsync(key, maxPerLap, cts);
    }

    /// <summary>
    /// Per-lap batches and field windows still waiting to decide (open
    /// items, 19 Nov 2026). Counted up as each is scheduled and down once it
    /// has queued its winners - a field window is counted before the batch
    /// that offers to it lets go - so 0 means every call offered so far has
    /// been said or dropped. For the tests, which waited a fixed 3 s for that
    /// and failed on a loaded machine.
    /// </summary>
    private int _lapCallsSettling;

    /// <summary>Whether every lap call offered so far has been decided - see _lapCallsSettling.</summary>
    internal bool LapCallsSettledForTests => Volatile.Read(ref _lapCallsSettling) == 0;

    private async Task FlushPerLapBatchAfterDelayAsync(string key, int maxPerLap, CancellationTokenSource cts)
    {
        try
        {
            await FlushPerLapBatchAfterDelayCoreAsync(key, maxPerLap, cts);
        }
        finally
        {
            Interlocked.Decrement(ref _lapCallsSettling);
        }
    }

    private async Task FlushPerLapBatchAfterDelayCoreAsync(string key, int maxPerLap, CancellationTokenSource cts)
    {
        try
        {
            await Task.Delay(PerLapBatchWindow, cts.Token);
        }
        catch (TaskCanceledException)
        {
            return; // a newer candidate for this batch arrived and rescheduled the flush
        }

        List<(int Weight, Action Enqueue)>? candidates;
        lock (_batchLock)
        {
            _pendingPerLapBatches.Remove(key, out candidates);
            _pendingBatchCancellation.Remove(key);
            cts.Dispose();
        }

        if (candidates is null || candidates.Count == 0)
        {
            return;
        }

        var winners = candidates.OrderByDescending(c => c.Weight).Take(Math.Max(1, maxPerLap)).ToList();

        // A car's VOICE calls go to the field rather than straight to the
        // speaker, so the best call across every car crossing at once is
        // the one that is said - see OfferToTheField. Sounds are not paced
        // and go as they always did.
        if (key.StartsWith("voice:", StringComparison.Ordinal))
        {
            OfferToTheField(winners, key["voice:".Length..]);

            return;
        }

        foreach (var candidate in winners)
        {
            candidate.Enqueue();
        }
    }


    /// <summary>
    /// Whether this driver's lap time should be spoken on this lap.
    ///
    /// The count is kept PER DRIVER, so "every 3rd lap" means each racer
    /// hears their own time every third lap of theirs. Counting across the
    /// field instead would mean one driver's time called, then another's
    /// two laps later - unpredictable for everyone and useful to nobody.
    ///
    /// Incremented even when the announcement is skipped, or the cadence
    /// would never advance.
    /// </summary>
    private bool ShouldAnnounceLapTime(string? transponder, AnnouncementEventConfig? config)
    {
        var everyN = Math.Max(1, config?.LapTimeEveryNLaps ?? 1);

        if (everyN == 1 || string.IsNullOrEmpty(transponder))
        {
            return true;
        }

        var count = _lapTimeCounts.GetValueOrDefault(transponder) + 1;
        _lapTimeCounts[transponder] = count;

        return count % everyN == 0;
    }

    /// <summary>Laps seen per driver since the session reset, for the lap-time cadence.</summary>
    private readonly Dictionary<string, int> _lapTimeCounts = new(StringComparer.OrdinalIgnoreCase);


    /// <summary>Spoken ordinals, so a running order reads as speech rather than as numbers.</summary>
    private static string Ordinal(int position) => position switch
    {
        1 => "first",
        2 => "second",
        3 => "third",
        4 => "fourth",
        5 => "fifth",
        6 => "sixth",
        7 => "seventh",
        8 => "eighth",
        9 => "ninth",
        10 => "tenth",

        // Past tenth a broadcast gives the place as a P - and SAID, so the
        // digit becomes a word. "P11" handed to a voice comes out as
        // "pee eleven" on one engine and "P, one one" on another. See
        // RaceNumbers.PositionSpoken.
        _ => RaceNumbers.PositionSpoken(position),
    };

    /// <summary>
    /// Reads out the running order every N leader laps.
    ///
    /// Keyed off the LEADER's lap count rather than a timer or a raw lap
    /// counter: "every 5 laps" should mean five laps of the race, not five
    /// transponder passes from anyone on track - on a full grid those are
    /// wildly different things, and a per-pass trigger would fire the
    /// report constantly.
    ///
    /// The leader's crossing STARTS the report; it is read once every car
    /// it names is over the line too (1 Nov 2026) - see
    /// SayWaitingPositionReport.
    /// </summary>
    private void MaybeAnnouncePositions(List<RaceStanding> ordered, RaceStanding leader)
    {
        // A report already waiting for the cars it names: this crossing may
        // be the last of them.
        SayWaitingPositionReport();

        var config = _voiceStore.Current.Events.GetValueOrDefault(nameof(AnnouncementEventType.PositionReport));
        if (config is null || !CallOn(config) || config.Templates.Count == 0)
        {
            return;
        }

        if (HeldByCaution(AnnouncementEventType.PositionReport, config))
        {
            return;
        }

        // Held by the dial (7 Nov 2026, bug hunt 13): Measured and Sparing
        // both hold the running order, and it never goes through
        // EnqueueVoice, where the dial is asked for everything else. Asked
        // here so a held report doesn't start waiting for its cars, and
        // again in SayPositionReport in case the dial moves while it waits.
        if (!DialLetsItSpeak(AnnouncementEventType.PositionReport, config))
        {
            return;
        }

        // With ghosts not announced, the report is about the real field
        // only. A pace ghost is still ranked in the standings (so gaps and
        // leader changes between real drivers stay right), but it used to
        // be read out by name, and when it LED the whole report was queued
        // under the ghost's transponder - which EnqueueVoiceWork refuses
        // for a muted ghost - so every real driver in it went unheard while
        // the screens still showed it. The first real car leads the report
        // and sets its cadence, so "Sam leads" is read as Sam crosses.
        if (AnnounceGhostsProvider?.Invoke() == false)
        {
            ordered = ordered.Where(standing => !MutedGhost(standing.Transponder)).ToList();

            if (ordered.FirstOrDefault() is not { } firstReal)
            {
                return;
            }

            leader = firstReal;
        }

        var everyLaps = Math.Max(1, config.PositionReportEveryLaps);

        // Needs at least a full interval of racing before the first report,
        // and only fires once per qualifying lap no matter how many cars
        // cross the line while the leader is on it.
        if (leader.Laps < everyLaps || leader.Laps % everyLaps != 0 || leader.Laps == _lastPositionReportLap)
        {
            return;
        }

        var depth = Math.Clamp(config.PositionReportDepth, 1, 10);
        if (PositionReportOrder.Read(ordered, depth, CrossingMs).Count < 2)
        {
            // A running order of one is not a running order.
            return;
        }

        _lastPositionReportLap = leader.Laps;

        // Not read yet (1 Nov 2026). The leader is over the line, but every
        // car behind is still on the previous lap: read now, the gap given
        // for each was measured a whole lap earlier, and on a five-second
        // Mini-Z lap a place can change hands before the voice gets to it.
        // The report waits until the last car it names has crossed this lap
        // too, then reads the order and the gaps as the board has them -
        // the same figures as its "Gap to P1" column. A car that never comes
        // round (stopped, off, in the pits) holds it up for half a lap more
        // than the leader's last lap at most; it is then read without that
        // car and anyone behind it. See PositionReportOrder.SettledCount.
        long leaderLapMs = 0;

        if (CrossingMs(leader.Transponder, leader.Laps) is { } crossed
            && CrossingMs(leader.Transponder, leader.Laps - 1) is { } before)
        {
            leaderLapMs = crossed - before;
        }
        else if (leader.Laps > 0)
        {
            leaderLapMs = leader.ElapsedMs / leader.Laps;
        }

        var waitMs = PositionReportOrder.WaitLimitMs(leaderLapMs);
        WaitingPositionReport waiting;
        WaitingPositionReport? stale;

        lock (_positionReportLock)
        {
            // Still waiting on the last report when the next one is due (a
            // report every lap, and a car that has stopped): the new one
            // reads the order as it is now, so the old one goes.
            stale = _waitingPositionReport;
            waiting = new WaitingPositionReport(leader.Laps, depth, TimingClock.NowMs + waitMs, ++_positionReportsStarted);
            _waitingPositionReport = waiting;
        }

        if (stale is not null)
        {
            StatusMessage?.Invoke(
                $"[voice] Dropped the lap {stale.Lap} position report - a car it names never crossed the line before the next report was due.");
        }

        // Read anyway once the wait is up, even if no car crosses again to
        // set it off.
        _ = SayPositionReportWhenWaitIsUpAsync(waiting, waitMs);

        // Every car it names may already be over (the cars behind a lap or
        // more down): then it is read straight away.
        SayWaitingPositionReport();
    }

    /// <summary>A position report started by the leader's crossing and waiting for the cars it names.</summary>
    private sealed record WaitingPositionReport(int Lap, int Depth, long ReadByMs, int Id);

    /// <summary>The report waiting for its cars, if any. Under _positionReportLock.</summary>
    private WaitingPositionReport? _waitingPositionReport;

    /// <summary>Numbers each report started, so a wait that ran out after its report was read does nothing.</summary>
    private int _positionReportsStarted;

    /// <summary>
    /// Taken around _waitingPositionReport: it is checked on the timing
    /// thread as cars cross and on a timer when the wait is up.
    /// </summary>
    private readonly object _positionReportLock = new();

    private async Task SayPositionReportWhenWaitIsUpAsync(WaitingPositionReport waiting, long waitMs)
    {
        try
        {
            await Task.Delay(TimeSpan.FromMilliseconds(waitMs + 50));
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        lock (_positionReportLock)
        {
            if (!ReferenceEquals(_waitingPositionReport, waiting))
            {
                return;
            }
        }

        SayWaitingPositionReport();
    }

    /// <summary>
    /// Reads the waiting position report if every car it names is over the
    /// line, or its wait is up - otherwise leaves it waiting.
    /// </summary>
    private void SayWaitingPositionReport()
    {
        WaitingPositionReport waiting;
        List<PositionReportOrder.Row> rows;
        int named;

        lock (_positionReportLock)
        {
            if (_waitingPositionReport is not { } pending)
            {
                return;
            }

            if (RunningOrderNow(pending.Depth) is not { } now)
            {
                // The board changed under the read; the next crossing, or
                // the end of the wait, reads it again.
                return;
            }

            var settled = PositionReportOrder.SettledCount(now, pending.Lap);
            var waitIsUp = TimingClock.NowMs >= pending.ReadByMs;

            if (settled < now.Count && !waitIsUp)
            {
                return;
            }

            _waitingPositionReport = null;
            waiting = pending;
            named = now.Count;
            rows = now.Take(settled).ToList();
        }

        if (rows.Count < named)
        {
            var missing = named - rows.Count;
            StatusMessage?.Invoke(rows.Count < 2
                ? $"[voice] Dropped the lap {waiting.Lap} position report - the cars behind the leader had not crossed the line in time."
                : $"[voice] Position report read without the last {Plural.Of(missing, "car")} - not over the line in time.");
        }

        if (rows.Count < 2)
        {
            return;
        }

        SayPositionReport(rows);
    }

    /// <summary>
    /// Words and queues a running order that is ready to be read: every car
    /// in <paramref name="rows"/> is over the line on the leader's lap.
    /// </summary>
    private void SayPositionReport(List<PositionReportOrder.Row> rows)
    {
        // Looked up again: the club may have switched it off, or a caution
        // come out, while it waited for the cars.
        var config = _voiceStore.Current.Events.GetValueOrDefault(nameof(AnnouncementEventType.PositionReport));
        if (config is null || !CallOn(config) || config.Templates.Count == 0)
        {
            return;
        }

        if (HeldByCaution(AnnouncementEventType.PositionReport, config))
        {
            return;
        }

        // Or the dial turned down - see MaybeAnnouncePositions.
        if (!DialLetsItSpeak(AnnouncementEventType.PositionReport, config))
        {
            StatusMessage?.Invoke(
                $"[voice] PositionReport held - the announcer is set to {ShowChatter.For(Chatter).Name}.");
            return;
        }

        var depth = rows.Count;
        var leader = rows[0].Standing;

        // The line is picked BEFORE the order is worded, because what the
        // club asked for changes how the list reads. Still one line per
        // report - only the order of these two steps moved.
        var template = TemplateBag.Pick(config.Templates);

        // The gaps are said only if the club switched them on AND left
        // somewhere for them to go. Reading the tick box alone would let a
        // club tick it, have no {gaps} in the line they wrote, and get a
        // report that said LESS than before - because the gap comes out of
        // the running order below when {gaps} is carrying it.
        var sayGaps = config.PositionReportSayGaps
            && template.Contains("{gaps}", StringComparison.OrdinalIgnoreCase);

        // And with the box unticked, no gaps at all - just the places
        // (9 Nov 2026). The box is labelled "Say each car's gap to the
        // leader", but unticked it only moved the gaps from {gaps} back
        // into the order, so a club that turned it off still heard "Bill
        // one point two three four back" on every report. Ticked with no
        // {gaps} in the line, they are said after each name as before.
        var placesOnly = !config.PositionReportSayGaps;

        // A heat classes share is worded by class place and never with gaps,
        // whatever the box says (WordSharedRunningOrder), so it is places
        // only for the checks below too (13 Nov 2026, bug hunt 17): with the
        // box ticked, a car it named crossing the line in the same place had
        // the report worded again to the same words, then thrown away as
        // "the order moved" - see PositionReportOrder.Moved.
        if (SharedHeatClasses?.Invoke() is { Count: > 0 })
        {
            placesOnly = true;
        }

        var text = WordRunningOrder(rows, template, sayGaps, placesOnly);

        if (text.Length == 0)
        {
            return;
        }

        // NOT run through the per-lap cap. That cap exists to stop several
        // REACTIONS to the same lap piling up, and it keeps the highest
        // weighted ones. A position report is a scheduled summary rather
        // than a reaction, and at weight 55 it lost every contest against
        // streak (70), fast lap (90) and leader change (100) - on a clean
        // run a streak fires almost every lap, so with the default cap of
        // two the report could never win a slot and was never heard at all.
        //
        // It already rations itself by only firing every N leader laps,
        // which is the right control for this announcement.
        //
        // Read at this event's own pace. A running order is the longest
        // line the announcer says, and the profile speed that suits a
        // two-word lap call rattles straight through it.
        var reportSpeed = config.SpeedFor(_voiceStore.Current.Speed);

        // The run-through, when a sponsor presents it.
        var (sponsorTag, countMention, releaseMention) = SponsorTag(AnnouncementEventType.PositionReport);
        text = (text + sponsorTag).Trim();

        // Not dropped for how long it waits for the speaker (1 Nov 2026).
        // It used to be, past three seconds, because its words were written
        // here and could be out of date by the time they were said. They
        // are read against the board again just before they are spoken
        // (below), so a wait no longer makes them wrong - and since the
        // report waits for the last car it names, it is queued the moment
        // that car crosses, right behind that car's own lap call, streak or
        // fast lap: it nearly always waited longer than three seconds, and
        // was hardly ever heard. A newer report still replaces one waiting
        // (EnqueueVoiceWork), so they cannot pile up.
        //
        // The text goes up only if the report is accepted - see
        // EnqueueVoiceWork. A report turned down (or kept and then lost)
        // gives the sponsor's ration back (20 Oct 2026, Bug Hunt 8).
        // The report's work, for the words it was queued with. Its own
        // function (9 Nov 2026) so a report dropped while it was rendered -
        // the order moved under it - can be queued once more, worded from
        // the board as it then stands, rather than leave the room waiting
        // laps for the next one. Nothing of the first was heard, so the
        // second is an ordinary report, not a correction.
        Func<Task> Work(IReadOnlyList<PositionReportOrder.Row> queuedRows, string queuedText, bool isRetry) => async () =>
        {
            // WHY (23 Oct 2026, bug hunt 10): counted only once the line is
            // certain to be heard (SynthesizeAndPlayAsync's spoken); a line
            // dropped for no voice, a flag, the countdown or a mute - or
            // whose synthesis threw - gives the reservation back instead.
            var heard = false;
            var movedWhileRendered = false;
            var handedOn = false;
            PositionReportOnAir? onAir = null;

            try
            {
                // Read again just before it is said (1 Nov 2026). The
                // words were written when the last car named crossed,
                // and the board can still move while the report waits
                // for the speaker - a penalty, or a car further back
                // getting by and over the line. When anything
                // in the order has moved, it is worded again from the
                // board as it stands, in the same line the club wrote,
                // and the screens are given the new words. See
                // PositionReportOrder.
                var said = queuedText;
                IReadOnlyList<PositionReportOrder.Row> worded = queuedRows;

                // Places only, a car over the line in the same place changes
                // no word - see PositionReportOrder.Moved (10 Nov 2026).
                if (RunningOrderNow(depth) is { Count: >= 2 } now && PositionReportOrder.Moved(queuedRows, now, placesOnly))
                {
                    var reworded = WordRunningOrder(now, template, sayGaps, placesOnly);

                    if (reworded.Length > 0)
                    {
                        said = (reworded + sponsorTag).Trim();
                        worded = now;
                        StatusMessage?.Invoke(
                            "[voice] Position report worded again - the order moved while it waited for the speaker (\""
                            + queuedText + "\" is now \"" + said + "\").");
                        RaiseAnnouncementText(said, now[0].Standing.Transponder);
                    }
                }

                // And again once it is rendered (8 Nov 2026): on eleven_v3
                // that is seconds, long enough for the order or its gaps to
                // move again. Dropped - and queued once more, below.
                await SynthesizeAndPlayAsync(said, isRetry ? "[voice] Position report (again)" : "[voice] Position report", speedOverride: reportSpeed,
                    whyNotNow: () =>
                    {
                        if (RunningOrderNow(depth) is { Count: >= 2 } after && PositionReportOrder.Moved(worded, after, placesOnly))
                        {
                            movedWhileRendered = true;
                            return "the order moved while it was being rendered";
                        }

                        return null;
                    },
                    spoken: () =>
                    {
                        heard = true;
                        countMention?.Invoke();

                        // From here the board can cut it off and correct
                        // it (9 Nov 2026) - see AnnouncementService.PositionReportCut.
                        onAir = PutPositionReportOnAir(OrderOf(worded), depth);
                    });

                if (movedWhileRendered && !heard && !isRetry)
                {
                    handedOn = RequeuePositionReport();
                }
            }
            finally
            {
                TakePositionReportOffAir(onAir);

                // A report queued again carries the sponsor's ration on.
                if (!heard && !handedOn)
                {
                    releaseMention?.Invoke();
                }
            }
        };

        // Once only: a second report the order moves under is dropped, as
        // every report was before - on a busy short track it could otherwise
        // be rendered and thrown away for the rest of the race.
        bool RequeuePositionReport()
        {
            // The same gates the report itself passed - a caution or the dial
            // may have come in while it was rendered.
            if (Muted || _postRaceQuiet
                || HeldByCaution(AnnouncementEventType.PositionReport, config)
                || !DialLetsItSpeak(AnnouncementEventType.PositionReport, config)
                || RunningOrderNow(depth) is not { Count: >= 2 } again)
            {
                return false;
            }

            var againText = WordRunningOrder(again, template, sayGaps, placesOnly);

            if (againText.Length == 0)
            {
                return false;
            }

            againText = (againText + sponsorTag).Trim();

            var queued = EnqueueVoiceWork(AnnouncementEventType.PositionReport, again[0].Standing.Transponder,
                Work(again, againText, isRetry: true), displayText: againText, lost: releaseMention);

            StatusMessage?.Invoke(queued
                ? "[voice] Position report queued again, worded from the board as it stands now."
                : "[voice] Position report not queued again - the queue turned it down.");

            return queued;
        }

        var accepted = EnqueueVoiceWork(AnnouncementEventType.PositionReport, leader.Transponder,
            Work(rows, text, isRetry: false),
            displayText: text,
            lost: releaseMention);

        if (!accepted)
        {
            releaseMention?.Invoke();
        }
    }

    /// <summary>
    /// The words of a running order: the club's <paramref name="template"/>
    /// with {positions} - and {gaps}, when <paramref name="sayGaps"/> - filled
    /// in from <paramref name="rows"/>, leader first. Empty when the line
    /// comes to nothing.
    /// </summary>
    /// <remarks>
    /// Its own method (1 Nov 2026) so one line can be worded twice: when the
    /// leader crosses, and again from the board as it stands when the order
    /// moved while the report waited for the speaker. See PositionReportOrder.
    /// </remarks>
    /// <param name="placesOnly">
    /// Each car by its place alone, no gap or laps down - for the correction
    /// after a report is cut off, where the gaps would be a lap old
    /// (AnnouncementService.PositionReportCut).
    /// </param>
    private string WordRunningOrder(IReadOnlyList<PositionReportOrder.Row> rows, string template, bool sayGaps, bool placesOnly = false)
    {
        // A heat classes share: places within each class (13 Nov 2026).
        if (SharedHeatClasses?.Invoke() is { Count: > 0 } classOf)
        {
            return WordSharedRunningOrder(rows, template, classOf);
        }

        var parts = new List<string>();

        // Each car read out with how far back it is, leader first - what
        // {gaps} is worded from. See AnnouncementText.SpokenGaps, which
        // drops the leader and anything that could not be measured.
        var order = new List<(string Name, RaceGap Gap)>();

        for (var i = 0; i < rows.Count; i++)
        {
            var name = SpokenNameFor(rows[i].Standing);

            if (i == 0)
            {
                // Nobody is in front of the leader, so there is no gap to
                // give for them. Measured against themselves it is zero,
                // and a voice reading "nought point nought" over the car
                // winning the race sounds like a dead heat.
                parts.Add($"{name} leads");
                order.Add((name, new RaceGap(0, null)));
                continue;
            }

            // Laps down is measured against the LEADER, at the line. This
            // report fires the instant the leader crosses, when everyone
            // behind still has one lap fewer on the counter - comparing lap
            // counts called nearly the whole field "a lap down", and said
            // "a lap" whatever the real difference was.
            //
            // Down a lap, that IS the whole answer: the seconds between the
            // two cars' last crossings are an accident of where each
            // happens to be on track, and reading them out would tell a
            // spectator a lapped car was four seconds off the lead. On the
            // lead lap: the gap to the leader, measured when each of them
            // crossed the same lap. Both are worked out in
            // PositionReportOrder.Read.
            var vsLeader = rows[i].VsLeader;
            var gap = rows[i].Gap;

            order.Add((name, gap));

            if (sayGaps || placesOnly)
            {
                // Everything about how far back this car is goes into
                // {gaps}, so the order itself reads as places. Said in
                // both, the announcer gave every interval twice in one
                // breath, and "a lap down" twice for a lapped car.
                parts.Add($"{name} {Ordinal(i + 1)}");
                continue;
            }

            if (vsLeader.LapsDown == 1)
            {
                parts.Add($"then {name}, a lap down");
                continue;
            }

            if (vsLeader.LapsDown > 1)
            {
                parts.Add($"then {name}, {RaceNumbers.Words(vsLeader.LapsDown)} laps down");
                continue;
            }

            // Exactly, to the thousandth, and to the LEADER (1 Nov 2026):
            // the same figure as the board's "Gap to P1" column. Rounded
            // the way a gap is called - "a second and a half back", or
            // "right on his tail" for anything under a second and a half -
            // it was heard as the wrong gap under a screen that showed it.
            // See RaceNumbers.GapExactly.
            if (gap.GapMs is { } gapMs and >= 0)
            {
                parts.Add($"{name} {RaceNumbers.BehindExactly(gapMs)}");
            }
            else
            {
                parts.Add($"{name} {Ordinal(i + 1)}");
            }
        }

        var positions = string.Join(", ", parts);
        var gaps = sayGaps ? AnnouncementText.SpokenGaps(order) : string.Empty;

        // TidyTemplate rather than the plain .Trim() this used to end with:
        // with the gaps off, or with nothing measurable to say, {gaps}
        // leaves a marker behind and the clause the club wrote around it
        // has to go with it. That is the same tidy every other announcement
        // line gets, and the reason it replaced .Trim() everywhere else.
        return template
            .Replace("{positions}", positions, StringComparison.OrdinalIgnoreCase)
            .WithPositionGaps(gaps)
            .TidyTemplate();
    }

    /// <summary>
    /// The running order of a heat two or more classes share (13 Nov 2026,
    /// mixed-class heats, plans\race-night-build-plan.md 4.1): the same cars
    /// in the same overall order, each called by their place in their own
    /// class - "Mo leads Mod, Sam leads Stock, Bea second in Stock" - since
    /// that is the place their results and championship count. The cars
    /// named are the overall top, so a car's place among them is its place
    /// in its class on the whole board: everyone ahead of it is named too.
    ///
    /// No gaps and no laps down: both are measured to the overall leader,
    /// who in a shared heat may be in another class, so "a lap down" or
    /// "three seconds back" would be said of a car leading its own class.
    /// {gaps} is left empty and the club's line round it tidied, as with the
    /// gaps off. A car the map doesn't know (a ghost) is called by its place
    /// overall.
    /// </summary>
    private string WordSharedRunningOrder(IReadOnlyList<PositionReportOrder.Row> rows, string template, IReadOnlyDictionary<string, string> classOf)
    {
        var parts = new List<string>();
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < rows.Count; i++)
        {
            var name = SpokenNameFor(rows[i].Standing);

            if (!classOf.TryGetValue(rows[i].Standing.Transponder?.Trim() ?? string.Empty, out var cls) || string.IsNullOrWhiteSpace(cls))
            {
                parts.Add(i == 0 ? $"{name} leads" : $"{name} {Ordinal(i + 1)}");
                continue;
            }

            var place = counts[cls] = counts.GetValueOrDefault(cls) + 1;

            parts.Add(place == 1 ? $"{name} leads {cls}" : $"{name} {Ordinal(place)} in {cls}");
        }

        return template
            .Replace("{positions}", string.Join(", ", parts), StringComparison.OrdinalIgnoreCase)
            .WithPositionGaps(string.Empty)
            .TidyTemplate();
    }

    // Same resolution order the other announcements use: the profile's
    // driver name, falling back to the standing's own display name
    // (which itself falls back to the transponder ID).
    // Spoken as commentary, not read as a table.
    //
    // "P1 Bill, P2 Phantom, P3 Skywalker" is a data readout - there's
    // no shape in it for a voice to perform, so it lands flat however
    // it's tagged. Describing the RELATIONSHIPS between drivers gives
    // the read something to do, and the gaps supply the drama that a
    // list of row labels can't.
    private string SpokenNameFor(RaceStanding s)
    {
        var profile = _profiles.Get(s.Transponder);
        var saidAs = profile is not null && !string.IsNullOrWhiteSpace(profile.SpokenName)
            ? profile.SpokenName
            : s.DisplayName;

        // WHY (23 Oct 2026, bug hunt 10): a car with no announcer name is said by
        // its number, which must stay in the room - noted, so listen-along
        // and the public captions leave the line out (the podium videos did
        // too, until 28 Oct 2026, when they stopped going online). See
        // NoteSpokenAsNumber.
        NoteSpokenAsNumber(s.Transponder, saidAs);

        return saidAs;
    }


    /// <summary>Leader lap the last report was read on, so it fires once per interval rather than once per car.</summary>
    private int _lastPositionReportLap = -1;

    /// <summary>
    /// Race time at which each driver completed each lap, built from the
    /// standing updates as they arrive. Lets the position report measure a
    /// gap against the moment the car ahead crossed the SAME lap, rather
    /// than comparing lap counts at an instant when they're bound to differ.
    /// Also holds the copy of the board the position report reads off the
    /// timing thread - see LapCrossingBook.
    /// </summary>
    private readonly LapCrossingBook _lapCrossings = new();

    /// <summary>
    /// The lap crossings, and a copy of the board as of the last crossing,
    /// under one lock.
    ///
    /// WHY (6 Nov 2026, Bug hunt 11b): these were two plain dictionaries,
    /// written on the timing thread as each car crossed and read, with no
    /// lock, by the position report - from a thread-pool timer when its wait
    /// ran out and from the voice thread just before it was spoken
    /// (RunningOrderNow). A read that caught a dictionary growing could throw
    /// IndexOutOfRangeException, and a new car joining while the board was
    /// copied threw ArgumentException; only InvalidOperationException was
    /// caught, so the timer's report died in an unobserved task and the
    /// voice lost the one it was about to say. Every touch now goes through
    /// here: each one is a lookup or a reference swap, so the timing thread
    /// never waits behind anything long. The board is a fresh array per
    /// crossing, never changed after, so a reader can rank it at leisure.
    /// OnStandingRemoved and ResetSession forget a car (or everything) here
    /// through Remove and Clear, so the copy drops it when _standings does.
    /// </summary>
    /// <remarks>
    /// WHY (8 Nov 2026): a crossing is now worked out by the board's own rule
    /// whenever the standing carries its lap times (RaceStanding.LapTimesMs,
    /// as Race Control's always do) - RaceGaps.CrossingMs, the same call the
    /// leaderboard's gap columns make. It used to be the elapsed time written
    /// against the lap count on every update, corrections included, so after
    /// a penalty only the current lap's crossing moved, and a lap added by
    /// hand got a "crossing" at the time of the lap before. Car A over lap 10
    /// at 60.0 then given 5 s, B over lap 9 at 56.0: the voice measured B
    /// against A's old lap-9 time of 54.0 and said "two seconds back"; the
    /// board, against 59.0, showed "-". Every gap the announcer says
    /// (GapBetween, the lapped and close battle calls, the position report)
    /// reads from here, so all of them now match the board. A source with no
    /// lap times keeps the old book, less any crossing for a hand correction.
    /// </remarks>
    private sealed class LapCrossingBook
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, Dictionary<int, long>> _crossings = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, LapHistory> _histories = new(StringComparer.OrdinalIgnoreCase);
        private RaceStanding[] _board = [];

        /// <summary>A car's lap times as the session last gave them, with the lap count and elapsed time they go with. Never changed.</summary>
        private sealed record LapHistory(long[] LapTimesMs, int Laps, long ElapsedMs);

        /// <summary>A car's crossing, and the board with it on, as one change.</summary>
        public void Record(RaceStanding standing, RaceStanding[] board)
        {
            lock (_lock)
            {
                _board = board;

                if (standing.LapTimesMs is { } lapTimes)
                {
                    // The whole history, every update: a penalty or a lap
                    // added or taken away by hand reshapes earlier crossings
                    // too, exactly as it does on the board.
                    _histories[standing.Transponder] = new LapHistory(lapTimes.ToArray(), standing.Laps, standing.ElapsedMs);
                    _crossings.Remove(standing.Transponder);
                    return;
                }

                if (standing.Laps <= 0 || standing.ElapsedMs <= 0)
                {
                    return;
                }

                if (!_crossings.TryGetValue(standing.Transponder, out var laps))
                {
                    laps = new Dictionary<int, long>();
                    _crossings[standing.Transponder] = laps;
                }

                if (IsHandAdjustment(standing))
                {
                    // Nobody crossed the line. A lap taken away loses its
                    // crossing; a lap added gets none, as on the board.
                    foreach (var lap in laps.Keys.Where(lap => lap > standing.Laps).ToList())
                    {
                        laps.Remove(lap);
                    }

                    return;
                }

                laps[standing.Laps] = standing.ElapsedMs;
            }
        }

        /// <summary>Race time at which a car completed a lap, or null.</summary>
        public long? CrossingMs(string transponder, int lap)
        {
            lock (_lock)
            {
                if (_histories.TryGetValue(transponder, out var history))
                {
                    return RaceGaps.CrossingMs(history.LapTimesMs, history.Laps, history.ElapsedMs, lap);
                }

                return _crossings.TryGetValue(transponder, out var laps) && laps.TryGetValue(lap, out var t) ? t : null;
            }
        }

        /// <summary>Every standing as of the last crossing. Never changed once handed out.</summary>
        public RaceStanding[] Board
        {
            get
            {
                lock (_lock)
                {
                    return _board;
                }
            }
        }

        public void Remove(string transponder)
        {
            lock (_lock)
            {
                _crossings.Remove(transponder);
                _histories.Remove(transponder);
                // Ordinal, as _standings itself is keyed.
                _board = _board
                    .Where(s => !string.Equals(s.Transponder, transponder, StringComparison.Ordinal))
                    .ToArray();
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _crossings.Clear();
                _histories.Clear();
                _board = [];
            }
        }
    }

    /// <summary>Race time at which a car completed a lap, from the crossings seen this session, or null.</summary>
    private long? CrossingMs(string transponder, int lap) => _lapCrossings.CrossingMs(transponder, lap);

    /// <summary>
    /// The top <paramref name="depth"/> of the running order as the board
    /// has it right now, for a position report about to be spoken - or null
    /// if the standings changed under it while it was being read, in which
    /// case the report is said as it was worded.
    /// </summary>
    /// <param name="noGhosts">
    /// Every ghost left out, announced or not - for the winner's call, since
    /// a ghost never wins the race (bug hunt 20).
    /// </param>
    private List<PositionReportOrder.Row>? RunningOrderNow(int depth, bool noGhosts = false)
    {
        try
        {
            // The same field the report was built from: no muted ghosts.
            // The board as of the last crossing, not _standings itself: this
            // runs on a timer or the voice thread while the timing thread
            // writes _standings (6 Nov 2026, Bug hunt 11b - see LapCrossingBook).
            var board = _lapCrossings.Board;

            // No black-flagged car, ever (bug hunt 22): it is out of the race,
            // as in HandleStandingUpdated. For the winner's lines no ghost
            // either - a ghost never wins.
            var outOfIt = board.Where(s => s.BlackFlagged).Select(s => s.Transponder).ToHashSet(StringComparer.OrdinalIgnoreCase);

            Func<string, bool>? leaveOut = noGhosts
                ? transponder => GhostService.IsGhost(transponder) || outOfIt.Contains(transponder)
                : AnnounceGhostsProvider?.Invoke() == false
                    ? transponder => MutedGhost(transponder) || outOfIt.Contains(transponder)
                    : outOfIt.Count > 0
                        ? transponder => outOfIt.Contains(transponder)
                        : null;

            var ranked = PositionReportOrder.Rank(board, leaveOut);

            return PositionReportOrder.Read(ranked, depth, CrossingMs);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private RaceGap GapBetween(RaceStanding car, RaceStanding ahead) =>
        RaceGaps.Compute(car.Laps, car.ElapsedMs, ahead.Laps, ahead.ElapsedMs,
            lap => CrossingMs(ahead.Transponder, lap));

    /// <summary>
    /// The gap the announcer would give from <paramref name="car"/> back to
    /// <paramref name="ahead"/>, by transponder, as of the last update - or
    /// null while either is not on its board. For tests holding it to the
    /// board's RaceGaps.Between (8 Nov 2026).
    /// </summary>
    internal RaceGap? GapAsSpoken(string car, string ahead)
    {
        var board = _lapCrossings.Board;
        var behind = board.FirstOrDefault(s => s.Transponder == car);
        var front = board.FirstOrDefault(s => s.Transponder == ahead);

        return behind is null || front is null ? null : GapBetween(behind, front);
    }

    private void HandleStandingUpdated(RaceStanding standing)
    {
        // A crossing - not a correction by hand - starts the clock on every
        // call about it (8 Nov 2026). See AnnouncementService.Freshness.
        if (!IsHandAdjustment(standing))
        {
            NoteLapSeen(standing.Transponder);
        }
        else if (_standings.TryGetValue(standing.Transponder, out var before) && standing.Laps < before.Laps)
        {
            // A lap taken off by hand (bug hunt 15): an AI line written
            // about it is no longer true - see NoteLapTakenOffForFacts.
            NoteLapTakenOffForFacts();
        }

        _standings[standing.Transponder] = standing;

        // The copy for readers off this thread is taken here, on the one
        // thread that writes _standings (6 Nov 2026, Bug hunt 11b).
        _lapCrossings.Record(standing, _standings.Values.ToArray());

        // Same ranking rule as the leaderboard: most laps, then lowest elapsed.
        // A black-flagged car is out of the race (bug hunt 22, Asher's pick B):
        // it leaves the order quietly - the flag comes in as an adjustment, so
        // the car behind is made leader without a call - and is in no gap,
        // position report, battle or lapping from then on.
        var ordered = _standings.Values
            .Where(s => !s.BlackFlagged)
            .OrderByDescending(s => s.Laps)
            .ThenBy(s => s.ElapsedMs)
            .ToList();

        var leader = ordered.FirstOrDefault();
        if (leader is null)
        {
            return;
        }

        // A correction by hand is not a pass (8 Nov 2026): the order is kept
        // in step, and nothing is said - see KeepInStepWithCorrection.
        if (IsHandAdjustment(standing))
        {
            KeepInStepWithCorrection(ordered);

            // A report being read out that the ruling has made wrong is cut
            // and corrected as the ruling it was (9 Nov 2026).
            CorrectPositionReportOnAir(byRuling: true);
            return;
        }

        MaybeAnnouncePositions(ordered, leader);

        // Everything below is about the real field while ghosts are not
        // announced, the same rule as the session's RaceLeader(). A pace
        // ghost on top used to become the remembered leader: its own lead
        // call was rightly dropped, but a real driver then taking the lead
        // behind it was never called, and the call came laps late when the
        // ghost dropped back. It could also be named as a rival or chaser.
        ordered = ordered.Where(s => !MutedGhost(s.Transponder)).ToList();
        leader = ordered.FirstOrDefault();
        if (leader is null)
        {
            return;
        }

        // A battle line still being said about two cars that have just
        // swapped is cut off here, and the pass called in its place (1 Nov
        // 2026) - see AnnouncementService.BattleLines. Before the lead
        // change call, because for a fight for the lead the pass call IS
        // the lead change call, and one crossing must not queue both.
        var passCalledTheLead = CallTheBattlePass(ordered);

        // And a position report being read out when a car it names has
        // changed place (9 Nov 2026) - its correction says a new leader the
        // same way. See AnnouncementService.PositionReportCut.
        passCalledTheLead |= CorrectPositionReportOnAir(byRuling: false);

        // In a most-laps qualifier the lead IS provisional pole, called from
        // the same crossing (CallProvisionalPoleIfTaken). Saying both put two
        // lines in the queue for one pass, so the lead change keeps its chime
        // and leaves the words to the pole call while that is switched on.
        // WHY (11 Nov 2026, Phase 5.1): points qualifying ranks a heat the
        // same way - most laps, then time (QualifyingLeader) - so its lead is
        // the pole call too.
        var poleSaysIt = IsQualifyingMode && _qualifyingMethod != QualifyingMethod.BestConsecutiveLaps
            && CallOn(_voiceStore.Current.Events.GetValueOrDefault(nameof(AnnouncementEventType.ProvisionalPole)));

        // Counted whatever the call does - the broadcast tower shows the
        // count even with the voice switched off. See LeadTracker.
        var leadChange = _leads.Observe(leader.Transponder);

        // And the stream's banner, voice or no voice - but not for a lead in
        // practice, which is nobody's, or in a qualifier, where the top of
        // the order is provisional pole and has a call of its own.
        if (leadChange is not null && !IsPracticeMode && !IsQualifyingMode)
        {
            PostLeadChangeMoment(leadChange);
        }

        // The race as an AI line written now would read it (8 Nov 2026) - see
        // AnnouncementService.Freshness. Whatever the calls below do.
        if (_currentLeaderTransponder is not null && leader.Transponder != _currentLeaderTransponder)
        {
            NoteLeadChangeForFacts();
        }

        NoteLeaderLapsForFacts(leader.Laps);

        if (_currentLeaderTransponder is not null && leader.Transponder != _currentLeaderTransponder)
        {
            // Made ready while they were nose to tail, when it was this pass
            // (11 Nov 2026) - its render is handed over and its words kept,
            // so the call below says the same and plays at once. See PassAhead.
            var predicted = TakeThePredictedPass(leader.Transponder, leadChange);

            // Only offered when they could be heard - see VoiceCandidateCanSpeak.
            var soundCfg = _soundStore.Current.Effects.GetValueOrDefault(nameof(SoundEffectKind.LeaderChange));
            if (SoundCandidateCanPlay(SoundEffectKind.LeaderChange, soundCfg))
            {
                ConsiderPerLapAction("sound", leader.Transponder, soundCfg.Weight, Math.Max(1, _soundStore.Current.MaxEffectsPerLap),
                    () => PlaySound(SoundEffectKind.LeaderChange, ToneGenerator.PlayLeaderChangeAsync, "leader change sound"));
            }

            var voiceCfg = _voiceStore.Current.Events.GetValueOrDefault(nameof(AnnouncementEventType.LeaderChange));
            if (!poleSaysIt && !passCalledTheLead && VoiceCandidateCanSpeak(AnnouncementEventType.LeaderChange, voiceCfg))
            {
                // The detail a broadcast booth gives with a lead change -
                // who it was taken from, how long they had it, and how
                // many times it has changed hands. Worked out now, while
                // the order is the one the change was seen in.
                var leadExtras = LeadChangeExtras(leadChange);
                var leadTail = leadChange is not null && voiceCfg.IncludeLeadChangeCount
                    ? predicted?.Tail ?? LeadTracker.Tail(leadChange)
                    : string.Empty;

                // Tagged with the two cars (3 Nov 2026), so "Sam takes the
                // lead" is cut off if Bill is straight back past him before
                // it is said - see AnnouncementService.BattleLines. Inside
                // the action: the scope has to be open when the batch queues it.
                var took = BattlePair.Passed(leader.Transponder, _currentLeaderTransponder, 1);

                ConsiderPerLapAction("voice", leader.Transponder, voiceCfg.Weight, MaxVoicePerLap, () =>
                {
                    using (AboutBattle(took))
                    {
                        EnqueueVoice(AnnouncementEventType.LeaderChange, leader, null, extras: leadExtras,
                            autoTail: leadTail.Length > 0 ? ("{leadchanges}", leadTail) : null);
                    }
                });
            }
        }

        _currentLeaderTransponder = leader.Transponder;

        // The next pass for the lead, made ready if a car is right behind
        // (11 Nov 2026) - see PassAhead.
        PredictTheLeadChange(ordered);

        // And the gap for the lead, each lap, at the end of the race (11 Nov
        // 2026) - see FinalLapsGap.
        CheckFinalLapsGap(ordered, standing);

        // A hundredth race, a thousandth lap (11 Nov 2026) - see DriverHistory.
        CheckMilestones(standing);

        CheckLapped(ordered);
        CheckCloseBattles(ordered);
        CheckRivalries(ordered);
    }

    /// <summary>
    /// What HandleStandingUpdated does for a penalty, or a lap added or taken
    /// away by hand: the announcer's idea of the race moved to match the
    /// board, with no call, sound, banner, battle cut or rivalry line.
    /// </summary>
    /// <remarks>
    /// WHY (8 Nov 2026): Race Control's OnStandingAdjusted promised no call
    /// for an adjustment, but the standing went through the lead change call,
    /// the lapped, close battle and rivalry checks and the battle-line pass
    /// cut like any crossing - so a 10 s penalty could make the room hear
    /// "Sam takes the lead" or "Bill has been lapped" for a pass nobody made.
    /// Skipping the lot was not enough either: the next real crossing would
    /// then find the stale leader, lapping or rivalry order and call the
    /// change a lap late. So each is brought into step here, quietly:
    ///
    ///   the leader         _currentLeaderTransponder, and the lead tracker
    ///                      (LeadTracker.Correct - not counted as a change)
    ///   lappings           _lappedAnnounced raised to what the board shows
    ///   rivalries          who is ahead, remembered without a line
    ///   battle lines       SettleBattleLinesAfterCorrection
    ///
    /// No running order is started either. A close battle is left alone - it
    /// is once per pair and marked only when said, so a real crossing with
    /// the two still close calls it as it would have.
    ///
    /// The AI's race-facts counters DO move on: a penalty that changes the
    /// lead makes an AI line about the old leader as untrue as a pass would,
    /// and it must be dropped the same way (AnnouncementService.Freshness).
    /// </remarks>
    private void KeepInStepWithCorrection(List<RaceStanding> ordered)
    {
        // The real field, as below in HandleStandingUpdated.
        var field = ordered.Where(s => !MutedGhost(s.Transponder)).ToList();

        if (field.FirstOrDefault() is not { } leader)
        {
            return;
        }

        if (_currentLeaderTransponder is not null && leader.Transponder != _currentLeaderTransponder)
        {
            NoteLeadChangeForFacts();
        }

        NoteLeaderLapsForFacts(leader.Laps);

        _leads.Correct(leader.Transponder);
        _currentLeaderTransponder = leader.Transponder;

        // Only ever up, as in CheckLapped.
        foreach (var car in field.Skip(1))
        {
            var lapsDown = GapBetween(car, leader).LapsDown;

            if (lapsDown > _lappedAnnounced.GetValueOrDefault(car.Transponder))
            {
                _lappedAnnounced[car.Transponder] = lapsDown;
            }
        }

        CheckRivalries(field, call: false);
        SettleBattleLinesAfterCorrection();
    }

    /// <summary>
    /// A race put back - an undone Stage or Reset, or one restored after a
    /// crash - loaded into the running order quietly: its standings, the
    /// leader, the lappings already shown and the report lap, with no call.
    /// </summary>
    /// <remarks>
    /// WHY (9 Nov 2026, Bug hunt 15): Stage over a running race resets the
    /// announcer, and the undo put the race back on the board but not here.
    /// After the green the first car over - a back-marker, say - was the
    /// whole field to the announcer and its silent "leader", so the real
    /// leader crossing next was called as taking the lead, every lapped car
    /// was called lapped again, and a position report could be read off the
    /// few cars seen so far. Routing the standings through OnStandingUpdated
    /// would make exactly those calls, so this sets them as
    /// KeepInStepWithCorrection does a correction.
    /// </remarks>
    public void SeedStandings(IEnumerable<RaceStanding> standings)
    {
        lock (_voiceQueueLock)
        {
            _standings.Clear();
            _lapCrossings.Clear();

            foreach (var standing in standings)
            {
                _standings[standing.Transponder] = standing;
            }

            var board = _standings.Values.ToArray();
            foreach (var standing in board)
            {
                _lapCrossings.Record(standing, board);
            }

            var ordered = board
                .Where(s => !s.BlackFlagged)
                .OrderByDescending(s => s.Laps)
                .ThenBy(s => s.ElapsedMs)
                .ToList();

            // The report's leader by its own rule - see MaybeAnnouncePositions -
            // so the lap the race is on is not read out again at the green.
            var reportLeader = AnnounceGhostsProvider?.Invoke() == false
                ? ordered.FirstOrDefault(s => !MutedGhost(s.Transponder))
                : ordered.FirstOrDefault();
            _lastPositionReportLap = reportLeader?.Laps ?? -1;

            var field = ordered.Where(s => !MutedGhost(s.Transponder)).ToList();
            if (field.FirstOrDefault() is not { } leader)
            {
                _currentLeaderTransponder = null;
                return;
            }

            NoteLeaderLapsForFacts(leader.Laps);

            _leads.Correct(leader.Transponder);
            _currentLeaderTransponder = leader.Transponder;

            // Only ever up, as in CheckLapped.
            foreach (var car in field.Skip(1))
            {
                var lapsDown = GapBetween(car, leader).LapsDown;

                if (lapsDown > _lappedAnnounced.GetValueOrDefault(car.Transponder))
                {
                    _lappedAnnounced[car.Transponder] = lapsDown;
                }
            }

            CheckRivalries(field, call: false);
        }
    }

    /// <summary>
    /// The lead changes of this race - how many, among how many drivers.
    /// Read by the broadcast graphics (WebServerService) as well as used for
    /// the lead change call, so the two always say the same number.
    /// </summary>
    public LeadTracker Leads => _leads;

    private readonly LeadTracker _leads = new();

    /// <summary>
    /// {leadchanges} {leaders} {previous} {previouslaps} (laps they led all race, not just this spell) {backinfront} for a
    /// lead change call. Null when there is nothing to say beyond the name -
    /// the first change of a race has no history to give.
    /// </summary>
    private IReadOnlyDictionary<string, (string Spoken, string Display)>? LeadChangeExtras(LeadChange? change)
    {
        if (change is null)
        {
            return null;
        }

        var previousLaps = _standings.TryGetValue(change.Previous, out var previous) ? previous.LapsLed : 0;
        var inv = System.Globalization.CultureInfo.InvariantCulture;

        // A count of one is left empty so a line built round it drops the
        // clause (TidyTemplate) rather than saying "the first lead change"
        // about a race that has only just had one.
        var changes = change.Number >= 2 ? LeadTracker.SpokenOrdinal(change.Number) : string.Empty;
        var changesShown = change.Number >= 2 ? change.Number.ToString(inv) : string.Empty;

        return new Dictionary<string, (string Spoken, string Display)>
        {
            ["leadchanges"] = (changes, changesShown),
            ["leaders"] = (change.Leaders >= 2 ? LeadTracker.SpokenCount(change.Leaders) : string.Empty,
                change.Leaders >= 2 ? change.Leaders.ToString(inv) : string.Empty),
            ["previous"] = (DisplayNameFor(change.Previous), DisplayNameFor(change.Previous)),
            ["previouslaps"] = (previousLaps > 0 ? Plural.Of(previousLaps, "lap") : string.Empty,
                previousLaps > 0 ? Plural.Of(previousLaps, "lap") : string.Empty),
            ["backinfront"] = (LeadTracker.BackInFront(change), LeadTracker.BackInFront(change)),
        };
    }

    /// <summary>The longest current streak among everyone except this driver.</summary>
    private (string? Transponder, int Count) BestStreakOtherThan(string transponder)
    {
        string? best = null;
        var bestCount = 0;
        var cutoff = DateTime.UtcNow - StreakStaleAfter;

        lock (_streakLock)
        {
            foreach (var (other, entry) in _currentStreaks)
            {
                // A muted ghost is not the driver to beat - it would be
                // named aloud as the chaser of a real driver's run.
                if (string.Equals(other, transponder, StringComparison.OrdinalIgnoreCase)
                    || MutedGhost(other)
                    || entry.Count <= bestCount
                    || entry.AtUtc < cutoff)
                {
                    continue;
                }

                best = other;
                bestCount = entry.Count;
            }
        }

        return (best, bestCount);
    }

    /// <summary>This driver's run as it stands right now.</summary>
    private int CurrentStreakOf(string transponder)
    {
        lock (_streakLock)
        {
            return _currentStreaks.TryGetValue(transponder, out var entry) ? entry.Count : 0;
        }
    }

    /// <summary>
    /// Queues a streak call whose NUMBER is worked out when it is spoken,
    /// not when the lap was crossed.
    ///
    /// The old path baked the count into the text at the crossing, then the
    /// line waited its turn behind the per-lap batch window and whatever
    /// else was speaking. On a busy grid that could be several laps, so the
    /// announcer said "five in a row" while the board showed eight - and
    /// carried on saying it after the run had already ended.
    /// </summary>
    private void EnqueueStreakAnnouncement(string transponder, AnnouncementEventConfig config, int lapNumber)
    {
        EnqueueVoiceWork(AnnouncementEventType.Streak, transponder, async () =>
        {
            var profile = _voiceStore.Current;

            // Checked again now it is about to be spoken: the line can wait
            // several laps behind other calls, and a flag can come out - or
            // Streak be switched off - in that time. The flag's own clear-out
            // only empties what was queued at that moment. Read from the
            // live profile, since the settings may have been replaced since
            // the crossing.
            var live = profile.Events.GetValueOrDefault(nameof(AnnouncementEventType.Streak));
            if (live is null || !CallOn(live))
            {
                StatusMessage?.Invoke($"[voice] Streak call for {DisplayNameFor(transponder)} dropped - switched off before it could be spoken.");
                return;
            }

            if (HeldByCaution(AnnouncementEventType.Streak, live))
            {
                StatusMessage?.Invoke($"[voice] Streak call for {DisplayNameFor(transponder)} dropped - a flag came out before it could be spoken.");
                return;
            }

            if (config.EffectiveTemplates.Count == 0)
            {
                return;
            }

            var threshold = _soundStore.Current.StreakThresholdFor(IsPracticeMode);
            var streak = CurrentStreakOf(transponder);

            // The run may have ended while this was waiting to be spoken.
            if (streak < threshold)
            {
                StatusMessage?.Invoke($"[voice] Streak call for {DisplayNameFor(transponder)} dropped - the run ended before it could be spoken.");
                return;
            }

            var (chaser, chaserCount) = BestStreakOtherThan(transponder);

            if (config.OnlyLongestStreak && streak < chaserCount)
            {
                StatusMessage?.Invoke($"[voice] Streak call for {DisplayNameFor(transponder)} dropped - {DisplayNameFor(chaser ?? string.Empty)} is on a longer run now ({chaserCount}).");
                return;
            }

            var driver = DisplayNameFor(transponder);
            var template = TemplateBag.Pick(config.EffectiveTemplates);

            var text = template
                .Replace("{driver}", driver, StringComparison.OrdinalIgnoreCase)
                .Replace("{car}", AnnouncementText.OrMissing(_profiles.CarModelFor(transponder)), StringComparison.OrdinalIgnoreCase)
                .Replace("{streak}", streak.ToString(), StringComparison.OrdinalIgnoreCase)
                // Captured at the crossing: the standings are the UI
                // thread's to touch, and this runs on the voice queue's.
                .Replace("{lap}", lapNumber.ToString(), StringComparison.OrdinalIgnoreCase)
                .WithDriverDetails(_profiles.Get(transponder))
                .TidyTemplate();

            if (text.Length == 0)
            {
                return;
            }

            // The runner-up, also as it stands now. Skipped when the two
            // share a spoken name - "Ghost is on 11" straight after a line
            // about Ghost reads as a contradiction rather than a battle.
            if (config.MentionStreakChaser && chaser is not null && chaserCount > 0)
            {
                var chaserName = DisplayNameFor(chaser);

                if (!string.Equals(chaserName, driver, StringComparison.OrdinalIgnoreCase))
                {
                    text = $"{text} {chaserName} is on {chaserCount}.";
                }
            }

            RaiseAnnouncementText(text, transponder);
            StatusMessage?.Invoke($"[voice] Streak ({streak} for {driver}): \"{text}\"");

            await SynthesizeAndPlayAsync(profile.SanitiseForModel(text), "[voice] Announcing",
                speedOverride: config.SpeedFor(profile.Speed));
        });
    }

    /// <summary>
    /// Announces the longest clean run CURRENTLY GOING changing hands.
    ///
    /// Deliberately about runs in progress, not the best anyone has managed
    /// all race: a driver whose 12-lap run ended is no longer on a run, so
    /// the 7 laps someone is on now really is the longest one out there.
    /// The default lines say "going right now" for that reason - calling it
    /// the longest of the race would be a claim this doesn't check.
    ///
    /// Fires on the lap the lead changes hands and not again while the same
    /// driver holds it, or it would be a second streak announcement every
    /// lap - the noise this feature exists to cut.
    /// </summary>
    private void CheckStreakLead(string transponder, int streakCount, string? chaser, int chaserCount)
    {
        // A ghost that isn't announced never holds the longest run. It used
        // to become the holder - only its line was dropped - and a pace
        // ghost lapping cleanly every lap then kept every real driver from
        // being called as taking it over.
        if (MutedGhost(transponder))
        {
            return;
        }

        // StreakThresholdFor floors at two on its own now, so the clamp
        // that used to live here would be a second authority on the same
        // number - see the comment on it.
        var threshold = _soundStore.Current.StreakThresholdFor(IsPracticeMode);

        if (streakCount < threshold)
        {
            // Their run ended; whoever is left can take the lead next lap.
            if (string.Equals(_streakLeader, transponder, StringComparison.OrdinalIgnoreCase))
            {
                _streakLeader = null;
                _streakLeaderCount = 0;
            }

            return;
        }

        var alreadyLeading = string.Equals(_streakLeader, transponder, StringComparison.OrdinalIgnoreCase);

        if (alreadyLeading)
        {
            _streakLeaderCount = streakCount;
            return;
        }

        // A holder who has stopped crossing the line no longer holds a run
        // in progress. Their count was only ever refreshed or cleared on
        // THEIR OWN crossings, so a car that pulled off with a dead battery
        // on 12 kept 12 as the bar everyone else had to beat - a driver on a
        // clean 7, 8, 9 wasn't called until 13, and never if their run ended
        // first. The runner-up count (BestStreakOtherThan) already leaves a
        // run untouched for StreakStaleAfter out for exactly this reason;
        // the stored holder now follows the same rule. A holder still
        // lapping is in chaserCount anyway, so nothing changes for them.
        //
        // Read, tested and cleared all inside the lock. ResetSession nulls
        // the holder under the same lock from another thread, and testing
        // it for null outside and reading it again inside could hand
        // TryGetValue a null key - which throws.
        var cutoff = DateTime.UtcNow - StreakStaleAfter;
        lock (_streakLock)
        {
            var holder = _streakLeader;

            // A muted ghost too: one holding it when "Announce ghosts like
            // real drivers" was switched off mid-race.
            if (holder is not null
                && (MutedGhost(holder) || !(_currentStreaks.TryGetValue(holder, out var held) && held.AtUtc >= cutoff)))
            {
                _streakLeader = null;
                _streakLeaderCount = 0;
            }
        }

        // A takeover means passing the field, not merely matching it - on
        // equal counts the driver who got there first keeps it.
        if (streakCount <= chaserCount || streakCount <= _streakLeaderCount)
        {
            return;
        }

        // The name and the number have to describe the SAME driver, and
        // they didn't: {rival} came from the stored PREVIOUS leader while
        // {rivalstreak} came from whoever currently holds the best other
        // run. On any grid bigger than two those are usually different
        // people, so the line named one driver and quoted another's streak -
        // which is what made the runner-up almost always wrong.
        //
        // Both now come from the live runner-up, and only when they are
        // actually on a run worth naming. Otherwise there is no rival and
        // it's simply the field.
        var rival = chaserCount >= threshold ? chaser : null;

        _streakLeader = transponder;
        _streakLeaderCount = streakCount;

        var config = _voiceStore.Current.Events.GetValueOrDefault(nameof(AnnouncementEventType.StreakLead));

        // The dial too (7 Nov 2026, bug hunt 13): this line skips
        // EnqueueVoice, so without it a takeover was called at Sparing -
        // and took a per-lap slot by its weight - while the plain streak
        // beside it was held. See VoiceCandidateCanSpeak.
        if (Muted || config is null || !CallOn(config) || config.EffectiveTemplates.Count == 0
            || HeldByCaution(AnnouncementEventType.StreakLead, config)
            || !DialLetsItSpeak(AnnouncementEventType.StreakLead, config))
        {
            return;
        }

        var template = TemplateBag.Pick(config.EffectiveTemplates);

        var text = template
            .Replace("{driver}", DisplayNameFor(transponder), StringComparison.OrdinalIgnoreCase)
            .Replace("{car}", AnnouncementText.OrMissing(_profiles.CarModelFor(transponder)), StringComparison.OrdinalIgnoreCase)
            .Replace("{streak}", streakCount.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{rival}", rival is null ? "the field" : DisplayNameFor(rival), StringComparison.OrdinalIgnoreCase)
            .Replace("{rivalstreak}", (rival is null ? 0 : Math.Max(chaserCount, 0)).ToString(), StringComparison.OrdinalIgnoreCase)
            .WithDriverDetails(_profiles.Get(transponder))
            .TidyTemplate();

        if (text.Length == 0)
        {
            return;
        }

        var speed = config.SpeedFor(_voiceStore.Current.Speed);

        // The text goes up from inside the per-lap action, and only once
        // the line is accepted - not here, when it is merely offered. It
        // used to be raised first, so a takeover that lost its slot to a
        // fast lap and a lead change, or was dropped for the announcer being
        // busy, was still put on the Big Screen and still made the crowd
        // ambience surge for a call nobody heard. See EnqueueVoiceWork.
        ConsiderPerLapAction("voice", transponder, config.Weight, MaxVoicePerLap,
            () => EnqueueVoiceWork(AnnouncementEventType.StreakLead, transponder,
                () => SynthesizeAndPlayAsync(text, "[voice] Longest streak", speedOverride: speed),
                displayText: text,
                renderAhead: () => RenderLineAheadAsync(text, AnnouncementEventType.StreakLead, speed)));
    }

    /// <summary>
    /// Announces a driver going a lap down to the leader.
    ///
    /// The "Car gets lapped" event has been in Voice Settings all along with
    /// nothing firing it. Measured at the line (see RaceGaps) rather than by
    /// lap count, or it would fire on lap one for the whole field - and once
    /// per lap lost, so a driver two laps down is announced twice, not on
    /// every standings update in between.
    /// </summary>
    private void CheckLapped(List<RaceStanding> ordered)
    {
        if (ordered.Count < 2)
        {
            return;
        }

        var leader = ordered[0];

        foreach (var car in ordered.Skip(1))
        {
            var lapsDown = GapBetween(car, leader).LapsDown;
            var announced = _lappedAnnounced.GetValueOrDefault(car.Transponder);

            // Only ever counts up. A lap count corrected downward by hand
            // shouldn't re-announce a lapping that already happened.
            if (lapsDown > announced)
            {
                _lappedAnnounced[car.Transponder] = lapsDown;
                FirePairLine(AnnouncementEventType.Lapped, leader.Transponder, car.Transponder);
            }
        }
    }

    /// <summary>
    /// How long a close battle offered to the per-lap batch counts as
    /// pending before it may be offered again.
    ///
    /// A batch normally settles within PerLapBatchWindow, and a settled line
    /// clears its own pending mark. But a line the per-lap cap drops, or one
    /// a flag clears out of the batch, never runs at all - so without a
    /// limit that pair would stay pending, and uncalled, for the rest of the
    /// race. Well past the batch window, so a line still waiting in its
    /// batch isn't offered a second time.
    /// </summary>
    private static readonly TimeSpan BattleOfferExpiresAfter = TimeSpan.FromSeconds(3);

    /// <summary>
    /// One close-battle line on offer: the pair's key in _battlesAnnounced,
    /// when it was offered (its mark in _battlesPending), and the
    /// _battlesGeneration it belongs to.
    /// </summary>
    private readonly record struct BattleOffer(string Key, DateTime OfferedAt, int Generation);

    /// <summary>
    /// Keeps a pair the pacing dial held from being offered again until what
    /// is LEFT of the gap has run - see _battlesCooling. The gap is measured
    /// from the last call the dial let through, as the dial measures it, so a
    /// battle held a second before the gap closes waits that second (well,
    /// the offer window), not a whole gap more.
    /// </summary>
    private void CoolBattle(BattleOffer offer)
    {
        double since;

        lock (_pacingLock)
        {
            since = _lastRoutineAcceptedUtc == DateTime.MinValue
                ? 0
                : (DateTime.UtcNow - _lastRoutineAcceptedUtc).TotalSeconds;
        }

        var until = DateTime.UtcNow + BattleCooldown(AnnouncerDensity.GapSeconds(Stature, Density), since);

        lock (_battlesLock)
        {
            if (offer.Generation == _battlesGeneration)
            {
                _battlesCooling[offer.Key] = until;
            }
        }
    }

    /// <summary>
    /// How long a held battle waits before it is offered again: what is left
    /// of the pacing gap since the last call let through, never under the
    /// offer window and never over a minute.
    /// </summary>
    internal static TimeSpan BattleCooldown(double gapSeconds, double sinceLastCallSeconds = 0)
    {
        var left = gapSeconds - (double.IsFinite(sinceLastCallSeconds) ? Math.Max(0, sinceLastCallSeconds) : 0);

        return double.IsFinite(left) && left > BattleOfferExpiresAfter.TotalSeconds
            ? TimeSpan.FromSeconds(Math.Min(left, 60))
            : BattleOfferExpiresAfter;
    }

    /// <summary>
    /// Takes this offer's pending mark away, if it is still this offer's.
    /// A newer offer of the same pair - after this one expired - keeps its
    /// own. Call only while holding _battlesLock.
    /// </summary>
    private void ForgetBattleOfferLocked(BattleOffer offer)
    {
        if (offer.Generation == _battlesGeneration
            && _battlesPending.TryGetValue(offer.Key, out var offeredAt)
            && offeredAt == offer.OfferedAt)
        {
            _battlesPending.Remove(offer.Key);
        }
    }

    /// <summary>
    /// Announces two cars on the same lap running within a second of each
    /// other - once per pair per race, as the event's own description says.
    ///
    /// Same gap measurement as everything else: the pair's positions at the
    /// line, so a car that simply hasn't crossed yet doesn't read as a
    /// second's worth of scrap.
    /// </summary>
    private void CheckCloseBattles(List<RaceStanding> ordered)
    {
        const long closeMs = 1000;

        // Not offered at all while it couldn't be heard - switched off, or
        // held by a flag. This runs on every standings update, so under a
        // yellow every close pair used to be offered, and logged as "held -
        // a flag is out", on every single crossing. Nothing is marked, so
        // the pair is still there to call once it's green.
        var config = _voiceStore.Current.Events.GetValueOrDefault(nameof(AnnouncementEventType.CloseBattle));

        // Two rivals from the Rivalries page within a second (11 Nov 2026,
        // phase 4 #4) - called as rivals, in place of the plain battle line.
        // Its own row, so it can be on with Close battle off.
        var rivalsConfig = _voiceStore.Current.Events.GetValueOrDefault(nameof(AnnouncementEventType.RivalsSideBySide));
        var battlesOn = VoiceCandidateCanSpeak(AnnouncementEventType.CloseBattle, config);
        var rivalsOn = VoiceCandidateCanSpeak(AnnouncementEventType.RivalsSideBySide, rivalsConfig);

        if (Muted || (!battlesOn && !rivalsOn))
        {
            return;
        }

        // Nor once the race is over: EnqueueVoiceWork would only drop it,
        // and the pair would be offered and dropped again every few seconds
        // until the next race.
        if (_postRaceQuiet && !IsAllowedAfterRaceOver(AnnouncementEventType.CloseBattle))
        {
            return;
        }

        // Nor while the announcer is mid-line. EnqueueVoiceWork drops a
        // routine line that arrives then, so the offer only came back as
        // "Dropped CloseBattle" - and, not being used up, it was offered and
        // dropped again on the next crossing, and the next. The same test
        // EnqueueVoiceWork makes; the pair is simply offered on a later
        // update, once the announcer is free.
        if (_isVoiceSpeaking
            && !IsPriorityAnnouncement(AnnouncementEventType.CloseBattle)
            && !IsWorthWaitingFor(AnnouncementEventType.CloseBattle))
        {
            return;
        }

        for (var i = 0; i < ordered.Count - 1; i++)
        {
            var front = ordered[i];
            var chaser = ordered[i + 1];

            // A muted ghost's batch drops its lines before they run, so the
            // pair would never be settled and would be offered again and
            // again.
            if (MutedGhost(front.Transponder))
            {
                continue;
            }

            // Not on lap one: everyone is within a second of everyone at
            // the start, which is a start, not a battle.
            if (chaser.Laps < 2)
            {
                continue;
            }

            if (GapBetween(chaser, front) is not { LapsDown: 0, GapMs: { } gapMs } || gapMs is <= 0 or >= closeMs)
            {
                continue;
            }

            var rivals = rivalsOn && AreRivals(front.Transponder, chaser.Transponder);

            // A heat classes share (bug hunt 20): two cars of different
            // classes are not fighting for a place, so no battle line - two
            // rivals side by side are still called, for no place.
            var acrossClasses = InDifferentClasses(front.Transponder, chaser.Transponder);

            if (!rivals && (!battlesOn || acrossClasses))
            {
                continue;
            }

            // Only CHECKED here; the pair is recorded as called by
            // FirePairLine once the line is actually accepted to be spoken.
            // It used to be recorded first, and the line could then say
            // nothing at all - held by a yellow, out-weighed in the per-lap
            // cap by a fast lap or a lead change, or dropped for the
            // announcer still speaking. Being once per pair per race, that
            // pair was then never called again: a field bunched up under a
            // caution used up every battle before the green.
            //
            // A pair already on offer is skipped too (see _battlesPending),
            // or every standings update before its batch settles added
            // another copy to the front car's batch.
            var key = $"{front.Transponder}|{chaser.Transponder}";
            var now = DateTime.UtcNow;
            BattleOffer offer;

            lock (_battlesLock)
            {
                if (_battlesAnnounced.Contains(key)
                    || (_battlesPending.TryGetValue(key, out var offeredAt) && now - offeredAt < BattleOfferExpiresAfter))
                {
                    continue;
                }

                // Held by the pacing dial a moment ago - waiting out the gap
                // (see _battlesCooling) rather than being held again.
                if (_battlesCooling.TryGetValue(key, out var coolUntil))
                {
                    if (now < coolUntil)
                    {
                        continue;
                    }

                    _battlesCooling.Remove(key);
                }

                _battlesPending[key] = now;
                offer = new BattleOffer(key, now, _battlesGeneration);
            }

            // What the fight is for - the broadcast's first question about
            // any battle. The front car's place among the cars being called.
            // In a heat classes share, the front car's place in its class
            // (bug hunt 20), as the position report says it.
            var inClass = ClassPlaceOf(ordered, front.Transponder);
            var battleFor = acrossClasses ? null
                : inClass is { } cls ? $"{(cls.Place <= 1 ? "the lead" : Ordinal(cls.Place))} in {cls.Class}"
                : i == 0 ? "the lead" : Ordinal(i + 1);

            // The chaser is named first: the one coming at their rival.
            if (!(rivals
                    ? FirePairLine(AnnouncementEventType.RivalsSideBySide, chaser.Transponder, front.Transponder, offer, battleFor,
                        about: new BattlePair(front.Transponder, chaser.Transponder, i + 1))
                    : FirePairLine(AnnouncementEventType.CloseBattle, front.Transponder, chaser.Transponder, offer, battleFor,
                        about: new BattlePair(front.Transponder, chaser.Transponder, i + 1))))
            {
                // Never reached the batch, so nothing would ever settle it.
                lock (_battlesLock)
                {
                    ForgetBattleOfferLocked(offer);
                }
            }
        }
    }

    /// <summary>
    /// "Battle for the lead!" in front of a battle line - after its opening
    /// delivery tag, when it has one ("[shouting] ..."), so the tag still
    /// opens the line and the voice says the words the way it was told to.
    /// </summary>
    /// <param name="lead">What the lead is: "the lead", or "the lead in Stock" in a heat classes share.</param>
    internal static string BattleForTheLead(string line, string lead = "the lead")
    {
        var match = System.Text.RegularExpressions.Regex.Match(line, @"^\s*(\[[^\]]+\]\s*)?");
        var tag = match.Groups[1].Value;

        return $"{tag}Battle for {lead}! {line[match.Length..]}".Trim();
    }

    /// <summary>
    /// Speaks a line about two drivers - {driver} and {rival} - for events
    /// the template system's single-driver substitution can't express.
    /// Shares the ghost-pass wording rules: announcer names, and the pass
    /// is dropped silently when the event is off or has no lines.
    ///
    /// Returns whether the line was handed to the per-lap batch. False means
    /// it never will be spoken, so a caller holding something for it can
    /// let go - see CheckCloseBattles.
    /// </summary>
    /// <param name="battle">
    /// For a close battle, the pair on offer. The pair is added to
    /// _battlesAnnounced only when the line is accepted to be spoken, so a
    /// line that never got heard doesn't use up the pair - see
    /// CheckCloseBattles. Null for a line that isn't once per pair.
    /// </param>
    /// <param name="battleFor">
    /// For a battle: what it is for - "the lead", "third" - filling
    /// {battle} as "for the lead". A fight for the lead the chosen line does
    /// not mention gets "Battle for the lead!" in front of it, because that
    /// is the one battle a broadcast always names.
    /// </param>
    /// <param name="about">For a close battle, the two cars as the line has them - see AnnouncementService.BattleLines.</param>
    private bool FirePairLine(AnnouncementEventType type, string driverTransponder, string rivalTransponder, BattleOffer? battle = null, string? battleFor = null, BattlePair? about = null)
    {
        if (Muted)
        {
            return false;
        }

        var config = _voiceStore.Current.Events.GetValueOrDefault(type.ToString());

        if (config is null || !CallOn(config) || config.EffectiveTemplates.Count == 0)
        {
            return false;
        }

        if (HeldByCaution(type, config))
        {
            StatusMessage?.Invoke($"[voice] {type} held - a flag is out.");
            return false;
        }

        // The dial (7 Nov 2026, bug hunt 13). This line goes straight to
        // the per-lap batch and EnqueueVoiceWork, never through
        // EnqueueVoice, so the floor there never saw it: a lapping was
        // still called at Sparing, which says it holds lappings, while the
        // Commentary page listed it as held. A close battle has asked the
        // dial already (VoiceCandidateCanSpeak); asking again here is what
        // catches the lapping.
        if (!DialLetsItSpeak(type, config))
        {
            StatusMessage?.Invoke(
                $"[voice] {type} held - the announcer is set to {ShowChatter.For(Chatter).Name}.");
            return false;
        }

        var template = TemplateBag.Pick(config.EffectiveTemplates);

        var text = template
            .Replace("{driver}", DisplayNameFor(driverTransponder), StringComparison.OrdinalIgnoreCase)
            .Replace("{rival}", DisplayNameFor(rivalTransponder), StringComparison.OrdinalIgnoreCase)
            .Replace("{car}", AnnouncementText.OrMissing(_profiles.CarModelFor(driverTransponder)), StringComparison.OrdinalIgnoreCase)
            .Replace("{battle}", AnnouncementText.OrMissing(string.IsNullOrWhiteSpace(battleFor) ? null : $"for {battleFor}"), StringComparison.OrdinalIgnoreCase)
            .WithDriverDetails(_profiles.Get(driverTransponder))
            .TidyTemplate();

        if (text.Length == 0)
        {
            return false;
        }

        if (battleFor is not null && battleFor.StartsWith("the lead", StringComparison.Ordinal)
            && !template.Contains("{battle}", StringComparison.OrdinalIgnoreCase))
        {
            text = BattleForTheLead(text, battleFor);
        }

        var speed = config.SpeedFor(_voiceStore.Current.Speed);

        // Through the per-lap cap like the other reactions to a crossing, so
        // a lapping doesn't stack on top of a fast lap and a leader change.
        //
        // The text goes up only once the line wins its slot and is accepted
        // (see EnqueueVoiceWork), not when it is offered - or a lapping the
        // cap dropped was still shown on the Big Screen and in the feed.
        ConsiderPerLapAction("voice", driverTransponder, config.Weight, MaxVoicePerLap, () =>
        {
            // This runs on a thread-pool thread, and the batches for
            // different cars can run at the same time - so the pair is
            // checked and claimed as one step under _battlesLock. Claimed
            // BEFORE it is queued, so two copies can't both be said; the
            // claim is handed back below if the line is dropped.
            if (battle is { } offer)
            {
                lock (_battlesLock)
                {
                    // Offered in a race that has since been reset - its
                    // batch was already past its delay when the reset came.
                    // Saying it, or marking the pair, would carry the old
                    // race into the new one.
                    if (offer.Generation != _battlesGeneration)
                    {
                        return;
                    }

                    // Settled now, whatever happens next.
                    ForgetBattleOfferLocked(offer);

                    // Already called - by another copy of the same pair.
                    if (!_battlesAnnounced.Add(offer.Key))
                    {
                        return;
                    }
                }
            }

            // A battle a sponsor presents carries their name - decided here,
            // as the line is queued, so the ration is the queue's to spend.
            var (sponsorTag, countMention, releaseMention) = SponsorTag(type);
            var said = (text + sponsorTag).Trim();

            // Never said, so it doesn't use the pair up - unless a reset
            // has already wiped the slate, when there is nothing to undo.
            //
            // WHY (20 Oct 2026, Bug Hunt 8): pulled out into one place so it
            // can also run when a call KEPT for the end of the current line
            // is lost later (pushed out of the held slot, binned by a flag,
            // or turned down when offered again) - EnqueueVoiceWork returns
            // true for a kept call, so before, a battle nobody heard still
            // used the pair up for the rest of the race. Running it twice,
            // or after the pair was re-claimed by a later race, is harmless:
            // the generation check stops a stale give-back.
            void GiveBack()
            {
                // The sponsor's ration too (20 Oct 2026, Bug Hunt 8): only
                // this line's own reservation, so running it twice, or after
                // a later line took the ration, undoes nothing.
                releaseMention?.Invoke();

                if (battle is not { } dropped)
                {
                    return;
                }

                lock (_battlesLock)
                {
                    if (dropped.Generation == _battlesGeneration)
                    {
                        _battlesAnnounced.Remove(dropped.Key);
                    }
                }
            }

            var paced = false;

            // A close battle carries its two cars, so it is cut off and the
            // pass called if they swap before it is said (3 Nov 2026) - see
            // AnnouncementService.BattleLines.
            using var tagged = AboutBattle(about);

            var accepted = EnqueueVoiceWork(type, driverTransponder,
                async () =>
                {
                    // WHY (23 Oct 2026, bug hunt 10): counted only once the line is
                    // certain to be heard (SynthesizeAndPlayAsync's spoken); a line
                    // dropped for no voice, a flag, the countdown or a mute - or
                    // whose synthesis threw - gives the reservation back - and the pair - instead (GiveBack).
                    var heard = false;

                    try
                    {
                        await SynthesizeAndPlayAsync(said, $"[voice] {type}", speedOverride: speed,
                            spoken: () =>
                            {
                                heard = true;
                                countMention?.Invoke();
                            });
                    }
                    finally
                    {
                        if (!heard)
                        {
                            GiveBack();
                        }
                    }
                },
                displayText: said,
                lost: GiveBack,
                paced: () => paced = true,
                renderAhead: () => RenderLineAheadAsync(said, type, speed));

            if (!accepted)
            {
                GiveBack();

                // Held by the pacing dial rather than dropped: free again,
                // but not offered until the gap has run (22 Oct 2026) - see
                // _battlesCooling.
                if (paced && battle is { } held)
                {
                    CoolBattle(held);
                }
            }
        });

        return true;
    }

    /// <summary>
    /// For each enabled rivalry, resolves both named drivers to whichever
    /// transponder they're currently assigned to and checks whether their
    /// relative order in the standings just flipped. Silently does nothing
    /// for a rivalry where either driver isn't currently on track (no
    /// profile pointing a transponder at that name) or hasn't posted a lap
    /// yet this session - there's no meaningful "ahead/behind" to compare.
    /// </summary>
    /// <param name="call">
    /// False for a correction by hand (8 Nov 2026): who is ahead is
    /// remembered, so the next crossing does not call a swap nobody made,
    /// but no line is said - see KeepInStepWithCorrection.
    /// </param>
    private void CheckRivalries(List<RaceStanding> ordered, bool call = true)
    {
        if (_rivalries is null || _rivalries.All.Count == 0)
        {
            return;
        }

        // The club's own wording is never sent through the HeatCaller voice
        // (17 Oct 2026, Bug Hunt 8, A11-2): the relay renders on HeatCaller's
        // account, and the Rivalries page's lock only stopped EDITING it.
        // The standard lines are said instead; the club's are kept, not
        // overwritten, for when the voice changes back.
        var standardOnly = _voiceStore.Current.EffectiveEngine == VoiceEngine.HeatCaller;
        List<string>? standard = standardOnly ? new RivalryLine().Templates : null;

        foreach (var rivalry in _rivalries.All)
        {
            var templates = standard ?? rivalry.Templates;

            if (!rivalry.Enabled || templates is null || templates.Count == 0)
            {
                continue;
            }

            // WHY (20 Oct 2026, Bug Hunt 8): the drivers were looked up in
            // _profiles.All, which leaves ghost cars out - but the Rivalries
            // page offers ghosts (it lists AllRaceable), so a rivalry with a
            // ghost was saved and then never said a word. AllRaceable puts
            // real drivers first, so a real driver still wins a shared name;
            // a muted ghost is already gone from 'ordered'.
            var transponderA = _profiles.AllRaceable.FirstOrDefault(p => string.Equals(p.DriverName, rivalry.DriverA, StringComparison.OrdinalIgnoreCase))?.Transponder;
            var transponderB = _profiles.AllRaceable.FirstOrDefault(p => string.Equals(p.DriverName, rivalry.DriverB, StringComparison.OrdinalIgnoreCase))?.Transponder;

            if (transponderA is null || transponderB is null)
            {
                continue; // one or both names aren't currently assigned to any transponder
            }

            var indexA = ordered.FindIndex(s => s.Transponder == transponderA);
            var indexB = ordered.FindIndex(s => s.Transponder == transponderB);

            if (indexA < 0 || indexB < 0)
            {
                continue; // one or both haven't posted a lap yet this session
            }

            var currentlyAhead = indexA < indexB ? transponderA : transponderB;

            if (call && _rivalryLastAhead.TryGetValue(rivalry.Id, out var previouslyAhead) && previouslyAhead != currentlyAhead)
            {
                var overtakerName = currentlyAhead == transponderA ? rivalry.DriverA : rivalry.DriverB;
                var overtakenName = currentlyAhead == transponderA ? rivalry.DriverB : rivalry.DriverA;
                var overtaken = currentlyAhead == transponderA ? transponderB : transponderA;
                FireRivalryLine(overtakerName, overtakenName, currentlyAhead, templates,
                    BattlePair.Passed(currentlyAhead, overtaken, Math.Min(indexA, indexB) + 1));
            }

            _rivalryLastAhead[rivalry.Id] = currentlyAhead;
        }
    }

    /// <summary>
    /// Fires a custom rivalry line. Content is configured per driver-pair
    /// on the Rivalries tab rather than through the generic per-event Voice
    /// Announcements settings, since it's specific to which two named
    /// drivers swapped position - so this bypasses EnqueueVoice's
    /// AnnouncementEventType-keyed template lookup and builds the text
    /// directly, but still shares the same synthesis/playback plumbing.
    /// </summary>
    /// <param name="passed">The swap, so the line is cut off if it is undone before it is said - see AnnouncementService.BattleLines.</param>
    private void FireRivalryLine(string overtakerName, string overtakenName, string overtakerTransponder, List<string> templates, BattlePair? passed = null)
    {
        if (Muted)
        {
            return;
        }

        // Random.Next(0) returns 0, so an empty list would index [0] on
        // nothing and throw - mid-race, from a rivalry whose lines were all
        // deleted. Nothing stops that being saved, so guard here.
        if (templates.Count == 0)
        {
            return;
        }

        // Rivalries follow the same rule as everything else: not over a flag.
        if (IsUnderCaution)
        {
            return;
        }

        // WHY (23 Oct 2026, bug hunt 10): this line goes straight to
        // EnqueueVoiceWork, so it skipped the club's chattiness dial - a
        // quiet announcer still called every rivalry swap, even for 8th.
        // A rivalry is a pass for position, so it is now weighed like a
        // close battle and asked of the dial first, the way a ghost pass is
        // (and, like that one, before a line is drawn from the bag, so a
        // held swap doesn't use one up). The pacing gap still sees it as
        // the LeaderChange it borrows below with this weight, not a lead
        // change's (routineWeight).
        var rivalryWeight = WeightOf(AnnouncementEventType.CloseBattle);

        if (!ShowChatter.CallIsWorthIt(rivalryWeight, Chatter, alwaysSaid: false))
        {
            StatusMessage?.Invoke($"[voice] Rivalry line held - the announcer is set to {ShowChatter.For(Chatter).Name}.");
            return;
        }

        var chosenTemplate = templates.Count == 1 ? templates[0] : TemplateBag.Pick(templates);
        var text = chosenTemplate
            .Replace("{overtaker}", overtakerName)
            .Replace("{overtaken}", overtakenName)
            .Trim();

        if (text.Length == 0)
        {
            return;
        }

        // Reuses LeaderChange purely as the internal dedup/tracking key
        // EnqueueVoiceWork needs - rivalries don't have their own
        // AnnouncementEventType since they're not a generic per-event
        // setting, just an implementation detail invisible to the user.
        //
        // Shown only if accepted - see EnqueueVoiceWork.
        //
        // Tagged with the swap (3 Nov 2026), so it is cut off if the rival
        // is straight back past - and that swap's own rivalry line says so.
        using var tagged = AboutBattle(passed);

        EnqueueVoiceWork(AnnouncementEventType.LeaderChange, overtakerTransponder, () => SynthesizeAndPlayAsync(text, "[voice] Rivalry announcement",
            "[voice] Skipped rivalry announcement - ElevenLabs API key or voice not configured."),
            displayText: text,
            routineWeight: rivalryWeight,
            renderAhead: () => RenderLineAheadAsync(text, AnnouncementEventType.LeaderChange, speed: null));
    }

    private void HandleGatePassed(GatePassEvent gate)
    {
        _standings.TryGetValue(gate.Transponder, out var standing);

        if (!string.Equals(gate.GateType, "finish", StringComparison.OrdinalIgnoreCase))
        {
            return; // Sound effects and streaks are scoped to full-lap (finish gate) passes.
        }

        // A pole called from this driver's last crossing that no personal
        // best followed (a first flying lap): its lap is over, so the next
        // personal best of theirs is a real improvement again.
        if (string.Equals(_poleCalledFromGateFor, gate.Transponder, StringComparison.Ordinal))
        {
            _poleCalledFromGateFor = null;
        }

        // --- Per-lap "good lap" / "invalid lap" sound feedback ---
        var sounds = _soundStore.Current;
        var maxSoundsPerLap = Math.Max(1, sounds.MaxEffectsPerLap);
        var maxVoicePerLap = MaxVoicePerLap;
        var transponder = gate.Transponder;

        // Every candidate below is offered only when it could be heard, so a
        // switched-off one can't take a slot from one that is on - see
        // VoiceCandidateCanSpeak.
        if (gate.Valid)
        {
            var cfg = sounds.Effects.GetValueOrDefault(nameof(SoundEffectKind.GoodLap));

            // A driver with a lap beep of their own hears THAT as their lap
            // (11 Nov 2026, voice speed plan, phase 4): the shared chime on
            // top of it, 0.12 s later, was a second sound on every crossing -
            // the one the Lap Beeps page itself calls no use to a driver,
            // since it says a lap happened but not whose. The invalid-lap
            // buzzer and the stings for a best, a fast lap or a streak still
            // play: those say something the beep does not.
            if (LapBeeps?.ToneFor(transponder) is not null)
            {
                cfg = null;
            }

            if (SoundCandidateCanPlay(SoundEffectKind.GoodLap, cfg))
            {
                ConsiderPerLapAction("sound", transponder, cfg.Weight, maxSoundsPerLap,
                    () => PlaySound(SoundEffectKind.GoodLap, ToneGenerator.PlayGoodLapAsync, "good lap chime"));
            }
        }
        else
        {
            var cfg = sounds.Effects.GetValueOrDefault(nameof(SoundEffectKind.InvalidLap));
            if (SoundCandidateCanPlay(SoundEffectKind.InvalidLap, cfg))
            {
                ConsiderPerLapAction("sound", transponder, cfg.Weight, maxSoundsPerLap,
                    () => PlaySound(SoundEffectKind.InvalidLap, ToneGenerator.PlayInvalidLapAsync, "invalid lap buzzer"));
            }
        }

        // --- Consistency streak tracking ---
        // If the caller supplies an authoritative count (see the doc
        // comment on GatePassEvent.StreakCountOverride), use it directly.
        // Otherwise fall back to deriving it ourselves from the bare
        // Streak flag - this is the Neon Timing API's own path, since NLT
        // tells us per-pass whether it's continuing a streak but not how
        // long that streak actually is.
        int streakCount;
        if (gate.StreakCountOverride is { } overrideCount)
        {
            streakCount = overrideCount;
        }
        else
        {
            streakCount = gate.Valid && gate.Streak
                ? _streakCounts.TryGetValue(transponder, out var prev) ? prev + 1 : 1
                : 0;
            _streakCounts[transponder] = streakCount;
        }

        // Tracked for everyone, announced or not: "the longest streak in the
        // field" is only answerable if every driver's current run is known.
        lock (_streakLock)
        {
            _currentStreaks[transponder] = (streakCount, DateTime.UtcNow);
        }

        // Keeps every "a few laps" window below in step with the track.
        if (gate.Valid && gate.LapTimeMs is { } observedLapMs)
        {
            ObserveLapPace(observedLapMs);
        }

        var (chaser, chaserCount) = BestStreakOtherThan(transponder);

        if (streakCount >= sounds.StreakThresholdFor(IsPracticeMode))
        {
            var streakSoundCfg = sounds.Effects.GetValueOrDefault(nameof(SoundEffectKind.Streak));
            if (SoundCandidateCanPlay(SoundEffectKind.Streak, streakSoundCfg))
            {
                ConsiderPerLapAction("sound", transponder, streakSoundCfg.Weight, maxSoundsPerLap,
                    () => PlaySound(SoundEffectKind.Streak, (v, ct) => ToneGenerator.PlayStreakAsync(streakCount, v, ct), $"streak sound (streak of {streakCount})", streakCount: streakCount));
            }

            var streakVoiceCfg = _voiceStore.Current.Events.GetValueOrDefault(nameof(AnnouncementEventType.Streak));
            var everyN = streakVoiceCfg?.StreakAnnounceEveryNFor(IsPracticeMode) ?? 1;

            // Due on the lap the run reaches the threshold, then every N
            // laps after: threshold 3, every 3 -> called at 3, 6, 9.
            //
            // Worked out from the run's own length rather than a counter of
            // laps since the last call. The counter started at one on the
            // threshold lap, so "every 3" first spoke at 5, then 8 - never
            // on the numbers the settings describe - and it was reset even
            // when the line was then dropped for the announcer being busy,
            // pushing the next call a further N laps away.
            var due = (streakCount - sounds.StreakThresholdFor(IsPracticeMode)) % Math.Max(1, everyN) == 0;

            // With a full grid every driver over the threshold gets called
            // every lap, which is most of what the announcer says. This
            // narrows it to whoever is actually leading the clean runs.
            var leadsTheField = streakCount >= chaserCount;
            var suppressed = streakVoiceCfg?.OnlyLongestStreak == true && !leadsTheField;

            // The same checks every other per-lap line makes before it is
            // offered (see VoiceCandidateCanSpeak). This one goes straight
            // to EnqueueVoiceWork rather than through EnqueueVoice, so it
            // skipped them entirely: switching Streak off - or off for
            // practice - didn't stop it, and a streak reached under a yellow
            // was called over the caution while its sound was rightly held.
            // A Streak entry with no lines at all would also have thrown when
            // the line came to pick one.
            if (!VoiceCandidateCanSpeak(AnnouncementEventType.Streak, streakVoiceCfg)
                || streakVoiceCfg.EffectiveTemplates.Count == 0)
            {
                if (streakVoiceCfg is not null && due)
                {
                    var reason = !CallOn(streakVoiceCfg)
                        ? $"switched off for {(IsPracticeMode ? "practice" : "races")}"
                        : streakVoiceCfg.EffectiveTemplates.Count == 0
                            ? "no lines configured"
                            // VoiceCandidateCanSpeak also holds a call the
                            // chattiness dial rules out, and that used to
                            // fall through to here too, so every streak a
                            // quiet dial held was logged as held by a flag
                            // that was not out. Name the dial, the way the
                            // other dial-held lines do (10 Nov 2026, bug
                            // hunt 16).
                            : HeldByCaution(AnnouncementEventType.Streak, streakVoiceCfg)
                                ? "a flag is out"
                                : $"the announcer is set to {ShowChatter.For(Chatter).Name}";

                    StatusMessage?.Invoke($"[voice] Streak for {DisplayNameFor(transponder)} not announced - {reason}.");
                }
            }
            else if (suppressed)
            {
                StatusMessage?.Invoke($"[voice] Streak for {DisplayNameFor(transponder)} not announced - {DisplayNameFor(chaser ?? string.Empty)} is on a longer run ({chaserCount}).");
            }
            else if (due)
            {
                // The number, the runner-up and the only-longest check are
                // all resolved when the line is actually spoken - see
                // EnqueueStreakAnnouncement.
                var lapNumber = standing?.Laps ?? 0;

                ConsiderPerLapAction("voice", transponder, streakVoiceCfg.Weight, maxVoicePerLap,
                    () => EnqueueStreakAnnouncement(transponder, streakVoiceCfg, lapNumber));
            }
            else
            {
                StatusMessage?.Invoke($"[voice] Streak for {DisplayNameFor(transponder)} not announced - {streakCount} in a row, called every {everyN} from {sounds.StreakThresholdFor(IsPracticeMode)}.");
            }
        }

        CheckStreakLead(transponder, streakCount, chaser, chaserCount);

        // --- Lap-completed voice announcement (existing feature) ---
        // A rejected pass isn't a completed lap - nothing below is said for
        // one. It used to be: "lap completed", and a lap time worked out
        // from an elapsed time that hadn't changed, which came back as the
        // driver's entire race time.
        //
        // The lap time comes from the source when it supplies one (the
        // direct decoder path does, and leaves it null for the start lap).
        // Only Next Level Timing, which doesn't, still derives it here.
        long? lapTimeMs = null;
        if (gate.Valid)
        {
            lapTimeMs = gate.LapTimeMs
                        ?? (gate.StreakCountOverride is null ? ComputeLapTimeMs(transponder, standing) : null);
        }

        var lapVoiceCfg = _voiceStore.Current.Events.GetValueOrDefault(nameof(AnnouncementEventType.LapCompleted));
        var restrictedToBestLap = lapVoiceCfg?.OnlyBestLap == true;
        if (gate.Valid && (!restrictedToBestLap || gate.Fast)
            && VoiceCandidateCanSpeak(AnnouncementEventType.LapCompleted, lapVoiceCfg))
        {
            ConsiderPerLapAction("voice", transponder, lapVoiceCfg.Weight, maxVoicePerLap,
                () => EnqueueVoice(AnnouncementEventType.LapCompleted, standing, gate, lapTimeMs));
        }

        // --- Lap-time-only announcement (independent of the above) ---
        // A deliberately minimal alternative: just the number, nothing
        // else, every lap. Meant to be used INSTEAD of "Every lap
        // completed" on fast tracks, not alongside it - toggle whichever
        // one fits, or neither.
        // The driver's own cue first - it's the one they're listening for,
        // and it should land as close to their crossing as possible rather
        // than behind a queue of shared chatter. Deliberately not queued for
        // the same reason: a cue that arrives late is worse than none.
        // Nor for a lap the window was told of too late (23 Oct 2026, timing
        // layer, P4b): a cue that arrives late is worse than none, as above.
        // Nor for a rejected pass (13 Nov 2026, bug hunt 17): the cue tells a
        // driver their lap counted, and a double read at the loop did not.
        if (gate.Valid && !Muted && LapBeeps?.ToneFor(transponder) is { } toneIndex && !SkippedAsStale())
        {
            var beepVolume = (float)_soundStore.Current.Volume;
            _ = ToneGenerator.PlayDriverLapAsync(toneIndex, beepVolume);
        }

        var lapTimeCfg = _voiceStore.Current.Events.GetValueOrDefault(nameof(AnnouncementEventType.LapTime));

        // No time, no call - the template would otherwise be spoken with
        // the time blanked out.
        if (lapTimeMs is > 0 && ShouldAnnounceLapTime(transponder, lapTimeCfg)
            && VoiceCandidateCanSpeak(AnnouncementEventType.LapTime, lapTimeCfg))
        {
            ConsiderPerLapAction("voice", transponder, lapTimeCfg.Weight, maxVoicePerLap,
                () => EnqueueVoice(AnnouncementEventType.LapTime, standing, gate, lapTimeMs));
        }

        if (gate.Fast)
        {
            // Whatever happens to this lap's own call, one still waiting
            // about the lap it beat is no longer true - see BinWaitingFastLaps.
            BinWaitingFastLaps();

            // And neither is an AI line written before it - "RIPS it with a
            // five forty-two" heard a second after a 4.936 (8 Nov 2026). See
            // AnnouncementService.Freshness.
            NoteRaceFastestLap();

            var fastLapSoundCfg = sounds.Effects.GetValueOrDefault(nameof(SoundEffectKind.FastLap));
            if (SoundCandidateCanPlay(SoundEffectKind.FastLap, fastLapSoundCfg))
            {
                ConsiderPerLapAction("sound", transponder, fastLapSoundCfg.Weight, maxSoundsPerLap,
                    () => PlaySound(SoundEffectKind.FastLap, ToneGenerator.PlayFastLapAsync, "fast lap chime"));
            }

            var fastLapVoiceCfg = _voiceStore.Current.Events.GetValueOrDefault(nameof(AnnouncementEventType.FastLap));
            if (VoiceCandidateCanSpeak(AnnouncementEventType.FastLap, fastLapVoiceCfg))
            {
                ConsiderPerLapAction("voice", transponder, fastLapVoiceCfg.Weight, maxVoicePerLap,
                    () => EnqueueVoice(AnnouncementEventType.FastLap, standing, gate));
            }
        }

        // THE RECORD IS NOT CHECKED HERE, and the note is worth keeping
        // because a hand that goes looking for it will find a gap that is
        // not one (15 Oct 2026).
        //
        // Both record calls already have a caller, and a good one:
        // RaceControlControl.CheckAndAnnounceTrackRecord fires
        // NotifyTrackRecord and persists the new mark, and the method
        // above it fires NotifyRecordPace once per driver per race from
        // their third counted lap. Between them they refuse a demo, a
        // practice session, a ghost, a rental, a driver flagged out of
        // records, and - through IsPlausibleRecord, against the operator's
        // own minimum lap time - a lap too quick to be believed.
        //
        // A second caller here announced every record TWICE for one lap.
        // That was shipped on 15 Oct and taken out the same day. It was
        // added on the strength of a search that found no caller, run
        // against a copy of the repo that was missing
        // RaceControlControl.RaceEvents.cs - which is where the caller
        // lives.

        // The flying laps a best-consecutive qualifier is scored on. Only the
        // session's own lap times: the start lap has none, and the qualifying
        // calculator leaves it out too (BestFlyingWindow).
        if (gate.Valid && gate.LapTimeMs is > 0)
        {
            if (!_qualifyingLaps.TryGetValue(transponder, out var flyingLaps))
            {
                flyingLaps = new List<long>();
                _qualifyingLaps[transponder] = flyingLaps;
            }

            flyingLaps.Add(gate.LapTimeMs.Value);
        }

        // Pole is checked on every crossing, not only on the race's fastest
        // lap. That was the only place it was looked at, so under the
        // default most-laps rule the driver who actually went top - by
        // completing a lap, quick or not - was never called. The first
        // flying lap is still covered: it is never reported as a personal
        // best, so this is the only report of it. Not while muted, the same
        // as the personal-best route, and only on a timed lap - the start
        // crossing puts nobody on pole. When a personal best for this same
        // lap follows, it finds the call already made (see
        // _poleCalledFromGateFor).
        if (IsQualifyingMode && !Muted && standing is not null && lapTimeMs is > 0
            && CallProvisionalPoleIfTaken(transponder, standing, lapTimeMs))
        {
            _poleCalledFromGateFor = transponder;
        }
    }
}
