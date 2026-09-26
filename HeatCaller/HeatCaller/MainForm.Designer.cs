#nullable enable

namespace HeatCaller;

partial class MainForm
{
    private System.ComponentModel.IContainer? components = null;

    protected override void Dispose(bool disposing)
    {
        if (disposing && (components != null))
        {
            components.Dispose();
        }
        base.Dispose(disposing);
    }

    private Label lblSerialPort = new();
    private ComboBox cmbSerialPort = new();
    private Button btnRefreshPorts = new();
    private Label lblBaud = new();
    private ComboBox cmbBaud = new();
    private Label lblConnectionType = new();
    private ComboBox cmbConnectionType = new();
    private CheckBox chkAutoConnect = new();
    private Button btnConnect = new();
    private Button btnDisconnect = new();
    private Label lblStatusCaption = new();
    private Label lblStatus = new();
    private Panel pnlStatusDot = new();
    private GroupBox grpConnection = new();
    private GroupBox grpSession = new();
    private ComboBox cmbTrackLayout = new();
    private TextBox txtSessionLabel = new();
    private ComboBox cmbActiveRound = new();

    // Fields rather than locals: the Session row is re-measured on every
    // resize (see MainForm.LayoutSessionRow), and that needs the captions
    // it is measuring against.
    private Label lblSessionLabel = new() { Text = "Name:", AutoSize = true };
    private Label lblTrack = new() { Text = "Track:", AutoSize = true };
    private Label lblActiveRound = new() { Text = "Round:", AutoSize = true };
    private GroupBox grpConsole = new();
    private TextBox txtConsole = new();
    private CheckBox chkAutoScroll = new();
    private Label lblConsoleFilter = new();
    private TextBox txtConsoleFilter = new();
    private ComboBox cmbConsolePreset = new();
    private CheckBox chkFilterExclude = new();
    private Button btnCopyConsole = new();
    private Button btnClearConsole = new();
    private Label lblFooterNote = new();

    // --- Sidebar navigation shell -----------------------------------------
    // Replaces the old top TabControl. "Live Session" is built directly
    // here (it's mostly the original form content); every other page is an
    // empty Panel that MainForm.cs fills with its UserControl once stores
    // are loaded, and adds to pnlContent alongside pageLiveSession. Only
    // one page is Visible at a time - see MainForm.cs's nav selection logic.
    private Panel pnlSidebar = new();
    private SidebarHeader lblAppTitle = null!;
    private SidebarNavList lstNav = new();
    private TextBox txtNavSearch = new();
    private Panel pnlNavSearch = new();
    private Panel pnlSidebarFooter = new();
    private Button btnManageNav = new();
    private Button btnMuteToggle = new();
    private Button btnUndo = new();
    private Panel pnlContent = new();

    private Panel pageLiveSession = new();

    // The one page whose contents are positioned absolutely rather than
    // docked. See where it is assembled, below.
    private Panel pnlLiveSessionBody = new();
    private Panel pageRaceControl = new();
    private Panel pageDriverProfiles = new();
    private Panel pageRaceProgram = new();
    private Panel pageDaySchedule = new();
    private Panel pageCheckIn = new();
    private Panel pageTrackLayouts = new();
    private Panel pageRaceHistory = new();
    private Panel pageAchievements = new();
    private Panel pageAnalytics = new();
    private Panel pageSetupLog = new();
    private Panel pageSeasons = new();
    private Panel pageDemoMode = new();
    private Panel pagePractice = new();
    private Panel pageVenues = new();
    private Panel pageThemes = new();
    private Panel pageSponsors = new();
    private Panel pageWebServer = new();
    private Panel pageVoice = new();
    private Panel pageSound = new();
    private Panel pageBackup = new();

    private void InitializeComponent()
    {
        SuspendLayout();

        // Form - width accounts for the sidebar (220px) on top of the
        // original 1000x860 content area, so every existing absolute
        // coordinate below (all originally laid out assuming a full-width
        // tab body) still renders identically inside pnlContent, which
        // gets exactly the same available width as before regardless of
        // the sidebar sitting to its left.
        // The build is in the title bar, so "which version are you on?"
        // never needs anyone to go looking for an About box.
        Text = AppVersion.Full;

        // Sized against the ACTUAL screen rather than a fixed 1220x894.
        // On a 1366x768 laptop - still common at clubs - that default was
        // taller than the usable desktop, and the old 1120x734 minimum was
        // too tall to resize down to, so the bottom of every page was
        // permanently unreachable with no way to fix it.
        var workingArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1220, 894);

        ClientSize = new Size(
            Math.Min(1220, workingArea.Width - 40),
            Math.Min(894, workingArea.Height - 40));

        // The minimum has to be genuinely reachable on a small screen, or
        // the window can't be made to fit at all. Pages scroll internally,
        // so a smaller window is usable rather than broken.
        MinimumSize = new Size(
            Math.Min(1120, workingArea.Width - 20),
            Math.Min(734, workingArea.Height - 20));

        StartPosition = FormStartPosition.CenterScreen;
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        // grpConnection
        //
        // No caption. The page's own heading says CONNECTION directly
        // above this box, and the box repeating it under itself was the
        // word twice in forty pixels.
        //
        // Nothing moves as a result: the first row inside starts at y=22
        // and a group box's DisplayRectangle is insetted from its FONT,
        // not from whether it has any text - so the band the caption used
        // to sit in simply becomes the card's top padding, which is what
        // every other card on the page already has.
        grpConnection.Text = string.Empty;

        // Kept so the layout check still has something to call this box in
        // the log. It falls back to the control's Name when there is no
        // caption, and to the TYPE name when there is no name either -
        // which would have turned every line about this box into one about
        // "GroupBox".
        grpConnection.Name = "Connection";
        grpConnection.Location = new Point(12, 12);
        grpConnection.Size = new Size(960, 170);
        grpConnection.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        // Placed after the caption rather than at a remembered x. "Connect
        // via:" in bold needs more than the 84px this row allowed at the
        // old text size, so the dropdown started underneath it and the word
        // "Direct" was hidden behind the label.
        lblConnectionType.Text = "Connect via:";
        lblConnectionType.Location = new Point(16, 28);
        lblConnectionType.AutoSize = true;
        lblConnectionType.Font = new Font(Font, FontStyle.Bold);

        cmbConnectionType.Location =
            new Point(lblConnectionType.Left + lblConnectionType.PreferredWidth + UiLayout.LabelGap, 25);
        cmbConnectionType.Width = 280;
        cmbConnectionType.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbConnectionType.Items.AddRange(DecoderConnection.Items());
        cmbConnectionType.SelectedIndex = 0;
        cmbConnectionType.SelectedIndexChanged += CmbConnectionType_SelectedIndexChanged;

        // The I-Lap serial row shares the auto-connect row's space - that
        // tick is EasyLap's alone, so only one of the two is ever visible.
        lblSerialPort.Text = "COM Port:";
        lblSerialPort.Location = new Point(16, 62);
        lblSerialPort.AutoSize = true;
        lblSerialPort.Visible = false;

        cmbSerialPort.Location = new Point(110, 59);
        cmbSerialPort.Width = 130;
        cmbSerialPort.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbSerialPort.Visible = false;

        btnRefreshPorts.Text = "Rescan";
        btnRefreshPorts.Size = new Size(UiLayout.ButtonWidth, UiLayout.ButtonHeight);
        btnRefreshPorts.Location = new Point(250, 56);
        btnRefreshPorts.Visible = false;
        btnRefreshPorts.Click += BtnRefreshPorts_Click;

        // I-Lap's baud rate, on the row below its COM port. Filled from
        // settings in MainForm, which is where they are read.
        lblBaud.Text = "Baud:";
        lblBaud.Location = new Point(16, 94);
        lblBaud.AutoSize = true;
        lblBaud.Visible = false;

        cmbBaud.Location = new Point(110, 91);
        cmbBaud.Width = 130;
        cmbBaud.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbBaud.Visible = false;

        // The two buttons go on the FIRST row, after the dropdown, and the
        // auto-connect tick gets a row to itself.
        //
        // They used to be at x=480 on the same rows as that tick, whose
        // caption is sixty characters long and auto-sizes - at this text
        // size it reaches well past 480, and because it is added to the
        // group before the buttons it sits IN FRONT of them. Connect was
        // reduced to a sliver of colour peeking out below the checkbox
        // text, and the part of it under that text could not be clicked at
        // all.
        btnConnect.Text = "Connect";
        btnConnect.Size = new Size(UiLayout.ButtonWidth, UiLayout.ButtonHeight);
        btnConnect.Location = new Point(cmbConnectionType.Right + UiLayout.ButtonGap * 2, 22);
        btnConnect.Click += BtnConnect_Click;

        btnDisconnect.Text = "Disconnect";
        btnDisconnect.Size = new Size(UiLayout.ButtonWidth, UiLayout.ButtonHeight);
        btnDisconnect.Location = new Point(btnConnect.Right + UiLayout.ButtonGap, 22);
        btnDisconnect.Enabled = false;
        btnDisconnect.Click += BtnDisconnect_Click;

        chkAutoConnect.Text = "Auto-connect (retries every few seconds until the decoder is found)";
        chkAutoConnect.Location = new Point(16, 66);
        chkAutoConnect.AutoSize = true;
        chkAutoConnect.CheckedChanged += ChkAutoConnect_CheckedChanged;

        pnlStatusDot.Location = new Point(16, 138);
        pnlStatusDot.Size = new Size(14, 14);
        pnlStatusDot.BackColor = Color.Gray;

        lblStatusCaption.Text = "Status:";
        lblStatusCaption.Location = new Point(38, 134);
        lblStatusCaption.AutoSize = true;
        lblStatusCaption.Font = new Font(Font, FontStyle.Bold);

        // Same row, measured - these two were eight pixels apart vertically
        // and overlapping horizontally, so the status read as one word
        // printed over another.
        lblStatus.Text = "Disconnected";
        lblStatus.Location =
            new Point(lblStatusCaption.Left + lblStatusCaption.PreferredWidth + UiLayout.LabelGap, 134);
        lblStatus.AutoSize = true;
        lblStatus.MaximumSize = new Size(600, 0);

        grpConnection.Controls.AddRange(new Control[]
        {
            lblConnectionType, cmbConnectionType,
            lblSerialPort, cmbSerialPort, btnRefreshPorts, lblBaud, cmbBaud,
            chkAutoConnect,
            btnConnect, btnDisconnect, pnlStatusDot, lblStatusCaption, lblStatus,
        });

        // Whatever the measurements, the things you click stay in front of
        // the things you only read.
        btnConnect.BringToFront();
        btnDisconnect.BringToFront();

        // grpSession
        //
        // One row now. This was a 168px "Session Setup" card carrying five
        // controls and a hint, parked on the most valuable screen in the
        // app - and since the program, the format chip and the Next Up bar
        // took over deciding what runs next, most of a race day never
        // touches it. What remains is the row of overrides: name a freeform
        // session, pick its track and type, jump to ANY round out of order,
        // and Reset Round for when it all needs to start again.
        grpSession.Text = "Session";
        grpSession.Font = AppTheme.BodyFont;

        // No Location, Size or Anchor here on purpose. This box is docked to
        // the top of the Race Control page and its height is measured from
        // the controls inside it - see BuildRaceControlPage and
        // LayoutSessionRow. Literals here were overwritten a moment after
        // they were set, which made them look like the layout while being
        // nothing of the sort: reading them told you the box was 960x76 at
        // (12, 190), and none of those three numbers was ever true on
        // screen.

        // Fields configured first - a control measures at its own font's
        // height, and every position below leans on those heights.
        txtSessionLabel.Font = AppTheme.BodyFont;
        txtSessionLabel.Width = 170;
        txtSessionLabel.PlaceholderText = "optional, e.g. \"Friday Main\"";
        txtSessionLabel.TextChanged += (_, _) =>
        {
            txtSessionLabel.BackColor = AppTheme.Surface;
            txtSessionLabel.ForeColor = AppTheme.TextDark;
        };

        cmbTrackLayout.Font = AppTheme.BodyFont;
        cmbTrackLayout.Width = 180;
        cmbTrackLayout.DropDownStyle = ComboBoxStyle.DropDownList;

        cmbActiveRound.Font = AppTheme.BodyFont;
        cmbActiveRound.Width = 280;
        cmbActiveRound.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbActiveRound.SelectedIndexChanged += CmbActiveRound_SelectedIndexChanged;

        lblSessionLabel.Font = AppTheme.BodyFont;
        lblTrack.Font = AppTheme.BodyFont;
        lblActiveRound.Font = AppTheme.BodyFont;

        // Placed by LayoutSessionRow, which runs on every resize. The row
        // used to be laid out here, left to right, at fixed field widths
        // and with nothing checking that it fitted - so "Reset Round" sat
        // wherever the arithmetic put it, which on a narrower window or a
        // larger text size was past the right-hand edge of the group.
        grpSession.Resize += (_, _) => LayoutSessionRow();

        grpSession.Controls.AddRange(new Control[]
        {
            lblSessionLabel, txtSessionLabel, lblTrack, cmbTrackLayout,
            lblActiveRound, cmbActiveRound,
        });

        // The Leaderboard box that used to sit here is gone, and so is the
        // grid inside it.
        //
        // It was still being built, still had its columns and its
        // double-click handler wired, and was still being filled on every
        // standing update - but it was never added to a parent, so none of
        // that reached a screen. The console took its space deliberately
        // (see grpConsole below); what was left behind was forty lines of
        // layout that looked live and drew nothing.
        //
        // The Race Control page has its own leaderboard, which is the one
        // people actually watch.

        // grpConsole
        // Every connection type logs here (EasyLap, I-Lap, audio timing and
        // the rest), and so does the app itself - naming one decoder sent
        // people looking elsewhere for the others.
        grpConsole.Text = "Developer Console — messages from the timing connection and the app's own log";
        // Moved up into the space the leaderboard and session groups left,
        // and made taller - the console is now the main thing on this page,
        // and a bigger one is genuinely more useful when diagnosing.
        grpConsole.Location = new Point(12, 196);
        grpConsole.Size = new Size(960, 368);
        grpConsole.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

        txtConsole.Multiline = true;
        txtConsole.ReadOnly = true;
        txtConsole.ScrollBars = ScrollBars.Vertical;
        txtConsole.Font = new Font("Consolas", 9F);
        txtConsole.Location = new Point(12, 56);
        txtConsole.Size = new Size(936, 300);
        txtConsole.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        txtConsole.WordWrap = false;

        chkAutoScroll.Text = "Auto-scroll";
        chkAutoScroll.Checked = true;
        chkAutoScroll.AutoSize = true;
        chkAutoScroll.Anchor = AnchorStyles.Top | AnchorStyles.Left;

        // Filter. Lines are kept in a full buffer and re-rendered, so
        // filtering never discards anything - you can narrow to one tag,
        // read it, and widen again without having lost the rest.
        // The whole filter row is run left to right from here, each control
        // starting where the last one ended. It was five fixed columns -
        // 12, 56, 244, 404, 534 - and every caption on it outgrew its
        // column at the larger text size, so "Filter:" sat on the text box,
        // and "Exclude matches" ran under the Auto-scroll tick.
        lblConsoleFilter.Text = "Filter:";
        lblConsoleFilter.Location = new Point(12, 26);
        lblConsoleFilter.AutoSize = true;
        lblConsoleFilter.Anchor = AnchorStyles.Top | AnchorStyles.Left;

        txtConsoleFilter.Location = new Point(UiLayout.After(lblConsoleFilter), 22);
        txtConsoleFilter.Width = 180;
        txtConsoleFilter.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        txtConsoleFilter.PlaceholderText = "text to match";

        cmbConsolePreset.Location = new Point(txtConsoleFilter.Right + UiLayout.ButtonGap, 22);
        cmbConsolePreset.Width = 150;
        cmbConsolePreset.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbConsolePreset.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        cmbConsolePreset.Items.AddRange(new object[]
        {
            "All", "Audio timing", "Voice", "Sound", "Race", "Decoder", "Storage", "Hide raw HID",
        });
        cmbConsolePreset.SelectedIndex = 0;

        chkFilterExclude.Text = "Exclude matches";
        chkFilterExclude.Location = new Point(cmbConsolePreset.Right + UiLayout.ButtonGap, 26);
        chkFilterExclude.AutoSize = true;
        chkFilterExclude.Anchor = AnchorStyles.Top | AnchorStyles.Left;

        // Measured through UiLayout rather than straight off PreferredSize.
        // An unparented check box reports the width it would autosize to in
        // the WinForms default font, not the one it is about to inherit,
        // which left this tick eleven pixels inside "Exclude matches".
        chkAutoScroll.Location = new Point(UiLayout.After(chkFilterExclude) + UiLayout.ButtonGap, 26);

        btnCopyConsole.Text = "Copy";
        btnCopyConsole.Location = new Point(780, 22);
        btnCopyConsole.Width = 80;
        btnCopyConsole.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        btnClearConsole.Text = "Clear";
        btnClearConsole.Location = new Point(868, 22);
        btnClearConsole.Width = 80;
        btnClearConsole.Anchor = AnchorStyles.Top | AnchorStyles.Right;

        grpConsole.Controls.AddRange(new Control[]
        {
            txtConsole, chkAutoScroll,
            lblConsoleFilter, txtConsoleFilter, cmbConsolePreset, chkFilterExclude,
            btnCopyConsole, btnClearConsole,
        });

        lblFooterNote.Text = "Choose your decoder above. Its laps feed the Race Control page, which runs the race.";
        lblFooterNote.Location = new Point(16, 858);
        lblFooterNote.AutoSize = true;
        lblFooterNote.ForeColor = AppTheme.TextMuted;
        lblFooterNote.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;

        // Assemble "Live Session" from everything above.
        // Connection and console only. The leaderboard duplicated the one on
        // Race Control, and session setup belongs with the race controls
        // that use it - this page is about getting connected and seeing
        // what the hardware is saying.
        //
        // Wrapped, rather than parented straight onto the page.
        //
        // Every other page holds a single Dock=Fill child, and a docked
        // child is laid out inside the parent's DisplayRectangle - so it
        // honours the page's Padding, which is how PageHeader reserves the
        // strip it draws the page's name in. This page is the exception:
        // its three controls are positioned absolutely, and absolute
        // coordinates are measured from the CLIENT rectangle, which Padding
        // does not touch. Without the wrapper the heading would have been
        // drawn underneath the Connection box.
        //
        // The wrapper keeps everything else identical. The three controls
        // get the same kind of parent they had - a plain Panel at the same
        // starting size, docked to fill afterwards - so the offsets their
        // anchors resolve against are the ones they have always had.
        pnlLiveSessionBody.Controls.Add(grpConnection);
        pnlLiveSessionBody.Controls.Add(grpConsole);
        pnlLiveSessionBody.Controls.Add(lblFooterNote);
        pnlLiveSessionBody.Dock = DockStyle.Fill;
        pnlLiveSessionBody.BackColor = AppTheme.Background;
        pageLiveSession.Controls.Add(pnlLiveSessionBody);

        foreach (Panel page in new[]
                 {
                     pageLiveSession, pageRaceControl, pageDriverProfiles, pageRaceProgram, pageDaySchedule, pageCheckIn, pageTrackLayouts,
                     pageRaceHistory, pageAnalytics, pageAchievements, pageSetupLog, pageSeasons, pagePractice, pageDemoMode, pageVenues, pageThemes, pageSponsors, pageWebServer, pageVoice, pageSound, pageBackup,
                 })
        {
            page.Dock = DockStyle.Fill;
            page.BackColor = AppTheme.Background;
            page.Visible = false;
        }

        // --- Sidebar ---
        pnlSidebar.Dock = DockStyle.Left;
        pnlSidebar.Width = 220;
        pnlSidebar.BackColor = AppTheme.SidebarBackground;

        // CurrentMode is the running look's family, set by AppTheme.UseLook
        // before this runs. A club logo, when there is one, is put in once
        // the window is built - MainForm.RefreshHeaderLogo - and sets the
        // height from there.
        var isSynthwave = AppTheme.CurrentMode == AppTheme.Mode.Synthwave;

        // A custom double-buffered control rather than a Label: the animated
        // scene repaints constantly, and a Label's un-buffered painting made
        // the moving grid lines flicker.
        lblAppTitle = new SidebarHeader("HEATCALLER", string.Empty) { SynthwaveMode = isSynthwave };
        lblAppTitle.Dock = DockStyle.Top;
        lblAppTitle.Height = isSynthwave ? 132 : 64;
        lblAppTitle.BackColor = AppTheme.SidebarBackground;

        lstNav.Dock = DockStyle.Fill;
        lstNav.BackColor = AppTheme.SidebarBackground;
        lstNav.ForeColor = AppTheme.SidebarText;
        lstNav.Font = AppTheme.NavFont;

        // A single row of three icon-only buttons rather than three stacked
        // text rows - see MainForm.SetUpFooterButton. Same 12px margin
        // either side as the old stack (12 + 60 + 8 + 60 + 8 + 60 + 12 =
        // 220 = pnlSidebar.Width), so the row lines up with the nav list
        // above it without its own separate inset.
        pnlSidebarFooter.Dock = DockStyle.Bottom;
        pnlSidebarFooter.Height = 70;
        pnlSidebarFooter.BackColor = AppTheme.SidebarBackground;

        btnManageNav.Location = new Point(12, 14);
        btnManageNav.Size = new Size(60, 42);
        btnManageNav.FlatStyle = FlatStyle.Flat;
        btnManageNav.BackColor = AppTheme.SidebarHover;
        btnManageNav.ForeColor = AppTheme.SidebarText;
        btnManageNav.Font = AppTheme.SmallFont;
        btnManageNav.Cursor = Cursors.Hand;

        btnMuteToggle.Location = new Point(80, 14);
        btnMuteToggle.Size = new Size(60, 42);
        btnMuteToggle.FlatStyle = FlatStyle.Flat;
        btnMuteToggle.BackColor = AppTheme.SidebarHover;
        btnMuteToggle.ForeColor = AppTheme.SidebarText;
        btnMuteToggle.Font = AppTheme.SmallFont;
        btnMuteToggle.Cursor = Cursors.Hand;

        btnUndo.Location = new Point(148, 14);
        btnUndo.Size = new Size(60, 42);
        btnUndo.FlatStyle = FlatStyle.Flat;
        btnUndo.BackColor = AppTheme.SidebarHover;
        btnUndo.ForeColor = AppTheme.SidebarText;
        btnUndo.Font = AppTheme.SmallFont;
        btnUndo.Cursor = Cursors.Hand;
        btnUndo.Enabled = false;

        pnlSidebarFooter.Controls.Add(btnUndo);
        pnlSidebarFooter.Controls.Add(btnManageNav);
        pnlSidebarFooter.Controls.Add(btnMuteToggle);

        // Search box sits between the header banner and the nav list. Docked
        // in its own panel so it gets padding without fighting the list's fill.
        pnlNavSearch.Dock = DockStyle.Top;
        pnlNavSearch.Height = 38;
        pnlNavSearch.BackColor = AppTheme.SidebarBackground;
        pnlNavSearch.Padding = new Padding(10, 4, 10, 6);

        txtNavSearch.Dock = DockStyle.Fill;
        txtNavSearch.PlaceholderText = "Search pages...";
        // None, not FixedSingle: InputOutline strips the native border from
        // every input and draws a themed ring from the parent instead. This
        // used to say FixedSingle and was silently overridden on the theme
        // pass - harmless, but it meant the two places disagreed, and the
        // one that ran last is the one that decided.
        txtNavSearch.BorderStyle = BorderStyle.None;
        txtNavSearch.BackColor = AppTheme.SidebarHover;
        txtNavSearch.ForeColor = AppTheme.SidebarText;
        pnlNavSearch.Controls.Add(txtNavSearch);

        pnlSidebar.Controls.Add(lstNav);
        pnlSidebar.Controls.Add(pnlNavSearch);
        pnlSidebar.Controls.Add(lblAppTitle);
        pnlSidebar.Controls.Add(pnlSidebarFooter);

        // --- Content area ---
        pnlContent.Dock = DockStyle.Fill;
        pnlContent.BackColor = AppTheme.Background;
        pnlContent.Controls.Add(pageLiveSession);

        Controls.Add(pnlContent);
        Controls.Add(pnlSidebar);

        ResumeLayout(false);
    }
}
