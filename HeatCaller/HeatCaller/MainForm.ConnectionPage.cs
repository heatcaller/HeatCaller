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
        var decoder = new Section("Timing decoder") { Name = "Timing decoder" };
        var grid = decoder.Add(new FieldGrid(), gapBelow: 0);

        grid.AddRow("Connect via:", FieldGrid.Inline(cmbConnectionType, btnConnect, btnDisconnect));

        // The decoders from 3 up: where each is reached - see MainForm.StreamDecoders.
        BuildStreamDecoderRows(grid);

        serialPortRow = FieldGrid.Inline(cmbSerialPort, btnRefreshPorts);
        lblSerialPort = grid.AddRow("COM port:", serialPortRow);
        lblBaud = grid.AddRow("Baud:", cmbBaud);

        // Whichever decoder: the line (and the split loop with it) connects
        // as HeatCaller starts. Set from the settings in the constructor.
        chkConnectAtStartup.Text = "Connect when HeatCaller starts";
        chkConnectAtStartup.AutoSize = true;
        grid.AddField(chkConnectAtStartup);

        grid.AddField(chkAutoConnect);

        // WHY (23 Oct 2026, timing layer, P4): the switch for the timing
        // thread, beside the decoder it is about. Read once at start, so the
        // hint says when a change counts. Set from the settings in the
        // constructor, where it is also saved.
        chkTimingThread.Text = "Run timing on its own thread";
        chkTimingThread.AutoSize = true;
        grid.AddField(chkTimingThread);
        grid.AddHint("Takes effect the next time HeatCaller starts.");

        // WHY (24 Oct 2026): pass recording was a settings-file switch only.
        // Beside the timing thread, because both are about timing; it starts
        // and stops at once (see MainForm's handler).
        chkRecordPasses.Text = "Record every pass to a file";
        chkRecordPasses.AutoSize = true;
        grid.AddField(chkRecordPasses);
        grid.AddHint("For checking timing afterwards. One file per run in this PC's timing-recordings folder, kept 30 days.");

        grid.AddRow("Status:", new StatusLine(pnlStatusDot, lblStatus), stretch: true);

        // Every check before a green flag at once - the decoder, the cars
        // heard, timing, the website, the sound. See MissionControl.
        var btnMissionControl = new Button { Text = "Mission Control..." };
        btnMissionControl.Click += (_, _) => ShowMissionControl();
        grid.AddRow("Before a race:", FieldGrid.Inline(btnMissionControl));

        // The webcam photo finish: the camera page in this PC's browser, and
        // the pictures it keeps - see PhotoFinish.
        var btnCamera = new Button { Text = "Open Camera Page" };
        btnCamera.Click += (_, _) => OpenCameraPage();
        var btnPhotoFinish = new Button { Text = "Photo Finish..." };
        btnPhotoFinish.Click += (_, _) => ShowPhotoFinish();
        grid.AddRow("Finish camera:", FieldGrid.Inline(btnCamera, btnPhotoFinish));

        // The split loop: the other decoder, partway round the track - see
        // MainForm.SplitLoop. Its light and line show only while it is on.
        cmbSplitLoop.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbSplitLoop.SelectedIndexChanged += CmbSplitLoop_SelectedIndexChanged;

        btnConnectSplit.Text = "Connect Split";
        btnConnectSplit.Click += (_, _) => ConnectSplitLoop(userChose: true);

        btnLineUp.Text = "Line Up...";
        btnLineUp.Click += BtnLineUp_Click;

        pnlSplitDot.BackColor = Color.Gray;
        lblSplitStatus.Text = "Not connected.";

        grid.AddRow("Split loop:", FieldGrid.Inline(cmbSplitLoop, btnConnectSplit, btnLineUp));
        splitStatusLine = new StatusLine(pnlSplitDot, lblSplitStatus);
        lblSplitStatusCaption = grid.AddRow("Split status:", splitStatusLine, stretch: true);
        lblSplitStatusCaption.Visible = false;
        splitStatusLine.Visible = false;
        btnConnectSplit.Visible = false;
        btnLineUp.Visible = false;

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

        // Simple mode (1 Nov 2026): the decoder, its port, Connect, status,
        // Before a race and the finish camera. The timing thread, pass
        // recording, the split loop and the console are Advanced - the split
        // status row follows the split loop's own switch as well
        // (RenderSplitStatus). See SimpleMode.
        SimpleMode.HideRow(grid, chkTimingThread);
        SimpleMode.HideRow(grid, chkRecordPasses);
        SimpleMode.HideRow(grid, cmbSplitLoop);

        var page = new FillPage();

        page.Add(intro);
        page.Add(decoder);
        page.Add(SimpleMode.MoreLine(), stretch: false);
        page.AddFill(SimpleMode.Hide(console), console.LeastHeight, gapBelow: 0);

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
}
