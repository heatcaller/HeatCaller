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
    //   highlight  = Accent                  (countdown, on-deck, quickest of anyone)
    //   own best   = Good                    (a driver's own best, streaks, up at the split)
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
    private Color GoodColor => _look.Good;

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

    private readonly RaceTiming _race;
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

    /// <summary>
    /// The sponsor's NAME, large and centred, for the length of a read
    /// they have no picture for (12 Oct 2026).
    ///
    /// The no-picture answer is not a blank screen and not the standings:
    /// it is the sponsor's name in front of the room while it is being
    /// read out, which is most of what a slide was doing for them anyway.
    /// See SponsorOnAir.
    /// </summary>
    private readonly Label _lblSponsorRead = new();

    /// <summary>
    /// True while a sponsor's READ owns the screen - which is not the same
    /// as <see cref="_showingSponsor"/> and must not be, or the rotation's
    /// own timer would call the slot over and move on mid-sentence. See
    /// UpdateSponsorRead.
    /// </summary>
    private bool _showingRead;
    private readonly Panel _pnlLeaderboardArea = new();

    // The podium videos' QR code strip (21 Oct 2026, PodiumVideoQr) is gone:
    // 28 Oct 2026, asked for by Asher, the videos stay on the laptop and are
    // no longer on the club's page for a phone to download.
    private readonly System.Windows.Forms.Timer _sponsorTimer = new();
    private int _sponsorIndex = -1;
    private bool _showingSponsor;

    /// <summary>
    /// The video player, made the first time a video slide comes round and
    /// kept for the window's life - see SponsorVideoSurface. Null until
    /// then, so a club with only pictures never loads LibVLC at all.
    /// </summary>
    private SponsorVideoSurface? _video;

    /// <summary>
    /// This window's console lines, for whoever opened it to pass on to the
    /// developer console.
    ///
    /// ADDED 11 Oct 2026 for one reason: the sponsor VIDEO path had no
    /// logging anywhere in it. SponsorVideo computes a careful English
    /// reason for every way it can fail - LibVLC's files missing from
    /// beside the exe, the player refusing to start, a file it cannot read
    /// - and then threw all of them away, because nothing ever read
    /// Problem. A club whose advert did not play could not tell "this PC
    /// cannot play video at all" from "that file is broken" from "the
    /// rotation never reached it", and neither could anyone helping them.
    ///
    /// Only the video path uses it so far. The rest of this window's
    /// silence is deliberate: a slide that comes round every twenty
    /// seconds does not want a line each time.
    /// </summary>
    internal event Action<string>? StatusMessage;

    /// <summary>
    /// Whether this window has already said that video cannot play on this
    /// PC - see ShowSponsorVideo, which explains why it is said once.
    /// </summary>
    private bool _videoProblemSaid;

    /// <summary>
    /// Whether this window has already asked for the video player to be
    /// warmed up - see WarmTheVideoPlayerIfNeeded. Asked once per window
    /// rather than on the tick that notices the first video slide, because
    /// the engine is loaded once per RUN of the app.
    /// </summary>
    private bool _videoWarmAsked;

    /// <summary>
    /// Whether this window has already said that a slide was skipped
    /// because the video player was still starting. Once per window, like
    /// _videoProblemSaid and for the same reason - the warm-up happens once
    /// and the sentence is only interesting the first time.
    /// </summary>
    private bool _videoWarmingSaid;

    /// <summary>
    /// Whether this window currently holds an ad break open (AdBreak.Begin
    /// without its End). Kept so the release happens exactly once, from
    /// whichever comes first: the video ending, the rotation moving on, or
    /// the window closing.
    /// </summary>
    private bool _adBreakHeld;

    /// <summary>
    /// Whether a sponsor video is on screen that holds no ad break open -
    /// silent, or with the music left up. Its end still owes the ad-break
    /// read (see AdBreak.QuietVideoEnded), and this is how it is counted
    /// exactly once, the way _adBreakHeld is.
    /// </summary>
    private bool _quietVideoShown;

    /// <summary>
    /// Whether the sponsor video on screen is playing its own soundtrack.
    /// Set once it is really on air, cleared by StopSponsorVideo - see
    /// StopSoundingVideoIfMuted, which is what reads it.
    /// </summary>
    private bool _videoHasSound;

    /// <summary>
    /// Whether this window is carrying the after-race commercial break: the
    /// sponsors running one slide straight into the next, rather than taking
    /// a turn each with the standings.
    ///
    /// Set and cleared only by <see cref="UpdateCommercialBreak"/>, which
    /// asks CommercialBreak on every tick rather than being told - so there
    /// is nothing remembered in here that can go stale, which is the whole
    /// of the argument for how the screen is always given back. See the note
    /// on that method.
    /// </summary>
    private bool _breakHasTheScreen;

    /// <summary>
    /// How many sponsor slots this window has started since the break took
    /// the screen. Once it reaches the club's slide count the rotation has
    /// been round once, which is what tells Race Control that the break has
    /// nothing left to show and should end rather than play them again - see
    /// CommercialBreak.EverySponsorShown.
    ///
    /// Slots STARTED, not slides seen: a missing file or an unreadable
    /// picture is skipped (see ShowNextSponsorSlide) and still counts, so a
    /// club whose slides have all gone walkabout gets one pass and an ending
    /// rather than a rotation going round for ever finding nothing.
    /// </summary>
    private int _breakSlidesShown;

    /// <summary>
    /// The longest a video slide may hold the screen before the rotation
    /// takes it back, whatever the file says. A video that stalls part way,
    /// or one whose container lies about its length, would otherwise keep
    /// the standings off the wall for the rest of the night - the timer is
    /// the backstop behind SponsorVideoSurface.Finished, not the normal way
    /// a video ends.
    /// </summary>
    private const int VideoBackstopMs = 5 * 60 * 1000;

    private readonly Label _lblAnnouncement = new();
    private DateTime? _lastAnnouncementUtc;

    /// <summary>
    /// Who the latest spoken line was about, as the band writes them -
    /// "Hargrove · Bellamy", or empty where the line named nobody.
    ///
    /// REPLACED BY EVERY LINE, including one that names nobody. The band
    /// has to be about what the room is hearing NOW: a flag call or a
    /// countdown names no driver, and leaving the last battle's two names
    /// up underneath it would be the screen contradicting the voice. See
    /// LowerThird.
    ///
    /// Written on the UI thread from OnAnnouncementTextReady's SafeInvoke
    /// and read on the UI thread by UpdateOnAir, so there is nothing to
    /// guard.
    /// </summary>
    private string _sayingAbout = string.Empty;

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

    // HeatCaller's mark, right of the countdown, as wide as the elapsed
    // clock on its left so the countdown stays in the middle - see
    // HeatCallerBug.
    private readonly HeatCallerBug _hcBug = new();

    // Held in a field so it can be taken off the static event again when the
    // window closes - a static event would otherwise keep every Big Screen
    // ever opened alive for the rest of the day.
    private readonly Action _displaysChangedHandler;
    private bool _displaysSubscribed;

    // The grid roll call (RollCall): while a race is staged, counting down
    // or warming up, the board gives way to a card for each car due in it,
    // each turning green as that car is heard. Null watch or roster - the
    // Big Screen opened from Practice - and it never shows.
    private readonly TransponderWatch? _watch;
    private readonly Func<IReadOnlyList<string>>? _roster;
    private readonly RollCallBoard _rollCall = new();

    /// <summary>When the roll call now showing began, less its grace - passes from before it don't count. Null while it isn't showing.</summary>
    private DateTime? _rollCallSinceUtc;

    // The live track map (TrackMapView) down the right of the board, when
    // the layout being raced on has a plan with a racing line. M shows and
    // hides it. Null source - the Big Screen opened from Practice - and it
    // never shows.
    private readonly Func<(TrackLine? Line, IReadOnlyList<TrackPositions.Car> Cars)>? _trackMap;
    private readonly TrackMapView _mapView = new();
    private bool _mapWanted = true;

    /// <summary>How much of the board's width the map takes.</summary>
    private const float MapShare = 0.36f;

    /// <summary>
    /// The between-races cards (AttractMode): up when no race is on, or a
    /// finished one's board has had its time - never over the roll call or a
    /// sponsor slide. <see cref="_attractShowing"/> while they are up.
    /// </summary>
    private readonly AttractBoard _attractBoard = new();
    private readonly Func<IReadOnlyList<AttractCard>>? _attract;
    private bool _attractShowing;
    private DateTime? _finishedSinceUtc;

    /// <summary>
    /// The championship as it would stand if the race ended now
    /// (ChampionshipLive), shown in the bottom bar in place of On Deck while
    /// a race that counts towards one is on. <see cref="_tickerShowing"/>
    /// while it is.
    /// </summary>
    private readonly Func<IfItEndedNow?>? _ifItEndedNow;
    private bool _tickerShowing;

    /// <summary>
    /// The on-air lower third (OnAirTreatment): a band at the very foot of
    /// the window while the announcer's show is on air, below even the On
    /// Deck bar, so the room sees a broadcast as well as hearing one.
    ///
    /// A band of its own rather than anything over the board, because a race
    /// always wins this screen - see the long note on OnAirTreatment for the
    /// argument and for what was turned down.
    /// </summary>
    private readonly OnAirBar _onAir = new();

    /// <summary>
    /// Held in a field so it can be taken off ShowOnAir's static event again
    /// when the window closes - the same reason the look's handler is, and
    /// with a worse ending: ShowOnAir is run by the announcer screen's
    /// director and outlives every Big Screen ever opened, so a dead form
    /// left on it is both a leak and a handler marshalling into a window
    /// that has been disposed.
    /// </summary>
    private readonly Action<bool> _onAirChangedHandler;
    private bool _onAirSubscribed;

    /// <summary>
    /// The captions strip (22 Oct 2026, driver experience, item 9): the
    /// announcer's latest line as text along the very foot of the window,
    /// for anybody in the room who can't hear the PA. Up while
    /// AppSettings.BigScreenCaptions is on (key C); see UpdateCaptions.
    /// </summary>
    private readonly Label _lblCaption = new();

    /// <summary>The number of the caption line the strip shows, so it is only re-set when a new one is heard.</summary>
    private long _captionShownN;

    /// <summary>The line the strip shows, kept for the fade - null when nothing has been said since the window opened.</summary>
    private CaptionLine? _captionShown;

    /// <summary>The strength the caption was last painted at, so the 500-ms tick only repaints while it is fading.</summary>
    private double _captionStrength = -1;

    private bool _isFullscreen;
    private FormBorderStyle _restoreBorderStyle;
    private FormWindowState _restoreWindowState;

    public BigScreenForm(RaceTiming race, AnnouncementService announcer, ProfileStore profiles,
        DayScheduleStore? schedule = null, SponsorStore? sponsors = null, AppSettings? settings = null,
        QualifyingProjector? projector = null, Func<AchievementStore?>? achievements = null,
        TransponderWatch? watch = null, Func<IReadOnlyList<string>>? roster = null,
        Func<(TrackLine? Line, IReadOnlyList<TrackPositions.Car> Cars)>? trackMap = null,
        Func<IReadOnlyList<AttractCard>>? attract = null, Func<IfItEndedNow?>? ifItEndedNow = null)
    {
        _trackMap = trackMap;
        _attract = attract;
        _ifItEndedNow = ifItEndedNow;
        _watch = watch;
        _roster = roster;
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

        // The roll call's and the track map's colours, from the same look.
        ColourRollCall();

        // The crowd-screen look and the logo can be changed on Themes →
        // Looks while this window is up on the wall; it follows straight
        // away rather than waiting to be reopened.
        _displaysChangedHandler = OnDisplaysChanged;
        Looks.DisplaysChanged += _displaysChangedHandler;
        _displaysSubscribed = true;

        // The announcer's show going on and off air. Taken off again in
        // FormClosed AND in Dispose, the two places UnsubscribeDisplays is
        // called from and for the same reason - see UnsubscribeOnAir.
        _onAirChangedHandler = _ => NudgeOnAir();
        ShowOnAir.PlayingChanged += _onAirChangedHandler;
        _onAirSubscribed = true;

        // Held in fields rather than inline lambdas, because an inline
        // lambda can't be unsubscribed - these two were left attached when
        // the window closed, so a reopened-and-closed Big Screen accumulated
        // dead handlers that still fired against a disposed form.
        _stateChangedHandler = _ => SafeInvoke(RefreshAll);
        _flagChangedHandler = _ => SafeInvoke(RefreshAll);

        _race.LapProcessed += OnRaceEvent;
        _race.StandingAdjusted += OnStandingAdjusted;

        // A ghost switched off mid-race leaves the board - refreshed the same way.
        _race.StandingRemoved += OnStandingAdjusted;
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
            else if (e.KeyCode == Keys.M && _trackMap is not null)
            {
                _mapWanted = !_mapWanted;
                UpdateTrackMap();
            }
            else if (e.KeyCode == Keys.O)
            {
                // The club's switch for the on-air treatment, remembered
                // between nights - see OnAirTreatment.Wanted for why it is a
                // key on this screen rather than a tick box on a settings
                // page. Applied here and not left to the tick, so pressing
                // it in front of a room does something straight away.
                OnAirTreatment.Wanted = !OnAirTreatment.Wanted;
                UpdateOnAir();
            }
            else if (e.KeyCode == Keys.C && _settings is not null)
            {
                // The captions strip, the O key's way (22 Oct 2026, driver
                // experience): a key on the screen it changes, listed in the
                // reminder strip, and remembered between nights - in
                // AppSettings, where P0 put the switch, saved at once so a
                // club that wants captions has them next week too. Applied
                // here rather than left to the tick, so pressing it in front
                // of a room does something straight away.
                _settings.BigScreenCaptions = !_settings.BigScreenCaptions;
                _settings.Save();
                UpdateCaptions();
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

            // Before anything else that could throw: a window closed mid-ad
            // must not leave the music muted for the rest of the night, and
            // this is the only place that can put it right once the window
            // is gone.
            StopSponsorVideo();
            _video?.Dispose();
            _video = null;

            _race.LapProcessed -= OnRaceEvent;
            _race.StandingAdjusted -= OnStandingAdjusted;
            _race.StandingRemoved -= OnStandingAdjusted;
            _race.StateChanged -= _stateChangedHandler;
            _race.FlagChanged -= _flagChangedHandler;
            _race.WarningIssued -= _warningIssuedHandler;
            _race.SectorsRecorded -= _sectorsRecordedHandler;
            _race.SplitCrossed -= _splitCrossedHandler;
            _announcer.AnnouncementTextReady -= OnAnnouncementTextReady;
            UnsubscribeDisplays();
            UnsubscribeOnAir();
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

    /// <summary>
    /// Comes off ShowOnAir's static event. Safe twice, which it has to be:
    /// it is called from FormClosed and again from Dispose, exactly as
    /// UnsubscribeDisplays is, so a window disposed without ever being
    /// closed cannot stay on a static event that lives for the rest of the
    /// day.
    /// </summary>
    private void UnsubscribeOnAir()
    {
        if (_onAirSubscribed)
        {
            ShowOnAir.PlayingChanged -= _onAirChangedHandler;
            _onAirSubscribed = false;
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
            UnsubscribeOnAir();
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
        ColourRollCall();
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
        _hcBug.BackColor = CanvasColor;
        _hcBug.SetColours(AccentColor, MutedColor);

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
        _lblSponsorRead.BackColor = CanvasColor;

        // WHY (20 Oct 2026): this was AppTheme.Accent - the director's app
        // theme, never checked against this look's background and not
        // following a look change. A pale app accent on a white crowd-screen
        // look made a sponsor's name unreadable during their paid read. It
        // is the look's own highlight now, like the countdown and on-deck.
        _lblSponsorRead.ForeColor = HighlightColor;

        _pnlOnDeck.BackColor = PanelColor;
        _lblOnDeck.ForeColor = HighlightColor;

        // The captions strip on the on-air band's plate - Surface, or the
        // hairline colour in a look whose Surface is its background - with
        // its ink worked out from that plate, as the warning banner's is.
        _lblCaption.BackColor = CaptionPlate;
        _captionStrength = -1;
        UpdateCaptions();

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
        _lblHint.Text = "F11: toggle fullscreen     M: track map     O: on air     C: captions     Esc: close";
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

        _hcBug.Dock = DockStyle.Right;

        _pnlCountdown.Controls.Add(_lblCountdown);
        _pnlCountdown.Controls.Add(_lblElapsed);
        _pnlCountdown.Controls.Add(_hcBug);

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

        _lblSponsorRead.Dock = DockStyle.Fill;
        _lblSponsorRead.TextAlign = ContentAlignment.MiddleCenter;
        _lblSponsorRead.Visible = false;

        // A long sponsor name wraps rather than running off the side of a
        // crowd screen. AutoSize off, because the label is docked to fill
        // the board's place and sizing itself to its text would fight the
        // dock.
        _lblSponsorRead.AutoSize = false;
        _lblSponsorRead.AutoEllipsis = true;

        _rollCall.Dock = DockStyle.Fill;
        _rollCall.Visible = false;

        _mapView.Dock = DockStyle.Right;
        _mapView.Visible = false;
        _mapView.Source = _trackMap;

        _attractBoard.Dock = DockStyle.Fill;
        _attractBoard.Visible = false;
        _attractBoard.Source = _attract;

        _pnlLeaderboardArea.Controls.Add(_attractBoard);
        _pnlLeaderboardArea.Controls.Add(_rollCall);
        _pnlLeaderboardArea.Controls.Add(_lblSponsorRead);
        _pnlLeaderboardArea.Controls.Add(_picSponsor);
        _pnlLeaderboardArea.Controls.Add(_grid);

        // Added last, so it takes its strip down the right before the board
        // fills what is left.
        _pnlLeaderboardArea.Controls.Add(_mapView);

        // On-deck bar: what's coming up next, for calling racers to the table.
        _pnlOnDeck.Dock = DockStyle.Bottom;
        _lblOnDeck.Dock = DockStyle.Fill;

        // The championship ticker can be longer than the bar is wide: cut
        // with "..." rather than wrapped half off the bottom.
        _lblOnDeck.AutoEllipsis = true;
        _lblOnDeck.TextAlign = ContentAlignment.MiddleCenter;
        _lblOnDeck.Text = string.Empty;
        _pnlOnDeck.Controls.Add(_lblOnDeck);
        _pnlOnDeck.Visible = _schedule is not null;

        _lblWarning.Dock = DockStyle.Top;
        _lblWarning.AutoSize = false;
        _lblWarning.TextAlign = ContentAlignment.MiddleCenter;
        _lblWarning.Visible = false;

        _onAir.Dock = DockStyle.Bottom;
        _onAir.Visible = false;

        // The captions strip. Wraps onto a second line and then cuts with
        // "...", rather than running off the side - a long line is still
        // mostly read. Shown and hidden only by the switch (UpdateCaptions),
        // never per line: a strip that came and went with every sentence
        // would push the leaderboard up and down all evening.
        _lblCaption.Dock = DockStyle.Bottom;
        _lblCaption.AutoSize = false;
        _lblCaption.AutoEllipsis = true;
        _lblCaption.UseMnemonic = false;
        _lblCaption.TextAlign = ContentAlignment.MiddleCenter;
        _lblCaption.Visible = false;

        Controls.Add(_pnlLeaderboardArea);
        Controls.Add(_pnlOnDeck);

        // Added AFTER the On Deck bar on purpose. Docking is laid out from
        // the last control added to the first (see the band above), so this
        // claims the very bottom of the window and On Deck sits above it -
        // which is where a lower third belongs, under everything, and which
        // leaves the bar's championship ticker where a club already reads
        // it. Nothing else moves: the clock, the countdown, the flag band
        // and the board are all laid out before this is even considered.
        Controls.Add(_onAir);

        // After the on-air band, for the same reason (22 Oct 2026): it
        // claims the very foot of the window, where subtitles are, with the
        // band and On Deck above it. Nothing above moves while it is up.
        Controls.Add(_lblCaption);

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

        // The map's share of the width follows every resize - a window made
        // only wider leaves the scale where it is, and returns just below.
        _mapView.Width = (int)(client.Width * MapShare);

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
            _hcBug.Width = _lblElapsed.Width;

            _grid.ColumnHeadersHeight = Math.Max(1, Sized(38));
            _grid.DefaultCellStyle.Padding = new Padding(Sized(18), Sized(4), Sized(8), Sized(4));
            _grid.RowTemplate.Height = Math.Max(1, Sized(64));

            foreach (DataGridViewRow row in _grid.Rows)
            {
                row.Height = _grid.RowTemplate.Height;
            }

            _pnlOnDeck.Height = Sized(62);
            _lblWarning.Height = Sized(84);

            // Taller than the On Deck bar and shorter than the warning: a
            // lower third has to read as broadcast furniture from the back
            // of a hall, and it is also a band the leaderboard gives up for
            // the length of a show, so it takes no more room than it needs
            // to carry two lines' worth of type. The band draws itself from
            // this height (OnAirBar.Draw), so it is the right size on a
            // laptop and on a projector without being told which.
            _onAir.Height = Sized(96);

            // Room for two lines of the caption face.
            _lblCaption.Height = Sized(88);
            _lblCaption.Padding = new Padding(Sized(24), 0, Sized(24), 0);

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
            _lblCaption.Font = Face(AppTheme.BoldFont.FontFamily, 20F);

            // The sponsor being read, large and centred in the board's
            // place. Sized here with every other font on this window, so
            // it follows the scale and is disposed with them rather than
            // being built fresh on each read - see OwnFont.
            _lblSponsorRead.Font = Face(AppTheme.BoldFont.FontFamily, 72F);

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
        // The championship ticker has the bar while a race is on (UpdateTicker).
        if (_schedule is null || _tickerShowing)
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
        // Started whenever there is a sponsor store, switched on or not: the
        // Sponsors page changes the same settings while this window is up,
        // and the tick reads them each time (SponsorTimer_Tick) - slides
        // switched on start without reopening the window, and switched off
        // stop.
        if (_sponsors is null)
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

        // A READ OWNS THE SCREEN while it is on air, and the rotation's
        // own timer must not call the slot over underneath it - that is
        // what would cut a sponsor's picture off mid-sentence. See
        // UpdateSponsorRead, which puts it up and takes it down.
        if (SponsorOnAir.Showing)
        {
            return;
        }

        if (_showingSponsor)
        {
            SponsorSlotEnded();
            return;
        }

        // Switched off, or no slides, since the window opened: keep checking
        // at the board's pace, and show nothing.
        if (!_sponsors.Current.Enabled || _sponsors.SlideFileNames.Count == 0)
        {
            _sponsorTimer.Interval = SponsorIntervalMs(_sponsors.Current.LeaderboardSeconds);
            return;
        }

        // The roll call keeps the screen while it is up: a sponsor slide
        // cutting in would hide whose car hasn't been heard.
        if (_rollCallSinceUtc is not null)
        {
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

    /// <param name="leadWithVideo">
    /// Start on a video slide where the club has one, rather than on
    /// whichever slide happens to be next - see the caller in
    /// UpdateCommercialBreak, which is the only one that passes it.
    /// </param>
    private void ShowNextSponsorSlide(bool leadWithVideo = false)
    {
        if (_sponsors is null || _sponsors.SlideFileNames.Count == 0)
        {
            return;
        }

        _sponsorIndex = (_sponsorIndex + 1) % _sponsors.SlideFileNames.Count;

        if (leadWithVideo && NextVideoSlide() is { } video)
        {
            _sponsorIndex = video;
        }

        // A VIDEO ONLY PLAYS IN A COMMERCIAL BREAK (11 Oct 2026).
        //
        // The rotation used to play whatever came next, video or still, at
        // whatever moment its timer happened to fire - and a video has its
        // own soundtrack. On 11 Oct the club's advert played twice in one
        // race: once in the gap before the break had even started, and
        // again seven seconds into the recap, talking over the announcer
        // reading the result. The second one is the serious half: it ducks
        // the club's music by design (see AdBreak) and then speaks over the
        // programme, which no amount of level setting can fix.
        //
        // A still slide is different and is NOT gated: it has no sound, it
        // is what the board cycles between races anyway, and a sponsor's
        // logo up during a recap is exactly what they paid for.
        //
        // So the rule is the one a broadcast already has - adverts run in
        // the ad break - and outside a break the rotation steps over a
        // video to the next still rather than showing nothing, so a club
        // whose slides are mostly videos still gets a board.
        // A sponsor's copy being read in the break holds a still too: a
        // video started under the read is two adverts over each other
        // (16 Oct 2026 - see CommercialBreak.ReadOnAir).
        var stillsOnly = !_breakHasTheScreen || CommercialBreak.ReadOnAir;

        if (stillsOnly && NextStillSlide() is { } still)
        {
            _sponsorIndex = still;
        }

        var fileName = _sponsors.SlideFileNames[_sponsorIndex];

        // Every slide the club has is a video and no break is on. Nothing
        // is shown rather than a video being played out of turn: the
        // standings stay up, which is the right thing on the wall between
        // races, and the adverts get the screen the moment a break starts.
        if (stillsOnly && SponsorMedia.IsVideoName(fileName))
        {
            return;
        }

        var path = _sponsors.ResolvePath(fileName);
        if (path is null)
        {
            return; // missing file - skip this slot rather than showing a blank screen
        }

        // A video is played rather than decoded. Its slot ends when the
        // video does, not after SlideSeconds (see ShowSponsorVideo).
        if (SponsorMedia.IsVideoName(fileName))
        {
            ShowSponsorVideo(path);

            // Playing, or handed back because the break went while it was
            // starting - either way this slot is settled.
            if ((_video is not null && _video.View.Visible) || !CommercialBreak.Running)
            {
                return;
            }

            // THE VIDEO COULD NOT PLAY - the player still starting up, a PC
            // where it never will, or a file it turned away. The break's
            // slot goes to the next still instead: the club's slides get
            // the break rather than the standings sitting under it with
            // nothing shown, at every break of the night.
            if (NextStillSlide() is not { } instead)
            {
                return;
            }

            _sponsorIndex = instead;
            path = _sponsors.ResolvePath(_sponsors.SlideFileNames[instead]);

            if (path is null)
            {
                return;
            }
        }

        if (!PutSponsorPictureUp(path))
        {
            return; // too big, or unreadable - stay on the leaderboard
        }

        // A picture following a video: the video surface comes down and the
        // music comes back, even though the rotation never returned to the
        // leaderboard in between.
        StopSponsorVideo();

        _grid.Visible = false;
        _attractBoard.Visible = false;
        _attractShowing = false;
        _picSponsor.Visible = true;
        _showingSponsor = true;
        _sponsorTimer.Interval = SponsorIntervalMs(_sponsors!.Current.SlideSeconds);
        UpdateTrackMap();
    }

    /// <summary>
    /// The index of the club's next video slide at or after the current
    /// one, wrapping once - or null when the club has no video slides at
    /// all, which is most clubs.
    ///
    /// Searched from where the rotation already is rather than from the
    /// start, so a club with two videos does not get the same one at every
    /// break all evening.
    /// </summary>
    private int? NextVideoSlide()
    {
        if (_sponsors is null)
        {
            return null;
        }

        // And none when video cannot play on this PC at all. Leading with one
        // spent every break's first slot on nothing, all night, while the
        // log said the break was leading with an advert.
        if (VideoCannotPlayHere())
        {
            return null;
        }

        var names = _sponsors.SlideFileNames;

        // FROM ZERO WHEN NOTHING HAS BEEN SHOWN YET (13 Oct 2026).
        // _sponsorIndex starts at -1 to mean "no slide yet", and the
        // rotation itself normalises that by doing (+1) before it reads -
        // but this search and NextStillSlide below did not, so (-1 + 0) %
        // count is -1 in C# and List[-1] threw.
        //
        // Reached by opening the Big Screen while a commercial break was
        // already running: the break asks for a video before any slide has
        // been shown, so the sentinel was still -1. A crash dialog in front
        // of a crowd, from the one screen a club cannot quietly close.
        var from = Math.Max(0, _sponsorIndex);

        for (var step = 0; step < names.Count; step++)
        {
            var at = (from + step) % names.Count;

            // The file has to be THERE as well as be a video: a path that
            // no longer resolves is skipped by the rotation anyway, and
            // leading a break with it would spend the break's first slot
            // on nothing.
            if (SponsorMedia.IsVideoName(names[at]) && _sponsors.ResolvePath(names[at]) is not null)
            {
                return at;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether the video player has finished starting up and turned out
    /// not to work on this PC - natives missing, the wrong architecture, a
    /// virus scanner in the plugin folder. Settled is asked first, so the
    /// engine's lock is never waited on while it is still loading - see
    /// SponsorVideoEngine.Settled.
    /// </summary>
    private bool VideoCannotPlayHere() =>
        SponsorVideoEngine.Settled && (SponsorVideoEngine.Problem is not null || _video is { Available: false });

    /// <summary>
    /// The index of the next STILL slide at or after the current one,
    /// wrapping once - or null when every slide the club has is a video.
    ///
    /// The mirror of NextVideoSlide, and used for the opposite reason: out
    /// of a commercial break the rotation steps over a video to the next
    /// still, because a video has sound and would play over whatever the
    /// announcer is saying. See ShowNextSponsorSlide.
    /// </summary>
    private int? NextStillSlide()
    {
        if (_sponsors is null)
        {
            return null;
        }

        var names = _sponsors.SlideFileNames;

        // From zero when nothing has been shown yet, for the reason
        // NextVideoSlide gives at length.
        var from = Math.Max(0, _sponsorIndex);

        for (var step = 0; step < names.Count; step++)
        {
            var at = (from + step) % names.Count;

            if (!SponsorMedia.IsVideoName(names[at]) && _sponsors.ResolvePath(names[at]) is not null)
            {
                return at;
            }
        }

        return null;
    }

    /// <summary>
    /// Plays a video slide, full-screen in the leaderboard's area, with the
    /// music muted under it if that is how the club has it set.
    ///
    /// The slot lasts as long as the video does: the rotation's SlideSeconds
    /// is about how long a still picture is worth looking at, and cutting an
    /// ad off after eight seconds would be worse than not showing it. The
    /// timer is set to VideoBackstopMs instead, purely so a stalled video
    /// cannot keep the standings off the wall.
    ///
    /// Falls back to leaving the leaderboard up when the video can't play at
    /// all - a PC where LibVLC won't load, or a file LibVLC turns away. The
    /// slide is skipped exactly as an unreadable picture is.
    /// </summary>
    private void ShowSponsorVideo(string path)
    {
        // NOT ON THE UI THREAD, EVER (11 Oct 2026). Making the surface
        // loads LibVLC on the thread that asks, and on the club's PC that
        // took 43 seconds: the Big Screen froze in front of the crowd for
        // three quarters of a minute at the exact moment a commercial break
        // started, the watchdog wrote a 1.2 GB dump mid-break, and the
        // recap being written behind it was dropped for taking too long.
        // See SponsorVideoEngine.Settled, which sets the whole thing out.
        //
        // So the engine is warmed in the background and this slide is
        // SKIPPED while that is still going on. A still slide or the
        // standings holding the screen for one more slot costs a club
        // nothing; a frozen window costs it the race.
        if (!SponsorVideoEngine.Settled)
        {
            WarmTheVideoPlayerIfNeeded();

            if (!_videoWarmingSaid)
            {
                _videoWarmingSaid = true;

                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [sponsor] The video player is still starting up, so this advert is "
                    + "skipped - the next break will have it. (Starting it takes a few seconds the first time, and "
                    + "only the first time.)");
            }

            return;
        }

        // Cheap now: the engine is already loaded, and all this makes is a
        // player and a view.
        _video ??= MakeVideoSurface();

        if (!_video.Available)
        {
            // ONCE PER WINDOW, not once per slide. The surface's problem is
            // settled when it is made and never changes, and a rotation
            // that comes round every twenty seconds would otherwise fill
            // the console with the same sentence all evening.
            if (!_videoProblemSaid)
            {
                _videoProblemSaid = true;

                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [sponsor] Video adverts can't play on this PC, so they are skipped: "
                    + (_video.Problem ?? "the video player would not start."));
            }

            return;
        }

        // WHY (23 Oct 2026, bug hunt 10): the sidebar's Mute means silence,
        // but this read only the sponsor's "play the video's sound" box, so
        // a muted console still had the Big Screen play each advert's
        // soundtrack at full volume through LibVLC's own output - which the
        // mute has no way to reach. A muted console now plays the video
        // silent (still slides and silent videos are still wanted in a
        // muted break), and the ad-break logic below follows `sound`.
        var sound = _sponsors!.Current.PlayVideoSound && !_announcer.Muted;
        var startedAt = Environment.TickCount64;

        if (!_video.Play(path, sound))
        {
            // Per FILE, because this one is about the file rather than the
            // PC - a club with one bad advert and three good ones wants to
            // know which.
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [sponsor] The video player turned away \"{Path.GetFileName(path)}\" - it is skipped. "
                + "Check the file plays in a normal video player.");

            return;
        }

        var tookToStart = (Environment.TickCount64 - startedAt) / 1000.0;

        // THE BREAK MAY HAVE GONE WHILE THIS WAS STARTING (11 Oct 2026).
        // An advert with its own soundtrack is only ever wanted inside the
        // commercial break that asked for it. If starting it took long
        // enough for the break to end - a slow first frame, a file being
        // read off a memory stick, a PC busy rendering the recap - then by
        // the time there is a picture the programme is on air, and what the
        // room gets is an advert talking over the night's own recap. That is
        // exactly what happened on 11 Oct.
        //
        // CommercialBreak.Running rather than this window's own
        // _breakHasTheScreen: that flag is only brought up to date on the
        // tick, and the tick has not had a turn since Play was called.
        if (!CommercialBreak.Running)
        {
            _video.Stop();

            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [sponsor] \"{Path.GetFileName(path)}\" took {tookToStart:0.#}s to start and "
                + "the commercial break was over by then, so it is not shown - an advert over the recap is worse than "
                + "no advert. It gets the next break.");

            ShowLeaderboardAgain();

            return;
        }

        StatusMessage?.Invoke(
            $"{AppTime.LogStampNow()}  [sponsor] Playing the video \"{Path.GetFileName(path)}\" on the Big Screen"
            + (sound ? " with its sound." : ", silent.")
            + (tookToStart >= 1.0 ? $" It took {tookToStart:0.#}s to start." : string.Empty));

        // Only while the video actually has sound to be talked over, and
        // only when the club wants it. A silent ad over the music is a
        // perfectly reasonable thing to want.
        if (sound && _sponsors.Current.MuteMusicDuringVideo && !_adBreakHeld)
        {
            _adBreakHeld = true;
            AdBreak.Begin();
        }
        else if (!_adBreakHeld)
        {
            _quietVideoShown = true;
        }

        _grid.Visible = false;
        _attractBoard.Visible = false;
        _attractShowing = false;
        _picSponsor.Visible = false;
        _lblSponsorRead.Visible = false;
        _showingRead = false;
        _video.View.Visible = true;
        _videoHasSound = sound;

        // No BringToFront. What takes this area is decided by visibility, not
        // z-order - the board and the picture are hidden above - and bringing
        // the video forward would put it over the track map, which is docked
        // down the right and deliberately added last to stay on top of
        // whatever is showing.
        _showingSponsor = true;
        _sponsorTimer.Interval = VideoBackstopMs;
        UpdateTrackMap();
    }

    /// <summary>
    /// Asks for the video player to be loaded in the background, once,
    /// where the club has a video advert at all.
    ///
    /// ASKED ON THE TICK rather than when this window opens, so a video
    /// added on the Sponsors page while the Big Screen is up is warmed
    /// within half a second of being added rather than at the break that
    /// wants it. The cost of asking is a bool once it has been asked.
    ///
    /// Why it has to happen at all, and why in the background, is in
    /// SponsorVideoEngine.Settled.
    /// </summary>
    private void WarmTheVideoPlayerIfNeeded()
    {
        if (_videoWarmAsked || SponsorVideoEngine.Settled || _sponsors is null)
        {
            return;
        }

        var hasOne = false;

        foreach (var name in _sponsors.SlideFileNames)
        {
            if (SponsorMedia.IsVideoName(name))
            {
                hasOne = true;
                break;
            }
        }

        // No videos: nothing to warm, and nothing is remembered either -
        // the next tick asks again, which is how one added later is caught.
        if (!hasOne)
        {
            return;
        }

        _videoWarmAsked = true;

        // Reported from the pool thread it runs on, so it is marshalled -
        // StatusMessage ends up on the console, which is a control.
        SponsorVideoEngine.WarmUp(message => SafeInvoke(() => StatusMessage?.Invoke(message)));
    }

    /// <summary>
    /// Makes the video surface and puts it in the leaderboard's area, where
    /// the picture and the board already live - one visible at a time.
    ///
    /// Added here rather than in BuildLayout because making it loads LibVLC
    /// and its plugins, and most clubs will never show a video. What takes
    /// this area is decided by visibility rather than z-order, exactly as it
    /// is for the picture - see ShowSponsorVideo.
    /// </summary>
    private SponsorVideoSurface MakeVideoSurface()
    {
        var surface = new SponsorVideoSurface();

        surface.View.Dock = DockStyle.Fill;
        surface.View.Visible = false;
        _pnlLeaderboardArea.Controls.Add(surface.View);

        // Finished arrives on a LibVLC thread. SafeInvoke puts it on the UI
        // thread, which is both where the controls can be touched and - just
        // as important - off LibVLC's own callback, from inside which its
        // methods must not be called at all.
        surface.Finished += () => SafeInvoke(() =>
        {
            // The video that just ended is the one on screen, and not one
            // the rotation has already moved past.
            if (_showingSponsor && _video is not null && _video.View.Visible)
            {
                // Through the same decision the rotation's own tick makes,
                // because during a commercial break the answer is not "the
                // standings come back" - it is "the next sponsor follows
                // this one". See SponsorSlotEnded.
                SponsorSlotEnded();
            }
        });

        return surface;
    }

    /// <summary>
    /// Takes the video off the screen, stops it, and lets the music back up.
    /// Safe when no video is playing, and safe twice - which it has to be,
    /// because it is called from the video's own end, from the rotation
    /// moving on, and from the window closing.
    /// </summary>
    /// <summary>
    /// The sponsor spot on screen comes off NOW - a video stopped, sound and
    /// all, a slide taken down - and the standings come back (26 Oct 2026,
    /// for Race Control's Reset - asked for by Asher: "music and crowd and
    /// sponsor spot should be stopped with a reset as well"). Through
    /// ShowLeaderboardAgain, the one way back to the board, so the music's
    /// hold is let go exactly once (see StopSponsorVideo) and the rotation
    /// starts its wait for the next spot from now. Safe when nothing is up,
    /// and twice. UI thread.
    /// </summary>
    internal void StopSponsorSpot()
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (_showingSponsor && _sponsors is not null)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [sponsor] Reset - the sponsor spot is taken off the Big Screen.");
            ShowLeaderboardAgain();
            return;
        }

        // Nothing "showing", but a video can still be making a sound - see
        // the roll call's own stop.
        StopSponsorVideo();
    }

    private void StopSponsorVideo()
    {
        if (_video is not null)
        {
            _video.View.Visible = false;
            _video.Stop();
        }

        _videoHasSound = false;

        if (_adBreakHeld)
        {
            _adBreakHeld = false;
            AdBreak.End();
        }

        if (_quietVideoShown)
        {
            _quietVideoShown = false;
            AdBreak.QuietVideoEnded();
        }
    }

    /// <summary>
    /// Takes a sponsor video that is playing its own soundtrack off the
    /// screen once the console is muted.
    ///
    /// WHY (23 Oct 2026, bug hunt 10): ShowSponsorVideo now starts a video
    /// silent while muted, but one already playing when Mute was pressed
    /// kept its sound to the end - the sidebar's mute has no hook into this
    /// window's LibVLC player. Asked on the refresh tick, which runs on the
    /// UI thread where Muted is set.
    ///
    /// Ended through SponsorSlotEnded, the same decision the video's own
    /// end makes, rather than straight to the standings: inside a break the
    /// next sponsor follows (a following video plays silent, since the
    /// console is muted), and outside one the standings come back. Going
    /// straight to ShowLeaderboardAgain would have left the standings up for
    /// the rest of the break, because the rotation then waits a whole
    /// LeaderboardSeconds. Every route out of it stops the old video and
    /// lets the music hold go exactly once - see StopSponsorVideo.
    /// </summary>
    private void StopSoundingVideoIfMuted()
    {
        if (!_videoHasSound || !_announcer.Muted)
        {
            return;
        }

        if (_showingSponsor && _video is not null && _video.View.Visible)
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [sponsor] Muted - the sponsor video with sound is taken off the Big Screen.");

            SponsorSlotEnded();
            return;
        }

        _videoHasSound = false;
    }

    private void ShowLeaderboardAgain()
    {
        _picSponsor.Visible = false;

        // AND THE SPONSOR BEING READ. Cleared here as well as in
        // UpdateSponsorRead, because this is the one path every other part
        // of the window comes back to the board through - a read's name
        // left visible underneath a roll call or a video would be stuck
        // there until the next read.
        _lblSponsorRead.Visible = false;
        _showingRead = false;

        StopSponsorVideo();

        // Not over the roll call, while one is up - it shows the board
        // again itself once the race goes green.
        _grid.Visible = _rollCallSinceUtc is null;
        _showingSponsor = false;
        _sponsorTimer.Interval = SponsorIntervalMs(_sponsors!.Current.LeaderboardSeconds);

        // Straight back to the cards when it is their time, rather than the
        // board for half a second first.
        UpdateAttract();
    }

    /// <summary>
    /// A sponsor rotation time in milliseconds, kept to 1 second .. 1 hour.
    /// The seconds come from the sponsors file, and a hand-edited value past
    /// about 2.1 million overflowed the "* 1000" into a negative Interval,
    /// which the timer throws on - taking the Big Screen down as it opened.
    /// </summary>
    private static int SponsorIntervalMs(int seconds) => Math.Clamp(seconds, 1, 3600) * 1000;

    /// <summary>
    /// A sponsor's slot has ended - the rotation's timer running out, or a
    /// video playing itself out. What follows depends on whether the
    /// after-race commercial break is on: during a break the next sponsor
    /// follows straight on, and otherwise the standings come back for their
    /// turn exactly as they always have.
    ///
    /// ONE METHOD rather than the same decision written twice, in the
    /// timer's tick and in the video's own Finished. That is precisely where
    /// this file went wrong before: the roll call put the picture away
    /// without stopping the video, so neither of those two routes could
    /// reach StopSponsorVideo - the only caller of AdBreak.End in the app -
    /// and the club's music stayed muted for a whole heat. A second copy of
    /// a rule about what takes this screen is how that happens again.
    /// </summary>
    private void SponsorSlotEnded()
    {
        if (_sponsors is null)
        {
            return;
        }

        // No break, or nothing this window can put in one: the standings,
        // which is what this has always done.
        if (!_breakHasTheScreen
            || !_sponsors.Current.Enabled
            || _sponsors.SlideFileNames.Count == 0
            || _rollCallSinceUtc is not null)
        {
            ShowLeaderboardAgain();
            return;
        }

        if (_breakSlidesShown >= _sponsors.SlideFileNames.Count)
        {
            // Been round every sponsor the club has. The rotation does NOT
            // start again: looping would sell a sponsor impressions nobody
            // bought, and Race Control's break ends on this instead - see
            // CommercialBreak.EverySponsorShown.
            CommercialBreak.EverySponsorShown();

            // And the slide that is up HOLDS the screen until the break is
            // ended, which is the next tick or two. A station holds on its
            // last caption; it does not flash back to a spreadsheet for two
            // seconds and then go to the recap.
            //
            // A video has nothing left to hold - what is on screen at the
            // end of one is whatever its last frame happened to be, or
            // black - so for that the standings come back now.
            if (_picSponsor.Visible)
            {
                _sponsorTimer.Interval = SponsorIntervalMs(_sponsors.Current.SlideSeconds);
                return;
            }

            ShowLeaderboardAgain();
            return;
        }

        // Counted BEFORE the slide is shown, because a slot that is skipped
        // for a missing file still had its turn - see _breakSlidesShown.
        _breakSlidesShown++;

        // A VIDEO THAT HAS ENDED comes off the screen before the next slot
        // is tried. If that slot cannot show anything - its file gone, a
        // picture too big to read, a second video the player turns away -
        // ShowNextSponsorSlide shows nothing and stops nothing, and the dead
        // video was left "visible": black over the standings, the music
        // muted, and the break held open because a video still looked to be
        // playing. Only the view is hidden here, not the hold on the music:
        // a following video keeps it, and anything else lets it go below.
        if (_video is not null && _video.View.Visible)
        {
            _video.View.Visible = false;
        }

        ShowNextSponsorSlide();

        // Nothing took the screen: the standings, rather than nothing at
        // all. This is also what stops the old video and brings the music
        // back up - see ShowLeaderboardAgain.
        if (!_picSponsor.Visible && !(_video is not null && _video.View.Visible))
        {
            ShowLeaderboardAgain();
        }
    }

    /// <summary>
    /// Keeps the crowd's screen in step with the after-race commercial
    /// break: the sponsors take the screen when one starts, and the
    /// standings come back when it ends.
    ///
    /// ASKED ON THE TICK, which is the whole safety argument and the same
    /// one UpdateOnAir makes. Furniture with exactly one route off this
    /// screen stays on the moment that route is missed - the sponsor video
    /// played over the start of a heat because StopSponsorVideo was its only
    /// way off, and the roll call went round it. Here there is no route at
    /// all: twice a second this asks CommercialBreak whether a break is on
    /// and makes the screen agree, so a break that ends, one abandoned
    /// because the next race was called, one whose Race Control page has
    /// gone away, and one that started while this window was being built all
    /// come to the same place within half a second. Nothing is remembered
    /// between calls except which side of that answer the screen is already
    /// on, so there is nothing that can go stale.
    ///
    /// It also REPORTS this window back, which is how Race Control knows
    /// whether there is any point having a break at all: a break with no
    /// screen behind it is silence added in front of the recap. Reported on
    /// the tick rather than counted up when the window opens and down when
    /// it closes - the way AdBreak counts a video - because a count can be
    /// got wrong in the direction that costs something, and a report that
    /// stops arriving cannot. See CommercialBreak.SponsorScreenUp.
    /// </summary>
    /// <summary>
    /// Decodes a sponsor picture into the picture box, or false when it
    /// cannot be shown.
    ///
    /// EXTRACTED 12 Oct 2026, when the sponsor's read wanted the same
    /// picture put up for a different reason - the rotation's own slot and
    /// a read being said. Two copies of this would be two answers to "how
    /// big a picture is too big" and two places to get the stream's
    /// lifetime wrong.
    ///
    /// Only a slide no bigger than the Sponsors page takes in, sized from
    /// its header before anything is decoded (see SponsorStore.ReadSlide).
    /// This used to decode whatever the file held, on the UI thread, each
    /// time the slide came round - so one too big to decode froze the Big
    /// Screen mid-event, again every rotation.
    /// </summary>
    private bool PutSponsorPictureUp(string path)
    {
        try
        {
            if (SponsorStore.ReadSlide(path) is not { } bytes)
            {
                return false;
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

            return true;
        }
        catch
        {
            return false; // unreadable image
        }
    }

    /// <summary>
    /// Puts the sponsor on the crowd screen while their read is being
    /// said, and takes them off when it is done.
    ///
    /// WHY THIS DOES NOT USE <see cref="_showingSponsor"/>. That flag
    /// means "the rotation has the screen", and the rotation's timer calls
    /// the slot over on its own schedule - which underneath a read would
    /// move to the next slide mid-sentence. So a read has a flag of its
    /// own and the rotation stands down while one is on air (see
    /// SponsorTimer_Tick).
    ///
    /// A VIDEO KEEPS THE SCREEN, the one exception, for the reason every
    /// other path here gives: cutting an advert off part way is worse for
    /// the sponsor than the read going out over the standings. In practice
    /// the two cannot collide - a paid read stands down while a video is
    /// up - and this is the guard for the case where that ever stops being
    /// true.
    /// </summary>
    private void UpdateSponsorRead()
    {
        if (SponsorOnAir.Showing)
        {
            if (_showingRead || (_video is not null && _video.View.Visible))
            {
                return;
            }

            // The roll call keeps the screen too: a sponsor's name over it
            // would hide whose car has not been heard.
            if (_rollCallSinceUtc is not null)
            {
                return;
            }

            _showingRead = ShowTheSponsorBeingRead(SponsorOnAir.PictureFileName, SponsorOnAir.SponsorName);

            return;
        }

        if (!_showingRead)
        {
            return;
        }

        _showingRead = false;
        _lblSponsorRead.Visible = false;

        // WHY (20 Oct 2026): a read that ends INSIDE a commercial break used
        // to put the standings back for the rest of it - UpdateCommercialBreak
        // sees the break already has the screen and does not take it back, so
        // the board sat there until the rotation timer next fired (up to a
        // minute) and the remaining sponsors' slides were never counted. Now
        // the slot goes to the break's rotation instead; SponsorSlotEnded
        // still falls back to the standings when it has nothing to show.
        // CommercialBreak.Running is asked as well because _breakHasTheScreen
        // is only brought up to date later in this tick - the same reasoning
        // ShowSponsorVideo gives.
        if (_breakHasTheScreen && CommercialBreak.Running)
        {
            // WHY (20 Oct 2026): the read's own picture, where it is still
            // up, counts as a sponsor slide from here. If the break's first
            // slot showed nothing, _showingSponsor is false, and a picture
            // SponsorSlotEnded holds (or leaves up when the next slide will
            // not load) would then be skipped by the break's hand-back and
            // stay on the wall into the recap.
            if (_picSponsor.Visible)
            {
                _showingSponsor = true;
            }

            SponsorSlotEnded();
            return;
        }

        ShowLeaderboardAgain();
    }

    /// <summary>
    /// The sponsor's picture where they have one, and their name where
    /// they do not. False when neither could be shown, and then the read
    /// goes out over the standings exactly as it used to.
    /// </summary>
    private bool ShowTheSponsorBeingRead(string? pictureFileName, string sponsorName)
    {
        if (pictureFileName is { Length: > 0 }
            && _sponsors is not null
            && _sponsors.ResolvePath(pictureFileName) is { } path
            && PutSponsorPictureUp(path))
        {
            _lblSponsorRead.Visible = false;
            _grid.Visible = false;
            _attractBoard.Visible = false;
            _attractShowing = false;
            _picSponsor.Visible = true;

            return true;
        }

        // NO PICTURE, so the name - which is the answer a club gets for
        // every sponsor until they upload one, and is most of what a slide
        // was doing for them anyway.
        if (sponsorName.Length == 0)
        {
            return false;
        }

        _lblSponsorRead.Text = sponsorName.ToUpperInvariant();

        _picSponsor.Visible = false;
        _grid.Visible = false;
        _attractBoard.Visible = false;
        _attractShowing = false;
        _lblSponsorRead.Visible = true;

        return true;
    }

    private void UpdateCommercialBreak()
    {
        if (IsDisposed || Disposing || _sponsors is null)
        {
            return;
        }

        var canCarryOne = false;
        bool onBreak;

        try
        {
            // Everything a break needs of this window: the club's slides
            // switched on, at least one of them, and no roll call holding
            // the screen for whose car has not been heard yet.
            canCarryOne = _sponsors.Current.Enabled
                          && _sponsors.SlideFileNames.Count > 0
                          && _rollCallSinceUtc is null;

            if (canCarryOne)
            {
                CommercialBreak.SponsorScreenReady();

                // While there is still time: the break that wants it is
                // somewhere between now and the end of the next race.
                WarmTheVideoPlayerIfNeeded();
            }

            // And whether an advert is actually PLAYING, which is what
            // keeps a break open past the recap being ready - see
            // CommercialBreak.VideoOnScreen. Reported on the same tick and
            // for the same reason the line above is.
            if (_video is not null && _video.View.Visible)
            {
                CommercialBreak.SponsorVideoOnScreen();
            }

            onBreak = CommercialBreak.Running;
        }
        catch
        {
            // No break rather than no Big Screen - the bargain UpdateOnAir,
            // UpdateAttract, UpdateTicker and UpdateRollCall all make. The
            // sponsors file is saved from the Sponsors page while this window
            // is up, and a store caught mid-save must not take the crowd's
            // screen down with it.
            onBreak = false;
        }

        if (!onBreak || !canCarryOne)
        {
            // A RACE BEING CALLED ends a video too, whatever the break did
            // about it. Letting an advert run on under the recap is one
            // thing; under the staging calls, the countdown and the opening
            // laps, with the standings hidden and the music muted, is
            // another. The roll call stops one when it comes up, but a race
            // with nobody on its roster (or practice) has no roll call.
            // Asked on every tick, not only as the break ends, so a video
            // still running past the recap is caught when the race is.
            if (_showingSponsor
                && _video is not null
                && _video.View.Visible
                && _race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing)
            {
                ShowLeaderboardAgain();
            }

            if (!_breakHasTheScreen)
            {
                return;
            }

            _breakHasTheScreen = false;

            // Handed back NOW rather than when the slot this window happens
            // to be in runs out: the recap's opener is seconds away, and a
            // sponsor slide still up underneath it is a broadcast talking
            // over its own advert.
            //
            // A VIDEO keeps its slot, which is the one exception. Cutting an
            // advert off part way is worse for the sponsor than letting it
            // run under the recap's first line - the rule ShowSponsorVideo
            // already states - and the video's own Finished brings the
            // screen back within seconds anyway.
            if (_showingSponsor && !(_video is not null && _video.View.Visible))
            {
                ShowLeaderboardAgain();
            }

            return;
        }

        if (_breakHasTheScreen)
        {
            return;
        }

        // A READ OWNS THE SCREEN, so the break waits for it (13 Oct 2026).
        //
        // The rotation already stood down for a read - SponsorTimer_Tick
        // returns on SponsorOnAir.Showing, and UpdateSponsorRead's own
        // summary says the rotation does - but a break STARTING did not.
        // It went straight to ShowNextSponsorSlide below and put another
        // sponsor's picture over the one whose read was being said. One
        // sponsor's paid airtime carrying another sponsor's logo, which is
        // the worst thing this window can do with a sponsor's money.
        //
        // RETURNED BEFORE _breakHasTheScreen IS SET, and that is the whole
        // care in this fix. The obvious guard was to put !_showingRead
        // into canCarryOne above - and that would have been wrong twice
        // over: canCarryOne false falls into the hand-back branch, which
        // calls ShowLeaderboardAgain and would take the READ'S OWN picture
        // down, and it is also the only route to AdBreak.End and so to
        // un-muting the club's music, which this file records going wrong
        // twice before. Returning here instead leaves the break running,
        // leaves SponsorScreenReady reported, leaves the music alone, and
        // simply tries again on the next tick - by which time the read,
        // which is seconds long against a break of about a minute, is
        // over.
        if (_showingRead)
        {
            return;
        }

        // The break has just started. The sponsors take the screen NOW
        // rather than when whatever is left of LeaderboardSeconds runs out:
        // that is up to a minute by default, which would be the whole of the
        // wait this break exists to fill, spent on the standings.
        _breakHasTheScreen = true;

        // One either way: a slide already up when the break began has had
        // its slot, so the break still ends after one pass round the club's
        // sponsors rather than one pass and a slide.
        _breakSlidesShown = 1;

        // LEADING WITH A VIDEO (11 Oct 2026). A break is usually about one
        // turn of the rotation long, so with the rotation taken in file
        // order a club's one video advert came up on a break roughly never
        // - the stills either side of it had the eight seconds and the
        // screen went back to the standings. An advert is the thing a
        // commercial break is FOR, so the break starts on one where there
        // is one, and the stills fill the rest of it.
        //
        // AND IT LEADS WITH ONE EVEN IF A SLIDE IS ALREADY UP (11 Oct
        // 2026). This used to be "if nothing is showing", and between races
        // the rotation has a still slide up about half the time - so half
        // the club's breaks never asked for a video at all. They kept the
        // still that happened to be up, and the break's remaining slots ran
        // on in file order; a break is about one turn of the rotation long,
        // so the advert came up roughly never. A club that had loaded three
        // videos saw one of them, once, all evening.
        //
        // A still already up has nothing to lose by being replaced - it has
        // had its slot, and it comes round again. A VIDEO already playing
        // does: cutting one off part way is the thing ShowSponsorVideo and
        // UpdateCommercialBreak both go out of their way not to do.
        var videoPlaying = _video is not null && _video.View.Visible;
        var haveAVideoToLeadWith = !videoPlaying && NextVideoSlide() is not null;

        if (!_showingSponsor || haveAVideoToLeadWith)
        {
            ShowNextSponsorSlide(leadWithVideo: true);
        }

        // SAID OUT LOUD, once per break, because this is the one decision
        // in this window a club cannot see the workings of. "I loaded three
        // videos and only one played" was a real report, and the log it
        // came with had nothing in it either way - no video line, no
        // skipped line, nothing. Now it says what the break leads with and,
        // where that is not an advert, why not.
        //
        // From what is actually ON the screen now, not what was asked for:
        // a video that would not play used to be logged as the break
        // leading with it.
        var ledWithVideo = !videoPlaying && _video is not null && _video.View.Visible;

        StatusMessage?.Invoke(
            $"{AppTime.LogStampNow()}  [sponsor] Commercial break on the Big Screen: "
            + (videoPlaying
                ? "an advert is already playing and keeps the screen."
                : ledWithVideo
                    ? "leading with a video advert."
                    : haveAVideoToLeadWith
                        ? "the video advert could not be played just now, so the break runs the slides instead."
                    : $"no video advert to lead with - {WhyNoVideo()} The break runs the slides instead."));
    }

    /// <summary>
    /// Why a break could not lead with a video, in a club's words. Only
    /// asked when NextVideoSlide has already answered null, so it is
    /// working out WHICH of the reasons it was.
    /// </summary>
    private string WhyNoVideo()
    {
        if (_sponsors is null)
        {
            return "this window has no sponsor slides.";
        }

        var videos = 0;
        var missing = 0;

        foreach (var name in _sponsors.SlideFileNames)
        {
            if (!SponsorMedia.IsVideoName(name))
            {
                continue;
            }

            videos++;

            if (_sponsors.ResolvePath(name) is null)
            {
                missing++;
            }
        }

        if (videos == 0)
        {
            return "none of the club's sponsor slides is a video (add one on the Sponsors page).";
        }

        if (missing >= videos)
        {
            return $"all {Plural.Of(videos, "video slide")} the club has are missing their files.";
        }

        if (VideoCannotPlayHere())
        {
            return "video adverts can't play on this PC.";
        }

        return $"the club has {Plural.Of(videos, "video slide")} but none could be found just now.";
    }

    private void OnRaceEvent(RacerStanding standing, bool isValid, bool isFastestLap) => SafeInvoke(RefreshAll);

    /// <summary>Manual lap and penalty changes no longer arrive as lap events, so they need their own refresh.</summary>
    private void OnStandingAdjusted(RacerStanding standing) => SafeInvoke(RefreshAll);

    private void OnAnnouncementTextReady(string text)
    {
        _lastAnnouncementUtc = DateTime.UtcNow;

        SafeInvoke(() =>
        {
            _lblAnnouncement.Text = text;

            // AND WHO IT IS ABOUT, for the band at the bottom (11 Oct
            // 2026). Worked out here rather than on the half-second tick
            // because this is the one moment the line is known, and
            // because walking the standings twice a second for the whole
            // of a race to re-answer a question whose answer cannot have
            // changed is exactly the sort of thing that makes a club
            // laptop warm.
            //
            // Inside the SafeInvoke: the standings and the profiles belong
            // to the UI thread, and this event arrives on whichever thread
            // queued the line.
            //
            // ONLY WHILE SOMETHING IS ON AIR. A race call between shows
            // ("Hargrove takes the lead") used to be kept here too, and the
            // next show opened with that driver on its band for the seconds
            // of its opener - a name from the last race over this one. A
            // show's own lines are queued after ShowOnAir.Begin, so they
            // still get theirs.
            _sayingAbout = ShowOnAir.Playing || _onAir.Visible
                ? LowerThird.Line(LowerThird.NamesIn(text, WhoCouldBeNamed()))
                : string.Empty;
        });
    }

    /// <summary>
    /// The drivers a spoken line could be about: everyone on track, by
    /// both the name the announcer would say and the name this screen
    /// shows.
    ///
    /// ON TRACK, not every profile the club has. A recap names the
    /// finishers of the race just run and the board beside the band is
    /// showing those same cars, so a club with two hundred profiles pays
    /// for a dozen comparisons rather than two hundred - and a line that
    /// mentioned a driver who is not out cannot put a name on the band
    /// over a board that has no row for them.
    ///
    /// THE TWO NAMES ARE OFTEN DIFFERENT, which is the whole reason this
    /// returns pairs: the announcer reads the club's Announcer name where
    /// they have typed one, and the board shows the driver's own. See
    /// LowerThird.Candidate.
    /// </summary>
    private IReadOnlyList<LowerThird.Candidate> WhoCouldBeNamed()
    {
        var who = new List<LowerThird.Candidate>();

        try
        {
            foreach (var standing in _race.GetOrderedStandings())
            {
                if (_profiles.Get(standing.TransponderId) is not { } profile)
                {
                    continue;
                }

                var said = profile.SpokenName;
                var shown = string.IsNullOrWhiteSpace(profile.DriverName) ? said : profile.DriverName;

                if (!string.IsNullOrWhiteSpace(said))
                {
                    who.Add(new LowerThird.Candidate(said, shown));
                }
            }
        }
        catch
        {
            // No names on the band rather than no Big Screen - the bargain
            // every Update method on this window makes. The caption still
            // says what is playing.
        }

        return who;
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
            FastestOverall = Ghosts.FastestLapMs(ordered),
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
                if (EmphasisColour(values[col].Emphasis) is { } colour)
                {
                    // The selection colour too: the grid selects its first
                    // row on its own, and the leader's fast lap was painted
                    // in plain text colour because of it.
                    var style = _grid.Rows[rowIndex].Cells[col].Style;
                    style.ForeColor = colour;
                    style.SelectionForeColor = colour;
                }
            }
        }

        RefreshCountdownOnly();
    }

    /// <summary>
    /// The colour a board cell is painted on the Big Screen, or null for
    /// the plain text colour. The quickest of anyone in the look's accent,
    /// a driver's own good news in its green - the same two colours Race
    /// Control and the online board use. Warnings stay plain.
    /// </summary>
    /// <remarks>
    /// WHY (1 Nov 2026): both used to be the accent, so with sector columns
    /// on the board a driver's own best sector was the same blue as the
    /// session's quickest, and two drivers looked like they held it.
    /// </remarks>
    private Color? EmphasisColour(KpiEmphasis emphasis) => BigScreenEmphasis.Colour(emphasis, HighlightColor, GoodColor);

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
        // Never for a ghost car (22 Oct 2026, known issues), as the fastest
        // sector already was not: a ghost has nobody to warn, and its id
        // would go up across the screen in place of a name.
        if (IsDisposed || Disposing || GhostService.IsGhost(standing.TransponderId))
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

        // White or near-black, whichever reads on the club's accent (22 Oct
        // 2026, known issues): always white, it vanished on a pale accent -
        // a yellow or a light green look.
        _lblWarning.ForeColor = Looks.ClearestTextOn(HighlightColor);
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

        UpdateRollCall();
        UpdateAttract();
        UpdateTrackMap();
        UpdateTicker();
        UpdateOnAir();
        UpdateCaptions();

        // The sponsor whose read is going out, before the break is asked:
        // a read cannot happen while a video is on the crowd screen (the
        // announcer's paid reads stand down for one), so the two never
        // want the screen at the same moment - and if they ever did, the
        // video would keep it. See UpdateSponsorRead.
        UpdateSponsorRead();

        // Before the break is asked, so a video taken down for the mute is
        // already gone when it is - see StopSoundingVideoIfMuted.
        StopSoundingVideoIfMuted();

        // Last, so everything above has had its turn at the screen before
        // the break takes it or gives it back. Asked here for the reason
        // UpdateOnAir is - see the note on it.
        UpdateCommercialBreak();

        // Also refresh the banner here, not just on lap events - otherwise
        // it goes stale between laps, and never recovers once a displayed
        // announcement's few seconds are up.
        _lblRaceState.Text = _race.IsWarmingUp
            ? "WARM-UP"
            : $"{_race.State.ToString().ToUpperInvariant()} — {_race.Flag.ToString().ToUpperInvariant()} FLAG";
        RefreshAnnouncementFallback();
    }

    /// <summary>
    /// Shows the grid roll call while a race is on its way, and the board
    /// again once it goes green (or is reset). Runs on the half-second timer,
    /// so a car crossing the line turns its card within half a second. A
    /// roll call that fails must not take the Big Screen with it: it is put
    /// away and the board shown.
    /// </summary>
    private void UpdateRollCall()
    {
        try
        {
            var roster = _watch is not null && _roster is not null ? _roster() : null;

            var showing = roster is not null
                && RollCall.Showing(_race.State, _race.IsWarmingUp, _race.IsPracticeMode, _race.IsDemoMode, roster.Count);

            if (!showing)
            {
                HideRollCall();
                return;
            }

            if (_rollCallSinceUtc is null)
            {
                _rollCallSinceUtc = DateTime.UtcNow - RollCall.Grace;
                _rollCall.Reset();
                ColourRollCall();

                _grid.Visible = false;
                _picSponsor.Visible = false;
                _lblSponsorRead.Visible = false;
                _showingRead = false;
                _rollCall.Visible = true;

                // A slide that was up when the race was staged ends here. It
                // used to stay "showing" under the roll call, and at the
                // green HideRollCall put it back over the board for whatever
                // was left of its time - the opening laps, with long slides.
                if (_showingSponsor && _sponsors is not null)
                {
                    // A VIDEO has to be stopped, not merely stopped being
                    // "showing". Only the picture was put away here, so
                    // LibVLC carried on playing the ad's sound under the roll
                    // call and over the start of the race - and with
                    // _showingSponsor false nothing could finish the slot:
                    // neither SponsorTimer_Tick nor the video's own Finished
                    // got as far as ShowLeaderboardAgain, which is the only
                    // route to StopSponsorVideo and so the only route to
                    // AdBreak.End. The music and the crowd bed stayed muted
                    // for the whole heat, and with PauseDuringRace on they
                    // stayed muted until after it. Safe when no video is
                    // playing, and safe twice - see StopSponsorVideo.
                    StopSponsorVideo();

                    _showingSponsor = false;
                    _sponsorTimer.Interval = SponsorIntervalMs(_sponsors.Current.LeaderboardSeconds);
                }
            }

            _rollCall.ShowEntries(RollCall.Build(roster!, _watch!, t => _profiles.Get(t)?.DriverName, _rollCallSinceUtc.Value));
        }
        catch
        {
            HideRollCall();
        }
    }

    private void HideRollCall()
    {
        if (_rollCallSinceUtc is null)
        {
            return;
        }

        _rollCallSinceUtc = null;
        _rollCall.Visible = false;
        _picSponsor.Visible = _showingSponsor;
        _grid.Visible = !_showingSponsor;
    }

    /// <summary>
    /// Shows the track map beside the board when there is a line to draw,
    /// it is wanted (M) and the roll call isn't up - which takes the whole
    /// area. The map reads its cars itself, ten times a second.
    /// </summary>
    private void UpdateTrackMap()
    {
        bool show;

        try
        {
            // Not beside a sponsor slide between races either: there is no
            // race to show, and the slide would lose a third of its width.
            // Nor under a sponsor's read, for UpdateAttract's reason: the
            // read's picture sits in front of the map, so what showed was
            // the map round the edges of somebody's logo.
            show = _trackMap is not null && _mapWanted && _rollCallSinceUtc is null && !_attractShowing
                   && !_showingRead
                   && !(_showingSponsor && _race.State is not (RaceState.Staging or RaceState.Countdown or RaceState.Racing))
                   && _trackMap().Line is not null;
        }
        catch
        {
            show = false;
        }

        if (_mapView.Visible != show)
        {
            _mapView.Visible = show;
        }
    }

    /// <summary>
    /// Shows the between-races cards in place of the board when it is their
    /// time (AttractMode.Showing) and there are any to show; the board again
    /// when it isn't. The roll call and a sponsor slide both come first. A
    /// card that fails must not take the Big Screen with it: the board is
    /// shown instead.
    /// </summary>
    private void UpdateAttract()
    {
        if (_race.State == RaceState.Finished)
        {
            _finishedSinceUtc ??= DateTime.UtcNow;
        }
        else
        {
            _finishedSinceUtc = null;
        }

        bool show;

        try
        {
            var finishedFor = _finishedSinceUtc is { } since ? DateTime.UtcNow - since : TimeSpan.Zero;

            // AND NOT OVER A SPONSOR'S READ (12 Oct 2026). _showingRead
            // was added to UpdateSponsorRead and to nothing else, and the
            // attract board is the FRONT-most control in
            // _pnlLeaderboardArea - so the cards came up over a paid read's
            // picture, between races, which is exactly when both of them
            // happen. The club had promised that sponsor their name on the
            // crowd screen. Every flag that says who owns this screen has
            // to be read by every method that takes it; see the note at the
            // top of this file.
            show = _attract is not null
                && _rollCallSinceUtc is null
                && !_showingSponsor
                && !_showingRead
                && AttractMode.Showing(_race.State, finishedFor, _race.IsWarmingUp, _race.IsPracticeMode, _race.IsDemoMode);

            if (show && !_attractShowing)
            {
                // Only when there is a card to show - otherwise the board stays.
                _attractBoard.Reload();
                show = _attractBoard.HasCards;
            }
            else if (show && !_attractBoard.HasCards)
            {
                // The cards ran out while up (the last race run, say): the board again.
                show = false;
            }
        }
        catch
        {
            show = false;
        }

        if (show == _attractShowing)
        {
            return;
        }

        _attractShowing = show;

        if (show)
        {
            ColourRollCall();
            _grid.Visible = false;
            _attractBoard.Visible = true;
        }
        else
        {
            _attractBoard.Visible = false;
            _grid.Visible = _rollCallSinceUtc is null && !_showingSponsor && !_showingRead;
        }

        // The map goes with the board, and comes back with it.
        UpdateTrackMap();
    }

    /// <summary>
    /// The bottom bar during a race that counts towards a championship: the
    /// table as it would stand if the race ended now, in place of On Deck.
    /// Back to On Deck when the race is over or counts towards none.
    /// </summary>
    private void UpdateTicker()
    {
        IfItEndedNow? table = null;

        try
        {
            table = _ifItEndedNow?.Invoke();
        }
        catch
        {
            // No ticker rather than no Big Screen.
        }

        if (table is not null)
        {
            _tickerShowing = true;
            _pnlOnDeck.Visible = true;

            var text = ChampionshipLive.Ticker(table, 3);

            if (_lblOnDeck.Text != text)
            {
                _lblOnDeck.Text = text;
            }

            return;
        }

        if (_tickerShowing)
        {
            _tickerShowing = false;
            _pnlOnDeck.Visible = _schedule is not null;
            _lblOnDeck.Text = string.Empty;
            RefreshOnDeck();
        }
    }

    /// <summary>
    /// The show has gone on or off air. Raised by ShowOnAir on whatever
    /// thread started or ended it - a thread-pool one in practice, because
    /// ShowTreatment's ending awaits with ConfigureAwait(false) - and raised
    /// WHILE ShowOnAir's own lock is held. So this does the least it
    /// possibly can: it posts, and returns.
    ///
    /// BeginInvoke, NEVER Invoke. Invoke would wait for the UI thread, and
    /// the UI thread asks ShowOnAir whether a show is on twice a second
    /// (UpdateOnAir) - so a show ending on a background thread would hold
    /// that lock waiting for a UI thread that was waiting for the lock, and
    /// a race night would stop dead with the club's music silenced. The same
    /// shape of bug AdBreak and ShowOnAir both describe in their own notes,
    /// one step further along: there it was the counting that crossed
    /// threads, here it would be the SUBSCRIBER doing the waiting.
    ///
    /// Nothing is read off ShowOnAir in here either. Reading Caption would
    /// re-enter the lock this is already inside - legal, since it is
    /// re-entrant on the one thread - but there is no need: UpdateOnAir asks
    /// for itself once it is on the UI thread and the lock is long gone.
    ///
    /// And it does not throw. A subscriber that throws is swallowed on
    /// purpose, so that a bug in here can never leave the club's music down
    /// for the night - but leaning on somebody else's catch is not a design.
    /// </summary>
    private void NudgeOnAir()
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        try
        {
            BeginInvoke(new Action(UpdateOnAir));
        }
        catch
        {
            // No handle yet - a show already on air as this window is being
            // built - or the handle went away between the check above and
            // the post, which means the window is closing. Either way the
            // half-second tick asks again: this event only makes the answer
            // prompt, it is NOT how the band gets on or off the screen.
        }
    }

    /// <summary>
    /// Puts the on-air band up while the announcer's show is on air, and
    /// takes it down when it is not.
    ///
    /// ASKED ON THE TICK, not done from the event, and that is the whole
    /// safety argument. The sponsor video stayed on screen with its sound
    /// over the start of a race because StopSponsorVideo was the only route
    /// off the screen and the roll call went round it (see UpdateRollCall) -
    /// furniture with exactly one route off stays on the moment that route
    /// is missed. Here there is no route at all: twice a second this asks
    /// the show itself whether it is on and makes the band agree, so a show
    /// that ends, fails halfway, or ends while this window was being built
    /// or was not listening all come to the same place within half a second.
    /// Nothing is remembered between calls, so there is nothing that can go
    /// stale.
    ///
    /// Nothing else on this screen is touched - not the board, the roll
    /// call, a sponsor slide, a video or the cards. The band is a dock of
    /// its own at the foot of the window, so the question "what happens when
    /// a show and the roll call both want the screen" has no answer to give:
    /// they are not asking for the same room. See OnAirTreatment.
    /// </summary>
    private void UpdateOnAir()
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        bool show;
        var caption = string.Empty;

        try
        {
            // ShowOnAir's lock, from the UI thread, while this thread holds
            // nothing of its own - which is the half of the bargain that
            // makes NudgeOnAir's BeginInvoke safe. Behind each of these is a
            // compare and a field read.
            show = OnAirTreatment.Showing(ShowOnAir.Playing, OnAirTreatment.Wanted);

            if (show)
            {
                caption = OnAirTreatment.Caption(ShowOnAir.Caption);
            }
            else if (OnAirTreatment.Wanted && CommercialBreak.Running)
            {
                // The commercial break is not a show - no opener, no bed and
                // nobody talking over it - but it IS what is on air, and a
                // band that went dark for the forty seconds of adverts
                // between the teaser and the recap would read as the
                // broadcast having stopped rather than gone to a break. It
                // is the same band, the same club switch and the same one
                // line of words; it is simply not a segment, which is why it
                // has a caption of its own rather than a SegmentKind. See
                // CommercialBreak.OnAirCaption.
                //
                // The SHOW wins where both are somehow on. A show is what
                // the room is actually hearing, and the two cannot overlap
                // in the sequence as written - the break ends before the
                // recap's opener is started.
                show = true;
                caption = OnAirTreatment.Caption(CommercialBreak.OnAirCaption);
            }
        }
        catch
        {
            // No band rather than no Big Screen - the same bargain
            // UpdateAttract, UpdateTicker and UpdateRollCall all make.
            show = false;
        }

        if (show)
        {
            // Before it is shown, so the band's first paint already has the
            // words in it rather than wiping on empty and filling in.
            //
            // AND WHO THE LINE IS ABOUT (11 Oct 2026). On the tick, like
            // the caption, so the names follow the voice through a show
            // rather than being set once when it opened: a two-hander
            // names one driver in its first turn and another in its
            // second. The band repaints for a change of names and only
            // wipes for a change of caption - see OnAirBar.Say.
            _onAir.Say(caption, _sayingAbout);
        }

        if (_onAir.Visible != show)
        {
            _onAir.Visible = show;

            if (!show)
            {
                // The show is over. Nothing is being said, so there is
                // nobody it is about - and the next show must not open
                // with the last one's names in it for the half second
                // before its first line arrives.
                _sayingAbout = string.Empty;
            }
        }
    }

    /// <summary>The captions strip's plate colour - see ApplyLook.</summary>
    private Color CaptionPlate => PanelColor == CanvasColor ? GridLineColor : PanelColor;

    /// <summary>How long a caption stays at full strength at the least - the design's eight seconds.</summary>
    internal static readonly TimeSpan CaptionHold = TimeSpan.FromSeconds(8);

    /// <summary>How long a caption takes to fade out once its time is up.</summary>
    internal static readonly TimeSpan CaptionFade = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Roughly how fast the announcer speaks, in characters a second - so a
    /// long line is not faded out while it is still being said.
    /// </summary>
    private const double CaptionCharsPerSecond = 15;

    /// <summary>
    /// How strongly a caption is shown <paramref name="nowUtc"/>: 1 while it
    /// is fresh, falling to 0 across <see cref="CaptionFade"/>, then 0 - when
    /// the strip goes blank. Fresh for <see cref="CaptionHold"/>, or for as
    /// long as a line that length takes to say if that is longer (a
    /// 300-character recap line is twenty seconds of speech, and fading it
    /// at eight would take the words away mid-sentence). Pure, for the tests.
    /// </summary>
    internal static double CaptionStrength(CaptionLine? line, DateTime nowUtc)
    {
        if (line is null || string.IsNullOrEmpty(line.Text))
        {
            return 0;
        }

        var hold = TimeSpan.FromSeconds(Math.Max(CaptionHold.TotalSeconds, line.Text.Length / CaptionCharsPerSecond));

        // A clock that has gone backwards reads as "just said".
        var age = nowUtc - line.AtUtc;

        if (age <= hold)
        {
            return 1;
        }

        var into = (age - hold).TotalMilliseconds / CaptionFade.TotalMilliseconds;

        return into >= 1 ? 0 : 1 - into;
    }

    /// <summary>
    /// The captions strip, made to agree with the switch and the log: up
    /// while BigScreenCaptions is on, showing the newest line from
    /// CaptionLog.Shared and fading it out. On the 500-ms tick, after
    /// C, and after a change of look. UI thread only.
    /// </summary>
    /// <remarks>
    /// READ FROM THE SHARED LOG, NOT FROM LineHeard (22 Oct 2026). MainForm
    /// is the one place that fills CaptionLog.Shared (MainForm.Captions.cs),
    /// from the announcer's LineHeard on whatever thread the line plays
    /// from. This window only reads it, here on the UI thread, through the
    /// log's own lock - so there is no event to unsubscribe, no BeginInvoke
    /// into a window that may be closing, and no question of which handler
    /// ran first. A line is on the wall within half a second of being heard.
    /// The room's own screen, so the line is shown in full: initials-only is
    /// an online rule (PublicPrivacy).
    /// </remarks>
    private void UpdateCaptions()
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        try
        {
            var wanted = _settings?.BigScreenCaptions == true;

            if (_lblCaption.Visible != wanted)
            {
                _lblCaption.Visible = wanted;
                _captionStrength = -1;
            }

            if (!wanted)
            {
                return;
            }

            var newest = CaptionLog.Shared.LatestN;

            if (newest != _captionShownN)
            {
                _captionShownN = newest;
                _captionShown = CaptionLog.Shared.Last(1).LastOrDefault();
                _captionStrength = -1;
            }

            var strength = CaptionStrength(_captionShown, DateTime.UtcNow);

            // Repainted only while something changes: the fade, a new line,
            // the switch or the look. Most ticks this is a compare.
            if (Math.Abs(strength - _captionStrength) < 0.001)
            {
                return;
            }

            _captionStrength = strength;

            var plate = CaptionPlate;
            var ink = Looks.ClearestTextOn(plate);

            _lblCaption.ForeColor = Blend(plate, ink, strength);
            _lblCaption.Text = strength > 0 && _captionShown is { } line ? line.Text : string.Empty;
        }
        catch
        {
            // No captions rather than no Big Screen - the same bargain
            // UpdateOnAir makes.
        }
    }

    /// <summary>The colour <paramref name="amount"/> of the way from <paramref name="from"/> to <paramref name="to"/> (0 to 1).</summary>
    private static Color Blend(Color from, Color to, double amount)
    {
        var t = Math.Clamp(amount, 0, 1);

        int Mix(int a, int b) => (int)Math.Round(a + (b - a) * t);

        return Color.FromArgb(Mix(from.R, to.R), Mix(from.G, to.G), Mix(from.B, to.B));
    }

    /// <summary>Whether the captions strip is up, and what it says - for the tests. Nothing in the app reads them.</summary>
    internal bool CaptionsUpForTests => _lblCaption.Visible;

    /// <inheritdoc cref="CaptionsUpForTests"/>
    internal string CaptionSaysForTests => _lblCaption.Text;

    /// <summary>
    /// Whether the on-air band is up - for the test that drives a real show
    /// past a real Big Screen (ShowOnScreenTests). Nothing in the app reads
    /// it: the alternative was that test reaching in by reflection, which
    /// would go quietly wrong the day a field is renamed.
    /// </summary>
    internal bool OnAirUpForTests => _onAir.Visible;

    /// <summary>What the on-air band says - for the same test.</summary>
    internal string OnAirSaysForTests => _onAir.Saying;

    /// <summary>Whether the leaderboard is still up - the same test's proof that the band covers nothing.</summary>
    internal bool BoardUpForTests => _grid.Visible;

    /// <summary>
    /// Whether the commercial break has the screen - for the test that
    /// drives a real break past a real Big Screen (CommercialBreakTests).
    /// Nothing in the app reads it: the alternative was that test reaching
    /// in by reflection, which would go quietly wrong the day a field is
    /// renamed.
    /// </summary>
    internal bool BreakHasTheScreenForTests => _breakHasTheScreen;

    /// <summary>Whether a sponsor slide or video is on the screen - the same test's proof that the break actually took it.</summary>
    internal bool SponsorUpForTests => _showingSponsor;

    /// <summary>The roll call in the look's colours - called as it opens, and whenever the look changes.</summary>
    private void ColourRollCall()
    {
        _rollCall.Canvas = CanvasColor;
        _rollCall.Card = PanelColor;
        _rollCall.Ink = TextColor;
        _rollCall.Muted = MutedColor;
        _rollCall.Highlight = HighlightColor;
        _rollCall.Invalidate();

        _mapView.Canvas = CanvasColor;
        _mapView.Track = PanelColor == CanvasColor ? GridLineColor : PanelColor;
        _mapView.Ink = TextColor;
        _mapView.Muted = MutedColor;
        _mapView.Invalidate();

        _attractBoard.Canvas = CanvasColor;
        _attractBoard.Ink = TextColor;
        _attractBoard.Muted = MutedColor;
        _attractBoard.Highlight = HighlightColor;
        _attractBoard.Rule = GridLineColor;
        _attractBoard.Invalidate();

        // The on-air band's plate is the look's Surface, except in a look
        // whose Surface IS its background - several are - where it takes the
        // hairline colour instead, the same stand-in the track map takes for
        // its tarmac above. It is handed no ink: the caption's colour is
        // worked out from the plate it lands on, because a plate this size
        // can be any colour a club's look makes it (OnAirBar.Draw).
        _onAir.Canvas = CanvasColor;
        _onAir.Plate = PanelColor == CanvasColor ? GridLineColor : PanelColor;
        _onAir.Highlight = HighlightColor;
        _onAir.Invalidate();
    }

    private void ToggleFullscreen()
    {
        if (_isFullscreen)
        {
            // Normal first, for the same reason as below: going back to
            // Maximized from Maximized changes nothing, and the window kept
            // the whole monitor under its restored frame.
            WindowState = FormWindowState.Normal;
            FormBorderStyle = _restoreBorderStyle;
            WindowState = _restoreWindowState;
            _isFullscreen = false;
        }
        else
        {
            _restoreBorderStyle = FormBorderStyle;
            _restoreWindowState = WindowState;

            // Already maximised, Maximized again changes nothing and the
            // window keeps the work area - the taskbar stays over its foot.
            if (WindowState == FormWindowState.Maximized)
            {
                WindowState = FormWindowState.Normal;
            }

            FormBorderStyle = FormBorderStyle.None;
            WindowState = FormWindowState.Maximized;
            _isFullscreen = true;
        }
    }

    /// <summary>
    /// HEATCALLER ON THE BIG SCREEN (31 Oct 2026, Asher: the mark is always
    /// on everywhere): the sun and HEATCALLER, small, in the bottom right of
    /// the countdown band, like a TV channel's corner logo. In the look's
    /// own colours - the sun in its primary, the word in its muted text -
    /// so it never competes with the race, on a light look or a dark one.
    /// Sized from the band's own height, so it grows with the screen.
    /// </summary>
    private sealed class HeatCallerBug : Control
    {
        private Color _sun = Color.White;
        private Color _word = Color.Gray;

        public HeatCallerBug()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                     | ControlStyles.OptimizedDoubleBuffer
                     | ControlStyles.UserPaint
                     | ControlStyles.ResizeRedraw, true);
        }

        public void SetColours(Color sun, Color word)
        {
            _sun = sun;
            _word = word;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            if (Width <= 0 || Height <= 0)
            {
                return;
            }

            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

            // About a fifth of the band's height, so a 118-pixel band at the
            // normal size gets a word a little over twenty pixels tall.
            using var font = new Font(AppTheme.BoldFont.FontFamily, Math.Max(6f, Height * 0.15f), FontStyle.Bold, GraphicsUnit.Pixel);

            var width = HeatCallerMark.MeasureSignOff(g, font);
            var height = font.GetHeight(g);
            var inset = Height * 0.12f;

            HeatCallerMark.DrawSignOff(g, font, new PointF(Width - width - inset, Height - height - inset), _sun, _word, _sun);
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
