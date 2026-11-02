namespace HeatCaller;

/// <summary>
/// MainForm's share of the decoders from 3 up - MyLaps P3, ZRound, Cano /
/// RCHourglass, OpenStint and the serial EasyLap - which StreamDecoderClient
/// runs, one at a time, as the line.
/// </summary>
/// <remarks>
/// WHY (1 Nov 2026, decoder support plan): each decoder keeps its own way
/// in (AppSettings.StreamDecoders) - a COM port and baud rate, an address
/// and port, or the simulated decoder. The COM port and baud rows are the
/// I-Lap's, shared; the Source and Address rows are these decoders' own.
/// They cannot be a split loop yet: the split loop's clock code knows the
/// EasyLap's and the I-Lap's only, and nobody has two of these to line up.
/// </remarks>
public partial class MainForm
{
    private readonly ComboBox cmbStreamSource = new();
    private readonly TextBox txtStreamHost = new();
    private readonly TextBox txtStreamPort = new();
    private Label? lblStreamSource;
    private Label? lblStreamAddress;
    private FlowLayoutPanel? streamAddressRow;

    /// <summary>The transports the Source picker lists right now, in its order.</summary>
    private DecoderTransport[] _streamSources = Array.Empty<DecoderTransport>();

    /// <summary>True while the Source picker is being filled, so filling it is not taken for a choice.</summary>
    private bool _fillingStreamSources;

    /// <summary>The address field's width at the normal text size.</summary>
    private const int StreamHostWidth = 180;

    /// <summary>The port field's width at the normal text size.</summary>
    private const int StreamPortWidth = 70;

    /// <summary>The line decoder is one StreamDecoderClient runs.</summary>
    private bool LineIsStream => DecoderConnection.IsStream(SelectedDecoder);

    /// <summary>The Source and Address rows, added to the decoder's card under "Connect via:".</summary>
    private void BuildStreamDecoderRows(FieldGrid grid)
    {
        cmbStreamSource.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbStreamSource.SelectedIndexChanged += CmbStreamSource_SelectedIndexChanged;

        txtStreamHost.PlaceholderText = "IP address";
        txtStreamHost.Width = UiLayout.AtTextSize(StreamHostWidth);
        txtStreamPort.Width = UiLayout.AtTextSize(StreamPortWidth);

        lblStreamSource = grid.AddRow("Source:", cmbStreamSource);
        streamAddressRow = FieldGrid.Inline(txtStreamHost, new Label { Text = "Port:", AutoSize = true }, txtStreamPort);
        lblStreamAddress = grid.AddRow("Address:", streamAddressRow);

        lblStreamSource.Visible = false;
        cmbStreamSource.Visible = false;
        lblStreamAddress.Visible = false;
        streamAddressRow.Visible = false;
    }

    private static string SourceLabel(int decoder, DecoderTransport transport) => transport switch
    {
        DecoderTransport.Serial => "Serial (COM port)",
        DecoderTransport.Network when decoder == DecoderConnection.OpenStint => "Network (ZeroMQ)",
        DecoderTransport.Network => "Network (TCP)",
        _ => "Simulated - rehearse without a decoder",
    };

    /// <summary>The transport chosen for the line decoder, from the Source picker - or what is saved when the picker isn't showing it.</summary>
    private DecoderTransport SelectedStreamTransport =>
        cmbStreamSource.SelectedIndex >= 0 && cmbStreamSource.SelectedIndex < _streamSources.Length
            ? _streamSources[cmbStreamSource.SelectedIndex]
            : _settings.StreamDecoder(SelectedDecoder).Transport;

    /// <summary>
    /// Fills the Source picker and the address for the line decoder, from
    /// its saved settings. From UpdateConnectionModeUi, when the line is one
    /// of these decoders.
    /// </summary>
    private void FillStreamDecoderFields(int decoder)
    {
        var saved = _settings.StreamDecoder(decoder);
        _streamSources = DecoderProtocols.Transports(decoder);

        _fillingStreamSources = true;

        try
        {
            cmbStreamSource.Items.Clear();
            cmbStreamSource.Items.AddRange(_streamSources.Select(t => (object)SourceLabel(decoder, t)).ToArray());
            var index = Array.IndexOf(_streamSources, saved.Transport);
            cmbStreamSource.SelectedIndex = index >= 0 ? index : 0;
        }
        finally
        {
            _fillingStreamSources = false;
        }

        // As wide as its longest choice, which changes with the decoder.
        _streamSourcePickerWidth = null;
        SizeConnectionFields();

        txtStreamHost.Text = saved.Host;
        txtStreamPort.Text = saved.Port > 0 ? saved.Port.ToString() : DecoderProtocols.DefaultPort(decoder).ToString();

        SelectBaud(saved.Baud > 0 ? saved.Baud : DecoderProtocols.DefaultBaud(decoder));
    }

    /// <summary>Shows the rows the chosen source needs: the COM port and baud for serial, the address for the network, neither for the simulator.</summary>
    private void ShowStreamSourceRows(bool isStream)
    {
        var transport = isStream ? SelectedStreamTransport : DecoderTransport.Simulated;
        var network = isStream && transport == DecoderTransport.Network;

        SetVisible(lblStreamSource, isStream);
        SetVisible(cmbStreamSource, isStream);
        SetVisible(lblStreamAddress, network);
        SetVisible(streamAddressRow, network);

        static void SetVisible(Control? control, bool visible)
        {
            if (control is not null && control.Visible != visible)
            {
                control.Visible = visible;
            }
        }
    }

    private void CmbStreamSource_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_fillingStreamSources || !LineIsStream)
        {
            return;
        }

        var saved = _settings.StreamDecoder(SelectedDecoder);
        var transport = SelectedStreamTransport;

        if (saved.Transport == transport)
        {
            return;
        }

        // Not mid-race: it would disconnect the decoder timing it.
        if (RaceUnderWay && _streamClient.State is not DecoderConnectionState.Disconnected)
        {
            _fillingStreamSources = true;

            try
            {
                cmbStreamSource.SelectedIndex = Math.Max(0, Array.IndexOf(_streamSources, saved.Transport));
            }
            finally
            {
                _fillingStreamSources = false;
            }

            RefuseDecoderChangeMidRace("decoder's source");
            return;
        }

        saved.Transport = transport;
        _settings.Save();

        // A connection on the old source is not the one wanted any more.
        if (_streamClient.State is not DecoderConnectionState.Disconnected)
        {
            _streamClient.Disconnect();
        }

        UpdateConnectionModeUi();
    }

    /// <summary>
    /// Connects the line when it is one of these decoders: saves what the
    /// fields say, then hands it to the client. False, with the reason
    /// shown, when something needed is missing.
    /// </summary>
    /// <param name="quiet">True for the start-up connect - a missing port is said in the console, not a message box.</param>
    private bool ConnectStreamDecoder(bool quiet = false)
    {
        var decoder = SelectedDecoder;
        var saved = _settings.StreamDecoder(decoder);
        var transport = SelectedStreamTransport;
        saved.Transport = transport;

        string? problem = null;

        switch (transport)
        {
            case DecoderTransport.Serial:
                if (cmbSerialPort.SelectedItem is not string port)
                {
                    problem = "No COM port selected. Plug the decoder in, press Rescan and pick its port.";
                    break;
                }

                saved.ComPort = port;

                if (cmbBaud.SelectedItem is int baud)
                {
                    saved.Baud = baud;
                }

                break;

            case DecoderTransport.Network:
                var host = txtStreamHost.Text.Trim();

                if (host.Length == 0)
                {
                    problem = "Type the decoder's IP address first - it's in the decoder's own settings, or its manual.";
                    break;
                }

                if (!int.TryParse(txtStreamPort.Text.Trim(), out var number) || number is <= 0 or > 65535)
                {
                    problem = $"The port must be a number from 1 to 65535 ({DecoderProtocols.DefaultPort(decoder)} is usual for this decoder).";
                    break;
                }

                saved.Host = host;
                saved.Port = number;
                break;
        }

        _settings.Save();

        if (problem is not null)
        {
            if (quiet)
            {
                OnClientRawMessage($"{AppTime.LogStampNow()}  [auto-connect] {problem}");
            }
            else
            {
                AppDialog.Show(this, problem, "Not connected", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            return false;
        }

        btnConnect.Enabled = false;
        btnDisconnect.Enabled = true;

        _streamClient.Connect(new StreamDecoderOptions
        {
            Decoder = decoder,
            Transport = transport,
            Host = saved.Host,
            Port = saved.Port,
            ComPort = saved.ComPort,
            Baud = saved.Baud > 0 ? saved.Baud : DecoderProtocols.DefaultBaud(decoder),
            SimulatedTransponders = _profiles.All.Select(p => p.Transponder).ToList(),
        });

        if (DecoderConnection.IsBeta(decoder))
        {
            OnClientRawMessage($"{AppTime.LogStampNow()}  [decoder] BETA: HeatCaller's {DecoderConnection.ShortName(decoder)} support was written from the published format and has not yet been tested on a real decoder. Run a backup timing system alongside it, and send the raw capture file (decoder-raw-*.log in the data folder) with any report.");
        }

        if (transport == DecoderTransport.Simulated)
        {
            OnClientRawMessage($"{AppTime.LogStampNow()}  [decoder] A simulated {DecoderConnection.ShortName(decoder)} decoder - its cars are made up, laps from it are not real. Disconnect it before racing.");
        }

        return true;
    }

    /// <summary>The line decoder's state, whichever it is.</summary>
    private DecoderConnectionState StreamStateOf(int decoder) =>
        _streamClient.Decoder == decoder ? _streamClient.State : DecoderConnectionState.Disconnected;

    private void OnStreamStatusChanged(DecoderConnectionState state, string message)
    {
        // Not a bare InvokeRequired - see OnDecoderStatusChanged.
        if (!IsHandleCreated || InvokeRequired) { OnUiThread(() => OnStreamStatusChanged(state, message)); return; }

        // Overtaken on its way here - see OnDecoderStatusChanged.
        if (state != _streamClient.State)
        {
            return;
        }

        // Only the line's: a connection for a decoder no longer chosen says nothing on the light.
        if (_streamClient.Decoder == SelectedDecoder)
        {
            RenderDecoderStatus(state, message);
        }
    }

    /// <summary>A pass from one of these decoders. Any thread. Always the line's, when it is the decoder chosen.</summary>
    private void OnStreamPass(DirectLapEvent lap)
    {
        if (_race.IsThreaded && SendLinePassToTiming(lap))
        {
            return;
        }

        OnUiThread(() => RoutePass(lap));
    }

    /// <summary>The COM port remembered for the line decoder - the I-Lap's, or a serial stream decoder's own.</summary>
    private string RememberedComPort() =>
        LineIsStream ? _settings.StreamDecoder(SelectedDecoder).ComPort : _settings.ILapPort;

    /// <summary>Selects a rate in the baud picker, adding it when it isn't one of the usual ones.</summary>
    private void SelectBaud(int rate)
    {
        if (!cmbBaud.Items.Contains(rate))
        {
            var rates = cmbBaud.Items.OfType<int>().Append(rate).OrderBy(r => r).ToList();
            cmbBaud.Items.Clear();
            cmbBaud.Items.AddRange(rates.Cast<object>().ToArray());
            _baudPickerWidth = null;
        }

        cmbBaud.SelectedItem = rate;
    }
}
