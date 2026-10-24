namespace HeatCaller;

/// <summary>
/// What the crash journal says about a race besides the session itself: the
/// track, the round, the typed race name, and whether it is a re-run. These
/// come from the screen: read from Race Control at every write while it is
/// there (as WriteJournal always read them), and also captured at Stage,
/// Countdown and the green and whenever Race Control changes them, so the
/// last ones read still stand once the page is gone.
/// </summary>
/// <param name="TrackLayoutName">The track layout chosen for this race, or empty when none is.</param>
/// <param name="RoundId">The round the race is being run for, or null when none is picked.</param>
/// <param name="RaceLabel">The race name typed on Race Control, or null to leave it unrecorded.</param>
/// <param name="IsRerun">The race is a re-run of a round already recorded - see RaceJournal.Entry.IsRerun.</param>
public sealed record RaceJournalDetails(string TrackLayoutName, string? RoundId, string? RaceLabel, bool IsRerun)
{
    /// <summary>Nothing captured yet: no track, no round, no name - what a journal written before Race Control is built carries.</summary>
    public static RaceJournalDetails None { get; } = new(string.Empty, null, null, false);
}

/// <summary>
/// Writes the crash journal (<see cref="RaceJournal"/>) at the moments the
/// race changes: after every crossing, every flag and every correction.
///
/// WHY (23 Oct 2026, timing layer, P2): the journal used to be written only
/// from Race Control's own handlers, so it was written only when that page
/// existed and its handlers ran - and a page that is closed, or not built,
/// or busy, is exactly when a race most needs its safety net. This is owned
/// by MainForm, which feeds it the session's events itself (see
/// MainForm.RaceJournal), so the journal is written whatever the page is
/// doing. Same file, same moments, same Clear rules as before: only who calls
/// Record has changed.
///
/// It holds no WinForms control. Inline (the default), it is called on the
/// UI thread only - MainForm marshals the session's events there first, as
/// Race Control always did.
///
/// THREADED MODE (23 Oct 2026, timing layer, P4b): with "Run timing on its
/// own thread" on, it listens on the timing side instead
/// (<see cref="ListenOnTimingSide"/>), so the journal is written on every
/// lap and flag even with the window frozen. Then:
/// <list type="bullet">
/// <item>every Journal.Record is made on the timing thread - a write the UI
/// asks for (<see cref="Write"/>) is sent there and waited for - which is
/// what RaceJournal's lock relies on;</item>
/// <item><see cref="DetailsSource"/> is never called there: the timing side
/// writes the details last captured, which the UI keeps up to date (at
/// Stage, Countdown and the green, on every name and track change, and on
/// MainForm's 500 ms race tick);</item>
/// <item><see cref="BeforeEachWrite"/> (the unfiled-race retry, which writes
/// a UI-thread store) is posted to the UI, never run there - see
/// WriteOnTimingSide;</item>
/// <item><see cref="Written"/> is raised on the UI thread, by a post;</item>
/// <item><see cref="SessionStartedAtUtc"/> is kept behind a lock.</item>
/// </list>
/// </summary>
public sealed class RaceJournalRecorder
{
    private readonly RaceTiming _race;

    public RaceJournalRecorder(RaceTiming race, RaceJournal? journal = null)
    {
        _race = race;
        Journal = journal ?? new RaceJournal();
    }

    /// <summary>
    /// The running race, written to disk as it happens.
    ///
    /// Everything else here can be done again; a race cannot. See
    /// <see cref="RaceJournal"/>. One for the app: filing clears it, Race
    /// Control's Reset clears it, and this writes it.
    /// </summary>
    public RaceJournal Journal { get; }

    /// <summary>
    /// When this race started, as the saved session will say - stamped at the
    /// green, cleared at Stage. Race Control reads and writes it as its own
    /// (its _sessionStartedAtUtc is this), and filing clears it once the race
    /// is over.
    /// </summary>
    /// <remarks>
    /// WHY (23 Oct 2026, timing layer, P4b): behind a lock - threaded, the
    /// timing side stamps it at the green while Race Control and filing read
    /// and set it on the UI thread.
    /// </remarks>
    public DateTime? SessionStartedAtUtc
    {
        get
        {
            lock (_startLock)
            {
                return _sessionStartedAtUtc;
            }
        }
        set
        {
            lock (_startLock)
            {
                _sessionStartedAtUtc = value;
            }
        }
    }

    private readonly object _startLock = new();

    private DateTime? _sessionStartedAtUtc;

    /// <summary>The track, round, name and re-run flag last captured - see <see cref="RaceJournalDetails"/>.</summary>
    /// <remarks>Swapped whole (volatile) - threaded, it is read on the timing thread and captured on the UI thread.</remarks>
    public RaceJournalDetails Details
    {
        get => _details;
        private set => _details = value;
    }

    private volatile RaceJournalDetails _details = RaceJournalDetails.None;

    /// <summary>
    /// Where the details are read from - at every write, and when they are
    /// captured. Race Control sets it as it is built, and takes it away again
    /// when it is torn down. Null while there is no page to read them from:
    /// the details last captured stand.
    /// </summary>
    public Func<RaceJournalDetails>? DetailsSource { get; set; }

    /// <summary>
    /// Runs before every write - the filing service's retry of a race that
    /// could not be filed, which has to happen before the next race's first
    /// write replaces that race in the journal (RaceFilingService.RetryUnfiledBeforeJournal).
    /// </summary>
    public Action? BeforeEachWrite { get; set; }

    /// <summary>
    /// Threaded mode only: whether <see cref="BeforeEachWrite"/> has work to
    /// do before the next write - asked on the timing thread, so it must be
    /// safe there (RaceFilingService.UnfiledRetryDue). Null: never.
    /// </summary>
    public Func<bool>? RetryDueBeforeWrite { get; set; }

    /// <summary>
    /// Raised after every write, whether or not Record actually wrote (it
    /// skips an idle, finished, demo or practice session, and throttles) -
    /// Race Control's race recording follows the race from here, as it always
    /// followed it from the journal's cadence.
    /// </summary>
    public event Action? Written;

    /// <summary>Reads the details from <see cref="DetailsSource"/> now, if there is one.</summary>
    public void CaptureDetails()
    {
        if (DetailsSource is { } source)
        {
            Details = source();
        }
    }

    /// <summary>Takes these details as the race's own - for whoever has them in hand.</summary>
    public void UseDetails(RaceJournalDetails details) => Details = details;

    /// <summary>
    /// Writes the journal, unless it was written a moment ago (RaceJournal's
    /// own throttle) - <paramref name="force"/> for flags and corrections.
    /// </summary>
    /// <remarks>
    /// Moved here from RaceControlControl.WriteJournal (23 Oct 2026, timing
    /// layer, P2), in the same order: the retry of an unfiled race first, then
    /// the write, then the race recording.
    /// </remarks>
    public void Write(bool force = false)
    {
        // Threaded (P4b): the UI's own writes - a restore, an undo, closing -
        // are made on the timing thread, as every other write is then.
        if (_race.Host is not null)
        {
            WriteFromUiThreaded(force);
            return;
        }

        BeforeEachWrite?.Invoke();

        // WHY (23 Oct 2026, timing layer): read from the page at every write
        // while there is one, exactly as WriteJournal read the Round box, the
        // track, the typed name and the re-run flag each time. Captured only
        // at Stage, Countdown, the green and a name/track/RefreshFormat change,
        // the journal could keep an old round whenever the host set
        // _activeRound without one of those following (the undo that puts a
        // freeform setup back sets it directly, for one) - and a restore
        // would then file the race against the wrong heat. Reading at every
        // write cannot drift. With no page, the details last captured stand.
        CaptureDetails();

        var details = Details;

        Journal.Record(_race, SessionStartedAtUtc, details.TrackLayoutName, force,
            details.RoundId, details.RaceLabel, details.IsRerun);

        Written?.Invoke();
    }

    // --- The session's events, and the moments they are -------------------
    //
    // The same moments Race Control wrote the journal at before 23 Oct 2026:
    // every crossing (throttled), and every flag, correction and removal
    // (forced). Called by MainForm's handlers, on the UI thread.

    /// <summary>
    /// Subscribes to the session's events, each one handed to
    /// <paramref name="dispatch"/> to run - MainForm's hop onto the UI thread
    /// (straight through when already on it). Once, for the life of the
    /// session: the recorder and the session live as long as the app.
    /// </summary>
    public void Listen(Action<Action> dispatch)
    {
        _race.StateChanged += state => dispatch(() => OnStateChanged(state));
        _race.FlagChanged += _ => dispatch(OnFlagChanged);
        _race.LapProcessed += (_, _, _) => dispatch(OnLapProcessed);
        _race.StandingAdjusted += _ => dispatch(OnStandingAdjusted);
        _race.StandingRemoved += _ => dispatch(OnStandingRemoved);
    }

    /// <summary>
    /// A new state: the session start is let go at Stage and stamped at the
    /// green (as Race Control does - the two agree, since both only ever set
    /// it to null at Stage and "??=" it at Racing), and the screen's details
    /// are captured as the race is staged, counted down and started.
    /// </summary>
    public void OnStateChanged(RaceState state)
    {
        // WHY (23 Oct 2026, timing layer, P2): done here as well as in Race
        // Control's own state handler, so a journal written with that page
        // not built still carries the race's start. Race Control's
        // _sessionStartedAtUtc IS this property, so with the page there the
        // two writes are the same write made twice.
        if (state == RaceState.Staging)
        {
            SessionStartedAtUtc = null;
        }

        if (state == RaceState.Racing)
        {
            SessionStartedAtUtc ??= DateTime.UtcNow;
        }

        if (state is RaceState.Staging or RaceState.Countdown or RaceState.Racing)
        {
            CaptureDetails();
        }
    }

    /// <summary>
    /// The safety net. Written after every crossing, so that whatever
    /// happens next - a crash, a power cut, a closed laptop - the race can be
    /// picked up from this lap.
    /// </summary>
    public void OnLapProcessed() => Write();

    /// <summary>A flag is a moment worth being able to come back to, so this one is written straight away rather than waiting for the next lap.</summary>
    public void OnFlagChanged() => Write(force: true);

    /// <summary>Forced: a correction by hand is rare and is exactly the sort of thing nobody wants to do twice.</summary>
    public void OnStandingAdjusted() => Write(force: true);

    /// <summary>A ghost switched off mid-race, taken out of the journal straight away.</summary>
    public void OnStandingRemoved() => Write(force: true);

    /// <summary>
    /// A crossing the journal's throttle held back is written here, at most
    /// a tick late, rather than waiting for the next crossing or flag - the
    /// last laps before a crash were lost that way. Not forced: Record keeps
    /// its own throttle. See RaceJournal.IsBehind.
    /// </summary>
    public void OnClockTick()
    {
        if (Journal.IsBehind)
        {
            Write();
        }
    }

    // --- Threaded mode (23 Oct 2026, timing layer, P4b) ---------------------

    /// <summary>Where the timing side posts its UI work: MainForm's post (false when the window can't take it). Set by ListenOnTimingSide.</summary>
    private Func<Action, bool>? _toUi;

    /// <summary>
    /// The unfiled-race retry, as the timing side and the UI hand it between
    /// them: 0 = none; 1 = posted, waiting for the UI; 2 = running on the UI.
    /// The UI takes it 1 -> 2 as it starts the retry; the timing side takes
    /// it 1 -> 0 when it gives up waiting - whichever comes first wins, so a
    /// retry the timing side gave up on never runs. See WriteOnTimingSide.
    /// </summary>
    private int _retryPosted;

    /// <summary>
    /// How long a write waits for the window to retry an unfiled race before
    /// the race running gets its journal anyway.
    /// </summary>
    internal const long RetryHoldMs = 2_000;

    /// <summary>When the retry was posted (TimingClock.NowMs). Timing thread only.</summary>
    private long _retryPostedAtMs;

    /// <summary>The race (SessionGeneration) the timing side gave up waiting for the retry for - no second post for it. Timing thread only.</summary>
    private int _retryGivenUpGeneration = int.MinValue;

    /// <summary>A write was held back for the retry and has not been made yet - the timing tick makes it once the hold is over. Timing thread only.</summary>
    private bool _writeHeld;

    /// <summary>
    /// Where a line for the console goes - always called on the UI thread,
    /// by a post. MainForm points it at its console (with the time stamp).
    /// Null: the line is dropped.
    /// </summary>
    public Action<string>? Log { get; set; }

    /// <summary>1 while a Written is posted to the UI and not yet raised - one waiting is enough.</summary>
    private int _writtenPosted;

    /// <summary>
    /// Threaded mode: listens to the session ON THE TIMING THREAD, so the
    /// journal is written at every crossing, flag and correction whatever the
    /// window is doing, and catches up a held-back write on the timing tick.
    /// The same moments as <see cref="Listen"/>. <paramref name="toUi"/> posts
    /// to the UI thread and never waits (false when the window cannot take
    /// it). Once, for the life of the session.
    /// </summary>
    public void ListenOnTimingSide(Func<Action, bool> toUi)
    {
        ArgumentNullException.ThrowIfNull(toUi);

        if (_race.Host is null)
        {
            throw new InvalidOperationException("ListenOnTimingSide is for threaded mode only - inline, use Listen.");
        }

        _toUi = toUi;

        _race.AddTimingSideHandler(e =>
        {
            e.StateChanged += OnStateChangedOnTimingSide;
            e.FlagChanged += _ => WriteOnTimingSide(force: true);
            e.LapProcessed += (_, _, _) => WriteOnTimingSide(force: false);
            e.StandingAdjusted += _ => WriteOnTimingSide(force: true);
            e.StandingRemoved += _ => WriteOnTimingSide(force: true);
        });

        // The 500 ms catch-up (OnClockTick), on the timing clock instead.
        //
        // WHY (24 Oct 2026, timing layer, P8): also the write held back for
        // the unfiled-race retry. Without this, once the hold was over the
        // race running was journalled only at its next crossing or flag - a
        // held green with the cars still on the start line could go
        // unwritten for as long as nobody crossed. Forced: what was held may
        // have been a flag. It goes no further than the hold allows (see
        // WriteOnTimingSide), so inside the hold it changes nothing.
        _race.AddTimingTick(() =>
        {
            if (_writeHeld)
            {
                WriteOnTimingSide(force: true);
            }
            else if (Journal.IsBehind)
            {
                WriteOnTimingSide(force: false);
            }
        });
    }

    /// <summary>
    /// OnStateChanged on the timing side: the start let go at Stage and
    /// stamped at the green, in one step under the lock; the screen's details
    /// captured on the UI thread, by a post.
    /// </summary>
    private void OnStateChangedOnTimingSide(RaceState state)
    {
        lock (_startLock)
        {
            if (state == RaceState.Staging)
            {
                _sessionStartedAtUtc = null;
            }

            if (state == RaceState.Racing)
            {
                _sessionStartedAtUtc ??= DateTime.UtcNow;
            }
        }

        if (state is RaceState.Staging or RaceState.Countdown or RaceState.Racing)
        {
            PostToUi(CaptureDetails);
        }
    }

    /// <summary>
    /// A write on the timing thread (threaded mode): the details last
    /// captured, never DetailsSource; then Written, posted to the UI.
    /// </summary>
    /// <remarks>
    /// WHY (23 Oct 2026, timing layer, P4b): the unfiled-race retry must run
    /// before the next race's first write replaces that race in the journal -
    /// the journal is its only copy on disk - and it writes the race history,
    /// a UI-thread store. It cannot run here, and run after the write it does
    /// harm: a retry that files the old race clears the journal, which by
    /// then is the NEW race's. So when one is due (RetryDueBeforeWrite, which
    /// is only ever so when a race could not be filed - a failed history
    /// write), this write is held back: the retry is posted to the UI, and
    /// once it has run there the write is made, forced. The journal keeps the
    /// old race until then, as inline. Every other write goes straight ahead.
    ///
    /// WHY (24 Oct 2026, timing layer, P8): but not for ever. Held until the
    /// UI ran the retry, a frozen window meant the NEW race's journal was not
    /// written at all for as long as the freeze lasted - every lap and flag
    /// skipped, the safety net gone exactly when the window had stopped. Now
    /// the hold lasts RetryHoldMs at most: the old race's on-disk copy is
    /// kept for that moment, then the race running wins and its journal is
    /// written (and the timing tick makes the held write, so it does not wait
    /// for the next crossing). That is the trade inline makes too: the retry
    /// runs once, then the new race overwrites, and a race still unfiled
    /// stays in memory and is filed after this one (RaceFilingService).
    /// Giving up takes the retry back (1 -> 0) so it does not run when the
    /// window wakes - its Commit would clear the journal, which by then is
    /// the new race's - and it is not posted again for this race. A retry the
    /// window has already started (2) is waited for: it sends the forced
    /// write itself as soon as it is done. Nothing here waits on the UI.
    /// </remarks>
    private void WriteOnTimingSide(bool force, bool afterRetry = false)
    {
        try
        {
            if (!afterRetry && RetryDueBeforeWrite?.Invoke() == true
                && _race.SessionGeneration != _retryGivenUpGeneration)
            {
                var state = Volatile.Read(ref _retryPosted);

                if (state == 0)
                {
                    if (PostRetryThenWrite())
                    {
                        _writeHeld = true;
                        return;
                    }
                }
                else if (state == 2)
                {
                    // The window is running the retry; it sends the forced write.
                    _writeHeld = true;
                    return;
                }
                else if (TimingClock.NowMs - _retryPostedAtMs < RetryHoldMs)
                {
                    _writeHeld = true;
                    return;
                }
                else if (Interlocked.CompareExchange(ref _retryPosted, 0, 1) == 1)
                {
                    _retryGivenUpGeneration = _race.SessionGeneration;
                    PostToUi(() => Log?.Invoke("[session] The window was busy, so this race's crash journal is written now "
                        + "without first retrying the earlier race that could not be saved. That race is kept in memory and "
                        + "saved after this one."));
                }
                else
                {
                    // The window took the retry a moment ago (now 2); it sends the forced write.
                    _writeHeld = true;
                    return;
                }
            }

            _writeHeld = false;

            var details = Details;

            Journal.Record(_race, SessionStartedAtUtc, details.TrackLayoutName, force,
                details.RoundId, details.RaceLabel, details.IsRerun);

            PostWritten();
        }
        catch
        {
            // Never let the safety net be the thing that breaks timing.
            // (Record catches its own; this is for the rest.)
        }
    }

    /// <summary>
    /// Posts the unfiled-race retry to the UI, which then sends a forced
    /// write back here. True when it is on its way (this one or one already
    /// posted); false when the window could not take it - the write then goes
    /// ahead now, since the race running needs its journal more.
    /// </summary>
    private bool PostRetryThenWrite()
    {
        if (Interlocked.CompareExchange(ref _retryPosted, 1, 0) != 0)
        {
            return true;
        }

        // P8 (24 Oct 2026): stamped for the hold - see WriteOnTimingSide.
        _retryPostedAtMs = TimingClock.NowMs;

        var posted = PostToUi(() =>
        {
            // WHY (24 Oct 2026, timing layer, P8): the timing side may have
            // given up waiting (it took this back, 1 -> 0) and written the new
            // race's journal since. Then the retry must NOT run here: filing
            // the old race clears the journal, which now holds the new race.
            // The old race stays in memory and is filed after this one.
            if (Interlocked.CompareExchange(ref _retryPosted, 2, 1) != 1)
            {
                return;
            }

            try
            {
                BeforeEachWrite?.Invoke();
            }
            catch
            {
                // The retry's own failure is its own to report; the race's
                // journal is written below regardless.
            }
            finally
            {
                Volatile.Write(ref _retryPosted, 0);
            }

            CaptureDetails();
            _race.Host?.Post(() => WriteOnTimingSide(force: true, afterRetry: true));
        });

        if (!posted)
        {
            Volatile.Write(ref _retryPosted, 0);
        }

        return posted;
    }

    /// <summary>Raises Written on the UI thread - one post waiting at a time; it says "the journal moved on", and once is enough for any number of writes.</summary>
    private void PostWritten()
    {
        if (Written is null || Interlocked.Exchange(ref _writtenPosted, 1) == 1)
        {
            return;
        }

        if (!PostToUi(() =>
            {
                Volatile.Write(ref _writtenPosted, 0);
                Written?.Invoke();
            }))
        {
            Volatile.Write(ref _writtenPosted, 0);
        }
    }

    private bool PostToUi(Action work)
    {
        try
        {
            return _toUi?.Invoke(work) == true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// <see cref="Write"/> from the UI thread, threaded mode: the retry and
    /// the details here on the UI thread, as inline, then the journal written
    /// on the timing thread (waited for - the UI may wait on timing), then
    /// Written, here.
    /// </summary>
    private void WriteFromUiThreaded(bool force)
    {
        BeforeEachWrite?.Invoke();
        CaptureDetails();

        var details = Details;
        var startedAt = SessionStartedAtUtc;

        _race.Host!.Invoke(() => Journal.Record(_race, startedAt, details.TrackLayoutName, force,
            details.RoundId, details.RaceLabel, details.IsRerun));

        Written?.Invoke();
    }
}
