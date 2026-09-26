namespace HeatCaller;

/// <summary>
/// The guided setup shown the first time the app starts.
///
/// It covers what a club needs before its first race - how the app looks,
/// the decoder, a track, the classes and a few drivers - and nothing else.
/// Every step can be skipped, the whole thing can be skipped from any step,
/// any step can be jumped to from the rail on the left, and everything it
/// sets can be changed later on the normal pages, which each step names so
/// people know where to find it again.
///
/// Each step is saved as it is left, not at the end, so closing the window
/// part-way keeps what was already entered.
///
/// It does not start the app tour itself: <see cref="TakeTour"/> tells the
/// caller the box was ticked, and MainForm starts it once the wizard has
/// gone (after the restart, when a new look needs one).
/// </summary>
public sealed class FirstRunWizard : Form
{
    private const int RailWidth = 190;
    private const int ContentLeft = RailWidth + 32;
    private const int ContentWidth = 520;
    private const int DriverRows = 6;

    private static readonly string[] StepNames =
    {
        "Welcome", "Look", "Timing", "Track", "Classes", "Drivers", "All set",
    };

    private static readonly string[] SuggestedClasses = { "Stock", "Modified", "Open", "Novice", "Spec" };

    private readonly AppSettings _settings;
    private readonly TrackLayoutStore _trackLayouts;
    private readonly RaceClassStore _raceClasses;
    private readonly ProfileStore _profiles;

    // The frame
    private readonly Panel _rail = new();
    private readonly Label _lblBrand = new();
    private readonly Label _lblRailCaption = new();
    private readonly Label[] _railLabels = new Label[StepNames.Length];
    private readonly Label _lblStepTitle = new();
    private readonly Label _lblStepBody = new();
    private readonly Panel _content = new();
    private readonly Label _lblProgress = new();
    private readonly Button _btnSkipAll = new();
    private readonly Button _btnBack = new();
    private readonly Button _btnNext = new();

    // Welcome
    private readonly Label _lblWelcomeRestore = new();
    private readonly Button _btnRestore = new();

    // Look
    private readonly ListBox _lstLooks = new();
    private readonly Panel _lookPreview = new();
    private readonly Label _lblLookDescription = new();
    private readonly CheckBox _chk24Hour = new();

    // Timing
    private readonly ComboBox _cmbConnection = new();
    private readonly Label _lblSerialPort = new();
    private readonly ComboBox _cmbSerialPort = new();
    private readonly Button _btnRescan = new();
    private readonly Label _lblBaud = new();
    private readonly ComboBox _cmbBaud = new();
    private readonly Label _lblTimingHint = new();

    // Track
    private readonly TextBox _txtTrackName = new();
    private readonly Label _lblTrackHint = new();

    // Classes
    private readonly TextBox _txtClasses = new();
    private readonly Label _lblChipsCaption = new();
    private readonly FlowLayoutPanel _classChips = new();
    private readonly Label _lblClassesHint = new();

    // Drivers
    private readonly Label _lblNameHeader = new();
    private readonly Label _lblTransponderHeader = new();
    private readonly TextBox[] _txtDriverNames = new TextBox[DriverRows];
    private readonly TextBox[] _txtTransponders = new TextBox[DriverRows];
    private readonly Label _lblDriverClass = new();
    private readonly ComboBox _cmbDriverClass = new();
    private readonly Label _lblDriversHint = new();

    // All set
    private readonly Label _lblSummary = new();
    private readonly CheckBox _chkTour = new();

    // What each step actually did, for the summary on the last page.
    private string? _doneLook;
    private string? _doneTiming;
    private string? _doneTrack;
    private readonly List<string> _doneClasses = new();
    private int _doneDrivers;
    private int _skippedDrivers;

    private readonly string _startingLookKey;
    private int _step;

    public FirstRunWizard(AppSettings settings, TrackLayoutStore trackLayouts, RaceClassStore raceClasses, ProfileStore profiles)
    {
        _settings = settings;
        _trackLayouts = trackLayouts;
        _raceClasses = raceClasses;
        _profiles = profiles;
        _startingLookKey = Looks.ForApp(settings).Key;

        Text = "Welcome to HeatCaller";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(ContentLeft + ContentWidth + 28, 580);
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        BuildLayout();
        AppTheme.ApplyRecursively(this);
        ShowStep(0);
    }

    /// <summary>True when the "show me around" box was ticked on the last page.</summary>
    public bool TakeTour { get; private set; }

    /// <summary>True when a different look was chosen - it only takes effect after a restart.</summary>
    public bool RestartForLook { get; private set; }

    /// <summary>True when "Restore a backup instead" was chosen - the caller opens Backup &amp; Restore.</summary>
    public bool OpenBackups { get; private set; }

    // --- Layout ----------------------------------------------------------

    private void BuildLayout()
    {
        // The rail: every step by name, the current one highlighted, so it
        // is always clear how much is left.
        _rail.Location = new Point(0, 0);
        _rail.Size = new Size(RailWidth, ClientSize.Height);

        _lblBrand.Text = "HEATCALLER";
        _lblBrand.AutoSize = true;
        _lblBrand.Font = AppTheme.NavFont;
        _lblBrand.Location = new Point(22, 26);

        _lblRailCaption.Text = "First-time setup";
        _lblRailCaption.AutoSize = true;
        _lblRailCaption.Font = AppTheme.SmallFont;
        _lblRailCaption.Location = new Point(22, 54);

        _rail.Controls.Add(_lblBrand);
        _rail.Controls.Add(_lblRailCaption);

        for (var i = 0; i < StepNames.Length; i++)
        {
            var index = i;
            var label = new Label
            {
                Text = StepNames[i],
                AutoSize = false,
                Size = new Size(RailWidth - 24, 34),
                Location = new Point(12, 100 + i * 40),
                Padding = new Padding(12, 0, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                Cursor = Cursors.Hand,
            };

            // Any step can be jumped to - it is a checklist, not a gate.
            label.Click += (_, _) =>
            {
                SaveCurrentStep();
                ShowStep(index);
            };

            _railLabels[i] = label;
            _rail.Controls.Add(label);
        }

        _lblStepTitle.Location = new Point(ContentLeft, 30);
        _lblStepTitle.AutoSize = true;
        _lblStepTitle.MaximumSize = new Size(ContentWidth, 0);
        _lblStepTitle.Font = AppTheme.TitleFont;

        _lblStepBody.Location = new Point(ContentLeft, 78);
        _lblStepBody.AutoSize = true;
        _lblStepBody.MaximumSize = new Size(ContentWidth, 0);

        _content.Location = new Point(ContentLeft, 180);
        _content.Size = new Size(ContentWidth, 310);

        _lblProgress.AutoSize = true;
        _lblProgress.Font = AppTheme.SmallFont;

        _btnSkipAll.Text = "Skip setup";
        _btnSkipAll.Width = 120;
        _btnSkipAll.Click += (_, _) => SkipEverything();

        _btnBack.Text = "Back";
        _btnBack.Width = 100;
        _btnBack.Click += (_, _) =>
        {
            SaveCurrentStep();
            ShowStep(_step - 1);
        };

        _btnNext.Text = "Next";
        _btnNext.Width = 130;
        _btnNext.Click += (_, _) => NextClicked();

        var buttonTop = ClientSize.Height - 58;
        _btnNext.Location = new Point(ClientSize.Width - 28 - _btnNext.Width, buttonTop);
        _btnBack.Location = new Point(_btnNext.Left - 12 - _btnBack.Width, buttonTop);
        _btnSkipAll.Location = new Point(ContentLeft, buttonTop);
        _lblProgress.Location = new Point(_btnSkipAll.Right + 16, buttonTop + 10);

        BuildWelcome();
        BuildLook();
        BuildTiming();
        BuildTrack();
        BuildClasses();
        BuildDrivers();
        BuildDone();

        Controls.Add(_rail);
        Controls.Add(_lblStepTitle);
        Controls.Add(_lblStepBody);
        Controls.Add(_content);
        Controls.Add(_lblProgress);
        Controls.Add(_btnSkipAll);
        Controls.Add(_btnBack);
        Controls.Add(_btnNext);
    }

    private void BuildWelcome()
    {
        _lblWelcomeRestore.Text = "Moving from another laptop? Restore your backup instead - it brings your drivers, "
                                  + "classes, results and settings with it.";
        _lblWelcomeRestore.AutoSize = true;
        _lblWelcomeRestore.MaximumSize = new Size(ContentWidth, 0);
        _lblWelcomeRestore.Font = AppTheme.SmallFont;
        _lblWelcomeRestore.Location = new Point(0, 120);

        _btnRestore.Text = "Restore a backup instead";
        _btnRestore.Width = 240;
        _btnRestore.Location = new Point(0, 160);
        _btnRestore.Click += (_, _) =>
        {
            OpenBackups = true;
            Finish(takeTour: false);
        };
    }

    private void BuildLook()
    {
        _lstLooks.Location = new Point(0, 0);
        _lstLooks.Size = new Size(200, 200);
        _lstLooks.IntegralHeight = false;

        foreach (var look in Looks.BuiltIn)
        {
            _lstLooks.Items.Add(new LookItem(look));
        }

        for (var i = 0; i < _lstLooks.Items.Count; i++)
        {
            if (_lstLooks.Items[i] is LookItem item
                && string.Equals(item.Look.Key, _startingLookKey, StringComparison.OrdinalIgnoreCase))
            {
                _lstLooks.SelectedIndex = i;
            }
        }

        if (_lstLooks.SelectedIndex < 0 && _lstLooks.Items.Count > 0)
        {
            _lstLooks.SelectedIndex = 0;
        }

        _lstLooks.SelectedIndexChanged += (_, _) =>
        {
            _lblLookDescription.Text = SelectedLook?.Description ?? string.Empty;
            _lookPreview.Invalidate();
        };

        _lookPreview.Location = new Point(220, 0);
        _lookPreview.Size = new Size(ContentWidth - 220, 130);
        _lookPreview.Paint += (_, e) => PaintLookPreview(e.Graphics, _lookPreview.ClientRectangle, SelectedLook);

        _lblLookDescription.Location = new Point(220, 142);
        _lblLookDescription.AutoSize = true;
        _lblLookDescription.MaximumSize = new Size(ContentWidth - 220, 0);
        _lblLookDescription.Font = AppTheme.SmallFont;
        _lblLookDescription.Text = SelectedLook?.Description ?? string.Empty;

        _chk24Hour.Text = "Show times on a 24-hour clock (14:30 rather than 2:30 PM)";
        _chk24Hour.AutoSize = true;
        _chk24Hour.Location = new Point(0, 222);
        _chk24Hour.Checked = _settings.Use24HourTime;
    }

    private void BuildTiming()
    {
        _cmbConnection.Location = new Point(0, 4);
        _cmbConnection.Width = 340;
        _cmbConnection.DropDownStyle = ComboBoxStyle.DropDownList;

        // The Connection page's own list, so the two never offer different
        // choices.
        _cmbConnection.Items.AddRange(DecoderConnection.Items());
        _cmbConnection.SelectedIndex = DecoderConnection.IndexOf(_settings.LastConnectionType);
        _cmbConnection.SelectedIndexChanged += (_, _) => ShowILapFields();

        // I-Lap: its COM port and baud rate, saved to the same settings the
        // Connection page reads. Nothing connects from here - the port is
        // only remembered, so Connect finds it chosen.
        _lblSerialPort.Text = "COM port:";
        _lblSerialPort.Location = new Point(0, 52);
        _lblSerialPort.AutoSize = true;

        _cmbSerialPort.Location = new Point(96, 49);
        _cmbSerialPort.Width = 130;
        _cmbSerialPort.DropDownStyle = ComboBoxStyle.DropDownList;

        _btnRescan.Text = "Rescan";
        _btnRescan.Location = new Point(238, 47);
        _btnRescan.Width = 100;
        _btnRescan.Click += (_, _) => FillSerialPorts();

        _lblBaud.Text = "Baud:";
        _lblBaud.Location = new Point(0, 92);
        _lblBaud.AutoSize = true;

        _cmbBaud.Location = new Point(96, 89);
        _cmbBaud.Width = 130;
        _cmbBaud.DropDownStyle = ComboBoxStyle.DropDownList;

        foreach (var rate in ILapClient.CommonBaudRates)
        {
            _cmbBaud.Items.Add(rate);
        }

        // A rate saved earlier that is not one of the usual ones is kept,
        // as the Connection page does, rather than quietly replaced.
        if (_settings.ILapBaud > 0 && !ILapClient.CommonBaudRates.Contains(_settings.ILapBaud))
        {
            _cmbBaud.Items.Add(_settings.ILapBaud);
        }

        _cmbBaud.SelectedItem = _settings.ILapBaud > 0 ? _settings.ILapBaud : ILapClient.CommonBaudRates[0];

        _lblTimingHint.AutoSize = true;
        _lblTimingHint.MaximumSize = new Size(ContentWidth, 0);
        _lblTimingHint.Font = AppTheme.SmallFont;
    }

    private void BuildTrack()
    {
        _txtTrackName.Location = new Point(0, 4);
        _txtTrackName.Width = 340;
        _txtTrackName.PlaceholderText = "e.g. Garage Oval, Shop Layout A";
        _txtTrackName.Text = _settings.LastTrackLayoutName;

        _lblTrackHint.Location = new Point(0, 52);
        _lblTrackHint.AutoSize = true;
        _lblTrackHint.MaximumSize = new Size(ContentWidth, 0);
        _lblTrackHint.Font = AppTheme.SmallFont;
    }

    private void BuildClasses()
    {
        _txtClasses.Location = new Point(0, 0);
        _txtClasses.Size = new Size(260, 150);
        _txtClasses.Multiline = true;
        _txtClasses.AcceptsReturn = true;
        _txtClasses.ScrollBars = ScrollBars.Vertical;
        _txtClasses.PlaceholderText = "One class per line";

        _lblChipsCaption.Text = "Quick add:";
        _lblChipsCaption.AutoSize = true;
        _lblChipsCaption.Location = new Point(280, 0);

        _classChips.Location = new Point(280, 28);
        _classChips.Size = new Size(ContentWidth - 280, 130);
        _classChips.FlowDirection = FlowDirection.LeftToRight;
        _classChips.WrapContents = true;

        foreach (var name in SuggestedClasses)
        {
            var chip = new Button { Text = name, Width = 104, Margin = new Padding(0, 0, 8, 8) };
            chip.Click += (_, _) => AddClassLine(name);
            _classChips.Controls.Add(chip);
        }

        _lblClassesHint.Location = new Point(0, 168);
        _lblClassesHint.AutoSize = true;
        _lblClassesHint.MaximumSize = new Size(ContentWidth, 0);
        _lblClassesHint.Font = AppTheme.SmallFont;
    }

    private void BuildDrivers()
    {
        _lblNameHeader.Text = "Driver name";
        _lblNameHeader.AutoSize = true;
        _lblNameHeader.Location = new Point(0, 0);
        _lblNameHeader.Font = AppTheme.SmallFont;

        _lblTransponderHeader.Text = "Transponder number";
        _lblTransponderHeader.AutoSize = true;
        _lblTransponderHeader.Location = new Point(270, 0);
        _lblTransponderHeader.Font = AppTheme.SmallFont;

        for (var i = 0; i < DriverRows; i++)
        {
            _txtDriverNames[i] = new TextBox
            {
                Location = new Point(0, 24 + i * 36),
                Width = 256,
                PlaceholderText = i == 0 ? "e.g. Sam Carter" : string.Empty,
            };

            _txtTransponders[i] = new TextBox
            {
                Location = new Point(270, 24 + i * 36),
                Width = 180,
                PlaceholderText = i == 0 ? "e.g. 8467BE" : string.Empty,
            };
        }

        var bottom = 24 + DriverRows * 36;

        _lblDriverClass.Text = "Class:";
        _lblDriverClass.AutoSize = true;
        _lblDriverClass.Location = new Point(0, bottom + 8);

        _cmbDriverClass.Location = new Point(56, bottom + 4);
        _cmbDriverClass.Width = 200;
        _cmbDriverClass.DropDownStyle = ComboBoxStyle.DropDownList;

        _lblDriversHint.Location = new Point(270, bottom + 8);
        _lblDriversHint.AutoSize = true;
        _lblDriversHint.MaximumSize = new Size(ContentWidth - 270, 0);
        _lblDriversHint.Font = AppTheme.SmallFont;
        _lblDriversHint.Text = "Rows left blank are skipped.";
    }

    private void BuildDone()
    {
        _lblSummary.Location = new Point(0, 0);
        _lblSummary.AutoSize = true;
        _lblSummary.MaximumSize = new Size(ContentWidth, 0);

        _chkTour.Text = "Show me around the app now (about two minutes)";
        _chkTour.AutoSize = true;
        _chkTour.Checked = true;
        _chkTour.Font = AppTheme.BoldFont;
    }

    // --- Steps -----------------------------------------------------------

    private void ShowStep(int step)
    {
        _step = Math.Clamp(step, 0, StepNames.Length - 1);
        _content.Controls.Clear();

        switch (_step)
        {
            case 0:
                _lblStepTitle.Text = "Welcome to HeatCaller";
                _lblStepBody.Text =
                    "HeatCaller runs an RC race night from one laptop: it reads your timing decoder, runs the race, "
                    + "calls it out loud, and keeps every result.\r\n\r\n"
                    + "This setup takes about three minutes. Every step can be skipped, and everything here can be "
                    + "changed later from the sidebar.";
                _content.Controls.Add(_lblWelcomeRestore);
                _content.Controls.Add(_btnRestore);
                break;

            case 1:
                _lblStepTitle.Text = "Pick a look";
                _lblStepBody.Text =
                    "How the app looks on this laptop. The screens and web pages your racers see can have a look of "
                    + "their own - set that, and your club logo, on the Themes page later.";
                _content.Controls.AddRange(new Control[] { _lstLooks, _lookPreview, _lblLookDescription, _chk24Hour });
                break;

            case 2:
                _lblStepTitle.Text = "Your timing decoder";
                _lblStepBody.Text =
                    "HeatCaller reads the decoder directly and runs race control itself. Pick the one you have - "
                    + "you connect to it on the Connection page when you're ready to race.";
                _content.Controls.AddRange(new Control[]
                {
                    _cmbConnection, _lblSerialPort, _cmbSerialPort, _btnRescan, _lblBaud, _cmbBaud, _lblTimingHint,
                });
                ShowILapFields();
                break;

            case 3:
                _lblStepTitle.Text = "Name your track";
                _lblStepBody.Text =
                    "Each track layout keeps its own lap record and average pace, so a rebuild doesn't mix up the "
                    + "records. Give today's layout a name you'll recognise.";
                _lblTrackHint.Text = _trackLayouts.All.Count > 0
                    ? $"Already set up: {string.Join(", ", _trackLayouts.All.Select(t => t.Name).Take(6))}. "
                      + "Add photos, marshal posts and more on the Track Layouts page."
                    : "Add a photo of the layout, marshal posts and more on the Track Layouts page later.";
                _content.Controls.Add(_txtTrackName);
                _content.Controls.Add(_lblTrackHint);
                break;

            case 4:
                _lblStepTitle.Text = "Your racing classes";
                _lblStepBody.Text =
                    "Classes group drivers into heats and standings. If everyone races together, a single class "
                    + "such as \"Open\" is all you need.";
                _lblClassesHint.Text = _raceClasses.All.Count > 0
                    ? $"Already set up: {string.Join(", ", _raceClasses.All.Select(c => c.Name).Take(8))}. "
                      + "Race formats, lap limits and rules are on the Classes page."
                    : "Race formats, lap limits and rules for each class are on the Classes page.";
                _content.Controls.AddRange(new Control[] { _txtClasses, _lblChipsCaption, _classChips, _lblClassesHint });
                break;

            case 5:
                _lblStepTitle.Text = "Add a few drivers";
                _lblStepBody.Text =
                    "Enter anyone you know now, or skip this: when a transponder the app doesn't know crosses the "
                    + "line, it asks you who it is. The number is printed on the transponder.";
                FillDriverClassChoices();
                _content.Controls.Add(_lblNameHeader);
                _content.Controls.Add(_lblTransponderHeader);

                for (var i = 0; i < DriverRows; i++)
                {
                    _content.Controls.Add(_txtDriverNames[i]);
                    _content.Controls.Add(_txtTransponders[i]);
                }

                _content.Controls.AddRange(new Control[] { _lblDriverClass, _cmbDriverClass, _lblDriversHint });
                break;

            case 6:
                _lblStepTitle.Text = "You're all set";
                _lblStepBody.Text = "Here's what's ready. Everything can be changed later from the sidebar.";
                _lblSummary.Text = BuildSummary();
                _content.Controls.Add(_lblSummary);
                _content.Controls.Add(_chkTour);
                break;
        }

        var last = _step == StepNames.Length - 1;

        _btnBack.Visible = _step > 0;
        _btnSkipAll.Visible = !last;
        _btnNext.Text = last ? "Finish" : (_step == 0 ? "Get started" : "Next");
        _lblProgress.Text = _step == 0 || last ? string.Empty : $"Step {_step} of {StepNames.Length - 2}";

        AppTheme.ApplyRecursively(_content);
        ApplyFrameColours();

        if (last)
        {
            // Placed after the theme pass, which can change the summary's
            // font and so its height.
            _chkTour.Location = new Point(0, _lblSummary.Bottom + 24);
        }

        // Enter moves on - except in the classes box, where it starts a
        // new line.
        AcceptButton = _step == 4 ? null : _btnNext;
        _btnNext.Focus();
    }

    /// <summary>
    /// The colours the theme pass would otherwise flatten: the rail keeps
    /// the sidebar's, the title the accent, hints the muted text, and the
    /// buttons their kinds.
    /// </summary>
    private void ApplyFrameColours()
    {
        _rail.BackColor = AppTheme.SidebarBackground;
        _lblBrand.BackColor = AppTheme.SidebarBackground;
        _lblBrand.ForeColor = AppTheme.Primary;
        _lblRailCaption.BackColor = AppTheme.SidebarBackground;
        _lblRailCaption.ForeColor = AppTheme.SidebarTextMuted;

        for (var i = 0; i < _railLabels.Length; i++)
        {
            var current = i == _step;
            _railLabels[i].BackColor = current ? AppTheme.SidebarHover : AppTheme.SidebarBackground;
            _railLabels[i].ForeColor = current ? AppTheme.Primary : AppTheme.SidebarText;
            _railLabels[i].Font = current ? AppTheme.BoldFont : AppTheme.BodyFont;
        }

        _lblStepTitle.ForeColor = AppTheme.Primary;
        _lblStepBody.ForeColor = AppTheme.TextDark;
        _lblProgress.ForeColor = AppTheme.TextMuted;

        foreach (var hint in new[] { _lblWelcomeRestore, _lblLookDescription, _lblTimingHint, _lblTrackHint, _lblClassesHint, _lblDriversHint, _lblNameHeader, _lblTransponderHeader })
        {
            hint.ForeColor = AppTheme.TextMuted;
        }

        AppTheme.StyleButton(_btnNext, AppTheme.ButtonKind.Primary);
        AppTheme.StyleButton(_btnBack, AppTheme.ButtonKind.Secondary);
        AppTheme.StyleButton(_btnSkipAll, AppTheme.ButtonKind.Secondary);
        AppTheme.StyleButton(_btnRestore, AppTheme.ButtonKind.Secondary);
        AppTheme.StyleButton(_btnRescan, AppTheme.ButtonKind.Secondary);

        foreach (Control chip in _classChips.Controls)
        {
            if (chip is Button button)
            {
                AppTheme.StyleButton(button, AppTheme.ButtonKind.Secondary);
            }
        }
    }

    private void NextClicked()
    {
        SaveCurrentStep();

        if (_step < StepNames.Length - 1)
        {
            ShowStep(_step + 1);
            return;
        }

        Finish(_chkTour.Checked);
    }

    private void SkipEverything()
    {
        // What was already typed on this step is kept - skipping the rest
        // is not the same as throwing this one away.
        SaveCurrentStep();
        Finish(takeTour: false);
    }

    private void Finish(bool takeTour)
    {
        TakeTour = takeTour;
        _settings.HasCompletedFirstRun = true;
        _settings.Save();
        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>Commits the step being left. Safe to call more than once for the same step.</summary>
    private void SaveCurrentStep()
    {
        switch (_step)
        {
            case 1:
                SaveLook();
                break;
            case 2:
                SaveTiming();
                break;
            case 3:
                SaveTrack();
                break;
            case 4:
                SaveClasses();
                break;
            case 5:
                SaveDrivers();
                break;
        }
    }

    private void SaveLook()
    {
        _settings.Use24HourTime = _chk24Hour.Checked;
        AppTime.Use24Hour = _chk24Hour.Checked;

        if (SelectedLook is { } look)
        {
            _settings.AppLook = look.Key;

            // The family as the old Light/Dark/Synthwave setting too - the
            // same pairing LooksControl saves.
            _settings.ThemeMode = look.Mode switch
            {
                AppTheme.Mode.Dark => 1,
                AppTheme.Mode.Synthwave => 2,
                _ => 0,
            };

            RestartForLook = !string.Equals(look.Key, _startingLookKey, StringComparison.OrdinalIgnoreCase);
            _doneLook = RestartForLook ? $"{look.Name} (the app restarts to apply it)" : look.Name;
        }

        _settings.Save();
    }

    private void SaveTiming()
    {
        // The saved number, not the picker position - see DecoderConnection.
        _settings.LastConnectionType = DecoderConnection.ValueAt(_cmbConnection.SelectedIndex);
        _doneTiming = _cmbConnection.SelectedItem?.ToString();

        if (_settings.LastConnectionType == DecoderConnection.ILap)
        {
            // Only a port that was actually chosen. None listed means the
            // decoder is not plugged in yet, and whatever was remembered
            // before is better kept than cleared.
            if (_cmbSerialPort.SelectedItem is string port)
            {
                _settings.ILapPort = port;
                _doneTiming += $" on {port}";
            }

            if (_cmbBaud.SelectedItem is int baud)
            {
                _settings.ILapBaud = baud;
            }
        }

        _settings.Save();
    }

    private void SaveTrack()
    {
        var trackName = _txtTrackName.Text.Trim();
        if (trackName.Length == 0)
        {
            return;
        }

        if (!_trackLayouts.All.Any(t => string.Equals(t.Name, trackName, StringComparison.OrdinalIgnoreCase)))
        {
            _trackLayouts.Upsert(new TrackLayout { Name = trackName });
        }

        _settings.LastTrackLayoutName = trackName;
        _settings.Save();
        _doneTrack = trackName;
    }

    private void SaveClasses()
    {
        foreach (var name in TypedClasses())
        {
            if (!_raceClasses.All.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                _raceClasses.Upsert(new RaceClass { Name = name });
            }

            if (!_doneClasses.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                _doneClasses.Add(name);
            }
        }
    }

    private void SaveDrivers()
    {
        var className = _cmbDriverClass.SelectedItem as string ?? string.Empty;

        for (var i = 0; i < DriverRows; i++)
        {
            var name = _txtDriverNames[i].Text.Trim();
            var transponder = _txtTransponders[i].Text.Trim();

            if (name.Length == 0 && transponder.Length == 0)
            {
                continue;
            }

            // A name with no number (or the other way round) cannot be
            // matched to a crossing, and a number already on file belongs
            // to someone - neither is saved, and the summary says so. The
            // row is left filled in, so going Back shows what to fix.
            if (name.Length == 0 || transponder.Length == 0 || _profiles.Get(transponder) is not null)
            {
                _skippedDrivers++;
                continue;
            }

            _profiles.Upsert(new DriverProfile
            {
                Transponder = transponder,
                DriverName = name,
                ClassName = className,
            });

            _doneDrivers++;

            // Cleared once saved, so going Back and Next again cannot count
            // (or refuse) the same driver twice.
            _txtDriverNames[i].Text = string.Empty;
            _txtTransponders[i].Text = string.Empty;
        }
    }

    // --- Helpers ---------------------------------------------------------

    private Look? SelectedLook => (_lstLooks.SelectedItem as LookItem)?.Look;

    private IEnumerable<string> TypedClasses() =>
        _txtClasses.Lines
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private void AddClassLine(string name)
    {
        if (TypedClasses().Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var text = _txtClasses.Text.TrimEnd();
        _txtClasses.Text = text.Length == 0 ? name : text + Environment.NewLine + name;
    }

    private void FillDriverClassChoices()
    {
        var previous = _cmbDriverClass.SelectedItem as string;
        var names = _raceClasses.All.Select(c => c.Name)
            .Concat(TypedClasses())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _cmbDriverClass.Items.Clear();
        _cmbDriverClass.Items.Add(string.Empty);

        foreach (var name in names)
        {
            _cmbDriverClass.Items.Add(name);
        }

        var wanted = !string.IsNullOrEmpty(previous) ? previous : _doneClasses.FirstOrDefault() ?? names.FirstOrDefault() ?? string.Empty;
        _cmbDriverClass.SelectedItem = _cmbDriverClass.Items.Contains(wanted) ? wanted : string.Empty;
        _cmbDriverClass.Enabled = names.Count > 0;
    }

    /// <summary>Shows the COM port and baud rows only while I-Lap is the chosen connection.</summary>
    private void ShowILapFields()
    {
        var isILap = DecoderConnection.ValueAt(_cmbConnection.SelectedIndex) == DecoderConnection.ILap;

        foreach (var control in new Control[] { _lblSerialPort, _cmbSerialPort, _btnRescan, _lblBaud, _cmbBaud })
        {
            control.Visible = isILap;
        }

        if (isILap && _cmbSerialPort.Items.Count == 0)
        {
            FillSerialPorts();
        }

        _lblTimingHint.Location = new Point(0, isILap ? 136 : 52);
        _lblTimingHint.Text = isILap
            ? "Leave the baud rate at 9600 unless the decoder has been set differently. No port listed? Plug the "
              + "decoder in and press Rescan - or pick it later on the Connection page.\r\n\r\n"
              + "No decoder here yet? You can still try everything with a simulated race in Demo Mode."
            : "Plug the EasyLap in by USB before you connect - there is nothing else to set.\r\n\r\n"
              + "No decoder here yet? You can still try everything with a simulated race in Demo Mode.";
    }

    /// <summary>Lists the COM ports present now, the remembered one selected - the same rule as the Connection page.</summary>
    private void FillSerialPorts()
    {
        var ports = ILapClient.AvailablePorts();

        _cmbSerialPort.Items.Clear();
        _cmbSerialPort.Items.AddRange(ports);

        if (ports.Length == 0)
        {
            return;
        }

        var remembered = Array.FindIndex(ports, p => string.Equals(p, _settings.ILapPort, StringComparison.OrdinalIgnoreCase));
        _cmbSerialPort.SelectedIndex = remembered >= 0 ? remembered : ports.Length - 1;
    }

    private string BuildSummary()
    {
        var decoder = _doneTiming ?? DecoderConnection.Items()[DecoderConnection.IndexOf(_settings.LastConnectionType)].ToString();
        var track = _doneTrack
                    ?? (_settings.LastTrackLayoutName.Length > 0 ? _settings.LastTrackLayoutName : "none yet - add one on Track Layouts");
        var classes = _doneClasses.Count > 0
            ? string.Join(", ", _doneClasses)
            : _raceClasses.All.Count > 0 ? $"{_raceClasses.All.Count} already set up" : "none yet - add them on Classes";
        var drivers = _doneDrivers > 0
            ? $"{_doneDrivers} added"
            : "none yet - they can be added as they cross the line";

        var lines = new List<string>
        {
            $"Look:   {_doneLook ?? Looks.ForApp(_settings).Name}",
            $"Decoder:   {decoder}",
            $"Track:   {track}",
            $"Classes:   {classes}",
            $"Drivers:   {drivers}",
        };

        if (_skippedDrivers > 0)
        {
            lines.Add($"      {_skippedDrivers} driver row(s) weren't saved - each needs a name and a transponder number "
                      + "that isn't already on file. Go Back to fix them.");
        }

        lines.Add(string.Empty);

        // The route a club actually takes on the night, and the reason this
        // is not the old one-liner.
        //
        // It used to say "open Connection and press Connect, then run it
        // from Race Control" - which works, and teaches a club to run the
        // whole evening without ever opening Race Day. The heats never get
        // built, nothing is filed against a day, and qualifying has no
        // boundary. The page that plans a night was reachable only by
        // clicking the sidebar out of curiosity.
        lines.Add("On race night: open Connection and press Connect, then go to Race Day.");
        lines.Add("Its Next Steps tab says the one next thing to do and has the button that does it.");
        lines.Add("No decoder here yet? Open Demo Mode and start a simulated race.");

        return string.Join(Environment.NewLine, lines);
    }

    private static void PaintLookPreview(Graphics g, Rectangle bounds, Look? look)
    {
        if (look is null)
        {
            return;
        }

        static Color Pick(string? hex, Color fallback)
        {
            try
            {
                return string.IsNullOrWhiteSpace(hex) ? fallback : ColorTranslator.FromHtml(hex);
            }
            catch
            {
                return fallback;
            }
        }

        var c = look.Colors;
        var bg = Pick(c.Bg, Color.Black);
        var surface = Pick(c.Surface, bg);
        var sidebar = Pick(c.Sidebar, bg);
        var primary = Pick(c.Primary, Color.OrangeRed);
        var accent = Pick(c.Accent, primary);
        var text = Pick(c.Text, Color.White);
        var muted = Pick(c.Muted, Color.Gray);

        using (var b = new SolidBrush(bg))
        {
            g.FillRectangle(b, bounds);
        }

        // A tiny window: a sidebar, a card with a heading, and two buttons.
        var side = new Rectangle(bounds.Left, bounds.Top, 60, bounds.Height);
        using (var b = new SolidBrush(sidebar))
        {
            g.FillRectangle(b, side);
        }

        for (var i = 0; i < 5; i++)
        {
            using var b = new SolidBrush(i == 0 ? primary : muted);
            g.FillRectangle(b, side.Left + 10, side.Top + 14 + i * 18, i == 0 ? 38 : 30, 6);
        }

        var card = new Rectangle(side.Right + 14, bounds.Top + 14, bounds.Width - side.Width - 28, bounds.Height - 28);

        void Fill(Color colour, int x, int y, int w, int h)
        {
            using var b = new SolidBrush(colour);
            g.FillRectangle(b, x, y, w, h);
        }

        Fill(surface, card.Left, card.Top, card.Width, card.Height);
        Fill(text, card.Left + 12, card.Top + 14, 110, 8);
        Fill(muted, card.Left + 12, card.Top + 30, Math.Min(150, card.Width - 24), 5);
        Fill(muted, card.Left + 12, card.Top + 40, Math.Min(130, card.Width - 24), 5);
        Fill(primary, card.Left + 12, card.Bottom - 30, 70, 18);
        Fill(accent, card.Left + 92, card.Bottom - 30, 40, 18);

        using var edge = new Pen(Color.FromArgb(80, text));
        g.DrawRectangle(edge, bounds.Left, bounds.Top, bounds.Width - 1, bounds.Height - 1);
    }

    private sealed class LookItem
    {
        public LookItem(Look look) => Look = look;

        public Look Look { get; }

        public override string ToString() => Look.Name;
    }
}
