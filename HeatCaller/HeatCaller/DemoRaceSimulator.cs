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

    private sealed class DemoRacer
    {
        public required string TransponderId { get; init; }
        public double BaseLapSeconds { get; init; }
        public double NextLapDueAtMs { get; set; }
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

    private async Task RunStartSequenceAsync(int stagingSeconds, int countdownSeconds, CancellationToken ct)
    {
        try
        {
            LogMessage?.Invoke("Staging...");
            DriveSession(_race.Stage);
            _ownedGeneration = _race.SessionGeneration;
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(1, stagingSeconds)), ct);

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

        // Flags are authoritative even in a demo. Previously this ticked on
        // regardless: under red the session discarded every pass while the
        // racers' schedules kept advancing, so the flag appeared to do
        // nothing and then the whole field fired a lap at once on green.
        if (_race.State != RaceState.Racing || _race.Flag == FlagState.Red)
        {
            return;
        }

        // Under caution the field slows rather than stopping, which is what
        // a yellow actually means - laps still count, just more slowly.
        // _deviceTimeMs is a long, so the scaled advance is rounded rather
        // than truncated - truncating a 55% tick would drift the clock slow.
        var pace = _race.Flag == FlagState.Yellow ? 0.55 : 1.0;
        _deviceTimeMs += (long)Math.Round(_tickTimer.Interval * pace);

        foreach (var racer in _racers)
        {
            if (_deviceTimeMs >= racer.NextLapDueAtMs)
            {
                FireLap(racer);
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
        // the current tick. The simulated clock only advances in 100ms
        // steps, so using it directly quantised every lap to a multiple of
        // 100ms - producing times like 7.100s and 6.500s that looked like a
        // display rounding bug, when really the demo just couldn't generate
        // anything finer. Real decoder hardware reports true millisecond
        // timestamps, so the demo should too.
        var crossingMs = racer.NextLapDueAtMs > 0
            ? (long)Math.Round(racer.NextLapDueAtMs)
            : _deviceTimeMs;

        racer.NextLapDueAtMs = crossingMs + lapSeconds * 1000;

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
