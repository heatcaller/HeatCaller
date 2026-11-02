namespace HeatCaller;

/// <summary>
/// MainForm's side of TimingWatch: feeding it the decoder's heartbeats and
/// the race's crossings, asking it every race tick what is wrong, logging
/// when that changes, and handing the line to Race Control's Next Up bar.
/// </summary>
/// <remarks>
/// Here rather than in Race Control because the decoders are MainForm's,
/// and because the race tick runs whichever page is showing - a red flag
/// or a restart has to be noticed even while somebody is on another page,
/// or the silence during it would be counted as cars gone missing.
/// </remarks>
public partial class MainForm
{
    private readonly TimingWatch _timingWatch = new();

    /// <summary>The problem showing now, by key, so the console hears about each one once.</summary>
    private string? _timingProblemKey;

    /// <summary>The TIMING line for the Next Up bar; null when all is well.</summary>
    private string? _timingProblemText;

    /// <summary>
    /// TimingWatch's clock: milliseconds since Windows started, which only
    /// ever goes forward. DateTime.UtcNow can step - a time sync, a clock
    /// change - and a step forward would read as seconds of silence. Only
    /// ever compared with itself; any thread.
    /// </summary>
    private static DateTime WatchClock() =>
        new(Environment.TickCount64 * TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);

    /// <summary>Hooks TimingWatch to the decoders and the race. Called once, from the constructor, after both exist.</summary>
    private void WireTimingWatch()
    {
        // On the decoder's own thread - TimingWatch stores these atomically.
        // Connected is set before the read loop starts, so it always comes
        // before that connection's first heartbeat.
        // The LINE decoder's only: an EasyLap split loop's connection and
        // heartbeat say nothing about whether laps are being timed.
        _decoderClient.StatusChanged += (state, _) =>
        {
            if (state == DecoderConnectionState.Connected && _lineDecoder == DecoderConnection.EasyLap)
            {
                _timingWatch.DecoderConnected(WatchClock());
            }
        };

        _decoderClient.HeartbeatReceived += _ =>
        {
            if (_lineDecoder == DecoderConnection.EasyLap)
            {
                _timingWatch.Heartbeat(WatchClock());
            }
        };

        // Every crossing the race judged, counted or thrown out as too soon
        // - either way the car is out there. Under its driver's own
        // transponder, alternates already folded in. Ghosts don't count.
        _race.LapProcessed += (standing, _, _) =>
        {
            if (!GhostService.IsGhost(standing.TransponderId))
            {
                _timingWatch.Pass(standing.TransponderId, WatchClock());
            }
        };
    }

    /// <summary>Asks TimingWatch what is wrong; logs a new problem, or its end, once. Called every race tick.</summary>
    private void UpdateTimingWatch()
    {
        TimingWatch.Problem? problem;
        var racing = false;

        try
        {
            var situation = TimingSituation();
            racing = situation.Racing;
            problem = _timingWatch.Check(situation, WatchClock());

            // The line's problems first: they're about laps. That includes
            // the decoder being off before the green - a heat staged with
            // it unplugged is the moment to say so, not a lap into the
            // race. The split loop's only show while the line is fine.
            problem ??= TimingWatch.DecoderOffBeforeTheGreen(_race.State, _race.IsDemoMode, situation.DecoderConnected);

            problem ??= SplitLoopProblem(situation);
        }
        catch (Exception ex)
        {
            // A watch that throws must not take the race tick with it.
            problem = null;

            if (_timingProblemKey != "check-failed")
            {
                _timingProblemKey = "check-failed";
                OnClientRawMessage($"{AppTime.LogStampNow()}  [timing] The timing check failed and is off for now: {ex.Message}");
            }

            _timingProblemText = null;
            return;
        }

        _timingProblemText = problem?.Text;

        if (string.Equals(problem?.Key, _timingProblemKey, StringComparison.Ordinal))
        {
            return;
        }

        if (problem is not null)
        {
            OnClientRawMessage($"{AppTime.LogStampNow()}  [timing] WARNING: {problem.Text}");
        }
        else if (racing && _timingProblemKey is not null && _timingProblemKey != "check-failed"
                 && !_timingProblemKey.StartsWith("missed-lap:", StringComparison.Ordinal))
        {
            // Only while racing: a warning that ends because the race did
            // (the flag, a red, a reset) hasn't been put right. Nor has a
            // possible missed lap that has simply been shown long enough.
            OnClientRawMessage($"{AppTime.LogStampNow()}  [timing] That's cleared - timing looks normal again.");
        }

        _timingProblemKey = problem?.Key;
    }

    /// <summary>The race as TimingWatch needs to see it.</summary>
    private TimingWatch.Situation TimingSituation()
    {
        var racing = _race.State == RaceState.Racing
                     && _race.Flag != FlagState.Red
                     && !_race.IsPracticeMode
                     && !_race.IsDemoMode;

        var connected = StateOf(SelectedDecoder) == DecoderConnectionState.Connected;

        if (!racing)
        {
            return new TimingWatch.Situation(false, false, connected, Array.Empty<TimingWatch.Car>());
        }

        // Nobody released yet - a hand-released start waiting on the
        // director, or a staggered one before its first release - is not a
        // race with nothing crossing. With no grid at all, everyone is
        // released at the green.
        var grid = _race.Grid;
        var anyoneReleased = grid.Count == 0 || grid.Any(_race.IsReleased);

        // A race of nothing but ghost cars has no decoder crossings to miss.
        var ghostsOnly = grid.Count > 0 && grid.All(t => GhostService.IsGhost(t));

        if (!anyoneReleased || ghostsOnly)
        {
            return new TimingWatch.Situation(false, false, connected, Array.Empty<TimingWatch.Car>());
        }

        // The cars that have crossed in this race so far. Not the grid: with
        // no round selected it is the LAST race's finishers, and a round's
        // roster can hold a no-show, and either would be named as missing
        // all race. Not a standing on its own either: a hand-released start
        // makes one for every grid row at the green, so a released no-show
        // would be named too - hence "has crossed": a lap in the list (a
        // jump-start credit, or a race brought back from the journal counts)
        // or a pass seen, even one thrown out as too soon. So a transponder flat from the very start isn't
        // caught here; Pre-race check (ReadinessCheckForm) is for that, before
        // the heat. Left out as well: ghosts, cars not yet released (a held
        // row), cars that have taken the flag, a black-flagged driver (called
        // in), and a car whose own window is over in a staggered start that
        // doesn't finish on the next crossing (its passes aren't counted).
        var cars = _race.GetOrderedStandings()
            .Where(s => !GhostService.IsGhost(s.TransponderId)
                        && (s.LapTimesMs.Count > 0 || s.LastSeenDeviceTimeMs != 0)
                        && _race.IsReleased(s.TransponderId)
                        && !_race.HasTakenFlag(s.TransponderId)
                        && !s.IsBlackFlagged
                        && !(!_race.FinishOnNextCrossing && _race.IsOutOfTime(s.TransponderId)))
            .Select(s =>
            {
                var name = _profiles.Get(s.TransponderId)?.DriverName;

                return new TimingWatch.Car(
                    s.TransponderId,
                    string.IsNullOrWhiteSpace(name) ? s.TransponderId : name,
                    TypicalLapMs(s));
            })
            .ToList();

        return new TimingWatch.Situation(true, _race.IsFinishing, connected, cars, HeartbeatsExpected: SelectedDecoder == DecoderConnection.EasyLap);
    }

    /// <summary>
    /// A car's usual lap so far: the median of its timed laps, leaving out
    /// the start lap (index 0) and any lap run through a crash recovery.
    /// Zero before it has one.
    /// </summary>
    private static long TypicalLapMs(RacerStanding standing)
    {
        var laps = standing.LapTimesMs
            .Select((ms, index) => (Ms: ms, Index: index))
            .Where(l => l.Index > 0 && l.Ms > 0 && !standing.IsRecoveryLap(l.Index))
            .Select(l => l.Ms)
            .OrderBy(ms => ms)
            .ToList();

        return laps.Count == 0 ? 0 : laps[laps.Count / 2];
    }
}
