using NAudio.Wave;

namespace HeatCaller;

/// <summary>
/// Turns race events into audio: spoken announcements via ElevenLabs TTS or
/// the free Windows voice (see RenderLineAsync for which speaks when),
/// and short local sound effects for lap feedback (good/invalid lap,
/// consistency streaks) in the style of Next Level Timing's own per-lap
/// audio cues. Driven by the app's race-control layer, whichever decoder
/// (EasyLap or I-Lap) feeds it - see the public Notify*/On* methods.
///
/// Sound effects and voice lines run on two SEPARATE queues, each playing
/// one item at a time. They used to share a single queue, which caused a
/// real problem on fast tracks: a spoken sentence takes several seconds to
/// synthesize and play, so on a track with 5-7 second laps, voice
/// announcements would back up and short sound effects (meant to be
/// near-instant lap feedback) would get stuck behind them, sometimes
/// audibly playing many seconds after the lap they were for.
///
/// The voice queue also "supersedes" stale pending announcements: if a new
/// streak announcement for a transponder is queued while an OLDER streak
/// announcement for that SAME transponder is still waiting (not yet
/// speaking), the old one is dropped. Otherwise, on a fast track, a driver
/// who racks up 4 more laps before the "3 lap streak" line even starts
/// playing would keep hearing outdated streak counts read out one after
/// another, well after they'd actually stopped.
/// </summary>
public sealed class AnnouncementService
{
    private readonly VoiceSettingsStore _voiceStore;
    private readonly ProfileStore _profiles;
    private readonly SoundEffectsStore _soundStore;
    private readonly RivalryStore? _rivalries;
    private readonly ElevenLabsClient _elevenLabs = new();

    private sealed record QueuedVoiceItem(AnnouncementEventType Type, string? Transponder, Func<Task> Work);

    private readonly List<QueuedVoiceItem> _voiceQueue = new();
    private readonly object _voiceQueueLock = new();
    private readonly SemaphoreSlim _voiceQueueSignal = new(0);

    /// <summary>
    /// One queued sound effect. The kind and the time it was queued travel
    /// with it so that a lap chime which has been waiting too long can be
    /// dropped instead of played late, and so a flag clearing the decks can
    /// tell lap chimes (binned) from the race's own start and finish sounds
    /// (kept).
    ///
    /// <see cref="Finished"/> is signalled however the sound leaves the
    /// queue - played, cut off, dropped as stale, cleared by a flag or
    /// thrown away by a mute. A call waiting for its lead-in sound (the
    /// finish call waits for the fanfare) used to wait out its whole
    /// timeout whenever the sound was discarded rather than played, and
    /// then spoke anyway - after a mute, too.
    /// </summary>
    private sealed record QueuedSound(SoundEffectKind Kind, DateTime QueuedAtUtc, Func<Task> Work, TaskCompletionSource? Finished);

    /// <summary>
    /// The sound effect being played right now (or waiting for the speaker
    /// as the sound queue's current item), so a flag call or a mute can cut
    /// it off. Voice lines could always be stopped; sounds held the speaker
    /// for their whole length, so a long custom start or finish file kept a
    /// yellow waiting for seconds.
    /// </summary>
    private CancellationTokenSource? _playingSoundCancel;
    private SoundEffectKind _playingSoundKind;
    private readonly object _playingSoundLock = new();

    private readonly List<QueuedSound> _soundQueue = new();
    private readonly object _soundQueueLock = new();

    /// <summary>
    /// How old a lap chime may be and still be worth playing.
    ///
    /// Chimes share the speaker with the voice, one thing at a time, and
    /// each carries almost a second of lead-in silence for the device to
    /// wake up. On a busy track they arrive faster than they can play, and
    /// without a limit the backlog grew minutes behind the racing - a chime
    /// for a lap nobody remembers, and a start or recap that kept waiting
    /// for "audio still pending" that was only stale chimes.
    /// </summary>
    private static readonly TimeSpan PerLapSoundMaxAge = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Most lap chimes allowed to wait at once. When another arrives the
    /// oldest waiting one is dropped - the newest lap is the one worth
    /// hearing. Race start, finish and other one-off sounds never count
    /// towards this and are never dropped by it.
    /// </summary>
    private const int MaxQueuedPerLapSounds = 3;
    private readonly SemaphoreSlim _soundQueueSignal = new(0);

    /// <summary>
    /// Guards the actual moment of audio OUTPUT (not the whole voice/sound
    /// work item). Splitting voice and sound into two independent queues
    /// fixed the backlog problem, but it introduced a new one: with two
    /// separate background loops, a voice line and a sound effect could end
    /// up calling WaveOutEvent.Play() on two separate instances at the
    /// literal same moment. Many audio drivers don't handle that cleanly -
    /// one stream can get silently dropped, or both can clip where they
    /// overlap. This lock only wraps the final playback call in each path
    /// (not voice synthesis, which can still happen over the network
    /// without blocking anything), so only one thing is ever truly coming
    /// out of the speaker at a time.
    /// </summary>
    private readonly SemaphoreSlim _audioPlaybackLock = new(1, 1);

    // Local tracking purely for leader-change detection (NLT sends no such
    // event itself - see VoiceModels.cs remarks on AnnouncementEventType.LeaderChange).
    private readonly Dictionary<string, RaceStanding> _standings = new();

    // Tracks which transponder was AHEAD last time each rivalry (keyed by
    // RivalryLine.Id) was checked, so an overtake announcement fires only
    // on the tick where the order actually flips - not on every standings
    // update while one driver stays ahead of the other.
    private readonly Dictionary<string, string> _rivalryLastAhead = new();

    /// <summary>How many laps down each driver has already been announced as being, so "lapped" is called once per lap lost rather than on every update.</summary>
    private readonly Dictionary<string, int> _lappedAnnounced = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Pairs already called as a close battle this race - a scrap lasts many laps and shouldn't be announced on every one of them.</summary>
    private readonly HashSet<string> _battlesAnnounced = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Every driver's CURRENT clean run and when it was last updated, so a
    /// streak can be compared against the rest of the field rather than
    /// announced in isolation.
    ///
    /// The timestamp matters: a car that stops (battery, breakage, packed up
    /// for the night) keeps whatever run it was on forever, and without an
    /// expiry that frozen number would go on suppressing everyone else's
    /// calls for the rest of the race.
    /// </summary>
    private readonly Dictionary<string, (int Count, DateTime AtUtc)> _currentStreaks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// How many ElevenLabs requests may be in flight at once.
    ///
    /// Their accounts carry a hard CONCURRENCY limit - three on this one -
    /// and going over it doesn't queue, it returns 429 and the line is
    /// simply lost. Staging used to fire its own line at the same moment as
    /// five flag lines being rendered in advance: six requests against a
    /// limit of three, so three succeeded and three came back 429. The
    /// staging call was one of the unlucky ones, which is why it went
    /// missing while the flag lines that beat it were "ready to fire
    /// instantly".
    ///
    /// Two rather than three deliberately: it leaves a slot spare for
    /// whatever else might be talking to the API - a voice test on the
    /// settings page, a retry - so a race can't be the thing that tips it
    /// over.
    /// </summary>
    public const int MaxConcurrentSynthesis = 2;

    private static readonly SemaphoreSlim SynthesisGate = new(MaxConcurrentSynthesis, MaxConcurrentSynthesis);

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
    private async Task<SynthesisResult> RenderLineAsync(VoiceProfile profile, string text)
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

        if (profile.EffectiveEngine == VoiceEngine.HeatCaller)
        {
            return await RenderWithHeatCallerAsync(profile, text);
        }

        if (!StandInAllowed(profile))
        {
            return await SynthesizeGatedResultAsync(profile, text);
        }

        if (await VoiceLibrary.Shared.TryLoadAsync(profile, text) is { } saved)
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

    // --- The HeatCaller voice ----------------------------------------------
    //
    // ElevenLabs on HeatCaller's own account, asked for through the club's
    // relay (see HeatCallerVoice.cs). The club pays nothing and holds no
    // key, so the rules are different from a club's own key in one way that
    // matters: when the month's characters are gone, EVERY line switches to
    // the free Windows voice, saved ones included, until the allowance
    // starts again. A night that is half one voice and half another is what
    // a club hears as "the announcer is broken"; one voice, with the reason
    // in the log and on the Mixer, is honest. Nothing is thrown away - the
    // saved lines are all still there next month.

    /// <summary>Set by the host: how this laptop asks its relay to speak. Null when the app has not wired one (tests, and the first moments of startup).</summary>
    internal HeatCallerVoiceClient? HeatCallerVoice { get; set; }

    private readonly object _heatCallerLock = new();

    /// <summary>Why the HeatCaller voice isn't speaking, or null while it is. Lower case, to sit inside a sentence.</summary>
    private string? _heatCallerReason;

    /// <summary>Nothing is sent to the relay before this. See the rest periods below.</summary>
    private DateTime _heatCallerRetryUtc;

    /// <summary>A probe is on its way. Never more than one.</summary>
    private bool _heatCallerProbeRunning;

    /// <summary>Where the allowance stood when the relay last said. For the Voice Settings page and the log.</summary>
    private HeatCallerAllowance? _heatCallerAllowance;

    /// <summary>Whether the "nearly spent" warning has been said for this month's allowance.</summary>
    private bool _heatCallerAllowanceWarned;

    /// <summary>Which month that warning was said for - the relay's own reset date, which moves exactly once a month.</summary>
    private DateTime? _heatCallerAllowanceResets;

    /// <summary>
    /// Latched while the month's characters are spent, and cleared only by a
    /// line the relay actually RENDERED.
    ///
    /// Its own field rather than a reading of the last refusal: the relay is
    /// asked again every half hour, and a Wi-Fi blip on one of those tries
    /// used to replace "the allowance is gone" with "can't be reached",
    /// which let saved lines play in the HeatCaller voice again for the rest
    /// of the month - half the night in one voice and half in the other,
    /// which is the whole thing this is here to prevent. A line the relay
    /// served from its own cache does not clear it either: that costs
    /// nothing and says nothing about the allowance.
    /// </summary>
    private bool _heatCallerAllowanceSpent;

    /// <summary>
    /// Until when the spent allowance holds - the relay's own reset date when
    /// it said one, otherwise the start of next month. Kept beside the flag
    /// so it can be remembered across a restart (see VoiceSettingsStore):
    /// a laptop restarted mid-night used to forget the allowance was gone
    /// and play every saved line in the HeatCaller voice again.
    /// </summary>
    private DateTime? _heatCallerSpentUntilUtc;

    /// <summary>
    /// Moved on by every refusal that rests the voice, and by a change of
    /// relay or voice on Voice Settings. Under _heatCallerLock.
    ///
    /// Up to two lines are with the relay at once. Line B can come back
    /// "allowance spent" while line A, asked for a moment earlier, is still
    /// rendering - and A then finishing used to clear the spent state B had
    /// just set and say "The HeatCaller voice is back", with the allowance
    /// gone. A request notes this number as it goes out; only an answer
    /// whose number is still current may end a rest. One that was
    /// overtaken by a refusal is still played, and still saved - it is a
    /// perfectly good take - it just says nothing about how things stand
    /// now.
    /// </summary>
    private int _heatCallerRefusalGeneration;

    /// <summary>
    /// Moved on only by ResetHeatCallerVoiceState - a change of relay, club,
    /// token or voice. Under _heatCallerLock.
    ///
    /// The refusal generation above stops an old ANSWER ending a rest; this
    /// stops an old REFUSAL starting one. A line asked of the old relay just
    /// before the club changed it, and refused afterwards - "allowance spent"
    /// from a test relay, say - used to rest the new relay over it, and
    /// remember that across a restart until the month was out.
    /// </summary>
    private int _heatCallerResetGeneration;

    /// <summary>True when the relay or voice has been changed since <paramref name="resetGeneration"/> was read.</summary>
    private bool HeatCallerResetSince(int resetGeneration)
    {
        lock (_heatCallerLock)
        {
            return resetGeneration != _heatCallerResetGeneration;
        }
    }

    /// <summary>
    /// Keeps the remembered spent date in step with the flag when two
    /// threads change it at once: the file is written from whatever the
    /// flag says at the time, under this, rather than from what each thread
    /// thought it said. Never taken inside _heatCallerLock.
    /// </summary>
    private readonly object _heatCallerMemoLock = new();

    private const string HeatCallerAllowanceSpentReason = "this month's HeatCaller voice allowance is used up";

    /// <summary>
    /// How long the relay rests after it could not be reached before one
    /// line is tried through it again, in the background. The same minute
    /// ElevenLabs rests for, and for the same reason: a club's Wi-Fi with no
    /// internet can hold a request for half a minute, and a lap call that
    /// late is worthless.
    /// </summary>
    private static readonly TimeSpan HeatCallerRestPeriod = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How long before a settled refusal - the allowance spent, the voice
    /// not included, a voice that has gone - is tried again. Long, because
    /// none of them mends itself in a minute; not never, because whoever
    /// runs the relay can put any of them right while the club races on.
    /// </summary>
    private static readonly TimeSpan HeatCallerSettledRetry = TimeSpan.FromMinutes(30);

    /// <summary>
    /// The audio for one line with the HeatCaller voice: the saved copy, the
    /// relay, or the Windows voice standing in.
    /// </summary>
    private async Task<SynthesisResult> RenderWithHeatCallerAsync(VoiceProfile profile, string text)
    {
        NoteVoiceRelayOnce();

        bool allowanceSpent;
        bool resting;

        lock (_heatCallerLock)
        {
            // Resting until the relay speaks a line again, not until a clock
            // runs out - the same shape as the ElevenLabs rest above. The
            // clock says when one line may be TRIED in the background; the
            // reason is what says the voice isn't speaking. Tied to the
            // clock, every line went to the relay again the moment it
            // expired, and any of them the relay happened to hold a copy of
            // came back in the HeatCaller voice - half a night in one voice
            // and half in the other, which is what this is here to prevent.
            resting = _heatCallerReason is not null;
            allowanceSpent = _heatCallerAllowanceSpent;
        }

        // The allowance is the one refusal that reaches past new lines: the
        // saved ones are passed over too, so the whole night is one voice.
        // (The same rule as HeatCallerSavedLinesPassedOver, read here in the
        // one look above.)
        if (!allowanceSpent && await VoiceLibrary.Shared.TryLoadAsync(profile, text) is { } saved)
        {
            return new SynthesisResult(saved, LineSource.Library);
        }

        if (HeatCallerVoice is not { } client || !profile.HeatCallerReady)
        {
            LogStandInOnce(
                "heatcaller-setup",
                "[voice] No HeatCaller voice is chosen yet - the free Windows voice is speaking. Pick one on Voice Settings.");

            return await RenderWithWindowsAsync(profile, text, LineSource.StandIn);
        }

        if (resting)
        {
            // The probe is claimed HERE, where it is actually started - not
            // when the rest was noticed. Claimed earlier, a line that then
            // came back from the library (the common case while resting: a
            // flag call that already has a take) took the claim away with
            // it and never gave it back, and no probe ran again for the
            // rest of the night.
            bool probe;

            lock (_heatCallerLock)
            {
                // One line a rest period is tried through the relay in the
                // background, to notice it coming back - a minute apart for
                // something that may mend itself, half an hour apart for
                // something somebody has to put right. The line itself has
                // already gone to the Windows voice, so nothing waits on it.
                probe = !_heatCallerProbeRunning && DateTime.UtcNow >= _heatCallerRetryUtc;

                if (probe)
                {
                    _heatCallerProbeRunning = true;
                }
            }

            if (probe)
            {
                StartHeatCallerProbe(client, profile, text);
            }

            return await RenderWithWindowsAsync(profile, text, LineSource.StandIn);
        }

        try
        {
            var audio = await SpeakThroughRelayAsync(client, profile, text, CancellationToken.None);

            VoiceLibrary.Shared.Save(profile, text, audio);

            return new SynthesisResult(audio, LineSource.HeatCaller);
        }
        catch (HeatCallerVoiceException ex)
        {
            NoteHeatCallerRefusal(ex);
            return await RenderWithWindowsAsync(profile, text, LineSource.StandIn);
        }
        catch (SynthesisSlotTimeoutException)
        {
            // This laptop's own queue, not the relay: nothing to rest over.
            LogStandInOnce(
                "heatcaller-slot",
                "[voice] The voice queue was full, so the free Windows voice said a line. Said once, not for every line.");

            return await RenderWithWindowsAsync(profile, text, LineSource.StandIn);
        }
    }

    /// <summary>Set once the log has been told the voice is pointed at a relay of its own.</summary>
    private int _voiceRelayNoted;

    /// <summary>
    /// Says, once per run, that the HeatCaller voice is speaking through a
    /// relay of its own rather than the club's.
    ///
    /// Not a stand-in reason, so it does not go through LogStandInOnce:
    /// that set is emptied whenever the voice recovers, and this is not
    /// something to be told again every time it does. It is a note about how
    /// the laptop is set up, and the night's log should carry it exactly
    /// once - so that a recording that sounds wrong can be explained without
    /// anyone having to guess which relay spoke.
    /// </summary>
    private void NoteVoiceRelayOnce()
    {
        if (HeatCallerVoice is not { UsingVoiceRelay: true })
        {
            return;
        }

        if (Interlocked.Exchange(ref _voiceRelayNoted, 1) != 0)
        {
            return;
        }

        StatusMessage?.Invoke(
            "[voice] The HeatCaller voice is speaking through the voice relay set on Voice Settings, not the one on the Club Website page. "
            + "Results, check-in and the race-day manager are unaffected.");
    }

    /// <summary>
    /// One line through the relay, in the same queue and under the same time
    /// limit as an ElevenLabs line - the announcer must never have more than
    /// a couple of renders outstanding, whoever is doing the rendering.
    /// </summary>
    private async Task<byte[]> SpeakThroughRelayAsync(
        HeatCallerVoiceClient client,
        VoiceProfile profile,
        string text,
        CancellationToken cancel)
    {
        // Also here, not only in RenderWithHeatCallerAsync: Prepare Lines
        // comes straight to this method, and a whole grid prepared through
        // a test relay should say so in the log too.
        NoteVoiceRelayOnce();

        // Which relay and voice this is asked of - see _heatCallerResetGeneration.
        // Read before the wait for a slot: the client was chosen before it,
        // and a change made while waiting must still mark this one's refusal.
        int resetGeneration;

        lock (_heatCallerLock)
        {
            resetGeneration = _heatCallerResetGeneration;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancel);
        timeout.CancelAfter(SynthesisTimeout);

        try
        {
            await SynthesisGate.WaitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new SynthesisSlotTimeoutException($"No synthesis slot came free within {SynthesisTimeout.TotalSeconds:0} s.");
        }

        try
        {
            // Noted as the request goes out, not as it comes back: see
            // _heatCallerRefusalGeneration.
            int generation;

            lock (_heatCallerLock)
            {
                generation = _heatCallerRefusalGeneration;
            }

            var (audio, allowance, cached) = await client.SpeakAsync(
                profile.HeatCallerVoice,
                text,
                IsFixedLine(text),
                timeout.Token);

            NoteHeatCallerAnswered(allowance, cached, generation);

            return audio;
        }
        catch (HeatCallerVoiceException ex) when (!ex.AskedBeforeReset && HeatCallerResetSince(resetGeneration))
        {
            // Refused by the relay or for the voice as they were before the
            // club changed them: marked, so it rests nothing (see
            // NoteHeatCallerRefusal).
            throw new HeatCallerVoiceException(ex.Refusal, ex.Message, ex.Allowance) { AskedBeforeReset = true };
        }
        catch (OperationCanceledException) when (!cancel.IsCancellationRequested)
        {
            throw new HeatCallerVoiceException(
                HeatCallerRefusal.Unreachable,
                $"The relay didn't answer within {SynthesisTimeout.TotalSeconds:0} s.")
            {
                AskedBeforeReset = HeatCallerResetSince(resetGeneration),
            };
        }
        finally
        {
            SynthesisGate.Release();
        }
    }

    /// <summary>
    /// Tries one line through the relay in the background while it rests.
    /// The line itself has already gone to the Windows voice; this is to
    /// find out whether the relay is back - and if it is, the take is saved
    /// like any other, so that line plays in the HeatCaller voice next time.
    /// </summary>
    private void StartHeatCallerProbe(HeatCallerVoiceClient client, VoiceProfile profile, string text)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                // Not once the club has chosen another voice: this would be
                // a request for a voice nobody is using.
                if (_voiceStore.Current.EffectiveEngine != VoiceEngine.HeatCaller)
                {
                    return;
                }

                // With the allowance spent, the line itself is no test. The
                // relay answers the lines it already holds - every flag
                // call, "Green flag!" - from its own cache, free, spent or
                // not, and a cached answer rightly doesn't end the rest.
                // Those are exactly the lines most likely to be the one
                // tried, so after a top-up or a new month every try came
                // back cached and the voice stayed on Windows, half an hour
                // at a time, all night. The allowance is asked for directly
                // instead - the same catalogue read the Voice Settings page
                // makes, which costs nothing.
                if (!await HeatCallerAllowanceBackAsync(client))
                {
                    return;
                }

                var audio = await SpeakThroughRelayAsync(client, profile, text, CancellationToken.None);

                VoiceLibrary.Shared.Save(profile, text, audio);
            }
            catch (HeatCallerVoiceException ex)
            {
                NoteHeatCallerRefusal(ex);
            }
            catch (Exception)
            {
                // A probe is a courtesy. It never surfaces as an error.
                lock (_heatCallerLock)
                {
                    _heatCallerRetryUtc = DateTime.UtcNow + HeatCallerRestPeriod;
                }
            }
            finally
            {
                lock (_heatCallerLock)
                {
                    _heatCallerProbeRunning = false;
                }
            }
        });
    }

    /// <summary>
    /// For the background try: false while the allowance is still spent (and
    /// nothing should be sent), true when it isn't spent or has room again -
    /// in which case the spent state is ended here, and the line that is
    /// then tried says whether the relay is speaking.
    ///
    /// "Room" is at least one of the longest lines the relay takes, not
    /// merely a count below the allowance: characters are reserved before a
    /// render, so a club a few characters short of the end is refused for a
    /// line longer than what is left while the count still reads below it.
    /// Ending the rest on that would bounce the night between the two voices
    /// every half hour - exactly what the spent state is there to stop.
    /// </summary>
    private async Task<bool> HeatCallerAllowanceBackAsync(HeatCallerVoiceClient client)
    {
        int generation;
        int resetGeneration;

        lock (_heatCallerLock)
        {
            if (!_heatCallerAllowanceSpent)
            {
                return true;
            }

            generation = _heatCallerRefusalGeneration;
            resetGeneration = _heatCallerResetGeneration;
        }

        // A refusal here is handled by the probe's own catch, as any other -
        // marked when it came from a relay the club has since changed, as a
        // spoken line's is (see SpeakThroughRelayAsync).
        HeatCallerCatalogue catalogue;

        try
        {
            catalogue = await client.CatalogueAsync();
        }
        catch (HeatCallerVoiceException ex) when (!ex.AskedBeforeReset && HeatCallerResetSince(resetGeneration))
        {
            throw new HeatCallerVoiceException(ex.Refusal, ex.Message, ex.Allowance) { AskedBeforeReset = true };
        }

        var allowance = catalogue.Allowance;
        var room = catalogue.Enabled
                   && allowance.Allowance > 0
                   && allowance.Allowance - allowance.Used >= HeatCallerVoiceClient.MaxCharacters;

        var ended = false;

        lock (_heatCallerLock)
        {
            _heatCallerAllowance = allowance;

            if (!room)
            {
                // Still spent: the next look is the usual half hour away.
                _heatCallerRetryUtc = DateTime.UtcNow + HeatCallerSettledRetry;
            }
            else if (generation != _heatCallerRefusalGeneration)
            {
                // Something was refused, or the relay changed, while this
                // was being asked. That is newer than this answer, so the
                // state it left is kept, and nothing is tried this time.
            }
            else
            {
                // The reason stays until the relay speaks the line tried
                // next: that is what says "back", in the usual place.
                _heatCallerAllowanceSpent = false;
                _heatCallerSpentUntilUtc = null;
                ended = true;
            }
        }

        if (ended)
        {
            RememberHeatCallerSpent();
        }
        else if (!room)
        {
            // A restart since the allowance ran out would otherwise stand in
            // with nothing in the log to say why.
            LogStandInOnce(
                "heatcaller-still-spent",
                $"[voice] {Capitalised(HeatCallerAllowanceSpentReason)} - the free Windows voice speaks every line until it starts again. "
                + "Nothing is lost: every line already saved is kept and plays again then.");
        }

        return ended;
    }

    /// <summary>
    /// Writes the spent allowance's date - or its absence - to the voice
    /// settings store, from whatever the flag says now. See _heatCallerMemoLock.
    /// </summary>
    private void RememberHeatCallerSpent()
    {
        lock (_heatCallerMemoLock)
        {
            DateTime? until;

            lock (_heatCallerLock)
            {
                until = _heatCallerAllowanceSpent ? _heatCallerSpentUntilUtc : null;
            }

            _voiceStore.SetHeatCallerSpentUntil(until);
        }
    }

    /// <summary>The start of next calendar month, in UTC - the relay's own month when it didn't say its reset date.</summary>
    private static DateTime NextMonthStartUtc(DateTime nowUtc) =>
        new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(1);

    /// <summary>
    /// Forgets why the HeatCaller voice was resting - the reason, a spent
    /// allowance, and when it may next be tried - so the next line is asked
    /// of the relay straight away.
    ///
    /// For Voice Settings, when the voice relay address, club or token is
    /// changed or a different HeatCaller voice is chosen. Each of those is a
    /// different question: a spent allowance on a test relay says nothing
    /// about the club's own, and a token or voice the relay turned down may
    /// be exactly what was just put right. Resting on over it kept the
    /// Windows voice speaking for up to half an hour - or, for the
    /// allowance, the rest of the month - after the club had fixed it. The
    /// same thing a new ElevenLabs key does for a rest over the old one.
    /// </summary>
    /// <param name="why">Lower case, to sit inside a sentence in the log.</param>
    internal void ResetHeatCallerVoiceState(string why)
    {
        bool wasResting;

        lock (_heatCallerLock)
        {
            wasResting = _heatCallerReason is not null || _heatCallerAllowanceSpent;

            _heatCallerReason = null;
            _heatCallerAllowanceSpent = false;
            _heatCallerSpentUntilUtc = null;
            _heatCallerRetryUtc = DateTime.MinValue;

            // Where some other relay's allowance stood.
            _heatCallerAllowance = null;

            // Anything already on its way was asked of the old relay, or for
            // the old voice: its answer says nothing about this one.
            _heatCallerRefusalGeneration++;

            // Nor does its refusal - see _heatCallerResetGeneration.
            _heatCallerResetGeneration++;
        }

        RememberHeatCallerSpent();

        // Under its own lock, never inside the one above - see
        // NoteHeatCallerAnswered.
        lock (_voiceHealthLock)
        {
            _standInLogged.Clear();
        }

        if (wasResting)
        {
            StatusMessage?.Invoke($"[voice] {Capitalised(why)} - the HeatCaller voice is tried again from the next line.");
        }
    }

    /// <summary>
    /// Whether saved HeatCaller takes are passed over for this profile right
    /// now: the HeatCaller voice chosen, and its month's allowance spent.
    /// The one rule behind the saved-line check in RenderWithHeatCallerAsync,
    /// the release calls read straight from the library, and the prepared
    /// flag and release takes - so that none of them plays in the HeatCaller
    /// voice on a night the rest is in the Windows voice.
    ///
    /// Only the allowance. While the relay merely can't be reached, saved
    /// lines DO still play in the HeatCaller voice - that is what the docs
    /// promise, and what RenderWithHeatCallerAsync has always done.
    /// </summary>
    private bool HeatCallerSavedLinesPassedOver(VoiceProfile profile)
    {
        if (profile.EffectiveEngine != VoiceEngine.HeatCaller)
        {
            return false;
        }

        lock (_heatCallerLock)
        {
            return _heatCallerAllowanceSpent;
        }
    }

    /// <summary>The relay spoke a line: whatever it last refused for is over, and the allowance is as it just said.</summary>
    /// <param name="cached">
    /// True when the relay served a copy it already had. Free, and no proof
    /// that the club has any allowance left - so it does not end a rest over
    /// a spent one.
    /// </param>
    /// <param name="generation">
    /// _heatCallerRefusalGeneration as the request went out. When a refusal
    /// has come in since, this answer ends nothing - see that field.
    /// </param>
    private void NoteHeatCallerAnswered(HeatCallerAllowance? allowance, bool cached, int generation)
    {
        bool back;
        var warn = false;
        var spentEnded = false;
        var overtaken = false;
        DateTime? warnedUntil = null;

        lock (_heatCallerLock)
        {
            if (generation != _heatCallerRefusalGeneration)
            {
                // Asked before the latest refusal (or before the relay or
                // voice was changed) and answered after it. The take is
                // good; what it would say about the state is out of date.
                overtaken = true;
                back = false;
            }
            else if (cached && _heatCallerAllowanceSpent)
            {
                // A line the relay had a copy of. Free, and served to a club
                // with nothing left just the same - so it is no proof the
                // allowance is back, and the rest goes on. Only the next try
                // is put back to its usual distance.
                _heatCallerRetryUtc = DateTime.UtcNow + HeatCallerSettledRetry;
                back = false;
            }
            else
            {
                back = _heatCallerReason is not null;
                _heatCallerReason = null;
                _heatCallerRetryUtc = DateTime.MinValue;

                if (!cached && _heatCallerAllowanceSpent)
                {
                    // Characters were really spent on this one, so there was
                    // room for them.
                    _heatCallerAllowanceSpent = false;
                    _heatCallerSpentUntilUtc = null;
                    spentEnded = true;
                }
            }

            if (allowance is not null)
            {
                // The month the relay says it is counting, which changes
                // exactly once a month - unlike the count itself, which also
                // goes down when a failed render gives its characters back.
                if (allowance.ResetsUtc is { } resets && _heatCallerAllowanceResets != resets)
                {
                    _heatCallerAllowanceResets = resets;
                    _heatCallerAllowanceWarned = false;
                }

                // Nine tenths spent, said once a month: the first sign of a
                // spent allowance is otherwise a whole race in the Windows
                // voice, on the night, with nothing having warned anybody.
                warn = !_heatCallerAllowanceWarned
                       && allowance.Allowance > 0
                       && allowance.Used * 10 >= allowance.Allowance * 9;

                if (warn)
                {
                    _heatCallerAllowanceWarned = true;

                    // Remembered until the month it was said for is over, so
                    // a restart doesn't say it again (see the constructor).
                    warnedUntil = allowance.ResetsUtc ?? NextMonthStartUtc(DateTime.UtcNow);
                }

                _heatCallerAllowance = allowance;
            }
        }

        if (spentEnded)
        {
            RememberHeatCallerSpent();
        }

        if (warnedUntil is { } until)
        {
            _voiceStore.SetHeatCallerWarnedUntil(until);
        }

        if (warn && allowance is not null)
        {
            StatusMessage?.Invoke(
                $"[voice] The HeatCaller voice has used {allowance.Used:N0} of {allowance.Allowance:N0} characters this month. "
                + "When it's gone, the free Windows voice speaks every line until it starts again"
                + (allowance.ResetsUtc is { } resets ? $" on {resets.ToLocalTime():d MMM}." : ".")
                + " Lines already saved are kept either way.");
        }

        // Not for an answer that was overtaken: the refusal that overtook it
        // has just been said, and forgetting that would say it again on the
        // very next line.
        if (overtaken)
        {
            return;
        }

        // Under its own lock, and never inside the one above: what has been
        // said is _voiceHealthLock's, and taking two locks in one place is
        // how a deadlock gets written.
        lock (_voiceHealthLock)
        {
            _standInLogged.Clear();
        }

        if (back)
        {
            StatusMessage?.Invoke(
                "[voice] The HeatCaller voice is back - it speaks from the next line, and the Windows voice stands down.");
        }
    }

    /// <summary>The relay would not speak: rest it for a while, and say why once.</summary>
    private void NoteHeatCallerRefusal(HeatCallerVoiceException ex)
    {
        // Asked of the relay or voice the club has since changed: the line
        // went to the Windows voice, and that is all. Resting the new relay
        // over the old one's answer is what the reset was there to stop.
        if (ex.AskedBeforeReset)
        {
            return;
        }

        // One line's own problem, not a state: too long, or nothing to say.
        // Nothing rests over it - the next line may be perfectly speakable.
        if (ex.Refusal == HeatCallerRefusal.Text)
        {
            LogStandInOnce("heatcaller-text", $"[voice] The HeatCaller voice couldn't speak a line ({ex.Message}) - the free Windows voice said it.");
            return;
        }

        var settled = ex.Refusal
            is HeatCallerRefusal.Allowance
            or HeatCallerRefusal.NotIncluded
            or HeatCallerRefusal.Off
            or HeatCallerRefusal.NotAllowed
            or HeatCallerRefusal.UnknownVoice;

        var reason = ex.Refusal switch
        {
            HeatCallerRefusal.Allowance => HeatCallerAllowanceSpentReason,
            HeatCallerRefusal.NotIncluded => "the HeatCaller voice isn't included for this club",
            HeatCallerRefusal.Off => "the HeatCaller voice isn't switched on for this relay",
            HeatCallerRefusal.NotAllowed => "the relay didn't accept this club's token",
            HeatCallerRefusal.UnknownVoice => "that HeatCaller voice isn't available any more",
            HeatCallerRefusal.Busy => "the HeatCaller voice is busy",
            _ => "the HeatCaller relay can't be reached",
        };

        bool first;
        var spentBegan = false;

        lock (_heatCallerLock)
        {
            first = _heatCallerReason != reason;
            _heatCallerReason = reason;

            // Any answer still on its way was asked for before this, and
            // may no longer end the rest - see _heatCallerRefusalGeneration.
            _heatCallerRefusalGeneration++;

            if (ex.Allowance is not null)
            {
                _heatCallerAllowance = ex.Allowance;
            }

            if (ex.Refusal == HeatCallerRefusal.Allowance && !_heatCallerAllowanceSpent)
            {
                _heatCallerAllowanceSpent = true;

                // Until the month the relay is counting is over - its own
                // date when it has said one, the calendar's otherwise. A
                // date already past is last month's figure, kept from an
                // earlier answer, and would be forgotten on the next start.
                var nowUtc = DateTime.UtcNow;

                _heatCallerSpentUntilUtc = _heatCallerAllowance?.ResetsUtc is { } resets && resets > nowUtc
                    ? resets
                    : NextMonthStartUtc(nowUtc);

                spentBegan = true;
            }

            _heatCallerRetryUtc = DateTime.UtcNow + (settled ? HeatCallerSettledRetry : HeatCallerRestPeriod);
        }

        // Remembered across a restart, so a laptop restarted mid-night
        // stays in one voice (see the constructor).
        if (spentBegan)
        {
            RememberHeatCallerSpent();
        }

        if (!first)
        {
            return;
        }

        StatusMessage?.Invoke(ex.Refusal == HeatCallerRefusal.Allowance
            ? $"[voice] {Capitalised(reason)} - the free Windows voice speaks every line until it starts again. "
              + "Nothing is lost: every line already saved is kept and plays again then. " + ex.Message
            : $"[voice] {Capitalised(reason)} ({ex.Message}) - the free Windows voice is standing in. "
              + (settled
                  ? "It is tried again about every half hour."
                  : "It is tried again in the background about once a minute."));
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
    /// Stand-in reasons already logged, so each is said once rather than on
    /// every line. Cleared whenever ElevenLabs answers, so the same failure
    /// coming back later is news again.
    /// </summary>
    private readonly HashSet<string> _standInLogged = new(StringComparer.Ordinal);

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

    /// <summary>Fired for status/errors the user should be able to see (e.g. in the dev console).</summary>
    public event Action<string>? StatusMessage;

    /// <summary>
    /// Reads whether the current session is practice, so per-context audio
    /// settings apply.
    ///
    /// A CALLBACK rather than a mirrored bool. The mirrored version had to
    /// be cleared by hand, and StopPractice is the only thing that did it -
    /// so navigating away mid-practice and then starting a race left the
    /// announcer applying practice settings to a real race. Reading the
    /// session on demand can't drift, however practice ends.
    ///
    /// Stays a callback rather than a direct session reference to keep this
    /// service decoupled from RaceControlSession, and defaults to "not
    /// practice" when nothing is wired up.
    /// </summary>
    public Func<bool>? PracticeModeProvider { get; set; }

    /// <summary>
    /// Whether the current session is a qualifier - so provisional-pole and
    /// qualifying-improvement lines fire, and only then. Same callback shape
    /// as PracticeModeProvider, for the same decoupling reason.
    /// </summary>
    public Func<bool>? QualifyingModeProvider { get; set; }

    /// <summary>Who currently holds provisional pole, so the call only fires when the top of the order actually changes hands.</summary>
    private string? _provisionalPoleTransponder;

    /// <summary>
    /// The current flag, so everything routine can be held while a caution
    /// is out. Supplied by the app rather than read from a session, because
    /// this service is deliberately ignorant of where its events come from.
    /// </summary>
    public Func<FlagState>? FlagStateProvider { get; set; }

    private bool IsUnderCaution => FlagStateProvider?.Invoke() is FlagState.Yellow or FlagState.Red;

    /// <summary>
    /// Whether this announcement has to wait for the track to go green.
    ///
    /// Flag calls and the other race-control lines are never held - they are
    /// the reason the caution is being announced at all.
    /// </summary>
    private bool HeldByCaution(AnnouncementEventType type, AnnouncementEventConfig? config)
    {
        if (!IsUnderCaution || IsPriorityAnnouncement(type) || IsInterruptingAnnouncement(type))
        {
            return false;
        }

        return config?.AllowUnderCaution != true;
    }

    private bool IsPracticeMode => PracticeModeProvider?.Invoke() ?? false;

    private bool IsQualifyingMode => QualifyingModeProvider?.Invoke() ?? false;

    /// <summary>
    /// Per-driver lap cues, when configured. Optional so the announcer
    /// works unchanged without one.
    /// </summary>
    public LapBeepStore? LapBeeps { get; set; }

    private int _soundInFlight;
    private int _voiceInFlight;

    /// <summary>
    /// Bumped every time a flag clears the decks (ClearTheDecksFor).
    ///
    /// Clearing the queue and cutting off playback isn't enough on its own:
    /// the voice loop may already have dequeued a routine line that is still
    /// waiting on ElevenLabs, and without this it would start playing a
    /// second or two AFTER the flag - lap chatter talking over a yellow.
    /// </summary>
    private int _interruptGeneration;

    /// <summary>
    /// The interrupt generation a line belongs to, flowed through its async
    /// work so SynthesizeAndPlayAsync can tell it has been overtaken by a
    /// flag. For a routine voice-queue line it is the generation it was
    /// dequeued under; for a flag call it is the generation that flag itself
    /// started, so only a NEWER flag silences it. Null for everything that
    /// must not be dropped this way: priority lines (a race-control line
    /// queued earlier stays, as ClearTheDecksFor keeps it) and anything
    /// spoken outside the voice queue.
    /// </summary>
    private readonly AsyncLocal<int?> _dequeuedUnderGeneration = new();

    /// <summary>
    /// The race-control call a line is being spoken for, flowed through its
    /// async work the same way, so SynthesizeAndPlayAsync can wait for that
    /// call's own sound between synthesising and speaking (see
    /// WaitForLeadInSoundAsync). Set only by SpeakInterrupting; null for
    /// every other line.
    /// </summary>
    private readonly AsyncLocal<AnnouncementEventType?> _leadInFor = new();

    /// <summary>True when the line being produced was dequeued before a flag interrupted.</summary>
    private bool IsSupersededByInterrupt() =>
        _dequeuedUnderGeneration.Value is int generation && generation != Volatile.Read(ref _interruptGeneration);

    /// <summary>
    /// Cues scheduled by PlaySoundAfterSpeech but not yet handed to the
    /// sound queue. Tracked separately because during that window they
    /// appear nowhere else, and callers waiting on audio would otherwise
    /// conclude nothing is happening.
    /// </summary>
    private int _deferredSoundsPending;

    /// <summary>
    /// True while anything is queued or actually playing. Lets callers wait
    /// for a cue to finish instead of guessing its length - the race start
    /// used a fixed delay with no relationship to how long the countdown
    /// sound really ran.
    /// </summary>
    public bool IsAudioPending
    {
        get
        {
            if (Volatile.Read(ref _soundInFlight) > 0
                || Volatile.Read(ref _voiceInFlight) > 0
                || Volatile.Read(ref _deferredSoundsPending) > 0)
            {
                return true;
            }

            lock (_soundQueueLock)
            {
                // A lap chime too old to play will be thrown away when its
                // turn comes, so it is not worth making a start wait for.
                if (_soundQueue.Any(item => !IsStaleSound(item.Kind, item.QueuedAtUtc)))
                {
                    return true;
                }
            }

            lock (_voiceQueueLock)
            {
                return _voiceQueue.Count > 0;
            }
        }
    }

    /// <summary>
    /// Waits for queued audio to finish, giving up after <paramref name="timeout"/>.
    /// The cap matters: a stuck or unusually long cue must never leave a
    /// race unable to start.
    /// </summary>
    public async Task WaitForAudioAsync(TimeSpan timeout, CancellationToken token = default)
    {
        var started = DateTime.UtcNow;
        var deadline = started + timeout;

        // Instrumented deliberately. Three attempts at this timing were made
        // by reasoning about the code; each was wrong. These lines report
        // what actually happened so the next change is based on evidence.
        StatusMessage?.Invoke(
            $"[audio-wait] enter: pending={IsAudioPending} voice={Volatile.Read(ref _voiceInFlight)} "
            + $"sound={Volatile.Read(ref _soundInFlight)} deferred={Volatile.Read(ref _deferredSoundsPending)} "
            + $"speaking={_isVoiceSpeaking} muted={Muted}");

        // Settle first, before deciding there's nothing to wait for.
        //
        // Announcements triggered from a background thread - demo, quick
        // race, the auto-start sequence - reach the announcer through
        // BeginInvoke, so the request is still sitting in the UI message
        // queue at the instant this is called. Checking immediately saw
        // silence and let the race start over the top of the cue.
        //
        // The manual Stage chain never hit this because it runs ON the UI
        // thread, where the handler executes synchronously and the audio is
        // already pending - which is why three paths misbehaved and one
        // didn't.
        // Skipped when nothing could be pending anyway - a muted announcer
        // has no cue coming, and 600ms of dead air before a green flag is
        // very noticeable.
        if (!Muted)
        {
            // 250ms rather than 600. Long enough to catch an announcement
            // still crossing threads, short enough not to be heard as a
            // pause - the green flag is the one moment where dead air is
            // most obvious.
            var settleUntil = DateTime.UtcNow + TimeSpan.FromMilliseconds(250);

            while (!IsAudioPending && DateTime.UtcNow < settleUntil && DateTime.UtcNow < deadline)
            {
                await Task.Delay(40, token);
            }
        }

        var sawAudio = false;

        while (IsAudioPending && DateTime.UtcNow < deadline)
        {
            sawAudio = true;
            await Task.Delay(40, token);
        }

        StatusMessage?.Invoke(
            $"[audio-wait] exit after {(DateTime.UtcNow - started).TotalMilliseconds:0}ms, "
            + $"sawAudio={sawAudio}, timedOut={DateTime.UtcNow >= deadline}");
    }

    /// <summary>Fired with the clean, resolved spoken text every time a voice announcement is about to be queued - for displays (like Big Screen mode) that want to show "what was just said" without parsing debug log lines.</summary>
    public event Action<string>? AnnouncementTextReady;

    /// <summary>
    /// Raised true when spoken playback starts and false when the LAST line
    /// speaking ends, so background audio can duck underneath it. Covers the
    /// voice queue and the flag calls that play outside it.
    ///
    /// Can fire on the UI thread (the voice queue's loop runs there) or on a
    /// thread-pool thread (flag calls) - subscribers must be thread-safe and
    /// must not block.
    /// </summary>
    public event Action<bool>? SpeakingChanged;

    /// <summary>How many lines are speaking right now, across the queue and the flag calls.</summary>
    private int _speakingCount;

    /// <summary>What SpeakingChanged last reported, so it only fires on a real change.</summary>
    private bool _speakingReported;

    /// <summary>
    /// Serialises the count change and the event together. Without it, two
    /// threads could decide "started" and "stopped" in one order and raise
    /// them in the other, leaving the crowd ducked with nothing speaking.
    /// </summary>
    private readonly object _speakingLock = new();

    /// <summary>
    /// Marks one line as speaking. Counted rather than a plain on/off, so a
    /// flag call cutting across a queued line keeps the duck: before, the
    /// cut-off line's own "finished" un-ducked the crowd just as the flag
    /// started speaking, and flag calls - which play outside the queue -
    /// never ducked anything at all.
    /// </summary>
    private void BeginSpeaking()
    {
        lock (_speakingLock)
        {
            _speakingCount++;

            if (!_speakingReported)
            {
                _speakingReported = true;
                RaiseSpeakingChanged(true);
            }
        }
    }

    /// <summary>Marks one line as finished; reports "not speaking" only once none are left.</summary>
    private void EndSpeaking()
    {
        lock (_speakingLock)
        {
            _speakingCount = Math.Max(0, _speakingCount - 1);

            if (_speakingCount == 0 && _speakingReported)
            {
                _speakingReported = false;
                RaiseSpeakingChanged(false);
            }
        }
    }

    /// <summary>
    /// Supplies a multiplier for announcement volume, letting the app raise
    /// the voice when loud background audio would otherwise bury it.
    /// Returns 1.0 (no change) when nothing is wired up.
    /// </summary>
    public Func<float>? VoiceBoostProvider { get; set; }

    /// <summary>
    /// The profile's current expression settings, so a caller can amplify
    /// them for one announcement without replacing what the user chose.
    ///
    /// Exposed here rather than handing the voice store to every control
    /// that wants to nudge delivery.
    /// </summary>
    public (double Stability, double Style) CurrentDelivery =>
        (_voiceStore.Current.Stability, _voiceStore.Current.Style);

    private void RaiseSpeakingChanged(bool speaking)
    {
        try
        {
            SpeakingChanged?.Invoke(speaking);
        }
        catch
        {
            // A subscriber throwing must never take down the voice queue -
            // this loop dying silently was a real bug earlier in this app.
        }
    }

    /// <summary>When true, every voice line and sound effect is silently skipped rather than queued - a fast "quiet mode" that doesn't require touching individual event/sound settings.</summary>
    private bool _muted;

    /// <summary>
    /// Silences all output. Muting also clears anything already queued -
    /// the per-item guards only stop NEW audio being enqueued, so without
    /// this a backlog kept playing after the mute and the button looked
    /// broken. That's especially visible in Demo Mode, which can generate
    /// laps far faster than they can be announced.
    /// </summary>
    public bool Muted
    {
        get => _muted;
        set
        {
            var wasMuted = _muted;
            _muted = value;

            if (value && !wasMuted)
            {
                // Everything, race-control lines and their sounds included:
                // muting means silence, and a finish call kept back here
                // simply spoke later.
                StopAllPendingAudio(keepPriority: false);

                // And whatever is already coming out of the speaker. Clearing
                // the queue alone left the line or sound in progress playing
                // to the end, so a mute pressed during the finish fanfare was
                // followed by the whole fanfare and then the finish call.
                StopCurrentPlayback();
                StopPlayingSound();
            }
        }
    }

    /// <summary>
    /// Speaks arbitrary text directly - used for schedule reminders, which
    /// need fully dynamic phrasing ("5 minutes to Stock Buggy A-Main")
    /// rather than one of the pre-configured template lines every other
    /// announcement type uses. Still goes through the same queue/drop-
    /// while-speaking/quota-handling machinery as everything else.
    /// </summary>
    /// <summary>
    /// Speaks a free-form line - AI commentary, schedule reminders - that
    /// didn't come from the template system.
    ///
    /// Lap times in the text are rewritten into the same spelled-out form
    /// the template announcements use. Without this the AI summary says
    /// "6.935 seconds" as written, and ElevenLabs renders that as "six
    /// thousand nine hundred thirty five" or "six point nine hundred
    /// thirty five" - the exact problem FormatSecondsForSpeech exists to
    /// solve, just bypassed because this path never went through it.
    /// </summary>
    /// <summary>
    /// Speaks a line for testing, bypassing the queue's drop-while-speaking
    /// rule.
    ///
    /// A test that silently does nothing because something else happened to
    /// be speaking is worse than useless - it reads as the feature being
    /// broken, which is how several things in this app got reported.
    /// </summary>
    public void SpeakTest(string text, double speed)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        // Wrapped: SynthesizeAndPlayAsync can throw on a network failure
        // or a rejected request, and an exception on a fire-and-forget
        // task is unobserved - it surfaces as a process crash, not as a
        // failed test.
        _ = Task.Run(async () =>
        {
            try
            {
                // A test is asked for by hand, so it speaks even while the
                // announcer is muted - see the remarks above.
                await SynthesizeAndPlayAsync(
                    text,
                    "[voice] Test",
                    "[voice] Test skipped - ElevenLabs API key or voice not configured.",
                    speedOverride: speed,
                    honourMute: false);
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke($"[voice] Test failed - {ex.Message}");
            }
        });
    }

    public void SpeakReminder(string text, (double Stability, double Style)? deliveryOverride = null)
    {
        if (Muted || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var spoken = SpellOutTimesForSpeech(text);
        EnqueueVoiceWork(AnnouncementEventType.Reminder, null,
            () => SynthesizeAndPlayAsync(spoken, "[voice] Reminder", deliveryOverride: deliveryOverride));
    }

    /// <summary>A line already rendered to audio, ready to play the instant it's wanted.</summary>
    public sealed record PreparedSpeech(string Text, byte[] Audio);

    /// <summary>
    /// Renders a line now and hands the audio back instead of speaking it.
    ///
    /// Exists so a caller that is about to wait anyway can pay the synthesis
    /// cost DURING that wait rather than after it. Synthesis is a network
    /// round trip - a second or two on a good connection, considerably more
    /// on a club's wifi - and when it lands after a configured pause, the
    /// pause the user set is not the pause they hear.
    ///
    /// Returns null when voice isn't configured, the announcer is muted, or
    /// the render failed. Every one of those is a cue for the caller to fall
    /// back to <see cref="SpeakReminder"/>, which synthesises at speak time.
    /// </summary>
    public async Task<PreparedSpeech?> PrepareReminderAsync(
        string text, (double Stability, double Style)? deliveryOverride = null)
    {
        if (Muted || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var profile = _voiceStore.Current;

        // Matches SynthesizeAndPlayAsync: a shallow copy, so the override
        // applies to this line alone and never leaks into the saved profile.
        if (deliveryOverride is { } delivery)
        {
            profile = profile.CloneForDelivery(delivery.Stability, delivery.Style);
        }

        // Whichever voice would say it live - the Windows voice, ElevenLabs,
        // or the Windows voice standing in. This used to ask for a key and a
        // voice only, which with the Windows voice chosen would have sent
        // every recap to the slower speak-time path.
        if (!CanSpeak(profile))
        {
            return null;
        }

        // Both text transforms the speak-time path applies, in the same
        // order, so a prepared line is identical to a live one.
        var spoken = profile.SanitiseForModel(SpellOutTimesForSpeech(text));

        if (string.IsNullOrWhiteSpace(spoken))
        {
            return null;
        }

        try
        {
            var audio = (await RenderLineAsync(profile, spoken)).Audio;
            return new PreparedSpeech(spoken, audio);
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke(
                $"[voice] Couldn't render a line in advance ({ex.Message}) - it will synthesise as it speaks instead.");
            return null;
        }
    }

    /// <summary>
    /// Renders a line to be kept as a soundscape's recorded voice line (see
    /// StingerLibrary), in that soundscape's delivery.
    ///
    /// Only a real announcer voice is kept - ElevenLabs on the club's key or
    /// the HeatCaller voice. Null when the Windows voice would say it,
    /// including the Windows voice standing in because ElevenLabs couldn't:
    /// a recording is played for good, and a stand-in take recorded by
    /// accident would replace the announcer on that line every race.
    /// </summary>
    public async Task<byte[]?> RecordVoiceLineAsync(string text, double stability, double style)
    {
        var profile = _voiceStore.Current.CloneForDelivery(stability, style);
        if (profile.EffectiveEngine == VoiceEngine.Windows || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var spoken = profile.SanitiseForModel(SpellOutTimesForSpeech(text));
        if (string.IsNullOrWhiteSpace(spoken))
        {
            return null;
        }

        try
        {
            var result = await RenderLineAsync(profile, spoken);
            return result.Source is LineSource.ElevenLabs or LineSource.HeatCaller or LineSource.Library
                ? result.Audio
                : null;
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"[voice] Couldn't record \"{text}\" - {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Speaks a line rendered earlier by <see cref="PrepareReminderAsync"/>.
    ///
    /// Queues as a Reminder like any other deliberate speech, so ducking,
    /// ordering and the post-race hold all behave exactly as they do for
    /// SpeakReminder - the only difference is that there is no network call
    /// between the decision to speak and the sound.
    /// </summary>
    /// <param name="tail">
    /// How much silence to leave behind the line - trimmed when another
    /// line follows immediately, extended for a long one whose ending would
    /// otherwise clip.
    /// </param>
    public void SpeakPrepared(PreparedSpeech prepared, SpeechTail tail = SpeechTail.Normal)
    {
        if (Muted)
        {
            return;
        }

        var volume = Math.Clamp(_voiceStore.Current.Volume * (VoiceBoostProvider?.Invoke() ?? 1f), 0.0, 1.0);
        var trailing = TailSecondsFor(tail);

        AnnouncementTextReady?.Invoke(prepared.Text);

        EnqueueVoiceWork(AnnouncementEventType.Reminder, null, async () =>
        {
            StatusMessage?.Invoke($"[voice] Reminder (prepared): \"{prepared.Text}\"");

            await _audioPlaybackLock.WaitAsync();
            try
            {
                // Muted while it waited for the speaker.
                if (Muted)
                {
                    StatusMessage?.Invoke("[voice] Reminder (prepared): dropped - the announcer was muted.");
                    return;
                }

                await PlayAsync(prepared.Audio, volume, trailing, CurrentLeadingSilence);
            }
            finally
            {
                _audioPlaybackLock.Release();
            }
        });
    }

    /// <summary>
    /// Finds decimal times in free text and rewrites them the way
    /// FormatSecondsForSpeech would.
    ///
    /// Matches a decimal number followed by "s", "sec", "secs", "second" or
    /// "seconds", which is how both the AI and the fact lines it's given
    /// write lap times. The unit is REQUIRED: without it any decimal at all
    /// was read out as a time - "2.4GHz" became "2 point four zero seconds
    /// GHz". Anything else - lap counts, positions, driver numbers, other
    /// decimals - is left alone.
    /// </summary>
    private static string SpellOutTimesForSpeech(string text) =>
        System.Text.RegularExpressions.Regex.Replace(
            text,
            // [0-9] rather than \d, which in .NET also matches other
            // scripts' digits that the parse below can't read. At most nine
            // whole digits, so the millisecond sum can't overflow.
            @"\b([0-9]{1,9})\.([0-9]{1,3})\s*(?:seconds?|secs?|s)\b",
            match =>
            {
                // InvariantCulture: this is digit text pulled out of a
                // regex, not a localised number.
                if (!long.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out var whole))
                {
                    return match.Value;
                }

                // The fraction as milliseconds - "9" is 900, "93" is 930,
                // "935" is 935 - then the SAME formatter the template lines
                // use. Rounding the fraction on its own used to lose the
                // carry: 6.995 came out as "6 point one zero zero".
                var fractionMs = int.Parse(match.Groups[2].Value.PadRight(3, '0'),
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture);

                return FormatSecondsForSpeech((whole * 1000) + fractionMs);
            },
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public AnnouncementService(ProfileStore profiles, VoiceSettingsStore voiceStore, SoundEffectsStore soundStore, RivalryStore? rivalries = null)
    {
        _profiles = profiles;
        _voiceStore = voiceStore;
        _soundStore = soundStore;
        _rivalries = rivalries;

        // What was known about the HeatCaller voice's month when the app
        // last ran. The warning is "once a month", not once a run; and a
        // spent allowance holds until the relay speaks a line again or the
        // month is over - a restart mid-night used to forget it, and every
        // saved line played in the HeatCaller voice again while the new ones
        // were in the Windows voice. A date already past is last month's and
        // is ignored. The first line after a restart with the allowance
        // spent sets the background check going straight away (the retry
        // time is left at its start), so a top-up since is noticed at once.
        var nowUtc = DateTime.UtcNow;

        if (voiceStore.HeatCallerWarnedUntilUtc is { } warnedUntil && warnedUntil > nowUtc)
        {
            _heatCallerAllowanceWarned = true;
            _heatCallerAllowanceResets = warnedUntil;
        }

        if (voiceStore.HeatCallerSpentUntilUtc is { } spentUntil && spentUntil > nowUtc)
        {
            _heatCallerAllowanceSpent = true;
            _heatCallerSpentUntilUtc = spentUntil;
            _heatCallerReason = HeatCallerAllowanceSpentReason;
        }

        // Every caller feeds this from the UI thread - the decoders marshal
        // their passes onto it before race control sees them. Next Level
        // Timing's client was the one source that did not, and it had its
        // own subscriptions here to hop threads; it was retired in 0.50.0.

        // The saved-voice library reports into the same console as every
        // other voice line. There is one announcer per app, so this is set
        // once; the size check then runs in the background at startup.
        VoiceLibrary.Shared.Log = message => StatusMessage?.Invoke(message);
        VoiceLibrary.Shared.StartPruneOnce();

        // The free Windows voice reports into the same console. Its list of
        // voices is read now, in the background, so the first line - or the
        // Voice Settings and Mixer pages being built - doesn't wait for it.
        WindowsVoice.Log = message => StatusMessage?.Invoke(message);
        WindowsVoice.WarmUp();

        // Two independent background loops, each waiting on its own
        // semaphore for work. This replaces an earlier "check a boolean
        // flag" approach that had a race condition: a loop could decide its
        // queue was empty and be on its way out at the exact moment a new
        // item was enqueued, and that new item would see the flag still set
        // and assume the (already-exiting) loop would handle it - silently
        // stranding it forever. A semaphore can't lose a wakeup like that.
        //
        // Each loop is started through a supervisor rather than directly.
        // Both loops only leave their `while (true)` via an exception
        // escaping the per-item try/catch, and starting them with a
        // discarded `_ = ` task meant that single exception used to kill
        // the whole announcer PERMANENTLY for the rest of the app's run -
        // nothing logged, no recovery short of restarting the app. That's
        // consistent with an announcer going silent mid-session and never
        // resuming. The supervisor catches whatever escapes, logs it, and
        // restarts the loop instead of letting it die for good.
        //
        // Worth knowing: "background" here means asynchronous, not a separate
        // thread. This constructor runs on the UI thread, so both loops pick
        // up the WinForms context and every step between awaits runs on the
        // UI thread. That is why something queued from a UI handler is not
        // picked up until that handler has returned.
        _ = SuperviseQueueLoop("voice", RunVoiceQueueLoopAsync);
        _ = SuperviseQueueLoop("sound", RunSoundQueueLoopAsync);
    }

    /// <summary>
    /// Runs a queue loop and restarts it if it ever exits. See the remarks
    /// where this is called for why that "if" used to be fatal.
    /// </summary>
    private async Task SuperviseQueueLoop(string label, Func<Task> loop)
    {
        while (true)
        {
            try
            {
                await loop();
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke(
                    $"[{label} queue] Background loop crashed and is restarting: {ex.GetType().Name}: {ex.Message}");
            }

            // A short pause before restarting - if something is broken in a
            // way that fails immediately every time, this keeps it to a few
            // restarts a second rather than a tight, CPU-pegging crash loop.
            await Task.Delay(250);
        }
    }

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
    /// Calls a staggered driver away - their delay is up, or the race
    /// director just released them by hand.
    ///
    /// Built from the transponder rather than a standing, because at the
    /// moment this fires the driver may have no standing at all: on a
    /// staggered start they have not crossed anything yet, which is exactly
    /// why they need telling.
    ///
    /// Not held by a caution, unlike most per-driver lines. A release under
    /// yellow is still an instruction the driver has to hear - and a car
    /// left waiting on the line because the announcer went quiet is worse
    /// than one more voice line during a caution.
    /// </summary>
    public void NotifyDriverRelease(string transponderId)
    {
        if (Muted || string.IsNullOrWhiteSpace(transponderId))
        {
            return;
        }

        var type = AnnouncementEventType.DriverRelease;
        var config = _voiceStore.Current.Events.GetValueOrDefault(type.ToString());

        if (config is null || !config.IsEnabledFor(IsPracticeMode) || config.EffectiveTemplates.Count == 0)
        {
            StatusMessage?.Invoke($"[voice] {type} skipped for {transponderId} - no lines configured, or switched off.");
            return;
        }

        // Rendered at the green, when the grid is already known - see
        // PrimeReleaseLines. A release call is the one line where a
        // second of synthesis is a second of a car sitting still.
        if (TakePreparedRelease(transponderId, config) is { } prepared)
        {
            var volume = Math.Clamp(_voiceStore.Current.Volume * (VoiceBoostProvider?.Invoke() ?? 1f), 0.0, 1.0);

            AnnouncementTextReady?.Invoke(prepared.Text);

            EnqueueVoiceWork(type, transponderId, async () =>
            {
                StatusMessage?.Invoke($"[voice] {type} (prepared): \"{prepared.Text}\"");

                await _audioPlaybackLock.WaitAsync();
                try
                {
                    // Muted while it waited for the speaker.
                    if (Muted)
                    {
                        StatusMessage?.Invoke($"[voice] {type} (prepared): dropped - the announcer was muted.");
                        return;
                    }

                    await PlayPcmAsync(prepared.Pcm, prepared.Format, volume, DefaultTrailingSilenceSeconds, CurrentLeadingSilence);
                }
                finally
                {
                    _audioPlaybackLock.Release();
                }
            });

            return;
        }

        var template = config.EffectiveTemplates[TemplatePicker.Next(config.EffectiveTemplates.Count)];

        var text = ReleaseTextFor(transponderId, template, _voiceStore.Current);

        if (text.Length == 0)
        {
            return;
        }

        AnnouncementTextReady?.Invoke(text);

        // The same speed PrimeReleaseLines renders at, so this is the same
        // request - and a line saved by either is found by the other.
        var speed = config.SpeedFor(_voiceStore.Current.Speed);

        EnqueueVoiceWork(type, transponderId, () => SynthesizeAndPlayAsync(text, $"[voice] {type}", speedOverride: speed));
    }

    /// <summary>The spoken words for one driver's release call.</summary>
    private string ReleaseTextFor(string transponderId, string template, VoiceProfile profile)
    {
        var driverProfile = _profiles.Get(transponderId);

        return profile.SanitiseForModel(template
            .Replace("{driver}", DisplayNameFor(transponderId), StringComparison.OrdinalIgnoreCase)
            .Replace("{car}", AnnouncementText.OrMissing(driverProfile?.CarModel), StringComparison.OrdinalIgnoreCase)
            .WithDriverDetails(driverProfile)
            .TidyTemplate());
    }

    /// <summary>
    /// A release call held as decoded audio, ready to play as-is. Source
    /// says who spoke it, so a take the Windows voice made standing in is
    /// not played once ElevenLabs can speak again - see PreparedTakeMatches.
    /// </summary>
    private sealed record PreparedClip(string Text, byte[] Pcm, WaveFormat Format, string Signature, LineSource Source);

    private readonly Dictionary<string, PreparedClip> _releaseLines = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _releaseLinesLock = new();

    /// <summary>
    /// Spoken between one driver's call and the next in a batched render.
    ///
    /// It has to do two jobs: give the voice a real pause to land on, so the
    /// cut between clips falls in silence rather than mid-breath, and be
    /// findable afterwards in whatever characters the API reports back.
    /// </summary>
    private const string ReleaseBatchSeparator = " ... ";

    /// <summary>
    /// Renders every release call in ONE request and cuts the result into
    /// per-driver clips.
    ///
    /// The cuts come from the character timings the API returns alongside
    /// the audio, so they are exact rather than guessed at from silences.
    /// Returns false when the timings aren't available - some accounts and
    /// models don't return them - which is the caller's cue to render each
    /// line on its own instead.
    ///
    /// Worth knowing: this does not reduce what the render costs. ElevenLabs
    /// bills by characters, and the characters are the same either way. What
    /// it saves is one round trip instead of a dozen fired at once.
    /// </summary>
    private async Task<bool> TryRenderReleaseBatchAsync(
        IReadOnlyList<(string Transponder, string Text)> lines,
        VoiceProfile renderProfile,
        string signature)
    {
        if (lines.Count < 2)
        {
            return false; // nothing to batch - one line is its own request
        }

        var combined = string.Join(ReleaseBatchSeparator, lines.Select(l => l.Text));
        var timed = await SynthesizeTimedGatedAsync(renderProfile, combined);

        if (timed is null)
        {
            StatusMessage?.Invoke("[voice] Timed render unavailable for this voice or model - rendering release calls separately instead.");
            return false;
        }

        // Rebuilt from what the API says it spoke, NOT from what was sent:
        // normalisation and audio tags can both change the text, and the
        // timings line up with the former.
        var spoken = new string(timed.Characters.ToArray());
        var bounds = new List<(int Start, int End)>();
        var cursor = 0;

        for (var i = 0; i < lines.Count; i++)
        {
            var isLast = i == lines.Count - 1;
            var end = isLast
                ? spoken.Length
                : spoken.IndexOf(ReleaseBatchSeparator.Trim(), cursor, StringComparison.Ordinal);

            if (end < 0 || end <= cursor)
            {
                StatusMessage?.Invoke("[voice] Couldn't find the split points in the batched render - rendering release calls separately instead.");
                return false;
            }

            bounds.Add((cursor, end));
            cursor = isLast ? end : end + ReleaseBatchSeparator.Trim().Length;
        }

        var (pcm, format) = DecodeMp3(timed.Audio);
        var bytesPerSecond = format.AverageBytesPerSecond;
        var block = Math.Max(1, format.BlockAlign);

        for (var i = 0; i < lines.Count; i++)
        {
            var (startIndex, endIndex) = bounds[i];

            // DecodeMp3 threw away the first DecodeSkipSeconds, so the PCM
            // runs that much ahead of the reported times.
            var startSeconds = Math.Max(0, timed.StartSeconds[startIndex] - DecodeSkipSeconds);
            var endSeconds = Math.Max(startSeconds, timed.EndSeconds[Math.Min(endIndex, timed.EndSeconds.Count) - 1] - DecodeSkipSeconds);

            var startByte = AlignDown((long)(startSeconds * bytesPerSecond), block);
            var endByte = AlignDown((long)(endSeconds * bytesPerSecond), block);

            startByte = Math.Clamp(startByte, 0, pcm.Length);
            endByte = Math.Clamp(endByte, startByte, pcm.Length);

            if (endByte - startByte < block)
            {
                StatusMessage?.Invoke($"[voice] Batched render produced an empty clip for {lines[i].Transponder} - rendering release calls separately instead.");
                return false;
            }

            var clip = new byte[endByte - startByte];
            Array.Copy(pcm, startByte, clip, 0, clip.Length);

            lock (_releaseLinesLock)
            {
                _releaseLines[lines[i].Transponder] = new PreparedClip(lines[i].Text, clip, format, signature, LineSource.ElevenLabs);
            }

            // Saved as the clip it was cut to, under the same key a render
            // of this line on its own would have - so a later release call,
            // or the live fallback in NotifyDriverRelease, finds it. Always
            // genuine ElevenLabs audio: this batch is only ever an ElevenLabs
            // request, and the Windows voice's renders never come near it.
            try
            {
                VoiceLibrary.Shared.Save(renderProfile, lines[i].Text, EncodeWav(clip, format));
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke($"[voice] Couldn't keep the release call for {lines[i].Transponder} ({ex.Message}) - it plays today, and is rendered again next time.");
            }
        }

        StatusMessage?.Invoke($"[voice] {lines.Count} release calls rendered in one request and cut apart locally.");
        return true;
    }

    private static long AlignDown(long value, int blockAlign) => value - (value % blockAlign);

    /// <summary>
    /// Renders one release call per driver on the grid, in the background,
    /// so each lands the instant that car is due away.
    ///
    /// Possible here and not for the other per-driver lines because a
    /// staggered grid is known at the green: who is racing, and therefore
    /// every name that will be called. A fast lap can't be rendered in
    /// advance because nobody knows whose it will be; a release can.
    ///
    /// Safe to call repeatedly. A line already rendered with the current
    /// voice is left alone, and anything belonging to a previous grid - or
    /// rendered in a voice since changed - is thrown away rather than
    /// spoken in last week's announcer.
    /// </summary>
    public void PrimeReleaseLines(IEnumerable<string> transponders)
    {
        var profile = _voiceStore.Current;

        // Whichever voice will say them - not only when there is an
        // ElevenLabs key, which left the Windows voice (chosen, or standing
        // in) rendering every release call at the moment the car was due.
        if (!CanSpeak(profile))
        {
            return;
        }

        var config = profile.Events.GetValueOrDefault(nameof(AnnouncementEventType.DriverRelease));

        if (config is null || !config.IsEnabledFor(IsPracticeMode) || config.EffectiveTemplates.Count == 0)
        {
            return;
        }

        var signature = VoiceSignature(profile, config);
        var speed = config.SpeedFor(profile.Speed);
        var renderProfile = profile.CloneForDelivery(profile.Stability, profile.Style, speed);

        var grid = transponders
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var toRender = new List<string>();

        lock (_releaseLinesLock)
        {
            foreach (var key in _releaseLines.Keys.ToList())
            {
                // Including a stand-in take once ElevenLabs can speak again,
                // so the grid is rendered afresh in ElevenLabs.
                if (!PreparedTakeMatches(_releaseLines[key].Signature, _releaseLines[key].Source, signature, profile)
                    || !grid.Contains(key, StringComparer.OrdinalIgnoreCase))
                {
                    _releaseLines.Remove(key);
                }
            }

            foreach (var transponder in grid)
            {
                if (!_releaseLines.ContainsKey(transponder))
                {
                    toRender.Add(transponder);
                }
            }
        }

        if (toRender.Count == 0)
        {
            return;
        }

        var lines = new List<(string Transponder, string Text)>();

        foreach (var transponder in toRender)
        {
            var template = config.EffectiveTemplates[TemplatePicker.Next(config.EffectiveTemplates.Count)];
            var text = ReleaseTextFor(transponder, template, renderProfile);

            if (text.Length > 0)
            {
                lines.Add((transponder, text));
            }
        }

        if (lines.Count == 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            // The Windows voice chosen: no saved lines and no batch - the
            // library holds ElevenLabs lines only, and each release call is
            // simply rendered here on this PC, one after another.
            var windows = renderProfile.EffectiveEngine == VoiceEngine.Windows;

            // Nor with the HeatCaller voice's allowance spent: saved lines
            // are passed over then, exactly as RenderWithHeatCallerAsync
            // passes them over for a live line, so the release calls don't
            // come out in the HeatCaller voice on a night everything else is
            // in the Windows voice. Each goes line by line below, where the
            // Windows voice says it.
            var passOverSaved = windows || HeatCallerSavedLinesPassedOver(renderProfile);

            // Otherwise whatever the saved-voice library already has first.
            // Those need no request at all, and on a regular club night that
            // is most of the grid.
            var missing = new List<(string Transponder, string Text)>();

            foreach (var (key, line) in lines)
            {
                var saved = passOverSaved ? null : await VoiceLibrary.Shared.TryLoadAsync(renderProfile, line);

                if (saved is null)
                {
                    missing.Add((key, line));
                    continue;
                }

                try
                {
                    var (pcm, format) = DecodeAudio(saved);

                    lock (_releaseLinesLock)
                    {
                        _releaseLines[key] = new PreparedClip(line, pcm, format, signature, LineSource.Library);
                    }
                }
                catch (Exception ex)
                {
                    // A saved file that will not decode would fail the same
                    // way every week - forgotten, and rendered afresh below.
                    VoiceLibrary.Shared.Forget(renderProfile, line);
                    StatusMessage?.Invoke($"[voice] A saved release call for {key} wouldn't play ({ex.Message}) - rendering it again.");
                    missing.Add((key, line));
                }
            }

            if (missing.Count < lines.Count)
            {
                StatusMessage?.Invoke($"[voice] {Plural.Of(lines.Count - missing.Count, "release call")} ready from saved voice lines.");
            }

            if (missing.Count == 0)
            {
                return;
            }

            StatusMessage?.Invoke($"[voice] Rendering {Plural.Of(missing.Count, "release call")} ahead of the start.");

            // One request for the rest of the grid, cut apart locally. Falls
            // through to a request per driver when the timings needed to cut
            // it accurately aren't available.
            //
            // Only when ElevenLabs can be asked at all: not for the Windows
            // voice, not with no key (the stand-in is saying these), and not
            // while ElevenLabs rests after a lost connection - each of those
            // goes line by line below, to whichever voice is speaking.
            // ElevenLabs and its own key, explicitly: the batch is the
            // with-timestamps endpoint, which is ElevenLabs' and not
            // something the relay offers. Written as "is the club's own
            // key" rather than "is not Windows", because with the
            // HeatCaller voice a club that still HAS an old key of its own
            // would have had every release call - every driver, by name -
            // billed to it, in the one mode whose promise is that it never
            // is. Those go line by line below, through the relay.
            var tryBatch = renderProfile.EffectiveEngine == VoiceEngine.ElevenLabs
                           && ElevenLabsReady(renderProfile)
                           && !(StandInAllowed(renderProfile) && ElevenLabsRestingFor(renderProfile));

            // The voice chosen again since these were set going - the Windows
            // voice picked while the grid was being rendered, say: what is
            // left is for a voice nobody is using, and with the Windows voice
            // chosen every line asked of ElevenLabs would be paid for and
            // thrown away.
            if (_voiceStore.Current.EffectiveEngine != renderProfile.EffectiveEngine)
            {
                return;
            }

            if (tryBatch)
            {
                try
                {
                    if (await TryRenderReleaseBatchAsync(missing, renderProfile, signature))
                    {
                        return;
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage?.Invoke($"[voice] Batched release render failed ({ex.Message}) - falling back to one request per driver.");

                    // No internet: rest ElevenLabs now, so the lines below go
                    // to the stand-in at once rather than each waiting out
                    // the same dead connection first.
                    if (StandInAllowed(renderProfile) && IsConnectionFailure(ex))
                    {
                        BeginElevenLabsRest(ex);
                    }
                }
            }

            foreach (var (key, line) in missing)
            {
                // As above, before every line - a grid takes a while.
                if (_voiceStore.Current.EffectiveEngine != renderProfile.EffectiveEngine)
                {
                    return;
                }

                lock (_releaseLinesLock)
                {
                    // A batch that half-succeeded, or a re-prime, shouldn't
                    // re-render what is already in hand.
                    if (_releaseLines.TryGetValue(key, out var existing)
                        && PreparedTakeMatches(existing.Signature, existing.Source, signature, profile))
                    {
                        continue;
                    }
                }

                try
                {
                    // The same rules as a live line: the Windows voice when
                    // it is chosen, ElevenLabs otherwise, and the Windows
                    // voice standing in for any line ElevenLabs can't do.
                    var rendered = await RenderLineAsync(renderProfile, line);
                    var (pcm, format) = DecodeAudio(rendered.Audio);

                    lock (_releaseLinesLock)
                    {
                        _releaseLines[key] = new PreparedClip(line, pcm, format, signature, rendered.Source);
                    }

                    StatusMessage?.Invoke(rendered.Source == LineSource.StandIn
                        ? $"[voice] Release call ready for {key}, in the Windows voice (standing in for ElevenLabs): \"{line}\""
                        : $"[voice] Release call ready for {key}: \"{line}\"");
                }
                catch (Exception ex)
                {
                    // Nothing is lost - the call still happens, it just has
                    // to synthesise at the moment the car is due away.
                    StatusMessage?.Invoke($"[voice] Couldn't prepare the release call for {key} ({ex.Message}). It will still speak, a moment later.");
                }
            }
        });
    }

    /// <summary>
    /// Takes this driver's prepared release call, if one is ready and still
    /// matches the current voice. Removed as it's taken: a release happens
    /// once per car per race.
    /// </summary>
    private PreparedClip? TakePreparedRelease(string transponderId, AnnouncementEventConfig config)
    {
        var profile = _voiceStore.Current;
        var signature = VoiceSignature(profile, config);

        lock (_releaseLinesLock)
        {
            if (!_releaseLines.TryGetValue(transponderId, out var prepared)
                || !PreparedTakeMatches(prepared.Signature, prepared.Source, signature, profile))
            {
                return null;
            }

            _releaseLines.Remove(transponderId);
            return prepared;
        }
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

    /// <summary>Reset session-local state (leader/lap/streak tracking) - call this on each new Connect.</summary>
    /// <summary>
    /// Immediately clears every QUEUED (not yet started) voice/sound
    /// announcement and cancels any pending per-lap batches waiting to
    /// flush. Whatever's playing at THIS INSTANT (at most one short sound
    /// or voice line) is left to finish naturally rather than being cut off
    /// mid-playback, but nothing further follows it.
    ///
    /// Call this whenever a race is being stopped/reset from outside its
    /// own normal end-of-race flow. Without it, a backlog of
    /// already-queued announcements - built up faster than they could be
    /// spoken/played, which a fast-paced source like Demo Mode can do
    /// easily - keeps draining and playing for a long time after the thing
    /// that generated it has already stopped, which reads as the app being
    /// stuck rather than as a backlog working itself off.
    /// </summary>
    /// <param name="keepPriority">
    /// Keep race-control announcements - the checkered flag, time over,
    /// and the recap - rather than binning them with the lap chatter.
    ///
    /// Default true because clearing them is almost never what's wanted:
    /// a demo finishing called this the instant the race ended and wiped
    /// the finish lines that had been queued a moment earlier, so the race
    /// ended in silence.
    /// </param>
    public void StopAllPendingAudio(bool keepPriority = true)
    {
        int voiceCleared, soundCleared, batchesCleared;

        // A stop or a reset is never followed by the green the ticks were
        // counting down to. They are not lap chatter, so the rule below
        // would keep them.
        CancelCountdownTicks();

        lock (_voiceQueueLock)
        {
            if (keepPriority)
            {
                var kept = _voiceQueue.Where(item => IsPriorityAnnouncement(item.Type)).ToList();
                voiceCleared = _voiceQueue.Count - kept.Count;

                _voiceQueue.Clear();
                _voiceQueue.AddRange(kept);
            }
            else
            {
                voiceCleared = _voiceQueue.Count;
                _voiceQueue.Clear();
            }
        }

        lock (_soundQueueLock)
        {
            // Same rule for sounds as for voice: keeping race-control lines
            // means keeping their start/finish sounds too, or the finish
            // call survives and its fanfare doesn't. (Muting clears the lot.)
            soundCleared = keepPriority
                ? RemoveQueuedSounds(item => IsPerLapSound(item.Kind))
                : RemoveQueuedSounds(_ => true);
        }

        lock (_batchLock)
        {
            batchesCleared = _pendingPerLapBatches.Count;
            foreach (var cts in _pendingBatchCancellation.Values)
            {
                cts.Cancel();
                cts.Dispose();
            }
            _pendingBatchCancellation.Clear();
            _pendingPerLapBatches.Clear();
        }

        if (voiceCleared + soundCleared + batchesCleared > 0)
        {
            StatusMessage?.Invoke($"[voice] Cleared {Plural.Of(voiceCleared, "queued voice line")}, {Plural.Of(soundCleared, "queued sound")}, and {Plural.Of(batchesCleared, "pending batch", "pending batches")}.");
        }
    }

    public void ResetSession()
    {
        EndPostRaceQuiet("the session was reset");

        lock (_voiceQueueLock)
        {
            _standings.Clear();
            _currentLeaderTransponder = null;
            _provisionalPoleTransponder = null;
            _poleCalledFromGateFor = null;

            // Or the next race would skip its first report - the counter
            // would still hold the last race's lap number.
            _lastPositionReportLap = -1;

            // Same reasoning: a driver mid-cycle at the flag would start the
            // next race offset, hearing their time on laps 2, 5, 8 instead
            // of 3, 6, 9.
            _lapTimeCounts.Clear();
            _lastLapElapsedMs.Clear();
            _lapCrossings.Clear();
            _streakCounts.Clear();
            _rivalryLastAhead.Clear();
            _lappedAnnounced.Clear();
            _battlesAnnounced.Clear();

            // Learned per track, and the next session may be a different one.
            _typicalLapMs = 0;
            lock (_streakLock)
            {
                _currentStreaks.Clear();
                _streakLeader = null;
                _streakLeaderCount = 0;
            }
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
    }

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
    /// Reads out the running order every N leader laps.
    ///
    /// Keyed off the LEADER's lap count rather than a timer or a raw lap
    /// counter: "every 5 laps" should mean five laps of the race, not five
    /// transponder passes from anyone on track - on a full grid those are
    /// wildly different things, and a per-pass trigger would fire the
    /// report constantly.
    /// </summary>

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
            StatusMessage?.Invoke($"[sound] Skipped {label} - disabled for {(IsPracticeMode ? "practice" : "races")} in Sound Effects settings.");
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

        // "finished" rides with the queued item rather than being set here,
        // so it is signalled however the sound leaves the queue - see
        // QueuedSound.
        EnqueueSoundWork(kind, finished, async () =>
        {
            // Registered from the moment this sound becomes the current one,
            // before it waits for the speaker, so a flag or a mute landing
            // during that wait stops it too.
            using var cancel = new CancellationTokenSource();
            BeginPlayingSound(kind, cancel);

            try
            {
                StatusMessage?.Invoke($"[sound] Playing {label} (volume={volume:0.00})...");

                await _audioPlaybackLock.WaitAsync();
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
                        await ToneGenerator.PlayFileAsync(file, volume, cancel.Token, CurrentLeadingSilence, semitones);
                    }
                    else
                    {
                        await playGenerated(volume, cancel.Token);
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
                    _audioPlaybackLock.Release();
                }
            }
            finally
            {
                EndPlayingSound(cancel);
            }
        });
    }

    /// <summary>Marks a sound as the one playing, so it can be cut off.</summary>
    private void BeginPlayingSound(SoundEffectKind kind, CancellationTokenSource cancel)
    {
        lock (_playingSoundLock)
        {
            _playingSoundCancel = cancel;
            _playingSoundKind = kind;
        }
    }

    /// <summary>
    /// Clears the playing sound. Done before its token is disposed, and under
    /// the same lock StopPlayingSound cancels under, so a stop can never
    /// reach a disposed token.
    /// </summary>
    private void EndPlayingSound(CancellationTokenSource cancel)
    {
        lock (_playingSoundLock)
        {
            if (ReferenceEquals(_playingSoundCancel, cancel))
            {
                _playingSoundCancel = null;
            }
        }
    }

    /// <summary>
    /// Cuts off the sound effect playing (or waiting for the speaker) right
    /// now. Returns true if there was one to cut.
    /// </summary>
    /// <param name="except">Leave it playing if it is this kind - a call's own lead-in sound.</param>
    /// <param name="only">Cut it only if it is this kind.</param>
    private bool StopPlayingSound(SoundEffectKind? except = null, SoundEffectKind? only = null)
    {
        lock (_playingSoundLock)
        {
            var cancel = _playingSoundCancel;

            if (cancel is null || cancel.IsCancellationRequested)
            {
                return false;
            }

            if ((except is { } spared && _playingSoundKind == spared)
                || (only is { } wanted && _playingSoundKind != wanted))
            {
                return false;
            }

            // Cancelling stops the output (ToneGenerator's registration).
            // Its PlaybackStopped handler only completes a task, so nothing
            // calls back into code that takes this lock.
            cancel.Cancel();
            return true;
        }
    }

    /// <summary>
    /// Takes queued sounds out of the queue, signalling each one's
    /// "finished" so nothing waits on a sound that will now never play.
    /// Call with _soundQueueLock held. Returns how many were removed.
    /// </summary>
    private int RemoveQueuedSounds(Predicate<QueuedSound> match)
    {
        var removed = _soundQueue.FindAll(match);

        if (removed.Count == 0)
        {
            return 0;
        }

        _soundQueue.RemoveAll(match);

        foreach (var item in removed)
        {
            item.Finished?.TrySetResult();
        }

        return removed.Count;
    }

    private static readonly string[] DigitWords =
    {
        "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine",
    };

    /// <summary>
    /// Formats a millisecond duration as speech text with an explicit
    /// "point" between the whole and fractional seconds, e.g. "5 point
    /// eight eight seconds". The fractional digits are spelled out as
    /// words rather than left as digit characters - just separating
    /// digits with a space ("8 8") wasn't reliable either: it worked
    /// sometimes, but ElevenLabs' own text normalization apparently still
    /// re-collapsed it back into the two-digit number "eighty-eight" other
    /// times, since digit characters still look like a number in context.
    /// Spelling them as words removes anything number-shaped for the
    /// engine to reinterpret.
    /// </summary>
    private static string FormatSecondsForSpeech(long ms)
    {
        var totalCentiseconds = (long)Math.Round(ms / 10.0);
        var whole = totalCentiseconds / 100;
        var fraction = totalCentiseconds % 100;
        // InvariantCulture and a bounds-checked lookup. Some cultures use
        // non-ASCII digit shapes, which would make (c - '0') fall outside
        // the array and throw from the middle of a race announcement.
        var fractionDigits = fraction
            .ToString("00", System.Globalization.CultureInfo.InvariantCulture)
            .Select(ch => ch >= '0' && ch <= '9' ? DigitWords[ch - '0'] : ch.ToString());
        return $"{whole} point {string.Join(" ", fractionDigits)} seconds";
    }

    /// <summary>
    /// The same duration written for READING rather than speaking - "5.88s".
    ///
    /// The spoken form deliberately spells digits out as words so the TTS
    /// engine can't re-collapse them into "eighty-eight", but that text was
    /// also what got shown on the Big Screen and the web pages, where it
    /// read as "5 point eight eight seconds". Screens want the numerals.
    /// </summary>
    private static string FormatSecondsForDisplay(long ms)
    {
        // Full millisecond precision. The centisecond rounding belongs to
        // the SPOKEN form only - nobody wants to hear "six point nine three
        // five seconds" - but a screen should show what was actually timed.
        // This previously reused the spoken rounding, so 6.935s displayed
        // as 6.94s and the number on screen disagreed with the lap chart.
        return $"{ms / 1000.0:0.000}s";
    }

    private static readonly Random TemplatePicker = new();

    /// <param name="suffix">
    /// Appended to the finished line, for context the template itself can't
    /// carry - currently the runner-up's streak.
    /// </param>
    private void EnqueueVoice(AnnouncementEventType type, RaceStanding? standing, GatePassEvent? gate, long? lapTimeMs = null, int? streakCount = null, string? suffix = null, string? roundLabel = null, string? className = null, string? marshals = null)
    {
        if (Muted)
        {
            return;
        }

        var profile = _voiceStore.Current;
        if (!profile.Events.TryGetValue(type.ToString(), out var config) || !config.IsEnabledFor(IsPracticeMode))
        {
            // Says which event was skipped and why - a silent announcement
            // is otherwise indistinguishable from one that never fired.
            StatusMessage?.Invoke(config is null
                ? $"[voice] {type} skipped - no configuration entry."
                : $"[voice] {type} skipped - switched off for {(IsPracticeMode ? "practice" : "races")}.");
            return;
        }

        if (HeldByCaution(type, config))
        {
            StatusMessage?.Invoke($"[voice] {type} held - a flag is out.");
            return;
        }

        // A flag with a line already rendered skips synthesis entirely.
        if (IsInterruptingAnnouncement(type) && TrySpeakPreparedFlag(type, config))
        {
            return;
        }

        var candidates = config.EffectiveTemplates;
        if (candidates.Count == 0)
        {
            return;
        }

        // Pick one phrasing at random each time, so an event that can
        // legitimately fire many times in a row (e.g. several new fastest
        // laps back to back) doesn't read out the identical sentence
        // every single time.
        var chosenTemplate = candidates.Count == 1 ? candidates[0] : candidates[TemplatePicker.Next(candidates.Count)];

        var transponder = standing?.Transponder ?? gate?.Transponder;
        var driverProfile = transponder is not null ? _profiles.Get(transponder) : null;

        // Diagnostic: this exact lookup has been reported as returning the
        // raw transponder ID instead of a configured driver name, and the
        // obvious first fix (case sensitivity) didn't resolve it - so log
        // enough here to see definitively, on the next test, whether the
        // profile is being found at all and what's actually in it, rather
        // than guessing at a second cause blind.
        StatusMessage?.Invoke(driverProfile is null
            ? $"[voice] Driver lookup: transponder=\"{transponder}\" -> NO PROFILE FOUND for this exact key."
            : $"[voice] Driver lookup: transponder=\"{transponder}\" -> found profile, DriverName=\"{driverProfile.DriverName}\"");

        // SpokenName prefers the announcer-specific name when one is set -
        // a nickname the crowd knows, or a spelling the voice actually
        // pronounces correctly - and falls back to the real name.
        var driver = driverProfile is not null && !string.IsNullOrWhiteSpace(driverProfile.SpokenName)
            ? driverProfile.SpokenName
            : standing?.DisplayName ?? transponder ?? "Racer";

        // Diagnostic: an announcement was reported with {driver} resolving
        // to an empty string ("The  get fastest lap!") despite
        // RaceStanding.DisplayName always falling back to the transponder
        // ID and never returning empty - which shouldn't be possible from
        // the code as written. Logging the raw chosen template alongside
        // the resolved driver value will show definitively, on the next
        // occurrence, whether "driver" itself is genuinely empty here or
        // something else (e.g. an unusual character in a saved custom
        // template) is going on, rather than guessing again.
        StatusMessage?.Invoke($"[voice] Template chosen: \"{chosenTemplate}\"  resolved driver=\"{driver}\"");
        var car = driverProfile?.CarModel ?? string.Empty;
        var lap = standing?.Laps.ToString() ?? string.Empty;
        var time = standing is { FastLapMs: > 0 } ? FormatSecondsForSpeech(standing.FastLapMs) : string.Empty;
        var lapTimeText = lapTimeMs.HasValue ? FormatSecondsForSpeech(lapTimeMs.Value) : string.Empty;

        // Display variants, substituted into a parallel string so screens
        // show numerals while the voice still gets the spelled-out form.
        var timeDisplay = standing is { FastLapMs: > 0 } ? FormatSecondsForDisplay(standing.FastLapMs) : string.Empty;
        var lapTimeDisplay = lapTimeMs.HasValue ? FormatSecondsForDisplay(lapTimeMs.Value) : string.Empty;
        var gateId = gate?.Gate ?? string.Empty;
        var streakText = streakCount?.ToString() ?? string.Empty;

        var roundText = roundLabel ?? string.Empty;
        var classText = className ?? string.Empty;
        var marshalsText = marshals ?? string.Empty;

        // Every one of these can legitimately be empty - a driver with no
        // car model, a session with no round, a lap with no time yet - and
        // an empty substitution used to leave the words that introduced
        // it: "in his !". OrMissing marks them; TidyTemplate takes the
        // clause out.
        var text = chosenTemplate
            .Replace("{driver}", driver)
            .Replace("{car}", AnnouncementText.OrMissing(car))
            .Replace("{lap}", AnnouncementText.OrMissing(lap))
            .Replace("{time}", AnnouncementText.OrMissing(time))
            .Replace("{laptime}", AnnouncementText.OrMissing(lapTimeText))
            .Replace("{gate}", AnnouncementText.OrMissing(gateId))
            .Replace("{streak}", AnnouncementText.OrMissing(streakText))
            .Replace("{round}", AnnouncementText.OrMissing(roundText))
            .Replace("{class}", AnnouncementText.OrMissing(classText))
            .WithMarshals(marshalsText)
            .WithDriverDetails(driverProfile)
            .TidyTemplate();

        var displayText = chosenTemplate
            .Replace("{driver}", driver)
            .Replace("{car}", AnnouncementText.OrMissing(car))
            .Replace("{lap}", AnnouncementText.OrMissing(lap))
            .Replace("{time}", AnnouncementText.OrMissing(timeDisplay))
            .Replace("{laptime}", AnnouncementText.OrMissing(lapTimeDisplay))
            .Replace("{gate}", AnnouncementText.OrMissing(gateId))
            .Replace("{streak}", AnnouncementText.OrMissing(streakText))
            .Replace("{round}", AnnouncementText.OrMissing(roundText))
            .Replace("{class}", AnnouncementText.OrMissing(classText))
            .WithMarshals(marshalsText)
            .WithDriverDetails(driverProfile)
            .TidyTemplate();

        // "Include lap time" auto-appends the relevant time for anyone who
        // wants it spoken without writing {laptime}/{time} into every one
        // of their phrasing variants by hand. Skipped if the CHOSEN line
        // already mentions it, so it's never said twice.
        if (type == AnnouncementEventType.LapCompleted && config.IncludeLapTime && lapTimeText.Length > 0
            && !chosenTemplate.Contains("{laptime}", StringComparison.OrdinalIgnoreCase))
        {
            text = $"{text} Lap time: {lapTimeText}.".Trim();
            displayText = $"{displayText} Lap time: {lapTimeDisplay}.".Trim();
        }

        if (type == AnnouncementEventType.FastLap && config.IncludeLapTime && time.Length > 0
            && !chosenTemplate.Contains("{time}", StringComparison.OrdinalIgnoreCase))
        {
            text = $"{text} New fastest time: {time}.".Trim();
            displayText = $"{displayText} New fastest time: {timeDisplay}.".Trim();
        }

        // The dedicated lap-time-only announcement: either "Lap time: X
        // seconds." or, with the label turned off, just "X seconds." - the
        // whole point of this event is to be as short as the user wants.
        // Optionally prefixed with the driver's name, for when more than
        // one racer is on track and a bare number doesn't say whose it is.
        if (type == AnnouncementEventType.LapTime && lapTimeText.Length > 0)
        {
            text = config.SayLapTimeLabel ? $"Lap time: {lapTimeText}." : $"{lapTimeText}.";
            displayText = config.SayLapTimeLabel ? $"Lap time: {lapTimeDisplay}." : $"{lapTimeDisplay}.";
            if (config.IncludeDriverNameInLapTime)
            {
                text = $"{driver}, {text}";
                displayText = $"{driver}, {displayText}";
            }
        }

        if (text.Length == 0)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(suffix))
        {
            text = (text + suffix).Trim();
            displayText = (displayText + suffix).Trim();
        }

        // Screens get the numeral form; the voice engine gets the
        // spelled-out one.
        AnnouncementTextReady?.Invoke(displayText.Length > 0 ? displayText : text);

        // At this announcement's own Speed. The Voice Settings page offers
        // one per announcement and its Test button plays at it, but every
        // line through here used to go out at the profile's speed - so a
        // race never sounded like its test. It is also what makes a
        // prepared flag line and this live fallback the same request (see
        // PrimeFlagLineAsync), so either one's saved copy serves both.
        var speed = config.SpeedFor(profile.Speed);

        EnqueueVoiceWork(type, transponder, () => SynthesizeAndPlayAsync(text, "[voice] Announcing", speedOverride: speed));
    }

    /// <summary>
    /// Shared synthesize-then-play logic used by every voice announcement
    /// path, generic or rivalry-specific alike - ElevenLabs or the Windows
    /// voice, as RenderLineAsync decides.
    /// </summary>
    /// <param name="skippedLogMessage">
    /// Said when ElevenLabs is chosen and the line can't be spoken at all -
    /// no key or voice, and no stand-in to cover. The Windows voice being
    /// unavailable says so in its own words.
    /// </param>
    /// <param name="deliveryOverride">
    /// Optional per-call voice settings. Used by the race summary, which
    /// wants more expressive delivery than a routine lap call - turning the
    /// whole announcer up instead would make every announcement over-acted.
    /// </param>
    /// <param name="honourMute">
    /// False only for a test asked for by hand, which must speak even while
    /// the announcer is muted. Everything else checks the mute again after
    /// synthesis and after waiting for the speaker, because a mute can land
    /// while a line is in either.
    /// </param>
    private async Task SynthesizeAndPlayAsync(
        string text,
        string announcingLogPrefix,
        string skippedLogMessage = "[voice] Skipped announcement - ElevenLabs API key or voice not configured.",
        (double Stability, double Style)? deliveryOverride = null,
        double? speedOverride = null,
        bool honourMute = true)
    {
        var currentProfile = _voiceStore.Current;

        if (deliveryOverride is { } delivery)
        {
            // A shallow copy so the override applies to THIS line only and
            // doesn't leak into the stored profile - mutating the real one
            // would leave every later announcement using summary settings.
            currentProfile = currentProfile.CloneForDelivery(delivery.Stability, delivery.Style, speedOverride);
        }
        else if (speedOverride is { } speedOnly)
        {
            // Speed without a delivery override - a long announcement that
            // needs room to breathe but no change in expression.
            currentProfile = currentProfile.CloneForDelivery(
                currentProfile.Stability, currentProfile.Style, speedOnly);
        }

        // Strips [excited]-style tags on models that can't use them, so a
        // tagged template doesn't get read out bracket by bracket when
        // someone switches off v3.
        var beforeSanitise = text;
        text = currentProfile.SanitiseForModel(text);

        // Reports what the RACE actually used. Tests build a profile from
        // the on-screen controls while races read the SAVED one, so an
        // unsaved model change makes tests and races behave differently -
        // this makes that visible instead of leaving it to be guessed at.
        //
        // ElevenLabs only. The Windows voice never uses tags, so taking them
        // out for it is expected rather than a model to go and change - and
        // the advice to pick a v3 model would be wrong for it.
        if (currentProfile.EffectiveEngine == VoiceEngine.ElevenLabs
            && !ReferenceEquals(beforeSanitise, text) && beforeSanitise != text)
        {
            StatusMessage?.Invoke(
                $"[voice] Audio tags stripped - saved model is \"{currentProfile.ModelId}\", which doesn't support them. "
                + "Pick a v3 model on the Voice Settings tab - it saves as you change it.");
        }

        // Whether ANY voice can say it. This asked for an ElevenLabs key and
        // voice only, which with the Windows voice chosen - or standing in
        // for a club with no key - would have skipped every line.
        if (!CanSpeak(currentProfile))
        {
            StatusMessage?.Invoke(currentProfile.EffectiveEngine == VoiceEngine.Windows
                ? $"[voice] Skipped announcement - {WindowsVoice.UnavailableReason ?? "the Windows voice can't speak on this PC"}."
                : skippedLogMessage);
            return;
        }

        if (IsSupersededByInterrupt())
        {
            StatusMessage?.Invoke($"{announcingLogPrefix}: dropped \"{text}\" - a flag call interrupted before it spoke.");
            return;
        }

        if (honourMute && Muted)
        {
            StatusMessage?.Invoke($"{announcingLogPrefix}: dropped \"{text}\" - the announcer is muted.");
            return;
        }

        StatusMessage?.Invoke($"{announcingLogPrefix}: \"{text}\"");
        var synthesis = await RenderLineAsync(currentProfile, text);
        var audio = synthesis.Audio;

        // A race-control call with its own sound - the start horn, the
        // time-up buzzer, the finish fanfare - lets the sound play first.
        // Waited for AFTER synthesis, so the words are rendered while the
        // sound plays and follow it straight away rather than a network
        // round trip later.
        if (_leadInFor.Value is { } leadInType)
        {
            await WaitForLeadInSoundAsync(leadInType);
        }

        await _audioPlaybackLock.WaitAsync();
        try
        {
            // Checked again after synthesis and after the playback lock: a
            // flag pressed while this line was on the network (or waiting
            // for the speaker) has already cleared the decks, and this line
            // must not then start speaking over it.
            if (IsSupersededByInterrupt())
            {
                StatusMessage?.Invoke($"{announcingLogPrefix}: dropped \"{text}\" - a flag call interrupted while it was being synthesised.");
                return;
            }

            // Muted while it was being synthesised or waiting its turn.
            if (honourMute && Muted)
            {
                StatusMessage?.Invoke($"{announcingLogPrefix}: dropped \"{text}\" - the announcer was muted before it spoke.");
                return;
            }

            // Boosted only when something else is actually playing loudly -
            // supplied by the ambience service, which knows its own current
            // level. A blanket boost would just make every announcement
            // louder, including over silence.
            var volume = currentProfile.Volume * (VoiceBoostProvider?.Invoke() ?? 1f);

            try
            {
                await PlayAsync(audio, Math.Clamp(volume, 0f, 1f), DefaultTrailingSilenceSeconds, CurrentLeadingSilence);
            }
            catch (Exception ex) when (synthesis.FromLibrary && IsUndecodable(ex))
            {
                // A saved line that won't decode would fail like this every
                // time it came round. Forgotten, so the next time it is
                // wanted it is rendered afresh.
                VoiceLibrary.Shared.Forget(currentProfile, text);
                throw;
            }
        }
        finally
        {
            _audioPlaybackLock.Release();
        }
    }

    /// <summary>What NAudio throws for bytes that are not the audio they claim to be - as opposed to a missing speaker.</summary>
    private static bool IsUndecodable(Exception ex) =>
        ex is InvalidDataException or FormatException or EndOfStreamException;

    // Tracks whether ElevenLabs is CURRENTLY refusing requests for quota
    // reasons, so the console shows one clear notice when voice goes down
    // and one clear notice when it comes back - instead of repeating the
    // same raw JSON error block on every single announcement attempt for
    // the rest of the race. Sound effects never touch this since they
    // don't call ElevenLabs at all.
    //
    // Set and cleared only under _voiceHealthLock (NoteQuotaExceeded,
    // NoteElevenLabsAnswered) - flag calls and lines rendered in advance
    // reach it from pool threads, not just the voice queue - and volatile
    // for the pages that read it without the lock.
    private volatile bool _voiceQuotaExceeded;

    /// <summary>
    /// True when ElevenLabs last refused a request for quota reasons.
    ///
    /// Exposed so the UI can say "no voice because the quota is gone"
    /// rather than leaving someone to wonder why a Test button does
    /// nothing - the failure happens asynchronously on the voice queue,
    /// long after the button returns.
    /// </summary>
    public bool VoiceQuotaExceeded => _voiceQuotaExceeded;

    /// <summary>
    /// Whether a voice could speak at all right now, and why not if it can't.
    ///
    /// Null while the Windows voice is standing in for ElevenLabs - there IS
    /// a voice then. DescribeStandIn says why it is standing in.
    /// </summary>
    public string? DescribeVoiceBlocker()
    {
        var profile = _voiceStore.Current;

        if (Muted)
        {
            return "the announcer is muted";
        }

        if (profile.EffectiveEngine == VoiceEngine.Windows)
        {
            return WindowsVoice.UnavailableReason;
        }

        if (StandInReasonFor(profile) is not null)
        {
            return null;
        }

        if (profile.EffectiveEngine == VoiceEngine.HeatCaller)
        {
            if (!profile.HeatCallerReady)
            {
                return "no HeatCaller voice is chosen";
            }

            // Nothing is standing in - that was checked above - so the only
            // way this cannot speak is the relay refusing on a PC with no
            // Windows voice to cover for it.
            lock (_heatCallerLock)
            {
                return WindowsVoice.IsAvailable ? null : _heatCallerReason;
            }
        }

        if (string.IsNullOrWhiteSpace(profile.ApiKey))
        {
            return "no ElevenLabs API key is set";
        }

        if (string.IsNullOrWhiteSpace(profile.VoiceId))
        {
            return "no voice is selected";
        }

        if (_voiceQuotaExceeded)
        {
            return "the ElevenLabs quota is exhausted";
        }

        return null;
    }

    /// <summary>
    /// Why the free Windows voice is standing in for ElevenLabs right now -
    /// "the free Windows voice is standing in - the ElevenLabs quota is used
    /// up" - or null when it isn't. For the Mixer, which shows it as a notice
    /// rather than the red NO VOICE banner: the announcer can still speak.
    /// </summary>
    public string? DescribeStandIn() =>
        StandInReasonFor(_voiceStore.Current) is { } reason
            ? $"the free Windows voice is standing in - {reason}"
            : null;

    // True while a voice line is actively being synthesized/played. Used to
    // DROP new per-lap announcements outright rather than queue them behind
    // it - on a fast track, several laps can pass in the time one
    // announcement takes to speak (plus the ~1s of audio device warm-up
    // padding baked into every one), and a queued backlog would read out
    // increasingly stale info well after it stopped being relevant. A
    // dropped announcement is better than a late one here.
    private volatile bool _isVoiceSpeaking;

    /// <summary>
    /// Race-control announcements that must never be dropped or delayed
    /// behind routine chatter. The drop-while-speaking guard and FIFO
    /// ordering exist to stop per-lap announcements piling up on a fast
    /// track - but a yellow or red is safety information, it's rare, and
    /// losing one because the announcer happened to be mid-sentence about
    /// someone's lap time is exactly backwards.
    /// </summary>
    /// <summary>
    /// Calls that cut straight across whatever is being said.
    ///
    /// A flag is an instruction to the drivers, and an instruction that
    /// waits politely for a lap-time announcement to finish is late by the
    /// time it lands. These stop the current line mid-word, jump the queue,
    /// and start synthesising the moment the button is pressed.
    /// </summary>
    private static bool IsInterruptingAnnouncement(AnnouncementEventType type) => type is
        AnnouncementEventType.RaceStarted or
        AnnouncementEventType.GreenFlagResumed or
        AnnouncementEventType.YellowFlag or
        AnnouncementEventType.RedFlag or
        AnnouncementEventType.RaceCompleted or
        AnnouncementEventType.RaceTimeOver;

    private static bool IsPriorityAnnouncement(AnnouncementEventType type) => type is
        AnnouncementEventType.RaceStaging or
        AnnouncementEventType.CountdownStarted or
        AnnouncementEventType.RaceStarted or
        AnnouncementEventType.YellowFlag or
        AnnouncementEventType.RedFlag or
        AnnouncementEventType.GreenFlagResumed or
        AnnouncementEventType.RaceTimeOver or
        AnnouncementEventType.RaceCompleted or

        // A release is an instruction to a driver sitting still waiting for
        // it. Dropping one behind lap chatter leaves a car on the line.
        AnnouncementEventType.DriverRelease or
        AnnouncementEventType.AchievementUnlocked or

        // The round being called to the grid - an instruction, and made
        // immediately before staging, whose own priority purge used to bin
        // it. See NotifyGridCall.
        AnnouncementEventType.GridCall or

        // Reminders are deliberate, scheduled speech - a schedule warning,
        // the recap teaser, the race summary itself. Without this they were
        // dropped whenever something else happened to be speaking, and the
        // teaser fires immediately after the RaceCompleted call, so it was
        // dropped every single time.
        AnnouncementEventType.Reminder;

    /// <summary>
    /// One-off race moments that wait their turn instead of being dropped
    /// when something else is mid-sentence.
    ///
    /// Each fires once per race (or rarely, for a record), so there is no
    /// backlog to fear - but they were being thrown away by the
    /// drop-while-speaking rule meant for lap chatter, and on a busy track
    /// something is nearly always speaking, so "last lap" and "one minute to
    /// go" were routinely never heard.
    ///
    /// Kept separate from IsPriorityAnnouncement on purpose. Priority also
    /// means "speaks through a yellow", "survives a flag clearing the
    /// queue" and "bins the routine lines queued ahead of it" - none of
    /// which is right for these. They only need to not be dropped - which
    /// includes not being binned by a release call or reminder queued after
    /// them (see EnqueueVoiceWork). A flag still clears them.
    /// </summary>
    private static bool IsWorthWaitingFor(AnnouncementEventType type) => type is
        AnnouncementEventType.LastLap or
        AnnouncementEventType.FinalMinute or
        AnnouncementEventType.TrackRecord;

    /// <summary>
    /// Queues a voice line, first dropping any PENDING (not yet started)
    /// announcement of the same type for the same transponder. Without
    /// this, a driver on a fast track could rack up several more laps
    /// before an early streak announcement even starts playing, and the
    /// backlog would read out increasingly stale streak counts one after
    /// another - "3 lap streak... 4 lap streak... 5 lap streak..." - long
    /// after the driver has moved on. Only the most recent one matters.
    /// Announcements already speaking are left alone; only queued-but-not-
    /// started items are eligible to be superseded.
    ///
    /// Priority (race-control) announcements are exempt from the
    /// drop-while-speaking rule and clear routine items out of the queue -
    /// see IsPriorityAnnouncement.
    /// </summary>
    /// <summary>
    /// Whether a newer line about a driver REPLACES an older one still
    /// waiting to be spoken.
    ///
    /// True for almost everything, and rightly so: a driver's lap time,
    /// their position, their streak are all STATUS. Two of them queued for
    /// the same driver means the first is already out of date, and saying
    /// it before the second one would be saying something untrue.
    ///
    /// Achievements are not status, they are FACTS - and that is the bug
    /// this exists to name. A driver who takes two badges in one race had
    /// the first one silently deleted from the queue the moment the second
    /// arrived, because both carried the same driver and the same event
    /// type. One race unlocked Hat-trick and Record Holder for the same
    /// driver and a third badge for another; the other driver's was called
    /// and Hat-trick simply never existed. Nothing logged it, because from
    /// the queue's point of view nothing went wrong.
    /// </summary>
    private static bool SupersedesPerDriver(AnnouncementEventType type) =>
        type is not AnnouncementEventType.AchievementUnlocked;

    private void EnqueueVoiceWork(AnnouncementEventType type, string? transponder, Func<Task> work)
    {
        // Checked before the priority rules, not after: a track record or a
        // personal best set on a lap completed after the flag is still a
        // line about a race that is already over, and "priority" only ever
        // meant "don't drop this behind lap chatter".
        if (_postRaceQuiet && !IsAllowedAfterRaceOver(type))
        {
            StatusMessage?.Invoke($"[voice] Dropped {type} - the race is over; only the wrap-up speaks now.");
            return;
        }

        var isPriority = IsPriorityAnnouncement(type);

        if (_isVoiceSpeaking && !isPriority && !IsWorthWaitingFor(type))
        {
            StatusMessage?.Invoke($"[voice] Dropped {type} announcement - still speaking the previous one.");
            return;
        }

        if (IsInterruptingAnnouncement(type))
        {
            SpeakInterrupting(type, work);
            return;
        }

        lock (_voiceQueueLock)
        {
            if (transponder is not null && SupersedesPerDriver(type))
            {
                _voiceQueue.RemoveAll(item => item.Type == type && item.Transponder == transponder);
            }

            if (isPriority)
            {
                // Drop any queued-but-not-yet-started routine announcements.
                // Once a flag is out, lap chatter from a moment ago is stale
                // and would only delay the message that actually matters.
                //
                // "Last lap", "one minute to go" and a track record are kept:
                // they are race moments rather than chatter, and a release
                // call or a reminder arriving behind them used to bin them
                // anyway - the very thing IsWorthWaitingFor exists to stop.
                // A real flag still clears them (ClearTheDecksFor), since a
                // yellow or a finish does make them stale.
                var discarded = _voiceQueue.RemoveAll(item =>
                    !IsPriorityAnnouncement(item.Type) && !IsWorthWaitingFor(item.Type));
                if (discarded > 0)
                {
                    StatusMessage?.Invoke($"[voice] Cleared {Plural.Of(discarded, "queued announcement")} to make way for {type}.");
                }
            }

            _voiceQueue.Add(new QueuedVoiceItem(type, transponder, async () =>
            {
                try
                {
                    // "Quota available again" is no longer worked out here. It
                    // is said where ElevenLabs actually answers - see
                    // NoteElevenLabsAnswered - which is still only a request
                    // ElevenLabs answered (a saved line proves nothing about
                    // the quota), but now includes the flag calls and the
                    // lines rendered in advance that never came through here.
                    await work();
                }
                catch (Exception ex) when (IsQuotaExceeded(ex))
                {
                    // Only reached with the stand-in off (or no Windows voice
                    // to stand in with) - with it on, the Windows voice has
                    // already said this line. The first refusal says the
                    // quota is gone; the rest say this line was skipped.
                    if (!NoteQuotaExceeded(standingIn: false))
                    {
                        StatusMessage?.Invoke("[voice] Still over quota - skipping this announcement.");
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage?.Invoke($"[voice] Playback failed: {ex.Message}");
                }
            }));
        }

        _voiceQueueSignal.Release();
    }

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

                    var audio = await SpeakThroughRelayAsync(client, line.Profile, line.Text, cancel);
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

        AnnouncementTextReady?.Invoke(prepared.Text);
        var generation = ClearTheDecksFor(type);

        // The finish call holds the voice queue until it has spoken - taken
        // here, synchronously, so NotifyRaceCompleted can see it was.
        var finishHold = ClaimFinishCallHold(type);

        var volume = Math.Clamp(profile.Volume * (VoiceBoostProvider?.Invoke() ?? 1f), 0.0, 1.0);

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

    /// <summary>
    /// Speaks a flag call at once: the line being spoken is cut off, the
    /// queued chatter behind it is binned, and synthesis starts immediately
    /// rather than waiting for a turn in the queue.
    ///
    /// Deliberately outside the voice queue. Putting it at the front of the
    /// queue would still leave it waiting on the line in progress, and the
    /// whole point is that a yellow can't wait.
    /// </summary>
    private void SpeakInterrupting(AnnouncementEventType type, Func<Task> work)
    {
        // Everything routine goes; a race-control line already queued stays.
        var generation = ClearTheDecksFor(type);

        // The finish call holds the voice queue until it has spoken - taken
        // here, synchronously, so NotifyRaceCompleted can see it was.
        var finishHold = ClaimFinishCallHold(type);

        _ = Task.Run(async () =>
        {
            // The flag's line carries the generation it just started, so it
            // passes its own interrupt check but not a LATER flag's. Without
            // that, a flag whose line was still synthesising could get the
            // speaker after a newer flag that played from memory - "time's
            // up" after the finish call, or "yellow" after the green that
            // lifted it. Set here, inside the task, so it is scoped to this
            // line alone and replaces anything copied from the caller.
            _dequeuedUnderGeneration.Value = generation;

            // Tells SynthesizeAndPlayAsync which call this is, so it waits
            // for the call's own sound AFTER synthesising rather than before
            // - the words are rendered while the sound plays.
            _leadInFor.Value = type;

            Interlocked.Increment(ref _voiceInFlight);
            BeginSpeaking();

            try
            {
                await work();
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke($"[voice] {type} failed: {ex.Message}");
            }
            finally
            {
                Interlocked.Decrement(ref _voiceInFlight);
                EndSpeaking();

                // However the call ended - spoken, dropped, failed - the
                // queue it was holding carries on.
                finishHold?.TrySetResult();
            }
        });
    }

    /// <summary>
    /// ElevenLabsClient surfaces API errors as a generic InvalidOperationException
    /// with the raw response body embedded in the message, so quota errors are
    /// detected by looking for ElevenLabs' own "quota_exceeded" error code in
    /// that text rather than a dedicated exception type.
    /// </summary>
    private static bool IsQuotaExceeded(Exception ex) =>
        ex.Message.Contains("quota_exceeded", StringComparison.OrdinalIgnoreCase);

    /// <summary>True for the lap-by-lap chimes that may be dropped when late; false for the race's one-off sounds.</summary>
    private static bool IsPerLapSound(SoundEffectKind kind) => SoundEffectsProfile.PerLapKinds.Contains(kind);

    /// <summary>True when a lap chime has waited too long to still mean anything.</summary>
    private static bool IsStaleSound(SoundEffectKind kind, DateTime queuedAtUtc) =>
        IsPerLapSound(kind) && DateTime.UtcNow - queuedAtUtc > PerLapSoundMaxAge;

    private void EnqueueSoundWork(SoundEffectKind kind, TaskCompletionSource? finished, Func<Task> work)
    {
        var dropped = 0;

        lock (_soundQueueLock)
        {
            if (IsPerLapSound(kind))
            {
                // Make room by dropping the OLDEST waiting lap chimes, never
                // a start/finish sound.
                while (_soundQueue.Count(item => IsPerLapSound(item.Kind)) >= MaxQueuedPerLapSounds)
                {
                    var oldest = _soundQueue.FindIndex(item => IsPerLapSound(item.Kind));
                    _soundQueue[oldest].Finished?.TrySetResult();
                    _soundQueue.RemoveAt(oldest);
                    dropped++;
                }
            }

            _soundQueue.Add(new QueuedSound(kind, DateTime.UtcNow, async () =>
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

        _soundQueueSignal.Release();
    }

    /// <summary>Independent voice playback loop - see the class-level remarks on why this is separate from sound effects.</summary>
    private async Task RunVoiceQueueLoopAsync()
    {
        while (true)
        {
            await _voiceQueueSignal.WaitAsync();

            // Nothing from the queue speaks until the finish call has - the
            // badges worked out at the flag used to beat it to the speaker.
            // Waited for BEFORE taking the next line, so what is taken is
            // whatever is at the front once the call is done. Always ends:
            // see FinishCallHoldMaxWait.
            await WaitForFinishCallAsync();

            Func<Task>? work;
            int? generation;
            lock (_voiceQueueLock)
            {
                if (_voiceQueue.Count > 0)
                {
                    var item = _voiceQueue[0];
                    work = item.Work;
                    _voiceQueue.RemoveAt(0);

                    // Captured under the queue lock, which ClearTheDecksFor's
                    // queue purge also takes: a routine line either gets the
                    // pre-flag generation (and is dropped if still
                    // synthesising when the flag lands) or was never here.
                    // Priority lines are exempt, matching the purge.
                    generation = IsPriorityAnnouncement(item.Type)
                        ? null
                        : Volatile.Read(ref _interruptGeneration);
                }
                else
                {
                    work = null;
                    generation = null;
                }
            }

            // Flows into work() and everything it awaits; reset every
            // iteration so nothing leaks from one line to the next.
            _dequeuedUnderGeneration.Value = generation;

            // Mute can land between an item being queued and it being
            // played, so check again here rather than trusting the
            // enqueue-time guard alone.
            if (Muted)
            {
                continue;
            }

            if (work is null)
            {
                continue; // shouldn't happen, but never let a spurious signal kill the loop
            }

            _isVoiceSpeaking = true;
            Interlocked.Increment(ref _voiceInFlight);

            // Raised around the ACTUAL playback, so anything ducking under
            // the voice covers the real speaking window rather than the
            // moment a line was queued - those can be seconds apart when
            // the queue is busy.
            BeginSpeaking();

            try
            {
                await work();
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke($"[voice queue] Unexpected playback error: {ex.Message}");
            }
            finally
            {
                Interlocked.Decrement(ref _voiceInFlight);
                _isVoiceSpeaking = false;
                EndSpeaking();
            }
        }
    }

    /// <summary>Independent sound-effect playback loop - see the class-level remarks on why this is separate from voice.</summary>
    private async Task RunSoundQueueLoopAsync()
    {
        while (true)
        {
            await _soundQueueSignal.WaitAsync();

            QueuedSound? item = null;
            lock (_soundQueueLock)
            {
                if (_soundQueue.Count > 0)
                {
                    item = _soundQueue[0];
                    _soundQueue.RemoveAt(0);
                }
            }

            if (item is null)
            {
                continue;
            }

            // Mute can land between an item being queued and it being
            // played, so check again here rather than trusting the
            // enqueue-time guard alone. Signalled as finished, so a call
            // waiting on this sound isn't left to wait out its timeout.
            if (Muted)
            {
                item.Finished?.TrySetResult();
                continue;
            }

            // A lap chime that has been waiting behind the voice for more
            // than a few seconds belongs to a lap that is long gone.
            if (IsStaleSound(item.Kind, item.QueuedAtUtc))
            {
                StatusMessage?.Invoke($"[sound] Dropped a late {item.Kind} sound - it waited too long to still match its lap.");
                item.Finished?.TrySetResult();
                continue;
            }

            var work = item.Work;

            Interlocked.Increment(ref _soundInFlight);
            try
            {
                await work();
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
    }

    /// <summary>
    /// Plays raw MP3 bytes (as returned by ElevenLabs) and waits for playback
    /// to finish, including a brief tail grace period.
    ///
    /// Streaming compressed audio straight into WaveOutEvent (the original
    /// approach here) could clip a fraction of a second off the start and
    /// end of every announcement. The real cause, confirmed by testing:
    /// WaveOutEvent defaults to ~300ms of internal device buffering before
    /// it's fully warmed up, so the very start of playback can be lost to
    /// startup latency before real audio ever reaches the speaker, and
    /// PlaybackStopped can fire slightly before the last buffer has
    /// actually finished draining. Fully decoding to PCM up front (rather
    /// than decoding in real time during playback), explicitly shrinking
    /// that device latency, and padding both ends with a touch of silence
    /// together remove the timing dependency that caused it.
    /// </summary>
    /// <summary>
    /// The line currently playing, so a flag call can stop it.
    ///
    /// Static alongside PlayAsync itself, which every path here goes
    /// through. Cancelling takes the line out of the shared mix (see
    /// AudioBus) and completes the awaiting task exactly as a natural end
    /// would.
    /// </summary>
    private static CancellationTokenSource? _activeLine;
    private static readonly object ActiveOutputLock = new();

    /// <summary>Stops whatever is playing. Returns true if there was something to stop.</summary>
    public static bool StopCurrentPlayback()
    {
        CancellationTokenSource? line;

        lock (ActiveOutputLock)
        {
            line = _activeLine;
        }

        if (line is null)
        {
            return false;
        }

        try
        {
            line.Cancel();
            return true;
        }
        catch
        {
            // Already finished and disposed - nothing to cut off.
            return false;
        }
    }

    /// <summary>
    /// The silence wrapped around every line. The LEADING pad is the fix for
    /// the device-startup clipping bug and is not safe to shorten. The
    /// TRAILING pad only stops the tail being cut when the device stops, so
    /// a caller that knows another line follows immediately can ask for less
    /// of it - 0.9s of trailing silence plus 0.9s of leading silence is
    /// nearly two seconds of dead air between two lines meant to run
    /// back to back.
    /// </summary>
    public const double DefaultLeadingSilenceSeconds = 0.9;
    public const double DefaultTrailingSilenceSeconds = 0.9;

    /// <summary>
    /// The leading pad for an output that doesn't sleep - a wired speaker,
    /// or audio going into OBS.
    ///
    /// The 0.9s above is a WAKE-UP allowance: a Bluetooth speaker powers its
    /// amplifier down between sounds and swallows the first fraction of a
    /// second of whatever plays next. An output that stays awake needs only
    /// enough to clear the device's own startup. This comes off the front of
    /// EVERY line, so on a wired setup it is three quarters of a second back
    /// on every single announcement.
    /// </summary>
    public const double WiredLeadingSilenceSeconds = 0.15;

    /// <summary>
    /// The leading pad the given output needs.
    ///
    /// The short one either way now. Every line plays through the shared
    /// output (see AudioBus), which never stops between sounds, so a
    /// wireless speaker is never asleep when a line arrives and there is
    /// nothing for the long pad to cover - it only put three quarters of a
    /// second of silence in front of every announcement.
    /// </summary>
    public static double LeadingSilenceFor(bool wirelessSpeaker) => WiredLeadingSilenceSeconds;

    /// <summary>The leading pad for whatever the saved profile says is plugged in.</summary>
    private double CurrentLeadingSilence => LeadingSilenceFor(_voiceStore.Current.WirelessSpeaker);

    /// <summary>Enough to stop the tail clipping, short enough not to be heard as a pause.</summary>
    public const double BackToBackTrailingSilenceSeconds = 0.25;

    /// <summary>
    /// For a long line - the race recap especially - where the last words
    /// were being clipped. A long line is a long single buffer chain, and
    /// the device has more of it still queued when the reader runs dry, so
    /// it needs more slack behind it than a one-sentence call does.
    /// </summary>
    public const double LongLineTrailingSilenceSeconds = 1.6;

    /// <summary>How much tail silence a line gets.</summary>
    public enum SpeechTail
    {
        /// <summary>The standard 0.9s.</summary>
        Normal,

        /// <summary>Trimmed, because another line follows immediately.</summary>
        BackToBack,

        /// <summary>Extended, for a long line whose ending was being clipped.</summary>
        Long,
    }

    private static double TailSecondsFor(SpeechTail tail) => tail switch
    {
        SpeechTail.BackToBack => BackToBackTrailingSilenceSeconds,
        SpeechTail.Long => LongLineTrailingSilenceSeconds,
        _ => DefaultTrailingSilenceSeconds,
    };

    /// <summary>
    /// How much of the decoded start is thrown away, and therefore how far
    /// the decoded audio runs AHEAD of any timings the API reported for it.
    /// Anything cutting that audio by time has to allow for this.
    /// </summary>
    public const double DecodeSkipSeconds = 0.08;

    /// <summary>
    /// Decodes an MP3 to raw PCM.
    ///
    /// Split out of PlayAsync so one render covering several lines can be
    /// decoded once and cut into pieces, each played on its own.
    /// </summary>
    public static (byte[] Pcm, WaveFormat Format) DecodeMp3(byte[] mp3Bytes)
    {
        using var mp3Stream = new MemoryStream(mp3Bytes);
        using var mp3Reader = new Mp3FileReader(mp3Stream);
        var format = mp3Reader.WaveFormat;

        // Many MP3 encoders prepend a LAME/Xing "header frame" that isn't
        // real audio - it's encoder metadata shaped like an MP3 frame for
        // compatibility. Decoded naively, it can produce a burst of static
        // right at the start. Increasing the leading silence padding made
        // this MORE audible rather than less, because padding alone doesn't
        // remove it - it was previously being hidden by coincidence, cut off
        // by the device-startup clipping bug this whole feature was built to
        // fix. Discarding a small slice of decoded audio here - not just
        // padding around it - actually removes it from the stream.
        var skipBuffer = new byte[(int)(format.AverageBytesPerSecond * DecodeSkipSeconds)];
        mp3Reader.Read(skipBuffer, 0, skipBuffer.Length);

        using var pcm = new MemoryStream();
        var buffer = new byte[format.AverageBytesPerSecond];
        int bytesRead;

        while ((bytesRead = mp3Reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            pcm.Write(buffer, 0, bytesRead);
        }

        return (pcm.ToArray(), format);
    }

    /// <summary>
    /// Decodes whatever a synthesis handed back.
    ///
    /// ElevenLabs sends MP3, but the saved-voice library can also hand back
    /// WAV: a release call cut out of a batched render is saved as the PCM
    /// it was cut to (see TryRenderReleaseBatchAsync), already trimmed, so
    /// nothing is skipped from its start the way an MP3's is.
    /// </summary>
    public static (byte[] Pcm, WaveFormat Format) DecodeAudio(byte[] audio)
    {
        if (VoiceLibrary.SniffAudio(audio) != VoiceLibrary.SavedAudioKind.Wav)
        {
            return DecodeMp3(audio);
        }

        using var stream = new MemoryStream(audio);
        using var reader = new WaveFileReader(stream);
        var format = reader.WaveFormat;

        using var pcm = new MemoryStream();
        var buffer = new byte[Math.Max(4096, format.AverageBytesPerSecond)];
        int bytesRead;

        while ((bytesRead = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            pcm.Write(buffer, 0, bytesRead);
        }

        return (pcm.ToArray(), format);
    }

    /// <summary>Wraps decoded PCM as a WAV file, for the saved-voice library.</summary>
    private static byte[] EncodeWav(byte[] pcm, WaveFormat format)
    {
        // ToArray works after the writer has closed the stream, and closing
        // it is what makes the writer fill in the header's lengths.
        var memory = new MemoryStream();

        using (var writer = new WaveFileWriter(memory, format))
        {
            writer.Write(pcm, 0, pcm.Length);
        }

        return memory.ToArray();
    }

    /// <summary>
    /// Plays audio under the same output lock the announcer's own lines use,
    /// so a Voice Settings test during a session waits its turn instead of
    /// starting a second device on top of a live call (see _audioPlaybackLock).
    /// </summary>
    public async Task PlayExclusiveAsync(byte[] mp3Bytes, double volume)
    {
        await _audioPlaybackLock.WaitAsync();
        try
        {
            await PlayAsync(mp3Bytes, volume);
        }
        finally
        {
            _audioPlaybackLock.Release();
        }
    }

    public static Task PlayAsync(
        byte[] mp3Bytes,
        double volume,
        double trailingSilenceSeconds = DefaultTrailingSilenceSeconds,
        double leadingSilenceSeconds = DefaultLeadingSilenceSeconds)
    {
        var (pcm, format) = DecodeAudio(mp3Bytes);
        return PlayPcmAsync(pcm, format, volume, trailingSilenceSeconds, leadingSilenceSeconds);
    }

    /// <summary>Plays already-decoded audio, padded the way every line is.</summary>
    public static async Task PlayPcmAsync(
        byte[] pcmBytes,
        WaveFormat format,
        double volume,
        double trailingSilenceSeconds = DefaultTrailingSilenceSeconds,
        double leadingSilenceSeconds = DefaultLeadingSilenceSeconds)
    {
        byte[] paddedPcm;

        {
            // Whole sample frames only. A pad that isn't a multiple of the
            // frame size (2 bytes for 16-bit mono) shifts every sample after
            // it by one byte, and the line plays as loud static. 16-bit mono
            // at 22.05 kHz is 44100 bytes a second, so the wired 0.15 s lead
            // (6615 bytes) and the 0.25 s back-to-back tail (11025) were both odd.
            var block = Math.Max(1, format.BlockAlign);
            var leadingBytes = new byte[(int)AlignDown((long)(format.AverageBytesPerSecond
                * Math.Clamp(leadingSilenceSeconds, 0.0, DefaultLeadingSilenceSeconds)), block)];
            var trailingBytes = new byte[(int)AlignDown((long)(format.AverageBytesPerSecond
                * Math.Clamp(trailingSilenceSeconds, 0.0, LongLineTrailingSilenceSeconds)), block)];

            using var pcm = new MemoryStream();
            pcm.Write(leadingBytes, 0, leadingBytes.Length);
            pcm.Write(pcmBytes, 0, pcmBytes.Length);
            pcm.Write(trailingBytes, 0, trailingBytes.Length);
            paddedPcm = pcm.ToArray();
        }

        using var pcmStream = new MemoryStream(paddedPcm);
        using var rawReader = new RawSourceWaveStream(pcmStream, format);

        // Through the shared output (see AudioBus) rather than one opened
        // for this line: opening an output per line stalled the music for a
        // moment every time the announcer spoke. Still throws when there is
        // no usable output device, which the voice queue already reports
        // and carries on from - a dead announcer is the outcome to avoid.
        using var stop = new CancellationTokenSource();

        lock (ActiveOutputLock)
        {
            _activeLine = stop;
        }

        try
        {
            await AudioBus.PlayAsync(rawReader.ToSampleProvider(), (float)Math.Clamp(volume, 0.0, 1.0), stop.Token);
        }
        finally
        {
            lock (ActiveOutputLock)
            {
                if (ReferenceEquals(_activeLine, stop))
                {
                    _activeLine = null;
                }
            }
        }

        // Grace period before the objects above get disposed, so the tail
        // end of the last buffer has time to actually finish playing
        // through the audio hardware rather than being cut off.
        //
        // The bus reports a line done when the last of it has been MIXED,
        // which is still a buffer or so ahead of what is being heard.
        await Task.Delay(350);
    }
}
