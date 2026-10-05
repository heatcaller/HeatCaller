namespace HeatCaller;

/// <summary>
/// MainForm's split loop: the second decoder, partway round the track.
///
/// The decoder chosen in "Connect via:" is the start/finish LINE, as it has
/// always been; with "Split loop:" on, the other one is connected too, and
/// every pass it reads goes to SplitLoop instead of the race - an I-Lap
/// beside an EasyLap line, or the other way about. Which decoder a pass came
/// from decides where it goes (RoutePass); the line's passes take exactly
/// the path they always did (OnDecoderLapEvent).
///
/// The core rule: the split loop can never change a lap. If it unplugs,
/// misreads or reads a stranger, the race is what it would have been
/// without it - and TimingWatch's TIMING line says the split loop is down
/// or quiet.
/// </summary>
public partial class MainForm
{
    private readonly ComboBox cmbSplitLoop = new();
    private readonly Button btnConnectSplit = new();
    private readonly Button btnLineUp = new();
    private readonly Panel pnlSplitDot = new();
    private readonly Label lblSplitStatus = new();
    private readonly System.Windows.Forms.Timer _splitStatusTimer = new() { Interval = 1000 };
    private Label? lblSplitStatusCaption;
    private Control? splitStatusLine;

    private SplitLoop? _splitLoop;

    /// <summary>
    /// The line decoder (DecoderConnection), kept for the decoders' own
    /// threads, which must not read the picker. Set by UpdateConnectionModeUi.
    /// </summary>
    private volatile int _lineDecoder = DecoderConnection.EasyLap;

    /// <summary>False until UpdateConnectionModeUi has first set _lineDecoder from the saved choice - so starting up on I-Lap isn't taken for a switch.</summary>
    private bool _lineDecoderKnown;

    /// <summary>A message for the split loop's line that stays until its decoder's state changes - see RenderSplitStatus.</summary>
    private (string Text, DecoderConnectionState State)? _splitNote;

    /// <summary>The sector lengths last worked out on the UI thread - see SectorLengthsFeetProvider.</summary>
    private volatile double[]? _sectorLengthsFeet;

    /// <summary>When the line last read a real car (TimingClock), for the split loop's "quiet" warning.</summary>
    private long? _lastLinePassAt;

    /// <summary>The last possible missed lap, for the TIMING line: its key, its text and when (TimingClock) - shown for <see cref="MissedLapShownMs"/>.</summary>
    private (string Key, string Text, long At)? _missedLapNote;

    private const long MissedLapShownMs = 20_000;

    /// <summary>When the split loop started being watched in this race (TimingClock), or null while not racing.</summary>
    private long? _splitWatchSince;

    /// <summary>Every pass the line decoder hands the race - for Line Up, which pairs them with the split loop's.</summary>
    private event Action<DirectLapEvent>? LinePassSeen;

    /// <summary>Whether a decoder is the line, the split loop, or not in use.</summary>
    private enum DecoderRole
    {
        None,
        Line,
        Split,
    }

    /// <summary>The decoder that isn't <paramref name="line"/>.</summary>
    private static int OtherDecoder(int line) =>
        line == DecoderConnection.ILap ? DecoderConnection.EasyLap : DecoderConnection.ILap;

    /// <summary>What a decoder is for right now. UI thread.</summary>
    private DecoderRole RoleOf(int decoder) =>
        decoder == SelectedDecoder ? DecoderRole.Line
        : _settings.SplitLoopEnabled && decoder == OtherDecoder(SelectedDecoder) ? DecoderRole.Split
        : DecoderRole.None;

    /// <summary>Whether the LINE decoder is connected - whichever it is.</summary>
    private bool LineDecoderConnected() =>
        _lineDecoder == DecoderConnection.ILap
            ? _ilapClient.State == DecoderConnectionState.Connected
            : _decoderClient.State == DecoderConnectionState.Connected;

    private DecoderConnectionState StateOf(int decoder) =>
        decoder == DecoderConnection.ILap ? _ilapClient.State : _decoderClient.State;

    /// <summary>Makes the split loop and hooks it to the decoders. Called once, from the constructor, after both exist.</summary>
    private void WireSplitLoop()
    {
        var split = new SplitLoop(_profiles.SplitOwnerOf)
        {
            // Only so the console can say a read is contested, not unowned.
            ClaimantsOf = transponder => _profiles.SplitOwnersOf(transponder).Count,

            // An EasyLap's own clock mapped onto the app's; an I-Lap's
            // passes are already on it (when they arrived).
            AppTimeOf = pass => pass.Decoder == DecoderConnection.EasyLap
                ? _decoderClient.Clock.ToApp(pass.DeviceTimeMs, TimingClock.NowMs)
                : pass.ReceivedMs != 0 ? pass.ReceivedMs : null,

            // The line's clock: an EasyLap's own, mapped; an I-Lap's is the app's.
            LineTimeOf = app => _lineDecoder == DecoderConnection.EasyLap
                ? _decoderClient.Clock.ToDevice(app, TimingClock.NowMs)
                : app,

            OffsetMs = () => _settings.SplitLoopOffsetMs,
        };

        split.Log += line => OnClientRawMessage($"{AppTime.LogStampNow()}  {line}");
        _splitLoop = split;

        // Sector lengths for the speed columns: from the split loops placed
        // on the plan of the layout being raced on - the Practice page's in
        // practice (RunningTrackName). It used to be Race Control's always,
        // so practice speeds came from another layout's sectors, or dashes.
        //
        // Worked out on the UI thread, where the layouts are changed; the
        // web server asks from its own, and gets the last answer worked out.
        _race.SectorLengthsFeetProvider = () =>
        {
            if (!IsHandleCreated || InvokeRequired)
            {
                return _sectorLengthsFeet;
            }

            var name = RunningTrackName(_settings.LastRaceControlTrackName);

            _sectorLengthsFeet = string.IsNullOrWhiteSpace(name)
                ? null
                : _trackLayouts.All.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal))?.SectorLengthsFeet();

            return _sectorLengthsFeet;
        };

        // Into the race: the row the car is on, and where on the line's
        // clock it crossed. Nothing goes in while the clocks can't be tied
        // (LineMs null) - a split time that is a guess is worse than none.
        split.PassRecorded += pass =>
        {
            if (pass.LineMs is { } line && RowForSplit(pass) is { } row)
            {
                _race.ProcessSplitPass(row, pass.Point, line);
            }
        };

        // A lap the line may have missed goes on the TIMING line for a
        // while - the race director decides; nothing is added.
        _race.PossibleMissedLap += (standing, _, _) =>
        {
            var name = _profiles.Get(standing.TransponderId)?.DriverName;
            var who = string.IsNullOrWhiteSpace(name) ? standing.TransponderId : name.Trim();

            _missedLapNote = (
                $"missed-lap:{standing.TransponderId}:{TimingClock.NowMs}",
                $"Possible missed lap: {who} was read at the split twice without crossing the line - check their lap count",
                TimingClock.NowMs);
        };

        _splitStatusTimer.Tick += (_, _) => RenderSplitStatus();
        _splitStatusTimer.Start();
    }

    /// <summary>A pass from the EasyLap, whatever it is for. Any thread.</summary>
    private void OnEasyLapPass(DirectLapEvent lap) => OnUiThread(() => RoutePass(lap));

    /// <summary>A pass from the I-Lap, whatever it is for. Any thread.</summary>
    private void OnILapPass(DirectLapEvent lap) => OnUiThread(() => RoutePass(lap));

    /// <summary>
    /// Sends a pass where its decoder's role says: the line's to the race,
    /// exactly as before; the split loop's to SplitLoop; and one from a
    /// decoder in neither role - only possible for the moment after a
    /// switch - nowhere.
    /// </summary>
    private void RoutePass(DirectLapEvent lap)
    {
        switch (RoleOf(lap.Decoder))
        {
            case DecoderRole.Line:
                OnDecoderLapEvent(lap);
                break;

            case DecoderRole.Split:
                _splitLoop?.Record(lap.Stamped(lap.Decoder, lap.ReceivedMs, TimingPoint.Split1));
                break;

            default:
                OnClientRawMessage($"{AppTime.LogStampNow()}  [decoder] A pass from a decoder that is neither the line nor the split loop now ({lap.TransponderId}) - not used.");
                break;
        }
    }

    /// <summary>The split loop choice: none, or the decoder that isn't the line.</summary>
    private void FillSplitLoopChoices()
    {
        var other = OtherDecoder(SelectedDecoder);
        var label = DecoderConnection.Items()[DecoderConnection.IndexOf(other)];

        _fillingSplitChoices = true;

        try
        {
            cmbSplitLoop.Items.Clear();
            cmbSplitLoop.Items.Add("None");
            cmbSplitLoop.Items.Add(label);
            cmbSplitLoop.SelectedIndex = _settings.SplitLoopEnabled ? 1 : 0;
        }
        finally
        {
            _fillingSplitChoices = false;
        }

        // Its longest choice changes with the line - measured again.
        _splitPickerWidth = null;
        SizeConnectionFields();
    }

    private bool _fillingSplitChoices;

    private void CmbSplitLoop_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_fillingSplitChoices)
        {
            return;
        }

        var on = cmbSplitLoop.SelectedIndex == 1;

        if (on == _settings.SplitLoopEnabled)
        {
            return;
        }

        // Not mid-race: switching the split loop on or off changes how the
        // race's laps are cut into sectors partway through, and off
        // disconnects the second decoder. Put back, and said why.
        if (RaceUnderWay)
        {
            _fillingSplitChoices = true;

            try
            {
                cmbSplitLoop.SelectedIndex = _settings.SplitLoopEnabled ? 1 : 0;
            }
            finally
            {
                _fillingSplitChoices = false;
            }

            RefuseDecoderChangeMidRace("split loop");
            return;
        }

        _settings.SplitLoopEnabled = on;
        _settings.Save();

        // Turned off: the other decoder goes (UpdateConnectionModeUi
        // disconnects a decoder with no role). Turned on while the line is
        // connected: the split loop joins it now, rather than at the next
        // Connect.
        UpdateConnectionModeUi();

        if (on && LineDecoderConnected())
        {
            ConnectSplitLoop();
        }
    }

    /// <summary>Connects the split loop's decoder, when there is one and it isn't connected.</summary>
    /// <param name="userChose">
    /// True when somebody pressed Connect or Connect Split, so the I-Lap port
    /// in the picker is one they chose. Otherwise - the line connecting by
    /// itself, start-up connect, the split loop being switched on - only the
    /// remembered port is used, and only when it is present: the picker falls
    /// back to the last port listed when that one is missing, and opening a
    /// stranger's Bluetooth port by ourselves (and saving it over the real
    /// one) is worse than waiting. See TryStartupConnect, which does the same.
    /// </param>
    private void ConnectSplitLoop(bool userChose = false)
    {
        // Whatever this go comes to, it is the one that counts now.
        _splitWaitingForPort = false;

        if (!_settings.SplitLoopEnabled)
        {
            return;
        }

        var split = OtherDecoder(SelectedDecoder);

        if (StateOf(split) is DecoderConnectionState.Connected or DecoderConnectionState.Connecting)
        {
            return;
        }

        _splitLoop?.Clear();
        _splitNote = null;

        if (split == DecoderConnection.ILap)
        {
            if (!userChose)
            {
                var remembered = _settings.ILapPort?.Trim() ?? string.Empty;

                if (remembered.Length == 0)
                {
                    RenderSplitStatus("Pick the I-Lap's COM port above, then click Connect Split.");
                    return;
                }

                if (!ILapClient.AvailablePorts().Contains(remembered, StringComparer.OrdinalIgnoreCase))
                {
                    _splitWaitingForPort = true;
                    RenderSplitStatus($"Waiting for the I-Lap on {remembered} - plug it in, or pick its port above and click Connect Split.");
                    return;
                }

                // Nothing saved: this is the port already remembered.
                _ilapClient.Connect(remembered, _settings.ILapBaud);
                RenderSplitStatus();
                return;
            }

            if (cmbSerialPort.SelectedItem is not string port)
            {
                RenderSplitStatus("Pick the I-Lap's COM port above, then click Connect Split.");
                return;
            }

            _settings.ILapPort = port;

            if (cmbBaud.SelectedItem is int baud)
            {
                _settings.ILapBaud = baud;
            }

            _settings.Save();
            _ilapClient.Connect(port, _settings.ILapBaud);
        }
        else
        {
            _decoderClient.Connect();
        }

        RenderSplitStatus();
    }

    /// <summary>
    /// True while an I-Lap split loop is waiting for the port it was last on
    /// to appear - set by ConnectSplitLoop when the port isn't there, and
    /// cleared by the next go at connecting it and by Disconnect. See
    /// TryConnectWaitingSplitLoop.
    /// </summary>
    private bool _splitWaitingForPort;

    /// <summary>
    /// On the auto-connect timer's tick: connects an I-Lap split loop that
    /// was left waiting for its port, once the port is there. It used to
    /// only say it was waiting: an I-Lap plugged in after the line had
    /// connected by itself was never connected, and heats ran all night
    /// without sector times.
    /// </summary>
    private void TryConnectWaitingSplitLoop()
    {
        if (!_splitWaitingForPort
            || !WaitingSplitLoopCanConnect(_settings.SplitLoopEnabled, OtherDecoder(SelectedDecoder), LineDecoderConnected(),
                _settings.ILapPort, ILapClient.AvailablePorts()))
        {
            return;
        }

        OnClientRawMessage($"{AppTime.LogStampNow()}  [split] {_settings.ILapPort!.Trim()} is there now - connecting the I-Lap split loop.");
        ConnectSplitLoop();
    }

    /// <summary>
    /// Whether an I-Lap split loop left waiting for its port may connect now:
    /// it is still the split loop, the line is connected, and the port it was
    /// last on is listed again. Only that port - see ConnectSplitLoop.
    /// </summary>
    internal static bool WaitingSplitLoopCanConnect(bool splitLoopOn, int splitDecoder, bool lineConnected, string? rememberedPort, IEnumerable<string> portsNow)
    {
        var remembered = rememberedPort?.Trim() ?? string.Empty;

        return splitLoopOn
               && splitDecoder == DecoderConnection.ILap
               && lineConnected
               && remembered.Length > 0
               && portsNow.Contains(remembered, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Disconnects the split loop's decoder, if it is connected.</summary>
    private void DisconnectSplitLoop()
    {
        // Disconnect sticks: no connecting by itself once its port appears.
        _splitWaitingForPort = false;

        var split = OtherDecoder(SelectedDecoder);

        if (split == DecoderConnection.ILap)
        {
            _ilapClient.Disconnect();
        }
        else
        {
            _decoderClient.Disconnect();
        }

        RenderSplitStatus();
    }

    /// <summary>
    /// The split loop's light and line: its decoder's state and, once
    /// connected, how well the two decoders' clocks are tied together. Run
    /// every second, and when either decoder's state changes.
    /// </summary>
    private void RenderSplitStatus(string? message = null)
    {
        var on = _settings.SplitLoopEnabled;

        if (lblSplitStatusCaption is not null && lblSplitStatusCaption.Visible != on)
        {
            lblSplitStatusCaption.Visible = on;
        }

        if (splitStatusLine is not null && splitStatusLine.Visible != on)
        {
            splitStatusLine.Visible = on;
        }

        btnConnectSplit.Visible = on;
        btnLineUp.Visible = on;

        if (!on)
        {
            return;
        }

        var split = OtherDecoder(SelectedDecoder);
        var state = StateOf(split);

        pnlSplitDot.BackColor = state switch
        {
            DecoderConnectionState.Connecting => Color.Goldenrod,
            DecoderConnectionState.Connected => Color.SeaGreen,
            DecoderConnectionState.Error => Color.Firebrick,
            _ => Color.Gray,
        };

        btnConnectSplit.Enabled = state is DecoderConnectionState.Disconnected or DecoderConnectionState.Error;

        // A note (no COM port picked, say) stays until the state changes,
        // rather than being written over by the next tick.
        if (message is not null)
        {
            _splitNote = (message, state);
        }
        else if (_splitNote is { } note && note.State != state)
        {
            _splitNote = null;
        }

        var text = _splitNote?.Text ?? state switch
        {
            DecoderConnectionState.Connected => "Connected. " + ClockLine(),
            DecoderConnectionState.Connecting => "Connecting...",
            DecoderConnectionState.Error => "Not connected - click Connect Split to try again. The console says why.",
            _ => "Not connected. It connects with the line when you click Connect, or click Connect Split.",
        };

        if (lblSplitStatus.Text != text)
        {
            lblSplitStatus.Text = text;
        }
    }

    /// <summary>How the EasyLap's clock is tied to the app's - the one clock that has to be learned - and the Line Up offset.</summary>
    private string ClockLine()
    {
        var offset = _settings.SplitLoopOffsetMs == 0
            ? "Not lined up yet."
            : $"Lined up: the split reads {_settings.SplitLoopOffsetMs} ms late.";

        if (_decoderClient.State != DecoderConnectionState.Connected)
        {
            return "Waiting for the EasyLap - split times need both decoders. " + offset;
        }

        return _decoderClient.Clock.Estimate(TimingClock.NowMs) is { } estimate
            ? $"Clocks tied together to within about {Math.Max(1, estimate.Jitter)} ms. {offset}"
            : "Learning the EasyLap's clock (a few seconds)... " + offset;
    }

    /// <summary>
    /// The race row a split pass belongs to: the driver's row, or - with two
    /// of their cars out - the row of the car the split transponder is
    /// fitted to. Null when it can't be told.
    /// </summary>
    private string? RowForSplit(SplitPass pass)
    {
        if (pass.Driver is null)
        {
            return null;
        }

        var rows = _race.GetOrderedStandings()
            .Where(s => !GhostService.IsGhost(s.TransponderId)
                        && string.Equals(_profiles.Get(s.TransponderId)?.Transponder, pass.Driver, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (rows.Count == 1)
        {
            return rows[0].TransponderId;
        }

        if (rows.Count > 1 && pass.CarId.Length > 0 && _profiles.Get(pass.Driver) is { } driver)
        {
            var inThatCar = rows
                .Where(r => string.Equals(driver.TransponderEntry(r.TransponderId)?.CarId, pass.CarId, StringComparison.Ordinal))
                .ToList();

            if (inThatCar.Count == 1)
            {
                return inThatCar[0].TransponderId;
            }
        }

        return null;
    }

    /// <summary>
    /// True while Line Up's window is open. A car driven across the loops for
    /// it is not a car out to practice: with practice set to start at the
    /// gate, the first crossing used to start practice, and Save was then
    /// refused as a change mid-practice - every try, until auto-start was
    /// unticked. See MaybeAutoStartPracticeOnGatePass.
    /// </summary>
    private bool _lineUpOpen;

    /// <summary>Line Up: measures how late the split loop reads a car, with both loops at the same spot.</summary>
    private void BtnLineUp_Click(object? sender, EventArgs e)
    {
        if (_splitLoop is null)
        {
            return;
        }

        // Not while a race or practice is on. The offset is applied as each
        // split is recorded, so changing it mid-race split one session's
        // sectors between two calibrations - sectors that still added up to
        // the lap, so nothing caught them, and a "fastest sector" that was
        // only the recalibration was called and kept as a record. Every
        // other change to timing refuses mid-race; this one did not.
        if (RaceUnderWay)
        {
            RefuseDecoderChangeMidRace("split loop's Line Up");
            return;
        }

        using var form = new SplitLineUpForm(_splitLoop, _profiles, _settings.SplitLoopOffsetMs, handler => LinePassSeen += handler, handler => LinePassSeen -= handler);

        DialogResult answer;
        _lineUpOpen = true;

        try
        {
            answer = form.ShowDialog(this);
        }
        finally
        {
            _lineUpOpen = false;
        }

        if (answer != DialogResult.OK || form.OffsetMs is not { } measured)
        {
            return;
        }

        // A race started while the window was open: kept as it was.
        if (RaceUnderWay)
        {
            RefuseDecoderChangeMidRace("split loop's Line Up");
            return;
        }

        _settings.SplitLoopOffsetMs = measured;
        _settings.Save();
        OnClientRawMessage($"{AppTime.LogStampNow()}  [split] Lined up: the split loop reads {measured} ms late; that is taken off every split time.");
        RenderSplitStatus();
    }

    /// <summary>
    /// The split loop's own TIMING warnings, after the line's (TimingWatch):
    /// its decoder gone, or nothing read at the split for three typical
    /// laps (half a minute at least) while cars cross the line. Laps are
    /// still timed either way, and the warning says so.
    /// </summary>
    private TimingWatch.Problem? SplitLoopProblem(TimingWatch.Situation situation)
    {
        var now = TimingClock.NowMs;

        // The split loop down or quiet first - it goes on until fixed;
        // a possible missed lap after it, for a while after it was seen.
        var loop = SplitLoopDown(situation, now);

        if (loop is not null)
        {
            return loop;
        }

        return situation.Racing && _missedLapNote is { } note && now - note.At < MissedLapShownMs
            ? new TimingWatch.Problem(note.Key, note.Text)
            : null;
    }

    /// <summary>How long "split loop not connected" stays on the Next Up bar each time it is noticed in a race.</summary>
    private const long SplitDownShownMs = 15_000;

    /// <summary>When, in this run of racing, the split loop was first seen not connected - or null.</summary>
    private long? _splitDownSince;

    /// <summary>Whether the console has been told the split loop is still down, since it was last seen up.</summary>
    private bool _splitDownLogged;

    /// <summary>The split loop's decoder gone, or nothing read at the split for three typical laps while cars cross the line.</summary>
    private TimingWatch.Problem? SplitLoopDown(TimingWatch.Situation situation, long now)
    {
        if (!situation.Racing || !_settings.SplitLoopEnabled || _splitLoop is null)
        {
            _splitWatchSince = null;
            _splitDownSince = null;
            return null;
        }

        _splitWatchSince ??= now;

        if (StateOf(OtherDecoder(SelectedDecoder)) != DecoderConnectionState.Connected)
        {
            // Said for a while, then left to the console and Mission Control.
            // It is shown on the Next Up bar's alarm, which means "laps are
            // not being counted" - and a split loop not plugged in costs only
            // sector times. Shown all race it turned every heat's one status
            // strip into a red alarm nobody could clear, and taught the
            // director to stop reading it.
            _splitDownSince ??= now;

            if (now - _splitDownSince.Value < SplitDownShownMs)
            {
                return new TimingWatch.Problem("split-down", "Split loop not connected - laps are still timed, splits are not");
            }

            if (!_splitDownLogged)
            {
                _splitDownLogged = true;
                OnClientRawMessage($"{AppTime.LogStampNow()}  [split] The split loop is still not connected - laps are timed, sectors are not. Set Split loop to None on the Connection page if it isn't in use tonight.");
            }

            return null;
        }

        _splitDownSince = null;
        _splitDownLogged = false;

        var field = situation.Cars.Select(c => c.TypicalLapMs).Where(ms => ms > 0).DefaultIfEmpty(0).Max();
        var quietFor = Math.Max(30_000L, field * 3);
        var since = Math.Max(_splitLoop.LastPassAt ?? 0, _splitWatchSince.Value);

        if (now - since >= quietFor && _lastLinePassAt is { } line && now - line < quietFor)
        {
            return new TimingWatch.Problem("split-quiet", "Split loop quiet - cars are crossing the line but nothing is read at the split");
        }

        return null;
    }
}
