namespace HeatCaller;

/// <summary>Turn the local web server on/off, set the port and the remote, check-in and manager PINs, and see the URLs to open on other devices.</summary>
public sealed class WebServerControl : UserControl
{
    private readonly WebServerService _server;
    private readonly AppSettings _settings;
    private readonly Action _openBigScreen;
    private readonly RelayPublisher _relay;
    private readonly Action _openOnline;

    /// <summary>The app's undo, for the column pickers. Null when the host didn't pass one.</summary>
    private readonly UndoService? _undo;

    private readonly NumericUpDown _numPort = new();
    private readonly TextBox _txtPin = new();
    private readonly TextBox _txtCheckInPin = new();
    private readonly TextBox _txtManagerPin = new();
    private readonly CheckBox _chkAllowResultEditing = new();
    private readonly CheckBox _chkShowPins = new();
    private readonly Button _btnStart = new();
    private readonly Button _btnStop = new();
    private readonly Label _lblStatus = new();
    private readonly Label _lblLastAction = new();
    private readonly TextBox _txtResultsUrl = new();
    private readonly TextBox _txtRemoteUrl = new();
    private readonly TextBox _txtCheckInUrl = new();
    private readonly TextBox _txtManageUrl = new();
    private readonly TextBox _txtOverlayUrl = new();

    // The relay's one line on this page. It is a shortcut to the Online
    // page, never the only door to it - somebody who never opens Phone &
    // Web must still be able to find the relay settings.
    private readonly Label _lblRelay = new();
    private readonly System.Windows.Forms.Timer _relayTick = new();

    public WebServerControl(
        WebServerService server,
        AppSettings settings,
        Action openBigScreen,
        RelayPublisher relay,
        Action openOnline,
        UndoService? undo = null)
    {
        _undo = undo;
        _server = server;
        _settings = settings;
        _openBigScreen = openBigScreen;
        _relay = relay;
        _openOnline = openOnline;

        Dock = DockStyle.Fill;
        // A floor rather than a promise: both groups size themselves from
        // what is in them, so the page is as tall as its contents.
        MinimumSize = new Size(820, 700);
        Font = AppTheme.BodyFont;

        BuildLayout();
        UpdateUi();
        RefreshRelayLine();
        RefreshLastAction();

        // Two seconds is plenty: this is a reassurance line, not an
        // instrument. The Club Website page ticks once a second because that is
        // where somebody watches it while fixing something.
        _relayTick.Interval = 2000;
        _relayTick.Tick += (_, _) =>
        {
            RefreshRelayLine();
            RefreshLastAction();
        };
        _relayTick.Start();

        // No auto-start here any more - see AutoStartIfConfigured.
    }

    /// <summary>
    /// Starts the server if the settings say to. Called by the host once
    /// its window has been shown.
    ///
    /// It used to happen in this constructor, which runs inside the main
    /// window's constructor - so the server was answering phones before
    /// there was a window to hand their requests to. A flag thrown from
    /// the remote in those seconds was answered "sent" and went nowhere.
    /// </summary>
    public void AutoStartIfConfigured()
    {
        if (_settings.WebServerAutoStart && !_server.IsRunning)
        {
            StartServer(silent: true);
        }
    }

    private const int Inset = 12;

    /// <summary>
    /// Builds the page top to bottom, each block measured from the one
    /// above it.
    ///
    /// Every position here used to be a literal - the Server group 150
    /// tall, the status line at 224, the URL group at 254, the column
    /// buttons at 466 - which is why there was nowhere to put a fourth URL
    /// row without moving four other numbers, and why the check-in page
    /// ended up documented in a hint instead of listed with the others.
    /// The groups now size themselves from their contents and the page
    /// flows, so adding a row costs one row.
    /// </summary>
    private void BuildLayout()
    {
        const int pageWidth = 780;

        var lblIntro = new Label
        {
            Text = "Serves five pages to any device on the same Wi-Fi: live results (also usable as an OBS browser "
                 + "source), a phone remote for throwing flags at the track, a self check-in page for drivers, a "
                 + "race-day manager for running entries, the program and results from a phone, and a "
                 + "transparent overlay for streaming.",
            Location = new Point(Inset, Inset),
            AutoSize = true,
            MaximumSize = new Size(pageWidth, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.BodyFont,
        };

        var grpSettings = new GroupBox
        {
            Text = "Server",
            Location = new Point(Inset, UiLayout.Below(lblIntro, UiLayout.RowGap)),
            Width = pageWidth,
            Font = AppTheme.BodyFont,
        };

        BuildServerGroup(grpSettings);

        _lblStatus.Location = new Point(Inset, UiLayout.Below(grpSettings, UiLayout.RowGap));
        _lblStatus.AutoSize = true;
        _lblStatus.Font = AppTheme.BoldFont;

        var grpManager = new GroupBox
        {
            Text = "Race-Day Manager (phone)",
            Location = new Point(Inset, UiLayout.Below(_lblStatus, UiLayout.RowGap)),
            Width = pageWidth,
            Font = AppTheme.BodyFont,
        };

        BuildManagerGroup(grpManager);

        var grpUrls = new GroupBox
        {
            Text = "Open these on your phone, tablet, or in OBS",
            Location = new Point(Inset, UiLayout.Below(grpManager, UiLayout.RowGap)),
            Width = pageWidth,
            Font = AppTheme.BodyFont,
        };

        BuildUrlGroup(grpUrls);

        var bottom = UiLayout.Below(grpUrls, UiLayout.RowGap);

        var btnWebColumns = new Button { Text = "Choose Web Columns..." };
        btnWebColumns.Click += (_, _) =>
        {
            using var form = new KpiPickerForm("Live Results page", _settings.WebKpis, KpiCatalog.DefaultWeb);
            if (form.ShowDialog(this) == DialogResult.OK)
            {
                // The picker's Restore Defaults lands here too, so the old
                // list is kept for Undo. Settings only - the page reads them
                // live when it serves.
                var before = _settings.WebKpis.ToList();
                if (!before.SequenceEqual(form.SelectedKeys))
                {
                    _undo?.Capture("Change the Live Results columns", () =>
                    {
                        _settings.WebKpis = before.ToList();
                        _settings.Save();
                    });
                }

                _settings.WebKpis = form.SelectedKeys;
                _settings.Save();
            }
        };

        var btnBigScreenColumns = new Button { Text = "Choose Big Screen Columns..." };
        btnBigScreenColumns.Click += (_, _) =>
        {
            using var form = new KpiPickerForm("Big Screen", _settings.BigScreenKpis, KpiCatalog.DefaultBigScreen);
            if (form.ShowDialog(this) == DialogResult.OK)
            {
                var before = _settings.BigScreenKpis.ToList();
                if (!before.SequenceEqual(form.SelectedKeys))
                {
                    _undo?.Capture("Change the Big Screen columns", () =>
                    {
                        _settings.BigScreenKpis = before.ToList();
                        _settings.Save();
                    });
                }

                _settings.BigScreenKpis = form.SelectedKeys;
                _settings.Save();
            }
        };

        var btnOpenBigScreen = new Button { Text = "Open Big Screen" };
        btnOpenBigScreen.Click += (_, _) => _openBigScreen();

        var buttonsBottom = UiLayout.FlowButtons(
            Inset, bottom, Inset + pageWidth,
            btnWebColumns, btnBigScreenColumns, btnOpenBigScreen);

        // Where the old "Use the Synthwave palette on the Big Screen" box
        // was. The Big Screen's colours are the crowd-screen look now, one
        // choice shared with these pages and the online ones, so a second
        // switch here could only disagree with it.
        var lblLooksNote = new Label
        {
            Text = "The Big Screen and these pages use the crowd-screen look set on Themes \u2192 Looks.",
            Location = new Point(Inset, buttonsBottom + UiLayout.RowGap),
            AutoSize = true,
            MaximumSize = new Size(pageWidth, 0),
            Font = AppTheme.BodyFont,
        };

        var lblColumnsHint = new Label
        {
            Text = "Column changes apply immediately on the web pages, and the next time the Big Screen is opened.",
            Location = new Point(Inset, UiLayout.Below(lblLooksNote, UiLayout.HintGap)),
            AutoSize = true,
            MaximumSize = new Size(pageWidth, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        _lblRelay.Location = new Point(Inset, UiLayout.Below(lblColumnsHint, UiLayout.SectionGap));
        _lblRelay.AutoSize = true;
        _lblRelay.MaximumSize = new Size(pageWidth, 0);
        _lblRelay.Font = AppTheme.BodyFont;

        var btnOnline = new Button { Text = "Club Website Settings...", Font = AppTheme.BodyFont };
        btnOnline.Size = new Size(UiLayout.WidthFor(btnOnline), UiLayout.ButtonHeight);
        btnOnline.Location = new Point(Inset, UiLayout.Below(_lblRelay, UiLayout.HintGap));
        btnOnline.Click += (_, _) => _openOnline();

        Controls.Add(_lblRelay);
        Controls.Add(btnOnline);
        Controls.Add(btnOpenBigScreen);
        Controls.Add(lblLooksNote);
        Controls.Add(btnWebColumns);
        Controls.Add(btnBigScreenColumns);
        Controls.Add(lblColumnsHint);
        Controls.Add(lblIntro);
        Controls.Add(grpSettings);
        Controls.Add(_lblStatus);
        Controls.Add(grpManager);
        Controls.Add(grpUrls);
    }

    private void BuildServerGroup(GroupBox group)
    {
        var inner = group.Width - Inset * 2;
        var y = UiLayout.InsideGroup(group);

        var lblPort = new Label { Text = "Port:", AutoSize = true, Font = AppTheme.BodyFont };

        _numPort.Font = AppTheme.BodyFont;
        _numPort.Width = UiLayout.NumericWidth + 20;
        _numPort.Minimum = 1024;
        _numPort.Maximum = 65535;
        _numPort.Value = Math.Clamp(_settings.WebServerPort, 1024, 65535);

        lblPort.Location = new Point(Inset, UiLayout.CentreOn(lblPort, y, _numPort.Height));
        _numPort.Location = new Point(UiLayout.After(lblPort, AppTheme.BodyFont), y);

        var lblPin = new Label { Text = "Remote PIN:", AutoSize = true, Font = AppTheme.BodyFont };

        _txtPin.Font = AppTheme.BodyFont;
        _txtPin.Width = 130;
        _txtPin.Text = _settings.WebServerPin;
        _txtPin.PlaceholderText = "4+ digits";
        _txtPin.UseSystemPasswordChar = true;

        lblPin.Location = new Point(
            _numPort.Right + UiLayout.SectionGap,
            UiLayout.CentreOn(lblPin, y, _txtPin.Height));

        _txtPin.Location = new Point(UiLayout.After(lblPin, AppTheme.BodyFont), y);

        _btnStart.Text = "Start Server";
        _btnStart.Font = AppTheme.BodyFont;
        _btnStart.Click += (_, _) => StartServer(silent: false);

        _btnStop.Text = "Stop Server";
        _btnStop.Font = AppTheme.BodyFont;
        _btnStop.Click += (_, _) =>
        {
            _server.Stop();
            UpdateUi();
        };

        _btnStart.Size = new Size(UiLayout.WidthFor(_btnStart), UiLayout.ButtonHeight);
        _btnStop.Size = new Size(UiLayout.WidthFor(_btnStop), UiLayout.ButtonHeight);

        // Pinned to the group's right edge, so the row does not depend on
        // how wide the port and PIN fields ended up being.
        _btnStop.Location = new Point(group.Width - Inset - _btnStop.Width, y);
        _btnStart.Location = new Point(_btnStop.Left - UiLayout.ButtonGap - _btnStart.Width, y);

        y = UiLayout.Below(_btnStart, UiLayout.RowGap);

        // Its own row, under the remote's: the Start and Stop buttons own
        // the right-hand end of the row above, and there is not room for a
        // second caption and box between them and the remote PIN.
        var lblCheckInPin = new Label { Text = "Check-in PIN (optional):", AutoSize = true, Font = AppTheme.BodyFont };

        _txtCheckInPin.Font = AppTheme.BodyFont;
        _txtCheckInPin.Width = _txtPin.Width;
        _txtCheckInPin.Text = _settings.WebServerCheckInPin;
        _txtCheckInPin.PlaceholderText = "blank = open";
        _txtCheckInPin.UseSystemPasswordChar = true;

        // The box lines up under the remote's, and its caption ends where
        // the box begins - however long the caption turns out to be.
        _txtCheckInPin.Location = new Point(_txtPin.Left, y);

        lblCheckInPin.Location = new Point(
            Math.Max(Inset, _txtPin.Left - UiLayout.LabelGap - TextRenderer.MeasureText(lblCheckInPin.Text, AppTheme.BodyFont).Width),
            UiLayout.CentreOn(lblCheckInPin, y, _txtCheckInPin.Height));

        // Hidden by default: this page is the one on the laptop screen at
        // the desk, where drivers queue to check in.
        _chkShowPins.Text = "Show PINs";
        _chkShowPins.AutoSize = true;
        _chkShowPins.Font = AppTheme.BodyFont;
        _chkShowPins.Location = new Point(
            _txtCheckInPin.Right + UiLayout.SectionGap,
            UiLayout.CentreOn(_chkShowPins, y, _txtCheckInPin.Height));
        _chkShowPins.CheckedChanged += (_, _) =>
        {
            _txtPin.UseSystemPasswordChar = !_chkShowPins.Checked;
            _txtCheckInPin.UseSystemPasswordChar = !_chkShowPins.Checked;
            _txtManagerPin.UseSystemPasswordChar = !_chkShowPins.Checked;
        };

        y = UiLayout.Below(_txtCheckInPin, UiLayout.RowGap);

        var lblPinHint = new Label
        {
            Text = "The race remote only works with a PIN of four or more digits - without one, anybody on the "
                 + "venue's Wi-Fi could throw flags. The check-in page has a PIN of its own, and needs one only if "
                 + "you set it: it goes on the wall for drivers, so never make it the same as the remote's. Live "
                 + "results are always open to view. Ten wrong PINs from one phone make it wait five minutes. "
                 + "The port and both PINs can only be changed while the server is stopped - they are read as it "
                 + "starts, so a change made now would not be the one the phones were using.",
            Location = new Point(Inset, y),
            AutoSize = true,
            MaximumSize = new Size(inner, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        y = UiLayout.Below(lblPinHint, UiLayout.RowGap);

        var chkAutoStart = new CheckBox
        {
            Text = "Start the server automatically when the app opens",
            Location = new Point(Inset, y),
            AutoSize = true,
            Font = AppTheme.BodyFont,
            Checked = _settings.WebServerAutoStart,
        };

        chkAutoStart.CheckedChanged += (_, _) =>
        {
            _settings.WebServerAutoStart = chkAutoStart.Checked;
            _settings.Save();
        };

        group.Controls.AddRange(new Control[]
        {
            lblPort, _numPort, lblPin, _txtPin, _btnStart, _btnStop,
            lblCheckInPin, _txtCheckInPin, _chkShowPins, lblPinHint, chkAutoStart,
        });

        group.Height = UiLayout.Below(chkAutoStart, Inset);
    }

    /// <summary>
    /// The race-day manager's own PIN and its one policy switch. Kept apart
    /// from the Server group above: it guards a whole phone page - entries,
    /// programme, race control, results - not just a couple of fields, and
    /// it is new enough that most clubs will never open it, so it earns its
    /// own explanation rather than a third row wedged into the PIN group.
    /// </summary>
    private void BuildManagerGroup(GroupBox group)
    {
        var inner = group.Width - Inset * 2;
        var y = UiLayout.InsideGroup(group);

        var lblManagerPin = new Label { Text = "Manager PIN:", AutoSize = true, Font = AppTheme.BodyFont };

        _txtManagerPin.Font = AppTheme.BodyFont;
        _txtManagerPin.Width = _txtPin.Width;
        _txtManagerPin.Text = _settings.ManagerPin;
        _txtManagerPin.PlaceholderText = "6+ digits";
        _txtManagerPin.UseSystemPasswordChar = true;

        lblManagerPin.Location = new Point(Inset, UiLayout.CentreOn(lblManagerPin, y, _txtManagerPin.Height));
        _txtManagerPin.Location = new Point(UiLayout.After(lblManagerPin, AppTheme.BodyFont), y);

        y = UiLayout.Below(_txtManagerPin, UiLayout.RowGap);

        _chkAllowResultEditing.Text = "Allow corrections to saved results";
        _chkAllowResultEditing.AutoSize = true;
        _chkAllowResultEditing.Font = AppTheme.BodyFont;
        _chkAllowResultEditing.Location = new Point(Inset, y);
        _chkAllowResultEditing.Checked = _settings.AllowResultEditing;

        _chkAllowResultEditing.CheckedChanged += (_, _) =>
        {
            _settings.AllowResultEditing = _chkAllowResultEditing.Checked;
            _settings.Save();
        };

        y = UiLayout.Below(_chkAllowResultEditing, UiLayout.RowGap);

        var lblManagerHint = new Label
        {
            Text = "The race-day manager is a fuller phone page than the remote: it can add and edit drivers, "
                 + "check people in, build and reorder the program, start races and throw flags, and - only "
                 + "with the box above checked - correct a saved result. It refuses every request until its PIN "
                 + "is six or more digits, so give it out only to people you'd trust with the laptop itself. Ten "
                 + "wrong PINs from one phone make it wait five minutes, same as the other PINs above. Like "
                 + "those, this PIN can only be changed while the server is stopped.",
            Location = new Point(Inset, y),
            AutoSize = true,
            MaximumSize = new Size(inner, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        y = UiLayout.Below(lblManagerHint, UiLayout.RowGap);

        _lblLastAction.Text = "Race-day manager - no changes yet this session.";
        _lblLastAction.AutoSize = true;
        _lblLastAction.Location = new Point(Inset, y);
        _lblLastAction.Font = AppTheme.SmallFont;
        _lblLastAction.ForeColor = AppTheme.TextMuted;

        group.Controls.AddRange(new Control[]
        {
            lblManagerPin, _txtManagerPin, _chkAllowResultEditing, lblManagerHint, _lblLastAction,
        });

        group.Height = UiLayout.Below(_lblLastAction, Inset);
    }

    private void BuildUrlGroup(GroupBox group)
    {
        var inner = group.Width - Inset * 2;

        var lblResults = new Label { Text = "Live results:", AutoSize = true, Font = AppTheme.BodyFont };
        var lblRemote = new Label { Text = "Phone remote:", AutoSize = true, Font = AppTheme.BodyFont };
        var lblCheckIn = new Label { Text = "Driver check-in:", AutoSize = true, Font = AppTheme.BodyFont };
        var lblManage = new Label { Text = "Race manager:", AutoSize = true, Font = AppTheme.BodyFont };
        var lblOverlay = new Label { Text = "OBS overlay:", AutoSize = true, Font = AppTheme.BodyFont };

        // One column for every caption, taken from the widest - which is
        // what makes the five fields line up rather than each starting
        // wherever its own label happened to end.
        var captionWidth = new[] { lblResults.Text, lblRemote.Text, lblCheckIn.Text, lblManage.Text, lblOverlay.Text }
            .Max(t => TextRenderer.MeasureText(t, AppTheme.BodyFont).Width);

        var fieldLeft = Inset + captionWidth + UiLayout.LabelGap;

        var btnCopyResults = new Button { Text = "Copy", Font = AppTheme.BodyFont };
        var btnQrResults = new Button { Text = "QR code", Font = AppTheme.BodyFont };
        var btnCopyRemote = new Button { Text = "Copy", Font = AppTheme.BodyFont };
        var btnQrRemote = new Button { Text = "QR code", Font = AppTheme.BodyFont };
        var btnCopyCheckIn = new Button { Text = "Copy", Font = AppTheme.BodyFont };
        var btnQrCheckIn = new Button { Text = "QR code", Font = AppTheme.BodyFont };
        var btnCopyManage = new Button { Text = "Copy", Font = AppTheme.BodyFont };
        var btnQrManage = new Button { Text = "QR code", Font = AppTheme.BodyFont };
        var btnCopyOverlay = new Button { Text = "Copy", Font = AppTheme.BodyFont };

        var copyWidth = Math.Max(90, TextRenderer.MeasureText("Copy", AppTheme.BoldFont).Width + 30);
        var qrWidth = Math.Max(100, TextRenderer.MeasureText("QR code", AppTheme.BoldFont).Width + 30);

        var fieldWidth = Math.Clamp(
            inner - (fieldLeft - Inset) - copyWidth - qrWidth - UiLayout.ButtonGap * 2,
            200,
            420);

        var copyLeft = fieldLeft + fieldWidth + UiLayout.ButtonGap;
        var qrLeft = copyLeft + copyWidth + UiLayout.ButtonGap;

        var y = UiLayout.InsideGroup(group);

        y = UrlRow(group, lblResults, _txtResultsUrl, btnCopyResults, btnQrResults,
            fieldLeft, fieldWidth, copyLeft, copyWidth, qrLeft, qrWidth, y);

        y = UrlRow(group, lblRemote, _txtRemoteUrl, btnCopyRemote, btnQrRemote,
            fieldLeft, fieldWidth, copyLeft, copyWidth, qrLeft, qrWidth, y);

        y = UrlRow(group, lblCheckIn, _txtCheckInUrl, btnCopyCheckIn, btnQrCheckIn,
            fieldLeft, fieldWidth, copyLeft, copyWidth, qrLeft, qrWidth, y);

        y = UrlRow(group, lblManage, _txtManageUrl, btnCopyManage, btnQrManage,
            fieldLeft, fieldWidth, copyLeft, copyWidth, qrLeft, qrWidth, y);

        y = UrlRow(group, lblOverlay, _txtOverlayUrl, btnCopyOverlay, null,
            fieldLeft, fieldWidth, copyLeft, copyWidth, qrLeft, qrWidth, y);

        btnCopyResults.Click += (_, _) => CopyIfPresent(_txtResultsUrl.Text);
        btnCopyRemote.Click += (_, _) => CopyIfPresent(_txtRemoteUrl.Text);
        btnCopyCheckIn.Click += (_, _) => CopyIfPresent(_txtCheckInUrl.Text);
        btnCopyManage.Click += (_, _) => CopyIfPresent(_txtManageUrl.Text);
        btnCopyOverlay.Click += (_, _) => CopyIfPresent(_txtOverlayUrl.Text);

        btnQrResults.Click += (_, _) => ShowPoster(
            _txtResultsUrl.Text, "LIVE RESULTS", "Scan with your phone camera. Same Wi-Fi as this laptop.");

        btnQrRemote.Click += (_, _) => ShowPoster(
            _txtRemoteUrl.Text, "RACE REMOTE", "Scan to control the race from your phone. Same Wi-Fi as this laptop.");

        btnQrCheckIn.Click += (_, _) => ShowPoster(
            _txtCheckInUrl.Text, "CHECK IN", "Scan, find your name, tap it. Same Wi-Fi as the timing laptop.");

        btnQrManage.Click += (_, _) => ShowPoster(
            _txtManageUrl.Text, "RACE MANAGER", "Scan, then enter the Manager PIN. Same Wi-Fi as the timing laptop.");

        var lblUrlHint = new Label
        {
            Text = "All devices must be on the same Wi-Fi. The overlay URL is the same results page with a "
                 + "transparent background and no sponsor slides, so it composites cleanly over video in an OBS "
                 + "Browser source. The check-in page has its own wall poster on the Entries page.",
            Location = new Point(Inset, y + UiLayout.HintGap),
            AutoSize = true,
            MaximumSize = new Size(inner, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        group.Controls.Add(lblUrlHint);

        group.Height = UiLayout.Below(lblUrlHint, Inset);
    }

    /// <summary>One caption, one read-only address, and its buttons. Returns the y of the row below.</summary>
    private static int UrlRow(
        GroupBox group, Label caption, TextBox field, Button copy, Button? qr,
        int fieldLeft, int fieldWidth, int copyLeft, int copyWidth, int qrLeft, int qrWidth, int y)
    {
        field.Font = AppTheme.BodyFont;
        field.Width = fieldWidth;
        field.ReadOnly = true;
        field.Location = new Point(fieldLeft, y);

        caption.Location = new Point(Inset, UiLayout.CentreOn(caption, y, field.Height));

        copy.Size = new Size(copyWidth, UiLayout.ButtonHeight);
        copy.Location = new Point(copyLeft, y - 3);

        group.Controls.Add(caption);
        group.Controls.Add(field);
        group.Controls.Add(copy);

        if (qr is not null)
        {
            qr.Size = new Size(qrWidth, UiLayout.ButtonHeight);
            qr.Location = new Point(qrLeft, y - 3);
            group.Controls.Add(qr);
        }

        return y + UiLayout.ButtonHeight + UiLayout.ButtonGap;
    }

    private void CopyIfPresent(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            Clipboard.SetText(text);
        }
    }

    private void StartServer(bool silent)
    {
        _server.Pin = _txtPin.Text.Trim();
        _server.CheckInPin = _txtCheckInPin.Text.Trim();
        _server.ManagerPin = _txtManagerPin.Text.Trim();
        _settings.WebServerPort = (int)_numPort.Value;
        _settings.WebServerPin = _server.Pin;
        _settings.WebServerCheckInPin = _server.CheckInPin;
        _settings.ManagerPin = _server.ManagerPin;
        _settings.Save();

        if (!_server.Start((int)_numPort.Value, out var error))
        {
            if (!silent)
            {
                AppDialog.Show(this, error ?? "Couldn't start the server.", "Server didn't start", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        else if (!silent && PinWarning() is { } warning)
        {
            // Started regardless: live results and check-in are worth
            // having even while the remote is locked. The server logs the
            // same thing to the console for an auto-start nobody watched.
            AppDialog.Show(this, warning, "Server started - check the PINs", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        UpdateUi();
    }

    /// <summary>What is wrong with the PINs as set, in plain words, or null when nothing is.</summary>
    private string? PinWarning()
    {
        if (!WebServerService.IsUsableRemotePin(_server.Pin))
        {
            return "The phone remote will refuse every command until its PIN is four or more digits (numbers only). "
                 + "Stop the server, set the Remote PIN, and start it again.";
        }

        if (_server.CheckInPin.Length > 0 && _server.CheckInPin == _server.Pin)
        {
            return "The check-in PIN is the same as the remote PIN. The check-in PIN goes on the wall for drivers, "
                 + "so anyone who reads it could throw flags too. Stop the server and change one of them.";
        }

        // Blank is left alone here, same as the check-in PIN: the manager is
        // an opt-in page, and most clubs will never set this. Only a PIN
        // that was actually typed in and doesn't meet the bar gets a nudge.
        if (_server.ManagerPin.Length > 0 && !RaceDayService.IsUsableManagerPin(_server.ManagerPin))
        {
            return "The race-day manager will refuse every request until its PIN is six or more digits (numbers "
                 + "only). Stop the server, fix the Manager PIN, and start it again.";
        }

        if (_server.CheckInPin.Length > 0 && _server.CheckInPin == _server.ManagerPin)
        {
            return "The check-in PIN is the same as the manager PIN. The check-in PIN goes on the wall for "
                 + "drivers, so anyone who reads it could edit entries, the program, and saved results too. "
                 + "Stop the server and change one of them.";
        }

        return null;
    }

    private void UpdateUi()
    {
        var running = _server.IsRunning;

        _btnStart.Enabled = !running;
        _btnStop.Enabled = running;
        _numPort.ReadOnly = running;
        _txtPin.ReadOnly = running;
        _txtCheckInPin.ReadOnly = running;
        _txtManagerPin.ReadOnly = running;

        if (running)
        {
            // Says what to do about it, not just what is true. The port and
            // the three PIN boxes go read-only while the server is running -
            // they are read as it starts - and a box that silently will not
            // take a keystroke reads as a broken page rather than as a
            // setting that is locked on purpose.
            _lblStatus.Text = $"Running on port {_server.Port} - stop the server to change the port or PINs";
            _lblStatus.ForeColor = AppTheme.Success;

            _txtResultsUrl.Text = _server.LocalUrl();
            _txtRemoteUrl.Text = _server.LocalUrl() + "remote";
            _txtCheckInUrl.Text = _server.LocalUrl() + "checkin";
            _txtManageUrl.Text = _server.LocalUrl() + "manage";
            _txtOverlayUrl.Text = _server.LocalUrl() + "?overlay=1";
        }
        else
        {
            _lblStatus.Text = "Stopped";
            _lblStatus.ForeColor = AppTheme.TextMuted;

            _txtResultsUrl.Text = string.Empty;
            _txtRemoteUrl.Text = string.Empty;
            _txtCheckInUrl.Text = string.Empty;
            _txtManageUrl.Text = string.Empty;
            _txtOverlayUrl.Text = string.Empty;
        }
    }

    /// <summary>
    /// Opens the scan-to-join poster for one of the URLs.
    ///
    /// Refuses on an empty URL rather than showing an unscannable card:
    /// before the server is running there is no address to encode, and a
    /// code that leads nowhere is worse than being told why.
    /// </summary>
    private void ShowPoster(string url, string heading, string subtitle)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            AppDialog.Show(
                this,
                "Start the web server first - there's no address to put in a code until it's listening.",
                "Nothing to scan yet",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);

            return;
        }

        // Shown non-modally so the race can carry on behind it - the poster
        // is meant to sit open on a second screen for the whole meeting.
        var poster = new JoinPosterForm(url, heading, subtitle);
        poster.Show(this);
    }

    /// <summary>
    /// One line about the relay: off, getting through, or not - and how
    /// long ago something last landed.
    /// </summary>
    private void RefreshRelayLine()
    {
        var status = _relay.Status;

        if (!status.Enabled)
        {
            _lblRelay.Text = "Online publishing is off. These pages are reachable only from this Wi-Fi, "
                           + "and results stop being visible when this laptop closes.";
            _lblRelay.ForeColor = AppTheme.TextMuted;
            return;
        }

        if (!status.Configured)
        {
            _lblRelay.Text = "Online publishing is on but not set up - it needs an address, a club id and a token.";
            _lblRelay.ForeColor = AppTheme.Primary;
            return;
        }

        _lblRelay.Text = (status.Online ? "Publishing online.  " : "Relay unreachable.  ")
                       + RelaySettingsControl.DescribeStatus(status);

        _lblRelay.ForeColor = status.Online ? AppTheme.TextDark : AppTheme.Primary;
    }

    /// <summary>
    /// What the race-day manager last did from a phone, if anything - the
    /// same reassurance the relay line gives for publishing, so somebody
    /// watching this page can tell a change actually reached the laptop.
    /// </summary>
    private void RefreshLastAction()
    {
        var manager = _server.Manager;

        _lblLastAction.Text = manager?.LastActionUtc is { } utc
            ? $"Race-day manager - last from a phone: {manager.LastAction} ({AppTime.TimeOfDay(utc.ToLocalTime())})"
            : "Race-day manager - no changes yet this session.";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _relayTick.Stop();
            _relayTick.Dispose();
        }

        base.Dispose(disposing);
    }
}
