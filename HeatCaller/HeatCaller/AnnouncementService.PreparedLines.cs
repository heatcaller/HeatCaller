using NAudio.Wave;

namespace HeatCaller;

/// <summary>
/// Lines prepared ahead of time: the flag calls, and planning and rendering
/// the lines a race night will need.
///
/// Moved here unchanged from AnnouncementService.cs on 26 Sep 2026, when the
/// file was split into parts. Fields with initializers stayed there.
/// </summary>
public sealed partial class AnnouncementService
{
    /// <summary>
    /// A flag line already turned into audio, waiting to be played.
    ///
    /// Synthesis is a network round trip - a second or so on a good
    /// connection, longer on a club's wifi - and that second is the whole
    /// delay between pressing yellow and hearing it. Rendering one line per
    /// flag ahead of time turns the press into playback of bytes already in
    /// memory.
    /// </summary>
    /// <remarks>
    /// Source says who spoke it: a take the Windows voice made standing in is
    /// not played once ElevenLabs can speak again (see PreparedTakeMatches),
    /// and only an ElevenLabs take is ever forgotten from the saved-voice
    /// library when it won't play.
    /// </remarks>
    private sealed record PreparedLine(string Text, byte[] Audio, string Signature, LineSource Source);

    private readonly Dictionary<AnnouncementEventType, PreparedLine> _flagLines = new();
    private readonly object _flagLinesLock = new();

    /// <summary>The flag calls worth rendering in advance - the ones behind a button someone presses.</summary>
    private static readonly AnnouncementEventType[] PreparedFlagTypes =
    {
        AnnouncementEventType.RaceStarted,
        AnnouncementEventType.GreenFlagResumed,
        AnnouncementEventType.YellowFlag,
        AnnouncementEventType.RedFlag,
        AnnouncementEventType.RaceCompleted,
    };

    /// <summary>
    /// Identifies the voice a prepared line was rendered with, so a change
    /// of voice, model or delivery throws the prepared audio away rather
    /// than speaking a flag in last week's voice.
    ///
    /// The engine and the Windows voice are part of it: switching between
    /// ElevenLabs and the Windows voice, or picking another Windows voice,
    /// renders every prepared line again (an ElevenLabs line comes straight
    /// back from the saved-voice library, so that costs nothing). Whether a
    /// take was the stand-in's travels with the take - see PreparedTakeMatches.
    /// </summary>
    private static string VoiceSignature(VoiceProfile profile, AnnouncementEventConfig config) =>
        string.Join("|",
            profile.EffectiveEngine, profile.WindowsVoiceName,
            profile.HeatCallerVoice, profile.HeatCallerRev,
            profile.VoiceId, profile.ModelId, profile.Stability, profile.SimilarityBoost,
            profile.Style, profile.Speed, profile.SpeakerBoost, profile.TextNormalization,
            profile.LanguageCode, profile.OutputFormat, config.SpeedFor(profile.Speed),
            string.Join("~", config.EffectiveTemplates));

    /// <summary>
    /// Renders one line per flag in the background so the next press plays
    /// instantly. Safe to call repeatedly - a line that is still current is
    /// left alone.
    /// </summary>
    public void PrimeFlagLines()
    {
        // One after another, in a single background pass.
        //
        // This used to start a render per flag at the same instant - five
        // requests in parallel, on top of whatever the announcer was
        // actually trying to SAY at that moment. Staging is exactly that
        // moment, which is how a staging call ended up being rejected by a
        // concurrency limit that the lines being prepared for later had
        // already filled.
        //
        // Nothing here is urgent: these are for a button that hasn't been
        // pressed yet, so they can take their turn.
        _ = Task.Run(async () =>
        {
            foreach (var type in PreparedFlagTypes)
            {
                await PrimeFlagLineAsync(type);
            }
        });
    }

    /// <summary>Fire-and-forget wrapper, for re-rendering one flag after it fires.</summary>
    private void PrimeFlagLine(AnnouncementEventType type) => _ = PrimeFlagLineAsync(type);

    private async Task PrimeFlagLineAsync(AnnouncementEventType type)
    {
        var profile = _voiceStore.Current;

        // Through the same rules as a live line, so a flag is ready to fire
        // even with no ElevenLabs key or no internet - the stand-in renders
        // it. This used to need a key and a voice, so without them every
        // flag call went out a network round trip late, or not at all.
        if (!CanSpeak(profile))
        {
            return;
        }

        var config = profile.Events.GetValueOrDefault(type.ToString());

        if (config is null || !config.IsEnabledFor(IsPracticeMode) || config.EffectiveTemplates.Count == 0)
        {
            return;
        }

        var signature = VoiceSignature(profile, config);

        lock (_flagLinesLock)
        {
            // A stand-in take stops counting once ElevenLabs can speak again,
            // so staging re-renders it in ElevenLabs.
            if (_flagLines.TryGetValue(type, out var existing)
                && PreparedTakeMatches(existing.Signature, existing.Source, signature, profile))
            {
                return;
            }
        }

        // Only lines that are fixed text. A placeholder can only be filled
        // in at the moment it fires, so those still synthesise live.
        var fixedTemplates = config.EffectiveTemplates.Where(t => !t.Contains('{')).ToList();

        if (fixedTemplates.Count == 0)
        {
            return;
        }

        var template = fixedTemplates[TemplatePicker.Next(fixedTemplates.Count)];

        // Tidied exactly as EnqueueVoice tidies the same line when it has to
        // speak it live, so the two are one request and share one saved copy.
        // (It was only trimmed here - identical for every built-in line, but a
        // hand-typed "Yellow!!" was a different request from the live one.)
        var text = PreparedFlagText(profile, template);

        if (text.Length == 0)
        {
            return;
        }

        var speed = config.SpeedFor(profile.Speed);
        var renderProfile = profile.CloneForDelivery(profile.Stability, profile.Style, speed);

        try
        {
            var rendered = await RenderLineAsync(renderProfile, text);

            lock (_flagLinesLock)
            {
                _flagLines[type] = new PreparedLine(text, rendered.Audio, signature, rendered.Source);
            }

            StatusMessage?.Invoke(rendered.Source == LineSource.StandIn
                ? $"[voice] {type} line ready to fire instantly, in the Windows voice (standing in for ElevenLabs)."
                : $"[voice] {type} line ready to fire instantly.");
        }
        catch (Exception ex)
        {
            // Nothing is lost - the flag still works, it just has to
            // synthesise at the moment it's pressed.
            StatusMessage?.Invoke($"[voice] Couldn't prepare the {type} line in advance ({ex.Message}). It will still speak, just a moment later.");
        }
    }

    /// <summary>The words a fixed line is spoken as - the same tidy EnqueueVoice gives it, and the same model rule.</summary>
    private static string PreparedFlagText(VoiceProfile profile, string template) =>
        profile.SanitiseForModel(template.TidyTemplate());

    /// <summary>The settings a line of this announcement is rendered with: the profile, at the announcement's own speed.</summary>
    private static VoiceProfile DeliveryProfileFor(VoiceProfile profile, AnnouncementEventConfig config) =>
        profile.CloneForDelivery(profile.Stability, profile.Style, config.SpeedFor(profile.Speed));

    // --- Preparing lines in advance ----------------------------------------
    //
    // "Download every configured voice" in practice: render, once, every
    // line whose words are already known, so the race finds each one saved
    // and pays for nothing. Run from the Voice Settings page, by hand -
    // never on its own, because it spends the club's ElevenLabs quota.

    /// <summary>
    /// One line Prepare would render: what it is for, and the exact request
    /// the race will make. Transponder is set for a driver's own line (a
    /// release call) and null for the rest.
    /// </summary>
    public sealed record PlannedLine(string Label, VoiceProfile Profile, string Text, string? Transponder = null);

    /// <summary>How a Prepare run ended.</summary>
    public sealed record PrepareOutcome(int Rendered, int AlreadySaved, int Failed, string? StoppedBecause);

    /// <summary>Who is checked in for the current event, so Prepare can render each one's release call. Set by MainForm.</summary>
    public Func<IReadOnlyList<string>>? CheckedInDriversProvider { get; set; }

    /// <summary>
    /// The announcements whose fixed lines Prepare renders, all spoken
    /// through EnqueueVoice. LapTime is left out - it is only ever a number.
    /// The rest (streaks, battles, positions, ghosts, badges) build their
    /// words their own way from live values, so they are saved the first
    /// time they are spoken instead.
    /// </summary>
    private static readonly AnnouncementEventType[] PreparableTypes =
    {
        AnnouncementEventType.RaceStaging,
        AnnouncementEventType.GridCall,
        AnnouncementEventType.RaceStarted,
        AnnouncementEventType.YellowFlag,
        AnnouncementEventType.RedFlag,
        AnnouncementEventType.GreenFlagResumed,
        AnnouncementEventType.FinalMinute,
        AnnouncementEventType.LastLap,
        AnnouncementEventType.RaceTimeOver,
        AnnouncementEventType.RaceCompleted,
        AnnouncementEventType.TrackRecord,
        AnnouncementEventType.RecordPace,
        AnnouncementEventType.LeaderChange,
        AnnouncementEventType.LapCompleted,
        AnnouncementEventType.FastLap,
        AnnouncementEventType.JumpStart,
        AnnouncementEventType.ProvisionalPole,
        AnnouncementEventType.QualifyingImprovement,
        AnnouncementEventType.PersonalBestLap,
    };

    /// <summary>
    /// Every line that can be known before it is needed, each as the exact
    /// request the race will make for it - voice, settings and words - so
    /// that once these are rendered the race finds every one already saved.
    ///
    /// Two kinds, and nothing else:
    ///  - every fixed line (no {placeholder}) of every announcement that is
    ///    switched on, for races or practice - the flags among them;
    ///  - every release-call line, for each driver checked in for the
    ///    current event.
    ///
    /// A line with a live value in it - a lap time, a running order, a
    /// streak count, a driver's name on a lap call - can't be known until it
    /// happens. Those are saved the first time they are spoken, and are
    /// free every time after that they come round word for word.
    ///
    /// Read on the UI thread: it walks the driver profiles.
    /// </summary>
    public IReadOnlyList<PlannedLine> PlanLinesToPrepare()
    {
        var profile = _voiceStore.Current;

        // Nothing to prepare for the Windows voice: it renders each line on
        // this PC as it's needed, and it never makes a request of anything.
        // Otherwise there has to be a voice to render with - an ElevenLabs
        // one, or a HeatCaller one the relay gave.
        var nothingToRenderWith = profile.EffectiveEngine == VoiceEngine.HeatCaller
            ? !profile.HeatCallerReady
            : string.IsNullOrWhiteSpace(profile.VoiceId);

        if (nothingToRenderWith || profile.EffectiveEngine == VoiceEngine.Windows)
        {
            return Array.Empty<PlannedLine>();
        }

        // By key, so a line two announcements share is rendered once.
        var plan = new Dictionary<string, PlannedLine>(StringComparer.Ordinal);

        void Add(string label, VoiceProfile renderProfile, string text, string? transponder = null)
        {
            if (!string.IsNullOrWhiteSpace(text))
            {
                plan.TryAdd(VoiceLibrary.KeyFor(renderProfile, text), new PlannedLine(label, renderProfile, text, transponder));
            }
        }

        foreach (var type in PreparableTypes)
        {
            if (!profile.Events.TryGetValue(type.ToString(), out var config) || !(config.IsEnabledFor(false) || config.IsEnabledFor(true)))
            {
                continue;
            }

            // A time is added to these whenever one is known, so the bare
            // line is not what the race says.
            if (config.IncludeLapTime && type is AnnouncementEventType.LapCompleted or AnnouncementEventType.FastLap)
            {
                continue;
            }

            var renderProfile = DeliveryProfileFor(profile, config);
            var label = VoiceProfile.Describe(type).Label;

            foreach (var template in config.EffectiveTemplates.Where(t => !t.Contains('{')))
            {
                Add(label, renderProfile, PreparedFlagText(profile, template));
            }
        }

        var release = profile.Events.GetValueOrDefault(nameof(AnnouncementEventType.DriverRelease));

        if (release is not null && (release.IsEnabledFor(false) || release.IsEnabledFor(true)) && release.EffectiveTemplates.Count > 0)
        {
            // Rendered as PrimeReleaseLines renders them, so the race's own
            // prime at the green finds them all.
            var renderProfile = DeliveryProfileFor(profile, release);

            var drivers = (CheckedInDriversProvider?.Invoke() ?? Array.Empty<string>())
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var transponder in drivers)
            {
                var label = $"Release call - {DisplayNameFor(transponder)}";

                foreach (var template in release.EffectiveTemplates)
                {
                    Add(label, renderProfile, ReleaseTextFor(transponder, template, renderProfile), transponder);
                }
            }
        }

        return plan.Values.ToList();
    }

    /// <summary>
    /// Renders each planned line that isn't saved yet, one at a time.
    ///
    /// One at a time on purpose: it holds at most one of the two synthesis
    /// slots, so a race going on around it keeps the other. It also waits
    /// whenever the announcer has something to say - a line for later never
    /// goes ahead of one for now.
    ///
    /// Stops on its own when the quota runs out, or after three failures in
    /// a row, rather than working through a long list that will all fail.
    /// </summary>
    public async Task<PrepareOutcome> PrepareLinesAsync(
        IReadOnlyList<PlannedLine> plan,
        IProgress<(int Done, int Total)>? progress,
        CancellationToken cancel)
    {
        var rendered = 0;
        var alreadySaved = 0;
        var failed = 0;
        var failedInARow = 0;

        for (var i = 0; i < plan.Count; i++)
        {
            progress?.Report((i, plan.Count));

            if (cancel.IsCancellationRequested)
            {
                return new PrepareOutcome(rendered, alreadySaved, failed, "Stop was pressed");
            }

            var line = plan[i];

            if (VoiceLibrary.Shared.Contains(line.Profile, line.Text))
            {
                alreadySaved++;
                continue;
            }

            while ((_isVoiceSpeaking || IsAudioPending) && !cancel.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(500, cancel);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            if (cancel.IsCancellationRequested)
            {
                return new PrepareOutcome(rendered, alreadySaved, failed, "Stop was pressed");
            }

            // Chosen while this was running - checked last thing before the
            // request, since the wait above can be a whole race long. With
            // the Windows voice chosen no ElevenLabs request is made at all.
            if (_voiceStore.Current.EffectiveEngine == VoiceEngine.Windows)
            {
                return new PrepareOutcome(rendered, alreadySaved, failed, "the Windows voice was chosen on Voice Settings");
            }

            try
            {
                // The chosen voice and nothing else - never the stand-in. A
                // line that can't be rendered here counts as failed, and the
                // library is only ever given real takes.
                if (line.Profile.EffectiveEngine == VoiceEngine.HeatCaller)
                {
                    if (HeatCallerVoice is not { } client)
                    {
                        return new PrepareOutcome(rendered, alreadySaved, failed, "the HeatCaller relay isn't set up");
                    }

                    // Split into pieces when it is over the relay's length
                    // limit, the same as a live line - or a long line could
                    // never be prepared here at all.
                    var audio = await SpeakWholeLineThroughRelayAsync(client, line.Profile, line.Text, cancel);
                    VoiceLibrary.Shared.Save(line.Profile, line.Text, audio);
                }
                else
                {
                    await SynthesizeGatedAsync(line.Profile, line.Text);
                }

                rendered++;
                failedInARow = 0;
            }
            catch (OperationCanceledException) when (cancel.IsCancellationRequested)
            {
                // Stop, not a failure: the wait for a synthesis slot is
                // cancelled with the run, and counting that as a line that
                // failed made every cancelled run report one.
                return new PrepareOutcome(rendered, alreadySaved, failed, "Stop was pressed");
            }
            catch (HeatCallerVoiceException ex)
            {
                failed++;
                failedInARow++;
                NoteHeatCallerRefusal(ex);
                StatusMessage?.Invoke($"[voice] Couldn't prepare {line.Label}: \"{line.Text}\" ({ex.Message})");

                // The settled refusals - the allowance, the club's plan, a
                // voice that has gone - will refuse every line after this
                // one too. Stopping now says so once instead of a hundred
                // times, and leaves what was prepared in place.
                if (ex.Refusal == HeatCallerRefusal.Allowance)
                {
                    return new PrepareOutcome(rendered, alreadySaved, failed, "this month's HeatCaller voice allowance ran out");
                }

                if (ex.Refusal is HeatCallerRefusal.NotIncluded or HeatCallerRefusal.Off or HeatCallerRefusal.NotAllowed or HeatCallerRefusal.UnknownVoice)
                {
                    return new PrepareOutcome(rendered, alreadySaved, failed, ex.Message);
                }

                if (failedInARow >= 3)
                {
                    return new PrepareOutcome(rendered, alreadySaved, failed, $"three lines in a row failed - the last said: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                failed++;
                failedInARow++;
                StatusMessage?.Invoke($"[voice] Couldn't prepare {line.Label}: \"{line.Text}\" ({ex.Message})");

                if (IsQuotaExceeded(ex))
                {
                    return new PrepareOutcome(rendered, alreadySaved, failed, "the ElevenLabs quota ran out");
                }

                if (failedInARow >= 3)
                {
                    return new PrepareOutcome(rendered, alreadySaved, failed, $"three lines in a row failed - the last said: {ex.Message}");
                }
            }
        }

        progress?.Report((plan.Count, plan.Count));
        return new PrepareOutcome(rendered, alreadySaved, failed, null);
    }

    /// <summary>
    /// Drops the flag and release lines held in memory, so a line just
    /// forgotten from the saved-voice library is not played again from
    /// here. They are rendered - or read from the library - again the next
    /// time they are primed.
    /// </summary>
    public void ForgetPreparedAudio()
    {
        lock (_flagLinesLock)
        {
            _flagLines.Clear();
        }

        lock (_releaseLinesLock)
        {
            _releaseLines.Clear();
        }
    }

    /// <summary>
    /// Plays the prepared line for a flag, if one is ready and still matches
    /// the current voice. Returns false when there is nothing prepared, in
    /// which case the caller synthesises as usual.
    /// </summary>
    private bool TrySpeakPreparedFlag(AnnouncementEventType type, AnnouncementEventConfig config)
    {
        PreparedLine? prepared;
        var profile = _voiceStore.Current;
        var signature = VoiceSignature(profile, config);

        lock (_flagLinesLock)
        {
            // A stand-in take is passed over once ElevenLabs can speak again:
            // the call goes live in ElevenLabs instead, a moment later, rather
            // than the Windows voice saying this flag all night.
            if (!_flagLines.TryGetValue(type, out prepared)
                || !PreparedTakeMatches(prepared.Signature, prepared.Source, signature, profile))
            {
                return false;
            }

            // Used once: the next press should get a different phrasing, and
            // a fresh render is queued below.
            _flagLines.Remove(type);
        }

        RaiseAnnouncementText(prepared.Text);
        var generation = ClearTheDecksFor(type);

        // The finish call holds the voice queue until it has spoken - taken
        // here, synchronously, so NotifyRaceCompleted can see it was.
        var finishHold = ClaimFinishCallHold(type);

        var volume = AnnouncementGain(profile.Volume);

        _ = Task.Run(async () =>
        {
            Interlocked.Increment(ref _voiceInFlight);
            BeginSpeaking();

            try
            {
                StatusMessage?.Invoke($"[voice] {type} (prepared): \"{prepared.Text}\"");

                await WaitForLeadInSoundAsync(type);

                await _audioPlaybackLock.WaitAsync();
                try
                {
                    // A newer flag has cleared the decks since this one was
                    // pressed - it is the current instruction, and this one
                    // speaking after it would say the opposite.
                    if (Volatile.Read(ref _interruptGeneration) != generation)
                    {
                        StatusMessage?.Invoke($"[voice] {type} (prepared): dropped - a newer flag call came in before it could speak.");
                        return;
                    }

                    // Muted while it waited for its sound or the speaker.
                    if (Muted)
                    {
                        StatusMessage?.Invoke($"[voice] {type} (prepared): dropped - the announcer was muted.");
                        return;
                    }

                    await PlayAsync(prepared.Audio, volume, DefaultTrailingSilenceSeconds, CurrentLeadingSilence);
                }
                finally
                {
                    _audioPlaybackLock.Release();
                }
            }
            catch (Exception ex)
            {
                // A saved take that won't decode would come straight back on
                // the prime below and fail the same way on the next press.
                // Forgotten, so that prime renders it afresh. The signature
                // check above means these are the settings it was made with.
                //
                // An ElevenLabs take only. A Windows take was never saved,
                // and forgetting under its words would throw away the good
                // ElevenLabs line saved for the same flag.
                if (IsUndecodable(ex) && prepared.Source is LineSource.ElevenLabs or LineSource.Library or LineSource.HeatCaller)
                {
                    VoiceLibrary.Shared.Forget(DeliveryProfileFor(profile, config), prepared.Text);
                }

                StatusMessage?.Invoke($"[voice] {type} playback failed: {ex.Message}");
            }
            finally
            {
                Interlocked.Decrement(ref _voiceInFlight);
                EndSpeaking();

                // However the call ended - spoken, dropped, failed - the
                // queue it was holding carries on.
                finishHold?.TrySetResult();

                // Render the next one now, while nothing is waiting on it.
                PrimeFlagLine(type);
            }
        });

        return true;
    }

    /// <summary>
    /// Stops the line in progress and bins the routine audio queued behind
    /// it, so a flag call lands in silence rather than on top of lap chatter.
    /// </summary>
    /// <returns>
    /// The interrupt generation this call started. The flag line carries it,
    /// so that if a NEWER flag clears the decks before this one has spoken,
    /// this one knows it has been overtaken and stays quiet.
    /// </returns>
    private int ClearTheDecksFor(AnnouncementEventType type)
    {
        int clearedVoice;
        int generation;

        lock (_voiceQueueLock)
        {
            // Bumped under the same lock the voice loop dequeues under, in
            // the same step as the purge: a routine line was either dequeued
            // before this (it now holds a stale generation and stays silent
            // even if it is still synthesising) or is still queued and binned
            // just below - there is no gap for one to slip through between.
            generation = Interlocked.Increment(ref _interruptGeneration);

            clearedVoice = _voiceQueue.RemoveAll(item => !IsPriorityAnnouncement(item.Type));
        }

        int clearedSounds;

        lock (_soundQueueLock)
        {
            // Only the lap chimes go. The race's own start, time-up and
            // finish sounds are queued a moment before their flag call
            // clears the decks, and binning everything used to throw away
            // the very sound that belonged to the call - so the start horn
            // and the finish fanfare never played at all.
            clearedSounds = RemoveQueuedSounds(item => IsPerLapSound(item.Kind));
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
        }

        var wasSpeaking = StopCurrentPlayback();

        // The sound effect in progress goes too - a long custom start or
        // finish file used to hold the speaker for its whole length while
        // the yellow waited behind it. Spared when it is this call's own
        // lead-in sound (the horn for the start, the buzzer for time up,
        // the fanfare for the finish), which is meant to play first.
        var cutSound = StopPlayingSound(except: LeadInSoundKindFor(type));

        StatusMessage?.Invoke(
            $"[voice] {type} interrupting - {(wasSpeaking ? "cut off the line in progress" : "nothing was speaking")}"
            + $"{(cutSound ? " and the sound playing" : string.Empty)}, "
            + $"cleared {Plural.Of(clearedVoice, "queued line")} and {Plural.Of(clearedSounds, "sound")}.");

        return generation;
    }
}
