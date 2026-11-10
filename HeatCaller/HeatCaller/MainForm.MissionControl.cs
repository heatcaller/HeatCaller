namespace HeatCaller;

/// <summary>
/// MainForm's side of Mission Control (MissionControl, MissionControlForm)
/// and of the transponder health record (TransponderHealthStore): gathering
/// the snapshot the checks are judged from - the decoders, the cars heard,
/// the next race's grid, timing, the website, the sound, the cloud backups -
/// and feeding every pass to the health record.
///
/// Here because the decoders, the relay and the announcer are MainForm's;
/// Race Control lends the next race's roster (PreRaceRoster).
/// </summary>
public partial class MainForm
{
    /// <summary>The Mission Control window while it is open. One at a time.</summary>
    private MissionControlForm? _missionControl;

    /// <summary>When the EasyLap's last heartbeat arrived (Environment.TickCount64), or 0 since it connected and before its first. Any thread.</summary>
    private long _lastHeartbeatTicks;

    /// <summary>When the EasyLap last connected (Environment.TickCount64) - so one that connects and never sends a heartbeat is called quiet too.</summary>
    private long _connectedTicks;

    /// <summary>Transponders already called fading in the console, by night - "2026-10-03|4385919" - so each is said once a night.</summary>
    private HashSet<string>? _fadingSaid;

    /// <summary>
    /// Since when (DateTime.UtcNow ticks) a pass counts as "heard" for the
    /// next race's grid, or 0 before any race has been staged or finished
    /// this session - then TransponderWatch's own window is all there is.
    /// Any thread - see WireMissionControl.
    ///
    /// TransponderWatch remembers a car for twelve minutes, so a car that
    /// ran the heat before and whose transponder died on its last lap still
    /// counted as heard, and Mission Control said "All clear" while the Big
    /// Screen's roll call said WAITING. This applies the roll call's rule
    /// (RollCall.Grace): heard since the race was staged, less the grace -
    /// and never from before the last race finished, so a pass made in
    /// that race is not taken as proof the transponder is alive now.
    /// </summary>
    private long _gridHeardSinceTicks;

    /// <summary>Hooks the heartbeat clock. Called once, from the constructor, after the decoders exist.</summary>
    private void WireMissionControl()
    {
        // On the decoder's own thread; a plain store is enough for a clock
        // only ever read for how long ago it was.
        _decoderClient.HeartbeatReceived += _ => Interlocked.Exchange(ref _lastHeartbeatTicks, Environment.TickCount64);

        _decoderClient.StatusChanged += (state, _) =>
        {
            if (state == DecoderConnectionState.Connected)
            {
                Interlocked.Exchange(ref _lastHeartbeatTicks, 0);
                Interlocked.Exchange(ref _connectedTicks, Environment.TickCount64);
            }
        };

        // Where the grid check starts counting from - see _gridHeardSinceTicks.
        var previousState = _race.State;
        _race.StateChanged += state =>
        {
            var wasStaged = previousState is RaceState.Staging or RaceState.Countdown;
            previousState = state;

            if (state == RaceState.Finished)
            {
                var finished = DateTime.UtcNow;
                Interlocked.Exchange(ref _gridHeardSinceTicks, finished.Ticks);

                // WHY (2026-11-05, Bug hunt 11): shared with the Big Screen's
                // roll call (RollCall.Since), so a screen opened after this
                // finish still starts its roll call after it too.
                RollCall.NoteFinish(finished);
            }
            else if ((state is RaceState.Staging or RaceState.Countdown) && !wasStaged)
            {
                // Staged (or straight to the countdown): the roll call starts
                // now, less its grace - but not from before the last finish.
                var staged = (DateTime.UtcNow - RollCall.Grace).Ticks;
                Interlocked.Exchange(ref _gridHeardSinceTicks, Math.Max(staged, Interlocked.Read(ref _gridHeardSinceTicks)));
            }
        };
    }

    /// <summary>Opens Mission Control, or brings it to the front when it is open already.</summary>
    internal void ShowMissionControl()
    {
        if (_missionControl is { IsDisposed: false } open)
        {
            if (open.WindowState == FormWindowState.Minimized)
            {
                open.WindowState = FormWindowState.Normal;
            }

            open.Activate();
            return;
        }

        _missionControl = new MissionControlForm(() => MissionControl.Assess(MissionControlInputs()));
        _missionControl.FormClosed += (_, _) => _missionControl = null;
        _missionControl.Show(this);
    }

    /// <summary>Everything Mission Control judges, read now. UI thread.</summary>
    private MissionControl.Inputs MissionControlInputs()
    {
        var lineState = StateOf(SelectedDecoder);

        TimeSpan? heartbeat = null;
        var lastBeat = Interlocked.Read(ref _lastHeartbeatTicks);

        // The heartbeat is the USB EasyLap's alone.
        if (SelectedDecoder == DecoderConnection.EasyLap && lineState == DecoderConnectionState.Connected)
        {
            // Since its last heartbeat - or, if none has come yet, since it
            // connected, once that is longer than a heartbeat can be late.
            var since = lastBeat > 0 ? lastBeat : Interlocked.Read(ref _connectedTicks);

            if (since > 0)
            {
                var age = TimeSpan.FromMilliseconds(Environment.TickCount64 - since);

                if (lastBeat > 0 || age >= MissionControl.HeartbeatLate)
                {
                    heartbeat = age;
                }
            }
        }

        var heard = _transponderWatch.Recent();

        DecoderConnectionState? split = _settings.SplitLoopEnabled
            ? StateOf(OtherDecoder(SelectedDecoder))
            : null;

        IReadOnlyList<string>? roster = null;
        var missing = new List<string>();

        if (_raceControlControl is not null)
        {
            var (expected, _) = _raceControlControl.PreRaceRoster();

            if (expected.Count > 0)
            {
                roster = expected.Select(DriverNameOf).ToList();
                // Heard since the grid check began, not merely within
                // TransponderWatch's twelve minutes - see _gridHeardSinceTicks.
                var sinceTicks = Interlocked.Read(ref _gridHeardSinceTicks);
                missing = expected
                    .Where(t => _transponderWatch.Of(t) is not { } sighting || sighting.LastSeenUtc.Ticks < sinceTicks)
                    .Select(DriverNameOf)
                    .ToList();
            }
        }

        var fading = _transponderHealth
            .Fading(TimeSpan.FromDays(31))
            .Select(h => $"{DriverNameOf(h.Transponder)} - {h.Summary}")
            .ToList();

        return new MissionControl.Inputs(
            LineDecoder: DecoderConnection.ShortName(SelectedDecoder),
            LineState: lineState,
            HeartbeatAge: heartbeat,
            LastPassAge: heard.Count > 0 ? heard[0].Ago : null,
            SplitState: split,
            CarsHeard: heard.Count,
            WeakCars: heard.Where(s => s.SignalKnown && s.Weak).Select(s => DriverNameOf(s.TransponderId)).ToList(),
            Roster: roster,
            RosterMissing: missing,
            TimingProblem: _timingProblemText,
            Fading: fading,
            Relay: _relayPublisher.Status,
            VoiceMuted: _announcer.Muted,
            CloudBackup: CloudBackupLight(),
            Allowances: UsageForMissionControl());
    }

    /// <summary>The cloud backup light as last worked out, and when (Environment.TickCount64). UI thread.</summary>
    private CloudBackupStatus.State? _cloudBackupLight;

    private long _cloudBackupLightTicks;

    /// <summary>How long the cloud backup light is kept before it is worked out again - see CloudBackupLight.</summary>
    private static readonly TimeSpan CloudBackupLightFor = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Whether the backups are reaching the cloud (CloudBackupStatus), kept
    /// for half a minute at a time.
    ///
    /// Mission Control reads its checks once a second, and working this one
    /// out looks for each service's sync folder on disk - which is why
    /// CloudBackup.FindSyncRoot takes such care over drives that can hang.
    /// Nothing about it changes by the second, so it is not asked by the
    /// second either.
    /// </summary>
    private CloudBackupStatus.State CloudBackupLight()
    {
        var now = Environment.TickCount64;

        if (_cloudBackupLight is { } kept && now - _cloudBackupLightTicks < CloudBackupLightFor.TotalMilliseconds)
        {
            return kept;
        }

        _cloudBackupLightTicks = now;
        return _cloudBackupLight = CloudBackupStatus.Assess(CloudBackupStatus.Read(_settings, DateTime.UtcNow));
    }

    /// <summary>A driver's name for a transponder, or the number when nobody has it.</summary>
    private string DriverNameOf(string transponder) =>
        _profiles.Get(transponder)?.DriverName is { Length: > 0 } name ? name : transponder;

    /// <summary>
    /// Adds a pass to the health record, and says in the console - once a
    /// night - when a transponder is reading weaker than it usually does.
    /// Asked once the night has enough readings to judge, so the first pass
    /// of the night is not compared on its own.
    /// </summary>
    private void RecordTransponderHealth(DirectLapEvent lap)
    {
        try
        {
            _transponderHealth.Record(lap);

            if (lap.IsSynthetic || lap.IsSimulated || lap.SignalHits <= 0)
            {
                return;
            }

            var key = TransponderHealthStore.NightOf(DateTime.Now) + "|" + lap.TransponderId;
            _fadingSaid ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (_fadingSaid.Contains(key))
            {
                return;
            }

            var health = _transponderHealth.Assess(lap.TransponderId);

            if (health.Latest is not { } tonight
                || tonight.Date != TransponderHealthStore.NightOf(DateTime.Now)
                || tonight.Readings < TransponderHealthStore.LeastReadings)
            {
                return;
            }

            // Judged once tonight has enough to go on, whatever it says.
            _fadingSaid.Add(key);

            if (health.Verdict == TransponderHealthStore.Verdict.Fading)
            {
                OnClientRawMessage($"{AppTime.LogStampNow()}  [transponders] {DriverNameOf(lap.TransponderId)}'s transponder "
                                   + $"{lap.TransponderId} is {health.Summary} - check its battery and how it's mounted before it drops out of a race.");
            }
        }
        catch (Exception ex)
        {
            // Advice only: it must never cost the race the pass.
            OnClientRawMessage($"{AppTime.LogStampNow()}  [transponders] Couldn't record a pass's signal: {ex.Message}");
        }
    }
}
