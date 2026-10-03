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
        var split = new SplitLoop(_profiles)
        {
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
    private void ConnectSplitLoop()
    {
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

    /// <summary>Disconnects the split loop's decoder, if it is connected.</summary>
    private void DisconnectSplitLoop()
    {
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

    /// <summary>Line Up: measures how late the split loop reads a car, with both loops at the same spot.</summary>
    private void BtnLineUp_Click(object? sender, EventArgs e)
    {
        if (_splitLoop is null)
        {
            return;
        }

        using var form = new SplitLineUpForm(_splitLoop, _profiles, _settings.SplitLoopOffsetMs, handler => LinePassSeen += handler, handler => LinePassSeen -= handler);

        if (form.ShowDialog(this) != DialogResult.OK || form.OffsetMs is not { } measured)
        {
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

    /// <summary>The split loop's decoder gone, or nothing read at the split for three typical laps while cars cross the line.</summary>
    private TimingWatch.Problem? SplitLoopDown(TimingWatch.Situation situation, long now)
    {
        if (!situation.Racing || !_settings.SplitLoopEnabled || _splitLoop is null)
        {
            _splitWatchSince = null;
            return null;
        }

        _splitWatchSince ??= now;

        if (StateOf(OtherDecoder(SelectedDecoder)) != DecoderConnectionState.Connected)
        {
            return new TimingWatch.Problem("split-down", "Split loop not connected - laps are still timed, splits are not");
        }

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
