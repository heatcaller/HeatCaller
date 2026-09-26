using NAudio.Wave;

namespace HeatCaller;

/// <summary>
/// The race's own calls: the public Notify* entry points for flags, the
/// countdown, starts, finishes, records, pole, grid calls, jump starts and
/// warnings, and the finish-call hold and post-race quiet.
///
/// Moved here unchanged from AnnouncementService.cs on 26 Sep 2026, when the
/// file was split into parts. Fields with initializers stayed there.
/// </summary>
public sealed partial class AnnouncementService
{
    // --- Public trigger methods -------------------------------------------
    // These are the actual announcement entry points. Race control calls
    // them for whichever decoder is connected, and any other live event
    // source can call the same methods to drive identical voice/sound
    // output through the same shared settings.

    /// <summary>Feed a standings update from any source. Also drives leader-change detection.</summary>
    public void OnStandingUpdated(RaceStanding standing) => HandleStandingUpdated(standing);

    /// <summary>Feed a gate/lap pass from any source.</summary>
    public void OnGatePassed(GatePassEvent gate) => HandleGatePassed(gate);

    public void NotifyRaceStaging()
    {
        // A new race is being called to the grid, so whatever the last one
        // left behind no longer applies.
        EndPostRaceQuiet("a new race is staging");

        // A countdown abandoned for a fresh staging takes its ticks with it.
        CancelCountdownTicks();

        // Rendered now, while there is time, so the flag buttons during the
        // race play from memory.
        PrimeFlagLines();

        // Voice queued FIRST. PlaySoundAfterSpeech waits briefly then watches
        // for the queue to fall quiet, so if the sound is started first that
        // window can pass before the voice has even been queued - and the
        // cue fires early, which is the bug this was meant to fix.
        EnqueueVoice(AnnouncementEventType.RaceStaging, null, null);
        PlaySoundAfterSpeech(SoundEffectKind.RaceStaging, ToneGenerator.PlayRaceStagingAsync, "race staging sound");
    }

    /// <summary>A yellow/caution flag was called mid-race.</summary>
    public void NotifyYellowFlag()
    {
        // A flag call means racing is live again, whatever happened before it.
        EndPostRaceQuiet("a yellow flag was called");
        PlayFlagSound(SoundEffectKind.YellowFlag, ToneGenerator.PlayYellowFlagAsync, "yellow flag sound", AnnouncementEventType.YellowFlag);
        EnqueueVoice(AnnouncementEventType.YellowFlag, null, null);
    }

    /// <summary>A red flag was called - racing is stopped.</summary>
    public void NotifyRedFlag()
    {
        EndPostRaceQuiet("a red flag was called");
        PlayFlagSound(SoundEffectKind.RedFlag, ToneGenerator.PlayRedFlagAsync, "red flag sound", AnnouncementEventType.RedFlag);
        EnqueueVoice(AnnouncementEventType.RedFlag, null, null);
    }

    /// <summary>
    /// Racing resumed after a caution. Deliberately a separate event from
    /// RaceStarted so a restart can be worded differently from the initial
    /// start ("racing resumes" vs "and they're off").
    /// </summary>
    public void NotifyGreenFlagResumed()
    {
        EndPostRaceQuiet("racing resumed");
        PlayFlagSound(SoundEffectKind.GreenFlagResumed, ToneGenerator.PlayGreenFlagResumedAsync, "green flag sound", AnnouncementEventType.GreenFlagResumed);
        EnqueueVoice(AnnouncementEventType.GreenFlagResumed, null, null);
    }

    /// <summary>Completes when the latest flag's sound has played (or was skipped). See PlayFlagSound.</summary>
    private volatile Task _flagSoundDone = Task.CompletedTask;

    /// <summary>
    /// A flag's sound: first, before its call, the way the start horn comes
    /// before "and they're off" (see WaitForLeadInSoundAsync).
    ///
    /// It makes its own way to the speaker. The call would clear the decks
    /// for it, but only when the call is switched on - a club using the
    /// sound alone would otherwise hear its yellow after whatever lap line
    /// was being spoken. And an older flag's sound still waiting its turn
    /// is dropped: it is no longer the instruction.
    /// </summary>
    private void PlayFlagSound(SoundEffectKind kind, Func<float, CancellationToken, Task> playGenerated, string label, AnnouncementEventType call)
    {
        // Ticks mean "the green is coming", which a flag has just made untrue.
        CancelCountdownTicks();

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _flagSoundDone = done.Task;

        var config = _soundStore.Current.Effects.GetValueOrDefault(kind.ToString());

        if (!Muted && config is not null && config.IsEnabledFor(IsPracticeMode))
        {
            lock (_soundQueueLock)
            {
                RemoveQueuedSounds(item => SoundEffectsProfile.FlagKinds.Contains(item.Kind) && item.Kind != kind);
            }

            // Spares this flag's own sound (see LeadInSoundKindFor), and
            // cuts an older flag's if it is still playing.
            ClearTheDecksFor(call);
        }

        // Skipped, and "done" signalled, by PlaySound itself when it is off.
        PlaySound(kind, playGenerated, label, done);
    }

    /// <summary>
    /// A new all-time fastest lap for a track layout was just set. Fired
    /// directly (not through the per-lap weighted batching used for
    /// FastLap/LeaderChange/etc.) since track records are rare enough that
    /// there's no real competition to weigh against - when one happens, it
    /// should always be announced.
    /// </summary>
    public void NotifyTrackRecord(RaceStanding standing)
    {
        EnqueueVoice(AnnouncementEventType.TrackRecord, standing, null);
        PlaySoundAfterSpeech(SoundEffectKind.TrackRecord, ToneGenerator.PlayTrackRecordAsync, "track record sound");
    }

    /// <summary>
    /// Somebody is lapping at the pace of the record without having taken
    /// it - the build-up to a track record rather than the record itself.
    ///
    /// Fired directly, for the same reason NotifyTrackRecord is: the caller
    /// has already decided this is worth saying (once per driver per race,
    /// and not before their third counted lap), so there is nothing left
    /// for the per-lap weighting to usefully choose between.
    ///
    /// No sound effect. The record falling gets the fanfare; this is the
    /// announcer noticing it might, and a second chime for the near-miss
    /// would make the real one mean less.
    /// </summary>
    public void NotifyRecordPace(RaceStanding standing, long lapMs) =>
        EnqueueVoice(AnnouncementEventType.RecordPace, standing, null, lapMs);

    /// <param name="greenIn">
    /// When the green is due, if the caller knows - the automatic start
    /// does. The ticks are timed to end on it. Null when nobody knows (a
    /// green pressed by hand, or one that follows the countdown's audio):
    /// the ticks then play straight after the countdown sound, and the
    /// green, which waits for the countdown's audio, lands as they end.
    /// </param>
    public void NotifyCountdownStarted(TimeSpan? greenIn = null)
    {
        EndPostRaceQuiet("a countdown started");

        // A countdown restarted over one already running starts its ticks again.
        CancelCountdownTicks();

        // Sound only, no voice. A spoken countdown line took ~7 seconds to
        // synthesise and speak before the beep could follow it, and the
        // green flag waits for all of that - so the start sequence dragged.
        // The beep alone is what a countdown actually needs; the announcer
        // has already said the interesting part at staging.
        PlaySound(SoundEffectKind.CountdownStarted, ToneGenerator.PlayCountdownStartedAsync, "countdown started sound");

        StartCountdownTicks(greenIn);
    }

    /// <summary>
    /// How long the countdown ticks run: a beep a second for five seconds,
    /// and the green due as they end. The built-in sound is exactly this
    /// long - see ToneGenerator.PlayCountdownTicksAsync.
    /// </summary>
    public static readonly TimeSpan CountdownTicksLength = TimeSpan.FromSeconds(5);

    private readonly object _countdownTicksLock = new();

    /// <summary>The ticks waiting to start, when the green is further off than they last. See StartCountdownTicks.</summary>
    private CancellationTokenSource? _countdownTicksCts;

    private void StartCountdownTicks(TimeSpan? greenIn)
    {
        if (Muted)
        {
            return;
        }

        var config = _soundStore.Current.Effects.GetValueOrDefault(nameof(SoundEffectKind.CountdownTicks));
        if (config is null || !config.IsEnabledFor(IsPracticeMode))
        {
            return;
        }

        var wait = greenIn is { } due ? due - CountdownTicksLength : TimeSpan.Zero;

        // Now: queued behind the countdown sound, so they follow it.
        if (wait <= TimeSpan.FromMilliseconds(300))
        {
            PlaySound(SoundEffectKind.CountdownTicks, ToneGenerator.PlayCountdownTicksAsync, "countdown ticks");
            return;
        }

        // Later, so they end on the green. NOT counted as pending audio while
        // they wait: the green's own wait for the audio would then hang on
        // ticks that a green pressed by hand is about to cancel.
        var cts = new CancellationTokenSource();

        lock (_countdownTicksLock)
        {
            _countdownTicksCts = cts;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(wait, cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            lock (_countdownTicksLock)
            {
                if (!ReferenceEquals(_countdownTicksCts, cts) || cts.IsCancellationRequested)
                {
                    return;
                }

                _countdownTicksCts = null;
            }

            PlaySound(SoundEffectKind.CountdownTicks, ToneGenerator.PlayCountdownTicksAsync, "countdown ticks");

            // Cancelled in the moment between the check above and the sound
            // being queued - it goes again.
            if (cts.IsCancellationRequested)
            {
                DropCountdownTicks();
            }
        });
    }

    /// <summary>
    /// Stops the countdown ticks - waiting, queued or playing. Anything that
    /// means the green isn't coming on time calls this: a flag, a new
    /// staging or countdown, the start itself, a reset.
    /// </summary>
    private void CancelCountdownTicks()
    {
        lock (_countdownTicksLock)
        {
            _countdownTicksCts?.Cancel();
            _countdownTicksCts = null;
        }

        DropCountdownTicks();
    }

    private void DropCountdownTicks()
    {
        lock (_soundQueueLock)
        {
            RemoveQueuedSounds(item => item.Kind == SoundEffectKind.CountdownTicks);
        }

        StopPlayingSound(only: SoundEffectKind.CountdownTicks);
    }

    /// <summary>
    /// Announces a place change between a real driver and a ghost.
    ///
    /// {driver} is always the human and {rival} always the ghost, whichever
    /// way the pass went - so a template reads correctly either way round.
    /// </summary>
    public void NotifyGhostPass(string humanTransponder, string ghostTransponder, bool humanPassedGhost)
    {
        var type = humanPassedGhost
            ? AnnouncementEventType.GhostPassed
            : AnnouncementEventType.PassedByGhost;

        var config = _voiceStore.Current.Events.GetValueOrDefault(type.ToString());

        if (config is null || !config.IsEnabledFor(IsPracticeMode) || config.Templates.Count == 0)
        {
            return;
        }

        if (HeldByCaution(type, config))
        {
            return;
        }

        var template = config.EffectiveTemplates[TemplatePicker.Next(config.EffectiveTemplates.Count)];

        var text = template
            .Replace("{driver}", DisplayNameFor(humanTransponder), StringComparison.OrdinalIgnoreCase)
            .Replace("{rival}", DisplayNameFor(ghostTransponder), StringComparison.OrdinalIgnoreCase)
            .Trim();

        if (text.Length == 0)
        {
            return;
        }

        AnnouncementTextReady?.Invoke(text);

        // At this announcement's own Speed, as its Test button plays it.
        var speed = config.SpeedFor(_voiceStore.Current.Speed);

        EnqueueVoiceWork(type, humanTransponder,
            () => SynthesizeAndPlayAsync(text, $"[voice] {type}", speedOverride: speed));
    }

    /// <summary>
    /// Calls out a badge somebody just earned.
    ///
    /// Built the same way as a release call - from the transponder, not a
    /// standing - because by the time badges are worked out the race is over
    /// and the live standings have been cleared.
    /// </summary>
    public void NotifyAchievement(string transponderId, string badgeName)
    {
        if (Muted || string.IsNullOrWhiteSpace(transponderId) || string.IsNullOrWhiteSpace(badgeName))
        {
            return;
        }

        var type = AnnouncementEventType.AchievementUnlocked;
        var config = _voiceStore.Current.Events.GetValueOrDefault(type.ToString());

        if (config is null || !config.IsEnabledFor(IsPracticeMode) || config.EffectiveTemplates.Count == 0)
        {
            return;
        }

        var template = config.EffectiveTemplates[TemplatePicker.Next(config.EffectiveTemplates.Count)];
        var driverProfile = _profiles.Get(transponderId);

        var text = _voiceStore.Current.SanitiseForModel(template
            .Replace("{driver}", DisplayNameFor(transponderId), StringComparison.OrdinalIgnoreCase)
            .Replace("{car}", AnnouncementText.OrMissing(driverProfile?.CarModel), StringComparison.OrdinalIgnoreCase)
            .Replace("{badge}", AnnouncementText.OrMissing(badgeName), StringComparison.OrdinalIgnoreCase)
            .WithDriverDetails(driverProfile)
            .TidyTemplate());

        if (text.Length == 0)
        {
            return;
        }

        AnnouncementTextReady?.Invoke(text);

        var speed = config.SpeedFor(_voiceStore.Current.Speed);

        EnqueueVoiceWork(type, transponderId, () => SynthesizeAndPlayAsync(text, $"[voice] {type}", speedOverride: speed));
    }

    /// <summary>
    /// The name to speak for a transponder, honouring the announcer name.
    ///
    /// Takes the ID rather than a standing, because the two standing types
    /// in this app (RaceStanding and RacerStanding) expose different
    /// members - the ID is the one thing both agree on.
    /// </summary>
    private string DisplayNameFor(string transponder)
    {
        var profile = _profiles.Get(transponder);

        return profile is not null && !string.IsNullOrWhiteSpace(profile.SpokenName)
            ? profile.SpokenName
            : transponder;
    }

    public void NotifyRaceStarted()
    {
        StatusMessage?.Invoke($"[audio-trace] NotifyRaceStarted (muted={Muted})");
        EndPostRaceQuiet("a new race started");

        // Normally long finished - the green waits for the countdown's audio.
        // Not when the green was forced early, and they mustn't tick on
        // under the start.
        CancelCountdownTicks();

        // The sound is queued first and then the call clears the decks. That
        // clear only bins lap chimes, so this sound survives it - it used to
        // bin everything, and the start, time-up and finish sounds below
        // never played at all.
        //
        // The horn plays first and the call after it, the same rule as the
        // finish (see WaitForLeadInSoundAsync) - they used to race for the
        // speaker. The call is synthesised while the horn plays, and its
        // wait is capped short so a long custom start file can't hold up
        // the green.
        var startSound = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _startSoundDone = startSound.Task;
        PlaySound(SoundEffectKind.RaceStarted, ToneGenerator.PlayRaceStartedAsync, "race started sound", startSound);
        EnqueueVoice(AnnouncementEventType.RaceStarted, null, null);
    }

    public void NotifyRaceTimeOver()
    {
        // Buzzer first, then the call - same lead-in rule as the start.
        var timeOverSound = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _timeOverSoundDone = timeOverSound.Task;
        PlaySound(SoundEffectKind.RaceTimeOver, ToneGenerator.PlayRaceTimeOverAsync, "race time over sound", timeOverSound);
        EnqueueVoice(AnnouncementEventType.RaceTimeOver, null, null);
    }

    public void NotifyRaceCompleted()
    {
        StatusMessage?.Invoke($"[audio-trace] NotifyRaceCompleted (muted={Muted})");
        CancelCountdownTicks();

        // Holds the voice queue until the finish call has spoken - see
        // _finishCallHold. Raised before anything is queued, so no badge or
        // wrap-up line can slip in ahead of it.
        var hold = BeginFinishCallHold();

        // The finish sound plays first and the finish call after it - they
        // used to race for the speaker, and the call often won. The call's
        // playback waits on this (see WaitForLeadInSoundAsync).
        var finishSound = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _finishSoundDone = finishSound.Task;
        PlaySound(SoundEffectKind.RaceCompleted, ToneGenerator.PlayRaceCompletedAsync, "race completed sound", finishSound);

        // Queue the finish call BEFORE the gate goes up, so the gate can
        // simply block everything except the wrap-up rather than needing an
        // exception for the very line that raises it. EnqueueVoice runs the
        // template work synchronously on this thread, so by the time the
        // next line executes the call is already in flight.
        EnqueueVoice(AnnouncementEventType.RaceCompleted, null, null);

        // No finish call went out - switched off, muted, no words - so there
        // is nothing to wait for. Released now rather than left to time out,
        // or the badges would sit in silence for no reason.
        if (ReferenceEquals(Interlocked.CompareExchange(ref _finishCallHoldToClaim, null, hold), hold))
        {
            hold.TrySetResult();
        }

        BeginPostRaceQuiet();
    }

    /// <summary>
    /// Completes once the finish call has finished speaking (or was never
    /// made). The voice queue waits on it before taking its next line.
    ///
    /// Since the finish call started waiting for the finish sound, it has
    /// had to compete for the speaker when the sound ends - and the badge
    /// lines, which are worked out the moment the result is saved and
    /// synthesised meanwhile, often won. "Hat-trick for Sam" before the
    /// checkered flag had even been called.
    /// </summary>
    private volatile Task _finishCallHold = Task.CompletedTask;

    /// <summary>
    /// The hold waiting for the finish call to take ownership of it. The
    /// call claims it as it goes out and releases it when it has spoken;
    /// NotifyRaceCompleted releases it itself when nothing claimed it.
    /// Only ever touched through Interlocked.
    /// </summary>
    private TaskCompletionSource? _finishCallHoldToClaim;

    /// <summary>
    /// The longest the voice queue is held for the finish call. Covers the
    /// finish sound, a synthesis that has to go to the network and the call
    /// itself; past this the queue carries on regardless - the hold must
    /// never be able to silence the announcer.
    /// </summary>
    private static readonly TimeSpan FinishCallHoldMaxWait = TimeSpan.FromSeconds(30);

    private TaskCompletionSource BeginFinishCallHold()
    {
        var hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        // A previous finish whose call never claimed its hold - let it go.
        Interlocked.Exchange(ref _finishCallHoldToClaim, hold)?.TrySetResult();
        _finishCallHold = hold.Task;

        // Always released in the end, whatever happens to the call.
        _ = Task.Delay(FinishCallHoldMaxWait).ContinueWith(_ => { hold.TrySetResult(); }, TaskScheduler.Default);

        return hold;
    }

    /// <summary>
    /// Taken by the finish call as it goes out, so it can release the hold
    /// once it has spoken. Null for every other call.
    /// </summary>
    private TaskCompletionSource? ClaimFinishCallHold(AnnouncementEventType type) =>
        type == AnnouncementEventType.RaceCompleted
            ? Interlocked.Exchange(ref _finishCallHoldToClaim, null)
            : null;

    /// <summary>Waits, from the voice queue, for the finish call to have spoken - capped, see FinishCallHoldMaxWait.</summary>
    private async Task WaitForFinishCallAsync()
    {
        var hold = _finishCallHold;

        if (hold.IsCompleted)
        {
            return;
        }

        StatusMessage?.Invoke("[voice] Holding the queue until the finish call has spoken.");
        await Task.WhenAny(hold, Task.Delay(FinishCallHoldMaxWait));
    }

    // ---------------------------------------------------------------
    // Post-race quiet
    // ---------------------------------------------------------------

    /// <summary>
    /// True from the moment the finish call is made until the next race
    /// activity. While it is set, the only voice allowed through is the
    /// wrap-up - the teaser and the recap that follows it.
    ///
    /// The race being over does not stop transponders crossing: cars still
    /// on track complete the lap they're on, the timing feed keeps
    /// delivering standings, and every one of those used to produce lap
    /// times, position changes, personal bests and streak lines talking over
    /// a race that had already finished. Nothing said after the checkered
    /// flag about a lap that no longer counts is worth hearing.
    /// </summary>
    private volatile bool _postRaceQuiet;

    /// <summary>
    /// The wrap-up, and only the wrap-up. Both the teaser and the AI recap
    /// are spoken through SpeakReminder, which queues as
    /// AnnouncementEventType.Reminder - so allowing that one type covers the
    /// whole post-race segment and nothing else.
    ///
    /// Scheduled reminders (a Day Schedule warning, say) share the type and
    /// are allowed through with it. That is deliberate: those are timed,
    /// deliberate speech that has nothing to do with the race that just
    /// ended, and silently swallowing one would be its own bug.
    /// </summary>
    private static bool IsAllowedAfterRaceOver(AnnouncementEventType type) =>
        type is AnnouncementEventType.Reminder

        // Every race-control call is allowed through as well, belt and
        // braces. They all clear the hold themselves before they speak, so
        // in principle they never meet it - but this hold exists to stop LAP
        // CHATTER, and if a path is ever added that reaches here without
        // clearing it first, the failure would be a silent staging or flag
        // call, which is far worse than one stray line. The hold should
        // never be able to swallow an instruction.
        or AnnouncementEventType.RaceStaging
        or AnnouncementEventType.CountdownStarted
        or AnnouncementEventType.RaceStarted
        or AnnouncementEventType.GreenFlagResumed
        or AnnouncementEventType.YellowFlag
        or AnnouncementEventType.RedFlag
        or AnnouncementEventType.DriverRelease

        // The next round being called to the grid. NotifyGridCall lifts the
        // hold itself, since a grid call means the next race is being set
        // up; listed here for the same belt-and-braces reason as the rest.
        or AnnouncementEventType.GridCall

        // Badges are worked out after the flag by definition, so the
        // post-race hold would otherwise swallow every one of them.
        or AnnouncementEventType.AchievementUnlocked;

    private void BeginPostRaceQuiet()
    {
        if (_postRaceQuiet)
        {
            return;
        }

        _postRaceQuiet = true;
        StatusMessage?.Invoke("[voice] Race over - holding every voice line except the wrap-up until the next race.");
    }

    /// <summary>
    /// Lifts the hold. Called by anything that means racing is live again -
    /// staging, a countdown, a start, a flag call, or a session reset.
    /// </summary>
    private void EndPostRaceQuiet(string reason)
    {
        if (!_postRaceQuiet)
        {
            return;
        }

        _postRaceQuiet = false;
        StatusMessage?.Invoke($"[voice] Post-race hold lifted - {reason}.");
    }



    /// <summary>
    /// Announces a jump start. Fired from the session rather than the gate
    /// pipeline, because it happens before the race is running and there is
    /// no lap to hang it off.
    /// </summary>
    public void NotifyJumpStart(string transponderId, long penaltyMs)
    {
        if (Muted)
        {
            return;
        }

        // Its own sound, on its own switch. It used to borrow the invalid-lap
        // buzzer, and only played when the spoken call was on as well - so a
        // club that wanted the buzzer alone couldn't have it.
        PlaySound(SoundEffectKind.JumpStart, ToneGenerator.PlayJumpStartAsync, "jump start sound");

        var config = _voiceStore.Current.Events.GetValueOrDefault(nameof(AnnouncementEventType.JumpStart));
        if (config is null || !config.IsEnabledFor(IsPracticeMode) || config.Templates.Count == 0)
        {
            return;
        }

        // Before the race there may be no standing yet. A bare one is
        // enough - EnqueueVoice resolves the driver's name from their
        // profile, so it doesn't need to be filled in here.
        var standing = _standings.TryGetValue(transponderId, out var known)
            ? known
            : new RaceStanding { Transponder = transponderId };

        EnqueueVoice(AnnouncementEventType.JumpStart, standing, null);

        StatusMessage?.Invoke(penaltyMs > 0
            ? $"[race] Jump start by {standing.DisplayName} - {penaltyMs / 1000.0:0.0}s penalty."
            : $"[race] Jump start by {standing.DisplayName} - flagged, no automatic penalty.");
    }

    /// <summary>
    /// Calls a warning from race control: the buzzer, then the line spoken
    /// as written ("Warning, Asher, blocking. Five second penalty."). The
    /// line is built by Race Control, which knows the driver's name and
    /// what the class's ladder made of it.
    ///
    /// Through SpeakReminder rather than a template event of its own, so
    /// it works with every voice already set up and needs no new entry in
    /// Voice Settings. Muted means muted - the Big Screen banner and the
    /// board still show it.
    /// </summary>
    public void NotifyWarning(string spokenLine, bool blackFlag)
    {
        if (Muted || string.IsNullOrWhiteSpace(spokenLine))
        {
            return;
        }

        PlaySound(SoundEffectKind.InvalidLap, ToneGenerator.PlayInvalidLapAsync, blackFlag ? "black flag buzzer" : "warning buzzer");
        SpeakReminder(spokenLine);
    }

    /// <summary>
    /// Announces that a driver beat their own previous best this race.
    ///
    /// A dedicated method rather than another flag on the gate event: the
    /// gate object is shared with the decoder-facing code and widening it
    /// for one announcement type would ripple further than the feature
    /// warrants.
    ///
    /// Skipped when this lap is ALSO the fastest of the race, since that
    /// fires its own, louder announcement - hearing both back to back is
    /// exactly the noise splitting these events was meant to remove.
    /// </summary>
    public void NotifyPersonalBestLap(string transponderId, long lapMs)
    {
        if (Muted || lapMs <= 0)
        {
            return;
        }

        var standing = _standings.TryGetValue(transponderId, out var known) ? known : null;
        if (standing is null)
        {
            return;
        }

        // The sound, for every personal best that isn't also the race's
        // fastest lap - that one has its own, bigger chime. Weighed with the
        // lap's other sounds against the per-lap limit, like them. Before the
        // qualifying split below, because an improvement in a qualifier is
        // just as much a personal best. Never for a ghost.
        if (!GhostService.IsGhost(transponderId)
            && !(standing.FastLapMs > 0 && standing.FastLapMs == lapMs && IsRaceFastest(lapMs)))
        {
            var sounds = _soundStore.Current;
            var bestCfg = sounds.Effects.GetValueOrDefault(nameof(SoundEffectKind.PersonalBest));
            ConsiderPerLapAction("sound", transponderId, bestCfg?.Weight ?? 60, Math.Max(1, sounds.MaxEffectsPerLap),
                () => PlaySound(SoundEffectKind.PersonalBest, ToneGenerator.PlayPersonalBestAsync, "personal best chime"));
        }

        // In a qualifier a personal best is a running-order event, not a
        // race-fastest one - everyone is chasing their own clock. So it
        // becomes a provisional-pole call when it goes to the top, or an
        // improvement otherwise, and the generic personal-best line is left
        // to real races. Checked BEFORE the personal-best config gate,
        // because those events have their own enable switches.
        if (IsQualifyingMode)
        {
            // The same lap has just been called as taking pole, from the
            // race's fastest lap (see HandleGatePassed, which the session
            // reports first). Calling it an improvement as well would be the
            // same lap announced twice.
            if (string.Equals(_poleCalledFromGateFor, transponderId, StringComparison.Ordinal))
            {
                _poleCalledFromGateFor = null;
                return;
            }

            if (CallProvisionalPoleIfTaken(transponderId, standing, lapMs))
            {
                return;
            }

            EnqueueVoice(AnnouncementEventType.QualifyingImprovement, standing, null, lapMs);
            return;
        }

        var config = _voiceStore.Current.Events.GetValueOrDefault(nameof(AnnouncementEventType.PersonalBestLap));
        if (config is null || !config.IsEnabledFor(IsPracticeMode) || config.Templates.Count == 0)
        {
            return;
        }

        // The race's fastest lap owns this moment if it's the same lap.
        if (standing.FastLapMs > 0 && standing.FastLapMs == lapMs && IsRaceFastest(lapMs))
        {
            return;
        }

        EnqueueVoice(AnnouncementEventType.PersonalBestLap, standing, null, lapMs);
    }

    /// <summary>
    /// The leader has begun the final lap of a lap-limited race - fired once
    /// by the session. The single most exciting call in a race, so it goes
    /// straight to the queue rather than through per-lap weighting.
    /// </summary>
    public void NotifyLastLap(string transponderId)
    {
        var standing = _standings.TryGetValue(transponderId, out var known) ? known : null;
        EnqueueVoice(AnnouncementEventType.LastLap, standing, null);
    }

    /// <summary>A timed race has one minute left - fired once by the session.</summary>
    public void NotifyFinalMinute() =>
        EnqueueVoice(AnnouncementEventType.FinalMinute, null, null);

    /// <summary>
    /// Calls a round to the grid as it starts - the spoken half of the Next
    /// Up bar. Fully dynamic strings (round label, class, who marshals), so
    /// they ride in as substitution values rather than being looked up from
    /// a driver profile.
    ///
    /// Made just BEFORE the round is staged, and spoken before the staging
    /// line: the grid call says which heat and who marshals it, the staging
    /// line then tells that grid to get ready - the order a race director
    /// would say them in. It used to be lost almost every time: the
    /// post-race hold was still up from the last race and dropped it, and
    /// when it did get queued, the staging line (a priority line) binned it
    /// from the queue a moment later. It is a priority line itself now, so
    /// it survives staging, is never dropped for something else speaking,
    /// and keeps its place ahead of the staging line.
    /// </summary>
    public void NotifyGridCall(string roundLabel, string className, string marshals)
    {
        // The next race is being set up - the last one's wrap-up hold no
        // longer applies. Staging would lift it a moment later anyway, but
        // by then this call had already been dropped.
        EndPostRaceQuiet("the next round was called to the grid");

        EnqueueVoice(AnnouncementEventType.GridCall, null, null,
            roundLabel: roundLabel, className: className, marshals: marshals);
    }

    /// <summary>
    /// Calls a round to the grid with {marshals} filled in - the drivers
    /// posted to the corners for this heat, said as "Sam, Bill and Frank".
    ///
    /// The roster is worked out here rather than passed in because it is
    /// never stored anywhere: the Marshals dialog on the Program tab
    /// computes it on the spot, and so does this, with the same inputs, so
    /// what the announcer says is what that dialog shows. Nobody assigned
    /// (no posts on the track, the first heat of the day, the previous heat
    /// all driving again) gives an empty value, and the grid call then
    /// drops the words that would have introduced the names.
    ///
    /// <paramref name="events"/> keeps the roster inside the round's own
    /// race day, as the Marshals dialog does: the store holds every day's
    /// rounds, and without it the first heat of tonight would be
    /// "marshalled" by whoever drove the last heat of last week.
    /// </summary>
    public void NotifyGridCall(RoundDefinition round, RoundDefinitionStore rounds, TrackLayoutStore layouts, RaceEventStore? events = null) =>
        NotifyGridCall(round.RoundLabel, round.ClassName, SpokenMarshalsFor(round, rounds, layouts, events));

    /// <summary>The marshals for one round as a spoken list; empty when none are assigned.</summary>
    private string SpokenMarshalsFor(RoundDefinition round, RoundDefinitionStore rounds, TrackLayoutStore layouts, RaceEventStore? events)
    {
        try
        {
            IReadOnlyList<RoundDefinition> classRounds = rounds.ForClass(round.ClassName);

            // Scoped to the race day the round is on (the legacy holder is
            // not a day, so it means "no scoping", exactly as ActiveDay
            // treats it on the Program tab).
            if (events?.EventForRound(round.Id) is { IsLegacy: false } day)
            {
                var dayIds = day.RoundIds.ToHashSet(StringComparer.Ordinal);
                classRounds = classRounds.Where(r => dayIds.Contains(r.Id)).ToList();
            }

            // Posts belong to the track, and the layout is found exactly the
            // way the Marshals dialog finds it - the first round of the class
            // that names one - so the two can never disagree about who is on
            // which corner.
            var layoutName = classRounds
                .Select(r => r.TrackLayoutName)
                .FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));

            var layout = layoutName is null
                ? null
                : layouts.All.FirstOrDefault(l => string.Equals(l.Name, layoutName, StringComparison.OrdinalIgnoreCase));

            var assignment = MarshalAssigner.Assign(classRounds, layout, _profiles)
                .FirstOrDefault(a => a.Round.Id == round.Id);

            if (assignment is null)
            {
                return string.Empty;
            }

            // The announcer name where one is set, same as {driver} - a
            // nickname the room knows, or a spelling the voice gets right.
            var names = assignment.Posts
                .Where(p => p.IsFilled)
                .Select(p => _profiles.Get(p.Transponder!)?.SpokenName ?? p.DriverName ?? p.Transponder!)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return AnnouncementText.SpokenList(names);
        }
        catch (Exception ex)
        {
            // A grid call without names beats no grid call at all.
            StatusMessage?.Invoke($"[voice] Couldn't work out the marshals for {round.RoundLabel}: {ex.Message}");
            return string.Empty;
        }
    }

    /// <summary>Clears the provisional-pole memory so the next qualifier starts its order fresh.</summary>
    public void ResetQualifying()
    {
        _provisionalPoleTransponder = null;
        _poleCalledFromGateFor = null;
    }

    /// <summary>
    /// The driver whose provisional pole was just called from the race's
    /// fastest lap, until their personal-best report for that same lap
    /// arrives (or their next crossing, when the lap was not a personal best
    /// - a first flying lap never is). Stops the one lap being called as
    /// taking pole and then again as an improvement.
    /// </summary>
    private string? _poleCalledFromGateFor;

    /// <summary>
    /// Calls provisional pole when this lap puts the driver on top of the
    /// qualifying order and somebody else held it. Records the pole holder
    /// either way. True when the call was made.
    ///
    /// Reached from two places, because neither sees every pole on its
    /// own: a personal best (the usual way to go top), and the race's
    /// fastest lap - a driver's FIRST flying lap is never reported as a
    /// personal best, since there was nothing of their own to beat, so a
    /// driver who went straight to the top first time out was never called
    /// and the pole holder kept pointing at whoever they had just beaten.
    /// </summary>
    private bool CallProvisionalPoleIfTaken(string transponderId, RaceStanding standing, long lapMs)
    {
        if (!IsRaceFastest(lapMs))
        {
            return false;
        }

        var tookPole = !string.Equals(_provisionalPoleTransponder, transponderId, StringComparison.Ordinal);
        _provisionalPoleTransponder = transponderId;

        if (!tookPole)
        {
            return false;
        }

        EnqueueVoice(AnnouncementEventType.ProvisionalPole, standing, null, lapMs);
        return true;
    }

    /// <summary>True when no other tracked racer has gone quicker.</summary>
    private bool IsRaceFastest(long lapMs) =>
        _standings.Values.All(s => s.FastLapMs <= 0 || s.FastLapMs >= lapMs);

    /// <summary>
    /// Derives this specific lap's duration from the change in cumulative
    /// elapsed time since this transponder's previous finish-line pass. The
    /// first lap has no previous pass to diff against, so its "lap time" is
    /// just the elapsed time so far (correct, assuming the race clock starts
    /// at the same moment the first lap does).
    /// </summary>
    private long? ComputeLapTimeMs(string transponder, RaceStanding? standing)
    {
        var currentElapsed = standing?.ElapsedMs ?? 0;
        if (currentElapsed <= 0)
        {
            return null;
        }

        long? lapTimeMs = _lastLapElapsedMs.TryGetValue(transponder, out var previousElapsed) && currentElapsed > previousElapsed
            ? currentElapsed - previousElapsed
            : currentElapsed;

        _lastLapElapsedMs[transponder] = currentElapsed;
        return lapTimeMs;
    }
}
