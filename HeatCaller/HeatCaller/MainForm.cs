using System.Text.Json;

namespace HeatCaller;

public partial class MainForm : Form
{
    private readonly AppSettings _settings;

    /// <summary>
    /// Publishes to the hosted relay on a thread of its own. Constructed
    /// once and shared: the Club Website page configures it, the Local Screens
    /// page reports on it, and the race path only ever hands it finished
    /// JSON.
    /// </summary>
    private readonly RelayPublisher _relayPublisher;

    /// <summary>Driver sign-up online: the invites, the sign-ups waiting for the desk, and approving them. See RelaySignupDesk.</summary>
    private readonly RelaySignupDesk _signupDesk;

    /// <summary>The Online Sign-ups window while it is open - one at a time, left open while the desk works through the queue.</summary>
    private SignupsForm? _signupsForm;

    /// <summary>The last roster document sent to the relay, so an unchanged one is not sent again every two seconds.</summary>
    private string _lastRelayRoster = string.Empty;

    /// <summary>The last of each of the relay's other documents that was sent, by channel - kept for the same reason as the roster's.</summary>
    private readonly Dictionary<RelayChannel, string> _lastRelayDocuments = new();

    /// <summary>The publisher's settings generation those were sent under. When it moves, everything is sent again.</summary>
    private int _relayGeneration = -1;

    /// <summary>Counts roster ticks, so the heavier documents are rebuilt every thirty seconds rather than every two.</summary>
    private int _relayExtrasTick;

    /// <summary>Rendered track pictures, kept between rebuilds. See RelayDocuments.Tracks.</summary>
    private readonly Dictionary<string, string?> _relayTrackImages = new();

    /// <summary>Build failures already logged, so one broken document is one line in the log rather than one every two seconds.</summary>
    private readonly HashSet<string> _relayBuildErrors = new(StringComparer.Ordinal);

    /// <summary>Drivers' photos as small data: URIs for the online check-in list. See RelayDocuments.PhotoFor.</summary>
    private readonly Dictionary<string, string?> _relayCheckInPhotos = new(StringComparer.Ordinal);

    /// <summary>
    /// Personal check-in PINs: checked for a new set falling due, and for
    /// racers waiting to be emailed theirs, every ten minutes. See
    /// RunRacerPinsPass. New racers get a PIN sooner than that, on the
    /// roster's two-second tick.
    /// </summary>
    private readonly System.Windows.Forms.Timer _racerPinsTimer = new() { Interval = 10 * 60 * 1000 };

    /// <summary>Stops an automatic PIN email run when the window closes.</summary>
    private readonly CancellationTokenSource _pinMailCancel = new();

    /// <summary>
    /// Automatic PIN emails that were refused this session, as
    /// "transponder|pin" - so a dead address is not tried again every ten
    /// minutes all day. A new PIN is a new key, and is tried afresh.
    /// </summary>
    private readonly HashSet<string> _pinMailFailed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The last reason automatic PIN email could not go, so it is logged once rather than every ten minutes.</summary>
    private string? _lastPinMailProblem;

    /// <summary>
    /// Online id to transponder, for every driver. Replaced whole, never
    /// changed in place: the LAN check-in page reads it from a request
    /// thread, and a reference swap is the one update that thread can
    /// never catch half-done.
    /// </summary>
    private volatile Dictionary<string, string> _relayBadgeIds = new(StringComparer.Ordinal);

    /// <summary>Drives the live-race frames going up to the relay. One a second while something is happening.</summary>
    private readonly System.Windows.Forms.Timer _relayStateTimer = new() { Interval = 1000 };

    /// <summary>The race state at the last relay frame, so a session ending still sends one final board.</summary>
    private RaceState _lastRelayRaceState = RaceState.Idle;

    /// <summary>
    /// When the relay change-caches were last emptied. See PublishExtrasToRelay:
    /// a document is remembered as sent the moment it is QUEUED, so one the
    /// relay never accepted - refused, or cleared by a delete - would
    /// otherwise never be offered again until it changed.
    /// </summary>
    private DateTime _relayCachesResetUtc = DateTime.UtcNow;

    /// <summary>When the badge catalogue was last queued. It carries every badge's artwork, so it is re-sent hourly rather than with the ten-minute refresh.</summary>
    private DateTime _relayBadgesSentUtc = DateTime.MinValue;

    /// <summary>Earned badges as last read from disk for the relay, and when. See RelayAchievements.</summary>
    private AchievementStore? _relayAchievements;
    private DateTime _relayAchievementsReadUtc = DateTime.MinValue;

    /// <summary>
    /// When each driver's check-in was last changed at the desk or from the
    /// LAN page, by transponder - or, for a relay tap, the time the tap was
    /// made. Memory only. A relay tap older than this is one the desk has
    /// already overruled; see ApplyRelayCheckIn.
    /// </summary>
    private readonly Dictionary<string, DateTime> _checkInChangedUtc = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Coalesces check-in roster republishes. A queue of drivers tapping in
    /// on their phones used to rebuild and reserialise the whole roster -
    /// and the relay's check-in list, photos and all - once per tap.
    /// </summary>
    private readonly System.Windows.Forms.Timer _checkInRosterCoalesce = new() { Interval = 300 };

    /// <summary>
    /// Console lines that arrived before the window had a handle, to be
    /// shown once it does. Guarded by itself.
    ///
    /// Before the handle exists there is no way onto the UI thread, and a
    /// line logged from a background thread then - a decoder, the web
    /// server, a store's save failing - used to be written straight into
    /// the text box from the wrong thread.
    /// </summary>
    private readonly List<string> _pendingConsoleLines = new();

    /// <summary>Set once the window has agreed to close. Handlers on other threads check it before posting work to a window that is going away.</summary>
    private volatile bool _closingDown;
    private readonly ProfileStore _profiles;
    private readonly VoiceSettingsStore _voiceStore;
    private readonly SoundEffectsStore _soundStore;
    private readonly TrackLayoutStore _trackLayouts;
    private readonly SessionHistoryStore _sessionHistory;
    private readonly RaceClassStore _raceClasses;
    private readonly RoundDefinitionStore _rounds;
    private readonly ThemeStore _themeStore;
    private readonly SeasonStore _seasonStore;
    private readonly RivalryStore _rivalryStore;
    private readonly VenueStore _venueStore;
    private readonly CheckInStore _checkInStore;
    private readonly DayScheduleStore _daySchedule;
    private readonly SponsorStore _sponsors;
    private readonly WebServerService _webServer;
    private readonly UndoService _undo = new();
    private readonly ToolTip _footerTooltip = new();

    /// <summary>
    /// Drives the animated synthwave header. Only ever repaints that one
    /// small label, never the whole form - and it isn't started at all
    /// unless Synthwave mode is active and animation is enabled, so no
    /// other theme pays any cost for it.
    /// </summary>
    private readonly System.Windows.Forms.Timer _headerAnimationTimer = new() { Interval = 50 };

    /// <summary>
    /// Keeps the check-in page's roster in step with the app.
    ///
    /// A timer rather than an event on every store that could change the
    /// answer. The page shows who is on the driver list and who has been
    /// marked present, and either can be changed from half a dozen places -
    /// Entries, Driver Profiles, an import, a new race day. Hooking all of
    /// them would be six couplings that have to be remembered every time a
    /// seventh appears; a two-second republish of a list this size costs
    /// nothing and cannot be forgotten.
    /// </summary>
    private readonly System.Windows.Forms.Timer _webRosterTimer = new() { Interval = 2000 };

    /// <summary>The Entries page, so a check-in arriving from a phone shows up on it straight away.</summary>
    private CheckInControl? _checkInControl;

    /// <summary>The Race Day page's Today tab, so anything that changes the day can tell it to re-read.</summary>
    private TodayControl? _todayControl;

    /// <summary>The Timetable tab, so the programme can keep its generated lines in step.</summary>
    private DayScheduleControl? _daySchedulePage;

    /// <summary>Starts a race day and shows it, from wherever the ask came from. Wired once the Race Day tabs exist.</summary>
    private Action? _startRaceDay;

    /// <summary>Puts the window on Race Day, on The Day tab. Wired once the Race Day tabs exist.</summary>
    private Action? _goToDayTab;

    /// <summary>Opens the race-day setup wizard. Wired once The Day and the programme exist - it drives both.</summary>
    private Action<int>? _openRaceDaySetup;
    private readonly SetupLogStore _setupLog = SetupLogStore.Load();
    private readonly PersonalBestResetStore _pbResets = PersonalBestResetStore.Load();
    private readonly ChassisTemplateStore _chassisTemplates = ChassisTemplateStore.Load();

    // The Garage. Two stores rather than one: the cars are the club's, the
    // chassis list is a lookup that outlives any of them - and a club that
    // sells every car still has its chassis list. Note that a ChassisType
    // here is the physical car's type, which is NOT the same thing as the
    // ChassisTemplate above (a setup sheet) - see GarageModels.
    private readonly CarStore _cars = CarStore.Load();
    private readonly ChassisTypeStore _chassisTypes = ChassisTypeStore.Load();
    // Declared here rather than in the Designer so adding a page touches
    // one file. The Designer's page loop only sets Dock/BackColor/Visible,
    // which ConfigurePage below does for these.
    /// <summary>
    /// Classes. A nav item of its own, because the rules a class runs
    /// under were reachable only through Driver Profiles, then a button,
    /// then a list, then Edit - four steps into a page about people.
    /// </summary>
    private readonly Panel pageClasses = new();

    /// <summary>The Club Website page - relay address, token, what may be published, and whether it is landing.</summary>
    private readonly Panel pageOnline = new();

    private readonly Panel pageRentals = new();
    private readonly Panel pageRaceDay = new();
    private readonly Panel pageChampionship = new();

    /// <summary>Refresh actions for the sub-tabs of Race Day and Championship, keyed by tab - the same idea as _navRefreshHooks one level down.</summary>
    private readonly Dictionary<TabPage, Action> _subTabRefresh = new();
    private readonly Panel pageHandicaps = new();
    private readonly Panel pageBatteries = new();

    private readonly RentalTransponderStore _rentals = RentalTransponderStore.Load();
    private readonly TechInspectionStore _techInspections = TechInspectionStore.Load();
    private readonly HandicapStore _handicaps = HandicapStore.Load();
    private readonly RaceEventStore _raceEvents = RaceEventStore.Load();

    /// <summary>
    /// What the event-spine migration moved, held until there is a console
    /// to say it in.
    ///
    /// The migration runs in the constructor, which is long before the
    /// console control exists - logging there writes into nothing.
    /// </summary>
    private string? _pendingSpineNote;

    /// <summary>True while F11 Race Mode has the app borderless and full screen with the sidebar hidden.</summary>
    private bool _raceMode;

    /// <summary>The window state to give back when Race Mode ends.</summary>
    private FormWindowState _preRaceModeState = FormWindowState.Maximized;
    private readonly BatteryPackStore _batteryPacks = BatteryPackStore.Load();
    private readonly LapBeepStore _lapBeeps = LapBeepStore.Load();
    private readonly AmbienceStore _ambienceStore = AmbienceStore.Load();
    private readonly AmbienceService _ambience = new();
    private readonly MusicStore _musicStore = MusicStore.Load();
    private readonly MusicPlayer _music = new();

    /// <summary>The Ambience and Music pages, so a soundscape that changes their settings can have them show it.</summary>
    private AmbienceControl? _ambienceControl;
    private MusicControl? _musicControl;
    private readonly GhostStore _ghostStore = GhostStore.Load();
    private GhostService? _ghosts;

    /// <summary>The Demo Mode page, for the ghosts a demo race was set up with. Null until the pages are built.</summary>
    private DemoModeControl? _demoControl;

    // "Once per race" guards for the derived ambience triggers - reset when
    // a race starts, so each new race can mark its own first battle and
    // first lapping rather than staying silent after the first one ever.
    /// <summary>Stops session audio being started twice for one session - once at staging and again at green.</summary>
    private bool _sessionAudioStarted;

    /// <summary>Held so a gate pass can start practice without the page being open.</summary>
    private PracticeModeControl? _practiceControl;

    /// <summary>
    /// False until the unfinished-race offer at startup has been made and
    /// answered (or there was nothing to offer). The decoder connects in
    /// the constructor, but the offer only comes at Ready - so a car
    /// crossing in between, with gate auto-start on, started practice
    /// first, and the offer then found the session busy and was silently
    /// skipped. Gate auto-start waits on this.
    /// </summary>
    private bool _recoveryDecided;

    private bool _ambienceBattleFired;
    private bool _ambienceLappedFired;

    /// <summary>Moved on by every start and end of speaking, so a duck waiting to go down knows it has been overtaken.</summary>
    private int _duckGeneration;
    private readonly object _duckLock = new();

    /// <summary>How long speaking has to last before the music and crowd duck - see the SpeakingChanged handler.</summary>
    private const int DuckOnsetDelayMs = 60;
    private readonly CommentaryService _commentary;
    private readonly DirectDecoderClient _decoderClient = new();
    private readonly ILapClient _ilapClient = new();
    private readonly System.Windows.Forms.Timer _decoderAutoConnectRetryTimer = new() { Interval = 3000 };
    private readonly AnnouncementService _announcer;

    /// <summary>
    /// How this laptop asks its relay for the HeatCaller voice. One for the
    /// app, shared by the announcer and Voice Settings, so the page and the
    /// race are always talking about the same club and the same allowance.
    /// </summary>
    private readonly HeatCallerVoiceClient _heatCallerVoice;

    private readonly RaceControlSession _race = new();
    private readonly System.Windows.Forms.Timer _raceTickTimer = new() { Interval = 500 };
    private readonly System.Windows.Forms.Timer _autoBackupCheckTimer = new() { Interval = (int)TimeSpan.FromHours(1).TotalMilliseconds };

    /// <summary>True while an automatic backup is being made in the background - see CheckAutoBackup.</summary>
    private bool _autoBackupRunning;

    private RoundDefinition? _activeRound;

    /// <summary>
    /// Set while the active-round combo is being rebuilt. Clearing a
    /// ComboBox's Items raises SelectedIndexChanged with nothing selected,
    /// which the handler read as "the user chose freeform" and used to wipe
    /// both _activeRound and the saved LastActiveRoundId. Any refresh - even
    /// just navigating to another page mid-race - silently dropped the
    /// selected heat, so a finished race had no round to link to.
    /// </summary>
    private bool _rebuildingRoundCombo;
    private VoiceSettingsControl _voiceSettingsControl = null!;
    private TabControl _tabVoiceSubNav = null!;
    private TabControl _tabSoundSubNav = null!;
    private TabControl _tabAnalyticsSubNav = null!;
    private TabPage _soundEffectsSubPage = null!;

    /// <summary>
    /// The Announcements tab, held for the same reason the one above is:
    /// RebuildVoiceAndSoundTabs has to swap what is inside it.
    /// </summary>
    private TabPage _voiceAnnouncementsSubPage = null!;

    /// <summary>
    /// The announcer's page. Built once at startup and kept for the life of
    /// the app even while its window is closed - the talking points it
    /// gathers are held here and nowhere else.
    /// </summary>
    private AnnouncerControl _announcerControl = null!;

    /// <summary>The announcer's window while it is open. One at a time.</summary>
    private AnnouncerForm? _announcerWindow;

    /// <summary>The Themes page's content (the Looks editor), which the Club Website page opens directly.</summary>
    private LooksControl _looksControl = null!;
    private RaceControlControl _raceControlControl = null!;

    /// <summary>The Program tab - a field (rather than PopulateTabs' local) only so the race-day manager can read and command it. See WireRaceDayService.</summary>
    private RaceProgramControl _raceProgram = null!;

    /// <summary>The one way a phone (and, once it exists, the online relay) changes anything on race day. See RaceDayService, raceday-design.md.</summary>
    private RaceDayService _raceDayService = null!;

    /// <summary>
    /// What the decoder has been hearing, kept so the pre-race check has
    /// something to check against.
    ///
    /// Fed from every pass the decoder reports, including the ones no race
    /// is counting - which are exactly the passes that matter here, since
    /// the useful moment is before a race rather than during one.
    /// </summary>
    private readonly TransponderWatch _transponderWatch = new();
    private SoundEffectsControl _soundEffectsControl = null!;

    /// <summary>
    /// The decoder chosen on the Connection page, as the number it is saved
    /// under (DecoderConnection.EasyLap or ILap) - not the picker position,
    /// which stopped matching it when Next Level Timing was retired. Compare
    /// this, never cmbConnectionType.SelectedIndex.
    /// </summary>
    private int SelectedDecoder => DecoderConnection.ValueAt(cmbConnectionType.SelectedIndex);

    public MainForm()
    {
        // Settings (specifically DarkMode) must be loaded before
        // InitializeComponent, since the Designer now builds the sidebar
        // and every page using AppTheme colors that depend on CurrentMode.
        _settings = AppSettings.Load();

        // "Setup Log" became "Tuning", and Tuning is now the lower half of
        // "Garage". The sidebar order and the hidden-pages list are saved
        // by page name, so carry the old names over - otherwise the page
        // would drop to the bottom of the sidebar and a hidden one would
        // come back.
        //
        // Both old names map straight to Garage rather than "Setup Log"
        // hopping to "Tuning" and on again: this runs once over the saved
        // list, so a two-step rename would leave anyone who last used the
        // Setup Log version pointing at a page that no longer exists.
        //
        // The same for "Phone & Web" and "Online", renamed so the two
        // pages stop sounding like each other.
        var renamedPages = new Dictionary<string, string>
        {
            ["Setup Log"] = "Garage",
            ["Tuning"] = "Garage",
            ["Phone & Web"] = "Local Screens",
            ["Online"] = "Club Website",

            // Shortened when the announcement rows became a tab inside it:
            // the sidebar row and its second tab would otherwise have read
            // almost the same. "Voice" also matches its sibling "Sounds",
            // which holds Mixer, Soundscapes, Effects, Ambience, Music and
            // Lap Beeps in exactly the same way.
            ["Voice Announcements"] = "Voice",
        };

        if (_settings.NavOrder.Concat(_settings.HiddenTabs).Any(renamedPages.ContainsKey))
        {
            _settings.NavOrder = _settings.NavOrder.Select(n => renamedPages.GetValueOrDefault(n, n)).ToList();
            _settings.HiddenTabs = _settings.HiddenTabs.Select(n => renamedPages.GetValueOrDefault(n, n)).ToList();
            _settings.Save();
        }

        // A saved sidebar order from before the Garage existed does not
        // mention it. NavOrder appends unknown names, so this is only about
        // the rename above having produced a duplicate: "Setup Log" and
        // "Tuning" both map to "Garage", and a club that has been through
        // both renames could hold both.
        if (_settings.NavOrder.Count(n => string.Equals(n, "Garage", StringComparison.Ordinal)) > 1)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            _settings.NavOrder = _settings.NavOrder.Where(seen.Add).ToList();
            _settings.Save();
        }
        // Connection now sits just above Backup & Restore - the setup pages
        // together at the bottom. The default order already has it there;
        // a saved order is moved once, then left to whatever the club does.
        if (!_settings.NavConnectionAboveBackup)
        {
            var order = _settings.NavOrder;
            var backup = order.IndexOf("Backup & Restore");

            if (order.Contains("Connection") && backup >= 0)
            {
                order.Remove("Connection");
                order.Insert(order.IndexOf("Backup & Restore"), "Connection");
            }

            _settings.NavConnectionAboveBackup = true;
            _settings.Save();
        }

        if (_settings.ThemeMode < 0)
        {
            _settings.ThemeMode = _settings.DarkMode ? 1 : 0; // one-time migration from the old boolean flag
            _settings.Save();
        }
        // Set alongside the theme, before any UI is built - renderers and
        // static helpers read this rather than being handed settings.
        AppTime.Use24Hour = _settings.Use24HourTime;

        // The look the app runs with, before a single control exists -
        // most of the window captures its colours as it is built, which is
        // why changing it takes a restart. The club's own looks are read
        // first so a custom one can be found. A settings file from before
        // looks existed has no AppLook, and ForApp falls back to the old
        // Light/Dark/Synthwave setting migrated just above, so nobody's
        // colours change by updating. UseLook also sets CurrentMode, which
        // still means the family - Synthwave header, neon clock and so on.
        Looks.LoadCustom();
        AppTheme.UseLook(Looks.ForApp(_settings));

        // Before InitializeComponent, so every control is built at the saved
        // size rather than being rebuilt at it a moment later.
        AppTheme.SetFontScale(_settings.FontScale);

        // The window's own colour, set before a single control exists.
        //
        // A Form defaults to the system control colour - near-white - and
        // it paints that the instant it is shown, whatever the theme is
        // going to be. The startup screen covers most of that, but the
        // frame between this window appearing and its pages painting was
        // still a white flash.
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        // ...which is necessary but not sufficient, so the window also
        // starts fully transparent and is revealed only once it has
        // finished painting. See the Ready machinery below for why.
        Opacity = 0d;

        InitializeComponent();

        // A race director's app is a full-screen app. Remembered rather
        // than forced: someone who deliberately runs it in a window on a
        // second monitor gets their choice back next launch.
        if (_settings.StartMaximized)
        {
            WindowState = FormWindowState.Maximized;
        }

        KeyPreview = true;
        KeyDown += MainForm_KeyDown;

        _profiles = ProfileStore.Load();

        // The Garage starts with the cars the club already named on its
        // driver profiles, rather than empty. Once, guarded by its own
        // flag - see GarageMigration.
        //
        // HERE, and not up with the other settings migrations near the top
        // of this constructor: _profiles is a readonly field assigned on
        // the line above rather than at its declaration, so it is still
        // null that far up. The compiler said so - "possible null reference
        // argument for parameter 'profiles'" - and it was right; the app
        // would not start.
        GarageMigration.RunOnce(_settings, _cars, _profiles);

        // The drivers added after that first fill, who never reached the
        // Garage. Once. See CatchUpOnce for who it leaves alone.
        GarageMigration.CatchUpOnce(_settings, _cars, _profiles);

        // And from now on as they arrive: a driver named from the
        // new-transponder prompt, added on Driver Profiles, imported,
        // signed up on a phone or entered on Race Day gets their car in the
        // Garage straight away. See ProfileStore.CarsAdded for why it is
        // handed only the NEW names.
        _profiles.CarsAdded += OnProfileCarsAdded;

        // A driver with a second car carries two transponders. Both the race
        // and the unknown-transponder watch ask the profiles which driver an
        // alternate belongs to, so a lap from the second car is filed under
        // the driver's main transponder instead of appearing as a stranger.
        _race.AlternateTransponderOwner = _profiles.PrimaryForAlternate;
        _transponderWatch.AlternateTransponderOwner = _profiles.PrimaryForAlternate;

        _voiceStore = VoiceSettingsStore.Load();

        // Straight after both loads, before anything can make or save a
        // secret: whether this laptop could open the keys it was given (a
        // restore onto a new PC can't), and where a backup gets its
        // password-locked copy of them from.
        SecretRecovery.RememberStartupState(_settings, _voiceStore);
        BackupService.SecretsProvider = () => BackupSecrets.Capture(_settings, _voiceStore);
        _soundStore = SoundEffectsStore.Load();
        _trackLayouts = TrackLayoutStore.Load();
        _sessionHistory = SessionHistoryStore.Load();
        _raceClasses = RaceClassStore.Load();

        // What a class with no format of its own runs under, read fresh each
        // time - a sanctioning preset edits a copy of it (see
        // RaceClassStore.AppDefaultFormat). Hung on the store so both places
        // a class is edited get it.
        _raceClasses.AppDefaultFormat = () => RaceFormat.FromSettings(_settings);
        _rounds = RoundDefinitionStore.Load();
        _themeStore = ThemeStore.Load();
        _seasonStore = SeasonStore.Load();
        _rivalryStore = RivalryStore.Load();
        _venueStore = VenueStore.Load();
        _checkInStore = CheckInStore.Load();
        _daySchedule = DayScheduleStore.Load();
        _sponsors = SponsorStore.Load();

        // --- The event spine ---
        //
        // Check-in, tech and the timetable are all scoped to an event now,
        // and this is what tells them which one - and re-tells them every
        // time it changes. It has to run after all four stores are loaded
        // and before any page reads one, which is why it sits here rather
        // than alongside any single store.
        // Rounds too, now: the timetable merges the programme's running order
        // with the club's own lines rather than keeping a second copy of it,
        // and this is where it is handed the programme to read.
        EventSpine.Bind(_raceEvents, _checkInStore, _techInspections, _daySchedule, _rounds);

        // Anything recorded before events existed gets a home. Idempotent -
        // it finds its own previous work by a fixed id, so every launch
        // after the first does nothing. The result is logged rather than
        // shown: a dialog on startup about a successful migration is an
        // interruption reporting that nothing is wrong.
        //
        // Both of these WRITE, and both are skipped when events.json could not
        // be read. An empty store is indistinguishable from a new install, so
        // migrating into it invents a legacy holder and a fresh race day and
        // saves them over a file that was only ever locked for a second - see
        // RaceEventStore.LoadedUnreadable. Nothing is lost by waiting: the
        // migration finds its own work by a fixed id, so the next launch that
        // can read the file does it properly.
        if (_raceEvents.LoadedUnreadable)
        {
            _pendingSpineNote =
                "events.json could not be read this time, so race days have been left completely alone. "
                + "Close anything that might have it open (a backup or sync tool is the usual cause) and restart - "
                + "nothing has been changed or lost.";
        }
        else
        {
            _pendingSpineNote = EventSpine.Migrate(
                _raceEvents, _checkInStore, _techInspections, _daySchedule, _rounds, _seasonStore);
        }

        // --- A first race day, for a club that has never had one ---
        //
        // Until now a fresh install had exactly one event: the legacy
        // holder, and nothing anywhere made a real one - not the wizard,
        // not the first race. That is not a harmless default. Rounds are
        // only filed against a day while one is active, so a club that
        // never opened the Event tab had qualifying with no boundary: the
        // second race day folded in the first day's heats and seeded its
        // mains from a mixture of both, which is the exact fault race days
        // were added to fix.
        //
        // Only ever on a club's first launch - see StartFirstDayIfNever
        // for why "never had one" rather than "hasn't one open". Not at all
        // when the file could not be read: "no events" is then a guess, and
        // acting on it is exactly how every race day got replaced by one
        // empty new one.
        if (!_raceEvents.LoadedUnreadable)
        {
            EventSpine.StartFirstDayIfNever(_raceEvents);
        }

        // --- Class renames ---
        //
        // A class is held BY NAME nearly everywhere. The cascade moves the
        // stores it is given itself (drivers, rounds, championships, tech
        // inspections, race days, run sheet lines), noting what already
        // carried the new name so that its undo leaves that alone; the rest
        // are held here and moved on its Renamed, which an undo raises again
        // with the names the other way round. Race days and the run sheet
        // must not be renamed here as well: they used to be, and the undo
        // then moved back everything under the new name, including what had
        // carried it before the rename. Handicap modes keep their own record
        // of what a rename replaced (HandicapStore.RenameClass).
        // Hung on the class store so both places a class can be edited -
        // the Classes page and the dialog inside a driver's profile - use
        // it. After every store is loaded, and these are the same store
        // objects the pages are given, so no page keeps an old copy.
        var classRenameCascade = new ClassRenameCascade(_profiles, _rounds, _seasonStore, _techInspections, _raceEvents, _daySchedule);
        _raceClasses.RenameCascade = classRenameCascade;
        classRenameCascade.Renamed += (oldName, newName) =>
        {
            _handicaps.RenameClass(oldName, newName);

            // Remembered choices that name the class - the programme's
            // class, auto-advance's class, the check-in filter. Left as
            // they were, each would quietly select nothing next time.
            var settingsChanged = false;

            if (string.Equals(_settings.LastRaceProgramClass, oldName, StringComparison.OrdinalIgnoreCase))
            {
                _settings.LastRaceProgramClass = newName;
                settingsChanged = true;
            }

            if (string.Equals(_settings.AutoAdvanceClassName, oldName, StringComparison.OrdinalIgnoreCase))
            {
                _settings.AutoAdvanceClassName = newName;
                settingsChanged = true;
            }

            if (string.Equals(_settings.LastCheckInClassFilter, oldName, StringComparison.OrdinalIgnoreCase))
            {
                _settings.LastCheckInClassFilter = newName;
                settingsChanged = true;
            }

            if (settingsChanged)
            {
                _settings.Save();
            }

            // Posted, not done here: this runs partway through the rename,
            // before the class itself carries its new name, so a page
            // redrawn now would look the round's class up and not find it.
            // Other pages redraw from the stores when they are next opened.
            TryPost(() =>
            {
                RefreshCurrentPage();
                _raceControlControl?.RefreshFormat();
            });
        };
        _announcer = new AnnouncementService(_profiles, _voiceStore, _soundStore, _rivalryStore);
        _heatCallerVoice = new HeatCallerVoiceClient(_settings);
        _commentary = new CommentaryService(_profiles, _settings, _rivalryStore);

        // Read live, so switching the model on the Voice Settings page takes
        // effect immediately rather than needing a restart.
        _commentary.VoiceSupportsAudioTagsProvider = () => _voiceStore.Current.SupportsAudioTags;

        _relayPublisher = new RelayPublisher(_settings);

        // From the publisher's own thread; OnClientRawMessage takes lines
        // from any thread, and holds them until the window is up.
        _relayPublisher.Log = OnClientRawMessage;

        // Said once, at the start: why the relay, the voice relay or the PIN
        // emails are quiet after a restore, in the log as well as on the
        // pages that switch them back on. See RestoredSettings.
        if (RestoredSettings.StartupNote() is { } restoredNote)
        {
            OnClientRawMessage(restoredNote);
        }

        // Driver sign-up online. Approving checks a driver in exactly as the
        // desk does, and a new driver gets their PIN the way any other does.
        _signupDesk = new RelaySignupDesk(_settings, _profiles, _checkInStore, _raceEvents, _raceClasses, _rentals, _relayPublisher, OnClientRawMessage)
        {
            CheckIn = ApplyPhoneCheckIn,
            RacerAdded = GiveSignedUpRacerTheirPin,
        };

        // Badges carry an online check-in link while online check-in is on,
        // and every scanner - the desk's USB one, the LAN page's camera -
        // reads either kind of badge. Settings are read at the moment of
        // use, so switching check-in on or off needs nothing re-wiring.
        QrBadgeRenderer.OnlineBadgeUrl = badgeTransponder => RelayCheckIn.BadgeUrl(_settings, badgeTransponder);
        QrBadgeRenderer.ResolveOnlineId = onlineId => _relayBadgeIds.TryGetValue(onlineId, out var knownTransponder) ? knownTransponder : null;

        // Only once the window is up: before then there is no way to get a
        // tap onto the UI thread, and the publisher does not collect taps
        // while nobody is listening, so none are acknowledged and lost.
        Shown += (_, _) =>
        {
            // From here on there is a UI thread to hand a phone's request
            // to; until now the web server answers "still starting".
            // (The '!' is for the compiler only: this handler is created in
            // the constructor before _webServer is assigned, but it cannot
            // run until the window is shown, long after.)
            _webServer!.HostReady = true;

            // Synchronous, and that is the point: the publisher only
            // acknowledges a tap to the relay - which then forgets it -
            // once this has said it was applied. Invoke rather than
            // BeginInvoke so that "applied" means applied, not queued. Safe
            // to block here: this runs on the publisher's own thread, never
            // the UI thread.
            _relayPublisher.CheckInReceived += relayTap =>
            {
                if (_closingDown || IsDisposed || Disposing || !IsHandleCreated)
                {
                    return false;
                }

                try
                {
                    return Invoke(new Func<bool>(() => !_closingDown && ApplyRelayCheckIn(relayTap))) is true;
                }
                catch
                {
                    // The window went away between the check and the call.
                    // Not applied, so not acknowledged: the relay keeps it.
                    return false;
                }
            };

            // No Invoke wrapper needed here, unlike CheckInReceived just
            // above: HandleJsonAsync already does its own UI-thread
            // marshaling and its own closing/disposed checks (see its own
            // doc comment, and the InvalidOperationException catch around
            // its BeginInvoke - that covers a window that is mid-Dispose
            // too, not only IsDisposed). There is no separate "applied"
            // signal to give back either: the JSON it returns IS the
            // answer, ready to post straight to the relay for the phone to
            // collect.
            _relayPublisher.ManageRequestReceived += (json, ct) =>
                _raceDayService.HandleJsonAsync(json, RaceDayChannel.Relay, ct).GetAwaiter().GetResult();

            // Sign-ups waiting on the relay. Posted, not Invoked: nothing is
            // acknowledged by collecting them, so there is nothing to wait for.
            _relayPublisher.SignupsCollected += signups => TryPost(() =>
            {
                if (!_closingDown)
                {
                    _signupDesk.Collected(signups);
                }
            });

            // Personal PINs brought up to date - a set falling due while the
            // laptop was shut, racers added since - and from then on every
            // ten minutes. Here rather than in the constructor so that an
            // automatic email's result has a window to come back to.
            RunRacerPinsPass();
            _racerPinsTimer.Start();
        };

        _racerPinsTimer.Tick += (_, _) => RunRacerPinsPass();

        // Every finished race, whichever path saved it. Hooked to the
        // store rather than to the places that call it - see
        // SessionHistoryStore.SessionAdded for what that cost the first
        // time round.
        _sessionHistory.SessionAdded += record =>
        {
            PublishSessionToRelay(record);
            PublishResultsIndexFor(record);
            PublishStandingsToRelay();

            // A finished session is when badges are earned; read them
            // afresh on the next pass rather than waiting out the cache.
            _relayAchievementsReadUtc = DateTime.MinValue;
        };

        // A saved result corrected after the flag - the laptop's Edit
        // Result, or a phone through RaceDayService - changes what
        // qualifying, the mains and the championship read, so it is
        // re-sent exactly as a freshly-finished race is. Badges are
        // deliberately left alone: they are not re-evaluated for a
        // correction (see ResultEditor, raceday-design.md).
        _sessionHistory.SessionEdited += record =>
        {
            PublishSessionToRelay(record);
            PublishStandingsToRelay();

            // The index carries the label, the track and the winner, so a
            // corrected result changes it too - without this, the archive
            // page and every link preview would go on showing the name of
            // whoever the result USED to say had won.
            PublishResultsIndexFor(record);
        };

        _webServer = new WebServerService(_race, _profiles, _daySchedule, _sponsors, _settings);
        _webServer.StatusMessage += OnClientRawMessage;

        // Sign-on from a phone. Same shape as the race-control commands
        // below: the request arrives on a thread-pool thread, and every
        // store it touches belongs to the UI thread.
        _webServer.CheckInRequested += (transponder, wanted) =>
        {
            TryPost(() =>
            {
                ApplyPhoneCheckIn(transponder, wanted);
            });
        };

        _checkInRosterCoalesce.Tick += (_, _) =>
        {
            _checkInRosterCoalesce.Stop();
            PublishCheckInRoster();
        };

        _webRosterTimer.Tick += (_, _) =>
        {
            // Always, whatever is switched on: the desk's USB scanner has to
            // recognise an online badge even on a night the relay is off.
            RebuildBadgeIds();

            // Before the roster goes out, so a racer added a moment ago goes
            // with their PIN in the same publish.
            KeepRacerPinsInStep();

            // Also when the local server is off. The relay is a separate
            // audience: a club publishing online without running the LAN
            // pages would otherwise never send a roster at all.
            if (_webServer.IsRunning || _settings.RelayEnabled)
            {
                PublishCheckInRoster();
            }

            PublishExtrasToRelay();
        };

        _webRosterTimer.Start();

        _relayStateTimer.Tick += (_, _) => PublishStateToRelay();
        _relayStateTimer.Start();
        _webServer.RunCommand = (action, force, shownRace) =>
        {
            // Requests arrive on a background thread - the request's own
            // thread-pool thread, never the UI thread - and every race-control
            // action touches UI state, so hop to the UI thread and let Race
            // Control run it through the same paths as the on-screen buttons.
            // RunRemoteCommand also makes the "stage or countdown over a
            // running race without the phone's yes" check that used to be
            // made here, and says what became of the command - which is what
            // the phone is told. It used to be told "sent" as soon as the
            // command had been posted, even when Race Control then refused it.
            //
            // TryPost because BeginInvoke throws when there's no window
            // handle, and this runs on a background thread - an unhandled
            // exception there takes the app down rather than surfacing as
            // a failed command. One that could not be posted never reached
            // Race Control, and the phone is told it did not go.
            var ranByRaceControl = new TaskCompletionSource<RemoteCommandOutcome>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var handedOver = TryPost(() =>
            {
                try
                {
                    ranByRaceControl.TrySetResult(_raceControlControl.RunRemoteCommand(action, force, shownRace));
                }
                catch (Exception failure)
                {
                    // The phone is told to check the laptop (see
                    // WebServerService.HandleCommand); the UI thread's own
                    // handler still hears of it, as it always has.
                    ranByRaceControl.TrySetException(failure);
                    throw;
                }
            });

            if (!handedOver)
            {
                return RemoteCommandOutcome.NotDelivered(
                    "The laptop could not take that command just now - it was NOT sent. Try again.");
            }

            // Three seconds is far longer than a command takes, unless Race
            // Control has put a question up on the laptop - and then what
            // happens is decided there, so the phone is told so rather than
            // being kept waiting for somebody at the laptop.
            if (ranByRaceControl.Task.Wait(TimeSpan.FromSeconds(3)))
            {
                return ranByRaceControl.Task.Result;
            }

            // Closing: a command posted just before the window went is never
            // run at all, and "it may be asking a question" would be wrong.
            return _closingDown
                ? RemoteCommandOutcome.NotDelivered("The laptop is closing - that command may not have gone. Check the laptop.")
                : RemoteCommandOutcome.Waiting("Check the laptop - it may be asking a question.");
        };
        _announcer.LapBeeps = _lapBeeps;
        _announcer.StatusMessage += OnClientRawMessage;

        // Storage failures used to crash the app; now they're caught and
        // reported. Surfacing them matters - a silently failing save means
        // someone loses a race programme without ever being told.
        // Filter wiring. Re-renders from the buffer, so nothing is lost.
        txtConsoleFilter.TextChanged += (_, _) => RenderConsole();
        chkFilterExclude.CheckedChanged += (_, _) => RenderConsole();

        cmbConsolePreset.SelectedIndexChanged += (_, _) =>
        {
            // "Hide raw HID" is the one preset that means "everything
            // EXCEPT", so it sets the exclude box itself rather than
            // needing two actions to be useful.
            if (cmbConsolePreset.SelectedItem as string == "Hide raw HID")
            {
                chkFilterExclude.Checked = true;
            }
            else if (chkFilterExclude.Checked)
            {
                chkFilterExclude.Checked = false;
            }

            RenderConsole();
        };

        btnClearConsole.Click += (_, _) =>
        {
            _consoleLines.Clear();
            txtConsole.Clear();
        };

        btnCopyConsole.Click += (_, _) =>
        {
            // Copies what's VISIBLE, so a filtered view is what lands on the
            // clipboard - which is the point when you're sharing a trace.
            var text = txtConsole.Text;

            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            try
            {
                Clipboard.SetText(text);
                OnClientRawMessage($"{AppTime.LogStampNow()}  [console] Copied {Plural.Of(text.Split('\n').Length, "visible line")}.");
            }
            catch
            {
                // The clipboard can be locked by another process - not worth
                // interrupting anyone over.
            }
        };

        BuildConsoleMenu(txtConsole);

        SafeFile.WriteFailed += message =>
        {
            OnClientRawMessage($"{AppTime.LogStampNow()}  {message}");
        };

        // Single source of truth for practice state. The announcer reads
        // this on demand rather than being told, so it stays correct no
        // matter how a practice session ends - including navigating away
        // without pressing Stop.
        _announcer.PracticeModeProvider = () => _race.IsPracticeMode;

        // Qualifying calls (provisional pole, improvements) fire only when
        // the session was started as a qualifier - asked on demand, same as
        // practice, so it can never go stale.
        _announcer.QualifyingModeProvider = () => _race.StartType == RaceStartType.Qualifier;

        // The board, the lap-detail popup and the race log ask the same
        // question the announcer does - how many clean laps make a streak -
        // so they now ask the same place. Resolved on demand rather than
        // pushed in, because practice has its own threshold and the session
        // is shared between the two.
        _race.StreakThresholdProvider = () => _soundStore.Current.StreakThresholdFor(_race.IsPracticeMode);

        // The final-lap shout and the one-minute warning: the session raises
        // them, the announcer says them.
        _race.LastLapStarted += standing =>
        {
            if (InvokeRequired) { TryPost(() => _announcer.NotifyLastLap(standing.TransponderId)); return; }
            _announcer.NotifyLastLap(standing.TransponderId);
        };

        _race.FinalMinute += () =>
        {
            if (InvokeRequired) { TryPost(() => _announcer.NotifyFinalMinute()); return; }
            _announcer.NotifyFinalMinute();
        };

        // Lets the announcer hold routine lines and lap chimes while a
        // yellow or red is out - see AnnouncementService.HeldByCaution.
        _announcer.FlagStateProvider = () => _race.Flag;

        // One gate for every start path. Capped so a stuck or unusually long
        // cue can't stall a race indefinitely.
        //
        // Ten seconds, not six: with the countdown ticks on, the countdown's
        // own audio is its sound (up to two and a half seconds in a pack)
        // and then five seconds of ticks - and a green pressed straight
        // after the countdown should land as the ticks end, not three ticks
        // in. The cap only ever matters when audio really is still playing.
        _race.BeforeGreenFlagAsync = () =>
            _announcer.WaitForAudioAsync(TimeSpan.FromSeconds(10), CancellationToken.None);

        // Duck the ambience under every spoken line, so a surge of cheering
        // doesn't bury the announcement it's celebrating.
        //
        // The duck waits a moment before going down. The announcer counts as
        // speaking from the moment a line is taken off the queue, and some
        // lines end there - dropped as stale, or cut off by a flag - so the
        // music used to dip and come straight back with nothing said. A real
        // line starts with a little silence and the output's own latency,
        // so waiting that long costs nothing; letting go is immediate, and
        // the music glides back up (see SmoothGainSampleProvider). Kept
        // short: the music's deep buffer already puts its duck a little
        // behind the voice.
        // The announcer's and the effects' shared output, opened now so
        // the first sound of the night doesn't pay for the wake-up.
        _ = Task.Run(AudioBus.Warm);

        _announcer.SpeakingChanged += speaking =>
        {
            int generation;

            lock (_duckLock)
            {
                generation = ++_duckGeneration;

                if (!speaking)
                {
                    _ambience.SetDucked(false);
                    _music.SetDucked(false);
                    return;
                }
            }

            _ = Task.Delay(DuckOnsetDelayMs).ContinueWith(_ =>
            {
                // Checked and applied together, so an end of speaking can't
                // slip in between and leave the music ducked under nothing.
                lock (_duckLock)
                {
                    if (_duckGeneration == generation)
                    {
                        _ambience.SetDucked(true);
                        _music.SetDucked(true);
                    }
                }
            }, TaskScheduler.Default);
        };

        _music.Settings = _musicStore.Current;
        _music.StatusMessage += OnClientRawMessage;

        // What "Theme music" on the Music page plays - the soundscape applied
        // last - and, for a music file from before that choice existed,
        // which of the two it starts on. See MusicStore.SettleMusicSource.
        _music.Theme = _themeStore.ActiveThemeMusic();
        _musicStore.SettleMusicSource(_music.Theme);

        // And lift the voice a little when the bed is genuinely loud. Only
        // then - boosting over a quiet or absent ambience would just make
        // announcements shout for no reason.
        _announcer.VoiceBoostProvider = () =>
        {
            var settings = _ambienceStore.Current;
            if (!settings.BoostVoiceOverAmbience || !_ambience.IsRunning)
            {
                return 1f;
            }

            // Scale the boost with how loud things actually are: full boost
            // at a peak-level bed, none at all when it's barely audible.
            var loudness = _ambience.CurrentLoudness();
            if (loudness < 0.25f)
            {
                return 1f;
            }

            var reach = Math.Clamp((loudness - 0.25f) / 0.75f, 0f, 1f);
            return 1f + (Math.Max(1f, settings.VoiceBoostMultiplier) - 1f) * reach;
        };

        // The drivers checked in for the current event, for Voice Settings'
        // "Prepare lines", which renders each one's release call ahead of
        // time.
        _announcer.CheckedInDriversProvider = () => _checkInStore.SnapshotCheckedIn();

        // How the announcer asks the club's relay to speak, when the club is
        // on the HeatCaller voice rather than a key of its own. One client
        // for the app: it reads the relay address, club and token from these
        // same settings each time, so changing them on the Club Website page takes
        // effect without anything being rebuilt.
        _announcer.HeatCallerVoice = _heatCallerVoice;

        // Which relay a HeatCaller line was rendered on is part of its saved
        // key. Voice Settings sets this too as it is built, but only when it
        // is first opened - set it here as well so a race called before that
        // looks its saved lines up under the right relay.
        VoiceLibrary.VoiceRelaySettings = _settings;

        PopulateTabs();
        WireRaceDayService();

        FillBaudRates();
        RefreshTrackLayoutCombo();

        // From here on the picker follows the store, whichever page adds,
        // renames or deletes a layout. See OnTrackLayoutsChanged.
        _trackLayouts.Changed += OnTrackLayoutsChanged;

        // Before the round combo, not after: a round's own layout is more
        // specific than the day's default and has to win. See
        // SyncTrackLayoutToRaceDay.
        SyncTrackLayoutToRaceDay();

        RefreshActiveRoundCombo();

        // Said once, on the launch that moves it: UpdateConnectionModeUi
        // below saves the decoder actually chosen, so the retired number is
        // gone from settings.json after this. A new install starts on
        // EasyLap (AppSettings' default), and a file whose first-run wizard
        // never ran has no connection it could have lost - neither is told.
        if (_settings.HasCompletedFirstRun && _settings.LastConnectionType == DecoderConnection.RetiredNeonApi)
        {
            OnClientRawMessage($"{AppTime.LogStampNow()}  [connection] Next Level Timing (Neon API) is no longer offered - the Connection page now starts on the EasyLap decoder.");
        }

        cmbConnectionType.SelectedIndex = DecoderConnection.IndexOf(_settings.LastConnectionType);

        // WHAT "RESET ROUND" USED TO BE.
        //
        // A button in the session row, next to the round picker, that wiped
        // the live session and then unbound the round and cleared the name.
        // The wiping half was Race Control's own Reset button, a few inches
        // away, with an all-but-identical body - two buttons, two undo
        // entries, one of them named after the programme it never touched.
        //
        // Now there is one Reset, and this is the rest of what it does: it
        // asks for the round and name to be cleared and hands back the
        // action that puts them back, so the whole thing is ONE undo step -
        // the race, the leaderboard, the round and the name together. Null
        // when there was no round and no name, so an already-clear session
        // does not leave an undo entry that undoes nothing.
        _raceControlControl.ClearRoundBinding = () =>
        {
            // The round itself, by id, not its place in the list. The list
            // is rebuilt whenever a heat is recorded or the program changes,
            // and an index taken now then pointed at a different heat - so
            // the undo put the wrong round back and the race was filed
            // under it.
            var roundIdSnapshot = (cmbActiveRound.SelectedItem as RoundComboItem)?.Round.Id;
            var labelSnapshot = txtSessionLabel.Text;

            if (string.IsNullOrEmpty(roundIdSnapshot) && labelSnapshot.Length == 0)
            {
                return null;
            }

            cmbActiveRound.SelectedIndex = 0;
            txtSessionLabel.Text = string.Empty;

            return () =>
            {
                // A round recorded or deleted since is no longer in the list,
                // and the box is left as it is rather than guessing.
                if (!string.IsNullOrEmpty(roundIdSnapshot))
                {
                    for (var i = 0; i < cmbActiveRound.Items.Count; i++)
                    {
                        if ((cmbActiveRound.Items[i] as RoundComboItem)?.Round.Id == roundIdSnapshot)
                        {
                            cmbActiveRound.SelectedIndex = i;
                            break;
                        }
                    }
                }

                txtSessionLabel.Text = labelSnapshot;
            };
        };

        UpdateConnectionModeUi();

        cmbTrackLayout.SelectedIndexChanged += (_, _) =>
        {
            var selected = cmbTrackLayout.SelectedItem as string;
            _settings.LastTrackLayoutName = selected is null or "(none)" ? string.Empty : selected;
            _settings.Save();
        };
        _decoderClient.StatusChanged += OnDecoderStatusChanged;
        _decoderClient.RawMessage += OnClientRawMessage;
        _decoderClient.LapEventReceived += OnDecoderLapEvent;

        // The I-Lap path ends in the same handler as EasyLap's - one lap
        // pipeline whatever the hardware at the gate.
        _ilapClient.StatusChanged += OnILapStatusChanged;
        _ilapClient.RawMessage += OnClientRawMessage;
        _ilapClient.LapEventReceived += OnDecoderLapEvent;

        _decoderAutoConnectRetryTimer.Tick += (_, _) =>
        {
            // Only while EasyLap is the decoder chosen. The box stays ticked
            // when somebody switches to I-Lap, and this used to go on
            // opening the EasyLap every three seconds regardless - two
            // decoders feeding one race.
            if (chkAutoConnect.Checked
                && SelectedDecoder == DecoderConnection.EasyLap
                && _decoderClient.State is DecoderConnectionState.Disconnected or DecoderConnectionState.Error)
            {
                OnClientRawMessage($"{AppTime.LogStampNow()}  [auto-connect] Retrying...");
                btnConnect.Enabled = false;
                btnDisconnect.Enabled = true;
                _decoderClient.Connect();
            }
        };
        _decoderAutoConnectRetryTimer.Start();
        chkAutoConnect.Checked = _settings.DirectDecoderAutoConnect; // fires CheckedChanged, which connects if already in EasyLap mode

        // Race control's own logic (time limits, lap counting, etc.) runs
        // continuously regardless of whether the Race Control window is
        // even open, so a race in progress isn't affected by someone
        // closing that window mid-session.
        _raceTickTimer.Tick += (_, _) =>
        {
            _race.Tick();

            // The live page's state document is built HERE, on the UI
            // thread, and served from a published copy - see
            // WebServerService.PublishState. A request thread must never
            // walk the standings while the lap pipeline is writing to them.
            _webServer.PublishState();
        };
        _raceTickTimer.Start();
        _race.LogMessage += msg => OnClientRawMessage($"[race] {msg}");

        // Checked once now and again every hour, so a long-running session
        // (a whole race day left open) still gets backed up on schedule
        // rather than only ever checking at the moment the app happens to
        // launch.
        CheckAutoBackup();
        _autoBackupCheckTimer.Tick += (_, _) => CheckAutoBackup();
        _autoBackupCheckTimer.Start();

        // Ready rather than Shown, and posted rather than run inline.
        //
        // The startup screen is still up at Shown, and it is TopMost - so a
        // dialog opened there appears UNDERNEATH it, which looks like the
        // app has hung. Ready fires after the window has painted and after
        // the startup screen has been told to close; posting from there
        // lets it actually go before the wizard arrives.
        // Keys that could not be opened on this PC - a restore onto a new
        // laptop - are recovered from the backup's password-locked copy
        // first, before anything else asks for them. Registered ahead of
        // the wizard so it runs first. A restart follows a recovery: every
        // page was built with the keys blank, and several save their boxes
        // back, which would blank the recovered values again.
        Ready += (_, _) => BeginInvoke(new Action(() =>
        {
            if (SecretRecovery.RunIfNeeded(this, _settings, _voiceStore))
            {
                SafeFile.Freeze();
                Application.Restart();
                Environment.Exit(0);
            }
        }));

        Ready += (_, _) => BeginInvoke(new Action(() =>
        {
            if (_settings.HasCompletedFirstRun)
            {
                // The tour asked for at the end of setup, when the look
                // chosen there needed a restart first.
                if (_settings.ShowTourOnNextStart)
                {
                    _settings.ShowTourOnNextStart = false;
                    _settings.Save();
                    StartTour();
                    return;
                }

                OfferRaceDaySetupOnce();
                return;
            }

            using var wizard = new FirstRunWizard(_settings, _trackLayouts, _raceClasses, _profiles);
            wizard.ShowDialog(this);

            // Mark it done even if dismissed with the X - the wizard is a
            // convenience, and re-showing it every launch to someone who
            // closed it deliberately would be an annoyance, not a help.
            _settings.HasCompletedFirstRun = true;

            // A new look only takes effect on a fresh start. The tour, if
            // it was asked for, waits for the restart so it shows the app
            // in the colours that were picked.
            if (wizard.RestartForLook)
            {
                _settings.ShowTourOnNextStart = wizard.TakeTour;
                _settings.Save();
                SafeFile.Freeze();
                RestartForNewLook();
                return;
            }

            _settings.Save();

            RefreshTrackLayoutCombo();
            RefreshActiveRoundCombo();

            // The Connection page read its choices from settings before the
            // wizard ran, so what was picked there is put onto the page now
            // rather than at the next launch. Changing the method refreshes
            // the COM port list itself; an unchanged I-Lap choice does not,
            // so it is done here. Positions, not saved numbers - see
            // DecoderConnection.
            var connectionIndex = DecoderConnection.IndexOf(_settings.LastConnectionType);

            if (cmbConnectionType.SelectedIndex != connectionIndex)
            {
                cmbConnectionType.SelectedIndex = connectionIndex;
            }
            else if (SelectedDecoder == DecoderConnection.ILap)
            {
                RefreshSerialPorts();
            }

            if (cmbBaud.Items.Contains(_settings.ILapBaud))
            {
                cmbBaud.SelectedItem = _settings.ILapBaud;
            }

            // Pages that list classes, drivers and tracks were built before
            // the wizard added any.
            foreach (var name in new[] { "Driver Profiles", "Classes", "Track Layouts" })
            {
                if (_navRefreshHooks.TryGetValue(name, out var refresh))
                {
                    refresh();
                }
            }

            if (wizard.OpenBackups)
            {
                GoToPage("Backup & Restore");
            }
            else if (wizard.TakeTour)
            {
                StartTour();
            }
        }));

        // A race that was running when the app last stopped is offered back
        // here.
        //
        // Hung off Ready, and posted, for the same reasons as the wizard
        // above: Shown fires before the window has drawn itself and while
        // the TopMost startup screen is still covering it, so a message box
        // opened there lands over a half-drawn app and under a splash.
        // Ready is the moment this comment used to have to approximate -
        // every Shown handler finished, the layout pass done, the window
        // painted and revealed.
        //
        // Gate auto-start of practice is held until this has run - see
        // _recoveryDecided. Set in finally, so it is set when there was
        // nothing to offer too, and even if the offer failed.
        Ready += (_, _) => BeginInvoke(new Action(() =>
        {
            try
            {
                _raceControlControl.OfferRaceRecovery();

                // A restored race comes back with the round it was run for,
                // but the Round box is this form's and still shows whatever
                // was remembered from last time - which would win when the
                // race is saved. Show the restored race's round instead, if
                // it is still in the list (not recorded or deleted since).
                if (_raceControlControl.RestoredRoundId is { } restoredRoundId)
                {
                    for (var i = 0; i < cmbActiveRound.Items.Count; i++)
                    {
                        if ((cmbActiveRound.Items[i] as RoundComboItem)?.Round.Id == restoredRoundId)
                        {
                            cmbActiveRound.SelectedIndex = i;
                            break;
                        }
                    }
                }
            }
            finally
            {
                _recoveryDecided = true;
            }
        }));

        // Synchronous, all of it. This was an async handler, and the first
        // await returned control to WinForms, which carried on closing: the
        // disconnect it was waiting on (Next Level Timing's, when that was
        // offered) then ran against a window already being torn down, and
        // the session in progress was never saved at all.
        FormClosing += (_, e) =>
        {
            // The X, Alt+F4 or the taskbar mid-race used to close the app
            // without a word. Only a person closing it is asked - Windows
            // shutting down or a restart after a restore must not hang on a
            // dialog nobody will see.
            if (e.CloseReason == CloseReason.UserClosing
                && _race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing)
            {
                // Practice runs on the same session, so it lands here too -
                // but it is stopped and saved on the way out (see
                // StopForShutdown below), not offered back like a race.
                var answer = _race.IsPracticeMode
                    ? AppDialog.Show(
                        this,
                        "Practice is running. Close HeatCaller anyway?\n\n"
                        + "Practice stops now, the same as pressing Stop (and is saved to Race History if "
                        + "\"Save automatically on stop\" is ticked). Nothing is timed while the app is closed.",
                        "Practice in progress",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning)
                    : AppDialog.Show(
                        this,
                        "A race is running. Close HeatCaller anyway?\n\n"
                        + "The race stops where it is. It can be offered back the next time the app opens, "
                        + "but nothing is timed while the app is closed.",
                        "Race in progress",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);

                if (answer != DialogResult.Yes)
                {
                    e.Cancel = true;
                    return;
                }
            }

            _closingDown = true;
            _webServer.HostReady = false;

            // The announcer's window is deliberately NOT owned by this one,
            // so that it can be left on a second screen while this one is
            // used - which also means closing this one does not close it.
            // Taken down by hand here, and by Dispose rather than Close:
            // its own handler turns a Close into a Hide, which at shutdown
            // would leave a window on screen with no app behind it.
            _announcerWindow?.Dispose();
            _announcerWindow = null;

            // A running practice is stopped the way its Stop button does it,
            // so it is saved (when set to) and its crash journal cleared.
            // Left running, the save was skipped and the practice was
            // offered back at the next launch as an unfinished race. Before
            // the journal flush below, which then has nothing to write.
            try
            {
                _practiceControl?.StopForShutdown();
            }
            catch
            {
                // Best effort - closing must not be stopped by a failed save.
            }

            // The last crossings before the window went away, written now
            // rather than whenever the journal's throttle next allows.
            try
            {
                _raceControlControl.FlushJournal();
            }
            catch
            {
                // Best effort - the journal from a moment ago still stands.
            }

            _settings.StartMaximized = WindowState == FormWindowState.Maximized;
            _settings.Save();

            // Application.Idle is static. A window closed before it ever
            // went idle - shut down during startup, say - would otherwise
            // leave a handler pointing at a disposed form.
            Application.Idle -= OnFirstIdle;

            _readyFallback?.Stop();
            _readyFallback?.Dispose();
            _readyFallback = null;

            _raceTickTimer.Stop();
            _decoderAutoConnectRetryTimer.Stop();
            _autoBackupCheckTimer.Stop();
            _headerAnimationTimer.Stop();
            _webRosterTimer.Stop();
            _relayStateTimer.Stop();
            _checkInRosterCoalesce.Stop();
            _racerPinsTimer.Stop();
            _pinMailCancel.Cancel();
            _webServer.Stop();

            // Ghost cars run on a timer of their own and feed laps into the
            // race; left running they log into a closing window.
            _ghosts?.Stop();

            _relayPublisher.Dispose();
            _decoderClient.Dispose();
            _ilapClient.Dispose();

            // Releases the ambience output device - it runs on its own
            // WaveOut independent of the announcement audio, so nothing
            // else's teardown covers it.
            _ambience.Dispose();
            _music.Dispose();
        };
    }

    /// <summary>
    /// Raised once, after the window has finished painting for the first
    /// time and has been made visible. The startup screen waits for this
    /// rather than for Shown.
    /// </summary>
    public event EventHandler? Ready;

    private bool _readyRaised;
    private System.Windows.Forms.Timer? _readyFallback;

    // There was a CreateParams override here that turned on WS_EX_COMPOSITED
    // while the window started, to composite the whole control tree
    // off-screen and keep the white window-class brush from ever showing.
    //
    // It is gone, and should not come back. It was belt and braces over the
    // opacity gate below, which already guarantees the window is invisible
    // until it has finished painting - so it was buying nothing. What it
    // cost was repainting: WS_EX_COMPOSITED is well known for making list
    // boxes, grids and anything that scrolls repaint badly, and it was
    // switched off again on the second idle after the reveal, which is not
    // a guarantee. Any launch where that second idle did not arrive left
    // the whole session composited.
    //
    // If the white flash ever comes back, the thing to check is the
    // opacity gate, not this.

    /// <summary>
    /// Waits for the window to go quiet, then reveals it.
    ///
    /// Shown is too early to hand over to: it fires when the window has
    /// been made visible, not when it has finished drawing itself, and
    /// with thirty-odd pages of controls those are not the same moment.
    /// Application.Idle is the honest signal - the message loop only
    /// reports idle once there is nothing left to paint, because a pending
    /// WM_PAINT keeps the queue non-empty.
    ///
    /// The timer is a backstop. Idle will fire; but a window that never
    /// went visible at all because of some fault upstream would otherwise
    /// leave the startup screen on the desktop forever, and that is a worse
    /// failure than a slightly early reveal.
    /// </summary>
    protected override void OnShown(EventArgs e)
    {
        // There is a window to hover over now, so tips may speak. Until this
        // point they are silent: the pages are built while the splash is up,
        // and one created under the pointer would otherwise pop its tip over
        // the splash as a stray box of text.
        AppTheme.ToolTipsReady();

        // Armed BEFORE the Shown event is raised. base.OnShown is what
        // runs the Shown handlers - the layout pass among them - and any
        // one of those could pump messages. The watch has to already be in
        // place if it is going to see the idle that follows.
        if (!_readyRaised)
        {
            Application.Idle += OnFirstIdle;

            _readyFallback = new System.Windows.Forms.Timer { Interval = 4000 };
            _readyFallback.Tick += (_, _) => RaiseReady();
            _readyFallback.Start();
        }

        base.OnShown(e);
    }

    private void OnFirstIdle(object? sender, EventArgs e) => RaiseReady();

    /// <summary>
    /// Posts work to the UI thread from any other, or does nothing if the
    /// window cannot take it. Returns whether it was posted.
    ///
    /// Every background event used to do this as two steps - "is there a
    /// handle?", then BeginInvoke - and the window could be destroyed
    /// between them, which threw on a thread-pool thread and took the whole
    /// app down on its way out. The check is still made first, because it
    /// is cheap and usually right; the catch is for the moment it is not.
    /// </summary>
    private bool TryPost(Action action)
    {
        if (_closingDown || IsDisposed || Disposing || !IsHandleCreated)
        {
            return false;
        }

        try
        {
            BeginInvoke(action);
            return true;
        }
        catch (InvalidOperationException)
        {
            // The handle went between the check and the post. Also covers
            // ObjectDisposedException, which derives from it (a separate
            // catch for it after this one is error CS0160).
            return false;
        }
    }

    /// <summary>Shows the console lines that arrived before there was a handle to show them with. See _pendingConsoleLines.</summary>
    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        DrainPendingConsoleLines();
        DrainPendingUiWork();
    }

    /// <summary>Work that arrived from a background thread before this window had a handle. See OnUiThread.</summary>
    private readonly List<Action> _pendingUiWork = new();

    /// <summary>
    /// Runs something on the UI thread, holding it until there IS one.
    ///
    /// The plain `if (InvokeRequired) { TryPost(...); return; }` that the
    /// decoder handlers used is wrong in one specific window, and it is a
    /// window this app opens on every launch: InvokeRequired answers FALSE
    /// when the form has no handle yet - there is no handle to ask - so a
    /// callback arriving then ran its body on the DECODER'S thread, straight
    /// into the UI. Auto-connect is switched on from the constructor
    /// (chkAutoConnect.Checked fires CheckedChanged, which connects), so a
    /// status callback beats the window being shown as a matter of course,
    /// and a crossing can too.
    ///
    /// TryPost alone would not do: it refuses without a handle and returns
    /// false, which turns the bug into a dropped lap. Held and replayed in
    /// order instead, the same as the console lines above.
    /// </summary>
    private void OnUiThread(Action action)
    {
        if (!IsHandleCreated)
        {
            lock (_pendingUiWork)
            {
                _pendingUiWork.Add(action);
            }

            // The handle may have arrived while that was being added, after
            // OnHandleCreated had already drained the list.
            if (IsHandleCreated)
            {
                TryPost(DrainPendingUiWork);
            }

            return;
        }

        if (InvokeRequired)
        {
            TryPost(action);
            return;
        }

        action();
    }

    private void DrainPendingUiWork()
    {
        List<Action> work;

        lock (_pendingUiWork)
        {
            if (_pendingUiWork.Count == 0)
            {
                return;
            }

            work = _pendingUiWork.ToList();
            _pendingUiWork.Clear();
        }

        foreach (var item in work)
        {
            try
            {
                item();
            }
            catch (Exception ex)
            {
                // One held callback failing must not stop the rest - they
                // are laps and status changes, not a transaction.
                System.Diagnostics.Debug.WriteLine($"Held UI work failed and was skipped: {ex}");
            }
        }
    }

    private void DrainPendingConsoleLines()
    {
        List<string> lines;

        lock (_pendingConsoleLines)
        {
            if (_pendingConsoleLines.Count == 0)
            {
                return;
            }

            lines = _pendingConsoleLines.ToList();
            _pendingConsoleLines.Clear();
        }

        foreach (var line in lines)
        {
            OnClientRawMessage(line);
        }
    }

    private void RaiseReady()
    {
        if (_readyRaised)
        {
            return;
        }

        _readyRaised = true;

        // Unhooked before anything else. Application.Idle is static, so a
        // handler left attached to a window that is going away outlives it.
        Application.Idle -= OnFirstIdle;

        if (_pendingSpineNote is { } spineNote)
        {
            _pendingSpineNote = null;
            OnClientRawMessage($"{AppTime.LogStampNow()}  [events] {spineNote}");
        }

        // Credentials this settings.json carried in plain text and that were
        // thrown away rather than taken in - see AppSettings
        // .AdoptPlainTextSecrets. Empty on every ordinary launch. Said out
        // loud, and not only to the console: a club upgrading from before
        // sealing would otherwise find its relay quietly stopped working with
        // nothing anywhere saying why.
        if (_settings.DroppedPlainTextSecrets.Count > 0)
        {
            var dropped = _settings.DroppedPlainTextSecrets.Distinct(StringComparer.Ordinal).ToList();
            _settings.DroppedPlainTextSecrets.Clear();

            OnClientRawMessage($"{AppTime.LogStampNow()}  [secrets] Plain-text {string.Join(", ", dropped)} found in settings.json and discarded - re-enter and they will be sealed.");

            AppDialog.Show(
                this,
                "This copy of settings.json held some keys in plain text, from a version before they were "
                + "locked to this PC. They have been removed rather than used, because a settings file can "
                + "come from anywhere.\r\n\r\nYou will need to type "
                + (dropped.Count == 1 ? dropped[0] : string.Join(", ", dropped.Take(dropped.Count - 1)) + " and " + dropped[^1])
                + " in again. Everything else is unaffected.",
                "Re-enter your keys",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        OnClientRawMessage($"{AppTime.LogStampNow()}  [tip] F11 toggles Race Mode - full screen, no sidebar, just the race.");

        _readyFallback?.Stop();
        _readyFallback?.Dispose();
        _readyFallback = null;

        if (IsDisposed || Disposing)
        {
            return;
        }

        // The window has finished painting; this is what makes it visible.
        // Everything it is going to show is already drawn, so it arrives in
        // one piece rather than as a sequence of controls appearing.
        Opacity = 1d;

        Ready?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Creates one instance of each converted screen and docks it into its
    /// tab. Each used to be its own Form, opened fresh (and disposed) every
    /// time its button was clicked; now they're built once, here, and live
    /// for the app's whole lifetime as ordinary tab content - which is also
    /// why RaceControlControl no longer needs the open/Activate/null-out
    /// dance a separate window required.
    /// </summary>
    /// <summary>
    /// The sidebar is styled as permanently-dark "app chrome" regardless of
    /// light/dark content mode - re-applied here because AppTheme.
    /// ApplyRecursively's generic pass (which also themes text inputs and
    /// list boxes for dark mode support) would otherwise overwrite lstNav
    /// and the footer buttons with the main content area's colors, since
    /// it can't tell the sidebar's ListBox/Buttons apart from any other.
    /// </summary>
    /// <summary>
    /// Ctrl and + / - resize the whole app; Ctrl+0 puts it back to normal.
    ///
    /// Done in ProcessCmdKey rather than KeyDown because this has to win
    /// before the focused control sees the keystroke - a grid or a spinner
    /// would otherwise swallow it. Both the main and numeric-keypad keys are
    /// accepted, since which one arrives depends on the keyboard.
    ///
    /// F11 (and Esc, once in) toggles Race Mode from anywhere for the same
    /// reason - dialogs are separate forms, so their own Escape handling is
    /// untouched.
    /// </summary>
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.F11)
        {
            ToggleRaceMode();
            return true;
        }

        // Not while the focused control has its own use for Escape. This
        // runs before that control sees the key, so Escape meant to cancel
        // a grid cell's edit or close an open drop-down threw the whole
        // window out of Race Mode instead - and left the edit in place.
        // F11 still leaves from anywhere.
        if (keyData == Keys.Escape && _raceMode && !FocusedControlWantsEscape())
        {
            ToggleRaceMode();
            return true;
        }

        if ((keyData & Keys.Control) == Keys.Control)
        {
            var key = keyData & Keys.KeyCode;

            // Ctrl+Z is the app's Undo - unless a text box has the focus,
            // where it belongs to the box's own undo.
            if (keyData == (Keys.Control | Keys.Z) && !IsTypingInTextField())
            {
                // Not btnUndo.PerformClick(): in Race Mode the footer is
                // hidden, and a hidden button ignores PerformClick.
                UndoSteps(1, " (Ctrl+Z)");
                return true;
            }

            // Ctrl+F: the page search at the top of the sidebar.
            if (keyData == (Keys.Control | Keys.F) && !_raceMode)
            {
                txtNavSearch.Focus();
                txtNavSearch.SelectAll();
                return true;
            }

            // Ctrl+1 to Ctrl+9: that page in the sidebar, counting from the
            // top in the order the sidebar shows them (hidden pages skipped).
            // Not in Race Mode, which is one page on purpose.
            if ((keyData & Keys.Modifiers) == Keys.Control && !_raceMode)
            {
                var index = key switch
                {
                    >= Keys.D1 and <= Keys.D9 => key - Keys.D1,
                    >= Keys.NumPad1 and <= Keys.NumPad9 => key - Keys.NumPad1,
                    _ => -1,
                };

                if (index >= 0)
                {
                    var pages = VisibleOrderedNavNames();
                    if (index < pages.Count)
                    {
                        GoToPage(pages[index]);
                    }

                    return true;
                }
            }

            var step = key switch
            {
                Keys.Oemplus or Keys.Add => 0.1f,
                Keys.OemMinus or Keys.Subtract => -0.1f,
                _ => 0f,
            };

            if (step != 0f)
            {
                NudgeFontScale(AppTheme.FontScale + step);
                return true;
            }

            if (key is Keys.D0 or Keys.NumPad0)
            {
                NudgeFontScale(1f);
                return true;
            }
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void NudgeFontScale(float scale)
    {
        if (!AppTheme.SetFontScale(scale))
        {
            return;
        }

        _settings.FontScale = AppTheme.FontScale;
        _settings.Save();

        ApplyFontScale();

        OnClientRawMessage($"{AppTime.LogStampNow()}  [ui] Text size {AppTheme.FontScale * 100:0}% (Ctrl+0 resets).");
    }

    /// <summary>
    /// Pushes the current scale through the whole window.
    ///
    /// The form's own Font is set first because most controls never set one
    /// and simply inherit it; the theme pass then reassigns the fonts of the
    /// ones that DO carry their own (buttons, grids, the nav list). Custom
    /// painted surfaces - the clock, the stat tiles, grid headers - read
    /// AppTheme's fonts at paint time, so they follow on the repaint.
    /// </summary>
    private void ApplyFontScale()
    {
        SuspendLayout();

        try
        {
            Font = AppTheme.BodyFont;
            AppTheme.ApplyRecursively(this);

            // The recursive pass paints the sidebar in the CONTENT theme's
            // colours, and the sidebar is deliberately not in the content
            // theme - it is dark chrome in every mode. So anything that runs
            // that pass has to put the chrome back afterwards.
            //
            // This one was missing, and it is the one that runs last at
            // startup: construction did ApplyRecursively, then
            // RestyleSidebarChrome, then called this - which wiped it again
            // with nothing following to restore it. In Dark and Synthwave
            // the content colours are close enough to the chrome that
            // nobody noticed. In Light Mode the sidebar came up light.
            RestyleSidebarChrome();
        }
        finally
        {
            ResumeLayout(true);
        }

        // The heading strip is measured from the micro font, which has just
        // changed size - so it has to be re-reserved before anything below
        // measures a page, or every page would be checked against the old
        // strip and the content would sit under or away from the rule.
        foreach (var item in _navItems)
        {
            PageHeader.Reserve(item.Page);
        }

        // Re-measured here as well as on resize: a text-size change moves
        // every caption in that row without changing the group's size, so
        // Resize never fires and the row would keep its old spacing.
        LayoutSessionRow();

        // Every page's spacing was judged against the font that was in
        // effect when it was last checked, so a size change invalidates all
        // of them. The one on screen is re-checked now; the rest are done as
        // they're opened.
        _tidiedPages.Clear();
        TidyAllPages();

        Invalidate(true);
    }

    /// <summary>
    /// The Session row on Race Control, measured against the group's real
    /// width on every resize.
    ///
    /// It was laid out once, left to right, at fixed field widths: a name
    /// box, a track picker, a session type, a 280px round picker and then
    /// "Reset Round" at whatever x the arithmetic reached. Nothing checked
    /// that the total fitted - and at the designed width it already did
    /// not, so the button sat off the right-hand edge of the group and, on
    /// a window narrower than the sum of the parts, off the page.
    ///
    /// Now the button is pinned to the right edge and the round picker
    /// takes whatever is left between its caption and the button. When
    /// that is too little to read a round name in, the row splits in two
    /// rather than squeezing. A picker showing half a round name with an
    /// ellipsis is not a picker - it is a tooltip you have to hover to read.
    /// </summary>
    private void LayoutSessionRow()
    {
        if (grpSession.IsDisposed || grpSession.Width < 80)
        {
            return;
        }

        const int inset = 12;

        // The narrowest the round picker is allowed to get before the row
        // gives up and wraps. Measured against the longest thing it ever
        // has to show rather than guessed.
        var wanted = TextRenderer.MeasureText(
            "(freeform - no race program round)",
            AppTheme.BodyFont,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width + 40;

        var right = grpSession.Width - inset;
        var top = UiLayout.InsideGroup(grpSession);
        var rowHeight = Math.Max(cmbActiveRound.Height, UiLayout.ButtonHeight);

        // --- Name, only while there is no round to derive one from ---
        //
        // With a round chosen the box is filled from that round and made
        // read-only, so it spends a caption and 170 pixels restating what
        // the Round picker already says. Hidden then, and the row closes up
        // behind it.
        var x = inset;

        // _sessionNameShown, not txtSessionLabel.Visible - see that field.
        if (_sessionNameShown)
        {
            lblSessionLabel.Location = new Point(x, UiLayout.CentreOn(lblSessionLabel, top, rowHeight));
            txtSessionLabel.Location = new Point(UiLayout.After(lblSessionLabel), UiLayout.CentreOn(txtSessionLabel, top, rowHeight));
            x = UiLayout.After(txtSessionLabel) + UiLayout.SectionGap;
        }

        lblTrack.Location = new Point(x, UiLayout.CentreOn(lblTrack, top, rowHeight));
        cmbTrackLayout.Location = new Point(UiLayout.After(lblTrack), UiLayout.CentreOn(cmbTrackLayout, top, rowHeight));

        // --- The round picker, on the same row when it fits ---
        //
        // It takes everything left over to the right-hand edge. There used
        // to be a "Reset Round" button pinned out there taking a bite out of
        // it; that is Race Control's own Reset now, which was doing the same
        // job two feet away.
        var oneRowLeft = UiLayout.After(cmbTrackLayout) + UiLayout.SectionGap;

        // Measured, not PreferredWidth: an autosize width leaves out the
        // glyph padding the caption is actually drawn with, which is the ten
        // pixels the layout pass spent every startup pushing this back out.
        var captionWidth = TextRenderer.MeasureText(lblActiveRound.Text, AppTheme.BodyFont).Width;
        var oneRowCaption = oneRowLeft + captionWidth + UiLayout.LabelGap;
        var oneRowSpace = right - oneRowCaption;

        var roundY = oneRowSpace >= wanted
            ? top
            : top + rowHeight + UiLayout.RowGap;

        var roundCaptionLeft = oneRowSpace >= wanted ? oneRowLeft : inset;

        lblActiveRound.Location = new Point(roundCaptionLeft, UiLayout.CentreOn(lblActiveRound, roundY, rowHeight));

        var comboLeft = UiLayout.After(lblActiveRound);

        cmbActiveRound.Width = Math.Max(140, right - comboLeft);
        cmbActiveRound.Location = new Point(comboLeft, UiLayout.CentreOn(cmbActiveRound, roundY, rowHeight));

        // The group grows for a second row rather than clipping it.
        var height = UiLayout.Below(cmbActiveRound, inset);

        if (grpSession.Height != height)
        {
            grpSession.Height = height;
        }
    }

    /// <summary>Set once the page search's handlers are wired, so a restyle doesn't add them again.</summary>
    private bool _navSearchFitHooked;

    /// <summary>Corner radius of the page search - the same rounding as the sidebar's buttons.</summary>
    private const int NavSearchRadius = 8;

    /// <summary>
    /// Where the page search's rounded box sits in its strip: the strip's
    /// padding on every side.
    /// </summary>
    private Rectangle NavSearchBox() => new(
        pnlNavSearch.Padding.Left,
        pnlNavSearch.Padding.Top,
        Math.Max(20, pnlNavSearch.ClientSize.Width - pnlNavSearch.Padding.Horizontal),
        Math.Max(10, pnlNavSearch.ClientSize.Height - pnlNavSearch.Padding.Vertical));

    /// <summary>
    /// Sizes and places the page search. The rounded box is drawn on the
    /// strip (PaintNavSearchBox) and the text box sits inside it with no
    /// frame of its own, so it reads as one button-shaped field.
    ///
    /// The text box is given a measured height with AutoSize off: a
    /// single-line box sizes itself to the font's line height, and with
    /// the app's own typeface that is shorter than the "g" and "p" reach.
    /// </summary>
    private void FitNavSearchHeight()
    {
        var textHeight = Math.Max(txtNavSearch.PreferredHeight, TextRenderer.MeasureText("Search pages gyp", txtNavSearch.Font).Height);

        txtNavSearch.AutoSize = false;
        txtNavSearch.Dock = DockStyle.None;
        txtNavSearch.Height = textHeight;

        // A little air above and below the letters - less than the strip
        // used to have, so it sits closer to a button's height.
        pnlNavSearch.Height = textHeight + 6 + pnlNavSearch.Padding.Vertical;

        var box = NavSearchBox();
        const int inset = 10;
        txtNavSearch.SetBounds(
            box.Left + inset,
            box.Top + (box.Height - textHeight) / 2,
            Math.Max(20, box.Width - inset * 2),
            textHeight);

        pnlNavSearch.Invalidate();
    }

    /// <summary>The page search's rounded box, filled and edged like the sidebar's buttons; the accent edge while typing.</summary>
    private void PaintNavSearchBox(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var box = NavSearchBox();
        var bounds = new RectangleF(box.Left + 0.5f, box.Top + 0.5f, box.Width - 1f, box.Height - 1f);
        var radius = Math.Max(1, Math.Min(NavSearchRadius, box.Height / 2));
        var focused = txtNavSearch.Focused;

        using var path = AppTheme.RoundedRect(bounds, radius);
        using var fill = new SolidBrush(AppTheme.SidebarHover);
        using var pen = new Pen(focused ? AppTheme.Primary : AppTheme.EdgeOn(pnlNavSearch.BackColor), focused ? 1.4f : 1f);

        g.FillPath(fill, path);
        g.DrawPath(pen, path);
    }

    private void RestyleSidebarChrome()
    {
        pnlSidebar.BackColor = AppTheme.SidebarBackground;
        pnlSidebarFooter.BackColor = AppTheme.SidebarBackground;
        lstNav.BackColor = AppTheme.SidebarBackground;
        lstNav.ForeColor = AppTheme.SidebarText;

        // The search row, which this used to leave behind - so the strip
        // between the header and the nav list came back in the content
        // theme's colours and sat there as a white band across a dark
        // sidebar.
        pnlNavSearch.BackColor = AppTheme.SidebarBackground;
        txtNavSearch.BackColor = AppTheme.SidebarHover;
        txtNavSearch.ForeColor = AppTheme.SidebarText;

        // Tall enough for the letters, descenders included. A single-line
        // box sizes itself to the font's line height, and with the app's
        // own typeface that line height is shorter than the "g" and "p"
        // actually reach - so the box cut them off however tall the strip
        // around it was. AutoSize off lets the box be given a real height,
        // measured from the text itself, and the strip follows. Runs again
        // on every theme and text-size change, and whenever the box's font
        // changes after this.
        if (!_navSearchFitHooked)
        {
            _navSearchFitHooked = true;

            // The square ring every other input gets would sit inside the
            // rounded box - this one draws its own edge.
            InputOutline.Exclude(txtNavSearch);

            txtNavSearch.FontChanged += (_, _) => FitNavSearchHeight();
            pnlNavSearch.Resize += (_, _) => FitNavSearchHeight();
            pnlNavSearch.Paint += PaintNavSearchBox;
            txtNavSearch.Enter += (_, _) => pnlNavSearch.Invalidate();
            txtNavSearch.Leave += (_, _) => pnlNavSearch.Invalidate();
        }

        FitNavSearchHeight();

        // BorderStyle is deliberately NOT set here.
        //
        // It was, briefly, copied from the Designer - and that put a native
        // FixedSingle frame back on top of the themed ring InputOutline
        // draws, so the field came out double-outlined in system white.
        // InputOutline owns the edge of every input in the app; anything
        // that sets BorderStyle afterwards is fighting it and wins, which is
        // the wrong outcome.

        SetUpFooterButton(btnManageNav, DrawMenuIcon);
        _footerTooltip.SetToolTip(btnManageNav, "Manage Navigation");

        SetUpFooterButton(btnMuteToggle, DrawMuteIcon);

        SetUpFooterButton(btnUndo, DrawUndoIcon, () => _undo.Count);
    }

    /// <summary>
    /// Footer buttons whose Paint handler has already been wired.
    ///
    /// This method runs again on every theme change and every font-size
    /// change. A second Paint handler on the same button draws the icon (and
    /// its badge) twice, which on anti-aliased edges reads as a smudge
    /// rather than as a bug, so it is the kind of thing that accumulates
    /// quietly.
    /// </summary>
    private static readonly HashSet<Button> FooterPainted = new();

    /// <summary>
    /// Styles a sidebar footer button as a plain icon button, one of a row
    /// of them: no caption - the name is a tooltip instead, set by the
    /// caller (Mute's is kept live by UpdateMuteButtonText, since it
    /// changes with the mute state; Undo's by the fuller one _undo.
    /// StateChanged already sets). An icon row reads at a glance and costs
    /// far less height than a stack of captioned buttons, which is what
    /// earns the nav list above it a taller list.
    ///
    /// badgeCount, when given, is read live at paint time (so it always
    /// shows the current count with no extra wiring) and draws a small
    /// pill in the icon's corner while it is above zero - only Undo uses
    /// this today.
    /// </summary>
    private static void SetUpFooterButton(Button button, Action<Graphics, Rectangle, Color> drawIcon, Func<int>? badgeCount = null)
    {
        button.Text = string.Empty;
        button.FlatStyle = FlatStyle.Flat;

        // Border comes from the theme, not from here - this used to set its
        // own dimmer cyan, which is why the footer looked different from
        // every other button.
        AppTheme.ApplyDefaultBorder(button);
        button.BackColor = AppTheme.SidebarHover;
        button.ForeColor = AppTheme.SidebarText;
        button.Font = AppTheme.SmallFont;

        if (!FooterPainted.Add(button))
        {
            return;
        }

        button.Paint += (_, e) =>
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            var size = Math.Min(button.Width, button.Height) - 20;
            var iconBounds = new Rectangle((button.Width - size) / 2, (button.Height - size) / 2, size, size);
            drawIcon(e.Graphics, iconBounds, AppTheme.SidebarText);

            var count = badgeCount?.Invoke() ?? 0;

            if (count > 0)
            {
                DrawFooterBadge(e.Graphics, button, count);
            }
        };
    }

    /// <summary>
    /// The small count pill in a footer icon's corner - today, how many
    /// undos are waiting, in place of the "Undo (3): ..." caption a row of
    /// icon buttons has no room left to show. The full detail is still one
    /// hover away, in the tooltip _undo.StateChanged keeps current.
    /// </summary>
    private static void DrawFooterBadge(Graphics g, Button button, int count)
    {
        var text = count > 99 ? "99+" : count.ToString();

        using var font = new Font(AppTheme.SmallFont.FontFamily, 7.5f, FontStyle.Bold);
        var textWidth = TextRenderer.MeasureText(g, text, font).Width;
        var width = Math.Max(16, textWidth + 8);
        var badgeRect = new Rectangle(button.Width - width - 4, 3, width, 15);

        using (var path = AppTheme.RoundedRect(badgeRect, badgeRect.Height / 2))
        using (var fill = new SolidBrush(AppTheme.Accent))
        {
            g.FillPath(fill, path);
        }

        TextRenderer.DrawText(g, text, font, badgeRect, AppTheme.OnAccent,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    /// <summary>Counter-clockwise arc with an arrowhead - the standard undo glyph.</summary>
    /// <summary>Starts or stops the header animation based on the current theme and setting.</summary>
    private void StartOrStopHeaderAnimation()
    {
        // Never while the club logo is showing: the header draws the logo
        // instead of the scene, so the timer would only be repainting a
        // still picture twenty times a second.
        var shouldRun = AppTheme.CurrentMode == AppTheme.Mode.Synthwave && _settings.AnimateHeader && !lblAppTitle.HasLogo;

        // The stars read the animation phase, which only moves while the
        // timer is running - so the header is told whether it is being
        // animated rather than having to guess from a frozen value.
        lblAppTitle.Animated = shouldRun;

        if (shouldRun)
        {
            _headerAnimationTimer.Start();
        }
        else
        {
            _headerAnimationTimer.Stop();
            lblAppTitle.Invalidate();
        }
    }

    /// <summary>Called by the Themes page when the animation setting is toggled.</summary>
    public void ApplyHeaderAnimationSetting() => StartOrStopHeaderAnimation();

    /// <summary>
    /// Puts the club logo in the sidebar's top-left corner, or takes it
    /// out, from settings - at startup, and again whenever the logo or
    /// "Show the logo in the app's top-left corner" changes on Themes →
    /// Looks. Live, unlike the look itself: the header is one control that
    /// repaints on its own, so there is nothing captured to go stale.
    ///
    /// The header keeps its own scaled copy (SidebarHeader.SetLogo), so the
    /// image loaded here is disposed straight after. With a logo the header
    /// is the tall Synthwave height whatever the look, so the logo has room
    /// to be seen; without one it goes back to what the look gives it.
    /// </summary>
    public void RefreshHeaderLogo()
    {
        var file = _settings.RelayLogoFile;
        var logo = _settings.ShowLogoInApp ? ClubLogo.Load(file) : null;

        try
        {
            lblAppTitle.SetLogo(logo, logo is null ? null : ClubLogo.PlateFor(file, AppTheme.SidebarBackground));
        }
        finally
        {
            logo?.Dispose();
        }

        var height = lblAppTitle.HasLogo || AppTheme.CurrentMode == AppTheme.Mode.Synthwave ? 132 : 64;

        if (lblAppTitle.Height != height)
        {
            lblAppTitle.Height = height;
        }

        lblAppTitle.Invalidate();
        StartOrStopHeaderAnimation();
    }

    /// <summary>
    /// Opens Themes (the Looks editor) - from the Club Website page, whose colour and logo
    /// settings moved there.
    /// </summary>
    private void ShowLooks()
    {
        GoToPage("Themes");
        _looksControl.RefreshAll();
    }

    /// <summary>
    /// Restarts the app after a new app look has been saved - the same
    /// restart the old Light/Dark/Synthwave button did, since most of the
    /// window captures its colours when it is built.
    /// </summary>
    private static void RestartForNewLook()
    {
        Application.Restart();
        Environment.Exit(0);
    }

    /// <summary>Re-runs the refresh hook for whichever page is currently showing, if it has one.</summary>
    /// <summary>
    /// Applies the same setup the Designer's page loop does, for pages
    /// declared outside it.
    /// </summary>

    /// <summary>
    /// Drives ambience intensity from race state, so the crowd follows what
    /// is actually happening rather than droning at one level.
    ///
    /// Deliberately reads settings at call time rather than caching them -
    /// the Ambience tab can be edited mid-race, and the next event should
    /// respect the change without needing a restart.
    /// </summary>
    private void WireAmbienceToRace()
    {
        _ambience.Settings = _ambienceStore.Current;
        _ambience.StatusMessage += OnClientRawMessage;

        // Deliberately NOT started here. The app opening is not a session
        // starting, and audio beginning on its own at launch is startling -
        // especially on a shared club machine where someone is just
        // checking the schedule. Ambience comes up when a race or practice
        // starts (see the auto-start settings on the Mixer), when Preview
        // is pressed, or when it's switched on by hand.

        _race.StateChanged += state =>
        {
            HandleAudioAutoStart(state);
            HandleGhosts(state);

            // The run sheet's "actually started" stamp. Here rather than in
            // the start buttons because every path to a green flag - manual,
            // auto-start, Start Next, the programme - lands in this event,
            // and MarkStarted keeps only the first stamp per line.
            //
            // Not for practice or a demo: neither runs the round, and a
            // stamp from one would show the heat as started when it never
            // was.
            if (state == RaceState.Racing && !_race.IsPracticeMode && !_race.IsDemoMode && _activeRound is { } startedRound)
            {
                _daySchedule.MarkStarted(startedRound.Id, DateTime.UtcNow);
            }

            // An automatic backup that came due during a race was put off
            // (see CheckAutoBackup) - made now the track has gone quiet,
            // rather than waiting up to an hour for the next check.
            //
            // Always posted, even on the UI thread: Idle is also passed
            // through on the way INTO a session - practice resets the
            // session and then goes green in the same call - so the state is
            // read again once whatever raised this has finished, and a
            // session that has just started still puts the backup off.
            if (state is RaceState.Finished or RaceState.Idle)
            {
                TryPost(CheckAutoBackup);
            }

            // Reset BEFORE the running check - ambience might be switched on
            // mid-race, and these flags need to reflect the race that's
            // actually happening rather than whenever audio last started.
            if (state == RaceState.Racing)
            {
                _ambienceBattleFired = false;
                _ambienceLappedFired = false;
            }

            if (!_ambience.IsRunning)
            {
                return;
            }

            _ambience.SetIntensity(state switch
            {
                RaceState.Staging or RaceState.Countdown => AmbienceService.Intensity.Anticipation,
                RaceState.Racing => AmbienceService.Intensity.Racing,

                // A finish is a moment worth reacting to, then everything
                // settles back to idle between races.
                // Finished is handled by RaceCompleted below, which holds
                // at peak for the celebration window and THEN settles. This
                // used to set Peak here and never come down, because the
                // session stays Finished until the next race starts.
                RaceState.Finished => _ambience.EffectiveIntensity,

                _ => AmbienceService.Intensity.Idle,
            });
        };

        // A red flag kills the atmosphere; a restart brings it back.
        _race.FlagChanged += flag =>
        {
            if (!_ambience.IsRunning || _race.State != RaceState.Racing)
            {
                return;
            }

            _ambience.SetIntensity(flag switch
            {
                FlagState.Red => AmbienceService.Intensity.Idle,
                FlagState.Yellow => AmbienceService.Intensity.Anticipation,
                _ => AmbienceService.Intensity.Racing,
            });
        };

        _race.JumpStartDetected += (_, _) =>
        {
            if (_ambienceStore.Current.SurgeOnJumpStart)
            {
                _ambience.Surge(reason: "Jump start");
            }
        };

        // The session already flags a race-best lap on the lap event, so
        // this needs no new plumbing - and it fires only for the FASTEST of
        // the race, not every personal best, which would surge constantly.
        _race.LapProcessed += (_, _, isFastestLap) =>
        {
            if (!_ambience.IsRunning || !_ambienceStore.Current.ReactToEvents)
            {
                return;
            }

            if (isFastestLap && _ambienceStore.Current.SurgeOnFastLap)
            {
                _ambience.Surge(reason: "Fastest lap of the race");
            }

            CheckAmbienceBattle();
            CheckAmbienceLapped();
        };

        _race.PersonalBestLap += _ =>
        {
            if (_ambienceStore.Current.SurgeOnPersonalBest)
            {
                _ambience.Surge(reason: "Personal best");
            }
        };

        // Last lap - the crowd knows it's nearly over.
        _race.TimeLimitReached += () =>
        {
            if (_ambienceStore.Current.SurgeOnLastLap)
            {
                _ambience.Surge(reason: "Last lap");
            }
        };

        _race.RaceCompleted += () =>
        {
            if (!_ambience.IsRunning)
            {
                return;
            }

            // A plain Surge would restore to the base mood, which at a
            // finish is itself peak - so the crowd never came down. This
            // holds for the configured window then fades to idle.
            if (_ambienceStore.Current.SurgeOnFinish)
            {
                _ambience.CelebrateFinishThenSettle(_ambienceStore.Current.FinishCelebrationSeconds);
            }
            else
            {
                _ambience.SetIntensity(AmbienceService.Intensity.Idle);
            }
        };

        // Lead changes and track records surface as announcement text - the
        // announcer already decides what counts as either, so matching on
        // its output avoids a second set of rules that could disagree.
        _announcer.AnnouncementTextReady += text =>
        {
            // Onto the UI thread, the same guard every other announcer
            // subscriber in this file uses.
            //
            // This is raised from more than one thread: the race recap and
            // un-cached flag calls are spoken from Task.Run, and they raise
            // it too. The ambience engine is driven from a WinForms timer
            // everywhere else, so calling Surge from a pool thread was a
            // second hand on it that nothing coordinated with.
            if (IsHandleCreated && InvokeRequired)
            {
                TryPost(() => ReactToAnnouncement(text));
                return;
            }

            ReactToAnnouncement(text);
        };
    }

    /// <summary>Surges the crowd ambience for the announcements that deserve it. UI thread only.</summary>
    private void ReactToAnnouncement(string text)
    {
        if (!_ambience.IsRunning || !_ambienceStore.Current.ReactToEvents)
        {
            return;
        }

        if (_ambienceStore.Current.SurgeOnTrackRecord
            && text.Contains("track record", StringComparison.OrdinalIgnoreCase))
        {
            _ambience.Surge(AmbienceService.Intensity.Peak, "Track record");
        }
        else if (_ambienceStore.Current.SurgeOnLeadChange
                 && (text.Contains("takes the lead", StringComparison.OrdinalIgnoreCase)
                     || text.Contains("new leader", StringComparison.OrdinalIgnoreCase)
                     || text.Contains("lead change", StringComparison.OrdinalIgnoreCase)
                     || text.Contains("into the lead", StringComparison.OrdinalIgnoreCase)
                     || text.Contains("hits the front", StringComparison.OrdinalIgnoreCase)
                     || text.Contains("grabs P1", StringComparison.OrdinalIgnoreCase)
                     || text.Contains("leads the way", StringComparison.OrdinalIgnoreCase)))
        {
            _ambience.Surge(reason: "Lead change");
        }
        else if (_ambienceStore.Current.SurgeOnStreak
                 && text.Contains("streak", StringComparison.OrdinalIgnoreCase))
        {
            _ambience.Surge(reason: "Streak");
        }
    }

    /// <summary>
    /// Surges when two cars on the same lap are running within a second of
    /// each other.
    ///
    /// Fires at most once per race: a genuine scrap lasts many laps, and
    /// surging on every one of them would leave the ambience pinned high
    /// for the whole race rather than marking the moment it developed.
    /// </summary>
    private void CheckAmbienceBattle()
    {
        if (_ambienceBattleFired || !_ambienceStore.Current.SurgeOnCloseBattle)
        {
            return;
        }

        var ordered = _race.GetOrderedStandings();

        for (var i = 0; i < ordered.Count - 1; i++)
        {
            // Same lap only - a one-second gap between cars a lap apart
            // isn't a battle, it's a coincidence of timing.
            // Measured at the line (see RaceGaps) - comparing lap counts
            // skipped every pair for the moment the front car had crossed
            // and the one behind hadn't yet.
            if (ordered[i].Laps < 2)
            {
                continue;
            }

            var gap = RaceGaps.Between(ordered[i + 1], ordered[i]);
            if (gap.LapsDown == 0 && gap.GapMs is > 0 and < 1000)
            {
                _ambienceBattleFired = true;
                _ambience.Surge(reason: "Close battle");
                return;
            }
        }
    }

    /// <summary>
    /// Surges when the leader first laps someone. Also once per race, for
    /// the same reason as the battle check.
    /// </summary>
    private void CheckAmbienceLapped()
    {
        if (_ambienceLappedFired || !_ambienceStore.Current.SurgeOnLapped)
        {
            return;
        }

        var ordered = _race.GetOrderedStandings();
        if (ordered.Count < 2)
        {
            return;
        }

        // A real lap down, not just the leader crossing the line first -
        // which is what the old lap-count comparison fired on, on lap one.
        if (RaceGaps.Between(ordered[^1], ordered[0]).LapsDown >= 1)
        {
            _ambienceLappedFired = true;
            _ambience.Surge(reason: "Car lapped");
        }
    }


    /// <summary>
    /// Brings music, ambience and the announcer up when a session starts,
    /// per the shared auto-start settings.
    ///
    /// Covers a quick race for free: that drives the same session through
    /// the same states as any other race, so keying off state rather than
    /// off which button was pressed means there's no separate path to keep
    /// in step.
    /// </summary>

    /// <summary>
    /// Starts and stops virtual opponents with the race.
    ///
    /// Ghosts get real driver profiles created for them, so every existing
    /// path - the leaderboard, announcements, the big screen - names them
    /// properly without knowing they aren't real.
    /// </summary>
    private void HandleGhosts(RaceState state)
    {
        _ghosts ??= CreateGhostService();

        if (state == RaceState.Racing && _ghostStore.Current.Enabled)
        {
            EnsureGhostProfiles();
            _ghosts.Settings = _ghostStore.Current;

            // A demo runs only the ghosts ticked for it on the Demo Mode
            // page (none, unless some were); a real race runs them all.
            var onlyThese = _race.IsDemoMode
                ? _demoControl?.SelectedGhosts ?? Array.Empty<string>()
                : null;

            _ghosts.Start(cmbTrackLayout.SelectedItem as string, onlyThese);
        }
        else if (state is RaceState.Idle or RaceState.Finished or RaceState.Staging or RaceState.Countdown)
        {
            // Staging and Countdown too: a race staged straight over one
            // still running (or over a demo) never passes through Idle, so
            // its ghosts kept lapping through the new start. They start
            // afresh, with the new race's track and pace, at its green.
            _ghosts.Stop();

            // Or the next race would open by announcing a pass that
            // happened in the last one.
            _ghostWasAhead.Clear();
        }
    }

    /// <summary>
    /// Ghost profiles kept in step as ghosts are added, edited and deleted -
    /// so a new ghost is immediately pickable everywhere.
    ///
    /// The WHOLE of it on the UI thread. A save from another thread used to
    /// hop across for the profiles only, and skip the picker refreshes that
    /// were the reason for the handler - so a ghost saved that way stayed
    /// missing from every list until the page was left and re-entered.
    /// </summary>
    private void OnGhostStoreSaved()
    {
        if (InvokeRequired)
        {
            TryPost(OnGhostStoreSaved);
            return;
        }

        EnsureGhostProfiles();

        // Rebuild the pickers immediately. They cache their driver list
        // when built and only rebuild on navigation, so a ghost added
        // while another page was open wouldn't appear until that page
        // was left and re-entered.
        _practiceControl?.RefreshGhosts();

        foreach (var refresh in _navRefreshHooks.Values)
        {
            try
            {
                refresh();
            }
            catch
            {
                // One page failing to refresh must not stop the others.
            }
        }
    }

    private GhostService CreateGhostService()
    {
        // The track layouts feed the track-record ghost; the personal-best
        // resets keep a PB ghost chasing the same number the Personal Bests
        // page shows rather than a lap that page has retired.
        var service = new GhostService(_race, _profiles, _sessionHistory, _trackLayouts, _pbResets);
        service.StatusMessage += OnClientRawMessage;

        // Position changes against a ghost are the whole point of racing
        // one - being told you've been passed is what makes it an opponent
        // rather than a number on a screen.
        service.GhostLapCompleted += (_, _) => CheckGhostPositions();

        return service;
    }

    /// <summary>Who is ahead of whom, last time we looked.</summary>
    private readonly Dictionary<string, bool> _ghostWasAhead = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Announces when a real driver and a ghost swap places.
    ///
    /// Compares against the LAST known state rather than announcing on
    /// every lap, so a steady gap stays quiet and only an actual change
    /// gets called - the same rule a human commentator follows.
    /// </summary>
    private void CheckGhostPositions()
    {
        if (!_ghostStore.Current.AnnounceGhosts || _race.State != RaceState.Racing)
        {
            return;
        }

        var standings = _race.GetOrderedStandings();

        if (standings.Count < 2)
        {
            return;
        }

        // RacerStanding uses TransponderId, not Transponder - a different
        // type from RaceStanding, which is what the announcer takes.
        var human = standings.FirstOrDefault(s => !GhostService.IsGhost(s.TransponderId));

        if (human is null)
        {
            return;
        }

        var humanIndex = standings.ToList().IndexOf(human);

        foreach (var ghost in standings.Where(s => GhostService.IsGhost(s.TransponderId)))
        {
            var ghostAhead = standings.ToList().IndexOf(ghost) < humanIndex;

            if (_ghostWasAhead.TryGetValue(ghost.TransponderId, out var wasAhead) && wasAhead != ghostAhead)
            {
                _announcer.NotifyGhostPass(
                    human.TransponderId,
                    ghost.TransponderId,
                    humanPassedGhost: !ghostAhead);
            }

            _ghostWasAhead[ghost.TransponderId] = ghostAhead;
        }
    }

    /// <summary>
    /// Gives each ghost a driver profile, so it shows a name rather than a
    /// raw transponder everywhere a driver is displayed.
    ///
    /// Marked ExcludeFromRecords - a virtual car must never take a club's
    /// all-time record, which would be both wrong and very annoying.
    /// </summary>
    /// <summary>
    /// Creates or updates a driver profile for every ghost, and removes
    /// profiles for ghosts that no longer exist.
    ///
    /// Runs on every ghost save, not just at race start - a ghost has to
    /// have a profile before it can be picked in a roster or demo mode,
    /// and it was only getting one after a race had already run.
    /// </summary>
    private void EnsureGhostProfiles()
    {
        // Ghosts that have been deleted or renamed leave orphan profiles
        // behind, which would keep appearing in every picker.
        var liveTransponders = _ghostStore.Current.Ghosts
            .Select(g => g.Transponder)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var orphans = _profiles.AllIncludingGhosts
            .Where(p => GhostService.IsGhost(p.Transponder) && !liveTransponders.Contains(p.Transponder))
            .Select(p => p.Transponder)
            .ToList();

        foreach (var orphan in orphans)
        {
            _profiles.Delete(orphan);
        }

        // Every ghost gets a profile, enabled or not - a disabled ghost
        // should still be selectable, it just won't run until switched on.
        foreach (var ghost in _ghostStore.Current.Ghosts)
        {
            var existing = _profiles.Get(ghost.Transponder);

            if (existing is not null
                && existing.DriverName == ghost.Name
                && existing.CarModel == ghost.CarModel)
            {
                continue;
            }

            _profiles.Upsert(new DriverProfile
            {
                Transponder = ghost.Transponder,
                DriverName = ghost.Name,
                CarModel = ghost.CarModel,
                ExcludeFromRecords = true,
                Notes = "Virtual opponent (ghost car).",
            });
        }

        _profiles.Save();

        // Reports what actually happened. Three attempts at making ghosts
        // appear in the pickers have now failed, so this says plainly how
        // many ghosts exist, how many profiles they have, and how many the
        // pickers will therefore show.
        var ghostCount = _ghostStore.Current.Ghosts.Count;
        var ghostProfiles = _profiles.AllIncludingGhosts.Count(p => GhostService.IsGhost(p.Transponder));
        var raceable = _profiles.AllRaceable.Count;

        OnClientRawMessage(
            $"{AppTime.LogStampNow()}  [ghost] {Plural.Of(ghostCount, "ghost")} configured, "
            + $"{Plural.Of(ghostProfiles, "ghost profile")}, {Plural.Of(raceable, "selectable driver")} total.");
    }

    private void HandleAudioAutoStart(RaceState state)
    {
        var settings = _settings;

        // Practice and races are the same states - the session's own flag
        // is what distinguishes them.
        var wanted = _race.IsPracticeMode
            ? settings.AudioStartWithPractice
            : settings.AudioStartWithRaces;

        // "From staging" also fires on Racing, because PRACTICE never
        // enters Staging at all - it calls GreenFlag directly and jumps
        // straight to Racing. Matching only Staging meant practice could
        // never trigger, however the setting was configured.
        //
        // _sessionAudioStarted stops the Racing case from re-firing for a
        // race that already started at staging, or after a caution.
        var trigger = settings.AudioStartAtStaging
            ? state is RaceState.Staging or RaceState.Racing
            : state is RaceState.Racing;

        if (state is RaceState.Idle or RaceState.Finished)
        {
            // Only tear down what this actually brought up. Ambience the
            // user started by hand should survive a race ending - stopping
            // it would be the app overriding a deliberate choice.
            if (_sessionAudioStarted)
            {
                StopSessionAudio();
            }

            _sessionAudioStarted = false;
        }

        if (wanted && trigger && !_sessionAudioStarted)
        {
            _sessionAudioStarted = true;
            StartSessionAudio();
        }

        // "Stop while a race is running" and "start music when a session
        // starts" are directly contradictory. Auto-start wins, because it's
        // the more specific instruction and the one just acted on - without
        // this guard the two fought each other inside a single call: music
        // started, then stopped microseconds later, looking like auto-start
        // simply didn't work.
        var autoStartWantsMusic = wanted && _settings.AudioStartMusic;

        if (!autoStartWantsMusic
            && _musicStore.Current.PauseDuringRaces
            && state is RaceState.Staging or RaceState.Countdown or RaceState.Racing)
        {
            _music.Stop();
        }
    }

    /// <summary>Starts whichever sources the auto-start settings name.</summary>

    /// <summary>
    /// Takes down the audio that StartSessionAudio brought up.
    ///
    /// Deliberately NOT the mirror image of the start: music defaults to
    /// carrying on, because the gap between heats is when a soundtrack is
    /// most wanted. Ambience defaults to stopping, because crowd noise at
    /// an empty track just sounds wrong.
    /// </summary>

    /// <summary>
    /// Ends the session's music once the broadcast is genuinely over.
    ///
    /// Fired after the recap rather than at the flag, so a fade doesn't cut
    /// across the summary - and it still fires when the recap is switched
    /// off, or music set to fade afterwards would simply never stop.
    /// </summary>
    private void HandleSessionAudioFinished()
    {
        // Stopped at the flag instead (see StopSessionAudio). Music playing
        // now was started since, by hand or by the next session - not this
        // broadcast's to end.
        if (!_music.IsPlaying || _settings.AudioStopMusicAfterSession)
        {
            return;
        }

        // The end of the last broadcast, heard after the next session has
        // already brought its music up: the recap and the wait after it run
        // well past the flag, and the next heat can be staged meanwhile. That
        // music is the new session's - fading it here stopped it a few
        // seconds into the heat. Only another session can have set the flag
        // by now: a session's own Finished clears it (HandleAudioAutoStart)
        // before its recap even starts.
        if (_sessionAudioStarted && _settings.AudioStartMusic)
        {
            OnClientRawMessage($"{AppTime.LogStampNow()}  [audio] Music kept playing - the next session has already started.");
            return;
        }

        switch (_settings.MusicAfterSummary)
        {
            case MusicEndBehaviour.FadeOut:
                var seconds = Math.Clamp(_settings.MusicFadeOutSeconds, 0.5, 30);
                OnClientRawMessage($"{AppTime.LogStampNow()}  [audio] Music fading out over {seconds:0.#}s - broadcast finished.");
                _ = _music.FadeOutAsync(seconds);
                break;

            case MusicEndBehaviour.StopImmediately:
                _music.Stop();
                OnClientRawMessage($"{AppTime.LogStampNow()}  [audio] Music stopped - broadcast finished.");
                break;

            case MusicEndBehaviour.KeepPlaying:
            default:
                // Deliberately nothing - the gap between heats is exactly
                // when a soundtrack earns its keep.
                break;
        }
    }

    private void StopSessionAudio()
    {
        if (_settings.AudioStopAmbienceAfterSession && _ambience.IsRunning)
        {
            // After the finish celebration, which runs from RaceCompleted
            // and holds the crowd at peak for its configured window, and then
            // faded out over the Ambience tab's fade-down time rather than
            // cut. It used to stop dead two seconds after the celebration,
            // part-way through the crowd settling - so the fade the club
            // set never played out, and the cheer just vanished.
            var celebration = _ambienceStore.Current.SurgeOnFinish
                ? Math.Clamp(_ambienceStore.Current.FinishCelebrationSeconds, 0, 300)
                : 0;
            var fade = Math.Clamp(_ambienceStore.Current.FadeDownSeconds, 0.5, 30);

            _ = Task.Run(async () =>
            {
                try
                {
                    if (celebration > 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(celebration));
                    }

                    // Only if another session hasn't started in the meantime
                    // - between heats that gap is short, and cutting the new
                    // race's atmosphere would be worse than leaving the old
                    // one running.
                    if (!_sessionAudioStarted && _ambience.IsRunning)
                    {
                        await _ambience.FadeOutAndStopAsync(fade);
                    }
                }
                catch
                {
                    // Ambience is decoration - never surface a failure.
                }
            });

            OnClientRawMessage(celebration > 0
                ? $"{AppTime.LogStampNow()}  [audio] Ambience will fade out over {fade:0.#}s after the {celebration}s finish celebration."
                : $"{AppTime.LogStampNow()}  [audio] Ambience fading out over {fade:0.#}s - session ended.");
        }

        // Music normally carries on through the recap - the checkered flag
        // is not the end of the broadcast, see HandleSessionAudioFinished.
        // Unless the club asked for it to stop at the flag ("At the flag,
        // stop: Music" on the Mixer), for a track that should go quiet the
        // moment racing does.
        if (_settings.AudioStopMusicAfterSession && _music.IsPlaying)
        {
            _music.Stop();
            OnClientRawMessage($"{AppTime.LogStampNow()}  [audio] Music stopped - the race ended.");
        }
    }

    private void StartSessionAudio()
    {
        // Ticking a source under "bring up" is the more specific
        // instruction, so it overrides that source's own master switch.
        // Requiring BOTH meant a disabled source silently refused to start
        // with nothing said about why - the setting looked broken.
        if (_settings.AudioStartMusic)
        {
            if (_music.IsPlaying)
            {
                // Already going - leave it rather than restarting a track
                // someone is part-way through. But not fading out: the last
                // session's end may still be taking it down, and that fade
                // used to stop it a few seconds into this one.
                _music.CancelFade();
            }
            else if (_musicStore.Current.Tracks.Count == 0)
            {
                OnClientRawMessage($"{AppTime.LogStampNow()}  [audio] Music not started - the library is empty.");
            }
            else
            {
                _music.Settings = _musicStore.Current;
                _music.Play();

                OnClientRawMessage(_music.IsPlaying
                    ? $"{AppTime.LogStampNow()}  [audio] Music started for the new session."
                    : $"{AppTime.LogStampNow()}  [audio] Music couldn't start - no enabled track has a readable file.");
            }
        }

        if (_settings.AudioStartAmbience && !_ambience.IsRunning)
        {
            _ambience.Settings = _ambienceStore.Current;

            // ignoreEnabledSetting, for the same reason as music above.
            _ambience.Start(ignoreEnabledSetting: true);

            OnClientRawMessage(_ambience.IsRunning
                ? $"{AppTime.LogStampNow()}  [audio] Ambience started for the new session."
                : $"{AppTime.LogStampNow()}  [audio] Ambience couldn't start - no enabled layer has a readable file.");
        }

        // Unmuting matters more than it sounds: a muted announcer is easy
        // to leave set from a previous session and produces a silent race
        // with no obvious cause.
        if (_settings.AudioUnmuteAnnouncer && _announcer.Muted)
        {
            SetAllSoundMuted(false);
            OnClientRawMessage($"{AppTime.LogStampNow()}  [audio] Unmuted for the new session.");
        }
    }

    /// <summary>
    /// F11: the whole screen becomes the race. Borderless, no sidebar, no
    /// taskbar - Race Control, the clock and the board, which is what the
    /// room actually needs from the moment cars stage. F11 or Esc brings
    /// the app back exactly as it was.
    ///
    /// Border first, then Normal, then Maximized: a window that is already
    /// maximized keeps its old working-area bounds when its border
    /// changes, so without the bounce through Normal the taskbar's strip
    /// stayed uncovered.
    /// </summary>
    private void ToggleRaceMode()
    {
        _raceMode = !_raceMode;

        SuspendLayout();

        try
        {
            if (_raceMode)
            {
                _preRaceModeState = WindowState;
                pnlSidebar.Visible = false;
                FormBorderStyle = FormBorderStyle.None;
                WindowState = FormWindowState.Normal;
                WindowState = FormWindowState.Maximized;

                // Selected, not just shown, so the sidebar is on Race
                // Control when the app comes back - see GoToPage.
                GoToPage("Race Control");
            }
            else
            {
                FormBorderStyle = FormBorderStyle.Sizable;

                // The same bounce through Normal as on the way in, for the
                // same reason: setting Maximized on a window that already
                // is one changes nothing, so it kept the borderless
                // full-screen bounds and sat over the taskbar with its
                // title bar off the top of the screen.
                WindowState = FormWindowState.Normal;
                WindowState = _preRaceModeState;
                pnlSidebar.Visible = true;

                // Race Control hidden from the sidebar could not be
                // highlighted on the way in. Coming back, the page under
                // the highlight is put back up, so the two agree again.
                if (lstNav.SelectedItem is string selected && selected != _shownPage)
                {
                    ShowPage(selected);
                }
            }
        }
        finally
        {
            ResumeLayout(true);
        }

        OnClientRawMessage($"{AppTime.LogStampNow()}  [view] Race Mode {(_raceMode ? "ON - F11 or Esc brings the app back" : "off")}.");
    }

    private static void ConfigurePage(Panel page)
    {
        page.Dock = DockStyle.Fill;
        page.BackColor = AppTheme.Background;
        page.Visible = false;
    }

    private void RefreshCurrentPage()
    {
        // The page on screen, not the sidebar's highlight - they differ when
        // a button has opened a page hidden from the sidebar, and an undo
        // then refreshed the page behind it and left the one in front stale.
        if ((_shownPage ?? lstNav.SelectedItem) is string name && _navRefreshHooks.TryGetValue(name, out var refresh))
        {
            try
            {
                refresh();
            }
            catch
            {
                // A refresh failing must never take down the undo itself.
            }
        }
    }

    // ---- Footer icons ----
    //
    // Drawn on a 24-unit grid and scaled into whatever square the button
    // gives them, with round caps and joins, the way an icon set is drawn.
    // They used to be placed in raw pixels - a speaker body a fixed 6x8, an
    // arrowhead a fixed few pixels off the arc - so at the footer's actual
    // size the speaker was a stub with a dot for a wave and the undo arrow
    // had lost its head. Scaling one grid keeps every part in proportion at
    // any text size.

    /// <summary>Runs a drawing on the 24-unit icon grid, scaled into the bounds.</summary>
    private static void DrawOnIconGrid(Graphics g, Rectangle b, Action<Graphics> draw)
    {
        var state = g.Save();

        try
        {
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
            g.TranslateTransform(b.Left, b.Top);
            g.ScaleTransform(b.Width / 24f, b.Height / 24f);
            draw(g);
        }
        finally
        {
            g.Restore(state);
        }
    }

    /// <summary>An icon stroke: the grid's line weight, with round ends and corners.</summary>
    private static Pen IconPen(Color color) => new(color, 2.2f)
    {
        StartCap = System.Drawing.Drawing2D.LineCap.Round,
        EndCap = System.Drawing.Drawing2D.LineCap.Round,
        LineJoin = System.Drawing.Drawing2D.LineJoin.Round,
    };

    /// <summary>
    /// Undo: a chevron pointing back, on a line that runs right and loops
    /// down and round - the usual "go back one step" arrow.
    /// </summary>
    private static void DrawUndoIcon(Graphics g, Rectangle b, Color color) => DrawOnIconGrid(g, b, grid =>
    {
        using var pen = IconPen(color);
        using var path = new System.Drawing.Drawing2D.GraphicsPath();

        // The arrow's shaft and its loop, as one stroke so the corners join.
        path.AddLine(4f, 9f, 14.5f, 9f);
        path.AddArc(9f, 9f, 11f, 11f, -90f, 180f);
        path.AddLine(14.5f, 20f, 10f, 20f);
        grid.DrawPath(pen, path);

        // The head.
        grid.DrawLines(pen, new[] { new PointF(9f, 14f), new PointF(4f, 9f), new PointF(9f, 4f) });
    });

    /// <summary>
    /// Set by a left mouse-down on the Undo button's caret, so the Click
    /// that follows opens the list rather than undoing one step.
    /// </summary>
    private bool _undoCaretPressed;

    /// <summary>
    /// Undoes the newest <paramref name="count"/> steps and logs each one to
    /// the console, the same line a single undo has always written, then
    /// refreshes the page in front of the user once. <paramref name="via"/>
    /// is tacked onto each line to say how it was asked for (" (Ctrl+Z)").
    /// </summary>
    private void UndoSteps(int count, string via)
    {
        if (!_undo.CanUndo)
        {
            OnClientRawMessage($"{AppTime.LogStampNow()}  [undo] Nothing to undo.");
            return;
        }

        // Stops at the first step that fails; that failure is logged by
        // UndoFailed, and whatever did come off before it is logged here.
        var undone = _undo.UndoSteps(count);

        if (undone.Count == 0)
        {
            return;
        }

        foreach (var description in undone)
        {
            OnClientRawMessage($"{AppTime.LogStampNow()}  [undo] Undid: {description}{via}");
        }

        // An undo can restore data the page in front of the user is
        // showing - a class deleted from a dialog, say - so re-run that
        // page's refresh rather than leaving it stale until the user
        // happens to navigate away and back.
        RefreshCurrentPage();
    }

    /// <summary>
    /// The list behind the Undo button: every pending step, newest first.
    /// Picking one undoes it AND everything newer above it, in order -
    /// the only order they can safely come off in, since a later change
    /// may sit on top of an earlier one.
    /// </summary>
    private void ShowUndoMenu()
    {
        var pending = _undo.PendingDescriptions;

        if (pending.Count == 0)
        {
            return;
        }

        var menu = new ContextMenuStrip { ShowItemToolTips = true };

        for (var i = 0; i < pending.Count; i++)
        {
            var steps = i + 1;
            var item = new ToolStripMenuItem($"Undo: {pending[i]}");

            if (i > 0)
            {
                item.ToolTipText = $"Also undoes the {Plural.Of(i, "newer step")} above it - {Plural.Of(steps, "step")} in all.";
            }

            item.Click += (_, _) => UndoSteps(steps, steps > 1 ? " (from the Undo list)" : string.Empty);
            menu.Items.Add(item);
        }

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Picking an older step undoes everything above it too.") { Enabled = false });

        // Styled after every item is in, so each item gets the theme's font
        // and padding - an unstyled menu is stock grey Windows chrome.
        AppTheme.StyleContextMenu(menu);
        menu.Closed += (_, _) => BeginInvoke((MethodInvoker)menu.Dispose);

        // Upwards: the button sits at the very bottom of the sidebar.
        menu.Show(btnUndo, new Point(0, 0), ToolStripDropDownDirection.AboveRight);
    }

    /// <summary>
    /// Where the Undo button's "there's a list" caret goes: the bottom-right
    /// corner, opposite the count badge at the top-right, clear of the icon.
    /// </summary>
    private static Rectangle UndoCaretBounds(Button button) =>
        new(button.Width - 16, button.Height - 13, 12, 10);

    /// <summary>A small downward chevron - the usual "click for more" mark on a split button.</summary>
    private static void DrawUndoCaret(Graphics g, Rectangle b, Color color)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var pen = new Pen(color, 1.6f) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };

        var midX = b.X + b.Width / 2f;
        var top = b.Y + b.Height / 2f - 2f;
        g.DrawLines(pen, new[] { new PointF(midX - 3.5f, top), new PointF(midX, top + 3.5f), new PointF(midX + 3.5f, top) });
    }

    /// <summary>Menu: three even bars.</summary>
    private static void DrawMenuIcon(Graphics g, Rectangle b, Color color) => DrawOnIconGrid(g, b, grid =>
    {
        using var pen = IconPen(color);

        foreach (var y in new[] { 6f, 12f, 18f })
        {
            grid.DrawLine(pen, 4f, y, 20f, y);
        }
    });

    /// <summary>
    /// Speaker with two sound waves, or with an X beside it when muted -
    /// reflects current state, so this can't be a static method like the
    /// other icons. Drawn in the theme's danger colour while muted rather
    /// than the ordinary icon colour, so a glance at the footer (not just a
    /// hover) shows whether the sound is off - the row has no room for a
    /// caption that says so in words.
    /// </summary>
    private void DrawMuteIcon(Graphics g, Rectangle b, Color color)
    {
        var muted = _announcer.Muted;
        var effective = muted ? AppTheme.Danger : color;

        DrawOnIconGrid(g, b, grid =>
        {
            using var pen = IconPen(effective);
            using var brush = new SolidBrush(effective);

            // The speaker: a box with a cone opening to the right. Filled,
            // and stroked with the same round join so its corners soften.
            var speaker = new[]
            {
                new PointF(3f, 9f),
                new PointF(7f, 9f),
                new PointF(11f, 5f),
                new PointF(11f, 19f),
                new PointF(7f, 15f),
                new PointF(3f, 15f),
            };
            grid.FillPolygon(brush, speaker);
            grid.DrawPolygon(pen, speaker);

            if (muted)
            {
                grid.DrawLine(pen, 16f, 9f, 22f, 15f);
                grid.DrawLine(pen, 22f, 9f, 16f, 15f);
            }
            else
            {
                // A near wave and a far one, both centred on the cone.
                grid.DrawArc(pen, 7f, 7f, 10f, 10f, -37f, 74f);
                grid.DrawArc(pen, 4f, 3f, 18f, 18f, -45f, 90f);
            }
        });
    }

    /// <summary>
    /// Keeps the mute button's icon and tooltip matching the current mute
    /// state. Unlike a caption drawn at paint time, a tooltip's text is
    /// only ever what SetToolTip last gave it, so this has to be called
    /// wherever the mute state can change, not just left to repaint.
    /// </summary>
    private void UpdateMuteButtonText()
    {
        _footerTooltip.SetToolTip(btnMuteToggle, _announcer.Muted ? "Unmute Announcer" : "Mute Announcer");
        btnMuteToggle.Invalidate();
    }

    /// <summary>Which beds the MUTE button itself silenced, so unmuting brings back exactly those and nothing it didn't stop.</summary>
    private bool _ambienceStoppedByMute;
    private bool _musicPausedByMute;

    /// <summary>
    /// The sidebar mute button means SILENCE, not "no announcer over the
    /// crowd loop": voice, sound effects and lap beeps stop through the
    /// announcer's own Muted flag, and the ambience bed and any music -
    /// separate services the flag never touched - stop here with it.
    /// Unmuting restores only what the mute stopped: a bed that was not
    /// running does not start because the button was pressed twice.
    /// </summary>
    private void SetAllSoundMuted(bool muted)
    {
        // Read before the flag changes. Muting when already muted (the
        // Mixer page can ask too) must not replace the record of what the
        // FIRST mute stopped with "nothing" - that is what the second look
        // at the beds would find, and the unmute would then bring nothing back.
        var wasMuted = _announcer.Muted;

        _announcer.Muted = muted;
        _settings.AnnouncerMuted = muted;
        _settings.Save();

        if (muted)
        {
            // Fresh record for a new mute; added to for a repeat one, so a
            // bed started since from its own tab is silenced as well, and
            // comes back with the rest at the unmute.
            var ambienceRunning = _ambience.IsRunning;
            var musicPlaying = _music.IsPlaying && !_music.IsPaused;

            _ambienceStoppedByMute = (wasMuted && _ambienceStoppedByMute) || ambienceRunning;
            _musicPausedByMute = (wasMuted && _musicPausedByMute) || musicPlaying;

            if (ambienceRunning)
            {
                _ambience.Stop();
            }

            if (musicPlaying)
            {
                _music.Pause();
            }
        }
        else
        {
            if (_ambienceStoppedByMute)
            {
                _ambience.Start(ignoreEnabledSetting: true);
            }

            if (_musicPausedByMute)
            {
                _music.Resume();
            }

            _ambienceStoppedByMute = false;
            _musicPausedByMute = false;
        }

        UpdateMuteButtonText();
        OnClientRawMessage($"{AppTime.LogStampNow()}  [voice] {(muted
            ? "Muted - voice, effects, beeps, ambience and music."
            : "Unmuted.")}");
    }

    /// <summary>One entry in the sidebar nav: a display name and the Panel showing its content. "Connection" can't be hidden, but it is no longer pinned to the top.</summary>
    private sealed record NavItem(string Name, Panel Page);

    private List<NavItem> _navItems = new();

    /// <summary>
    /// Whether the sidebar is currently BUILT for race night - not whether it
    /// ought to be.
    ///
    /// The two are deliberately separate. OrderedNavNames sorts by this and
    /// SidebarNavList asks for each row's heading through it, and those two
    /// answers have to agree or the list draws a "TONIGHT" heading above rows
    /// that are not in that group. A race day can open from several places -
    /// the Today checklist, the banner on Who's Racing, the Day tab, a switch
    /// of current day - and any of them could land between a repaint and a
    /// rebuild. One flag, changed only by SyncRaceNightNav, immediately before
    /// the rebuild that acts on it.
    /// </summary>
    private bool _navRaceNight;

    /// <summary>
    /// Whether the sidebar OUGHT to be in race-night order: the club wants it,
    /// and there is a real race day open to be in the middle of.
    ///
    /// The legacy holder does not count. It is where everything that pre-dates
    /// race days is filed and it is never closed, so treating it as an open
    /// day would put the sidebar in race-night order permanently, for
    /// everybody, including a club that has never started a day at all.
    /// </summary>
    private bool RaceNightWanted =>
        _settings.RaceNightFocus && _raceEvents.Active is { IsLegacy: false, IsOpen: true };

    /// <summary>
    /// Rebuilds the sidebar when a race day has opened or closed, and does
    /// nothing at all otherwise.
    ///
    /// Guarded because this is called from the event that fires whenever the
    /// current race day changes, and RebuildNav re-selects and re-shows a page.
    /// Doing that on every switch between days - when the grouping is the same
    /// either way - would be work nobody asked for in the middle of an
    /// evening.
    /// </summary>
    private void SyncRaceNightNav()
    {
        var wanted = RaceNightWanted;

        if (wanted == _navRaceNight)
        {
            return;
        }

        _navRaceNight = wanted;
        RebuildNav();
    }

    private readonly Dictionary<string, Action> _navRefreshHooks = new();

    /// <summary>
    /// The page actually on screen, whichever way it got there. Not always
    /// the sidebar's selection: Race Mode hides the sidebar, and a page the
    /// user has hidden from the sidebar can still be brought up by a
    /// button. The flag keys go by this, so they follow what is showing.
    /// </summary>
    private string? _shownPage;

    private void PopulateTabs()
    {
        var raceControl = new RaceControlControl(_race, _announcer, _profiles, _raceClasses, _trackLayouts, _sessionHistory, _settings, _undo, _daySchedule, _sponsors, _rounds, () => _activeRound, _commentary, _transponderWatch, _raceEvents, _checkInStore, _seasonStore, techInspections: _techInspections) { Dock = DockStyle.Fill };
        _raceControlControl = raceControl;
        raceControl.StatusMessage += OnClientRawMessage;

        // A finished race changes race-program state, so refresh the visible
        // page rather than leaving a heat showing "Scheduled" until the user
        // happens to navigate away and back.
        raceControl.SessionSaved += () =>
        {
            RefreshActiveRoundCombo();
            RefreshCurrentPage();
        };
        // Session setup moved here from the connection page: it's what you
        // set immediately before starting a race, so it belongs beside the
        // race controls rather than next to the decoder settings.
        //
        // IN A HOST PANEL, so it sits on the same two edges as everything
        // below it.
        //
        // It used to be docked straight to the page, which put it flush
        // against both sides. Everything under it - the Next Up bar, the
        // Race Control box, the board, the clock column - is placed by Race
        // Control itself, PageLeft in from the left and PageRight in from a
        // right edge measured INSIDE its own scrollbar. So the Session box
        // stuck out sixteen pixels past the lot on the left, and on the
        // right ran on over the column the scrollbar occupies underneath
        // it: two boxes stacked one over the other, sharing neither edge.
        //
        // Docking ignores a control's Margin, and only the parent's Padding
        // can inset a docked child - hence the host, whose Padding carries
        // the page's margins plus however wide Race Control's scrollbar is
        // at the moment. See KeepSessionOnPageRails.
        grpSession.Dock = DockStyle.Top;

        var sessionHost = new Panel { Dock = DockStyle.Top };
        sessionHost.Controls.Add(grpSession);

        void KeepSessionOnPageRails()
        {
            // Width minus ClientSize is the vertical scrollbar when it is
            // showing and nothing when it is not - the same allowance Race
            // Control makes for it when it places its own right edge.
            var scrollbar = Math.Max(0, raceControl.Width - raceControl.ClientSize.Width);

            var wanted = new Padding(
                UiLayout.PageLeft,
                8,
                UiLayout.PageRight + scrollbar,
                0);

            if (sessionHost.Padding != wanted)
            {
                sessionHost.Padding = wanted;
            }

            // Follows the group, which grows a second row when the round
            // picker cannot fit beside the track and shrinks again when the
            // Name box is hidden. See LayoutSessionRow.
            var height = wanted.Top + grpSession.Height + wanted.Bottom;

            if (sessionHost.Height != height)
            {
                sessionHost.Height = height;
            }
        }

        grpSession.SizeChanged += (_, _) => KeepSessionOnPageRails();

        // ClientSizeChanged, not just Resize: the scrollbar coming and going
        // changes the client area without changing the control's size.
        raceControl.ClientSizeChanged += (_, _) => KeepSessionOnPageRails();
        raceControl.Resize += (_, _) => KeepSessionOnPageRails();

        // As tall as its one row, not the 168px card it used to be - the
        // difference goes straight to the leaderboard below.
        grpSession.Height = UiLayout.InsideGroup(grpSession) + UiLayout.ButtonHeight + 12;

        // Track Layout and Session Label are no longer SHOWN here - Race
        // Control has the visible pair - but session saving still reads
        // these fields, so they're mirrored from the ones on screen.
        // Without this a saved session would record a blank track.
        // Session Setup is the single place these are set; Race Control's
        // copies are hidden and follow along. The flow used to be the other
        // way round, which is why both were visible and either could be
        // changed independently.
        cmbTrackLayout.SelectedIndexChanged += (_, _) =>
        {
            if (cmbTrackLayout.SelectedItem is string name)
            {
                raceControl.SetTrackLayout(name);
            }
        };

        txtSessionLabel.TextChanged += (_, _) => raceControl.SetSessionLabel(txtSessionLabel.Text);

        // Fill first, top second: docking resolves from the last child added
        // backwards, so the host claims its strip before Race Control fills
        // what is left.
        pageRaceControl.Controls.Add(raceControl);
        pageRaceControl.Controls.Add(sessionHost);

        KeepSessionOnPageRails();

        // The stores the driver card reads its numbers from - results,
        // championships, rivalries, personal-best resets and the
        // race-day paperwork. Every one of them already lives here.
        var driverStatsSources = new DriverStatsSources
        {
            History = _sessionHistory,
            Seasons = _seasonStore,
            Rounds = _rounds,
            Events = _raceEvents,
            Rivalries = _rivalryStore,
            PersonalBestResets = _pbResets,
            Handicaps = _handicaps,
            TechInspections = _techInspections,
            CheckIns = _checkInStore,
            SetupLog = _setupLog,
        };

        var driverProfiles = new ProfileManagerControl(_profiles, _raceClasses, _undo, driverStatsSources, _settings) { Dock = DockStyle.Fill };
        pageDriverProfiles.Controls.Add(driverProfiles);
        _navRefreshHooks["Driver Profiles"] = driverProfiles.RefreshAll;

        // Next to Driver Profiles in the sidebar: the two together are "who
        // races and under what rules", and they are the two pages somebody
        // sets up before a first race day.
        ConfigurePage(pageClasses);

        var classes = new RaceClassesControl(_raceClasses, _undo) { Dock = DockStyle.Fill };
        pageClasses.Controls.Add(classes);

        // Refreshed on entry, not only at construction: a class can be
        // edited from the dialog on Driver Profiles while this page sits in
        // the background.
        _navRefreshHooks["Classes"] = classes.RefreshAll;

        // --- Race Day and Championship ---
        //
        // Six nav items became two pages, and the split is by time scale:
        // Championship is the season (the series, its calendar, its
        // standings), Race Day is TODAY (the event, who is here, the
        // program, the run sheet). The two are linked underneath - an
        // event belongs to a championship, and everything on Race Day is
        // filed against the current event - but on race day nobody should
        // be navigating a season to find the check-in sheet.
        //
        // Race Day's tab order is the order the day runs in, left to
        // right: settle the event, get people through the door, build the
        // program, then run the sheet. The tabs ARE the flow.
        ConfigurePage(pageRaceDay);
        ConfigurePage(pageChampionship);

        // Plain English, and the day's own order.
        //
        // The five nouns these used to be - Event, Entries, Program, Run
        // Sheet, Tech Log - were one per store rather than one per job, and
        // a club that does not already know the app's vocabulary could not
        // tell from them where anything was. "Entries" was the worst of
        // them: it had no way to enter anyone.
        //
        // Today leads, because it is the only tab that says what to do
        // next. Everything behind it is the detail drawer for one part of
        // the day.
        var subPageToday = new TabPage("Next Steps");
        var subPageEvent = new TabPage("Setup");
        var subPageEntries = new TabPage("Who's Racing");

        // There is no "Heats & Mains" tab any more. It held a next-step card
        // that said, word for word, what Today's card says - both ask
        // ProgramNextStep - and a programme grid that Today had no way to
        // show. The grid has moved under Today's card, where it belongs, and
        // the tab has gone. See the Today section further down.
        var subPageStandings = new TabPage("Standings");
        var subPageCalendar = new TabPage("Calendar");

        var raceProgram = new RaceProgramControl(_raceClasses, _profiles, _rounds, _trackLayouts, _sessionHistory, _checkInStore, _settings, _undo, _raceEvents, _daySchedule) { Dock = DockStyle.Fill };
        _raceProgram = raceProgram;
        // "Use for Next Session" is gone from the Program tab, and with it
        // the second way to put a round on track. There is one now -
        // RoundStartRequested, below - because two paths to the same
        // outcome is exactly what made that page hard to read.
        // Parented further down, into the lower half of Today. Nothing owns
        // it here any more.
        raceProgram.StatusMessage += OnClientRawMessage;

        // The heat out on track, so undoing a build or a seeding does not
        // delete it from under the race. Anything but Idle counts - a
        // finished race is still waiting for its result to be saved.
        raceProgram.RoundOnTrackId = () => _race.State == RaceState.Idle ? null : _activeRound?.Id;

        raceProgram.RoundStartRequested += round =>
        {
            // Checked before the round is set up or its grid call spoken -
            // practice is on the same session, and starting over it would
            // wipe the practice laps.
            if (_raceControlControl.RefuseIfPracticeRunning("start a race"))
            {
                return;
            }

            // Asked here, before the round picker moves or the grid call is
            // spoken: asking at Stage instead would have announced a heat
            // that then does not run, and left the pickers on it. True
            // when nothing is running. The answer is passed on below so
            // Stage does not ask a second time.
            if (!_raceControlControl.ConfirmAbandonRunningRace($"start \"{round.RoundLabel}\""))
            {
                return;
            }

            StartRoundNow(round, confirmedOverRunningRace: true, showRaceControl: true,
                $"[program] Starting \"{round.RoundLabel}\" from the race program.");
        };

        // The Next Up bar's Start Next - the same path again, minus the
        // page switch, because the person pressing it is already there.
        raceControl.NextRoundStartRequested += round =>
        {
            if (_raceControlControl.RefuseIfPracticeRunning("start a race"))
            {
                return;
            }

            // Asked before the round moves or its grid call is spoken, as
            // for the programme above - and the answer passed on so Stage
            // does not ask again.
            if (!_raceControlControl.ConfirmAbandonRunningRace($"start \"{round.RoundLabel}\""))
            {
                return;
            }

            StartRoundNow(round, confirmedOverRunningRace: true, showRaceControl: false,
                $"[day] Starting \"{round.RoundLabel}\" from the Next Up bar.");
        };

        // Built once and kept, even though it lives in a drawer now.
        //
        // Not a fresh control per opening: the undo entries this page
        // captures call RefreshAll on the instance that made them, and a new
        // one each time would leave those holding a page that had been
        // thrown away - the same fault the scrutineering log was fixed for.
        // It is parented into the drawer's window on the way in and taken
        // back out in FormClosed, or that window would dispose it.
        var daySchedule = new DayScheduleControl(_daySchedule, _raceClasses, _announcer, _undo, _rounds, _raceEvents) { Dock = DockStyle.Fill };
        _daySchedulePage = daySchedule;

        // Who's Racing IS the merged roster: present, tech and inspect
        // per row. The Tech Log tab keeps the scrutineer's full record
        // list; the gate itself is one sheet.
        var checkIn = new CheckInControl(_profiles, _checkInStore, _raceClasses, _settings, _undo, _techInspections, _webServer, _raceEvents) { Dock = DockStyle.Fill };
        _checkInControl = checkIn;

        // The banner this page shows when check-ins have nowhere to go.
        checkIn.StartRaceDayRequested += StartRaceDayFromBanner;

        // The desk's changes are timed, so a slower phone tap made before
        // them cannot undo them - see ApplyRelayCheckIn.
        checkIn.CheckInChangedAtDesk += deskTransponder => _checkInChangedUtc[deskTransponder] = DateTime.UtcNow;

        // Online sign-ups: the button that opens them says how many are
        // waiting, and so does the sidebar, so a sign-up is noticed without
        // anyone having to go looking.
        checkIn.SignupsRequested += ShowSignups;
        _signupDesk.Changed += UpdateSignupCounts;
        subPageEntries.Controls.Add(checkIn);
        _subTabRefresh[subPageEntries] = checkIn.RefreshAll;

        ConfigurePage(pageRentals);
        ConfigurePage(pageHandicaps);
        ConfigurePage(pageBatteries);

        var rentals = new RentalTransponderControl(_rentals, _profiles, _undo) { Dock = DockStyle.Fill };
        pageRentals.Controls.Add(rentals);
        _navRefreshHooks["Rentals"] = rentals.RefreshAll;

        // Keep the sidebar count in step with the page, so outstanding units
        // are visible from wherever you happen to be working.
        rentals.OutstandingCountChanged += count => lstNav.SetBadge("Rentals", count);

        // Set once up front as well - otherwise a unit left out overnight
        // wouldn't show a badge until someone happened to open the page.
        lstNav.SetBadge("Rentals", _rentals.OutNow.Count);

        // Scrutineering is no longer a tab. What it held was the
        // scrutineer's record list, which a scrutineer opens a few times an
        // evening and nobody else opens at all; the gate itself has always
        // been the Tech column on Who's Racing. So it is a drawer off that
        // page now - "Scrutineering Log..." - and Race Day is three tabs.
        //
        // A fresh page per opening, and the one currently open is held in
        // techLog so the two places that have to redraw it can find it.
        //
        // It was one long-lived control moved from window to window, which is a
        // worse idea than it looks in WinForms: a themed scrollbar parks itself
        // as a SIBLING of the control it scrolls, so it belongs to the window,
        // and closing that window disposed a bar the page was still
        // subscribed to. The page's next ParentChanged then added a disposed
        // control to the next window, and showing it threw ObjectDisposedException
        // out of the message pump. ThinScrollBar refuses that now - a disposed
        // bar no longer re-parents, and a dead entry no longer blocks a new one -
        // but a page that never outlives its window cannot get into that state
        // in the first place.
        TechInspectionControl? techLog = null;

        void ShowScrutineeringLog()
        {
            using var host = new Form
            {
                Text = "Scrutineering log",
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                ShowIcon = false,
                ClientSize = new Size(1040, 660),
                MinimumSize = new Size(720, 480),
            };

            var log = new TechInspectionControl(_techInspections, _profiles, _raceClasses, _undo)
            {
                Dock = DockStyle.Fill,
            };

            var logFooter = new Panel { Dock = DockStyle.Bottom, Height = UiLayout.ButtonHeight + 24 };

            var btnCloseLog = new Button
            {
                Text = "Close",
                Size = new Size(UiLayout.ButtonWidth, UiLayout.ButtonHeight),
                Location = new Point(12, 12),
            };

            btnCloseLog.Click += (_, _) => host.Close();
            logFooter.Controls.Add(btnCloseLog);

            // Fill added first and docked last: WinForms resolves docking from
            // the end of the collection backwards.
            host.Controls.Add(log);
            host.Controls.Add(logFooter);

            host.AcceptButton = btnCloseLog;
            host.CancelButton = btnCloseLog;

            AppTheme.ApplyRecursively(host);

            techLog = log;

            // Cleared on the way out, so nothing refreshes a page that has
            // gone. Both readers below are null-conditional for the ordinary
            // case: the log is shut, and there is nothing to redraw.
            host.FormClosed += (_, _) => techLog = null;

            host.ShowDialog(this);
        }

        checkIn.TechLogRequested += ShowScrutineeringLog;

        // Jumping from Check-In to record or edit a tech inspection without
        // leaving the check-in table - the whole point of surfacing tech
        // status there instead of merging the two records together.
        checkIn.InspectRequested += profile =>
        {
            var latest = _techInspections.Today(profile.Transponder, profile.ClassName);

            // Only today's inspection is opened for editing. A race day can
            // span a weekend, and the inspection that counts on Sunday
            // morning can be Saturday's - editing that one overwrote
            // Saturday's record with Sunday's look at the car, instead of
            // filing a new one for Sunday. The Tech Log still edits any
            // record picked from its list. "Today" also takes in the last
            // few hours, so a club night running past midnight still edits
            // the inspection made before twelve rather than starting over.
            var existing = latest is not null
                           && (latest.InspectedAtUtc.ToLocalTime().Date == DateTime.Now.Date
                               || DateTime.UtcNow - latest.InspectedAtUtc < TimeSpan.FromHours(6))
                ? latest
                : null;

            using var form = existing is not null
                ? new TechInspectionEditForm(existing, _profiles, _raceClasses)
                : new TechInspectionEditForm(null, _profiles, _raceClasses, profile.Transponder);

            if (form.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            // Just this one record, not the whole log: undoing it must not
            // also throw away inspections recorded since.
            var before = existing is null ? null : _techInspections.CopyOf(existing.Id);
            var resultId = form.Result.Id;
            _undo.Capture($"{(existing is null ? "Record" : "Edit")} tech inspection for {profile.DriverName}", () =>
            {
                // Said out loud when it has been deleted since, rather than
                // logging "Undid" over a step that put nothing back.
                var putBack = _techInspections.UndoUpsert(resultId, before);
                techLog?.RefreshAll();
                checkIn.RefreshAll();

                if (!putBack)
                {
                    throw new InvalidOperationException("that inspection has been deleted since, so there is nothing to put back.");
                }
            });

            _techInspections.Upsert(form.Result);

            // Both pages show tech state, so both need to pick up the change
            // immediately rather than whichever one you happen to revisit -
            // the log only when it is actually open behind this dialog.
            techLog?.RefreshAll();
            checkIn.RefreshAll();
        };

        var handicaps = new HandicapControl(_handicaps, _profiles, _raceClasses, _sessionHistory, _undo) { Dock = DockStyle.Fill };
        pageHandicaps.Controls.Add(handicaps);
        _navRefreshHooks["Handicaps"] = handicaps.RefreshAll;

        var eventsPage = new EventsControl(_raceEvents, _rounds, _sessionHistory, _undo, _seasonStore, _trackLayouts) { Dock = DockStyle.Fill };
        subPageCalendar.Controls.Add(eventsPage);
        _subTabRefresh[subPageCalendar] = eventsPage.RefreshAll;

        var batteries = new BatteryPackControl(_batteryPacks, _sessionHistory, _profiles, _undo) { Dock = DockStyle.Fill };
        pageBatteries.Controls.Add(batteries);
        _navRefreshHooks["Batteries"] = batteries.RefreshAll;

        var trackLayouts = new TrackLayoutManagerControl(_trackLayouts, _settings, _undo) { Dock = DockStyle.Fill };
        pageTrackLayouts.Controls.Add(trackLayouts);
        _navRefreshHooks["Track Layouts"] = trackLayouts.RefreshAll;

        var raceHistory = new RaceHistoryControl(_sessionHistory, _undo, _settings, _rounds, _raceClasses, _profiles, _trackLayouts, _techInspections, _rentals, _handicaps, _raceEvents) { Dock = DockStyle.Fill };
        pageRaceHistory.Controls.Add(raceHistory);
        _navRefreshHooks["Race History"] = raceHistory.RefreshAll;

        var achievements = new AchievementsControl(_profiles) { Dock = DockStyle.Fill };
        pageAchievements.Controls.Add(achievements);

        // Refreshed on entry, not just at construction: Race Control awards
        // badges while this page sits in the background, so a page built
        // once would show yesterday's picture forever.
        _navRefreshHooks["Achievements"] = achievements.RefreshAll;

        // The Garage, in the page Tuning used to have. Tuning logged a
        // setup against a driver's name and a typed car string; the Garage
        // is that with the car made real, and the setup log is the lower
        // half of it - so there is one page about a car rather than two
        // that half-know about each other.
        var garage = new GarageControl(
            _cars, _chassisTypes, _profiles, _raceClasses, _setupLog, _sessionHistory,
            _trackLayouts, _chassisTemplates, _undo, _raceEvents) { Dock = DockStyle.Fill };

        pageSetupLog.Controls.Add(garage);
        _navRefreshHooks["Garage"] = garage.RefreshAll;

        // --- Race Analytics ---
        //
        // Four separate nav items became one page with tabs, the same shape
        // as Sounds. They were always one job - "what does the history say"
        // - split four ways down the sidebar, and a question like "is he
        // quicker than me, and is he improving?" meant two round trips
        // through the nav to answer.
        _tabAnalyticsSubNav = new TabControl { Dock = DockStyle.Fill };

        // First tab: the one nobody has to be told to look at. Every other
        // tab answers a question you arrived with; this one answers the
        // question on the wall of every club - what is the record here, and
        // who holds it.
        var subPageOverview = new TabPage("Overview");
        var overview = new AnalyticsOverviewControl(_sessionHistory);
        subPageOverview.Controls.Add(overview);

        var subPagePersonalBests = new TabPage("Personal Bests");
        var personalBests = new PersonalBestsControl(_sessionHistory, _pbResets, _undo) { Dock = DockStyle.Fill };
        subPagePersonalBests.Controls.Add(personalBests);

        var subPageLapTimes = new TabPage("Lap Times");
        var lapChart = new LapChartControl(_sessionHistory, _settings) { Dock = DockStyle.Fill };
        subPageLapTimes.Controls.Add(lapChart);

        var subPageHeadToHead = new TabPage("Head to Head");
        var headToHead = new HeadToHeadControl(_sessionHistory) { Dock = DockStyle.Fill };
        subPageHeadToHead.Controls.Add(headToHead);

        var subPageTrends = new TabPage("Trends");
        var trend = new TrendChartControl(_sessionHistory, _settings) { Dock = DockStyle.Fill };
        subPageTrends.Controls.Add(trend);

        // The same table Race Control opens after a race, with a picker on
        // it. Reading pace is not only a thing you do in the ten minutes
        // after a heat - "why was I slower last week" is the question this
        // answers best, and it needed a way in that was not "find the race
        // in History and open it".
        var subPagePace = new TabPage("Pace");
        var pace = new PaceAnalysisControl(_sessionHistory);
        subPagePace.Controls.Add(pace);

        _tabAnalyticsSubNav.TabPages.Add(subPageOverview);
        _tabAnalyticsSubNav.TabPages.Add(subPagePersonalBests);
        _tabAnalyticsSubNav.TabPages.Add(subPageLapTimes);
        _tabAnalyticsSubNav.TabPages.Add(subPageHeadToHead);
        _tabAnalyticsSubNav.TabPages.Add(subPageTrends);
        _tabAnalyticsSubNav.TabPages.Add(subPagePace);
        AppTheme.StyleTabControl(_tabAnalyticsSubNav);

        // Every one of these reads the session history, and races finish
        // while this page is in the background - so the tab being opened is
        // what refreshes it, not the page being built once at startup.
        void RefreshAnalyticsTab()
        {
            switch (_tabAnalyticsSubNav.SelectedTab)
            {
                case { } tab when tab == subPageOverview:
                    overview.RefreshAll();
                    break;
                case { } tab when tab == subPagePersonalBests:
                    personalBests.RefreshAll();
                    break;
                case { } tab when tab == subPageLapTimes:
                    lapChart.RefreshAll();
                    break;
                case { } tab when tab == subPageHeadToHead:
                    headToHead.RefreshAll();
                    break;
                case { } tab when tab == subPageTrends:
                    trend.RefreshAll();
                    break;
                case { } tab when tab == subPagePace:
                    pace.RefreshAll();
                    break;
            }
        }

        _tabAnalyticsSubNav.SelectedIndexChanged += (_, _) => RefreshAnalyticsTab();

        pageAnalytics.Controls.Add(_tabAnalyticsSubNav);

        // Opening the page refreshes whichever tab is showing, so arriving
        // from the sidebar is as current as switching tabs once here.
        _navRefreshHooks["Race Analytics"] = RefreshAnalyticsTab;

        var seasons = new SeasonsControl(_seasonStore, _sessionHistory, _undo, _rounds, _raceClasses, _raceEvents, _profiles) { Dock = DockStyle.Fill };
        subPageStandings.Controls.Add(seasons);
        _subTabRefresh[subPageStandings] = seasons.RefreshAll;

        // --- Assemble the two pages, now that every tab has its control ---
        var eventTab = new RaceDayEventControl(
            _raceEvents, _venueStore, _trackLayouts, _seasonStore, _checkInStore, _rounds, _daySchedule, _raceClasses, _undo,
            _sessionHistory, _settings);

        subPageEvent.Controls.Add(eventTab);
        _subTabRefresh[subPageEvent] = eventTab.RefreshAll;

        // The setup wizard, opened from The Day. Built here rather than
        // there because it drives three pages' commands, not one - and
        // every one of them is handed in, so the wizard never grows its
        // own version of starting a day or building a programme. See
        // RaceDaySetupHost.
        // Opened at a step: 0 is the welcome page, and the Ready to Race bar
        // on Next Steps asks for the first step that is not done yet.
        _openRaceDaySetup = startStep =>
        {
            using var wizard = new RaceDaySetupWizard(
                _settings, _raceClasses, _profiles, _trackLayouts, _venueStore, _raceEvents, _checkInStore,
                new RaceDaySetupHost(
                    StartDay: eventTab.StartNewDayFromWizard,
                    ChangeDay: eventTab.ChangeDayFromElsewhere,
                    SetClassesRacing: eventTab.SetRacingClassesFromElsewhere,
                    PlanBuild: () => _raceProgram.PlanQualifyingBuild(),
                    Build: () => _raceProgram.BuildQualifying(string.Empty)),
                startStep);

            wizard.ShowDialog(this);

            // Everything the wizard did went through the stores, so the
            // pages re-read rather than being told what changed.
            eventTab.RefreshAll();
            _raceProgram.RefreshAll();
            _todayControl?.RefreshAll();
            RefreshCurrentPage();

            if (wizard.GoToPageOnClose is { } page)
            {
                GoToPage(page);
            }
        };

        eventTab.SetUpTonightRequested += () => _openRaceDaySetup?.Invoke(0);

        // Starting a race day from anywhere else - the Today checklist, the
        // banner on Who's Racing - goes through the Day tab's own command,
        // so there is one implementation with one undo entry rather than
        // three that drift.
        //
        // "Start a Race Day" - the next-step card on Next Steps, the banner on
        // Who's Racing - opens the Set Up Tonight wizard now, not the
        // two-question dialog. The wizard's day step starts the day through
        // this same tab's command (RaceDaySetupHost.StartDay), so there is
        // still one implementation; the wizard just walks on from there to
        // the classes, check-in and the programme instead of leaving them to
        // be found. Opened at the first step not done yet - the step the
        // Ready to Race bar's own button would open.
        //
        // The Calendar's New Race Day below still uses the dialog: from
        // there somebody is filing a day, not setting up tonight.
        _startRaceDay = () =>
        {
            var step = _todayControl?.FirstSetupStep() ?? RaceDaySetupWizard.StepDay;
            _openRaceDaySetup?.Invoke(step);
        };

        // The Calendar's New Race Day is that same command - one dialog,
        // one undo entry, one set of defaults carried forward - but it
        // stays on the Calendar afterwards rather than marching the window
        // to The Day. The day arrives with its shape, its classes and the
        // last day's venue and format already on it, so there is nothing
        // waiting to be typed.
        eventsPage.StartRaceDayRequested += () =>
        {
            if (!eventTab.StartNewDayFromElsewhere())
            {
                return;
            }

            eventsPage.RefreshAll();
            RefreshCurrentPage();
        };

        var tabRaceDay = new TabControl { Dock = DockStyle.Fill };
        tabRaceDay.TabPages.Add(subPageToday);
        tabRaceDay.TabPages.Add(subPageEvent);
        tabRaceDay.TabPages.Add(subPageEntries);
        AppTheme.StyleTabControl(tabRaceDay);

        var tabChampionship = new TabControl { Dock = DockStyle.Fill };
        tabChampionship.TabPages.Add(subPageStandings);
        tabChampionship.TabPages.Add(subPageCalendar);
        AppTheme.StyleTabControl(tabChampionship);

        void RefreshSubTab(TabControl tabs)
        {
            if (tabs.SelectedTab is { } tab && _subTabRefresh.TryGetValue(tab, out var refresh))
            {
                refresh();
            }
        }

        // Held so anything outside this method can put the page on a
        // particular tab - see _startRaceDay.
        _goToDayTab = () =>
        {
            GoToPage("Race Day");
            tabRaceDay.SelectedTab = subPageEvent;
        };

        tabRaceDay.SelectedIndexChanged += (_, _) => RefreshSubTab(tabRaceDay);
        tabChampionship.SelectedIndexChanged += (_, _) => RefreshSubTab(tabChampionship);

        // The Program tab's banner sends the director somewhere else when
        // the day is not set up yet - there is no class list to build a
        // programme from, so saying "go and make one" and taking them
        // there beats a button that explains why it cannot work.
        //
        // Wired HERE rather than where raceProgram is built, because these
        // need the tab control, and that does not exist until the tabs
        // have all been made.
        raceProgram.GoToEventTab += () => tabRaceDay.SelectedTab = subPageEvent;
        raceProgram.ManageDriversRequested += () => GoToPage("Driver Profiles");

        // The two empty states the banner could not tell apart before, and
        // so could not send anywhere useful - see ProgramNextStep.
        raceProgram.GoToEntriesTab += () => tabRaceDay.SelectedTab = subPageEntries;
        raceProgram.SetUpClassesRequested += () => GoToPage("Classes");
        raceProgram.StartRaceDayRequested += StartRaceDayFromBanner;

        // Race Control asks the programme what is owed before the next
        // race, rather than working "next" out on its own - which is how
        // its Next Up bar came to offer Start Next on a main whose grid
        // was about to change. One authority, three screens.
        // Filling in the times writes a time against every line AND the
        // day's first-race time, so the two pages that read those - Today's
        // Due column, and The Day's field - are stale until they re-read.
        daySchedule.TimesFilled += () =>
        {
            _todayControl?.RefreshAll();
            _raceProgram.RefreshAll();
            RefreshCurrentPage();
        };

        // Move Up on a round's line moves the ROUND. There is one running
        // order and the programme owns it; the timetable redraws afterwards
        // because it reads that order, not because it was told to.
        daySchedule.MoveRoundRequested += (roundId, direction) => raceProgram.MoveRound(roundId, direction);

        // The timetable follows the programme instead of waiting to be
        // rebuilt by hand.
        //
        // Done HERE rather than on the page, because the page is a drawer
        // now and is shut for most of an evening. Hung off the control, the
        // sheet would only catch up at the moment somebody opened it - and
        // the printed run sheet, the countdown and the drift would all have
        // been read off a stale order until then. It is a data operation;
        // it belongs with the data.
        raceProgram.ProgrammeChanged += SyncTimetableToProgramme;

        // And Today, because the programme grid is the lower half of that tab
        // now. Building or seeding from one of the grid's own buttons changes
        // what the card above it should be saying, and before this the card
        // sat on the stale answer until the tab was left and come back to.
        raceProgram.ProgrammeChanged += () => _todayControl?.RefreshAll();

        raceControl.NextStepProvider = () => raceProgram.NextStep();
        raceControl.RunProgramStepRequested += step => raceProgram.RunStep(step);

        // --- Today ---
        //
        // Built last of the sub-tabs because it reads the programme tab
        // rather than the stores: the one next step, and the command that
        // runs it, stay in one place that way.
        var today = new TodayControl(
            raceProgram,
            _raceEvents,
            _profiles,
            _raceClasses,
            _checkInStore,
            _daySchedule,
            // Either decoder, because which one is live depends on the
            // venue's saved connection type and only one of them is ever
            // out of Disconnected at a time.
            () => _decoderClient.State == DecoderConnectionState.Connected
                  || _ilapClient.State == DecoderConnectionState.Connected) { Dock = DockStyle.Fill };

        _todayControl = today;

        // The Ready to Race bar's one button: the Set Up Tonight wizard, at
        // the first step still to do.
        today.SetUpRequested += step => _openRaceDaySetup?.Invoke(step);

        // --- The timetable drawer ---
        //
        // The sheet is a few-times-an-evening job: set the times, add doors
        // and lunch, print it, arm the countdown. That is a drawer, the same
        // as the scrutineering log - and since the running order started
        // deriving from the programme, a tab for it was a third copy of a
        // list already on two screens.
        //
        // The one instance is moved into this window and taken back out on
        // the way, never rebuilt: its undo entries call RefreshAll on the
        // instance that captured them.
        Form? timetableHost = null;

        void ShowTimetable()
        {
            // Already open - bring it forward rather than stacking a second.
            if (timetableHost is { IsDisposed: false } already)
            {
                already.Activate();
                return;
            }

            using var host = new Form
            {
                Text = "Timetable",
                StartPosition = FormStartPosition.CenterParent,
                MinimizeBox = false,
                ShowIcon = false,
                ClientSize = new Size(1040, 700),
                MinimumSize = new Size(760, 520),
            };

            var footer = new Panel { Dock = DockStyle.Bottom, Height = UiLayout.ButtonHeight + 24 };

            var btnClose = new Button
            {
                Text = "Close",
                Size = new Size(UiLayout.ButtonWidth, UiLayout.ButtonHeight),
                Location = new Point(12, 12),
            };

            btnClose.Click += (_, _) => host.Close();
            footer.Controls.Add(btnClose);

            // Fill added first and docked last: WinForms resolves docking
            // from the end of the collection backwards.
            host.Controls.Add(daySchedule);
            host.Controls.Add(footer);

            host.AcceptButton = btnClose;
            host.CancelButton = btnClose;

            AppTheme.ApplyRecursively(host);

            timetableHost = host;

            // Out of the window before it is disposed, or the page goes with
            // it and every undo entry it captured is left holding a control
            // that has been thrown away.
            host.FormClosed += (_, _) =>
            {
                host.Controls.Remove(daySchedule);
                timetableHost = null;
            };

            daySchedule.RefreshAll();
            host.ShowDialog(this);
        }

        // On the programme's footer now, with the other day tools.
        raceProgram.TimetableRequested += ShowTimetable;

        // "There is no programme yet - go and build one." Was a tab switch;
        // from inside a drawer it has to shut the drawer first, or the
        // window the director is being sent to is behind the one they are
        // looking at. Wired here rather than beside the page's other events
        // because it is the drawer's window it closes.
        daySchedule.GoToProgramTab += () =>
        {
            timetableHost?.Close();
            tabRaceDay.SelectedTab = subPageToday;
        };

        today.ConnectTimerRequested += () => GoToPage("Connection");

        // --- Today, with the programme under it ---
        //
        // This is the fold of the old Heats & Mains tab. That page had two
        // halves: a next-step card that printed the same sentence Today's
        // card prints, because both ask ProgramNextStep, and a grid of the
        // rounds with the commands that act on one. The card was duplication;
        // the grid was the only place in the app that showed the running
        // order and let somebody start a round from it. So the card comes off
        // and the grid moves here, under the card that was already saying
        // what to do next.
        //
        // A splitter rather than a fixed stack, because which half matters
        // changes through the evening. Before the first heat the checklist is
        // the page and there is no programme to look at; by the third round
        // the checklist is six ticks and the grid is the page. Rather than
        // guess, the line is the director's to drag, and where they leave it
        // is remembered.
        raceProgram.UseAsProgrammePane();

        // How much of the page each half wants to open at. Measured rather
        // than written down, because what "enough to be useful" costs in
        // pixels depends entirely on the text-size setting: the summary needs
        // its heading, the card and one row of tiles; the programme needs its
        // two-row footer, the grid's header and a few rounds under it.
        //
        // These are NOT Panel1MinSize and Panel2MinSize, and that is the whole
        // point of this comment. Assigning those properties validates
        // immediately against the size the control happens to be at the time,
        // and a SplitContainer that has not been laid out yet is 150x100 - so
        // setting a 228px Panel2MinSize in the initializer threw
        // "SplitterDistance must be between Panel1MinSize and Width -
        // Panel2MinSize" out of the constructor, before the window ever
        // appeared. They are the opening position's bounds instead, applied
        // once there is room for them, and the panels keep WinForms' own 25px
        // minimums - which are small enough to survive any window size, and
        // still stop either half being dragged away to nothing.
        var todayTopMin = AppTheme.TitleFont.Height + AppTheme.BodyFont.Height
                          + AppTheme.HeadingFont.Height + UiLayout.ButtonHeight
                          + UiLayout.SectionGap * 2;

        var todayBottomMin = UiLayout.ButtonHeight * 2 + AppTheme.SmallFont.Height * 2
                             + AppTheme.MicroFont.Height + AppTheme.BodyFont.Height * 3
                             + UiLayout.RowGap * 5;

        var todaySplit = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,

            // Panel1 keeps its height when the window is resized and the grid
            // takes the slack. That is the right way round - a taller window
            // does not want a taller checklist - and it also means resizing
            // never moves the splitter, so nothing overwrites the saved
            // position behind the director's back.
            FixedPanel = FixedPanel.Panel1,

            SplitterWidth = 6,
        };

        // The bar between the two halves. The SplitContainer itself is not a
        // Panel, so the recursive theming pass never reaches its BackColor -
        // left alone it draws the system grey, which is a bright line across
        // the page in either dark theme.
        todaySplit.BackColor = AppTheme.Border;

        // Kept out of the layout repair pass, halves and all. Both halves lay
        // themselves out - Next Steps measures every band, the programme's
        // header and footer are already left alone - and a SplitContainer is
        // a scrollable control: caught mid-resize with a half hanging past
        // its edge, the pass turned scrolling on for the WHOLE split, which
        // put a second pair of scrollbars round the entire tab.
        LayoutDoctor.LeaveAlone(todaySplit);

        todaySplit.Panel1.Controls.Add(today);
        todaySplit.Panel2.Controls.Add(raceProgram);

        // Placed on the first resize that has room for it, not here: a
        // SplitterDistance assigned before the control has a height is either
        // ignored or throws, depending on how far out it is.
        var todaySplitPlaced = false;

        void PlaceTodaySplitter()
        {
            if (todaySplitPlaced)
            {
                return;
            }

            var room = todaySplit.Height - todaySplit.SplitterWidth;

            // Not enough room to open at a useful size: leave WinForms to it
            // and try again on the next resize. Nothing is assigned until the
            // arithmetic below is known to be in range.
            if (room < todayTopMin + todayBottomMin)
            {
                return;
            }

            // 55% the first time, measured rather than written down: the
            // summary's real height depends on the text-size setting, and any
            // pixel count decided here would be right at one setting only.
            var saved = _settings.TodayProgrammeSplit;
            var wanted = saved > 0 ? saved : room * 55 / 100;

            todaySplit.SplitterDistance = Math.Clamp(wanted, todayTopMin, room - todayBottomMin);

            // Set last, so the assignment above - which raises SplitterMoved -
            // does not save the default back over a position the director
            // chose in an earlier session.
            todaySplitPlaced = true;

            FitTodayToContent();
        }

        // The top half is always tall enough to show all of Next Steps -
        // the card, Ready to Race and the tiles - without scrolling. It
        // only ever GROWS the half, so a director who drags the line down to
        // give the programme less room keeps that; dragging it up past the
        // content is what is undone, the next time the content changes or
        // the window is resized. Still leaves the programme its minimum.
        void FitTodayToContent()
        {
            if (!todaySplitPlaced || todaySplit.IsDisposed)
            {
                return;
            }

            var room = todaySplit.Height - todaySplit.SplitterWidth;
            var most = room - todayBottomMin;

            if (most < todayTopMin)
            {
                return;
            }

            var wanted = Math.Clamp(today.ContentHeight, todayTopMin, most);

            if (todaySplit.SplitterDistance < wanted)
            {
                todaySplit.SplitterDistance = wanted;
            }
        }

        today.ContentHeightChanged += FitTodayToContent;

        todaySplit.Resize += (_, _) =>
        {
            PlaceTodaySplitter();
            FitTodayToContent();
        };

        // And when the page is first shown, in case the sidebar page it lives
        // on was never laid out at a real height while it was hidden. Both
        // hooks are harmless once the splitter has been placed - the method
        // returns immediately after that.
        todaySplit.VisibleChanged += (_, _) => PlaceTodaySplitter();

        todaySplit.SplitterMoved += (_, _) =>
        {
            if (!todaySplitPlaced)
            {
                return;
            }

            _settings.TodayProgrammeSplit = todaySplit.SplitterDistance;
            _settings.Save();
        };

        subPageToday.Controls.Add(todaySplit);

        // Both halves, because they are one tab now. Today reads the
        // programme control for its next step, so the programme is refreshed
        // first - otherwise the card above the grid is a refresh behind it.
        _subTabRefresh[subPageToday] = () =>
        {
            raceProgram.RefreshAll();
            today.RefreshAll();
        };

        pageRaceDay.Controls.Add(tabRaceDay);
        pageChampionship.Controls.Add(tabChampionship);

        _navRefreshHooks["Race Day"] = () => RefreshSubTab(tabRaceDay);
        _navRefreshHooks["Championship"] = () => RefreshSubTab(tabChampionship);

        // Switching the current event switches what every scoped store
        // answers, so whatever page is showing re-reads. Wired once, here,
        // rather than each page having to know events exist.
        _raceEvents.CurrentChanged += (_, _) =>
        {
            if (!IsDisposed)
            {
                RefreshCurrentPage();

                // Today names the day in its heading and counts against
                // it, so it re-reads whether or not it is the tab being
                // looked at - switching the day while standing on Race
                // Control should not leave yesterday's name waiting on
                // Race Day.
                _todayControl?.RefreshAll();

                // The Track box follows the day that just opened, rather
                // than staying on whatever was used last. See
                // SyncTrackLayoutToRaceDay for what that was costing.
                SyncTrackLayoutToRaceDay();

                // And the sidebar, if this was a day opening or the last one
                // closing. A no-op when the grouping is unchanged.
                SyncRaceNightNav();
            }
        };

        var pageGhosts = new Panel();
        ConfigurePage(pageGhosts);
        var ghostControl = new GhostControl(_ghostStore, _profiles, _sessionHistory, _undo) { Dock = DockStyle.Fill };
        pageGhosts.Controls.Add(ghostControl);

        var practice = new PracticeModeControl(_race, _profiles, _sessionHistory, _trackLayouts, _settings, _ghostStore, _announcer, _daySchedule, _sponsors, undo: _undo) { Dock = DockStyle.Fill };
        // Ghost profiles kept in step as ghosts are added, edited and
        // deleted - so a new ghost is immediately pickable everywhere.
        _ghostStore.Saved += _ => OnGhostStoreSaved();

        // Run once at startup as well, so ghosts saved before this existed
        // get their profiles without needing to be edited first.
        EnsureGhostProfiles();

        raceControl.DeepDuckRequested += deep =>
        {
            if (!_settings.SummaryDeepDuck)
            {
                return;
            }

            var fraction = deep
                ? Math.Clamp(_settings.SummaryDuckPercent, 2, 100) / 100f
                : (float?)null;

            _ambience.SetDuckDepthOverride(fraction);
            _music.SetDuckDepthOverride(fraction);
        };

        raceControl.SessionAudioFinished += () =>
        {
            if (InvokeRequired)
            {
                TryPost(HandleSessionAudioFinished);
                return;
            }

            HandleSessionAudioFinished();
        };

        practice.GhostsPageRequested += () => GoToPage("Ghost Cars");

        // Practice writes its own lap and time targets into the shared
        // session while it runs and hands the old ones back when it stops.
        // Race Control's format line is re-read then, so it shows what the
        // next race will actually use rather than what was there before.
        practice.PracticeStopped += _raceControlControl.RefreshFormat;
        _practiceControl = practice;
        pagePractice.Controls.Add(practice);
        _navRefreshHooks["Practice"] = practice.RefreshAll;

        var demoMode = new DemoModeControl(_race, _announcer, _profiles, _trackLayouts, _settings, _rounds, _raceEvents, _ghostStore) { Dock = DockStyle.Fill };
        _demoControl = demoMode;

        // Race Control ends a running demo through this before it stages,
        // starts or resets over it, so the simulator lets go of the session
        // and its queued lap calls instead of carrying on inside the real race.
        _raceControlControl.StopRunningDemo = demoMode.StopForTakeover;
        pageDemoMode.Controls.Add(demoMode);
        _navRefreshHooks["Demo Mode"] = demoMode.RefreshAvailableData;

        // Wired here rather than beside RoundStartRequested above, because
        // demoMode doesn't exist yet at that point in this method.
        raceProgram.RoundDemoRequested += round =>
        {
            // A demo drives the same session practice is running on, and
            // would wipe the practice laps just as a real start would.
            if (_raceControlControl.RefuseIfPracticeRunning("run a demo"))
            {
                return;
            }

            // Same for a race being started or run: the demo page refuses
            // it too, but only after the pickers below had already been
            // moved off the round the race is using. Checked here first,
            // with the demo page's own words. A demo already running is
            // not refused - the simulator stops and restarts it.
            if (!_race.IsDemoMode && _race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing)
            {
                AppDialog.Show(this, "A race is running. Starting a demo would wipe it. Finish it or reset it on Race Control first.",
                    "Can't start a demo", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // A demo must never consume a real round. With a round selected
            // in the session row, the demo's FAKE results would save against
            // it, mark it recorded, and the Next Up bar would skip it as
            // done - a heat nobody actually ran, gone from the day. Dropped
            // to freeform BEFORE the simulator stages, so the saved session
            // carries no round id and the real heat stays runnable.
            cmbActiveRound.SelectedIndex = 0;

            // Refresh first: the driver list has to know about this round's
            // drivers before StartDemoForRound can tick them.
            demoMode.RefreshAvailableData();
            demoMode.StartDemoForRound(round);

            // Race Control rather than the Demo Mode page - a demo drives
            // the same session, leaderboard and announcer as a real race,
            // so that's where someone actually wants to be watching.
            GoToPage("Race Control");

            OnClientRawMessage($"{AppTime.LogStampNow()}  [program] Running \"{round.RoundLabel}\" as a demo.");
        };

        // Voice Announcements hosts a small internal sub-nav: general
        // voice settings, and Rivalries (custom lines for specific driver
        // matchups) - grouped together since rivalries are fundamentally
        // a voice-announcement feature, not a standalone concern.
        // Everything to do with the app SPEAKING lives here: the voice
        // itself, the AI that writes lines for it, the live announcer view,
        // and per-driver rivalry lines. Announcer and Commentary were each
        // a top-level nav item, which scattered one concern across three
        // places in the sidebar.
        _tabVoiceSubNav = new TabControl { Dock = DockStyle.Fill };
        var subPageVoiceSettings = new TabPage("Voice Settings");

        // Second, right after the voice that speaks them. The thirty
        // announcement rows used to be the last group at the bottom of Voice
        // Settings - below the engine, the connection, the tuning and the
        // line library - so the longest thing on the page was also the
        // furthest below the fold.
        var subPageAnnouncements = new TabPage("Announcements");
        var subPageCommentary = new TabPage("Commentary");
        var subPageRivalries = new TabPage("Rivalries");
        _tabVoiceSubNav.TabPages.Add(subPageVoiceSettings);
        _tabVoiceSubNav.TabPages.Add(subPageAnnouncements);
        _tabVoiceSubNav.TabPages.Add(subPageCommentary);
        _tabVoiceSubNav.TabPages.Add(subPageRivalries);
        AppTheme.StyleTabControl(_tabVoiceSubNav);

        var rivalries = new RivalriesControl(_rivalryStore, _profiles, _undo)
        {
            Dock = DockStyle.Fill,

            // Rivalry lines are spoken by the announcer, so the HeatCaller
            // voice fixes them as it fixes the ones on Voice Settings.
            WordingIsFixed = () => _voiceStore.Current.EffectiveEngine == VoiceEngine.HeatCaller,
        };
        subPageRivalries.Controls.Add(rivalries);

        // Built once and kept for the life of the app, but NOT parented
        // anywhere yet: it lives in a window of its own now, opened from
        // Race Control. Built here all the same, because the talking-point
        // feed it gathers has no store behind it - a control built when the
        // window opens would start every race with an empty page and lose
        // the lot each time the window was closed.
        _announcerControl = new AnnouncerControl(_race, _profiles, _sessionHistory, _daySchedule, _announcer, _commentary, _settings, _undo) { Dock = DockStyle.Fill };
        var announcer = _announcerControl;

        // One click each way between the only two screens anybody is on
        // while a race is running.
        //
        // Wired here rather than where Race Control is built, because this
        // is the first point at which BOTH exist. Neither control knows
        // what pages there are or how to reach one - they raise an event
        // and this decides what that means, which is the only place that
        // knows.
        // Quick Race drops the pickers back to a plain freeform Race first,
        // so the session neither runs under some selected round's format
        // nor records itself against that round. Round first: selecting
        // freeform is what re-enables the session-type combo.
        _raceControlControl.QuickRaceSetupRequested += () => cmbActiveRound.SelectedIndex = 0;

        // The format follows the round: pick a different round and the
        // chip - and the session limits, if nothing is running - follow.
        cmbActiveRound.SelectedIndexChanged += (_, _) => _raceControlControl.RefreshFormat();

        _raceControlControl.AnnouncerRequested += OpenAnnouncerWindow;

        // Brings Race Control forward without closing the announcer's
        // window. It used to be a tab, so "Race Control" meant leaving the
        // announcer's page; now the two can be side by side and this only
        // has to say which one has the keyboard.
        announcer.RaceControlRequested += () =>
        {
            GoToPage("Race Control");

            if (WindowState == FormWindowState.Minimized)
            {
                WindowState = FormWindowState.Normal;
            }

            Activate();
        };

        var commentary = new CommentaryControl(_settings, _commentary) { Dock = DockStyle.Fill };
        subPageCommentary.Controls.Add(commentary);

        // The model client's "[llm] ..." lines, which Race Control's "any
        // [llm] line above says why" points at. Stamped here, as the client
        // sends them bare; OnClientRawMessage is safe from any thread.
        commentary.ClientStatusMessage += msg => OnClientRawMessage($"{AppTime.LogStampNow()}  {msg}");

        // Rivalries reads the driver list, which changes while the app runs,
        // so it refreshes on tab entry rather than only at construction.
        // The announcer used to be refreshed here too; it is a window now
        // and OpenAnnouncerWindow refreshes it on the way in.
        _tabVoiceSubNav.SelectedIndexChanged += (_, _) =>
        {
            if (_tabVoiceSubNav.SelectedTab == subPageRivalries)
            {
                rivalries.RefreshDriverCombos();
            }
        };

        var voiceSettings = new VoiceSettingsControl(_voiceStore, _settings, _undo) { Dock = DockStyle.Fill, Announcer = _announcer, HeatCallerVoice = _heatCallerVoice };
        voiceSettings.StatusMessage += OnClientRawMessage;
        subPageVoiceSettings.Controls.Add(voiceSettings);
        _voiceSettingsControl = voiceSettings;

        // The announcement rows, built by the same control so they stay part
        // of the one voice profile it saves, but parented here so they get a
        // tab of their own. See VoiceSettingsControl.Announcements.cs.
        subPageAnnouncements.Controls.Add(voiceSettings.AnnouncementsPage);
        _voiceAnnouncementsSubPage = subPageAnnouncements;

        pageVoice.Controls.Add(_tabVoiceSubNav);

        _navRefreshHooks["Voice"] = () => rivalries.RefreshDriverCombos();

        // Sound Effects hosts the audio sub-nav: discrete effects, the
        // continuous ambience beds, and per-driver lap cues. Grouped because
        // all three are "what the app sounds like", and Lap Beeps never
        // justified a top-level nav slot of its own.
        _tabSoundSubNav = new TabControl { Dock = DockStyle.Fill };

        var subPageEffects = new TabPage("Effects");
        var soundEffects = new SoundEffectsControl(_soundStore, _undo) { Dock = DockStyle.Fill };
        subPageEffects.Controls.Add(soundEffects);
        _soundEffectsControl = soundEffects;

        var subPageAmbience = new TabPage("Ambience");
        var ambienceControl = new AmbienceControl(_ambienceStore, _ambience, _undo) { Dock = DockStyle.Fill };
        _ambienceControl = ambienceControl;
        subPageAmbience.Controls.Add(ambienceControl);

        var subPageMusic = new TabPage("Music");
        var musicControl = new MusicControl(_musicStore, _music, _undo) { Dock = DockStyle.Fill };
        _musicControl = musicControl;
        subPageMusic.Controls.Add(musicControl);

        // First tab: the blend matters more often than any individual
        // setting, and it's the page someone opens mid-event.
        var subPageMixer = new TabPage("Mixer");
        var mixerControl = new MixerControl(
            _voiceStore, _soundStore, _ambienceStore, _ambience, _musicStore, _music, _announcer, _settings, _undo)
        { Dock = DockStyle.Fill };

        // The Mixer's mute goes through the sidebar's, so it is saved and
        // the sidebar's unmute knows what it stopped - see SetAllSoundMuted.
        mixerControl.SetAllSoundMutedRequested = SetAllSoundMuted;
        subPageMixer.Controls.Add(mixerControl);

        var subPageLapBeeps = new TabPage("Lap Beeps");
        var lapBeeps = new LapBeepControl(_lapBeeps, _profiles, _soundStore, _undo) { Dock = DockStyle.Fill };
        subPageLapBeeps.Controls.Add(lapBeeps);

        // Soundscapes sits right after the Mixer: it sets up everything the
        // tabs after it hold - effects, voice, ambience, music - in one go.
        // It lived on Themes as "Announcer Style" until 0.56.
        var subPageSoundscapes = new TabPage("Soundscapes");
        var themes = new ThemesControl(_themeStore, _voiceStore, _soundStore, _undo, _ambienceStore, _musicStore) { Dock = DockStyle.Fill, Announcer = _announcer };
        themes.ThemeApplied += AfterSoundscapeApplied;

        // Editing or deleting the applied soundscape changes what Theme music
        // plays (or leaves it none): the player's copy is refreshed here, and
        // the next queue it builds - a track ending, or Play - uses it.
        themes.SoundscapesChanged += () =>
        {
            _music.Theme = _themeStore.ActiveThemeMusic();
            _musicControl?.RefreshAll();
        };
        subPageSoundscapes.Controls.Add(themes);

        _tabSoundSubNav.TabPages.Add(subPageMixer);
        _tabSoundSubNav.TabPages.Add(subPageSoundscapes);
        _tabSoundSubNav.TabPages.Add(subPageEffects);
        _tabSoundSubNav.TabPages.Add(subPageAmbience);
        _tabSoundSubNav.TabPages.Add(subPageMusic);
        _tabSoundSubNav.TabPages.Add(subPageLapBeeps);
        AppTheme.StyleTabControl(_tabSoundSubNav);

        // Lap Beeps lists driver profiles, so it needs refreshing when the
        // tab is opened rather than only at construction.
        _tabSoundSubNav.SelectedIndexChanged += (_, _) =>
        {
            if (_tabSoundSubNav.SelectedTab == subPageLapBeeps)
            {
                lapBeeps.RefreshAll();
            }
            else if (_tabSoundSubNav.SelectedTab == subPageMixer)
            {
                // Levels may have been changed on the other tabs since this
                // was last shown - re-read rather than showing stale faders.
                mixerControl.RefreshAll();
            }
        };

        pageSound.Controls.Add(_tabSoundSubNav);
        _soundEffectsSubPage = subPageEffects;

        // Ambience is wired to race state so the crowd follows the action.
        WireAmbienceToRace();

        // Themes is the Looks page itself - the colours the app and the crowd
        // screens wear, and the club logo - with no tabs. It held two tabs
        // until 0.56, when Announcer Style (now Soundscapes) moved to Sounds.
        _looksControl = new LooksControl(_settings, _undo, ApplyHeaderAnimationSetting, RefreshHeaderLogo, RestartForNewLook) { Dock = DockStyle.Fill };
        pageThemes.Controls.Add(_looksControl);

        // Looks shows what settings and the looks file hold, and those can
        // change while the page is out of sight - an undo, most often - so it
        // re-reads them each time it comes into view.
        _navRefreshHooks["Themes"] = () => _looksControl.RefreshAll();

        var sponsors = new SponsorsControl(_sponsors, _undo) { Dock = DockStyle.Fill };
        pageSponsors.Controls.Add(sponsors);

        var webServer = new WebServerControl(
            _webServer,
            _settings,
            () => _raceControlControl.TriggerOpenBigScreen(),
            _relayPublisher,
            () => GoToPage("Club Website"),
            _undo)
        {
            Dock = DockStyle.Fill,
        };

        pageWebServer.Controls.Add(webServer);

        // Not from the control's constructor, which runs inside this one:
        // the server would be answering phones before there was a window
        // to act on what they asked. Shown handlers run in the order they
        // were added, so the one that marks the host ready has run first.
        Shown += (_, _) => webServer.AutoStartIfConfigured();

        // Next to Local Screens in the sidebar: the two together answer "how
        // does somebody who is not stood at this laptop see the race" -
        // one on the venue's wifi, one from anywhere.
        ConfigurePage(pageOnline);

        var online = new RelaySettingsControl(_relayPublisher, _settings, _profiles, _undo) { Dock = DockStyle.Fill };
        pageOnline.Controls.Add(online);

        // The club pages' colours and logo live on Themes → Looks now,
        // shared with the app and the other screens; the Club Website page links
        // there rather than keeping a second copy of the same settings.
        online.OpenLooksRequested += ShowLooks;

        // Same reasoning: the Manager PIN lives on Local Screens, shared by
        // the Wi-Fi and relay manager pages alike, so the Club Website page's
        // "Set Manager PIN..." goes there rather than keeping a second box.
        online.OpenPhoneWebRequested += () => GoToPage("Local Screens");

        // The HeatCaller voice speaks through this same relay unless Voice
        // Settings points it elsewhere: a new address, club or token here is
        // a fresh question for it, not a reason to keep resting on the old
        // relay's answer.
        online.VoiceRelayChanged += () => _announcer.ResetHeatCallerVoiceState("the relay on the Club Website page was changed");

        // A new set made, or personal PINs switched on or off: straight to
        // the relay, and emailed when the club has asked for that, rather
        // than waiting up to ten minutes for the next pass.
        online.RacerPinsChanged += () =>
        {
            if (!RunRacerPinsPass())
            {
                RepublishCheckInNow();
            }
        };

        // Reissue All Badges made a new badge key, so every driver's online
        // id is different now: the ids the desk scanner and relay check-ins
        // are matched against are worked out again at once, and the check-in
        // list - which carries them - goes to the relay now rather than on
        // the next two-second pass.
        online.BadgesReissued += () =>
        {
            RebuildBadgeIds();
            RepublishCheckInNow();
        };

        // What the page did, in the console, like every other page's.
        online.StatusMessage += OnClientRawMessage;

        // Refreshed on entry: the boxes have to show what settings hold,
        // not what they held when the page was built.
        _navRefreshHooks["Club Website"] = online.RefreshAll;

        var venues = new VenuesControl(_venueStore, _trackLayouts, _themeStore, _undo) { Dock = DockStyle.Fill };
        venues.VenueApplied += venue =>
        {
            // Everything below is taken first, so Undo can put the decoder,
            // track, soundscape and race defaults back as they were. The
            // combo boxes are this form's own and live as long as it does,
            // which is why the undo may touch them (see "Reset round").
            var decoderBefore = cmbConnectionType.SelectedIndex;
            var trackBefore = cmbTrackLayout.SelectedItem as string;
            var lapLimitBefore = _settings.RaceLapLimit;
            var timeLimitBefore = _settings.RaceTimeLimitMinutes;
            var minLapBefore = _settings.RaceMinLapTimeMs;
            var streakBefore = _settings.RaceStreakWindowPercent;
            var venueTheme = string.IsNullOrWhiteSpace(venue.PreferredThemeName) ? null : _themeStore.FindByName(venue.PreferredThemeName);
            var putBackSoundscape = venueTheme is null ? null : ThemeStore.CaptureForUndo(venueTheme, _voiceStore, _soundStore, _ambienceStore);

            var venueUndoDescription = $"Apply venue '{venue.Name}'";
            Action? undoVenue = null;
            undoVenue = () =>
            {
                // Not while cars are on track: switching the decoder back
                // disconnects the one timing the race, and the track change
                // would re-file the race under another layout. The step is
                // put back on the stack before refusing, so it can still be
                // undone once the race is over.
                if (_race.IsPracticeMode || _race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing)
                {
                    _undo.Capture(venueUndoDescription, undoVenue!);
                    throw new InvalidOperationException("a race or practice is running - finish it first, then undo again.");
                }

                if (putBackSoundscape is not null)
                {
                    putBackSoundscape();
                    AfterSoundscapeApplied();
                }

                if (decoderBefore >= 0 && decoderBefore < cmbConnectionType.Items.Count && cmbConnectionType.SelectedIndex != decoderBefore)
                {
                    cmbConnectionType.SelectedIndex = decoderBefore;
                }

                if (trackBefore is not null && cmbTrackLayout.Items.Contains(trackBefore))
                {
                    cmbTrackLayout.SelectedItem = trackBefore;
                }

                // Back through the same door the venue used, so the race
                // format is worked out again. The window percent goes in as
                // the multiplier ApplyRaceDefaults converts it from.
                _raceControlControl.ApplyRaceDefaults(lapLimitBefore, timeLimitBefore, minLapBefore, 1.0 + streakBefore / 100.0);
            };
            _undo.Capture(venueUndoDescription, undoVenue);

            // The decoder, on the Connection page. The venue's host and port
            // were Next Level Timing's address and are no longer applied - a
            // venue saved for it gets the EasyLap decoder, and says so.
            if (!DecoderConnection.IsOffered(venue.ConnectionType))
            {
                var retired = venue.ConnectionType == DecoderConnection.RetiredNeonApi;
                var was = retired
                    ? "Next Level Timing, which is no longer offered"
                    : "a decoder this version does not know";

                OnClientRawMessage($"{AppTime.LogStampNow()}  [venue] \"{venue.Name}\" was set up for {was} - using the EasyLap decoder. Pick the right decoder for it on the Venues page.");

                // Saved as what it now does, so this is said once rather than
                // on every apply. Only the retired number: one from a newer
                // version is left as it is, in case that version comes back.
                if (retired)
                {
                    venue.ConnectionType = DecoderConnection.EasyLap;
                    _venueStore.Upsert(venue);
                }
            }

            cmbConnectionType.SelectedIndex = DecoderConnection.IndexOf(venue.ConnectionType);

            // Preferred track layout, if it still exists.
            if (!string.IsNullOrWhiteSpace(venue.PreferredTrackLayoutName) && cmbTrackLayout.Items.Contains(venue.PreferredTrackLayoutName))
            {
                cmbTrackLayout.SelectedItem = venue.PreferredTrackLayoutName;
            }

            // Preferred theme, if it still exists - applies voice+sound and rebuilds those pages to reflect it.
            if (!string.IsNullOrWhiteSpace(venue.PreferredThemeName))
            {
                var theme = venueTheme;
                if (theme is not null)
                {
                    ThemeStore.Apply(theme, _voiceStore, _soundStore, _ambienceStore, _musicStore);
                    _themeStore.MarkApplied(theme);
                    AfterSoundscapeApplied();
                }
            }

            // Race-control defaults.
            raceControl.ApplyRaceDefaults(venue.RaceLapLimit, venue.RaceTimeLimitMinutes, venue.RaceMinLapTimeMs, venue.RaceStreakSlowMultiplier);

            OnClientRawMessage($"{AppTime.LogStampNow()}  [venue] Applied venue \"{venue.Name}\".");
        };
        pageVenues.Controls.Add(venues);
        _navRefreshHooks["Venues"] = venues.RefreshDropdownSources;

        var backupRestore = new BackupRestoreControl(_settings, _undo) { Dock = DockStyle.Fill };

        // A restore restarts the app, which would end a race or practice
        // mid-session - the page asks first when this says one is on.
        backupRestore.IsRaceUnderWay = () => _race.IsPracticeMode || _race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing;
        backupRestore.RestoreCompleted += () =>
        {
            // Every store only reads from disk once, at startup - the only
            // way restored data actually takes effect is a fresh process.
            //
            // Frozen first: closing this process saves settings and stores
            // on its way out, and every one of those writes would put the
            // pre-restore data straight back over what was just restored.
            SafeFile.Freeze();
            Application.Restart();
            Environment.Exit(0);
        };

        // Windows will not replace a file that is open, so everything this
        // app holds open in the data folder is let go just before the
        // restore starts putting files back: the music track, the ambience
        // loops and the I-Lap raw capture log. Each on its own, so one that
        // fails does not leave the others open. Nothing is started again -
        // the app restarts after a restore.
        backupRestore.ReleaseFilesForRestore += () =>
        {
            foreach (var release in new Action[] { _music.Stop, _ambience.Stop, _ilapClient.Disconnect })
            {
                try
                {
                    release();
                }
                catch
                {
                    // A file still open is retried, then reported, by the restore.
                }
            }
        };
        pageBackup.Controls.Add(backupRestore);

        // The session row's Round picker can go stale while another page
        // adds or removes rounds, so it is refreshed every time Race Control
        // becomes the active page.
        //
        // This was keyed to "Connection", from when the session row lived on
        // the Connection page. It moved to Race Control and this did not
        // move with it, so the row was refreshed on a visit to a page it was
        // no longer on and never on a visit to the one it was. A track made
        // on Track Layouts, or a programme built on Race Day, did not appear
        // in it until the app restarted.
        //
        // The track picker is not refreshed here any more: it follows the
        // layout store directly (OnTrackLayoutsChanged), which catches a
        // change made while Race Control is already showing, and this could
        // not. Safe mid-race: the round list is rebuilt around the running
        // round, and RefreshFormat only writes the race limits while the
        // race is idle or finished.
        _navRefreshHooks["Race Control"] = RefreshActiveRoundCombo;

        _navItems = new List<NavItem>
        {
            new("Race Control", pageRaceControl),
            new("Race Day", pageRaceDay),
            new("Championship", pageChampionship),
            new("Driver Profiles", pageDriverProfiles),
            new("Classes", pageClasses),
            new("Rentals", pageRentals),
            new("Handicaps", pageHandicaps),
            new("Batteries", pageBatteries),
            new("Track Layouts", pageTrackLayouts),
            new("Race History", pageRaceHistory),
            new("Race Analytics", pageAnalytics),
            new("Achievements", pageAchievements),
            new("Garage", pageSetupLog),
            new("Practice", pagePractice),
            new("Ghost Cars", pageGhosts),
            new("Demo Mode", pageDemoMode),
            new("Venues", pageVenues),
            new("Themes", pageThemes),
            new("Sponsors", pageSponsors),
            new("Local Screens", pageWebServer),
            new("Club Website", pageOnline),
            new("Voice", pageVoice),
            new("Sounds", pageSound),
            new("Connection", pageLiveSession),
            new("Backup & Restore", pageBackup),
        };

        // Every page except the ones the designer already parented -
        // pageLiveSession (Connection) is added there. Testing the parent
        // rather than skipping index 0 means the list can be in any order.
        foreach (var item in _navItems)
        {
            if (item.Page.Parent is null)
            {
                pnlContent.Controls.Add(item.Page);
            }
        }

        // Every page gets the same top edge, from here - one place, and the
        // title is the nav's own name for the page, so the two can never
        // disagree. PageHeader reserves its strip with the page's Padding,
        // which the Dock=Fill child each page holds honours on its own; no
        // page's own layout code knows this exists.
        foreach (var item in _navItems)
        {
            PageHeader.Attach(item.Page, item.Name);
        }

        txtNavSearch.TextChanged += (_, _) => RebuildNav();
        // ~20fps is plenty for a slow-drifting grid, and keeps the cost
        // negligible on a laptop at a track.
        _headerAnimationTimer.Tick += (_, _) =>
        {
            AppTheme.AnimationPhase = (AppTheme.AnimationPhase + 0.004f) % 1f;
            lblAppTitle.Invalidate();
        };

        // The club logo, if there is one and it is wanted there - which
        // also starts the header animation, or leaves it off while a logo
        // is showing.
        RefreshHeaderLogo();

        btnManageNav.Click += BtnManageNav_Click;

        _announcer.Muted = _settings.AnnouncerMuted;

        // The icon and the tooltip both default to "not muted" until told
        // otherwise (SetUpFooterButton runs before this setting is known) -
        // this is what makes a club that starts up already muted show it.
        UpdateMuteButtonText();

        // Files an undoable action set aside (a deleted song, a replaced
        // logo) are kept for a week in case they're wanted back, then
        // cleared here. Off the UI thread since it walks folders on disk;
        // Prune never throws.
        _ = Task.Run(UndoHoldingFolder.Prune);

        _undo.UndoFailed += (description, ex) =>
            OnClientRawMessage($"{AppTime.LogStampNow()}  [undo] FAILED to undo '{description}': {ex.Message}");

        _undo.StateChanged += () =>
        {
            btnUndo.Enabled = _undo.CanUndo;

            // Full history on hover - the button itself only has room for
            // the next step.
            _footerTooltip.SetToolTip(btnUndo, _undo.CanUndo
                ? "Will undo, in order:\r\n" + string.Join("\r\n", _undo.PendingDescriptions.Select((d, i) => $"  {i + 1}. {d}"))
                    + (_undo.Count > 1 ? "\r\n\r\nRight-click to undo several at once." : string.Empty)
                : "Nothing to undo.");

            btnUndo.Invalidate();
        };
        btnUndo.Click += (_, _) =>
        {
            // A left-click on the little caret opens the list instead - see
            // UndoCaretBounds. Anything else (the rest of the button, or
            // Space/Enter with it focused) is the plain one-step undo.
            if (_undoCaretPressed)
            {
                _undoCaretPressed = false;
                ShowUndoMenu();
                return;
            }

            UndoSteps(1, string.Empty);
        };

        // Several steps at once: right-click the button (or click its
        // caret) for the list of what's pending, newest first. A disabled
        // button gets no mouse events at all, which is right - there is
        // nothing to list.
        btnUndo.MouseDown += (_, e) =>
            _undoCaretPressed = e.Button == MouseButtons.Left && _undo.Count > 1 && UndoCaretBounds(btnUndo).Contains(e.Location);
        // Pressed on the caret and then dragged off: no Click comes, and
        // the flag must not turn a later Space press into the list.
        btnUndo.MouseLeave += (_, _) => _undoCaretPressed = false;
        btnUndo.MouseUp += (_, e) =>
        {
            if (e.Button == MouseButtons.Right)
            {
                ShowUndoMenu();
            }
        };

        // The caret itself, drawn on top of the icon by a second Paint
        // handler wired here once (SetUpFooterButton re-runs on every theme
        // change, which is why it isn't done there). Only while there is
        // more than one step - a list of one is just the button.
        btnUndo.Paint += (_, e) =>
        {
            if (_undo.Count > 1)
            {
                DrawUndoCaret(e.Graphics, UndoCaretBounds(btnUndo), AppTheme.SidebarText);
            }
        };

        AppTheme.StyleToolTip(_footerTooltip);
        AppTheme.ApplyRecursively(this);
        RestyleSidebarChrome();
        ApplyFontScale();

        btnMuteToggle.Click += (_, _) => SetAllSoundMuted(!_announcer.Muted);

        lstNav.SelectionChanged += LstNav_SelectionChanged;
        lstNav.ItemsReordered += LstNav_ItemsReordered;

        // Essentials and club tools, each under a quiet heading - see NavTiers.
        // Asked per row on every repaint, so it has to read the same flag the
        // ordering above does - see _navRaceNight.
        lstNav.SectionOf = name => NavTiers.SectionOf(name, _navRaceNight);

        WireLayoutReporting();

        // Before the first build, not after: a club that closed the app
        // mid-race-day reopens it with that day still current, and the sidebar
        // should already be in race-night order rather than rearranging itself
        // a moment later.
        _navRaceNight = RaceNightWanted;

        RebuildNav();

        // After the window is actually up: the check needs real handles and
        // real font metrics, neither of which exist while it is still being
        // built.
        Shown += (_, _) => TidyAllPages();
    }


    /// <summary>
    /// Keywords that should surface a page even though they don't appear
    /// in its name - "where's the minimum lap time setting" shouldn't
    /// require already knowing it lives under Race Control.
    /// </summary>
    private static readonly Dictionary<string, string[]> NavSearchAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Race Control"] = new[] { "flag", "green", "yellow", "red", "checkered", "stage", "countdown", "penalty", "minimum lap", "min lap", "streak", "big screen", "clock", "timer", "race mode", "f11", "full screen", "fullscreen", "next up", "re-seed", "reseed",

            // The announcer's screen is opened from this page - it is
            // no longer a tab anywhere - so this is where searching for
            // it has to land.
            "announcer", "announcer screen", "spotlight", "mc", "microphone", "mic", "talking points" },
        ["Connection"] = new[] { "connect", "decoder", "easylap", "i-lap", "ilap", "com port", "serial", "baud", "auto-connect", "console", "quick race" },
        ["Driver Profiles"] = new[] { "transponder", "car", "name", "roster", "import", "export" },
        // Race Day swallowed four pages, so it answers for all four
        // pages' words - someone searching "check-in" is not wrong just
        // because it became a tab.
        ["Race Day"] = new[]
        {
            "heat", "qualifying", "main", "round", "grid", "print", "program",
            "reminder", "timeline", "running order", "on deck", "schedule",
            "qr", "badge", "scan", "present", "attendance", "check in", "check-in",
            "tech", "scrutineer", "scrutineering", "inspect", "inspection", "checklist",
            "event", "meeting", "weekend", "entries",
        },
        // "sanctioning" and the body names are in here deliberately: the
        // preset that encodes them lives two clicks inside this page, and
        // somebody looking for it searches for the thing, not for the page
        // that happens to hold it.
        // "relay", "cloud" and "publish" are all in here because nobody
        // calls it the same thing twice, and the page is named for what it
        // achieves rather than for the machinery behind it.
        ["Club Website"] = new[]
        {
            "online", "relay", "cloud", "publish", "publishing", "internet", "remote", "public", "url",
            "share", "token", "club id", "results page", "standings page", "spectator", "stream",
            "https", "hosted", "away", "off site", "offsite", "website", "site", "web site", "club website",
        },
        ["Classes"] = new[]
        {
            "class", "classes", "rules", "rulebook", "sanctioning", "preset", "roar", "ifmar", "brca",
            "pn racing", "pnwc", "qualifying", "consecutive", "best of", "main", "mains", "bump up",
            "bump-up", "drivers per main", "tech", "scrutineering", "checklist", "colour", "color",
            "race format", "jump start",
        },
        ["Rentals"] = new[] { "rental", "transponder", "loan", "loaner", "borrow", "deposit", "hire", "equipment" },
        ["Handicaps"] = new[] { "handicap", "hcp", "credit", "even", "mixed", "ability", "novice", "stagger" },
        ["Batteries"] = new[] { "battery", "batteries", "pack", "packs", "cell", "nimh", "lipo", "rotation", "charge" },
        ["Track Layouts"] = new[] { "record", "photo", "map", "average lap", "length" },
        ["Race History"] = new[] { "results", "past", "notes", "export", "csv", "delete session" },
        // Personal Bests, Lap Times, Head to Head and Trends used to be
        // keyed here under their own names - but they are tabs of Race
        // Analytics now, and a key that is not a sidebar name is never
        // looked up, so typing "pb" found nothing. Their words live in
        // NavSearchTabAliases below, filed under the page that owns them.
        ["Championship"] = new[]
        {
            "season", "standings", "points", "export", "csv", "series",
            "calendar", "regional", "drop", "title", "events",
        },
        ["Practice"] = new[] { "lapping", "rolling average", "tuning", "open" },
        ["Demo Mode"] = new[] { "simulate", "fake", "test race" },
        ["Venues"] = new[] { "location", "shop", "preset", "profile" },
        ["Themes"] = new[]
        {
            "look", "looks", "colour", "color", "theme", "dark mode", "light mode", "synthwave",
            "logo", "club logo", "appearance", "24-hour", "military time",
        },
        ["Sponsors"] = new[] { "logo", "advert", "slide", "rotation" },
        ["Local Screens"] = new[]
        {
            "remote", "obs", "overlay", "stream", "server", "url", "browser", "live results",
            "phone", "phones", "web", "wifi", "wi-fi", "local", "network", "screen", "screens",
        },
        // Absorbs the terms from the former Announcer and Commentary nav
        // items - those are tabs here now, and searching for them must
        // still land somewhere useful.
        ["Voice"] = new[]
        {
            "elevenlabs", "speech", "tts", "api key", "rivalries", "volume",
            "commentary", "commentator", "llm", "ai", "ollama", "colour", "color",
            "narration", "local model",

            // The old name of this page, so anyone who learned it still
            // finds it, plus what its Announcements tab is now called.
            "voice announcements", "announcements", "announcement", "lines", "phrasing", "wording",
        },
        ["Ghost Cars"] = new[] { "ghost", "virtual", "opponent", "rival", "solo", "practice alone", "ai car", "pace" },
        ["Sounds"] = new[] { "soundscape", "soundscapes", "preset", "voice pack", "sound pack", "announcer style", "personality", "sound effects", "music", "soundtrack", "track", "song", "playlist", "beep", "beeps", "cue", "chime", "per-driver", "ambience", "crowd", "atmosphere", "track noise", "tone", "beep", "audio", "volume", "test" },
        ["Backup & Restore"] = new[] { "save", "restore", "export", "zip", "auto backup" },
        ["Garage"] = new[] { "car", "cars", "chassis", "shell", "bodyshell", "tuning", "setup log", "setup", "tires", "gearing", "ride height", "springs", "loan", "borrow", "club car" },
    };

    /// <summary>
    /// Words for the tabs inside a page, keyed by the sidebar page that
    /// owns them. A match here does two things: the owning page stays in
    /// the filtered sidebar, and when that page is opened the matching tab
    /// is brought to the front - someone who typed "trends" wants the
    /// Trends chart, not whichever Race Analytics tab was open last.
    ///
    /// An ordered list rather than a dictionary per page, so when a word
    /// fits two tabs the first one listed - the left-most - wins, every
    /// time. The tab names must match the TabPage captions exactly; the
    /// caption itself always counts as a keyword.
    /// </summary>
    private static readonly Dictionary<string, (string Tab, string[] Words)[]> NavSearchTabAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        // The Tab name here is matched against TabPage.Text exactly, so
        // these move whenever a tab is renamed. The old names stay in the
        // word lists - somebody who learnt "entries" or "run sheet" should
        // still land on the right tab after they were renamed.
        ["Race Day"] = new[]
        {
            // Today absorbed the Heats & Mains tab, so its words land here
            // now. The old ones are kept: somebody who learnt "program" or
            // went looking for "mains" should still arrive at the grid,
            // wherever it has been moved to since.
            ("Next Steps", new[]
            {
                "next steps", "today", "next", "next step", "what next", "ready", "checklist", "overview",
                "heats", "mains", "heats and mains", "program", "programme", "qualifying",
                "main", "heat", "round", "grid", "bump", "bump up", "bump-up", "seed", "seeding",
                "build", "start", "running order", "standings",

                // The Timetable tab became a drawer off this page, so its
                // words land here now - searching "run sheet" should arrive
                // at the page with the button on it rather than at nothing.
                "timetable", "run sheet", "schedule", "reminder", "timeline", "on deck",
                "late", "running late", "drift", "times",
            }),
            ("Setup", new[] { "set up", "set up tonight", "details", "the day", "day", "event", "meeting", "weekend", "race day", "kind of night", "delete day" }),
            // Who's Racing absorbed the Scrutineering tab, so its words land
            // here. Kept, for the same reason as Today's: somebody who learnt
            // "tech log" should still arrive where the log now lives.
            ("Who's Racing", new[]
            {
                "who", "whos racing", "who's racing", "entries", "entry", "qr", "badge", "scan",
                "present", "attendance", "check in", "check-in",
                "scrutineering", "scrutineer", "tech", "tech log", "inspect", "inspection",
            }),
        },
        ["Championship"] = new[]
        {
            ("Standings", new[] { "standings", "points", "season", "drop" }),
            ("Calendar", new[] { "calendar", "race days", "race day", "all days", "events", "regional", "archive" }),
        },
        ["Race Analytics"] = new[]
        {
            ("Overview", new[] { "overview", "track record", "lap record" }),
            ("Personal Bests", new[] { "pb", "personal best", "best lap", "record" }),
            ("Lap Times", new[] { "lap times", "chart", "graph", "consistency", "std dev" }),
            ("Head to Head", new[] { "head to head", "compare", "versus", "vs", "rivalry" }),
            ("Trends", new[] { "trend", "progress", "improvement", "over time", "history chart" }),
            ("Pace", new[] { "pace" }),
        },
        ["Voice"] = new[]
        {
            ("Voice Settings", new[] { "voice", "elevenlabs", "speech", "tts", "api key" }),
            ("Announcements", new[] { "announcements", "announcement", "lines", "phrasing", "templates", "wording", "weight", "caution" }),
            ("Commentary", new[] { "commentary", "commentator", "llm", "ai", "ollama", "narration", "local model" }),
            ("Rivalries", new[] { "rivalries", "rivalry" }),
        },
        ["Sounds"] = new[]
        {
            ("Mixer", new[] { "mixer", "blend", "volume" }),
            ("Soundscapes", new[] { "soundscape", "soundscapes", "announcer style", "personality", "voice pack", "sound pack", "preset" }),
            ("Effects", new[] { "effects", "sound effects", "cue", "chime" }),
            ("Ambience", new[] { "ambience", "crowd", "atmosphere", "track noise" }),
            ("Music", new[] { "music", "soundtrack", "song", "playlist" }),
            ("Lap Beeps", new[] { "lap beeps", "beep", "per-driver", "tone" }),
        },
    };

    private static bool TabMatchesSearch((string Tab, string[] Words) tab, string query) =>
        tab.Tab.Contains(query, StringComparison.OrdinalIgnoreCase)
        || tab.Words.Any(w => w.Contains(query, StringComparison.OrdinalIgnoreCase));

    private static bool MatchesNavSearch(string navName, string query)
    {
        if (navName.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (NavSearchTabAliases.TryGetValue(navName, out var tabs) && tabs.Any(t => TabMatchesSearch(t, query)))
        {
            return true;
        }

        return NavSearchAliases.TryGetValue(navName, out var aliases)
            && aliases.Any(a => a.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// With a search typed, brings forward the tab of this page that the
    /// search was for - see NavSearchTabAliases.
    ///
    /// Three letters at least for a partial match: one or two match a tab
    /// on almost every page, and the tabs flicking about under each
    /// keystroke would be worse than not choosing one at all. Shorter than
    /// that, only a whole keyword counts - "pb", "vs", "mc" - since those
    /// are words somebody types on purpose. A search that matched the page
    /// but none of its tabs leaves the tab alone.
    /// </summary>
    private void SelectSearchedTab(string navName)
    {
        var query = txtNavSearch.Text.Trim();

        if (query.Length == 0 || !NavSearchTabAliases.TryGetValue(navName, out var tabs))
        {
            return;
        }

        var match = query.Length >= 3
            ? tabs.FirstOrDefault(t => TabMatchesSearch(t, query))
            : tabs.FirstOrDefault(t => t.Words.Any(w => string.Equals(w, query, StringComparison.OrdinalIgnoreCase)));

        if (match.Tab is null)
        {
            return;
        }

        // Every page with tabs holds its TabControl directly - see where
        // each page is assembled.
        var tabControl = _navItems.FirstOrDefault(i => i.Name == navName)?.Page.Controls.OfType<TabControl>().FirstOrDefault();
        var tabPage = tabControl?.TabPages.Cast<TabPage>().FirstOrDefault(p => p.Text == match.Tab);

        if (tabControl is not null && tabPage is not null && tabControl.SelectedTab != tabPage)
        {
            // Raises SelectedIndexChanged, which refreshes the tab.
            tabControl.SelectedTab = tabPage;
        }
    }

    /// <summary>Every nav item, hidden ones included, in the user's chosen order.</summary>
    private List<string> OrderedNavNames() => OrderedNavNames(_navRaceNight);

    /// <param name="raceNight">
    /// False asks for the CLUB'S OWN order - the arrangement they dragged
    /// into place - without the race-night grouping laid over it.
    ///
    /// Anything about to SAVE an order has to ask for that one. The
    /// grouping is a view: it lifts Race Day and Race Control to the top
    /// while a day is open and drops them back when it closes. Written to
    /// NavOrder it stops being a view and becomes the arrangement, and the
    /// two of them stay at the top for ever - which is what happened by
    /// opening Manage Navigation during a race night and pressing OK
    /// without touching anything.
    /// </param>
    private List<string> OrderedNavNames(bool raceNight)
    {
        var ordered = _navItems.Select(i => i.Name).ToList();
        if (_settings.NavOrder.Count > 0)
        {
            var known = new HashSet<string>(ordered);
            var reordered = _settings.NavOrder.Where(known.Contains).ToList();
            reordered.AddRange(ordered.Where(n => !reordered.Contains(n)));
            ordered = reordered;
        }

        // Kept in their groups - Tonight (only while a race day is open),
        // Essentials, then club tools - whatever order was saved: the club's
        // own order within each group is kept (the sort is stable), but a page
        // can't be dragged into another group.
        //
        // The secondary key only does anything inside the Tonight group, where
        // Race Day comes before Race Control because that is the order of the
        // evening. It is zero everywhere else, and a stable sort leaves those
        // rows exactly as they were.
        ordered = ordered
            .OrderBy(n => NavTiers.TierOf(n, raceNight))
            .ThenBy(n => NavTiers.TonightRank(n, raceNight))
            .ToList();

        return ordered;
    }

    /// <summary>Names of visible nav items in the user's chosen order - "Connection" can't be hidden but is no longer forced first.</summary>
    private List<string> VisibleOrderedNavNames() => VisibleOrderedNavNames(_navRaceNight);

    /// <summary>The same, in the club's own order when raceNight is false. See OrderedNavNames.</summary>
    private List<string> VisibleOrderedNavNames(bool raceNight)
    {
        var ordered = OrderedNavNames(raceNight);

        // Connection is no longer pinned to the top - it sits low in the
        // canonical order with the other setup pages - but it still can't
        // be hidden, so it survives the visibility filter regardless.
        return ordered.Where(n => n == "Connection" || !_settings.HiddenTabs.Contains(n)).ToList();
    }

    /// <summary>Rebuilds the sidebar list from the current order/visibility settings and shows whichever item was selected before (or Race Control by default).</summary>
    private void RebuildNav()
    {
        var previouslySelected = lstNav.SelectedItem;

        var names = VisibleOrderedNavNames();

        var query = txtNavSearch.Text.Trim();
        if (query.Length > 0)
        {
            var matched = names.Where(n => MatchesNavSearch(n, query)).ToList();
            // Never filter down to nothing - an empty sidebar looks broken
            // and leaves no way back, so a query with no hits just shows
            // everything rather than a blank panel.
            if (matched.Count > 0)
            {
                names = matched;
            }
        }

        lstNav.SetItems(names);

        var toSelect = previouslySelected is not null && names.Contains(previouslySelected)
            ? previouslySelected
            : names.Contains("Race Control") ? "Race Control" : names.FirstOrDefault() ?? "Race Control";

        lstNav.SelectedItem = toSelect;
        ShowPage(toSelect);
        SelectSearchedTab(toSelect);
    }

    private void ShowPage(string name)
    {
        var item = _navItems.FirstOrDefault(i => i.Name == name);
        if (item is null)
        {
            return;
        }

        // The whole swap happens with the content area's painting off.
        //
        // Every page is a Dock=Fill child of the same panel, stacked in
        // z-order with one of them visible. Hiding the current page
        // UNCOVERS whichever page is behind it, and Windows paints that the
        // instant it is uncovered - so walking the list setting Visible put
        // a page nobody asked for on screen for as long as it took the loop
        // to reach the one they did. That is the wrong screen for a moment
        // before the right one.
        //
        // The refresh hook below repopulates grids, and the first visit to
        // a page also straightens it, and both of those paint as well.
        // Nothing here is worth watching in progress. Only the result is.
        NoRedraw.Begin(pnlContent);
        pnlContent.SuspendLayout();

        try
        {
            // The wanted page first, and in front: with it already covering
            // the area, nothing behind it is ever exposed to begin with.
            // This alone fixes the swap; the redraw suppression above is
            // what covers the refresh and the straightening that follow.
            item.Page.Visible = true;
            item.Page.BringToFront();
            _shownPage = name;

            foreach (var nav in _navItems)
            {
                // Only the pages that are actually showing. Setting Visible
                // on all thirty-odd meant thirty-odd layout passes to
                // change one thing.
                if (nav != item && nav.Page.Visible)
                {
                    nav.Page.Visible = false;
                }
            }

            if (_navRefreshHooks.TryGetValue(name, out var refresh))
            {
                refresh();
            }

            TidyPage(name, item.Page);
        }
        finally
        {
            pnlContent.ResumeLayout(true);

            // In a finally: a panel left with its painting switched off
            // never draws again, and the app would sit showing whatever
            // page happened to be up when something threw.
            NoRedraw.End(pnlContent);
        }
    }

    /// <summary>
    /// Takes the user to a page the way clicking it in the sidebar would,
    /// so the sidebar's highlight, the page on screen and the keys that
    /// depend on the page all agree.
    ///
    /// ShowPage on its own swaps the content but leaves the sidebar
    /// pointing at the old page - which is how F11 used to put Race Control
    /// up while the flag keys still thought the user was somewhere else.
    ///
    /// A search that has filtered the page out of the sidebar is cleared
    /// first, since there is no other way to highlight it. A page the user
    /// has hidden from the sidebar altogether is still shown, just without
    /// a highlight - it has nothing to highlight.
    /// </summary>
    /// <summary>
    /// The "Start a Race Day" button, wherever it is pressed.
    ///
    /// Before the Race Day tabs are built there is nothing to start one
    /// with, and that is not an error worth a dialog - the button cannot
    /// be on screen yet either.
    /// </summary>
    private void StartRaceDayFromBanner() => _startRaceDay?.Invoke();

    /// <summary>
    /// Offers the setup wizard, once ever.
    ///
    /// A question rather than the wizard itself, because this fires on an
    /// existing club's first launch after upgrading as well as on a fresh
    /// install, and a window nobody asked for is a poor way to meet one. A
    /// club that says no is not asked again - HasSeenRaceDaySetup means
    /// "has answered", not "has completed" - and the button on The Day is
    /// there for ever either way.
    ///
    /// Not offered while a race day is already open: somebody mid-event who
    /// restarted the app is not setting up, and that is the moment it would
    /// be most unwelcome.
    /// </summary>
    private void OfferRaceDaySetupOnce()
    {
        if (_settings.HasSeenRaceDaySetup || IsDisposed || _openRaceDaySetup is null)
        {
            return;
        }

        if (_raceEvents.Active is { IsLegacy: false, IsOpen: true })
        {
            return;
        }

        _settings.HasSeenRaceDaySetup = true;
        _settings.Save();

        if (AppDialog.Show(this,
                "Would you like a hand setting up a race night?\r\n\r\n"
                + "It walks through the classes, the drivers, the day itself, who has turned up and the program - "
                + "and every step can be skipped. It is on Race Day > Setup as \"Set Up Tonight...\" whenever you want it.",
                "Set up tonight", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        _openRaceDaySetup.Invoke(0);
    }

    /// <summary>
    /// Brings the timetable's generated half in line with the programme.
    ///
    /// Two rules keep it from being presumptuous. It only ever writes the
    /// ROUNDS' own lines - anything somebody typed, doors or lunch or the
    /// presentation, is left exactly where it was put, and SyncToProgram
    /// keeps the lines already there rather than rebuilding them, so a time
    /// set against a round survives. And it only runs for a real race day:
    /// with only the legacy holder open the programme is the whole store,
    /// and writing that onto the holder's sheet would file every round the
    /// club has ever run as tonight's running order.
    ///
    /// It fills an empty sheet as well as maintaining a filled one. That
    /// used to need "Fill from the Programme" pressed once, which was fine
    /// while the timetable was a tab somebody landed on and is not fine now
    /// that it is a drawer - the one button that made the page work would
    /// have been behind the door it opens.
    ///
    /// No undo entry: this is not a change the user made, it is the
    /// timetable following one they made somewhere else, and a stack full of
    /// "run sheet rebuilt" buries the thing they actually did.
    /// </summary>
    private void SyncTimetableToProgramme()
    {
        if (_raceEvents.Active is not { IsLegacy: false })
        {
            return;
        }

        // The sheet this writes to is whichever day the schedule store is
        // pointed at, and the programme below is the CURRENT day's. They
        // agree because EventSpine.Bind subscribes to CurrentChanged before
        // this form does, so the store has already followed by the time a
        // day switch reaches here - but that is an ordering two files
        // apart, and if it ever stopped holding, the line below would write
        // tonight's programme onto yesterday's run sheet and clear
        // yesterday's lines off it. Worth one comparison to make that
        // impossible rather than merely unlikely.
        if (!string.Equals(_daySchedule.CurrentEventId, _raceEvents.CurrentId, StringComparison.Ordinal))
        {
            return;
        }

        var programme = EventSpine.Programme(_raceEvents, _rounds)
            .Select(r => new DayScheduleStore.ProgramLine(r.Id, r.ClassName, r.RoundLabel))
            .ToList();

        // An empty programme is handed over rather than skipped. Deleting
        // the day's last round used to return here, leaving its generated
        // line on the sheet with no round behind it - still printed on the
        // running order, still counted by Next Up, still measured for
        // running late. Lines somebody typed are not generated and
        // SyncToProgram leaves those alone, so an empty programme clears
        // exactly the lines the programme put there and nothing else.
        _daySchedule.SyncToProgram(programme);

        // Only when it is open. Shut, it re-reads on the way in.
        _daySchedulePage?.RefreshAll();
    }

    private void GoToPage(string name)
    {
        if (!lstNav.Items.Contains(name) && txtNavSearch.Text.Length > 0)
        {
            // Rebuilds the sidebar through TextChanged.
            txtNavSearch.Text = string.Empty;
        }

        if (!lstNav.Items.Contains(name))
        {
            ShowPage(name);
            return;
        }

        if (lstNav.SelectedItem == name)
        {
            // Already highlighted, so setting it again raises nothing - but
            // an earlier ShowPage may have put a different page up since.
            if (_shownPage != name)
            {
                ShowPage(name);
            }

            return;
        }

        // Raises SelectionChanged, which shows the page.
        lstNav.SelectedItem = name;
    }

    /// <summary>
    /// Pages already checked at the current text size. Cleared whenever the
    /// text size changes, since every page's spacing has to be re-judged
    /// against the new font - see ApplyFontScale.
    /// </summary>
    private readonly HashSet<string> _tidiedPages = new(StringComparer.Ordinal);

    /// <summary>
    /// Sends the layout check's findings to the console log - including
    /// from dialogs, which report through <see cref="LayoutDoctor.Log"/>
    /// since they have no other sink of their own.
    /// </summary>
    private void WireLayoutReporting() => LayoutDoctor.Log = OnClientRawMessage;

    /// <summary>
    /// Runs the layout check over a page the first time it is shown at this
    /// text size.
    ///
    /// On SHOW rather than on build, deliberately: a control on a hidden
    /// page reports itself hidden, and a hidden control has to be left out
    /// of the check - otherwise it acts as an invisible wall that everything
    /// visible gets pushed around.
    /// </summary>
    private int TidyPage(string name, Control page)
    {
        if (!_tidiedPages.Add(name))
        {
            return 0;
        }

        // Race Control lays itself out: LayoutRightColumn places every
        // group, the board's grid and the correction strip against the
        // real width on every resize. The repair pass moving things here
        // was one authority too many - its nudges were made against
        // whatever width the page had at startup and then fought the
        // page's own pass at the next resize.
        if (string.Equals(name, "Race Control", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        // The "one yellow button per page" pass used to run here. It is
        // gone, and this is the note explaining why so it does not get
        // reinvented.
        //
        // It picked the first button whose CAPTION read as a main action
        // and painted it yellow. But that reading is a substring match -
        // "export", "apply", "add" - over the order the controls happened
        // to be added in, and neither of those has anything to do with
        // which button matters. On Race History it landed on "Export
        // Session", which sat yellow next to a pink "Export Results" doing
        // the same kind of job; on Venues it turned the page's only main
        // action yellow and left nothing pink at all.
        //
        // The theme already says "this is the main one" in pink. A second
        // colour, chosen by keyword, was not a stronger signal - it was a
        // competing one.

        return LayoutDoctor.Tidy(page, name, OnClientRawMessage);
    }

    /// <summary>
    /// Checks EVERY page once, as soon as the window is up, rather than
    /// waiting for each to be opened.
    ///
    /// The point is to get the whole report in one go: after a change to the
    /// text size, the console lists every page that needed straightening and
    /// exactly what moved, instead of the problems turning up one at a time
    /// over the following week.
    ///
    /// Each page is made visible for the duration of its own check, because
    /// a control on a hidden page reports itself hidden and would be left
    /// out. Nothing is painted in between - no message is pumped inside this
    /// loop - so the pages flashing past is not something anybody sees.
    /// </summary>
    private void TidyAllPages()
    {
        // Only once the window is actually on screen. Before that every
        // control reports itself hidden, so a sweep would check nothing and
        // then mark every page as done.
        if (!Visible || _navItems.Count == 0)
        {
            return;
        }

        var adjusted = 0;
        var pages = 0;

        // Pass two resumes layout itself (it has to - see there), so the
        // finally must not do it again.
        var resumed = false;

        // Same reasoning as ShowPage. At startup the window has not been
        // revealed yet so there is nothing to see either way - but this
        // sweep runs again every time the text size changes, and then it is
        // thirty pages flashing past on a visible window.
        NoRedraw.Begin(pnlContent);
        pnlContent.SuspendLayout();

        try
        {
            // Pass one: the pages themselves, with layout suspended. Each
            // covers whichever of its tabs happens to be showing.
            foreach (var nav in _navItems)
            {
                var wasVisible = nav.Page.Visible;

                nav.Page.Visible = true;

                if (TidyPage(nav.Name, nav.Page) > 0)
                {
                    adjusted++;
                }

                nav.Page.Visible = wasVisible;
                pages++;
            }

            // Layout back on before pass two, and this is not optional.
            //
            // A TabPage is sized by its TabControl when it is selected, and
            // a TabControl inside a suspended parent has no real size to
            // give it. Checked that way, a never-opened tab measures itself
            // at about 192px wide and every label on it "runs past the right
            // edge" - which is what the first version of this reported for
            // Pace and Soundscapes. Nothing is painted either way; NoRedraw
            // is still on.
            pnlContent.ResumeLayout(true);
            resumed = true;

            // Pass two: every OTHER tab, which pass one could not see.
            //
            // LayoutDoctor works on what is VISIBLE - a hidden control
            // reports no useful bounds, so it skips one - and inside a
            // TabControl exactly one tab is visible at a time. Race Day's
            // Today was checked and its Entries tab never was; the Calendar
            // under Championship never was. Because a page is marked done
            // after one pass, they never would be either.
            //
            // That is how a label grown past the coordinate below it
            // survived on the Calendar. Not a check that failed - a check
            // that never ran.
            foreach (var nav in _navItems)
            {
                var wasVisible = nav.Page.Visible;

                nav.Page.Visible = true;

                foreach (var (tabName, tabPage) in SubTabsOf(nav.Page))
                {
                    if (TidyPage($"{nav.Name} › {tabName}", tabPage) > 0)
                    {
                        adjusted++;
                    }

                    pages++;
                }

                nav.Page.Visible = wasVisible;
            }
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"{AppTime.LogStampNow()}  [layout] Sweep stopped early - {ex.Message}");
        }
        finally
        {
            if (!resumed)
            {
                pnlContent.ResumeLayout(true);
            }

            NoRedraw.End(pnlContent);
        }

        OnClientRawMessage($"{AppTime.LogStampNow()}  [layout] Checked {Plural.Of(pages, "page")} at {AppTheme.FontScale * 100:0}% text size - "
            + (adjusted == 0 ? "nothing needed moving." : $"{adjusted} needed adjusting (listed above)."));
    }

    /// <summary>
    /// Every tab page inside a page, each one made visible long enough to
    /// be measured.
    ///
    /// Selecting a tab is the only honest way to do this: a TabPage that has
    /// never been shown has no laid-out children to measure, so there is
    /// nothing to be gained by asking it while it is hidden. The original
    /// selection is put back before this returns, and the whole sweep runs
    /// inside NoRedraw with layout suspended, so none of it is painted.
    ///
    /// Selecting a tab can raise SelectedIndexChanged, and a few tabs
    /// refresh themselves on that - which is harmless here (they re-read
    /// their own stores) and is also exactly what would happen the first
    /// time somebody clicked the tab.
    /// </summary>
    private static IEnumerable<(string Name, Control Page)> SubTabsOf(Control page)
    {
        // Collected first, while everything is still as it was. Walking the
        // tree does not care what is visible, so a TabControl nested inside
        // another tab is found here even though it is not on screen.
        var controls = new List<TabControl>();
        Collect(page, controls);

        foreach (var tabs in controls)
        {
            var previous = tabs.SelectedTab;

            try
            {
                foreach (TabPage tab in tabs.TabPages)
                {
                    // The tab that was already showing has just been checked
                    // as part of the page itself - it WAS the visible one.
                    // Without this, "Voice Announcements" and "Voice
                    // Announcements > Voice Settings" both reported the same
                    // sixteen adjustments over the same controls, which
                    // double-counts the page and reads as two problems.
                    if (ReferenceEquals(tab, previous))
                    {
                        continue;
                    }

                    tabs.SelectedTab = tab;

                    // YIELDED while the tab is still selected. Returning a
                    // finished list instead would hand back tab pages that
                    // are hidden again by the time the caller measures them,
                    // which is the exact blindness this exists to fix.
                    //
                    // Named by its caption, because that is what whoever
                    // reads the log sees on screen: "Race Day > Entries".
                    yield return (tab.Text, tab);
                }
            }
            finally
            {
                // Runs when the caller's foreach ends, however it ends.
                if (previous is not null)
                {
                    tabs.SelectedTab = previous;
                }
            }
        }

        static void Collect(Control parent, List<TabControl> into)
        {
            foreach (Control child in parent.Controls)
            {
                if (child is TabControl tabs)
                {
                    into.Add(tabs);
                }

                Collect(child, into);
            }
        }
    }

    /// <summary>
    /// F-key shortcuts for race control, active only when that page is
    /// showing (or Race Mode is on) - lets the person running the race trigger flags without
    /// having to click a specific small button, useful when they're also
    /// the one driving and only glancing at the screen. F-keys are safe to
    /// use globally like this since they never collide with normal text
    /// entry in any of the app's textboxes.
    /// </summary>
    private void MainForm_KeyDown(object? sender, KeyEventArgs e)
    {
        // Never Ctrl or Alt. Alt+F4 is "close the window", and it used to
        // throw a yellow flag on its way out; the Ctrl shortcuts are handled
        // in ProcessCmdKey, before a focused grid or box can swallow them.
        // Shift is allowed through for the few keys that use it (Shift+P,
        // Shift+W, ?) - never with an F-key, which stays bare.
        if (e.Control || e.Alt)
        {
            return;
        }

        // A letter, unlike an F-key, IS text - so letters and symbols only
        // count when nothing that takes typing has the focus, or naming a
        // session "Wet track" would warn whoever was selected.
        var typing = IsTypingInTextField();

        // --- Anywhere in the app ---
        if (!typing)
        {
            if (e.KeyCode == Keys.OemQuestion && e.Shift)
            {
                ShowShortcutSheet();
                MarkHandled(e);
                return;
            }

            if (e.KeyCode == Keys.T && e.Shift)
            {
                StartTour();
                MarkHandled(e);
                return;
            }

            if (e.KeyCode == Keys.M && !e.Shift)
            {
                var mute = !_announcer.Muted;
                SetAllSoundMuted(mute);
                OnClientRawMessage($"{AppTime.LogStampNow()}  [audio] {(mute ? "All sound muted" : "Sound back on")} (M).");
                MarkHandled(e);
                return;
            }

            // Shift+S, beside Shift+T for the tour: the two things that
            // walk you through something. Shifted, because the unshifted
            // letters belong to Race Control.
            if (e.KeyCode == Keys.S && e.Shift && _openRaceDaySetup is not null)
            {
                _openRaceDaySetup.Invoke(0);
                MarkHandled(e);
                return;
            }
        }

        // What is on screen, not what the sidebar has highlighted - the two
        // can differ (a button that opens Race Control from another page,
        // or Race Control hidden from the sidebar), and a flag key that
        // silently did nothing with the flags in plain view was the bug.
        // Race Mode counts whatever it shows: it exists to run the race.
        if (!_raceMode && _shownPage != "Race Control")
        {
            return;
        }

        var rc = _raceControlControl;

        // --- Function keys: bare only, and they work even mid-typing ---
        if (!e.Shift)
        {
            switch (e.KeyCode)
            {
                case Keys.F1: rc.TriggerStage(); e.Handled = true; return;
                case Keys.F2: rc.TriggerCountdown(); e.Handled = true; return;
                case Keys.F3: rc.TriggerGreen(); e.Handled = true; return;
                case Keys.F4: rc.TriggerYellow(); e.Handled = true; return;
                case Keys.F5: rc.TriggerRed(); e.Handled = true; return;
                case Keys.F6: rc.TriggerCheckered(); e.Handled = true; return;
                case Keys.F7: rc.TriggerResetFromKeyboard(); e.Handled = true; return;
                case Keys.F8: rc.TriggerQuickRace(); e.Handled = true; return;
            }
        }

        if (typing)
        {
            return;
        }

        // --- Letters and symbols: Race Control and the selected driver ---
        var handled = true;

        switch (e.KeyCode)
        {
            case Keys.W:
                if (e.Shift) rc.TriggerTakeBackWarning(); else rc.TriggerWarning();
                break;

            case Keys.P:
                rc.TriggerPenalty(e.Shift ? 10000 : 5000);
                break;

            // "=" and "+" share a key; either, with or without Shift, and
            // the keypad plus.
            case Keys.Oemplus or Keys.Add:
                rc.TriggerLapAdjust(1);
                break;

            case Keys.OemMinus or Keys.Subtract when !e.Shift:
                rc.TriggerLapAdjust(-1);
                break;

            case Keys.C when !e.Shift: rc.TriggerClearPenalty(); break;
            case Keys.L when !e.Shift: rc.TriggerLapBreakdown(); break;
            case Keys.E when !e.Shift: rc.TriggerEditDriver(); break;
            case Keys.B when !e.Shift: rc.TriggerOpenBigScreen(); break;
            case Keys.R when !e.Shift: rc.TriggerReadinessCheck(); break;
            case Keys.A when !e.Shift: rc.TriggerAnnouncerScreen(); break;

            default:
                handled = false;
                break;
        }

        if (handled)
        {
            MarkHandled(e);
        }
    }

    private static void MarkHandled(KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private AppTour? _tour;

    /// <summary>
    /// Starts the app tour (Shift+T, and the end of first-run setup). Only
    /// one at a time: asking again while it is open brings it forward.
    /// </summary>
    private void StartTour()
    {
        if (_tour is { IsDisposed: false })
        {
            _tour.Activate();
            return;
        }

        if (_raceMode)
        {
            // Race Mode hides the sidebar the tour walks through.
            OnClientRawMessage($"{AppTime.LogStampNow()}  [tour] Leave Race Mode (F11) to take the tour.");
            return;
        }

        _tour = new AppTour(this, GoToPage);
        _tour.FormClosed += (_, _) => _tour = null;
        _tour.Show(this);
    }

    /// <summary>The ? cheat sheet - see ShortcutCatalog.</summary>
    private void ShowShortcutSheet()
    {
        using var sheet = new ShortcutSheetForm();
        sheet.ShowDialog(this);
    }

    /// <summary>
    /// True when the focused control takes typed keys - a text box, a
    /// number box, or any combo (a drop-down list jumps to the item a
    /// letter starts, so a letter there is still the user choosing, not a
    /// shortcut).
    /// </summary>
    private bool IsTypingInTextField()
    {
        Control? focused = ActiveControl;

        while (focused is ContainerControl { ActiveControl: { } inner })
        {
            focused = inner;
        }

        // A grid cell being edited types into an editing box the grid owns,
        // which ActiveControl does not reach - the grid itself says so.
        return focused is TextBoxBase or UpDownBase or ComboBox
               || focused is DataGridView { IsCurrentCellInEditMode: true };
    }

    /// <summary>
    /// True when Escape means something to the focused control: a grid cell
    /// being edited (the key cancels the edit) or a drop-down that is open
    /// (the key closes it). A grid's editing box is a child of the grid, so
    /// it is what ActiveControl reaches, and it says so by what it is.
    /// </summary>
    private bool FocusedControlWantsEscape()
    {
        Control? focused = ActiveControl;

        while (focused is ContainerControl { ActiveControl: { } inner })
        {
            focused = inner;
        }

        return focused is IDataGridViewEditingControl
               || focused is DataGridView { IsCurrentCellInEditMode: true }
               || focused is ComboBox { DroppedDown: true };
    }

    private void LstNav_SelectionChanged()
    {
        if (lstNav.SelectedItem is string name)
        {
            ShowPage(name);
            SelectSearchedTab(name);
        }
    }

    /// <summary>Persists a new nav order after a drag-and-drop reorder in the sidebar.</summary>
    private void LstNav_ItemsReordered(List<string> newOrder)
    {
        // Persist the FULL order, hidden items included, so hiding/showing
        // later doesn't lose this arrangement.
        //
        // The dragged list is only what the sidebar was SHOWING - with a page
        // search typed, a handful of pages. The rest used to be appended
        // after it in the app's default order, so one drag with a search
        // active threw away the club's own arrangement of every other page.
        // Now the shown pages take their new order in the slots they already
        // held, and every other page stays exactly where it was.
        // Mapped in the order the sidebar is actually SHOWING, because that
        // is what was dragged. It is put back into the club's own order just
        // below, before anything is saved.
        var current = OrderedNavNames();
        var shown = new HashSet<string>(newOrder);
        var inNewOrder = new Queue<string>(newOrder.Where(current.Contains));
        var fullOrder = current
            .Select(name => shown.Contains(name) && inNewOrder.Count > 0 ? inNewOrder.Dequeue() : name)
            .ToList();

        // A drag dropped back where it started changes nothing, and must not
        // push a do-nothing step onto Undo.
        var orderBefore = _settings.NavOrder.ToList();

        // Race Day and Race Control are at the top of a race-night sidebar
        // because of the grouping, not because the club put them there.
        // Saving the list as dragged would write that into NavOrder and
        // leave them pinned there after the day closed - the same fault as
        // Manage Navigation's. They keep the slot the saved order already
        // gives them; the drag rearranges everything else.
        if (_navRaceNight)
        {
            fullOrder = WithoutRaceNightGrouping(fullOrder, orderBefore);
        }
        if (!orderBefore.SequenceEqual(fullOrder))
        {
            _undo.Capture("Reorder the sidebar", () =>
            {
                _settings.NavOrder = orderBefore.ToList();
                _settings.Save();
                RebuildNav();
            });
        }

        _settings.NavOrder = fullOrder;
        _settings.Save();

        // A page dropped into the other group goes back into its own.
        if (!OrderedNavNames().Where(newOrder.Contains).SequenceEqual(newOrder))
        {
            RebuildNav();
        }
    }

    /// <summary>
    /// A dragged order with the race-night grouping taken back out: the
    /// promoted pages returned to wherever the club's saved order had them,
    /// everything else left exactly as dragged.
    /// </summary>
    /// <param name="saved">
    /// The order as last saved. Empty on a club that has never rearranged
    /// anything, in which case the app's canonical order stands in - without
    /// that, the two promoted pages would be appended at the BOTTOM, which
    /// is the opposite of the bug this is preventing.
    /// </param>
    private List<string> WithoutRaceNightGrouping(List<string> dragged, List<string> saved)
    {
        var promoted = dragged
            .Where(n => NavTiers.TierOf(n, raceNight: true) == NavTier.Tonight)
            .ToList();

        if (promoted.Count == 0)
        {
            return dragged;
        }

        var reference = saved.Count > 0 ? saved : _navItems.Select(i => i.Name).ToList();
        var rest = dragged.Where(n => !promoted.Contains(n)).ToList();

        // Lowest saved index first, so inserting one does not shift the slot
        // the next one is aiming for.
        foreach (var name in promoted.OrderBy(n => reference.IndexOf(n) is var at && at < 0 ? int.MaxValue : at))
        {
            var at = reference.IndexOf(name);

            rest.Insert(at < 0 || at > rest.Count ? rest.Count : at, name);
        }

        return rest;
    }

    private void BtnManageNav_Click(object? sender, EventArgs e)
    {
        var toggleable = _navItems.Where(i => i.Name != "Connection").Select(i => i.Name).ToList();
        // The club's own order, NOT what the sidebar is showing. During a
        // race night the sidebar leads with Race Day and Race Control
        // because of the grouping; seeding this dialog from that and then
        // saving what comes back is what wrote the grouping into NavOrder
        // permanently. See OrderedNavNames.
        var clubOrder = VisibleOrderedNavNames(raceNight: false);

        var orderedToggleable = clubOrder.Where(toggleable.Contains)
            .Concat(toggleable.Where(n => !clubOrder.Contains(n)))
            .ToList();

        // _navItems is the app's canonical order, which is exactly what
        // "defaults" should mean here.
        var defaultOrder = _navItems.Where(i => i.Name != "Connection").Select(i => i.Name).ToList();

        using var form = new TabVisibilityForm(
            orderedToggleable, _settings.HiddenTabs, defaultOrder, _settings.RaceNightFocus);
        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        // Connection is not in the dialog (it cannot be hidden), so it goes
        // back where it already was. This used to put it first every time,
        // left over from when the connection page was pinned to the top -
        // so pressing OK here quietly moved it above Race Control.
        var connectionIndex = OrderedNavNames(raceNight: false).IndexOf("Connection");
        var newOrder = form.OrderedNames.ToList();
        newOrder.Insert(Math.Clamp(connectionIndex, 0, newOrder.Count), "Connection");

        // Restore Defaults in the dialog lands here too, so the sidebar as
        // it was is kept for Undo.
        var hiddenBefore = _settings.HiddenTabs.ToList();
        var orderBefore = _settings.NavOrder.ToList();
        var raceNightBefore = _settings.RaceNightFocus;

        if (!form.HiddenTabs.SetEquals(hiddenBefore)
            || !orderBefore.SequenceEqual(newOrder)
            || form.RaceNightFocus != raceNightBefore)
        {
            _undo.Capture("Change the sidebar's pages", () =>
            {
                _settings.HiddenTabs = hiddenBefore.ToList();
                _settings.NavOrder = orderBefore.ToList();
                _settings.RaceNightFocus = raceNightBefore;
                _settings.Save();

                // Recomputed, not remembered: the undo can be pressed much
                // later, by which time the race day this depended on may have
                // been closed.
                _navRaceNight = RaceNightWanted;
                RebuildNav();
            });
        }

        _settings.HiddenTabs = form.HiddenTabs.ToList();
        _settings.NavOrder = newOrder;
        _settings.RaceNightFocus = form.RaceNightFocus;
        _settings.Save();

        _navRaceNight = RaceNightWanted;
        RebuildNav();
    }

    /// <summary>
    /// VoiceSettingsControl and SoundEffectsControl each read their store's
    /// Current value once, at construction, to populate their fields - they
    /// have no mechanism to notice the underlying data changed out from
    /// under them. Applying a theme changes exactly that, so the simplest
    /// reliable fix is rebuilding both controls from scratch afterward,
    /// rather than trying to retrofit a refresh path into forms that were
    /// never designed to be re-populated live.
    /// </summary>
    /// <summary>
    /// After a soundscape is applied - on the Soundscapes page or by a venue:
    /// the pages that read their settings once are rebuilt or refreshed, and
    /// anything already playing picks up the new background straight away.
    /// Nothing is STARTED that wasn't playing - see SoundscapeBackground.
    /// (Ambience is switched ON by a soundscape with a crowd, but not
    /// started - it plays when a session or the Ambience page brings it up.)
    /// </summary>
    private void AfterSoundscapeApplied()
    {
        RebuildVoiceAndSoundTabs();

        _ambience.Settings = _ambienceStore.Current;
        if (_ambience.IsRunning)
        {
            // Layers are opened when ambience starts, so a new bed needs a restart.
            _ambience.Stop();
            _ambience.Start(ignoreEnabledSetting: true);
        }

        _music.Settings = _musicStore.Current;
        _music.Theme = _themeStore.ActiveThemeMusic();

        // Only with Theme music playing: the soundscape no longer touches
        // "My music", so music playing from the club's own library has
        // nothing new to pick up and carries on mid-track.
        if (_music.IsPlaying && _musicStore.Current.PlayThemeMusic == true)
        {
            // The queue is built when play starts; restarting plays the new soundscape's tracks.
            _music.Stop();
            _music.Play();
        }

        _ambienceControl?.RefreshAll();
        _musicControl?.RefreshAll();
    }

    /// <summary>
    /// Opens the announcer's screen, or brings it forward if it is already
    /// open. Reached from Race Control's button, the "A" shortcut and the
    /// remote - all three come through RaceControlControl.AnnouncerRequested.
    /// </summary>
    private void OpenAnnouncerWindow()
    {
        // Built once and reused for the life of the app. The X hides this
        // window rather than closing it - see AnnouncerForm - so a second
        // press brings the same one back with everything still on it, not a
        // fresh one with an empty talking-points list.
        if (_announcerWindow is not { IsDisposed: false })
        {
            _announcerWindow = new AnnouncerForm(_announcerControl);

            // Only reached on a real shutdown, given that hiding. Cleared
            // all the same, so a press afterwards finds nothing rather than
            // a disposed form - the same guard the projection window keeps.
            _announcerWindow.FormClosed += (_, _) => _announcerWindow = null;

            // Beside the main window where there is room, so the director
            // keeps Race Control and the announcer gets a screen of their
            // own rather than the two fighting over one. On the FIRST open
            // only: after that it stays wherever it was put.
            var screen = Screen.FromControl(this).WorkingArea;

            _announcerWindow.Location = Right + _announcerWindow.Width <= screen.Right
                ? new Point(Right + 8, Math.Max(screen.Top, Top))
                : new Point(
                    Math.Max(screen.Left, screen.Right - _announcerWindow.Width - 24),
                    Math.Max(screen.Top, screen.Top + 24));
        }

        var window = _announcerWindow;

        if (!window.Visible)
        {
            window.Show();
        }

        if (window.WindowState == FormWindowState.Minimized)
        {
            window.WindowState = FormWindowState.Normal;
        }

        // Always, whether the window was already up or not. The running
        // order changes during a race, and somebody pressing this twice is
        // usually pressing it BECAUSE something changed.
        _announcerControl.RefreshAll();

        window.Activate();
    }

    private void RebuildVoiceAndSoundTabs()
    {
        var voiceSubPage = _tabVoiceSubNav.TabPages[0];
        voiceSubPage.Controls.Remove(_voiceSettingsControl);

        // The announcement rows belong to the same control but are parented
        // in a tab of their own, so UserControl.Dispose never reaches them:
        // they have to be taken out and disposed by hand. Skipping this
        // would leave the Announcements tab showing the OLD page, still
        // wired to a control that had just been disposed - so the rows
        // would look fine, save nothing, and throw on the first Test.
        _voiceAnnouncementsSubPage.Controls.Remove(_voiceSettingsControl.AnnouncementsPage);
        _voiceSettingsControl.AnnouncementsPage.Dispose();

        _voiceSettingsControl.Dispose();
        _voiceSettingsControl = new VoiceSettingsControl(_voiceStore, _settings, _undo) { Dock = DockStyle.Fill, Announcer = _announcer, HeatCallerVoice = _heatCallerVoice };
        _voiceSettingsControl.StatusMessage += OnClientRawMessage;
        voiceSubPage.Controls.Add(_voiceSettingsControl);
        _voiceAnnouncementsSubPage.Controls.Add(_voiceSettingsControl.AnnouncementsPage);

        // Swapped inside its TAB page, not on pageSound - Sound Effects now
        // lives in a tab host, so removing/adding against the page would
        // have dropped it out of the tab and left it covering the whole
        // page instead.
        _soundEffectsSubPage.Controls.Remove(_soundEffectsControl);
        _soundEffectsControl.Dispose();
        _soundEffectsControl = new SoundEffectsControl(_soundStore, _undo) { Dock = DockStyle.Fill };
        _soundEffectsSubPage.Controls.Add(_soundEffectsControl);

        AppTheme.ApplyRecursively(voiceSubPage);
        AppTheme.ApplyRecursively(_voiceAnnouncementsSubPage);
        AppTheme.ApplyRecursively(pageSound);

        // Both pages were checked with the old controls in them, so the new
        // ones were never checked at all - at a larger text size they kept
        // whatever overlaps the check would have straightened. Forgotten
        // here, they are checked again the next time either page is shown.
        _tidiedPages.Remove("Voice");
        _tidiedPages.Remove("Sounds");
    }

    /// <summary>
    /// Runs a backup if automatic backups are enabled and the configured
    /// interval has elapsed since the last one. Safe to call often - it's a
    /// no-op the vast majority of the time, only actually creating a backup
    /// once the interval is genuinely due.
    /// </summary>
    private void CheckAutoBackup()
    {
        // Not while keys from another PC are still to be recovered: that
        // backup would carry no keys, and pruning could then delete an
        // older one that did. The hourly check runs it afterwards.
        if (!_settings.AutoBackupEnabled || SecretRecovery.Pending)
        {
            return;
        }

        var dueAtUtc = (_settings.LastAutoBackupUtc ?? DateTime.MinValue).AddDays(Math.Max(1, _settings.AutoBackupIntervalDays));
        if (DateTime.UtcNow < dueAtUtc)
        {
            return;
        }

        // One at a time, and not once a restore has frozen the files or the
        // app is closing. A Back Up Now still being written counts too:
        // two zips of the same folder at once only make both slower. Nothing
        // is recorded as done, so the next check (hourly, or the race
        // finishing) makes it - and when Back Up Now finishes it marks the
        // backup done itself, which usually means none is due by then.
        if (_autoBackupRunning || BackupService.IsBackupRunning || _closingDown || SafeFile.IsFrozen)
        {
            return;
        }

        // Postponed, not skipped: nothing is recorded as done, so the next
        // check - the hourly one, or the race finishing or being reset -
        // makes it. Zipping a big music library is heavy on the disk, and
        // that is not wanted while cars are on the track.
        // Practice counts too: it is flagged before its green, which waits
        // on the announcer, so it can still read Idle for a moment.
        if (_race.IsPracticeMode || _race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing)
        {
            return;
        }

        // Everything the background work needs is read here, on the UI
        // thread - the settings and the secrets can be changed by it at
        // any moment, and must not be read from another thread.
        // Guarded, not taken as read: a restored settings.json can name any
        // destination it likes, and this pass runs unattended every hour.
        // See BackupService.SafeBackupFolder. Pruning gets the same value,
        // so it never reaches into somewhere the backup did not go.
        var folder = BackupService.SafeBackupFolder(_settings.AutoBackupFolder);
        var keep = _settings.AutoBackupMaxToKeep;
        var clouds = CloudBackup.EnabledTargets(_settings);
        TaskScheduler uiThread;
        BackupSecrets? secrets;

        try
        {
            uiThread = TaskScheduler.FromCurrentSynchronizationContext();
            secrets = BackupService.CaptureSecrets();
        }
        catch (Exception ex)
        {
            // Never let a backup failure interrupt anything else the app is
            // doing - just note it and try again at the next check.
            OnClientRawMessage($"{AppTime.LogStampNow()}  [backup] Automatic backup failed: {ex.Message}");
            return;
        }

        // Made off the UI thread. It used to run right here, and zipping
        // the data folder (music library included) froze the whole app -
        // timing and all - for as long as that took.
        _autoBackupRunning = true;

        Task.Run(() =>
            {
                var path = BackupService.CreateBackup(folder, true, secrets);
                BackupService.PruneOldBackups(folder, keep);

                // After the local backup is safely made: a cloud copy that
                // fails is reported, but never undoes or blocks that.
                var cloudResults = CloudBackup.CopyToTargets(path, clouds, keep);
                return (Path: path, Cloud: cloudResults);
            })
            .ContinueWith(task =>
            {
                _autoBackupRunning = false;

                if (IsDisposed || SafeFile.IsFrozen)
                {
                    return;
                }

                if (task.Exception is { } ex)
                {
                    // Not marked as done, so the next check tries again.
                    OnClientRawMessage($"{AppTime.LogStampNow()}  [backup] Automatic backup failed: {ex.GetBaseException().Message}");
                    return;
                }

                _settings.LastAutoBackupUtc = DateTime.UtcNow;
                _settings.Save();

                OnClientRawMessage($"{AppTime.LogStampNow()}  [backup] Automatic backup created: {Path.GetFileName(task.Result.Path)}");

                foreach (var line in task.Result.Cloud)
                {
                    OnClientRawMessage($"{AppTime.LogStampNow()}  [backup] {line}");
                }
            },
            uiThread);
    }

    private void ChkAutoConnect_CheckedChanged(object? sender, EventArgs e)
    {
        _settings.DirectDecoderAutoConnect = chkAutoConnect.Checked;
        _settings.Save();

        if (chkAutoConnect.Checked && SelectedDecoder == DecoderConnection.EasyLap
            && _decoderClient.State is DecoderConnectionState.Disconnected or DecoderConnectionState.Error)
        {
            btnConnect.Enabled = false;
            btnDisconnect.Enabled = true;
            _decoderClient.Connect();
        }
    }

    private void OnDecoderStatusChanged(DecoderConnectionState state, string message)
    {
        // Not a bare InvokeRequired: it answers false before this
        // window has a handle, and this is called from the decoder's
        // own thread. See OnUiThread.
        if (!IsHandleCreated || InvokeRequired) { OnUiThread(() => OnDecoderStatusChanged(state, message)); return; }

        if (SelectedDecoder != DecoderConnection.EasyLap)
        {
            return; // not the active connection method right now - don't steal the shared status light
        }

        RenderDecoderStatus(state, message);
    }

    private void OnILapStatusChanged(DecoderConnectionState state, string message)
    {
        // Not a bare InvokeRequired: it answers false before this
        // window has a handle, and this is called from the decoder's
        // own thread. See OnUiThread.
        if (!IsHandleCreated || InvokeRequired) { OnUiThread(() => OnILapStatusChanged(state, message)); return; }

        if (SelectedDecoder != DecoderConnection.ILap)
        {
            return; // not the active connection method right now - don't steal the shared status light
        }

        RenderDecoderStatus(state, message);
    }

    /// <summary>Repopulates the COM port picker, keeping the remembered port selected when it is still present.</summary>
    private void RefreshSerialPorts()
    {
        var ports = ILapClient.AvailablePorts();

        cmbSerialPort.Items.Clear();
        cmbSerialPort.Items.AddRange(ports);

        if (ports.Length == 0)
        {
            return;
        }

        var remembered = Array.FindIndex(ports, p => string.Equals(p, _settings.ILapPort, StringComparison.OrdinalIgnoreCase));
        cmbSerialPort.SelectedIndex = remembered >= 0 ? remembered : ports.Length - 1;
    }

    private void BtnRefreshPorts_Click(object? sender, EventArgs e) => RefreshSerialPorts();

    /// <summary>
    /// Fills the I-Lap baud picker and keeps the setting in step with it.
    ///
    /// The setting always existed and the client always honoured it, but
    /// nothing on screen could change it - so a decoder set to anything but
    /// 9600 meant editing the settings file by hand. A rate already saved
    /// that is not one of the usual ones is kept in the list rather than
    /// quietly replaced, since somebody chose it on purpose.
    /// </summary>
    private void FillBaudRates()
    {
        var rates = ILapClient.CommonBaudRates.ToList();

        if (_settings.ILapBaud > 0 && !rates.Contains(_settings.ILapBaud))
        {
            rates.Add(_settings.ILapBaud);
            rates.Sort();
        }

        cmbBaud.Items.Clear();

        foreach (var rate in rates)
        {
            cmbBaud.Items.Add(rate);
        }

        cmbBaud.SelectedItem = rates.Contains(_settings.ILapBaud) ? _settings.ILapBaud : ILapClient.CommonBaudRates[0];

        // Wired after the first selection, so filling the list is not
        // mistaken for the user choosing a rate.
        cmbBaud.SelectedIndexChanged += (_, _) =>
        {
            if (cmbBaud.SelectedItem is not int rate || rate == _settings.ILapBaud)
            {
                return;
            }

            _settings.ILapBaud = rate;
            _settings.Save();

            if (_ilapClient.State is not DecoderConnectionState.Disconnected)
            {
                OnClientRawMessage($"{AppTime.LogStampNow()}  [ilap] Baud rate set to {rate} - it takes effect the next time you connect.");
            }
        };
    }

    private void RenderDecoderStatus(DecoderConnectionState state, string message)
    {
        lblStatus.Text = message;
        pnlStatusDot.BackColor = state switch
        {
            DecoderConnectionState.Disconnected => Color.Gray,
            DecoderConnectionState.Connecting => Color.Goldenrod,
            DecoderConnectionState.Connected => Color.SeaGreen,
            DecoderConnectionState.Error => Color.Firebrick,
            _ => Color.Gray,
        };

        btnConnect.Enabled = state is DecoderConnectionState.Disconnected or DecoderConnectionState.Error;
        btnDisconnect.Enabled = !btnConnect.Enabled;

        if (state == DecoderConnectionState.Connected)
        {
            var known = _profiles.All;
            OnClientRawMessage(known.Count == 0
                ? $"{AppTime.LogStampNow()}  [profiles] No driver profiles configured at all."
                : $"{AppTime.LogStampNow()}  [profiles] Known transponder keys: {string.Join(", ", known.Select(p => $"\"{p.Transponder}\"={p.DriverName}"))}");
        }
    }

    private void OnDecoderLapEvent(DirectLapEvent lap)
    {
        // Not a bare InvokeRequired: it answers false before this
        // window has a handle, and this is called from the decoder's
        // own thread. See OnUiThread.
        if (!IsHandleCreated || InvokeRequired) { OnUiThread(() => OnDecoderLapEvent(lap)); return; }

        // Recorded first, and whatever happens to the pass afterwards. A
        // pass that no race counts still proves the transponder is alive,
        // and that is the whole point of keeping them.
        _transponderWatch.Record(lap);

        // Checked BEFORE the lap is processed. Starting practice afterwards
        // would mean the crossing that triggered it lands in a session that
        // didn't exist yet, and the driver's first lap silently vanishes.
        // Practice goes green here, and this same crossing is then counted
        // by the line below as the driver's start lap.
        MaybeAutoStartPracticeOnGatePass(lap);

        _race.ProcessLapEvent(lap);
    }

    /// <summary>
    /// Starts a practice session when a car crosses the gate and nothing
    /// else is running.
    ///
    /// Deliberately narrow: only from Idle, never during a race, a
    /// countdown or an existing practice. At an open practice night people
    /// drift on and off track constantly, and nobody wants to walk back to
    /// the laptop to press Start - but nothing here should ever interrupt
    /// a real session.
    /// </summary>
    /// <param name="lap">
    /// The crossing that triggered it. Its decoder time stamps the green
    /// just before it, so the crossing counts - a green stamped from the
    /// estimated clock could land a few milliseconds after it, and the
    /// crossing was then thrown away as coming before the start.
    /// </param>
    private void MaybeAutoStartPracticeOnGatePass(DirectLapEvent lap)
    {
        // Not until the unfinished-race offer has been answered: a
        // practice started first took the session, and the race waiting to
        // be recovered was then never offered. See _recoveryDecided.
        if (!_settings.PracticeAutoStartOnGatePass
            || !_recoveryDecided
            || _practiceControl is null
            || _race.State != RaceState.Idle)
        {
            return;
        }

        // A pass the app made up has no decoder time to stamp the green
        // with - see DirectLapEvent.IsSynthetic. Ghosts do not come this
        // way today, but the estimated clock is the right fallback if one did.
        if (_practiceControl.TryAutoStartPractice(lap.IsSynthetic ? (long?)null : lap.DeviceTimeMs))
        {
            OnClientRawMessage($"{AppTime.LogStampNow()}  [practice] Auto-started - a car crossed the gate.");
        }
    }

    /// <summary>
    /// Shows the chosen decoder's own fields - the COM port and baud rate
    /// for I-Lap, the auto-connect tick for EasyLap - and puts that
    /// decoder's state on the shared status light.
    /// </summary>
    private void UpdateConnectionModeUi()
    {
        var decoder = SelectedDecoder;
        var isEasyLap = decoder == DecoderConnection.EasyLap;
        var isILap = decoder == DecoderConnection.ILap;

        // Auto-connect retry belongs to the EasyLap HID search alone; a
        // serial port that fails to open should say so once, not every
        // few seconds.
        chkAutoConnect.Visible = isEasyLap;

        lblSerialPort.Visible = isILap;
        cmbSerialPort.Visible = isILap;
        btnRefreshPorts.Visible = isILap;
        lblBaud.Visible = isILap;
        cmbBaud.Visible = isILap;

        if (isILap)
        {
            RefreshSerialPorts();
        }

        btnConnect.Text = "Connect";

        // The saved number, not the picker position - see DecoderConnection.
        // This is also what moves a retired Next Level Timing choice (0) on
        // to EasyLap in settings.json, the first time this runs after it.
        _settings.LastConnectionType = decoder;
        _settings.Save();

        // Only the chosen decoder stays connected. Switching used to leave
        // the old one running underneath - its laps still arriving, into
        // the same race, from hardware the screen said was not in use.
        if (!isEasyLap && _decoderClient.State is not DecoderConnectionState.Disconnected)
        {
            _decoderClient.Disconnect();
        }

        if (!isILap && _ilapClient.State is not DecoderConnectionState.Disconnected)
        {
            _ilapClient.Disconnect();
        }

        // The chosen decoder's ACTUAL state, rather than leaving the other
        // one's showing - switching from a connected EasyLap to I-Lap
        // should show I-Lap's own (probably disconnected) state, not a
        // stale green light.
        if (isILap)
        {
            RenderDecoderStatus(_ilapClient.State, _ilapClient.State.ToString());
        }
        else
        {
            RenderDecoderStatus(_decoderClient.State, _decoderClient.State.ToString());
        }
    }

    private void CmbConnectionType_SelectedIndexChanged(object? sender, EventArgs e) => UpdateConnectionModeUi();

    private void BtnConnect_Click(object? sender, EventArgs e)
    {
        if (SelectedDecoder == DecoderConnection.ILap)
        {
            if (cmbSerialPort.SelectedItem is not string ilapPort)
            {
                AppDialog.Show(this, "No COM port selected. Plug the I-Lap decoder in, press Rescan and pick its port.",
                    "No port", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _settings.ILapPort = ilapPort;

            // The picker's rate, which is also what is saved - read from
            // the picker so the two can never disagree at the moment of
            // connecting.
            if (cmbBaud.SelectedItem is int baud)
            {
                _settings.ILapBaud = baud;
            }

            _settings.Save();

            btnConnect.Enabled = false;
            btnDisconnect.Enabled = true;
            _ilapClient.Connect(ilapPort, _settings.ILapBaud);
            return;
        }

        btnConnect.Enabled = false;
        btnDisconnect.Enabled = true;
        _decoderClient.Connect();
    }

    private void BtnDisconnect_Click(object? sender, EventArgs e)
    {
        if (SelectedDecoder == DecoderConnection.ILap)
        {
            _ilapClient.Disconnect();
            btnConnect.Enabled = true;
            btnDisconnect.Enabled = false;
            return;
        }

        chkAutoConnect.Checked = false; // manual disconnect should stick, not immediately auto-retry
        _decoderClient.Disconnect();
        btnConnect.Enabled = true;
        btnDisconnect.Enabled = false;
    }

    /// <summary>Repopulates the "Race Round" dropdown with not-yet-recorded rounds from the race program, preserving the current selection or falling back to the last one used.</summary>
    private void RefreshActiveRoundCombo()
    {
        var previousId = (cmbActiveRound.SelectedItem as RoundComboItem)?.Round.Id
            ?? _activeRound?.Id
            ?? _settings.LastActiveRoundId;

        // Declared outside the try so it's still in scope for the
        // no-longer-available check further down.
        var idx = -1;

        _rebuildingRoundCombo = true;
        try
        {
            cmbActiveRound.Items.Clear();
            cmbActiveRound.Items.Add(RoundComboItem.None);
            // Tonight's rounds only, the same scope auto-advance uses. The
            // box listed every unrun round the club had ever made, so last
            // month's heats sat above tonight's with nothing to tell them
            // apart, and choosing one filed the race against that meeting.
            foreach (var round in _rounds.Unrecorded(_raceEvents.ActiveRoundScope()))
            {
                cmbActiveRound.Items.Add(new RoundComboItem(round));
            }

            if (previousId is not null)
            {
                for (var i = 0; i < cmbActiveRound.Items.Count; i++)
                {
                    if ((cmbActiveRound.Items[i] as RoundComboItem)?.Round.Id == previousId)
                    {
                        idx = i;
                        break;
                    }
                }
            }

            cmbActiveRound.SelectedIndex = idx >= 0 ? idx : 0;
        }
        finally
        {
            _rebuildingRoundCombo = false;
        }

        // The handler was suppressed during the rebuild, so bring the rest
        // of the UI back in step with whatever ended up selected - without
        // the handler's side effect of erasing the saved round id.
        var restored = cmbActiveRound.SelectedItem as RoundComboItem;
        if (restored is not null && restored.Round.Id != RoundComboItem.None.Round.Id)
        {
            _activeRound = restored.Round;
            ApplyRoundToSessionSetup(restored.Round);
        }
        else
        {
            _activeRound = null;
            cmbTrackLayout.Enabled = true;
            txtSessionLabel.ReadOnly = false;
            SetSessionNameVisible(true);
        }

        // The combo's own change handler re-reads Race Control's format,
        // but it fired while _activeRound still held the old round, so it
        // read the wrong round's format - at startup the remembered round's
        // format was never applied at all. Read again now _activeRound is
        // right. Null during construction, before Race Control exists.
        _raceControlControl?.RefreshFormat();

        // If the previously-selected round is no longer selectable (it was
        // recorded, or deleted), say so rather than silently reverting to
        // freeform - that silence is exactly what made this hard to spot.
        if (previousId is not null && idx < 0)
        {
            OnClientRawMessage(
                $"{AppTime.LogStampNow()}  [program] The selected race round is no longer available "
                + "(already recorded or deleted) - session setup is back to freeform.");
        }
    }

    private void CmbActiveRound_SelectedIndexChanged(object? sender, EventArgs e)
    {
        // Ignore the events raised while the list is being rebuilt; only a
        // real user choice should change the active round.
        if (_rebuildingRoundCombo)
        {
            return;
        }

        var item = cmbActiveRound.SelectedItem as RoundComboItem;
        if (item is null || item.Round.Id == RoundComboItem.None.Round.Id)
        {
            _activeRound = null;
            cmbTrackLayout.Enabled = true;
            txtSessionLabel.ReadOnly = false;
            SetSessionNameVisible(true);

            // Back to the day's own layout, since nothing more specific is
            // choosing one any more.
            SyncTrackLayoutToRaceDay();

            _settings.LastActiveRoundId = null;
            _settings.Save();
            return;
        }

        ApplyRoundToSessionSetup(item.Round);
        _settings.LastActiveRoundId = item.Round.Id;
        _settings.Save();
    }

    /// <summary>
    /// Takes a copy of the session setup - the active round, session type,
    /// track and label - and hands back an action that puts it back.
    ///
    /// Captured by a program or Next Up start BEFORE it switches the setup
    /// to the new round, and passed to Race Control as the host's part of
    /// the undo for the race that start abandons. Without it, undoing the
    /// start put the old race back but left the new round selected, so the
    /// old race's result was filed under the wrong heat.
    /// </summary>
    /// <summary>
    /// Puts a programme round on track and starts it - the one way a round
    /// is started, whether from the Program tab, the Next Up bar or a
    /// phone (RaceDayService).
    ///
    /// It REFUSES rather than asks: practice running, or a race running
    /// that nobody has agreed to abandon, is answered with the reason and
    /// nothing is touched. The desk asks its own questions first (practice,
    /// abandon the running race?) and passes the answer in as
    /// <paramref name="confirmedOverRunningRace"/>; a phone asks on the
    /// phone. Returns null when the round was started.
    /// </summary>
    /// <param name="showRaceControl">Switch the laptop to Race Control - true from the Program tab, false where the person is already there or not at the laptop.</param>
    /// <param name="logLine">What to write in the console, after the time stamp.</param>
    private string? StartRoundNow(RoundDefinition round, bool confirmedOverRunningRace, bool showRaceControl, string logLine)
    {
        // Practice is on the same session, and starting over it would wipe
        // the practice laps. The Practice page has its own save and
        // teardown, so it is stopped there, never taken over here.
        if (_race.IsPracticeMode)
        {
            return "Practice is running, so a race can't be started yet - stop practice first on the Practice page.";
        }

        if (_race.State == RaceState.Racing && !confirmedOverRunningRace)
        {
            return _race.IsDemoMode
                ? $"A demo is running. Starting \"{round.RoundLabel}\" ends it - confirm to go ahead."
                : $"A race is running. Starting \"{round.RoundLabel}\" abandons it and clears its laps - confirm to go ahead.";
        }

        // A demo is ended BEFORE the grid call is spoken: ending it
        // clears its backlog of queued lap calls, which would take the
        // grid call with it if Stage ended the demo afterwards.
        _raceControlControl.StopDemoForTakeover($"start \"{round.RoundLabel}\"");

        // The setup as it is now, before this round replaces it - so
        // undoing a start over a running race puts that race's round,
        // track and label back with it.
        var restoreSessionSetup = CaptureSessionSetupForUndo();

        // Same path as picking the round by hand, then hitting
        // auto-start - so there's one code path for starting a race.
        ApplyRoundToSessionSetup(round);
        _activeRound = round;
        RefreshActiveRoundCombo();

        if (showRaceControl)
        {
            GoToPage("Race Control");
        }

        _announcer.NotifyGridCall(round, _rounds, _trackLayouts, _raceEvents);
        _raceControlControl.TriggerAutoStart(confirmedOverRunningRace: true, restoreHostOnUndo: restoreSessionSetup);

        OnClientRawMessage($"{AppTime.LogStampNow()}  {logLine}");
        return null;
    }

    /// <summary>
    /// Checks a driver in or out from a phone - the LAN check-in page and
    /// the race-day manager both come here. True when it changed anything.
    /// UI thread only.
    /// </summary>
    private bool ApplyPhoneCheckIn(string transponder, bool wanted)
    {
        // Already so: no write, no republish. The server checks the
        // same thing against its copy of the roster, which can be a
        // moment behind; the store is the one that is right.
        if (_checkInStore.IsCheckedIn(transponder) == wanted)
        {
            return false;
        }

        _checkInStore.SetCheckedIn(transponder, wanted);
        _checkInChangedUtc[transponder] = DateTime.UtcNow;

        // Republished promptly rather than waiting for the next
        // tick, so the phone that made the change sees it on its
        // very next poll - but coalesced, so ten phones tapping at
        // once is one rebuild, not ten.
        PublishCheckInRosterSoon();

        // Only the ticks, not the whole page: somebody may be
        // working down the list at the desk.
        _checkInControl?.RefreshCheckInMarks();
        return true;
    }

    /// <summary>
    /// Builds the race-day manager and hands it the stores, Race Control
    /// and the Program tab - the same objects the desk uses - plus the
    /// three MainForm methods that stand in for a dialog: starting a round,
    /// checking a driver in, and sending a result again. Called once
    /// PopulateTabs has built Race Control and the Program tab. See
    /// RaceDayService and raceday-design.md.
    /// </summary>
    private void WireRaceDayService()
    {
        _raceDayService = new RaceDayService(new RaceDayServiceHost
        {
            UiThread = this,
            Settings = _settings,
            Undo = _undo,
            Race = _race,
            Events = _raceEvents,
            Classes = _raceClasses,
            Profiles = _profiles,
            Rounds = _rounds,
            History = _sessionHistory,
            CheckIns = _checkInStore,
            Tech = _techInspections,
            Schedule = _daySchedule,
            Program = _raceProgram,
            RaceControl = _raceControlControl,
            ActiveRound = () => _activeRound,

            // A phone never switches what is on the laptop's own screen -
            // showRaceControl is a Program-tab/Next-Up-bar convenience for
            // somebody sitting at the laptop, not something a request from
            // someone else's pocket should do.
            StartRound = (round, confirmedOverRunningRace, logLine) =>
                StartRoundNow(round, confirmedOverRunningRace, showRaceControl: false, logLine),

            SetCheckedIn = ApplyPhoneCheckIn,
            RepublishSession = PublishSessionToRelay,

            RefreshDesk = () =>
            {
                RefreshCurrentPage();
                RefreshActiveRoundCombo();
                _raceControlControl?.RefreshFormat();
                _checkInControl?.RefreshCheckInMarks();
            },

            Log = OnClientRawMessage,
            IsClosing = () => _closingDown,
        });

        // Lets the Wi-Fi /manage page reach the same service the desk does -
        // see WebServerService.Manager.
        _webServer.Manager = _raceDayService;
    }

    private Action CaptureSessionSetupForUndo()
    {
        var round = _activeRound;
        var trackLayout = cmbTrackLayout.SelectedItem as string;
        var label = txtSessionLabel.Text;

        return () =>
        {
            if (round is not null)
            {
                ApplyRoundToSessionSetup(round);
            }
            else
            {
                // Freeform, the same way picking "(freeform)" in the Round
                // box leaves it. Set here as well as by the box's own
                // handler, which does not run when the box was already on
                // freeform.
                cmbActiveRound.SelectedIndex = 0;
                _activeRound = null;
                cmbTrackLayout.Enabled = true;
                txtSessionLabel.ReadOnly = false;
                SetSessionNameVisible(true);
            }

            // The values themselves, as they were. The round above sets its
            // own, but a round with no track of its own leaves the track on
            // whatever the new round chose - and freeform sets none at all.
            if (trackLayout is not null)
            {
                var trackIdx = cmbTrackLayout.Items.IndexOf(trackLayout);
                if (trackIdx >= 0)
                {
                    cmbTrackLayout.SelectedIndex = trackIdx;
                }
            }

            txtSessionLabel.Text = label;
        };
    }

    /// <summary>
    /// Whether the session Name box is meant to be on screen.
    /// </summary>
    /// <remarks>
    /// A field rather than reading Control.Visible, which answers a
    /// different question: it is false for any child of a window that has
    /// not been shown yet, however the property was set. So at startup -
    /// before the form is up, which is exactly when a remembered round is
    /// restored - asking the label whether it is visible answers "no" for
    /// every control on the page, the guard below would skip the hide, and
    /// the box would appear beside a round that had already locked it.
    /// </remarks>
    private bool _sessionNameShown = true;

    /// <summary>
    /// Shows or hides the session Name box, and re-flows the row behind it.
    /// </summary>
    private void SetSessionNameVisible(bool visible)
    {
        if (_sessionNameShown == visible)
        {
            return;
        }

        _sessionNameShown = visible;

        lblSessionLabel.Visible = visible;
        txtSessionLabel.Visible = visible;

        LayoutSessionRow();
    }

    /// <summary>
    /// Points the Track box at the open race day's layout.
    /// </summary>
    /// <remarks>
    /// THIS BOX USED TO BE A SECOND, UNSYNCHRONISED COPY of something the
    /// race day already knows.
    ///
    /// Nothing ever pushed RaceEvent.TrackLayoutName into it. It was seeded
    /// from the last layout used and only picked up the day's by accident,
    /// when a programme round that had inherited the day's layout happened to
    /// be selected. So a FREEFORM session on a day set to one track recorded
    /// itself against whichever track was used last - and that name is what
    /// decides which track's all-time record a lap challenges, and what every
    /// personal best, driver stat, trend and relay record is filed under. It
    /// was wrong quietly, which is the worst way for it to be wrong.
    ///
    /// Called when a day is opened or switched, when the round goes back to
    /// freeform, and once at startup. NOT on every visit to the page: picking
    /// a track by hand for one session is allowed, and it stays picked until
    /// the day or the round changes under it.
    /// </remarks>
    private void SyncTrackLayoutToRaceDay()
    {
        // A round's own layout is more specific than the day's default, and
        // the box is locked to it while one is chosen.
        if (_activeRound is not null)
        {
            return;
        }

        var wanted = _raceEvents.Current.TrackLayoutName;

        // A day that names no layout has no opinion, so whatever is in the
        // box stays - which is also what happens with no day open at all.
        if (string.IsNullOrWhiteSpace(wanted))
        {
            return;
        }

        var idx = cmbTrackLayout.Items.IndexOf(wanted);

        if (idx >= 0 && cmbTrackLayout.SelectedIndex != idx)
        {
            cmbTrackLayout.SelectedIndex = idx;
        }
    }

    /// <summary>Pre-fills and locks Track/Label from a race-program round.</summary>
    private void ApplyRoundToSessionSetup(RoundDefinition round)
    {
        _activeRound = round;

        if (!string.IsNullOrWhiteSpace(round.TrackLayoutName))
        {
            var trackIdx = cmbTrackLayout.Items.IndexOf(round.TrackLayoutName);
            if (trackIdx >= 0)
            {
                cmbTrackLayout.SelectedIndex = trackIdx;
            }
        }
        cmbTrackLayout.Enabled = false;

        // Still SET, so the saved session keeps its "Class - Round" name,
        // but hidden: with a round chosen, this box shows a name derived
        // from the round and refuses to be edited - a field spending a row
        // to repeat what the Round picker beside it already says.
        txtSessionLabel.Text = $"{round.ClassName} - {round.RoundLabel}";
        txtSessionLabel.ReadOnly = true;
        SetSessionNameVisible(false);

        // Reflect the selection in the dropdown itself, in case this was
        // triggered from the Race Program dialog rather than the combo.
        for (var i = 0; i < cmbActiveRound.Items.Count; i++)
        {
            if ((cmbActiveRound.Items[i] as RoundComboItem)?.Round.Id == round.Id)
            {
                cmbActiveRound.SelectedIndex = i;
                return;
            }
        }
    }

    /// <summary>Wraps a RoundDefinition with a friendly display string for the combo box.</summary>
    private sealed record RoundComboItem(RoundDefinition Round)
    {
        public static readonly RoundComboItem None = new(new RoundDefinition { Id = string.Empty, RoundLabel = "(freeform - no race program round)" });

        public override string ToString() =>
            Round.Id == string.Empty ? Round.RoundLabel : $"[{Round.ClassName}] {Round.RoundLabel} ({Round.Kind})";
    }

    /// <summary>
    /// Adds a Garage car for each car name a driver has just gained.
    /// </summary>
    /// <remarks>
    /// Marshalled to the UI thread: the phone's race-day page and relay
    /// signups add drivers from wherever the request arrived, and the car
    /// store is read by the Garage page on this one.
    /// </remarks>
    private void OnProfileCarsAdded(DriverProfile profile, IReadOnlyList<string> names)
    {
        if (IsDisposed || _closingDown)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => OnProfileCarsAdded(profile, names));
            return;
        }

        var added = GarageSync.AddCars(profile, names, _cars);

        if (added == 0)
        {
            return;
        }

        var who = string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName;

        OnClientRawMessage(
            $"{AppTime.LogStampNow()}  [garage] Added {Plural.Of(added, "car")} for {who}: {string.Join(", ", names)}.");

        // Showing on screen right now - the phone and the new-transponder
        // prompt can add a car while somebody is looking at the wall.
        if (_shownPage == "Garage")
        {
            RefreshCurrentPage();
        }
    }

    /// <summary>
    /// Keeps the Race Control track picker in step with the layouts on
    /// disk, whichever page changed them.
    /// </summary>
    /// <remarks>
    /// The picker used to be refreshed only at startup, after the first-run
    /// wizard, and when the CONNECTION page was opened - which was right
    /// when the session row lived on Connection, and has been wrong ever
    /// since it moved to Race Control. A track made on Track Layouts
    /// therefore never reached the picker you would go and look for it in.
    ///
    /// The store says when it saved; this decides whether that save changed
    /// anything the picker shows. Most saves do not - a new track record
    /// set mid-race is a save too - and rebuilding the list clears and
    /// re-selects the box, which churns the hidden copy Race Control records
    /// races against and writes settings twice. So the NAMES are compared
    /// first, and nothing happens unless one was added, removed or renamed.
    /// </remarks>
    private void OnTrackLayoutsChanged()
    {
        if (IsDisposed || _closingDown)
        {
            return;
        }

        // The record check that saves mid-race runs off the lap pipeline,
        // which is not promised to be on this thread.
        if (InvokeRequired)
        {
            BeginInvoke(() => OnTrackLayoutsChanged());
            return;
        }

        var shown = cmbTrackLayout.Items.Cast<object>().Skip(1).Select(o => o as string);
        var stored = _trackLayouts.All.Select(t => t.Name);

        if (shown.SequenceEqual(stored, StringComparer.Ordinal))
        {
            return;
        }

        RefreshTrackLayoutCombo();

        // The day may name a track that did not exist until a moment ago -
        // the setup wizard creates both together. Fills that gap only: a
        // track somebody picked by hand is left where they put it.
        if (cmbTrackLayout.SelectedIndex <= 0)
        {
            SyncTrackLayoutToRaceDay();
        }
    }

    /// <summary>Repopulates the track layout dropdown, preserving the current selection if it still exists, or falling back to the last one used (persisted across restarts) if there's no in-memory selection yet.</summary>
    private void RefreshTrackLayoutCombo()
    {
        var previous = cmbTrackLayout.SelectedItem as string ?? (_settings.LastTrackLayoutName.Length > 0 ? _settings.LastTrackLayoutName : null);
        cmbTrackLayout.Items.Clear();
        cmbTrackLayout.Items.Add("(none)");
        foreach (var t in _trackLayouts.All)
        {
            cmbTrackLayout.Items.Add(t.Name);
        }

        var idx = previous is not null ? cmbTrackLayout.Items.IndexOf(previous) : -1;
        cmbTrackLayout.SelectedIndex = idx >= 0 ? idx : 0;

        // Race Control keeps its own list of layouts to record races
        // against; a layout added, renamed or removed has to reach it too,
        // or a race is filed against a name that no longer exists. Null
        // on the first call, which comes before Race Control is built.
        _raceControlControl?.RefreshTrackLayouts();
    }

    /// <summary>
    /// Every console line, unfiltered.
    ///
    /// Kept separately from what's displayed so a filter narrows the VIEW
    /// rather than throwing lines away - you can focus on one tag, then
    /// widen again with everything still there. Capped so a long race day
    /// doesn't grow it without limit.
    /// </summary>
    private readonly List<string> _consoleLines = new();

    private const int MaxConsoleLines = 5000;

    /// <summary>
    /// How far over the cap the console is allowed to run before it is
    /// trimmed back.
    ///
    /// This is the flicker.
    ///
    /// Trimming used to happen the moment the console went one line over
    /// 5000 - which, once it is full, is EVERY line. And trimming rebuilds
    /// the box: five thousand lines joined into a single ~400KB string and
    /// assigned to Text, which clears the control and refills it. During a
    /// race, with messages arriving several times a second, that is a
    /// multi-thousand-line text box being emptied and repopulated in front
    /// of you, continuously.
    ///
    /// Letting it run a block over the cap before trimming turns that into
    /// one rebuild per thousand lines instead of one per line.
    /// </summary>
    private const int ConsoleTrimBlock = 1000;

    /// <summary>
    /// Turns a control's painting off while its contents are replaced.
    ///
    /// A multiline TextBox is a native EDIT control. It does its own
    /// painting and ignores DoubleBuffered, so rewriting it shows as a
    /// clear followed by a refill however fast the two happen.
    /// WM_SETREDRAW is what stops that: painting is suppressed entirely
    /// while the text changes, and the refresh on the way out draws the
    /// finished result once.
    /// </summary>
    private static class NoRedraw
    {
        private const int WmSetRedraw = 0x000B;

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        public static void Begin(Control control)
        {
            // Nothing to suppress before the control has a window, and
            // asking for its Handle would force one into existence early.
            if (control.IsHandleCreated)
            {
                SendMessage(control.Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
            }
        }

        public static void End(Control control)
        {
            if (!control.IsHandleCreated)
            {
                return;
            }

            SendMessage(control.Handle, WmSetRedraw, 1, IntPtr.Zero);

            // Refresh rather than Invalidate: the control has been blind to
            // every change just made to it, and a repaint queued for later
            // would leave it showing the old contents until something else
            // happened to be on screen.
            control.Refresh();
        }
    }

    /// <summary>
    /// Hands the web server a fresh copy of the driver list and who is
    /// marked present.
    ///
    /// A copy, not the store. The server answers requests on thread-pool
    /// threads and CheckInStore is plain dictionaries and hash sets - it is
    /// only ever safe on this thread, so what crosses over is an immutable
    /// snapshot built here.
    /// </summary>
    private void PublishCheckInRoster()
    {
        // Stopped whichever way this was reached: a coalesced publish still
        // pending would only repeat this one.
        _checkInRosterCoalesce.Stop();

        static string NameOf(DriverProfile profile) =>
            string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName;

        // All, not AllRaceable: ghosts are raceable, but nobody checks in a
        // ghost, and a list of real drivers with "Ghost of Christmas Past"
        // in it - on the LAN page and in the relay's roster - is a list
        // somebody has to explain.
        var roster = _profiles.All
            .OrderBy(NameOf, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => new WebServerService.CheckInEntry(
                p.Transponder,
                NameOf(p),
                p.ClassName,
                _checkInStore.IsCheckedIn(p.Transponder)))
            .ToList();

        _webServer.PublishRoster(roster);
        PublishRosterToRelay(roster);
        PublishCheckInToRelay(roster);
    }

    /// <summary>
    /// A roster republish within a moment, coalescing a burst of check-ins
    /// into one. See _checkInRosterCoalesce.
    /// </summary>
    private void PublishCheckInRosterSoon()
    {
        if (!_checkInRosterCoalesce.Enabled)
        {
            _checkInRosterCoalesce.Start();
        }
    }

    /// <summary>
    /// The online check-in list. Sent whether check-in is on or off - off,
    /// it is an empty list, which is what takes the names back off the
    /// relay when a club switches it off.
    /// </summary>
    private void PublishCheckInToRelay(IReadOnlyList<WebServerService.CheckInEntry> roster)
    {
        if (!_settings.RelayEnabled)
        {
            return;
        }

        var current = _raceEvents.Current;
        var eventName = current.Id == RaceEventStore.LegacyEventId ? string.Empty : current.Name;

        SendToRelayIfChanged(RelayChannel.CheckIn, "the check-in list", () => RelayDocuments.CheckIn(
            roster, _profiles, _settings, eventName, _relayCheckInPhotos));
    }

    /// <summary>
    /// The check-in list sent again now, whether or not it looks changed -
    /// for a change the club is waiting to see work, like a new set of PINs.
    /// </summary>
    private void RepublishCheckInNow()
    {
        _lastRelayDocuments.Remove(RelayChannel.CheckIn);

        if (_webServer.IsRunning || _settings.RelayEnabled)
        {
            PublishCheckInRoster();
        }
    }

    /// <summary>
    /// Gives a racer added since the last tick their PIN, and forgets the
    /// PINs of racers deleted. Cheap - a dictionary check per racer - so it
    /// rides the roster's two-second tick; ProfileStore has no event to
    /// hang it on, and the half-dozen places that add a profile would each
    /// have to remember to call it.
    /// </summary>
    private void KeepRacerPinsInStep()
    {
        if (SecretRecovery.Pending || !_settings.RelayCheckInEnabled || !_settings.RelayRacerPinsEnabled)
        {
            return;
        }

        try
        {
            if (RacerPins.EnsurePins(_settings, _profiles))
            {
                _settings.Save();
            }
        }
        catch (Exception ex)
        {
            // Logged once per distinct fault, like a document that will not
            // build: this runs every two seconds.
            if (_relayBuildErrors.Add($"pins|{ex.Message}"))
            {
                OnClientRawMessage($"[pins] Could not give new racers a check-in PIN: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Personal PINs, kept up to date: a new set when the rotation says one
    /// is due, a PIN for anybody without one, then - when the club has
    /// asked for it - an email to every racer who has not been sent their
    /// current PIN. At startup, every ten minutes, and when the Club Website page
    /// changes them.
    ///
    /// Only while online check-in and personal PINs are both on: a PIN
    /// nobody can use is not worth emailing anybody.
    /// </summary>
    /// <returns>True when it changed the PINs and has already sent the check-in list again.</returns>
    private bool RunRacerPinsPass()
    {
        // Not while keys locked to another PC are still to be recovered:
        // the racers' current PINs are among them, and a set made now
        // would be emailed out and then replaced by the recovery.
        if (_closingDown || SecretRecovery.Pending || !_settings.RelayCheckInEnabled || !_settings.RelayRacerPinsEnabled)
        {
            return false;
        }

        var republished = false;

        // Never mid-race. A new set is for the start of a day, and a desk
        // queue of racers whose PINs had just stopped working, between two
        // heats, is the one moment it must not happen. The next pass after
        // the race picks it up.
        var raceLive = _race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing;

        try
        {
            // When racing last stopped, so a set that falls due at midnight
            // with the meeting still going waits for the track to go quiet
            // rather than changing every PIN between two heats. A session
            // saved without an end time counts from its start.
            var lastSessionEndUtc = _sessionHistory.All
                .Select(s => s.EndedAtUtc > s.StartedAtUtc ? s.EndedAtUtc : s.StartedAtUtc)
                .DefaultIfEmpty(DateTime.MinValue)
                .Max();
            var rotated = !raceLive && RacerPins.RotateIfDue(
                _settings, _profiles, DateTime.UtcNow, lastSessionEndUtc == DateTime.MinValue ? (DateTime?)null : lastSessionEndUtc);
            var ensured = RacerPins.EnsurePins(_settings, _profiles);

            if (rotated || ensured)
            {
                // Published and emailed only once the PINs are actually on
                // disk. They live nowhere else - RacerPins is inside the
                // sealed blob in settings.json - so a failed write means the
                // set about to be emailed to sixty drivers will not survive
                // the next restart. It worked all evening and then every PIN
                // in every inbox stopped, with one console line hours earlier
                // as the only clue.
                if (!_settings.Save())
                {
                    OnClientRawMessage(
                        $"{AppTime.LogStampNow()}  [pins] New racer PINs could NOT be saved, so they have not been "
                        + "published or emailed. The PINs already in use are unchanged. Close anything that might "
                        + "have settings.json open and try again.");

                    return false;
                }

                RepublishCheckInNow();
                republished = true;
            }

            if (rotated)
            {
                OnClientRawMessage($"[pins] Made a new set of check-in PINs for {Plural.Of(_settings.RacerPins.Count, "racer")}.");
            }
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"[pins] Could not update the check-in PINs: {ex.Message}");
            return republished;
        }

        if (!raceLive)
        {
            EmailPinsAutomatically();
        }

        return republished;
    }

    /// <summary>
    /// Starts emailing every racer who is waiting for their current PIN, in
    /// the background, when the club has switched that on and set up its
    /// mailbox. Returns at once: the sending takes a second and a half per
    /// racer, and none of it is on this thread.
    /// </summary>
    private void EmailPinsAutomatically()
    {
        if (!_settings.RacerPinsEmailAutomatically
            || !PinMailer.IsConfigured(_settings)
            || PinMailer.IsSending
            || RelayCheckIn.CheckInPageUrl(_settings) is null)
        {
            return;
        }

        var pending = RacerPins.AwaitingEmail(_settings, _profiles)
            .Where(t => !_pinMailFailed.Contains($"{t}|{_settings.RacerPins[t]}"))
            .ToList();

        if (pending.Count == 0)
        {
            return;
        }

        _ = EmailPinsInBackgroundAsync(pending);
    }

    private async Task EmailPinsInBackgroundAsync(IReadOnlyList<string> transponders)
    {
        try
        {
            // Called on the UI thread, and SendPinsAsync reads settings and
            // profiles before its first await - so all of that happens here,
            // on this thread. Only the sending happens elsewhere.
            var result = await PinMailer.SendPinsAsync(_settings, _profiles, transponders, null, _pinMailCancel.Token)
                .ConfigureAwait(false);

            // Settings are changed on the UI thread and nowhere else.
            TryPost(() => ApplyAutomaticPinMail(result));
        }
        catch (Exception ex)
        {
            // Fire and forget, so nothing else would ever see this.
            OnClientRawMessage($"[pins] Could not email check-in PINs: {ex.Message}");
        }
    }

    /// <summary>Records what an automatic PIN email run sent, and says so in the console in one line.</summary>
    private void ApplyAutomaticPinMail(MailResult result)
    {
        if (_closingDown)
        {
            return;
        }

        if (RacerPins.MarkEmailed(_settings, result.Sent) > 0)
        {
            _settings.Save();
        }

        foreach (var transponder in result.FailedTransponders)
        {
            if (_settings.RacerPins.TryGetValue(transponder, out var pin))
            {
                _pinMailFailed.Add($"{transponder}|{pin}");
            }
        }

        if (result.Cancelled)
        {
            return;
        }

        if (!result.Ok)
        {
            if (result.Message != _lastPinMailProblem)
            {
                _lastPinMailProblem = result.Message;
                OnClientRawMessage($"[pins] Could not email check-in PINs: {result.Message}");
            }

            if (result.Sent.Count == 0)
            {
                return;
            }
        }
        else
        {
            _lastPinMailProblem = null;
        }

        var line = $"[pins] Emailed {Plural.Of(result.Sent.Count, "racer")} their new PIN";

        if (result.Failures.Count > 0)
        {
            line += $"; {result.Failures.Count} could not be sent";
        }

        var noEmail = _profiles.All.Count(p => _settings.RacerPins.ContainsKey(p.Transponder) && RacerPins.EmailFor(p) is null);

        if (noEmail > 0)
        {
            line += $"; {noEmail} {(noEmail == 1 ? "has" : "have")} no email";
        }

        OnClientRawMessage(line + ".");
    }

    // ------------------------------------------------------ online sign-up

    /// <summary>Opens Online Sign-ups, or brings it forward if it is already open.</summary>
    private void ShowSignups()
    {
        if (_signupsForm is { IsDisposed: false } open)
        {
            open.Activate();
            return;
        }

        var form = new SignupsForm(_signupDesk, _settings, _profiles, _raceClasses, _rentals, _undo);
        form.FormClosed += (_, _) =>
        {
            if (ReferenceEquals(_signupsForm, form))
            {
                _signupsForm = null;
            }

            form.Dispose();
        };

        _signupsForm = form;
        form.Show(this);
    }

    /// <summary>How many sign-ups are waiting, on the Entries button and in the sidebar.</summary>
    private void UpdateSignupCounts()
    {
        var waiting = _signupDesk.Waiting.Count;

        _checkInControl?.SetSignupCount(waiting);
        lstNav.SetBadge("Race Day", waiting);
    }

    /// <summary>
    /// A driver just approved from an online sign-up: their badge id, their
    /// own check-in PIN, the check-in list sent again with them in it, and
    /// the PIN emailed - whether or not the club emails PINs automatically,
    /// since the sign-up page told them it would come.
    ///
    /// Returns whether the PIN email was started, so RelaySignupDesk only
    /// tells the phone "your PIN is on its way" when it is.
    /// </summary>
    private bool GiveSignedUpRacerTheirPin(string transponder)
    {
        RebuildBadgeIds();
        _checkInControl?.RefreshAll();

        if (SecretRecovery.Pending || !_settings.RelayCheckInEnabled || !_settings.RelayRacerPinsEnabled)
        {
            RepublishCheckInNow();
            return false;
        }

        try
        {
            if (RacerPins.EnsurePins(_settings, _profiles))
            {
                _settings.Save();
            }
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"[signup] Could not give the new racer a check-in PIN: {ex.Message}");
        }

        RepublishCheckInNow();

        if (!_settings.RacerPins.ContainsKey(transponder)
            || RacerPins.EmailFor(_profiles.Get(transponder)) is null
            || !PinMailer.IsConfigured(_settings))
        {
            return false;
        }

        if (PinMailer.IsSending)
        {
            OnClientRawMessage("[signup] PINs are being emailed right now, so the new racer's PIN wasn't sent. Use Email PINs... on the Club Website page once that's done.");
            return false;
        }

        _ = EmailPinsInBackgroundAsync(new[] { transponder });
        return true;
    }

    /// <summary>
    /// Every driver's online id, recomputed. Cheap - an HMAC per driver -
    /// and only once a key exists: making one just to fill this map would
    /// be deciding, on the club's behalf, that it wants online check-in.
    /// </summary>
    private void RebuildBadgeIds()
    {
        var secret = _settings.RelayCheckInSecret;

        if (string.IsNullOrWhiteSpace(secret))
        {
            if (_relayBadgeIds.Count > 0)
            {
                _relayBadgeIds = new Dictionary<string, string>(StringComparer.Ordinal);
            }

            return;
        }

        var map = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var profile in _profiles.AllRaceable)
        {
            if (!GhostService.IsGhost(profile.Transponder) && !string.IsNullOrWhiteSpace(profile.Transponder))
            {
                map[RelayCheckIn.IdFor(profile.Transponder, secret)] = profile.Transponder;
            }
        }

        _relayBadgeIds = map;
    }

    /// <summary>
    /// A driver checked in or out from their own phone. Applied exactly as
    /// a tap on the LAN check-in page is, so the Entries page, the roster
    /// and everything downstream cannot tell the two apart.
    /// </summary>
    /// <returns>
    /// True when the tap has been dealt with - applied, or deliberately
    /// ignored - and may be acknowledged to the relay. False only when it
    /// could not be dealt with, so the relay keeps it for next time.
    /// </returns>
    private bool ApplyRelayCheckIn(RelayCheckInTap tap)
    {
        try
        {
            if (!_relayBadgeIds.TryGetValue(tap.Id, out var transponder))
            {
                // The map is rebuilt every two seconds; a driver added in the
                // last two may simply not be in it yet.
                RebuildBadgeIds();

                if (!_relayBadgeIds.TryGetValue(tap.Id, out transponder))
                {
                    OnClientRawMessage("[relay] A check-in arrived for a driver this laptop does not recognise - their profile may have been deleted, or the tap came from a QR check-in or list from before QR check-ins were reissued.");
                    return true;
                }
            }

            var profile = _profiles.Get(transponder);
            var name = string.IsNullOrWhiteSpace(profile?.DriverName) ? transponder : profile!.DriverName;

            // From another day: the relay keeps taps for twelve hours, and
            // a laptop that was off overnight would otherwise open the next
            // meeting by checking in everybody from the last one.
            if (DateTime.UtcNow - tap.AtUtc > TimeSpan.FromHours(12))
            {
                OnClientRawMessage($"[relay] Ignored an old check-{(tap.Wanted ? "in" : "out")} for {name} from {tap.AtUtc.ToLocalTime():g}.");
                return true;
            }

            // Overruled already. The relay is polled every few seconds, so a
            // tap can arrive after the desk has changed the same driver -
            // and applying it then would quietly undo the desk.
            if (_checkInChangedUtc.TryGetValue(transponder, out var changedUtc) && tap.AtUtc < changedUtc)
            {
                OnClientRawMessage($"[relay] Ignored a check-{(tap.Wanted ? "in" : "out")} for {name} from their phone - it was changed here since.");
                return true;
            }

            // The tap's own time, not now: a second tap made just after it,
            // collected in the same batch, must not look older than this.
            _checkInChangedUtc[transponder] = tap.AtUtc;

            if (_checkInStore.IsCheckedIn(transponder) == tap.Wanted)
            {
                return true;
            }

            _checkInStore.SetCheckedIn(transponder, tap.Wanted);

            OnClientRawMessage($"[relay] {name} checked {(tap.Wanted ? "in" : "out")} from their phone.");

            PublishCheckInRosterSoon();
            _checkInControl?.RefreshCheckInMarks();

            return true;
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"[relay] Could not apply a check-in from a phone: {ex.Message}. It will be tried again.");
            return false;
        }
    }

    /// <summary>
    /// The relay's copy of the roster, which is NOT the one the local
    /// check-in page gets.
    ///
    /// Two differences, both deliberate. The LAN page keeps full names,
    /// because a driver standing at the track has to be able to find
    /// themselves in the list; the relay honours whatever the club chose.
    /// And no transponder ids cross: on a public address, an id that
    /// follows somebody from meeting to meeting, next to their name, is a
    /// tracking key nobody asked for. The page does not need it.
    /// </summary>
    private void PublishRosterToRelay(IReadOnlyList<WebServerService.CheckInEntry> roster)
    {
        try
        {
            var abbreviate = _settings.RelayAbbreviateNames;

            // The LAN roster names an unnamed driver by their transponder;
            // that stops here.
            var names = RelayPublisher.DisplayNames(
                roster.Select(d => RelayDocuments.PublicNameOrUnnamed(d.Name, d.Transponder)).ToList(),
                abbreviate);

            var document = JsonSerializer.Serialize(new
            {
                checkedIn = roster.Count(d => d.CheckedIn),
                total = roster.Count,
                drivers = roster.Select((d, i) => new
                {
                    name = names[i],
                    className = d.ClassName,
                    checkedIn = d.CheckedIn,
                }).ToList(),
            });

            // This runs every two seconds whether anything moved or not.
            // Sending an identical document that often is a POST a second
            // for nothing, all meeting, so it only goes when it changes.
            if (document == _lastRelayRoster)
            {
                return;
            }

            _lastRelayRoster = document;
            _relayPublisher.PublishSnapshot(RelayChannel.Roster, document);
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"[relay] Could not build the roster: {ex.Message}");
        }
    }

    /// <summary>
    /// A frame of the live race, once a second while one is happening.
    ///
    /// Idle time is skipped on purpose - a board sitting between heats
    /// does not need re-sending sixty times a minute - but the frame that
    /// records a session STOPPING does go, so the relay is left showing
    /// the result of the heat that just ended rather than the last moment
    /// of it running.
    /// </summary>
    private void PublishStateToRelay()
    {
        if (!_settings.RelayEnabled)
        {
            return;
        }

        var state = _race.State;
        var live = state is RaceState.Staging or RaceState.Countdown or RaceState.Racing;
        var changed = state != _lastRelayRaceState;

        _lastRelayRaceState = state;

        if (!live && !changed)
        {
            return;
        }

        try
        {
            _relayPublisher.PublishSnapshot(
                RelayChannel.State,
                _webServer.BuildStateJson(_settings.RelayAbbreviateNames, forRelay: true));
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"[relay] Could not build the live state: {ex.Message}");
        }
    }

    /// <summary>
    /// Championship standings, for every season, in one document.
    ///
    /// Every season rather than "the current one" because there is no such
    /// thing in this app's data: SeasonStore holds a list and nothing
    /// marks one as active. Inventing a current season here would be a
    /// guess that every later feature would then have to keep making the
    /// same way. Sending them all costs a few hundred bytes and lets the
    /// public page offer whichever one a club wants to show.
    /// </summary>
    private void PublishStandingsToRelay()
    {
        if (!_settings.RelayEnabled)
        {
            return;
        }

        try
        {
            var abbreviate = _settings.RelayAbbreviateNames;

            // Standings are keyed by name, and a car nobody named raced
            // under its transponder number - so any "name" that is one of
            // those numbers is replaced before it goes out.
            var transponderNames = TransponderNamesInHistory();

            var document = JsonSerializer.Serialize(new
            {
                seasons = _seasonStore.All.Select(season =>
                {
                    var table = SeasonStore.ComputeStandings(
                        season,
                        EventSpine.SessionsFor(season, _raceEvents, _rounds, _sessionHistory),
                        _profiles);

                    // Per season, because a season is the list a reader
                    // compares within - and shortened together, so two
                    // drivers never share a name in the same table.
                    var names = RelayPublisher.DisplayNames(
                        table.Select(entry => transponderNames.Contains((entry.DriverName ?? string.Empty).Trim())
                            ? RelayDocuments.UnnamedDriver
                            : entry.DriverName ?? string.Empty).ToList(),
                        abbreviate);

                    return new
                    {
                        id = season.Id,
                        name = season.Name,
                        standings = table.Select((entry, index) => new
                        {
                            pos = index + 1,
                            name = names[index],
                            points = entry.TotalPoints,
                            counted = entry.SessionsCounted,
                            wins = entry.Wins,
                            podiums = entry.Podiums,

                            // BestFinish starts at int.MaxValue to mean
                            // "no finish yet". Sent as null rather than as
                            // a two-billionth place.
                            best = entry.BestFinish == int.MaxValue ? (int?)null : entry.BestFinish,
                            dropped = entry.DroppedRounds,
                        }).ToList(),
                    };
                }).ToList(),
            });

            _relayPublisher.PublishSnapshot(RelayChannel.Standings, document);
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"[relay] Could not build standings: {ex.Message}");
        }
    }

    /// <summary>
    /// Every saved result whose driver "name" is only its transponder
    /// number - a car nobody had named when it raced. For keeping those
    /// numbers off the relay where a document has names but no
    /// transponders to compare them with.
    /// </summary>
    private HashSet<string> TransponderNamesInHistory()
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var session in _sessionHistory.All)
        {
            foreach (var result in session.Results)
            {
                if (RelayDocuments.PublicNameOrUnnamed(result.DriverName, result.Transponder) == RelayDocuments.UnnamedDriver
                    && !string.IsNullOrWhiteSpace(result.DriverName))
                {
                    found.Add(result.DriverName.Trim());
                }
            }
        }

        return found;
    }

    /// <summary>
    /// The schedule, badges and tracks pages' documents.
    ///
    /// Rides the roster's two-second tick. The schedule is cheap and is
    /// what a driver refreshes to see whether they are up, so it is built
    /// every tick; badges earned and the tracks involve reading files and
    /// drawing pictures, and change after a race rather than during one,
    /// so they are rebuilt every thirty seconds. Every one of them is only
    /// SENT when it differs from the last one that was.
    /// </summary>
    private void PublishExtrasToRelay()
    {
        if (!_settings.RelayEnabled)
        {
            return;
        }

        var generation = _relayPublisher.Generation;

        if (generation != _relayGeneration)
        {
            // A new club, a new address, or names switched to short form:
            // everything already sent was sent somewhere else, or said
            // somebody's name the club no longer wants said. Start again.
            _relayGeneration = generation;
            _lastRelayDocuments.Clear();
            _lastRelayRoster = string.Empty;
            _relayBuildErrors.Clear();
            _relayExtrasTick = 0;
            _relayCachesResetUtc = DateTime.UtcNow;
            _relayBadgesSentUtc = DateTime.MinValue;

            // Standings otherwise only go after a session is saved, which
            // would leave a newly set-up club with an empty standings page
            // until its next race.
            PublishStandingsToRelay();

            // And the day's results, which are otherwise sent once, as each
            // session is saved, from a queue held only in memory. A laptop
            // closed with results still queued - or pointed at a new relay
            // - lost them for good. Generation starts below the publisher's
            // own, so this runs on the first pass after startup too.
            RepublishRecentResults();
        }
        else if (DateTime.UtcNow - _relayCachesResetUtc > TimeSpan.FromMinutes(10))
        {
            // Every ten minutes, forget what was sent. A document counts as
            // sent the moment it is queued, not when the relay takes it, so
            // one it refused, or lost to a delete or a relay restart, would
            // otherwise stay missing until it next changed - which for the
            // tracks or the site look can be never.
            _relayCachesResetUtc = DateTime.UtcNow;
            _lastRelayDocuments.Remove(RelayChannel.Site);
            _lastRelayDocuments.Remove(RelayChannel.Programme);
            _lastRelayDocuments.Remove(RelayChannel.Achievements);
            _lastRelayDocuments.Remove(RelayChannel.Tracks);
            _lastRelayDocuments.Remove(RelayChannel.CheckIn);
            _lastRelayDocuments.Remove(RelayChannel.Manage);
            _lastRelayDocuments.Remove(RelayChannel.Signup);
            _lastRelayRoster = string.Empty;
        }

        var abbreviate = _settings.RelayAbbreviateNames;

        // The look first, so a club that has just picked a theme sees it on
        // the very next page load. Cheap: the logo is cached.
        SendToRelayIfChanged(RelayChannel.Site, "the site look", () => RelayDocuments.Site(_settings));

        // Settings-only and just as cheap as the look, so it goes every
        // tick rather than waiting for the throttled group below - a club
        // that has just switched the manager on, or changed its PIN,
        // reaches the relay within a couple of seconds rather than up to
        // thirty.
        SendToRelayIfChanged(RelayChannel.Manage, "the race-day manager", () => RelayDocuments.Manage(_settings));

        // Settings-only too: sign-up open or not, the classes, the invite
        // codes' hashes - so an invite just emailed works within seconds.
        // Only once a club has used it: a relay from before sign-up has no
        // such channel, and a club that never asked for sign-up should not
        // be told its relay "may need updating" because of it.
        if (_signupDesk.InUse)
        {
            SendToRelayIfChanged(RelayChannel.Signup, "driver sign-up", _signupDesk.Document);
        }

        SendToRelayIfChanged(RelayChannel.Programme, "the schedule", () => RelayDocuments.Programme(
            _daySchedule, _rounds, _raceEvents, _profiles, _settings.RelayPublishRoster, abbreviate));

        if (_relayExtrasTick++ % 15 != 0)
        {
            return;
        }

        SendToRelayIfChanged(RelayChannel.Achievements, "badges earned", () => RelayDocuments.Achievements(
            RelayAchievements(), _profiles, abbreviate));

        SendToRelayIfChanged(RelayChannel.Tracks, "the tracks", () => RelayDocuments.Tracks(
            _trackLayouts, _sessionHistory, _profiles, _pbResets, _raceEvents.Current.TrackLayoutName, abbreviate, _relayTrackImages));

        // The catalogue is fixed while the app runs, and it carries every
        // badge's artwork - so it is drawn and sent once, not twice a minute.
        // Once an hour it goes again regardless, for the same reason the
        // other documents are refreshed: once was once per process, and a
        // relay that lost it kept a badges page with no badges all day.
        if (!_lastRelayDocuments.ContainsKey(RelayChannel.Badges)
            || DateTime.UtcNow - _relayBadgesSentUtc > TimeSpan.FromHours(1))
        {
            _lastRelayDocuments.Remove(RelayChannel.Badges);
            _relayBadgesSentUtc = DateTime.UtcNow;
            SendToRelayIfChanged(RelayChannel.Badges, "the badge list", RelayDocuments.Badges);
        }
    }

    /// <summary>
    /// Earned badges for the relay, read from disk at most every five
    /// minutes - or on the next pass after a session is saved, which is
    /// when badges are awarded.
    ///
    /// It was a fresh load from disk on the UI thread every thirty seconds,
    /// all meeting, for a list that changes a few times a night. There is
    /// no change event on the store to wait for instead.
    /// </summary>
    private AchievementStore RelayAchievements()
    {
        if (_relayAchievements is null || DateTime.UtcNow - _relayAchievementsReadUtc > TimeSpan.FromMinutes(5))
        {
            _relayAchievements = AchievementStore.Load();
            _relayAchievementsReadUtc = DateTime.UtcNow;
        }

        return _relayAchievements;
    }

    /// <summary>
    /// Sends every session from the last thirty-six hours again.
    ///
    /// The relay keeps results by session id and takes a newer copy over
    /// an older one, so sending one it already has changes nothing - and
    /// sending one it lost, or never got, puts it back. Thirty-six hours
    /// covers a meeting that ran past midnight and a laptop reopened the
    /// next morning, without re-sending a whole season to a new relay.
    /// </summary>
    private void RepublishRecentResults()
    {
        try
        {
            var since = DateTime.UtcNow - TimeSpan.FromHours(36);

            var sent = _sessionHistory.All
                .Where(s => (s.EndedAtUtc > s.StartedAtUtc ? s.EndedAtUtc : s.StartedAtUtc) >= since)

                // Oldest first, so they land in the order they were raced.
                .OrderBy(RelayResultsIndex.EffectiveStart)
                .ToList();

            foreach (var session in sent)
            {
                PublishSessionToRelay(session);
            }

            // Once per year, after the results rather than with each of
            // them. A night of twenty heats is twenty result documents and
            // ONE index - sending the index after each would re-send the
            // whole year twenty times over a connection that has just come
            // back, which is the moment it can least afford it.
            foreach (var year in RelayResultsIndex.YearsOf(sent))
            {
                PublishResultsIndexForYear(year);
            }
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"[relay] Could not re-send recent results: {ex.Message}");
        }
    }

    private void SendToRelayIfChanged(RelayChannel channel, string what, Func<string> build)
    {
        try
        {
            var document = build();

            if (_lastRelayDocuments.TryGetValue(channel, out var last) && last == document)
            {
                return;
            }

            _lastRelayDocuments[channel] = document;
            _relayPublisher.PublishSnapshot(channel, document);
        }
        catch (Exception ex)
        {
            if (_relayBuildErrors.Add($"{channel}|{ex.Message}"))
            {
                OnClientRawMessage($"[relay] Could not build {what}: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// A result status worth showing a spectator, or nothing.
    ///
    /// The app marks an ordinary finisher "active". That is a state this
    /// program tracks, not news - and published as-is it appeared under
    /// "Time" for every driver on the page.
    /// </summary>
    private static string PublishableStatus(string? status)
    {
        var text = (status ?? string.Empty).Trim();

        return text.Equals("active", StringComparison.OrdinalIgnoreCase) ? string.Empty : text;
    }

    /// <summary>Republishes the index for the year one session belongs to. A demo race is not in it, so it changes nothing.</summary>
    private void PublishResultsIndexFor(SessionRecord session)
    {
        if (session.IsDemoSession())
        {
            return;
        }

        PublishResultsIndexForYear(RelayResultsIndex.YearOf(session));
    }

    /// <summary>
    /// Builds and publishes one year of the results index.
    ///
    /// Built from the whole history every time rather than patched: a
    /// patched index would drift from what was actually saved the first
    /// time a session was deleted or a result corrected outside the paths
    /// that call this, and an index that disagrees with the results is
    /// worse than no index - the page would offer a session the relay
    /// cannot hand back. A year of a busy club is a few hundred sessions,
    /// so building it costs nothing worth saving.
    /// </summary>
    private void PublishResultsIndexForYear(string year)
    {
        try
        {
            var json = RelayResultsIndex.Build(
                year,
                _sessionHistory.All,

                // A round belongs to a meeting, and a meeting usually to a
                // championship. The championship's name is what makes a
                // season picker worth having - one entry per season rather
                // than one per race night - so it is preferred, and the
                // meeting's own name is the fallback for a one-off.
                roundId => SeasonLabelFor(roundId),
                _settings.RelayAbbreviateNames,
                _settings.RelayResultsFromUtc);

            _relayPublisher.PublishResultsIndex(year, json);
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"[relay] Could not build the results index for {year}: {ex.Message}");
        }
    }

    /// <summary>The championship a round is part of, or the meeting's own name, or nothing.</summary>
    private string? SeasonLabelFor(string? roundId)
    {
        if (string.IsNullOrWhiteSpace(roundId))
        {
            return null;
        }

        var raceEvent = _raceEvents.EventForRound(roundId);

        if (raceEvent is null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(raceEvent.ChampionshipId))
        {
            var season = _seasonStore.All.FirstOrDefault(s =>
                string.Equals(s.Id, raceEvent.ChampionshipId, StringComparison.Ordinal));

            if (season is not null && !string.IsNullOrWhiteSpace(season.Name))
            {
                return season.Name;
            }
        }

        return raceEvent.Id == RaceEventStore.LegacyEventId ? null : raceEvent.Name;
    }

    /// <summary>
    /// Publishes one finished session's results.
    ///
    /// Built here rather than inside RelayPublisher because the stores are
    /// only safe on this thread, and because the club's name setting has
    /// to be applied where the document is made - the publisher is
    /// deliberately blind to what it carries, so it could not apply it
    /// even if asked.
    ///
    /// Transponder ids are left out for the same reason as the roster.
    /// </summary>
    private void PublishSessionToRelay(SessionRecord session)
    {
        // A demo race is a rehearsal, not a result - it never goes on the
        // club's public results page.
        if (session.IsDemoSession())
        {
            return;
        }

        try
        {
            var abbreviate = _settings.RelayAbbreviateNames;

            // The whole field at once, so two drivers never arrive at the
            // same shortened name. One at a time turned "Ghost of
            // Christmas Past" and "Ghost of Christmas Present" into two
            // identical rows.
            //
            // A car nobody had named raced under its transponder number,
            // and that is what its result says its name is. Not online.
            var names = RelayPublisher.DisplayNames(
                session.Results.Select(r => RelayDocuments.PublicNameOrUnnamed(r.DriverName, r.Transponder)).ToList(),
                abbreviate);

            var document = JsonSerializer.Serialize(new
            {
                id = session.Id,
                type = session.SessionType.ToString(),
                label = session.Label,
                track = session.TrackLayoutName,
                roundId = session.RoundId,
                startedUtc = session.StartedAtUtc,
                endedUtc = session.EndedAtUtc,
                results = session.Results.Select((r, i) => new
                {
                    pos = r.Position,
                    name = names[i],
                    car = r.CarModel,
                    laps = r.Laps,
                    fastLapMs = r.FastLapMs,
                    elapsedMs = r.ElapsedMs,

                    // "active" is what this app calls a driver who simply
                    // raced. It is internal vocabulary, and published it
                    // filled the finishing-time column with the word
                    // "active" on every row. Only a real exception - a
                    // DNF, a disqualification - is worth a reader's
                    // attention, so only that is sent.
                    status = PublishableStatus(r.Status),
                }).ToList(),
            });

            // The effective start - the end, for a session saved without a
            // start - as the results index files it (see
            // RelayResultsIndex.EffectiveStart). The raw start of such a
            // session is DateTime.MinValue, which PublishResult's window
            // ignores, so the index then left out a session whose result
            // the relay had been given.
            _relayPublisher.PublishResult(session.Id, document, RelayResultsIndex.EffectiveStart(session));
        }
        catch (Exception ex)
        {
            OnClientRawMessage($"[relay] Could not build the results document: {ex.Message}");
        }
    }

    private void OnClientRawMessage(string line)
    {
        // Closing the window disposes the pages, and disposing Demo Mode
        // resets the race session - which logs, which lands here after the
        // console box is already gone. That threw ObjectDisposedException
        // out of Dispose and showed as a crash on exit.
        if (IsDisposed || Disposing || txtConsole.IsDisposed)
        {
            return;
        }

        // No handle yet - the constructor is still running, or the window
        // has not been shown. InvokeRequired answers false then even on a
        // background thread (there is no handle to ask), so the line used
        // to be written into the text box from whichever thread logged it.
        // Held instead, and shown when the handle arrives.
        if (!IsHandleCreated)
        {
            lock (_pendingConsoleLines)
            {
                _pendingConsoleLines.Add(line);
            }

            // The handle may have been created while that line was being
            // added, after OnHandleCreated had already drained the list.
            if (IsHandleCreated)
            {
                TryPost(DrainPendingConsoleLines);
            }

            return;
        }

        if (InvokeRequired)
        {
            TryPost(() => OnClientRawMessage(line));
            return;
        }

        _consoleLines.Add(line);

        if (_consoleLines.Count > MaxConsoleLines + ConsoleTrimBlock)
        {
            // Trimmed in a block rather than one line at a time - see
            // ConsoleTrimBlock. The list operation was already batched; it
            // was the REBUILD it triggers that was running per line.
            _consoleLines.RemoveRange(0, _consoleLines.Count - MaxConsoleLines);
            RenderConsole();
            return;
        }

        if (!ConsoleLineVisible(line))
        {
            return;
        }

        txtConsole.AppendText(line + Environment.NewLine);

        // AppendText already leaves the caret at the end and scrolls there,
        // so this is only needed when it did not - when the user has
        // clicked into the log and the selection was restored to where they
        // put it. Doing it unconditionally scrolled and repainted the box a
        // second time for every line.
        if (chkAutoScroll.Checked && txtConsole.SelectionStart != txtConsole.TextLength)
        {
            txtConsole.SelectionStart = txtConsole.TextLength;
            txtConsole.ScrollToCaret();
        }
    }

    /// <summary>Whether a line passes the current filter.</summary>
    private bool ConsoleLineVisible(string line)
    {
        var needle = CurrentConsoleFilter();

        if (string.IsNullOrWhiteSpace(needle))
        {
            return true;
        }

        // Several terms separated by spaces match ANY of them, so
        // "audio-wait audio-trace" shows both without regex.
        var terms = needle.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var matched = terms.Any(t => line.Contains(t, StringComparison.OrdinalIgnoreCase));

        return chkFilterExclude.Checked ? !matched : matched;
    }

    /// <summary>The active filter text - the preset unless something is typed.</summary>
    private string CurrentConsoleFilter()
    {
        var typed = txtConsoleFilter.Text.Trim();

        if (typed.Length > 0)
        {
            return typed;
        }

        // Parenthesised: "x as string switch" is ambiguous to the compiler,
        // which warns rather than guessing at the precedence.
        return (cmbConsolePreset.SelectedItem as string) switch
        {
            "Audio timing" => "[audio-wait] [audio-trace]",
            "Voice" => "[voice]",
            "Sound" => "[sound]",
            "Race" => "[race]",
            "Decoder" => "[decoded [raw HID]",
            "Storage" => "[storage]",
            "Hide raw HID" => "[raw HID]",
            _ => string.Empty,
        };
    }

    /// <summary>Redraws the console from the full buffer, applying the filter.</summary>
    /// <summary>
    /// The console's own right-click menu, drawn like the rest of the app.
    ///
    /// A read-only TextBox otherwise shows the stock Windows menu - grey,
    /// square-cornered, and offering Cut, Paste and Delete on a box that
    /// cannot be edited. Every other menu in the app is drawn by the theme,
    /// so this was the one place the illusion broke.
    ///
    /// The items are what a log actually needs: take a copy of what you can
    /// see, take a copy of everything, or clear it.
    /// </summary>
    private void BuildConsoleMenu(TextBox console)
    {
        var menu = new ContextMenuStrip();

        var copySelection = new ToolStripMenuItem("Copy");
        copySelection.Click += (_, _) => CopyToClipboard(console.SelectedText);

        var copyVisible = new ToolStripMenuItem("Copy everything shown");
        copyVisible.Click += (_, _) => CopyToClipboard(console.Text);

        var selectAll = new ToolStripMenuItem("Select all");
        selectAll.Click += (_, _) =>
        {
            console.Focus();
            console.SelectAll();
        };

        var clear = new ToolStripMenuItem("Clear the console");
        clear.Click += (_, _) =>
        {
            _consoleLines.Clear();
            console.Clear();
        };

        menu.Items.AddRange(new ToolStripItem[]
        {
            copySelection, copyVisible, new ToolStripSeparator(), selectAll, new ToolStripSeparator(), clear,
        });

        // Copy is pointless with nothing selected, so it says so rather
        // than sitting there doing nothing when clicked.
        menu.Opening += (_, _) =>
        {
            copySelection.Enabled = console.SelectionLength > 0;
            copyVisible.Enabled = console.TextLength > 0;
            clear.Enabled = _consoleLines.Count > 0;
        };

        AppTheme.StyleContextMenu(menu);

        console.ContextMenuStrip = menu;
    }

    /// <summary>
    /// Puts text on the clipboard without letting a locked clipboard become
    /// an error dialog - another process can hold it, and a failed copy is
    /// not worth interrupting anyone over.
    /// </summary>
    private void CopyToClipboard(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        try
        {
            Clipboard.SetText(text);
            OnClientRawMessage($"{AppTime.LogStampNow()}  [console] Copied {Plural.Of(text.Split('\n').Length, "line")}.");
        }
        catch
        {
            // ignored - see the summary.
        }
    }

    private void RenderConsole()
    {
        var visible = _consoleLines.Where(ConsoleLineVisible).ToList();

        // Three separate changes to the box below - the text, the trailing
        // newline, and the scroll - each of which would repaint on its own.
        // Wrapped so the control paints once, at the end, already correct.
        NoRedraw.Begin(txtConsole);

        try
        {
            txtConsole.Text = string.Join(Environment.NewLine, visible);

            if (visible.Count > 0)
            {
                txtConsole.AppendText(Environment.NewLine);
            }

            if (chkAutoScroll.Checked)
            {
                txtConsole.SelectionStart = txtConsole.TextLength;
                txtConsole.ScrollToCaret();
            }
        }
        finally
        {
            // In a finally: a control left with its painting switched off
            // is a control that never draws again, which looks like the app
            // has frozen.
            NoRedraw.End(txtConsole);
        }
    }
}
