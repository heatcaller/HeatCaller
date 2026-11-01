namespace HeatCaller;

/// <summary>
/// Runs virtual opponents during a session.
///
/// Ghosts feed the SAME lap pipeline as real hardware - they emit
/// DirectLapEvents with reserved transponder IDs. That's deliberate:
/// everything downstream (standings, personal bests, lead-change
/// announcements, the leaderboard, the big screen) then works on a ghost
/// without a single one of them needing to know ghosts exist.
///
/// The alternative - a parallel "virtual standings" system - would mean
/// reimplementing all of that and keeping two versions in step.
/// </summary>
public sealed class GhostService : IDisposable
{
    private readonly RaceTiming _race;
    private readonly ProfileStore _profiles;
    private readonly SessionHistoryStore _history;
    private readonly TrackLayoutStore? _trackLayouts;
    private readonly PersonalBestResetStore? _pbResets;

    private readonly Dictionary<string, GhostRunner> _runners = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _lock = new();
    private readonly Random _random = new();

    private System.Threading.Timer? _timer;
    private bool _running;

    /// <summary>
    /// Which run of the ghosts this is, bumped by every Start and Stop
    /// (under _lock). A crossing carries the run it was scheduled in, and is
    /// dropped on the UI thread if the ghosts have been stopped and started
    /// again since.
    /// </summary>
    /// <remarks>
    /// 17 Oct 2026, Bug Hunt 8, A28-2: Practice "Clear Stats" resets the race
    /// and green-flags it again synchronously, and the ghosts are stopped and
    /// started with the same transponders. A crossing posted just before that
    /// still passed the "still running and racing" check and landed in the new
    /// practice on the old run's clock - one bogus very long lap, then one
    /// dropped lap for that ghost. SessionGeneration is not enough on its own,
    /// as a race put back can restore it.
    /// </remarks>
    private int _runId;

    /// <summary>
    /// The ghosts' clock: milliseconds since the green flag.
    ///
    /// It used to be anchored to the session's LastKnownDeviceTimeMs plus
    /// wall time. That value is the time of the last REAL pass, which can be
    /// minutes old when the ghosts start - so ghost times ran minutes behind
    /// the decoder's clock, and every gap between a ghost and a real car
    /// (and the running order on the same lap) was wrong by that much. With
    /// no pass seen since the app opened the anchor was 0, which put the
    /// real driver an hour behind their ghosts.
    ///
    /// Ghost passes are marked synthetic, and the session measures them
    /// against the green directly, so the two clocks never have to agree.
    /// </summary>
    /// <remarks>
    /// Read off the session's monotonic clock (MsSinceGreen) rather than
    /// DateTime.UtcNow, so a Windows time sync mid-race does not bunch the
    /// ghosts' crossings together or hold them back.
    /// </remarks>
    private long NowMs => Math.Max(0, _race.MsSinceGreen);

    /// <summary>
    /// The UI thread, captured at Start. Passes are handed back to it:
    /// the timer runs on a pool thread, and the session isn't thread-safe -
    /// a ghost crossing at the same moment as a real one (which arrives on
    /// the UI thread) could corrupt the standings or throw while the
    /// leaderboard was reading them.
    /// </summary>
    private SynchronizationContext? _uiContext;

    public GhostService(RaceTiming race, ProfileStore profiles, SessionHistoryStore history,
        TrackLayoutStore? trackLayouts = null, PersonalBestResetStore? pbResets = null)
    {
        _race = race;
        _profiles = profiles;
        _history = history;
        _trackLayouts = trackLayouts;
        _pbResets = pbResets;
    }

    /// <summary>The track layout this session is running on, or null when none is selected.</summary>
    private string? _trackLayoutName;

    /// <summary>
    /// What each driver is running right now, for the live-pace ghosts.
    ///
    /// Held as a snapshot rather than read from the session directly: the
    /// ghost timer runs on a pool thread, and walking the live standings
    /// from there while the UI thread is adding laps to them is exactly the
    /// kind of race that throws mid-session. This is refreshed from the lap
    /// event instead, which arrives on the UI thread.
    /// </summary>
    private readonly object _paceLock = new();
    private Dictionary<string, (long BestMs, IReadOnlyList<long> PaceLaps)> _pace = new(StringComparer.OrdinalIgnoreCase);
    private string? _leadingRealCar;

    private Action<RacerStanding, bool, bool>? _lapHandler;

    private void RefreshPaceSnapshot()
    {
        var standings = _race.GetOrderedStandings();
        var pace = new Dictionary<string, (long, IReadOnlyList<long>)>(StringComparer.OrdinalIgnoreCase);

        foreach (var standing in standings)
        {
            // Flying laps only - the first crossing runs from the green to
            // the line and is not a lap time - and none that ran through a
            // crash recovery, which spans the whole stoppage and would drag
            // a live-pace ghost's average far off the driver's real pace.
            //
            // The laps themselves are kept rather than one average. The
            // window a "recent average" ghost averages over is the ghost's
            // own setting (GhostCar.LiveAverageLaps), so two ghosts following
            // the same driver can want different windows. A single average
            // over the last 20 laps used to be worked out here for everyone,
            // and the ghost's setting (5 by default) was never read - so the
            // ghost took about four times as long as it should to catch up
            // when the driver found pace. PaceLaps hands back a fresh list,
            // so the pool thread can read it without touching the standings.
            pace[standing.TransponderId] = (standing.FastestLapMs, RaceGaps.PaceLaps(standing));
        }

        lock (_paceLock)
        {
            _pace = pace;
            _leadingRealCar = standings.FirstOrDefault(s => !IsGhost(s.TransponderId))?.TransponderId;
        }
    }

    public GhostSettings Settings { get; set; } = new();

    public event Action<string>? StatusMessage;

    /// <summary>A ghost that has crossed the line, so the app can announce it.</summary>
    public event Action<GhostCar, long>? GhostLapCompleted;

    public bool IsRunning
    {
        get { lock (_lock) { return _running; } }
    }

    /// <summary>Whether this ghost is one of those running now.</summary>
    private bool IsStillRunning(string transponder)
    {
        lock (_lock)
        {
            return _running && _runners.ContainsKey(transponder);
        }
    }

    /// <summary>One ghost's state while a session is running.</summary>
    private sealed class GhostRunner
    {
        public required GhostCar Car { get; init; }

        /// <summary>
        /// Settable, not init-only: a live-pace ghost re-reads its target
        /// every lap, so this is the pace it is currently running rather
        /// than the pace it started on.
        /// </summary>
        public required int BaseLapMs { get; set; }

        public long NextCrossingMs { get; set; }
        public int LapsCompleted { get; set; }

        /// <summary>The pace worked out at the green, used when a live source has nothing to read yet.</summary>
        /// <remarks>Settable since 22 Oct 2026: an own-best ghost brought up to a new best (RefreshChangedPaces) moves it too.</remarks>
        public required int FallbackLapMs { get; set; }

        /// <summary>The car's PaceRevision this runner's pace was worked out at - see RefreshChangedPaces.</summary>
        public int PaceRevisionSeen { get; set; }

        /// <summary>The base lap this runner ran at for each PaceRevision it has seen, so an Undo of "bring up to date" puts the old pace back.</summary>
        /// <remarks>
        /// WHY (22 Oct 2026): an own-best ghost's pace is a live lookup, not
        /// something saved on the car, so undoing a refresh used to re-read
        /// the NEW best and keep the ghost at it. This remembers what each
        /// revision actually ran at.
        /// </remarks>
        public Dictionary<int, int> PaceByRevision { get; } = new();

        /// <summary>Last pace announced in the log, so a live ghost only reports real changes.</summary>
        public int LastLoggedLapMs { get; set; }
    }

    /// <summary>
    /// Starts every enabled ghost, working out each one's pace.
    ///
    /// A ghost whose pace can't be resolved - no personal best on this
    /// track yet, say - is skipped with a reason rather than silently
    /// running at some default, which would be worse than not running.
    /// </summary>
    /// <param name="onlyThese">
    /// When given, only ghosts whose transponder is in it run - a demo race
    /// passes the ghosts that were ticked for it. Null runs every enabled
    /// ghost, as a real race does.
    /// </param>
    /// <remarks>
    /// Also called when a race is put back rather than started - an undone
    /// Stage, Countdown or Reset brings the race back to Racing, and the
    /// ghosts with it. A ghost that has already crossed the line in that race
    /// carries on from its last crossing (see ResumeCrossingMs) rather than
    /// starting again as though the race had just gone green.
    /// </remarks>
    public void Start(string? trackLayoutName = null, IReadOnlyCollection<string>? onlyThese = null)
    {
        Stop();

        if (!Settings.Enabled)
        {
            return;
        }

        // "(none)" is what the track dropdown shows when nothing is picked,
        // and it was being passed through as a track NAME - so a personal
        // best filtered to a track called "(none)" matched nothing and every
        // PB ghost was skipped for want of a track that never existed.
        _trackLayoutName = string.IsNullOrWhiteSpace(trackLayoutName) || trackLayoutName == "(none)"
            ? null
            : trackLayoutName;

        var started = 0;

        _uiContext = SynchronizationContext.Current;

        // Live-pace ghosts read this, and it has to be built on the thread
        // the standings are mutated on.
        RefreshPaceSnapshot();
        _lapHandler = (_, _, _) => RefreshPaceSnapshot();
        _race.LapProcessed += _lapHandler;

        // Read here for the same reason: the standings belong to the UI
        // thread, and this is on it.
        var lastCrossings = GhostCrossingsInRunningRace();

        lock (_lock)
        {
            // A new run: anything still queued from the last one is stale (A28-2).
            _runId++;

            foreach (var ghost in Settings.Ghosts.Where(g => g.Enabled
                && (onlyThese is null || onlyThese.Contains(g.Transponder, StringComparer.OrdinalIgnoreCase))))
            {
                var baseLapMs = ResolveBaseLapMs(ghost, _trackLayoutName);

                if (baseLapMs is null)
                {
                    StatusMessage?.Invoke($"[ghost] {ghost.Name} not started - {DescribeMissingPace(ghost)}.");
                    continue;
                }

                // A race that has been put back, and this ghost was already
                // lapping in it. Every ghost used to be started afresh here,
                // so after an undone Stage the old race came back with its
                // ghosts' laps on the board but the ghosts themselves back
                // on the line, a whole lap and a random stagger from their
                // next crossing, as though the race had only just gone green.
                var carryingOn = lastCrossings.TryGetValue(ghost.Transponder, out var last);

                long nextCrossingMs;

                if (carryingOn)
                {
                    nextCrossingMs = ResumeCrossingMs(last.CrossedAtMs, baseLapMs.Value, NowMs);
                }
                else
                {
                    // The first crossing is part of a lap, as it is for a
                    // real car: the grid is behind the line, so a car
                    // crosses it for the first time well under a lap after
                    // the green. A ghost used to wait a whole lap plus a
                    // stagger, which put it a lap behind the field from the
                    // start - one crossing short of everyone - so the leader
                    // was announced as lapping it on lap two, and the board
                    // showed it a lap down.
                    //
                    // Staggered between about half a lap and most of one,
                    // rather than all crossing together - a field that laps
                    // in perfect unison sounds broken.
                    var firstLapMs = baseLapMs.Value * (0.45 + _random.NextDouble() * 0.45);

                    nextCrossingMs = NowMs + (long)firstLapMs;
                }

                var runner = new GhostRunner
                {
                    Car = ghost,
                    BaseLapMs = baseLapMs.Value,
                    FallbackLapMs = baseLapMs.Value,
                    NextCrossingMs = nextCrossingMs,
                    LapsCompleted = carryingOn ? last.Laps : 0,
                    LastLoggedLapMs = baseLapMs.Value,
                    PaceRevisionSeen = ghost.PaceRevision,
                };

                // The pace it starts at, so an Undo back to this revision can
                // put it back (see RefreshChangedPaces). 22 Oct 2026.
                runner.PaceByRevision[ghost.PaceRevision] = baseLapMs.Value;
                _runners[ghost.Transponder] = runner;

                // Says what it actually resolved to. A ghost silently running
                // at a pace nobody chose is the thing worth catching, and
                // "it didn't start" was the only feedback there used to be.
                StatusMessage?.Invoke(carryingOn
                    ? $"[ghost] {ghost.Name} back on track at {baseLapMs.Value / 1000.0:0.000}s, carrying on from {Plural.Of(last.Laps, "lap")} - {DescribePace(ghost, _trackLayoutName)}."
                    : $"[ghost] {ghost.Name} on track at {baseLapMs.Value / 1000.0:0.000}s - {DescribePace(ghost, _trackLayoutName)}.");

                started++;
            }

            if (started == 0)
            {
                return;
            }

            _running = true;

            // 100ms is fine: a lap is seconds long, and a tenth of jitter on
            // a virtual opponent is well inside the variation they already
            // have by design.
            _timer = new System.Threading.Timer(_ => Tick(), null, 100, 100);
        }

        StatusMessage?.Invoke($"[ghost] {Plural.Of(started, "ghost car")} on track.");
    }

    /// <summary>
    /// Where each ghost last crossed the line in the race that is running,
    /// on the ghosts' own clock (milliseconds since the green), with the laps
    /// it has counted - by transponder.
    ///
    /// Only a race that has been put back has any: the green clears the
    /// standings before the ghosts are started, so at a real start no ghost
    /// has crossed anything yet and every one starts afresh, as it always
    /// has. UI thread only - it reads the standings.
    /// </summary>
    private Dictionary<string, (long CrossedAtMs, int Laps)> GhostCrossingsInRunningRace()
    {
        var crossings = new Dictionary<string, (long CrossedAtMs, int Laps)>(StringComparer.OrdinalIgnoreCase);

        if (_race.State != RaceState.Racing)
        {
            return crossings;
        }

        foreach (var standing in _race.GetOrderedStandings())
        {
            // The last pass the session saw from it, counted or thrown out
            // as too quick - either way, that is where the ghost was. Zero
            // means it never crossed, or a race brought back from the crash
            // journal, which clears these readings on purpose (see
            // RaceJournal.Restore); that ghost starts afresh, as before.
            if (IsGhost(standing.TransponderId) && standing.LastSeenDeviceTimeMs > 0)
            {
                crossings[standing.TransponderId] = (standing.LastSeenDeviceTimeMs, standing.Laps);
            }
        }

        return crossings;
    }

    /// <summary>
    /// When a ghost that is carrying on next crosses the line: on its own
    /// rhythm from its last crossing, as though it had kept lapping while it
    /// was stopped - which is what the restored race clock says happened,
    /// since it counts that time too. Always within one lap of now.
    ///
    /// The crossings that fell while it was stopped are not made. The cars
    /// on track had theirs ignored while the race was staged or reset, so
    /// the lap that spans the gap comes out long for them; it does for the
    /// ghost too, and nobody's count jumps by a burst of laps nobody saw.
    /// It is the same as a ghost under a red, which keeps lapping while its
    /// crossings are not counted.
    /// </summary>
    private static long ResumeCrossingMs(long lastCrossingMs, int lapMs, long nowMs)
    {
        var lap = Math.Max(1, lapMs);

        // A clock that has stepped backwards reads as a crossing just made.
        var sinceLast = Math.Max(0, nowMs - lastCrossingMs);

        return nowMs + (lap - sinceLast % lap);
    }

    public void Stop()
    {
        if (_lapHandler is not null)
        {
            _race.LapProcessed -= _lapHandler;
            _lapHandler = null;
        }

        lock (_lock)
        {
            _timer?.Dispose();
            _timer = null;
            _runners.Clear();
            _running = false;

            // 17 Oct 2026, Bug Hunt 8, A28-2: crossings already posted are stale.
            _runId++;
        }
    }

    /// <summary>The run the ghosts are on now (A28-2).</summary>
    private int CurrentRunId()
    {
        lock (_lock)
        {
            return _runId;
        }
    }

    /// <summary>1 while a tick is running, threaded mode - see Tick.</summary>
    private int _threadedTickBusy;

    private void Tick()
    {
        // WHY (23 Oct 2026, timing layer, P4b): threaded, a tick hands its
        // crossings to the race from this timer thread and waits for the
        // timing thread to count them, so a tick can outlast the 100 ms
        // beat. A second tick running alongside it could hand a crossing in
        // ahead of an older one; so, threaded, a tick that finds one still
        // running is skipped - the next takes up whatever fell due, since a
        // crossing stays due until a tick takes it. Inline, as it always was.
        if (_race.IsThreaded)
        {
            if (Interlocked.Exchange(ref _threadedTickBusy, 1) == 1)
            {
                return;
            }

            try
            {
                TickOnce();
            }
            finally
            {
                Volatile.Write(ref _threadedTickBusy, 0);
            }

            return;
        }

        TickOnce();
    }

    private void TickOnce()
    {
        List<(GhostRunner Runner, long CrossingMs)> due = new();
        int runId;

        lock (_lock)
        {
            if (!_running || _race.State != RaceState.Racing)
            {
                return;
            }

            // The run these crossings belong to (A28-2).
            runId = _runId;

            var now = NowMs;

            foreach (var runner in _runners.Values)
            {
                if (now < runner.NextCrossingMs)
                {
                    continue;
                }

                due.Add((runner, runner.NextCrossingMs));

                // Next lap scheduled from the CROSSING, not from now - or
                // timer jitter would accumulate and the ghost would drift
                // slower over a long race.
                runner.NextCrossingMs += NextLapMs(runner);
                runner.LapsCompleted++;
            }
        }

        foreach (var (runner, crossingMs) in due)
        {
            var lap = new DirectLapEvent
            {
                TransponderId = runner.Car.Transponder,
                DeviceTimeMs = (uint)Math.Max(0, crossingMs),
                SignalHits = 1,
                SignalStrength = 100,
                IsSynthetic = true,

                // WHY (23 Oct 2026, timing layer, P1): stamped as it is made,
                // on this timer thread, as a decoder's pass is stamped as it
                // arrives - the race judges it by this, however long the post
                // to the UI thread below takes. See ProcessLapEvent.
                ReceivedMs = TimingClock.NowMs,
            };
            var car = runner.Car;
            var baseLapMs = runner.BaseLapMs;

            void Deliver()
            {
                try
                {
                    // Re-checked on the UI thread: the race may have been
                    // stopped between scheduling and delivery - or this
                    // ghost switched off while the others carry on, when its
                    // crossing would put it back on the board it has just
                    // been taken off (MainForm.KeepRunningGhostsInStep) - or
                    // the ghosts restarted since it was scheduled, as Clear
                    // Stats does, when it is on the old run's clock (17 Oct
                    // 2026, Bug Hunt 8, A28-2).
                    if (!IsStillRunning(car.Transponder) || _race.State != RaceState.Racing || runId != CurrentRunId())
                    {
                        return;
                    }

                    _race.ProcessLapEvent(lap);
                    GhostLapCompleted?.Invoke(car, baseLapMs);
                }
                catch
                {
                    // A ghost failing must never disturb a real race.
                }
            }

            // WHY (23 Oct 2026, timing layer, P4b): threaded, straight to the
            // race from here - the race is not the UI thread's any more, and a
            // ghost's lap must not wait for a frozen window any more than a
            // real car's does.
            if (_race.IsThreaded)
            {
                DeliverFromTimerThread(lap, car, baseLapMs, runId);
                continue;
            }

            if (_uiContext is { } context)
            {
                context.Post(_ => Deliver(), null);
            }
            else
            {
                Deliver();
            }
        }
    }

    /// <summary>
    /// A ghost's crossing, threaded mode, handed to the race on this timer
    /// thread: the same checks Deliver makes on the UI thread, then the pass
    /// counted on the timing thread - only while the race is Racing, checked
    /// and counted in one go there (RaceTiming.ProcessLapEventWhileRacing).
    /// GhostLapCompleted, whose listener is screen code, is posted to the UI.
    /// </summary>
    /// <remarks>
    /// The checks and the count are made under _lock, so a Stop or a ghost
    /// switched off on the UI thread waits for a crossing already being
    /// counted rather than having it land after it (as inline, where both
    /// ran on the UI thread). That waits on the timing thread while holding
    /// _lock, which is safe because nothing on the timing thread ever takes
    /// _lock - keep it so: no ghost code may run there.
    /// </remarks>
    private void DeliverFromTimerThread(DirectLapEvent lap, GhostCar car, long baseLapMs, int runId)
    {
        try
        {
            bool counted;

            lock (_lock)
            {
                if (!_running || !_runners.ContainsKey(car.Transponder) || runId != _runId)
                {
                    return;
                }

                counted = _race.ProcessLapEventWhileRacing(lap);
            }

            if (!counted || GhostLapCompleted is null)
            {
                return;
            }

            void Completed()
            {
                try
                {
                    GhostLapCompleted?.Invoke(car, baseLapMs);
                }
                catch
                {
                    // A ghost failing must never disturb a real race.
                }
            }

            if (_uiContext is { } context)
            {
                context.Post(_ => Completed(), null);
            }
            else
            {
                Completed();
            }
        }
        catch
        {
            // A ghost failing must never disturb a real race.
        }
    }

    /// <summary>This ghost's next lap, with its consistency applied.</summary>
    private int NextLapMs(GhostRunner runner)
    {
        // A live-pace ghost re-reads its target each lap, so it tracks the
        // session as it develops rather than being fixed at the green.
        if (runner.Car.IsLivePace)
        {
            // The live number is the driver's raw pace, so the ghost's pace
            // adjustment has to be applied to it here too. It used to be
            // applied only once, at the green, which meant a "2% quicker
            // than me" ghost dropped to exactly the driver's pace as soon as
            // they set a lap - it never stayed ahead, and a ghost set to
            // trail fell straight onto their tail. The fallback was adjusted
            // when the ghost started, so it is used as it is.
            var live = ResolveLivePaceMs(runner.Car) is { } liveMs
                ? ApplyPaceAdjustment(liveMs, runner.Car.PacePercent)
                : runner.FallbackLapMs;
            runner.BaseLapMs = Math.Max(500, live);

            // Only reported when it has actually moved, and only past a
            // tenth - otherwise every lap writes a line that says nothing.
            if (Math.Abs(runner.BaseLapMs - runner.LastLoggedLapMs) >= 100)
            {
                StatusMessage?.Invoke(
                    $"[ghost] {runner.Car.Name} re-paced to {runner.BaseLapMs / 1000.0:0.000}s.");
                runner.LastLoggedLapMs = runner.BaseLapMs;
            }
        }

        var variation = runner.Car.VariationFraction;

        if (variation <= 0)
        {
            return runner.BaseLapMs;
        }

        // Two samples averaged, which clusters results around the base pace
        // instead of spreading them evenly. Real lap times bunch near a
        // driver's natural rhythm with occasional outliers, and a flat
        // random spread doesn't feel like that.
        var roll = (_random.NextDouble() + _random.NextDouble()) / 2.0;
        var offset = (roll - 0.5) * 2.0 * variation;

        return Math.Max(500, (int)(runner.BaseLapMs * (1 + offset)));
    }

    /// <summary>Works out a ghost's base lap, or null when it can't be resolved.</summary>
    private int? ResolveBaseLapMs(GhostCar ghost, string? trackLayoutName)
    {
        var resolved = ghost.PaceSource switch
        {
            GhostPaceSource.FixedLapTime => ghost.FixedLapMs > 0 ? ghost.FixedLapMs : null,
            GhostPaceSource.YourPersonalBest => BestLapFor(ghost.SourceTransponder, trackLayoutName),
            GhostPaceSource.DriverPersonalBest => BestLapFor(ghost.SourceTransponder, trackLayoutName),
            GhostPaceSource.YourRecentAverage => RecentAverageFor(ghost.SourceTransponder),
            GhostPaceSource.TrackRecord => TrackRecordFor(trackLayoutName),
            GhostPaceSource.PersonalBestInRange => BestLapFor(ghost.SourceTransponder, trackLayoutName, ghost.RangeFromUtc, ghost.RangeToUtc),
            GhostPaceSource.OwnBestTonight => OwnBestTonightFor(ghost.SourceTransponder, trackLayoutName),

            // A live ghost still needs something to run at until the session
            // has laps to read. The edit form shows the lap time for these
            // sources as "Until laps come in:", so a time typed there comes
            // first - the driver's best used to come first, which ignored a
            // gentle 8-second warm-up time for any driver with a saved best.
            // But the form always saves that box, and it starts at the model's
            // 7.000s default, so "a time is set" is true for every ghost. See
            // LiveStartLapMs for how the untouched default is told apart.
            GhostPaceSource.LiveOwnPace => ResolveLivePaceMs(ghost)
                                           ?? LiveStartLapMs(ghost.FixedLapMs, BestLapFor(ghost.SourceTransponder, trackLayoutName)),
            GhostPaceSource.LiveLeaderPace => ResolveLivePaceMs(ghost)
                                              ?? LiveStartLapMs(ghost.FixedLapMs, BestLapFor(ghost.SourceTransponder, trackLayoutName)),
            _ => null,
        };

        if (resolved is not { } baseMs || baseMs <= 0)
        {
            return null;
        }

        // Pace adjustment last, so it applies whatever the source was.
        return ApplyPaceAdjustment(baseMs, ghost.PacePercent);
    }

    /// <summary>The lap time a brand-new ghost starts with (GhostCar.FixedLapMs's default).</summary>
    private static readonly int DefaultFixedLapMs = new GhostCar().FixedLapMs;

    /// <summary>
    /// What a live ghost runs at before the session has laps to read.
    ///
    /// The lap-time box can't say "nothing typed": every ghost carries the
    /// model's 7.000s default, a settings file without the field gets it
    /// too, and the edit form writes the box back for every source. So a
    /// plain "is a time set" check is always true and the driver's best
    /// could never be used. That silently moved live ghosts saved before the
    /// typed time came first: one left on the default used to start at the
    /// driver's PB (say 10s on a big track) and now went out at 7s, three
    /// seconds up the road before the live pace ever arrived.
    ///
    /// So the untouched default counts as "no time chosen": the driver's
    /// best wins over it, and the default is only the last resort when
    /// there is no best (a new driver, or a leader-pace ghost with no
    /// driver). Any other time is a deliberate choice and comes first. The
    /// cost is that someone who types exactly 7.000 on purpose gets the
    /// best instead; telling those apart needs the form to record whether
    /// the box was touched, which this can't see.
    /// </summary>
    internal static int? LiveStartLapMs(int fixedLapMs, int? bestLapMs)
    {
        if (fixedLapMs > 0 && fixedLapMs != DefaultFixedLapMs)
        {
            return fixedLapMs;
        }

        if (bestLapMs is > 0)
        {
            return bestLapMs;
        }

        return fixedLapMs > 0 ? fixedLapMs : null;
    }

    /// <summary>
    /// A lap time with a ghost's pace adjustment applied: 98% is 2% quicker,
    /// 110% is 10% slower. One place for it, because the start of a run and
    /// a live ghost's lap-by-lap re-pace both need it, and the two drifting
    /// apart is how a live ghost lost its adjustment after the first lap.
    /// Clamped to the same 50-200% the edit form allows, and never under
    /// half a second, so a bad setting can't produce an impossible lap.
    /// </summary>
    internal static int ApplyPaceAdjustment(int lapMs, int pacePercent)
    {
        var adjusted = (int)(lapMs * Math.Clamp(pacePercent, 50, 200) / 100.0);
        return Math.Max(500, adjusted);
    }

    /// <summary>
    /// A driver's best lap from saved history: on the given track when one
    /// is selected, and within a date window when the ghost asks for one.
    ///
    /// Honours the Personal Bests page's own reset cutoffs, so a ghost built
    /// on "your personal best" chases the same number that page shows. It
    /// used to read every session ever saved, which meant a reset there left
    /// the ghost still chasing the lap you had just retired.
    /// </summary>
    private int? BestLapFor(string transponder, string? trackLayoutName, DateTime? fromUtc = null, DateTime? toUtc = null)
    {
        if (string.IsNullOrWhiteSpace(transponder))
        {
            return null;
        }

        var driverName = _profiles.Get(transponder)?.DriverName ?? string.Empty;

        // Saved demo races are left out: their laps are invented, and a
        // ghost built on one would be chasing a lap nobody ever drove.
        var best = _history.All
            .Where(s => !s.IsNotScored())
            .Where(s => trackLayoutName is null
                        || string.Equals(s.TrackLayoutName, trackLayoutName, StringComparison.OrdinalIgnoreCase))
            .Where(s => fromUtc is not { } from || s.StartedAtUtc >= from)
            .Where(s => toUtc is not { } to || s.StartedAtUtc <= to)
            .Where(s => CutoffAllows(driverName, s))
            .SelectMany(s => s.Results)
            .Where(r => string.Equals(r.Transponder, transponder, StringComparison.OrdinalIgnoreCase) && r.FastLapMs > 0)
            .Select(r => r.FastLapMs)
            .DefaultIfEmpty(0)
            .Min();

        return best > 0 ? (int)best : null;
    }

    /// <summary>
    /// The pace an OwnBestTonight ghost runs at (22 Oct 2026): the driver's
    /// best lap tonight on this layout - the session running now, or any
    /// session saved since the night began - and, when they have set none
    /// tonight, their best ever on it (the same number a personal-best
    /// ghost chases, resets and all).
    ///
    /// Tonight first because tonight's grip, tonight's tyres and tonight's
    /// setup are what the driver is on; a best from a different evening can
    /// be a target nobody could reach on this surface. The all-time best is
    /// the fallback so the first run of the night still has something to
    /// chase. UI thread only, like the rest of Start.
    /// </summary>
    private int? OwnBestTonightFor(string transponder, string? trackLayoutName)
    {
        if (string.IsNullOrWhiteSpace(transponder))
        {
            return null;
        }

        long? runningNow;

        lock (_paceLock)
        {
            runningNow = _pace.TryGetValue(transponder, out var entry) && entry.BestMs > 0 ? entry.BestMs : null;
        }

        var tonight = TonightAtTheTrack.NightOf(DateTime.Now);
        var driverName = _profiles.Get(transponder)?.DriverName ?? string.Empty;

        // The Personal Bests page's resets are honoured here too, as
        // BestLapFor does: a best retired earlier tonight is not chased.
        var savedTonight = _history.All
            .Where(s => !s.IsNotScored())
            .Where(s => CutoffAllows(driverName, s))
            .Where(s => trackLayoutName is null
                        || string.Equals(s.TrackLayoutName, trackLayoutName, StringComparison.OrdinalIgnoreCase))
            .Where(s => TonightAtTheTrack.NightOf(s.StartedAtUtc.ToLocalTime()) == tonight)
            .SelectMany(s => s.Results)
            .Where(r => string.Equals(r.Transponder, transponder, StringComparison.OrdinalIgnoreCase) && r.FastLapMs > 0)
            .Select(r => r.FastLapMs)
            .DefaultIfEmpty(0)
            .Min();

        return OwnBestTonightMs(runningNow, savedTonight > 0 ? savedTonight : null, BestLapFor(transponder, trackLayoutName));
    }

    /// <summary>
    /// OwnBestTonightFor's choice on its own, for testing: the quicker of
    /// the running session's best and tonight's saved best, else the best
    /// ever, else nothing.
    /// </summary>
    internal static int? OwnBestTonightMs(long? runningNowMs, long? savedTonightMs, int? everMs)
    {
        var tonight = new[] { runningNowMs, savedTonightMs }
            .Where(ms => ms is > 0)
            .Select(ms => ms!.Value)
            .DefaultIfEmpty(0)
            .Min();

        if (tonight > 0)
        {
            return (int)Math.Min(tonight, int.MaxValue);
        }

        return everMs is > 0 ? everMs : null;
    }

    /// <summary>
    /// Brings running own-best ghosts up to the driver's new best (22 Oct
    /// 2026): any whose PaceRevision has moved since it was started - "Chase
    /// a Best Lap..." picked again for the same driver - re-reads its pace
    /// and runs at it from its next lap. Nothing restarts and nothing goes
    /// back to the line; the lap already under way finishes at the old pace.
    /// Called on the UI thread after a ghost save. Returns how many moved.
    /// </summary>
    public int RefreshChangedPaces()
    {
        if (!IsRunning)
        {
            return 0;
        }

        // Read the running session's bests first, on this (the UI) thread.
        RefreshPaceSnapshot();

        var moved = new List<(string Name, int LapMs, bool Back)>();

        lock (_lock)
        {
            foreach (var runner in _runners.Values)
            {
                // The ghost as the settings hold it NOW, found by its
                // transponder. An Undo (GhostStore.Restore) or an edit
                // replaces the ghost objects, while a running ghost keeps
                // the one it was started with - whose revision never moves.
                var car = Settings.Ghosts.FirstOrDefault(g =>
                              string.Equals(g.Transponder, runner.Car.Transponder, StringComparison.OrdinalIgnoreCase))
                          ?? runner.Car;

                if (car.PaceSource != GhostPaceSource.OwnBestTonight
                    || runner.PaceRevisionSeen == car.PaceRevision)
                {
                    continue;
                }

                // WHY (22 Oct 2026): an Undo of "bring up to date" puts the
                // revision BACK, which used to be treated like any other move
                // - the best was looked up again, so the ghost stayed at the
                // new pace and the log said it was brought up. Going back to a
                // revision this runner has already run at now restores that
                // pace; only a new revision reads the driver's best.
                var wentBack = car.PaceRevision < runner.PaceRevisionSeen;
                runner.PaceRevisionSeen = car.PaceRevision;

                int lapMs;

                // Only "put back" when it really is the old pace restored: a
                // runner started after the refresh never ran at the earlier
                // revision, so that one is looked up like any other. 22 Oct 2026.
                var restored = false;

                if (wentBack && runner.PaceByRevision.TryGetValue(car.PaceRevision, out var earlier))
                {
                    lapMs = earlier;
                    restored = true;
                }
                else if (ResolveBaseLapMs(car, _trackLayoutName) is { } resolved)
                {
                    lapMs = resolved;
                    runner.PaceByRevision[car.PaceRevision] = lapMs;
                }
                else
                {
                    continue;
                }

                runner.BaseLapMs = lapMs;
                runner.FallbackLapMs = lapMs;
                runner.LastLoggedLapMs = lapMs;
                moved.Add((car.Name, lapMs, restored));
            }
        }

        foreach (var (name, lapMs, back) in moved)
        {
            StatusMessage?.Invoke(back
                ? $"[ghost] {name} put back to {lapMs / 1000.0:0.000}s."
                : $"[ghost] {name} brought up to {lapMs / 1000.0:0.000}s - the new best to chase.");
        }

        return moved.Count;
    }

    /// <summary>Whether a session predates the personal-best reset for this driver and track.</summary>
    private bool CutoffAllows(string driverName, SessionRecord session)
    {
        if (_pbResets is null || string.IsNullOrWhiteSpace(driverName))
        {
            return true;
        }

        return _pbResets.EffectiveCutoff(driverName, session.TrackLayoutName) is not { } cutoff
               || session.StartedAtUtc > cutoff;
    }

    /// <summary>The selected layout's all-time record, or null when there's no layout or no record on it.</summary>
    private int? TrackRecordFor(string? trackLayoutName)
    {
        if (_trackLayouts is null || string.IsNullOrWhiteSpace(trackLayoutName))
        {
            return null;
        }

        var layout = _trackLayouts.All.FirstOrDefault(t =>
            string.Equals(t.Name, trackLayoutName, StringComparison.OrdinalIgnoreCase));

        return layout is { RecordLapMs: > 0 } ? (int)layout.RecordLapMs : null;
    }

    /// <summary>
    /// The live number a moving ghost follows, read from the session that is
    /// running. Null until there is something to read - a ghost can't chase
    /// a lap nobody has done yet.
    /// </summary>
    private int? ResolveLivePaceMs(GhostCar ghost)
    {
        (long BestMs, IReadOnlyList<long> PaceLaps) entry;

        lock (_paceLock)
        {
            // The leader among REAL cars. Following the overall leader would
            // let a ghost chase another ghost, which is a mirror, not a race.
            var key = ghost.PaceSource == GhostPaceSource.LiveLeaderPace
                ? _leadingRealCar
                : ghost.SourceTransponder;

            if (key is null || !_pace.TryGetValue(key, out entry))
            {
                return null;
            }
        }

        var useAverage = ghost.PaceSource == GhostPaceSource.LiveOwnPace
                         && ghost.LiveBasis == GhostLiveBasis.RecentAverage;

        var chosen = useAverage ? RecentAverageMs(entry.PaceLaps, ghost.LiveAverageLaps) : entry.BestMs;

        return chosen > 0 ? (int)chosen : null;
    }

    /// <summary>
    /// The mean of the last <paramref name="window"/> laps, or 0 when there
    /// are none. A window under one (a hand-edited or very old settings
    /// file) is treated as one, so the ghost follows the latest lap rather
    /// than dividing by nothing. A window longer than the laps run so far
    /// simply averages all of them.
    /// </summary>
    internal static long RecentAverageMs(IReadOnlyList<long> laps, int window)
    {
        if (laps.Count == 0)
        {
            return 0;
        }

        return (long)laps.TakeLast(Math.Max(1, window)).Average();
    }

    /// <summary>Plain-language account of where a started ghost's pace came from, for the log.</summary>
    private string DescribePace(GhostCar ghost, string? trackLayoutName)
    {
        var driver = _profiles.Get(ghost.SourceTransponder)?.DriverName;
        var who = string.IsNullOrWhiteSpace(driver) ? ghost.SourceTransponder : driver;
        var where = trackLayoutName is null ? "any track" : trackLayoutName;
        var adjust = ghost.PacePercent == 100 ? string.Empty : $", adjusted to {ghost.PacePercent}%";

        return ghost.PaceSource switch
        {
            GhostPaceSource.FixedLapTime => $"fixed lap time{adjust}",
            GhostPaceSource.TrackRecord => $"the track record on {where}{adjust}",
            GhostPaceSource.YourRecentAverage => $"{who}'s recent average pace{adjust}",
            GhostPaceSource.LiveOwnPace => $"{who}'s live pace this session{adjust}",
            GhostPaceSource.LiveLeaderPace => $"the leader's live pace{adjust}",
            GhostPaceSource.PersonalBestInRange => $"{who}'s best on {where} between {Format(ghost.RangeFromUtc)} and {Format(ghost.RangeToUtc)}{adjust}",
            GhostPaceSource.OwnBestTonight => $"{who}'s best tonight on {where}, or their best ever there before they set one{adjust}",
            _ => $"{who}'s personal best on {where}{adjust}",
        };

        static string Format(DateTime? utc) => utc is { } d ? d.ToLocalTime().ToString("d MMM yyyy") : "the start";
    }

    private int? RecentAverageFor(string transponder)
    {
        if (string.IsNullOrWhiteSpace(transponder))
        {
            return null;
        }

        // The most recent session that driver actually appears in - not
        // simply the newest session, which they may not have raced. Saved
        // demo races don't count - their times are invented. One with no
        // timed lap to average (only the start lap) is passed over too.
        return _history.All
            .Where(s => !s.IsNotScored())
            .OrderByDescending(s => s.StartedAtUtc)
            .SelectMany(s => s.Results)
            .Where(r => string.Equals(r.Transponder, transponder, StringComparison.OrdinalIgnoreCase)
                        && r.Laps > 0
                        && r.ElapsedMs > 0)
            .Select(AveragePaceMs)
            .FirstOrDefault(ms => ms is not null);
    }

    /// <summary>
    /// A saved result's usual lap: the mean of its timed laps, leaving out
    /// the start lap and any lap under half or over twice the median - a
    /// dash to the line, a battery swap, a tuning stop. Null when it has no
    /// timed lap. A result saved before lap times were kept has only its
    /// total, so that over its laps, as before.
    ///
    /// It used to be the total over the laps for everyone. A practice's
    /// total runs from its green, so the walk to the track after Start
    /// Practice - or twelve minutes of someone else lapping before the
    /// driver's first crossing - went into the average, and the ghost ran
    /// three times slower than the driver drives.
    /// </summary>
    internal static int? AveragePaceMs(SessionResultEntry entry)
    {
        if (entry.LapTimesMs is not { Count: > 0 } laps)
        {
            return entry.Laps > 0 && entry.ElapsedMs > 0 ? (int)(entry.ElapsedMs / entry.Laps) : null;
        }

        var timed = laps.Skip(1).Where(ms => ms > 0).OrderBy(ms => ms).ToList();

        if (timed.Count == 0)
        {
            return null;
        }

        var median = timed[timed.Count / 2];

        // Never empty: the median itself is always in range.
        return (int)timed.Where(ms => ms * 2 >= median && ms <= median * 2).Average();
    }

    private static string DescribeMissingPace(GhostCar ghost) => ghost.PaceSource switch
    {
        GhostPaceSource.FixedLapTime => "no lap time set",
        GhostPaceSource.YourPersonalBest => "no personal best recorded on this track yet",
        GhostPaceSource.DriverPersonalBest => "that driver has no personal best on this track yet",
        GhostPaceSource.YourRecentAverage => "no completed session to average from yet",
        GhostPaceSource.TrackRecord => "no track layout selected, or no record set on it yet",
        GhostPaceSource.PersonalBestInRange => "that driver set no lap in the chosen date range",
        GhostPaceSource.OwnBestTonight => "that driver has no lap on this track yet, tonight or before",
        GhostPaceSource.LiveOwnPace => "nothing to pace against yet - give it a personal best or a fixed lap time to start from",
        GhostPaceSource.LiveLeaderPace => "nothing to pace against yet - give it a personal best or a fixed lap time to start from",
        _ => "its pace couldn't be worked out",
    };

    /// <summary>
    /// Whether a transponder belongs to a ghost rather than real hardware.
    ///
    /// Forwards to <see cref="Ghosts.IsGhost"/>, which is where the check
    /// now lives: the timing code needs to ask it and has no business
    /// needing the simulator and its four stores to do so. Kept here so
    /// every caller that already goes through GhostService stays as it is.
    /// </summary>
    public static bool IsGhost(string? transponder) => Ghosts.IsGhost(transponder);

    public void Dispose() => Stop();
}
