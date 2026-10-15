namespace HeatCaller;

/// <summary>
/// Drives a fake race through the SAME RaceControlSession real hardware
/// uses, by generating synthetic DirectLapEvents for a handful of made-up
/// racers. Everything downstream - the leaderboard, voice announcements,
/// sound effects, streak tracking, fastest-lap/track-record checks, even
/// the race clock - runs completely normally, since none of it can tell a
/// simulated lap from a real one. This makes it a genuine end-to-end
/// rehearsal of the announcer setup, not a separate mocked-up preview.
///
/// Demo races are marked via RaceControlSession.IsDemoMode so they never set
/// real track records. Race Control only saves one to history when "Save
/// demo races to history" is ticked, and then marks the saved copy
/// SessionRecord.IsDemo, which keeps it out of every result that scores -
/// rounds, championship points, badges, personal bests and records.
/// </summary>
public sealed class DemoRaceSimulator : IDisposable
{
    private readonly RaceControlSession _race;
    private readonly AnnouncementService _announcer;
    private readonly System.Windows.Forms.Timer _tickTimer = new() { Interval = 100 };
    private readonly Random _random = new();
    private readonly List<DemoRacer> _racers = new();

    private long _deviceTimeMs;

    /// <summary>
    /// Real time since the demo's green, which the simulated decoder clock
    /// (_deviceTimeMs) follows - see Tick. Restarted at each green.
    /// </summary>
    private readonly System.Diagnostics.Stopwatch _sinceGreen = new();

    /// <summary>What _deviceTimeMs read at the green, so the clock can be rebuilt from the stopwatch each tick.</summary>
    private long _deviceTimeAtGreenMs;
    private CancellationTokenSource? _sequenceCts;

    public bool IsRunning { get; private set; }

    /// <summary>
    /// The ghost cars picked for the current demo. Ghosts are not driven by
    /// this simulator - the ghost service runs them - so they are kept apart
    /// here and the host starts only these, rather than every enabled ghost.
    /// Empty when no demo is running or none were picked.
    /// </summary>
    public IReadOnlyCollection<string> SelectedGhosts { get; private set; } = Array.Empty<string>();

    public event Action<string>? LogMessage;

    /// <summary>
    /// Raised when the demo ended because something else took the session
    /// over - Race Control staging, starting or resetting a real race - as
    /// opposed to finishing on its own or being stopped from the Demo page.
    /// Carries a short reason. The session is left to whoever took it.
    /// </summary>
    public event Action<string>? TakenOver;

    /// <summary>
    /// The SessionGeneration of the session this demo is driving, as it
    /// stood after the demo's own Stage and again after its own green.
    ///
    /// The session is shared with Race Control, and a demo used to go on
    /// treating it as its own whatever happened to it: a real race staged
    /// over a demo kept receiving the demo's fake laps, was flagged as a
    /// demo itself (so not saved, or saved as [DEMO]), and a later Stop on
    /// the Demo page reset it. A generation that has moved on means
    /// somebody else staged or reset the session, and the demo lets go.
    /// </summary>
    private int _ownedGeneration = -1;

    /// <summary>
    /// True while the simulator itself is calling into the session, so the
    /// state watcher does not mistake the demo's own Stage, countdown, green
    /// or reset for somebody else taking the session over.
    /// </summary>
    private bool _drivingSession;

    /// <summary>
    /// True when this demo switched a split loop on for itself, so Stop
    /// can switch it back off.
    ///
    /// WHY IT HAS TO (14 Oct 2026, asked for from the floor). The sector
    /// calls - a sector best, a split up on a driver's own lap, a split
    /// ahead of record pace - were unrehearsable: every one of them needs
    /// RaceControlSession.SplitCount above nought, and that is set from the
    /// Connection page's split-loop tick box, which a club cannot
    /// honestly tick without a second decoder physically wired to the
    /// track. So a club could set those calls up, pick their phrasing, and
    /// have no way at all to hear one before a race night.
    ///
    /// The demo already pretends to be the line decoder; pretending to be
    /// the split decoder as well is the same promise. PUT BACK on Stop
    /// because it is the club's setting and not the demo's: a rehearsal
    /// that left the split loop switched on would have Race Control
    /// expecting sector reads from hardware that is not there.
    /// </summary>
    private bool _lentASplitLoop;

    private sealed class DemoRacer
    {
        public required string TransponderId { get; init; }
        public double BaseLapSeconds { get; init; }
        public double NextLapDueAtMs { get; set; }

        /// <summary>
        /// When this car is next due at each split loop, in order, on the
        /// simulated decoder clock - nought for a loop it is not booked at
        /// yet. Sized to the session's SplitCount when the demo starts.
        ///
        /// Scheduled a lap ahead by FireLap rather than worked out here,
        /// for the reason the lap times are stamped at their due moment:
        /// the tick is about every 100ms and a sector quantised to that
        /// reads as a rounding fault.
        /// </summary>
        public double[] SplitDueAtMs { get; set; } = Array.Empty<double>();
    }

    public DemoRaceSimulator(RaceControlSession race, AnnouncementService announcer)
    {
        _race = race;
        _announcer = announcer;
        _tickTimer.Tick += (_, _) => Tick();
        _race.RaceCompleted += OnRaceCompleted;
        _race.StateChanged += OnSessionStateChanged;
    }

    /// <summary>
    /// Whether the session is still this demo's: the demo is running, the
    /// session is still marked as a demo, and nobody has staged or reset it
    /// since the demo last moved it on.
    /// </summary>
    private bool OwnsSession() =>
        IsRunning && _race.IsDemoMode && _race.SessionGeneration == _ownedGeneration;

    /// <summary>
    /// Switches off the split loop this demo lent itself, if it did - see
    /// <see cref="_lentASplitLoop"/>.
    ///
    /// CALLED FROM EVERY WAY A DEMO CAN END, which is why it is a method
    /// and not two lines in Stop: a takeover is the dangerous one. Race
    /// Control stages a real race over a running demo, Release hands the
    /// session over, and a split loop left switched on would have that real
    /// race waiting on sector reads from a decoder nobody has wired up -
    /// every lap missing its sectors, and nothing anywhere saying why.
    ///
    /// Safe twice, and safe when the club had its own split loop on all
    /// along: the flag is only ever set when this demo turned one on.
    /// </summary>
    private void GiveBackTheSplitLoop()
    {
        if (!_lentASplitLoop)
        {
            return;
        }

        _lentASplitLoop = false;
        _race.SplitCount = 0;
    }

    /// <summary>Runs one of the demo's own session calls with the state watcher told it is the demo's.</summary>
    private void DriveSession(Action call)
    {
        _drivingSession = true;
        try
        {
            call();
        }
        finally
        {
            _drivingSession = false;
        }
    }

    /// <summary>
    /// The safety net under Race Control's own hand-over (see
    /// StopForTakeover): any change of race state the demo did not make
    /// itself means somebody else is using the session now - a Stage, a
    /// countdown, a Reset, an undo putting another race back - so the demo
    /// lets go of it rather than carrying on inside a real race.
    ///
    /// Finishing is the one exception: a checkered on the demo's own race,
    /// from Race Control or from its lap limit, is how a demo ends, and
    /// OnRaceCompleted deals with it.
    ///
    /// Nothing is reset here and the audio is left alone - whoever changed
    /// the state may have just queued its own call.
    /// </summary>
    private void OnSessionStateChanged(RaceState state)
    {
        if (!IsRunning || _drivingSession)
        {
            return;
        }

        if (state == RaceState.Finished && OwnsSession())
        {
            return;
        }

        Release("the race session was taken over from Race Control", clearAudio: false);
    }

    /// <summary>
    /// Ends the demo without touching the session, because somebody else
    /// now owns it. Unlike Stop, never resets - that would wipe the race
    /// that just took over. The demo is not finished, so it is never saved.
    /// </summary>
    private void Release(string reason, bool clearAudio)
    {
        if (!IsRunning)
        {
            return;
        }

        _sequenceCts?.Cancel();
        _tickTimer.Stop();
        IsRunning = false;
        _race.IsDemoMode = false;

        GiveBackTheSplitLoop();

        if (clearAudio)
        {
            _announcer.StopAllPendingAudio();
        }

        LogMessage?.Invoke($"Demo ended - {reason}. It was not saved.");
        TakenOver?.Invoke(reason);
    }

    /// <summary>
    /// Ends a running demo so Race Control can use the session for a real
    /// race - called by the host just BEFORE Race Control stages, starts or
    /// resets. The session is not reset (the caller is about to do its own
    /// thing with it), IsDemoMode is cleared so the real race is not taken
    /// for a demo, and the demo's backlog of queued lap calls is dropped so
    /// the real race's calls are heard promptly. Does nothing when no demo
    /// is running.
    /// </summary>
    public void StopForTakeover()
    {
        Release("Race Control took over the session for a real race", clearAudio: true);
    }

    /// <summary>
    /// Starts a simulated race: staging, countdown, green flag, then ongoing
    /// fake laps at varied but plausible pace until the race ends naturally
    /// (lap/time limit, exactly like a real race) or Stop() is called.
    /// </summary>
    /// <param name="realDriverTransponders">
    /// Transponder IDs of actual driver profiles to use for this run, if
    /// any. Using real profiles means announcements resolve real driver
    /// names (and can exercise rivalry lines), rather than the generic
    /// "Demo Racer N" placeholders.
    /// </param>
    /// <param name="placeholderRacerCount">
    /// Additional made-up racers to fill out the field with, using
    /// temporary demo transponder IDs that don't correspond to any saved
    /// profile. Used on its own when no real profiles are selected, or
    /// alongside them to pad out a small roster.
    /// </param>
    /// <param name="averageLapSeconds">
    /// The selected track layout's configured average lap time, if any -
    /// used as the base pace for every racer (with per-racer variance still
    /// applied) instead of an arbitrary made-up range, so a rehearsal times
    /// out closer to how a real race on that track actually feels.
    /// </param>
    public void Start(IReadOnlyList<string> realDriverTransponders, int placeholderRacerCount, int stagingSeconds, int countdownSeconds, double? averageLapSeconds)
    {
        Stop();

        _racers.Clear();

        var ghosts = new List<string>();

        foreach (var transponder in realDriverTransponders)
        {
            // A ghost is run by the ghost service at its own pace; driving it
            // here as well would put two cars on one transponder.
            if (GhostService.IsGhost(transponder))
            {
                ghosts.Add(transponder);
                continue;
            }

            _racers.Add(new DemoRacer
            {
                TransponderId = transponder,
                BaseLapSeconds = RandomizedBasePace(averageLapSeconds),
            });
        }

        for (var i = 0; i < placeholderRacerCount; i++)
        {
            _racers.Add(new DemoRacer
            {
                TransponderId = $"DEMO{i + 1:00}",
                BaseLapSeconds = RandomizedBasePace(averageLapSeconds),
            });
        }

        SelectedGhosts = ghosts;

        // A SPLIT LOOP FOR THE RUN, when the club has none - see
        // _lentASplitLoop. One, which is what the Connection page's tick
        // box buys; a club that already has it on keeps its own setting and
        // this lends nothing.
        _lentASplitLoop = _race.SplitCount == 0;

        if (_lentASplitLoop)
        {
            _race.SplitCount = 1;
        }

        foreach (var racer in _racers)
        {
            racer.SplitDueAtMs = new double[Math.Max(0, _race.SplitCount)];
        }

        LogMessage?.Invoke(_race.SplitCount > 0
            ? _lentASplitLoop
                ? $"Split timing on for the demo: {Plural.Of(_race.SplitCount, "loop")}, switched back off when it stops."
                : $"Split timing on: the club's own {Plural.Of(_race.SplitCount, "split loop")}."
            : "No split timing - sector calls will not fire.");

        _race.IsDemoMode = true;
        _deviceTimeMs = 0;
        IsRunning = true;

        // Until the demo's own Stage (below, before this returns) moves it
        // on, the session is the one it was handed.
        _ownedGeneration = _race.SessionGeneration;

        _sequenceCts = new CancellationTokenSource();
        _ = RunStartSequenceAsync(stagingSeconds, countdownSeconds, _sequenceCts.Token);
    }

    /// <summary>
    /// Picks a per-racer base pace with some spread so the leaderboard
    /// actually separates out, centered on the track's configured average
    /// lap time when one's given, or an arbitrary 5.0-7.5s range otherwise.
    /// </summary>
    private double RandomizedBasePace(double? averageLapSeconds)
    {
        if (averageLapSeconds is > 0)
        {
            return averageLapSeconds.Value * (0.9 + _random.NextDouble() * 0.2); // ±10% spread around the track's average
        }

        return 5.0 + _random.NextDouble() * 2.5;
    }

    /// <summary>
    /// The longest the countdown waits for the staging cue to finish. The
    /// same ten seconds Race Control's own start sequence allows - see
    /// RaceControlControl.LongestStagingCue, and keep the two the same.
    /// </summary>
    private static readonly TimeSpan LongestStagingCue = TimeSpan.FromSeconds(10);

    private async Task RunStartSequenceAsync(int stagingSeconds, int countdownSeconds, CancellationToken ct)
    {
        try
        {
            LogMessage?.Invoke("Staging...");
            DriveSession(_race.Stage);
            _ownedGeneration = _race.SessionGeneration;
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, stagingSeconds)), ct);

            // AND THEN LET THE STAGING CUE FINISH (13 Oct 2026).
            //
            // This is a SECOND copy of the start sequence - Race Control has
            // its own in RunAutoStartSequenceAsync - and the same fix had to
            // be made in both, which is the whole trouble with there being
            // two. Fixing only Race Control's left the demo cutting "bring
            // your cars to the grid" off mid-word exactly as before, because
            // a demo never goes near that method.
            //
            // The delay above is the staging length off this page's spinner,
            // whose minimum is one second, and it has no relationship to how
            // long the staging sound actually plays. So on a demo it is
            // nearly always too short.
            //
            // Bounded, for the reason Race Control's copy gives: a club may
            // point its staging sound at a long bed and the start must not
            // wait that out.
            await _announcer.WaitForAudioAsync(LongestStagingCue, ct);

            // Each step checks the session is still the demo's first. The
            // delays are cancelled by Stop and by a takeover, but a session
            // taken over by anything that did not come through either would
            // otherwise have had the demo's countdown and green dropped on
            // top of it.
            if (!StillInCharge(ct))
            {
                return;
            }

            LogMessage?.Invoke("Countdown...");
            DriveSession(_race.StartCountdown);
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, countdownSeconds)), ct);

            if (!StillInCharge(ct))
            {
                return;
            }

            LogMessage?.Invoke("Green flag - simulated race begins.");

            // As the session's own gate does: a red already out when the
            // green is asked for is the one this green clears, and only a
            // red thrown during the wait holds it.
            var flagAtRequest = _race.Flag;

            // Gated like every other start path, so the demo doesn't cut its
            // own countdown cue off - but the gate is awaited HERE rather
            // than through GreenFlagAfterAudioAsync, so the green itself is
            // only dropped once the demo has checked it still owns the
            // session. That call takes no token and could not be called off:
            // a real green pressed on Race Control during the demo's
            // countdown took the session over, and then the demo's own green
            // landed a moment later as well - the first of the two setting
            // the real race's start from the demo's made-up clock.
            if (_race.BeforeGreenFlagAsync is { } gate)
            {
                try
                {
                    await gate();
                }
                catch
                {
                    // An audio wait must never stop the demo from starting.
                }
            }

            // The wait can take a while, and Stop(), a fresh Start() or a
            // takeover may have happened meanwhile. Starting the tick timer
            // after any of them would fill a stopped demo, or a real race,
            // with fake laps.
            if (!StillInCharge(ct))
            {
                return;
            }

            // What the session's own gate refuses to start under, refused
            // here too.
            if (_race.State != RaceState.Countdown
                || (_race.Flag == FlagState.Red && flagAtRequest != FlagState.Red))
            {
                LogMessage?.Invoke($"Green held - the session is at {_race.State} with the {_race.Flag.ToString().ToLowerInvariant()} flag out. "
                    + "Press Stop and start the demo again.");
                return;
            }

            DriveSession(() => _race.GreenFlag(_deviceTimeMs));

            // The green starts a new session generation; that one is the
            // demo's too.
            _ownedGeneration = _race.SessionGeneration;

            foreach (var racer in _racers)
            {
                // Stagger initial laps slightly so racers don't all cross the line in the same tick.
                racer.NextLapDueAtMs = racer.BaseLapSeconds * 1000 * (0.5 + _random.NextDouble() * 0.5);
            }

            // The simulated clock runs on real time from here - see Tick.
            _deviceTimeAtGreenMs = _deviceTimeMs;
            _sinceGreen.Restart();

            _tickTimer.Start();
        }
        catch (OperationCanceledException)
        {
            // Stop() was called before the sequence reached green - nothing more to do.
        }
    }

    /// <summary>
    /// Whether the start sequence may take its next step: not cancelled,
    /// and the session still the demo's. A session somebody else has taken
    /// is let go of here, so the demo does not stay "running" with nothing
    /// driving it.
    /// </summary>
    private bool StillInCharge(CancellationToken ct)
    {
        if (ct.IsCancellationRequested || !IsRunning)
        {
            return false;
        }

        if (!OwnsSession())
        {
            Release("the race session was taken over from Race Control", clearAudio: false);
            return false;
        }

        return true;
    }

    private void Tick()
    {
        // A session taken over without going through StopForTakeover - the
        // demo flag cleared, or a new generation - gets no more fake laps.
        if (!OwnsSession())
        {
            Release("the race session was taken over from Race Control", clearAudio: false);
            return;
        }

        if (_race.State != RaceState.Racing)
        {
            return;
        }

        // The simulated decoder clock follows REAL time since the green, as
        // a real decoder's does. It used to add a fixed 100ms per timer tick
        // (55ms under yellow, nothing under red). A WinForms timer never
        // fires sooner than its interval - on Windows about every 109ms, and
        // later whenever the UI thread is busy - so the demo's clock ran
        // slower than real time. Ghosts picked for a demo are timed by real
        // time since the green (GhostService.NowMs), so a ghost with the
        // same pace as a demo car pulled away from it steadily, and under a
        // yellow by a lap or so, with nothing in either car's lap times to
        // show why. Read fresh from the stopwatch each tick, the clock can't
        // drift whatever the timer does.
        var nowMs = _deviceTimeAtGreenMs + _sinceGreen.ElapsedMilliseconds;
        var elapsedMs = Math.Max(0, nowMs - _deviceTimeMs);
        _deviceTimeMs = nowMs;

        // Flags slow or stop the CARS rather than the clock - again what
        // happens with real cars on a real decoder. Under caution the field
        // slows rather than stopping, which is what a yellow actually means:
        // laps still count, just more slowly, and now their times show it.
        // Under red every car is held where it is. Each racer's next
        // crossing is pushed back by the part of this tick it did not
        // cover: all of it under red, 45% of it under yellow.
        //
        // The red has to hold the cars, not just skip the tick. Under red
        // the session discards every pass, and the demo once ticked on
        // regardless - the racers' schedules kept advancing, so the flag
        // appeared to do nothing and then the whole field fired a lap at
        // once on green. Holding each car's next crossing back by the time
        // spent under red keeps the field where the red found it; the lap
        // that spans the stoppage comes out long, as a real car's does.
        var pace = _race.Flag switch
        {
            FlagState.Red => 0.0,
            FlagState.Yellow => 0.55,
            _ => 1.0,
        };

        if (pace < 1.0)
        {
            var heldBackMs = elapsedMs * (1.0 - pace);

            foreach (var racer in _racers)
            {
                racer.NextLapDueAtMs += heldBackMs;

                // The splits with them. Held back only where one is
                // actually booked: adding to a nought would book a car at
                // the loop a moment into the race, before it has a lap for
                // the read to belong to.
                for (var slot = 0; slot < racer.SplitDueAtMs.Length; slot++)
                {
                    if (racer.SplitDueAtMs[slot] > 0)
                    {
                        racer.SplitDueAtMs[slot] += heldBackMs;
                    }
                }
            }
        }

        if (_race.Flag == FlagState.Red)
        {
            return;
        }

        foreach (var racer in _racers)
        {
            // THE SPLITS BEFORE THE LINE, because that is the order a car
            // meets them: a split booked at 0.5 of the lap is read before
            // the crossing that ends it. Fired the other way round, the
            // read would arrive after the lap it belongs to had already
            // been counted, and the session would carry it over to the next
            // one - a whole lap of sectors wrong, on every lap.
            FireSplits(racer);

            if (_deviceTimeMs >= racer.NextLapDueAtMs)
            {
                FireLap(racer);
            }
        }
    }

    /// <summary>
    /// Any split loop this car is now due at, read into the session.
    ///
    /// STRAIGHT TO ProcessSplitPass, not through SplitLoop. That class
    /// exists to work out WHOSE split transponder a number is and to put
    /// the read on the line decoder's clock - two problems a demo does not
    /// have and cannot fake honestly: the made-up cars have no split
    /// transponder registered to anybody, so SplitLoop would correctly
    /// decide every read was nobody's and drop it. The simulator already
    /// goes straight to ProcessLapEvent for the same reason, and this is
    /// the same depth in: the session is where a real split read ends up,
    /// and everything downstream of it - the sectors, the sector calls, the
    /// leaderboard's split column - cannot tell the difference.
    ///
    /// ONE CLOCK. The demo's clock IS the line's clock, so the read goes
    /// in as it stands. On real hardware the two clocks are different and
    /// relating them is most of what SplitLoop does.
    /// </summary>
    private void FireSplits(DemoRacer racer)
    {
        for (var slot = 0; slot < racer.SplitDueAtMs.Length; slot++)
        {
            var due = racer.SplitDueAtMs[slot];

            if (due <= 0 || _deviceTimeMs < due)
            {
                continue;
            }

            // Cleared first: a read the session refuses - the car has no
            // counted lap yet, a red is out, it has taken the flag - must
            // not be offered again on every tick for the rest of the race.
            racer.SplitDueAtMs[slot] = 0;

            var point = (TimingPoint)(slot + 1);
            var at = (long)Math.Round(due);

            if (_race.ProcessSplitPass(racer.TransponderId, point, at))
            {
                LogMessage?.Invoke($"{racer.TransponderId} {SplitLoopNames.Of(point).ToLowerInvariant()}: {at / 1000.0:0.000}s");
            }
        }
    }

    private void FireLap(DemoRacer racer)
    {
        // Mostly consistent pace with some natural variance, occasionally a
        // notably faster lap (to exercise fast-lap/streak/track-record
        // announcements), and rarely an unrealistically-fast glitch pass
        // (to exercise the invalid-lap filter, same as real hardware noise).
        var roll = _random.NextDouble();
        double lapSeconds;
        if (roll < 0.05)
        {
            lapSeconds = 0.3 + _random.NextDouble() * 0.4; // glitch - MinLapTimeMs should reject this
        }
        else if (roll < 0.20)
        {
            lapSeconds = racer.BaseLapSeconds * (0.85 + _random.NextDouble() * 0.05); // a notably fast lap
        }
        else
        {
            lapSeconds = racer.BaseLapSeconds * (0.95 + _random.NextDouble() * 0.12); // normal variance
        }

        // Stamp the crossing at the moment the lap was actually DUE, not at
        // the current tick. The simulated clock only moves on when the
        // timer ticks, about every 100ms, so using it directly quantised every lap to a multiple of
        // 100ms - producing times like 7.100s and 6.500s that looked like a
        // display rounding bug, when really the demo just couldn't generate
        // anything finer. Real decoder hardware reports true millisecond
        // timestamps, so the demo should too.
        var crossingMs = racer.NextLapDueAtMs > 0
            ? (long)Math.Round(racer.NextLapDueAtMs)
            : _deviceTimeMs;

        racer.NextLapDueAtMs = crossingMs + lapSeconds * 1000;

        // WHERE ROUND THE NEXT LAP THIS CAR MEETS EACH LOOP. Evenly spaced
        // on paper - one loop sits at half distance, two at a third and two
        // thirds - with a few per cent of wander on each, because the whole
        // point of rehearsing the sector calls is that the sectors DIFFER:
        // loops placed at exactly the same fraction every lap would give
        // every car the same two sector times all race, and a sector best
        // would be set once on lap two and never again.
        for (var slot = 0; slot < racer.SplitDueAtMs.Length; slot++)
        {
            var evenly = (slot + 1.0) / (racer.SplitDueAtMs.Length + 1.0);
            var wander = (_random.NextDouble() - 0.5) * 0.08;
            var through = Math.Clamp(evenly + wander, 0.05, 0.95);

            racer.SplitDueAtMs[slot] = crossingMs + lapSeconds * 1000 * through;
        }

        _race.ProcessLapEvent(new DirectLapEvent
        {
            TransponderId = racer.TransponderId,
            DeviceTimeMs = (uint)Math.Max(0, crossingMs),
            SignalHits = 25 + _random.Next(20),
            SignalStrength = 70 + _random.Next(60),
        });

        LogMessage?.Invoke($"{racer.TransponderId} lap: {lapSeconds:0.000}s");
    }

    private void OnRaceCompleted()
    {
        // Every race's finish raises this, not only a demo's. Without this
        // check a REAL race finishing also cleared the audio queue - which
        // can hold the finish calls Race Control had just queued - and
        // wrote "Simulated race complete" to the Demo page's log.
        if (!IsRunning)
        {
            return;
        }

        // Running, but the race that finished is not the demo's any more.
        if (!OwnsSession())
        {
            Release("the race session was taken over from Race Control", clearAudio: false);
            return;
        }

        _tickTimer.Stop();
        IsRunning = false;
        _race.IsDemoMode = false;

        GiveBackTheSplitLoop();

        // Clear the audio backlog, exactly as Stop() does. A demo can
        // generate laps faster than they can be announced, so finishing on
        // a lap or time limit used to leave a queue of beeps still playing
        // long after the race was over - which reads as the app being stuck.
        _announcer.StopAllPendingAudio();

        LogMessage?.Invoke("Simulated race complete - any queued announcements were cleared.");
    }

    /// <summary>
    /// Stops the simulation immediately, whatever stage it's at - equivalent
    /// to a real Reset. Also clears any announcements already queued but
    /// not yet played (see AnnouncementService.StopAllPendingAudio) - a
    /// demo can generate laps much faster than real hardware does, so
    /// without this a backlog can keep announcing itself for a while after
    /// Stop is clicked, which reads as the app being stuck rather than as
    /// a backlog draining.
    ///
    /// Only resets a session the demo still owns. One that Race Control has
    /// since staged or started a real race on is not the demo's to reset:
    /// Stop used to reset it regardless, so pressing Stop on the Demo page
    /// after a takeover wiped a live real race.
    /// </summary>
    public void Stop()
    {
        var wasRunning = IsRunning;
        var ownsSession = OwnsSession();

        _sequenceCts?.Cancel();
        _tickTimer.Stop();

        // Cleared before the reset, so the state watcher sees a demo that
        // has already stopped rather than one being taken over.
        IsRunning = false;

        if (wasRunning && ownsSession)
        {
            DriveSession(_race.Reset);
        }

        // Only the simulator ever sets this, so clearing it is right
        // whoever has the session now.
        _race.IsDemoMode = false;

        GiveBackTheSplitLoop();

        if (wasRunning && !ownsSession)
        {
            // Running, but the session is somebody else's now: let go of it
            // without touching it or its audio, exactly as a takeover does.
            LogMessage?.Invoke("Stopped. The race session had already been taken over from Race Control, so it was left as it is.");
            return;
        }

        _announcer.StopAllPendingAudio();

        if (wasRunning)
        {
            LogMessage?.Invoke("Stopped - any queued announcements were cleared.");
        }
    }

    public void Dispose()
    {
        _race.RaceCompleted -= OnRaceCompleted;
        _race.StateChanged -= OnSessionStateChanged;
        Stop();
        _tickTimer.Dispose();
        _sequenceCts?.Dispose();
    }
}
