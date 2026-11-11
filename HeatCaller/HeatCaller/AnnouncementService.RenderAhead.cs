namespace HeatCaller;

/// <summary>
/// The next line renders while this one plays (10 Nov 2026, voice speed
/// plan, phase 2).
/// </summary>
/// <remarks>
/// WHY. With eleven_v4_turbo a render is under a second, and the timing
/// lines (VoiceLineTiming) showed where the slowest calls lost their time:
/// a change of lead kept for the end of a line, or a fastest lap queued
/// behind one, was ready to go the moment the speaker came free - and then
/// spent another 0.6-1 s being rendered, in silence, before a word of it
/// was heard. The words of those calls are fixed when they are queued, so
/// they can be made while the line before them is still being said.
///
/// ONLY THE NEXT LINE. Only the call that will be taken next - the front of
/// the queue, else the call kept for the end of this line (HeldOver) - is
/// started, when a line is about to be heard and whenever a call is queued
/// or kept while one is. A render nobody hears is a request paid for, so a
/// call that will be past its deadline before the playing line can end is
/// not rendered at all, and the race's summary counts how many were used.
///
/// HANDED OVER BY EXACT MATCH. The early render is asked for with the same
/// profile SynthesizeAndPlayAsync would build (ProfileForLine, the model's
/// tags, the race's energy) and kept under a key of every setting that
/// shapes the audio. When the line's turn comes, it takes the early render
/// only if its own key is the same - the race's energy moving in between,
/// a change on the Voice page, a different voice: any of those and the line
/// is rendered then, as it always was. A render still on its way is waited
/// for rather than asked for twice; one that failed is rendered again.
///
/// NOTHING FROM THE PLAYING LINE CARRIES OVER. The early render is started
/// with the execution context suppressed, so none of the playing line's
/// AsyncLocals - its timing, its flag generation, its render cancel - reach
/// it. It is not timed itself; the line that takes it records "rendered
/// ahead".
/// </remarks>
public sealed partial class AnnouncementService
{
    /// <summary>
    /// The lines rendered ahead and not yet taken - their keys and renders,
    /// finished or not - newest last. Under _renderAheadLock. More than one,
    /// because a call queued in FRONT of one already rendered (a release, a
    /// last lap) would otherwise throw that render away, and the line behind
    /// it would be asked for twice.
    /// </summary>
    private readonly List<(string Key, Task<SynthesisResult> Render)> _renderedAhead = new();

    /// <summary>The most kept at once - only the next line or two can use one.</summary>
    internal const int MostRenderedAhead = 3;

    private readonly object _renderAheadLock = new();

    /// <summary>
    /// The least time the playing line is taken to have left - its lead-in
    /// and the shortest call. A call due before then would be stale by its
    /// turn, so it is not rendered ahead.
    /// </summary>
    internal const long PlayingLineLeastLeftMs = 1_500;

    /// <summary>
    /// Every setting that shapes a line's audio, and its words: the saved-
    /// line key (VoiceLibrary.KeyFor), plus the engine and the Kokoro voice,
    /// which that key - made for ElevenLabs takes - leaves out.
    /// </summary>
    internal static string RenderAheadKey(VoiceProfile profile, string text) =>
        $"{profile.EffectiveEngine}\n{profile.KokoroVoiceName}\n{VoiceLibrary.KeyFor(profile, text)}";

    /// <summary>
    /// A call was queued or kept for the end of a line: if a line is
    /// speaking (or being rendered) now, the call that will follow it may
    /// just have changed, so it is started early. Most calls arrive this
    /// way - the first race with render ahead started it only as each line
    /// began, before the calls made during that line existed, and rendered
    /// nothing ahead at all.
    /// </summary>
    private void RenderAheadIfALineIsSpeaking()
    {
        if (_isVoiceSpeaking)
        {
            RenderTheNextLineAhead();
        }
    }

    /// <summary>
    /// Starts rendering the call that will be said next, if there is one
    /// that can be. Returns at once. Called as a line is about to be heard,
    /// and as a call is queued or kept while one is (RenderAheadIfALineIsSpeaking).
    /// </summary>
    private void RenderTheNextLineAhead()
    {
        Func<Task>? next = null;

        try
        {
            var lineEndsNoSoonerThan = TimingClock.NowMs + PlayingLineLeastLeftMs;

            lock (_voiceQueueLock)
            {
                if (_voiceQueue.Count > 0)
                {
                    var front = _voiceQueue[0];

                    if (front.RenderAhead is not null
                        && !(front.SayByMs is { } sayBy && sayBy <= lineEndsNoSoonerThan))
                    {
                        next = front.RenderAhead;
                    }
                }
            }

            // Nothing queued: the call kept for the end of this line goes
            // next, so it is the one worth having ready.
            if (next is null && !QueueHasAnything())
            {
                next = _heldOver.Peek()?.RenderAhead;
            }
        }
        catch
        {
            // Rendering ahead is a head start, never a reason a line fails.
            return;
        }

        if (next is null)
        {
            return;
        }

        // None of the playing line's AsyncLocals - see the remarks.
        using (ExecutionContext.SuppressFlow())
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await next();
                }
                catch (Exception ex)
                {
                    StatusMessage?.Invoke($"[voice] Rendering the next line ahead failed - it will be rendered when its turn comes ({ex.Message}).");
                }
            });
        }
    }

    private bool QueueHasAnything()
    {
        lock (_voiceQueueLock)
        {
            return _voiceQueue.Count > 0;
        }
    }

    /// <summary>
    /// Renders a queued lap call's words ahead, exactly as
    /// SynthesizeAndPlayAsync will ask for them when it is EnqueueVoice's
    /// line: no delivery override, its own speed, no second voice, the
    /// race's energy on.
    /// </summary>
    /// <param name="speed">The call's own speed, as it passes SynthesizeAndPlayAsync's speedOverride - null for one that passes none.</param>
    private async Task RenderLineAheadAsync(string text, AnnouncementEventType type, double? speed)
    {
        if (LineAsItWillBeAsked(text, type, speed) is not { } line)
        {
            return;
        }

        var (profile, key) = (line.Profile, line.Key);
        text = line.Text;
        var render = new TaskCompletionSource<SynthesisResult>(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_renderAheadLock)
        {
            if (_renderedAhead.Any(already => already.Key == key))
            {
                return;
            }

            if (_renderedAhead.Count >= MostRenderedAhead)
            {
                _renderedAhead.RemoveAt(0);
            }

            _renderedAhead.Add((key, render.Task));
        }

        // Counted for the race's summary, used or not (VoiceTimingTally).
        Timings.NoteRenderedAhead();

        try
        {
            render.TrySetResult(await RenderLineAsync(profile, text));
        }
        catch (Exception ex)
        {
            render.TrySetException(ex);

            // Observed, so a failure nobody comes to take is not reported
            // as unobserved; the line that comes for it renders afresh.
            _ = render.Task.Exception;
        }
    }

    /// <summary>
    /// A line as SynthesizeAndPlayAsync will ask for it when it is said with
    /// no delivery override and no second voice: its profile (with the
    /// race's energy), its words (with the model's tags) and its key. Null
    /// when nothing could say it now. Sets _dequeuedCall, so call it only
    /// where nothing of the queue's own is flowing - see RenderTheNextLineAhead.
    /// </summary>
    private (VoiceProfile Profile, string Text, string Key)? LineAsItWillBeAsked(string text, AnnouncementEventType type, double? speed)
    {
        // On the race-calls model, as the line will be (ForRaceCall reads it).
        _dequeuedCall.Value = type;

        var profile = ProfileForLine(type, null, speed);
        text = profile.SanitiseForModel(text);

        if (Muted || !CanSpeak(profile))
        {
            return null;
        }

        profile = WithTheRacesEnergy(profile, text);

        return (profile, text, RenderAheadKey(profile, text));
    }

    /// <summary>
    /// Hands an early render back to be taken - for one kept outside the
    /// list until its line is about to be said (the winner's call, which a
    /// flag's clear-out must not take away).
    /// </summary>
    private void OfferRenderedAhead(string key, Task<SynthesisResult> render)
    {
        lock (_renderAheadLock)
        {
            _renderedAhead.RemoveAll(ahead => ahead.Key == key);
            _renderedAhead.Add((key, render));
        }
    }

    /// <summary>
    /// The early render for exactly this line, if there is one - taken, so
    /// it is used once. Null otherwise.
    /// </summary>
    private Task<SynthesisResult>? TakeRenderedAhead(VoiceProfile profile, string text)
    {
        var key = RenderAheadKey(profile, text);

        lock (_renderAheadLock)
        {
            var at = _renderedAhead.FindIndex(ahead => ahead.Key == key);

            if (at >= 0)
            {
                var render = _renderedAhead[at].Render;
                _renderedAhead.RemoveAt(at);

                return render;
            }
        }

        return null;
    }

    /// <summary>
    /// The line's audio: its early render when there is one for exactly
    /// these words and settings, else rendered now as it always was. Second
    /// is whether the early one was used.
    /// </summary>
    private async Task<(SynthesisResult Synthesis, bool RenderedAhead)> RenderAheadOrNowAsync(
        VoiceProfile withoutVoice, VoiceProfile profile, SecondVoice? voice, string text)
    {
        if (voice is null && TakeRenderedAhead(profile, text) is { } ahead)
        {
            try
            {
                return (await ahead, true);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !RenderCancel.Value.IsCancellationRequested)
            {
                // Rendered again now, the way it would have been - with
                // every fallback that has.
            }
        }

        return (await RenderCastLineAsync(withoutVoice, profile, voice, text), false);
    }

    /// <summary>A flag, the countdown or a new race: what was rendered ahead is for a line that is going. Not needed for correctness - the key would not match a new line - but it frees the audio.</summary>
    private void ForgetRenderedAhead()
    {
        lock (_renderAheadLock)
        {
            _renderedAhead.Clear();
        }
    }
}
