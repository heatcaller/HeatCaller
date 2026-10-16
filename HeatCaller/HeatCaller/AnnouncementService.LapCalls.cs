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
    // in practice, so this only adds a small, consistent delay - much
    // smaller than the ~1s of audio device warm-up padding already baked
    // into every voice announcement regardless, and barely noticeable for
    // short chimes.
    private static readonly TimeSpan PerLapBatchWindow = TimeSpan.FromMilliseconds(300);

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
        && config.IsEnabledFor(IsPracticeMode)
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

        _ = FlushPerLapBatchAfterDelayAsync(key, maxPerLap, cts);
    }

    private async Task FlushPerLapBatchAfterDelayAsync(string key, int maxPerLap, CancellationTokenSource cts)
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
            OfferToTheField(winners);

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
    /// </summary>
    private void MaybeAnnouncePositions(List<RaceStanding> ordered, RaceStanding leader)
    {
        var config = _voiceStore.Current.Events.GetValueOrDefault(nameof(AnnouncementEventType.PositionReport));
        if (config is null || !config.IsEnabledFor(IsPracticeMode) || config.Templates.Count == 0)
        {
            return;
        }

        if (HeldByCaution(AnnouncementEventType.PositionReport, config))
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
        var named = ordered.Take(depth).ToList();
        if (named.Count < 2)
        {
            // A running order of one is not a running order.
            return;
        }

        _lastPositionReportLap = leader.Laps;

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
        string SpokenNameFor(RaceStanding s)
        {
            var profile = _profiles.Get(s.Transponder);
            return profile is not null && !string.IsNullOrWhiteSpace(profile.SpokenName)
                ? profile.SpokenName
                : s.DisplayName;
        }

        // The line is picked BEFORE the order is worded, because what the
        // club asked for changes how the list reads. Still one line per
        // report - only the order of these two steps moved.
        var template = config.Templates[TemplatePicker.Next(config.Templates.Count)];

        // The gaps are said only if the club switched them on AND left
        // somewhere for them to go. Reading the tick box alone would let a
        // club tick it, have no {gaps} in the line they wrote, and get a
        // report that said LESS than before - because the gap comes out of
        // the running order below when {gaps} is carrying it.
        var sayGaps = config.PositionReportSayGaps
            && template.Contains("{gaps}", StringComparison.OrdinalIgnoreCase);

        var parts = new List<string>();

        // Each car read out with how far back it is, leader first - what
        // {gaps} is worded from. See AnnouncementText.SpokenGaps, which
        // drops the leader and anything that could not be measured.
        var order = new List<(string Name, RaceGap Gap)>();

        for (var i = 0; i < named.Count; i++)
        {
            var name = SpokenNameFor(named[i]);

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
            var vsLeader = GapBetween(named[i], named[0]);

            // Down a lap, that IS the whole answer: the seconds between the
            // two cars' last crossings are an accident of where each
            // happens to be on track, and reading them out would tell a
            // spectator a lapped car was four seconds off the lead. On the
            // lead lap: the gap to the car directly ahead, measured when
            // each of them crossed the same lap.
            var gap = vsLeader.LapsDown > 0 ? vsLeader : GapBetween(named[i], named[i - 1]);

            order.Add((name, gap));

            if (sayGaps)
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

            var gapMs = gap.GapMs;

            if (gapMs is > 0 and < 1500)
            {
                parts.Add($"{name} right on his tail");
            }
            else if (gapMs is > 0)
            {
                // "a second and a half back", not "1.5 back" - the gap the
                // way it is called, in words, which also means a
                // comma-decimal PC can no longer turn it into "one, five".
                // See RaceNumbers.Behind.
                parts.Add($"{name} {RaceNumbers.Behind(gapMs.Value / 1000.0)}");
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
        var text = template
            .Replace("{positions}", positions, StringComparison.OrdinalIgnoreCase)
            .WithPositionGaps(gaps)
            .TidyTemplate();

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

        // The text goes up only if the report is accepted - see
        // EnqueueVoiceWork.
        EnqueueVoiceWork(AnnouncementEventType.PositionReport, leader.Transponder,
            () => SynthesizeAndPlayAsync(text, "[voice] Position report", speedOverride: reportSpeed),
            displayText: text);
    }

    /// <summary>Leader lap the last report was read on, so it fires once per interval rather than once per car.</summary>
    private int _lastPositionReportLap = -1;

    /// <summary>
    /// Race time at which each driver completed each lap, built from the
    /// standing updates as they arrive. Lets the position report measure a
    /// gap against the moment the car ahead crossed the SAME lap, rather
    /// than comparing lap counts at an instant when they're bound to differ.
    /// </summary>
    private readonly Dictionary<string, Dictionary<int, long>> _lapCrossings = new(StringComparer.OrdinalIgnoreCase);

    private void RecordCrossing(RaceStanding standing)
    {
        if (standing.Laps <= 0 || standing.ElapsedMs <= 0)
        {
            return;
        }

        if (!_lapCrossings.TryGetValue(standing.Transponder, out var laps))
        {
            laps = new Dictionary<int, long>();
            _lapCrossings[standing.Transponder] = laps;
        }

        laps[standing.Laps] = standing.ElapsedMs;
    }

    private RaceGap GapBetween(RaceStanding car, RaceStanding ahead)
    {
        _lapCrossings.TryGetValue(ahead.Transponder, out var aheadLaps);

        return RaceGaps.Compute(car.Laps, car.ElapsedMs, ahead.Laps, ahead.ElapsedMs,
            lap => aheadLaps is not null && aheadLaps.TryGetValue(lap, out var t) ? (long?)t : null);
    }

    private void HandleStandingUpdated(RaceStanding standing)
    {
        _standings[standing.Transponder] = standing;
        RecordCrossing(standing);

        // Same ranking rule as the leaderboard: most laps, then lowest elapsed.
        var ordered = _standings.Values
            .OrderByDescending(s => s.Laps)
            .ThenBy(s => s.ElapsedMs)
            .ToList();

        var leader = ordered.FirstOrDefault();
        if (leader is null)
        {
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

        // In a most-laps qualifier the lead IS provisional pole, called from
        // the same crossing (CallProvisionalPoleIfTaken). Saying both put two
        // lines in the queue for one pass, so the lead change keeps its chime
        // and leaves the words to the pole call while that is switched on.
        var poleSaysIt = IsQualifyingMode && _qualifyingMethod == QualifyingMethod.TotalLaps
            && _voiceStore.Current.Events.GetValueOrDefault(nameof(AnnouncementEventType.ProvisionalPole))?.IsEnabledFor(IsPracticeMode) == true;

        if (_currentLeaderTransponder is not null && leader.Transponder != _currentLeaderTransponder)
        {
            // Only offered when they could be heard - see VoiceCandidateCanSpeak.
            var soundCfg = _soundStore.Current.Effects.GetValueOrDefault(nameof(SoundEffectKind.LeaderChange));
            if (SoundCandidateCanPlay(SoundEffectKind.LeaderChange, soundCfg))
            {
                ConsiderPerLapAction("sound", leader.Transponder, soundCfg.Weight, Math.Max(1, _soundStore.Current.MaxEffectsPerLap),
                    () => PlaySound(SoundEffectKind.LeaderChange, ToneGenerator.PlayLeaderChangeAsync, "leader change sound"));
            }

            var voiceCfg = _voiceStore.Current.Events.GetValueOrDefault(nameof(AnnouncementEventType.LeaderChange));
            if (!poleSaysIt && VoiceCandidateCanSpeak(AnnouncementEventType.LeaderChange, voiceCfg))
            {
                ConsiderPerLapAction("voice", leader.Transponder, voiceCfg.Weight, MaxVoicePerLap,
                    () => EnqueueVoice(AnnouncementEventType.LeaderChange, leader, null));
            }
        }

        _currentLeaderTransponder = leader.Transponder;

        CheckLapped(ordered);
        CheckCloseBattles(ordered);
        CheckRivalries(ordered);
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
            if (live is null || !live.IsEnabledFor(IsPracticeMode))
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
            var template = config.EffectiveTemplates[TemplatePicker.Next(config.EffectiveTemplates.Count)];

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

        if (Muted || config is null || !config.IsEnabledFor(IsPracticeMode) || config.EffectiveTemplates.Count == 0
            || HeldByCaution(AnnouncementEventType.StreakLead, config))
        {
            return;
        }

        var template = config.EffectiveTemplates[TemplatePicker.Next(config.EffectiveTemplates.Count)];

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
                displayText: text));
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

        if (Muted || !VoiceCandidateCanSpeak(AnnouncementEventType.CloseBattle, config))
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

                _battlesPending[key] = now;
                offer = new BattleOffer(key, now, _battlesGeneration);
            }

            if (!FirePairLine(AnnouncementEventType.CloseBattle, front.Transponder, chaser.Transponder, offer))
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
    private bool FirePairLine(AnnouncementEventType type, string driverTransponder, string rivalTransponder, BattleOffer? battle = null)
    {
        if (Muted)
        {
            return false;
        }

        var config = _voiceStore.Current.Events.GetValueOrDefault(type.ToString());

        if (config is null || !config.IsEnabledFor(IsPracticeMode) || config.EffectiveTemplates.Count == 0)
        {
            return false;
        }

        if (HeldByCaution(type, config))
        {
            StatusMessage?.Invoke($"[voice] {type} held - a flag is out.");
            return false;
        }

        var template = config.EffectiveTemplates[TemplatePicker.Next(config.EffectiveTemplates.Count)];

        var text = template
            .Replace("{driver}", DisplayNameFor(driverTransponder), StringComparison.OrdinalIgnoreCase)
            .Replace("{rival}", DisplayNameFor(rivalTransponder), StringComparison.OrdinalIgnoreCase)
            .Replace("{car}", AnnouncementText.OrMissing(_profiles.CarModelFor(driverTransponder)), StringComparison.OrdinalIgnoreCase)
            .WithDriverDetails(_profiles.Get(driverTransponder))
            .TidyTemplate();

        if (text.Length == 0)
        {
            return false;
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

            var accepted = EnqueueVoiceWork(type, driverTransponder,
                () => SynthesizeAndPlayAsync(text, $"[voice] {type}", speedOverride: speed),
                displayText: text);

            if (!accepted && battle is { } dropped)
            {
                // Never said, so it doesn't use the pair up - unless a reset
                // has already wiped the slate, when there is nothing to undo.
                lock (_battlesLock)
                {
                    if (dropped.Generation == _battlesGeneration)
                    {
                        _battlesAnnounced.Remove(dropped.Key);
                    }
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
    private void CheckRivalries(List<RaceStanding> ordered)
    {
        if (_rivalries is null || _rivalries.All.Count == 0)
        {
            return;
        }

        foreach (var rivalry in _rivalries.All)
        {
            if (!rivalry.Enabled || rivalry.Templates.Count == 0)
            {
                continue;
            }

            var transponderA = _profiles.All.FirstOrDefault(p => string.Equals(p.DriverName, rivalry.DriverA, StringComparison.OrdinalIgnoreCase))?.Transponder;
            var transponderB = _profiles.All.FirstOrDefault(p => string.Equals(p.DriverName, rivalry.DriverB, StringComparison.OrdinalIgnoreCase))?.Transponder;

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

            if (_rivalryLastAhead.TryGetValue(rivalry.Id, out var previouslyAhead) && previouslyAhead != currentlyAhead)
            {
                var overtakerName = currentlyAhead == transponderA ? rivalry.DriverA : rivalry.DriverB;
                var overtakenName = currentlyAhead == transponderA ? rivalry.DriverB : rivalry.DriverA;
                FireRivalryLine(overtakerName, overtakenName, currentlyAhead, rivalry.Templates);
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
    private void FireRivalryLine(string overtakerName, string overtakenName, string overtakerTransponder, List<string> templates)
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

        var chosenTemplate = templates.Count == 1 ? templates[0] : templates[TemplatePicker.Next(templates.Count)];
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
        EnqueueVoiceWork(AnnouncementEventType.LeaderChange, overtakerTransponder, () => SynthesizeAndPlayAsync(text, "[voice] Rivalry announcement",
            "[voice] Skipped rivalry announcement - ElevenLabs API key or voice not configured."),
            displayText: text);
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
                    var reason = !streakVoiceCfg.IsEnabledFor(IsPracticeMode)
                        ? $"switched off for {(IsPracticeMode ? "practice" : "races")}"
                        : streakVoiceCfg.EffectiveTemplates.Count == 0
                            ? "no lines configured"
                            : "a flag is out";

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
        if (!Muted && LapBeeps?.ToneFor(transponder) is { } toneIndex)
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
