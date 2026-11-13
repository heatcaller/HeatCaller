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

    // The Connection page's controls, put on the page in
    // MainForm.ConnectionPage. The two captions come from the rows they
    // name, which are hidden for any decoder but I-Lap.
    private Label lblSerialPort = null!;
    private ComboBox cmbSerialPort = new();
    private Button btnRefreshPorts = new();
    private Label lblBaud = null!;
    private ComboBox cmbBaud = new();
    private ComboBox cmbConnectionType = new();
    private CheckBox chkAutoConnect = new ThemedCheckBox();
    private Button btnConnect = new();
    private Button btnDisconnect = new();
    private Label lblStatus = new();
    private Panel pnlStatusDot = new();

    // The Session row on Race Control. Made in InitializeComponent rather
    // than here: a card takes the theme's surface colour as it is made, and
    // these initialisers run before the constructor has chosen the look.
    private Section grpSession = null!;
    private FieldRow sessionRow = null!;

    private ComboBox cmbTrackLayout = new();
    private TextBox txtSessionLabel = new();
    private ComboBox cmbActiveRound = new();

    // Fields rather than locals: the Session row's FieldRow is handed them
    // in InitializeComponent, and SetSessionNameVisible hides the Name
    // caption with its box.
    private Label lblSessionLabel = new() { Text = "Name:", AutoSize = true };
    private Label lblTrack = new() { Text = "Track:", AutoSize = true };
    private Label lblActiveRound = new() { Text = "Round:", AutoSize = true };


    // The developer console, on the Connection page.
    private TextBox txtConsole = new();
    private CheckBox chkAutoScroll = new ThemedCheckBox();
    private TextBox txtConsoleFilter = new();
    private ComboBox cmbConsolePreset = new();
    private CheckBox chkFilterExclude = new ThemedCheckBox();
    private Button btnCopyConsole = new();
    private Button btnClearConsole = new();

    // --- Sidebar navigation shell -----------------------------------------
    // Replaces the old top TabControl. "Live Session" - the Connection page
    // now - is built here (MainForm.ConnectionPage); every other page is an
    // empty Panel that MainForm.cs fills with its UserControl once stores
    // are loaded, and adds to pnlContent alongside pageLiveSession. Only
    // one page is Visible at a time - see MainForm.cs's nav selection logic.
    private Panel pnlSidebar = new();
    private SidebarHeader lblAppTitle = null!;
    private SidebarNavList lstNav = new();
    private TextBox txtNavSearch = new();
    private Panel pnlNavSearch = new();
    private Button btnCollapseNav = new();
    private Panel pnlSidebarFooter = new();
    private Button btnManageNav = new();
    private Button btnMuteToggle = new();
    private Button btnUndo = new();
    private Panel pnlContent = new();

    private Panel pageLiveSession = new();
    private Panel pageRaceControl = new();
    private Panel pageDriverProfiles = new();
    private Panel pageTrackLayouts = new();
    private Panel pageRaceHistory = new();
    private Panel pageAchievements = new();
    private Panel pageAnalytics = new();
    private Panel pageSetupLog = new();
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

        // The Connection page - the timing decoder and the developer
        // console. Built on the layout pieces in MainForm.ConnectionPage.
        BuildConnectionPage();

        // grpSession
        //
        // One row now. This was a 168px "Session Setup" card carrying five
        // controls and a hint, parked on the most valuable screen in the
        // app - and since the program, the format chip and the Next Up bar
        // took over deciding what runs next, most of a race day never
        // touches it. What remains is the row of overrides: name a freeform
        // session, pick its track, and jump to ANY round out of order.
        //
        // A card from the layout pieces, put at the top of the Race Control
        // page in PopulateTabs, its row laid out by a FieldRow: each field
        // beside its measured caption, the round picker taking the width the
        // others leave, and the row going onto a second line rather than
        // squeezing the picker. No positions, sizes or fonts here: the
        // fields' widths are measured too (SessionNameWidth,
        // SessionTrackWidth, SessionRoundLeastWidth), and the card and the
        // row measure themselves in the window's font, and follow it when
        // the text size changes.
        grpSession = new Section("Session");
        sessionRow = new FieldRow();

        txtSessionLabel.PlaceholderText = "optional, e.g. \"Friday Main\"";
        txtSessionLabel.TextChanged += (_, _) =>
        {
            txtSessionLabel.BackColor = AppTheme.Surface;
            txtSessionLabel.ForeColor = AppTheme.TextDark;
        };

        cmbTrackLayout.DropDownStyle = ComboBoxStyle.DropDownList;

        cmbActiveRound.DropDownStyle = ComboBoxStyle.DropDownList;
        cmbActiveRound.SelectedIndexChanged += CmbActiveRound_SelectedIndexChanged;

        sessionRow.Add(lblSessionLabel, txtSessionLabel, SessionNameWidth);
        sessionRow.Add(lblTrack, cmbTrackLayout, SessionTrackWidth);
        sessionRow.AddLast(lblActiveRound, cmbActiveRound, SessionRoundLeastWidth);

        grpSession.Add(sessionRow);

        // The Leaderboard box that used to sit here is gone, and so is the
        // grid inside it.
        //
        // It was still being built, still had its columns and its
        // double-click handler wired, and was still being filled on every
        // standing update - but it was never added to a parent, so none of
        // that reached a screen. The console took its space deliberately
        // (it is the Connection page's lower card now); what was left
        // behind was forty lines of layout that looked live and drew
        // nothing.
        //
        // The Race Control page has its own leaderboard, which is the one
        // people actually watch.

        foreach (Panel page in new[]
                 {
                     pageLiveSession, pageRaceControl, pageDriverProfiles, pageTrackLayouts,
                     pageRaceHistory, pageAnalytics, pageAchievements, pageSetupLog, pagePractice, pageDemoMode, pageVenues, pageThemes, pageSponsors, pageWebServer, pageVoice, pageSound, pageBackup,
                 })
        {
            page.Dock = DockStyle.Fill;
            page.BackColor = AppTheme.Background;
            page.Visible = false;
        }

        // --- Sidebar ---
        //
        // Its width, the header's height and the footer's row of buttons are
        // at the screen's DPI (UiLayout.AtDpi), as the point-size text in
        // them is: on a screen the app draws at 150% (--dpi-aware) that text
        // is half as big again in pixels. Exactly these sizes at 96 DPI.
        pnlSidebar.Dock = DockStyle.Left;
        pnlSidebar.Width = SidebarWidth;
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
        lblAppTitle.Height = isSynthwave ? UiLayout.AtDpi(132) : UiLayout.AtDpi(64);
        lblAppTitle.BackColor = AppTheme.SidebarBackground;

        lstNav.Dock = DockStyle.Fill;
        lstNav.BackColor = AppTheme.SidebarBackground;
        lstNav.ForeColor = AppTheme.SidebarText;
        lstNav.Font = AppTheme.NavFont;

        // A single row of three icon-only buttons rather than three stacked
        // text rows - see MainForm.SetUpFooterButton. Same 12px margin
        // either side as the old stack (12 + 60 + 8 + 60 + 8 + 60 + 12 =
        // 220, the sidebar's width at the normal text size), so the row lines
        // up with the nav list above it without its own separate inset; kept
        // in the middle when the sidebar is wider (PlaceSidebar).
        pnlSidebarFooter.Dock = DockStyle.Bottom;
        pnlSidebarFooter.Height = UiLayout.AtDpi(70);
        pnlSidebarFooter.BackColor = AppTheme.SidebarBackground;

        btnManageNav.Location = new Point(UiLayout.AtDpi(12), UiLayout.AtDpi(14));
        btnManageNav.Size = new Size(UiLayout.AtDpi(60), UiLayout.AtDpi(42));
        btnManageNav.FlatStyle = FlatStyle.Flat;
        btnManageNav.BackColor = AppTheme.SidebarHover;
        btnManageNav.ForeColor = AppTheme.SidebarText;
        btnManageNav.Font = AppTheme.SmallFont;
        btnManageNav.Cursor = Cursors.Hand;

        btnMuteToggle.Location = new Point(UiLayout.AtDpi(80), UiLayout.AtDpi(14));
        btnMuteToggle.Size = new Size(UiLayout.AtDpi(60), UiLayout.AtDpi(42));
        btnMuteToggle.FlatStyle = FlatStyle.Flat;
        btnMuteToggle.BackColor = AppTheme.SidebarHover;
        btnMuteToggle.ForeColor = AppTheme.SidebarText;
        btnMuteToggle.Font = AppTheme.SmallFont;
        btnMuteToggle.Cursor = Cursors.Hand;

        btnUndo.Location = new Point(UiLayout.AtDpi(148), UiLayout.AtDpi(14));
        btnUndo.Size = new Size(UiLayout.AtDpi(60), UiLayout.AtDpi(42));
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

        // The chevron that narrows the sidebar to its icons (Ctrl+B - see
        // MainForm.SetNavIconsOnly), in the search row rather than in a strip
        // of its own, so the toggle costs the nav list no height. Added after
        // the search box, which puts it in front of it: the box is docked
        // Fill until FitNavSearchHeight gives it measured bounds, and until
        // then it covers the whole row. Styled in SetUpCollapseButton and
        // placed in FitNavSearchHeight; both run on the first theme pass.
        btnCollapseNav.FlatStyle = FlatStyle.Flat;
        btnCollapseNav.BackColor = AppTheme.SidebarBackground;
        btnCollapseNav.ForeColor = AppTheme.SidebarText;
        btnCollapseNav.Size = new Size(UiLayout.AtDpi(24), UiLayout.AtDpi(24));
        btnCollapseNav.TabStop = false;
        btnCollapseNav.Cursor = Cursors.Hand;
        pnlNavSearch.Controls.Add(btnCollapseNav);

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

        // Measured again now the Connection page is in the window: its
        // pickers were built in the default font, before it was.
        _connectionTextSize.Apply();

        ResumeLayout(false);
    }
}
