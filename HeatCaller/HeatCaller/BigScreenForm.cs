namespace HeatCaller;

/// <summary>
/// A standalone, high-contrast, large-format display meant to be projected
/// or shown on a second monitor for spectators - the live leaderboard,
/// the most recent announcement, and a laps/time-remaining countdown, all
/// readable from across a room. Painted in the crowd-screen look (Themes →
/// Looks), the same one the Wi-Fi pages and the online pages use, so the
/// wall, the phones and the club's site all match. The club's logo sits in
/// the top-left corner when one is set.
/// </summary>
public sealed class BigScreenForm : Form
{
    // "Race Wall": this is a DISPLAY, not an interface. Read from two metres
    // away, in a room with the lights up, the things that matter are
    // contrast and type size - card chrome, tonal steps and rounded panels
    // all just eat room that the numbers should have. So only a handful of
    // the look's colours are used, each in one role:
    //   canvas     = the look's Background   (window, grid, top bands)
    //   panel      = Surface                 (the on-deck bar)
    //   text       = Text                    (announcement, leaderboard)
    //   race state = Primary                 (state line, elapsed clock)
    //   highlight  = Accent                  (countdown, on-deck, fast laps)
    //   hairlines  = Border, hints = Muted
    // A look is checked for exactly these pairings (text on background,
    // primary and accent on a card), so a light look reads as well as a
    // dark one. The look can change while the window is open - see
    // ApplyLook - so these are read from the current look, not captured.
    private ResolvedLook _look;

    private Color CanvasColor => _look.Background;
    private Color PanelColor => _look.Surface;
    private Color TextColor => _look.Text;
    private Color AccentColor => _look.Primary;
    private Color HighlightColor => _look.Accent;

    // Hairlines, not gridlines. At this size a visible grid reads as a
    // spreadsheet; the rows need separating, not boxing.
    private Color GridLineColor => _look.Border;
    private Color MutedColor => _look.Muted;

    /// <summary>
    /// Supplied by Race Control when it opens this window; null when the
    /// Big Screen was opened from Practice, where nothing is qualifying.
    /// </summary>
    private readonly QualifyingProjector? _projector;

    // Where the Badges column gets its counts. A function rather than a
    // store because Race Control swaps its store for a freshly loaded one
    // whenever someone unlocks a badge - holding the instance it had when
    // this window opened would leave the Big Screen a race behind the
    // announcement. Null (and the column shows "-") only if a caller has
    // nothing to offer.
    private readonly Func<AchievementStore?>? _achievements;

    private readonly RaceControlSession _race;
    private readonly AnnouncementService _announcer;
    private readonly ProfileStore _profiles;
    private readonly DayScheduleStore? _schedule;
    private readonly SponsorStore? _sponsors;
    private readonly AppSettings? _settings;
    private readonly KpiTracker _kpiTracker = new();
    private List<KpiDefinition> _kpiColumns = new();
    private readonly System.Windows.Forms.Timer _refreshTimer = new() { Interval = 500 };

    private Action<RaceState>? _stateChangedHandler;
    private Action<FlagState>? _flagChangedHandler;

    private readonly Label _lblOnDeck = new();
    private readonly PictureBox _picSponsor = new();
    private readonly Panel _pnlLeaderboardArea = new();
    private readonly System.Windows.Forms.Timer _sponsorTimer = new();
    private int _sponsorIndex = -1;
    private bool _showingSponsor;

    private readonly Label _lblAnnouncement = new();
    private DateTime? _lastAnnouncementUtc;

    // The warning banner: a full-width strip under the top band, shown for
    // a few seconds when race control warns somebody, then hidden again.
    // Yellow and black for a warning, black and white for a black flag -
    // the colours a driver already knows from the flags themselves, and
    // the same whatever look the screen is wearing.
    private readonly Label _lblWarning = new();
    private DateTime? _warningShownUtc;
    private static readonly TimeSpan WarningBannerTime = TimeSpan.FromSeconds(8);
    private Action<RacerStanding, RaceWarning>? _warningIssuedHandler;
    private Action<RacerStanding, int, long[]>? _sectorsRecordedHandler;
    private Action<RacerStanding, TimingPoint, long>? _splitCrossedHandler;

    /// <summary>True while the banner shows a warning - a fastest sector never covers one.</summary>
    private bool _bannerIsWarning;
    private readonly Label _lblElapsed = new();
    private readonly Label _lblCountdown = new();
    private readonly Label _lblRaceState = new();
    private readonly DataGridView _grid = new();

    // Kept as fields (they used to be locals in BuildLayout) so a change of
    // look can recolour them while the window is open.
    private readonly Label _lblHint = new();
    private readonly Panel _pnlTop = new();
    private readonly Panel _pnlCountdown = new();
    private readonly Panel _pnlOnDeck = new();

    // The club logo, top-left, and an empty strip of the same width on the
    // right. The strip is there so the race-state line and the announcement
    // stay centred on the SCREEN rather than on what is left of it, and can
    // never run underneath the logo.
    private readonly LogoBox _logoBox = new();
    private readonly Panel _logoBalance = new();

    // Held in a field so it can be taken off the static event again when the
    // window closes - a static event would otherwise keep every Big Screen
    // ever opened alive for the rest of the day.
    private readonly Action _displaysChangedHandler;
    private bool _displaysSubscribed;

    private bool _isFullscreen;
    private FormBorderStyle _restoreBorderStyle;
    private FormWindowState _restoreWindowState;

    public BigScreenForm(RaceControlSession race, AnnouncementService announcer, ProfileStore profiles,
        DayScheduleStore? schedule = null, SponsorStore? sponsors = null, AppSettings? settings = null,
        QualifyingProjector? projector = null, Func<AchievementStore?>? achievements = null)
    {
        _projector = projector;
        _achievements = achievements;
        _race = race;
        _announcer = announcer;
        _profiles = profiles;
        _schedule = schedule;
        _sponsors = sponsors;
        _settings = settings;
        _look = CurrentLook();

        Text = "HeatCaller — Big Screen";
        StartPosition = FormStartPosition.CenterScreen;

        // 1280x800 at the screen's DPI (UiLayout.AtDpi - exactly that while
        // the app runs DPI-unaware, the default), and in a DPI-aware run no
        // bigger than the screen it opens on: at 150% on a 1080p laptop the
        // scaled size would be 1920x1200, taller than the screen.
        var opens = new Size(UiLayout.AtDpi(1280), UiLayout.AtDpi(800));

        if (UiLayout.DpiScale > 1f)
        {
            var area = Screen.FromPoint(Cursor.Position).WorkingArea;
            opens = new Size(Math.Min(opens.Width, area.Width), Math.Min(opens.Height, area.Height));
        }

        Size = opens;
        MinimumSize = new Size(Math.Min(UiLayout.AtDpi(800), opens.Width), Math.Min(UiLayout.AtDpi(500), opens.Height));
        KeyPreview = true;

        BuildLayout();
        ApplyLook();

        // The crowd-screen look and the logo can be changed on Themes →
        // Looks while this window is up on the wall; it follows straight
        // away rather than waiting to be reopened.
        _displaysChangedHandler = OnDisplaysChanged;
        Looks.DisplaysChanged += _displaysChangedHandler;
        _displaysSubscribed = true;

        // Held in fields rather than inline lambdas, because an inline
        // lambda can't be unsubscribed - these two were left attached when
        // the window closed, so a reopened-and-closed Big Screen accumulated
        // dead handlers that still fired against a disposed form.
        _stateChangedHandler = _ => SafeInvoke(RefreshAll);
        _flagChangedHandler = _ => SafeInvoke(RefreshAll);

        _race.LapProcessed += OnRaceEvent;
        _race.StandingAdjusted += OnStandingAdjusted;
        _race.StateChanged += _stateChangedHandler;
        _race.FlagChanged += _flagChangedHandler;
        _announcer.AnnouncementTextReady += OnAnnouncementTextReady;

        _warningIssuedHandler = (standing, warning) => SafeInvoke(() => ShowWarningBanner(standing, warning));
        _race.WarningIssued += _warningIssuedHandler;

        _sectorsRecordedHandler = (standing, _, sectors) => SafeInvoke(() =>
        {
            ShowFastestSector(standing, sectors);
            RefreshAll();
        });
        _race.SectorsRecorded += _sectorsRecordedHandler;

        _splitCrossedHandler = (_, _, _) => SafeInvoke(RefreshAll);
        _race.SplitCrossed += _splitCrossedHandler;

        _refreshTimer.Tick += (_, _) => RefreshCountdownOnly();
        _refreshTimer.Start();

        StartSponsorRotationIfConfigured();

        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape)
            {
                Close();
            }
            else if (e.KeyCode == Keys.F11)
            {
                ToggleFullscreen();
            }
        };

        FormClosed += (_, _) =>
        {
            // Disposed as well as stopped. This window is opened and closed
            // repeatedly across a race day, and a stopped-but-undisposed
            // timer still holds its native handle each time.
            _refreshTimer.Stop();
            _refreshTimer.Dispose();
            _sponsorTimer.Stop();
            _sponsorTimer.Dispose();

            _picSponsor.Image?.Dispose();
            _race.LapProcessed -= OnRaceEvent;
            _race.StandingAdjusted -= OnStandingAdjusted;
            _race.StateChanged -= _stateChangedHandler;
            _race.FlagChanged -= _flagChangedHandler;
            _race.WarningIssued -= _warningIssuedHandler;
            _race.SectorsRecorded -= _sectorsRecordedHandler;
            _race.SplitCrossed -= _splitCrossedHandler;
            _announcer.AnnouncementTextReady -= OnAnnouncementTextReady;
            UnsubscribeDisplays();
        };

        RefreshAll();
    }

    /// <summary>
    /// The look to paint with: the crowd-screen look from settings, or the
    /// app's own look when the window was opened without settings.
    /// </summary>
    private ResolvedLook CurrentLook() =>
        _settings is null ? AppTheme.Active : Looks.ForDisplays(_settings);

    private void UnsubscribeDisplays()
    {
        if (_displaysSubscribed)
        {
            Looks.DisplaysChanged -= _displaysChangedHandler;
            _displaysSubscribed = false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            // Normally already done in FormClosed; repeated here for a window
            // disposed without ever being closed, which would otherwise stay
            // on the static event for good.
            // (The logo image goes with the logo box, one of this window's
            // own controls, which base.Dispose disposes.)
            UnsubscribeDisplays();
        }

        base.Dispose(disposing);

        // After the controls that use them are gone. A control never
        // disposes a Font it was handed, and this window is opened and
        // closed repeatedly across a race day - eight GDI fonts each time.
        if (disposing)
        {
            foreach (var font in _ownedFonts)
            {
                font.Dispose();
            }

            _ownedFonts.Clear();
        }
    }

    /// <summary>The fonts this window made for itself, disposed with it - see Dispose.</summary>
    private readonly List<Font> _ownedFonts = new();

    /// <summary>Remembers a font this window created, so it is disposed when the window is, and hands it back.</summary>
    private Font OwnFont(Font font)
    {
        _ownedFonts.Add(font);
        return font;
    }

    /// <summary>
    /// Raised on the UI thread by the Looks page, but marshalled anyway: a
    /// stray call from another thread must not touch the controls, and one
    /// arriving as the window closes must not throw.
    /// </summary>
    private void OnDisplaysChanged()
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(new Action(OnDisplaysChanged));
            }
            catch (InvalidOperationException)
            {
                // The window's handle went away between the check and the
                // call - it is closing, so there is nothing left to recolour.
            }

            return;
        }

        _look = CurrentLook();
        ApplyLook();
        RefreshAll();
    }

    /// <summary>
    /// Puts the current look's colours on every part of the window, and
    /// shows, swaps or hides the club logo. Safe to call again at any time;
    /// the per-cell highlight colours are put back by the next RefreshAll.
    /// </summary>
    private void ApplyLook()
    {
        BackColor = CanvasColor;
        ForeColor = TextColor;

        _lblHint.BackColor = CanvasColor;
        _lblHint.ForeColor = MutedColor;

        _pnlTop.BackColor = CanvasColor;
        _lblRaceState.ForeColor = AccentColor;
        _lblAnnouncement.ForeColor = TextColor;

        _pnlCountdown.BackColor = CanvasColor;
        _lblCountdown.ForeColor = HighlightColor;
        _lblElapsed.ForeColor = AccentColor;

        _grid.BackgroundColor = CanvasColor;
        _grid.GridColor = GridLineColor;
        _grid.ColumnHeadersDefaultCellStyle.BackColor = CanvasColor;
        _grid.ColumnHeadersDefaultCellStyle.ForeColor = MutedColor;
        _grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = CanvasColor;
        _grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = MutedColor;
        _grid.DefaultCellStyle.BackColor = CanvasColor;
        _grid.DefaultCellStyle.ForeColor = TextColor;

        // Selection painted as if unselected: nobody is clicking this grid,
        // and the first row is selected by default - a selection colour
        // would read as "P1 is special" for no reason.
        _grid.DefaultCellStyle.SelectionBackColor = CanvasColor;
        _grid.DefaultCellStyle.SelectionForeColor = TextColor;

        // Deliberately NO zebra striping - see BuildLayout.
        _grid.AlternatingRowsDefaultCellStyle.BackColor = CanvasColor;
        _grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = CanvasColor;

        _pnlLeaderboardArea.BackColor = CanvasColor;
        _picSponsor.BackColor = CanvasColor;

        _pnlOnDeck.BackColor = PanelColor;
        _lblOnDeck.ForeColor = HighlightColor;

        ApplyLogo();
    }

    /// <summary>
    /// The club logo in the top-left corner, when there is one and it is
    /// switched on for the screens (Themes → Looks). On a plate when the
    /// logo would otherwise disappear into this look's background - a black
    /// logo on a black wall.
    /// </summary>
    private void ApplyLogo()
    {
        Image? logo = null;
        Color? plate = null;

        if (_settings is not null && _settings.ShowLogoOnScreens)
        {
            logo = ClubLogo.Load(_settings.RelayLogoFile);

            if (logo is not null)
            {
                plate = ClubLogo.PlateFor(_settings.RelayLogoFile, CanvasColor);
            }
        }

        // The box takes the image over and disposes the one it had.
        _logoBox.SetLogo(logo, plate);
        _logoBox.BackColor = CanvasColor;
        _logoBalance.BackColor = CanvasColor;

        var show = logo is not null;
        _logoBox.Visible = show;
        _logoBalance.Visible = show;
    }

    private void BuildLayout()
    {
        // Colours are left to ApplyLook, which runs straight after this and
        // again whenever the crowd-screen look changes.
        _lblHint.Text = "F11: toggle fullscreen     Esc: close";
        _lblHint.Dock = DockStyle.Top;
        _lblHint.Height = UiLayout.AtDpi(24);
        _lblHint.TextAlign = ContentAlignment.MiddleRight;
        _lblHint.Font = AppTheme.SmallFont;
        _lblHint.Padding = new Padding(0, 0, UiLayout.AtDpi(12), 0);

        // The bands, columns, paddings and faces are sized in ApplyScale:
        // at the screen's DPI, as the point-size faces are (on a screen the
        // app draws at 150% - --dpi-aware - a 56-point clock is half as tall
        // again in pixels, and a band of fixed pixels would clip it), and
        // grown or shrunk with the window - see ApplyScale.
        _pnlTop.Dock = DockStyle.Top;

        _lblRaceState.Text = "IDLE";
        _lblRaceState.AutoSize = false;
        _lblRaceState.Dock = DockStyle.Top;
        _lblRaceState.TextAlign = ContentAlignment.MiddleCenter;

        _lblAnnouncement.Text = "Waiting for the race to start...";
        _lblAnnouncement.AutoSize = false;
        _lblAnnouncement.Dock = DockStyle.Fill;
        _lblAnnouncement.TextAlign = ContentAlignment.MiddleCenter;

        // The logo box is square and as tall as the whole band - the state
        // line and the announcement together - which on a wall makes it
        // about the height of two lines of the leaderboard: big enough to
        // recognise from the back of the room without competing with the
        // race. Hidden (taking no room) when there is no logo to show.
        _logoBox.Dock = DockStyle.Left;
        _logoBox.Visible = false;

        _logoBalance.Dock = DockStyle.Right;
        _logoBalance.Visible = false;

        // Docking is laid out from the last control added to the first: the
        // logo and its balancing strip are added last so they claim the full
        // height of the band before the state line takes the top of what is
        // left, and the announcement fills the rest.
        _pnlTop.Controls.Add(_lblAnnouncement);
        _pnlTop.Controls.Add(_lblRaceState);
        _pnlTop.Controls.Add(_logoBalance);
        _pnlTop.Controls.Add(_logoBox);

        _pnlCountdown.Dock = DockStyle.Top;

        _lblCountdown.Text = string.Empty;
        _lblCountdown.Dock = DockStyle.Fill;
        _lblCountdown.TextAlign = ContentAlignment.MiddleCenter;

        // Elapsed clock, docked left of the countdown. Previously the only
        // time shown here was the countdown, which stays blank unless a lap
        // or time limit is configured - so opening the Big Screen mid-race
        // with no limits set showed no timing at all until a car crossed
        // the line. This ticks from the moment the race goes green.
        _lblElapsed.Text = string.Empty;
        _lblElapsed.Dock = DockStyle.Left;
        _lblElapsed.TextAlign = ContentAlignment.MiddleCenter;

        _pnlCountdown.Controls.Add(_lblCountdown);
        _pnlCountdown.Controls.Add(_lblElapsed);

        // This grid is hand-styled with the Big Screen's own palette
        // rather than going through AppTheme.StyleGrid, so it needs the
        // same double-buffering fix applied directly or it flickers on
        // hover like every other un-buffered DataGridView.
        try
        {
            typeof(DataGridView)
                .GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(_grid, true);
        }
        catch
        {
            // Rendering nicety only - never worth crashing over.
        }

        _grid.Dock = DockStyle.Fill;
        _grid.BorderStyle = BorderStyle.None;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.EnableHeadersVisualStyles = false;
        _grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        _grid.ColumnHeadersDefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleLeft;
        // Deliberately NO zebra striping. At this size alternating fills halve
        // the contrast of every other row, and the hairline below already
        // separates them - striping is a small-type habit that hurts here.
        // (ApplyLook paints alternate rows the same as the rest.)
        _grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        RebuildKpiColumns();

        // The leaderboard and sponsor slide occupy the same area, with
        // only one visible at a time - wrapping them in a container keeps
        // the swap simple and avoids either fighting the other for dock space.
        _pnlLeaderboardArea.Dock = DockStyle.Fill;

        _picSponsor.Dock = DockStyle.Fill;
        _picSponsor.SizeMode = PictureBoxSizeMode.Zoom;
        _picSponsor.Visible = false;

        _pnlLeaderboardArea.Controls.Add(_picSponsor);
        _pnlLeaderboardArea.Controls.Add(_grid);

        // On-deck bar: what's coming up next, for calling racers to the table.
        _pnlOnDeck.Dock = DockStyle.Bottom;
        _lblOnDeck.Dock = DockStyle.Fill;
        _lblOnDeck.TextAlign = ContentAlignment.MiddleCenter;
        _lblOnDeck.Text = string.Empty;
        _pnlOnDeck.Controls.Add(_lblOnDeck);
        _pnlOnDeck.Visible = _schedule is not null;

        _lblWarning.Dock = DockStyle.Top;
        _lblWarning.AutoSize = false;
        _lblWarning.TextAlign = ContentAlignment.MiddleCenter;
        _lblWarning.Visible = false;

        Controls.Add(_pnlLeaderboardArea);
        Controls.Add(_pnlOnDeck);
        Controls.Add(_pnlCountdown);
        Controls.Add(_lblWarning);
        Controls.Add(_pnlTop);
        Controls.Add(_lblHint);

        ApplyScale(force: true);
    }

    /// <summary>
    /// The inside of the 1280x800 window this screen was laid out for, at
    /// the normal size: every band, face and gap below is that size when the
    /// window is, and grows or shrinks with it (ApplyScale).
    /// </summary>
    private const int DesignClientWidth = 1264;
    private const int DesignClientHeight = 760;

    /// <summary>How far the screen grows or shrinks at most - so a window dragged very small, or a huge wall, still shows something sensible.</summary>
    private const float LeastScale = 0.6f;
    private const float MostScale = 3f;

    /// <summary>How big everything is drawn against the size it was laid out for - see ApplyScale.</summary>
    private float _scale = 1f;

    /// <summary>
    /// Sizes the screen to its window: every band, column, gap and face is
    /// the size it was laid out for in a 1280x800 window, times how much
    /// bigger or smaller the window is - the lesser of the two ways, so
    /// nothing runs off either edge. Run as the window opens and whenever
    /// it is resized - full screen (F11) on the TV included.
    ///
    /// Why: it was laid out for 1280x800 and stayed that size whatever the
    /// window was, so full screen on a 1080p TV the writing was small, with
    /// a band of empty screen round it - on the screen that is meant to be
    /// read from across the room. It does not follow the text size the
    /// laptop is set to (Ctrl + and -): that is the director's, for the
    /// laptop's screen, and the crowd is not at the laptop (Asher, 1 Oct).
    /// </summary>
    private void ApplyScale(bool force = false)
    {
        var client = ClientSize;

        if (client.Width <= 0 || client.Height <= 0)
        {
            return;
        }

        var scale = Math.Clamp(
            Math.Min(
                client.Width / (float)UiLayout.AtDpi(DesignClientWidth),
                client.Height / (float)UiLayout.AtDpi(DesignClientHeight)),
            LeastScale,
            MostScale);

        // Not for a pixel or two: a window being dragged to a new size would
        // make every face again at each step.
        if (!force && Math.Abs(scale - _scale) < 0.02f)
        {
            return;
        }

        _scale = scale;

        SuspendLayout();

        try
        {
            _pnlTop.Height = Sized(132);
            _lblRaceState.Height = Sized(32);
            _logoBox.Width = _pnlTop.Height;
            _logoBalance.Width = _pnlTop.Height;

            _pnlCountdown.Height = Sized(118);
            _lblElapsed.Width = Sized(320);

            _grid.ColumnHeadersHeight = Math.Max(1, Sized(38));
            _grid.DefaultCellStyle.Padding = new Padding(Sized(18), Sized(4), Sized(8), Sized(4));
            _grid.RowTemplate.Height = Math.Max(1, Sized(64));

            foreach (DataGridViewRow row in _grid.Rows)
            {
                row.Height = _grid.RowTemplate.Height;
            }

            _pnlOnDeck.Height = Sized(62);
            _lblWarning.Height = Sized(84);

            // The faces made again at this size; the old ones disposed once
            // nothing wears them.
            var old = _ownedFonts.ToList();
            _ownedFonts.Clear();

            _lblRaceState.Font = Face(AppTheme.BoldFont.FontFamily, 13F);
            _lblAnnouncement.Font = Face(AppTheme.BoldFont.FontFamily, 26F);
            _lblCountdown.Font = Face(AppTheme.BoldFont.FontFamily, 56F);
            _lblElapsed.Font = Face(AppTheme.BoldFont.FontFamily, 56F);
            _grid.ColumnHeadersDefaultCellStyle.Font = Face(AppTheme.BoldFont.FontFamily, 12F);
            _grid.DefaultCellStyle.Font = Face(AppTheme.BodyFont.FontFamily, 24F);
            _lblOnDeck.Font = Face(AppTheme.BoldFont.FontFamily, 20F);
            _lblWarning.Font = Face(AppTheme.BoldFont.FontFamily, 30F);

            foreach (var font in old)
            {
                font.Dispose();
            }
        }
        finally
        {
            ResumeLayout(true);
        }

        int Sized(int pixels) => (int)Math.Round(UiLayout.AtDpi(pixels) * _scale);

        Font Face(FontFamily family, float points) =>
            OwnFont(new Font(family, Math.Max(1f, points * _scale), FontStyle.Bold));
    }

    protected override void OnClientSizeChanged(EventArgs e)
    {
        base.OnClientSizeChanged(e);

        // Before the window is built, there is nothing to size yet.
        if (_ownedFonts.Count > 0)
        {
            ApplyScale();
        }
    }

    /// <summary>Shows whatever's next on the day schedule, so racers can be called to the table. Hidden entirely when no schedule was passed in.</summary>
    private void RefreshOnDeck()
    {
        if (_schedule is null)
        {
            return;
        }

        var nextUp = _schedule.NextUp;
        if (nextUp is null)
        {
            _lblOnDeck.Text = string.Empty;
            return;
        }

        var parts = new[] { nextUp.ScheduledTime, nextUp.ClassName, nextUp.Description }
            .Where(s => !string.IsNullOrWhiteSpace(s));
        // All three fields default to empty, so an entry with none of them
        // set printed a bare "ON DECK:" across the spectator screen. The
        // schedule page already falls back to "(untitled entry)" for the
        // same state; this is the surface that did not.
        var summary = string.Join("  \u2022  ", parts);

        _lblOnDeck.Text = summary.Length > 0 ? $"ON DECK:  {summary}" : string.Empty;
    }

    /// <summary>
    /// Kicks off the leaderboard/sponsor rotation, if sponsors are
    /// configured and enabled. The timer alternates between the two: it
    /// fires after LeaderboardSeconds to show a slide, then after
    /// SlideSeconds to go back, advancing through the slide list each time.
    /// </summary>
    private void StartSponsorRotationIfConfigured()
    {
        if (_sponsors is null || !_sponsors.Current.Enabled || _sponsors.SlideFileNames.Count == 0)
        {
            return;
        }

        // Clamped before the "* 1000" - see SponsorIntervalMs.
        _sponsorTimer.Interval = SponsorIntervalMs(_sponsors.Current.LeaderboardSeconds);
        _sponsorTimer.Tick += SponsorTimer_Tick;
        _sponsorTimer.Start();
    }

    private void SponsorTimer_Tick(object? sender, EventArgs e)
    {
        if (_sponsors is null)
        {
            return;
        }

        if (_showingSponsor)
        {
            ShowLeaderboardAgain();
            return;
        }

        // Don't cut away from live standings mid-race if configured not to -
        // just wait and try again after another full leaderboard interval.
        if (_sponsors.Current.PauseDuringRace && _race.State == RaceState.Racing)
        {
            return;
        }

        ShowNextSponsorSlide();
    }

    private void ShowNextSponsorSlide()
    {
        if (_sponsors is null || _sponsors.SlideFileNames.Count == 0)
        {
            return;
        }

        _sponsorIndex = (_sponsorIndex + 1) % _sponsors.SlideFileNames.Count;
        var path = _sponsors.ResolvePath(_sponsors.SlideFileNames[_sponsorIndex]);
        if (path is null)
        {
            return; // missing file - skip this slot rather than showing a blank screen
        }

        try
        {
            // Only a slide no bigger than the Sponsors page takes in, sized
            // from its header before anything is decoded (see
            // SponsorStore.ReadSlide). This used to decode whatever the file
            // held, on the UI thread, each time the slide came round - so
            // one too big to decode froze the Big Screen mid-event, again
            // every rotation. It is skipped like an unreadable one.
            if (SponsorStore.ReadSlide(path) is not { } bytes)
            {
                return;
            }

            var previous = _picSponsor.Image;
            // Copied into a Bitmap of its own before the stream closes - an
            // Image from FromStream keeps reading its stream for life, and
            // this one's is disposed at the end of the block. Orientation is
            // fixed first, as the copy doesn't carry the EXIF tag. (An
            // animated GIF shows its first frame.)
            using var stream = new MemoryStream(bytes);
            using var decoded = Image.FromStream(stream);
            TrackAttachmentStore.ApplyExifOrientation(decoded);
            _picSponsor.Image = new Bitmap(decoded);
            previous?.Dispose();
        }
        catch
        {
            return; // unreadable image - stay on the leaderboard
        }

        _grid.Visible = false;
        _picSponsor.Visible = true;
        _showingSponsor = true;
        _sponsorTimer.Interval = SponsorIntervalMs(_sponsors!.Current.SlideSeconds);
    }

    private void ShowLeaderboardAgain()
    {
        _picSponsor.Visible = false;
        _grid.Visible = true;
        _showingSponsor = false;
        _sponsorTimer.Interval = SponsorIntervalMs(_sponsors!.Current.LeaderboardSeconds);
    }

    /// <summary>
    /// A sponsor rotation time in milliseconds, kept to 1 second .. 1 hour.
    /// The seconds come from the sponsors file, and a hand-edited value past
    /// about 2.1 million overflowed the "* 1000" into a negative Interval,
    /// which the timer throws on - taking the Big Screen down as it opened.
    /// </summary>
    private static int SponsorIntervalMs(int seconds) => Math.Clamp(seconds, 1, 3600) * 1000;

    private void OnRaceEvent(RacerStanding standing, bool isValid, bool isFastestLap) => SafeInvoke(RefreshAll);

    /// <summary>Manual lap and penalty changes no longer arrive as lap events, so they need their own refresh.</summary>
    private void OnStandingAdjusted(RacerStanding standing) => SafeInvoke(RefreshAll);

    private void OnAnnouncementTextReady(string text)
    {
        _lastAnnouncementUtc = DateTime.UtcNow;
        SafeInvoke(() => _lblAnnouncement.Text = text);
    }

    private void SafeInvoke(Action action)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }
        if (InvokeRequired)
        {
            try
            {
                BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
                // The window's handle went away between the check above and
                // the post - it is closing. The announcer raises its text
                // from background threads (the recap, the streak calls), and
                // this used to throw back into whichever of them was
                // speaking, as OnDisplaysChanged already guards against.
            }

            return;
        }
        action();
    }


    /// <summary>Rebuilds the leaderboard columns from the saved Big Screen KPI selection.</summary>
    private void RebuildKpiColumns()
    {
        _kpiColumns = KpiCatalog.Resolve(_settings?.BigScreenKpis ?? new List<string>(), KpiCatalog.DefaultBigScreen);

        _grid.Columns.Clear();
        foreach (var kpi in _kpiColumns)
        {
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = kpi.Key,
                HeaderText = kpi.Label,
                FillWeight = kpi.Weight,
            });
        }
    }

    private void RefreshAll()
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        _lblRaceState.Text = _race.IsWarmingUp
            ? "WARM-UP"
            : $"{_race.State.ToString().ToUpperInvariant()} — {_race.Flag.ToString().ToUpperInvariant()} FLAG";

        RefreshAnnouncementFallback();
        _lblElapsed.Text = ElapsedText();

        RefreshOnDeck();

        _grid.Rows.Clear();
        var ordered = _race.GetOrderedStandings();
        _kpiTracker.Observe(ordered, _race.SessionGeneration);

        var context = new KpiContext
        {
            Ordered = ordered,
            Profiles = _profiles,
            Tracker = _kpiTracker,
            FastestOverall = ordered.Where(s => s.FastestLapMs > 0).Select(s => s.FastestLapMs).DefaultIfEmpty(0).Min(),
            StreakThreshold = _race.StreakThreshold,
            SectorLengthsFeet = _race.SectorLengthsFeet,

            // Shared with the Race Control leaderboard, and cached there -
            // so putting the Qual column on both boards is one calculation,
            // not two.
            Projection = _projector?.Current()?.Slots,

            // Without this the Badges column - which the Big Screen column
            // picker offers like any other - showed "-" for every driver
            // while Race Control and the web board showed real counts.
            Achievements = _achievements?.Invoke(),
        };

        for (var i = 0; i < ordered.Count; i++)
        {
            var values = _kpiColumns.Select(k => k.Compute(ordered[i], i, context)).ToList();
            var rowIndex = _grid.Rows.Add(values.Select(v => (object)v.Text).ToArray());

            for (var col = 0; col < values.Count; col++)
            {
                // Only the positive highlights carry over here - a big
                // screen read from across the room wants the fast lap to
                // stand out, not a wall of red warnings.
                if (values[col].Emphasis is KpiEmphasis.Good or KpiEmphasis.Highlight)
                {
                    // The selection colour too: the grid selects its first
                    // row on its own, and the leader's fast lap was painted
                    // in plain text colour because of it.
                    var style = _grid.Rows[rowIndex].Cells[col].Style;
                    style.ForeColor = HighlightColor;
                    style.SelectionForeColor = HighlightColor;
                }
            }
        }

        RefreshCountdownOnly();
    }

    /// <summary>
    /// The announcement banner previously held a static "Waiting for the
    /// race to start..." placeholder that was only ever replaced when a
    /// voice announcement fired - so with the announcer muted, no voice
    /// configured, or simply no announcement yet, it stayed on that line
    /// mid-race. It now falls back to describing the actual race state,
    /// and only yields to an announcement for a few seconds after one
    /// arrives so those still get their moment on screen.
    /// </summary>
    private void RefreshAnnouncementFallback()
    {
        if (_lastAnnouncementUtc is not null && (DateTime.UtcNow - _lastAnnouncementUtc.Value).TotalSeconds < 6)
        {
            return; // a recent announcement is still showing
        }

        _lblAnnouncement.Text = _race.State switch
        {
            RaceState.Idle or RaceState.Finished when _race.IsWarmingUp => "WARM-UP — FREE LAPS, NOT COUNTED",
            RaceState.Racing when _race.Flag == FlagState.Red => "RED FLAG — RACE STOPPED",
            RaceState.Racing when _race.Flag == FlagState.Yellow => "YELLOW FLAG — CAUTION",
            RaceState.Racing => "RACE IN PROGRESS",
            RaceState.Staging => "STAGING — GET READY",
            RaceState.Countdown => "COUNTDOWN — STAND BY",
            RaceState.Finished => "RACE COMPLETE",
            _ => "Waiting for the race to start...",
        };
    }


    /// <summary>Live elapsed race time, blank when no race is underway.</summary>
    private string ElapsedText()
    {
        if (_race.RaceStartUtc is null || _race.State is RaceState.Idle)
        {
            return string.Empty;
        }

        var elapsed = _race.ElapsedRaceTime;
        var text = $"{(int)elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";

        // Make it obvious the clock is intentionally frozen, not broken.
        return _race.Flag == FlagState.Red ? text + "  (PAUSED)" : text;
    }

    /// <summary>Puts the warning up across the screen - see _lblWarning. Hidden again by the refresh tick.</summary>
    private void ShowWarningBanner(RacerStanding standing, RaceWarning warning)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        var name = _profiles.Get(standing.TransponderId)?.DriverName;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = standing.TransponderId;
        }

        var reason = string.IsNullOrWhiteSpace(warning.Reason) ? string.Empty : $" \u2014 {warning.Reason.ToUpperInvariant()}";

        var blackFlag = warning.Action == WarningAction.BlackFlag;
        var cost = warning.Action switch
        {
            WarningAction.TimePenalty => $"  (+{warning.PenaltyMs / 1000.0:0.#}s)",
            _ => string.Empty,
        };

        _lblWarning.Text = blackFlag
            ? $"\u2691 BLACK FLAG \u2014 {name.ToUpperInvariant()}{reason}"
            : $"\u26A0 WARNING {warning.Number} \u2014 {name.ToUpperInvariant()}{reason}{cost}";

        _lblWarning.BackColor = blackFlag ? Color.Black : Color.FromArgb(250, 204, 21);
        _lblWarning.ForeColor = blackFlag ? Color.White : Color.Black;
        _lblWarning.Visible = true;
        _warningShownUtc = DateTime.UtcNow;
        _bannerIsWarning = true;
    }

    /// <summary>
    /// A lap's sectors came in: when one of them is now the quickest anyone
    /// has gone through that sector this session, it goes up across the
    /// screen - "FASTEST SECTOR 1 - MAYA 12.31". Not for the very first
    /// sector times of the session, which are fastest by default, and never
    /// over a warning that is still showing.
    /// </summary>
    private void ShowFastestSector(RacerStanding standing, long[] sectors)
    {
        if (IsDisposed || Disposing || GhostService.IsGhost(standing.TransponderId)
            || (_bannerIsWarning && _warningShownUtc is not null))
        {
            return;
        }

        var field = _race.GetOrderedStandings();

        if (field.Sum(s => Sectors.LapsOf(s).Count()) < 2
            || Sectors.SessionBests(field, sectors.Length) is not { } bests)
        {
            return;
        }

        var sector = -1;

        for (var i = 0; i < sectors.Length; i++)
        {
            if (sectors[i] == bests[i])
            {
                sector = i;
            }
        }

        if (sector < 0)
        {
            return;
        }

        var name = _profiles.Get(standing.TransponderId)?.DriverName;

        if (string.IsNullOrWhiteSpace(name))
        {
            name = standing.TransponderId;
        }

        _lblWarning.Text = $"\u25C6 FASTEST SECTOR {sector + 1} \u2014 {name.ToUpperInvariant()}  {Sectors.Format(sectors[sector])}";
        _lblWarning.BackColor = HighlightColor;
        _lblWarning.ForeColor = Color.White;
        _lblWarning.Visible = true;
        _warningShownUtc = DateTime.UtcNow;
        _bannerIsWarning = false;
    }

    private void RefreshCountdownOnly()
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (_warningShownUtc is { } shown && DateTime.UtcNow - shown > WarningBannerTime)
        {
            _lblWarning.Visible = false;
            _warningShownUtc = null;
        }
        _lblCountdown.Text = _race.GetCountdownText();
        _lblElapsed.Text = ElapsedText();

        // Also refresh the banner here, not just on lap events - otherwise
        // it goes stale between laps, and never recovers once a displayed
        // announcement's few seconds are up.
        _lblRaceState.Text = _race.IsWarmingUp
            ? "WARM-UP"
            : $"{_race.State.ToString().ToUpperInvariant()} — {_race.Flag.ToString().ToUpperInvariant()} FLAG";
        RefreshAnnouncementFallback();
    }

    private void ToggleFullscreen()
    {
        if (_isFullscreen)
        {
            FormBorderStyle = _restoreBorderStyle;
            WindowState = _restoreWindowState;
            _isFullscreen = false;
        }
        else
        {
            _restoreBorderStyle = FormBorderStyle;
            _restoreWindowState = WindowState;
            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            _isFullscreen = true;
        }
    }

    /// <summary>
    /// The club logo, fitted and centred, on a rounded plate when it needs
    /// one. Drawn by hand rather than with a PictureBox so the plate can be
    /// its own rounded shape with a margin round the logo - a PictureBox
    /// could only colour its whole rectangle.
    /// </summary>
    private sealed class LogoBox : Control
    {
        private const int Inset = 10;
        private const int PlatePadding = 6;
        private const int PlateRadius = 14;

        private Image? _logo;
        private Color? _plate;

        public LogoBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint
                     | ControlStyles.ResizeRedraw, true);
        }

        /// <summary>Takes the image over (and disposes the one it had). Null clears it.</summary>
        public void SetLogo(Image? logo, Color? plate)
        {
            var previous = _logo;
            _logo = logo;
            _plate = plate;

            if (previous is not null && !ReferenceEquals(previous, logo))
            {
                previous.Dispose();
            }

            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (_logo is null)
            {
                return;
            }

            var box = new Rectangle(Inset, Inset, Width - Inset * 2, Height - Inset * 2);

            if (box.Width <= 0 || box.Height <= 0 || _logo.Width <= 0 || _logo.Height <= 0)
            {
                return;
            }

            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

            if (_plate is Color plate)
            {
                using (var path = AppTheme.RoundedRect(box, PlateRadius))
                using (var brush = new SolidBrush(plate))
                {
                    g.FillPath(brush, path);
                }

                box.Inflate(-PlatePadding, -PlatePadding);
            }

            var scale = Math.Min((float)box.Width / _logo.Width, (float)box.Height / _logo.Height);
            var width = _logo.Width * scale;
            var height = _logo.Height * scale;

            g.DrawImage(_logo, new RectangleF(
                box.Left + (box.Width - width) / 2f,
                box.Top + (box.Height - height) / 2f,
                width,
                height));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _logo?.Dispose();
                _logo = null;
            }

            base.Dispose(disposing);
        }
    }
}
