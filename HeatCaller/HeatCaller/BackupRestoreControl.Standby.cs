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
        _txtStandbyPrimary.Leave += (_, _) => CommitStandbyPrimary(pressedEnter: false);

        // WHY (23 Nov 2026, bug hunt 24): RestoredSettings.StandbyMessage
        // tells the director to click in this box and press Enter to take on
        // a timing PC address that came from a restored backup - and Enter
        // here did nothing, so a held standby stayed paused for good.
        _txtStandbyPrimary.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter)
            {
                return;
            }

            // No ding: a single-line box has nothing to do with Enter itself.
            e.SuppressKeyPress = true;
            e.Handled = true;
            CommitStandbyPrimary(pressedEnter: true);
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

            // WHY (23 Nov 2026, bug hunt 24): ticked by hand, with the
            // address in the box beside it, is the club saying this is the
            // timing PC to copy - so an address from a restored backup is
            // taken on (RestoredSettings), as Enter in the box does.
            // WHY (24 Nov 2026, bug hunt 24 review): only a real tick. Make
            // This a Standby Again ticks the box itself, and that took on a
            // restored timing-PC address without anyone looking at it.
            if (TickConfirmsRestoredAddress(_chkStandbyMode.Checked, tickedByHand: !_tickingStandbyInCode))
            {
                RestoredSettings.Confirm(RestoredSettings.Part.Standby);
            }

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

        // RestoredSettings.StandbyMessage names "Backup & Restore": the & is
        // a character, not a shortcut key.
        _lblStandbyStatus.UseMnemonic = false;
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

    /// <summary>
    /// The Timing PC address box committed: left, or Enter pressed in it.
    /// A changed address is saved, and is the club's own, so it is no longer
    /// held as one from a restored backup. Enter takes on the address even
    /// unchanged - somebody pressing a key in the box with it in front of
    /// them - which is what RestoredSettings.StandbyMessage asks for. Just
    /// leaving the box unchanged does not, so tabbing through it never lets
    /// the PIN go to an address nobody looked at.
    /// </summary>
    private void CommitStandbyPrimary(bool pressedEnter)
    {
        var typed = _txtStandbyPrimary.Text.Trim();
        var changed = !string.Equals(typed, _settings.StandbyPrimaryAddress, StringComparison.Ordinal);
        var wasHeld = RestoredSettings.IsHeld(RestoredSettings.Part.Standby);

        if (changed)
        {
            _settings.StandbyPrimaryAddress = typed;
            _settings.Save();
        }

        if (wasHeld && typed.Length > 0 && (changed || pressedEnter))
        {
            RestoredSettings.Confirm(RestoredSettings.Part.Standby);
        }

        if (changed || (wasHeld && !RestoredSettings.IsHeld(RestoredSettings.Part.Standby)))
        {
            StandbySettingsChanged?.Invoke();
        }

        RefreshStandby();
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
            // WHY (23 Nov 2026, bug hunt 24): the refusal used to be written
            // straight into the label, which RefreshStandby rewrites on every
            // 2-second tick (and at once after a tick box) - so within two
            // seconds the card read "On the standby, type this PC's
            // address..." as if the new PIN had taken, while the old one was
            // still in force. RefreshStandby now says it for as long as the
            // box holds the refused PIN (while the box isn't being typed in).
            _lblStandbyServe.Text = StandbyPinRefusal(typed, _settings.StandbyPin, typing: false) ?? string.Empty;
            return;
        }

        _settings.StandbyPin = typed;
        _settings.Save();
        StandbySettingsChanged?.Invoke();
    }

    /// <summary>
    /// What to say when the PIN in the box was turned down and not saved, or
    /// null when there is nothing to say: the box is empty, holds the saved
    /// PIN, holds a good PIN, or is still being typed in.
    /// </summary>
    internal static string? StandbyPinRefusal(string? typedPin, string? savedPin, bool typing)
    {
        var typed = (typedPin ?? string.Empty).Trim();

        if (typing
            || typed.Length == 0
            || string.Equals(typed, savedPin, StringComparison.Ordinal)
            || WarmStandby.IsUsablePin(typed))
        {
            return null;
        }

        return WarmStandby.IsUsablePin(savedPin)
            ? "The standby PIN needs eight letters or digits or more - not saved. The old PIN is still the one in use."
            : "The standby PIN needs eight letters or digits or more - not saved.";
    }

    /// <summary>The card brought up to date: what this PC is, how fresh the copy is, and which buttons make sense. UI thread.</summary>
    private void RefreshStandby()
    {
        var taken = WarmStandby.ReadTakenOver(AppPaths.Root);
        var pinOk = WarmStandby.IsUsablePin(_settings.StandbyPin);

        // --- The timing PC side ---
        if (StandbyPinRefusal(_txtStandbyPin.Text, _settings.StandbyPin, typing: _txtStandbyPin.Focused) is { } refusal)
        {
            _lblStandbyServe.Text = refusal;
        }
        else if (!_settings.StandbyServeEnabled)
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

        var mirroring = _settings.StandbyModeEnabled && taken is null;

        // WHY (23 Nov 2026, bug hunt 24): while the timing PC's address is
        // held as one from a restored backup, the mirror copies nothing, and
        // this card said nothing about why. It says so, and what to do.
        _lblStandbyStatus.Text = mirroring && RestoredSettings.IsHeld(RestoredSettings.Part.Standby)
            ? RestoredSettings.StandbyMessage
            : WarmStandby.Line(status, DateTime.UtcNow)
              ?? "Not a standby. Tick the box above on the second laptop, not on the timing PC.";
        _txtStandbyPrimary.Enabled = taken is null;
        // Always there, greyed unless mirroring: a bar with every button
        // hidden collapses to nothing (and the layout check says so).
        _btnStandbyCopyNow.Enabled = mirroring;
        _btnTakeOver.Visible = taken is null && (mirroring || StandbyMirror?.HasCopy == true);
        _btnTakeOver.Enabled = StandbyMirror?.HasCopy == true;
        _btnStandbyAgain.Visible = taken is not null;

        ShowStandbyTile(taken is not null, status);
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
    ///   4. RunRestore, which keeps this PC's own settings and voice files
    ///      (WarmStandby.MachineOwnFiles) and restarts.
    ///   5. The marker left and standby mode switched off BEFORE anything
    ///      is replaced - so from the restart on, this PC is never copied
    ///      over - but AFTER the restore's safety backup is made, at its
    ///      point of no return. The safety backup is this PC as a standby,
    ///      so restoring it puts the standby back; made after the marker,
    ///      it brought back a PC that said it had taken over and copied
    ///      nothing.
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

        // After a restart the mirror knows the copy is there but not how
        // much is in it until an ask succeeds - and with the timing PC dead
        // none will - so it is counted here rather than asked as "0 files".
        var files = status.Files > 0 ? status.Files : FilesInCopy(mirror.LatestPath);

        var confirm = AppDialog.Show(this,
            WarmStandby.TakeOverQuestion(copyAt, DateTime.UtcNow, status.Primary, files),
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

        RunRestore(restoring, WarmStandby.MachineOwnFiles,
            refused: () =>
            {
                // Refused with nothing touched, the marker and the setting
                // included: back to being the standby.
                if (wasStandby)
                {
                    mirror.Start();
                }

                RefreshStandby();
            },
            atPointOfNoReturn: () =>
            {
                try
                {
                    WarmStandby.WriteTakenOver(AppPaths.Root, new WarmStandby.TakenOver(DateTime.UtcNow, status.Primary, copyAt));
                }
                finally
                {
                    // Whatever became of the marker, standby mode goes off.
                    _settings.StandbyModeEnabled = false;
                    _settings.Save();
                }
            });
    }

    /// <summary>How many stores the copy on disk holds, or zero when it can't be read or isn't a good copy. Never throws.</summary>
    private static int FilesInCopy(string path)
    {
        try
        {
            // Shared with everything, so a copy arriving now is not stopped from replacing it.
            using var copy = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var check = WarmStandby.Validate(copy);
            return check.Ok ? check.Files : 0;
        }
        catch (Exception)
        {
            return 0;
        }
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
        // mirror - and Start reads the marker afresh, now gone. Marked as
        // ticked in code, so it does not take on a restored address.
        _tickingStandbyInCode = true;
        try
        {
            _chkStandbyMode.Checked = true;
        }
        finally
        {
            _tickingStandbyInCode = false;
        }

        StandbySettingsChanged?.Invoke();
        RefreshStandby();
    }

    /// <summary>True while the standby box is being ticked by the app itself, not by someone at the PC.</summary>
    private bool _tickingStandbyInCode;

    /// <summary>
    /// Whether ticking the standby box takes on a timing-PC address that
    /// came with a restored backup: only a tick someone made by hand, with
    /// the address in the box beside it. See RestoredSettings.Part.Standby.
    /// </summary>
    internal static bool TickConfirmsRestoredAddress(bool ticked, bool tickedByHand) => ticked && tickedByHand;
}
