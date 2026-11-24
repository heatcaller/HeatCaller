using NAudio.Wave;

namespace HeatCaller;

/// <summary>
/// Sound effects: playing them, after speech or on their own; stopping
/// them; lead-in sounds before a call; and the sound queue.
///
/// Moved here unchanged from AnnouncementService.cs on 26 Sep 2026, when the
/// file was split into parts. Fields with initializers stayed there.
/// </summary>
public sealed partial class AnnouncementService
{
    /// <summary>
    /// Plays a sound AFTER the voice queue goes quiet, so an announcement
    /// and its cue don't talk over each other.
    ///
    /// Waiting is necessary rather than just reordering the two calls:
    /// sounds play instantly from a local file while speech has to be
    /// synthesised over the network first, so a sound queued second still
    /// lands a second before the voice does.
    ///
    /// Gives up after a few seconds. If speech is stuck or the queue is
    /// backed up, a late cue is worse than a missing one - by then the
    /// countdown it belongs to has moved on.
    /// </summary>
    private void PlaySoundAfterSpeech(SoundEffectKind kind, Func<float, CancellationToken, Task> playGenerated, string label)
    {
        if (Muted)
        {
            return;
        }

        // WHY (24 Oct 2026, timing bug hunt): checked here, on the stale
        // lap's own thread - the sound itself is played later from the pool,
        // outside the stale scope, so a track-record fanfare used to play on
        // its own for a lap the room never heard called.
        if (SkippedAsStale())
        {
            return;
        }

        // Only counted as pending if the sound will ACTUALLY play. Your
        // trace showed the staging cue reported as pending for 8.5 seconds
        // before being skipped as "disabled for races" - the gate dutifully
        // waited for audio that was never going to arrive.
        var config = _soundStore.Current.Effects.GetValueOrDefault(kind.ToString());
        if (config is null || !config.IsEnabledFor(IsPracticeMode))
        {
            StatusMessage?.Invoke($"[sound] Skipped {label} - disabled for {(IsPracticeMode ? "practice" : "races")}.");
            return;
        }

        // Counted as pending from the moment it's SCHEDULED, not from when
        // it starts playing. Without this a deferred cue is invisible to
        // IsAudioPending during its wait, so anything gating on audio - the
        // green flag especially - sees silence and goes early.
        Interlocked.Increment(ref _deferredSoundsPending);

        // A countdown starting while this waits cuts the line it was waiting
        // to follow - and this cue with it (see CutForCountdown). Without
        // the check, the cut line ending looked like the cue's moment, and
        // a staging sound would have played over the countdown.
        var countdownGeneration = Volatile.Read(ref _countdownCutGeneration);

        _ = Task.Run(async () =>
        {
            try
            {
                var waitedMs = 0;

                // Let the voice line actually start before watching for it
                // to end - synthesis takes a moment, and checking straight
                // away would see a queue that hasn't begun speaking yet.
                await Task.Delay(250);

                // Volatile.Read to match how this counter is read elsewhere -
                // this runs on a pool thread, while the counter is changed by
                // the voice queue's loop (which runs on the UI thread) and by
                // flag calls on pool threads.
                while ((_isVoiceSpeaking || Volatile.Read(ref _voiceInFlight) > 0) && waitedMs < 6000)
                {
                    await Task.Delay(100);
                    waitedMs += 100;
                }

                // A short beat after the words, so the cue reads as a
                // follow-up rather than being clipped onto the end.
                await Task.Delay(180);

                if (Volatile.Read(ref _countdownCutGeneration) != countdownGeneration)
                {
                    StatusMessage?.Invoke($"[sound] Skipped {label} - the countdown started before it played.");
                    return;
                }

                PlaySound(kind, playGenerated, label);
            }
            catch
            {
                // A cue failing to play is never worth surfacing.
            }
            finally
            {
                // Released once PlaySound has been called - from there the
                // normal sound counters take over the tracking.
                Interlocked.Decrement(ref _deferredSoundsPending);
            }
        });
    }

    /// <summary>
    /// Plays a sound effect for the given kind, if enabled in settings. Uses
    /// the user's custom audio file if they've set one, otherwise falls back
    /// to the built-in generated tone passed in.
    ///
    /// Always logs why it did or didn't play - a silent no-op here (the
    /// original behavior) is indistinguishable from a real bug from the
    /// outside, since "disabled in settings" and "some other failure" both
    /// produce zero console output either way.
    /// </summary>
    /// <param name="playGenerated">The built-in tone, given the volume and a token that cuts it off.</param>
    /// <param name="finished">
    /// Signalled when the sound is over - played, cut off, skipped or
    /// discarded from the queue. A call that must follow it waits on this.
    /// </param>
    /// <param name="streakCount">For the streak sound only: how long the run is, so a recorded streak sound can climb with it the way the generated one does.</param>
    private void PlaySound(SoundEffectKind kind, Func<float, CancellationToken, Task> playGenerated, string label, TaskCompletionSource? finished = null, int streakCount = 0)
    {
        StatusMessage?.Invoke($"[audio-trace] PlaySound requested: {kind} (muted={Muted})");

        if (Muted)
        {
            finished?.TrySetResult();
            return;
        }

        // A sound for a lap the window was told of too late (23 Oct 2026,
        // timing layer, P4b) - see WhileLapCallsAreStale.
        if (SkippedAsStale())
        {
            finished?.TrySetResult();
            return;
        }

        var sounds = _soundStore.Current;
        // A null entry counts as none: a hand-edited or restored settings
        // file (or a soundscape applied from one) can say "GoodLap": null,
        // and reading it below then threw on every lap.
        if (!sounds.Effects.TryGetValue(kind.ToString(), out var config) || config is null)
        {
            StatusMessage?.Invoke($"[sound] Skipped {label} - no configuration entry found for {kind}.");
            finished?.TrySetResult();
            return;
        }

        if (!config.IsEnabledFor(IsPracticeMode))
        {
            StatusMessage?.Invoke($"[sound] Skipped {label} - disabled for {(IsPracticeMode ? "practice" : "races")} on Sounds \u2192 Effects.");
            finished?.TrySetResult();
            return;
        }

        // Lap chimes and streak stings wait for green. The lifecycle sounds -
        // staging, countdown, start, finish - are part of the flag sequence
        // itself and always play.
        if (IsUnderCaution && SoundEffectsProfile.PerLapKinds.Contains(kind) && !config.AllowUnderCaution)
        {
            StatusMessage?.Invoke($"[sound] Held {label} - a flag is out.");
            finished?.TrySetResult();
            return;
        }

        var volume = (float)sounds.Volume;
        var queuedAtUtc = DateTime.UtcNow;

        // Decided as it is queued, from the Sound Effects page's switch - see
        // SoundOverlap.
        var atOnce = SoundOverlap.PlaysAtOnce(kind, sounds.EffectsOverlap);

        // "finished" rides with the queued item rather than being set here,
        // so it is signalled however the sound leaves the queue - see
        // QueuedSound.
        EnqueueSoundWork(kind, finished, atOnce, async () =>
        {
            // Registered from the moment this sound becomes the current one,
            // before it waits for the speaker, so a flag or a mute landing
            // during that wait stops it too.
            using var cancel = new CancellationTokenSource();
            var stagger = BeginPlayingSound(kind, cancel, atOnce);

            try
            {
                // A second of the same sound in the same instant - two cars
                // on a Good lap - starts a hair after the first, so the room
                // hears two chimes rather than one loud one. See
                // SoundOverlap.SameSoundStagger.
                if (stagger > TimeSpan.Zero)
                {
                    try
                    {
                        await Task.Delay(stagger, cancel.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        // Cut while it waited; dropped just below.
                    }
                }

                // A lap chime no longer waits for the voice (10 Nov 2026,
                // voice speed plan, phase 1). It took the same lock as the
                // spoken lines, so a chime that began while a call was being
                // rendered held that call back when it was ready - 0.8 s a
                // call in the baseline race, as long as the render itself -
                // and a chime that came during a line waited the whole line
                // and was then dropped as too late (203 in one race). The
                // lock was there so two outputs never started at once; there
                // is one output now, the bus, which mixes. So a lap chime
                // plays on its lap, under the voice, a little quieter. The
                // flag-sequence sounds still take the lock: the voice is
                // timed around them (WaitForLeadInSoundAsync).
                var underTheVoice = IsPerLapSound(kind);
                var playAt = underTheVoice && VoiceLineIsPlaying ? volume * ChimeUnderVoiceGain : volume;

                StatusMessage?.Invoke($"[sound] Playing {label} (volume={playAt:0.00}{(playAt < volume ? ", under the voice" : "")})...");

                if (!underTheVoice)
                {
                    await _audioPlaybackLock.WaitAsync();
                }

                // The finish fanfare gives the speaker up for its last
                // seconds (11 Nov 2026, asked for by Asher): the finish call
                // comes in over its tail, which is lowered under it, rather
                // than after it - see FanfareTalkOver. Once only, whichever
                // comes first: the tail or the end.
                var lockHeld = !underTheVoice ? 1 : 0;

                void GiveUpTheSpeaker()
                {
                    if (Interlocked.Exchange(ref lockHeld, 0) == 1)
                    {
                        _audioPlaybackLock.Release();
                    }
                }

                var talkOver = kind == SoundEffectKind.RaceCompleted && finished is not null
                    ? new TalkOver(FanfareTalkOver, FanfareDuckTo, FanfareTalkOverShortest, () =>
                    {
                        StatusMessage?.Invoke($"[sound] The finish call comes in over the last {FanfareTalkOver.TotalSeconds:0.0}s of the fanfare, which is lowered under it.");
                        GiveUpTheSpeaker();
                        finished.TrySetResult();
                    })
                    : null;

                try
                {
                    // Checked again once the speaker is free: the wait for the
                    // lock can itself be a whole spoken line long, and a lap
                    // chime that lands after that is feedback for the wrong lap.
                    if (IsStaleSound(kind, queuedAtUtc))
                    {
                        StatusMessage?.Invoke($"[sound] Dropped {label} - it waited too long behind the voice to still match its lap.");
                        return;
                    }

                    // Cut off by a flag call, or muted, while it waited.
                    if (cancel.IsCancellationRequested || Muted)
                    {
                        StatusMessage?.Invoke($"[sound] Dropped {label} - {(Muted ? "the announcer was muted" : "a flag call cut it off")} before it played.");
                        return;
                    }

                    // Quieter for each other sound it joins (24 Nov 2026) -
                    // see SoundOverlap.StackGain. Asked now, as it starts,
                    // since that is when the others are or aren't playing.
                    // Only ever below 1 when sounds overlap: with the switch
                    // off they never play at once.
                    var stackGain = SoundOverlap.StackGain(OtherSoundsPlaying(cancel));

                    if (stackGain < 1f)
                    {
                        playAt *= stackGain;
                        StatusMessage?.Invoke($"[sound] {label} joins {Plural.Of(OtherSoundsPlaying(cancel), "other sound")}, at {playAt:0.00}.");
                    }

                    // The club's own file, else the effect's sound pack, else
                    // the generated tone - see SoundPacks.ResolveFile.
                    var file = SoundPacks.ResolveFile(config, kind);
                    if (file is not null)
                    {
                        var semitones = kind == SoundEffectKind.Streak
                            ? SoundPacks.StreakSemitones(streakCount, _soundStore.Current.StreakThresholdFor(IsPracticeMode))
                            : 0;

                        // The same wake-up pad a spoken line gets on this
                        // output - files had none, and a short chime from a
                        // file could vanish on a wireless speaker.
                        await ToneGenerator.PlayFileAsync(file, playAt, cancel.Token, CurrentLeadingSilence, semitones, talkOver);
                    }
                    else
                    {
                        await playGenerated(playAt, cancel.Token);
                    }

                    if (cancel.IsCancellationRequested)
                    {
                        StatusMessage?.Invoke($"[sound] Cut off {label} part-way.");
                    }
                }
                finally
                {
                    // Before "finished" is signalled (by the queue wrapper,
                    // once this returns), so a line waiting on this sound
                    // (see WaitForLeadInSoundAsync) starts cleanly after it.
                    GiveUpTheSpeaker();
                }
            }
            finally
            {
                EndPlayingSound(cancel);
            }
        });
    }

    /// <summary>
    /// Adds a sound to the ones playing, so it can be cut off. Returns how
    /// long to hold it first so it starts SoundOverlap.SameSoundStagger after
    /// the last of its kind - only for a sound that plays at once with
    /// others; one in the line already follows the one before.
    /// </summary>
    private TimeSpan BeginPlayingSound(SoundEffectKind kind, CancellationTokenSource cancel, bool atOnce = false)
    {
        lock (_playingSoundLock)
        {
            _playingSounds.Add(new PlayingSound(kind, cancel));

            if (!atOnce)
            {
                return TimeSpan.Zero;
            }

            var now = DateTime.UtcNow;
            var stagger = SoundOverlap.StaggerFor(_lastSoundStartUtc.TryGetValue(kind, out var last) ? last : null, now);

            // Booked now, so a third of the same sound in the same instant
            // goes after the second rather than beside it.
            _lastSoundStartUtc[kind] = now + stagger;

            return stagger;
        }
    }

    /// <summary>
    /// Takes a sound off the ones playing. Done before its token is disposed,
    /// and under the same lock StopPlayingSound cancels under, so a stop can
    /// never reach a disposed token.
    /// </summary>
    private void EndPlayingSound(CancellationTokenSource cancel)
    {
        lock (_playingSoundLock)
        {
            _playingSounds.RemoveAll(playing => ReferenceEquals(playing.Cancel, cancel));
        }
    }

    /// <summary>How many sounds are playing besides this one - for SoundOverlap.StackGain.</summary>
    private int OtherSoundsPlaying(CancellationTokenSource self)
    {
        lock (_playingSoundLock)
        {
            return _playingSounds.Count(playing => !ReferenceEquals(playing.Cancel, self) && !playing.Cancel.IsCancellationRequested);
        }
    }

    /// <summary>How many sound effects are playing (or waiting for the speaker) right now. For the tests.</summary>
    internal int SoundsPlayingForTests
    {
        get
        {
            lock (_playingSoundLock)
            {
                return _playingSounds.Count;
            }
        }
    }

    /// <summary>
    /// Cuts off the sound effects playing (or waiting for the speaker) right
    /// now - every one that matches, since several can play at once (24 Nov
    /// 2026). Returns true if there was one to cut.
    /// </summary>
    /// <param name="except">Leave a sound of this kind playing - a call's own lead-in sound.</param>
    /// <param name="only">Cut only the sounds of this kind.</param>
    private bool StopPlayingSound(SoundEffectKind? except = null, SoundEffectKind? only = null)
    {
        lock (_playingSoundLock)
        {
            var cut = false;

            foreach (var playing in _playingSounds)
            {
                if (playing.Cancel.IsCancellationRequested
                    || (except is { } spared && playing.Kind == spared)
                    || (only is { } wanted && playing.Kind != wanted))
                {
                    continue;
                }

                // Cancelling stops the output (ToneGenerator's registration).
                // Its PlaybackStopped handler only completes a task, so nothing
                // calls back into code that takes this lock.
                playing.Cancel.Cancel();
                cut = true;
            }

            return cut;
        }
    }

    /// <summary>
    /// Takes queued sounds out of the queue, signalling each one's
    /// "finished" so nothing waits on a sound that will now never play.
    /// Call with _soundQueueLock held. Returns how many were removed.
    /// </summary>
    private int RemoveQueuedSounds(Predicate<QueuedSound> match)
    {
        // Both lanes (24 Nov 2026): a flag clearing the lap chimes, or a
        // countdown clearing the decks, means the ones waiting to play at
        // once too.
        var removed = _soundQueue.FindAll(match);
        removed.AddRange(_momentQueue.FindAll(match));

        if (removed.Count == 0)
        {
            return 0;
        }

        _soundQueue.RemoveAll(match);
        _momentQueue.RemoveAll(match);

        foreach (var item in removed)
        {
            item.Finished?.TrySetResult();
        }

        return removed.Count;
    }

    /// <summary>How much of the end of the finish fanfare the finish call talks over (11 Nov 2026).</summary>
    internal static readonly TimeSpan FanfareTalkOver = TimeSpan.FromSeconds(1.5);

    /// <summary>What the fanfare is lowered to under the finish call - a little, not out.</summary>
    internal const float FanfareDuckTo = 0.4f;

    /// <summary>A fanfare shorter than this (the built-in tones, a short sting) is heard out in full first, as always.</summary>
    internal static readonly TimeSpan FanfareTalkOverShortest = TimeSpan.FromSeconds(3);

    /// <summary>Completes when the race-completed sound has played (or was skipped). See NotifyRaceCompleted.</summary>
    private volatile Task _finishSoundDone = Task.CompletedTask;

    /// <summary>Completes when the race-started horn has played (or was skipped). See NotifyRaceStarted.</summary>
    private volatile Task _startSoundDone = Task.CompletedTask;

    /// <summary>Completes when the time-up buzzer has played (or was skipped). See NotifyRaceTimeOver.</summary>
    private volatile Task _timeOverSoundDone = Task.CompletedTask;

    /// <summary>
    /// The longest a finish call will wait for the finish sound. The sound is
    /// signalled however it ends now, so this is only a safety net - and past
    /// it the fanfare is left to finish; the call simply takes the speaker
    /// next. The finish is not a moment where a second or two matters.
    /// </summary>
    private static readonly TimeSpan LeadInSoundMaxWait = TimeSpan.FromSeconds(8);

    /// <summary>
    /// The longest the start and time-up calls wait for their sound. The
    /// built-in horn and buzzer are about a second and a half; a custom file
    /// can be anything, and the green is the one call where dead air is
    /// most obvious - so past this the sound is cut and the call speaks.
    /// </summary>
    private static readonly TimeSpan QuickLeadInMaxWait = TimeSpan.FromSeconds(3);

    /// <summary>The sound a race-control call follows, or null for a call that has none.</summary>
    private static SoundEffectKind? LeadInSoundKindFor(AnnouncementEventType type) => type switch
    {
        AnnouncementEventType.RaceStarted => SoundEffectKind.RaceStarted,
        AnnouncementEventType.RaceTimeOver => SoundEffectKind.RaceTimeOver,
        AnnouncementEventType.RaceCompleted => SoundEffectKind.RaceCompleted,
        AnnouncementEventType.YellowFlag => SoundEffectKind.YellowFlag,
        AnnouncementEventType.RedFlag => SoundEffectKind.RedFlag,
        AnnouncementEventType.GreenFlagResumed => SoundEffectKind.GreenFlagResumed,
        _ => null,
    };

    /// <summary>
    /// Holds a race-control call until its own sound has played, so the
    /// sound comes first and the voice after it: the start horn before the
    /// green call, the time-up buzzer before "time's up", the fanfare before
    /// the finish call. Every other line goes straight through.
    /// </summary>
    private async Task WaitForLeadInSoundAsync(AnnouncementEventType type)
    {
        Task sound;
        TimeSpan maxWait;

        switch (type)
        {
            case AnnouncementEventType.RaceCompleted:
                sound = _finishSoundDone;
                maxWait = LeadInSoundMaxWait;
                break;
            case AnnouncementEventType.RaceStarted:
                sound = _startSoundDone;
                maxWait = QuickLeadInMaxWait;
                break;
            case AnnouncementEventType.RaceTimeOver:
                sound = _timeOverSoundDone;
                maxWait = QuickLeadInMaxWait;
                break;

            // A flag's sound, then its call - capped short like the start,
            // since a yellow is the last thing that should wait.
            case AnnouncementEventType.YellowFlag:
            case AnnouncementEventType.RedFlag:
            case AnnouncementEventType.GreenFlagResumed:
                sound = _flagSoundDone;
                maxWait = QuickLeadInMaxWait;
                break;
            default:
                return;
        }

        if (sound.IsCompleted)
        {
            return;
        }

        var first = await Task.WhenAny(sound, Task.Delay(maxWait));

        if (first == sound || type == AnnouncementEventType.RaceCompleted)
        {
            return;
        }

        // Still going (a long custom file) or still waiting for the speaker:
        // the start and time-up calls can't wait any longer. Cut it if it is
        // playing and drop it if it hasn't started, or it would only take
        // the speaker back from the call.
        if (LeadInSoundKindFor(type) is { } kind)
        {
            var cut = StopPlayingSound(only: kind);
            int dropped;

            lock (_soundQueueLock)
            {
                dropped = RemoveQueuedSounds(item => item.Kind == kind);
            }

            StatusMessage?.Invoke(
                $"[sound] {kind} sound {(cut ? "cut short" : dropped > 0 ? "dropped" : "still pending")} after {maxWait.TotalSeconds:0} s so the {type} call isn't held up.");
        }
    }

    /// <summary>True for the lap-by-lap chimes that may be dropped when late; false for the race's one-off sounds.</summary>
    private static bool IsPerLapSound(SoundEffectKind kind) => SoundEffectsProfile.PerLapKinds.Contains(kind);

    /// <summary>True when a lap chime has waited too long to still mean anything.</summary>
    private static bool IsStaleSound(SoundEffectKind kind, DateTime queuedAtUtc) =>
        IsPerLapSound(kind) && DateTime.UtcNow - queuedAtUtc > PerLapSoundMaxAge;

    /// <param name="atOnce">
    /// Into the moment lane, to play beside others as it fires (24 Nov 2026,
    /// see SoundOverlap); false for the one-at-a-time line.
    /// </param>
    private void EnqueueSoundWork(SoundEffectKind kind, TaskCompletionSource? finished, bool atOnce, Func<Task> work)
    {
        var dropped = 0;

        lock (_soundQueueLock)
        {
            var lane = atOnce ? _momentQueue : _soundQueue;

            if (IsPerLapSound(kind))
            {
                // Make room by dropping the OLDEST waiting lap chimes, never
                // a start/finish sound. In the moment lane a sound with a free
                // place to play in is not waiting, only not yet picked up -
                // a burst of four from one moment lands here before the loop
                // takes any, and counting them all dropped the first chime
                // of the four. So the free places are added to the limit.
                var limit = MaxQueuedPerLapSounds + (atOnce ? _momentSlots.CurrentCount : 0);

                while (lane.Count(item => IsPerLapSound(item.Kind)) >= limit)
                {
                    var oldest = lane.FindIndex(item => IsPerLapSound(item.Kind));
                    lane[oldest].Finished?.TrySetResult();
                    lane.RemoveAt(oldest);
                    dropped++;
                }
            }

            lane.Add(new QueuedSound(kind, DateTime.UtcNow, async () =>
            {
                try
                {
                    await work();
                }
                catch (Exception ex)
                {
                    StatusMessage?.Invoke($"[sound] Playback failed: {ex.Message}");
                }
                finally
                {
                    // Played, cut off or failed - either way it is over.
                    finished?.TrySetResult();
                }
            }, finished));
        }

        if (dropped > 0)
        {
            StatusMessage?.Invoke($"[sound] Dropped {Plural.Of(dropped, "waiting lap chime")} - they were piling up faster than they could play.");
        }

        (atOnce ? _momentQueueSignal : _soundQueueSignal).Release();
    }

    /// <summary>
    /// Takes the next sound off a lane, or null - dropping it instead, and
    /// signalling it finished, when the announcer has been muted since it
    /// was queued or it is a lap chime too late to mean anything.
    /// </summary>
    private QueuedSound? TakeNextSound(List<QueuedSound> lane)
    {
        QueuedSound? item = null;
        lock (_soundQueueLock)
        {
            if (lane.Count > 0)
            {
                item = lane[0];
                lane.RemoveAt(0);
            }
        }

        if (item is null)
        {
            return null;
        }

        // Mute can land between an item being queued and it being
        // played, so check again here rather than trusting the
        // enqueue-time guard alone. Signalled as finished, so a call
        // waiting on this sound isn't left to wait out its timeout.
        if (Muted)
        {
            item.Finished?.TrySetResult();
            return null;
        }

        // A lap chime that has been waiting behind the voice for more
        // than a few seconds belongs to a lap that is long gone.
        if (IsStaleSound(item.Kind, item.QueuedAtUtc))
        {
            StatusMessage?.Invoke($"[sound] Dropped a late {item.Kind} sound - it waited too long to still match its lap.");
            item.Finished?.TrySetResult();
            return null;
        }

        return item;
    }

    /// <summary>Plays one sound, counted as in flight while it does - see IsAudioPending.</summary>
    private async Task PlayQueuedAsync(QueuedSound item)
    {
        Interlocked.Increment(ref _soundInFlight);
        try
        {
            await item.Work();
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"[sound queue] Unexpected playback error: {ex.Message}");
        }
        finally
        {
            Interlocked.Decrement(ref _soundInFlight);
        }
    }

    /// <summary>
    /// Independent sound-effect playback loop - see the class-level remarks
    /// on why this is separate from voice. One at a time: the race sequence
    /// sounds always, and every sound when the Sound Effects page's overlap
    /// switch is off.
    /// </summary>
    private async Task RunSoundQueueLoopAsync()
    {
        while (true)
        {
            await _soundQueueSignal.WaitAsync();

            if (TakeNextSound(_soundQueue) is { } item)
            {
                await PlayQueuedAsync(item);
            }
        }
    }

    /// <summary>
    /// The moment lane's loop (24 Nov 2026, asked for by Asher): starts each
    /// sound as it comes and does not wait for it to end, so two cars on a
    /// Good lap are both heard as they cross. Up to SoundOverlap.MaxAtOnce
    /// play together; the next waits here for one of them to end, and is
    /// dropped as late by TakeNextSound if that takes too long.
    /// </summary>
    private async Task RunMomentQueueLoopAsync()
    {
        while (true)
        {
            await _momentQueueSignal.WaitAsync();
            await _momentSlots.WaitAsync();

            var item = TakeNextSound(_momentQueue);

            if (item is null)
            {
                _momentSlots.Release();
                continue;
            }

            _ = PlayMomentAsync(item);
        }
    }

    private async Task PlayMomentAsync(QueuedSound item)
    {
        try
        {
            await PlayQueuedAsync(item);
        }
        finally
        {
            _momentSlots.Release();
        }
    }
}
