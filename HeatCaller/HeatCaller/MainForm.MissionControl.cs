namespace HeatCaller;

/// <summary>
/// MainForm's side of Mission Control (MissionControl, MissionControlForm)
/// and of the transponder health record (TransponderHealthStore): gathering
/// the snapshot the checks are judged from - the decoders, the cars heard,
/// the next race's grid, timing, the website, the sound - and feeding every
/// pass to the health record.
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
        var iLap = SelectedDecoder == DecoderConnection.ILap;
        var lineState = iLap ? _ilapClient.State : _decoderClient.State;

        TimeSpan? heartbeat = null;
        var lastBeat = Interlocked.Read(ref _lastHeartbeatTicks);

        if (!iLap && lineState == DecoderConnectionState.Connected)
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
                missing = expected.Where(t => _transponderWatch.Of(t) is null).Select(DriverNameOf).ToList();
            }
        }

        var fading = _transponderHealth
            .Fading(TimeSpan.FromDays(31))
            .Select(h => $"{DriverNameOf(h.Transponder)} - {h.Summary}")
            .ToList();

        return new MissionControl.Inputs(
            LineDecoder: iLap ? "I-Lap" : "EasyLap",
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
            VoiceMuted: _announcer.Muted);
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

            if (lap.IsSynthetic || lap.SignalHits <= 0)
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
