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

        if (chkAutoConnect.Checked && SelectedDecoder == DecoderConnection.EasyLap
            && _decoderClient.State is DecoderConnectionState.Disconnected or DecoderConnectionState.Error)
        {
            btnConnect.Enabled = false;
            btnDisconnect.Enabled = true;
            _decoderClient.Connect();
        }
    }

    private void OnDecoderStatusChanged(DecoderConnectionState state, string message)
    {
        // Not a bare InvokeRequired: it answers false before this
        // window has a handle, and this is called from the decoder's
        // own thread. See OnUiThread.
        if (!IsHandleCreated || InvokeRequired) { OnUiThread(() => OnDecoderStatusChanged(state, message)); return; }

        if (SelectedDecoder != DecoderConnection.EasyLap)
        {
            return; // not the active connection method right now - don't steal the shared status light
        }

        RenderDecoderStatus(state, message);
    }

    private void OnILapStatusChanged(DecoderConnectionState state, string message)
    {
        // Not a bare InvokeRequired: it answers false before this
        // window has a handle, and this is called from the decoder's
        // own thread. See OnUiThread.
        if (!IsHandleCreated || InvokeRequired) { OnUiThread(() => OnILapStatusChanged(state, message)); return; }

        if (SelectedDecoder != DecoderConnection.ILap)
        {
            return; // not the active connection method right now - don't steal the shared status light
        }

        RenderDecoderStatus(state, message);
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

        var remembered = Array.FindIndex(ports, p => string.Equals(p, _settings.ILapPort, StringComparison.OrdinalIgnoreCase));
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
            if (cmbBaud.SelectedItem is not int rate || rate == _settings.ILapBaud)
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

        // Recorded first, and whatever happens to the pass afterwards. A
        // pass that no race counts still proves the transponder is alive,
        // and that is the whole point of keeping them.
        _transponderWatch.Record(lap);

        // Checked BEFORE the lap is processed. Starting practice afterwards
        // would mean the crossing that triggered it lands in a session that
        // didn't exist yet, and the driver's first lap silently vanishes.
        // Practice goes green here, and this same crossing is then counted
        // by the line below as the driver's start lap.
        MaybeAutoStartPracticeOnGatePass(lap);

        _race.ProcessLapEvent(lap);
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
    private void MaybeAutoStartPracticeOnGatePass(DirectLapEvent lap)
    {
        // Not until the unfinished-race offer has been answered: a
        // practice started first took the session, and the race waiting to
        // be recovered was then never offered. See _recoveryDecided.
        if (!_settings.PracticeAutoStartOnGatePass
            || !_recoveryDecided
            || _practiceControl is null
            || _race.State != RaceState.Idle)
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
        var isEasyLap = decoder == DecoderConnection.EasyLap;
        var isILap = decoder == DecoderConnection.ILap;

        // Auto-connect retry belongs to the EasyLap HID search alone; a
        // serial port that fails to open should say so once, not every
        // few seconds.
        chkAutoConnect.Visible = isEasyLap;

        // The port picker's whole row, with its Rescan button, rather than
        // the two in it: an emptied row would keep its place in the card.
        lblSerialPort.Visible = isILap;
        serialPortRow.Visible = isILap;
        lblBaud.Visible = isILap;
        cmbBaud.Visible = isILap;

        if (isILap)
        {
            RefreshSerialPorts();
        }

        btnConnect.Text = "Connect";

        // The saved number, not the picker position - see DecoderConnection.
        // This is also what moves a retired Next Level Timing choice (0) on
        // to EasyLap in settings.json, the first time this runs after it.
        _settings.LastConnectionType = decoder;
        _settings.Save();

        // Only the chosen decoder stays connected. Switching used to leave
        // the old one running underneath - its laps still arriving, into
        // the same race, from hardware the screen said was not in use.
        if (!isEasyLap && _decoderClient.State is not DecoderConnectionState.Disconnected)
        {
            _decoderClient.Disconnect();
        }

        if (!isILap && _ilapClient.State is not DecoderConnectionState.Disconnected)
        {
            _ilapClient.Disconnect();
        }

        // The chosen decoder's ACTUAL state, rather than leaving the other
        // one's showing - switching from a connected EasyLap to I-Lap
        // should show I-Lap's own (probably disconnected) state, not a
        // stale green light.
        if (isILap)
        {
            RenderDecoderStatus(_ilapClient.State, _ilapClient.State.ToString());
        }
        else
        {
            RenderDecoderStatus(_decoderClient.State, _decoderClient.State.ToString());
        }
    }

    private void CmbConnectionType_SelectedIndexChanged(object? sender, EventArgs e) => UpdateConnectionModeUi();

    private void BtnConnect_Click(object? sender, EventArgs e)
    {
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
            return;
        }

        btnConnect.Enabled = false;
        btnDisconnect.Enabled = true;
        _decoderClient.Connect();
    }

    private void BtnDisconnect_Click(object? sender, EventArgs e)
    {
        if (SelectedDecoder == DecoderConnection.ILap)
        {
            _ilapClient.Disconnect();
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
