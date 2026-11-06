using NAudio.Wave;

namespace HeatCaller;

/// <summary>
/// Turns race events into audio: spoken announcements via ElevenLabs TTS,
/// the HeatCaller relay or the free Kokoro voice on this PC (see
/// RenderLineAsync for which speaks when),
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
public sealed partial class AnnouncementService
{
    private readonly VoiceSettingsStore _voiceStore;
    private readonly ProfileStore _profiles;
    private readonly SoundEffectsStore _soundStore;
    private readonly RivalryStore? _rivalries;
    private readonly ElevenLabsClient _elevenLabs = new();

    /// <param name="OnAir">
    /// What to call this line on the race director's strip while it is
    /// being spoken - "Race Recap", "Tonight's Racing" - or null to be
    /// named after the KIND of call instead.
    ///
    /// CARRIED WITH THE LINE RATHER THAN SET WHEN IT IS ASKED FOR (14 Oct
    /// 2026), which is the only arrangement that can be right: speaking is
    /// a queue, and the gap between a segment deciding to speak and the
    /// speakers actually saying it is seconds on a busy night. A label set
    /// at the asking would have named whatever was queued, not what the
    /// room is hearing - and on a night with a flag call cutting in, those
    /// are different lines.
    /// </param>
    private sealed record QueuedVoiceItem(AnnouncementEventType Type, string? Transponder, Func<Task> Work, string? OnAir = null, BattleLine? Battle = null);

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

    /// <summary>The Voice tab's Race calls switch for a race or for practice (VoiceProfile.RaceCallsInRaces / RaceCallsInPractice).</summary>
    private bool RaceCallsOn(bool practice) =>
        practice ? _voiceStore.Current.RaceCallsInPractice : _voiceStore.Current.RaceCallsInRaces;

    /// <summary>
    /// Whether a race call is on now: its own row ticked for a race or for
    /// practice, AND the Voice tab's Race calls switch on for that. Every
    /// race call asks this rather than the row alone - the switch is a gate
    /// over the rows, so the rows keep their ticks while it is off.
    /// </summary>
    private bool CallOn(AnnouncementEventConfig? config) =>
        config is not null && config.IsEnabledFor(IsPracticeMode) && RaceCallsOn(IsPracticeMode);
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

    /// <summary>Pairs already called as a close battle this race - a scrap lasts many laps and shouldn't be announced on every one of them. Only touched under _battlesLock.</summary>
    private readonly HashSet<string> _battlesAnnounced = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Pairs offered as a close battle and not yet settled one way or the
    /// other, with when they were offered. Only touched under _battlesLock.
    ///
    /// Without this a pair waiting in the per-lap batch was offered again on
    /// every standings update, and each copy went into the front car's batch
    /// at weight 65 - crowding out that car's own lap time and lap call
    /// under the per-lap limit. A pair leaves here when its line is settled
    /// (said, or dropped by EnqueueVoiceWork), at a reset, or once it has
    /// been waiting longer than BattleOfferExpiresAfter - which is how a
    /// line the per-lap cap dropped, or a flag cleared out of the batch,
    /// gets offered again rather than being stuck as pending all race.
    /// </summary>
    private readonly Dictionary<string, DateTime> _battlesPending = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Pairs whose battle line the pacing dial HELD (not dropped), with
    /// when they may be offered again. Only touched under _battlesLock.
    ///
    /// WHY (22 Oct 2026, known issues): a held line used to give the pair
    /// straight back, as a dropped one does, so the very next standings
    /// update offered the same battle again - inside the same gap - and it
    /// was held again, crossing after crossing. Nothing wrong was said, but
    /// the "held" count and the lost-lines window filled with copies of one
    /// call. A held pair now waits out the gap before it is offered again;
    /// a dropped one is still free at once.
    /// </summary>
    private readonly Dictionary<string, DateTime> _battlesCooling = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Guards _battlesAnnounced, _battlesPending, _battlesCooling and _battlesGeneration.
    ///
    /// Needed because the pairs are checked on the decoder thread (from
    /// CheckCloseBattles), settled from inside the per-lap action - which
    /// runs on thread-pool threads, and for different cars at the same
    /// time - and cleared by ResetSession. Nothing else is ever taken while
    /// holding it, so it can't be part of a deadlock.
    /// </summary>
    private readonly object _battlesLock = new();

    /// <summary>
    /// Bumped by every ResetSession. A close-battle line captures it when it
    /// is offered and checks it when its batch runs, so a line from the race
    /// just reset - its batch already past its delay - can't mark the same
    /// pair as called in the next race.
    /// </summary>
    private int _battlesGeneration;

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
    /// Stand-in reasons already logged, so each is said once rather than on
    /// every line. Cleared whenever ElevenLabs answers, so the same failure
    /// coming back later is news again.
    /// </summary>
    private readonly HashSet<string> _standInLogged = new(StringComparer.Ordinal);

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
    /// Whether ghost cars are called like real drivers (the Ghost Cars
    /// page's "Announce ghosts like real drivers"). Set by MainForm; none
    /// means yes. Off, nothing is said or played FOR a ghost - its laps,
    /// streaks, a lead it takes - while its standing is still tracked, so
    /// the real drivers' positions and gaps stay right (MutedGhost).
    /// </summary>
    public Func<bool>? AnnounceGhostsProvider { get; set; }

    /// <summary>
    /// How chatty the club has asked the announcer to be - the key of one
    /// of ShowChatter.All, or null/blank for Standard.
    ///
    /// ASKED ON DEMAND, like practice and qualifying beside it, so turning
    /// the dial mid-race takes effect at the next crossing rather than the
    /// next race. That is what the commentary segments have always done and
    /// what a club reasonably expects of a control that calls itself
    /// chattiness.
    ///
    /// WHY THIS CLASS CARES AT ALL (14 Oct 2026). It did not, and that was
    /// the fault: the dial governed the written segments and left every
    /// per-event call off the timing loop untouched, so a club that turned
    /// it to Sparing mid-race heard no difference at all in the thing that
    /// was actually talking. See ShowChatter, which has the report it came
    /// from. Null leaves every call and the club's own per-lap cap exactly
    /// as they were, which is what a host that never sets this gets.
    /// </summary>
    public Func<string?>? ChatterProvider { get; set; }

    /// <summary>The dial's current setting, or blank for Standard - see <see cref="ChatterProvider"/>.</summary>
    private string? Chatter => ChatterProvider?.Invoke();

    /// <summary>
    /// The club's per-lap announcement cap with the dial applied - see
    /// ShowChatter.ScaledPerLap.
    ///
    /// One property rather than the expression written out at each of the
    /// seven places that offer a per-lap candidate: they were already seven
    /// copies of Math.Max(1, ...) before this, and seven copies of a
    /// SCALED cap would be seven chances for one of them to be the stop
    /// that behaves differently.
    /// </summary>
    private int MaxVoicePerLap =>
        ShowChatter.ScaledPerLap(_voiceStore.Current.MaxAnnouncementsPerLap, Chatter);

    /// <summary>True for a ghost car's transponder while ghosts are not to be announced - see AnnounceGhostsProvider.</summary>
    private bool MutedGhost(string? transponder) =>
        transponder is not null && GhostService.IsGhost(transponder) && AnnounceGhostsProvider?.Invoke() == false;

    /// <summary>
    /// Whether the current session is a qualifier - so provisional-pole and
    /// qualifying-improvement lines fire, and only then. Same callback shape
    /// as PracticeModeProvider, for the same decoupling reason.
    /// </summary>
    public Func<bool>? QualifyingModeProvider { get; set; }

    /// <summary>Who currently holds provisional pole, so the call only fires when the top of the order actually changes hands.</summary>
    private string? _provisionalPoleTransponder;

    /// <summary>
    /// How the class now qualifying is scored, so provisional pole goes to
    /// whoever tops the order the board and the qualifying projection show.
    /// It used to go to the quickest single lap, which no class scores by:
    /// under the default most-laps rule a driver with nine laps and one
    /// lucky tour was called "P1 in qualifying" while the board had them
    /// fifth, and the real leader was never called at all. Set by Race
    /// Control at each green (SetQualifyingRule); until then, the class
    /// default.
    /// </summary>
    private QualifyingMethod _qualifyingMethod = QualifyingMethod.TotalLaps;

    /// <summary>How many laps make a run under BestConsecutiveLaps - the class's ConsecutiveLapCount.</summary>
    private int _qualifyingConsecutiveLaps = 3;

    /// <summary>
    /// Each driver's flying laps this session, in order - the same list the
    /// qualifying calculator scores a best-consecutive run from. Kept from
    /// the session's own lap times on the gate pass; only read under
    /// BestConsecutiveLaps.
    /// </summary>
    private readonly Dictionary<string, List<long>> _qualifyingLaps = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Tells the announcer how this race's class scores qualifying - see _qualifyingMethod.</summary>
    public void SetQualifyingRule(QualifyingMethod method, int consecutiveLaps)
    {
        _qualifyingMethod = method;
        _qualifyingConsecutiveLaps = Math.Max(1, consecutiveLaps);
    }

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
    /// must not be dropped this way: the lines a flag spares (a race-control
    /// line queued earlier stays, as ClearTheDecksFor keeps it - see
    /// SurvivesAFlag) and anything spoken outside the voice queue.
    ///
    /// Not every PRIORITY line, which is what it used to be and what made
    /// the two guards below dead code for a Reminder: scheduled speech is
    /// priority so that chatter cannot drop it, which is no reason for a
    /// stale recap to speak over a flag.
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
    /// Lines a caller has asked a model to write and will speak when the
    /// answer comes back - the AI round intro at staging. Counted as pending
    /// audio from the moment the request goes out, so the staging chain
    /// waits for the intro instead of seeing silence while the model thinks
    /// and starting the countdown on top of it (rehearsal 1: the intro
    /// arrived mid-countdown and pushed the countdown sounds back behind
    /// itself, so every race heard the countdown twice).
    ///
    /// Changed only under _linesBeingWrittenLock; read without it.
    /// </summary>
    private int _linesBeingWritten;

    private readonly object _linesBeingWrittenLock = new();

    /// <summary>
    /// Bumped every time a countdown starts (CutForCountdown). The countdown
    /// cuts off everything still talking, and this is how a line that was
    /// already taken off the queue - still synthesising, or waiting for the
    /// speaker - finds out it has been overtaken. It is the same idea as
    /// _interruptGeneration, kept separate because a flag spares
    /// race-control lines and the countdown spares nothing.
    ///
    /// Changed under _voiceQueueLock AND _linesBeingWrittenLock, so a line
    /// dequeued, or a line-being-written started, reads a value that is
    /// either before the cut or after it.
    /// </summary>
    private int _countdownCutGeneration;

    /// <summary>The countdown generation a voice-queue line was dequeued under. See _countdownCutGeneration.</summary>
    private readonly AsyncLocal<int?> _dequeuedUnderCountdown = new();

    /// <summary>True when the line being produced was dequeued before a countdown started.</summary>
    private bool IsCutByCountdown() =>
        _dequeuedUnderCountdown.Value is int generation && generation != Volatile.Read(ref _countdownCutGeneration);

    /// <summary>
    /// Marks a line as being written - see _linesBeingWritten. Dispose the
    /// result once the line has been handed to the announcer (or given up
    /// on); disposing twice is harmless.
    ///
    /// Never holds audio pending for longer than <paramref name="maxHold"/>,
    /// whatever happens to the caller: a request whose answer never arrives
    /// must not leave every later start waiting out its full cap. A countdown
    /// starting releases it at once as well (CutForCountdown) - the line it
    /// stands for would only be dropped when it arrived.
    /// </summary>
    public IDisposable BeginLineBeingWritten(TimeSpan maxHold)
    {
        LineBeingWritten token;

        lock (_linesBeingWrittenLock)
        {
            _linesBeingWritten++;
            token = new LineBeingWritten(this, _countdownCutGeneration);
        }

        _ = Task.Delay(maxHold).ContinueWith(_ => token.Dispose(), TaskScheduler.Default);
        return token;
    }

    private void EndLineBeingWritten(int countdownGeneration)
    {
        lock (_linesBeingWrittenLock)
        {
            // A countdown since it began has already released it - see
            // CutForCountdown, which zeroes the count.
            if (countdownGeneration == _countdownCutGeneration && _linesBeingWritten > 0)
            {
                _linesBeingWritten--;
            }
        }
    }

    private sealed class LineBeingWritten : IDisposable
    {
        private readonly AnnouncementService _owner;
        private readonly int _countdownGeneration;
        private int _ended;

        public LineBeingWritten(AnnouncementService owner, int countdownGeneration)
        {
            _owner = owner;
            _countdownGeneration = countdownGeneration;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _ended, 1) == 0)
            {
                _owner.EndLineBeingWritten(_countdownGeneration);
            }
        }
    }

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
                || Volatile.Read(ref _deferredSoundsPending) > 0
                || Volatile.Read(ref _linesBeingWritten) > 0)
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
    /// <returns>
    /// True when the speakers actually went quiet, false when the wait
    /// ran out with something still playing. Most callers are waiting
    /// out a cue before a flag and have nothing useful to do with the
    /// difference, so they discard it; the recap sequence does not -
    /// see RaceControlControl.HeardOutAsync, which reports a recap
    /// still speaking past its limit rather than letting the rest of
    /// the programme queue up behind it unexplained.
    /// </returns>
    public async Task<bool> WaitForAudioAsync(TimeSpan timeout, CancellationToken token = default)
    {
        var started = DateTime.UtcNow;
        var deadline = started + timeout;

        // Instrumented deliberately. Three attempts at this timing were made
        // by reasoning about the code; each was wrong. These lines report
        // what actually happened so the next change is based on evidence.
        StatusMessage?.Invoke(
            $"[audio-wait] enter: pending={IsAudioPending} voice={Volatile.Read(ref _voiceInFlight)} "
            + $"sound={Volatile.Read(ref _soundInFlight)} deferred={Volatile.Read(ref _deferredSoundsPending)} "
            + $"writing={Volatile.Read(ref _linesBeingWritten)} "
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

        var timedOut = DateTime.UtcNow >= deadline;

        StatusMessage?.Invoke(
            $"[audio-wait] exit after {(DateTime.UtcNow - started).TotalMilliseconds:0}ms, "
            + $"sawAudio={sawAudio}, timedOut={timedOut}");

        return !timedOut;
    }

    /// <summary>Fired with the clean, resolved spoken text every time a voice announcement is about to be queued - for displays (like Big Screen mode) that want to show "what was just said" without parsing debug log lines.</summary>
    public event Action<string>? AnnouncementTextReady;

    /// <summary>
    /// Raises AnnouncementTextReady with the line as a screen should show
    /// it. Every line this service builds for display goes through here
    /// rather than raising the event directly, for two reasons.
    ///
    /// The [audio tags] come out first. Nearly every built-in phrasing opens
    /// with one ("[shouting] {driver} sets the fastest lap of the race!"),
    /// and they are directions for the voice, not words - but the Big Screen
    /// and the Announcer page's feed put the text up exactly as given, so the
    /// crowd was reading "[shouting]" in front of almost every call. They are
    /// taken out here, once, rather than in each subscriber, so every display
    /// gets clean text whichever voice model is chosen. The spoken copy is
    /// not touched: it keeps its tags for a model that can perform them.
    ///
    /// And a line about a ghost while ghosts are not announced is not shown
    /// at all. EnqueueVoiceWork already refuses to SAY it (MutedGhost), but
    /// the text was raised before the line ever reached the queue, so the
    /// screens still showed a ghost's personal best or badge that nobody
    /// heard. Pass the transponder the line is about, or null for a line
    /// that is about no one driver.
    /// </summary>
    private void RaiseAnnouncementText(string text, string? transponder = null)
    {
        if (MutedGhost(transponder))
        {
            return;
        }

        var clean = VoiceProfile.StripAudioTags(text);

        // A line that was nothing but tags has nothing to show.
        if (string.IsNullOrWhiteSpace(clean))
        {
            return;
        }

        AnnouncementTextReady?.Invoke(clean);
    }

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

    /// <summary>
    /// What the line that is speaking called itself, or null for one that
    /// is named after its kind instead. See <see cref="OnAirNow"/>.
    /// </summary>
    private string? _onAirNamed;

    /// <summary>And which kind of call it is - see <see cref="OnAirNow"/>.</summary>
    private AnnouncementEventType? _onAirCall;

    /// <summary>
    /// Whether anything is being said this moment - the state SpeakingChanged
    /// reports the edges of, for something that starts mid-line (an imaging
    /// bed) and so missed the edge (22 Oct 2026, bug hunt). OnAirNow can be
    /// null while talking when the line has no name.
    /// </summary>
    internal bool IsTalking
    {
        get
        {
            lock (_speakingLock)
            {
                return _speakingCount > 0;
            }
        }
    }

    /// <summary>
    /// What is going out of the speakers RIGHT NOW, in words a race
    /// director can read in the second they have for it - "Race Recap",
    /// "Yellow flag", "Lap time". Null when nothing is being spoken.
    ///
    /// ASKED FOR FROM THE FLOOR (14 Oct 2026). The announcer strip could
    /// say that the PA was busy and what the announcer had LAST said, and
    /// those are not the same question: a race director with a microphone
    /// in their hand wants to know whether the thing they can hear is a
    /// four-minute recap or a two-second lap call, because one of those is
    /// worth waiting out and the other is not.
    ///
    /// THE OUTERMOST LINE'S NAME, not the newest, which matches
    /// ShowOnAir.Caption and is right for the same reason: a flag call
    /// cutting across a segment is counted here too, and a label that
    /// flipped to the interruption and back would read as a fault on a
    /// strip somebody glances at once.
    ///
    /// A segment's own name where the line carried one, and otherwise the
    /// call's label from VoiceProfile.Describe - which is the table the
    /// Voice page's own list is built from, so the strip and the settings
    /// cannot disagree about what a call is called. The parenthetical half
    /// of those labels ("Fast lap (race best)") is settings wording and is
    /// dropped: a strip is not a place to explain a setting.
    /// </summary>
    public string? OnAirNow
    {
        get
        {
            lock (_speakingLock)
            {
                if (_speakingCount == 0)
                {
                    return null;
                }

                if (_onAirNamed is { Length: > 0 } named)
                {
                    return named;
                }

                if (_onAirCall is not { } call)
                {
                    return null;
                }

                var label = VoiceProfile.Describe(call).Label ?? string.Empty;

                var bracket = label.IndexOf(" (", StringComparison.Ordinal);

                return bracket > 0 ? label[..bracket] : label;
            }
        }
    }

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
    /// <param name="call">Which kind of call this line is, for <see cref="OnAirNow"/>.</param>
    /// <param name="named">
    /// What the line calls itself, where it carried a name - see
    /// QueuedVoiceItem.OnAir. Null is normal: most lines are named after
    /// their kind.
    /// </param>
    private void BeginSpeaking(AnnouncementEventType? call = null, string? named = null)
    {
        lock (_speakingLock)
        {
            _speakingCount++;

            if (!_speakingReported)
            {
                // Set before the event, so a handler asking what is on air
                // on the way in is answered about THIS line rather than the
                // last one - the same ordering ShowOnAir.Begin keeps, and
                // for the same reason.
                _onAirCall = call;
                _onAirNamed = string.IsNullOrWhiteSpace(named) ? null : named.Trim();

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
                // Cleared before the event, the mirror of BeginSpeaking's
                // reason: a handler being told nothing is speaking must not
                // be handed the line that has just stopped.
                _onAirCall = null;
                _onAirNamed = null;

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
    /// The most the ambience boost may lift a line by. Matches the top of the
    /// Ambience settings' multiplier range, so a hand-edited settings file
    /// can't turn the announcer into a fog horn.
    /// </summary>
    private const float MaxVoiceBoost = 2f;

    /// <summary>
    /// The gain to play an announcement at: the announcer's own level, then
    /// the ambience boost on top of it.
    ///
    /// The two are clamped separately on purpose. Every caller used to clamp
    /// the PRODUCT to 1.0, and the announcer's default level is 100% - so the
    /// boost the Ambience settings describe ("extra gain while ambience is
    /// playing loudly") was cut straight back to 1.0 and did nothing at all
    /// in the default setup, and only part of its job at 80%. The base level
    /// is still a 0-100% slider and stays within it; the boost is make-up
    /// gain above that and is allowed past unity.
    ///
    /// The result can be above 1.0, so whatever plays it has to accept a
    /// gain above unity and keep the peaks from clipping.
    /// </summary>
    private float AnnouncementGain(double baseVolume)
    {
        var boost = VoiceBoostProvider?.Invoke() ?? 1f;
        if (float.IsNaN(boost))
        {
            boost = 1f;
        }

        return (float)Math.Clamp(baseVolume, 0.0, 1.0) * Math.Clamp(boost, 1f, MaxVoiceBoost);
    }

    /// <summary>
    /// The profile's current expression settings, so a caller can amplify
    /// them for one announcement without replacing what the user chose.
    ///
    /// Exposed here rather than handing the voice store to every control
    /// that wants to nudge delivery.
    /// </summary>
    public (double Stability, double Style) CurrentDelivery =>
        (_voiceStore.Current.Stability, _voiceStore.Current.Style);

    /// <summary>
    /// How many times the announcer has started speaking since the app
    /// opened (16 Oct 2026) - so the recap can tell whether anything was
    /// said since its last stinger, and not fire a second one into the same
    /// silence. Only ever goes up.
    /// </summary>
    public long TimesStartedSpeaking => Interlocked.Read(ref _timesStartedSpeaking);

    private long _timesStartedSpeaking;

    private void RaiseSpeakingChanged(bool speaking)
    {
        if (speaking)
        {
            Interlocked.Increment(ref _timesStartedSpeaking);
        }

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
    /// How many times the announcer has been muted (26 Oct 2026, review). A
    /// caller feeding lines in over time - tonight's recap, a driver's night -
    /// captures it at the start and stops once it moves: a mute bins what was
    /// queued, and a mute lifted while the caller waited must not let the read
    /// carry on from the middle. See AnnouncerControl.ReadIsOver.
    /// </summary>
    public long MutedGeneration => Interlocked.Read(ref _mutedGeneration);

    private long _mutedGeneration;

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
                // Counted first (26 Oct 2026, review), so a long read feeding
                // its pieces in sees the mute even if it is lifted before it
                // looks - see MutedGeneration.
                Interlocked.Increment(ref _mutedGeneration);

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

                // And the sponsor's picture with it - see SponsorOnAir.End,
                // which was written for this and called from nowhere. A
                // mute is the club saying "stop", and a logo left on the
                // crowd screen for another half minute with nothing being
                // said about it is a read the sponsor did not get and the
                // room cannot explain.
                SponsorOnAir.End();
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
    /// <param name="voice">Another speaker's voice to test - the recap's mechanic, from the Commentary page. Null for the announcer.</param>
    public void SpeakTest(string text, double speed, SecondVoice? voice = null)
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
                    honourMute: false,
                    voice: voice,
                    forListeners: false);
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke($"[voice] Test failed - {ex.Message}");
            }
        });
    }

    /// <summary>
    /// Which engine speaks - ElevenLabs, the HeatCaller voice or the Windows
    /// voice - as saved on Voice Settings. For a second speaker, whose voice
    /// is picked per engine (see SecondVoice).
    /// </summary>
    public VoiceEngine VoiceEngineInUse => _voiceStore.Current.EffectiveEngine;

    /// <summary>
    /// What a line rendered now would sound like - engine, voice, model and
    /// delivery - as one string, for Race Control to tell whether the lines
    /// it voiced ahead are still in the voice in use (22 Oct 2026, bug hunt).
    /// Only the fields that change the sound: a fader or theme saving the
    /// same file must not throw the takes away.
    /// </summary>
    public string CurrentVoiceSignature
    {
        get
        {
            var p = _voiceStore.Current;
            return string.Join("|", p.EffectiveEngine, p.VoiceId, p.ModelId, p.HeatCallerVoice, p.KokoroVoiceName,
                p.Stability, p.SimilarityBoost, p.Style, p.Speed, p.KokoroStandsIn);
        }
    }

    /// <param name="voice">
    /// Another speaker's voice for this line alone - the mechanic in the
    /// recap's interview. Null for the announcer. Only the part picked for
    /// the engine in use changes anything.
    /// </param>
    /// <param name="showOnScreens">
    /// True for a line that would have gone to the screens had it been
    /// rendered ahead - the recap, the teaser and the lines that stand in
    /// for SpeakPrepared when its render failed. SpeakPrepared shows its
    /// text; this did not, so a recap spoken the slow way never reached the
    /// Big Screen or the Announcer page's feed. Off by default: the level
    /// check, the soundscape preview and the day's timers are not race calls.
    /// </param>
    /// <param name="tail">
    /// How much silence to leave behind the line - trimmed when another
    /// follows immediately. See SpeechTail.
    /// </param>
    /// <param name="lead">
    /// And how much in front of it - none when it follows another
    /// immediately. See SpeechLead.
    /// </param>
    /// <param name="onAir">
    /// What to call this line on the race director's strip while it is
    /// spoken - the segment's own name, where it is part of one. Null
    /// leaves it named after the kind of call, which is right for a level
    /// check or one of the day's timers and wrong for a four-minute recap.
    /// See QueuedVoiceItem.OnAir and OnAirNow.
    /// </param>
    public void SpeakReminder(string text, (double Stability, double Style)? deliveryOverride = null, SecondVoice? voice = null, bool showOnScreens = false,
        SpeechTail tail = SpeechTail.Normal, SpeechLead lead = SpeechLead.Normal, string? onAir = null, Action? dequeued = null, bool forListeners = true)
    {
        if (Muted || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        // forListeners: false for the level checks and previews (17 Oct 2026,
        // Bug Hunt 8, A06-10) - like SpeakTest, they are not the race and
        // must not go out on the public listen-along page.
        var spoken = SpellOutTimesForSpeech(text);
        EnqueueVoiceWork(AnnouncementEventType.Reminder, null,
            () =>
            {
                // WHY (26 Oct 2026): told the moment the queue takes this
                // line, so a long read fed in piece by piece knows when the
                // queue has nothing left behind it - see dequeued.
                Dequeued(dequeued);
                return SynthesizeAndPlayAsync(spoken, "[voice] Reminder", deliveryOverride: deliveryOverride, voice: voice, tail: tail, lead: lead, forListeners: forListeners);
            },
            displayText: showOnScreens ? text : null,
            onAir: onAir);
    }

    /// <summary>Tells a caller its line has been taken off the queue - see SpeakPrepared's dequeued. A throwing caller never stops the line.</summary>
    private static void Dequeued(Action? dequeued)
    {
        try
        {
            dequeued?.Invoke();
        }
        catch
        {
            // Nothing a caller does here is worth the line.
        }
    }

    /// <summary>A line already rendered to audio, ready to play the instant it's wanted.</summary>
    /// <param name="Text">What was sent to the voice - times spelled out for speech.</param>
    /// <param name="DisplayText">
    /// The line as written, for the Big Screen, the overlay and the Announcer
    /// feed. Screens used to be handed <paramref name="Text"/>, so a prepared
    /// recap showed "6 point nine four seconds" where the live path shows
    /// "6.935s".
    /// </param>
    /// <param name="StandIn">
    /// True when Kokoro stood in for the chosen voice on this line (22 Oct
    /// 2026, bug hunt 9) - a one-off refusal or server error can do that
    /// without starting a rest, so the caller cannot tell from the rest alone.
    /// </param>
    /// <param name="AudioShortened">
    /// Whether <paramref name="Audio"/> says the names shortened, for
    /// listen-along - see AudioFromShortenedText (23 Oct 2026, bug hunt 10).
    /// </param>
    public sealed record PreparedSpeech(string Text, byte[] Audio, string DisplayText, bool StandIn = false, bool AudioShortened = false)
    {
        /// <summary>
        /// Made by ElevenLabs, or read from its saved lines (5 Nov 2026) - so
        /// passed over if its quota has run out by the time it is played. See
        /// SpeakPrepared and ElevenLabsSavedLinesPassedOver.
        /// </summary>
        internal bool InElevenLabs { get; init; }

        /// <summary>Renders the line again as it would be live now, with whether that audio says the names shortened. Set by PrepareReminderAsync.</summary>
        internal Func<Task<(byte[] Audio, bool Shortened)>>? RenderAgain { get; init; }
    }

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
    /// <param name="voice">Another speaker's voice for this line alone, as for SpeakReminder. Null for the announcer.</param>
    /// <param name="cancel">
    /// Stops the render once nobody will hear it - the next race called
    /// over a recap (22 Oct 2026, known issues). Null comes back then, and
    /// nothing is said about it: it is not a fault.
    /// </param>
    public async Task<PreparedSpeech?> PrepareReminderAsync(
        string text, (double Stability, double Style)? deliveryOverride = null, SecondVoice? voice = null, CancellationToken cancel = default)
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

        var withoutVoice = profile;

        if (voice is not null)
        {
            profile = profile.CloneWithVoice(voice);
        }

        // Whichever voice would say it live - Kokoro, ElevenLabs, the relay,
        // or Kokoro standing in. This used to ask for a key and a voice
        // only, which with a free voice chosen would have sent every recap
        // to the slower speak-time path.
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
            // Rendered ahead, so a long line gets the long limit - see
            // RenderingAhead (22 Oct 2026, bug hunt).
            RenderingAhead.Value = true;
            var rendered = await RenderCastLineAsync(withoutVoice, profile, voice, spoken, cancel);
            return new PreparedSpeech(spoken, rendered.Audio, text, rendered.Source == LineSource.StandIn,
                AudioShortened: AudioFromShortenedText(rendered.Source, spoken))
            {
                InElevenLabs = rendered.Source is LineSource.ElevenLabs or LineSource.Library,
                RenderAgain = async () =>
                {
                    var again = await RenderCastLineAsync(withoutVoice, profile, voice, spoken);
                    return (again.Audio, AudioFromShortenedText(again.Source, spoken));
                },
            };
        }
        catch (OperationCanceledException) when (cancel.IsCancellationRequested)
        {
            StatusMessage?.Invoke("[voice] Stopped making a line in advance - the next race was called, so nobody would hear it.");
            return null;
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke(
                $"[voice] Couldn't render a line in advance ({ex.Message}) - it will synthesize as it speaks instead.");
            return null;
        }
    }

    /// <summary>
    /// Renders a line to be kept as a soundscape's recorded voice line (see
    /// StingerLibrary), in that soundscape's delivery.
    ///
    /// Only a real announcer voice is kept - ElevenLabs on the club's key or
    /// the HeatCaller voice. Null when Kokoro would say it - it speaks any
    /// line for free on this PC, so there is nothing to keep - including
    /// Kokoro standing in because ElevenLabs couldn't: a recording is played
    /// for good, and a stand-in take recorded by accident would replace the
    /// announcer on that line every race.
    /// </summary>
    /// <param name="fresh">
    /// True for Re-record all: a new ElevenLabs take, not the one the
    /// saved-voice library kept from the first recording - same voice, same
    /// delivery, same words, so the same key - which used to come straight
    /// back, and re-recording a bad take changed nothing. The HeatCaller
    /// voice's relay keeps one take of each line, so it has no new one to give.
    /// </param>
    public async Task<byte[]?> RecordVoiceLineAsync(string text, double stability, double style, bool fresh = false)
    {
        var profile = _voiceStore.Current.CloneForDelivery(stability, style);
        if (profile.EffectiveEngine == VoiceEngine.Kokoro || string.IsNullOrWhiteSpace(text))
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
            RenderingAhead.Value = true;
            var result = await RenderLineAsync(profile, spoken, fresh);
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
    /// <param name="lead">
    /// How much silence to leave in FRONT of the line - none when it
    /// follows another immediately. See SpeechLead.
    /// </param>
    /// <param name="onAir">
    /// What to call this line on the race director's strip - see
    /// SpeakReminder's own, and OnAirNow. A turn of a two-hander rendered
    /// ahead comes through here rather than SpeakReminder, so without this
    /// an exchange would have been the one segment the strip could not
    /// name.
    /// </param>
    /// <param name="dequeued">
    /// Called the moment the voice queue takes this line to play it (26 Oct
    /// 2026) - so a long read that feeds its pieces in as they are voiced
    /// (AnnouncerControl.FeedTheReadAsync) knows the queue has nothing left
    /// behind this one and the next must go in now. Never called for a line
    /// that is dropped instead. On the voice loop's thread; must not block.
    /// </param>
    public void SpeakPrepared(PreparedSpeech prepared, SpeechTail tail = SpeechTail.Normal, SpeechLead lead = SpeechLead.Normal, string? onAir = null, Action? dequeued = null)
    {
        if (Muted)
        {
            return;
        }

        var volume = AnnouncementGain(_voiceStore.Current.Volume);
        var trailing = TailSecondsFor(tail);

        // Read here with the volume and the tail, on the calling thread,
        // rather than inside the queued work: CurrentLeadingSilence reads
        // the saved profile, and the profile can be written from the Voice
        // page while this line waits its turn for the speaker.
        var leading = LeadSecondsFor(lead);

        // The text goes up only if the line is accepted - see
        // EnqueueVoiceWork, which raises it once nothing will drop it.
        EnqueueVoiceWork(AnnouncementEventType.Reminder, null, onAir: onAir, work: async () =>
        {
            Dequeued(dequeued);

            StatusMessage?.Invoke($"[voice] Reminder (prepared): \"{prepared.Text}\"");

            // Made in ElevenLabs before its quota ran out, and played after
            // (5 Nov 2026): said again now, the way a live line would be -
            // Kokoro standing in, in the speaker's own stand-in voice - so a
            // recap prepared during the teaser is not the one ElevenLabs
            // line on a night the rest is Kokoro's. See
            // ElevenLabsSavedLinesPassedOver.
            var audio = prepared.Audio;
            var shortened = prepared.AudioShortened;

            if (prepared.InElevenLabs && prepared.RenderAgain is { } again && ElevenLabsSavedLinesPassedOver(_voiceStore.Current))
            {
                try
                {
                    (audio, shortened) = await again();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    StatusMessage?.Invoke($"[voice] Reminder (prepared): dropped \"{prepared.Text}\" - it was made in ElevenLabs, whose quota is used up, and couldn't be said again ({ex.Message}).");
                    return;
                }
            }

            await _audioPlaybackLock.WaitAsync();
            try
            {
                // The same checks SynthesizeAndPlayAsync makes after the
                // playback lock. This line was taken off the queue before it
                // waited for the speaker, so neither a flag's queue purge nor
                // the countdown's could reach it - and when the cut stops
                // the sound holding the lock, this line would get the
                // speaker first and play a whole recap over (or ahead of)
                // the countdown sounds queued behind it. The voice loop
                // stamped both generations when it dequeued this work, and
                // they flow in here, so a change since then means it has
                // been overtaken.
                if (IsSupersededByInterrupt())
                {
                    StatusMessage?.Invoke($"[voice] Reminder (prepared): dropped \"{prepared.Text}\" - a flag call interrupted while it waited for the speaker.");
                    return;
                }

                if (IsCutByCountdown())
                {
                    StatusMessage?.Invoke($"[voice] Reminder (prepared): dropped \"{prepared.Text}\" - the countdown started while it waited for the speaker.");
                    return;
                }

                // A battle line whose two cars swapped - see AnnouncementService.BattleLines.
                if (IsOvertakenOnTrack())
                {
                    StatusMessage?.Invoke($"[voice] Reminder (prepared): dropped \"{prepared.Text}\" - the two cars it is about swapped while it waited for the speaker.");
                    return;
                }

                // Muted while it waited for the speaker.
                if (Muted)
                {
                    StatusMessage?.Invoke("[voice] Reminder (prepared): dropped - the announcer was muted.");
                    return;
                }

                // Listeners at home hear a prepared line (recap, teaser, a
                // two-hander's turn) too - offered just before the speaker,
                // the way the queued lines are. This path never did.
                //
                // WHY (23 Oct 2026, bug hunt 10): with whether its audio says
                // the names shortened, so the line is not held back from
                // listeners just because this path could not say.
                var onAir = OfferLineToListeners(prepared.Text, audio, shortened);

                // Noting when its last word lands, so a piece of imaging
                // can hit the post on it (17 Oct 2026) - see TalkUp. Plays
                // exactly as PlayAsync did.
                // WHY (23 Oct 2026, bug hunt 10): re-checked once the line is registered - see PlayPcmAsync.
                await PlayNotingLastWordAsync(audio, volume, trailing, leading, StillWantedUnlessOvertaken(honourMute: true), onAir);
            }
            finally
            {
                _audioPlaybackLock.Release();
            }
        }, displayText: prepared.DisplayText);
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
    ///
    /// A GAP IS NOT A LAP TIME (15 Oct 2026). Every match used to be read
    /// as a lap, so the model's "Bill is 0.3s behind" went out as "zero
    /// point three zero seconds behind" - the computer reading at its most
    /// obvious, and in the AI's lines, which are the ones meant to sound
    /// most like a broadcast. A figure is now read as a GAP when it says
    /// so: under a second (no lap is), or with a word beside it that only a
    /// gap takes - "behind", "clear", "by", "within" and the rest of
    /// <see cref="GapWordsAfter"/> and <see cref="GapWordsBefore"/>. Those
    /// are said through RaceNumbers.MarginSpoken - "three tenths behind",
    /// "won by four hundredths" - and everything else is still a lap time,
    /// "a five oh six".
    /// </summary>
    internal static string SpellOutTimesForSpeech(string text) =>
        System.Text.RegularExpressions.Regex.Replace(
            // A time the model spelled digit by digit is put back into
            // figures first, so it is said by the rules below rather than
            // read out a digit at a time (3 Nov 2026). See RaceNumbers.
            RaceNumbers.DigitsFromSpelledTimes(text),
            // [0-9] rather than \d, which in .NET also matches other
            // scripts' digits that the parse below can't read. At most nine
            // whole digits, so the millisecond sum can't overflow. The word
            // either side is only LOOKED at - the lookbehind and lookahead
            // capture it without consuming it, so it is left exactly as
            // written.
            @"(?<=\b(?<pre>[A-Za-z]+)\s+)?(?<![0-9]:)\b(?<whole>[0-9]{1,9})\.(?<frac>[0-9]{1,3})\s*(?:seconds?|secs?|s)\b(?=(?:\s+(?<post>[A-Za-z]+))?)",
            match =>
            {
                // InvariantCulture: this is digit text pulled out of a
                // regex, not a localised number.
                if (!long.TryParse(match.Groups["whole"].Value, System.Globalization.NumberStyles.None,
                        System.Globalization.CultureInfo.InvariantCulture, out var whole))
                {
                    return match.Value;
                }

                // The fraction as milliseconds - "9" is 900, "93" is 930,
                // "935" is 935 - then the SAME formatter the template lines
                // use. Rounding the fraction on its own used to lose the
                // carry: 6.995 came out as "6 point one zero zero".
                var fractionMs = int.Parse(match.Groups["frac"].Value.PadRight(3, '0'),
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture);

                var ms = (whole * 1000) + fractionMs;

                // Under a second it is a gap whatever is around it. Over a
                // second only the words that cannot follow a lap make it
                // one - "5.06s down from 5.2s" is a lap time going down.
                var after = match.Groups["post"].Value;

                var readsAsGap = whole == 0
                    || (GapWordsAfter.Contains(after) && (whole < 1 || !LapCanBe(after)))
                    || GapWordsBefore.Contains(match.Groups["pre"].Value);

                return readsAsGap
                    ? RaceNumbers.MarginSpoken(ms / 1000.0)
                    : FormatSecondsForSpeech(ms);
            },
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>
    /// Words that, straight AFTER a figure, make it a gap and not a lap:
    /// "0.3s behind", "1.2 seconds clear", "half a second up the road".
    /// </summary>
    private static readonly HashSet<string> GapWordsAfter = new(StringComparer.OrdinalIgnoreCase)
    {
        "behind", "back", "ahead", "clear", "adrift", "up", "down", "off", "faster", "slower", "quicker",
        "apart", "between", "covering", "separating", "margin", "gap", "better", "worse",
    };

    /// <summary>
    /// The gap words that can also follow a LAP TIME - "5.06s down from
    /// 5.2s", "a 5.06 off the pace", "5.1s faster than last week". Under a
    /// second they still mean a gap (no lap is that short); over it they
    /// are left to read as a lap.
    /// </summary>
    private static bool LapCanBe(string after) =>
        after.ToLowerInvariant() is "up" or "down" or "off" or "faster" or "slower" or "quicker" or "better" or "worse"
            or "margin" or "gap" or "between" or "covering" or "separating";

    /// <summary>
    /// Words that, straight BEFORE a figure, make it a gap: "by 0.04s",
    /// "within 0.3s", "just 0.2s".
    ///
    /// NOT "of" and NOT "under", which both look like gap words and are
    /// far commoner in front of a lap: "a best lap of 5.06s", "the first
    /// car under 5.5s". Read as gaps, those would come out as "a best lap
    /// of five seconds" - a lap time rounded to the half second.
    /// </summary>
    private static readonly HashSet<string> GapWordsBefore = new(StringComparer.OrdinalIgnoreCase)
    {
        "by", "within", "just", "only", "mere", "barely",
    };

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
        // were in the Kokoro voice. A date already past is last month's and
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

        // The free Kokoro voice reports into the same console.
        KokoroSpeech.Log = message => StatusMessage?.Invoke(message);
        KokoroSpeech.DeleteLeftoverDownload();

        // Loaded now, in the background, whenever it is the voice that
        // would speak - whether because the club chose it or because it is
        // what stands in for the chosen one.
        //
        // It used to be loaded only when it was the voice CHOSEN, because
        // the model takes a few hundred MB of memory and a club on
        // ElevenLabs had no reason to give that up: the free Windows voice
        // was what covered for ElevenLabs, and that needed no loading. With
        // Windows gone, Kokoro is what covers - and the first load reads
        // 310 MB off a track PC's disk. A yellow flag thrown the moment the
        // club Wi-Fi drops would have waited the whole of that, which is
        // exactly as bad as being silent. A club that has turned the
        // stand-in off still pays nothing (StandInAllowed says no).
        var profile = _voiceStore.Current;

        if (profile.EffectiveEngine == VoiceEngine.Kokoro || StandInAllowed(profile))
        {
            KokoroSpeech.WarmUp();
        }

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

        // WHY (23 Oct 2026, bug hunt 10): a line binned here gives back what
        // it reserved (a sponsor's ration, a battle's pair) once the queue
        // lock is released - see BinQueuedLocked.
        List<Action>? giveBack = null;

        lock (_voiceQueueLock)
        {
            // RemoveAll keeps the survivors in order, as Clear + AddRange did.
            voiceCleared = keepPriority
                ? BinQueuedLocked(item => !IsPriorityAnnouncement(item.Type), ref giveBack)
                : BinQueuedLocked(_ => true, ref giveBack);
        }

        GiveBackLost(giveBack);

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

            // 17 Oct 2026, Bug Hunt 8, A06-5: and the field window, as a flag
            // and the countdown already do - or a flush up to a third of a
            // second behind still spoke about the race that was stopped.
            BinTheFieldCallsUnderLock();
        }

        // And the call kept for the end of a line (A06-5), for the same reason.
        ForgetTheHeldOverCall(count: true);

        if (voiceCleared + soundCleared + batchesCleared > 0)
        {
            StatusMessage?.Invoke($"[voice] Cleared {Plural.Of(voiceCleared, "queued voice line")}, {Plural.Of(soundCleared, "queued sound")}, and {Plural.Of(batchesCleared, "pending batch", "pending batches")}.");
        }
    }

    /// <summary>Reset session-local state (leader/lap/streak tracking) - call this on each new Connect.</summary>
    public void ResetSession()
    {
        EndPostRaceQuiet("the session was reset");

        lock (_voiceQueueLock)
        {
            _standings.Clear();
            _currentLeaderTransponder = null;
            _leads.Reset();
            _provisionalPoleTransponder = null;
            _poleCalledFromGateFor = null;

            // Or the next race would skip its first report - the counter
            // would still hold the last race's lap number.
            _lastPositionReportLap = -1;

            // And a report still waiting for its cars is not read into the
            // next session.
            lock (_positionReportLock)
            {
                _waitingPositionReport = null;
            }

            // Same reasoning: a driver mid-cycle at the flag would start the
            // next race offset, hearing their time on laps 2, 5, 8 instead
            // of 3, 6, 9.
            _lapTimeCounts.Clear();
            ForgetSplitTimeCounts();
            ForgetCoachingCounts();
            _lastLapElapsedMs.Clear();
            _lapCrossings.Clear();
            _qualifyingLaps.Clear();
            _streakCounts.Clear();
            _rivalryLastAhead.Clear();
            _lappedAnnounced.Clear();

            // Under its own lock - see _battlesLock and _battlesGeneration.
            lock (_battlesLock)
            {
                _battlesAnnounced.Clear();
                _battlesPending.Clear();
                _battlesCooling.Clear();
                _battlesGeneration++;
            }

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

            // 17 Oct 2026, Bug Hunt 8, A06-5 - see StopAllPendingAudio.
            BinTheFieldCallsUnderLock();
        }

        ForgetTheHeldOverCall(count: true);
    }
}
