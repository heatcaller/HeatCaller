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

    /// <summary>
    /// A row taken off the board mid-race - a ghost switched off. Forgotten
    /// here as well, or it stayed in the running order the calls are worked
    /// out from, frozen on its last lap: the leader was called as lapping it
    /// again on every lap it lost, and it could still be named in a battle,
    /// a rivalry or a streak. The car now in front of it isn't called as
    /// taking a lead it was simply left.
    /// </summary>
    public void OnStandingRemoved(string transponder)
    {
        lock (_voiceQueueLock)
        {
            _standings.Remove(transponder);
            _lapCrossings.Remove(transponder);
            _lappedAnnounced.Remove(transponder);

            if (string.Equals(_currentLeaderTransponder, transponder, StringComparison.OrdinalIgnoreCase))
            {
                _currentLeaderTransponder = null;
            }

            _leads.Forget(transponder);

            // Nor is the car behind it called as passing it in a position
            // report it was named in (10 Nov 2026, bug hunt 16).
            ForgetInPositionReportOnAir(transponder);

            lock (_streakLock)
            {
                _currentStreaks.Remove(transponder);

                if (string.Equals(_streakLeader, transponder, StringComparison.OrdinalIgnoreCase))
                {
                    _streakLeader = null;
                    _streakLeaderCount = 0;
                }
            }
        }
    }

    public void NotifyRaceStaging()
    {
        // A new race is being called to the grid, so whatever the last one
        // left behind no longer applies.
        EndPostRaceQuiet("a new race is staging");

        // Including how many calls the last race made and lost, and the
        // pacing clock - see ResetAnnouncerLoad, which says what happens
        // to a club that presses green from Idle without staging at all.
        ResetAnnouncerLoad();

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

    /// <summary>
    /// The quickest anyone has gone through this sector this session. The
    /// caller (Race Control) has decided it beats an earlier best, so it is
    /// not the session's first sector time, which is fastest by default.
    /// </summary>
    public void NotifyFastestSector(RaceStanding standing, int sector, long sectorMs) =>
        EnqueueVoice(AnnouncementEventType.FastestSector, standing, null, extras: SectorExtras(sector, sectorMs));

    /// <summary>
    /// {sector} and {sectortime}, spoken and on screen.
    ///
    /// A sector is NOT a lap time, so it is said through RaceNumbers.Plainly
    /// ("two point zero six seconds") - 17 Oct 2026, Bug Hunt 8, A06-9/A07-4:
    /// it went through FormatSecondsForSpeech, the lap-time convention, which
    /// the remarks on that method and on Plainly both say a sector does not
    /// use.
    /// </summary>
    internal static Dictionary<string, (string, string)> SectorExtras(int sector, long sectorMs) => new()
    {
        ["sector"] = (sector.ToString(System.Globalization.CultureInfo.InvariantCulture), sector.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        // (10 Nov 2026, bug hunt 16) shown from the same hundredths as said.
        ["sectortime"] = (RaceNumbers.Plainly(sectorMs), RaceNumbers.HundredthsShown(sectorMs)),
    };

    /// <summary>Whether this call is switched on for the session running now (races or practice) - so a caller choosing between calls picks one that will be said.</summary>
    public bool WillCall(AnnouncementEventType type) =>
        !Muted
        && _voiceStore.Current.Events.TryGetValue(type.ToString(), out var config)
        && CallOn(config)
        && !HeldByCaution(type, config);

    /// <summary>A driver crossed the split loop up on their own best lap by <paramref name="upMs"/>.</summary>
    public void NotifySplitPace(RaceStanding standing, string split, long upMs) =>
        EnqueueVoice(AnnouncementEventType.SplitPace, standing, null, extras: SplitExtras(split, upMs), sayByMs: SplitSayBy());

    /// <summary>A driver crossed the split loop ahead of the track record lap there by <paramref name="upMs"/>.</summary>
    public void NotifySplitRecordPace(RaceStanding standing, string split, long upMs) =>
        EnqueueVoice(AnnouncementEventType.SplitRecordPace, standing, null, extras: SplitExtras(split, upMs), sayByMs: SplitSayBy());

    /// <summary>{split} and {gap} - a gap to the tenth, said as "two tenths".</summary>
    private static Dictionary<string, (string, string)> SplitExtras(string split, long upMs)
    {
        // Tenths: a split time is good to a few hundredths. SAID the way
        // a gap is called (15 Oct 2026) - "two tenths up", "half a second
        // up" - where it used to be "point two seconds", which is the
        // computer reading RaceNumbers exists to replace. Never under a
        // tenth: a split that came out at nought would read as "nothing
        // up", and a call this fires on is at least a tenth by its rule.
        var tenths = Math.Max(1, (long)Math.Round(Math.Abs(upMs) / 100.0));
        var spoken = RaceNumbers.GapSpoken(tenths / 10.0);

        return new Dictionary<string, (string, string)>
        {
            ["split"] = (split, split),
            ["gap"] = (spoken, (tenths / 10.0).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "s"),
        };
    }

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

        // Whatever is still talking stops here, before the countdown's own
        // sounds are queued - they share the speaker, and in rehearsal 1 the
        // AI round intro was still speaking when the countdown started, so
        // the countdown sounds waited behind it and came out late, over
        // the green.
        CutForCountdown();

        // Sound only, no voice. A spoken countdown line took ~7 seconds to
        // synthesise and speak before the beep could follow it, and the
        // green flag waits for all of that - so the start sequence dragged.
        // The beep alone is what a countdown actually needs; the announcer
        // has already said the interesting part at staging.
        //
        // Said in the log, though, when the club has switched the line on:
        // the settings still offer it, test it and record it, and it used to
        // go unsaid every race with nothing anywhere to say why.
        if (!Muted && CallOn(_voiceStore.Current.Events.GetValueOrDefault(nameof(AnnouncementEventType.CountdownStarted))))
        {
            StatusMessage?.Invoke("[voice] CountdownStarted not spoken - the countdown is sound only, so the green is not held behind a spoken line. Put the words in Race staging instead.");
        }

        // And only when the ticks are off. With both, every race heard two
        // countdowns back to back - three quick beeps, then five slow ones -
        // and the ticks are the one that ends on the green.
        if (CountdownTicksAreOn())
        {
            StatusMessage?.Invoke("[sound] Skipped countdown started sound - the countdown ticks are on, so they are the countdown.");
        }
        else
        {
            PlaySound(SoundEffectKind.CountdownStarted, ToneGenerator.PlayCountdownStartedAsync, "countdown started sound");
        }

        StartCountdownTicks(greenIn);
    }

    /// <summary>
    /// Whether StartCountdownTicks will play the ticks: not muted, and the
    /// effect switched on for races or practice, whichever this is.
    /// </summary>
    private bool CountdownTicksAreOn()
    {
        if (Muted)
        {
            return false;
        }

        var config = _soundStore.Current.Effects.GetValueOrDefault(nameof(SoundEffectKind.CountdownTicks));
        return config is not null && config.IsEnabledFor(IsPracticeMode);
    }

    /// <summary>
    /// Cuts off everything still talking when a countdown starts: the line
    /// playing, the lines queued, a line already taken off the queue and
    /// still synthesising (see _countdownCutGeneration), the sound playing,
    /// the sounds queued, a sound waiting to follow its line (see
    /// PlaySoundAfterSpeech), per-lap lines waiting to be weighed, and any
    /// line a caller is still waiting on a model to write (see
    /// BeginLineBeingWritten).
    ///
    /// Unlike a flag (ClearTheDecksFor), nothing is spared: race-control
    /// lines queued before the countdown - the staging call, the grid call,
    /// the round intro - were all about getting to this moment, and the
    /// countdown is the instruction that replaces them.
    /// </summary>
    private void CutForCountdown() => CutEverything("Countdown started", alsoFlagCalls: false);

    /// <summary>
    /// Everything the announcer is saying or about to say stops NOW (26 Oct
    /// 2026, asked for by Asher: "when using reset the voice should stop.
    /// everything should stop and be brought back to nothing") - the line in
    /// the speakers, the sound playing, every queued line and sound, a line
    /// still being written or voiced, the countdown's ticks, and the
    /// sponsor's picture. The countdown's own cut (CutForCountdown), with
    /// nothing spared, and the ticks as well, since a reset is never
    /// followed by the green they were counting down to. For Race Control's
    /// Reset.
    /// </summary>
    public void SilenceNow(string why)
    {
        CancelCountdownTicks();
        CutEverything(why, alsoFlagCalls: true);
    }

    /// <summary>
    /// The cut itself - see CutForCountdown and SilenceNow. <paramref name="why"/>
    /// starts the log line. <paramref name="alsoFlagCalls"/>: a flag call
    /// still being voiced is cut too (SilenceNow). The countdown spares it -
    /// a flag call is never queued behind the countdown's cut generation -
    /// but a reset must not have "Red flag" arrive a second after it, so the
    /// flags' own generation is moved on as well, which is what silences a
    /// flag call overtaken by a newer flag.
    /// </summary>
    /// <summary>
    /// Raised once everything has been cut by <see cref="SilenceNow"/> - a
    /// reset (26 Oct 2026). Not by the countdown's own cut. On whatever thread
    /// called SilenceNow; subscribers must not block.
    /// </summary>
    public event Action? Silenced;

    /// <summary>
    /// How many times <see cref="SilenceNow"/> has cut everything. A caller
    /// that feeds lines in over time captures it at the start and stops when
    /// it moves (26 Oct 2026) - see AnnouncerControl.FeedTheReadAsync.
    /// </summary>
    public long SilencedGeneration => Interlocked.Read(ref _silencedGeneration);

    private long _silencedGeneration;

    private void CutEverything(string why, bool alsoFlagCalls)
    {
        int clearedVoice;
        int abandonedWriting;

        // WHY (23 Oct 2026, bug hunt 10): a line binned here gives back what
        // it reserved (a sponsor's ration, a battle's pair) once the queue
        // lock is released - see BinQueuedLocked.
        List<Action>? giveBack = null;

        lock (_voiceQueueLock)
        {
            lock (_linesBeingWrittenLock)
            {
                // Under the queue lock, which the voice loop dequeues under:
                // a line was either taken before this (and now holds an old
                // generation, so it stays silent) or is still queued and
                // cleared just below. Under the writing lock too, so a line
                // being written is either released here or started after.
                Interlocked.Increment(ref _countdownCutGeneration);

                if (alsoFlagCalls)
                {
                    Interlocked.Increment(ref _interruptGeneration);

                    // Moved on under the same locks as the cut itself (26 Oct
                    // 2026, review), so a long read's feeder that queues a
                    // piece is either before the cut (and binned by it) or
                    // sees the new generation. Raised as Silenced below.
                    Interlocked.Increment(ref _silencedGeneration);
                }

                abandonedWriting = _linesBeingWritten;
                _linesBeingWritten = 0;
            }

            clearedVoice = BinQueuedLocked(_ => true, ref giveBack);
        }

        GiveBackLost(giveBack);

        // WHY (7 Nov 2026, bug hunt 14): a position report still waiting for
        // the cars it names has its own timer, and when that ran out after a
        // reset it read the abandoned race's order to the room - queued under
        // the new generation, so nothing above silenced it - up to a minute
        // after the director pressed Reset. Only on a reset (SilenceNow), as
        // that is the cut that leaves the race behind. The lap counter and
        // the crossings are left alone, so undoing the reset still has them.
        if (alsoFlagCalls)
        {
            lock (_positionReportLock)
            {
                _waitingPositionReport = null;
            }
        }

        int clearedSounds;

        lock (_soundQueueLock)
        {
            // The countdown's own sounds are not queued yet - they are
            // queued straight after this.
            clearedSounds = RemoveQueuedSounds(_ => true);
        }

        lock (_batchLock)
        {
            foreach (var cts in _pendingBatchCancellation.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }

            _pendingBatchCancellation.Clear();
            _pendingPerLapBatches.Clear();

            // And the field's calls waiting for their window - the same
            // lap chatter, one step further on. See OfferToTheField.
            BinTheFieldCallsUnderLock();
        }

        // And the one call kept for the end of a line: what it was about
        // is no longer the race. See HeldOver.
        ForgetTheHeldOverCall(count: true);

        var wasSpeaking = StopCurrentPlayback();
        var cutSound = StopPlayingSound();

        // AND THE SPONSOR'S PICTURE COMES OFF THE CROWD SCREEN (13 Oct
        // 2026). SponsorOnAir.End was written for "a read abandoned" and
        // called from nowhere, so a read cut off here kept the screen for
        // the rest of its ESTIMATED length - up to forty seconds of a
        // sponsor's logo over a race going green, which is the one thing
        // on that screen the crowd is actually watching for.
        //
        // Unconditionally, not only when something was speaking: a read
        // whose words finished a moment ago still holds the screen through
        // its overhang, and the countdown has to win that. See
        // SponsorOnAir, and BigScreenForm, where Showing takes priority
        // over everything else it would otherwise draw.
        SponsorOnAir.End();

        // WHY (26 Oct 2026, review of the voiced-ahead long reads): tonight's
        // recap and a driver's night feed their pieces into the queue as they
        // are voiced, so a piece voiced AFTER this cut would be queued under
        // the new generation and play over the reset. They watch this to stop
        // feeding - see SilencedGeneration. Raised whether or not anything was
        // speaking: a read between two pieces has nothing in the speaker.
        if (alsoFlagCalls)
        {
            try
            {
                Silenced?.Invoke();
            }
            catch
            {
                // A subscriber throwing must never stop a reset.
            }
        }

        // Silent when there was nothing to cut, which is the usual case -
        // the staging chain waits for the audio before it starts a countdown.
        if (!wasSpeaking && !cutSound && clearedVoice == 0 && clearedSounds == 0 && abandonedWriting == 0)
        {
            return;
        }

        StatusMessage?.Invoke(
            $"[voice] {why} - {(wasSpeaking ? "cut off the line in progress" : "nothing was speaking")}"
            + $"{(cutSound ? " and the sound playing" : string.Empty)}, "
            + $"cleared {Plural.Of(clearedVoice, "queued line")} and {Plural.Of(clearedSounds, "sound")}"
            + $"{(abandonedWriting > 0 ? $", and stopped waiting for {Plural.Of(abandonedWriting, "line")} still being written" : string.Empty)}.");
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

        if (config is null || !CallOn(config) || config.Templates.Count == 0)
        {
            return;
        }

        if (HeldByCaution(type, config))
        {
            return;
        }

        // WHY (20 Oct 2026): this call goes straight to EnqueueVoiceWork,
        // not through EnqueueVoice, so it skipped the club's chattiness
        // dial - a quiet announcer still called every ghost pass. It now
        // asks the dial first and logs the hold in the same words.
        if (!DialLetsItSpeak(type, config))
        {
            StatusMessage?.Invoke(
                $"[voice] {type} held - the announcer is set to {ShowChatter.For(Chatter).Name}.");
            return;
        }

        var template = TemplateBag.Pick(config.EffectiveTemplates);

        var text = template
            .Replace("{driver}", DisplayNameFor(humanTransponder), StringComparison.OrdinalIgnoreCase)
            .Replace("{rival}", DisplayNameFor(ghostTransponder), StringComparison.OrdinalIgnoreCase)
            .Trim();

        if (text.Length == 0)
        {
            return;
        }

        // At this announcement's own Speed, as its Test button plays it.
        var speed = config.SpeedFor(_voiceStore.Current.Speed);

        // Screen text raised by EnqueueVoiceWork, once the line is accepted.
        EnqueueVoiceWork(type, humanTransponder,
            () => SynthesizeAndPlayAsync(text, $"[voice] {type}", speedOverride: speed),
            displayText: text,
            renderAhead: () => RenderLineAheadAsync(text, type, speed));
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
        if (string.IsNullOrWhiteSpace(transponderId) || string.IsNullOrWhiteSpace(badgeName))
        {
            return;
        }

        // The stream's milestone banner goes up whether or not the voice
        // says it - a muted announcer still has graphics. Not for a ghost:
        // a pace car does not reach milestones.
        if (!GhostService.IsGhost(transponderId))
        {
            PostMilestoneMoment(transponderId, badgeName);
        }

        if (Muted)
        {
            return;
        }

        var type = AnnouncementEventType.AchievementUnlocked;
        var config = _voiceStore.Current.Events.GetValueOrDefault(type.ToString());

        if (config is null || !CallOn(config) || config.EffectiveTemplates.Count == 0)
        {
            return;
        }

        var template = TemplateBag.Pick(config.EffectiveTemplates);
        var driverProfile = _profiles.Get(transponderId);

        var text = _voiceStore.Current.SanitiseForModel(template
            .Replace("{driver}", DisplayNameFor(transponderId), StringComparison.OrdinalIgnoreCase)
            .Replace("{car}", AnnouncementText.OrMissing(_profiles.CarModelFor(transponderId)), StringComparison.OrdinalIgnoreCase)
            .Replace("{achievement}", AnnouncementText.OrMissing(badgeName), StringComparison.OrdinalIgnoreCase)
            // {badge} is the old name for {achievement} - still filled in so
            // a line a club wrote before the rename is spoken right (17 Oct 2026).
            .Replace("{badge}", AnnouncementText.OrMissing(badgeName), StringComparison.OrdinalIgnoreCase)
            .WithDriverDetails(driverProfile)
            .TidyTemplate());

        if (text.Length == 0)
        {
            return;
        }

        var speed = config.SpeedFor(_voiceStore.Current.Speed);

        // A sponsor who presents the milestones gets their name on it.
        var (sponsorTag, countMention, releaseMention) = SponsorTag(type);
        text = SponsoredMoments.WithCredit(text, sponsorTag);

        // Screen text raised by EnqueueVoiceWork, once the line is accepted.
        //
        // 20 Oct 2026: a line the queue turns down - or keeps and then
        // loses - gives the sponsor's allowance back, or the sponsor went
        // unnamed on milestones for three minutes over a line nobody heard.
        var accepted = EnqueueVoiceWork(type, transponderId,
            async () =>
            {
                // WHY (23 Oct 2026, bug hunt 10): counted only once the line is
                // certain to be heard (SynthesizeAndPlayAsync's spoken); a line
                // dropped for no voice, a flag, the countdown or a mute - or
                // whose synthesis threw - gives the reservation back instead.
                var heard = false;

                try
                {
                    await SynthesizeAndPlayAsync(text, $"[voice] {type}", speedOverride: speed,
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
                        releaseMention?.Invoke();
                    }
                }
            },
            displayText: text,
            lost: releaseMention,
            renderAhead: () => RenderLineAheadAsync(text, type, speed));

        if (!accepted)
        {
            releaseMention?.Invoke();
        }
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

        var saidAs = profile is not null && !string.IsNullOrWhiteSpace(profile.SpokenName)
            ? profile.SpokenName
            : transponder;

        // WHY (23 Oct 2026, bug hunt 10): a car with no announcer name is said by
        // its number, which must stay in the room - noted, so listen-along
        // and the public captions leave the line out (the podium videos did
        // too, until 28 Oct 2026, when they stopped going online). See
        // NoteSpokenAsNumber.
        NoteSpokenAsNumber(transponder, saidAs);

        return saidAs;
    }

    public void NotifyRaceStarted()
    {
        StatusMessage?.Invoke($"[audio-trace] NotifyRaceStarted (muted={Muted})");
        EndPostRaceQuiet("a new race started");

        // An AI line written about the last race is not about this one - see
        // AnnouncementService.Freshness.
        NoteNewRaceForFacts();

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

        // Time's up, so the next car over the line wins it: the winner's
        // call is made now for the top two (10 Nov 2026) - see Winner.
        PrepareTheWinnerCalls();
    }

    public void NotifyRaceCompleted()
    {
        StatusMessage?.Invoke($"[audio-trace] NotifyRaceCompleted (muted={Muted})");
        CancelCountdownTicks();

        // How quickly the race's calls reached the room (10 Nov 2026) - the
        // lines said while it was racing, before the finish call and the
        // wrap-up join them. See VoiceTimingTally.
        if (Timings.Summary(Load.Offered, Load.Dropped, Load.Held) is { } timings)
        {
            StatusMessage?.Invoke(timings);
        }

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
            // WHY (9 Nov 2026, bug hunt 15): the decks are cleared at the
            // checkered only by the finish call itself, so with it off (a
            // club using the fanfare alone) whatever was already queued or
            // rendering - an AI line, a running order - was still said over
            // the finish, as PlayFlagSound found for the flags. Spares the
            // fanfare (see LeadInSoundKindFor).
            ClearTheDecksFor(AnnouncementEventType.RaceCompleted);

            hold.TrySetResult();
        }

        // The winner's name, the moment the finish call has been said (10 Nov
        // 2026) - see AnnouncementService.Winner. Only with the finish call on.
        QueueTheWinnersCall();

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

        // AND WHAT THE RACE COST THE ANNOUNCER, on the record, once per
        // race. The strip carries this live while the race is on; this is
        // the line a club reads afterwards, which is the whole reason the
        // count is sayable at all - see AnnouncerLoad.Report.
        StatusMessage?.Invoke($"[voice] {Load.Report()}");
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
        if (config is null || !CallOn(config) || config.Templates.Count == 0)
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

        // Its own sound, as the jump start has. It used to borrow the
        // invalid-lap buzzer - off by default, and a lap sound, so it was
        // also held under a yellow, dropped behind a long line as too late
        // for its lap, and cleared by a flag call.
        PlaySound(SoundEffectKind.Warning, ToneGenerator.PlayInvalidLapAsync, blackFlag ? "black flag buzzer" : "warning buzzer");
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
            && !(standing.FastLapMs > 0 && standing.FastLapMs == lapMs && IsRaceFastest(transponderId, lapMs)))
        {
            var sounds = _soundStore.Current;
            var bestCfg = sounds.Effects.GetValueOrDefault(nameof(SoundEffectKind.PersonalBest));

            // Only offered when it could be heard: it is off by default, and
            // at weight 60 a switched-off one used to take the slot of the
            // lap's Good lap chime (40) - see SoundCandidateCanPlay.
            if (SoundCandidateCanPlay(SoundEffectKind.PersonalBest, bestCfg))
            {
                ConsiderPerLapAction("sound", transponderId, bestCfg.Weight, Math.Max(1, sounds.MaxEffectsPerLap),
                    () => PlaySound(SoundEffectKind.PersonalBest, ToneGenerator.PlayPersonalBestAsync, "personal best chime"));
            }
        }

        // In a qualifier a personal best is a running-order event, not a
        // race-fastest one - everyone is chasing their own clock. So it
        // becomes a provisional-pole call when it goes to the top, or an
        // improvement otherwise, and the generic personal-best line is left
        // to real races. Checked BEFORE the personal-best config gate,
        // because those events have their own enable switches.
        if (IsQualifyingMode)
        {
            // The same lap has just been called as taking pole, from its
            // crossing (see HandleGatePassed, which the session reports
            // first). Calling it an improvement as well would be the
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

        // The race's fastest lap owns this moment if it's the same lap.
        if (standing.FastLapMs > 0 && standing.FastLapMs == lapMs && IsRaceFastest(transponderId, lapMs))
        {
            return;
        }

        // Their quickest ever on this track, not just tonight (10 Nov 2026,
        // voice speed plan, phase 4) - called as that instead, whether or not
        // the ordinary personal best is on. See BestEverHere.
        if (CallTheBestEverHere(transponderId, standing, lapMs))
        {
            return;
        }

        var config = _voiceStore.Current.Events.GetValueOrDefault(nameof(AnnouncementEventType.PersonalBestLap));
        if (config is null || !CallOn(config) || config.Templates.Count == 0)
        {
            return;
        }

        EnqueueVoice(AnnouncementEventType.PersonalBestLap, standing, null, lapMs);
    }

    /// <summary>
    /// Asked by Race Control's history (RaceControlControl.BestEverHereBefore):
    /// the driver's quickest lap ever on tonight's layout before tonight, when
    /// this lap beats it and they have not been told this race - else null.
    /// </summary>
    public Func<string, long, long?>? BestEverHereBefore { get; set; }

    /// <summary>
    /// Says the lap as the driver's best ever here, when it is one and the
    /// call is on. True when it was called, so the ordinary personal best
    /// is not said for the same lap.
    /// </summary>
    private bool CallTheBestEverHere(string transponderId, RaceStanding standing, long lapMs)
    {
        var config = _voiceStore.Current.Events.GetValueOrDefault(nameof(AnnouncementEventType.BestEverHere));

        if (config is null || !CallOn(config) || config.EffectiveTemplates.Count == 0)
        {
            return false;
        }

        long? before;

        try
        {
            before = BestEverHereBefore?.Invoke(transponderId, lapMs);
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"[voice] Couldn't check {transponderId}'s best ever here ({ex.Message}) - called as a personal best.");
            return false;
        }

        if (before is not { } previous)
        {
            return false;
        }

        var extras = new Dictionary<string, (string Spoken, string Display)>(StringComparer.OrdinalIgnoreCase)
        {
            ["previous"] = (FormatSecondsForSpeech(previous), FormatSecondsForDisplay(previous)),
        };

        EnqueueVoice(AnnouncementEventType.BestEverHere, standing, null, lapMs, extras: extras);

        return true;
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

        // The winner's call, made now for the top two (10 Nov 2026) - see Winner.
        PrepareTheWinnerCalls();
    }

    /// <summary>
    /// A timed race has one minute left - fired once by the session.
    /// <paramref name="timeLeft"/> is the race clock's time left as the call
    /// is heard; null when it cannot be told.
    /// </summary>
    public void NotifyFinalMinute(TimeSpan? timeLeft = null)
    {
        if (FinalMinuteSayByMs(TimingClock.NowMs, timeLeft) is not { } sayByMs)
        {
            return;
        }

        // Bug hunt 14 (7 Nov 2026): with a say-by time, as the other time
        // calls have had since 28 Oct. It waits its turn behind a line
        // already playing, and without one "one minute to go" could be heard
        // with twenty or thirty seconds left.
        EnqueueVoice(AnnouncementEventType.FinalMinute, null, null, sayByMs: sayByMs);
    }

    /// <summary>
    /// When "one minute to go" must be said by, or null when it is already
    /// too late to say it at all.
    ///
    /// WHY (10 Nov 2026, bug hunt 16): the say-by time ran from when the
    /// call was heard, not from the minute. A call heard twelve seconds late
    /// after a freeze still got its full fifteen, so it could be said with
    /// about thirty seconds left. It now runs from the minute itself, worked
    /// out from the time left, and a call heard more than
    /// FinalMinuteSayWithinMs after the minute is dropped, the same way
    /// TimeLeftCalls.SayWithin drops a mark it reaches late.
    /// </summary>
    internal static long? FinalMinuteSayByMs(long nowMs, TimeSpan? timeLeft)
    {
        if (timeLeft is not { } left)
        {
            return nowMs + FinalMinuteSayWithinMs;
        }

        var lateMs = 60_000 - (long)left.TotalMilliseconds;

        if (left <= TimeSpan.Zero || lateMs > FinalMinuteSayWithinMs)
        {
            return null;
        }

        return nowMs + FinalMinuteSayWithinMs - Math.Max(0, lateMs);
    }

    /// <summary>
    /// How long after the minute "one minute to go" is still worth saying.
    /// Longer than the last-minute calls' (the next one, forty-five seconds,
    /// is fifteen seconds behind it and replaces it anyway), shorter than the
    /// minutes-left calls', because a quarter of the minute gone makes it wrong.
    /// </summary>
    internal const long FinalMinuteSayWithinMs = 15_000;

    /// <summary>
    /// How long after its mark a minutes-left call is still worth saying.
    /// Said twenty seconds late, "five minutes to go" is near enough; later
    /// than that the clock has moved on from it.
    /// </summary>
    internal const long MinutesLeftSayWithinMs = 20_000;

    /// <summary>
    /// The same for a last-minute call: "thirty seconds" said at twenty-two
    /// is wrong, and the next call is only fifteen seconds behind it.
    /// </summary>
    internal const long FinalSecondsSayWithinMs = 6_000;

    /// <summary>
    /// A timed race has just reached <paramref name="secondsLeft"/> - a whole
    /// minute from two up, or 45, 30 or 15 in the last minute (28 Oct 2026,
    /// asked for by Asher). Sent by the window as the clock passes each mark
    /// (see TimeLeftCalls); one minute is NotifyFinalMinute's, and anything
    /// else is ignored.
    ///
    /// Each is a race moment, so it waits its turn rather than being dropped
    /// for a line already playing (IsWorthWaitingFor) - but a newer time
    /// call replaces one still waiting, and one that waited past the point
    /// where it was true is not said at all, rather than telling the room
    /// thirty seconds when there are twenty left.
    /// </summary>
    public void NotifyTimeLeft(int secondsLeft)
    {
        if (secondsLeft >= 120 && secondsLeft % 60 == 0)
        {
            var minutes = secondsLeft / 60;

            if (_voiceStore.Current.Events.TryGetValue(nameof(AnnouncementEventType.MinutesLeft), out var config)
                && config is not null
                && !config.SaysMinutesLeft(minutes))
            {
                StatusMessage?.Invoke($"[voice] MinutesLeft skipped - {minutes} minutes is more than the last {config.MinutesLeftFrom} it is set to call.");
                return;
            }

            EnqueueVoice(AnnouncementEventType.MinutesLeft, null, null,
                extras: new Dictionary<string, (string Spoken, string Display)>
                {
                    ["minutes"] = (RaceNumbers.Words(minutes), minutes.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                },
                sayByMs: TimingClock.NowMs + MinutesLeftSayWithinMs);
            return;
        }

        if (secondsLeft is 45 or 30 or 15)
        {
            EnqueueVoice(AnnouncementEventType.FinalSeconds, null, null,
                extras: new Dictionary<string, (string Spoken, string Display)>
                {
                    ["seconds"] = (RaceNumbers.Words(secondsLeft), secondsLeft.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                },
                sayByMs: TimingClock.NowMs + FinalSecondsSayWithinMs);
        }
    }

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
                .Select(p =>
                {
                    var name = _profiles.Get(p.Transponder!)?.SpokenName ?? p.DriverName ?? p.Transponder!;

                    // WHY (23 Oct 2026, bug hunt 10): a marshal with no name is
                    // said by number - noted, so the line never goes online.
                    NoteSpokenAsNumber(p.Transponder, name);
                    return name;
                })
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
        _qualifyingLaps.Clear();
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
    /// Reached from two places: every crossing (HandleGatePassed - under
    /// the default most-laps rule a driver goes top by completing a lap,
    /// fast or not), and a personal best, which finds the call already
    /// made for the same lap. "On top" is the class's own qualifying order
    /// (see QualifyingLeader); it used to be the quickest single lap.
    /// </summary>
    private bool CallProvisionalPoleIfTaken(string transponderId, RaceStanding standing, long? lapMs)
    {
        var field = _standings.Values.Where(s => !MutedGhost(s.Transponder));
        var top = QualifyingLeader(field, _qualifyingLaps, _qualifyingMethod, _qualifyingConsecutiveLaps);
        if (!string.Equals(top, transponderId, StringComparison.Ordinal))
        {
            return false;
        }

        var tookPole = !string.Equals(_provisionalPoleTransponder, transponderId, StringComparison.Ordinal);
        _provisionalPoleTransponder = transponderId;

        if (!tookPole)
        {
            return false;
        }

        // Weighed with the lap's other lines against the per-lap limit, as
        // the lead change it stands in for is (see HandleStandingUpdated):
        // it now comes as often as the order changes hands, and queued
        // straight in it would add a line on every one of those laps.
        var config = _voiceStore.Current.Events.GetValueOrDefault(nameof(AnnouncementEventType.ProvisionalPole));
        if (VoiceCandidateCanSpeak(AnnouncementEventType.ProvisionalPole, config))
        {
            ConsiderPerLapAction("voice", transponderId, config.Weight, MaxVoicePerLap,
                () => EnqueueVoice(AnnouncementEventType.ProvisionalPole, standing, null, lapMs));
        }

        return true;
    }

    /// <summary>
    /// Who tops a qualifier's running order, ranked the way
    /// QualifyingCalculator ranks a heat: most laps then lowest elapsed
    /// time, or under BestConsecutiveLaps the quickest run of that many
    /// flying laps, with drivers yet to complete a run behind on laps and
    /// time. Null for an empty field.
    /// </summary>
    internal static string? QualifyingLeader(IEnumerable<RaceStanding> field,
        IReadOnlyDictionary<string, List<long>> flyingLaps, QualifyingMethod method, int consecutiveLaps)
    {
        var standings = field.ToList();
        if (standings.Count == 0)
        {
            return null;
        }

        if (method == QualifyingMethod.BestConsecutiveLaps)
        {
            var n = Math.Max(1, consecutiveLaps);
            // 20 Oct 2026: a penalty given during the race is added to the
            // driver's run, as QualifyingCalculator does, so a penalized
            // driver is no longer called on provisional pole. Only a driver
            // with a run is charged; a negative penalty is ignored.
            var bestRun = standings
                .Select(s =>
                {
                    var run = flyingLaps.TryGetValue(s.Transponder, out var laps)
                        ? QualifyingCalculator.BestConsecutiveWindow(laps, n).TotalMs
                        : 0L;
                    return (Standing: s, RunMs: run > 0 ? run + Math.Max(0, s.PenaltyMs) : 0L);
                })
                .Where(x => x.RunMs > 0)
                .OrderBy(x => x.RunMs)
                .Select(x => x.Standing)
                .FirstOrDefault();

            if (bestRun is not null)
            {
                return bestRun.Transponder;
            }
        }

        return standings
            .OrderByDescending(s => s.Laps)
            .ThenBy(s => s.ElapsedMs)
            .First()
            .Transponder;
    }

    /// <summary>
    /// True when this driver's lap is the race's fastest: no other tracked
    /// racer has gone quicker. While real cars are racing, ghosts are left
    /// out on both sides, as the session leaves them out of the race's
    /// fastest lap - so a quicker ghost no longer made a real driver's
    /// race-fastest lap speak as a personal best instead, which then
    /// drowned out the fastest-lap call. A field of nothing but ghosts
    /// still counts them all.
    /// </summary>
    private bool IsRaceFastest(string transponderId, long lapMs)
    {
        var realCarsRacing = _standings.Values.Any(s => !GhostService.IsGhost(s.Transponder));

        if (realCarsRacing && GhostService.IsGhost(transponderId))
        {
            return false;
        }

        return _standings.Values
            .Where(s => !(realCarsRacing && GhostService.IsGhost(s.Transponder)))
            .All(s => s.FastLapMs <= 0 || s.FastLapMs >= lapMs);
    }

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
