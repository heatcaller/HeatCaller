using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// The snapshot channels the relay holds one current document for. Results
/// are not here: they are a collection, published one session at a time,
/// and they go through <see cref="RelayPublisher.PublishResult"/>.
/// </summary>
public enum RelayChannel
{
    State,
    Roster,
    Programme,
    Standings,
    Badges,
    Achievements,
    Tracks,
    Site,
    Preview,
    CheckIn,
    Manage,
    Signup,

    // 22 Oct 2026 (driver experience). The first two are private, per
    // badge, and only taken by the relay while drivers' nights are open;
    // captions is public, and its text is redacted (PublicPrivacy).
    DriverLive,
    DriverMe,
    Captions,

    // 4 Nov 2026 (the relay roadmap): where qualifying stands for each class
    // on the race day in hand, and the main each driver would make. Public,
    // as the results it is worked out from are.
    Qualifying,

    // 5 Nov 2026 (Asher: "add sponsorships to the live tab at the bottom"):
    // the club's sponsors for the foot of Live - see RelaySponsors. Public.
    Sponsors,

    // 12 Nov 2026 (setup plan D13): the club's next race night, or that it's
    // off - see NextNight. Public: it's what the club tells everybody.
    NextNight,
}

/// <summary>
/// What the publisher is doing, as one immutable object the UI can read
/// from any thread without locking.
/// </summary>
/// <param name="Enabled">Publishing is switched on in settings.</param>
/// <param name="Configured">A base address, club and token are all present.</param>
/// <param name="Online">The last attempt reached the relay and was accepted.</param>
/// <param name="LastSuccessUtc">When something last landed, or null if nothing has.</param>
/// <param name="Queued">Documents waiting to go.</param>
/// <param name="Dropped">Results discarded because the queue was full, or refused outright.</param>
/// <param name="LastError">The most recent failure in plain words, or null.</param>
public sealed record RelayStatus(
    bool Enabled,
    bool Configured,
    bool Online,
    DateTime? LastSuccessUtc,
    int Queued,
    int Dropped,
    string? LastError)
{
    public static readonly RelayStatus Off = new(false, false, false, null, 0, 0, null);
}

/// <summary>The outcome of a one-off check, worded for a human.</summary>
public sealed record RelayProbe(bool Ok, string Message);

/// <summary>
/// Publishes what the app already builds to a hosted relay, on a thread of
/// its own.
///
/// THE RULE THIS FILE EXISTS TO KEEP: nothing in the race path ever waits
/// on the network. Every public method that a race-day code path calls
/// returns immediately, never throws, and never blocks on a socket. Track
/// wifi is bad; a race that stalls because a POST timed out is worse than
/// no relay at all.
///
/// Two kinds of traffic, treated differently on purpose:
///
///   SNAPSHOTS (state, roster, programme, standings) are droppable. Each
///   one supersedes the last, so a backlog is discarded rather than
///   replayed - publishing the live race five times while offline and then
///   sending all five would show a spectator four positions that are no
///   longer true.
///
///   RESULTS are not droppable. A race day produces one record per session
///   and they all matter, so they queue in order until they land.
///
/// Threading: the UI thread calls Publish*; this class owns a single
/// background thread that does all the sending. The stores are never
/// touched here - callers hand over a finished JSON string built on the UI
/// thread, which is the same discipline the embedded web server follows.
/// </summary>
public sealed partial class RelayPublisher : IDisposable
{
    /// <summary>Far beyond a race day's worth of sessions; a ceiling, not a working limit.</summary>
    private const int MaxQueuedResults = 500;

    /// <summary>Kept under the relay's own default body limit so a payload is refused here, with a readable message, rather than there.</summary>
    private const int MaxPayloadBytes = 900 * 1024;

    /// <summary>The most of any one answer that is read. Has to hold the relay's fullest driver/inbox - see where the client is made.</summary>
    internal const int MaxAnswerBytes = 4 * 1024 * 1024;

    private static readonly TimeSpan[] Backoff =
    {
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4),
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(15),
        TimeSpan.FromSeconds(30),
    };

    private readonly AppSettings _settings;
    private readonly HttpClient _http;
    private readonly object _gate = new();
    private readonly Dictionary<string, Pending> _snapshots = new(StringComparer.Ordinal);
    private readonly Queue<Pending> _results = new();

    /// <summary>The exceptions to RelayResultsFromUtc. Read and written under its own lock. See RelayResultsIndex.Ledger.</summary>
    private readonly RelayResultsIndex.Ledger _ledger = RelayResultsIndex.Ledger.Load();
    private readonly object _ledgerGate = new();

    /// <summary>
    /// The thread this was made on - the UI thread, which settings belong
    /// to (see DeletePublishedAsync). The worker learns when a result has
    /// reached the relay, and the window that moves with it is a setting,
    /// so that note is posted here rather than written from the worker.
    /// Null only where there is no such thread; the note is then made inline.
    /// </summary>
    private readonly SynchronizationContext? _settingsThread;
    private readonly ManualResetEventSlim _wake = new(false);
    private readonly CancellationTokenSource _stopping = new();
    private readonly Thread _worker;

    private long _lastSeq;
    private int _dropped;
    private int _failures;

    /// <summary>
    /// When the worker may next try to send, after a failure, as
    /// Environment.TickCount64 milliseconds. Zero when it is not backing
    /// off. Guarded by _gate.
    ///
    /// Held as a time rather than worked out from the wait, because the
    /// wait can be cut short: every Enqueue sets the wake event, and a
    /// live race enqueues a frame a second. Before this, each of those
    /// woke the worker straight into another attempt, so a relay that was
    /// down got a request a second instead of one every thirty.
    ///
    /// On the tick count rather than the wall clock: it was DateTime.UtcNow,
    /// and a clock put back ten minutes - Windows syncing time on a laptop
    /// that had drifted - left the worker waiting ten minutes, not thirty
    /// seconds, with nothing published and no check-ins collected. The
    /// tick count only ever goes forward.
    /// </summary>
    private long _nextAttemptTick;

    /// <summary>
    /// The worker's current pass - one round of sending and collecting -
    /// so QuiesceAsync can cut it short. Null between passes. Guarded by
    /// _gate.
    /// </summary>
    private CancellationTokenSource? _pass;

    /// <summary>Set by QuiesceAsync, cleared by SettingsChanged: no new pass starts while it is set. Guarded by _gate.</summary>
    private bool _quiesced;

    /// <summary>Set whenever the worker is between passes. See QuiesceAsync.</summary>
    private readonly ManualResetEventSlim _idle = new(true);

    private bool _online;
    private DateTime? _lastSuccessUtc;
    private string? _lastError;
    private volatile RelayStatus _status = RelayStatus.Off;

    public RelayPublisher(AppSettings settings)
    {
        _settings = settings;
        _target = ReadTarget(settings);
        _settingsThread = SynchronizationContext.Current;

        // A ledger from before it recorded its relay describes the one in
        // use: until now the window was cleared whenever that changed.
        AdoptLedger(_target);

        // A timeout, not the default of 100 seconds. A socket that hangs
        // must not hold the queue for a minute and a half.
        //
        // No redirects: the token rides on every request, and a relay
        // address that answered "go over there instead" - a typo, a
        // hijacked name, a proxy misconfigured - would otherwise have it
        // sent on to wherever "there" is. A relay that moves gets its new
        // address typed in, deliberately.
        //
        // Four megabytes for any answer. Nearly everything the relay says
        // back is a line or two of JSON; a misconfigured address that
        // answers with a video stream must not be read into memory whole.
        //
        // Four, not one, because of the drivers' requests: the relay holds
        // up to 200 of them at up to 12 KB each (a setup sheet) and gives
        // them all in one answer - 2.4 MB at the very most. Over the limit
        // that answer could never be read, so nothing was ever confirmed
        // and the inbox never emptied.
        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            // A backstop only. The real limit is per request and is worked
            // out from what that request has to upload - see Send. This one
            // has to be longer than the longest of those or it would cut
            // them short, because HttpClient.Timeout is a hard ceiling that
            // a per-request token cannot extend.
            Timeout = TimeSpan.FromSeconds(60),
            MaxResponseContentBufferSize = MaxAnswerBytes,
        };

        _worker = new Thread(Run)
        {
            IsBackground = true,
            Name = "relay-publisher",
        };

        // EVERY OFF SWITCH IS TOLD ON EVERY START (17 Oct 2026, Bug Hunt 8,
        // A16-1 / A14-2). These four were armed only by the untick itself,
        // so an untick that never reached the relay - Wi-Fi down, or
        // publishing switched off at the time - was forgotten on restart
        // and the club's announcer, shout-outs, vote or drivers' evenings
        // stayed public. Each close is idempotent, backed off, and cleared
        // once the relay takes it (a 404 counts: see IsGoneForAClose).
        ArmCloses(settings);

        // The manager requests already run, from before a restart (17 Oct
        // 2026, Bug Hunt 8, A16-2) - see ManageRunLedger.
        foreach (var pair in ManageRunLedger.Load(ManageRunLedger.DefaultPath, DateTime.UtcNow))
        {
            _manageRunUntil[pair.Key] = pair.Value;
        }

        RefreshStatus();
        _worker.Start();
    }

    /// <summary>
    /// Marks a close as owed for every relay feature that is switched off,
    /// so the relay in use is told even when the untick that switched it
    /// off was never sent (17 Oct 2026, Bug Hunt 8, A16-1 / A14-2).
    /// </summary>
    private void ArmCloses(AppSettings settings)
    {
        _listenClosePending = !settings.ListenAlongOnTheRelay;
        _nightsClosePending = !settings.PublishDriverNightsToTheRelay;
        _notesClosePending = !settings.ClubNotesFromTheRelay;
        _voteClosePending = !settings.RoomVoteFromTheRelay;

        // 22 Oct 2026 (driver experience): the asks drivers sent from their
        // phones are held on the relay until collected, so switching them
        // off has to be told too, or the relay keeps taking them. Off with
        // drivers' nights too - see RelayPublisher.Requests.AsksWanted.
        _asksClosePending = !AsksWanted(settings);
    }

    /// <summary>
    /// A close of the drivers' own requests is owed to the relay: it then
    /// stops taking them and throws away every one it still holds. Set by
    /// ArmCloses on every start while the switch is off, and by
    /// CloseDriverAsksOnTheRelay; cleared by the requests pass
    /// (RelayPublisher.Requests) once the relay has taken it.
    /// (22 Oct 2026, driver experience.)
    /// </summary>
    private volatile bool _asksClosePending;

    /// <summary>
    /// Called when the club unticks driver requests: the relay is told, on
    /// the worker thread, until it has taken it - see _asksClosePending.
    /// Returns at once; nothing here waits on the network.
    /// </summary>
    public void CloseDriverAsksOnTheRelay()
    {
        _asksClosePending = true;
        _wake.Set();
    }

    /// <summary>
    /// The drivers' own requests (details, battery pack, result queries):
    /// collected from the relay, filed, then confirmed, and the close sent
    /// while one is owed. Implemented in RelayPublisher.Requests.cs; runs on
    /// the worker thread once per pass, after sign-up, and keeps its own
    /// tick. (22 Oct 2026, driver experience.)
    /// </summary>
    partial void CollectDriverAsksPass(CancellationToken token);

    /// <summary>
    /// Whether a refused CLOSE (a switch going off) may count as taken.
    /// A 404 on these routes means the relay has no such route (an old
    /// relay) or no such club - and either way it cannot be serving the
    /// feature, so there is nothing to close. Without this, the closes
    /// armed on every start (17 Oct 2026, Bug Hunt 8, A16-1) would retry
    /// against an old relay forever.
    /// </summary>
    private static bool IsGoneForAClose(HttpResponseMessage response) =>
        (int)response.StatusCode == 404;

    /// <summary>
    /// Sends one request with a time limit worked out from how much it has
    /// to UPLOAD, instead of one flat limit for everything.
    ///
    /// WHY. The client's timeout was eight seconds, flat, and it covers the
    /// whole exchange including the upload. A published document is allowed
    /// up to MaxPayloadBytes - 900 KB - so on a club's ordinary broadband
    /// the big publish could not physically finish in time: 900 KB needs
    /// about 7 seconds at 1 Mbps up and about 15 at half that. The publish
    /// timed out, the backoff took over, and the club's results page simply
    /// stopped updating - with the laptop reporting a network problem for a
    /// network that was working exactly as fast as it always had.
    ///
    /// Raising the flat number was the wrong fix the other way: most of
    /// what this class sends is a poll with no body at all, and a dead
    /// relay would then hold the worker thread for the best part of a
    /// minute on every one.
    ///
    /// So: eight seconds of hand-shaking and thinking time, plus a second
    /// for every 32 KiB there is to push. A poll keeps the eight it had; a
    /// full document gets about thirty-six.
    /// </summary>
    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(token);
        limit.CancelAfter(TimeoutFor(request));

        return await _http.SendAsync(request, limit.Token).ConfigureAwait(false);
    }

    /// <summary>The blocking form, for the worker thread. Same limit.</summary>
    private HttpResponseMessage Send(HttpRequestMessage request, CancellationToken token) =>
        SendAsync(request, token).GetAwaiter().GetResult();

    private static TimeSpan TimeoutFor(HttpRequestMessage request)
    {
        // ContentLength is set for every body this class sends (they are all
        // StringContent). Null means no body, which is a poll.
        var bytes = request.Content?.Headers.ContentLength ?? 0;

        return TimeSpan.FromSeconds(8 + Math.Max(0, bytes) / (32.0 * 1024));
    }

    /// <summary>The current picture. Safe to read from any thread.</summary>
    public RelayStatus Status => _status;

    private int _generation;

    /// <summary>
    /// Moves on every time the settings change.
    ///
    /// Callers that only publish a document when it differs from the last
    /// one they sent read this to know when "the last one they sent" stops
    /// meaning anything: a new club, a new address, or names switched to
    /// short form all need everything sent again, even though nothing in
    /// the documents themselves changed. Deliberately NOT moved by
    /// deleting: a club that has just cleared the relay does not want it
    /// filled straight back up.
    /// </summary>
    public int Generation => Volatile.Read(ref _generation);

    /// <summary>
    /// A driver checked in or out from their phone. Raised on the
    /// publisher's own thread - handlers marshal to the UI themselves.
    ///
    /// The handler says whether the tap was dealt with - applied, or
    /// deliberately ignored - and it must not return until it has been.
    /// A tap is acknowledged to the relay, which then forgets it, only once
    /// this has said true for it and for every tap before it; a handler
    /// that queued the work and said yes at once was how a tap arriving as
    /// the laptop closed got acknowledged and never applied.
    /// </summary>
    public event Func<RelayCheckInTap, bool>? CheckInReceived;

    /// <summary>Why online check-ins are not being collected, in plain words, or null when they are.</summary>
    public string? CheckInProblem => _checkInProblem;

    /// <summary>
    /// A phone's race-day manager, signed in over the relay, has asked for
    /// something. Raised on the publisher's own thread, one request at a
    /// time and never concurrently with itself - CollectManageRequests
    /// awaits each answer before asking for the next.
    ///
    /// Unlike CheckInReceived's bool, the handler hands back the answer
    /// itself: RaceDayService.HandleJsonAsync already does its own
    /// UI-thread marshaling (see its doc comment), so there is no separate
    /// "applied" signal to give - the JSON it returns IS the result, ready
    /// to post straight back to the relay for the phone to collect.
    /// </summary>
    public event Func<string, CancellationToken, string>? ManageRequestReceived;

    /// <summary>Why the race-day manager is not answering over the relay, in plain words, or null when it is.</summary>
    public string? ManagerProblem => _managerProblem;

    /// <summary>
    /// Every driver sign-up waiting on the relay for the desk, as of the
    /// latest look - the whole list each time, not just what is new, so a
    /// sign-up a phone has corrected replaces its old self. Raised on the
    /// publisher's own thread; the handler marshals to the UI itself.
    ///
    /// Nothing is acknowledged by collecting: a sign-up stays on the relay
    /// until the desk answers it (see AnswerSignup), so a handler that is
    /// not there, or a laptop that closes, loses nothing.
    /// </summary>
    public event Action<IReadOnlyList<RelaySignupRequest>>? SignupsCollected;

    /// <summary>
    /// Why sign-up is not working, in plain words, or null when it is: the
    /// relay refusing the signup document itself first (see
    /// _signupDocumentProblem), else why sign-ups are not being collected.
    /// </summary>
    public string? SignupProblem => _signupDocumentProblem ?? _signupProblem;

    /// <summary>
    /// A line for the app's console, from the publisher's own thread - the
    /// handler must be safe to call from any thread. Only for the rare thing
    /// worth a line of its own, such as a sign-up answer given up on.
    /// </summary>
    public Action<string>? Log { get; set; }

    /// <summary>
    /// Whether to look for sign-ups at all - set by the desk from its own
    /// store (sign-up's switch lives there, not in settings). Answers
    /// already queued are posted whatever this says.
    /// </summary>
    public bool SignupsWanted
    {
        get => Volatile.Read(ref _signupsWanted);
        set
        {
            Volatile.Write(ref _signupsWanted, value);

            if (value)
            {
                _nextSignupPollTick = 0;
                _wake.Set();
            }
        }
    }

    /// <summary>RelayPinLockedUntilUtc's twin for sign-up's invite codes.</summary>
    public DateTime? RelaySignupLockedUntilUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref _signupLockedUntilTicks);

            if (ticks <= 0)
            {
                return null;
            }

            var until = new DateTime(ticks, DateTimeKind.Utc);
            return until > DateTime.UtcNow ? until : null;
        }
    }

    /// <summary>
    /// Queues the desk's answer to one sign-up, by its invite's hash, to be
    /// posted on the next pass - and again on every pass until the relay
    /// takes it. <paramref name="answerJson"/> is the whole body for
    /// signup/answer, the hash included. A later answer for the same sign-up
    /// replaces one not yet posted. Returns at once; safe from any thread.
    /// </summary>
    public void AnswerSignup(string hash, string answerJson)
    {
        lock (_signupGate)
        {
            _signupAnswers[hash] = answerJson;

            // A new answer gets a fresh hour - see SignupAnswerGiveUpAfter.
            _signupAnswerFirstFailed.Remove(hash);
        }

        _nextSignupPollTick = 0;
        _wake.Set();
    }

    /// <summary>
    /// RelayPinLockedUntilUtc's twin for the manager PIN - when the relay
    /// will let it be tried again, after too many wrong guesses. Kept
    /// entirely apart from the check-in lock: a club's staff PIN and
    /// manager PIN are guessed at by different people for different
    /// reasons, and one being locked out must not read as the other being
    /// closed too.
    /// </summary>
    public DateTime? RelayManagerPinLockedUntilUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref _managerPinLockedUntilTicks);

            if (ticks <= 0)
            {
                return null;
            }

            var until = new DateTime(ticks, DateTimeKind.Utc);
            return until > DateTime.UtcNow ? until : null;
        }
    }

    /// <summary>
    /// When the relay will let the club's staff PIN be tried again, in
    /// laptop UTC, while the relay has closed PIN sign-in after too many
    /// wrong guesses. Null when it is not locked, or the time has passed.
    ///
    /// Read from the inbox answer, which the laptop polls anyway: otherwise
    /// a club would only find out from a phone at the desk that nobody can
    /// sign in. Changing the staff PIN is what reopens it. Safe from any
    /// thread - held as ticks so it is read and written in one piece.
    /// </summary>
    public DateTime? RelayPinLockedUntilUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref _pinLockedUntilTicks);

            if (ticks <= 0)
            {
                return null;
            }

            var until = new DateTime(ticks, DateTimeKind.Utc);
            return until > DateTime.UtcNow ? until : null;
        }
    }

    private readonly object _clockGate = new();
    private long _clockOffsetTicks;
    private bool _haveClockOffset;

    /// <summary>
    /// A sample far from RelayClockOffset, waiting for a second one to
    /// agree with it before the offset jumps to it. See SampleClock.
    /// </summary>
    private long _pendingStepTicks;
    private bool _havePendingStep;

    /// <summary>A sample this far from the offset is a clock step, not jitter.</summary>
    private static readonly TimeSpan ClockStep = TimeSpan.FromSeconds(3);

    /// <summary>How close two step samples must be to each other to be believed.</summary>
    private static readonly TimeSpan ClockStepAgree = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How far the relay's clock is ahead of this laptop's (relay minus
    /// local), measured from the Date header on the relay's answers and
    /// smoothed. Zero until the first answer arrives. Safe from any thread.
    ///
    /// Needed because a phone's check-in is stamped by the RELAY's clock
    /// and a change at the desk by THIS one, and the two are compared to
    /// decide which came last. A laptop running two minutes slow - common
    /// on a machine that is rarely online - made every tap in the two
    /// minutes after a desk change look older than it, and quietly dropped
    /// it. Taps handed out through CheckInReceived are already converted
    /// with this; nothing else needs to apply it.
    /// </summary>
    public TimeSpan RelayClockOffset
    {
        get
        {
            lock (_clockGate)
            {
                return TimeSpan.FromTicks(_clockOffsetTicks);
            }
        }
    }

    /// <summary>
    /// Folds one answer's Date header into RelayClockOffset.
    ///
    /// The header is whole seconds, rounded down, so half a second is
    /// added back, and the laptop's side is taken at the middle of the
    /// round trip. An answer that took over two seconds is not used: the
    /// middle of a long wait says little about when the relay wrote the
    /// header. The first good answer is taken as it is; after that each
    /// moves the figure a fifth of the way, so one odd answer cannot
    /// swing it by much.
    ///
    /// Except when a clock has been stepped - Windows syncing a laptop
    /// that was two minutes out, say. Smoothing alone took about twenty
    /// answers, a minute or more, to follow that, and for that minute
    /// desk changes and phone taps were compared on clocks that disagreed
    /// again. So a sample more than three seconds from the figure is held,
    /// and when the next one agrees with it to within a second, the figure
    /// jumps straight to it. Two in agreement is a step; one on its own
    /// might be one slow answer, and is not trusted alone.
    /// </summary>
    private void SampleClock(HttpResponseMessage response, DateTime sentUtc, DateTime receivedUtc)
    {
        try
        {
            if (response.Headers.Date is not { } relayDate)
            {
                return;
            }

            var roundTrip = receivedUtc - sentUtc;

            if (roundTrip < TimeSpan.Zero || roundTrip > TimeSpan.FromSeconds(2))
            {
                return;
            }

            var localMiddle = sentUtc + TimeSpan.FromTicks(roundTrip.Ticks / 2);
            var relayMiddle = relayDate.UtcDateTime + TimeSpan.FromMilliseconds(500);
            var sample = (relayMiddle - localMiddle).Ticks;

            lock (_clockGate)
            {
                if (!_haveClockOffset)
                {
                    _clockOffsetTicks = sample;
                    _haveClockOffset = true;
                }
                else if (Math.Abs(sample - _clockOffsetTicks) > ClockStep.Ticks)
                {
                    if (_havePendingStep && Math.Abs(sample - _pendingStepTicks) <= ClockStepAgree.Ticks)
                    {
                        _clockOffsetTicks = sample;
                        _havePendingStep = false;
                    }
                    else
                    {
                        _pendingStepTicks = sample;
                        _havePendingStep = true;
                    }
                }
                else
                {
                    // Near the figure: ordinary jitter, and any step that
                    // was waiting for a second opinion did not get one.
                    _havePendingStep = false;
                    _clockOffsetTicks += (sample - _clockOffsetTicks) / 5;
                }
            }
        }
        catch
        {
            // A malformed header is no reason to fail a send that worked.
        }
    }

    /// <summary>
    /// A time stamped by the relay's clock, moved onto this laptop's (see
    /// RelayClockOffset). Clamped rather than thrown on: a stamp near the
    /// ends of DateTime's range would otherwise fail every poll, and the
    /// tap carrying it would sit unacknowledged on the relay for good,
    /// holding up every tap behind it.
    /// </summary>
    private static DateTime ToLaptopClock(DateTime relayTime, TimeSpan relayAhead)
    {
        var ticks = relayTime.ToUniversalTime().Ticks - relayAhead.Ticks;
        ticks = Math.Clamp(ticks, DateTime.MinValue.Ticks, DateTime.MaxValue.Ticks);
        return new DateTime(ticks, DateTimeKind.Utc);
    }

    private void ForgetClockOffset()
    {
        lock (_clockGate)
        {
            _clockOffsetTicks = 0;
            _haveClockOffset = false;
            _havePendingStep = false;
        }
    }

    /// <summary>How often the laptop asks the relay for taps. A few seconds' wait for a check-in matters to nobody.</summary>
    private static readonly TimeSpan PollEvery = TimeSpan.FromSeconds(4);

    private volatile string? _checkInProblem;

    /// <summary>RelayPinLockedUntilUtc as UTC ticks; zero when not locked.</summary>
    private long _pinLockedUntilTicks;

    /// <summary>
    /// When check-ins are next collected, as Environment.TickCount64
    /// milliseconds; zero for "now". The tick count, not the wall clock,
    /// for the reason given at _nextAttemptTick.
    /// </summary>
    private long _nextPollTick;

    private volatile string? _managerProblem;

    /// <summary>RelayManagerPinLockedUntilUtc as UTC ticks; zero when not locked.</summary>
    private long _managerPinLockedUntilTicks;

    /// <summary>_nextPollTick's twin for the manager tunnel - see the note where it is used in Run().</summary>
    private long _nextManagePollTick;

    private volatile string? _signupProblem;
    private bool _signupsWanted;

    /// <summary>
    /// Why the relay refused the signup DOCUMENT - the list of invite
    /// hashes - or null once it has taken one.
    ///
    /// Kept apart from _lastError on purpose. A relay without a "signup"
    /// channel refuses that one document and takes every other, and the
    /// next other document to land cleared _lastError - so the Club Website page
    /// went back to a clean "Publishing" while every invite handed out
    /// opened a sign-up page that had never heard of its code. This is
    /// cleared only by the relay taking a signup document, or by pointing
    /// at a different relay or club. RefreshStatus shows it on the Online
    /// page's status line, and SignupProblem in the Sign-ups window.
    /// </summary>
    private volatile string? _signupDocumentProblem;

    /// <summary>
    /// How long to leave the sign-up routes alone after the relay answers
    /// them "not found" - a relay from before sign-up, most likely, which
    /// will say the same thing every ten seconds until it is redeployed.
    /// Changing the settings, or switching sign-up on, asks again at once.
    /// </summary>
    private static readonly TimeSpan SignupNotFoundBackoff = TimeSpan.FromMinutes(5);

    /// <summary>
    /// How long an answer the relay keeps failing (not refusing - failing,
    /// with a 5xx or similar) is tried before it is given up on. Long
    /// enough to ride out a relay restart or an evening's bad Wi-Fi; the
    /// desk sends the answer again from its own record if the relay offers
    /// that sign-up again later.
    /// </summary>
    private static readonly TimeSpan SignupAnswerGiveUpAfter = TimeSpan.FromHours(1);

    /// <summary>When each queued answer first failed to post, as Environment.TickCount64. Guarded by _signupGate.</summary>
    private readonly Dictionary<string, long> _signupAnswerFirstFailed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>RelaySignupLockedUntilUtc as UTC ticks; zero when not locked.</summary>
    private long _signupLockedUntilTicks;

    /// <summary>When sign-ups are next collected - a slower cadence than taps: nobody is stood waiting at the relay for one.</summary>
    private long _nextSignupPollTick;

    private static readonly TimeSpan SignupPollEvery = TimeSpan.FromSeconds(10);

    /// <summary>Answers not yet posted, by invite hash. Guarded by _signupGate.</summary>
    private readonly Dictionary<string, string> _signupAnswers = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _signupGate = new();

    /// <summary>
    /// Taps already handed on but not yet acknowledged, by number AND time,
    /// so a tap whose acknowledgement was lost is not applied twice.
    ///
    /// It used to be a high-water mark - "everything up to tap 40 is done"
    /// - which silently skipped every new tap after the relay's queue was
    /// wiped and started counting from one again: tap 1 of the new queue
    /// looked like tap 1 of the old. A number with its time cannot repeat
    /// across a reset. Cleared once the relay confirms it has forgotten
    /// them, which keeps it to a handful of entries.
    /// </summary>
    private readonly HashSet<string> _appliedTaps = new(StringComparer.Ordinal);
    private readonly object _tapGate = new();

    /// <summary>
    /// A manager request already run, by reqId, holding the JSON
    /// HandleJsonAsync answered - so a request seen again because a prior
    /// manage/answer post was lost is not run a second time. Unlike a
    /// check-in tap, an op here can add a driver, publish a result or
    /// change the programme, and running it twice would be a real bug
    /// rather than a harmless re-application.
    ///
    /// Removed once its post to manage/answer succeeds, and pruned of
    /// anything the relay no longer lists as pending - see
    /// CollectManageRequests and PruneManageAnswered. Forgotten whole only
    /// when the relay or club changes, or the club deletes everything it
    /// published - see SettingsChanged and DeletePublishedAsync.
    ///
    /// Each answer carries the order it was filed in, so that when there
    /// are ever too many it is the oldest that goes (see MaxManageAnswered).
    /// </summary>
    private readonly Dictionary<string, (long Order, string Json)> _manageAnswered = new(StringComparer.Ordinal);
    private readonly object _manageGate = new();

    /// <summary>Counts the answers filed in _manageAnswered, so the oldest has the lowest Order. Guarded by _manageGate.</summary>
    private long _manageAnsweredOrder;

    /// <summary>
    /// Every manager request this laptop has run, by reqId, with the time
    /// (this laptop's clock) until which it must be remembered. Guarded by
    /// _manageGate.
    ///
    /// Separate from _manageAnswered on purpose. That cache exists so a
    /// request whose answer was not confirmed is not run twice, and it is
    /// emptied as soon as the answer is posted or the relay stops listing
    /// the request - which is right for an honest relay, but left nothing
    /// that remembered a request had ever run. A relay that put the same
    /// signed request back in the inbox a minute later found no cached
    /// answer, the signature and timestamp still checked out (same bytes,
    /// still inside MaxAge), and the request ran again: a check-in put
    /// back, a driver moved back to where the manager had just moved them
    /// from. The signature is there so the laptop does not have to trust
    /// the relay, so a replay has to be refused here, not left to the relay.
    ///
    /// Kept for twice RelayManagerDevices.MaxAge from the moment it ran: a
    /// request may carry a sentAt up to MaxAge ahead of this laptop's clock
    /// and stays acceptable until MaxAge after that, so nothing older can
    /// pass VerifyPinSigned again and there is no need to remember it.
    /// Keyed by reqId because the reqId is inside the signature - a relay
    /// cannot change it without the request failing verification. Not
    /// forgotten with the answers when the relay, club or published data
    /// changes: remembering a few extra ids for ten minutes costs nothing.
    /// </summary>
    private readonly Dictionary<string, DateTime> _manageRunUntil = new(StringComparer.Ordinal);

    /// <summary>
    /// The most answers _manageAnswered holds. A ceiling, not a working
    /// limit, like MaxQueuedResults: it only ever holds answers to requests
    /// the relay listed as waiting at the last look, and the relay holds at
    /// most 300 of those (MANAGE_QUEUE_MAX_ITEMS in server.ts).
    /// </summary>
    private const int MaxManageAnswered = 500;

    /// <summary>Where to publish and as whom, read from settings once and swapped whole. See ReadTarget.</summary>
    private sealed record RelayTarget(string Root, string Club, string Token)
    {
        public bool Configured => Root.Length > 0 && Club.Length > 0 && Token.Length > 0 && IsValidClubId(Club);
    }

    /// <summary>
    /// The address, club and token as one immutable snapshot, replaced
    /// only by SettingsChanged.
    ///
    /// Read from settings field by field, a send could be built from the
    /// new address and the old token while the Club Website page was halfway
    /// through saving - sending a token to a relay it does not belong to.
    /// One reference, swapped atomically, cannot be caught half-changed.
    /// </summary>
    private volatile RelayTarget _target;

    /// <remarks>
    /// Nothing at all while the address came from a restored backup and has
    /// not been checked on the Club Website page (RestoredSettings): an empty
    /// target is "not configured", so nothing is published, polled or
    /// collected, and the token goes nowhere. The page's Save and Test
    /// Connection confirm it and call SettingsChanged, which reads it again.
    /// </remarks>
    private static RelayTarget ReadTarget(AppSettings settings) =>
        RestoredSettings.IsHeld(RestoredSettings.Part.PublishRelay)
            ? new RelayTarget(string.Empty, string.Empty, string.Empty)
            : new RelayTarget(
                NormaliseBaseUrl(settings.RelayBaseUrl),
                settings.RelayClub.Trim().ToLowerInvariant(),
                settings.RelayToken.Trim());

    /// <summary>
    /// Whether a club id is one the relay could hold: lower-case letters,
    /// digits and single hyphens, three to thirty-two characters, starting
    /// and ending with a letter or digit - the relay's own rule. Anything
    /// else it answers 404, which looked exactly like a club that had been
    /// deleted.
    /// </summary>
    public static bool IsValidClubId(string club) =>
        club.Length is >= 3 and <= 32
        && club.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-')
        && club[0] != '-' && club[^1] != '-'
        && !club.Contains("--", StringComparison.Ordinal);

    /// <summary>Call after the settings change, so a corrected token is tried at once instead of after the backoff.</summary>
    public void SettingsChanged()
    {
        var previous = _target;
        _target = ReadTarget(_settings);

        var sameRelay = string.Equals(previous.Root, _target.Root, StringComparison.OrdinalIgnoreCase);

        // A different relay is a different clock.
        if (!sameRelay)
        {
            ForgetClockOffset();
        }

        lock (_gate)
        {
            _failures = 0;
            _nextAttemptTick = 0;
            _lastError = null;

            // Whatever QuiesceAsync was holding still for is over: the
            // settings it was protecting have now been saved.
            _quiesced = false;
        }

        Interlocked.Increment(ref _generation);

        // Same relay AND same club: the queue this laptop is talking to is
        // the one it was talking to a moment ago, whatever else was saved.
        var sameQueue = sameRelay && string.Equals(previous.Club, _target.Club, StringComparison.Ordinal);

        // A different relay or club numbers its taps from its own start -
        // and ONLY those. The set is what stops a tap the relay has not
        // acked yet from being applied twice, so clearing it on every save
        // - the roster or short-names tick, Test Connection, publishing
        // switched off and back on - meant that after a failed ack the next
        // poll brought the same taps back and ran them a second time. A tap
        // toggles a check-in, so a driver who had just checked in on their
        // phone was quietly checked straight back out, on the sheet the
        // heats are built from. Exactly the fault the manager's answers
        // below were fixed for; this line sat beside them and kept it.
        if (!sameQueue)
        {
            ForgetAppliedTaps();

            // AND NEITHER SWITCH HAS BEEN TOLD ANYTHING. Both flags mean
            // "this relay knows" rather than "this laptop has decided", so
            // a different relay or club has to be told again from scratch -
            // and until it is, it refuses what the switch guards.
            //
            // Listening recovers from this by itself: a line refused with a
            // 403 clears the flag where it is sent. A driver's night goes
            // through the results queue instead and cannot, which is why
            // this line exists rather than being left to heal.
            _listenOpenTold = false;
            _nightsOpenTold = false;
            // No backoff carried over to the new settings (20 Oct 2026, #85).
            _nextNightsOpenTick = 0;
            // Listening's neither (2026-11-05, Bug hunt 11: RelayPublisher) -
            // see _listenOpenBackoffFor.
            _nextListenOpenTick = 0;

            // And the shout-outs switch and the vote's ballot, which never
            // healed at all: the notes inbox answers 200 and empty for a
            // club whose flag is missing, so nothing looked wrong while
            // phones on the club page got "not found" all evening.
            _notesOpenTold = false;
            _ballotAsPublished = null;

            // And a new relay or club is told what is switched OFF, too
            // (17 Oct 2026, Bug Hunt 8, A16-1 / A14-2) - it may be holding
            // an open flag from before this laptop ever spoke to it.
            ArmCloses(_settings);
        }

        _nextPollTick = 0;
        _checkInProblem = null;

        // Said by the old relay or club, about its own PIN.
        Interlocked.Exchange(ref _pinLockedUntilTicks, 0);

        // The manager's answers still waiting to be posted are owed to
        // requests in one club's queue on one relay, so only a different
        // relay or club forgets them. It used to be every save - the roster
        // or short-names tick, Test Connection, publishing switched off for
        // Delete Everything Published and back on - and a request whose
        // answer had just failed to post was then run a second time: the
        // phone adding a driver was told the transponder already belonged
        // to that same driver. A new token keeps them: it names the same
        // club, and the queue is the club's (replacing a leaked token is
        // exactly when posts fail). So does switching the manager or
        // publishing off: the relay keeps a waiting request for ten minutes
        // whatever the switches say, and switched back on inside that time
        // the request would run again. Delete Everything Published forgets
        // them itself - see DeletePublishedAsync.
        if (!sameQueue)
        {
            ForgetManageAnswered();

            // The results window and ledger are NOT cleared here any more.
            // "previous" is only the last target this laptop held, which
            // can be the empty one a restored backup is held back as, or a
            // club id typed wrong and put right a moment later - and either
            // read as a different relay, so confirming a restored backup, or
            // fixing a typo, wiped the window and the next index dropped
            // every earlier race day of the season. The ledger records the
            // relay and club it describes instead, and starts again only
            // once a different one has actually taken a result - see
            // NoteResultDelivered. Until then ResultIsOnRelay lists nothing
            // for a relay that is not the ledger's.
            //
            // A ledger from before that was recorded describes whatever was
            // in use before this save, which the old clearing guaranteed.
            AdoptLedger(previous);
        }

        // A lockout said by the relay or club this laptop may just have
        // left behind means nothing about the one it now points at, and
        // the next poll, straight away, says it again if it still holds.
        _nextManagePollTick = 0;
        _managerProblem = null;
        Interlocked.Exchange(ref _managerPinLockedUntilTicks, 0);

        // Sign-up answers are owed to one club's queue on one relay, like the
        // manager's. The desk sends them again from its own record when that
        // club's sign-ups are next collected, so nothing is lost by this.
        if (!sameQueue)
        {
            lock (_signupGate)
            {
                _signupAnswers.Clear();
                _signupAnswerFirstFailed.Clear();
            }

            // What the old relay or club said about the signup document.
            // Only then: every other save leaves it standing, and the next
            // signup document the relay takes is what clears it - see
            // _signupDocumentProblem.
            _signupDocumentProblem = null;
        }

        _nextSignupPollTick = 0;
        _signupProblem = null;
        Interlocked.Exchange(ref _signupLockedUntilTicks, 0);

        RefreshStatus();
        _wake.Set();
    }

    /// <summary>
    /// Queues a snapshot, replacing any earlier one for the same channel
    /// that has not gone yet.
    /// </summary>
    public void PublishSnapshot(RelayChannel channel, string json)
    {
        // The roster is the one channel that is a list of people's names,
        // some of them children's, and this guard is deliberately here
        // rather than only at the call sites: one refusal in one place is
        // checkable, five scattered ones are not. The relay withholds it
        // too - both ends have to agree before a name is public.
        //
        // Replaced, not dropped. Dropping it left the last roster sent while
        // the box was ticked on the relay for good - every name, class and
        // check-in still served while the laptop said the roster was off.
        // An empty roster in its place takes it down, the way the check-in
        // channel withdraws itself (RelayDocuments.CheckIn).
        if (channel == RelayChannel.Roster && !_settings.RelayPublishRoster)
        {
            json = WithdrawnRoster;
        }

        Enqueue(NameOf(channel), null, json);
    }

    /// <summary>
    /// Throws away a snapshot of this channel that is still waiting to go
    /// (22 Oct 2026, driver experience). For a switched channel whose switch
    /// has just closed: the relay would refuse it with a 403 anyway, and the
    /// copy it last held has been taken down by the close. One already in
    /// flight is not recalled - its refusal is dropped in Send.
    /// </summary>
    public void DropQueuedSnapshot(RelayChannel channel)
    {
        lock (_gate)
        {
            _snapshots.Remove(NameOf(channel));
        }
    }

    /// <summary>
    /// The roster sent while the club has not chosen to publish it: the
    /// same shape as a real one (MainForm.PublishRosterToRelay), with nobody
    /// in it. What replaces a roster published before the box was unticked.
    /// </summary>
    internal const string WithdrawnRoster = "{\"checkedIn\":0,\"total\":0,\"drivers\":[]}";

    /// <summary>
    /// Queues one year of the results index - what is in the results
    /// collection, so a page can navigate years of it without fetching
    /// years of it. See RelayResultsIndex.
    ///
    /// Keyed by the year, so a race night rewrites this year's document
    /// and leaves every other year alone.
    /// </summary>
    public void PublishResultsIndex(string year, string json)
    {
        var id = SafeItemId(year);

        if (id is null)
        {
            return;
        }

        Enqueue(RelayResultsIndex.Channel, id, json);
    }

    /// <summary>Queues one finished session's results. These are never dropped for being superseded.</summary>
    /// <param name="startedUtc">When that session was raced, so the results index knows how far back the relay has been given. See NoteResultsWindow.</param>
    /// <param name="correction">The relay may already hold a copy: corrected since it was saved, or sent before (RelayResultsIndex.MayAlreadyBeOnRelay). See RelayResultsIndex.Ledger.NoteWithheld.</param>
    public void PublishResult(string sessionId, string json, DateTime startedUtc, bool correction = false)
    {
        var id = SafeItemId(sessionId);

        if (id is null)
        {
            return;
        }

        var note = new ResultNote(sessionId, startedUtc, correction);

        // Queued and noted as owed under the ledger's lock, so the worker
        // cannot take it and note it delivered in between - which would
        // leave it owed after it had gone, and sent once more at the next
        // start for nothing. Enqueue only takes _gate inside, and nothing
        // takes _ledgerGate while holding _gate, so this cannot deadlock.
        lock (_ledgerGate)
        {
            NoteResultsWindow(note, Enqueue("results", id, json, note));
        }
    }

    /// <summary>
    /// A queued result, as the results index has to know it once the relay
    /// has answered for it. See NoteResultDelivered and NoteResultRefused.
    /// </summary>
    private sealed record ResultNote(string SessionId, DateTime StartedUtc, bool Correction);

    /// <summary>
    /// Whether the relay was given this session's result, for the results
    /// index: inside the window and not withheld from it, or sent since
    /// from before it. See RelayResultsIndex.Ledger.
    /// </summary>
    internal bool ResultIsOnRelay(string sessionId, DateTime startedUtc)
    {
        var target = _target;

        lock (_ledgerGate)
        {
            // Nothing, to a relay or club the ledger does not describe: it
            // has been given nothing the ledger knows of. Not cleared for
            // that, though - see SettingsChanged. No relay at all (not set
            // up, or a restored one not yet confirmed) publishes nothing,
            // so the ledger is answered as it stands; answering "nothing"
            // made every year's index look changed the moment it was
            // confirmed, and all of them went at once.
            return (!target.Configured || _ledger.BelongsTo(target.Root, target.Club))
                   && _ledger.Listed(sessionId, startedUtc, _settings.RelayResultsFromUtc);
        }
    }

    /// <summary>Whether the relay has not yet taken this session's result as it now stands - queued and not delivered, or a correction that could not be sent. See RelayResultsIndex.Ledger.Owed.</summary>
    internal bool ResultIsOwed(string sessionId)
    {
        lock (_ledgerGate)
        {
            return _ledger.Owed.Contains(sessionId);
        }
    }

    /// <summary>
    /// Records whether this session's result is on its way, for the results
    /// index.
    ///
    /// HERE rather than at the call site, because here is where it is known
    /// whether the session is going anywhere at all. Enqueue drops silently
    /// when publishing is off or the relay is not set up, and the window
    /// was being widened by those too - so a club that raced all season
    /// with publishing off, then turned it on, published the last 36 hours
    /// and an index claiming the whole year. The archive drew a heading for
    /// every past race night, each full of "that result is not on the relay
    /// any more".
    ///
    /// Queued is not sent. A queued result is only noted as owed, on disk,
    /// and the window and ledger move when the relay takes it - see
    /// NoteResultDelivered. They used to move here, so a night of results
    /// queued while the relay was unreachable, then lost with the queue at
    /// shutdown, was recorded as on the relay for good.
    /// </summary>
    private void NoteResultsWindow(ResultNote note, bool queued)
    {
        try
        {
            lock (_ledgerGate)
            {
                var changed = queued
                    ? _ledger.Owed.Add(note.SessionId)
                    : _ledger.NoteWithheld(note.SessionId, note.StartedUtc, _settings.RelayResultsFromUtc, note.Correction);

                if (changed)
                {
                    _ledger.Save();
                }
            }
        }
        catch
        {
            // Like Enqueue: nothing here may throw into the race path. The
            // ledger's own write reports itself through SafeFile.
        }
    }

    /// <summary>
    /// The relay has taken a result - or holds a newer copy of it, which
    /// is the same for the index. Posted from the worker to the settings
    /// thread (OnSettingsThread), as it moves the window.
    ///
    /// The window opens at the first result taken and is never moved after
    /// that; what does not fit it is kept in the ledger instead. It used to
    /// be widened back to any older session re-sent, which claimed every
    /// session in between, and nothing recorded one withheld inside it -
    /// publishing unticked for a few weeks and ticked again put those weeks
    /// in the archive with no results behind them.
    ///
    /// Nothing clears the window but a DIFFERENT relay or club taking a
    /// result, or the club's published data being deleted (see
    /// DeletePublishedAsync). That is decided here, by what actually took
    /// the result, and not when settings are saved: a restored backup not
    /// yet confirmed, or a club id typed wrong for a moment, never takes
    /// anything - see SettingsChanged.
    /// </summary>
    /// <param name="target">The relay and club the result was sent to.</param>
    private void NoteResultDelivered(ResultNote note, RelayTarget target)
    {
        try
        {
            lock (_ledgerGate)
            {
                var from = _settings.RelayResultsFromUtc;
                bool changed;

                if (!_ledger.BelongsTo(target.Root, target.Club))
                {
                    // A different relay, or a different club on it, has been
                    // given nothing else by this laptop. The results index
                    // must say so, or it would promise the new relay a
                    // history that only the old one ever held.
                    _ledger.MoveTo(target.Root, target.Club);
                    from = null;
                    changed = true;
                }
                else
                {
                    changed = _ledger.Adopt(target.Root, target.Club);
                }

                var now = _ledger.NoteSent(note.SessionId, note.StartedUtc, from, out var noted);
                changed |= noted;
                changed |= KeepOwedWhileAnotherCopyIsQueued(note);

                if (now != _settings.RelayResultsFromUtc)
                {
                    _settings.RelayResultsFromUtc = now;
                    _settings.Save();
                }

                if (changed)
                {
                    _ledger.Save();
                }
            }
        }
        catch
        {
            // Nothing here may throw into the UI thread it is posted to. The
            // ledger's own write reports itself through SafeFile; one not
            // written stays owed, and is sent again at the next start.
        }
    }

    /// <summary>
    /// The relay refused a result outright (400 or 413) - it will never
    /// take it as it is, so it is no longer owed, and the index treats it
    /// as one that could not be queued. Posted from the worker, as
    /// NoteResultDelivered is.
    /// </summary>
    private void NoteResultRefused(ResultNote note)
    {
        try
        {
            lock (_ledgerGate)
            {
                // NoteWithheld puts a correction back in Owed - the relay
                // still has the uncorrected copy - as it does when one is
                // too big to queue at all.
                var changed = _ledger.Owed.Remove(note.SessionId);
                changed |= _ledger.NoteWithheld(note.SessionId, note.StartedUtc, _settings.RelayResultsFromUtc, note.Correction);
                changed |= KeepOwedWhileAnotherCopyIsQueued(note);

                if (changed)
                {
                    _ledger.Save();
                }
            }
        }
        catch
        {
            // As NoteResultDelivered.
        }
    }

    /// <summary>
    /// Puts a session back in Owed when a different copy of its result -
    /// queued by a later PublishResult, so not this note's own copy or its
    /// re-numbered reissue - is still waiting in the queue. Call under
    /// _ledgerGate; takes _gate inside, the order PublishResult takes them.
    ///
    /// WHY (2026-11-05, Bug hunt 11: RelayPublisher): a correction made
    /// while the first copy sat at the head of a stalled queue queues behind
    /// it (ReplaceQueuedResult never touches the head). When the link came
    /// back long enough for the OLD copy to go, its delivery cleared Owed
    /// and saved the ledger, though the correction was still only in
    /// memory. Closed before that went, and opened more than 36 hours later,
    /// nothing re-sent it and the relay kept the uncorrected result for
    /// good - the very loss Owed exists to prevent.
    /// </summary>
    /// <returns>Whether Owed changed.</returns>
    private bool KeepOwedWhileAnotherCopyIsQueued(ResultNote note)
    {
        bool queued;

        lock (_gate)
        {
            queued = _results.Any(one => one.Result is { } other
                                         && !ReferenceEquals(other, note)
                                         && string.Equals(other.SessionId, note.SessionId, StringComparison.Ordinal));
        }

        return queued && _ledger.Owed.Add(note.SessionId);
    }

    /// <summary>
    /// Runs a note from the worker on the thread settings belong to - see
    /// _settingsThread. Posted, never waited for: the worker must not stall
    /// on a UI thread that is busy. Never throws.
    /// </summary>
    private void OnSettingsThread(Action note)
    {
        if (_settingsThread is null)
        {
            note();
            return;
        }

        try
        {
            _settingsThread.Post(_ => note(), null);
        }
        catch
        {
            // The window has gone - shutting down. Not noted, so the result
            // stays owed and is sent again at the next start; nothing lost.
        }
    }

    /// <summary>
    /// Records which relay and club the ledger describes, when it has never
    /// said - a ledger from before it recorded that. Does nothing for a
    /// target that is not set up. Never throws.
    /// </summary>
    private void AdoptLedger(RelayTarget target)
    {
        if (!target.Configured)
        {
            return;
        }

        try
        {
            lock (_ledgerGate)
            {
                if (_ledger.Adopt(target.Root, target.Club))
                {
                    _ledger.Save();
                }
            }
        }
        catch
        {
            // Adopted in memory, written with the ledger's next change.
        }
    }

    /// <summary>Clears the ledger with the window it qualifies. Never throws.</summary>
    private void ForgetLedger()
    {
        try
        {
            lock (_ledgerGate)
            {
                if (_ledger.Clear())
                {
                    _ledger.Save();
                }
            }
        }
        catch
        {
            // As ForgetResultsWindow: cleared in memory, written next time.
        }
    }

    /// <summary>
    /// Checks an address, club and token without publishing anything.
    ///
    /// Takes its arguments rather than reading settings, so the Test
    /// button can check what is typed in the boxes before it is saved.
    /// </summary>
    public async Task<RelayProbe> TestAsync(string baseUrl, string club, string token, CancellationToken cancel = default)
    {
        var root = NormaliseBaseUrl(baseUrl);

        if (root.Length == 0)
        {
            return new RelayProbe(false, "Enter the relay's address first, for example https://your-relay.fly.dev");
        }

        // Lower case, as the relay keeps it. "Acme-RC" typed in the box is
        // plainly meant to be acme-rc, and failing it would be pedantry.
        club = club.Trim().ToLowerInvariant();

        if (club.Length == 0 || token.Trim().Length == 0)
        {
            return new RelayProbe(false, "A club id and a publish token are both needed.");
        }

        if (!IsValidClubId(club))
        {
            return new RelayProbe(false, InvalidClubMessage);
        }

        if (!IsSafeForToken(root))
        {
            return new RelayProbe(false, UnsafeAddressMessage);
        }

        try
        {
            using var health = await _http.GetAsync($"{root}/healthz", cancel).ConfigureAwait(false);

            if (!health.IsSuccessStatusCode)
            {
                return new RelayProbe(false, $"Reached {root}, but it does not look like a relay (HTTP {(int)health.StatusCode}).");
            }
        }
        catch (Exception ex)
        {
            return new RelayProbe(false, $"Could not reach {root}. {Plain(ex)}");
        }

        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{root}/api/v1/c/{Uri.EscapeDataString(club)}/whoami");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());

            using var response = await SendAsync(request, cancel).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false);

            return (int)response.StatusCode switch
            {
                200 => new RelayProbe(true, $"Connected as {ClubNameFrom(body, club)}."),
                401 => new RelayProbe(false, "The relay does not recognize that token."),
                403 when IsPaused(body) => new RelayProbe(false, PausedMessage),
                403 => new RelayProbe(false, "That token belongs to a different club. Check the club id."),
                404 => new RelayProbe(false, NotFound(body, club)),
                var other => new RelayProbe(false, $"The relay answered HTTP {other}."),
            };
        }
        catch (Exception ex)
        {
            return new RelayProbe(false, $"Could not check the token. {Plain(ex)}");
        }
    }

    /// <summary>Erases everything this club has published. Irreversible on the relay's side.</summary>
    public async Task<RelayProbe> DeletePublishedAsync(CancellationToken cancel = default)
    {
        var target = _target;
        var root = target.Root;
        var club = target.Club;

        if (root.Length == 0 || club.Length == 0)
        {
            return new RelayProbe(false, "Nothing to delete: no relay address or club is set.");
        }

        if (!IsSafeForToken(root))
        {
            return new RelayProbe(false, UnsafeAddressMessage);
        }

        // The thread that called - the UI thread, from the Club Website page -
        // captured before the first await, which leaves it. Settings belong
        // to that thread: saving them from the thread pool below, while the
        // page might be changing them, could write a half-changed file.
        var caller = SynchronizationContext.Current;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, $"{root}/api/v1/c/{Uri.EscapeDataString(club)}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);

            using var response = await SendAsync(request, cancel).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new RelayProbe(false, $"The relay refused the delete (HTTP {(int)response.StatusCode}).");
            }
        }
        catch (Exception ex)
        {
            return new RelayProbe(false, $"Could not reach the relay. {Plain(ex)}");
        }

        // Deleted. Nothing from here on is about reaching the relay, so it
        // is outside the try above: a fault in this bookkeeping used to be
        // reported as "Could not reach the relay" for a delete that had
        // worked.

        // Anything still queued would put it straight back.
        lock (_gate)
        {
            _snapshots.Clear();
            _results.Clear();
        }

        // The inbox went with everything else, and starts counting
        // again from one.
        ForgetAppliedTaps();

        // So did the manager's queue, and with it every request an
        // answer was still waiting to go back for: none is owed any
        // more. Forgotten here, not by publishing being switched off
        // for the delete - SettingsChanged keeps them for anything
        // short of a different relay or club.
        ForgetManageAnswered();

        // And every switch it had been told about went with it: the
        // shout-outs flag, the vote's ballot, listening and drivers'
        // nights. Left marked as told, none of them was ever sent again
        // until a restart, so the club page said shout-outs were closed
        // while this laptop still thought they were open.
        _notesOpenTold = false;
        _ballotAsPublished = null;
        _listenOpenTold = false;
        _nightsOpenTold = false;
        // No backoff carried over from before the delete (20 Oct 2026, #85).
        _nextNightsOpenTick = 0;
        // Listening's neither (2026-11-05, Bug hunt 11: RelayPublisher).
        _nextListenOpenTick = 0;

        // And the results the index was describing. Left set, the
        // next index published would name a season of sessions
        // that were deleted a moment ago. Done on the caller's thread
        // (see above), posted there before this returns - so it is in
        // place before anything the caller does next.
        if (caller is not null)
        {
            caller.Post(_ => ForgetResultsWindow(), null);
        }
        else
        {
            ForgetResultsWindow();
        }

        RefreshStatus();
        return new RelayProbe(true, $"Everything published for \"{club}\" has been deleted.");
    }

    /// <summary>Clears RelayResultsFromUtc after a delete. Never throws: the delete it follows has already worked.</summary>
    private void ForgetResultsWindow()
    {
        try
        {
            _settings.RelayResultsFromUtc = null;
            _settings.Save();
        }
        catch
        {
            // The file couldn't be written. The window is cleared in memory,
            // and the next save of settings writes it.
        }

        ForgetLedger();
    }

    /// <summary>
    /// Stops the worker mid-send and waits until it has stopped, before
    /// something that must not be undone by a send already on its way - the
    /// delete above.
    ///
    /// Switching publishing off stops new documents being queued, but not
    /// the one already travelling, nor the rest of that pass's snapshots:
    /// a live frame that reached the relay just after the delete put the
    /// live page straight back. This cancels the request in flight, lets
    /// nothing new start, and returns once the worker is between passes.
    ///
    /// Nothing is sent again until the next SettingsChanged - which is
    /// what switching publishing back on calls. The generation is not moved
    /// here: that happens when publishing is switched back on, and moving
    /// it while off would only make callers rebuild documents that are
    /// then dropped. Waits at most ten seconds; a worker stuck longer than
    /// that is past the HTTP timeout and will find nothing to send.
    /// </summary>
    public async Task QuiesceAsync()
    {
        CancellationTokenSource? pass;
        CancellationTokenSource? uploads;

        lock (_gate)
        {
            _quiesced = true;
            pass = _pass;

            // An upload on its way - a driver's clip - is stopped too (22 Oct
            // 2026) - see RelayPublisher.Uploads.cs, _uploadStop. Never
            // disposed: an upload may still be linking to it.
            uploads = _uploadStop;
            _uploadStop = null;
        }

        try
        {
            uploads?.Cancel();
        }
        catch (AggregateException)
        {
            // A callback threw; the upload is cancelled all the same.
        }

        try
        {
            pass?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The pass finished and was put away between the read above
            // and here. Nothing left to stop.
        }
        catch (AggregateException)
        {
            // A cancellation callback threw. The request is cancelled all
            // the same; the wait below still applies.
        }

        await Task.Run(() => _idle.Wait(TimeSpan.FromSeconds(10))).ConfigureAwait(false);
    }

    public void Dispose()
    {
        try
        {
            _stopping.Cancel();
            _wake.Set();
            _worker.Join(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Shutting down. Nothing here is worth delaying an exit for.
        }

        // Not disposed if the worker may still be running - it sets this
        // on its way out of a pass, and a QuiesceAsync racing the exit may
        // be waiting on it. An event left to the collector costs nothing.
        // 17 Oct 2026, Bug Hunt 8, A32-2: the same for the client, the wake
        // event and the token source - a worker still inside a check-in
        // handler when the two seconds ran out used them after they were
        // freed. It is a background thread; the process exit takes them.
        if (!_worker.IsAlive)
        {
            _http.Dispose();
            _wake.Dispose();
            _stopping.Dispose();
            _idle.Dispose();
        }
    }

    // ---------------------------------------------------------------- queue

    /// <param name="Reissued">Already sent once with a fresh seq after the relay said it held a newer one. See Drain.</param>
    /// <param name="Result">A session's result, for the results index once the relay has answered for it; null for anything else. See PublishResult.</param>
    private sealed record Pending(string Channel, string? ItemId, string Json, long Seq, bool Reissued = false, ResultNote? Result = null);

    /// <param name="result">For a session's result, what the worker notes for the results index once the relay answers. See Pending.</param>
    /// <returns>Whether it was queued - false when it was dropped, which PublishResult has to know. See NoteResultsWindow.</returns>
    private bool Enqueue(string channel, string? itemId, string json, ResultNote? result = null)
    {
        try
        {
            if (!_settings.RelayEnabled || !IsConfigured() || string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            if (Encoding.UTF8.GetByteCount(json) > MaxPayloadBytes)
            {
                lock (_gate)
                {
                    _dropped++;
                    _lastError = $"A {channel} payload was too large to publish.";
                }

                RefreshStatus();
                return false;
            }

            lock (_gate)
            {
                var item = new Pending(channel, itemId, json, NextSeq(), Result: result);

                if (itemId is null)
                {
                    // Latest wins. This single line IS the drop policy for
                    // live state: an un-sent frame is replaced, never
                    // queued behind.
                    _snapshots[channel] = item;
                }
                else if (!ReplaceQueuedResult(item))
                {
                    if (_results.Count >= MaxQueuedResults)
                    {
                        DropOneQueuedResult();
                        _dropped++;
                    }

                    _results.Enqueue(item);
                }
            }

            _wake.Set();
            return true;
        }
        catch
        {
            // A publisher that can throw into the race path is a publisher
            // that will eventually stop a race. There is nothing here
            // worth propagating.
            return false;
        }
    }

    /// <summary>
    /// Puts a newer copy of a document in the place of one already waiting
    /// under the same channel and id, and says whether it did. Call under
    /// _gate.
    ///
    /// WITHOUT THIS THE QUEUE HELD EVERY COPY. A driver's night is queued
    /// again after every race they run and the results index after every
    /// session, so an evening offline filled the queue with stale copies of
    /// the same few documents - and the drop-oldest rule then pushed real
    /// results out to make room for them. Only the newest copy is worth
    /// sending: the relay keeps the latest by seq anyway.
    ///
    /// The head is left alone, because the worker may be sending it right
    /// now and recognises it by reference (CompleteResult). A newer copy of
    /// the head simply queues behind it, as before.
    /// </summary>
    private bool ReplaceQueuedResult(Pending item)
    {
        if (_results.Count < 2)
        {
            return false;
        }

        var held = _results.ToArray();

        for (var i = 1; i < held.Length; i++)
        {
            if (string.Equals(held[i].Channel, item.Channel, StringComparison.Ordinal)
                && string.Equals(held[i].ItemId, item.ItemId, StringComparison.Ordinal))
            {
                held[i] = item;

                _results.Clear();

                foreach (var one in held)
                {
                    _results.Enqueue(one);
                }

                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Makes room in a full results queue. Call under _gate.
    ///
    /// A driver's night or a results index goes first, oldest of them
    /// first: both are built again from the facts (a night when the
    /// driver next races, the index after the next session), and a result
    /// is what a club actually needs the relay for. Only a queue holding
    /// nothing else loses its oldest result, which is what it always did.
    /// The head is never picked out of the middle like this, because the
    /// worker may be sending it - see <see cref="ReplaceQueuedResult"/>.
    /// </summary>
    private void DropOneQueuedResult()
    {
        var held = _results.ToList();

        for (var i = 1; i < held.Count; i++)
        {
            if (held[i].Channel is DriverNight.Channel or RelayResultsIndex.Channel)
            {
                held.RemoveAt(i);

                _results.Clear();

                foreach (var one in held)
                {
                    _results.Enqueue(one);
                }

                return;
            }
        }

        _results.Dequeue();
    }

    /// <summary>
    /// Milliseconds since the epoch, which the relay uses only to decide
    /// which of two frames is newer.
    ///
    /// A counter starting at 1 would be wrong: after a restart it would
    /// begin below what the relay already holds, and every publish would
    /// be answered "already have a newer one" until it caught up. The
    /// clock keeps rising across restarts. The Max guards the one case it
    /// does not - a clock moved backwards - by refusing to go back.
    /// </summary>
    private long NextSeq()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _lastSeq = Math.Max(now, _lastSeq + 1);
        return _lastSeq;
    }

    private bool HasWork()
    {
        lock (_gate)
        {
            return _results.Count > 0 || _snapshots.Count > 0;
        }
    }

    private Pending? PeekResult()
    {
        lock (_gate)
        {
            return _results.Count > 0 ? _results.Peek() : null;
        }
    }

    /// <summary>Swaps the head of the results queue for a re-numbered copy, keeping its place at the front. Call under _gate.</summary>
    private void ReplaceHeadResult(Pending old, Pending fresh)
    {
        if (_results.Count == 0 || !ReferenceEquals(_results.Peek(), old))
        {
            return;
        }

        var rest = _results.Skip(1).ToList();
        _results.Clear();
        _results.Enqueue(fresh);

        foreach (var item in rest)
        {
            _results.Enqueue(item);
        }
    }

    private void CompleteResult(Pending item)
    {
        lock (_gate)
        {
            if (_results.Count > 0 && ReferenceEquals(_results.Peek(), item))
            {
                _results.Dequeue();
            }
        }
    }

    private List<Pending> TakeSnapshots()
    {
        lock (_gate)
        {
            var taken = _snapshots.Values.ToList();
            _snapshots.Clear();
            return taken;
        }
    }

    private void ReturnSnapshot(Pending item)
    {
        lock (_gate)
        {
            // Only if nothing newer arrived while it was in flight. A
            // failed send must not resurrect a frame that has already been
            // superseded.
            if (!_snapshots.ContainsKey(item.Channel))
            {
                _snapshots[item.Channel] = item;
            }
        }
    }

    // --------------------------------------------------------------- worker

    private void Run()
    {
        var token = _stopping.Token;

        while (!token.IsCancellationRequested)
        {
            try
            {
                var backingOff = BackoffRemaining();

                _wake.Wait(backingOff > TimeSpan.Zero ? backingOff : TimeSpan.FromMilliseconds(500), token);
                _wake.Reset();

                if (token.IsCancellationRequested)
                {
                    break;
                }

                if (!_settings.RelayEnabled || !IsConfigured())
                {
                    RefreshStatus();
                    continue;
                }

                // Woken early - a new frame was queued - while still
                // backing off. The frame waits with the rest; see
                // _nextAttemptTick for what ignoring this used to cost.
                if (BackoffRemaining() > TimeSpan.Zero)
                {
                    RefreshStatus();
                    continue;
                }

                // Each pass under a token of its own, so QuiesceAsync can
                // stop this pass without stopping the worker. Started under
                // the same lock QuiesceAsync takes, so a pass either exists
                // for it to cancel or does not start at all.
                CancellationTokenSource? pass = null;

                lock (_gate)
                {
                    if (!_quiesced)
                    {
                        pass = CancellationTokenSource.CreateLinkedTokenSource(token);
                        _pass = pass;
                        _idle.Reset();
                    }
                }

                if (pass is null)
                {
                    RefreshStatus();
                    continue;
                }

                try
                {
                    // WHY (20 Oct 2026, Bug Hunt 8, #85): nights queued
                    // before the relay was told the club offers them were
                    // sent first, refused with a 403 and dropped for good.
                    // The relay is now told first. The call further down
                    // stays; once told, it is just a check of a flag.
                    if (_settings.PublishDriverNightsToTheRelay)
                    {
                        SendNightsOpen(pass.Token);
                    }

                    // The announcer's lines before anything else in the pass,
                    // and again between the documents Drain sends - see
                    // SendListenLinesWaiting for why.
                    SendListenLinesWaiting(pass.Token);

                    Drain(pass.Token);

                    if (_settings.RelayCheckInEnabled && Environment.TickCount64 >= _nextPollTick)
                    {
                        _nextPollTick = Environment.TickCount64 + (long)PollEvery.TotalMilliseconds;
                        CollectCheckIns(pass.Token);
                    }
                    else if (!_settings.RelayCheckInEnabled)
                    {
                        _checkInProblem = null;
                        Interlocked.Exchange(ref _pinLockedUntilTicks, 0);
                    }

                    // A tick of its own rather than sharing check-in's: the
                    // manager can be on while check-in is off (or the other
                    // way round), and each should keep its own four-second
                    // cadence rather than borrowing a countdown the other
                    // feature keeps resetting.
                    if (_settings.ManagerViaRelay && Environment.TickCount64 >= _nextManagePollTick)
                    {
                        _nextManagePollTick = Environment.TickCount64 + (long)PollEvery.TotalMilliseconds;
                        CollectManageRequests(pass.Token);
                    }
                    else if (!_settings.ManagerViaRelay)
                    {
                        _managerProblem = null;
                        Interlocked.Exchange(ref _managerPinLockedUntilTicks, 0);
                    }

                    // Shout-outs for the announcer, on a slow tick of its
                    // own - see RelayPublisher.Notes. Slow because a
                    // dedication is not urgent and the relay holds it
                    // safely; and only while the club wants them, because
                    // the inbox is the one route that costs a request per
                    // poll with nothing to show for it on almost every
                    // club.
                    if (_settings.ClubNotesFromTheRelay && Environment.TickCount64 >= _nextNotesPollTick)
                    {
                        _nextNotesPollTick = Environment.TickCount64 + (long)NotesPollEvery.TotalMilliseconds;
                        CollectClubNotes(pass.Token);
                    }
                    else if (!_settings.ClubNotesFromTheRelay)
                    {
                        // Switched off: the relay told, until it has taken
                        // it - see CloseClubNotesOnTheRelay. On the notes'
                        // own tick, so a relay that is down is asked twice
                        // a minute rather than twice a second.
                        if (_notesClosePending && Environment.TickCount64 >= _nextNotesPollTick)
                        {
                            _nextNotesPollTick = Environment.TickCount64 + (long)NotesPollEvery.TotalMilliseconds;
                            SendNotesClose(pass.Token);
                        }
                        else if (!_notesClosePending)
                        {
                            ForgetNotesTrouble();
                        }
                    }

                    // The announcer's voice, out to anyone listening from
                    // home - see RelayPublisher.Listen. On NO tick of its
                    // own: a line that arrives late is worse than a line
                    // that never arrives, so it goes on the pass it was
                    // offered on, woken by OfferToListeners. The cost of
                    // having no tick is paid in that file instead, by the
                    // open flag's own backoff and by the queue being
                    // allowed to throw work away.
                    if (_settings.ListenAlongOnTheRelay)
                    {
                        Listening.Wanted = true;
                        SendListenLines(pass.Token);
                    }
                    else
                    {
                        if (Listening.Wanted)
                        {
                            // Unticked while running. This is the queue
                            // letting go of what it was holding, so a box
                            // unticked mid-race does not send three more
                            // lines after it.
                            Listening.Wanted = false;
                            ForgetListenTrouble();
                        }

                        // And the relay told, until it has taken it - see
                        // CloseListeningOnTheRelay.
                        if (_listenClosePending)
                        {
                            SendListenClose(pass.Token);
                        }
                    }

                    // A driver's own night - see RelayPublisher.Nights. On
                    // NO tick of its own either, but for the opposite
                    // reason to listening: the documents themselves go
                    // into the results queue as each race is filed, so all
                    // this pass does is make sure the relay has been TOLD
                    // the club offers them. It refuses a night to a club it
                    // has not been told about, and a 403 per driver per
                    // race - discovered after a night of racing - is a far
                    // worse way to find that out than one request at the
                    // start. Cheap after the first: the flag goes once and
                    // then this is a field comparison.
                    if (_settings.PublishDriverNightsToTheRelay)
                    {
                        SendNightsOpen(pass.Token);
                    }
                    else
                    {
                        // Until the relay takes it - see
                        // CloseDriverNightsOnTheRelay. This is what takes
                        // a club's drivers' evenings back DOWN rather than
                        // merely stopping new ones.
                        //
                        // The trouble forgotten only once nothing is
                        // pending (22 Oct 2026, bug hunt), as notes and the
                        // vote do: forgotten every pass, a close that kept
                        // failing was logged again every 30 seconds all night.
                        if (_nightsClosePending)
                        {
                            SendNightsClose(pass.Token);
                        }
                        else
                        {
                            ForgetNightsTrouble();
                        }
                    }

                    // The room's vote, on a faster tick than the notes -
                    // see RelayPublisher.Vote. Faster because the number
                    // the race director watches to decide when to close
                    // the vote has to be moving, and the window is only a
                    // few minutes long.
                    if (_settings.RoomVoteFromTheRelay && Environment.TickCount64 >= _nextVotePollTick)
                    {
                        _nextVotePollTick = Environment.TickCount64 + (long)VotePollEvery.TotalMilliseconds;
                        CollectRoomVotes(pass.Token);
                    }
                    else if (!_settings.RoomVoteFromTheRelay)
                    {
                        // Switched off: the relay told, until it has taken
                        // it - see CloseRoomVoteOnTheRelay. On the vote's
                        // own tick, for the same reason as the notes above.
                        if (_voteClosePending && Environment.TickCount64 >= _nextVotePollTick)
                        {
                            _nextVotePollTick = Environment.TickCount64 + (long)VotePollEvery.TotalMilliseconds;
                            SendVoteClose(pass.Token);
                        }
                        else if (!_voteClosePending)
                        {
                            ForgetVoteTrouble();
                        }
                    }

                    // Driver sign-up on its own, slower tick. Answers owed
                    // to phones go even when sign-up has since been
                    // switched off - the relay is holding those sign-ups -
                    // but the inbox is only read while it is on (see
                    // CollectSignups), and an answer is not owed forever
                    // (SignupAnswerGiveUpAfter), so a club that has
                    // switched sign-up off stops asking altogether.
                    if ((SignupsWanted || HasSignupAnswers()) && Environment.TickCount64 >= _nextSignupPollTick)
                    {
                        _nextSignupPollTick = Environment.TickCount64 + (long)SignupPollEvery.TotalMilliseconds;
                        CollectSignups(pass.Token);
                    }
                    else if (!SignupsWanted && !HasSignupAnswers())
                    {
                        _signupProblem = null;
                        Interlocked.Exchange(ref _signupLockedUntilTicks, 0);
                    }

                    // The drivers' own requests (22 Oct 2026, driver
                    // experience) - see RelayPublisher.Requests. Its own
                    // tick and its own close, like sign-up above.
                    CollectDriverAsksPass(pass.Token);

                    // What phones saw of the sponsors on Live (5 Nov 2026) -
                    // see RelayPublisher.Sponsors. Its own three-minute tick.
                    CollectSponsorViewsPass(pass.Token);
                }
                finally
                {
                    lock (_gate)
                    {
                        _pass = null;
                    }

                    pass.Dispose();
                    _idle.Set();
                }

                RefreshStatus();

                // Only one queued result goes per pass, so if more are
                // waiting, come straight back rather than idling out the
                // half-second timeout between each one - but only while
                // things are going through. After a failure this used to
                // set the wake straight back up, and the wait it was
                // meant to be sitting out returned at once, every time:
                // a tight loop of requests at a relay that was down.
                if (BackoffRemaining() <= TimeSpan.Zero && HasWork())
                {
                    _wake.Set();
                }
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                // A pass stopped by QuiesceAsync, not the publisher shutting
                // down. Not a failure: nothing went wrong with the relay.
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // The worker thread must outlive any single failure.
                lock (_gate)
                {
                    _lastError = Plain(ex);
                }

                Failed();
                RefreshStatus();
            }
        }
    }

    /// <summary>
    /// One pass: at most one queued result, then every pending snapshot.
    ///
    /// The result goes first because a finished session matters more than
    /// a live frame, but only ONE of them, so that draining a backlog of
    /// twenty results does not freeze the live page for twenty round
    /// trips.
    /// </summary>
    private void Drain(CancellationToken token)
    {
        var result = PeekResult();

        if (result is not null)
        {
            // Where it is going, for the results index (NoteResultDelivered):
            // read here, as Send reads it, so a save landing mid-send is at
            // worst noted against the relay the save just left - the one
            // Send most likely used.
            var target = _target;

            switch (Send(result, token, out var heldSeq))
            {
                case SendOutcome.Sent:
                    CompleteResult(result);
                    Succeeded();

                    // Only now is it on the relay - see NoteResultsWindow.
                    if (result.Result is { } delivered)
                    {
                        OnSettingsThread(() => NoteResultDelivered(delivered, target));
                    }

                    break;

                case SendOutcome.Superseded:
                    Succeeded();

                    // The relay holds this session's result - a newer copy
                    // of it - so for the index it is there either way.
                    if (result.Result is { } held)
                    {
                        OnSettingsThread(() => NoteResultDelivered(held, target));
                    }

                    // Sent again under a number above the relay's, once. A
                    // result the relay says it has "a newer copy of" is a
                    // result from a clock that has since gone backwards, or
                    // from another laptop publishing as this club - and
                    // either way the heat that just finished HERE is the
                    // one that should be on the page.
                    lock (_gate)
                    {
                        _lastSeq = Math.Max(_lastSeq, heldSeq);

                        if (result.Reissued)
                        {
                            if (_results.Count > 0 && ReferenceEquals(_results.Peek(), result))
                            {
                                _results.Dequeue();
                            }
                        }
                        else
                        {
                            ReplaceHeadResult(result, result with { Seq = NextSeq(), Reissued = true });
                        }
                    }

                    break;

                case SendOutcome.Rejected:
                    // The relay will never accept this one, so leaving it
                    // at the head of the queue would block every result
                    // behind it for the rest of the day.
                    CompleteResult(result);
                    lock (_gate)
                    {
                        _dropped++;
                    }

                    if (result.Result is { } refused)
                    {
                        OnSettingsThread(() => NoteResultRefused(refused));
                    }

                    break;

                case SendOutcome.Retry:
                    // Cancelled is not failed: a pass stopped by QuiesceAsync
                    // or by shutting down says nothing about the relay, and
                    // must not start a backoff or mark it offline.
                    if (!token.IsCancellationRequested)
                    {
                        Failed();
                    }

                    return;
            }

            // A result can be a big upload on a club's Wi-Fi. What the
            // announcer said meanwhile goes before the snapshots - see
            // SendListenLinesWaiting.
            SendListenLinesWaiting(token);
        }

        var taken = TakeSnapshots();

        for (var i = 0; i < taken.Count; i++)
        {
            var snapshot = taken[i];

            if (token.IsCancellationRequested)
            {
                // This one and every one after it, not just this one: the
                // rest of the list was taken off the queue too, and would
                // otherwise be lost rather than sent next pass.
                for (var j = i; j < taken.Count; j++)
                {
                    ReturnSnapshot(taken[j]);
                }

                return;
            }

            switch (Send(snapshot, token, out var heldSnapshotSeq))
            {
                case SendOutcome.Sent:
                    Succeeded();
                    break;

                case SendOutcome.Superseded:
                    Succeeded();

                    // As for a result above, but only if nothing newer has
                    // been queued meanwhile - that one will carry the
                    // raised number anyway.
                    lock (_gate)
                    {
                        _lastSeq = Math.Max(_lastSeq, heldSnapshotSeq);

                        if (!snapshot.Reissued && !_snapshots.ContainsKey(snapshot.Channel))
                        {
                            _snapshots[snapshot.Channel] = snapshot with { Seq = NextSeq(), Reissued = true };
                        }
                    }

                    break;

                case SendOutcome.Rejected:
                    // Dropped on purpose: a snapshot the relay refuses is
                    // one a newer frame will replace in a second anyway.
                    break;

                case SendOutcome.Retry:
                    // As above, and the snapshots after this one go back
                    // too - they have not been tried.
                    for (var j = i; j < taken.Count; j++)
                    {
                        ReturnSnapshot(taken[j]);
                    }

                    if (!token.IsCancellationRequested)
                    {
                        Failed();
                    }

                    return;
            }

            // Between one snapshot and the next - the badges' artwork alone
            // can be most of a megabyte. See SendListenLinesWaiting.
            SendListenLinesWaiting(token);
        }
    }

    /// <summary>
    /// Fetches the taps waiting on the relay, hands each one on, and only
    /// then tells the relay it can forget them - so a laptop that crashes
    /// half-way sees the rest again next time rather than losing them.
    ///
    /// Failures here are reported through CheckInProblem and nothing else:
    /// they must not disturb publishing, which has its own backoff, and a
    /// relay that is merely older than this app (no check-in endpoints
    /// yet) is not a reason to call the whole connection broken.
    /// </summary>
    private void CollectCheckIns(CancellationToken token)
    {
        // Nobody to hand taps to yet - the window is still starting up.
        // Collecting now would acknowledge taps that then go nowhere, and
        // an acknowledged tap is one the relay forgets.
        if (CheckInReceived is null)
        {
            return;
        }

        // One snapshot for the whole exchange, so the list and its
        // acknowledgement go to the same relay as the same club.
        var target = _target;
        var club = Uri.EscapeDataString(target.Club);

        if (!IsSafeForToken(target.Root))
        {
            _checkInProblem = UnsafeAddressMessage;
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{target.Root}/api/v1/c/{club}/inbox");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);

            // Measured on this very answer, before any tap in it is read, so
            // even the first taps after starting up are on the right clock.
            var sentUtc = DateTime.UtcNow;
            using var response = Send(request, token);
            SampleClock(response, sentUtc, DateTime.UtcNow);

            if (!response.IsSuccessStatusCode)
            {
                _checkInProblem = (int)response.StatusCode == 404
                    ? "The relay answered \"not found\" for check-ins. Check the club id on the Club Website page matches the relay's; "
                        + "if it does, the relay predates online check-in - redeploy it (fly deploy --ha=false)."
                    : $"Could not collect check-ins: the relay answered HTTP {(int)response.StatusCode}.";
                return;
            }

            var text = response.Content.ReadAsStringAsync(token).GetAwaiter().GetResult();
            var taps = new List<(long N, string Key, RelayCheckInTap Tap)>();

            // Relay time to laptop time - see RelayClockOffset. Read once,
            // so every tap in the batch is moved by the same amount and
            // keeps its order against the others.
            var relayAhead = RelayClockOffset;

            using (var doc = JsonDocument.Parse(text))
            {
                if (doc.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        // ValueKind before TryGetInt64: that one THROWS on
                        // anything that is not a number, rather than
                        // answering false like its name suggests. Every
                        // other field here is checked that way; this one was
                        // not, so a relay (or anything able to answer as
                        // one) sending "n": null took the whole collection
                        // down with an InvalidOperationException, and with
                        // it every check-in still waiting in the batch.
                        if (item.TryGetProperty("n", out var n) && n.ValueKind == JsonValueKind.Number
                            && n.TryGetInt64(out var number)
                            && item.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String
                            && item.TryGetProperty("wanted", out var wanted)
                            && wanted.ValueKind is JsonValueKind.True or JsonValueKind.False)
                        {
                            var hasWhen = item.TryGetProperty("atUtc", out var when) && when.ValueKind == JsonValueKind.String;

                            // Stamped by the relay's clock; handed on on this
                            // laptop's, so it can be compared with a change
                            // made at the desk. No stamp means "now", which
                            // is already the laptop's own.
                            var at = hasWhen && when.TryGetDateTime(out var parsed)
                                ? ToLaptopClock(parsed, relayAhead)
                                : DateTime.UtcNow;

                            // The relay's own text for the time, not the
                            // parsed value: it is what makes the key the
                            // same on every poll.
                            var key = $"{number}|{(hasWhen ? when.GetString() : string.Empty)}";

                            taps.Add((number, key, new RelayCheckInTap(id.GetString()!, wanted.GetBoolean(), at)));
                        }
                    }
                }

                // Present only while PIN sign-in is locked, so its absence
                // is what clears it. Read tolerantly: a relay older than
                // this never sends it, and anything unreadable counts as
                // not locked rather than failing the whole collection.
                // Moved onto this laptop's clock like the taps' times.
                var lockedTicks = 0L;

                if (doc.RootElement.TryGetProperty("pinLockedUntil", out var locked)
                    && locked.ValueKind == JsonValueKind.String
                    && locked.TryGetDateTime(out var lockedUntil))
                {
                    lockedTicks = ToLaptopClock(lockedUntil, relayAhead).Ticks;
                }

                Interlocked.Exchange(ref _pinLockedUntilTicks, lockedTicks);
            }

            _checkInProblem = null;

            if (taps.Count == 0)
            {
                return;
            }

            // Everything up to here has been applied, in order, with no gap.
            // Only this far is acknowledged: a tap that could not be applied
            // - the window closing, a handler that threw - stays on the
            // relay, and so does everything after it, to be tried again on
            // the next poll rather than forgotten.
            long appliedUpTo = 0;

            foreach (var (number, key, tap) in taps.OrderBy(t => t.N))
            {
                bool seen;

                lock (_tapGate)
                {
                    seen = _appliedTaps.Contains(key);
                }

                if (!seen)
                {
                    bool handled;

                    try
                    {
                        handled = CheckInReceived?.Invoke(tap) == true;
                    }
                    catch
                    {
                        handled = false;
                    }

                    if (!handled)
                    {
                        break;
                    }

                    lock (_tapGate)
                    {
                        _appliedTaps.Add(key);
                    }
                }

                appliedUpTo = number;
            }

            if (appliedUpTo <= 0)
            {
                return;
            }

            using var ack = new HttpRequestMessage(HttpMethod.Post, $"{target.Root}/api/v1/c/{club}/inbox/ack");
            ack.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);
            ack.Content = new StringContent(JsonSerializer.Serialize(new { upTo = appliedUpTo }), Encoding.UTF8, "application/json");

            using var acked = Send(ack, token);

            if (acked.IsSuccessStatusCode)
            {
                // The relay has forgotten them, so there is nothing left to
                // recognise. Anything after appliedUpTo was never added.
                ForgetAppliedTaps();
            }
            else
            {
                // Not fatal: the same taps come back next time and the set
                // above stops them being applied twice. But somebody
                // watching the Club Website page should know it is happening.
                _checkInProblem = $"Could not confirm check-ins with the relay (HTTP {(int)acked.StatusCode}). They will be collected again.";
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _checkInProblem = $"Could not collect check-ins. {Plain(ex)}";
        }
    }

    private void ForgetAppliedTaps()
    {
        lock (_tapGate)
        {
            _appliedTaps.Clear();
        }
    }

    private bool HasSignupAnswers()
    {
        lock (_signupGate)
        {
            return _signupAnswers.Count > 0;
        }
    }

    /// <summary>
    /// Posts every answer the desk has given since the last pass, then
    /// fetches the sign-ups still waiting and hands the whole list on.
    ///
    /// An answer is forgotten here only once the relay has taken it, so an
    /// answer made while the relay was out of reach goes when it is back.
    /// A sign-up whose answer is still queued is left out of the list
    /// handed on, so the desk never sees one it has already dealt with
    /// reappear while the answer is on its way.
    ///
    /// Failures are reported through SignupProblem and nothing else, as for
    /// check-ins and the manager.
    ///
    /// Each answer on its own: one the relay won't take no longer holds up
    /// the others, or the look at the inbox after them. One the relay says
    /// it no longer has (404, 410) is dropped at once; one that keeps
    /// failing otherwise is dropped after SignupAnswerGiveUpAfter, with a
    /// line in the console.
    /// </summary>
    private void CollectSignups(CancellationToken token)
    {
        var target = _target;
        var club = Uri.EscapeDataString(target.Club);

        if (!IsSafeForToken(target.Root))
        {
            _signupProblem = UnsafeAddressMessage;
            return;
        }

        try
        {
            List<KeyValuePair<string, string>> answers;

            lock (_signupGate)
            {
                answers = _signupAnswers.ToList();
            }

            // What went wrong posting answers this pass, if anything - kept
            // to report once the inbox has been read, which would otherwise
            // clear it.
            string? answerProblem = null;

            foreach (var (hash, answerJson) in answers)
            {
                token.ThrowIfCancellationRequested();

                using var post = new HttpRequestMessage(HttpMethod.Post, $"{target.Root}/api/v1/c/{club}/signup/answer");
                post.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);

                // Built whole on the UI thread, hash and all (see
                // RelaySignupDesk.Answer); never looked inside here.
                post.Content = new StringContent(answerJson, Encoding.UTF8, "application/json");

                HttpResponseMessage posted;

                try
                {
                    posted = Send(post, token);
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !token.IsCancellationRequested)
                {
                    // The relay could not be reached at all, so neither can
                    // it be for the answers after this one, or the inbox.
                    // Counted against this answer's hour all the same.
                    NoteSignupAnswerFailed(hash, answerJson, "it could not be reached");
                    _signupProblem = $"Could not send a sign-up answer. {Plain(ex)} It will be sent again.";
                    return;
                }

                using (posted)
                {
                    var code = (int)posted.StatusCode;

                    if (posted.IsSuccessStatusCode || code == 400)
                    {
                        // 400 is the relay refusing this answer outright;
                        // sending it again every ten seconds would change
                        // nothing.
                        ForgetSignupAnswer(hash, answerJson);
                    }
                    else if (code is 404 or 410)
                    {
                        // The relay no longer has this sign-up - or has no
                        // sign-up at all, being older than it. Either way
                        // this answer can never land, and the desk sends it
                        // again from its own record if the relay ever
                        // offers the sign-up again. (A relay from before
                        // sign-up is caught by the inbox's own 404 below,
                        // which is what backs the polling off.)
                        ForgetSignupAnswer(hash, answerJson);
                        Log?.Invoke($"[signup] Dropped a sign-up answer the relay couldn't take (HTTP {code}) - it no longer has that sign-up.");
                    }
                    else
                    {
                        // On to the next answer: one the relay is choking
                        // on must not hold up every answer behind it.
                        NoteSignupAnswerFailed(hash, answerJson, $"HTTP {code}");
                        answerProblem = $"Could not send a sign-up answer: the relay answered HTTP {code}. It will be sent again.";
                    }
                }
            }

            if (!SignupsWanted || SignupsCollected is null)
            {
                _signupProblem = answerProblem;
                return;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, $"{target.Root}/api/v1/c/{club}/signup/inbox");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);

            using var response = Send(request, token);

            if (!response.IsSuccessStatusCode)
            {
                if ((int)response.StatusCode == 404)
                {
                    // Asked again in minutes, not seconds: see
                    // SignupNotFoundBackoff.
                    _signupProblem = SignupNotFoundMessage;
                    _nextSignupPollTick = Environment.TickCount64 + (long)SignupNotFoundBackoff.TotalMilliseconds;
                }
                else
                {
                    _signupProblem = $"Could not collect sign-ups: the relay answered HTTP {(int)response.StatusCode}.";
                }

                return;
            }

            var text = response.Content.ReadAsStringAsync(token).GetAwaiter().GetResult();
            var found = new List<RelaySignupRequest>();
            var lockedTicks = 0L;

            using (var doc = JsonDocument.Parse(text))
            {
                if (doc.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        if (ReadSignup(item) is { } signup)
                        {
                            found.Add(signup);
                        }
                    }
                }

                if (doc.RootElement.TryGetProperty("pinLockedUntil", out var locked)
                    && locked.ValueKind == JsonValueKind.String
                    && locked.TryGetDateTime(out var lockedUntil))
                {
                    lockedTicks = ToLaptopClock(lockedUntil, RelayClockOffset).Ticks;
                }
            }

            Interlocked.Exchange(ref _signupLockedUntilTicks, lockedTicks);
            _signupProblem = answerProblem;

            lock (_signupGate)
            {
                found.RemoveAll(s => _signupAnswers.ContainsKey(s.Hash));
            }

            SignupsCollected?.Invoke(found);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _signupProblem = $"Could not collect sign-ups. {Plain(ex)}";
        }
    }

    /// <summary>Said when the relay answers "not found" for anything to do with sign-up.</summary>
    private const string SignupNotFoundMessage =
        "The relay answered \"not found\" for driver sign-up. Check the club id on the Club Website page matches the relay's; "
        + "if it does, the relay predates driver sign-up - redeploy it (fly deploy --ha=false).";

    /// <summary>Takes an answer off the queue - unless a newer one for the same sign-up has replaced it meanwhile.</summary>
    private void ForgetSignupAnswer(string hash, string answerJson)
    {
        lock (_signupGate)
        {
            if (_signupAnswers.TryGetValue(hash, out var current) && current == answerJson)
            {
                _signupAnswers.Remove(hash);
                _signupAnswerFirstFailed.Remove(hash);
            }
        }
    }

    /// <summary>Counts a failed post against an answer, and gives it up once it has failed for SignupAnswerGiveUpAfter.</summary>
    private void NoteSignupAnswerFailed(string hash, string answerJson, string why)
    {
        var now = Environment.TickCount64;
        var giveUp = false;

        lock (_signupGate)
        {
            if (!_signupAnswers.TryGetValue(hash, out var current) || current != answerJson)
            {
                return;
            }

            if (!_signupAnswerFirstFailed.TryGetValue(hash, out var first))
            {
                _signupAnswerFirstFailed[hash] = now;
            }
            else if (now - first >= (long)SignupAnswerGiveUpAfter.TotalMilliseconds)
            {
                _signupAnswers.Remove(hash);
                _signupAnswerFirstFailed.Remove(hash);
                giveUp = true;
            }
        }

        if (giveUp)
        {
            Log?.Invoke($"[signup] Gave up sending a sign-up answer after an hour of trying ({why}). "
                + "If that sign-up is still waiting on the relay, it will be answered again from this laptop's record.");
        }
    }

    /// <summary>One sign-up from the relay's inbox, or null for anything not shaped like one.</summary>
    private RelaySignupRequest? ReadSignup(JsonElement item)
    {
        if (item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty("h", out var h) || h.ValueKind != JsonValueKind.String
            || !item.TryGetProperty("form", out var form) || form.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        string Text(string name, int max)
        {
            var value = form.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String ? prop.GetString() ?? string.Empty : string.Empty;

            // One plain line, whatever came in. The relay cleans these too,
            // but this laptop does not take that on trust: a name with a
            // line break in it forged extra "[signup]" lines in the console,
            // and a right-to-left override made a name read differently in
            // the Sign-ups list from what went into the profile, the emails
            // and every export.
            value = OneLine(value);
            return value.Length > max ? value[..max] : value;
        }

        var hash = h.GetString() ?? string.Empty;

        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
        {
            return null;
        }

        var kind = Text("transponderKind", 10);

        if (kind is not ("own" or "rental" or "unknown"))
        {
            kind = "unknown";
        }

        var submitted = item.TryGetProperty("submittedAt", out var at) && at.ValueKind == JsonValueKind.String && at.TryGetDateTime(out var when)
            ? ToLaptopClock(when, RelayClockOffset)
            : DateTime.UtcNow;

        // The relay's own text for it, untouched: the converted time above
        // moves a little with every clock sample, so it cannot say whether
        // this is the same sign-up as last time. See SubmittedAtRaw.
        var submittedRaw = at.ValueKind == JsonValueKind.String ? at.GetString() ?? string.Empty : string.Empty;
        if (submittedRaw.Length > 40)
        {
            submittedRaw = submittedRaw[..40];
        }

        return new RelaySignupRequest(
            hash.ToLowerInvariant(),
            Text("name", 60),
            Text("className", 60),
            kind,
            kind == "own" ? Text("transponder", 20) : string.Empty,
            Text("car", 60),
            Text("email", 254),
            form.TryGetProperty("guardian", out var guardian) && guardian.ValueKind == JsonValueKind.True,
            submitted)
        {
            SubmittedAtRaw = submittedRaw,
        };
    }

    /// <summary>
    /// Text from the relay as one trimmed line: control characters, line
    /// and paragraph breaks and invisible formatting characters (bidi
    /// overrides, zero-width marks) each become a space, and runs of space
    /// become one.
    /// </summary>
    private static string OneLine(string value)
    {
        var text = new StringBuilder(value.Length);
        var pendingSpace = false;

        foreach (var c in value)
        {
            var category = char.GetUnicodeCategory(c);

            if (char.IsWhiteSpace(c) || char.IsControl(c)
                || category is System.Globalization.UnicodeCategory.Format
                    or System.Globalization.UnicodeCategory.LineSeparator
                    or System.Globalization.UnicodeCategory.ParagraphSeparator)
            {
                pendingSpace = text.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                text.Append(' ');
                pendingSpace = false;
            }

            text.Append(c);
        }

        return text.ToString();
    }

    /// <summary>
    /// Fetches requests a phone's signed-in race-day manager has made
    /// through the relay, runs each once and posts the answer back - so
    /// its manage/response poll finds it and moves on to whatever it asks
    /// next.
    ///
    /// Deliberately not CollectCheckIns's shape: check-in's inbox/ack acks
    /// one ordered run of taps with a single call, which fits taps that
    /// are naturally idempotent and must apply in order. A manager
    /// request is neither - the relay's manage/answer removes one reqId
    /// at a time (see answerManage in server.ts), nothing about one
    /// manager's ask depends on another's, and running one twice (add a
    /// driver, publish a result) is a real bug rather than a harmless
    /// repeat. So every pending request here is answered on its own, and
    /// one failing is no reason to hold up the rest.
    ///
    /// Failures are reported through ManagerProblem and nothing else -
    /// same reasoning as CheckInProblem: they must not disturb publishing
    /// or check-in, which have their own backoff and their own status.
    /// </summary>
    private void CollectManageRequests(CancellationToken token)
    {
        // Nobody to hand requests to yet - the window is still starting
        // up. Collecting now would fetch a request with no MainForm to
        // run it against, and it would sit un-acked until the next poll
        // anyway once a handler is subscribed.
        if (ManageRequestReceived is null)
        {
            return;
        }

        // One snapshot for the whole exchange, so the fetch and every
        // answer it produces go to the same relay as the same club.
        var target = _target;
        var club = Uri.EscapeDataString(target.Club);

        if (!IsSafeForToken(target.Root))
        {
            _managerProblem = UnsafeAddressMessage;
            return;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{target.Root}/api/v1/c/{club}/manage/inbox");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);

            using var response = Send(request, token);

            if (!response.IsSuccessStatusCode)
            {
                _managerProblem = (int)response.StatusCode == 404
                    ? "The relay answered \"not found\" for the race-day manager. Check the club id on the Club Website page matches the "
                        + "relay's; if it does, the relay predates the manager over the relay - redeploy it (fly deploy --ha=false)."
                    : $"Could not collect manager requests: the relay answered HTTP {(int)response.StatusCode}.";
                return;
            }

            var text = response.Content.ReadAsStringAsync(token).GetAwaiter().GetResult();
            var pending = new List<ManageInboxItem>();
            var lockedTicks = 0L;

            using (var doc = JsonDocument.Parse(text))
            {
                if (doc.RootElement.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in items.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Object
                            && item.TryGetProperty("reqId", out var reqIdProp) && reqIdProp.ValueKind == JsonValueKind.String
                            && item.TryGetProperty("body", out var bodyProp))
                        {
                            var reqId = reqIdProp.GetString();

                            if (!string.IsNullOrEmpty(reqId))
                            {
                                // The raw JSON text of "body", not a re-serialised
                                // copy: it is already exactly what HandleJsonAsync
                                // expects ({op, args?, confirm?}), the same shape
                                // the phone posted to manage/request in the first
                                // place, and the relay never looks inside it either.
                                //
                                // "payload" is the phone's OWN text of that same
                                // thing, the bytes it signed, which the relay
                                // carries through untouched. Where there is a
                                // signature it is the payload that runs and this
                                // body is not read at all - see RunnableBody.
                                pending.Add(new ManageInboxItem(
                                    reqId,
                                    bodyProp.GetRawText(),
                                    StringOf(item, "payload"),
                                    StringOf(item, "deviceId"),
                                    StringOf(item, "sentAt"),
                                    StringOf(item, "mac")));
                            }
                        }
                    }
                }

                // Present only while manager PIN sign-in is locked, so its
                // absence is what clears it - see readManageInbox in server.ts,
                // the same convention as check-in's own pinLockedUntil.
                if (doc.RootElement.TryGetProperty("pinLockedUntil", out var locked)
                    && locked.ValueKind == JsonValueKind.String
                    && locked.TryGetDateTime(out var lockedUntil))
                {
                    lockedTicks = ToLaptopClock(lockedUntil, RelayClockOffset).Ticks;
                }
            }

            Interlocked.Exchange(ref _managerPinLockedUntilTicks, lockedTicks);
            _managerProblem = null;

            if (pending.Count == 0)
            {
                // Nothing waiting: nothing should still be cached either,
                // or an answer whose post kept failing would sit in memory
                // for good once the phone gave up asking about it.
                PruneManageAnswered(Array.Empty<string>());
                return;
            }

            var seenReqIds = new List<string>(pending.Count);

            foreach (var waiting in pending)
            {
                var reqId = waiting.ReqId;

                if (token.IsCancellationRequested)
                {
                    // Stopped part-way - QuiesceAsync, or closing - so left
                    // without the prune below. It used to break out and
                    // prune, which took the requests not reached yet for
                    // ones the relay had dropped and forgot the answers
                    // still owed to them. Harmless while every settings save
                    // forgot every answer anyway; now they outlive the save
                    // that ends a quiesce, and those requests would run twice.
                    return;
                }

                seenReqIds.Add(reqId);

                string? answerJson = null;

                lock (_manageGate)
                {
                    if (_manageAnswered.TryGetValue(reqId, out var cached))
                    {
                        answerJson = cached.Json;
                    }
                }

                // What to run, and whether to run anything at all. Checked
                // only when there is no answer already: a request answered
                // on an earlier poll has had this done to it once and must
                // not be judged - or run - a second time.
                string? bodyJson = null;

                if (answerJson is null && AlreadyRan(reqId))
                {
                    // Run once already and its answer posted, yet listed
                    // again: an honest relay never does that while it still
                    // holds the answer, so this is a replay. Refused rather
                    // than skipped, and cached like any refusal, so the
                    // phone (if anyone is waiting) is told and it is judged
                    // once rather than at every poll.
                    answerJson = JsonSerializer.Serialize(new
                    {
                        ok = false,
                        error = "That request has already been run once, so it has not been run again. Reload to see where things stand.",
                    });
                    Log?.Invoke("[manage] A request that had already been run was offered again and refused.");
                    RememberManageAnswer(reqId, answerJson);
                }

                if (answerJson is null)
                {
                    bodyJson = RunnableBody(waiting, target.Club, out var refusal);

                    if (bodyJson is null)
                    {
                        // Refused before anything ran, and answered rather
                        // than dropped: a manager watching their phone is
                        // told why. Cached like any other answer, so a
                        // forged request is judged once rather than on
                        // every poll for the ten minutes the relay keeps it.
                        answerJson = JsonSerializer.Serialize(new { ok = false, error = refusal });
                        RememberManageAnswer(reqId, answerJson);
                    }
                }

                if (answerJson is null)
                {
                    try
                    {
                        answerJson = ManageRequestReceived?.Invoke(bodyJson!, token);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        // HandleJsonAsync itself is exception-safe (see its
                        // own RunSafely) and never returns null, so this
                        // only fires for something outside it going wrong -
                        // still answered, so a manager staring at their
                        // phone is told something rather than left at
                        // "..." until their own client-side timeout gives up.
                        answerJson = JsonSerializer.Serialize(new
                        {
                            ok = false,
                            error = $"The laptop hit a problem doing that: {Plain(ex)}",
                        });
                    }

                    if (answerJson is null)
                    {
                        // The event lost its last subscriber between the
                        // null check above and here. Leave it for next
                        // time rather than posting nothing back.
                        continue;
                    }

                    // Recorded only once the handler has come back with an
                    // answer, the same point the answer cache is filled: a
                    // request stopped by cancellation, or left because the
                    // handler went away, is still run next time, as before.
                    RememberManageRun(reqId);
                    RememberManageAnswer(reqId, answerJson);
                }

                if (PostManageAnswer(target, club, reqId, answerJson, token))
                {
                    lock (_manageGate)
                    {
                        _manageAnswered.Remove(reqId);
                    }
                }
                else
                {
                    // Not fatal: the same request comes back next poll and
                    // the cache above stops it running twice.
                    _managerProblem = "Could not confirm an answer with the relay. It will be posted again.";
                }
            }

            PruneManageAnswered(seenReqIds);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _managerProblem = $"Could not collect manager requests. {Plain(ex)}";
        }
    }

    /// <summary>
    /// One request as the relay offers it, with the phone's proof beside it.
    ///
    /// Payload, DeviceId, SentAt and Mac are empty on anything posted by a
    /// phone that did not sign - a manager page from before PIN signing, or a
    /// request the relay made up - which is why they are read as optional
    /// here and judged, and refused, in RunnableBody.
    /// </summary>
    private readonly record struct ManageInboxItem(
        string ReqId,
        string BodyJson,
        string Payload,
        string DeviceId,
        string SentAt,
        string Mac);

    /// <summary>One string property of an inbox item, or empty - a property that is absent, null or not a string is simply not there.</summary>
    private static string StringOf(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    /// <summary>
    /// The JSON to hand to the race-day manager, or null with a reason when
    /// this request is not going to be run.
    ///
    /// This is the whole of the check that the relay did not make a request
    /// up. Every request must be signed with the key the manager PIN makes
    /// (RelayManagerDevices.VerifyPinSigned) - the phone makes it when the
    /// manager signs in with the PIN, this laptop from its own copy. Signed,
    /// the SIGNED payload runs and the relay's body is not read at all: the
    /// relay rebuilds the body it forwards, so a proof over text that is not
    /// the text being executed would prove nothing. Unsigned or wrongly
    /// signed, it is refused with something a manager can act on.
    ///
    /// Until 26 Sep this took the relay's word unless a phone had been
    /// enrolled by QR code at the desk. The QR enrolment is gone - the
    /// manager signs in with the PIN alone - and so is taking the relay's
    /// word: there is always a PIN to check against now.
    /// </summary>
    private string? RunnableBody(ManageInboxItem item, string club, out string refusal)
    {
        refusal = string.Empty;

        // Read once: the Club Website and Local Screens pages change these on
        // the UI thread, and this runs on the publisher's own.
        var pin = _settings.ManagerPin.Trim();
        var salt = _settings.RelayPinSalt;

        // Nothing to check a signature against - no usable PIN, or no salt
        // published yet - is nothing that can be run. The relay itself will
        // not sign anybody in without a published PIN, so this is a relay
        // acting on its own, or a moment between a PIN being cleared and the
        // relay hearing about it.
        if (!RaceDayService.IsUsableManagerPin(pin) || !PinKdf.IsSalt(salt))
        {
            refusal = "The race-day manager has no usable PIN on this laptop right now, so nothing sent through the relay is run.";
            Log?.Invoke($"[manage] A request was refused before running: {refusal}");
            return null;
        }

        if (!RelayManagerDevices.VerifyPinSigned(
                pin,
                salt,
                club.Trim().ToLowerInvariant(),
                item.DeviceId,
                item.ReqId,
                item.SentAt,
                item.Payload,
                item.Mac,
                DateTime.UtcNow,
                out refusal))
        {
            // Said in the log as well as to the phone. A run of these is
            // being told something about the relay - or about a phone still
            // signed in from before the PIN changed - and the phone's own
            // toast is gone in four seconds.
            Log?.Invoke($"[manage] A request was refused before running: {refusal}");
            return null;
        }

        // Shape checked here rather than left to the manager: the payload
        // is text the relay handed over, and a signature only says the
        // phone wrote it, not that it wrote something sensible.
        try
        {
            using var parsed = JsonDocument.Parse(item.Payload);

            if (parsed.RootElement.ValueKind != JsonValueKind.Object
                || !parsed.RootElement.TryGetProperty("op", out var op)
                || op.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(op.GetString()))
            {
                refusal = "That request was signed but did not say what to do, so it has not been run.";
                return null;
            }
        }
        catch (JsonException)
        {
            refusal = "That request was signed but could not be read, so it has not been run.";
            return null;
        }

        // The SIGNED payload, never the relay's own copy of the body: the
        // relay rebuilds what it forwards, and a proof over text that is not
        // the text being executed would prove nothing.
        return item.Payload;
    }

    /// <summary>
    /// Files an answer against its reqId, so a request whose answer was
    /// posted but not confirmed is never run a second time.
    ///
    /// Room is made by forgetting the OLDEST answer, never all of them. It
    /// used to Clear() the lot once there were more than 500: every answer
    /// still waiting to be posted was forgotten at once, and the next poll
    /// ran each of those requests a second time - a second driver added, a
    /// second race day - across many requests together. Out of reach while
    /// the relay holds at most 300 (see MaxManageAnswered), but that number
    /// is the relay's, not this file's. Oldest first, as RaceDayService
    /// forgets its confirmations: the oldest answer is the likeliest to
    /// have been collected, or to have aged out of the relay's ten-minute
    /// window, already.
    /// </summary>
    private void RememberManageAnswer(string reqId, string answerJson)
    {
        lock (_manageGate)
        {
            while (_manageAnswered.Count >= MaxManageAnswered && !_manageAnswered.ContainsKey(reqId))
            {
                var oldest = _manageAnswered.MinBy(pair => pair.Value.Order).Key;
                _manageAnswered.Remove(oldest);
            }

            _manageAnswered[reqId] = (++_manageAnsweredOrder, answerJson);
        }
    }

    /// <summary>Whether a request with this reqId has been run within the last 2 x MaxAge. See _manageRunUntil.</summary>
    private bool AlreadyRan(string reqId)
    {
        lock (_manageGate)
        {
            return _manageRunUntil.TryGetValue(reqId, out var until) && until > DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Notes that a request has run, and forgets any that are now too old to
    /// pass VerifyPinSigned again. Never capped by count: dropping one still
    /// inside its window would reopen the replay this guards against, and
    /// the relay's own queue limit (MANAGE_QUEUE_MAX_ITEMS) and the time
    /// limit keep it small.
    /// </summary>
    private void RememberManageRun(string reqId)
    {
        var now = DateTime.UtcNow;
        Dictionary<string, DateTime> snapshot;

        lock (_manageGate)
        {
            foreach (var key in _manageRunUntil.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToList())
            {
                _manageRunUntil.Remove(key);
            }

            _manageRunUntil[reqId] = now + RelayManagerDevices.MaxAge + RelayManagerDevices.MaxAge;
            snapshot = new Dictionary<string, DateTime>(_manageRunUntil, StringComparer.Ordinal);
        }

        // On disk too, outside the lock (17 Oct 2026, Bug Hunt 8, A16-2):
        // a request run just before a crash or a failed answer must not
        // run again after the restart while the relay still lists it.
        ManageRunLedger.Save(ManageRunLedger.DefaultPath, snapshot);
    }

    /// <summary>Posts one answer back to manage/answer. True once the relay has accepted it.</summary>
    private bool PostManageAnswer(RelayTarget target, string club, string reqId, string answerJson, CancellationToken token, bool replacing = false)
    {
        // "response" carries the raw JSON HandleJsonAsync returned, spliced
        // in as a value rather than a string: the relay hands it straight
        // back to the phone's manage/response poll exactly as posted (see
        // manageAnswerPoll in server.ts), and HandleJsonAsync's return is
        // already valid JSON text - that is its contract as an HTTP
        // response body on the Wi-Fi manage page today. "reqId" alone goes
        // through JsonSerializer, which is all it needs: a plain string.
        var payload = $"{{\"reqId\":{JsonSerializer.Serialize(reqId)},\"response\":{answerJson}}}";

        using var post = new HttpRequestMessage(HttpMethod.Post, $"{target.Root}/api/v1/c/{club}/manage/answer");
        post.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);
        post.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var posted = Send(post, token);

        if ((int)posted.StatusCode == 413 && !replacing)
        {
            // Bigger than the relay carries (256 KB - a search matching
            // hundreds of drivers, say). Posted again it is refused again,
            // every poll for ten minutes, and the phone waits for an answer
            // that can never come. So the phone is told that instead: the
            // op has already run, whatever it did, and must not run again.
            return PostManageAnswer(target, club, reqId, TooBigAnswer, token, replacing: true);
        }

        return posted.IsSuccessStatusCode;
    }

    /// <summary>What a phone is answered with when the laptop's real answer is too big for the relay to carry. See PostManageAnswer.</summary>
    private static readonly string TooBigAnswer = JsonSerializer.Serialize(new
    {
        ok = false,
        error = "The laptop's answer was too big to send through the relay. Reload to see where things stand; "
                + "for a list, ask for less (search for a name).",
    });

    /// <summary>Drops any cached answer for a reqId the relay no longer lists as pending - it has either been collected already or aged out of the relay's own window.</summary>
    private void PruneManageAnswered(IReadOnlyCollection<string> stillPending)
    {
        lock (_manageGate)
        {
            if (_manageAnswered.Count == 0)
            {
                return;
            }

            foreach (var key in _manageAnswered.Keys.Where(k => !stillPending.Contains(k)).ToList())
            {
                _manageAnswered.Remove(key);
            }
        }
    }

    private void ForgetManageAnswered()
    {
        lock (_manageGate)
        {
            _manageAnswered.Clear();
        }
    }

    private enum SendOutcome
    {
        Sent,

        /// <summary>A problem that may clear on its own: the network, the relay being down, a bad token the operator is about to fix.</summary>
        Retry,

        /// <summary>The relay will never accept this document. Retrying cannot help.</summary>
        Rejected,

        /// <summary>Delivered, but the relay kept a copy it says is newer (applied:false) - see Drain.</summary>
        Superseded,
    }

    /// <param name="heldSeq">With Superseded, the seq the relay says it already holds.</param>
    private SendOutcome Send(Pending item, CancellationToken token, out long heldSeq)
    {
        heldSeq = 0;

        // Read once: the address, club and token for this request all come
        // from the same saved set. See _target.
        var target = _target;
        var root = target.Root;
        var club = Uri.EscapeDataString(target.Club);

        // Held, not dropped: the queue drains the moment the address is
        // corrected to https, and nothing is lost in the meantime.
        if (!IsSafeForToken(root))
        {
            lock (_gate)
            {
                _lastError = UnsafeAddressMessage;
            }

            return SendOutcome.Retry;
        }

        var path = item.ItemId is null
            ? $"{root}/api/v1/c/{club}/publish/{item.Channel}?seq={item.Seq}"
            : $"{root}/api/v1/c/{club}/publish/{item.Channel}/{item.ItemId}?seq={item.Seq}";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", target.Token);
            request.Content = new StringContent(item.Json, Encoding.UTF8, "application/json");

            // Synchronous on a thread that exists to block. No
            // SynchronizationContext here, so this cannot deadlock.
            var sentUtc = DateTime.UtcNow;
            using var response = Send(request, token);
            SampleClock(response, sentUtc, DateTime.UtcNow);

            var code = (int)response.StatusCode;
            var body = ReadBodyQuietly(response, token);

            // The signup document is watched on its own - see
            // _signupDocumentProblem.
            var isSignup = item.ItemId is null && item.Channel == NameOf(RelayChannel.Signup);

            if (response.IsSuccessStatusCode)
            {
                if (isSignup)
                {
                    // Taken (or already held in a newer copy, which is a
                    // relay that has the channel all the same).
                    _signupDocumentProblem = null;
                }

                // {"ok":true,"applied":false,"seq":N} means the relay kept
                // what it had, because its copy carries a higher number.
                // Ignored, that was a result the page never showed and a
                // "Publishing" status that said all was well.
                if (IsSupersededBy(body, item.Seq, out var held))
                {
                    heldSeq = held;
                    return SendOutcome.Superseded;
                }

                return SendOutcome.Sent;
            }

            // A 404 is only final when the relay says it has no such
            // CHANNEL - this build publishing something that relay does not
            // hold. "No such club" is what a relay says while its tenant
            // list is being fixed, or for a club id with a typo about to be
            // corrected, and "No such endpoint" is an older relay about to
            // be redeployed; dropping a finished heat's results for either
            // lost them for good.
            var unknownChannel = code == 404 && body.Contains("No channel called", StringComparison.OrdinalIgnoreCase);

            // THE DRIVERS' LIVE PAGES ARE SWITCHED, like a night (22 Oct
            // 2026, driver experience): the relay refuses driverlive with a
            // 403 while drivers' nights are closed there, and driverme while
            // driver requests are. Either refusal means the relay has not
            // been told what this laptop believes - a relay redeployed onto
            // an empty volume, or a snapshot that went out a moment before
            // the switch did. Dropped, not retried: retried, it started the
            // publisher's backoff and held every snapshot behind it, results
            // queue included. A newer copy follows within seconds anyway.
            var driverLiveRefused = code is 401 or 403 && item.ItemId is null
                && item.Channel == NameOf(RelayChannel.DriverLive);
            var driverMeRefused = (code is 401 or 403 && item.ItemId is null
                && item.Channel == NameOf(RelayChannel.DriverMe))

                // A driver's garage goes with driverme's switch (5 Nov
                // 2026) and is a collection item, so it must never sit at
                // the head of the results queue either.
                || (code is 401 or 403 && item.Channel == DriverGarage.Channel);

            // And not called "a different club": a 403 on a switched channel
            // is a switch not told, and the token is fine. A 401 is still the
            // token, and says so.
            var switchedRefused = code == 403 && !IsPaused(body)
                && (driverLiveRefused || driverMeRefused || item.Channel == DriverNight.Channel);

            lock (_gate)
            {
                _lastError = switchedRefused ? _lastError : code switch
                {
                    401 => "The relay does not recognize this token.",
                    403 when IsPaused(body) => PausedMessage,
                    403 => "This token belongs to a different club.",
                    404 when unknownChannel => $"The relay has no \"{item.Channel}\" channel - it may need updating.",
                    404 when body.Contains("endpoint", StringComparison.OrdinalIgnoreCase)
                        => "The relay is an older build that cannot take this. Redeploy it.",
                    404 => $"The relay has no club called \"{target.Club}\". Check the club id.",
                    413 => "The relay refused the payload as too large.",
                    507 => $"The relay is out of room for \"{item.Channel}\" (HTTP 507).",
                    _ => $"The relay answered HTTP {code}.",
                };
            }

            // 401 and 403 are retried on purpose: they are what a
            // mistyped token looks like, and the queue should drain the
            // moment it is corrected rather than having been thrown away.
            //
            // A 507 for a year of the results index is final too: the relay
            // refuses an older year rather than evict a newer one to fit it,
            // and asking again cannot change that. Retried, it sat at the
            // head of the queue and held every result behind it for good.
            var indexRefused = code == 507 && item.ItemId is not null && item.Channel == RelayResultsIndex.Channel;

            // A NIGHT REFUSED IS DROPPED, NEVER RETRIED, and this is the
            // one decision putting drivers' nights through this queue
            // forced. The relay refuses a night to a club it has not been
            // TOLD offers them, with a 403 - and 401 and 403 are retried
            // here on purpose, because they are what a mistyped token looks
            // like and the queue should drain the moment it is corrected.
            //
            // A night retried would sit at the head of this queue and hold
            // every RESULT behind it, which is the exact fault the results
            // index comment above records. Results are what a club actually
            // needs the relay for. A night is a courtesy and is built again
            // from the facts the next time a race is filed, and again on the
            // re-send pass - so letting one go costs nothing and keeping it
            // costs the evening's results.
            //
            // AND THE SWITCH IS MARKED TO BE TOLD AGAIN. A 403 here means
            // this laptop believes the club offers nights and the relay does
            // not: a relay redeployed onto an empty volume, or a club moved
            // to a different one. Without this the flag would stay "told"
            // and every night of the evening would be refused in turn.
            var nightRefused = code is 401 or 403 && item.Channel == DriverNight.Channel;

            if (nightRefused)
            {
                _nightsOpenTold = false;

                // And told again on the very next pass, not after a
                // thirty-second backoff left from an earlier failure -
                // otherwise every night in that window is refused too
                // (20 Oct 2026, Bug Hunt 8, #85).
                _nextNightsOpenTick = 0;
            }

            if (driverLiveRefused)
            {
                // driverlive rides on the nights switch, so it is the nights
                // switch that is told again - at once, as above.
                _nightsOpenTold = false;
                _nextNightsOpenTick = 0;
            }

            if (driverMeRefused)
            {
                // Forgotten, so the requests pass tells the relay "open"
                // again. The worker thread owns both, and this is it. Taken,
                // that counts as a reopen (DriverAsksOpenedCount), which is
                // what makes MainForm send driverme again.
                _asksOpenToldTo = null;
            }

            var outcome = code is 400 or 413 || unknownChannel || indexRefused || nightRefused
                    || driverLiveRefused || driverMeRefused
                ? SendOutcome.Rejected
                : SendOutcome.Retry;

            if (isSignup && outcome == SendOutcome.Rejected)
            {
                // Refused for good: every invite code is now one the relay
                // has never been told about. Said until the relay takes a
                // signup document - not until the next other document lands.
                _signupDocumentProblem = unknownChannel
                    ? "The relay won't take driver sign-up - it has no \"signup\" channel, so it predates sign-up and needs redeploying "
                      + "(fly deploy --ha=false). Until then, invite links open a sign-up page that doesn't know their code."
                    : $"The relay refused the driver sign-up list (HTTP {code}). Until it takes it, invite links open a sign-up page "
                      + "that doesn't know their code.";
            }

            return outcome;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Stopped on purpose - see QuiesceAsync. Held for the next pass,
            // and no error recorded: "it did not answer in time" would be
            // untrue.
            return SendOutcome.Retry;
        }
        catch (Exception ex)
        {
            lock (_gate)
            {
                _lastError = Plain(ex);
            }

            return SendOutcome.Retry;
        }
    }

    private static string ReadBodyQuietly(HttpResponseMessage response, CancellationToken token)
    {
        try
        {
            return response.Content.ReadAsStringAsync(token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Too big, cut off, not text. The status code still says what
            // happened; the body only ever adds detail.
            return string.Empty;
        }
    }

    /// <summary>Whether a publish answer says the relay kept its own, newer copy.</summary>
    private static bool IsSupersededBy(string body, long ours, out long held)
    {
        held = 0;

        if (body.Length == 0)
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object
                && root.TryGetProperty("applied", out var applied) && applied.ValueKind == JsonValueKind.False
                && root.TryGetProperty("seq", out var seq) && seq.TryGetInt64(out held))
            {
                return held >= ours;
            }
        }
        catch
        {
            // Not the JSON expected. A 2xx is still a 2xx.
        }

        held = 0;
        return false;
    }

    private void Succeeded()
    {
        lock (_gate)
        {
            _failures = 0;
            _nextAttemptTick = 0;
            _online = true;
            _lastError = null;
            _lastSuccessUtc = DateTime.UtcNow;
        }
    }

    private void Failed()
    {
        lock (_gate)
        {
            _failures = Math.Min(_failures + 1, Backoff.Length);
            _nextAttemptTick = Environment.TickCount64
                + (long)Backoff[Math.Min(_failures - 1, Backoff.Length - 1)].TotalMilliseconds;
            _online = false;
        }
    }

    /// <summary>How long until the next attempt is allowed, or zero or less when it already is.</summary>
    private TimeSpan BackoffRemaining()
    {
        long next;

        lock (_gate)
        {
            next = _nextAttemptTick;
        }

        return next == 0 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(next - Environment.TickCount64);
    }

    private void RefreshStatus()
    {
        lock (_gate)
        {
            _status = new RelayStatus(
                _settings.RelayEnabled,
                IsConfigured(),
                _online,
                _lastSuccessUtc,
                _results.Count + _snapshots.Count,
                _dropped,

                // A refused signup document stays on the status line after
                // other documents land - see _signupDocumentProblem.
                _signupDocumentProblem is not { } signupProblem ? _lastError
                : _lastError is null ? signupProblem
                : $"{_lastError}  {signupProblem}");
        }
    }

    // ---------------------------------------------------------------- bits

    private bool IsConfigured() => _target.Configured;

    /// <summary>Said wherever a club id the relay could never hold stops a publish or a test.</summary>
    public const string InvalidClubMessage =
        "That club id is not one the relay can hold. Use 3 to 32 lower-case letters, digits and single hyphens, "
        + "starting and ending with a letter or digit - for example acme-rc.";

    private static string NameOf(RelayChannel channel) => channel switch
    {
        RelayChannel.State => "state",
        RelayChannel.Roster => "roster",
        RelayChannel.Programme => "programme",
        RelayChannel.Standings => "standings",
        RelayChannel.Badges => "badges",
        RelayChannel.Achievements => "achievements",
        RelayChannel.Tracks => "tracks",
        RelayChannel.Site => "site",
        RelayChannel.Preview => "preview",
        RelayChannel.CheckIn => "checkin",
        RelayChannel.Manage => "manage",
        RelayChannel.Signup => "signup",
        RelayChannel.DriverLive => "driverlive",
        RelayChannel.DriverMe => "driverme",
        RelayChannel.Captions => "captions",
        RelayChannel.Qualifying => "qualifying",
        RelayChannel.Sponsors => "sponsors",
        RelayChannel.NextNight => "nextnight",

        // A NAME THE RELAY WILL REFUSE, not "state" (13 Oct 2026). This
        // arm used to answer "state", so a channel added to the enum and
        // forgotten here would not have failed - it would have published
        // over the LIVE RACE FRAME, every second, for the whole evening.
        // Nothing would have said so; the board would simply have shown
        // the wrong thing. Now it is refused by the relay with the channel
        // named in the error, which is a bug somebody can find.
        _ => "unnamed-channel",
    };

    /// <summary>
    /// Shortened names for a whole list at once, keeping them telling
    /// each other apart.
    ///
    /// DisplayName on its own is not enough, and a real race day showed
    /// why: "Ghost of Christmas Past" and "Ghost of Christmas Present"
    /// both become "Ghost of Christmas P", and a results page with two
    /// identical names in it is worse than one with no shortening at all
    /// - the reader cannot tell which row is theirs.
    ///
    /// So the last name is cut to one letter, and then any set that
    /// collides gets a letter back, and again, until they differ or there
    /// is nothing left to give. Names that were ALREADY identical are
    /// left alone: two drivers genuinely called Bob are not this
    /// function's problem to solve, and lengthening them would not help.
    ///
    /// Takes and returns a list rather than a dictionary because the
    /// input can legitimately contain the same name twice, and a map
    /// keyed by name would quietly lose one of them.
    ///
    /// There is deliberately no single-name version. One existed, and
    /// every caller of it was a place that could not see the collision it
    /// was about to cause - which is the whole defect. Shortening is a
    /// decision about a LIST, so the only way to ask for it is to bring
    /// the list.
    ///
    /// Applied where documents are BUILT, never in the publisher: it does
    /// not look inside what it carries, and a privacy rule enforced by
    /// something that cannot read the payload is not enforced at all.
    ///
    /// INITIALS ONLY (22 Oct 2026, driver experience). Every caller of this
    /// builds something bound for the internet, so this is also where a
    /// driver who asked to be shown online by initials only becomes "S.P."
    /// (PublicPrivacy.Current) - whether or not the club shortens names.
    /// Decided on the name AS GIVEN, before any shortening: "Sam P" is
    /// nobody in the register, "Sam Parker" is. Where the caller knows each
    /// row's transponder it should pass them, so a row carrying a name from
    /// before a rename is still caught. Two drivers who would share
    /// initials get a number ("S.P.", "S.P. 2") rather than letters back,
    /// which would give away part of the name they asked to keep off.
    /// Spoken lines that stay in the room use DisplayNamesForTheRoom.
    /// </summary>
    /// <param name="transponders">Each name's transponder, in the same order, where the caller has them; null (or a null entry) matches by name alone.</param>
    public static IReadOnlyList<string> DisplayNames(
        IReadOnlyList<string> names,
        bool abbreviate,
        IReadOnlyList<string?>? transponders = null) =>
        DisplayNames(names, abbreviate, transponders, PublicPrivacy.Current);

    /// <summary>DisplayNames against a register of the caller's choosing - for tests, and for a document that has already read the register once.</summary>
    internal static IReadOnlyList<string> DisplayNames(
        IReadOnlyList<string> names,
        bool abbreviate,
        IReadOnlyList<string?>? transponders,
        PublicPrivacy privacy)
    {
        // The usual case, nobody asked: exactly the old answer, so every
        // document stays byte for byte what it was.
        if (privacy is null || (!privacy.AnyInitials && transponders is null))
        {
            return DisplayNamesForTheRoom(names, abbreviate);
        }

        var source = names.Select(n => (n ?? string.Empty).Trim()).ToList();
        var hidden = new bool[source.Count];

        for (var i = 0; i < source.Count; i++)
        {
            var transponder = transponders is not null && i < transponders.Count ? transponders[i] : null;

            // The stand-in for a car nobody named is not anybody's name.
            hidden[i] = source[i].Length > 0
                        && !string.Equals(source[i], RelayDocuments.UnnamedDriver, StringComparison.Ordinal)
                        && (privacy.InitialsForTransponder(transponder) || privacy.InitialsFor(source[i]));
        }

        if (!hidden.Any(h => h))
        {
            return DisplayNamesForTheRoom(names, abbreviate);
        }

        // Everybody else is shortened among themselves only. Shortened with
        // the hidden names still in, "Sam Pike" would have grown to "Sam Pi"
        // to keep clear of a "Sam Parker" nobody can see - telling the
        // reader there is another Sam P. in the field.
        var visible = Enumerable.Range(0, source.Count).Where(i => !hidden[i]).ToList();
        var shortened = DisplayNamesForTheRoom(visible.Select(i => source[i]).ToList(), abbreviate);
        var result = new string[source.Count];

        for (var v = 0; v < visible.Count; v++)
        {
            result[visible[v]] = shortened[v];
        }

        for (var i = 0; i < source.Count; i++)
        {
            if (hidden[i])
            {
                // A name with no letter or digit in it has nothing to hide.
                var initials = PublicPrivacy.Initials(source[i]);
                result[i] = initials.Length > 0 ? initials : source[i];
            }
        }

        KeepInitialsApart(result, source, hidden);
        return result;
    }

    /// <summary>
    /// Two different drivers shown as the same initials - Sam Parker and
    /// Steve Pike, both "S.P." - numbered so a reader can tell the rows
    /// apart (22 Oct 2026, driver experience). The same name twice is the
    /// same driver, as DisplayNames has always had it, and is left alone.
    /// In order of the full name, so the same pair is numbered the same way
    /// in every document that holds both of them.
    /// </summary>
    private static void KeepInitialsApart(string[] result, IReadOnlyList<string> source, bool[] hidden)
    {
        var used = new HashSet<string>(result, StringComparer.CurrentCultureIgnoreCase);

        var clashes = Enumerable.Range(0, result.Length)
            .GroupBy(i => result[i], StringComparer.CurrentCultureIgnoreCase)
            .Where(group => group.Any(i => hidden[i])
                            && group.Select(i => source[i]).Distinct(StringComparer.CurrentCultureIgnoreCase).Count() > 1)
            .ToList();

        foreach (var clash in clashes)
        {
            var shown = clash.Key;

            // Somebody NOT hidden already reads this way (a driver who
            // really is called "S.P."), so every hidden one takes a number.
            var number = clash.Any(i => !hidden[i]) ? 2 : 1;

            var people = clash
                .Where(i => hidden[i])
                .Select(i => source[i])
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var person in people)
            {
                string label;

                if (number == 1)
                {
                    label = shown;
                    number++;
                }
                else
                {
                    do
                    {
                        label = $"{shown} {number++}";
                    }
                    while (used.Contains(label));

                    used.Add(label);
                }

                foreach (var i in clash.Where(i => hidden[i] && string.Equals(source[i], person, StringComparison.CurrentCultureIgnoreCase)))
                {
                    result[i] = label;
                }
            }
        }
    }

    /// <summary>
    /// DisplayNames as it was before anybody could ask for initials only:
    /// the shortening alone, with nobody's privacy choice applied (22 Oct
    /// 2026, driver experience). For what stays in the room - the short
    /// names the cloud voices say over the PA (AnnouncementService
    /// .ShortNames) - since the PA, the Big Screen and the Wi-Fi pages all
    /// use the full name whatever a driver asked for online. Never for a
    /// document bound for the relay.
    /// </summary>
    public static IReadOnlyList<string> DisplayNamesForTheRoom(IReadOnlyList<string> names, bool abbreviate)
    {
        var source = names.Select(n => (n ?? string.Empty).Trim()).ToList();

        if (!abbreviate)
        {
            return source;
        }

        var words = source
            .Select(n => n.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .ToList();

        var keep = new int[source.Count];
        Array.Fill(keep, 1);

        static string Build(string original, string[] parts, int letters)
        {
            // WHY (23 Oct 2026, bug hunt 10): the stand-in for a car nobody
            // named was shortened like a real name, so results, the year
            // index and the vote ballot listed a driver called "Unnamed d".
            // It is nobody's name, so it always stays whole.
            if (parts.Length < 2 || string.Equals(original, RelayDocuments.UnnamedDriver, StringComparison.Ordinal))
            {
                return original;
            }

            var last = parts[^1];
            var take = Math.Clamp(letters, 1, last.Length);

            return string.Join(' ', parts[..^1]) + " " + last[..take];
        }

        // Bounded: every pass gives at least one name another letter, and
        // no name has more letters than it started with.
        for (var pass = 0; pass < 32; pass++)
        {
            var candidates = source.Select((n, i) => Build(n, words[i], keep[i])).ToList();
            var grew = false;

            var clashes = candidates
                .Select((text, index) => (text, index))
                .GroupBy(entry => entry.text, StringComparer.CurrentCultureIgnoreCase)
                .Where(group => group.Count() > 1);

            foreach (var clash in clashes)
            {
                var indexes = clash.Select(entry => entry.index).ToList();

                // Only a problem if the underlying names actually differ.
                var distinct = indexes
                    .Select(i => source[i])
                    .Distinct(StringComparer.CurrentCultureIgnoreCase)
                    .Count();

                if (distinct < 2)
                {
                    continue;
                }

                foreach (var i in indexes)
                {
                    var last = words[i].Length > 0 ? words[i][^1] : string.Empty;

                    if (keep[i] < last.Length)
                    {
                        keep[i]++;
                        grew = true;
                    }
                }
            }

            if (!grew)
            {
                return candidates;
            }
        }

        return source.Select((n, i) => Build(n, words[i], keep[i])).ToList();
    }

    /// <summary>
    /// Trims trailing slashes and supplies a scheme, so "localhost:8787"
    /// typed in a box still works - and takes a pasted PAGE address back
    /// to the relay it lives on.
    ///
    /// The address a club operator actually has in hand is rarely the
    /// base. It is the live page's link, because that is the string this
    /// app tells them to hand out. Pasting it here used to fail the
    /// connection test with a 404 - technically correct, since
    /// /c/club/live/api/v1 is not a thing, and completely unhelpful at
    /// the moment somebody is trying to go live. The suffixes the relay
    /// itself serves (/healthz, /api/..., /c/club and its three pages)
    /// are recognised and taken off; any other path is left alone,
    /// because a relay parked behind a reverse proxy under /some/prefix
    /// is legitimate, and nothing here can tell that prefix apart from
    /// the base it belongs to.
    /// </summary>
    public static string NormaliseBaseUrl(string? value)
    {
        var text = (value ?? string.Empty).Trim();

        // A pasted badge or page link carries a query ("?d=...") or a
        // fragment; neither is part of any relay's address, and left on
        // they stopped the page name in front of them being recognised.
        var cut = text.IndexOfAny(new[] { '?', '#' });

        if (cut >= 0)
        {
            text = text[..cut];
        }

        text = text.TrimEnd('/');

        if (text.Length == 0)
        {
            return string.Empty;
        }

        // No scheme typed: https, unless the host is one plain http may
        // carry the token to - a relay being tried out on localhost:8787
        // has no certificate and does not need one. A .local or .lan NAME
        // gets https like anywhere else: it used to get http, silently, and
        // the token then went in the clear to whichever device on the Wi-Fi
        // answered to that name (see IsPlainHttpHost). Typing http:// on
        // purpose is refused with UnsafeAddressMessage, so it is never silent.
        if (!text.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            var host = text.Split('/', 2)[0];
            text = (IsPlainHttpHost(host) ? "http://" : "https://") + text;
        }

        return StripRelayPaths(text);
    }

    /// <summary>
    /// Whether the publish token may be sent to this address.
    ///
    /// Over https, always. Over plain http, only to this laptop or a
    /// private network ADDRESS: anywhere else the token would cross the
    /// internet readable by every network it passed through, and whoever
    /// read it could publish, or delete, as the club. See IsPlainHttpHost
    /// for why a .local name is not one of those.
    /// </summary>
    public static bool IsSafeForToken(string root) =>
        Uri.TryCreate(root, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps
            || (uri.Scheme == Uri.UriSchemeHttp && IsPlainHttpHost(uri.Authority)));

    /// <summary>
    /// Whether this address can only be this laptop itself: localhost,
    /// 127.x.x.x or ::1.
    ///
    /// Narrower than the local-network rule IsSafeForToken uses, on
    /// purpose. This one decides whether the publish token may be REUSED,
    /// untyped, for an address somebody has typed somewhere else - see
    /// HeatCallerVoiceClient.Target. "Your own network" is not enough for
    /// that: a .local or .lan name, or a 192.168 address, is whatever
    /// answers to it on whichever Wi-Fi the laptop is on - a track's shared
    /// network, a hotel's - and over plain http the token would go to it
    /// readable by anyone else on that network. Only the laptop itself is
    /// certain to be the club's.
    /// </summary>
    public static bool IsLoopbackRoot(string root)
    {
        if (!Uri.TryCreate(root, UriKind.Absolute, out var uri))
        {
            return false;
        }

        // "localhost." (with the dot) is a fully-qualified DNS name, not the
        // resolver's built-in localhost: whatever DNS server the Wi-Fi hands
        // out may answer it. So a name ending in a dot never counts as this
        // laptop. (HostOnly used to take the dot off; since 17 Oct 2026 it
        // keeps it - see there - and this check stays as the explicit rule.)
        if (uri.IdnHost.EndsWith('.'))
        {
            return false;
        }

        var host = HostOnly(uri.Authority);

        return host == "localhost"
               || (System.Net.IPAddress.TryParse(host, out var ip) && System.Net.IPAddress.IsLoopback(ip));
    }

    /// <summary>Said wherever an unsafe address stops the token going out.</summary>
    public const string UnsafeAddressMessage =
        "The relay address must start with https:// - the token is never sent unencrypted. "
        + "(Plain http is only allowed for a relay on this laptop, or at a private IP address such as 192.168.1.20 "
        + "- not a .local or .lan name, which any device on the Wi-Fi can answer to.)";

    /// <summary>
    /// Where plain http may carry the token: localhost, or a loopback,
    /// private or link-local IP ADDRESS.
    ///
    /// Not a .local, .lan or .home.arpa NAME any more (25 Sep security
    /// audit). Those are resolved by multicast or by whatever the Wi-Fi's
    /// router says, with nothing to check who is answering - anyone at the
    /// track could answer for "clubrelay.local" from a phone and be handed
    /// the publish token on the first poll, and with it the power to publish
    /// or delete as the club. The same reasoning had already narrowed
    /// IsLoopbackRoot; it applies just as much to a token typed on purpose.
    /// A literal address is at least the machine the club pointed at.
    /// </summary>
    private static bool IsPlainHttpHost(string hostAndPort)
    {
        var host = HostOnly(hostAndPort);

        // A trailing dot is a DNS name, never this laptop or a literal
        // address (17 Oct 2026, Bug Hunt 8, A16-4) - refused outright rather
        // than left to whatever IPAddress.TryParse makes of "10.0.0.1.".
        if (host.EndsWith('.'))
        {
            return false;
        }

        if (host == "localhost")
        {
            return true;
        }

        if (System.Net.IPAddress.TryParse(host, out var ip))
        {
            if (System.Net.IPAddress.IsLoopback(ip) || ip.IsIPv6LinkLocal || ip.IsIPv6UniqueLocal)
            {
                return true;
            }

            var b = ip.GetAddressBytes();

            return b.Length == 4 && (b[0] == 10
                                     || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                                     || (b[0] == 192 && b[1] == 168)
                                     || (b[0] == 169 && b[1] == 254));
        }

        // No name but localhost - a bare single-label one ("timing-laptop")
        // included. It usually resolves on the local network, but not
        // always: a DNS search suffix can turn it into somebody's public
        // host. A relay on the LAN is reached by its IP, or over https.
        return false;
    }

    /// <summary>
    /// The host alone from "host", "host:port" or "[v6]:port" - lower case.
    /// A trailing dot is KEPT (17 Oct 2026, Bug Hunt 8, A16-4): it was
    /// trimmed, so "localhost." and "10.0.0.1." passed the plain-http rule
    /// as this laptop or a private address, when "localhost." is a DNS name
    /// whatever server the Wi-Fi hands out may answer - the reason
    /// IsLoopbackRoot already refused it.
    /// </summary>
    private static string HostOnly(string hostAndPort)
    {
        var host = hostAndPort.Trim();

        if (host.StartsWith('['))
        {
            var close = host.IndexOf(']');
            host = close > 0 ? host[1..close] : host;
        }
        else if (host.Count(c => c == ':') == 1)
        {
            host = host[..host.IndexOf(':')];
        }

        return host.ToLowerInvariant();
    }

    /// <summary>The suffixes the relay itself serves, taken off a pasted address. See NormaliseBaseUrl.</summary>
    private static string StripRelayPaths(string url)
    {
        // Only the path may ever be cut - never the host, however
        // club-shaped a piece of it looks.
        var hostStart = url.IndexOf("//", StringComparison.Ordinal) + 2;
        var pathStart = url.IndexOf('/', hostStart);

        if (pathStart < 0)
        {
            return url;
        }

        if (url.EndsWith("/healthz", StringComparison.OrdinalIgnoreCase))
        {
            return url[..^"/healthz".Length].TrimEnd('/');
        }

        var api = url.IndexOf("/api/", pathStart, StringComparison.OrdinalIgnoreCase);

        if (api >= 0)
        {
            return url[..api].TrimEnd('/');
        }

        var club = url.IndexOf("/c/", pathStart, StringComparison.OrdinalIgnoreCase);

        if (club < 0)
        {
            return url;
        }

        // "/c/{club}" alone, or with exactly one of the relay's page
        // names after it. Anything else following "/c/" is somebody's
        // real path and is left where it is.
        var rest = url[(club + "/c/".Length)..].Split('/');

        var isRelayPage = rest.Length == 1
            ? rest[0].Length > 0
            : rest.Length == 2 && IsRelayPageName(rest[1]);

        return isRelayPage ? url[..club].TrimEnd('/') : url;
    }

    /// <summary>Every page the relay serves under /c/{club}/ - its PAGES table. A pasted link to any of them is taken back to the relay.</summary>
    private static bool IsRelayPageName(string segment) =>
        segment.Equals("live", StringComparison.OrdinalIgnoreCase)
        || segment.Equals("schedule", StringComparison.OrdinalIgnoreCase)
        || segment.Equals("results", StringComparison.OrdinalIgnoreCase)
        || segment.Equals("standings", StringComparison.OrdinalIgnoreCase)
        || segment.Equals("badges", StringComparison.OrdinalIgnoreCase)
        || segment.Equals("tracks", StringComparison.OrdinalIgnoreCase)
        || segment.Equals("checkin", StringComparison.OrdinalIgnoreCase)
        || segment.Equals("mynight", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Makes a session id safe to be a file name on the relay.
    ///
    /// The relay validates this too and would refuse anything else, but a
    /// refusal is a result lost. Better to send an id it will accept: the
    /// id only has to be stable and unique, not pretty.
    /// </summary>
    public static string? SafeItemId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var builder = new StringBuilder(value.Length);

        foreach (var c in value)
        {
            if (char.IsAsciiLetterOrDigit(c) || c == '.' || c == '-' || c == '_')
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var text = builder.ToString().Trim('-', '.');

        // The relay's pattern insists on starting with a letter or digit,
        // forbids "..", and stops at 64 characters.
        while (text.Contains("..", StringComparison.Ordinal))
        {
            text = text.Replace("..", ".", StringComparison.Ordinal);
        }

        text = text.TrimStart('.', '-', '_');

        if (text.Length > 64)
        {
            text = text[..64].TrimEnd('.', '-', '_');
        }

        return text.Length == 0 ? null : text;
    }

    /// <summary>
    /// Tells the two different 404s apart.
    ///
    /// The relay answers "No such club." for a club it does not hold and
    /// "No such endpoint." for a route it does not have - and an older
    /// relay, started before whoami existed, gives the second. Reporting
    /// both as a missing club sends somebody off to check a club id that
    /// was never wrong.
    /// </summary>
    private static string NotFound(string body, string club)
    {
        if (body.Contains("endpoint", StringComparison.OrdinalIgnoreCase))
        {
            return "This relay is running an older build that has no /whoami. Rebuild and restart it "
                 + "(npm test, then npm start), and try again.";
        }

        return $"The relay has no club called \"{club}\".";
    }

    private static string ClubNameFrom(string body, string fallback)
    {
        try
        {
            using var document = JsonDocument.Parse(body);

            if (document.RootElement.TryGetProperty("name", out var name) && name.ValueKind == JsonValueKind.String)
            {
                return name.GetString() ?? fallback;
            }
        }
        catch
        {
            // Not JSON, or not the shape expected. The club id is a fine answer.
        }

        return fallback;
    }

    /// <summary>One readable sentence from an exception, rather than a stack trace in a status line.</summary>
    private static string Plain(Exception ex) => ex switch
    {
        TaskCanceledException => "It did not answer in time.",
        HttpRequestException http when http.InnerException is not null => http.InnerException.Message,
        _ => ex.Message,
    };

    /// <summary>
    /// What the club sees when HeatCaller has paused it on the relay (24 Oct
    /// 2026, relay admin): every route under the club answers 403 with
    /// "paused": true. Said as what it is, rather than the "token belongs to
    /// a different club" a plain 403 means - the token is fine, and a club
    /// told otherwise would go hunting for a typo that isn't there.
    /// </summary>
    internal const string PausedMessage = "This club is paused on the HeatCaller relay. Ask HeatCaller to switch it back on.";

    /// <summary>Whether a refusal from the relay is the club being paused.</summary>
    internal static bool IsPaused(string? body) =>
        body is not null && body.Contains("\"paused\":true", StringComparison.Ordinal);
}

/// <summary>
/// The manager requests already run, on disk (17 Oct 2026, Bug Hunt 8,
/// A16-2). RelayPublisher's replay guard was memory only, so a request
/// still listed on the relay after a crash or a failed answer ran a second
/// time when the laptop restarted inside its five-minute signing window.
/// Tiny: bounded by the relay's queue and a ten-minute expiry. Never
/// throws - a ledger that cannot be read or written is the old behaviour,
/// not a fault.
/// </summary>
internal static class ManageRunLedger
{
    /// <summary>Where the ledger lives.</summary>
    public static string DefaultPath => Path.Combine(AppPaths.Root, "relay-manage-ran.json");

    /// <summary>The requests still inside their window at <paramref name="nowUtc"/>; empty when there is no readable ledger.</summary>
    public static Dictionary<string, DateTime> Load(string path, DateTime nowUtc)
    {
        var kept = new Dictionary<string, DateTime>(StringComparer.Ordinal);

        try
        {
            if (!File.Exists(path))
            {
                return kept;
            }

            var read = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(SafeFile.ReadAllTextWithRetry(path));

            if (read is null)
            {
                return kept;
            }

            foreach (var pair in read)
            {
                var until = pair.Value.Kind == DateTimeKind.Utc ? pair.Value : pair.Value.ToUniversalTime();

                if (!string.IsNullOrEmpty(pair.Key) && until > nowUtc)
                {
                    kept[pair.Key] = until;
                }
            }
        }
        catch (Exception)
        {
            // Unreadable: start empty, as before this was saved at all.
        }

        return kept;
    }

    /// <summary>Writes the ledger. False when it could not be written.</summary>
    public static bool Save(string path, IReadOnlyDictionary<string, DateTime> runUntil)
    {
        try
        {
            return SafeFile.WriteAllText(path, JsonSerializer.Serialize(runUntil));
        }
        catch (Exception)
        {
            return false;
        }
    }
}
