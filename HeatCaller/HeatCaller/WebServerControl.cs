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
    private readonly CheckBox _chkAllowResultEditing = new ThemedCheckBox();
    private readonly CheckBox _chkShowPins = new ThemedCheckBox();
    private readonly Button _btnStart = new();
    private readonly Button _btnStop = new();

    /// <summary>
    /// The one-off administrator step phones need - see NetworkSetup. Not
    /// enabled while it runs: it opens a UAC prompt, and a second press
    /// behind that prompt would open another.
    /// </summary>
    private readonly Button _btnSetUpPhonePages = new();

    /// <summary>Whether the phone-pages setup is waiting on the administrator prompt or on netsh.</summary>
    private bool _settingUpPhonePages;
    private readonly Label _lblStatus = new();
    private readonly Label _lblLastAction = new();
    private readonly TextBox _txtResultsUrl = new();
    private readonly TextBox _txtRemoteUrl = new();
    private readonly TextBox _txtCheckInUrl = new();
    private readonly TextBox _txtCoachUrl = new();
    private readonly TextBox _txtManageUrl = new();
    private readonly TextBox _txtOverlayUrl = new();
    private readonly TextBox _txtBroadcastUrl = new();

    // The relay's one line on this page. It is a shortcut to the Online
    // page, never the only door to it - somebody who never opens Phone &
    // Web must still be able to find the relay settings.
    //
    // As tall as the longest thing it says (SteadyLabel), so the button
    // under it does not hop as the line is rewritten every two seconds.
    private readonly SteadyLabel _lblRelay = new();
    private readonly System.Windows.Forms.Timer _relayTick = new();

    /// <summary>What the relay line says with publishing off, and on but not set up.</summary>
    private const string RelayOffText = "Online publishing is off. These pages are reachable only from this Wi-Fi, "
                                      + "and results stop being visible when this laptop closes.";

    private const string RelayNotSetUpText = "Online publishing is on but not set up - it needs an address, a club id and a token.";

    /// <summary>
    /// The relay line with the counts at their longest, for its height - see
    /// _lblRelay. An error from the relay can run longer, and the line grows
    /// for it.
    /// </summary>
    private const string LongestRelayStatus =
        "Relay unreachable.  Last published 99 minutes ago.  999 waiting to go.  999 could not be published.";

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>The PIN boxes and the address boxes at the normal text size.</summary>
    private const int PinBoxWidth = 130;
    private const int UrlBoxWidth = 420;

    /// <summary>The line under the coach page's QR code: what a driver does once they've scanned it.</summary>
    internal const string CoachPosterSubtitle =
        "Scan, pick your driver, tick Say my lap time. Same Wi-Fi as the timing laptop.";

    /// <summary>The phone coach page's address, from the live results one - the /coach route in WebServerService.</summary>
    internal static string CoachUrl(string localUrl) => localUrl + "coach";

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

        // The body face, worn rather than set once, so it follows the text
        // size - see TextSize.
        //
        // No MinimumSize: the 820x700 floor was honoured over the size the
        // page was given, and a small window lost the bottom of the page. A
        // short window scrolls the page instead (PageStack).
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout();

        // The three PIN boxes are saved as soon as somebody is done with
        // them, not only when Start Server is pressed. The Manager PIN in
        // particular is read straight from the settings by the relay - the
        // Club Website page's "Set Manager PIN..." sends people here to set
        // it - and a club that only reaches the manager through the relay
        // has no reason ever to start the LAN server, so a PIN that waited
        // for Start was never stored, never published, and gone after a
        // restart. Leave covers clicking or tabbing away; Validated covers
        // the window closing while the caret is still in a box, which
        // validates the focused control without moving focus off it.
        foreach (var box in new[] { _txtPin, _txtCheckInPin, _txtManagerPin })
        {
            box.Leave += (_, _) => SavePins();
            box.Validated += (_, _) => SavePins();
        }

        // And when the page goes out of sight - another page picked in the
        // sidebar - in case focus never left the box on the way.
        VisibleChanged += (_, _) =>
        {
            if (!Visible)
            {
                SavePins();
            }
        };

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

    /// <summary>The page's own face changed - handed the body face at a new text size - so everything else it keeps in step follows. See TextSize.</summary>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>A new text size, reaching this page through its parent's font - the only way it does, since the page wears a face of its own. See TextSize.</summary>
    protected override void OnParentFontChanged(EventArgs e)
    {
        base.OnParentFontChanged(e);
        _textSize.Apply();
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

    /// <summary>
    /// The intro, then three cards down the page. Server: the port and the
    /// two PINs, with Show PINs under them, what the PINs are for, starting
    /// with the app, and Start and Stop with what the server is doing beside
    /// them. Race-day manager: its PIN - in the same caption column as the
    /// server's, so the three PIN boxes line up down the page - its one
    /// switch, what it is, and what it last did. Then the seven addresses,
    /// each with its Copy and QR code buttons. Under the cards the column
    /// pickers and Open Big Screen, the note on the crowd-screen look, and
    /// the relay's line with the way to the Club Website page. On the layout
    /// pieces (LayoutPieces.cs), so nothing is placed by hand; the page
    /// scrolls when it is taller than the window (PageStack).
    ///
    /// It used to be placed in pixels down a 780px column, each block
    /// measured from the one above it: the group boxes sized to what they
    /// held once, at build, Start and Stop pinned to the Server group's
    /// right-hand edge, the check-in PIN's caption worked out backwards from
    /// the remote's box, the address boxes clamped between 200px and 420px
    /// with their buttons worked out beside them - and an 820x700 floor the
    /// page kept however small its window. Every font was set once, so
    /// nothing followed the text size.
    /// </summary>
    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "Serves pages to any device on the same Wi-Fi: live results, a phone remote for throwing flags, "
                 + "a self check-in page for drivers, a race-day manager for entries, the program and results, and "
                 + "overlays for streaming.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        var server = BuildServerCard(out var serverGrid);
        var manager = BuildManagerCard(out var managerGrid);
        var urls = BuildUrlCard(out var urlGrid);

        // --- The screens' columns, and the Big Screen ---

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

        // Where the old "Use the Synthwave palette on the Big Screen" box
        // was. The Big Screen's colours are the crowd-screen look now, one
        // choice shared with these pages and the online ones, so a second
        // switch here could only disagree with it.
        var lblLooksNote = new Label
        {
            Text = "The Big Screen and these pages use the crowd-screen look set in the Looks card on the Themes page.",
            AutoSize = true,
        };

        var lblColumnsHint = new Label
        {
            Text = "Column changes apply immediately on the web pages, and the next time the Big Screen is opened.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _textSize.Wear(lblColumnsHint, () => AppTheme.SmallFont);

        // --- The relay ---

        _lblRelay.Wordings = new[] { RelayOffText, RelayNotSetUpText, LongestRelayStatus };

        var btnOnline = new Button { Text = "Club Website Settings..." };
        btnOnline.Click += (_, _) => _openOnline();

        // --- The page ---

        var page = new PageStack();

        // Simple mode (1 Nov 2026) keeps the server's start, stop and PINs and
        // the addresses; the port, the race-day manager (result editing with
        // it) and the column choosers are Advanced. See SimpleMode.
        SimpleMode.HideRow(serverGrid, _numPort);

        page.Add(lblIntro);
        page.Add(server);
        page.Add(SimpleMode.Hide(manager));
        page.Add(urls);
        page.Add(new ButtonBar().With(SimpleMode.Hide(btnWebColumns), SimpleMode.Hide(btnBigScreenColumns), btnOpenBigScreen), gapBelow: UiLayout.RowGap);
        page.Add(lblLooksNote, gapBelow: UiLayout.HintGap);
        page.Add(SimpleMode.Hide(lblColumnsHint));
        page.Add(_lblRelay, gapBelow: UiLayout.HintGap);
        page.Add(new ButtonBar().With(btnOnline), gapBelow: 0);
        page.Add(SimpleMode.MoreLine(), stretch: false, gapBelow: 0);

        Controls.Add(page);

        // Once the fields are on the page and wear its face - what they are
        // measured in.
        //
        // One caption column for the server's PINs and the manager's, so
        // the three PIN boxes line up down the page as they are read.
        // (22 Oct 2026, page tidy): the addresses' grid was left out, so its
        // boxes started at a different x from the PIN boxes above them.
        _textSize.Keep(SizeFields);
        _textSize.Keep(() => FieldGrid.LineUp(new[] { serverGrid, managerGrid, urlGrid }));
    }

    /// <summary>
    /// The port and the remote and check-in PINs, one caption column, with
    /// Show PINs under them; what the PINs are for; starting with the app;
    /// and Start and Stop with the status line at the end of their line - or
    /// on a line of its own when it no longer fits there (FieldRow).
    /// </summary>
    private Section BuildServerCard(out FieldGrid grid)
    {
        _numPort.Minimum = 1024;
        _numPort.Maximum = 65535;
        _numPort.Value = Math.Clamp(_settings.WebServerPort, 1024, 65535);

        // WHY (20 Oct 2026): the port used to be saved only when Start was
        // pressed. 'Set up phone pages' sets Windows up for the port in this
        // box, but auto-start reads the saved one - so a club that changed
        // the port, set it up and closed the app came back to a server on
        // the old port, with no permission or firewall rule for it. Now it is
        // saved as it changes, the same way the auto-start box is. Hooked up
        // after the saved value is loaded above, so loading it saves nothing;
        // never while the server runs (the box is disabled then anyway).
        _numPort.ValueChanged += (_, _) =>
        {
            if (!_server.IsRunning && _settings.WebServerPort != (int)_numPort.Value)
            {
                _settings.WebServerPort = (int)_numPort.Value;
                _settings.Save();
            }
        };

        _txtPin.Text = _settings.WebServerPin;
        _txtPin.PlaceholderText = "4+ digits";
        _txtPin.UseSystemPasswordChar = true;

        // A row of its own, under the remote's. Its caption is the longest,
        // and sets the caption column, so the boxes line up under each
        // other however long it turns out to be.
        _txtCheckInPin.Text = _settings.WebServerCheckInPin;
        _txtCheckInPin.PlaceholderText = "blank = open";
        _txtCheckInPin.UseSystemPasswordChar = true;

        // Hidden by default: this page is the one on the laptop screen at
        // the desk, where drivers queue to check in.
        _chkShowPins.Text = "Show PINs";
        _chkShowPins.AutoSize = true;
        _chkShowPins.CheckedChanged += (_, _) =>
        {
            _txtPin.UseSystemPasswordChar = !_chkShowPins.Checked;
            _txtCheckInPin.UseSystemPasswordChar = !_chkShowPins.Checked;
            _txtManagerPin.UseSystemPasswordChar = !_chkShowPins.Checked;
        };

        grid = new FieldGrid();

        grid.AddRow("Port:", _numPort);
        grid.AddRow("Remote PIN:", _txtPin);
        grid.AddRow("Check-in PIN (optional):", _txtCheckInPin);
        grid.AddField(_chkShowPins);

        var lblPinHint = new Label
        {
            Text = "The race remote only works with a PIN of four or more digits, or anybody on the venue's Wi-Fi "
                 + "could throw flags. The check-in PIN goes on the wall for drivers, so never make it the remote's. "
                 + "Live results are always open. Ten wrong PINs from one phone make it wait five minutes. The port "
                 + "and PINs can only be changed while the server is stopped.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _textSize.Wear(lblPinHint, () => AppTheme.SmallFont);

        var chkAutoStart = new ThemedCheckBox
        {
            Text = "Start the server automatically when the app opens",
            AutoSize = true,
            Checked = _settings.WebServerAutoStart,
        };

        chkAutoStart.CheckedChanged += (_, _) =>
        {
            _settings.WebServerAutoStart = chkAutoStart.Checked;
            _settings.Save();
        };

        _btnStart.Text = "Start Server";
        _btnStart.Click += (_, _) => StartServer(silent: false);

        _btnStop.Text = "Stop Server";
        _btnStop.Click += (_, _) =>
        {
            _server.Stop();
            UpdateUi();
        };

        _btnSetUpPhonePages.Text = "Set up phone pages";
        _btnSetUpPhonePages.Click += BtnSetUpPhonePages_Click;

        // At the end of the buttons' line, cut short with "..." rather than
        // wrapped - or on a line of its own when it no longer fits there
        // (FieldRow).
        _lblStatus.AutoSize = false;
        _lblStatus.AutoEllipsis = true;
        _textSize.Wear(_lblStatus, () => AppTheme.BoldFont);

        var run = new FieldRow();

        run.Add(null, new ButtonBar().With(_btnStart, _btnStop, _btnSetUpPhonePages));
        run.AddLast(null, _lblStatus, () => _lblStatus.GetPreferredSize(Size.Empty).Width);

        var card = new Section("Server");

        card.Add(grid);
        card.Add(lblPinHint);
        card.Add(chkAutoStart, stretch: false);
        card.Add(run, gapBelow: 0);

        return card;
    }

    /// <summary>
    /// The race-day manager's own PIN and its one policy switch. Kept apart
    /// from the Server card above: it guards a whole phone page - entries,
    /// programme, race control, results - not just a couple of fields, and
    /// it is new enough that most clubs will never open it, so it earns its
    /// own explanation rather than a third row wedged into the server's.
    /// </summary>
    private Section BuildManagerCard(out FieldGrid grid)
    {
        _txtManagerPin.Text = _settings.ManagerPin;
        _txtManagerPin.PlaceholderText = "6+ digits";
        _txtManagerPin.UseSystemPasswordChar = true;

        grid = new FieldGrid();

        grid.AddRow("Manager PIN:", _txtManagerPin);

        _chkAllowResultEditing.Text = "Allow corrections to saved results";
        _chkAllowResultEditing.AutoSize = true;
        _chkAllowResultEditing.Checked = _settings.AllowResultEditing;

        _chkAllowResultEditing.CheckedChanged += (_, _) =>
        {
            _settings.AllowResultEditing = _chkAllowResultEditing.Checked;
            _settings.Save();
        };

        var lblManagerHint = new Label
        {
            Text = "A fuller phone page than the remote: it can add and edit drivers, check people in, build the "
                 + "program, start races, throw flags and, with the box above ticked, correct a saved result. It "
                 + "refuses every request until its PIN is six or more digits, so give it only to people you'd "
                 + "trust with the laptop. Like the PINs above, ten wrong tries mean a five-minute wait, and it can "
                 + "only be changed while the server is stopped.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _textSize.Wear(lblManagerHint, () => AppTheme.SmallFont);

        _lblLastAction.Text = "Race-day manager - no changes yet this session.";
        _lblLastAction.AutoSize = true;
        _lblLastAction.ForeColor = AppTheme.TextMuted;
        _textSize.Wear(_lblLastAction, () => AppTheme.SmallFont);

        var card = new Section("Race-day manager (phone)");

        card.Add(grid);
        card.Add(_chkAllowResultEditing, stretch: false);
        card.Add(lblManagerHint);
        card.Add(_lblLastAction, gapBelow: 0);

        return card;
    }

    /// <summary>
    /// The seven addresses, their captions in one column, each box followed
    /// by its Copy and QR code buttons - which go under the box when the
    /// line is too narrow for them (FieldGrid.Inline) - and a note under
    /// them.
    /// </summary>
    private Section BuildUrlCard(out FieldGrid grid)
    {
        var btnCopyResults = new Button { Text = "Copy" };
        var btnQrResults = new Button { Text = "QR code" };
        var btnCopyRemote = new Button { Text = "Copy" };
        var btnQrRemote = new Button { Text = "QR code" };
        var btnCopyCheckIn = new Button { Text = "Copy" };
        var btnQrCheckIn = new Button { Text = "QR code" };
        var btnCopyCoach = new Button { Text = "Copy" };
        var btnQrCoach = new Button { Text = "QR code" };
        var btnCopyManage = new Button { Text = "Copy" };
        var btnQrManage = new Button { Text = "QR code" };
        var btnCopyOverlay = new Button { Text = "Copy" };
        var btnCopyBroadcast = new Button { Text = "Copy" };

        foreach (var box in new[] { _txtResultsUrl, _txtRemoteUrl, _txtCheckInUrl, _txtCoachUrl, _txtManageUrl, _txtOverlayUrl, _txtBroadcastUrl })
        {
            box.ReadOnly = true;
        }

        // One column for every caption, as wide as the widest - which is
        // what makes the seven boxes line up rather than each starting
        // wherever its own caption happened to end.
        grid = new FieldGrid();

        grid.AddRow("Live results:", FieldGrid.Inline(_txtResultsUrl, btnCopyResults, btnQrResults));
        grid.AddRow("Phone remote:", FieldGrid.Inline(_txtRemoteUrl, btnCopyRemote, btnQrRemote));
        grid.AddRow("Driver check-in:", FieldGrid.Inline(_txtCheckInUrl, btnCopyCheckIn, btnQrCheckIn));

        // The phone coach page - see WebServerService.Coach. It was served
        // from 22 Oct 2026 but never listed here, so the only way to it was
        // typing /coach onto the live results address (29 Oct 2026).
        grid.AddRow("Driver coach:", FieldGrid.Inline(_txtCoachUrl, btnCopyCoach, btnQrCoach));
        grid.AddRow("Race manager:", FieldGrid.Inline(_txtManageUrl, btnCopyManage, btnQrManage));
        grid.AddRow("OBS overlay:", FieldGrid.Inline(_txtOverlayUrl, btnCopyOverlay));

        // The broadcast graphics for a stream - see WebServerService.Camera.
        grid.AddRow("Broadcast graphics:", FieldGrid.Inline(_txtBroadcastUrl, btnCopyBroadcast));

        btnCopyResults.Click += (_, _) => CopyIfPresent(_txtResultsUrl.Text);
        btnCopyRemote.Click += (_, _) => CopyIfPresent(_txtRemoteUrl.Text);
        btnCopyCheckIn.Click += (_, _) => CopyIfPresent(_txtCheckInUrl.Text);
        btnCopyCoach.Click += (_, _) => CopyIfPresent(_txtCoachUrl.Text);
        btnCopyManage.Click += (_, _) => CopyIfPresent(_txtManageUrl.Text);
        btnCopyOverlay.Click += (_, _) => CopyIfPresent(_txtOverlayUrl.Text);
        btnCopyBroadcast.Click += (_, _) => CopyIfPresent(_txtBroadcastUrl.Text);

        btnQrResults.Click += (_, _) => ShowPoster(
            _txtResultsUrl.Text, "LIVE RESULTS", "Scan with your phone camera. Same Wi-Fi as this laptop.");

        btnQrRemote.Click += (_, _) => ShowPoster(
            _txtRemoteUrl.Text, "RACE REMOTE", "Scan to control the race from your phone. Same Wi-Fi as this laptop.");

        btnQrCheckIn.Click += (_, _) => ShowPoster(
            _txtCheckInUrl.Text, "CHECK IN", "Scan, find your name, tap it. Same Wi-Fi as the timing laptop.");

        btnQrCoach.Click += (_, _) => ShowPoster(
            _txtCoachUrl.Text, "DRIVER COACH", CoachPosterSubtitle);

        btnQrManage.Click += (_, _) => ShowPoster(
            _txtManageUrl.Text, "RACE MANAGER", "Scan, then enter the Manager PIN. Same Wi-Fi as the timing laptop.");

        var lblUrlHint = new Label
        {
            Text = "All devices must be on the same Wi-Fi. The OBS overlay is the results page with a transparent "
                 + "background and no sponsor slides. The check-in wall poster is Self Check-In Poster on Race Day > Who's Racing.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _textSize.Wear(lblUrlHint, () => AppTheme.SmallFont);

        var card = new Section("Open these on your phone, tablet, or in OBS");

        card.Add(grid);
        card.Add(lblUrlHint, gapBelow: 0);

        return card;
    }

    /// <summary>
    /// The fields' widths at the text size in use. The port box is as wide
    /// as its five digits and its arrows need, and never narrower than a
    /// number box was at the normal text size; the PIN boxes and the address
    /// boxes are the width they were at the normal text size, at this size -
    /// an address is a few dozen characters, and a box across the page for
    /// it would push its buttons out to the far edge.
    ///
    /// Run again when the text size changes (TextSize.Keep).
    /// </summary>
    /// <summary>
    /// The one-off administrator step: a URL reservation and a firewall rule,
    /// so phones on the track's Wi-Fi can reach these pages. Asks first,
    /// because it opens a UAC prompt and a volunteer who did not expect one
    /// will answer No to it.
    ///
    /// Why there is a button at all: both changes are administrator-only, so
    /// a per-user installer cannot make them, and the alternative is a
    /// volunteer pasting two netsh commands into an admin Command Prompt on a
    /// race night. See NetworkSetup.
    /// </summary>
    private async void BtnSetUpPhonePages_Click(object? sender, EventArgs e)
    {
        if (_settingUpPhonePages)
        {
            return;
        }

        var port = (int)_numPort.Value;

        var answer = AppDialog.Show(this,
            "Windows needs two one-off changes before phones on this Wi-Fi can open HeatCaller's "
            + $"pages: permission for the app to accept connections on port {port}, and a firewall "
            + "rule to let them through.\r\n\r\n"
            + "Both need administrator, so Windows will ask you to allow it once. HeatCaller itself "
            + "keeps running as it is - nothing else changes, and this doesn't need doing again "
            + "unless you change the port.\r\n\r\n"
            + NetworkSetup.PrivateNetworkNote,
            "Set up phone pages",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Information);

        if (answer != DialogResult.OK)
        {
            return;
        }

        _settingUpPhonePages = true;
        _btnSetUpPhonePages.Text = "Setting up...";
        UpdateUi();

        NetworkSetup.Result result;

        try
        {
            result = await NetworkSetup.RunAsync(port);
        }
        finally
        {
            _settingUpPhonePages = false;

            // IsDisposed: this page is rebuilt when a soundscape, a venue
            // or Lap times only is applied, and the await above outlives
            // that - the same trap the Kokoro download has to guard against
            // on Voice Settings.
            if (!IsDisposed)
            {
                _btnSetUpPhonePages.Text = "Set up phone pages";
                UpdateUi();
            }
        }

        if (IsDisposed)
        {
            return;
        }

        // The console gets what netsh actually said - it is what a support
        // bundle will be read for if this goes wrong on somebody else's PC -
        // and the dialog gets the sentence.
        if (result.Transcript.Length > 0)
        {
            SessionLog.Write($"[web] Phone pages setup, port {port}:\r\n{result.Transcript}");
        }
        else
        {
            SessionLog.Write($"[web] Phone pages setup, port {port}: {result.Message}");
        }

        if (!result.Done)
        {
            // The commands themselves, so somebody with an admin account can
            // run them by hand - the fallback this button was meant to
            // replace, not remove.
            SessionLog.Write("[web] The two commands, for an admin Command Prompt:\r\n" + NetworkSetup.BothCommands(port));
        }

        AppDialog.Show(this,
            result.Message,
            result.Done ? "Phone pages are set up" : "Phone pages aren't set up",
            MessageBoxButtons.OK,
            result.Done ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private void SizeFields()
    {
        var digits = new string('0', ((int)_numPort.Maximum).ToString().Length);
        var port = Math.Max(
            UiLayout.AtTextSize(UiLayout.NumericWidth),
            TextRenderer.MeasureText(digits, Font).Width + SystemInformation.VerticalScrollBarWidth + 12 + UpDownButtonsSkin.Extra);

        var pin = UiLayout.AtTextSize(PinBoxWidth);
        var url = UiLayout.AtTextSize(UrlBoxWidth);

        Fit(_numPort, port);

        foreach (var box in new[] { _txtPin, _txtCheckInPin, _txtManagerPin })
        {
            Fit(box, pin);
        }

        foreach (var box in new[] { _txtResultsUrl, _txtRemoteUrl, _txtCheckInUrl, _txtCoachUrl, _txtManageUrl, _txtOverlayUrl, _txtBroadcastUrl })
        {
            Fit(box, url);
        }

        // Only when it changed: a new width lays the grid out again.
        static void Fit(Control field, int width)
        {
            if (field.Width != width)
            {
                field.Width = width;
            }
        }
    }

    private void CopyIfPresent(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            // Caught (17 Oct 2026, Bug Hunt 8, A18-1): a clipboard another
            // program is holding throws, and on a button click that was the
            // "Unexpected error" crash dialog. Every other copy in the app
            // already catches it.
            try
            {
                Clipboard.SetText(text);
            }
            catch (System.Runtime.InteropServices.ExternalException)
            {
                AppDialog.Show(this,
                    "Couldn't copy - another program is using the clipboard. Try again.",
                    "Not copied",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }
    }

    /// <summary>
    /// Copies the three PIN boxes, trimmed, into the settings and saves
    /// them - but only when one of them actually differs, so tabbing
    /// through the page does not rewrite settings.json each time. A changed
    /// Manager PIN is also handed to the relay publisher at once: the relay
    /// manager checks every request against it, and a PIN changed to clear
    /// a relay lockout should not wait for the next pass to be published.
    ///
    /// Does nothing while the server is running: the boxes are read-only
    /// then, and the PINs saved are the ones it started with.
    /// </summary>
    private void SavePins()
    {
        if (_server.IsRunning)
        {
            return;
        }

        // A changed PIN that is already some racer's personal PIN is not
        // taken (RacerPins.IsHeldByRacer): its box goes back to the PIN
        // saved, the others are saved as usual, and the page says so - in
        // words that don't tell the desk whose PIN it is.
        var refused = new List<string>();

        var pin = Allowed(_txtPin, _settings.WebServerPin, "Remote PIN");
        var checkInPin = Allowed(_txtCheckInPin, _settings.WebServerCheckInPin, "check-in PIN");
        var managerPin = Allowed(_txtManagerPin, _settings.ManagerPin, "Manager PIN");

        if (refused.Count > 0)
        {
            AppDialog.Show(this,
                $"The {string.Join(" and the ", refused)} you typed can't be used - "
                + (refused.Count == 1 ? "it is" : "they are") + " already in use elsewhere in HeatCaller. "
                + "Choose another. " + (refused.Count == 1 ? "The box has" : "The boxes have") + " been put back to what was saved.",
                "PIN not saved", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        var managerChanged = !string.Equals(_settings.ManagerPin, managerPin, StringComparison.Ordinal);

        if (!managerChanged
            && string.Equals(_settings.WebServerPin, pin, StringComparison.Ordinal)
            && string.Equals(_settings.WebServerCheckInPin, checkInPin, StringComparison.Ordinal))
        {
            return;
        }

        _settings.WebServerPin = pin;
        _settings.WebServerCheckInPin = checkInPin;
        _settings.ManagerPin = managerPin;

        // Set again after a restore whose keys couldn't be had back: check-in
        // may open now, and a PIN cleared after this is the club's own choice
        // (AppSettings.CheckInPinNeedsEntering).
        if (checkInPin.Length > 0)
        {
            _settings.CheckInPinRestored();
        }

        _settings.Save();

        if (managerChanged)
        {
            _relay.SettingsChanged();
        }

        string Allowed(TextBox box, string saved, string name)
        {
            var typed = box.Text.Trim();

            if (string.Equals(typed, saved, StringComparison.Ordinal) || !RacerPins.IsHeldByRacer(_settings, typed))
            {
                return typed;
            }

            refused.Add(name);
            box.Text = saved;
            return saved;
        }
    }

    private void StartServer(bool silent)
    {
        // Normally already saved as the boxes were left (pressing Start
        // moves focus off them), but an auto-start or a keyboard shortcut
        // may not have moved it, so saved here too before the server reads
        // them.
        SavePins();

        _server.Pin = _settings.WebServerPin;
        _server.CheckInPin = _settings.WebServerCheckInPin;
        _server.ManagerPin = _settings.ManagerPin;
        _settings.WebServerPort = (int)_numPort.Value;
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

        // Allowed whether the server is running or not: the usual way a club
        // finds out it is needed is starting the server and watching every
        // phone time out.
        _btnSetUpPhonePages.Enabled = !_settingUpPhonePages;
        // Enabled, not ReadOnly, for the port: on a NumericUpDown ReadOnly
        // only stops typing, and the spin buttons, the arrow keys and the
        // mouse wheel still change the value. A stray wheel over the box
        // mid-meeting left it showing a port the server was not on, and the
        // next Stop and Start moved the server there - and every QR poster,
        // OBS source and bookmarked phone with it.
        _numPort.Enabled = !running;
        _txtPin.ReadOnly = running;
        _txtCheckInPin.ReadOnly = running;
        _txtManagerPin.ReadOnly = running;

        if (running)
        {
            // Says what to do about it, not just what is true. The port goes
            // gray and the three PIN boxes read-only while the server is running -
            // they are read as it starts - and a box that silently will not
            // take a keystroke reads as a broken page rather than as a
            // setting that is locked on purpose.
            _lblStatus.Text = $"Running on port {_server.Port} - stop the server to change the port or PINs";
            _lblStatus.ForeColor = AppTheme.Success;

            _txtResultsUrl.Text = _server.LocalUrl();
            _txtRemoteUrl.Text = _server.LocalUrl() + "remote";
            _txtCheckInUrl.Text = _server.LocalUrl() + "checkin";
            _txtCoachUrl.Text = CoachUrl(_server.LocalUrl());
            _txtManageUrl.Text = _server.LocalUrl() + "manage";
            _txtOverlayUrl.Text = _server.LocalUrl() + "?overlay=1";
            // localhost, not the network address: OBS runs on this PC, and a
            // saved source must not break when the venue's Wi-Fi hands the
            // laptop a different address.
            _txtBroadcastUrl.Text = $"http://localhost:{_server.Port}/broadcast";
        }
        else
        {
            _lblStatus.Text = "Stopped";
            _lblStatus.ForeColor = AppTheme.TextMuted;

            _txtResultsUrl.Text = string.Empty;
            _txtRemoteUrl.Text = string.Empty;
            _txtCheckInUrl.Text = string.Empty;
            _txtCoachUrl.Text = string.Empty;
            _txtManageUrl.Text = string.Empty;
            _txtOverlayUrl.Text = string.Empty;
            _txtBroadcastUrl.Text = string.Empty;
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
            _lblRelay.Text = RelayOffText;
            _lblRelay.ForeColor = AppTheme.TextMuted;
            return;
        }

        if (!status.Configured)
        {
            _lblRelay.Text = RelayNotSetUpText;
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
