using NAudio.Wave;

namespace HeatCaller;

/// <summary>
/// Turning a line of text into audio: the synthesis gate, ElevenLabs with
/// its rest and probe, the Kokoro stand-in, and the reasons given when a
/// voice can't speak.
///
/// Moved here unchanged from AnnouncementService.cs on 26 Sep 2026, when the
/// file was split into parts. Fields with initializers stayed there.
/// </summary>
public sealed partial class AnnouncementService
{
    /// <summary>
    /// Every ElevenLabs request in this class goes through here, so the
    /// concurrency limit is enforced in ONE place rather than depending on
    /// each caller being well behaved. (Kokoro has a gate of its own - see
    /// KokoroSpeech - and never waits on this one.)
    ///
    /// Bounded by <see cref="SynthesisTimeout"/> - covering the wait for a
    /// gate slot AND the request itself. Without it, one hung request held a
    /// slot for HttpClient's 100 s default while every later line (a flag
    /// call included) queued behind it; a line that late is worthless anyway.
    ///
    /// A line already in the saved-voice library comes back from disk and
    /// costs nothing - see VoiceLibrary.
    /// </summary>
    private async Task<byte[]> SynthesizeGatedAsync(VoiceProfile profile, string text) =>
        (await SynthesizeGatedResultAsync(profile, text)).Audio;

    /// <summary>
    /// The same, saying whether the audio came from the library. ElevenLabs
    /// and nothing else - this is the path with no stand-in, as Prepare
    /// Lines needs it; race lines go through RenderLineAsync, which uses it.
    ///
    /// A request ElevenLabs actually answered is reported to
    /// NoteElevenLabsAnswered, which is what says a spent quota is back or a
    /// lost connection has returned - a saved line proves nothing about the
    /// account.
    /// </summary>
    /// <param name="reuseSaved">False when the caller has just asked the library itself.</param>
    private async Task<SynthesisResult> SynthesizeGatedResultAsync(VoiceProfile profile, string text, bool reuseSaved = true)
    {
        var result = await SynthesizeOrReuseAsync(_elevenLabs, profile, text, reuseSaved);

        if (!result.FromLibrary)
        {
            NoteElevenLabsAnswered();
        }

        return result;
    }

    /// <summary>
    /// The same gated, time-bounded synthesis for callers outside this class
    /// - the Voice Settings page's Test buttons. The concurrency limit is
    /// account-wide, and several test buttons can be pressed at once (each
    /// only disables itself), so a test going around the gate could push a
    /// race's own lines over the limit and get them rejected with a 429.
    /// </summary>
    /// <param name="reuseSaved">
    /// False for a Test button: it always renders a fresh take, and that
    /// take replaces whatever was saved for the same line. So a line that
    /// came out wrong is fixed by testing it until it sounds right - the
    /// race then plays the take that was last heard.
    /// </param>
    public static async Task<byte[]> SynthesizeThroughGateAsync(ElevenLabsClient client, VoiceProfile profile, string text, bool reuseSaved = true)
    {
        // The last gate before a club's own key is used. With the HeatCaller
        // voice chosen there is no key of the club's to use, and the account
        // behind that voice is not the club's to test with - so the Test
        // buttons are hidden on Voice Settings, and this refuses in case a
        // path is ever added that forgets to check.
        if (profile.EffectiveEngine == VoiceEngine.HeatCaller)
        {
            throw new InvalidOperationException(
                "The HeatCaller voice can't be tested from here. Choose \"ElevenLabs - natural voices from your own ElevenLabs account\" on the Voice tab to test a voice you pay for.");
        }

        // A Test press waits on its own button, not in the voice queue.
        RenderingAhead.Value = true;
        return (await SynthesizeOrReuseAsync(client, profile, text, reuseSaved)).Audio;
    }

    /// <summary>Who rendered a line.</summary>
    private enum LineSource
    {
        /// <summary>ElevenLabs, just now.</summary>
        ElevenLabs,

        /// <summary>The saved-voice library: an ElevenLabs or HeatCaller take from before.</summary>
        Library,

        /// <summary>The HeatCaller voice, through the club's relay.</summary>
        HeatCaller,

        /// <summary>
        /// Kokoro standing in for ElevenLabs or the HeatCaller voice, which
        /// couldn't speak the line. Kept apart from <see cref="Kokoro"/>
        /// below because a stand-in take only counts while the voice it
        /// stood in for still can't speak - see PreparedTakeMatches.
        /// </summary>
        StandIn,

        /// <summary>The Kokoro voice, on this PC, because it is the voice chosen on Voice Settings.</summary>
        Kokoro,
    }

    /// <summary>What a synthesis handed back, and who made it.</summary>
    private readonly record struct SynthesisResult(byte[] Audio, LineSource Source)
    {
        /// <summary>From the saved-voice library rather than a request just made.</summary>
        public bool FromLibrary => Source == LineSource.Library;
    }

    /// <param name="cancelRender">Stops the render once nobody will hear it; when not given, RenderCancel's is used.</param>
    private static async Task<SynthesisResult> SynthesizeOrReuseAsync(ElevenLabsClient client, VoiceProfile profile, string text, bool reuseSaved, CancellationToken cancelRender = default)
    {
        // Before the gate: a saved line needs no slot, and must not wait
        // behind a render for one.
        if (reuseSaved && await VoiceLibrary.Shared.TryLoadAsync(profile, text) is { } saved)
        {
            return new SynthesisResult(saved, LineSource.Library);
        }

        var limit = LimitFor(text);
        // Cut short by the caller too, once nobody will hear it - see RenderCancel.
        var cancel = cancelRender.CanBeCanceled ? cancelRender : RenderCancel.Value;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(limit);
        // WHY (20 Oct 2026): the gate wait and the request share the one
        // 30 s limit, so a line that got its slot at 28 s had 2 s to be
        // answered and was then reported as ElevenLabs not responding -
        // which rested ElevenLabs for a minute though only this laptop's own
        // queue was slow. How long the wait took is kept so a timeout can be
        // blamed on the right thing (see RequestTimedOut).
        var gateClock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await SynthesisGate.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancel.IsCancellationRequested)
        {
            throw new SynthesisSlotTimeoutException($"No synthesis slot came free within {limit.TotalSeconds:0} s.");
        }

        var waitedForSlot = gateClock.Elapsed;

        byte[] audio;
        try
        {
            audio = await client.SynthesizeAsync(profile, text, timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancel.IsCancellationRequested)
        {
            throw RequestTimedOut(waitedForSlot, limit);
        }
        finally
        {
            SynthesisGate.Release();
        }

        // Kept, so the next time this exact line is wanted - later today or
        // after a restart - it is not bought again. ElevenLabs audio only:
        // nothing the Windows voice says ever comes through here.
        VoiceLibrary.Shared.Save(profile, text, audio);

        return new SynthesisResult(audio, LineSource.ElevenLabs);
    }

    /// <remarks>Sent as given: the caller (the release batch) has already shortened names - see AnnouncementService.ShortNames.</remarks>
    private async Task<ElevenLabsClient.TimedSpeech?> SynthesizeTimedGatedAsync(VoiceProfile profile, string text)
    {

        var limit = LimitFor(text);
        var cancel = RenderCancel.Value;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(limit);
        // WHY (20 Oct 2026): timed the same way as SynthesizeOrReuseAsync,
        // so a request starved by a long wait for its slot is not blamed on
        // the network.
        var gateClock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await SynthesisGate.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancel.IsCancellationRequested)
        {
            throw new SynthesisSlotTimeoutException($"No synthesis slot came free within {limit.TotalSeconds:0} s.");
        }

        var waitedForSlot = gateClock.Elapsed;

        try
        {
            var timed = await _elevenLabs.SynthesizeWithTimestampsAsync(profile, text, timeout.Token);

            if (timed is not null)
            {
                NoteElevenLabsAnswered();
            }

            return timed;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancel.IsCancellationRequested)
        {
            throw RequestTimedOut(waitedForSlot, limit);
        }
        finally
        {
            SynthesisGate.Release();
        }
    }

    /// <summary>
    /// Upper bound on one gated synthesis of a SHORT line, gate wait
    /// included - a long one gets more, see SynthesisTimeoutFor. Shorter
    /// than ElevenLabsClient's own HttpClient timeout so this, with its
    /// clearer message, is the one that fires.
    /// </summary>
    private static readonly TimeSpan SynthesisTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Set while a line is rendered AHEAD of being said - a recap prepared
    /// during the teaser, a recorded voice line, a Test press - and only
    /// then does a long line get the long limit (22 Oct 2026, bug hunt). A
    /// line rendered inside the voice queue holds the one voice loop while
    /// it renders, and a 150 s wait there froze every call behind it, so
    /// those keep the flat 30 s. An AsyncLocal, so it reaches the render
    /// through the awaits without every method in between carrying it.
    /// </summary>
    private static readonly AsyncLocal<bool> RenderingAhead = new();

    /// <summary>
    /// What stops a line rendered ahead once nobody will hear it - the next
    /// race being called over a recap still being made (22 Oct 2026, known
    /// issues). Set by PrepareReminderAsync and RenderLineAsync from the
    /// token they are given, and read where the slot is waited for and the
    /// request made, so a recap's render no longer runs to the end - up to
    /// two and a half minutes, holding one of the two synthesis slots and
    /// spending its characters - while the new race starts. An AsyncLocal
    /// for the same reason as RenderingAhead: it reaches the render through
    /// every voice's path without each method in between carrying it.
    /// </summary>
    private static readonly AsyncLocal<CancellationToken> RenderCancel = new();

    /// <summary>The limit for one line, by where it is being rendered - see RenderingAhead.</summary>
    private static TimeSpan LimitFor(string? text) =>
        RenderingAhead.Value ? SynthesisTimeoutFor(text) : SynthesisTimeout;

    /// <summary>The most a single line is ever given, however long it is.</summary>
    private static readonly TimeSpan LongestSynthesisTimeout = TimeSpan.FromSeconds(150);

    /// <summary>
    /// How long ONE line is given, gate wait included (21 Oct 2026).
    ///
    /// WHY: the limit was a flat 30 s, and a race recap is one line - a
    /// minute or more of speech, which eleven_v3 can take longer than 30 s
    /// to make on a good connection. On 21 Oct the recap ran out of time,
    /// was reported as "Can't reach ElevenLabs", ElevenLabs was rested, and
    /// Kokoro read the recap - then ElevenLabs was tried again a minute
    /// later, answered at once, and the booth interview and the sign-off
    /// went out in the ElevenLabs voices. One programme, two voices, and
    /// nothing wrong with the network at all.
    ///
    /// So a short line keeps the 30 s it had - a race call that late is
    /// worthless anyway - and a long one gets a second more for every 25
    /// characters past the first 300, up to two and a half minutes.
    /// </summary>
    internal static TimeSpan SynthesisTimeoutFor(string? text)
    {
        var extra = Math.Max(0, (text?.Length ?? 0) - 300) / 25;
        var limit = SynthesisTimeout + TimeSpan.FromSeconds(extra);

        return limit < LongestSynthesisTimeout ? limit : LongestSynthesisTimeout;
    }

    /// <summary>
    /// True when more than half of SynthesisTimeout went on waiting for a
    /// synthesis slot, leaving the request itself too little of the limit
    /// for its running out to say anything about the network.
    /// </summary>
    internal static bool SlotWaitTookMostOfLimit(TimeSpan waitedForSlot) => SlotWaitTookMostOfLimit(waitedForSlot, SynthesisTimeout);

    /// <summary>The same, against the limit one line was actually given - see SynthesisTimeoutFor.</summary>
    internal static bool SlotWaitTookMostOfLimit(TimeSpan waitedForSlot, TimeSpan limit) => waitedForSlot > limit / 2;

    /// <summary>
    /// The exception for a request cut off by SynthesisTimeout. Added 20 Oct
    /// 2026: if most of the limit was spent queued for a slot, it is a
    /// SynthesisSlotTimeoutException - this laptop's own queue, so
    /// ElevenLabs is not rested - and only otherwise "ElevenLabs didn't
    /// respond". The single 30 s upper bound is kept either way.
    /// </summary>
    private static TimeoutException RequestTimedOut(TimeSpan waitedForSlot, TimeSpan limit) =>
        SlotWaitTookMostOfLimit(waitedForSlot, limit)
            ? new SynthesisSlotTimeoutException(
                $"Waited {waitedForSlot.TotalSeconds:0} s for a synthesis slot, which left the request too little of the {limit.TotalSeconds:0} s limit.")
            : new TimeoutException($"ElevenLabs didn't respond within {limit.TotalSeconds:0} s.");

    /// <summary>
    /// Waited the whole SynthesisTimeout for one of the ElevenLabs slots and
    /// never got one - or (since 20 Oct 2026) got one so late that the
    /// request was cut off for want of time: this laptop's own requests
    /// were queued up (a long recap on a slow model, say), not the network
    /// being down - so the stand-in says the line, but ElevenLabs is not
    /// rested for it (see IsConnectionFailure).
    /// </summary>
    private sealed class SynthesisSlotTimeoutException : TimeoutException
    {
        public SynthesisSlotTimeoutException(string message)
            : base(message)
        {
        }
    }

    // --- Which voice says a line -------------------------------------------
    //
    // Every race and announcer line gets its audio from RenderLineAsync. The
    // Voice Settings Test buttons don't - they must show ElevenLabs' own
    // error, so they call SynthesizeThroughGateAsync (or Kokoro itself)
    // themselves - and nor does Prepare Lines, which fills the saved-voice
    // library with ElevenLabs lines and nothing else.

    /// <summary>
    /// A line in a second speaker's voice. Their Kokoro voice picked for every
    /// engine (25 Oct 2026, SecondVoice.KokoroAlways) sends it to Kokoro
    /// beside an ElevenLabs or HeatCaller announcer - and when Kokoro can't
    /// say it after all (the model went, or wouldn't load), the line is said
    /// as it was before there was the choice, in the engine's voice for them
    /// or the announcer's, rather than lost. Kokoro chosen as the engine is
    /// not this: nothing covers for it, as RenderWithKokoroAsync says.
    /// </summary>
    /// <param name="withoutVoice">The profile the line would have had with no second voice.</param>
    /// <param name="withVoice">The same with <paramref name="voice"/> on it (VoiceProfile.CloneWithVoice).</param>
    private async Task<SynthesisResult> RenderCastLineAsync(
        VoiceProfile withoutVoice, VoiceProfile withVoice, SecondVoice? voice, string text, CancellationToken cancel = default)
    {
        var sentToKokoro = voice is not null
            && withVoice.EffectiveEngine == VoiceEngine.Kokoro
            && withoutVoice.EffectiveEngine != VoiceEngine.Kokoro;

        if (!sentToKokoro)
        {
            return await RenderLineAsync(withVoice, text, cancel: cancel);
        }

        // Asked first, so a Kokoro that has just gone doesn't log that
        // nothing on this PC can speak - the announcer's voice still can.
        if (KokoroSpeech.UnavailableReason is not { } unavailable)
        {
            try
            {
                return await RenderLineAsync(withVoice, text, cancel: cancel);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                unavailable = ex.Message;
            }
        }

        LogStandInOnce("cast-kokoro:" + unavailable,
            $"[voice] Kokoro couldn't say a line for a Kokoro voice picked on the Cast tab ({unavailable}) - "
            + "it was said in the voice they have without it instead. Said once for this reason.");

        return await RenderLineAsync(withoutVoice.CloneWithVoice(voice!, kokoroReady: false), text, cancel: cancel);
    }

    /// <summary>
    /// The audio for one race or announcer line, from whichever voice should
    /// say it:
    ///
    ///  - Kokoro chosen: Kokoro, always. No ElevenLabs request is made, and
    ///    the saved-voice library is neither read nor written - it is keyed
    ///    by the ElevenLabs settings, and a Kokoro take saved there would
    ///    later play as though it were the ElevenLabs line.
    ///  - ElevenLabs chosen, stand-in off (or no Kokoro model on this PC):
    ///    ElevenLabs exactly as it always was - the saved line, otherwise a
    ///    request - and a failure is thrown to the caller as before.
    ///  - ElevenLabs chosen, stand-in on: a saved line plays from disk
    ///    whatever state ElevenLabs is in. With no key or voice to ask with,
    ///    or while ElevenLabs rests after a lost connection, Kokoro says it
    ///    straight away. Otherwise ElevenLabs is asked, and when it can't -
    ///    quota, connection, any refusal - Kokoro says the same line, so a
    ///    flag call is never silent.
    ///
    /// Throws only when no voice could say it - which, since the Windows
    /// voice went, means a PC with no Kokoro model and nothing else able to
    /// speak either. CanSpeak is what stops a line reaching here in that
    /// state and says so in the console; this throws rather than hand back
    /// silence, so nothing can swallow it.
    /// </summary>
    /// <param name="fresh">
    /// ElevenLabs only: pass over the saved-voice library and ask for a new
    /// take, which replaces the saved one only once it has come back (see
    /// SynthesizeOrReuseAsync). For Re-record all - see RecordVoiceLineAsync.
    /// </param>
    /// <param name="cancel">
    /// Stops a line rendered ahead once nobody will hear it (22 Oct 2026) -
    /// the next race called over a recap, say. Throws
    /// OperationCanceledException then; nothing is rested or stood in for.
    /// </param>
    private async Task<SynthesisResult> RenderLineAsync(VoiceProfile profile, string text, bool fresh = false, CancellationToken cancel = default)
    {
        if (cancel.CanBeCanceled)
        {
            RenderCancel.Value = cancel;
        }

        RenderCancel.Value.ThrowIfCancellationRequested();

        // A line recorded for a soundscape plays as recorded, whichever
        // voice is chosen - that is the point of recording it (see
        // StingerLibrary). Checked first, so it needs no key, no relay and
        // no internet.
        //
        // EXCEPT a take recorded in a cloned HeatCaller voice whose consent
        // has been taken back (17 Oct 2026, Bug Hunt 8, A10-1): it falls
        // through to the live path, which applies the same gate to new
        // lines. A take recorded before the voice id was stored is matched
        // by name to the HeatCaller voice chosen now - see TakeAllowed.
        //
        // And except while ElevenLabs' allowance is used up and Kokoro stands
        // in (5 Nov 2026): a recording is a take in an ElevenLabs or HeatCaller
        // voice like any saved one - see ElevenLabsSavedLinesPassedOver.
        if (!ElevenLabsSavedLinesPassedOver(profile) && StingerLibrary.Shared.TryLoadWithEntry(text) is { } recorded)
        {
            if (HeatCallerVoice is not { } consent
                || StingerLibrary.TakeAllowed(recorded.Entry, consent.ConsentAllows, profile.HeatCallerVoice, profile.HeatCallerVoiceName))
            {
                return new SynthesisResult(recorded.Audio, LineSource.Library);
            }

            StatusMessage?.Invoke("[voice] Passed over a recorded voice line - its HeatCaller voice's consent has been taken back.");
        }

        // "A-Main" said as the letter, not "ah main" (8 Nov 2026) - for every
        // engine, after the recordings (which are matched by the words as
        // written). See RaceNumbers.SpokenRoundNames.
        text = RaceNumbers.SpokenRoundNames(text);

        if (profile.EffectiveEngine == VoiceEngine.Kokoro)
        {
            return await RenderWithKokoroAsync(profile, text);
        }

        // Off this PC from here on: names shortened when the club has asked
        // for that on its pages (22 Oct 2026) - see AnnouncementService.ShortNames.
        // Kokoro standing in is ON this PC, so it is given the line as written,
        // full names and all - the same words it says when it is the voice chosen.
        var asWritten = text;
        text = ForCloudVoice(text);

        if (profile.EffectiveEngine == VoiceEngine.HeatCaller)
        {
            return await RenderWithHeatCallerAsync(profile, text, asWritten);
        }

        if (!StandInAllowed(profile))
        {
            return await SynthesizeGatedResultAsync(profile, text, reuseSaved: !fresh);
        }

        // Not while the allowance is used up (5 Nov 2026): the saved line is
        // passed over and the line goes on below, where ElevenLabs is asked
        // first - its refusal is quick, and an answer is what says the
        // allowance is back - and Kokoro says it when it refuses.
        if (!fresh
            && !ElevenLabsSavedLinesPassedOver(profile)
            && await VoiceLibrary.Shared.TryLoadAsync(profile, text) is { } saved)
        {
            return new SynthesisResult(saved, LineSource.Library);
        }

        // The usage chip at 100% (5 Nov 2026 - see ElevenLabsAllowanceUsedUp):
        // ElevenLabs isn't asked at all; the meter says when it is back.
        if (ElevenLabsMeterSpent())
        {
            return await RenderWithStandInAsync(profile, asWritten);
        }

        if (!ElevenLabsReady(profile))
        {
            LogStandInOnce("setup", $"[voice] {Capitalised(NotConfiguredReason(profile))} - the free Kokoro voice is speaking instead.");
            return await RenderWithStandInAsync(profile, asWritten);
        }

        // Resting after a lost connection, or a key ElevenLabs turned down:
        // this line doesn't wait on the network at all. Once a rest period,
        // one line is also tried through ElevenLabs in the background, to
        // notice it coming back.
        if (IsElevenLabsResting(profile, out var probe))
        {
            if (probe)
            {
                StartElevenLabsProbe(profile, text);
            }

            return await RenderWithStandInAsync(profile, asWritten);
        }

        try
        {
            // The library was asked a moment ago.
            return await SynthesizeGatedResultAsync(profile, text, reuseSaved: false);
        }
        catch (OperationCanceledException) when (RenderCancel.Value.IsCancellationRequested)
        {
            // Nobody will hear it (RenderCancel): not a lost connection, so
            // ElevenLabs is not rested and nothing stands in.
            throw;
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            BeginElevenLabsRest(ex);
            return await RenderWithStandInAsync(profile, asWritten);
        }
        catch (Exception ex) when (IsQuotaExceeded(ex))
        {
            // ElevenLabs is still asked first on every line - the refusal is
            // quick - so the quota coming back is noticed straight away.
            NoteQuotaExceeded(standingIn: true);
            return await RenderWithStandInAsync(profile, asWritten);
        }
        catch (Exception ex) when (IsKeyRefusal(ex))
        {
            // The key turned down: every line would be turned down the same
            // way, so ElevenLabs rests - lines, flags included, go straight
            // to Kokoro, rather than each waiting for the same refusal
            // first - until a background try gets through, or the
            // key is changed on Voice Settings.
            BeginElevenLabsRest(ex, refusedKey: profile.ApiKey);
            return await RenderWithStandInAsync(profile, asWritten);
        }
        catch (Exception ex)
        {
            // A bad key, too many requests, a server error: Kokoro takes
            // this line, and the reason is said once, not every line.
            LogStandInOnce(ReasonKey(ex), StandInRefusalMessage(ex));
            return await RenderWithStandInAsync(profile, asWritten);
        }
    }

    /// <summary>
    /// Kokoro standing in for a voice that couldn't speak this line -
    /// ElevenLabs, or the HeatCaller relay. Always marked
    /// <see cref="LineSource.StandIn"/>, never Kokoro: the take is only good
    /// while the voice it stood in for still can't speak (see
    /// PreparedTakeMatches), and that is the whole difference between the two.
    ///
    /// Callers check <see cref="StandInAllowed"/> first; this throws rather
    /// than hand back silence if Kokoro cannot speak after all.
    /// </summary>
    private static async Task<SynthesisResult> RenderWithStandInAsync(VoiceProfile profile, string text) =>
        new SynthesisResult(await KokoroSpeech.RenderAsync(profile, text, renderingAhead: RenderingAhead.Value), LineSource.StandIn);

    /// <summary>
    /// A line in the Kokoro voice, on this PC, because it is the voice
    /// chosen on Voice Settings.
    ///
    /// Nothing stands in for it. It IS the stand-in now - the free Windows
    /// voice that used to cover for it went on 10 Oct 2026 - so when Kokoro
    /// can't say a line (the model isn't on this PC, it wouldn't load, a
    /// render that stuck) this throws, loudly, naming the one button that
    /// fixes it. The alternative was an announcer that went quiet on a
    /// Saturday and said nothing about why, which is the failure this whole
    /// class is written to avoid.
    /// </summary>
    private async Task<SynthesisResult> RenderWithKokoroAsync(VoiceProfile profile, string text)
    {
        if (KokoroSpeech.UnavailableReason is { } unavailable)
        {
            LogStandInOnce("kokoro-setup", NoVoiceAtAllMessage(unavailable));
            throw new InvalidOperationException($"The Kokoro voice can't speak - {unavailable}. {GetKokoroAdvice}");
        }

        try
        {
            // A long line rendered ahead - the recap, during the teaser - gets
            // the time its length needs (6 Nov 2026, see KokoroSpeech.RenderTimeoutFor).
            return new SynthesisResult(await KokoroSpeech.RenderAsync(profile, text, renderingAhead: RenderingAhead.Value), LineSource.Kokoro);
        }
        // Not a cancel: a line dropped because a flag interrupted it is the
        // queue working as designed, and reporting that as "Kokoro couldn't
        // speak" would put a fault in the console where there isn't one.
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogStandInOnce("kokoro:" + ex.Message,
                $"[voice] Kokoro couldn't speak a line ({ex.Message}) - and nothing else on this PC can, so the line was lost. "
                + "Said once for this reason, not for every line.");
            throw;
        }
    }

    /// <summary>
    /// What to press when nothing can speak. One sentence, in one place, so
    /// the console, the Mixer and Voice Settings all send a club to the same
    /// button instead of each guessing at one.
    /// </summary>
    internal const string GetKokoroAdvice =
        "Open the Voice tab and press Download under the Kokoro voice ("
        + KokoroSpeech.DownloadSizeText + ", once) - until then the announcer can't say anything at all.";

    /// <summary>
    /// The console line for an announcer that cannot speak with any voice.
    /// Deliberately loud: this is the one state where the app has nothing to
    /// say and no way to say it, and the person reading the console is the
    /// only one who can fix it.
    /// </summary>
    internal static string NoVoiceAtAllMessage(string reason) =>
        $"[voice] NO VOICE - {Capitalised(reason)}. {GetKokoroAdvice}";

    /// <summary>
    /// Whether this exact line is one of the announcer's fixed lines - a
    /// template with nothing filled into it.
    ///
    /// The relay is told, so that a line with no name in it can be kept for
    /// every club and rendered once for all of them: "Green flag!" is the
    /// same words everywhere. Anything with a driver's name is that club's
    /// alone. Worked out by looking the words up among this profile's own
    /// templates rather than by guessing at the text, because guessing
    /// wrong in one direction shares a child's name and in the other pays
    /// for the same line a hundred times.
    /// </summary>
    private bool IsFixedLine(string text)
    {
        // The SAVED profile, not the per-announcement copy a render is given:
        // a delivery copy carries the voice and the speed but no templates at
        // all (see CloneForDelivery), so asking it would answer "nothing is
        // fixed" and every club would pay for its own "Green flag!".
        var profile = _voiceStore.Current;
        var known = _fixedLines;

        if (known is null || !ReferenceEquals(known.Events, profile.Events))
        {
            // Both fields at once, as one object: this is read from pool
            // threads, and a set stored without the templates it came from
            // could be read against another profile's. Two threads building
            // it at the same time is harmless - they build the same thing.
            known = new FixedLines(profile.Events, BuildFixedLines(profile));
            _fixedLines = known;
        }

        return known.Lines.Contains(text);
    }

    /// <summary>The announcer's placeholder-free lines, and the template set they were built from.</summary>
    private sealed record FixedLines(object Events, HashSet<string> Lines);

    private volatile FixedLines? _fixedLines;

    private static HashSet<string> BuildFixedLines(VoiceProfile profile)
    {
        // Only wording that SHIPS with the app - the standard lines and the
        // built-in announcer styles - may be called shared.
        //
        // A line is shared when the relay may keep one copy of it for every
        // club, which is what makes a flag call cost nothing. That is only
        // true of words every club has: the same "Green flag!". Words
        // somebody typed are that club's, and a line somebody typed can have
        // a driver's name in it and no placeholder to give it away - a
        // child's name, in a cache outside that club's own folder. The page
        // makes the lines read-only on this voice, but a style can still
        // carry wording in (ThemeStore.Apply), so the test is on the words
        // themselves rather than on where they came from.
        var known = new HashSet<string>(StringComparer.Ordinal);

        void Learn(VoiceProfile source)
        {
            foreach (var config in source.Events.Values)
            {
                foreach (var template in config.EffectiveTemplates)
                {
                    known.Add(template);
                }
            }
        }

        Learn(VoiceProfile.CreateDefault());

        foreach (var theme in Theme.CreateBuiltInDefaults())
        {
            Learn(theme.Voice);
        }

        var lines = new HashSet<string>(StringComparer.Ordinal);

        foreach (var config in profile.Events.Values)
        {
            foreach (var template in config.EffectiveTemplates)
            {
                if (template.Contains('{') || !known.Contains(template))
                {
                    continue;
                }

                // Exactly as a line is prepared before it is spoken, so the
                // comparison is against what actually goes to the relay.
                var tidy = profile.SanitiseForModel(template.TidyTemplate());

                if (!string.IsNullOrWhiteSpace(tidy))
                {
                    lines.Add(tidy);
                }
            }
        }

        return lines;
    }

    /// <summary>True when there is a key and a voice to ask ElevenLabs with. A key sealed on another PC is no key here.</summary>
    private static bool ElevenLabsReady(VoiceProfile profile) =>
        !string.IsNullOrWhiteSpace(profile.ApiKey) && !string.IsNullOrWhiteSpace(profile.VoiceId);

    /// <summary>
    /// With ElevenLabs chosen: whether the free Kokoro voice may say what
    /// ElevenLabs can't - switched on in Voice Settings, and a Kokoro model
    /// on this PC to do it with.
    /// </summary>
    /// <remarks>
    /// With the HeatCaller voice it is not a choice: Kokoro always stands
    /// in. A club on that voice has no key of its own to fall back on, so
    /// the alternative to standing in is silence - and the allowance running
    /// out on a Saturday must not take the flag calls with it.
    ///
    /// With Kokoro itself chosen the answer is NO, and that is not an
    /// oversight: there is nothing underneath it to stand in. It used to
    /// answer yes, because the free Windows voice covered for Kokoro while
    /// the model was still being downloaded - and because of that the Mixer
    /// showed "a voice is standing in" rather than the red NO VOICE banner.
    /// Now a Kokoro that can't speak IS an announcer that can't speak, and
    /// both say so.
    /// </remarks>
    private static bool StandInAllowed(VoiceProfile profile) =>
        profile.EffectiveEngine switch
        {
            VoiceEngine.HeatCaller => KokoroSpeech.IsAvailable,
            VoiceEngine.ElevenLabs => profile.KokoroStandsIn && KokoroSpeech.IsAvailable,
            _ => false,
        };

    /// <summary>
    /// Whether this profile can say anything at all: Kokoro chosen and able
    /// to speak, or ElevenLabs set up, or the stand-in ready to cover for
    /// it. The early checks that skip a line used to test for a key and a
    /// voice only - with a free voice chosen, that would have skipped every
    /// line.
    /// </summary>
    /// <remarks>
    /// This is the gate that makes a missing Kokoro model LOUD rather than
    /// silent. Every path that speaks asks it first, and the answer being
    /// false is said in the console in words that name the button which
    /// fixes it - see SynthesizeAndPlayAsync's skipped line, and
    /// DescribeVoiceBlocker, which is what the Mixer's NO VOICE banner reads.
    /// </remarks>
    private static bool CanSpeak(VoiceProfile profile) =>
        profile.EffectiveEngine switch
        {
            VoiceEngine.Kokoro => KokoroSpeech.IsAvailable,

            // The relay may still refuse - the allowance, the club's plan -
            // but Kokoro covers every one of those, so a club on the
            // HeatCaller voice can say something as long as the model is
            // there.
            VoiceEngine.HeatCaller => profile.HeatCallerReady || KokoroSpeech.IsAvailable,
            _ => ElevenLabsReady(profile) || StandInAllowed(profile),
        };

    /// <summary>Why ElevenLabs can't be asked at all - lower case, to go inside a sentence.</summary>
    private static string NotConfiguredReason(VoiceProfile profile) =>
        profile.ApiKeyLocked ? "the ElevenLabs key is locked to the PC it was saved on"
        : string.IsNullOrWhiteSpace(profile.ApiKey) ? "no ElevenLabs API key is set"
        : "no ElevenLabs voice is selected";

    private static string Capitalised(string text) =>
        string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text[1..];

    // --- ElevenLabs' health, as the stand-in sees it ------------------------

    /// <summary>Guards the quota flag, the rest, and what has been said about them - lines on several threads update these at once.</summary>
    private readonly object _voiceHealthLock = new();

    /// <summary>True from a lost connection until ElevenLabs answers again. Lines then go straight to Kokoro.</summary>
    private bool _elevenLabsResting;

    /// <summary>
    /// The API key ElevenLabs turned down, when that is why it rests - null
    /// for a lost connection. A different key on Voice Settings ends that
    /// rest at once (see IsElevenLabsResting).
    /// </summary>
    private string? _restForKey;

    /// <summary>When the next background probe may start - see IsElevenLabsResting.</summary>
    private DateTime _nextProbeAtUtc;

    /// <summary>A probe is on its way. Never more than one.</summary>
    private bool _probeRunning;

    /// <summary>
    /// How long ElevenLabs rests after a lost connection before one line is
    /// tried through it again, in the background. A club Wi-Fi with no
    /// internet can hold a request for the whole SynthesisTimeout, and a lap
    /// call half a minute late is worthless - so for this long nothing waits
    /// on it.
    /// </summary>
    private static readonly TimeSpan ElevenLabsRestPeriod = TimeSpan.FromMinutes(1);

    private const string ReachableAgainMessage =
        "[voice] ElevenLabs is reachable again - it speaks from the next line, and the Kokoro voice stands down.";

    /// <summary>
    /// ElevenLabs answered a request, so it is working whatever the quota
    /// flag or a lost connection said.
    ///
    /// The one place "quota available again" is said. It used to be worked
    /// out around each queued line, from a count of answered requests taken
    /// before and after it - which missed every flag call and every line
    /// rendered in advance, and would have said it twice had this said it
    /// as well.
    /// </summary>
    private void NoteElevenLabsAnswered()
    {
        bool quotaBack;

        lock (_voiceHealthLock)
        {
            quotaBack = _voiceQuotaExceeded;
            _voiceQuotaExceeded = false;
            _standInLogged.Clear();
        }

        // The quota line below says ElevenLabs is back, so a rest that ended
        // quietly is not said again here - decided inside the end's own lock,
        // or a probe finishing in between could put the debt back and have it
        // said twice.
        EndElevenLabsRest(forgetOwed: quotaBack);

        if (quotaBack)
        {
            StatusMessage?.Invoke("[voice] ElevenLabs quota available again - voice announcements have resumed.");
        }
    }

    /// <summary>
    /// ElevenLabs refused a request for quota. True - and said, once - the
    /// first time; false when it was already known.
    /// </summary>
    private bool NoteQuotaExceeded(bool standingIn)
    {
        lock (_voiceHealthLock)
        {
            if (_voiceQuotaExceeded)
            {
                return false;
            }

            _voiceQuotaExceeded = true;
        }

        StatusMessage?.Invoke(standingIn
            // Saved lines are passed over too since 5 Nov 2026 - see
            // ElevenLabsSavedLinesPassedOver - so the message no longer
            // promises they play on.
            ? "[voice] ElevenLabs quota exceeded - the free Kokoro voice is standing in until it's back, each of the cast in "
              + "their own Kokoro voice. Saved ElevenLabs lines are held back too, so the whole night is in one set of voices. "
              + "ElevenLabs is still asked first each time, so it takes over again by itself the moment a request succeeds. "
              + "Top up your ElevenLabs account or wait for your monthly reset."
            : "[voice] ElevenLabs quota exceeded - voice announcements are paused for now. " +
              "Sound effects are unaffected and will keep working normally. " +
              "Top up your ElevenLabs account or wait for your monthly reset; voice will resume automatically the next time a request succeeds.");

        return true;
    }

    /// <summary>
    /// A request couldn't reach ElevenLabs - or it turned the key down
    /// (<paramref name="refusedKey"/>): rest it for a while, and say so once.
    ///
    /// Already resting after a lost connection, a key refusal makes it a rest
    /// over that key, and that is said: the request got through, so the key
    /// is what's wrong now, and a new key should end the rest at once. A lost
    /// connection reported by a line that was already on its way leaves a
    /// rest over the key as it is - either one used to swap the rest's reason
    /// silently, whichever line happened to finish last.
    /// </summary>
    private void BeginElevenLabsRest(Exception ex, string? refusedKey = null)
    {
        bool began;
        bool nowOverTheKey;

        lock (_voiceHealthLock)
        {
            began = !_elevenLabsResting;
            nowOverTheKey = !began && refusedKey is not null && _restForKey is null;
            _elevenLabsResting = true;

            if (began || refusedKey is not null)
            {
                _restForKey = refusedKey;
            }

            _nextProbeAtUtc = DateTime.UtcNow + ElevenLabsRestPeriod;
        }

        if (began && refusedKey is null)
        {
            StatusMessage?.Invoke(
                $"[voice] Can't reach ElevenLabs ({ShortReason(ex)}) - the free Kokoro voice is standing in. "
                + "Lines already saved still play in your ElevenLabs voice, and ElevenLabs is tried again in the background about once a minute.");
        }
        else if (began || nowOverTheKey)
        {
            StatusMessage?.Invoke(KeyTurnedDownMessage(ex));
        }
    }

    private static string KeyTurnedDownMessage(Exception ex) =>
        $"[voice] ElevenLabs turned the API key down ({ShortReason(ex)}) - the free Kokoro voice is standing in. "
        + "Lines already saved still play in your ElevenLabs voice. Check the key on the Voice tab: ElevenLabs is tried again "
        + "in the background about once a minute, and straight away once the key is changed.";

    /// <summary>
    /// A rest ended without "reachable again" being said - by a try that got
    /// through only to be refused, or by the key being changed - which is
    /// then said the next time ElevenLabs does speak a line. Nothing used to
    /// say it at all, and the console's last word was that the free voice
    /// was standing in, long after ElevenLabs had taken over again.
    /// </summary>
    private bool _reachableAgainOwed;

    /// <summary>
    /// ElevenLabs can be reached after all. Ends the rest, and says so if
    /// there was one - unless <paramref name="announce"/> is false, for a
    /// try that got through only to be refused, where "the Kokoro voice
    /// stands down" would be followed straight away by why it hasn't. That
    /// is then said the next time a line gets through (see
    /// _reachableAgainOwed).
    /// </summary>
    /// <param name="forgetOwed">
    /// For a caller about to say ElevenLabs is back in its own words - the
    /// quota line - so a debt owed from a quiet end is dropped rather than
    /// said as well. A rest that is ending right now is still announced: that
    /// is a different recovery (the connection, or the key) from the quota's.
    /// </param>
    private void EndElevenLabsRest(bool announce = true, bool forgetOwed = false)
    {
        bool say;

        lock (_voiceHealthLock)
        {
            var wasResting = _elevenLabsResting;
            _elevenLabsResting = false;
            _restForKey = null;

            if (announce)
            {
                say = wasResting || (_reachableAgainOwed && !forgetOwed);
                _reachableAgainOwed = false;
            }
            else
            {
                say = false;
                _reachableAgainOwed |= wasResting;
            }
        }

        if (say)
        {
            StatusMessage?.Invoke(ReachableAgainMessage);
        }
    }

    /// <summary>
    /// True while ElevenLabs is resting. <paramref name="probe"/> comes back
    /// true for at most one line per rest period: the one to try through
    /// ElevenLabs as well, in the background, to notice it coming back.
    /// </summary>
    private bool IsElevenLabsResting(VoiceProfile profile, out bool probe)
    {
        lock (_voiceHealthLock)
        {
            probe = false;

            if (!RestingForLocked(profile))
            {
                return false;
            }

            if (!_probeRunning && DateTime.UtcNow >= _nextProbeAtUtc)
            {
                _probeRunning = true;
                probe = true;
            }

            return true;
        }
    }

    /// <summary>Whether ElevenLabs is resting for this profile, without claiming the probe.</summary>
    private bool ElevenLabsRestingFor(VoiceProfile profile)
    {
        lock (_voiceHealthLock)
        {
            return RestingForLocked(profile);
        }
    }

    /// <summary>
    /// Whether ElevenLabs rests for this profile - under _voiceHealthLock. A
    /// rest over a key it turned down ends as soon as a different key is set:
    /// the new key is tried on the next line, and "reachable again" is said
    /// once it speaks one (see _reachableAgainOwed).
    /// </summary>
    private bool RestingForLocked(VoiceProfile profile)
    {
        if (!_elevenLabsResting)
        {
            return false;
        }

        if (_restForKey is not null && !string.Equals(_restForKey, profile.ApiKey, StringComparison.Ordinal))
        {
            _elevenLabsResting = false;
            _restForKey = null;
            _reachableAgainOwed = true;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Tries one line through ElevenLabs in the background while it rests.
    /// The line itself has already gone to Kokoro; this is to find out
    /// whether ElevenLabs is back - and if it is, the take is saved
    /// like any other, so that line plays in the ElevenLabs voice next time.
    /// </summary>
    private void StartElevenLabsProbe(VoiceProfile profile, string text)
    {
        _ = Task.Run(async () =>
        {
            // Its own life: not stopped with the line that happened to start it (see RenderCancel).
            RenderCancel.Value = default;

            try
            {
                // Only while the club's own ElevenLabs key is the voice:
                // Kokoro makes no requests at all, and with the HeatCaller
                // voice a request on the club's key is one it never asked
                // for.
                if (_voiceStore.Current.EffectiveEngine != VoiceEngine.ElevenLabs)
                {
                    return;
                }

                // An answer ends the rest (NoteElevenLabsAnswered).
                await SynthesizeGatedResultAsync(profile, text, reuseSaved: false);
            }
            catch (Exception ex) when (IsKeyRefusal(ex))
            {
                // It got through, and the key was turned down: another rest
                // period before the next try - and a rest over a lost
                // connection is a rest over the key from here, said once, so
                // the Mixer says why and a new key ends it at once. Only while
                // it still rests: one ended meanwhile (a new key, a line that
                // got through) is not started again by this older try.
                bool nowOverTheKey;

                lock (_voiceHealthLock)
                {
                    _nextProbeAtUtc = DateTime.UtcNow + ElevenLabsRestPeriod;
                    nowOverTheKey = _elevenLabsResting && _restForKey is null && !string.IsNullOrEmpty(profile.ApiKey);

                    if (nowOverTheKey)
                    {
                        _restForKey = profile.ApiKey;
                    }
                }

                if (nowOverTheKey)
                {
                    StatusMessage?.Invoke(KeyTurnedDownMessage(ex));
                }
            }
            catch (Exception ex) when (IsConnectionFailure(ex) || ex is SynthesisSlotTimeoutException)
            {
                // Still out of reach - or the try never got a slot, queued
                // behind requests hanging on the same dead connection, which
                // proves nothing either way: another rest period before the
                // next try. A slot that never came free used to count as an
                // answer, and ended the rest with ElevenLabs still out of
                // reach.
                lock (_voiceHealthLock)
                {
                    _nextProbeAtUtc = DateTime.UtcNow + ElevenLabsRestPeriod;
                }
            }
            catch (Exception ex)
            {
                // It answered - so it can be reached - but said no. The rest is
                // over; lines from here ask it first and are stood in for like
                // any other refusal. Quietly: saying "the Kokoro voice stands
                // down" and then why it hasn't would contradict itself.
                EndElevenLabsRest(announce: false);

                if (IsQuotaExceeded(ex))
                {
                    NoteQuotaExceeded(standingIn: true);
                }
                else
                {
                    LogStandInOnce(ReasonKey(ex), StandInRefusalMessage(ex));
                }
            }
            finally
            {
                lock (_voiceHealthLock)
                {
                    _probeRunning = false;
                }
            }
        });
    }

    /// <summary>Logs a stand-in reason the first time it happens - see _standInLogged.</summary>
    private void LogStandInOnce(string key, string message)
    {
        bool first;

        lock (_voiceHealthLock)
        {
            first = _standInLogged.Add(key);
        }

        if (first)
        {
            StatusMessage?.Invoke(message);
        }
    }

    private static string StandInRefusalMessage(Exception ex) =>
        $"[voice] ElevenLabs couldn't speak a line ({ShortReason(ex)}) - the free Kokoro voice is saying the lines it can't. "
        + "Said once for this reason, not for every line.";

    /// <summary>
    /// A failure to REACH ElevenLabs - no internet, no DNS, a dropped
    /// connection, a request that never came back - as opposed to it
    /// answering "no". Checked down the inner exceptions, which is where
    /// HttpClient puts the socket error.
    ///
    /// EndOfStreamException is an IOException too, but it comes from
    /// decoding audio that arrived cut short, not from the network - a
    /// minute of rest for that would be the wrong answer.
    /// </summary>
    private static bool IsConnectionFailure(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            // This laptop's own queue for a slot, not the network.
            if (e is SynthesisSlotTimeoutException)
            {
                return false;
            }

            if (e is HttpRequestException or System.Net.Sockets.SocketException
                or (IOException and not EndOfStreamException)
                or TimeoutException or OperationCanceledException)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// ElevenLabs turning the API key down - a 401 that is not about the
    /// quota (ElevenLabs sends its quota refusal as a 401 too, which
    /// IsQuotaExceeded tells apart). A wrong or revoked key, typically:
    /// every line would be refused the same way.
    /// </summary>
    private static bool IsKeyRefusal(Exception ex) =>
        !IsQuotaExceeded(ex)
        && System.Text.RegularExpressions.Regex.IsMatch(ex.Message ?? string.Empty, @"\(401\)");

    /// <summary>The readable part of a failure, for one log line: ElevenLabs' own message, with its status code, when it sent one.</summary>
    private static string ShortReason(Exception ex)
    {
        var raw = ex.Message ?? string.Empty;
        var message = System.Text.RegularExpressions.Regex.Match(raw, "\"message\"\\s*:\\s*\"([^\"]+)\"");
        var code = System.Text.RegularExpressions.Regex.Match(raw, @"\((\d{3})\)");

        if (message.Success)
        {
            return code.Success ? $"{code.Groups[1].Value} - {message.Groups[1].Value}" : message.Groups[1].Value;
        }

        return raw.Length <= 120 ? raw : raw[..117] + "...";
    }

    /// <summary>What makes two refusals "the same reason" for LogStandInOnce: the HTTP status, or failing that the kind of failure.</summary>
    private static string ReasonKey(Exception ex)
    {
        var code = System.Text.RegularExpressions.Regex.Match(ex.Message ?? string.Empty, @"\((\d{3})\)");
        return code.Success ? $"http-{code.Groups[1].Value}" : ex.GetType().Name;
    }

    /// <summary>
    /// Why the free Kokoro voice is standing in for this profile right now,
    /// or null when it isn't: ElevenLabs chosen with the stand-in on, and
    /// ElevenLabs unable to speak - not set up, over quota, or resting after
    /// a lost connection. A refusal of one line (a bad key, a server error)
    /// is not a state, so it doesn't count here.
    /// </summary>
    /// <remarks>
    /// Kokoro chosen is always null, because StandInAllowed says no for it:
    /// Kokoro is what stands in, so it cannot stand in for itself. A Kokoro
    /// that can't speak is reported as a BLOCKER instead - see
    /// DescribeVoiceBlocker - which is what puts the Mixer's red NO VOICE
    /// banner up rather than a mild notice that a voice is covering.
    /// </remarks>
    private string? StandInReasonFor(VoiceProfile profile)
    {
        if (!StandInAllowed(profile))
        {
            return null;
        }

        if (profile.EffectiveEngine == VoiceEngine.HeatCaller)
        {
            if (!profile.HeatCallerReady)
            {
                return "no HeatCaller voice is chosen";
            }

            lock (_heatCallerLock)
            {
                // The reason IS the rest. The clock beside it only says when
                // the next background try may go, and it runs out every rest
                // period by design - reading that instead had the Mixer say
                // "Ready" between tries while every line went to the free
                // voice, and had every prepared stand-in take stop matching
                // and be rendered again on each flag.
                return _heatCallerReason;
            }
        }

        if (!ElevenLabsReady(profile))
        {
            return NotConfiguredReason(profile);
        }

        if (_voiceQuotaExceeded)
        {
            return "the ElevenLabs quota is used up";
        }

        // The usage chip at 100% (5 Nov 2026) - see ElevenLabsAllowanceUsedUp.
        if (ElevenLabsMeterSpent())
        {
            return "the ElevenLabs allowance is at 100%";
        }

        // One look, under one lock: taken twice, the rest could end in
        // between and still be described.
        lock (_voiceHealthLock)
        {
            if (!RestingForLocked(profile))
            {
                return null;
            }

            return _restForKey is not null ? "ElevenLabs turned the API key down" : "ElevenLabs can't be reached";
        }
    }

    /// <summary>
    /// Whether a take rendered earlier is still the one to play.
    ///
    /// The voice and settings must match (the signature, which includes the
    /// engine and the Kokoro voice). And a take Kokoro made while standing
    /// in counts only while it is STILL standing in: once ElevenLabs can
    /// speak again the take is passed over, the line is spoken live in
    /// ElevenLabs, and it is prepared again in ElevenLabs the next time
    /// lines are primed - rather than that flag staying in the free voice
    /// for the rest of the night.
    ///
    /// The other way round for the HeatCaller voice's spent allowance: a take
    /// in the HeatCaller voice - rendered by the relay, or read from the
    /// saved lines - stops counting once the month's characters are gone, so
    /// a flag or release call prepared earlier in the evening is not the one
    /// line in the HeatCaller voice on a night the rest is in the Kokoro
    /// voice. See HeatCallerSavedLinesPassedOver.
    /// </summary>
    /// <remarks>
    /// And the same for ElevenLabs' own allowance (5 Nov 2026): a take
    /// ElevenLabs rendered, or one read from the saved lines, stops counting
    /// while its quota is used up - see ElevenLabsSavedLinesPassedOver.
    /// </remarks>
    private bool PreparedTakeMatches(string takeSignature, LineSource takeSource, string signature, VoiceProfile profile) =>
        takeSignature == signature
        && (takeSource != LineSource.StandIn || StandInReasonFor(profile) is not null)
        && !((takeSource is LineSource.HeatCaller or LineSource.Library) && HeatCallerSavedLinesPassedOver(profile))
        && !((takeSource is LineSource.ElevenLabs or LineSource.Library) && ElevenLabsSavedLinesPassedOver(profile));

    /// <summary>
    /// Whether saved ElevenLabs takes are passed over for this profile right
    /// now: ElevenLabs chosen, Kokoro standing in for it, and its allowance
    /// used up - the usage chip at 100%, or a quota refusal (see
    /// ElevenLabsAllowanceUsedUp). The one rule behind the saved-line and recorded-line checks in
    /// RenderLineAsync, the release calls read straight from the library,
    /// and the prepared flag and release takes.
    ///
    /// NO SAVED ELEVENLABS ONCE IT KICKS OVER TO KOKORO (5 Nov 2026, asked
    /// for by Asher: "when it kicks over to kokoro no saved elevenlabs should
    /// play for continuity"). Saved lines used to play on in the ElevenLabs
    /// voice while every new line was Kokoro's, so a night swapped voices
    /// mid-sentence depending on which lines it had said before. The same
    /// rule the HeatCaller voice has had for its spent allowance
    /// (HeatCallerSavedLinesPassedOver).
    ///
    /// Only the quota: while ElevenLabs merely can't be reached, saved lines
    /// still play - that is a blip, not the rest of the night. And never
    /// without the stand-in: with nothing to say a line instead, a saved
    /// take is better than silence. The quota coming back - the next answer
    /// from ElevenLabs, NoteElevenLabsAnswered - ends it by itself.
    /// </summary>
    private bool ElevenLabsSavedLinesPassedOver(VoiceProfile profile) =>
        PassesOverSavedElevenLabs(profile, ElevenLabsAllowanceUsedUp(profile), KokoroSpeech.IsAvailable);

    /// <summary>
    /// Whether the ElevenLabs meter on the usage chip says the club's own
    /// key is at 100% this month (UsageMeters, MeterKind.ElevenLabsKey -
    /// the chip's percentage rounds down, so 100% there is exactly Spent).
    /// Settable so the tests can say so without a plan to read; the app
    /// never sets it.
    /// </summary>
    internal Func<bool> ElevenLabsMeterSpent { get; set; } =
        () => UsageMeters.Shared.Get(MeterKind.ElevenLabsKey) is { Spent: true };

    /// <summary>Whether "the chip says 100%" was last said - so it is said once going in and once coming out.</summary>
    private bool _meterSpentSaid;

    /// <summary>
    /// THE USAGE CHIP AT 100% IS WHEN IT KICKS OVER (5 Nov 2026, Asher: "it
    /// should be usage chip 100%"). With the ElevenLabs meter used up, every
    /// line goes to Kokoro - each of the cast in their own stand-in voice -
    /// without ElevenLabs being asked at all, and no saved ElevenLabs line
    /// plays. ElevenLabs may well go on answering past the plan's figure
    /// (overage), which is exactly why the refusal could not be the trigger:
    /// the chip is the club's own line in the sand. The meter is read again
    /// every few minutes (UsageMeters.RefreshAsync), so a new month or a
    /// bigger plan brings ElevenLabs, and its saved lines, back by itself.
    ///
    /// A quota refusal from ElevenLabs still counts as well - the meter can
    /// be a few minutes behind, or unreadable for this key - so a night that
    /// has actually run out is one voice either way.
    /// </summary>
    private bool ElevenLabsAllowanceUsedUp(VoiceProfile profile)
    {
        if (profile.EffectiveEngine != VoiceEngine.ElevenLabs)
        {
            return false;
        }

        var spent = ElevenLabsMeterSpent();
        bool say;

        lock (_voiceHealthLock)
        {
            say = spent != _meterSpentSaid;
            _meterSpentSaid = spent;
        }

        if (say)
        {
            StatusMessage?.Invoke(spent
                ? "[voice] The ElevenLabs allowance is at 100% on the usage chip - the free Kokoro voice is speaking from here, each of "
                  + "the cast in their own Kokoro voice, and saved ElevenLabs lines are held back so the night is in one set of voices. "
                  + "ElevenLabs speaks again once the chip is under 100% (a new month or a bigger plan)."
                : "[voice] The ElevenLabs allowance is under 100% again - ElevenLabs and its saved lines speak from the next line.");
        }

        return spent || _voiceQuotaExceeded;
    }

    /// <summary>
    /// The rule behind ElevenLabsSavedLinesPassedOver, with what it reads
    /// from this PC and the service handed in, so it can be tested without
    /// either: ElevenLabs chosen, its quota used up, and Kokoro standing in
    /// for it (ticked on the Voice tab and able to speak here).
    /// </summary>
    internal static bool PassesOverSavedElevenLabs(VoiceProfile profile, bool quotaUsedUp, bool kokoroReady) =>
        profile.EffectiveEngine == VoiceEngine.ElevenLabs
        && quotaUsedUp
        && profile.KokoroStandsIn
        && kokoroReady;
}
