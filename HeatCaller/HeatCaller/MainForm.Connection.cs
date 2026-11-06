using System.Text.Json;

namespace HeatCaller;

/// <summary>
/// MainForm's decoder connection: EasyLap and I-Lap, the Connection page's
/// controls, auto-connect, and lap events arriving from the decoder.
///
/// Moved here unchanged from MainForm.cs on 26 Sep 2026, when the file was
/// split into parts. Fields with initializers stayed in MainForm.cs.
/// </summary>
public partial class MainForm
{
    private void ChkAutoConnect_CheckedChanged(object? sender, EventArgs e)
    {
        _settings.DirectDecoderAutoConnect = chkAutoConnect.Checked;
        _settings.Save();

        if (chkAutoConnect.Checked && RoleOf(DecoderConnection.EasyLap) != DecoderRole.None
            && _decoderClient.State is DecoderConnectionState.Disconnected or DecoderConnectionState.Error)
        {
            // The line's buttons only when the EasyLap is the line.
            if (RoleOf(DecoderConnection.EasyLap) == DecoderRole.Line)
            {
                btnConnect.Enabled = false;
                btnDisconnect.Enabled = true;
            }

            _decoderClient.Connect();
        }
    }

    private void OnDecoderStatusChanged(DecoderConnectionState state, string message)
    {
        // Not a bare InvokeRequired: it answers false before this
        // window has a handle, and this is called from the decoder's
        // own thread. See OnUiThread.
        if (!IsHandleCreated || InvokeRequired) { OnUiThread(() => OnDecoderStatusChanged(state, message)); return; }

        // ConnectLine's own go at the EasyLap has finished, one way or the
        // other. Read before it is cleared: this status IS the end of that
        // connect, and it is the one status that must not connect the split
        // loop a second time. Cleared whatever role the EasyLap now has, so
        // a decoder swap partway through a connect cannot leave it set.
        var connectLinesOwn = _connectingLine;

        if (state is not DecoderConnectionState.Connecting)
        {
            _connectingLine = false;
        }

        // Overtaken on its way here: statuses from two threads can arrive
        // out of order - a Connected posted just before a Disconnect landed
        // after it and painted the light green again. Whatever the client
        // is now has a status of its own still to come, so this one is
        // dropped rather than drawn over it.
        if (state != _decoderClient.State)
        {
            return;
        }

        // The line's light for the line; the split loop's own for the split.
        switch (RoleOf(DecoderConnection.EasyLap))
        {
            case DecoderRole.Line:
                RenderDecoderStatus(state, message);

                // However the line came to connect - Connect, or
                // auto-connect - the split loop joins it. ConnectLine
                // connects it itself, with the port picked - see
                // _connectingLine.
                if (state == DecoderConnectionState.Connected && !connectLinesOwn)
                {
                    ConnectSplitLoop();
                }

                break;
            case DecoderRole.Split:
                RenderSplitStatus();
                break;
        }
    }

    private void OnILapStatusChanged(DecoderConnectionState state, string message)
    {
        // Not a bare InvokeRequired: it answers false before this
        // window has a handle, and this is called from the decoder's
        // own thread. See OnUiThread.
        if (!IsHandleCreated || InvokeRequired) { OnUiThread(() => OnILapStatusChanged(state, message)); return; }

        // Overtaken on its way here - see OnDecoderStatusChanged.
        if (state != _ilapClient.State)
        {
            return;
        }

        switch (RoleOf(DecoderConnection.ILap))
        {
            case DecoderRole.Line:
                RenderDecoderStatus(state, message);

                if (state == DecoderConnectionState.Connected)
                {
                    ConnectSplitLoop();
                }

                break;
            case DecoderRole.Split:
                RenderSplitStatus();
                break;
        }
    }

    /// <summary>Repopulates the COM port picker, keeping the remembered port selected when it is still present.</summary>
    private void RefreshSerialPorts()
    {
        var ports = ILapClient.AvailablePorts();

        cmbSerialPort.Items.Clear();
        cmbSerialPort.Items.AddRange(ports);

        // The longest port name may have come or gone.
        _serialPickerWidth = null;
        SizeConnectionFields();

        if (ports.Length == 0)
        {
            return;
        }

        var remembered = Array.FindIndex(ports, p => string.Equals(p, RememberedComPort(), StringComparison.OrdinalIgnoreCase));
        cmbSerialPort.SelectedIndex = remembered >= 0 ? remembered : ports.Length - 1;
    }

    private void BtnRefreshPorts_Click(object? sender, EventArgs e) => RefreshSerialPorts();

    /// <summary>
    /// Fills the I-Lap baud picker and keeps the setting in step with it.
    ///
    /// The setting always existed and the client always honoured it, but
    /// nothing on screen could change it - so a decoder set to anything but
    /// 9600 meant editing the settings file by hand. A rate already saved
    /// that is not one of the usual ones is kept in the list rather than
    /// quietly replaced, since somebody chose it on purpose.
    /// </summary>
    private void FillBaudRates()
    {
        var rates = ILapClient.CommonBaudRates.ToList();

        if (_settings.ILapBaud > 0 && !rates.Contains(_settings.ILapBaud))
        {
            rates.Add(_settings.ILapBaud);
            rates.Sort();
        }

        cmbBaud.Items.Clear();

        foreach (var rate in rates)
        {
            cmbBaud.Items.Add(rate);
        }

        cmbBaud.SelectedItem = rates.Contains(_settings.ILapBaud) ? _settings.ILapBaud : ILapClient.CommonBaudRates[0];

        _baudPickerWidth = null;
        SizeConnectionFields();

        // Wired after the first selection, so filling the list is not
        // mistaken for the user choosing a rate.
        cmbBaud.SelectedIndexChanged += (_, _) =>
        {
            if (cmbBaud.SelectedItem is not int chosen)
            {
                return;
            }

            // A decoder from 3 up keeps its own rate.
            if (LineIsStream)
            {
                var saved = _settings.StreamDecoder(SelectedDecoder);

                if (saved.Baud != chosen)
                {
                    saved.Baud = chosen;
                    _settings.Save();
                }

                return;
            }

            if (chosen is var rate && rate == _settings.ILapBaud)
            {
                return;
            }

            _settings.ILapBaud = rate;
            _settings.Save();

            if (_ilapClient.State is not DecoderConnectionState.Disconnected)
            {
                OnClientRawMessage($"{AppTime.LogStampNow()}  [ilap] Baud rate set to {rate} - it takes effect the next time you connect.");
            }
        };
    }

    private void RenderDecoderStatus(DecoderConnectionState state, string message)
    {
        lblStatus.Text = message;
        pnlStatusDot.BackColor = state switch
        {
            DecoderConnectionState.Disconnected => Color.Gray,
            DecoderConnectionState.Connecting => Color.Goldenrod,
            DecoderConnectionState.Connected => Color.SeaGreen,
            DecoderConnectionState.Error => Color.Firebrick,
            _ => Color.Gray,
        };

        btnConnect.Enabled = state is DecoderConnectionState.Disconnected or DecoderConnectionState.Error;
        btnDisconnect.Enabled = !btnConnect.Enabled;

        // WHY (2026-11-05, Bug hunt 11: Disconnect greyed out while a decoder
        // from 3 up keeps retrying): their client goes on trying every few
        // seconds after an Error ("press Disconnect to stop"), so Disconnect
        // stays usable then - it greyed out, and the only way to stop the
        // retries was to switch decoder or close the app. Connect stays too,
        // to try again at once with a corrected address.
        if (state == DecoderConnectionState.Error
            && DecoderConnection.IsStream(SelectedDecoder)
            && _streamClient.Decoder == SelectedDecoder
            && _streamClient.State == DecoderConnectionState.Error)
        {
            btnDisconnect.Enabled = true;
        }

        if (state == DecoderConnectionState.Connected)
        {
            var known = _profiles.All;
            OnClientRawMessage(known.Count == 0
                ? $"{AppTime.LogStampNow()}  [profiles] No driver profiles configured at all."
                : $"{AppTime.LogStampNow()}  [profiles] Known transponder keys: {string.Join(", ", known.Select(p => $"\"{p.Transponder}\"={p.DriverName}"))}");
        }
    }

    private void OnDecoderLapEvent(DirectLapEvent lap)
    {
        // Not a bare InvokeRequired: it answers false before this
        // window has a handle, and this is called from the decoder's
        // own thread. See OnUiThread.
        if (!IsHandleCreated || InvokeRequired) { OnUiThread(() => OnDecoderLapEvent(lap)); return; }

        LinePassSeenOnUi(lap);

        // Checked BEFORE the lap is processed. Starting practice afterwards
        // would mean the crossing that triggered it lands in a session that
        // didn't exist yet, and the driver's first lap silently vanishes.
        // Practice goes green here, and this same crossing is then counted
        // by the line below as the driver's start lap.
        MaybeAutoStartPracticeOnGatePass(lap);

        _race.ProcessLapEvent(lap);
    }

    /// <summary>
    /// The screens' share of a line pass - Line Up, the transponder watch and
    /// its health across nights - before the race counts it. UI thread.
    /// </summary>
    /// <remarks>
    /// WHY (23 Oct 2026, timing layer, P4): its own method so a threaded pass,
    /// which the race counts on the timing thread, still does exactly this on
    /// the UI thread (see SendLinePassToTiming). Inline it runs where it
    /// always did, at the top of OnDecoderLapEvent.
    /// </remarks>
    private void LinePassSeenOnUi(DirectLapEvent lap)
    {
        // For the split loop's "quiet" warning and for Line Up, which pairs
        // the line's passes with the split loop's. Line Up can never cost
        // the race a lap: whatever it does, the pass goes on below.
        _lastLinePassAt = TimingClock.NowMs;

        try
        {
            LinePassSeen?.Invoke(lap);
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"{AppTime.LogStampNow()}  [split] Line Up couldn't use a pass: {ex.Message}");
        }

        // Recorded first, and whatever happens to the pass afterwards. A
        // pass that no race counts still proves the transponder is alive,
        // and that is the whole point of keeping them.
        _transponderWatch.Record(lap);

        // And across nights, so a transponder that is fading shows up before
        // it fails - see TransponderHealthStore. Never throws.
        RecordTransponderHealth(lap);
    }

    /// <summary>
    /// Starts a practice session when a car crosses the gate and nothing
    /// else is running.
    ///
    /// Deliberately narrow: only from Idle, never during a race, a
    /// countdown or an existing practice. At an open practice night people
    /// drift on and off track constantly, and nobody wants to walk back to
    /// the laptop to press Start - but nothing here should ever interrupt
    /// a real session.
    /// </summary>
    /// <param name="lap">
    /// The crossing that triggered it. Its decoder time stamps the green
    /// just before it, so the crossing counts - a green stamped from the
    /// estimated clock could land a few milliseconds after it, and the
    /// crossing was then thrown away as coming before the start.
    /// </param>
    /// <remarks>
    /// Threaded (24 Oct 2026, timing layer, P7), the line's passes do not come
    /// this way: the timing thread starts practice on them itself, from the
    /// checks below handed over beforehand - see RefreshPracticeAutoStartPlan.
    /// This still runs for a line pass that takes the UI path (the moment
    /// after the line is switched, see SendLinePassToTiming), as it always did.
    /// </remarks>
    private void MaybeAutoStartPracticeOnGatePass(DirectLapEvent lap)
    {
        // Not until the unfinished-race offer has been answered: a
        // practice started first took the session, and the race waiting to
        // be recovered was then never offered. See _recoveryDecided. Nor
        // while Line Up is open - see _lineUpOpen.
        //
        // WHY (24 Oct 2026, timing layer, P8): nor while On Air is opening a
        // race. It runs with the session Idle, sometimes for minutes, and a
        // car warming up during the opening started practice - and On Air was
        // then called off ("the race was moved on"). Threaded, On Air holds
        // the timing side's start the same way (RaceTiming.HoldPracticeAutoStart).
        if (!_settings.PracticeAutoStartOnGatePass
            || !_recoveryDecided
            || _lineUpOpen
            || _practiceControl is null
            || _raceControlControl?.OnAirRunning == true
            || _race.State != RaceState.Idle
            || _race.IsWarmingUp)
        {
            return;
        }

        // A pass the app made up has no decoder time to stamp the green
        // with - see DirectLapEvent.IsSynthetic. Ghosts do not come this
        // way today, but the estimated clock is the right fallback if one did.
        if (_practiceControl.TryAutoStartPractice(lap.IsSynthetic ? (long?)null : lap.DeviceTimeMs))
        {
            OnClientRawMessage($"{AppTime.LogStampNow()}  [practice] Auto-started - a car crossed the gate.");
        }
    }

    /// <summary>
    /// Shows the chosen decoder's own fields - the COM port and baud rate
    /// for I-Lap, the auto-connect tick for EasyLap - and puts that
    /// decoder's state on the shared status light.
    /// </summary>
    private void UpdateConnectionModeUi()
    {
        var decoder = SelectedDecoder;

        // The line moved to the other decoder while the split loop is on:
        // the two have swapped roles. Neither may carry on as it was - the
        // decoder partway round the track would otherwise start counting
        // laps on the spot, reading split transponders as line ones. Both
        // are disconnected (Connect brings them back in their new roles),
        // the split loop forgets what it read, and the Line Up offset, which
        // measured one decoder against the other, now runs the other way.
        if (_lineDecoderKnown && decoder != _lineDecoder && _settings.SplitLoopEnabled)
        {
            // Auto-connect, when ticked, brings the EasyLap back in its new
            // role - which is what passes are then routed by.
            if (_decoderClient.State is not DecoderConnectionState.Disconnected)
            {
                _decoderClient.Disconnect();
            }

            if (_ilapClient.State is not DecoderConnectionState.Disconnected)
            {
                _ilapClient.Disconnect();
            }

            _splitLoop?.Clear();

            OnClientRawMessage($"{AppTime.LogStampNow()}  [split] The line and the split loop swapped decoders - both disconnected. Click Connect to connect them in their new places, and run Line Up again to be sure.");
        }

        // The Line Up offset measured one decoder against the other, so it
        // runs the other way whenever the line moves to the other decoder -
        // whether or not the split loop is on at that moment. It used to be
        // turned round only while the split loop was on: take the loop off,
        // swap the line, put the loop back, and the roles had swapped with
        // the offset still the old way round - every split out by twice the
        // offset, in sectors that still added up to the lap and so were
        // believed, and saved as records.
        //
        // Only between the EasyLap and the I-Lap, the two the offset can be
        // measured between (1 Nov 2026): a decoder from 3 up as the line in
        // the meantime leaves it as it was, rather than turning it round on
        // the way there and again on the way back to the same one.
        //
        // WHY (2026-11-05, Bug hunt 11: the offset was not turned round after
        // a restart): _classicLine started each run empty, so a run that
        // began on a decoder from 3 up never turned it round on the way to
        // the other of the two. It starts from the one saved instead.
        if (_classicLine is null && _settings.SplitLoopOffsetLine is DecoderConnection.EasyLap or DecoderConnection.ILap)
        {
            _classicLine = _settings.SplitLoopOffsetLine;
        }

        if (!DecoderConnection.IsStream(decoder) && _classicLine is { } classic && classic != decoder && _settings.SplitLoopOffsetMs != 0)
        {
            _settings.SplitLoopOffsetMs = -_settings.SplitLoopOffsetMs;

            if (!_settings.SplitLoopEnabled)
            {
                OnClientRawMessage($"{AppTime.LogStampNow()}  [split] The line moved to the other decoder, so the split loop's Line Up now runs the other way ({_settings.SplitLoopOffsetMs} ms). Run Line Up again when the split loop is next on, to be sure.");
            }
        }

        _lineDecoder = decoder;
        _lineDecoderKnown = true;

        var lineIsStream = DecoderConnection.IsStream(decoder);

        if (!lineIsStream)
        {
            _classicLine = decoder;

            // Saved with LastConnectionType below - Bug hunt 11 (2026-11-05), see above.
            _settings.SplitLoopOffsetLine = decoder;
        }

        // A decoder from 3 up can't have a split loop beside it yet (see
        // MainForm.StreamDecoders): the split loop is turned off, and said so.
        if (lineIsStream && _settings.SplitLoopEnabled)
        {
            _settings.SplitLoopEnabled = false;
            OnClientRawMessage($"{AppTime.LogStampNow()}  [split] Split loop turned off - it works with the EasyLap and I-Lap only. Turn it on again after switching back to one of them.");
        }

        // The decoders' threads and the timing thread read these copies,
        // never the settings (24 Oct 2026, timing layer, P8) - see
        // SendSplitPassToTiming. After the offset is turned round above.
        _splitLoopOn = _settings.SplitLoopEnabled;
        _frozenSplitOffsetMs = _settings.SplitLoopOffsetMs;

        // How many sectors a lap is cut into: one split loop, two sectors.
        _race.SplitCount = _settings.SplitLoopEnabled ? 1 : 0;

        // The line's own, or the split loop's: each decoder's settings show
        // while it has either role.
        var isEasyLap = RoleOf(DecoderConnection.EasyLap) != DecoderRole.None;
        var isILap = RoleOf(DecoderConnection.ILap) != DecoderRole.None;
        var lineIsILap = decoder == DecoderConnection.ILap;

        // Auto-connect retry belongs to the EasyLap HID search alone; a
        // serial port that fails to open should say so once, not every
        // few seconds.
        chkAutoConnect.Visible = isEasyLap;

        // A decoder from 3 up: its source, and its address when on the
        // network - filled from what it last used.
        if (lineIsStream)
        {
            FillStreamDecoderFields(decoder);
        }
        else if (cmbBaud.Items.Contains(_settings.ILapBaud))
        {
            cmbBaud.SelectedItem = _settings.ILapBaud;
        }

        ShowStreamSourceRows(lineIsStream);

        // The COM port rows for the I-Lap, and for a decoder from 3 up on a serial port.
        var serialRows = isILap || (lineIsStream && SelectedStreamTransport == DecoderTransport.Serial);

        // The port picker's whole row, with its Rescan button, rather than
        // the two in it: an emptied row would keep its place in the card.
        lblSerialPort.Visible = serialRows;
        serialPortRow.Visible = serialRows;
        lblBaud.Visible = serialRows;
        cmbBaud.Visible = serialRows;

        if (serialRows)
        {
            RefreshSerialPorts();
        }

        btnConnect.Text = "Connect";

        // The saved number, not the picker position - see DecoderConnection.
        // This is also what moves a retired Next Level Timing choice (0) on
        // to EasyLap in settings.json, the first time this runs after it.
        _settings.LastConnectionType = decoder;
        _settings.Save();

        // Only the decoders in use stay connected - the line, and the split
        // loop when it is on. Switching used to leave the old one running
        // underneath - its laps still arriving, into the same race, from
        // hardware the screen said was not in use. (A pass from a decoder
        // with no role is not used either way - see RoutePass.)
        if (!isEasyLap && _decoderClient.State is not DecoderConnectionState.Disconnected)
        {
            _decoderClient.Disconnect();
        }

        if (!isILap && _ilapClient.State is not DecoderConnectionState.Disconnected)
        {
            _ilapClient.Disconnect();
        }

        // One decoder from 3 up at a time, and only while it is the line.
        if (_streamClient.Decoder != decoder && _streamClient.State is not DecoderConnectionState.Disconnected)
        {
            _streamClient.Disconnect();
        }

        // The chosen decoder's ACTUAL state, rather than leaving the other
        // one's showing - switching from a connected EasyLap to I-Lap
        // should show I-Lap's own (probably disconnected) state, not a
        // stale green light.
        if (lineIsStream)
        {
            RenderDecoderStatus(StreamStateOf(decoder), StreamStateOf(decoder).ToString());
        }
        else if (lineIsILap)
        {
            RenderDecoderStatus(_ilapClient.State, _ilapClient.State.ToString());
        }
        else
        {
            RenderDecoderStatus(_decoderClient.State, _decoderClient.State.ToString());
        }

        // The split loop is whichever decoder the line isn't.
        FillSplitLoopChoices();
        RenderSplitStatus();
    }

    /// <summary>
    /// A race or practice is on - the states the Venues page and Backup
    /// already refuse in (venues.IsRaceUnderWay). Changing which decoder is
    /// the line or the split loop then is refused too.
    /// </summary>
    private bool RaceUnderWay =>
        _race.IsPracticeMode || _race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing;

    /// <summary>True while a picker is being put back after a refusal, so the put-back is not taken for a change.</summary>
    private bool _revertingDecoderPicker;

    /// <summary>
    /// Says why a decoder picker change was refused - in the console and in
    /// a message, since the picker has just jumped back under the mouse.
    /// </summary>
    private void RefuseDecoderChangeMidRace(string what)
    {
        OnClientRawMessage($"{AppTime.LogStampNow()}  [timing] Refused: {what} while a race or practice is running - it would stop laps being counted.");
        AppDialog.Show(this,
            $"The {what} can't be changed while a race or practice is running - it would disconnect the decoder timing it, and laps would stop being counted.\r\n\r\nFinish or reset the session first.",
            "Race under way", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void CmbConnectionType_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_revertingDecoderPicker)
        {
            return;
        }

        // One change of "Connect via:" mid-race - a mis-click, or the mouse
        // wheel over the focused box - disconnected the decoder timing the
        // heat, with nothing said, and auto-connect did not bring it back.
        // The Venues page already refused the same switch; this is the same
        // rule at the other door.
        if (_lineDecoderKnown && SelectedDecoder != _lineDecoder && RaceUnderWay)
        {
            _revertingDecoderPicker = true;

            try
            {
                cmbConnectionType.SelectedIndex = DecoderConnection.IndexOf(_lineDecoder);
            }
            finally
            {
                _revertingDecoderPicker = false;
            }

            RefuseDecoderChangeMidRace("decoder");
            return;
        }

        UpdateConnectionModeUi();
    }

    /// <summary>
    /// True from start-up while an I-Lap is still to be connected by itself
    /// - waiting for the port it was last on to appear. Cleared once it is
    /// connected, and by Connect, Disconnect or unticking "Connect when
    /// HeatCaller starts", any of which means somebody has taken over.
    /// </summary>
    private bool _startupConnectPending;

    /// <summary>
    /// "Connect when HeatCaller starts" (AppSettings.ConnectDecodersAtStartup):
    /// connects the line decoder, and the split loop with it, as Connect
    /// would. Called once, from RaiseReady, so the window is up and the
    /// console shows what happened - never in the layout test, which never
    /// raises Ready.
    ///
    /// Before this only an EasyLap could connect by itself (its auto-connect
    /// tick); the I-Lap, the line decoder now, waited for somebody to press
    /// Connect every race night.
    /// </summary>
    private void ConnectAtStartup()
    {
        if (!_settings.ConnectDecodersAtStartup)
        {
            return;
        }

        _startupConnectPending = true;
        TryStartupConnect(firstTry: true);
    }

    /// <summary>
    /// One go at the start-up connect: at start-up, and then on the
    /// auto-connect timer's tick while an I-Lap's port has yet to appear.
    /// </summary>
    private void TryStartupConnect(bool firstTry)
    {
        if (!_startupConnectPending || IsDisposed)
        {
            return;
        }

        if (!_settings.ConnectDecodersAtStartup)
        {
            _startupConnectPending = false;
            return;
        }

        // A decoder from 3 up, as it was last reached. Its own client keeps
        // trying when the link drops, so one go is all this needs - but a
        // serial one waits for its port, as the I-Lap does.
        if (LineIsStream)
        {
            if (_streamClient.State is not (DecoderConnectionState.Disconnected or DecoderConnectionState.Error))
            {
                _startupConnectPending = false;
                return;
            }

            var saved = _settings.StreamDecoder(SelectedDecoder);

            if (saved.Transport == DecoderTransport.Serial
                && !ILapClient.AvailablePorts().Contains(saved.ComPort.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                if (firstTry)
                {
                    OnClientRawMessage(saved.ComPort.Trim().Length == 0
                        ? $"{AppTime.LogStampNow()}  [auto-connect] No port remembered for the {DecoderConnection.ShortName(SelectedDecoder)} decoder yet - pick it and press Connect once."
                        : $"{AppTime.LogStampNow()}  [auto-connect] Waiting for the {DecoderConnection.ShortName(SelectedDecoder)} decoder on {saved.ComPort} - it connects as soon as the port appears.");
                }

                if (saved.ComPort.Trim().Length == 0)
                {
                    _startupConnectPending = false;
                }

                return;
            }

            _startupConnectPending = false;

            if (saved.Transport == DecoderTransport.Serial)
            {
                RefreshSerialPorts();
            }

            OnClientRawMessage($"{AppTime.LogStampNow()}  [auto-connect] Connecting the {DecoderConnection.ShortName(SelectedDecoder)} decoder.");
            ConnectLine();
            return;
        }

        if (SelectedDecoder == DecoderConnection.ILap)
        {
            if (_ilapClient.State is not (DecoderConnectionState.Disconnected or DecoderConnectionState.Error))
            {
                _startupConnectPending = false;
                return;
            }

            // The port it was last connected on, and no other: the picker
            // falls back to the last port listed when that one is missing,
            // and connecting to a stranger's Bluetooth port by ourselves
            // would be worse than not connecting.
            var port = _settings.ILapPort?.Trim() ?? string.Empty;

            if (port.Length == 0)
            {
                _startupConnectPending = false;
                OnClientRawMessage($"{AppTime.LogStampNow()}  [auto-connect] No I-Lap port remembered yet - pick it and press Connect once; from then on it connects by itself.");
                return;
            }

            if (!ILapClient.AvailablePorts().Contains(port, StringComparer.OrdinalIgnoreCase))
            {
                if (firstTry)
                {
                    OnClientRawMessage($"{AppTime.LogStampNow()}  [auto-connect] Waiting for the I-Lap on {port} - it connects as soon as the port appears. Plug it in, or press Connect to choose another port.");
                }

                return;
            }

            _startupConnectPending = false;
            RefreshSerialPorts();

            if (!string.Equals(cmbSerialPort.SelectedItem as string, port, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            OnClientRawMessage($"{AppTime.LogStampNow()}  [auto-connect] Connecting the I-Lap on {port}.");
            ConnectLine();
            return;
        }

        _startupConnectPending = false;

        // An EasyLap that auto-connect has already started on is left to it.
        if (_decoderClient.State is DecoderConnectionState.Disconnected or DecoderConnectionState.Error)
        {
            OnClientRawMessage($"{AppTime.LogStampNow()}  [auto-connect] Connecting the EasyLap.");
            ConnectLine();
        }
    }

    private void BtnConnect_Click(object? sender, EventArgs e)
    {
        // Somebody pressed Connect: the start-up connect stands down.
        _startupConnectPending = false;
        ConnectLine(userChose: true);
    }

    /// <summary>Connects the line decoder, and the split loop with it - Connect's work, for the button and for the start-up connect.</summary>
    /// <param name="userChose">True from the Connect button - see ConnectSplitLoop.</param>
    private void ConnectLine(bool userChose = false)
    {
        // A decoder from 3 up: no split loop beside it.
        if (LineIsStream)
        {
            ConnectStreamDecoder(quiet: !userChose);
            return;
        }

        if (SelectedDecoder == DecoderConnection.ILap)
        {
            if (cmbSerialPort.SelectedItem is not string ilapPort)
            {
                AppDialog.Show(this, "No COM port selected. Plug the I-Lap decoder in, press Rescan and pick its port.",
                    "No port", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _settings.ILapPort = ilapPort;

            // The picker's rate, which is also what is saved - read from
            // the picker so the two can never disagree at the moment of
            // connecting.
            if (cmbBaud.SelectedItem is int baud)
            {
                _settings.ILapBaud = baud;
            }

            _settings.Save();

            btnConnect.Enabled = false;
            btnDisconnect.Enabled = true;
            _ilapClient.Connect(ilapPort, _settings.ILapBaud);
            ConnectSplitLoop(userChose);
            return;
        }

        btnConnect.Enabled = false;
        btnDisconnect.Enabled = true;
        _connectingLine = true;
        _decoderClient.Connect();
        ConnectSplitLoop(userChose);
    }

    /// <summary>
    /// True from ConnectLine starting the EasyLap until that connect has
    /// finished, because ConnectLine connects the split loop itself, with
    /// the port picked. OnDecoderStatusChanged used to connect an I-Lap
    /// split loop on the REMEMBERED port the moment the line went green -
    /// so the port picked for it was never read, and a wrong remembered port
    /// could not be put right with Connect.
    ///
    /// It used to be cleared in a finally around Connect, which was enough
    /// while the EasyLap said it was connected before Connect returned.
    /// Connect is asynchronous now (see DirectDecoderClient.Connect) and
    /// hands back at Connecting, with Connected published from the decoder's
    /// own thread afterwards - so it is cleared when that status arrives
    /// instead, which is the moment it was always really about.
    /// </summary>
    private bool _connectingLine;

    private void BtnDisconnect_Click(object? sender, EventArgs e)
    {
        // Disconnect sticks: no start-up connect after it.
        _startupConnectPending = false;

        // The split loop goes with the line: it has nothing to time against.
        // An EasyLap split loop would otherwise be opened again by
        // auto-connect a few seconds later.
        if (_settings.SplitLoopEnabled)
        {
            if (RoleOf(DecoderConnection.EasyLap) == DecoderRole.Split)
            {
                chkAutoConnect.Checked = false;
            }

            DisconnectSplitLoop();
        }

        if (SelectedDecoder == DecoderConnection.ILap)
        {
            _ilapClient.Disconnect();
            btnConnect.Enabled = true;
            btnDisconnect.Enabled = false;
            return;
        }

        if (LineIsStream)
        {
            _streamClient.Disconnect();
            btnConnect.Enabled = true;
            btnDisconnect.Enabled = false;
            return;
        }

        chkAutoConnect.Checked = false; // manual disconnect should stick, not immediately auto-retry
        _decoderClient.Disconnect();
        btnConnect.Enabled = true;
        btnDisconnect.Enabled = false;
    }
}
