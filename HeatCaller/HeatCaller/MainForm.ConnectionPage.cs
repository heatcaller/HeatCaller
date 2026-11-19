namespace HeatCaller;

/// <summary>
/// The Connection page, on the layout pieces (LayoutPieces.cs): a line
/// saying what the page is for, the timing decoder's card - which decoder,
/// connecting to it, its own settings and its status - and the developer
/// console under it, taking the height that is left.
///
/// It was the one page whose controls were placed absolutely: two group
/// boxes at fixed positions, the first row measured from a caption in bold,
/// I-Lap's rows sharing the space of EasyLap's tick, and the console's
/// filter bar run left to right from fixed starting points with Copy and
/// Clear pinned to the right-hand edge. The layout test found things
/// overlapping or cut off on it at every size it tries, and more the larger
/// the text. Nothing here is placed by hand now: a short window scrolls the
/// page, and the console never goes below six lines.
///
/// What the controls DO is in MainForm.Connection (the decoder) and
/// MainForm.Console (the log); this only builds the page they sit on.
/// </summary>
public partial class MainForm
{
    /// <summary>The decoder picker's width at the normal text size - it is never narrower, and is as wide as its longest choice.</summary>
    private const int ConnectionPickerWidth = 280;

    /// <summary>The COM port and baud pickers' width at the normal text size.</summary>
    private const int SerialPickerWidth = 130;

    /// <summary>The console filter's box at the normal text size.</summary>
    private const int ConsoleFilterWidth = 180;

    /// <summary>The console filter's presets at the normal text size.</summary>
    private const int ConsolePresetWidth = 150;

    /// <summary>The console's type size at the normal text size.</summary>
    private const float ConsoleFontSize = 9F;

    /// <summary>The fewest lines of the console the page keeps in view - below that it scrolls instead.</summary>
    private const int ConsoleLeastLines = 6;

    /// <summary>What the Connection page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _connectionTextSize = new();

    /// <summary>
    /// The COM port picker and its Rescan button - I-Lap's row, hidden with
    /// its caption for any other decoder. The row is hidden rather than what
    /// is in it: an empty row would still keep its place in the card. See
    /// UpdateConnectionModeUi.
    /// </summary>
    private FlowLayoutPanel serialPortRow = null!;

    /// <summary>The pickers' widths as last measured - see Pieces.PickerWidth. The port and baud ones are forgotten when their lists are refilled.</summary>
    private (Font Font, int Count, int Width)? _connectionPickerWidth;
    private (Font Font, int Count, int Width)? _serialPickerWidth;
    private (Font Font, int Count, int Width)? _baudPickerWidth;
    private (Font Font, int Count, int Width)? _consolePresetWidth;
    private (Font Font, int Count, int Width)? _splitPickerWidth;
    private (Font Font, int Count, int Width)? _streamSourcePickerWidth;

    /// <summary>The text size the console's type was last made for - 0 before it has been.</summary>
    private float _consoleFontScale;

    /// <summary>
    /// Builds the page. Called from InitializeComponent, where the page's
    /// controls are made: the cards take the theme's surface colour as they
    /// are made, so not before the look is chosen. InitializeComponent runs
    /// SizeConnectionFields again once the page is in the window, whose font
    /// the pickers are measured in.
    /// </summary>
    /// <summary>"Connect when HeatCaller starts" - AppSettings.ConnectDecodersAtStartup. Here rather than in the Designer file, which is not touched.</summary>
    private readonly CheckBox chkConnectAtStartup = new ThemedCheckBox();

    private void BuildConnectionPage()
    {
        // --- The decoder ---
        cmbConnectionType.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbConnectionType.Items.AddRange(DecoderConnection.Items());
        cmbConnectionType.SelectedIndex = 0;
        cmbConnectionType.SelectedIndexChanged += CmbConnectionType_SelectedIndexChanged;

        // On the first row, after the picker: pick, then connect.
        btnConnect.Text = "Connect";
        btnConnect.Click += BtnConnect_Click;

        btnDisconnect.Text = "Disconnect";
        btnDisconnect.Enabled = false;
        btnDisconnect.Click += BtnDisconnect_Click;

        // I-Lap's port and baud rate, each on a row of its own. Filled in
        // MainForm.Connection, where the settings are read.
        cmbSerialPort.DropDownStyle = ComboBoxStyle.DropDownList;

        btnRefreshPorts.Text = "Rescan";
        btnRefreshPorts.Click += BtnRefreshPorts_Click;

        cmbBaud.DropDownStyle = ComboBoxStyle.DropDownList;

        // EasyLap's alone: a serial port that fails to open should say so
        // once, not every few seconds.
        chkAutoConnect.Text = "Auto-connect (retries every few seconds until the decoder is found)";
        chkAutoConnect.AutoSize = true;
        chkAutoConnect.CheckedChanged += ChkAutoConnect_CheckedChanged;

        // The light and the line beside it - see RenderDecoderStatus.
        pnlStatusDot.BackColor = Color.Gray;
        lblStatus.Text = "Disconnected";

        // No caption on the card used to read CONNECTION, under the page's
        // own heading saying the same thing. This one names what the card
        // is about instead.
        //
        // 17 Nov 2026 (page jazz-up): the one card that held everything is
        // four now, each with a line saying what it is for - the decoder
        // itself, how it starts up, the tools for a race, and the split
        // loop - with the page's state in tiles over them.
        var decoder = new Section("Timing decoder") { Name = "Timing decoder" };
        decoder.Add(ConnectionHint("Pick the decoder at your track and press Connect. The light turns green once HeatCaller is talking to it."));
        var grid = decoder.Add(new FieldGrid(), gapBelow: 0);

        grid.AddRow("Connect via:", FieldGrid.Inline(cmbConnectionType, btnConnect, btnDisconnect));

        // The decoders from 3 up: where each is reached - see MainForm.StreamDecoders.
        BuildStreamDecoderRows(grid);

        serialPortRow = FieldGrid.Inline(cmbSerialPort, btnRefreshPorts);
        lblSerialPort = grid.AddRow("COM port:", serialPortRow);
        lblBaud = grid.AddRow("Baud:", cmbBaud);

        grid.AddRow("Status:", new StatusLine(pnlStatusDot, lblStatus), stretch: true);

        // --- Start-up options ---
        var startUp = new Section("Start-up options") { Name = "Start-up options" };
        startUp.Add(ConnectionHint("How the decoder connects as HeatCaller opens, and what is kept of the passes it hears."));
        var options = startUp.Add(new FieldGrid(), gapBelow: 0);

        // Whichever decoder: the line (and the split loop with it) connects
        // as HeatCaller starts. Set from the settings in the constructor.
        chkConnectAtStartup.Text = "Connect when HeatCaller starts";
        chkConnectAtStartup.AutoSize = true;
        options.AddField(chkConnectAtStartup);

        options.AddField(chkAutoConnect);

        // WHY (23 Oct 2026, timing layer, P4): the switch for the timing
        // thread, beside the decoder it is about. Read once at start, so the
        // hint says when a change counts. Set from the settings in the
        // constructor, where it is also saved.
        chkTimingThread.Text = "Run timing on its own thread";
        chkTimingThread.AutoSize = true;
        options.AddField(chkTimingThread);
        options.AddHint("Takes effect the next time HeatCaller starts.");

        // WHY (24 Oct 2026): pass recording was a settings-file switch only.
        // Beside the timing thread, because both are about timing; it starts
        // and stops at once (see MainForm's handler).
        chkRecordPasses.Text = "Record every pass to a file";
        chkRecordPasses.AutoSize = true;
        options.AddField(chkRecordPasses);
        options.AddHint("For checking timing afterwards. One file per run in this PC's timing-recordings folder, kept 30 days.");

        // --- Race tools ---
        var tools = new Section("Race tools") { Name = "Race tools" };
        tools.Add(ConnectionHint("Every check before a green flag in one window, and the webcam photo finish."));
        var toolGrid = tools.Add(new FieldGrid(), gapBelow: 0);

        // Every check before a green flag at once - the decoder, the cars
        // heard, timing, the website, the sound. See MissionControl.
        var btnMissionControl = new Button { Text = "Mission Control..." };
        btnMissionControl.Click += (_, _) => ShowMissionControl();
        toolGrid.AddRow("Before a race:", FieldGrid.Inline(btnMissionControl));

        // The webcam photo finish: the camera page in this PC's browser, and
        // the pictures it keeps - see PhotoFinish.
        var btnCamera = new Button { Text = "Open Camera Page" };
        btnCamera.Click += (_, _) => OpenCameraPage();
        var btnPhotoFinish = new Button { Text = "Photo Finish..." };
        btnPhotoFinish.Click += (_, _) => ShowPhotoFinish();
        toolGrid.AddRow("Finish camera:", FieldGrid.Inline(btnCamera, btnPhotoFinish));

        // --- Split loop ---
        //
        // The split loop: the other decoder, partway round the track - see
        // MainForm.SplitLoop. Its light and line show only while it is on.
        var split = new Section("Split loop") { Name = "Split loop" };
        split.Add(ConnectionHint("A second decoder partway round the track, for sector times. Line Up matches its clock to the line's."));
        var splitGrid = split.Add(new FieldGrid(), gapBelow: 0);

        cmbSplitLoop.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbSplitLoop.SelectedIndexChanged += CmbSplitLoop_SelectedIndexChanged;

        btnConnectSplit.Text = "Connect Split";
        btnConnectSplit.Click += (_, _) => ConnectSplitLoop(userChose: true);

        btnLineUp.Text = "Line Up...";
        btnLineUp.Click += BtnLineUp_Click;

        pnlSplitDot.BackColor = Color.Gray;
        lblSplitStatus.Text = "Not connected.";

        splitGrid.AddRow("Split loop:", FieldGrid.Inline(cmbSplitLoop, btnConnectSplit, btnLineUp));
        splitStatusLine = new StatusLine(pnlSplitDot, lblSplitStatus);
        lblSplitStatusCaption = splitGrid.AddRow("Split status:", splitStatusLine, stretch: true);
        lblSplitStatusCaption.Visible = false;
        splitStatusLine.Visible = false;
        btnConnectSplit.Visible = false;
        btnLineUp.Visible = false;

        // The decoder's card and the split loop's run the page's width, one
        // under the other: one caption column for both, so their pickers
        // line up.
        _connectionTextSize.Keep(() => FieldGrid.LineUp(new[] { grid, splitGrid }));

        // Only the chosen decoder's rows show - UpdateConnectionModeUi,
        // which runs as the window is set up. EasyLap is the first choice.
        lblSerialPort.Visible = false;
        serialPortRow.Visible = false;
        lblBaud.Visible = false;
        cmbBaud.Visible = false;

        // --- The console ---
        //
        // Every connection type logs here (EasyLap, I-Lap, audio timing and
        // the rest), and so does the app itself - naming one decoder sent
        // people looking elsewhere for the others.
        txtConsole.Multiline = true;
        txtConsole.ReadOnly = true;
        txtConsole.ScrollBars = ScrollBars.Vertical;
        txtConsole.WordWrap = false;

        var consoleHint = new Label
        {
            Text = "Messages from the timing connection and the app's own log. A filter only hides lines - clear it and they are all back.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        // Filtering. Lines are kept in a full buffer and re-rendered, so
        // filtering never discards anything - you can narrow to one tag,
        // read it, and widen again without having lost the rest. See
        // MainForm.Console.
        txtConsoleFilter.PlaceholderText = "text to match";

        cmbConsolePreset.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbConsolePreset.Items.AddRange(new object[]
        {
            "All", "Audio timing", "Voice", "Sound", "Race", "Decoder", "Storage", "Hide raw HID",
        });
        cmbConsolePreset.SelectedIndex = 0;

        chkFilterExclude.Text = "Exclude matches";
        chkFilterExclude.AutoSize = true;

        chkAutoScroll.Text = "Auto-scroll";
        chkAutoScroll.Checked = true;
        chkAutoScroll.AutoSize = true;

        // One line while there is room for it, onto a second when there is
        // not - rather than "Filter:" sitting on its box and "Exclude
        // matches" running under the Auto-scroll tick, as the row of fixed
        // columns did at the larger text size.
        var filterRow = new FieldRow();

        filterRow.Add(new Label { Text = "Filter:" }, txtConsoleFilter, ConsoleFilterBoxWidth);
        filterRow.Add(null, cmbConsolePreset, ConsolePresetPickerWidth);
        filterRow.Add(null, chkFilterExclude);
        filterRow.Add(null, chkAutoScroll);

        btnCopyConsole.Text = "Copy";
        btnClearConsole.Text = "Clear";

        var console = new Section("Developer console") { Name = "Developer console" };

        console.Add(consoleHint);
        console.Add(filterRow);
        console.AddFill(txtConsole, ConsoleLeastHeight);
        console.Add(new ButtonBar().With(btnCopyConsole, btnClearConsole), gapBelow: 0);

        // --- The page ---
        var intro = new Label
        {
            Text = "Choose your timing decoder and connect to it. Its laps feed the Race Control page, which runs the race. "
                   + "With two decoders, the other one can be a split loop partway round the track.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        // The split status row follows the split loop's own switch
        // (RenderSplitStatus). The split loop has a card of its own (17 Nov
        // 2026).

        // The start-up ticks and the race tools side by side on a wide
        // page, one over the other on a narrow one or at a large text size.
        var middle = new CardColumns { TwoColumnsFrom = () => UiLayout.AtTextSize(900) };
        middle.AddLeft(startUp);
        middle.AddRight(tools);

        // --- The tiles: the page at a glance ---
        BuildConnectionTiles();

        var page = new FillPage();

        page.Add(intro);
        page.Add(new TileStrip(_connectionTiles));
        page.Add(decoder);
        page.Add(middle);
        page.Add(split);
        page.AddFill(console, console.LeastHeight, gapBelow: 0);

        // The hint's face and the fields' widths follow the text size. The
        // page wears the window's font, so a new text size reaches it as a
        // font change (ApplyFontScale sets the window's).
        _connectionTextSize.Wear(consoleHint, () => AppTheme.SmallFont);
        _connectionTextSize.Keep(SizeConnectionFields);
        page.FontChanged += (_, _) => _connectionTextSize.Apply();

        pageLiveSession.Controls.Add(page);
    }

    /// <summary>
    /// The fields' sizes at the text size in use: the pickers as wide as
    /// their longest choice and never narrower than they were at the normal
    /// text size, and the console's type at this size.
    ///
    /// Run again when the text size changes (TextSize.Keep), and when the
    /// port or baud list is refilled (RefreshSerialPorts, FillBaudRates),
    /// which can change the longest choice without changing how many there
    /// are.
    /// </summary>
    private void SizeConnectionFields()
    {
        FitWidth(cmbConnectionType, Math.Max(UiLayout.AtTextSize(ConnectionPickerWidth), Pieces.PickerWidth(cmbConnectionType, ref _connectionPickerWidth)));
        FitWidth(cmbSerialPort, Math.Max(UiLayout.AtTextSize(SerialPickerWidth), Pieces.PickerWidth(cmbSerialPort, ref _serialPickerWidth)));
        FitWidth(cmbBaud, Math.Max(UiLayout.AtTextSize(SerialPickerWidth), Pieces.PickerWidth(cmbBaud, ref _baudPickerWidth)));
        FitWidth(cmbSplitLoop, Math.Max(UiLayout.AtTextSize(ConnectionPickerWidth), Pieces.PickerWidth(cmbSplitLoop, ref _splitPickerWidth)));
        FitWidth(cmbStreamSource, Math.Max(UiLayout.AtTextSize(SerialPickerWidth), Pieces.PickerWidth(cmbStreamSource, ref _streamSourcePickerWidth)));

        // Its own face, which nothing else hands it: a monospaced log reads
        // in columns. Made again only for a new text size.
        if (_consoleFontScale != AppTheme.FontScale)
        {
            _consoleFontScale = AppTheme.FontScale;
            txtConsole.Font = new Font("Consolas", ConsoleFontSize * AppTheme.FontScale);
        }

        // Only when it changed: a new width lays the row out again.
        static void FitWidth(Control field, int width)
        {
            if (field.Width != width)
            {
                field.Width = width;
            }
        }
    }

    /// <summary>The console filter's box: wide enough to show its hint, and never under the 180px it had at the normal text size.</summary>
    private int ConsoleFilterBoxWidth() =>
        Math.Max(
            UiLayout.AtTextSize(ConsoleFilterWidth),
            TextRenderer.MeasureText(txtConsoleFilter.PlaceholderText, txtConsoleFilter.Font).Width + 16);

    /// <summary>The console filter's presets: as wide as the longest, and never under the 150px they had at the normal text size.</summary>
    private int ConsolePresetPickerWidth() =>
        Math.Max(UiLayout.AtTextSize(ConsolePresetWidth), Pieces.PickerWidth(cmbConsolePreset, ref _consolePresetWidth));

    /// <summary>The least the console can do with: six lines of its type. Below that the page scrolls instead - see FillPage.</summary>
    private int ConsoleLeastHeight() =>
        Pieces.LineHeight(txtConsole.Font) * ConsoleLeastLines + UiLayout.HintGap * 2;

    // --- The tiles (17 Nov 2026, page jazz-up) ---------------------------------------

    /// <summary>The page at a glance, over the cards: the decoder, its status, the split loop and the passes heard.</summary>
    private readonly StatTile[] _connectionTiles = { new(), new(), new(), new() };

    private static readonly string[] ConnectionTileCaptions = { "Decoder", "Status", "Split loop", "Passes heard" };

    /// <summary>The status tile's value, drawn as a chip in the state's colour.</summary>
    private ConnectionTileChip? _connectionStatusChip;

    /// <summary>The split loop tile's value, the same way.</summary>
    private ConnectionTileChip? _splitStatusChip;

    /// <summary>Line passes heard since HeatCaller opened - only ever shown, never kept.</summary>
    private long _connectionPassesHeard;

    /// <summary>A card's line saying what it is for, in the small muted type, following the text size.</summary>
    private Label ConnectionHint(string text) =>
        _connectionTextSize.Wear(new Label { Text = text, AutoSize = true, ForeColor = AppTheme.TextMuted }, () => AppTheme.SmallFont);

    /// <summary>
    /// The tiles' faces, and what keeps them up to date. They only watch:
    /// the light and line the decoder code already sets (RenderDecoderStatus,
    /// RenderSplitStatus), the pickers, and the passes as they are heard -
    /// so nothing about connecting changes for them.
    /// </summary>
    private void BuildConnectionTiles()
    {
        for (var i = 0; i < _connectionTiles.Length; i++)
        {
            _connectionTextSize.Wear(_connectionTiles[i].Value, () => AppTheme.TitleFont);
            _connectionTextSize.Wear(_connectionTiles[i].Caption, () => AppTheme.MicroFont);
            _connectionTiles[i].Set("-", ConnectionTileCaptions[i]);
        }

        _connectionStatusChip = new ConnectionTileChip(_connectionTiles[1]);
        _splitStatusChip = new ConnectionTileChip(_connectionTiles[2]);

        cmbConnectionType.SelectedIndexChanged += (_, _) => ShowConnectionTiles();
        cmbSplitLoop.SelectedIndexChanged += (_, _) => ShowConnectionTiles();
        lblStatus.TextChanged += (_, _) => ShowConnectionTiles();
        pnlStatusDot.BackColorChanged += (_, _) => ShowConnectionTiles();
        lblSplitStatus.TextChanged += (_, _) => ShowConnectionTiles();
        pnlSplitDot.BackColorChanged += (_, _) => ShowConnectionTiles();

        // On the UI thread, after the pass is routed (LinePassSeenOnUi);
        // only a number goes up here, and it cannot throw.
        LinePassSeen += _ =>
        {
            _connectionPassesHeard++;
            _connectionTiles[3].Set(_connectionPassesHeard.ToString("N0"), ConnectionTileCaptions[3]);
        };

        ShowConnectionTiles();
    }

    /// <summary>The tiles brought up to date from what the page shows now. UI thread.</summary>
    private void ShowConnectionTiles()
    {
        var line = cmbConnectionType.SelectedIndex >= 0 ? SelectedDecoder : 0;

        _connectionTiles[0].Set(line == 0 ? "-" : DecoderConnection.ShortName(line), ConnectionTileCaptions[0]);

        var (word, tone) = StateFromLight(pnlStatusDot.BackColor);
        _connectionTiles[1].Set(word, ConnectionTileCaptions[1]);

        if (_connectionStatusChip is not null)
        {
            _connectionStatusChip.Tone = tone;
        }

        // The split loop: off, or the other decoder and how it is doing.
        var other = OtherDecoder(line);
        var splitOn = _settings is not null && _settings.SplitLoopEnabled && other != 0;

        if (splitOn)
        {
            var (splitWord, splitTone) = StateFromLight(pnlSplitDot.BackColor);
            _connectionTiles[2].Set($"{DecoderConnection.ShortName(other)} - {splitWord}", ConnectionTileCaptions[2]);
            _splitStatusChip!.Tone = splitTone;
        }
        else
        {
            _connectionTiles[2].Set("Off", ConnectionTileCaptions[2]);
            _splitStatusChip!.Tone = AppTheme.TextMuted;
        }

        if (_connectionPassesHeard == 0)
        {
            _connectionTiles[3].Set("-", ConnectionTileCaptions[3]);
        }
    }

    /// <summary>
    /// The state a status light shows, in a word and the look's colour for
    /// it: the light's own colours are the ones RenderDecoderStatus and
    /// RenderSplitStatus paint it.
    /// </summary>
    private static (string Word, Color Tone) StateFromLight(Color light) =>
        light.ToArgb() == Color.SeaGreen.ToArgb() ? ("Connected", AppTheme.Success)
        : light.ToArgb() == Color.Goldenrod.ToArgb() ? ("Connecting", AppTheme.Warning)
        : light.ToArgb() == Color.Firebrick.ToArgb() ? ("Error", AppTheme.Danger)
        : ("Disconnected", AppTheme.TextMuted);
}
