using NAudio.Wave;

namespace HeatCaller;

/// <summary>
/// Turning a line of text into audio: the synthesis gate, ElevenLabs with
/// its rest and probe, the Windows stand-in, and the reasons given when a
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
    /// each caller being well behaved. (The Windows voice has a gate of its
    /// own - see WindowsVoice - and never waits on this one.)
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
                "The HeatCaller voice can't be tested from here. Choose \"My own ElevenLabs account\" on Voice Settings to test a voice you pay for.");
        }

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

        /// <summary>The Windows voice, because it is the voice chosen on Voice Settings.</summary>
        Windows,

        /// <summary>The Windows voice standing in for ElevenLabs, which couldn't speak the line.</summary>
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

    private static async Task<SynthesisResult> SynthesizeOrReuseAsync(ElevenLabsClient client, VoiceProfile profile, string text, bool reuseSaved)
    {
        // Before the gate: a saved line needs no slot, and must not wait
        // behind a render for one.
        if (reuseSaved && await VoiceLibrary.Shared.TryLoadAsync(profile, text) is { } saved)
        {
            return new SynthesisResult(saved, LineSource.Library);
        }

        using var timeout = new CancellationTokenSource(SynthesisTimeout);
        try
        {
            await SynthesisGate.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new SynthesisSlotTimeoutException($"No synthesis slot came free within {SynthesisTimeout.TotalSeconds:0} s.");
        }

        byte[] audio;
        try
        {
            audio = await client.SynthesizeAsync(profile, text, timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException($"ElevenLabs didn't respond within {SynthesisTimeout.TotalSeconds:0} s.");
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

    private async Task<ElevenLabsClient.TimedSpeech?> SynthesizeTimedGatedAsync(VoiceProfile profile, string text)
    {
        using var timeout = new CancellationTokenSource(SynthesisTimeout);
        try
        {
            await SynthesisGate.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new SynthesisSlotTimeoutException($"No synthesis slot came free within {SynthesisTimeout.TotalSeconds:0} s.");
        }

        try
        {
            var timed = await _elevenLabs.SynthesizeWithTimestampsAsync(profile, text, timeout.Token);

            if (timed is not null)
            {
                NoteElevenLabsAnswered();
            }

            return timed;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            throw new TimeoutException($"ElevenLabs didn't respond within {SynthesisTimeout.TotalSeconds:0} s.");
        }
        finally
        {
            SynthesisGate.Release();
        }
    }

    /// <summary>
    /// Upper bound on one gated synthesis, gate wait included. Shorter than
    /// ElevenLabsClient's own 35 s HttpClient timeout so this, with its
    /// clearer message, is normally the one that fires. Long enough for a
    /// full race recap on the slower v3 model; short enough that a stalled
    /// connection no longer holds a synthesis slot for the old 100 s.
    /// </summary>
    private static readonly TimeSpan SynthesisTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Waited the whole SynthesisTimeout for one of the ElevenLabs slots and
    /// never got one: this laptop's own requests were queued up (a long recap
    /// on a slow model, say), not the network being down - so the stand-in
    /// says the line, but ElevenLabs is not rested for it (see
    /// IsConnectionFailure).
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
    // error, so they call SynthesizeThroughGateAsync (or the Windows voice)
    // themselves - and nor does Prepare Lines, which fills the saved-voice
    // library with ElevenLabs lines and nothing else.

    /// <summary>
    /// The audio for one race or announcer line, from whichever voice should
    /// say it:
    ///
    ///  - Windows voice chosen: the Windows voice, always. No ElevenLabs
    ///    request is made, and the saved-voice library is neither read nor
    ///    written - it is keyed by the ElevenLabs settings, and a Windows take
    ///    saved there would later play as though it were the ElevenLabs line.
    ///  - ElevenLabs chosen, stand-in off (or no Windows voice on this PC):
    ///    ElevenLabs exactly as it always was - the saved line, otherwise a
    ///    request - and a failure is thrown to the caller as before.
    ///  - ElevenLabs chosen, stand-in on: a saved line plays from disk
    ///    whatever state ElevenLabs is in. With no key or voice to ask with,
    ///    or while ElevenLabs rests after a lost connection, the Windows voice
    ///    says it straight away. Otherwise ElevenLabs is asked, and when it
    ///    can't - quota, connection, any refusal - the Windows voice says the
    ///    same line, so a flag call is never silent.
    ///
    /// Throws only when no voice could say it.
    /// </summary>
    /// <param name="fresh">
    /// ElevenLabs only: pass over the saved-voice library and ask for a new
    /// take, which replaces the saved one only once it has come back (see
    /// SynthesizeOrReuseAsync). For Re-record all - see RecordVoiceLineAsync.
    /// </param>
    private async Task<SynthesisResult> RenderLineAsync(VoiceProfile profile, string text, bool fresh = false)
    {
        // A line recorded for a soundscape plays as recorded, whichever
        // voice is chosen - that is the point of recording it (see
        // StingerLibrary). Checked first, so it needs no key, no relay and
        // no internet.
        if (StingerLibrary.Shared.TryLoad(text) is { } recorded)
        {
            return new SynthesisResult(recorded, LineSource.Library);
        }

        if (profile.EffectiveEngine == VoiceEngine.Windows)
        {
            return await RenderWithWindowsAsync(profile, text, LineSource.Windows);
        }

        if (profile.EffectiveEngine == VoiceEngine.Kokoro)
        {
            return await RenderWithKokoroAsync(profile, text);
        }

        if (profile.EffectiveEngine == VoiceEngine.HeatCaller)
        {
            return await RenderWithHeatCallerAsync(profile, text);
        }

        if (!StandInAllowed(profile))
        {
            return await SynthesizeGatedResultAsync(profile, text, reuseSaved: !fresh);
        }

        if (!fresh && await VoiceLibrary.Shared.TryLoadAsync(profile, text) is { } saved)
        {
            return new SynthesisResult(saved, LineSource.Library);
        }

        if (!ElevenLabsReady(profile))
        {
            LogStandInOnce("setup", $"[voice] {Capitalised(NotConfiguredReason(profile))} - the free Windows voice is speaking instead.");
            return await RenderWithWindowsAsync(profile, text, LineSource.StandIn);
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

            return await RenderWithWindowsAsync(profile, text, LineSource.StandIn);
        }

        try
        {
            // The library was asked a moment ago.
            return await SynthesizeGatedResultAsync(profile, text, reuseSaved: false);
        }
        catch (Exception ex) when (IsConnectionFailure(ex))
        {
            BeginElevenLabsRest(ex);
            return await RenderWithWindowsAsync(profile, text, LineSource.StandIn);
        }
        catch (Exception ex) when (IsQuotaExceeded(ex))
        {
            // ElevenLabs is still asked first on every line - the refusal is
            // quick - so the quota coming back is noticed straight away.
            NoteQuotaExceeded(standingIn: true);
            return await RenderWithWindowsAsync(profile, text, LineSource.StandIn);
        }
        catch (Exception ex) when (IsKeyRefusal(ex))
        {
            // The key turned down: every line would be turned down the same
            // way, so ElevenLabs rests - lines, flags included, go straight
            // to the Windows voice, rather than each waiting for the same
            // refusal first - until a background try gets through, or the
            // key is changed on Voice Settings.
            BeginElevenLabsRest(ex, refusedKey: profile.ApiKey);
            return await RenderWithWindowsAsync(profile, text, LineSource.StandIn);
        }
        catch (Exception ex)
        {
            // A bad key, too many requests, a server error: the Windows voice
            // takes this line, and the reason is said once, not every line.
            LogStandInOnce(ReasonKey(ex), StandInRefusalMessage(ex));
            return await RenderWithWindowsAsync(profile, text, LineSource.StandIn);
        }
    }

    private static async Task<SynthesisResult> RenderWithWindowsAsync(VoiceProfile profile, string text, LineSource source) =>
        new SynthesisResult(await WindowsVoice.RenderAsync(profile, text), source);

    /// <summary>
    /// A line in the Kokoro voice, on this PC. When Kokoro can't say it - not
    /// downloaded yet, the model wouldn't load, a render that stuck - the
    /// Windows voice says it instead, as it does for ElevenLabs and the
    /// HeatCaller voice, so a flag call is never silent. The reason is said
    /// once, not every line. Throws only when neither can speak.
    /// </summary>
    private async Task<SynthesisResult> RenderWithKokoroAsync(VoiceProfile profile, string text)
    {
        if (KokoroSpeech.UnavailableReason is { } unavailable)
        {
            if (!WindowsVoice.IsAvailable)
            {
                throw new InvalidOperationException($"The Kokoro voice can't speak - {unavailable}.");
            }

            LogStandInOnce("kokoro-setup", $"[voice] {Capitalised(unavailable)} - the free Windows voice is speaking instead.");
            return await RenderWithWindowsAsync(profile, text, LineSource.StandIn);
        }

        try
        {
            return new SynthesisResult(await KokoroSpeech.RenderAsync(profile, text), LineSource.Kokoro);
        }
        catch (Exception ex) when (WindowsVoice.IsAvailable)
        {
            LogStandInOnce("kokoro:" + ex.Message,
                $"[voice] Kokoro couldn't speak a line ({ex.Message}) - the free Windows voice said it. Said once for this reason, not for every line.");
            return await RenderWithWindowsAsync(profile, text, LineSource.StandIn);
        }
    }

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
    /// With ElevenLabs chosen: whether the Windows voice may say what
    /// ElevenLabs can't - switched on in Voice Settings, and a Windows voice
    /// on this PC to do it with.
    /// </summary>
    /// <remarks>
    /// With the HeatCaller voice it is not a choice: the Windows voice
    /// always stands in. A club on that voice has no key of its own to fall
    /// back on, so the alternative to standing in is silence - and the
    /// allowance running out on a Saturday must not take the flag calls
    /// with it.
    /// </remarks>
    private static bool StandInAllowed(VoiceProfile profile) =>
        profile.EffectiveEngine switch
        {
            VoiceEngine.HeatCaller => WindowsVoice.IsAvailable,
            VoiceEngine.ElevenLabs => profile.WindowsVoiceStandsIn && WindowsVoice.IsAvailable,

            // Like the HeatCaller voice, not a choice: a club on Kokoro with
            // the model not yet downloaded would otherwise hear nothing.
            VoiceEngine.Kokoro => WindowsVoice.IsAvailable,
            _ => false,
        };

    /// <summary>
    /// Whether this profile can say anything at all: the Windows voice chosen
    /// and able to speak, or ElevenLabs set up, or the stand-in ready to cover
    /// for it. The early checks that skip a line used to test for a key and a
    /// voice only - with the Windows voice chosen, that would have skipped
    /// every line.
    /// </summary>
    private static bool CanSpeak(VoiceProfile profile) =>
        profile.EffectiveEngine switch
        {
            VoiceEngine.Windows => WindowsVoice.IsAvailable,
            VoiceEngine.Kokoro => KokoroSpeech.IsAvailable || WindowsVoice.IsAvailable,

            // The relay may still refuse - the allowance, the club's plan -
            // but the Windows voice covers every one of those, so a club on
            // the HeatCaller voice can always say something.
            VoiceEngine.HeatCaller => profile.HeatCallerReady || WindowsVoice.IsAvailable,
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

    /// <summary>True from a lost connection until ElevenLabs answers again. Lines then go straight to the Windows voice.</summary>
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
        "[voice] ElevenLabs is reachable again - it speaks from the next line, and the Windows voice stands down.";

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
            ? "[voice] ElevenLabs quota exceeded - the free Windows voice is standing in until it's back. "
              + "Lines already saved still play in your ElevenLabs voice, and ElevenLabs is still asked first each time, "
              + "so it takes over again by itself the moment a request succeeds. Top up your ElevenLabs account or wait for your monthly reset."
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
                $"[voice] Can't reach ElevenLabs ({ShortReason(ex)}) - the free Windows voice is standing in. "
                + "Lines already saved still play in your ElevenLabs voice, and ElevenLabs is tried again in the background about once a minute.");
        }
        else if (began || nowOverTheKey)
        {
            StatusMessage?.Invoke(KeyTurnedDownMessage(ex));
        }
    }

    private static string KeyTurnedDownMessage(Exception ex) =>
        $"[voice] ElevenLabs turned the API key down ({ShortReason(ex)}) - the free Windows voice is standing in. "
        + "Lines already saved still play in your ElevenLabs voice. Check the key on Voice Settings: ElevenLabs is tried again "
        + "in the background about once a minute, and straight away once the key is changed.";

    /// <summary>
    /// A rest ended without "reachable again" being said - by a try that got
    /// through only to be refused, or by the key being changed - which is
    /// then said the next time ElevenLabs does speak a line. Nothing used to
    /// say it at all, and the console's last word was that the Windows voice
    /// was standing in, long after ElevenLabs had taken over again.
    /// </summary>
    private bool _reachableAgainOwed;

    /// <summary>
    /// ElevenLabs can be reached after all. Ends the rest, and says so if
    /// there was one - unless <paramref name="announce"/> is false, for a
    /// try that got through only to be refused, where "the Windows voice
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
    /// The line itself has already gone to the Windows voice; this is to
    /// find out whether ElevenLabs is back - and if it is, the take is saved
    /// like any other, so that line plays in the ElevenLabs voice next time.
    /// </summary>
    private void StartElevenLabsProbe(VoiceProfile profile, string text)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                // Only while the club's own ElevenLabs key is the voice:
                // the Windows voice makes no requests at all, and with the
                // HeatCaller voice a request on the club's key is one it
                // never asked for.
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
                // any other refusal. Quietly: saying "the Windows voice stands
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
        $"[voice] ElevenLabs couldn't speak a line ({ShortReason(ex)}) - the free Windows voice is saying the lines it can't. "
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
    /// Why the Windows voice is standing in for this profile right now, or
    /// null when it isn't: ElevenLabs chosen with the stand-in on, and
    /// ElevenLabs unable to speak - not set up, over quota, or resting after
    /// a lost connection. A refusal of one line (a bad key, a server error)
    /// is not a state, so it doesn't count here.
    /// </summary>
    private string? StandInReasonFor(VoiceProfile profile)
    {
        if (!StandInAllowed(profile))
        {
            return null;
        }

        // Kokoro stands aside for the Windows voice while it can't speak at
        // all on this PC - not downloaded, or a model that wouldn't load. A
        // single line that failed is not a state, as for ElevenLabs. The load
        // failure is the one kept until a load works: UnavailableReason lets
        // it go between tries, which had the Mixer say "Ready - Kokoro voice"
        // while the Windows voice said every line (as for the HeatCaller
        // voice below).
        if (profile.EffectiveEngine == VoiceEngine.Kokoro)
        {
            return KokoroSpeech.UnavailableReason ?? KokoroSpeech.LoadFailure;
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
                // "Ready" between tries while every line went to the Windows
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
    /// engine and the Windows voice). And a take the Windows voice made while
    /// standing in counts only while it is STILL standing in: once ElevenLabs
    /// can speak again the take is passed over, the line is spoken live in
    /// ElevenLabs, and it is prepared again in ElevenLabs the next time lines
    /// are primed - rather than that flag staying in the Windows voice for the
    /// rest of the night.
    ///
    /// The other way round for the HeatCaller voice's spent allowance: a take
    /// in the HeatCaller voice - rendered by the relay, or read from the
    /// saved lines - stops counting once the month's characters are gone, so
    /// a flag or release call prepared earlier in the evening is not the one
    /// line in the HeatCaller voice on a night the rest is in the Windows
    /// voice. See HeatCallerSavedLinesPassedOver.
    /// </summary>
    private bool PreparedTakeMatches(string takeSignature, LineSource takeSource, string signature, VoiceProfile profile) =>
        takeSignature == signature
        && (takeSource != LineSource.StandIn || StandInReasonFor(profile) is not null)
        && !((takeSource is LineSource.HeatCaller or LineSource.Library) && HeatCallerSavedLinesPassedOver(profile));
}
