namespace HeatCaller;

/// <summary>
/// The Standby PC card (11 Nov 2026, race-night build plan, Phase 6.2 - see
/// WarmStandby): the timing PC ticks "Let a standby PC copy this one", the
/// second laptop ticks "This PC is a standby" and types the timing PC's
/// address, the same PIN goes on both, and the standby shows how fresh its
/// copy is beside a Take Over button.
///
/// WHY on Backup &amp; Restore: a standby is a live backup, and Take Over
/// is a restore - it runs through RunRestore, the very same path as
/// Restore Selected, safety backup and restart included. Putting it
/// anywhere else would mean a second restore path to keep honest.
/// </summary>
public sealed partial class BackupRestoreControl
{
    /// <summary>
    /// The standby mirror, handed over by the host (MainForm), which owns
    /// it. Null until then; the card then shows only the timing PC side.
    /// </summary>
    internal StandbyMirror? StandbyMirror { get; set; }

    /// <summary>The address a standby types to reach this PC ("192.168.1.20:8080"), or null while the Local Screens server is off. Asked by the card.</summary>
    public Func<string?>? StandbyServeAddress { get; set; }

    /// <summary>When a standby last collected a copy from this PC, and who. Asked by the card.</summary>
    public Func<(DateTime? AtUtc, string? Who)>? StandbyLastServed { get; set; }

    /// <summary>Raised when a standby setting changes, for the host to pass it to the Local Screens server and start or stop the mirror.</summary>
    public event Action? StandbySettingsChanged;

    private readonly CheckBox _chkStandbyServe = new ThemedCheckBox();
    private readonly CheckBox _chkStandbyMode = new ThemedCheckBox();
    private readonly TextBox _txtStandbyPin = new();
    private readonly TextBox _txtStandbyPrimary = new();
    private readonly Label _lblStandbyServe = new();
    private readonly Label _lblStandbyStatus = new();
    private readonly Button _btnStandbyCopyNow = new() { Text = "Copy Now" };
    private readonly Button _btnTakeOver = new() { Text = "Take Over..." };
    private readonly Button _btnStandbyAgain = new() { Text = "Make This a Standby Again..." };
    private readonly System.Windows.Forms.Timer _standbyTimer = new() { Interval = 2000 };

    private Section BuildStandbyCard()
    {
        var lblHint = new Label
        {
            Text = "A second laptop on the track's network can keep a copy of this one's race data - drivers, classes, heats, results, "
                   + "check-ins and the race in progress - and take over by hand if this one dies. The copy only goes to PCs on "
                   + "this network that give the standby PIN, and only one way: nothing on the standby can change this PC. "
                   + "Racing never waits for it.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _textSize.Wear(lblHint, () => AppTheme.SmallFont);

        _txtStandbyPin.UseSystemPasswordChar = true;
        _txtStandbyPin.Text = _settings.StandbyPin;
        _txtStandbyPin.PlaceholderText = "8+ letters or digits";
        _txtStandbyPin.Leave += (_, _) => SaveStandbyPin();

        _txtStandbyPrimary.Text = _settings.StandbyPrimaryAddress;
        _txtStandbyPrimary.PlaceholderText = "192.168.1.20:8080";
        _txtStandbyPrimary.Leave += (_, _) =>
        {
            var typed = _txtStandbyPrimary.Text.Trim();
            if (!string.Equals(typed, _settings.StandbyPrimaryAddress, StringComparison.Ordinal))
            {
                _settings.StandbyPrimaryAddress = typed;
                _settings.Save();
                StandbySettingsChanged?.Invoke();
            }

            RefreshStandby();
        };

        _chkStandbyServe.Text = "Let a standby PC copy this one (this is the timing PC)";
        _chkStandbyServe.AutoSize = true;
        _chkStandbyServe.Checked = _settings.StandbyServeEnabled;
        _chkStandbyServe.CheckedChanged += (_, _) =>
        {
            if (_settings.StandbyServeEnabled == _chkStandbyServe.Checked)
            {
                return;
            }

            SaveStandbyPin();
            _settings.StandbyServeEnabled = _chkStandbyServe.Checked;

            // One or the other: a standby handing out its own copy of a
            // night it isn't timing would only confuse a third PC.
            if (_chkStandbyServe.Checked && _chkStandbyMode.Checked)
            {
                _settings.StandbyModeEnabled = false;
                _chkStandbyMode.Checked = false;
            }

            _settings.Save();
            StandbySettingsChanged?.Invoke();
            RefreshStandby();
        };

        _chkStandbyMode.Text = "This PC is a standby - copy the timing PC every 15 seconds";
        _chkStandbyMode.AutoSize = true;
        _chkStandbyMode.Checked = _settings.StandbyModeEnabled;
        _chkStandbyMode.CheckedChanged += (_, _) =>
        {
            if (_settings.StandbyModeEnabled == _chkStandbyMode.Checked)
            {
                return;
            }

            // Once taken over, being a standby again is its own decision,
            // asked for by its own button - never a tick that slips on.
            if (_chkStandbyMode.Checked && WarmStandby.ReadTakenOver(AppPaths.Root) is not null)
            {
                _chkStandbyMode.Checked = false;
                AppDialog.Show(this,
                    "This PC took over as the timing PC, so it isn't copied over. Use \"Make This a Standby Again\" if it really should go back to being the standby.",
                    "Standby PC", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            SaveStandbyPin();
            _settings.StandbyPrimaryAddress = _txtStandbyPrimary.Text.Trim();
            _settings.StandbyModeEnabled = _chkStandbyMode.Checked;

            if (_chkStandbyMode.Checked && _chkStandbyServe.Checked)
            {
                _settings.StandbyServeEnabled = false;
                _chkStandbyServe.Checked = false;
            }

            _settings.Save();
            StandbySettingsChanged?.Invoke();
            RefreshStandby();
        };

        _lblStandbyServe.AutoSize = true;
        _lblStandbyServe.ForeColor = AppTheme.TextMuted;
        _textSize.Wear(_lblStandbyServe, () => AppTheme.SmallFont);

        _lblStandbyStatus.AutoSize = true;
        _lblStandbyStatus.ForeColor = AppTheme.TextMuted;
        _textSize.ReserveLines(_lblStandbyStatus, 2);

        _btnStandbyCopyNow.Click += async (_, _) =>
        {
            if (StandbyMirror is not { } mirror)
            {
                return;
            }

            _btnStandbyCopyNow.Enabled = false;
            try
            {
                await mirror.AskOnceAsync();
            }
            finally
            {
                _btnStandbyCopyNow.Enabled = true;
                RefreshStandby();
            }
        };

        _btnTakeOver.Click += (_, _) => TakeOver();
        _btnStandbyAgain.Click += (_, _) => MakeStandbyAgain();

        var grid = new FieldGrid();
        grid.AddRow("Standby PIN:", _txtStandbyPin);
        grid.AddRow("Timing PC address:", _txtStandbyPrimary);

        _textSize.Keep(() =>
        {
            var width = UiLayout.AtTextSize(UiLayout.FieldWidth);
            if (_txtStandbyPin.Width != width)
            {
                _txtStandbyPin.Width = width;
            }

            if (_txtStandbyPrimary.Width != width)
            {
                _txtStandbyPrimary.Width = width;
            }
        });

        var card = new Section("Standby PC");

        card.Add(lblHint, gapBelow: UiLayout.RowGap);
        card.Add(grid);
        card.Add(_chkStandbyServe, stretch: false, gapBelow: 0);
        card.Add(_lblStandbyServe, gapBelow: UiLayout.RowGap);
        card.Add(_chkStandbyMode, stretch: false);
        card.Add(_lblStandbyStatus, gapBelow: UiLayout.HintGap);
        card.Add(new ButtonBar().With(_btnStandbyCopyNow, _btnTakeOver, _btnStandbyAgain), gapBelow: 0);

        _standbyTimer.Tick += (_, _) => RefreshStandby();
        VisibleChanged += (_, _) =>
        {
            _standbyTimer.Enabled = Visible;
            if (Visible)
            {
                RefreshStandby();
            }
        };
        Disposed += (_, _) => _standbyTimer.Dispose();

        RefreshStandby();

        return card;
    }

    /// <summary>Saves the PIN as typed, once it is good enough - a half-typed one is never passed to the server.</summary>
    private void SaveStandbyPin()
    {
        var typed = _txtStandbyPin.Text.Trim();

        if (string.Equals(typed, _settings.StandbyPin, StringComparison.Ordinal))
        {
            return;
        }

        if (typed.Length > 0 && !WarmStandby.IsUsablePin(typed))
        {
            _lblStandbyServe.Text = "The standby PIN needs eight letters or digits or more - not saved.";
            return;
        }

        _settings.StandbyPin = typed;
        _settings.Save();
        StandbySettingsChanged?.Invoke();
    }

    /// <summary>The card brought up to date: what this PC is, how fresh the copy is, and which buttons make sense. UI thread.</summary>
    private void RefreshStandby()
    {
        var taken = WarmStandby.ReadTakenOver(AppPaths.Root);
        var pinOk = WarmStandby.IsUsablePin(_settings.StandbyPin);

        // --- The timing PC side ---
        if (!_settings.StandbyServeEnabled)
        {
            _lblStandbyServe.Text = "Off: no PC can copy this one.";
        }
        else if (!pinOk)
        {
            _lblStandbyServe.Text = "Set a standby PIN of eight letters or digits or more - until then no copy is handed out.";
        }
        else if (StandbyServeAddress?.Invoke() is not { } address)
        {
            _lblStandbyServe.Text = "Start the Local Screens server (Local Screens page) - the standby copies through it.";
        }
        else
        {
            var served = StandbyLastServed?.Invoke();
            var last = served?.AtUtc is { } at
                ? $" Last copied {WarmStandby.Age(DateTime.UtcNow - at)} ago{(served?.Who is { Length: > 0 } who ? $" by {who}" : string.Empty)}."
                : " No standby has copied it yet.";

            _lblStandbyServe.Text = $"On the standby, type this PC's address: {address}.{last}";
        }

        // --- The standby side ---
        var status = StandbyMirror?.Status ?? WarmStandby.MirrorStatus.Off;
        if (taken is not null)
        {
            status = status with { TakenOverUtc = taken.AtUtc };
        }

        _lblStandbyStatus.Text = WarmStandby.Line(status, DateTime.UtcNow)
                                 ?? "Not a standby. Tick the box above on the second laptop, not on the timing PC.";

        var mirroring = _settings.StandbyModeEnabled && taken is null;
        _txtStandbyPrimary.Enabled = taken is null;
        // Always there, greyed unless mirroring: a bar with every button
        // hidden collapses to nothing (and the layout check says so).
        _btnStandbyCopyNow.Enabled = mirroring;
        _btnTakeOver.Visible = taken is null && (mirroring || StandbyMirror?.HasCopy == true);
        _btnTakeOver.Enabled = StandbyMirror?.HasCopy == true;
        _btnStandbyAgain.Visible = taken is not null;
    }

    /// <summary>
    /// TAKE OVER: this PC becomes the timing PC, from the newest copy.
    ///
    /// In this order, each step for a reason:
    ///   1. Refused while a race runs here or a backup is being written,
    ///      and once this PC has already taken over (the marker).
    ///   2. Asked, with how old the copy is (WarmStandby.TakeOverQuestion).
    ///   3. The mirror stopped, and the newest copy set aside under a name
    ///      of its own and checked again, so no copy arriving now can
    ///      change what is restored halfway through.
    ///   4. The marker left and standby mode switched off BEFORE anything
    ///      is replaced - so from the restart on, this PC is never copied
    ///      over. Both are put back if the restore is refused.
    ///   5. RunRestore, which keeps this PC's own settings and voice files
    ///      (WarmStandby.MachineOwnFiles) and restarts.
    /// </summary>
    private void TakeOver()
    {
        if (StandbyMirror is not { } mirror || !mirror.HasCopy)
        {
            AppDialog.Show(this, "There is no copy of the timing PC on this PC yet, so there is nothing to take over from.",
                "Take over", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (WarmStandby.ReadTakenOver(AppPaths.Root) is not null)
        {
            AppDialog.Show(this, "This PC has already taken over - it is the timing PC.", "Take over", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!CanRestoreNow())
        {
            return;
        }

        var status = mirror.Status;
        var copyAt = status.LastCopyUtc ?? File.GetLastWriteTimeUtc(mirror.LatestPath);

        var confirm = AppDialog.Show(this,
            WarmStandby.TakeOverQuestion(copyAt, DateTime.UtcNow, status.Primary, status.Files),
            "Take over as the timing PC", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

        if (confirm != DialogResult.Yes || !CanRestoreNow())
        {
            return;
        }

        mirror.Stop();

        var restoring = Path.Combine(Path.GetDirectoryName(mirror.LatestPath)!, "standby-taking-over.zip");

        try
        {
            File.Copy(mirror.LatestPath, restoring, overwrite: true);

            WarmStandby.Check check;
            using (var copy = File.OpenRead(restoring))
            {
                check = WarmStandby.Validate(copy);
            }

            if (!check.Ok)
            {
                throw new InvalidDataException(check.Problem);
            }

            WarmStandby.WriteTakenOver(AppPaths.Root, new WarmStandby.TakenOver(DateTime.UtcNow, status.Primary, copyAt));
        }
        catch (Exception ex)
        {
            if (_settings.StandbyModeEnabled)
            {
                mirror.Start();
            }

            AppDialog.Show(this, $"Couldn't take over: {ex.Message}", "Take over", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var wasStandby = _settings.StandbyModeEnabled;
        _settings.StandbyModeEnabled = false;
        _settings.Save();

        RunRestore(restoring, WarmStandby.MachineOwnFiles, refused: () =>
        {
            // Refused with nothing touched: back to being the standby.
            WarmStandby.ClearTakenOver(AppPaths.Root);
            _settings.StandbyModeEnabled = wasStandby;
            _settings.Save();

            if (wasStandby)
            {
                mirror.Start();
            }

            RefreshStandby();
        });
    }

    /// <summary>
    /// Undoes the marker, by hand and after asking: for the PC that took
    /// over once the night is done and the old timing PC is back in charge.
    /// The next copy then replaces only the copy zips, never the data -
    /// that still takes a Take Over.
    /// </summary>
    private void MakeStandbyAgain()
    {
        var confirm = AppDialog.Show(this,
            "Make this PC a standby again?\n\nIt took over as the timing PC. As a standby it copies another PC's race data, "
            + "and a later Take Over would replace this PC's data with that copy. Only do this once another PC is timing the club's nights.",
            "Make this a standby again", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

        if (confirm != DialogResult.Yes)
        {
            return;
        }

        try
        {
            WarmStandby.ClearTakenOver(AppPaths.Root);
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, $"Couldn't: {ex.Message}", "Standby PC", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        // Ticked here, which saves the setting and has the host start the
        // mirror - and Start reads the marker afresh, now gone.
        _chkStandbyMode.Checked = true;
        StandbySettingsChanged?.Invoke();
        RefreshStandby();
    }
}
