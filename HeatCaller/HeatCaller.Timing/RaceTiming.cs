namespace HeatCaller;

/// <summary>
/// The one front door to the race engine. Every screen, service and helper
/// in the app holds a RaceTiming, never a <see cref="RaceControlSession"/>:
/// the board, Race Control, the Big Screen, practice, the demo, the ghosts,
/// the announcer, the web server, the journal and the filing service all
/// reach the race through here.
///
/// Its surface is the engine's own, member for member: the same names, the
/// same signatures and the same return types, so moving a caller over is a
/// change of type and nothing else. The members and the types they hand
/// out (RacerStanding, RaceControlSession.SessionSnapshot,
/// RaceControlSession.StandingEdit, RaceWarning ...) are the engine's.
/// See <c>TimingLayer_P3_FacadeTests</c>, which checks the two surfaces
/// still match and lists the engine members deliberately left off.
///
/// HOW IT FORWARDS: nothing here talks to the engine directly. Every member
/// goes through one of four private helpers:
/// <list type="bullet">
/// <item><see cref="Read{T}"/> - a property get or a question (standings,
/// the clock, "has this car jumped?"). Reads change nothing.</item>
/// <item><see cref="Do"/> / <see cref="Do{T}"/> - anything that changes
/// the race or its settings: a flag, a lap, a warning, a property set, an
/// undo. Do{T} is a command that answers (AdjustLaps, CreateSnapshot ...).</item>
/// <item><see cref="AddHandler{TDelegate}"/> / <see cref="RemoveHandler{TDelegate}"/>
/// - every event subscription.</item>
/// </list>
/// In inline mode (the default) each helper simply runs on the caller's
/// thread, so the app behaves exactly as it did holding the engine itself.
/// In threaded mode ("Run timing on its own thread") they run on the
/// <see cref="TimingHost"/> - see "THREADED MODE" below.
/// </summary>
/// <remarks>
/// <para>
/// WHY (23 Oct 2026, timing layer, P3): the step before timing gets its own
/// thread (P4). With every caller on this one class and every call through
/// those four helpers, the threaded mode changes only the helpers, and not
/// one of the ~40 files that use the race has to change again. No WinForms
/// types in here: it moves to the timing class library with the engine (P6).
/// </para>
/// <para>
/// THREADED MODE (23 Oct 2026, timing layer, P4). The engine lives on the
/// timing thread and is touched nowhere else:
/// </para>
/// <list type="bullet">
/// <item>Read and Do run there through <see cref="TimingHost.Invoke{T}"/>:
/// the caller waits (the UI may wait on timing; timing never waits on the
/// UI). On the timing thread itself they run inline, which is what keeps
/// EditStanding's change - app code calling back in - working.</item>
/// <item>Nothing live leaves: the standings, the leader, the grid, the
/// warning ladder, a warning given or taken back and the sector lengths are
/// copies made on the timing thread (<see cref="RacerStanding.Clone"/>,
/// <see cref="RaceWarning.Clone"/>). Two exceptions, both by design: the
/// StandingEdit token, handed back as it is (see EditStanding); and
/// CreateSnapshot, whose standings are the engine's own deep copies but share
/// the race's RaceWarning objects (an undo puts warnings back by reference),
/// and which become the live standings again once that snapshot is restored -
/// so a snapshot is not read after it has been put back.</item>
/// <item>Events: one handler per engine event, wired here on the timing
/// thread, copies the arguments and queues the raise for the UI thread, in
/// order, on one queue. The queue is delivered on the UI thread by a post to
/// <c>uiContext</c> - and also straight after any command the UI thread
/// itself issues, before that command returns, so code that counts on a
/// command's events having fired by the time it returns (the demo's
/// DriveSession, a "suppress the flag calls" guard round an undo) behaves
/// exactly as inline. The UI's handlers are kept here, not on the engine.</item>
/// <item>The race clock ticks on the host, every 100 ms (MainForm's 500 ms
/// UI timer no longer calls Tick), and with it the staggered start's release
/// calls and anything added with <see cref="AddTimingTick"/> (P5, P4b).</item>
/// <item>The start (P5): the screen's start rules (<see cref="StartRulesOnUi"/>)
/// are applied on the UI before the green is asked for; the green waits on
/// the timing side for the countdown audio alone, on a pool thread, capped at
/// <see cref="GreenAudioCap"/> - never for the window.</item>
/// <item>While an event is delivered the UI can ask when it was raised
/// (<see cref="DeliveringEventRaisedAtMs"/>, <see cref="DeliveringIsStale"/> -
/// a lap call heard of late is not spoken) and, for RaceCompleted, the race
/// as it finished (<see cref="DeliveringFinishedRace"/>) (P4b).</item>
/// </list>
/// </remarks>
public sealed class RaceTiming
{
    /// <summary>Wraps one engine. The engine is not shared with another RaceTiming.</summary>
    public RaceTiming(RaceControlSession engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        Engine = engine;
    }

    /// <summary>
    /// Threaded mode: the engine moves onto <paramref name="host"/>'s thread
    /// and stays there; events are delivered through <paramref name="uiContext"/>.
    /// Made on the UI thread - the thread it is made on is taken to be the one
    /// <paramref name="uiContext"/> runs things on.
    /// </summary>
    /// <param name="tickOnHost">True (the app) ticks the race clock on the host every <see cref="TimingHost.DefaultTickInterval"/>; a test that ticks by hand passes false.</param>
    public RaceTiming(RaceControlSession engine, TimingHost host, SynchronizationContext uiContext, bool tickOnHost = true)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(uiContext);

        Engine = engine;
        _host = host;
        _uiContext = uiContext;
        _uiThreadId = Environment.CurrentManagedThreadId;

        host.Invoke(() => ForwardEventsToUi(engine));

        if (tickOnHost)
        {
            host.StartTicking(OnHostTick);
        }
    }

    /// <summary>
    /// The race clock's tick on the timing thread, threaded mode: the
    /// engine's own Tick, then the release calls, then whatever else was
    /// handed to <see cref="AddTimingTick"/> (the crash journal's catch-up).
    /// </summary>
    /// <remarks>
    /// WHY (23 Oct 2026, timing layer, P5): the staggered start's release
    /// calls were polled from Race Control's 250 ms screen timer, so a busy
    /// window held a car's "go" back. Here they are polled on the timing
    /// clock with the rest of the race, every 100 ms. PollReleases changes
    /// nothing when no release is due, so polling it more often than the
    /// screen did is harmless.
    /// </remarks>
    private void OnHostTick()
    {
        try
        {
            Engine.Tick();
        }
        finally
        {
            AfterEngineTick();
        }
    }

    /// <summary>The rest of OnHostTick: each part on its own, so one failing never stops the others.</summary>
    private void AfterEngineTick()
    {
        // WHY (24 Oct 2026, timing layer, P8): first, so an automatic start's
        // green due on this tick drops before anything else the tick does.
        try
        {
            FireArmedGreenIfDue();
        }
        catch (Exception ex)
        {
            ReportFromTimingSide($"The automatic start's green could not be checked ({ex.GetType().Name}: {ex.Message}) - timing carries on.");
        }

        try
        {
            Engine.PollReleases();
        }
        catch (Exception ex)
        {
            ReportFromTimingSide($"The release calls could not be checked ({ex.GetType().Name}: {ex.Message}) - timing carries on.");
        }

        foreach (var tick in Volatile.Read(ref _timingTicks))
        {
            try
            {
                tick();
            }
            catch (Exception ex)
            {
                ReportFromTimingSide($"A timing-side tick failed ({ex.GetType().Name}: {ex.Message}) - timing carries on.");
            }
        }
    }

    /// <summary>
    /// A line for the race log from the timing thread: queued for the UI's
    /// LogMessage handlers like any other raise, never waited for.
    /// </summary>
    private void ReportFromTimingSide(string message)
    {
        if (HasUiHandlers(EventSlot.LogMessage))
        {
            ToUi(EventSlot.LogMessage, h => ((Action<string>)h)(message));
        }
    }

    /// <summary>
    /// The engine itself - for the tests, and for the few places named in
    /// the timing-layer notes that genuinely need it. App code uses the
    /// members below. In threaded mode it may only be touched on the timing
    /// thread (see <see cref="AddTimingSideHandler"/>).
    /// </summary>
    internal RaceControlSession Engine { get; }

    // ---- Threaded mode's state. All null / unused in inline mode. ----

    private readonly TimingHost? _host;
    private readonly SynchronizationContext? _uiContext;
    private readonly int _uiThreadId;

    /// <summary>The UI's handlers, one slot per engine event (EventSlot). Changed under the array's own lock, read with Volatile.</summary>
    private readonly Delegate?[] _uiHandlers = new Delegate?[EventSlotCount];

    /// <summary>Raises (and pass work) waiting for the UI thread, oldest first. Under its own lock.</summary>
    private readonly LinkedList<UiItem> _forUi = new();

    /// <summary>The next UiItem's number (under _forUi's lock): what tells a command's own raises from older ones.</summary>
    private long _nextUiSeq;

    /// <summary>How deep the UI thread is in a delivery - above zero while a handler or a pass's UI work runs. UI thread only.</summary>
    private int _deliveryDepth;

    /// <summary>1 while a delivery is posted to the UI and not yet begun - so a frozen window gets one waiting message, not thousands.</summary>
    private int _deliveryPosted;

    /// <summary>What the UI has said practice may start on at the next line pass at Idle - see PracticeAutoStart. Swapped whole, read with Volatile.</summary>
    private PracticeAutoStartPlan? _practiceAutoStart;

    /// <summary>Auto-started practices the UI has not taken over yet (PostLinePass queued the notice; its delivery ends it). Raised on the timing thread, lowered on the UI thread.</summary>
    private int _autoStartsOnTheirWay;

    /// <summary>The last practice started at the gate, until a race start takes it back - see TakeBackPracticeAutoStartForRace. Timing thread only.</summary>
    private PendingAutoStart? _pendingAutoStart;

    /// <summary>Race starts under way on the UI that no gate-pass practice may start under - see TakeBackPracticeAutoStartForRace. Any thread, Interlocked.</summary>
    private int _autoStartHolds;

    /// <summary>The engine's own gate as the app set it - see BeforeGreenFlagAsync.</summary>
    private Func<Task>? _gateAsSet;

    /// <summary>The automatic start's green, armed (threaded) - see ArmGreenAfterCountdown. Timing thread only.</summary>
    private ArmedGreen? _armedGreen;

    /// <summary>The start rules, threaded mode - see StartRulesOnUi.</summary>
    private Action? _startRules;

    /// <summary>What else runs on the timing tick, threaded mode - see AddTimingTick. Replaced whole under its own lock, read with Volatile.</summary>
    private Action[] _timingTicks = Array.Empty<Action>();

    private readonly object _ticksLock = new();

    /// <summary>
    /// How old a lap call may be by the time the UI gets to it and still be
    /// said: three seconds (threaded mode). See <see cref="DeliveringIsStale"/>.
    /// </summary>
    internal const long StaleLapCallMs = 3_000;

    /// <summary>
    /// The cap on the green's wait for the countdown audio, threaded mode:
    /// the same ten seconds MainForm gives WaitForAudioAsync. See
    /// BeforeGreenFlagAsync.
    /// </summary>
    internal static readonly TimeSpan GreenAudioCap = TimeSpan.FromSeconds(10);

    /// <summary>True in threaded mode: the engine is on its own thread (AppSettings.TimingOnItsOwnThread).</summary>
    internal bool IsThreaded => _host is not null;

    /// <summary>The timing thread, in threaded mode; null inline.</summary>
    internal TimingHost? Host => _host;

    /// <summary>
    /// While a queued event is being delivered on the UI thread (threaded
    /// mode): when the engine raised it, on TimingClock. Inline, while a pass
    /// is being processed: when that pass arrived (bug hunt 22). Null
    /// otherwise. For the staleness rule - a lap call more than a few
    /// seconds old by the time the UI gets to it is not spoken (P4b).
    /// </summary>
    internal long? DeliveringEventRaisedAtMs { get; private set; }

    /// <summary>
    /// While a lap's events are being delivered: when the car crossed, by
    /// the decoder's own clock (DirectLapEvent.CrossedMs), on TimingClock -
    /// a little before the pass reached the app. Null outside a delivery, or
    /// when the decoder's clock could not say. What the announcer times a
    /// call about the lap from (21 Nov 2026) - see
    /// AnnouncementService.LapCrossedAtMs.
    /// </summary>
    internal long? DeliveringLapCrossedAtMs { get; private set; }

    /// <summary>
    /// Threaded: the decoder-mapped crossing time of the pass the engine is
    /// judging right now, so the raises it makes can carry it to the UI.
    /// Timing thread only; 0 between passes.
    /// </summary>
    private long _crossingBeingJudgedMs;

    /// <summary>
    /// True while the UI is being told of an event (threaded mode) that the
    /// race raised more than <see cref="StaleLapCallMs"/> ago - the window
    /// was busy or frozen meanwhile - inline, a pass that arrived that long
    /// ago. Always false outside a delivery. UI thread.
    /// </summary>
    /// <remarks>
    /// WHY (23 Oct 2026, timing layer, P4b): after a freeze the UI is handed
    /// every lap it missed, in order, all at once. The board, the results and
    /// the journal want every one of them; the room does not want to hear
    /// ten seconds of old lap calls read out over the race that is running
    /// now. So the screens that drive the announcer from a lap ask this and,
    /// when it is true, let the announcer keep its count of the race but say
    /// nothing (AnnouncementService.WhileLapCallsAreStale).
    /// </remarks>
    internal bool DeliveringIsStale =>
        DeliveringEventRaisedAtMs is { } raisedAt && TimingClock.NowMs - raisedAt > StaleLapCallMs;

    /// <summary>How long ago the event being delivered was raised, in milliseconds (threaded mode, or a pass being processed inline); null outside a delivery.</summary>
    internal long? DeliveringAgeMs =>
        DeliveringEventRaisedAtMs is { } raisedAt ? TimingClock.NowMs - raisedAt : null;

    /// <summary>
    /// While the UI is being told the race has finished (threaded mode): the
    /// race as it stood when it finished, frozen on the timing thread as
    /// RaceCompleted was raised. Null otherwise, and always null inline,
    /// where the race the handler reads IS the race as it finished.
    /// </summary>
    /// <remarks>
    /// WHY (23 Oct 2026, timing layer, P4b): filing reads the standings, the
    /// start time and who jumped the start when the UI gets to RaceCompleted.
    /// Threaded, the timing thread has carried on by then - a pass, a tick, a
    /// correction from the phones - and the race filed must be the one that
    /// finished, not whatever the board has become since. RaceFilingService
    /// (and Race Control's part of the filing) read this first.
    /// </remarks>
    internal FinishedRace? DeliveringFinishedRace { get; private set; }

    /// <summary>
    /// Adds <paramref name="tick"/> to the timing thread's clock tick,
    /// threaded mode - after the engine's own tick and the release calls,
    /// every 100 ms. Runs on the timing thread, so it must never touch the
    /// UI or wait for it. Inline there is no timing tick: refused.
    /// </summary>
    internal void AddTimingTick(Action tick)
    {
        ArgumentNullException.ThrowIfNull(tick);

        if (_host is null)
        {
            throw new InvalidOperationException("AddTimingTick is for threaded mode only - inline, MainForm's race tick does this work.");
        }

        lock (_ticksLock)
        {
            var ticks = new Action[_timingTicks.Length + 1];
            _timingTicks.CopyTo(ticks, 0);
            ticks[^1] = tick;
            Volatile.Write(ref _timingTicks, ticks);
        }
    }

    /// <summary>
    /// The start rules (threaded mode): screen work that has to be done
    /// before a real green drops - Race Control's release schedule, the
    /// uninspected-driver warning, the qualifying rule. Run on the UI thread
    /// by GreenFlagAfterAudioAsync just before the green is issued, and by
    /// the gate a get of BeforeGreenFlagAsync answers (the demo awaits that
    /// one on the UI thread itself). Null for none. Never set inline, where
    /// Race Control puts its rules inside the engine's gate, as it always did.
    /// </summary>
    /// <remarks>
    /// WHY (23 Oct 2026, timing layer, P5): inline the rules ran inside the
    /// gate the green waits for, Invoked onto the UI thread from wherever the
    /// green was waiting. Threaded the green waits on the timing thread, which
    /// must never wait for the UI - so the rules are applied on the UI before
    /// the green is asked for, and the timing side waits only for the audio.
    /// </remarks>
    internal Action? StartRulesOnUi
    {
        get => Volatile.Read(ref _startRules);
        set
        {
            if (_host is null)
            {
                throw new InvalidOperationException("StartRulesOnUi is for threaded mode only - inline, the start rules live in the engine's gate.");
            }

            Volatile.Write(ref _startRules, value);
        }
    }

    private bool OnUiThread => Environment.CurrentManagedThreadId == _uiThreadId;

    // ---- The forwarding helpers. P4 changes these and only these. ----

    /// <summary>A read of the race: changes nothing.</summary>
    private T Read<T>(Func<RaceControlSession, T> read) =>
        _host is null ? read(Engine) : _host.Invoke(() => read(Engine));

    /// <summary>A command: changes the race or its settings.</summary>
    private void Do(Action<RaceControlSession> command)
    {
        if (_host is null)
        {
            command(Engine);
            return;
        }

        DoThreaded(() =>
        {
            command(Engine);
            return true;
        });
    }

    /// <summary>A command that answers.</summary>
    private T Do<T>(Func<RaceControlSession, T> command) =>
        _host is null ? command(Engine) : DoThreaded(() => command(Engine));

    /// <summary>
    /// A command in threaded mode. From the UI thread, whatever the UI has
    /// not been told yet is delivered first, then the command runs on the
    /// timing thread, then its own events are delivered - all before this
    /// returns, as inline. From any other thread it just runs and waits; its
    /// events reach the UI by the post.
    /// </summary>
    /// <remarks>
    /// A command issued from INSIDE a delivery - a handler, or the Practice
    /// page taking over a practice started at the gate - delivers only its
    /// own raises before it returns (as inline, where they ran inside it).
    /// What was already waiting, and any pass work, stays for the delivery
    /// under way to reach in order.
    /// </remarks>
    private T DoThreaded<T>(Func<T> command)
    {
        if (!OnUiThread)
        {
            return _host!.Invoke(command);
        }

        var nested = _deliveryDepth > 0;

        if (!nested)
        {
            DeliverToUi();
        }

        // WHY (23 Oct 2026, timing layer, review): the command's own raises
        // are numbered from where the queue stood on the TIMING thread just
        // before the command ran, up to where it stood just after. Read here
        // on the UI thread instead, a tick or a pass the timing thread ran
        // between that read and the command was taken for the command's own,
        // and a nested command delivered it ahead of older news still waiting.
        long ownFrom = long.MaxValue;
        long ownTo = long.MaxValue;

        T Run()
        {
            ownFrom = NextUiSeq();

            try
            {
                return command();
            }
            finally
            {
                ownTo = NextUiSeq();
            }
        }

        try
        {
            return _host!.Invoke(Run);
        }
        finally
        {
            if (!nested)
            {
                DeliverToUi();
            }

            // Its own raises, whatever is left: a nested command's (a
            // command that was not nested has had everything delivered above).
            DeliverOwnRaises(ownFrom, ownTo);
        }
    }

    /// <summary>The number the next UiItem will get. Any thread.</summary>
    private long NextUiSeq()
    {
        lock (_forUi)
        {
            return _nextUiSeq;
        }
    }

    /// <summary>Subscribes a handler to one of the engine's events. A null handler is ignored, as += null is.</summary>
    private void AddHandler<TDelegate>(TDelegate? handler, EventSlot slot, Action<RaceControlSession, TDelegate> add)
        where TDelegate : Delegate
    {
        if (handler is null)
        {
            return;
        }

        if (_host is null)
        {
            add(Engine, handler);
            return;
        }

        lock (_uiHandlers)
        {
            _uiHandlers[(int)slot] = Delegate.Combine(_uiHandlers[(int)slot], handler);
        }
    }

    /// <summary>Takes a handler off one of the engine's events.</summary>
    private void RemoveHandler<TDelegate>(TDelegate? handler, EventSlot slot, Action<RaceControlSession, TDelegate> remove)
        where TDelegate : Delegate
    {
        if (handler is null)
        {
            return;
        }

        if (_host is null)
        {
            remove(Engine, handler);
            return;
        }

        lock (_uiHandlers)
        {
            _uiHandlers[(int)slot] = Delegate.Remove(_uiHandlers[(int)slot], handler);
        }
    }

    // ---- Events (all 18 of the engine's). ----

    /// <summary>See <see cref="RaceControlSession.SectorsRecorded"/>.</summary>
    public event Action<RacerStanding, int, long[]>? SectorsRecorded
    {
        add => AddHandler(value, EventSlot.SectorsRecorded, static (e, h) => { e.SectorsRecorded += h; });
        remove => RemoveHandler(value, EventSlot.SectorsRecorded, static (e, h) => { e.SectorsRecorded -= h; });
    }

    /// <summary>See <see cref="RaceControlSession.SplitCrossed"/>.</summary>
    public event Action<RacerStanding, TimingPoint, long>? SplitCrossed
    {
        add => AddHandler(value, EventSlot.SplitCrossed, static (e, h) => { e.SplitCrossed += h; });
        remove => RemoveHandler(value, EventSlot.SplitCrossed, static (e, h) => { e.SplitCrossed -= h; });
    }

    /// <summary>See <see cref="RaceControlSession.PossibleMissedLap"/>.</summary>
    public event Action<RacerStanding, long, long>? PossibleMissedLap
    {
        add => AddHandler(value, EventSlot.PossibleMissedLap, static (e, h) => { e.PossibleMissedLap += h; });
        remove => RemoveHandler(value, EventSlot.PossibleMissedLap, static (e, h) => { e.PossibleMissedLap -= h; });
    }

    /// <summary>See <see cref="RaceControlSession.StandingRemoved"/>.</summary>
    public event Action<RacerStanding>? StandingRemoved
    {
        add => AddHandler(value, EventSlot.StandingRemoved, static (e, h) => { e.StandingRemoved += h; });
        remove => RemoveHandler(value, EventSlot.StandingRemoved, static (e, h) => { e.StandingRemoved -= h; });
    }

    /// <summary>See <see cref="RaceControlSession.JumpStartDetected"/>.</summary>
    public event Action<string, long>? JumpStartDetected
    {
        add => AddHandler(value, EventSlot.JumpStartDetected, static (e, h) => { e.JumpStartDetected += h; });
        remove => RemoveHandler(value, EventSlot.JumpStartDetected, static (e, h) => { e.JumpStartDetected -= h; });
    }

    /// <summary>See <see cref="RaceControlSession.StateChanged"/>.</summary>
    public event Action<RaceState>? StateChanged
    {
        add => AddHandler(value, EventSlot.StateChanged, static (e, h) => { e.StateChanged += h; });
        remove => RemoveHandler(value, EventSlot.StateChanged, static (e, h) => { e.StateChanged -= h; });
    }

    /// <summary>See <see cref="RaceControlSession.FlagChanged"/>.</summary>
    public event Action<FlagState>? FlagChanged
    {
        add => AddHandler(value, EventSlot.FlagChanged, static (e, h) => { e.FlagChanged += h; });
        remove => RemoveHandler(value, EventSlot.FlagChanged, static (e, h) => { e.FlagChanged -= h; });
    }

    /// <summary>See <see cref="RaceControlSession.LapProcessed"/>.</summary>
    public event Action<RacerStanding, bool, bool>? LapProcessed
    {
        add => AddHandler(value, EventSlot.LapProcessed, static (e, h) => { e.LapProcessed += h; });
        remove => RemoveHandler(value, EventSlot.LapProcessed, static (e, h) => { e.LapProcessed -= h; });
    }

    /// <summary>See <see cref="RaceControlSession.PersonalBestLap"/>.</summary>
    public event Action<RacerStanding>? PersonalBestLap
    {
        add => AddHandler(value, EventSlot.PersonalBestLap, static (e, h) => { e.PersonalBestLap += h; });
        remove => RemoveHandler(value, EventSlot.PersonalBestLap, static (e, h) => { e.PersonalBestLap -= h; });
    }

    /// <summary>See <see cref="RaceControlSession.StandingAdjusted"/>.</summary>
    public event Action<RacerStanding>? StandingAdjusted
    {
        add => AddHandler(value, EventSlot.StandingAdjusted, static (e, h) => { e.StandingAdjusted += h; });
        remove => RemoveHandler(value, EventSlot.StandingAdjusted, static (e, h) => { e.StandingAdjusted -= h; });
    }

    /// <summary>See <see cref="RaceControlSession.RaceCompleted"/>.</summary>
    public event Action? RaceCompleted
    {
        add => AddHandler(value, EventSlot.RaceCompleted, static (e, h) => { e.RaceCompleted += h; });
        remove => RemoveHandler(value, EventSlot.RaceCompleted, static (e, h) => { e.RaceCompleted -= h; });
    }

    /// <summary>See <see cref="RaceControlSession.LastLapStarted"/>.</summary>
    public event Action<RacerStanding>? LastLapStarted
    {
        add => AddHandler(value, EventSlot.LastLapStarted, static (e, h) => { e.LastLapStarted += h; });
        remove => RemoveHandler(value, EventSlot.LastLapStarted, static (e, h) => { e.LastLapStarted -= h; });
    }

    /// <summary>See <see cref="RaceControlSession.FinalMinute"/>.</summary>
    public event Action? FinalMinute
    {
        add => AddHandler(value, EventSlot.FinalMinute, static (e, h) => { e.FinalMinute += h; });
        remove => RemoveHandler(value, EventSlot.FinalMinute, static (e, h) => { e.FinalMinute -= h; });
    }

    /// <summary>See <see cref="RaceControlSession.TimeLimitReached"/>.</summary>
    public event Action? TimeLimitReached
    {
        add => AddHandler(value, EventSlot.TimeLimitReached, static (e, h) => { e.TimeLimitReached += h; });
        remove => RemoveHandler(value, EventSlot.TimeLimitReached, static (e, h) => { e.TimeLimitReached -= h; });
    }

    /// <summary>See <see cref="RaceControlSession.LogMessage"/>.</summary>
    public event Action<string>? LogMessage
    {
        add => AddHandler(value, EventSlot.LogMessage, static (e, h) => { e.LogMessage += h; });
        remove => RemoveHandler(value, EventSlot.LogMessage, static (e, h) => { e.LogMessage -= h; });
    }

    /// <summary>See <see cref="RaceControlSession.WarningIssued"/>.</summary>
    public event Action<RacerStanding, RaceWarning>? WarningIssued
    {
        add => AddHandler(value, EventSlot.WarningIssued, static (e, h) => { e.WarningIssued += h; });
        remove => RemoveHandler(value, EventSlot.WarningIssued, static (e, h) => { e.WarningIssued -= h; });
    }

    /// <summary>See <see cref="RaceControlSession.WarmUpChanged"/>.</summary>
    public event Action? WarmUpChanged
    {
        add => AddHandler(value, EventSlot.WarmUpChanged, static (e, h) => { e.WarmUpChanged += h; });
        remove => RemoveHandler(value, EventSlot.WarmUpChanged, static (e, h) => { e.WarmUpChanged -= h; });
    }

    /// <summary>See <see cref="RaceControlSession.DriverReleased"/>.</summary>
    public event Action<string, long>? DriverReleased
    {
        add => AddHandler(value, EventSlot.DriverReleased, static (e, h) => { e.DriverReleased += h; });
        remove => RemoveHandler(value, EventSlot.DriverReleased, static (e, h) => { e.DriverReleased -= h; });
    }

    // ---- Settings the app sets on the race (property sets are commands). ----

    public int MinLapTimeMs
    {
        get => Read(static e => e.MinLapTimeMs);
        set => Do(e => { e.MinLapTimeMs = value; });
    }

    public int SplitCount
    {
        get => Read(static e => e.SplitCount);
        set => Do(e => { e.SplitCount = value; });
    }

    public double StreakWindowPercent
    {
        get => Read(static e => e.StreakWindowPercent);
        set => Do(e => { e.StreakWindowPercent = value; });
    }

    public bool StreakFasterLapBreaks
    {
        get => Read(static e => e.StreakFasterLapBreaks);
        set => Do(e => { e.StreakFasterLapBreaks = value; });
    }

    public int? LapLimit
    {
        get => Read(static e => e.LapLimit);
        set => Do(e => { e.LapLimit = value; });
    }

    public bool FinishOnNextCrossing
    {
        get => Read(static e => e.FinishOnNextCrossing);
        set => Do(e => { e.FinishOnNextCrossing = value; });
    }

    public TimeSpan? TimeLimit
    {
        get => Read(static e => e.TimeLimit);
        set => Do(e => { e.TimeLimit = value; });
    }

    public bool DetectJumpStarts
    {
        get => Read(static e => e.DetectJumpStarts);
        set => Do(e => { e.DetectJumpStarts = value; });
    }

    public long JumpStartPenaltyMs
    {
        get => Read(static e => e.JumpStartPenaltyMs);
        set => Do(e => { e.JumpStartPenaltyMs = value; });
    }

    public bool JumpStartCostsTheLap
    {
        get => Read(static e => e.JumpStartCostsTheLap);
        set => Do(e => { e.JumpStartCostsTheLap = value; });
    }

    public bool IsDemoMode
    {
        get => Read(static e => e.IsDemoMode);
        set => Do(e => { e.IsDemoMode = value; });
    }

    public bool IsPracticeMode
    {
        get => Read(static e => e.IsPracticeMode);
        set => Do(e => { e.IsPracticeMode = value; });
    }

    /// <remarks>
    /// Inline, the list is the engine's own, live: whoever sets it should not
    /// change it afterwards. Threaded (P4), the engine is given a copy of the
    /// list and its steps, and a get answers a copy - the settings page edits
    /// a class's ladder in place on the UI thread while the timing thread
    /// reads it at every warning.
    /// </remarks>
    public List<WarningStep>? WarningLadder
    {
        get => _host is null ? Read(static e => e.WarningLadder) : Read(static e => CopyOf(e.WarningLadder));
        set
        {
            if (_host is null)
            {
                Do(e => { e.WarningLadder = value; });
                return;
            }

            var copy = CopyOf(value);
            Do(e => { e.WarningLadder = copy; });
        }
    }

    public RaceStartType StartType
    {
        get => Read(static e => e.StartType);
        set => Do(e => { e.StartType = value; });
    }

    public StaggerClock StaggerClockMode
    {
        get => Read(static e => e.StaggerClockMode);
        set => Do(e => { e.StaggerClockMode = value; });
    }

    // ---- Lookups the engine calls back into during a race. ----
    //
    // WHY (23 Oct 2026, timing layer, P4): in threaded mode the engine calls
    // these on the TIMING thread, so each one the app sets must be safe there
    // and must never reach for the UI thread. MainForm sets them that way:
    // the alternates go to ProfileStore's alternate index, rebuilt whole and
    // swapped by reference on the UI thread (PrimaryForAlternate is safe from
    // any thread); the streak threshold and the sector lengths answer values
    // worked out on the UI thread and kept in volatile fields (MainForm's
    // RefreshFrozenLookups). Nothing here enforces it - a lookup set
    // elsewhere must be written the same way.

    /// <remarks>Called by the engine inside its own lap and status work - app code runs inside engine calls (threaded: on the timing thread).</remarks>
    public Func<double[]?>? SectorLengthsFeetProvider
    {
        get => Read(static e => e.SectorLengthsFeetProvider);
        set => Do(e => { e.SectorLengthsFeetProvider = value; });
    }

    /// <remarks>Called by the engine inside its own lap work - app code runs inside engine calls (threaded: on the timing thread).</remarks>
    public Func<int>? StreakThresholdProvider
    {
        get => Read(static e => e.StreakThresholdProvider);
        set => Do(e => { e.StreakThresholdProvider = value; });
    }

    /// <remarks>Called by the engine on every pass and at the green - app code runs inside engine calls (threaded: on the timing thread).</remarks>
    public Func<string, string?>? AlternateTransponderOwner
    {
        get => Read(static e => e.AlternateTransponderOwner);
        set => Do(e => { e.AlternateTransponderOwner = value; });
    }

    /// <remarks>
    /// <para>Awaited by GreenFlagAfterAudioAsync before the green drops - Race Control's start gate.</para>
    /// <para>
    /// WHY (23 Oct 2026, timing layer, P5): threaded, the green is awaited on
    /// the timing thread, which must never wait for the UI. So in threaded
    /// mode the gate set here must need no UI: it is MainForm's wait for the
    /// countdown audio (AnnouncementService.WaitForAudioAsync, which reads
    /// only counters and locks of its own). The engine is given a wrapper that
    /// starts it on a pool thread - never on the timing thread, so none of the
    /// announcer's locks is ever taken there - and stops waiting for it after
    /// <see cref="GreenAudioCap"/> on its own timer, so neither a frozen
    /// window nor a stuck sound card can stretch the start. The screen's part
    /// of the start goes in <see cref="StartRulesOnUi"/> instead, run before
    /// the green is asked for.
    /// </para>
    /// <para>
    /// A get answers, threaded, the gate as set with the start rules in
    /// front of it - what the engine's gate held inline once Race Control had
    /// chained onto it - so the demo, which awaits it on the UI thread, still
    /// settles the start before its green. With no start rules it is the gate
    /// exactly as set.
    /// </para>
    /// </remarks>
    public Func<Task>? BeforeGreenFlagAsync
    {
        get => _host is null ? Read(static e => e.BeforeGreenFlagAsync) : GateWithStartRules();
        set
        {
            if (_host is null)
            {
                Do(e => { e.BeforeGreenFlagAsync = value; });
                return;
            }

            Volatile.Write(ref _gateAsSet, value);
            var onTimingSide = value is null ? null : OffTheTimingThread(value);
            Do(e => { e.BeforeGreenFlagAsync = onTimingSide; });
        }
    }

    /// <summary>The pass recorder (P0), or null when recording is off.</summary>
    internal TimingRecorder? Recorder
    {
        get => Read(static e => e.Recorder);
        set => Do(e => { e.Recorder = value; });
    }

    // ---- Reads: the race's state and clock. ----

    /// <remarks>Threaded: a copy of the array (P4).</remarks>
    public double[]? SectorLengthsFeet =>
        _host is null ? Read(static e => e.SectorLengthsFeet) : Read(static e => e.SectorLengthsFeet?.ToArray());
    public int StreakThreshold => Read(static e => e.StreakThreshold);
    public bool IsFinishing => Read(static e => e.IsFinishing);
    public RaceState State => Read(static e => e.State);
    public FlagState Flag => Read(static e => e.Flag);
    public DateTime? RaceStartUtc => Read(static e => e.RaceStartUtc);
    public DateTime? StagedAtUtc => Read(static e => e.StagedAtUtc);
    public long RaceFastestLapMs => Read(static e => e.RaceFastestLapMs);
    public string RaceFastestLapTransponder => Read(static e => e.RaceFastestLapTransponder);
    public long MsSinceGreen => Read(static e => e.MsSinceGreen);
    public TimeSpan ElapsedRaceTime => Read(static e => e.ElapsedRaceTime);
    public bool GreenStartedTheRace => Read(static e => e.GreenStartedTheRace);
    public long LastKnownDeviceTimeMs => Read(static e => e.LastKnownDeviceTimeMs);
    public long EstimatedDeviceTimeNowMs => Read(static e => e.EstimatedDeviceTimeNowMs);
    public int SessionGeneration => Read(static e => e.SessionGeneration);
    public DateTime? WarmUpEndsUtc => Read(static e => e.WarmUpEndsUtc);
    public TimeSpan WarmUpLength => Read(static e => e.WarmUpLength);
    public bool IsWarmingUp => Read(static e => e.IsWarmingUp);
    public TimeSpan WarmUpRemaining => Read(static e => e.WarmUpRemaining);

    /// <remarks>Inline, the engine's own grid list, read-only to callers. Threaded, a copy (P4).</remarks>
    public IReadOnlyList<string> Grid =>
        _host is null ? Read(static e => e.Grid) : Read(static e => (IReadOnlyList<string>)e.Grid.ToList());

    public bool HasTakenFlag(string transponderId) => Read(e => e.HasTakenFlag(transponderId));
    public bool HasJumpStarted(string transponderId) => Read(e => e.HasJumpStarted(transponderId));
    public string HeardAs(string rowTransponder) => Read(e => e.HeardAs(rowTransponder));
    public bool IsSameRace(int generation) => Read(e => e.IsSameRace(generation));
    public long OwedJumpStartPenaltyMs(string transponderId) => Read(e => e.OwedJumpStartPenaltyMs(transponderId));
    public WarningStep NextWarningStep(string transponderId) => Read(e => e.NextWarningStep(transponderId));

    /// <remarks>
    /// The list is new each call. Inline, the RacerStanding objects in it are
    /// the engine's live ones; threaded, deep copies made on the timing
    /// thread (P4) - so two calls never hand out the same object, and a
    /// standing is compared by TransponderId, never by reference.
    /// </remarks>
    public IReadOnlyList<RacerStanding> GetOrderedStandings() =>
        _host is null ? Read(static e => e.GetOrderedStandings()) : Read(static e => CopyOf(e.GetOrderedStandings()));

    /// <summary>
    /// What the live state document reads of the race, all from one moment:
    /// one Read rather than seven. The standings and the sector lengths are
    /// copies when threaded and the engine's own inline, as
    /// GetOrderedStandings and SectorLengthsFeet are.
    /// </summary>
    /// <remarks>
    /// WHY (24 Oct 2026, timing layer, P8): with timing on its own thread the
    /// live state is built off the UI thread (WebServerService, from
    /// MainForm.LiveState), and each separate read was its own trip to the
    /// timing thread - between which a lap could land, so the board and the
    /// state beside it could be from two moments. The web server uses this
    /// threaded only; inline it reads as it always did.
    /// </remarks>
    internal LiveRaceView ReadLiveView() =>
        _host is null
            ? Read(static e => new LiveRaceView(
                e.GetOrderedStandings(), e.State, e.Flag, e.GetCountdownText(), e.SessionGeneration, e.StreakThreshold, e.SectorLengthsFeet,
                e.EstimatedDeviceTimeNowMs))
            : Read(static e => new LiveRaceView(
                CopyOf(e.GetOrderedStandings()), e.State, e.Flag, e.GetCountdownText(), e.SessionGeneration, e.StreakThreshold, e.SectorLengthsFeet?.ToArray(),
                e.EstimatedDeviceTimeNowMs));

    /// <remarks>Inline, the engine's live RacerStanding, or null; threaded, a copy (P4).</remarks>
    public RacerStanding? RaceLeader() =>
        _host is null ? Read(static e => e.RaceLeader()) : Read(static e => e.RaceLeader()?.Clone());

    public string GetCountdownText() => Read(static e => e.GetCountdownText());
    public int StartPositionOf(string transponderId) => Read(e => e.StartPositionOf(transponderId));
    public long ReleaseOffsetMsFor(string transponderId) => Read(e => e.ReleaseOffsetMsFor(transponderId));
    public bool IsReleased(string transponderId) => Read(e => e.IsReleased(transponderId));
    public IReadOnlyList<string> ReleaseRoster() => Read(static e => e.ReleaseRoster());
    public IReadOnlyList<(string Transponder, long OffsetMs)> PendingReleases() => Read(static e => e.PendingReleases());

    /// <summary>See <see cref="RaceControlSession.IsOutOfTime"/>: internal there, for TimingWatch's host.</summary>
    internal bool IsOutOfTime(string transponderId) => Read(e => e.IsOutOfTime(transponderId));

    // ---- Commands: passes in. ----

    public void ProcessLapEvent(DirectLapEvent lap)
    {
        if (_host is not null)
        {
            Do(e =>
            {
                _crossingBeingJudgedMs = lap.CrossedMs;

                try
                {
                    e.ProcessLapEvent(lap);
                }
                finally
                {
                    _crossingBeingJudgedMs = 0;
                }
            });
            return;
        }

        // Inline (bug hunt 22): the pass was posted to this thread by the
        // decoder's and may be handled seconds after it arrived (the window
        // was busy). The engine already judges it by its arrival; the lap's
        // calls are now timed from it too, as threaded mode's are - so a
        // call the room would hear late is not said, and one that is said
        // has its say-by counted from the crossing (see
        // DeliveringEventRaisedAtMs and AnnouncementService.LapRaisedAtMs).
        // The age is on the engine's own clock, then put on TimingClock.
        var previous = DeliveringEventRaisedAtMs;
        var previousCrossed = DeliveringLapCrossedAtMs;
        var ageMs = Math.Max(0, Engine.NowMsSource() - Engine.ArrivalOf(lap));
        DeliveringEventRaisedAtMs = TimingClock.NowMs - ageMs;
        DeliveringLapCrossedAtMs = lap.CrossedMs > 0 ? lap.CrossedMs : null;

        try
        {
            Engine.ProcessLapEvent(lap);
        }
        finally
        {
            DeliveringEventRaisedAtMs = previous;
            DeliveringLapCrossedAtMs = previousCrossed;
        }
    }
    public bool ProcessSplitPass(string rowTransponder, TimingPoint point, long lineMs) => Do(e => e.ProcessSplitPass(rowTransponder, point, lineMs));

    // ---- Commands: the race's flags and clock. ----

    public bool BeginWarmUp(TimeSpan length) => Do(e => e.BeginWarmUp(length));
    public void EndWarmUp(string reason = "Warm-up over.") => Do(e => e.EndWarmUp(reason));
    public void Stage() => Do(static e => e.Stage());
    public void StartCountdown() => Do(static e => e.StartCountdown());

    /// <remarks>
    /// Awaits BeforeGreenFlagAsync (app code) before the green - see the
    /// engine. Threaded, the start rules (StartRulesOnUi) are applied first,
    /// on the UI thread, and the engine's own wait is the audio alone (P5).
    /// </remarks>
    public Task GreenFlagAfterAudioAsync() =>
        _host is null ? Do(static e => e.GreenFlagAfterAudioAsync()) : GreenAfterStartRules(static e => e.GreenFlagAfterAudioAsync());

    public Task GreenFlagAfterAudioAsync(long currentDeviceTimeMs) =>
        _host is null ? Do(e => e.GreenFlagAfterAudioAsync(currentDeviceTimeMs)) : GreenAfterStartRules(e => e.GreenFlagAfterAudioAsync(currentDeviceTimeMs));
    public void GreenFlagBefore(long crossingDeviceTimeMs) => Do(e => e.GreenFlagBefore(crossingDeviceTimeMs));
    public void GreenFlag(long currentDeviceTimeMs) => Do(e => e.GreenFlag(currentDeviceTimeMs));
    public void YellowFlag() => Do(static e => e.YellowFlag());
    public void RedFlag() => Do(static e => e.RedFlag());
    public void CheckeredFlag() => Do(static e => e.CheckeredFlag());
    public void Reset() => Do(static e => e.Reset());
    public void Tick() => Do(static e => e.Tick());

    // ---- Commands: the grid and the staggered release. ----

    public void SetGrid(IEnumerable<string> transponders, bool renumberOverStarters = false) => Do(e => e.SetGrid(transponders, renumberOverStarters));
    public void SetReleaseSchedule(IReadOnlyDictionary<string, long> offsetsMs) => Do(e => e.SetReleaseSchedule(offsetsMs));
    public void ReleaseDriver(string transponderId) => Do(e => e.ReleaseDriver(transponderId));
    public void ReleaseAll() => Do(static e => e.ReleaseAll());
    public void PollReleases() => Do(static e => e.PollReleases());

    // ---- Commands: standings, penalties and warnings. ----

    public bool RemoveGhostStanding(string transponderId) => Do(e => e.RemoveGhostStanding(transponderId));
    public bool AdjustLaps(string transponderId, int delta) => Do(e => e.AdjustLaps(transponderId, delta));
    public bool AddStandingByHand(string transponderId) => Do(e => e.AddStandingByHand(transponderId));
    public bool RemoveHandAddedStanding(string transponderId) => Do(e => e.RemoveHandAddedStanding(transponderId));
    public bool AdjustPenaltyTime(string transponderId, long deltaMs) => Do(e => e.AdjustPenaltyTime(transponderId, deltaMs));
    public long ClearPenalties(string transponderId) => Do(e => e.ClearPenalties(transponderId));
    /// <remarks>Threaded: a copy of the warning given (P4).</remarks>
    public RaceWarning? IssueWarning(string transponderId, string reason) =>
        _host is null ? Do(e => e.IssueWarning(transponderId, reason)) : Do(e => e.IssueWarning(transponderId, reason)?.Clone());

    /// <remarks>Threaded: a copy of the warning taken back (P4).</remarks>
    public RaceWarning? RemoveLastWarning(string transponderId) =>
        _host is null ? Do(e => e.RemoveLastWarning(transponderId)) : Do(e => e.RemoveLastWarning(transponderId)?.Clone());

    /// <remarks>
    /// <para>Runs <paramref name="change"/> (app code) inside the engine's edit - threaded, on the timing thread, where its own calls to this facade run inline.</para>
    /// <para>
    /// WHY (23 Oct 2026, timing layer, P4): the edit is handed back as it is,
    /// in both modes, not copied. It is a token: the app only holds it and
    /// passes it back to UndoStandingEdit (its members are the engine's),
    /// and its lists are already copies EditStanding took. What it does hold
    /// live - the race's own RaceWarning objects - it must: the undo puts a
    /// warning back BY REFERENCE, so a copy would put a second one on the
    /// board. It is only ever read inside the engine, on the timing thread.
    /// So <paramref name="change"/> must only call this facade - never the
    /// UI (today's three: Leaderboard's lap and penalty buttons, Clear
    /// Penalties and Take Back Warning, do only that).
    /// </para>
    /// </remarks>
    public RaceControlSession.StandingEdit? EditStanding(string transponderId, Func<bool> change) => Do(e => e.EditStanding(transponderId, change));

    public void UndoStandingEdit(RaceControlSession.StandingEdit edit) => Do(e => e.UndoStandingEdit(edit));

    // ---- Commands: snapshot and restore (undo and the journal). ----

    /// <remarks>The snapshot holds deep copies of the standings - see the engine.</remarks>
    public RaceControlSession.SessionSnapshot CreateSnapshot() => Do(static e => e.CreateSnapshot());

    public void RestoreSnapshot(RaceControlSession.SessionSnapshot snapshot) => Do(e => e.RestoreSnapshot(snapshot));
    public bool RestoreSnapshotAfterGap(RaceControlSession.SessionSnapshot snapshot, DateTime takenUtc, DateTime? nowUtc = null) => Do(e => e.RestoreSnapshotAfterGap(snapshot, takenUtc, nowUtc));

    // ---- Threaded mode: the timing side and the pass route (P4). ----

    /// <summary>
    /// Runs <paramref name="subscribe"/> with the live engine on the timing
    /// side - inline, there and then. For the few listeners that must hear
    /// the race ON the timing thread, with the live objects, whatever the
    /// window is doing (the crash journal, P4b): <c>e => e.LapProcessed += h</c>.
    /// A handler subscribed this way runs on the timing thread in threaded
    /// mode, so it must never touch the UI or wait for it.
    /// </summary>
    internal void AddTimingSideHandler(Action<RaceControlSession> subscribe)
    {
        ArgumentNullException.ThrowIfNull(subscribe);

        if (_host is null)
        {
            subscribe(Engine);
            return;
        }

        _host.Invoke(() => subscribe(Engine));
    }

    /// <summary>Takes a timing-side handler off again: <c>e => e.LapProcessed -= h</c>. See AddTimingSideHandler.</summary>
    internal void RemoveTimingSideHandler(Action<RaceControlSession> unsubscribe) => AddTimingSideHandler(unsubscribe);

    /// <summary>
    /// Threaded mode only: hands a line decoder's pass to the timing thread
    /// from the decoder's own thread, without waiting - so a frozen window
    /// can't hold it, whatever the race is doing. On the timing thread, in
    /// arrival order: <paramref name="uiSide"/> (the transponder watch, Line
    /// Up ...) is queued for the UI; then, when the race is Idle and the UI
    /// has armed a <see cref="PracticeAutoStart"/>, practice is started on
    /// this crossing (see StartPracticeOnTimingSide); then the pass is
    /// counted, there and then.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY (23 Oct 2026, timing layer, P4): passes reach the race in the
    /// order they arrived - every one goes the same road, onto the timing
    /// queue, so a pass can never overtake another.
    /// </para>
    /// <para>
    /// WHY (24 Oct 2026, timing layer, P7): a pass at Idle used to be sent
    /// round by the UI, and every pass behind it followed, only so practice
    /// could start on it before it was counted (MainForm.
    /// MaybeAutoStartPracticeOnGatePass). So a frozen window still held the
    /// passes up while no race ran, and the first practice lap waited for
    /// it. Now the UI says beforehand whether practice may start (the plan),
    /// the timing side decides at the crossing and starts it, and the window
    /// takes over the running practice when it gets to the notice.
    /// </para>
    /// </remarks>
    internal void PostLinePass(DirectLapEvent lap, Action<DirectLapEvent> uiSide)
    {
        ArgumentNullException.ThrowIfNull(lap);
        ArgumentNullException.ThrowIfNull(uiSide);

        if (_host is null)
        {
            throw new InvalidOperationException("PostLinePass is for threaded mode only - inline, passes take MainForm's own UI path.");
        }

        _host.Post(() =>
        {
            // The UI's share first, so the screens hear of the pass before its
            // lap - the order OnDecoderLapEvent has always done them in.
            EnqueueForUi(new UiItem(() => uiSide(lap), "the screens' share of a pass"));

            // Checked BEFORE the pass is counted, as OnDecoderLapEvent does:
            // the crossing that starts practice is the driver's start lap.
            if (TakePracticeAutoStart(lap) is { } plan)
            {
                StartPracticeOnTimingSide(plan, lap);
            }

            // At Idle with no practice started, the engine ignores the pass
            // (bar its clock reference), exactly as it did inline.
            Engine.ProcessLapEvent(lap);
        });
    }

    /// <summary>
    /// Threaded mode only: hands a split loop's pass to the timing thread
    /// without waiting, onto the same queue as the line's passes
    /// (<see cref="PostLinePass"/>), so the two keep the order they arrived
    /// in. On the timing thread: <paramref name="understand"/> works out
    /// what the read was (whose split transponder, when on the line's
    /// clock - lookups safe off the UI); <paramref name="uiSide"/> (the
    /// split loop's own lists, the console, Line Up) is queued for the UI
    /// with the reading; then, while a race with split loops is running,
    /// <paramref name="intoRace"/> picks the row from the standings' ids and
    /// the split goes into the race there and then. Generic so this library
    /// never sees the app's split types.
    /// </summary>
    /// <remarks>
    /// WHY (24 Oct 2026, timing layer, P8): split passes used to go round by
    /// the window (MainForm.RoutePass -> SplitLoop.Record -> RowForSplit ->
    /// ProcessSplitPass) while the line's came here on time. So during a
    /// freeze every lap was closed before its split read arrived, and when
    /// the window woke the reads could only fill the lap just finished - a
    /// frozen minute lost every sector time in it, or offered them as a
    /// missed lap. Inline both kinds of pass waited in the same queue, which
    /// is why this never happened there; now both wait in the timing one.
    /// </remarks>
    internal void PostSplitPass<TReading>(
        Func<TReading> understand,
        Action<TReading> uiSide,
        Func<TReading, IReadOnlyList<string>, (string Row, TimingPoint Point, long LineMs)?> intoRace)
        where TReading : class
    {
        ArgumentNullException.ThrowIfNull(understand);
        ArgumentNullException.ThrowIfNull(uiSide);
        ArgumentNullException.ThrowIfNull(intoRace);

        if (_host is null)
        {
            throw new InvalidOperationException("PostSplitPass is for threaded mode only - inline, split passes take MainForm's own UI path.");
        }

        _host.Post(() =>
        {
            TReading reading;

            try
            {
                reading = understand();
            }
            catch (Exception ex)
            {
                ReportFromTimingSide($"A split pass could not be read ({ex.GetType().Name}: {ex.Message}) - laps are not affected.");
                return;
            }

            // The UI's share first, so the split loop's console line and
            // PassRecorded come before any SectorsRecorded this read causes -
            // the order SplitLoop.Record has always done them in inline.
            EnqueueForUi(new UiItem(() => uiSide(reading), "the split loop's share of a split pass"));

            // ProcessSplitPass refuses these with nothing changed, so leaving
            // here is the same answer, without copying the standings' ids.
            if (Engine.SplitCount == 0 || Engine.State != RaceState.Racing)
            {
                return;
            }

            (string Row, TimingPoint Point, long LineMs)? into;

            try
            {
                // Only the rows' ids leave the engine, never a live standing.
                var rows = Engine.GetOrderedStandings().Select(s => s.TransponderId).ToList();
                into = intoRace(reading, rows);
            }
            catch (Exception ex)
            {
                ReportFromTimingSide($"A split pass could not be matched to a row ({ex.GetType().Name}: {ex.Message}) - laps are not affected.");
                return;
            }

            if (into is { } split)
            {
                Engine.ProcessSplitPass(split.Row, split.Point, split.LineMs);
            }
        });
    }

    // ---- Threaded mode: practice starting at the gate (P7). ----

    /// <summary>
    /// Threaded mode: whether practice may start at the next line pass made
    /// at Idle, and with which targets - or null for not. Set by the UI
    /// (MainForm) whenever anything it checks changes, and on its 500 ms
    /// tick; used once (the timing side clears it as it starts practice).
    /// Any thread: the plan is immutable and swapped whole. Inline there is
    /// no plan - MainForm starts practice itself, before the pass: refused.
    /// </summary>
    /// <remarks>
    /// WHY (24 Oct 2026, timing layer, P7): what decides whether practice may
    /// start lives on the UI (the setting, the recovery offer answered, Line
    /// Up shut, the Practice page there and not running); what decides
    /// whether it does - the race Idle, no warm-up, a real crossing - lives
    /// with the race. The UI hands its half over beforehand, so the timing
    /// thread can start practice on the crossing itself without asking the
    /// window, which it never waits for.
    /// </remarks>
    internal PracticeAutoStartPlan? PracticeAutoStart
    {
        get => Volatile.Read(ref _practiceAutoStart);
        set
        {
            if (_host is null)
            {
                throw new InvalidOperationException("PracticeAutoStart is for threaded mode only - inline, MainForm starts practice before the pass.");
            }

            Volatile.Write(ref _practiceAutoStart, value);
        }
    }

    /// <summary>
    /// On the timing thread, for a line pass: the armed plan, taken (cleared)
    /// so it starts practice once, when practice should start on this pass -
    /// a real crossing, the race Idle, no warm-up open, and no earlier
    /// auto-started practice still waiting for the UI to take it over. Null
    /// otherwise, leaving the plan as it is.
    /// </summary>
    private PracticeAutoStartPlan? TakePracticeAutoStart(DirectLapEvent lap)
    {
        // A pass the app made up has no decoder time to stamp the green with
        // (DirectLapEvent.IsSynthetic); none comes this way today. A warm-up
        // is not a RaceState - the session stays Idle through it - so it is
        // asked about on its own, as TryAutoStartPractice does.
        //
        // And not while the UI has yet to take over the last one: the plan it
        // armed was made before that practice existed, and the before-values
        // a second start would capture would be the first practice's targets.
        if (lap.IsSynthetic
            || Engine.State != RaceState.Idle
            || Engine.IsWarmingUp
            || Volatile.Read(ref _autoStartsOnTheirWay) > 0
            || Volatile.Read(ref _autoStartHolds) > 0
            || Volatile.Read(ref _practiceAutoStart) is not { } plan)
        {
            return null;
        }

        // Used once. Only if it is still this plan: one the UI has swapped in
        // meanwhile is its newer word, left for it to replace again.
        Interlocked.CompareExchange(ref _practiceAutoStart, null, plan);
        return plan;
    }

    /// <summary>
    /// On the timing thread: the engine's part of PracticeModeControl.
    /// StartPractice for a gate-pass start, done in one go so nothing can
    /// come between its steps - the session's targets and start type kept,
    /// Reset, practice mode on, the plan's targets, a standard start, and the
    /// green one millisecond before the crossing that started it. Then the
    /// notice is queued for the UI, after the start's own raises and before
    /// the pass's, for the Practice page to take the session over.
    /// </summary>
    private void StartPracticeOnTimingSide(PracticeAutoStartPlan plan, DirectLapEvent lap)
    {
        // Kept to hand back at Stop - read before anything below changes them,
        // as StartPractice reads them before its Reset.
        var lapLimitBefore = Engine.LapLimit;
        var timeLimitBefore = Engine.TimeLimit;
        var startTypeBefore = Engine.StartType;

        try
        {
            Engine.Reset();
            Engine.IsPracticeMode = true;
            Engine.LapLimit = plan.LapLimit;
            Engine.TimeLimit = plan.TimeLimit;
            Engine.StartType = RaceStartType.Standard;
            Engine.GreenFlagBefore(lap.DeviceTimeMs);
        }
        catch (Exception ex)
        {
            // Not expected - none of these throws today. A practice half
            // started, with no page running it, would leave Race Control
            // refusing every start: the session is handed back as Stop would,
            // and the pass is then counted (ignored) at Idle.
            try
            {
                HandBackPracticeSession(lapLimitBefore, timeLimitBefore, startTypeBefore);
            }
            catch
            {
                // Reported below either way.
            }

            ReportFromTimingSide($"Practice could not be started at the gate ({ex.GetType().Name}: {ex.Message}) - the session was handed back.");
            return;
        }

        var notice = new PracticeAutoStarted(lapLimitBefore, timeLimitBefore, startTypeBefore, lap);
        var pending = new PendingAutoStart(notice);
        _pendingAutoStart = pending;
        Interlocked.Increment(ref _autoStartsOnTheirWay);

        EnqueueForUi(new UiItem(() =>
        {
            try
            {
                // The window takes it over - unless a race start took it back
                // first, when the page is only told so (no take-over).
                if (Interlocked.CompareExchange(ref pending.State, PendingAutoStart.TakenOver, PendingAutoStart.Waiting) == PendingAutoStart.Waiting)
                {
                    plan.Adopt(notice);
                }
                else
                {
                    plan.Adopt(notice.AsCancelled());
                }
            }
            finally
            {
                Interlocked.Decrement(ref _autoStartsOnTheirWay);
            }
        }, "the Practice page taking over a practice started at the gate"));
    }

    /// <summary>The session handed back from a practice, as PracticeModeControl.StopPractice does: reset, practice mode off, the race's limits and start type back. Timing thread.</summary>
    private void HandBackPracticeSession(int? lapLimit, TimeSpan? timeLimit, RaceStartType startType)
    {
        Engine.Reset();
        Engine.IsPracticeMode = false;
        Engine.LapLimit = lapLimit;
        Engine.TimeLimit = timeLimit;
        Engine.StartType = startType;
    }

    /// <summary>A practice started at the gate, as the timing side keeps it until the window has it (or a race start takes it back).</summary>
    private sealed class PendingAutoStart
    {
        public const int Waiting = 0;
        public const int TakenOver = 1;
        public const int TakenBack = 2;

        public PendingAutoStart(PracticeAutoStarted notice) => Notice = notice;

        public PracticeAutoStarted Notice { get; }

        /// <summary>Waiting, TakenOver (the notice reached the window) or TakenBack (a race start won). Changed with Interlocked only.</summary>
        public int State;
    }

    /// <summary>
    /// Threaded mode: a race is being started from the UI (Stage, Countdown,
    /// Green, Quick Race, a warm-up, On Air, a heat from the program, a demo).
    /// The race wins over a practice the timing thread started at the gate
    /// that the window has NOT taken over yet: the session is handed back
    /// exactly as Stop hands it back, the take-over notice becomes a "cancelled"
    /// one, and no further practice starts at the gate until this UI message
    /// is over. Once the window has taken a practice over this does nothing,
    /// and the screen's own checks see a running practice, as always. Then
    /// what the window has not heard yet is delivered, so the caller's checks
    /// that follow read the session as it now is. Does nothing inline.
    /// </summary>
    /// <remarks>
    /// <para>
    /// WHY (24 Oct 2026, timing layer, P7): a click (or the warm-up's end,
    /// with cars still lapping) that found the session Idle could otherwise
    /// land on a practice a car started a moment later on the timing thread
    /// - refused as "practice is running", or worse, staged on top of it.
    /// Inline that could not happen: the pass waited for the click.
    /// </para>
    /// <para>
    /// Called at the very start of a race start, not inside the engine's
    /// Stage/Countdown/Green: by the time those run, the click has already
    /// written the format, the grid and the start rules to the session (each
    /// a command that would hand the window the take-over first), and a
    /// hand-back's Reset then would wipe them. The hold lasts until the UI
    /// context runs the posted release - the end of this UI message, or a
    /// modal dialog's message loop if the start shows one first.
    /// </para>
    /// </remarks>
    internal void TakeBackPracticeAutoStartForRace()
    {
        if (_host is null)
        {
            return;
        }

        // The hold first, so a pass after the check below cannot start one.
        Interlocked.Increment(ref _autoStartHolds);

        try
        {
            _uiContext!.Post(static state => Interlocked.Decrement(ref ((RaceTiming)state!)._autoStartHolds), this);
        }
        catch
        {
            // The window is going: nothing to hold for.
            Interlocked.Decrement(ref _autoStartHolds);
        }

        var tookBack = _host.Invoke(CancelPendingAutoStart);

        if (tookBack && OnUiThread && _deliveryDepth == 0)
        {
            DeliverToUi();
        }
    }

    /// <summary>Threaded mode: no practice starts at the gate until the returned hold is disposed - for a race start that spends time at Idle first (On Air). Any thread. Inline: a hold that does nothing.</summary>
    /// <remarks>
    /// WHY (24 Oct 2026, timing layer, P8): On Air runs between races with the
    /// session Idle, sometimes for minutes, before its hand-over stages the
    /// race. TakeBackPracticeAutoStartForRace's hold lasts only until the end
    /// of the UI message it is called in, so a car warming up during the
    /// opening started practice at the gate on the timing thread - and On Air
    /// was then called off ("the race was moved on"). This hold lasts as long
    /// as the caller keeps it. Disposing it twice lets go once.
    /// </remarks>
    internal IDisposable HoldPracticeAutoStart()
    {
        if (_host is null)
        {
            return NoAutoStartHold.Instance;
        }

        Interlocked.Increment(ref _autoStartHolds);
        return new AutoStartHold(this);
    }

    /// <summary>A hold on practice starting at the gate - see HoldPracticeAutoStart. Let go once, however often it is disposed.</summary>
    private sealed class AutoStartHold : IDisposable
    {
        private RaceTiming? _owner;

        public AutoStartHold(RaceTiming owner) => _owner = owner;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _owner, null) is { } owner)
            {
                Interlocked.Decrement(ref owner._autoStartHolds);
            }
        }
    }

    /// <summary>The inline hold: nothing to hold, since practice starts at the gate on the UI thread there.</summary>
    private sealed class NoAutoStartHold : IDisposable
    {
        public static readonly NoAutoStartHold Instance = new();

        public void Dispose()
        {
        }
    }

    /// <summary>On the timing thread: hands back a practice started at the gate that the window has not taken over. True when it did.</summary>
    private bool CancelPendingAutoStart()
    {
        if (_pendingAutoStart is not { } pending)
        {
            return false;
        }

        _pendingAutoStart = null;

        if (Interlocked.CompareExchange(ref pending.State, PendingAutoStart.TakenBack, PendingAutoStart.Waiting) != PendingAutoStart.Waiting)
        {
            // The window has it: the screens' own checks see a running practice.
            return false;
        }

        // Still the gate practice (nothing else turns practice mode off but
        // the page): handed back as Stop would. Otherwise someone else has
        // the session already, and it is left to them.
        if (Engine.IsPracticeMode)
        {
            HandBackPracticeSession(pending.Notice.LapLimitBefore, pending.Notice.TimeLimitBefore, pending.Notice.StartTypeBefore);
        }

        return true;
    }

    /// <summary>
    /// Threaded mode, on the UI thread: waits for the timing thread to finish
    /// what it was handed, then delivers everything the window has not heard
    /// yet - so a practice already started at the gate is taken over now. For
    /// the Practice page's own Start, which then finds that practice running
    /// instead of a "race" it would refuse with a dialog. Does nothing inline,
    /// off the UI thread, or inside a delivery (whose order it would break).
    /// </summary>
    internal void CatchUpWithTiming()
    {
        if (_host is null || !OnUiThread || _deliveryDepth > 0)
        {
            return;
        }

        _host.Invoke(static () => { });
        DeliverToUi();
    }

    // ---- Threaded mode: copies (P4). ----

    private static IReadOnlyList<RacerStanding> CopyOf(IReadOnlyList<RacerStanding> standings)
    {
        var copies = new List<RacerStanding>(standings.Count);

        foreach (var standing in standings)
        {
            copies.Add(standing.Clone());
        }

        return copies;
    }

    private static List<WarningStep>? CopyOf(List<WarningStep>? ladder) =>
        ladder?.Select(step => new WarningStep(step.Action, step.PenaltySeconds)).ToList();

    // ---- Threaded mode: the start (P5). ----

    /// <summary>
    /// The engine's side of the start gate, threaded: starts <paramref name="gate"/>
    /// on a pool thread and answers a task that completes when it does
    /// (faulted the same way), or after <see cref="GreenAudioCap"/> if it has
    /// not by then. Awaited on the timing thread, it resumes there
    /// (TimingHost's context) without ever blocking it, and it never needs
    /// the UI. See BeforeGreenFlagAsync.
    /// </summary>
    private static Func<Task> OffTheTimingThread(Func<Task> gate) => () =>
    {
        Task wait;

        try
        {
            wait = Task.Run(gate);
        }
        catch (Exception ex)
        {
            // Not started at all: a failed wait, which the engine catches
            // and starts the race - as inline.
            return Task.FromException(ex);
        }

        return CappedAsync(wait);
    };

    /// <summary>The gate's wait, given up after GreenAudioCap. A gate that failed stays failed (the engine starts the race regardless).</summary>
    private static async Task CappedAsync(Task wait)
    {
        var finished = await Task.WhenAny(wait, Task.Delay(GreenAudioCap)).ConfigureAwait(false);

        if (finished == wait)
        {
            await wait.ConfigureAwait(false);
        }
    }

    /// <summary>
    /// What a get of BeforeGreenFlagAsync answers, threaded: the gate as set,
    /// with the start rules run in front of it when there are any. Run on
    /// the UI thread by whoever awaits it (the demo does, on its own start).
    /// </summary>
    private Func<Task>? GateWithStartRules()
    {
        var gate = Volatile.Read(ref _gateAsSet);

        if (Volatile.Read(ref _startRules) is not { } rules)
        {
            return gate;
        }

        return async () =>
        {
            rules();

            if (gate is not null)
            {
                await gate();
            }
        };
    }

    /// <summary>
    /// A green asked for from the app, threaded: the start rules on the UI
    /// thread first (a real start only - the engine waits for no gate when
    /// it is already racing, and nor do these), then the green itself.
    /// </summary>
    /// <remarks>
    /// The rules are applied by this caller and finished before the green is
    /// asked for, so the green's wait on the timing thread is for the audio
    /// alone. Every app path asks for the green from the UI thread (Race
    /// Control, the start sequence, practice). From another thread the rules
    /// are sent to the UI and waited for there - any thread but the timing
    /// thread may wait for the UI; from the timing thread itself they cannot
    /// be, and are left out with a line in the log.
    /// </remarks>
    private Task GreenAfterStartRules(Func<RaceControlSession, Task> green)
    {
        if (Volatile.Read(ref _startRules) is { } rules && Read(static e => e.State) != RaceState.Racing)
        {
            try
            {
                if (OnUiThread)
                {
                    rules();
                }
                else if (_host!.IsTimingThread)
                {
                    ReportFromTimingSide("The start rules could not be applied - the green was asked for on the timing thread, which never waits for the window. The race starts without them.");
                }
                else
                {
                    _uiContext!.Send(static state => ((Action)state!)(), rules);
                }
            }
            catch (Exception ex)
            {
                // As inline, where they ran inside the gate: start rules that
                // fail never stop the race from starting.
                ReportFromTimingSide($"The start rules failed ({ex.GetType().Name}: {ex.Message}) - the race starts all the same.");
            }
        }

        return Do(green);
    }

    // ---- Threaded mode: the automatic start's green on the timing tick (P8). ----

    /// <summary>
    /// Threaded mode, UI thread: the automatic start hands its green to the timing tick. The start rules
    /// (StartRulesOnUi) are applied here, now, on the UI; then the green is asked for on the timing
    /// thread once <paramref name="audioFirst"/> has finished (or <paramref name="audioCap"/> has passed)
    /// and <paramref name="notBefore"/> has elapsed - unless the session moved on, or a yellow or red is
    /// out (AutoStartMayContinue's rule). False, doing nothing, inline or off the UI thread.
    /// </summary>
    /// <remarks>
    /// WHY (24 Oct 2026, timing layer, P8): the countdown's ticks run on a pool
    /// thread and end on time, and line passes reach the timing thread on time
    /// - but the green was asked for by the UI, after its wait. A frozen window
    /// held the green while the room heard the countdown end, and every car
    /// that crossed the line meanwhile was booked a jump start. Now the timing
    /// tick asks for it, after the same waits: the clock starts on time, and
    /// only the horn (the announcer's, on the UI) can be late. The green still
    /// waits for the countdown audio - the owner's decision - and the engine's
    /// own gate (the audio wait, capped at GreenAudioCap) is unchanged.
    /// </remarks>
    internal bool ArmGreenAfterCountdown(TimeSpan notBefore, Func<CancellationToken, Task>? audioFirst, TimeSpan audioCap)
    {
        if (_host is null || !OnUiThread)
        {
            return false;
        }

        // As GreenAfterStartRules applies them for a green asked for on the UI.
        if (Volatile.Read(ref _startRules) is { } rules && Read(static e => e.State) != RaceState.Racing)
        {
            try
            {
                rules();
            }
            catch (Exception ex)
            {
                ReportFromTimingSide($"The start rules failed ({ex.GetType().Name}: {ex.Message}) - the race starts all the same.");
            }
        }

        var cts = new CancellationTokenSource();
        Task audio;

        if (audioFirst is null)
        {
            audio = Task.CompletedTask;
        }
        else
        {
            var token = cts.Token;
            audio = CappedQuietly(Task.Run(() => audioFirst(token)), audioCap);
        }

        var notBeforeMs = (long)notBefore.TotalMilliseconds;

        _host.Invoke(() =>
        {
            _armedGreen?.Cancel();
            _armedGreen = new ArmedGreen(Engine.SessionGeneration, TimingClock.NowMs + notBeforeMs, audio, cts);
        });

        return true;
    }

    /// <summary>Threaded: drops an armed green (CancelAutoStart). Waits for the timing thread. Inline: nothing.</summary>
    internal void DisarmGreenAfterCountdown()
    {
        _host?.Invoke(() =>
        {
            _armedGreen?.Cancel();
            _armedGreen = null;
        });
    }

    /// <summary>
    /// On the timing tick: asks for the armed green once its audio wait is
    /// over and its time has come. Dropped silently when somebody moved the
    /// race on (as AutoStartMayContinue); stopped with a line in the log
    /// under a yellow or red, as the automatic start stops on the UI.
    /// </summary>
    private void FireArmedGreenIfDue()
    {
        if (_armedGreen is not { } armed)
        {
            return;
        }

        if (Engine.SessionGeneration != armed.Generation || Engine.State != RaceState.Countdown)
        {
            _armedGreen = null;
            armed.Cancel();
            return;
        }

        if (!armed.Audio.IsCompleted || TimingClock.NowMs < armed.NotBeforeMs)
        {
            return;
        }

        _armedGreen = null;

        if (Engine.Flag is FlagState.Yellow or FlagState.Red)
        {
            ReportFromTimingSide($"Automatic start stopped at Countdown - the {Engine.Flag.ToString().ToLowerInvariant()} flag is out. Press Green when the track is ready, or Stage again to restart the sequence.");
            return;
        }

        // The engine's own gate is the audio wait alone, capped (P5), and its
        // overrule checks are unchanged. Its await resumes here, on the
        // timing thread, through TimingHost's context.
        _ = Engine.GreenFlagAfterAudioAsync();
    }

    /// <summary>The armed wait for the countdown audio: done when it ends or <paramref name="cap"/> passes, and never faulted or cancelled (as ChainAfterAudio's catch).</summary>
    private static async Task CappedQuietly(Task wait, TimeSpan cap)
    {
        try
        {
            await Task.WhenAny(wait, Task.Delay(cap)).ConfigureAwait(false);
        }
        catch
        {
            // A failed wait must not strand the start.
        }

        // Observed, so a wait that fails after the cap is not reported unobserved.
        _ = wait.ContinueWith(static t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
    }

    /// <summary>The automatic start's green as armed - see ArmGreenAfterCountdown.</summary>
    private sealed class ArmedGreen
    {
        private readonly CancellationTokenSource _cts;

        public ArmedGreen(int generation, long notBeforeMs, Task audio, CancellationTokenSource cts)
        {
            Generation = generation;
            NotBeforeMs = notBeforeMs;
            Audio = audio;
            _cts = cts;
        }

        /// <summary>The session it was armed for (SessionGeneration).</summary>
        public int Generation { get; }

        /// <summary>The earliest it may drop, on TimingClock.</summary>
        public long NotBeforeMs { get; }

        /// <summary>Completes when the audio wait ends or its cap runs out; never faults.</summary>
        public Task Audio { get; }

        /// <summary>Stops the audio wait it started, if it is still running. Any thread; never runs the wait's own code on the caller's.</summary>
        public void Cancel()
        {
            // WHY (24 Oct 2026, timing layer, P8): CancelAsync, not Cancel. This
            // runs on the timing thread, and Cancel would run the announcer's
            // wait (its Task.Delay continuations) right here; CancelAsync runs
            // them on the pool, as the gate's wait is always run (P5).
            try
            {
                _ = _cts.CancelAsync();
            }
            catch (ObjectDisposedException)
            {
                // Already finished with.
            }
        }
    }

    /// <summary>
    /// A ghost's crossing, threaded mode, from the ghosts' own timer thread:
    /// counted only while the race is Racing, checked and counted in one go
    /// on the timing thread so nothing can come between the two. True when it
    /// was counted. Inline the ghosts check State and count on the UI thread
    /// themselves, as they always did.
    /// </summary>
    internal bool ProcessLapEventWhileRacing(DirectLapEvent lap)
    {
        ArgumentNullException.ThrowIfNull(lap);

        return Do(e =>
        {
            if (e.State != RaceState.Racing)
            {
                return false;
            }

            e.ProcessLapEvent(lap);
            return true;
        });
    }

    // ---- Threaded mode: events to the UI (P4). ----

    /// <summary>The engine's 18 events, one slot each in _uiHandlers.</summary>
    private enum EventSlot
    {
        SectorsRecorded,
        SplitCrossed,
        PossibleMissedLap,
        StandingRemoved,
        JumpStartDetected,
        StateChanged,
        FlagChanged,
        LapProcessed,
        PersonalBestLap,
        StandingAdjusted,
        RaceCompleted,
        LastLapStarted,
        FinalMinute,
        TimeLimitReached,
        LogMessage,
        WarningIssued,
        WarmUpChanged,
        DriverReleased,
    }

    private const int EventSlotCount = (int)EventSlot.DriverReleased + 1;

    /// <summary>One thing waiting for the UI: an event's raise (its slot and how to call a handler with the copied arguments), or a piece of pass work.</summary>
    private sealed class UiItem
    {
        public UiItem(EventSlot slot, Action<Delegate> raise)
        {
            Slot = slot;
            Raise = raise;
            What = $"A {slot} handler";
            RaisedAtMs = TimingClock.NowMs;
        }

        public UiItem(Action work, string what)
        {
            Work = work;
            What = what;
            RaisedAtMs = TimingClock.NowMs;
        }

        public EventSlot Slot { get; }

        public Action<Delegate>? Raise { get; }

        public Action? Work { get; }

        public string What { get; }

        public long RaisedAtMs { get; }

        /// <summary>Its place in the queue, set as it is queued.</summary>
        public long Seq { get; set; }

        /// <summary>For a RaceCompleted raise: the race as it finished (see DeliveringFinishedRace). Null for anything else.</summary>
        public FinishedRace? Finished { get; init; }

        /// <summary>For a raise made while a pass was judged: its decoder-mapped crossing time (see DeliveringLapCrossedAtMs); 0 otherwise.</summary>
        public long CrossedAtMs { get; init; }
    }

    private bool HasUiHandlers(EventSlot slot) => Volatile.Read(ref _uiHandlers[(int)slot]) is not null;

    private void ToUi(EventSlot slot, Action<Delegate> raise) => EnqueueForUi(new UiItem(slot, raise) { CrossedAtMs = _crossingBeingJudgedMs });

    /// <summary>Queues for the UI and makes sure a delivery is posted. Any thread; never waits.</summary>
    private void EnqueueForUi(UiItem item)
    {
        lock (_forUi)
        {
            item.Seq = _nextUiSeq++;
            _forUi.AddLast(item);
        }

        EnsureDeliveryPosted();
    }

    /// <summary>Makes sure one delivery is posted to the UI context. Any thread; never waits.</summary>
    private void EnsureDeliveryPosted()
    {
        if (Interlocked.Exchange(ref _deliveryPosted, 1) == 1)
        {
            return;
        }

        try
        {
            _uiContext!.Post(static state => ((RaceTiming)state!).DeliverPosted(), this);
        }
        catch
        {
            // The window is going: nothing can be delivered any more. Let
            // the next raise try again rather than never.
            Volatile.Write(ref _deliveryPosted, 0);
        }
    }

    /// <summary>The posted delivery: everything waiting, oldest first.</summary>
    private void DeliverPosted()
    {
        // Before taking anything: a raise queued from here on posts again.
        Volatile.Write(ref _deliveryPosted, 0);
        DeliverToUi();
    }

    /// <summary>The next item to deliver: the oldest of all.</summary>
    /// <remarks>
    /// WHY (24 Oct 2026, timing layer, P7): a pass sent round by the UI used
    /// to stop a delivery made inside a UI command, so practice could not be
    /// started on it in the middle of a click. No pass goes round by the UI
    /// any more - practice starts on the timing side (PostLinePass) - so
    /// every delivery takes everything, oldest first. The Practice page's
    /// take-over notice is delivered like an event: a click that issues a
    /// command hears of the practice before its own command runs, as it
    /// hears of any other change the race made first.
    /// </remarks>
    private UiItem? TakeForUi()
    {
        lock (_forUi)
        {
            if (_forUi.First is not { } first)
            {
                return null;
            }

            _forUi.RemoveFirst();
            return first.Value;
        }
    }

    /// <summary>
    /// For a command issued inside a delivery: the oldest event raise the
    /// command itself made (numbered from <paramref name="from"/> up to, not
    /// including, <paramref name="to"/>), leaving older items, pass work and
    /// anything raised since where they are.
    /// </summary>
    private UiItem? TakeOwnRaise(long from, long to)
    {
        lock (_forUi)
        {
            // From the newest back: a command's own raises are near the end.
            LinkedListNode<UiItem>? earliest = null;

            for (var node = _forUi.Last; node is not null && node.Value.Seq >= from; node = node.Previous)
            {
                if (node.Value.Work is null && node.Value.Seq < to)
                {
                    earliest = node;
                }
            }

            if (earliest is null)
            {
                return null;
            }

            _forUi.Remove(earliest);
            return earliest.Value;
        }
    }

    /// <summary>
    /// Delivers what is waiting for the UI, oldest first. UI thread only.
    /// Each handler is called on its own and a failing one is logged and
    /// skipped, as the engine's SafeRaise does inline.
    /// </summary>
    /// <remarks>
    /// WHY (24 Oct 2026, timing layer, P7): this used to stop, inside a
    /// command the UI issued, at a pass sent round by the UI (see TakeForUi),
    /// and post a delivery for what it left. Nothing is left now: it takes
    /// everything there is.
    /// </remarks>
    private void DeliverToUi() => DeliverEach(TakeForUi);

    /// <summary>Delivers a nested command's own raises - see TakeOwnRaise. UI thread only.</summary>
    private void DeliverOwnRaises(long from, long to) => DeliverEach(() => TakeOwnRaise(from, to));

    private void DeliverEach(Func<UiItem?> take)
    {
        _deliveryDepth++;

        try
        {
            while (take() is { } item)
            {
                var previous = DeliveringEventRaisedAtMs;
                var previousCrossed = DeliveringLapCrossedAtMs;
                var previousFinished = DeliveringFinishedRace;
                DeliveringEventRaisedAtMs = item.RaisedAtMs;
                DeliveringLapCrossedAtMs = item.CrossedAtMs > 0 ? item.CrossedAtMs : null;
                DeliveringFinishedRace = item.Finished;

                try
                {
                    Deliver(item);
                }
                finally
                {
                    DeliveringEventRaisedAtMs = previous;
                    DeliveringLapCrossedAtMs = previousCrossed;
                    DeliveringFinishedRace = previousFinished;
                }
            }
        }
        finally
        {
            _deliveryDepth--;
        }
    }

    private void Deliver(UiItem item)
    {
        if (item.Work is { } work)
        {
            try
            {
                work();
            }
            catch (Exception ex)
            {
                LogOnUi($"{char.ToUpperInvariant(item.What[0])}{item.What[1..]} failed and was skipped ({ex.GetType().Name}: {ex.Message}). The race carries on.");
            }

            return;
        }

        if (Volatile.Read(ref _uiHandlers[(int)item.Slot]) is not { } handlers)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                item.Raise!(handler);
            }
            catch (Exception ex)
            {
                LogOnUi($"{item.What} failed and was skipped ({ex.GetType().Name}: {ex.Message}). The race carries on.");
            }
        }
    }

    /// <summary>Says something in the race log on the UI thread, to the UI's LogMessage handlers - each on its own, never throwing.</summary>
    private void LogOnUi(string message)
    {
        if (Volatile.Read(ref _uiHandlers[(int)EventSlot.LogMessage]) is not { } handlers)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action<string>)handler)(message);
            }
            catch
            {
                // Nowhere left to report it.
            }
        }
    }

    /// <summary>
    /// One handler per engine event, on the timing thread: when the UI has
    /// handlers for it, the arguments are copied there and then and the raise
    /// is queued for the UI. Wired once, by the threaded constructor.
    /// </summary>
    private void ForwardEventsToUi(RaceControlSession e)
    {
        e.SectorsRecorded += (standing, lapIndex, sectors) =>
        {
            if (HasUiHandlers(EventSlot.SectorsRecorded))
            {
                var copy = standing.Clone();
                var sectorsCopy = (long[])sectors?.Clone()!;
                ToUi(EventSlot.SectorsRecorded, h => ((Action<RacerStanding, int, long[]>)h)(copy, lapIndex, sectorsCopy));
            }
        };

        e.SplitCrossed += (standing, point, sinceLineMs) =>
        {
            if (HasUiHandlers(EventSlot.SplitCrossed))
            {
                var copy = standing.Clone();
                ToUi(EventSlot.SplitCrossed, h => ((Action<RacerStanding, TimingPoint, long>)h)(copy, point, sinceLineMs));
            }
        };

        e.PossibleMissedLap += (standing, firstMs, secondMs) =>
        {
            if (HasUiHandlers(EventSlot.PossibleMissedLap))
            {
                var copy = standing.Clone();
                ToUi(EventSlot.PossibleMissedLap, h => ((Action<RacerStanding, long, long>)h)(copy, firstMs, secondMs));
            }
        };

        e.StandingRemoved += standing =>
        {
            if (HasUiHandlers(EventSlot.StandingRemoved))
            {
                var copy = standing.Clone();
                ToUi(EventSlot.StandingRemoved, h => ((Action<RacerStanding>)h)(copy));
            }
        };

        e.JumpStartDetected += (transponder, earlyByMs) =>
        {
            if (HasUiHandlers(EventSlot.JumpStartDetected))
            {
                ToUi(EventSlot.JumpStartDetected, h => ((Action<string, long>)h)(transponder, earlyByMs));
            }
        };

        e.StateChanged += state =>
        {
            if (HasUiHandlers(EventSlot.StateChanged))
            {
                ToUi(EventSlot.StateChanged, h => ((Action<RaceState>)h)(state));
            }
        };

        e.FlagChanged += flag =>
        {
            if (HasUiHandlers(EventSlot.FlagChanged))
            {
                ToUi(EventSlot.FlagChanged, h => ((Action<FlagState>)h)(flag));
            }
        };

        e.LapProcessed += (standing, valid, isStartLap) =>
        {
            if (HasUiHandlers(EventSlot.LapProcessed))
            {
                var copy = standing.Clone();
                ToUi(EventSlot.LapProcessed, h => ((Action<RacerStanding, bool, bool>)h)(copy, valid, isStartLap));
            }
        };

        e.PersonalBestLap += standing =>
        {
            if (HasUiHandlers(EventSlot.PersonalBestLap))
            {
                var copy = standing.Clone();
                ToUi(EventSlot.PersonalBestLap, h => ((Action<RacerStanding>)h)(copy));
            }
        };

        e.StandingAdjusted += standing =>
        {
            if (HasUiHandlers(EventSlot.StandingAdjusted))
            {
                var copy = standing.Clone();
                ToUi(EventSlot.StandingAdjusted, h => ((Action<RacerStanding>)h)(copy));
            }
        };

        e.RaceCompleted += () =>
        {
            if (HasUiHandlers(EventSlot.RaceCompleted))
            {
                // The race as it finished, frozen here and now, for the
                // filing that runs when the UI gets to this (P4b) - see
                // DeliveringFinishedRace.
                EnqueueForUi(new UiItem(EventSlot.RaceCompleted, static h => ((Action)h)())
                {
                    Finished = FinishedRace.Of(e),
                });
            }
        };

        e.LastLapStarted += standing =>
        {
            if (HasUiHandlers(EventSlot.LastLapStarted))
            {
                var copy = standing.Clone();
                ToUi(EventSlot.LastLapStarted, h => ((Action<RacerStanding>)h)(copy));
            }
        };

        e.FinalMinute += () =>
        {
            if (HasUiHandlers(EventSlot.FinalMinute))
            {
                ToUi(EventSlot.FinalMinute, static h => ((Action)h)());
            }
        };

        e.TimeLimitReached += () =>
        {
            if (HasUiHandlers(EventSlot.TimeLimitReached))
            {
                ToUi(EventSlot.TimeLimitReached, static h => ((Action)h)());
            }
        };

        e.LogMessage += message =>
        {
            if (HasUiHandlers(EventSlot.LogMessage))
            {
                ToUi(EventSlot.LogMessage, h => ((Action<string>)h)(message));
            }
        };

        e.WarningIssued += (standing, warning) =>
        {
            if (HasUiHandlers(EventSlot.WarningIssued))
            {
                var copy = standing.Clone();
                var warningCopy = warning.Clone();
                ToUi(EventSlot.WarningIssued, h => ((Action<RacerStanding, RaceWarning>)h)(copy, warningCopy));
            }
        };

        e.WarmUpChanged += () =>
        {
            if (HasUiHandlers(EventSlot.WarmUpChanged))
            {
                ToUi(EventSlot.WarmUpChanged, static h => ((Action)h)());
            }
        };

        e.DriverReleased += (transponder, offsetMs) =>
        {
            if (HasUiHandlers(EventSlot.DriverReleased))
            {
                ToUi(EventSlot.DriverReleased, h => ((Action<string, long>)h)(transponder, offsetMs));
            }
        };
    }
}

/// <summary>
/// The race as the live state document reads it, from one moment - see
/// <see cref="RaceTiming.ReadLiveView"/>. Added 24 Oct 2026 (timing layer, P8).
/// </summary>
/// <param name="DeviceNowMs">
/// What the decoder's clock read at that moment, as best the race knows
/// (EstimatedDeviceTimeNowMs) - what a row's last lap is aged against for
/// the relay (13 Nov 2026, bug hunt 17; see RelayLapAge in the app).
/// </param>
internal sealed record LiveRaceView(
    IReadOnlyList<RacerStanding> Ordered,
    RaceState State,
    FlagState Flag,
    string Countdown,
    int Generation,
    int StreakThreshold,
    double[]? SectorLengthsFeet,
    long DeviceNowMs);

/// <summary>
/// A finished race as it stood the moment it finished - frozen on the timing
/// thread as RaceCompleted was raised (threaded mode): what filing reads, so
/// what is filed is the race that finished. See
/// <see cref="RaceTiming.DeliveringFinishedRace"/>.
/// </summary>
/// <remarks>Added 23 Oct 2026 (timing layer, P4b). No WinForms: it goes to the timing class library with RaceTiming (P6).</remarks>
internal sealed class FinishedRace
{
    private readonly HashSet<string> _jumpStarted;

    private FinishedRace(IReadOnlyList<RacerStanding> standings, DateTime? raceStartUtc, bool isPracticeMode, int sessionGeneration, HashSet<string> jumpStarted, TimeSpan? timeLimit, int? lapLimit, DateTime? stagedAtUtc)
    {
        Standings = standings;
        StagedAtUtc = stagedAtUtc;
        TimeLimit = timeLimit;
        LapLimit = lapLimit;
        RaceStartUtc = raceStartUtc;
        IsPracticeMode = isPracticeMode;
        SessionGeneration = sessionGeneration;
        _jumpStarted = jumpStarted;
    }

    /// <summary>The finishing order: deep copies (RacerStanding.Clone), as GetOrderedStandings ordered them.</summary>
    public IReadOnlyList<RacerStanding> Standings { get; }

    /// <summary>As the race's RaceStartUtc read.</summary>
    public DateTime? RaceStartUtc { get; }

    /// <summary>As the race's StagedAtUtc read - when it was first called to the grid (11 Nov 2026, night pace).</summary>
    public DateTime? StagedAtUtc { get; }

    /// <summary>As the race's IsPracticeMode read.</summary>
    public bool IsPracticeMode { get; }

    /// <summary>As the race's SessionGeneration read.</summary>
    public int SessionGeneration { get; }

    /// <summary>As the race's TimeLimit read - filed with the result so its length can be shown (28 Oct 2026).</summary>
    public TimeSpan? TimeLimit { get; }

    /// <summary>As the race's LapLimit read.</summary>
    public int? LapLimit { get; }

    /// <summary>As the race's HasJumpStarted answered, for every car in <see cref="Standings"/>; false for any other.</summary>
    public bool HasJumpStarted(string transponderId) => _jumpStarted.Contains(transponderId);

    /// <summary>Freezes <paramref name="engine"/> as it is now. On the thread that owns the engine.</summary>
    internal static FinishedRace Of(RaceControlSession engine)
    {
        var live = engine.GetOrderedStandings();
        var standings = new List<RacerStanding>(live.Count);
        var jumpStarted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var standing in live)
        {
            standings.Add(standing.Clone());

            if (engine.HasJumpStarted(standing.TransponderId))
            {
                jumpStarted.Add(standing.TransponderId);
            }
        }

        return new FinishedRace(standings, engine.RaceStartUtc, engine.IsPracticeMode, engine.SessionGeneration, jumpStarted, engine.TimeLimit, engine.LapLimit, engine.StagedAtUtc);
    }
}
