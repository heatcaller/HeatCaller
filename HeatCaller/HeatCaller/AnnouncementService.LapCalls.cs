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
    /// Queues a per-lap CANDIDATE action (a voice line or a sound effect)
    /// rather than performing it immediately. All candidates raised under
    /// the same batchNamespace ("voice" or "sound") for the same
    /// transponder within a short window are collected together; once the
    /// window closes, only the highest-Weight ones (up to maxPerLap) are
    /// actually performed, and the rest are silently dropped. See the
    /// class-level remarks on _pendingPerLapBatches for why.
    /// </summary>
    private void ConsiderPerLapAction(string batchNamespace, string? transponder, int weight, int maxPerLap, Action action)
    {
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

        foreach (var candidate in candidates.OrderByDescending(c => c.Weight).Take(Math.Max(1, maxPerLap)))
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
        _ => $"P{position}",
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

        var parts = new List<string>();

        for (var i = 0; i < named.Count; i++)
        {
            var name = SpokenNameFor(named[i]);

            if (i == 0)
            {
                parts.Add($"{name} leads");
                continue;
            }

            // Laps down is measured against the LEADER, at the line. This
            // report fires the instant the leader crosses, when everyone
            // behind still has one lap fewer on the counter - comparing lap
            // counts called nearly the whole field "a lap down", and said
            // "a lap" whatever the real difference was.
            var vsLeader = GapBetween(named[i], named[0]);

            if (vsLeader.LapsDown == 1)
            {
                parts.Add($"then {name}, a lap down");
                continue;
            }

            if (vsLeader.LapsDown > 1)
            {
                parts.Add($"then {name}, {vsLeader.LapsDown} laps down");
                continue;
            }

            // On the lead lap: the gap to the car directly ahead, measured
            // when each crossed the same lap.
            var gapMs = GapBetween(named[i], named[i - 1]).GapMs;

            if (gapMs is > 0 and < 1500)
            {
                parts.Add($"{name} right on his tail");
            }
            else if (gapMs is > 0)
            {
                // InvariantCulture: on a comma-decimal PC the gap was
                // written "1,5", which the voice reads as "one, five".
                var gapText = (gapMs.Value / 1000.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
                parts.Add($"{name} {gapText} back");
            }
            else
            {
                parts.Add($"{name} {Ordinal(i + 1)}");
            }
        }

        var positions = string.Join(", ", parts);

        var template = config.Templates[TemplatePicker.Next(config.Templates.Count)];
        var text = template.Replace("{positions}", positions, StringComparison.OrdinalIgnoreCase).Trim();

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
        AnnouncementTextReady?.Invoke(text);
        // Read at this event's own pace. A running order is the longest
        // line the announcer says, and the profile speed that suits a
        // two-word lap call rattles straight through it.
        var reportSpeed = config.SpeedFor(_voiceStore.Current.Speed);

        EnqueueVoiceWork(AnnouncementEventType.PositionReport, leader.Transponder,
            () => SynthesizeAndPlayAsync(text, "[voice] Position report", speedOverride: reportSpeed));
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

        if (_currentLeaderTransponder is not null && leader.Transponder != _currentLeaderTransponder)
        {
            var soundCfg = _soundStore.Current.Effects.GetValueOrDefault(nameof(SoundEffectKind.LeaderChange));
            ConsiderPerLapAction("sound", leader.Transponder, soundCfg?.Weight ?? 100, Math.Max(1, _soundStore.Current.MaxEffectsPerLap),
                () => PlaySound(SoundEffectKind.LeaderChange, ToneGenerator.PlayLeaderChangeAsync, "leader change sound"));

            var voiceCfg = _voiceStore.Current.Events.GetValueOrDefault(nameof(AnnouncementEventType.LeaderChange));
            ConsiderPerLapAction("voice", leader.Transponder, voiceCfg?.Weight ?? 100, Math.Max(1, _voiceStore.Current.MaxAnnouncementsPerLap),
                () => EnqueueVoice(AnnouncementEventType.LeaderChange, leader, null));
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
                if (string.Equals(other, transponder, StringComparison.OrdinalIgnoreCase)
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
                .Replace("{car}", AnnouncementText.OrMissing(_profiles.Get(transponder)?.CarModel), StringComparison.OrdinalIgnoreCase)
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

            AnnouncementTextReady?.Invoke(text);
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
            .Replace("{car}", AnnouncementText.OrMissing(_profiles.Get(transponder)?.CarModel), StringComparison.OrdinalIgnoreCase)
            .Replace("{streak}", streakCount.ToString(), StringComparison.OrdinalIgnoreCase)
            .Replace("{rival}", rival is null ? "the field" : DisplayNameFor(rival), StringComparison.OrdinalIgnoreCase)
            .Replace("{rivalstreak}", (rival is null ? 0 : Math.Max(chaserCount, 0)).ToString(), StringComparison.OrdinalIgnoreCase)
            .WithDriverDetails(_profiles.Get(transponder))
            .TidyTemplate();

        if (text.Length == 0)
        {
            return;
        }

        AnnouncementTextReady?.Invoke(text);

        var speed = config.SpeedFor(_voiceStore.Current.Speed);

        ConsiderPerLapAction("voice", transponder, config.Weight, Math.Max(1, _voiceStore.Current.MaxAnnouncementsPerLap),
            () => EnqueueVoiceWork(AnnouncementEventType.StreakLead, transponder,
                () => SynthesizeAndPlayAsync(text, "[voice] Longest streak", speedOverride: speed)));
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

        for (var i = 0; i < ordered.Count - 1; i++)
        {
            var front = ordered[i];
            var chaser = ordered[i + 1];

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

            var key = $"{front.Transponder}|{chaser.Transponder}";
            if (!_battlesAnnounced.Add(key))
            {
                continue;
            }

            FirePairLine(AnnouncementEventType.CloseBattle, front.Transponder, chaser.Transponder);
        }
    }

    /// <summary>
    /// Speaks a line about two drivers - {driver} and {rival} - for events
    /// the template system's single-driver substitution can't express.
    /// Shares the ghost-pass wording rules: announcer names, and the pass
    /// is dropped silently when the event is off or has no lines.
    /// </summary>
    private void FirePairLine(AnnouncementEventType type, string driverTransponder, string rivalTransponder)
    {
        if (Muted)
        {
            return;
        }

        var config = _voiceStore.Current.Events.GetValueOrDefault(type.ToString());

        if (config is null || !config.IsEnabledFor(IsPracticeMode) || config.EffectiveTemplates.Count == 0)
        {
            return;
        }

        if (HeldByCaution(type, config))
        {
            StatusMessage?.Invoke($"[voice] {type} held - a flag is out.");
            return;
        }

        var template = config.EffectiveTemplates[TemplatePicker.Next(config.EffectiveTemplates.Count)];

        var text = template
            .Replace("{driver}", DisplayNameFor(driverTransponder), StringComparison.OrdinalIgnoreCase)
            .Replace("{rival}", DisplayNameFor(rivalTransponder), StringComparison.OrdinalIgnoreCase)
            .Replace("{car}", AnnouncementText.OrMissing(_profiles.Get(driverTransponder)?.CarModel), StringComparison.OrdinalIgnoreCase)
            .WithDriverDetails(_profiles.Get(driverTransponder))
            .TidyTemplate();

        if (text.Length == 0)
        {
            return;
        }

        AnnouncementTextReady?.Invoke(text);

        var speed = config.SpeedFor(_voiceStore.Current.Speed);

        // Through the per-lap cap like the other reactions to a crossing, so
        // a lapping doesn't stack on top of a fast lap and a leader change.
        ConsiderPerLapAction("voice", driverTransponder, config.Weight, Math.Max(1, _voiceStore.Current.MaxAnnouncementsPerLap),
            () => EnqueueVoiceWork(type, driverTransponder, () => SynthesizeAndPlayAsync(text, $"[voice] {type}", speedOverride: speed)));
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
        AnnouncementTextReady?.Invoke(text);
        EnqueueVoiceWork(AnnouncementEventType.LeaderChange, overtakerTransponder, () => SynthesizeAndPlayAsync(text, "[voice] Rivalry announcement",
            "[voice] Skipped rivalry announcement - ElevenLabs API key or voice not configured."));
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
        var maxVoicePerLap = Math.Max(1, _voiceStore.Current.MaxAnnouncementsPerLap);
        var transponder = gate.Transponder;

        if (gate.Valid)
        {
            var cfg = sounds.Effects.GetValueOrDefault(nameof(SoundEffectKind.GoodLap));
            ConsiderPerLapAction("sound", transponder, cfg?.Weight ?? 40, maxSoundsPerLap,
                () => PlaySound(SoundEffectKind.GoodLap, ToneGenerator.PlayGoodLapAsync, "good lap chime"));
        }
        else
        {
            var cfg = sounds.Effects.GetValueOrDefault(nameof(SoundEffectKind.InvalidLap));
            ConsiderPerLapAction("sound", transponder, cfg?.Weight ?? 40, maxSoundsPerLap,
                () => PlaySound(SoundEffectKind.InvalidLap, ToneGenerator.PlayInvalidLapAsync, "invalid lap buzzer"));
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
            ConsiderPerLapAction("sound", transponder, streakSoundCfg?.Weight ?? 70, maxSoundsPerLap,
                () => PlaySound(SoundEffectKind.Streak, (v, ct) => ToneGenerator.PlayStreakAsync(streakCount, v, ct), $"streak sound (streak of {streakCount})", streakCount: streakCount));

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

            if (suppressed)
            {
                StatusMessage?.Invoke($"[voice] Streak for {DisplayNameFor(transponder)} not announced - {DisplayNameFor(chaser ?? string.Empty)} is on a longer run ({chaserCount}).");
            }
            else if (due && streakVoiceCfg is not null)
            {
                // The number, the runner-up and the only-longest check are
                // all resolved when the line is actually spoken - see
                // EnqueueStreakAnnouncement.
                var lapNumber = standing?.Laps ?? 0;

                ConsiderPerLapAction("voice", transponder, streakVoiceCfg.Weight, maxVoicePerLap,
                    () => EnqueueStreakAnnouncement(transponder, streakVoiceCfg, lapNumber));
            }
            else if (streakVoiceCfg is not null)
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
        if (gate.Valid && (!restrictedToBestLap || gate.Fast))
        {
            ConsiderPerLapAction("voice", transponder, lapVoiceCfg?.Weight ?? 40, maxVoicePerLap,
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
        if (lapTimeMs is > 0 && ShouldAnnounceLapTime(transponder, lapTimeCfg))
        {
            ConsiderPerLapAction("voice", transponder, lapTimeCfg?.Weight ?? 30, maxVoicePerLap,
                () => EnqueueVoice(AnnouncementEventType.LapTime, standing, gate, lapTimeMs));
        }

        if (gate.Fast)
        {
            var fastLapSoundCfg = sounds.Effects.GetValueOrDefault(nameof(SoundEffectKind.FastLap));
            ConsiderPerLapAction("sound", transponder, fastLapSoundCfg?.Weight ?? 90, maxSoundsPerLap,
                () => PlaySound(SoundEffectKind.FastLap, ToneGenerator.PlayFastLapAsync, "fast lap chime"));

            var fastLapVoiceCfg = _voiceStore.Current.Events.GetValueOrDefault(nameof(AnnouncementEventType.FastLap));
            ConsiderPerLapAction("voice", transponder, fastLapVoiceCfg?.Weight ?? 90, maxVoicePerLap,
                () => EnqueueVoice(AnnouncementEventType.FastLap, standing, gate));

            // In a qualifier the race's fastest lap is also the top of the
            // order - and for a driver's first flying lap it is the ONLY
            // report there is: the session never calls a first flying lap a
            // personal best, so pole taken first time out used to go
            // uncalled, and the pole holder was left as whoever had just
            // been beaten. Not while muted, the same as the personal-best
            // route. When a personal best for this same lap follows, it
            // finds the call already made (see _poleCalledFromGateFor).
            if (IsQualifyingMode && !Muted && standing is not null && lapTimeMs is > 0
                && CallProvisionalPoleIfTaken(transponder, standing, lapTimeMs.Value))
            {
                _poleCalledFromGateFor = transponder;
            }
        }
    }
}
