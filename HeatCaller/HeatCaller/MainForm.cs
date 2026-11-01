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

    /// <summary>
    /// This month's allowances (UsageMeters) asked for again every five
    /// minutes - the relay's and ElevenLabs' figures - so the sidebar chip,
    /// the pages and Mission Control stay near the truth through a long
    /// night without a request per spoken line. See MainForm.Usage.
    /// </summary>
    private readonly System.Windows.Forms.Timer _usageRefreshTimer = new() { Interval = 5 * 60 * 1000 };

    /// <summary>The allowances at the foot of the sidebar ("AI 47% · Voice 34%") - see UsageChip and MainForm.Usage.</summary>
    private readonly UsageChip _usageChip = new();

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

    /// <summary>
    /// Tonight's club notes - the asides that are not about the racing.
    ///
    /// One instance, here, because two things read it and one writes it:
    /// Race Control's notes window edits it, the announcer's director asks
    /// it for the next one to say, and the web routes add to it. A second
    /// instance would be a second copy of the file in memory, and a note
    /// approved in the window would never reach the announcer.
    /// </summary>
    private readonly ClubNotes _clubNotes;

    /// <summary>
    /// Tonight's room vote - one instance, for the same reason the notes
    /// are one: the window the race director opens it from, the Wi-Fi
    /// route the phones vote through and the announcer that reads the
    /// verdict must all be looking at the same list of votes.
    /// </summary>
    private readonly RoomVote _roomVote;

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
    /// Entries, Driver Hub, an import, a new race day. Hooking all of
    /// them would be six couplings that have to be remembered every time a
    /// seventh appears; a two-second republish of a list this size costs
    /// nothing and cannot be forgotten.
    /// </summary>
    private readonly System.Windows.Forms.Timer _webRosterTimer = new() { Interval = 2000 };

    // --- Driver experience hooks (22 Oct 2026) ---------------------------
    //
    // WHY (22 Oct 2026, driver experience): nine packages built the driver
    // features at the same time, so each owns a MainForm part file of its
    // own and joins in only through these. A partial method nobody has
    // implemented compiles to nothing, call and all. Each has exactly one
    // implementation, in the file named beside it.

    /// <summary>
    /// What the desk has answered for one badge's requests, for the phone
    /// (driverlive asks). Set by MainForm.DriverRequests.cs; read by
    /// MainForm.DriverLive.cs. Null until requests are wired. Called from
    /// the UI thread.
    /// </summary>
    private Func<string, IReadOnlyList<DriverAskAnswer>>? _askAnswersFor;

    // Once, at start-up, after the web server and the relay publisher are
    // made and wired - BEFORE the pages are built (PopulateTabs), so
    // _raceControlControl and _checkInControl are not there yet.
    partial void WireDriverRequests();   // MainForm.DriverRequests.cs
    partial void WireDriverLive();       // MainForm.DriverLive.cs
    partial void WireWiFiExtras();       // MainForm.WiFiExtras.cs
    partial void WireCaptions();         // MainForm.Captions.cs
    partial void WirePrivacy();          // MainForm.Privacy.cs
    partial void WireNightEmail();       // MainForm.NightEmail.cs

    // Every two seconds on the UI thread, on _webRosterTimer, after the
    // check-in roster and the relay extras have gone.
    partial void RosterTickPrivacy();        // MainForm.Privacy.cs
    partial void RosterTickDriverLive();     // MainForm.DriverLive.cs
    partial void RosterTickCaptions();       // MainForm.Captions.cs
    partial void RosterTickDriverRequests(); // MainForm.DriverRequests.cs

    // Each time a race is filed or corrected, from PublishDriverNightsFor
    // (MainForm.Relay.cs), whatever the relay switches say - the Wi-Fi
    // pages want it too.
    partial void RaceFiledWiFiExtras(SessionRecord session); // MainForm.WiFiExtras.cs
    partial void RaceFiledDriverLive(SessionRecord session); // MainForm.DriverLive.cs

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
    /// under were reachable only through Driver Hub, then a button,
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

    /// <summary>
    /// Held in a field so it can be taken off AdBreak's static event at
    /// shutdown. A static event holding a handler that closes over this
    /// window keeps the whole window alive after it has closed, and in the
    /// test run it would reach across from one test to the next.
    /// </summary>
    private Action<bool>? _adBreakHandler;

    /// <summary>Held for the same reason <see cref="_adBreakHandler"/> is, on ShowOnAir's static event.</summary>
    private Action<bool>? _showOnAirHandler;

    /// <summary>How long speaking has to last before the music and crowd duck - see the SpeakingChanged handler.</summary>
    private const int DuckOnsetDelayMs = 60;
    private readonly CommentaryService _commentary;
    private readonly DirectDecoderClient _decoderClient = new();
    private readonly ILapClient _ilapClient = new(AppPaths.Root);
    private readonly System.Windows.Forms.Timer _decoderAutoConnectRetryTimer = new() { Interval = 3000 };
    private readonly AnnouncementService _announcer;

    /// <summary>
    /// How this laptop asks its relay for the HeatCaller voice. One for the
    /// app, shared by the announcer and Voice Settings, so the page and the
    /// race are always talking about the same club and the same allowance.
    /// </summary>
    private readonly HeatCallerVoiceClient _heatCallerVoice;

    // WHY (23 Oct 2026, timing layer, P3): the app holds the race through
    // RaceTiming, its one front door, never the engine itself - so P4 can
    // put the engine on a thread of its own without touching the callers.
    //
    // WHY (23 Oct 2026, timing layer, P4): made in the constructor, straight
    // after the settings are read, because "Run timing on its own thread"
    // (AppSettings.TimingOnItsOwnThread) decides which kind it is: inline
    // (the default - exactly as before) or on the timing thread, _timingHost.
    private readonly RaceTiming _race;

    /// <summary>The timing thread, when "Run timing on its own thread" is on; null otherwise. Disposed at close - see FormClosing.</summary>
    private readonly TimingHost? _timingHost;

    /// <summary>"Run timing on its own thread" - AppSettings.TimingOnItsOwnThread, on the Connection page. Takes effect at the next start.</summary>
    private readonly CheckBox chkTimingThread = new ThemedCheckBox();
    private readonly CheckBox chkRecordPasses = new ThemedCheckBox();
    private readonly System.Windows.Forms.Timer _raceTickTimer = new() { Interval = 500 };

    /// <summary>When the clock of a timed race passes a mark worth calling - see TimeLeftCalls.</summary>
    private readonly TimeLeftCalls _timeLeftCalls = new();

    /// <summary>
    /// Whether the race on track is one the clock ends, for the time-left
    /// calls (1 Nov 2026) - see AnnouncerControl.ClockEndsTheRace. Before
    /// the announcer's page is built there is nothing to read the leader
    /// from, and the session's own rule (a time limit is set) stands.
    /// </summary>
    private bool ClockEndsTheRace()
    {
        try
        {
            return _announcerControl?.ClockEndsTheRace() ?? true;
        }
        catch
        {
            return true;
        }
    }
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

    /// <summary>
    /// The Recording tab, built with Sounds (its show music and the night's
    /// pieces are wired to Imaging and the announcer there) and hosted on the
    /// Club Website page since 26 Oct 2026 - see BuildClubWebsitePage.
    /// </summary>
    private TabPage _recordingSubPage = null!;

    /// <summary>The Club Website page's tabs (26 Oct 2026): Website, then Recording.</summary>
    private TabControl _tabClubWebsiteSubNav = null!;
    private TabControl _tabAnalyticsSubNav = null!;
    private TabPage _soundEffectsSubPage = null!;

    /// <summary>
    /// The Race Calls tab (Announcements until 17 Oct 2026), held for the
    /// same reason the one above is: RebuildVoiceAndSoundTabs has to swap
    /// what is inside it - and move the rivalries at its foot across.
    /// </summary>
    private TabPage _voiceAnnouncementsSubPage = null!;

    /// <summary>
    /// The Voice tab (Voice Settings until 17 Oct 2026), held for the same
    /// reason (17 Oct 2026). It
    /// was found as the Voice page's first tab, which is true only for as
    /// long as nobody reorders the tabs.
    /// </summary>
    private TabPage _voiceSettingsSubPage = null!;

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

    /// <summary>
    /// The app's crash-journal writer and race filing (23 Oct 2026, timing
    /// layer, P2) - made in WireRaceFiling before the pages, and handed to
    /// Race Control. See MainForm.RaceJournal.
    /// </summary>
    private RaceJournalRecorder _journalRecorder = null!;
    private RaceFilingService _raceFiling = null!;

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

    /// <summary>
    /// How well each transponder has been read, night by night - so one that
    /// is fading is noticed before it drops out of a race. See
    /// TransponderHealthStore and MainForm.MissionControl.
    /// </summary>
    private readonly TransponderHealthStore _transponderHealth = TransponderHealthStore.Load();

    /// <summary>Where each car is on the track, for the live track map - see TrackPositions and MainForm.TrackMap.</summary>
    private readonly TrackPositions _trackPositions = new();

    /// <summary>The webcam photo finish - which crossings the camera page should keep. See PhotoFinish and MainForm.PhotoFinish.</summary>
    private readonly PhotoFinish _photoFinish = new();
    private SoundEffectsControl _soundEffectsControl = null!;

    /// <summary>
    /// The decoder chosen on the Connection page, as the number it is saved
    /// under (DecoderConnection.EasyLap or ILap) - not the picker position,
    /// which stopped matching it when Next Level Timing was retired. Compare
    /// this, never cmbConnectionType.SelectedIndex.
    /// </summary>
    private int SelectedDecoder => DecoderConnection.ValueAt(cmbConnectionType.SelectedIndex);

    // The eighteen stores and services this window owns are assigned in the
    // constructor below, every one of them unconditionally, between its lines
    // 616 and 926 - no branch, no early return, no try. The compiler reports
    // CS8618 for all eighteen anyway ("must contain a non-null value when
    // exiting constructor"), because this constructor is about 1,490 lines
    // long and Roslyn's nullable flow analysis gives up on a method that
    // size and reports the fields it could not follow.
    //
    // So this is the compiler failing to see an assignment, not a field that
    // can really be null, and that is why the warning is turned off for this
    // one constructor rather than fixed at the eighteen declarations. The
    // alternative - "= null!" on each of them - would also silence every
    // GENUINE null warning about those fields for ever after, and would tell
    // the next reader they can be null, which they cannot: a window with a
    // null _announcer or _webServer has never existed and could not start.
    //
    // Scoped to the constructor on purpose. If you add a field here that is
    // NOT assigned in the constructor, you want to hear about it - so keep
    // the restore immediately after the closing brace, and never widen this
    // to the file.
#pragma warning disable CS8618
    public MainForm()
    {
        // Settings (specifically DarkMode) must be loaded before
        // InitializeComponent, since the Designer now builds the sidebar
        // and every page using AppTheme colors that depend on CurrentMode.
        _settings = AppSettings.Load();

        // The race, straight after the settings and before anything that
        // could use it. Threaded only with the setting on and a UI context to
        // deliver the race's events through (WinForms installs one as the
        // window is made, before this runs); otherwise inline, as always.
        if (_settings.TimingOnItsOwnThread && SynchronizationContext.Current is { } uiContext)
        {
            _timingHost = new TimingHost(log: SessionLog.Write);
            _race = new RaceTiming(new RaceControlSession(), _timingHost, uiContext);
            SessionLog.Write("[timing] Timing runs on its own thread (Connection page: \"Run timing on its own thread\").");
        }
        else
        {
            _race = new RaceTiming(new RaceControlSession());
        }

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

            // Driver Profiles became a driver's whole page - their cars,
            // transponders and setups as well as the profile - so its name
            // says that now.
            ["Driver Profiles"] = "Driver Hub",
        };

        if (_settings.NavOrder.Concat(_settings.HiddenTabs).Any(renamedPages.ContainsKey))
        {
            _settings.NavOrder = _settings.NavOrder.Select(n => renamedPages.GetValueOrDefault(n, n)).ToList();
            _settings.HiddenTabs = _settings.HiddenTabs.Select(n => renamedPages.GetValueOrDefault(n, n)).ToList();
            _settings.Save();
        }

        // Pages folded into another, whose names are simply dropped rather
        // than renamed: Batteries became the Batteries tab on Driver Hub
        // and Club Batteries on the Garage. Renamed to Driver Hub, a club
        // that had hidden Batteries would have had Driver Hub hidden too.
        var foldedPages = new[] { "Batteries" };

        if (_settings.NavOrder.Concat(_settings.HiddenTabs).Any(n => foldedPages.Contains(n, StringComparer.Ordinal)))
        {
            _settings.NavOrder = _settings.NavOrder.Where(n => !foldedPages.Contains(n, StringComparer.Ordinal)).ToList();
            _settings.HiddenTabs = _settings.HiddenTabs.Where(n => !foldedPages.Contains(n, StringComparer.Ordinal)).ToList();
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

        // Setups logged before setups belonged to cars, linked to the car
        // they were for wherever that is certain. Once. The rest wait on
        // the Garage page's Setups Without a Car. See SetupLinking.
        SetupLinking.LinkOnce(_settings, _setupLog, _profiles, _cars);

        // And from now on as they arrive: a driver named from the
        // new-transponder prompt, added on Driver Hub, imported,
        // signed up on a phone or entered on Race Day gets their car in the
        // Garage straight away. See ProfileStore.CarsAdded for why it is
        // handed only the NEW names.
        _profiles.CarsAdded += OnProfileCarsAdded;

        // The other direction: a car renamed, retired, deleted or given to
        // somebody else in the Garage reaches the driver's profile - the
        // names they are announced in. Once now, for car lists that drifted
        // before this version kept them in step (what anyone is announced
        // in is left alone - nothing has just changed), then on every change.
        // Not into an empty garage: a garage.json lost or never written
        // would leave every driver with just their active car.
        if (_cars.All.Count > 0)
        {
            GarageSync.SyncAll(_profiles, _cars, Array.Empty<CarChange>());
        }

        _cars.Changed += OnCarsChanged;

        // A row's car on the board, in the calls and in the results: the
        // car the transponder heard is fitted to, when its driver has said.
        _profiles.RaceCarName = row => GarageSync.FittedCar(_race.HeardAs(row), _profiles, _cars)?.DisplayName;

        // A driver with a second car carries two transponders. Both the race
        // and the unknown-transponder watch ask the profiles which driver an
        // alternate belongs to, so a lap from the second car is filed under
        // the driver's main transponder instead of appearing as a stranger.
        _race.AlternateTransponderOwner = _profiles.PrimaryForAlternate;
        _transponderWatch.AlternateTransponderOwner = _profiles.PrimaryForAlternate;

        // WHY (23 Oct 2026, timing layer, P0): when asked for, every pass and
        // race command is recorded for playing back (see TimingRecorder). Off
        // by default; with it off the race is given no recorder and nothing
        // changes. The file is written on a thread of its own, and a file
        // that can't be opened just means no recording.
        //
        // WHY (24 Oct 2026): it can now be switched on and off from the
        // Connection page while HeatCaller runs (StartPassRecording /
        // StopPassRecording in MainForm.TimingThread.cs), so the closing
        // handler is always there.
        if (_settings.RecordTimingPasses)
        {
            StartPassRecording();
        }

        FormClosed += (_, _) => StopPassRecording();

        _voiceStore = VoiceSettingsStore.Load();

        // Straight after both loads, before anything can make or save a
        // secret: whether this laptop could open the keys it was given (a
        // restore onto a new PC can't), and where a backup gets its
        // password-locked copy of them from.
        SecretRecovery.RememberStartupState(_settings, _voiceStore);
        BackupService.SecretsProvider = () => BackupSecrets.Capture(_settings, _voiceStore);

        // An update the club answered "After tonight" to goes in HERE. This
        // start IS the next start it was waiting for, and it is the safest
        // moment there is: nothing is open, no race is running, nobody is
        // looking at a screen. Velopack replaces this process when it
        // applies, so nothing below this line runs at all.
        //
        // The position is exact, and both halves of it matter. AFTER the two
        // lines above, because the backup taken before an update has to
        // carry the sealed secrets, and SecretsProvider is what gives them
        // to it - taken any earlier, the one backup standing between a club
        // and a bad release would come out without the relay token or the
        // PINs in it. BEFORE PopulateTabs, because the alternative is
        // building thirty pages and then throwing the process away.
        //
        // The log goes to the session log, not the console: the console is
        // part of a window that does not exist yet. WireUpdates swaps it for
        // the console once there is one.
        AppUpdates.Log = SessionLog.Write;

        // No marshalling here: this is the UI thread, in the constructor,
        // with nothing else running yet. WireUpdates replaces this with the
        // version that marshals, for the checks that happen later on a
        // background thread.
        AppUpdates.BackUp = () => BackupService.CreateBackup(
            BackupService.SafeBackupFolder(_settings.AutoBackupFolder),
            automatic: false,
            BackupService.CaptureSecrets());

        // Not "nothing is open" by definition after all: the app also
        // restarts mid-meeting (a new look, a crash), so the held update is
        // asked the same three things as the live question. The race days
        // are already loaded (a field), and a file that could not be read
        // counts as a day being open - it cannot be told that one is not.
        // Only when something is held, so an ordinary start does not read
        // the journal here as well as where it is offered back.
        //
        // WHY (20 Oct 2026): "UpdateWaitingForNextStart": null in a
        // hand-edited settings.json threw here and the app would not open.
        // A null now reads as nothing held. (AppSettings.NormaliseCollections
        // is where the other null strings get repaired.)
        if (_settings.UpdateWaitingForNextStart is { Length: > 0 }
            && AppUpdates.ApplyWhatWasHeldForNextStart(
                _settings,
                raceDayOpen: _raceEvents.LoadedUnreadable || _raceEvents.Active is { IsLegacy: false, IsOpen: true },
                raceState: _race.State,
                unfinishedRaceInJournal: RaceJournal.Unfinished() is not null))
        {
            return;
        }
        _soundStore = SoundEffectsStore.Load();
        _trackLayouts = TrackLayoutStore.Load();
        _sessionHistory = SessionHistoryStore.Load();

        // Battery tags from before they were per driver: the ones on a
        // session one driver ran alone become that driver's, and a pack
        // only ever tagged for one driver becomes theirs. Once - here,
        // straight after the history it reads is loaded. See
        // BatteryPackStore.ConvertOnce.
        _batteryPacks.ConvertOnce(_sessionHistory, _profiles);

        _raceClasses = RaceClassStore.Load();

        // What a class with no format of its own runs under, read fresh each
        // time - a sanctioning preset edits a copy of it (see
        // RaceClassStore.AppDefaultFormat). Hung on the store so both places
        // a class is edited get it.
        _raceClasses.AppDefaultFormat = () => RaceFormat.FromSettings(_settings);
        _raceClasses.Handicaps = _handicaps;
        _rounds = RoundDefinitionStore.Load();
        _themeStore = ThemeStore.Load();
        _seasonStore = SeasonStore.Load();
        _rivalryStore = RivalryStore.Load();
        _venueStore = VenueStore.Load();
        _checkInStore = CheckInStore.Load();
        _daySchedule = DayScheduleStore.Load();
        _sponsors = SponsorStore.Load();
        _clubNotes = ClubNotes.Load();
        _roomVote = RoomVote.Load();

        // THE NOTES AND THE VOTE ARE PUT ON THE DAY HERE, not only when the
        // director next gathers - that runs only with commentary on and a
        // day open, so a note typed or sent in before it was filed under
        // last week's day and wiped when the gather came round. On the
        // way up with no day open this clears last week's (ClubNotes.
        // NewMeeting adopts what is added under no day when one opens),
        // and a day opened or switched to puts them on that day at once.
        // Closing a day changes nothing, so a close undone keeps tonight's.
        // WHY (23 Oct 2026, bug hunt 10): only an open day moves the notes
        // and the vote. Race Day's picker makes a closed day current just to
        // look at its results; treating that as a new meeting wiped
        // tonight's open room vote and club notes for good, and a restart
        // while viewing one now keeps what is filed on a day still open.
        _clubNotes.StartUp(_raceEvents.Active, _raceEvents.Get);
        _roomVote.StartUp(_raceEvents.Active, _raceEvents.Get);

        _raceEvents.CurrentChanged += (_, _) =>
        {
            _clubNotes.FollowDay(_raceEvents.Active);
            _roomVote.FollowDay(_raceEvents.Active);
        };

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
        var classRenameCascade = new ClassRenameCascade(_profiles, _rounds, _seasonStore, _techInspections, _raceEvents, _daySchedule, _cars);
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

        // Whether the race being called is timed or run to a lap count, so
        // no live line counts laps in a race the clock ends (1 Nov 2026).
        // From the grid to the flag only - between races there is no race
        // to describe, and practice runs a clock only for its ring.
        _commentary.RaceLimitsNow = () =>
            (_race.State is RaceState.Staging or RaceState.Countdown or RaceState.Racing) && !_race.IsPracticeMode
                ? (_race.LapLimit, _race.TimeLimit)
                : null;

        _relayPublisher = new RelayPublisher(_settings);

        // From the publisher's own thread; OnClientRawMessage takes lines
        // from any thread, and holds them until the window is up.
        _relayPublisher.Log = OnClientRawMessage;

        // Drivers' own race clips go up through it (22 Oct 2026). (It was
        // the podium's highlight videos first, until 28 Oct 2026, when they
        // started staying on this PC.)
        RelayPublisher.ForClips = _relayPublisher;

        // Where a shout-out collected from the club's page is filed - the
        // same single ClubNotes instance the announcer reads from and the
        // Wi-Fi route writes to, so all three agree. The publisher files
        // on its own thread; ClubNotes writes its own file and nothing
        // here touches a control.
        _relayPublisher.TonightsNotes = () => _clubNotes;

        // And tonight's vote, which the publisher both PUBLISHES a ballot
        // from and reads votes into - see RelayPublisher.Vote. The same
        // single instance again, so a vote from the club's web page lands
        // in the list the announcer reads its verdict from.
        _relayPublisher.TonightsVote = () => _roomVote;

        // WHY (20 Oct 2026): when the vote closes (the green flag, or by
        // hand) the closed ballot goes up at once rather than on the next
        // fifteen-second tick, so a late phone is told "closed". Safe from
        // any thread - WakeRoomVote only sets a number and a wake-up.
        _roomVote.Closed += _relayPublisher.WakeRoomVote;

        // Said once, at the start: why the relay, the voice relay or the PIN
        // emails are quiet after a restore, in the log as well as on the
        // pages that switch them back on. See RestoredSettings.
        if (RestoredSettings.StartupNote() is { } restoredNote)
        {
            OnClientRawMessage(restoredNote);
        }

        // Where this run's data is, when it is the rehearsal copy - and what
        // a rehearsal does differently. See Rehearsal.
        if (Rehearsal.StartupNote() is { } rehearsalNote)
        {
            OnClientRawMessage(rehearsalNote);
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
            // once this has said it was applied. Waited for rather than just
            // queued, so that "applied" means applied. Safe to block here:
            // this runs on the publisher's own thread, never the UI thread.
            _relayPublisher.CheckInReceived += relayTap =>
            {
                if (_closingDown || IsDisposed || Disposing || !IsHandleCreated)
                {
                    return false;
                }

                try
                {
                    // 17 Oct 2026, Bug Hunt 8, A32-2: posted and waited for
                    // against _closingDown rather than a blocking Invoke.
                    // Closing joins this thread (RelayPublisher.Dispose) while
                    // the UI thread no longer runs the posted call, so an
                    // Invoke in flight held the exit for two seconds. Still
                    // "applied means applied": the result is only true once
                    // the UI thread has run it; a call left behind by closing
                    // checks _closingDown first and applies nothing.
                    var pending = BeginInvoke(new Func<bool>(() => !_closingDown && ApplyRelayCheckIn(relayTap)));

                    while (!pending.AsyncWaitHandle.WaitOne(100))
                    {
                        if (_closingDown || IsDisposed)
                        {
                            return false;
                        }
                    }

                    return EndInvoke(pending) is true;
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
            // A result pasted in from another program's results page (1 Nov
            // 2026) is history to look at, not a race run tonight: nothing
            // below may follow from it - no relay page, no standings, no
            // intro written ahead, no night-before preview taken down.
            if (record.IsImported())
            {
                return;
            }

            PublishSessionToRelay(record, firstPublish: true);
            PublishResultsIndexFor(record);
            PublishStandingsToRelay();

            // AND EACH OF ITS DRIVERS THEIR OWN NIGHT, while the club
            // offers them - see PublishDriverNightsFor. Here rather than
            // anywhere earlier for the reason everything else in this
            // handler is here: a FILED result is the first moment the
            // night a document describes is true.
            PublishDriverNightsFor(record);

            // A finished session is when badges are earned; read them
            // afresh on the next pass rather than waiting out the cache -
            // and the badge list with them, so a badge not on offer where
            // the club races, earned anyway, is on the website's list too.
            _relayAchievementsReadUtc = DateTime.MinValue;
            _lastRelayDocuments.Remove(RelayChannel.Badges);

            // AND THE NEXT ROUND'S INTRO IS WRITTEN NOW, so the start it
            // belongs to waits for no model - see WrittenAhead.
            //
            // HERE, AND THIS IS THE ONE PLACE IT MAY GO. A result having
            // been filed is what makes the next intro's championship and
            // form figures true; written any earlier - at the green of the
            // race that was just run, which is what the plan originally
            // proposed - the held line would state a points total the race
            // then changed. See RaceControlControl.WriteTheNextIntroAhead.
            //
            // Hooked to the store rather than to the places that save, for
            // the same reason everything above it is.
            //
            // With the record, so the round it was run for is passed over:
            // this is raised before that round is marked as run.
            _raceControlControl?.WriteTheNextIntroAhead(record);

            // AND THE NIGHT-BEFORE PREVIEW COMES OFF THE CLUB'S PAGE, for
            // the same reason and in the same place: a FILED result is the
            // first moment the app can be sure the meeting is under way
            // rather than being set up. A green flag can be a test and a
            // programme can be built and abandoned; a filed session
            // happened. Idempotent after the first - see
            // WithdrawTheNightBefore.
            //
            // WHY (20 Oct 2026): with the record, so only a race from one of
            // the open day's own rounds takes the preview down - a midweek
            // practice or a test run saved while Saturday is open used to.
            WithdrawTheNightBefore(record);
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

            // A correction changes what a driver's night SAYS - a position,
            // a lap count, whether they won it - so their document is
            // built again from the corrected results, exactly as the
            // results and the standings are. Without this a driver would
            // go on holding the evening the result used to say they had.
            PublishDriverNightsFor(record);

            // The index carries the label, the track and the winner, so a
            // corrected result changes it too - without this, the archive
            // page and every link preview would go on showing the name of
            // whoever the result USED to say had won.
            PublishResultsIndexFor(record);

            // A correction can change the points and the form the next
            // round's intro, written ahead, states - so it is thrown away
            // and written again from the corrected results.
            _raceControlControl?.WriteTheNextIntroAhead(record, corrected: true);

            // A correction that took off the lap holding the layout's
            // all-time record - a phantom crossing, say - leaves that record
            // standing, from a lap that no longer exists, and no real lap can
            // beat it. A layout keeps no earlier record to go back to, so it
            // is said here, with where to clear it, rather than guessed at.
            foreach (var track in _trackLayouts.All)
            {
                if (ResultEditor.RecordLapTakenOff(record, track) is { } holder)
                {
                    OnClientRawMessage($"{AppTime.LogStampNow()}  [race] {track.Name}'s track record ({holder.DriverName}, {track.RecordLapMs / 1000.0:0.000}s) "
                                       + "was set on a lap this correction has taken off. It still stands - clear it on Track Layouts if it should go.");
                }
            }
        };

        _webServer = new WebServerService(_race, _profiles, _daySchedule, _sponsors, _settings);
        _webServer.StatusMessage += OnClientRawMessage;

        // Shout-outs from a phone in the room - see
        // WebServerService.Notes. The switch is read on every request
        // rather than captured, so turning it on takes effect without
        // restarting the server; the store is the same single instance the
        // announcer reads from, so an approved note actually goes out.
        _webServer.TonightsNotes = () => _clubNotes;
        _webServer.NotesFromTheRoomOpen = () => _settings.ClubNotesFromTheRoom;

        // And the room's vote, the same way and for the same reasons: the
        // setting is read on each request rather than captured, and the
        // store is the one instance the announcer reads the verdict from.
        _webServer.TonightsVote = () => _roomVote;
        _webServer.VotingFromTheRoomOpen = () => _settings.RoomVoteFromTheRoom;

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

        // The driver experience packages (22 Oct 2026) - see the hooks by
        // _webRosterTimer. Here, with the server and publisher wired and
        // before the roster timer starts, so the first tick finds them ready.
        WireDriverRequests();
        WireDriverLive();
        WireWiFiExtras();
        WireCaptions();
        WirePrivacy();
        WireNightEmail();

        _webRosterTimer.Tick += (_, _) =>
        {
            // Always, whatever is switched on: the desk's USB scanner has to
            // recognise an online badge even on a night the relay is off.
            RebuildBadgeIds();

            // Before the roster goes out, so a racer added a moment ago goes
            // with their PIN in the same publish.
            KeepRacerPinsInStep();

            // A transponder on the "no driver" banner that has been named
            // some other way since - by hand, a phone sign-up, an import.
            DropNamedUnknownTransponders();

            // Also when the local server is off. The relay is a separate
            // audience: a club publishing online without running the LAN
            // pages would otherwise never send a roster at all.
            if (_webServer.IsRunning || _settings.RelayEnabled)
            {
                PublishCheckInRoster();
            }

            PublishExtrasToRelay();

            // 22 Oct 2026 (driver experience). Privacy first, so the
            // register the others redact by is this tick's.
            RosterTickPrivacy();
            RosterTickDriverLive();
            RosterTickCaptions();
            RosterTickDriverRequests();
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
        _announcer.AnnounceGhostsProvider = () => _ghostStore.Current.AnnounceGhosts;

        // How chatty the club wants it, asked on demand for the same
        // reason as the two around it: the dial is on the Commentary page
        // and moving it mid-race has to take effect at the next crossing.
        // See AnnouncementService.ChatterProvider.
        _announcer.ChatterProvider = () => _settings.CommentaryChatter;

        // And how much ROOM it leaves between them, which is a different
        // question from how many it makes - see AppSettings.AnnouncerDensity
        // for the report that made it two dials instead of one. Asked on
        // demand for the same reason as the chattiness dial beside it.
        _announcer.DensityProvider = () => _settings.AnnouncerDensity;

        // What the race in front of it is worth, so the gap is wide in
        // practice and tight in the top main. The same expression the
        // Announcer page's own RaceSize uses (see MainForm.Pages) rather
        // than a second reading of the programme: two answers to "what is
        // this race worth" is how the announcer ends up pacing a heat like
        // a main.
        _announcer.StatureProvider = () => RaceStature.Of(_activeRound, _rounds, _raceEvents.ActiveRoundScope());

        // Qualifying calls (provisional pole, improvements) fire only when
        // the session was started as a qualifier - asked on demand, same as
        // practice, so it can never go stale.
        _announcer.QualifyingModeProvider = () => _race.StartType == RaceStartType.Qualifier;

        // THE BROADCAST FEATURES (18 Oct 2026). The sponsors who present a
        // moment ("the fastest lap, presented by"), read fresh each time so
        // a sponsor sold mid-evening counts from their next moment; the
        // moments for the stream's banner; and the lead change count for
        // the pylon, from the one tracker the voice uses so the two agree.
        // See SponsoredMoments, BroadcastMomentFeed and LeadTracker.
        _announcer.SponsorsProvider = () => _sponsors.Current.Sponsors.ToList();
        _announcer.MomentHappened += moment => _webServer.Moments.Post(moment);
        //
        // Threaded, through a frozen feed (24 Oct 2026, timing layer, P8):
        // the live state is built off the UI then, and the count is read here
        // on the UI tick - see MainForm.LiveFeed.
        _webServer.LeadChanges = LiveFeed<(int Changes, int Leaders)>(() => (_announcer.Leads.Changes, _announcer.Leads.Leaders));

        // A written commentary segment carries the sponsor of its moment (a
        // battle watch, the battles' sponsor), rationed like the calls.
        // WHY (20 Oct 2026): the mention is no longer counted here, as it is
        // asked - CommentaryService runs the Count once the model has
        // actually written a line, so a segment that came back empty no
        // longer goes in the sponsor report. (A segment the director writes
        // and then drops is still counted - not worth a second path back.)
        if (_commentary is not null)
        {
            // With the give-back (22 Oct 2026, bug hunt): a segment that comes
            // back empty returns the ration to the announcer's own calls.
            _commentary.SegmentSponsorTakenWithRelease = kind =>
                SponsoredMoments.ForSegment(kind) is { } moment ? _announcer.TakeSponsorWithRelease(moment) : null;

            // The sponsor's tagline, said after every credit HeatCaller puts
            // on a written line - the intros, the recap, a segment (21 Oct
            // 2026). Looked up by the name the credit carries.
            _commentary.SponsorTagline = _announcer.TaglineOf;
        }

        // The board, the lap-detail popup and the race log ask the same
        // question the announcer does - how many clean laps make a streak -
        // so they now ask the same place. Resolved on demand rather than
        // pushed in, because practice has its own threshold and the session
        // is shared between the two.
        //
        // Threaded (23 Oct 2026, timing layer, P4), the race asks on the
        // timing thread, which must not read the sound store the UI edits: it
        // answers the two figures last read on the UI thread instead - see
        // FrozenStreakThreshold.
        if (_race.IsThreaded)
        {
            RefreshFrozenStreakThresholds();
            _race.StreakThresholdProvider = FrozenStreakThreshold;
        }
        else
        {
            _race.StreakThresholdProvider = () => _soundStore.Current.StreakThresholdFor(_race.IsPracticeMode);
        }

        // The final-lap shout and the one-minute warning: the session raises
        // them, the announcer says them.
        //
        // WHY (23 Oct 2026, timing layer, P4b): a last-lap shout the window is
        // only now being told of, seconds late after a freeze (threaded), is
        // not said - see RaceTiming.DeliveringIsStale. Never so inline.
        _race.LastLapStarted += standing =>
        {
            if (InvokeRequired) { TryPost(() => _announcer.NotifyLastLap(standing.TransponderId)); return; }
            if (_race.DeliveringIsStale) { _announcer.WhileLapCallsAreStale(() => _announcer.NotifyLastLap(standing.TransponderId), _race.DeliveringAgeMs); return; }
            _announcer.NotifyLastLap(standing.TransponderId);
        };

        // Only when the clock is what ends the race (1 Nov 2026): in a race
        // with a lap limit too, "one minute to go" is wrong when the laps
        // will run out first - see ClockEndsTheRace.
        _race.FinalMinute += () =>
        {
            if (InvokeRequired) { TryPost(() => { if (ClockEndsTheRace()) _announcer.NotifyFinalMinute(); }); return; }
            if (ClockEndsTheRace()) _announcer.NotifyFinalMinute();
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

        // A sponsor video with its own sound is on the Big Screen: the music
        // and the crowd bed go silent under it until it finishes. The Big
        // Screen says when (AdBreak) rather than being handed the music -
        // it is built by Race Control and by Practice Mode, neither of which
        // has the player.
        //
        // No onset delay, unlike the announcer's duck above: that delay is
        // there so a line dropped before it speaks doesn't dip the music for
        // nothing, and an ad break is never that short.
        _adBreakHandler = playing =>
        {
            _ambience.SetAdBreakMuted(playing);
            _music.SetAdBreakMuted(playing);
        };

        AdBreak.PlayingChanged += _adBreakHandler;

        // The show's own opener and bed are playing under a segment: the
        // club's playlist goes silent for the duration, because two pieces
        // of music at once is a mess and the bed is the one the words were
        // written over. See ShowOnAir and ShowTreatment.
        //
        // The crowd is deliberately left alone. It is not music, and crowd
        // noise under a broadcast open is the atmosphere the segment is
        // there to sell; it ducks under the voice as it always has.
        _showOnAirHandler = playing => _music.SetShowMuted(playing);

        ShowOnAir.PlayingChanged += _showOnAirHandler;

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

        // This month's allowances, before the pages that show them are built
        // - see MainForm.Usage.
        WireUsage();

        // Which relay a HeatCaller line was rendered on is part of its saved
        // key. Voice Settings sets this too as it is built, but only when it
        // is first opened - set it here as well so a race called before that
        // looks its saved lines up under the right relay.
        VoiceLibrary.VoiceRelaySettings = _settings;

        // Every page built with its layout held, and every page but the one
        // on screen still held afterwards - see LayoutFreeze. Let go in a
        // finally: a page left holding its layout would stay blank. Timed
        // for the session log, which is how a slow start gets found.
        var building = System.Diagnostics.Stopwatch.StartNew();
        _startupClock = System.Diagnostics.Stopwatch.StartNew();
        LayoutFreeze.BeginBuilding();

        // Before the pages: Race Control is handed the filing service, and
        // the journal's handlers go on the session ahead of the page's - see
        // WireRaceFiling.
        WireRaceFiling();

        try
        {
            PopulateTabs();
        }
        finally
        {
            LayoutFreeze.EndBuilding(
                _navItems.Select(item => (Control)item.Page),
                _navItems.FirstOrDefault(item => item.Name == _shownPage)?.Page);
        }

        StartupMark("letting go");

        OnClientRawMessage($"{AppTime.LogStampNow()}  [startup] Pages built in {building.Elapsed.TotalSeconds:0.0}s"
            + (_startupMarks.Count > 0 ? " - " + string.Join(", ", _startupMarks.Select(mark => $"{mark.Part} {mark.Seconds:0.0}s")) : string.Empty)
            + ".");

        _startupClock = null;

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
        _decoderClient.LapEventReceived += OnEasyLapPass;

        // The I-Lap path ends in the same handler as EasyLap's - one lap
        // pipeline whatever the hardware at the gate. Whichever decoder is
        // the split loop goes to the split loop instead (RoutePass).
        _ilapClient.StatusChanged += OnILapStatusChanged;
        _ilapClient.RawMessage += OnClientRawMessage;
        _ilapClient.LapEventReceived += OnILapPass;

        WireSplitLoop();

        // Watches for timing going quietly wrong mid-race - a decoder gone
        // quiet, nothing crossing, a car not seen. See TimingWatch.
        WireTimingWatch();

        // The heartbeat clock Mission Control reads - see MainForm.MissionControl.
        WireMissionControl();

        // The live track map: following the race's crossings - see MainForm.TrackMap.
        WireTrackMap();

        // The phone coach page's report cards, for a laptop restarted mid-evening - see MainForm.Coach.
        PublishCoachCards();

        // A target lap set or cleared reaches the phone coach page at once,
        // not at the driver's next lap - see MainForm.Coach.
        WatchDriverTargets();

        WirePhotoFinish();

        _decoderAutoConnectRetryTimer.Tick += (_, _) =>
        {
            // Only while EasyLap is the decoder chosen. The box stays ticked
            // when somebody switches to I-Lap, and this used to go on
            // opening the EasyLap every three seconds regardless - two
            // decoders feeding one race.
            //
            // Connecting is deliberately NOT in the list below, and that is
            // what guards the three-second tick now Connect is asynchronous:
            // a connect still waiting on the driver leaves the state at
            // Connecting, so this tick lets it be rather than asking for
            // another one on top of it every three seconds.
            // (DirectDecoderClient refuses a second one anyway - see its
            // _connecting - but the state is what keeps the console and the
            // buttons honest.)
            if (chkAutoConnect.Checked
                && RoleOf(DecoderConnection.EasyLap) != DecoderRole.None
                && _decoderClient.State is DecoderConnectionState.Disconnected or DecoderConnectionState.Error)
            {
                OnClientRawMessage($"{AppTime.LogStampNow()}  [auto-connect] Retrying...");

                // The line's buttons only when the EasyLap is the line - an
                // EasyLap split loop has its own (RenderSplitStatus).
                if (RoleOf(DecoderConnection.EasyLap) == DecoderRole.Line)
                {
                    btnConnect.Enabled = false;
                    btnDisconnect.Enabled = true;
                }

                _decoderClient.Connect();
            }

            // An I-Lap waited for at start-up, whose port has not appeared
            // yet - see TryStartupConnect.
            TryStartupConnect(firstTry: false);

            // And an I-Lap split loop waiting for its port the same way.
            TryConnectWaitingSplitLoop();
        };
        _decoderAutoConnectRetryTimer.Start();
        chkAutoConnect.Checked = _settings.DirectDecoderAutoConnect; // fires CheckedChanged, which connects if already in EasyLap mode

        // Read once at start (see the constructor), so a change is saved for
        // the next start and says so on the page.
        chkTimingThread.Checked = _settings.TimingOnItsOwnThread;
        chkTimingThread.CheckedChanged += (_, _) =>
        {
            if (_settings.TimingOnItsOwnThread == chkTimingThread.Checked)
            {
                return;
            }

            _settings.TimingOnItsOwnThread = chkTimingThread.Checked;
            _settings.Save();
            OnClientRawMessage($"{AppTime.LogStampNow()}  [timing] \"Run timing on its own thread\" turned {(chkTimingThread.Checked ? "on" : "off")} - it takes effect the next time HeatCaller starts.");
        };

        // The pass recording, switched straight away (24 Oct 2026) - the
        // recorder is handed to the race through RaceTiming, which is safe
        // in either timing mode.
        chkRecordPasses.Checked = _settings.RecordTimingPasses;
        chkRecordPasses.CheckedChanged += (_, _) =>
        {
            if (_settings.RecordTimingPasses == chkRecordPasses.Checked)
            {
                return;
            }

            _settings.RecordTimingPasses = chkRecordPasses.Checked;
            _settings.Save();

            if (chkRecordPasses.Checked)
            {
                StartPassRecording();
            }
            else
            {
                StopPassRecording();
                OnClientRawMessage($"{AppTime.LogStampNow()}  [timing] Pass recording stopped.");
            }
        };

        // Set before it is wired, so reading the setting in is not taken for
        // somebody changing it. Acted on once the window is up - see
        // ConnectAtStartup, called from RaiseReady.
        chkConnectAtStartup.Checked = _settings.ConnectDecodersAtStartup;
        chkConnectAtStartup.CheckedChanged += (_, _) =>
        {
            _settings.ConnectDecodersAtStartup = chkConnectAtStartup.Checked;
            _settings.Save();

            // Unticked while still waiting for the I-Lap's port: stop waiting.
            if (!chkConnectAtStartup.Checked)
            {
                _startupConnectPending = false;
            }
        };

        // Race control's own logic (time limits, lap counting, etc.) runs
        // continuously regardless of whether the Race Control window is
        // even open, so a race in progress isn't affected by someone
        // closing that window mid-session.
        _raceTickTimer.Tick += (_, _) =>
        {
            // Threaded (23 Oct 2026, timing layer, P4), the race's clock
            // ticks on the timing thread itself, every 100 ms, whatever this
            // window is doing - so here only the lookups it reads are brought
            // up to date. Everything else in this tick is as it was.
            if (_race.IsThreaded)
            {
                RefreshFrozenLookups();
            }
            else
            {
                _race.Tick();
            }

            // The time left in a timed race (28 Oct 2026): "five minutes to
            // go", and every fifteen seconds of the last minute. After the
            // tick, so inline the session's own final minute has gone first.
            // Not under a red flag, where the race clock is stopped anyway.
            if (_timeLeftCalls.Next(
                    _race.RaceStartUtc,
                    _race.TimeLimit,
                    _race.ElapsedRaceTime,
                    timedRace: _race.State == RaceState.Racing && !_race.IsPracticeMode && _race.Flag != FlagState.Red) is { } secondsLeft
                && ClockEndsTheRace())
            {
                _announcer.NotifyTimeLeft(secondsLeft);
            }

            // A crossing the journal's throttle held back, written at most a
            // tick late whether or not Race Control is there to do it (23 Oct
            // 2026, timing layer, P2) - see RaceJournalRecorder.OnClockTick.
            //
            // WHY (23 Oct 2026, timing layer, P4b): threaded, the journal
            // catches up on the timing tick itself, and the timing side never
            // reads the screen - so here the screen's details (track, round,
            // name, re-run) are brought up to date for it instead, twice a
            // second, which is what the inline write read at every write.
            if (_race.IsThreaded)
            {
                _journalRecorder.CaptureDetails();
            }
            else
            {
                _journalRecorder.OnClockTick();
            }

            UpdateTimingWatch();

            // At most once a minute, and only when a pass has changed it.
            _transponderHealth.SaveIfDue();

            // The live page's state document is built HERE, on the UI
            // thread, and served from a published copy - see
            // WebServerService.PublishState. A request thread must never
            // walk the standings while the lap pipeline is writing to them.
            //
            // WHY (24 Oct 2026, timing layer, P8): threaded, it is built off
            // the UI on a timer of its own (MainForm.LiveState), so the
            // phones and the relay stay live through a freeze; here only what
            // that build reads from this window is brought up to date.
            if (_race.IsThreaded)
            {
                RefreshFrozenLiveInputs();
            }
            else
            {
                _webServer.PublishState();
            }
        };
        _raceTickTimer.Start();

        // Threaded only: the live state and the relay's frame, off the UI.
        StartLiveStateOffUi();
        _race.LogMessage += msg => OnClientRawMessage($"[race] {msg}");

        // Checked once now and again every hour, so a long-running session
        // (a whole race day left open) still gets backed up on schedule
        // rather than only ever checking at the moment the app happens to
        // launch.
        CheckAutoBackup();
        _autoBackupCheckTimer.Tick += (_, _) => CheckAutoBackup();
        _autoBackupCheckTimer.Start();

        // The startup steps that may open a dialog, run in order once the
        // window is up - see the Ready handler after the last of them.
        var startupSteps = new List<Action>
        {
            // The re-enter-your-keys message, first: noted while the window
            // was built (ShowDroppedSecrets), shown once it can be seen.
            ShowDroppedSecrets,
        };

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
        startupSteps.Add(() =>
        {
            if (SecretRecovery.RunIfNeeded(this, _settings, _voiceStore))
            {
                SafeFile.Freeze();
                Application.Restart();
                Environment.Exit(0);
            }
        });

        startupSteps.Add(() =>
        {
            // settings.json was there but could not be read, so these are
            // the defaults and nothing will be saved this session - see
            // AppSettings.LoadedUnreadable. The defaults have never been
            // through the wizard, but this is not a first run: offering it
            // would have the club set everything up again for nothing to be
            // kept. Said plainly instead, since every page is showing the
            // defaults rather than the club's own choices.
            if (_settings.LoadedUnreadable)
            {
                AppDialog.Show(
                    this,
                    "HeatCaller couldn't read its settings file this time, so it has started with the standard settings "
                    + "and won't save any changes to them. Your own settings and keys are still in the file - nothing has "
                    + "been changed or lost.\r\n\r\nClose anything that might have it open (a backup or sync tool is the "
                    + "usual cause), then restart HeatCaller.",
                    "Settings couldn't be read",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

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

            // The look step offers the sound too, applied through this form -
            // see MainForm.SoundOffer.cs.
            using var wizard = new FirstRunWizard(_settings, _trackLayouts, _raceClasses, _profiles, MakeSoundOfferHost());
            wizard.ShowDialog(this);

            // "Don't ask about the sound again" may have been ticked.
            _themesControl?.ShowAskTick();

            // Mark it done even if dismissed with the X - the wizard is a
            // convenience, and re-showing it every launch to someone who
            // closed it deliberately would be an annoyance, not a help.
            _settings.HasCompletedFirstRun = true;

            // A new look only takes effect on a fresh start. The tour, if
            // it was asked for, waits for the restart so it shows the app
            // in the colours that were picked.
            // 17 Oct 2026, Bug Hunt 8, A32-3: not when "Restore a backup
            // instead" was asked for - the restart returned before the
            // OpenBackups branch below and the request was lost. The new
            // look is saved and shows on the next start (a restore restarts
            // the app anyway).
            if (wizard.RestartForLook && !wizard.OpenBackups)
            {
                _settings.ShowTourOnNextStart = wizard.TakeTour;
                _settings.Save();
                SafeFile.Freeze();
                RestartForNewLook();
                return;
            }

            _settings.Save();

            // 22 Oct 2026: the wizard's last step can keep untested pages out
            // of the sidebar - shown now, not at the next start.
            RebuildNav();

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
            foreach (var name in new[] { "Driver Hub", "Classes", "Track Layouts" })
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
        });

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
        startupSteps.Add(() =>
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

                // Threaded, practice may start at the gate from now on (24
                // Oct 2026, timing layer, P7). Never throws.
                RefreshPracticeAutoStartPlan();
            }
        });

        // All of the above as ONE posted call, run in order. Each step used
        // to be posted on its own, but a modal dialog keeps pumping messages,
        // so the next posted step ran INSIDE the dialog before it: the
        // backup-password prompt, the wizard and the race-recovery offer
        // could open on top of each other, and a key recovery's restart
        // could cut off a message still on screen underneath it. One at a
        // time, each step's dialogs close before the next begins.
        //
        // A step that throws is reported and the rest still run, as they did
        // when each was posted separately - the race recovery step in
        // particular must always run (see _recoveryDecided).
        Ready += (_, _) => BeginInvoke(new Action(() =>
        {
            foreach (var step in startupSteps)
            {
                if (IsDisposed || Disposing)
                {
                    return;
                }

                try
                {
                    step();
                }
                catch (Exception ex)
                {
                    OnClientRawMessage($"{AppTime.LogStampNow()}  [startup] A startup step failed: {ex.GetType().Name}: {ex.Message}");
                }
            }
        }));

        // Synchronous, all of it. This was an async handler, and the first
        // await returned control to WinForms, which carried on closing: the
        // disconnect it was waiting on (Next Level Timing's, when that was
        // offered) then ran against a window already being torn down, and
        // the session in progress was never saved at all.
        FormClosing += (_, e) =>
        {
            // WHY (20 Oct 2026): WinForms arrives here with e.Cancel already
            // true when a control fails validation (a bad Points cell on
            // Championship, say). The close is then refused and the window
            // stays open - but this handler used to carry on and stop the
            // timing, the decoders, the web server and the relay anyway,
            // leaving an app that looked alive and timed nothing. Now a
            // person closing it is left to fix the cell and nothing is torn
            // down; Windows shutting down is not held up by a typo.
            if (e.Cancel)
            {
                if (e.CloseReason == CloseReason.UserClosing)
                {
                    return;
                }

                e.Cancel = false;
            }

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

            // The live state built off the UI (threaded only), stopped and
            // waited for before anything it reads - the web server, the relay,
            // the stores, the timing thread - is taken down (24 Oct 2026,
            // timing layer, P8).
            StopLiveStateOffUi();

            // Off AdBreak's static event before the audio goes - see
            // _adBreakHandler.
            if (_adBreakHandler is not null)
            {
                AdBreak.PlayingChanged -= _adBreakHandler;
                _adBreakHandler = null;
            }

            if (_showOnAirHandler is not null)
            {
                ShowOnAir.PlayingChanged -= _showOnAirHandler;
                _showOnAirHandler = null;
            }

            // Closing can take a while (saving, stopping the decoder), and
            // the window stops answering pings as it goes - which is not a
            // freeze.
            HangWatchdog.Stop();

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
                // Somebody closing the window is there to be told if the
                // practice could not be saved; Windows shutting down is not.
                _practiceControl?.StopForShutdown(personIsThere: e.CloseReason == CloseReason.UserClosing);
            }
            catch
            {
                // Best effort - closing must not be stopped by a failed save.
            }

            // Tonight's transponder readings, not left to the once-a-minute save.
            try
            {
                _transponderHealth.Save();
            }
            catch
            {
                // Best effort - a few minutes of readings are all that is lost.
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

            // Race Mode always has the window maximized and borderless, so
            // closing from it (Alt+F4, the taskbar, Windows logging off)
            // would record "maximized" for someone who actually runs the app
            // in a window, and they would lose that choice next launch. The
            // state they had before pressing F11 is the one that is theirs.
            var windowState = _raceMode ? _preRaceModeState : WindowState;

            // 17 Oct 2026, Bug Hunt 8, A32-1: closed while minimised (from
            // the taskbar, at logoff) says nothing about maximised, so the
            // choice saved last time stands - see StartMaximizedFor.
            if (StartMaximizedFor(windowState) is { } startMaximized)
            {
                _settings.StartMaximized = startMaximized;
            }

            // WHY (23 Oct 2026, bug hunt 10): a PIN email run or night send
            // still going at close was never recorded (a closing window takes
            // no posts), so the same people were emailed again next start.
            // Both stop the send and record who it reached, before this save.
            RecordPinMailOnClose();
            RecordNightEmailsOnClose();

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
            _usageRefreshTimer.Stop();
            _pinMailCancel.Cancel();
            _webServer.Stop();

            // Ghost cars run on a timer of their own and feed laps into the
            // race; left running they log into a closing window.
            _ghosts?.Stop();

            _relayPublisher.Dispose();
            _decoderClient.Dispose();
            _ilapClient.Dispose();

            // The timing thread, last of the timing (23 Oct 2026, timing
            // layer, P4): after practice was stopped and filed, the journal
            // flushed, the ghosts stopped and both decoders closed, so nothing
            // is left to hand it a pass. What it still holds runs first; from
            // then on the race is read on the calling thread (see
            // TimingHost.Dispose), so the window's own teardown still can.
            _timingHost?.Dispose();

            // Releases the ambience output device - it runs on its own
            // WaveOut independent of the announcement audio, so nothing
            // else's teardown covers it.
            _ambience.Dispose();
            _music.Dispose();
        };
    }
#pragma warning restore CS8618

    /// <summary>
    /// Raised once, after the window has finished painting for the first
    /// time and has been made visible. The startup screen waits for this
    /// rather than for Shown.
    /// </summary>
    public event EventHandler? Ready;

    /// <summary>Plain-text keys found in settings.json and discarded, to tell the club about once the window is up - see ShowDroppedSecrets.</summary>
    private List<string>? _droppedSecrets;

    /// <summary>
    /// The first startup step (see the Ready handler in the constructor):
    /// says which keys were found in plain text and removed, so they are
    /// typed in again. Shown here rather than where they were found, while
    /// the window was still transparent and the TopMost startup screen was
    /// up - the dialog sat underneath that screen and the app looked hung.
    /// </summary>
    private void ShowDroppedSecrets()
    {
        if (_droppedSecrets is not { Count: > 0 } dropped)
        {
            return;
        }

        _droppedSecrets = null;

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
        // The layout test shows this window only to measure it. Nothing that
        // showing it starts - the Shown handlers (web server, relay, the page
        // sweep), the reveal, the startup questions that follow Ready - is
        // wanted there, and most of it would reach outside the test.
        if (LayoutCheckMode)
        {
            return;
        }

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

        // HeatCaller's icon by the clock - see MainForm.Tray.cs.
        StartTrayIcon();

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

    /// <summary>
    /// Asks the UI thread something from another one and waits for the
    /// answer, or gives back <paramref name="whenGone"/> if the window
    /// cannot take the question. The waiting twin of <see cref="TryPost"/>,
    /// with the same four checks and the same catch.
    ///
    /// Written for AppUpdates (see WireUpdates), which called Invoke
    /// directly for all three of the things it asks this window - with no
    /// check and no catch - from its own background thread, two minutes
    /// after launch and every six hours after that. One of those landing
    /// while the window was closing threw ObjectDisposedException on a
    /// thread nobody was watching and took the app down on its way out:
    /// exactly what TryPost was written for, at the one door that still
    /// had none of it.
    /// </summary>
    private T TryAsk<T>(Func<T> ask, T whenGone)
    {
        if (_closingDown || IsDisposed || Disposing || !IsHandleCreated)
        {
            return whenGone;
        }

        try
        {
            return Invoke(ask);
        }
        catch (InvalidOperationException)
        {
            // The handle went between the check and the call - see TryPost.
            return whenGone;
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

        // Already in the session log - OnClientRawMessage wrote them there
        // when they arrived.
        foreach (var line in lines)
        {
            ShowConsoleLine(line);
        }
    }

    /// <summary>
    /// Hands AppUpdates the three things only this window can answer - what
    /// the app is doing, how to ask a question, and how to make a backup -
    /// and starts the slow check. See AppUpdates for the rules; this is only
    /// the wiring.
    ///
    /// The marshalling is the point. AppUpdates calls all of this from a
    /// background thread, and race state and windows belong to this one.
    /// </summary>
    private void WireUpdates()
    {
        if (!AppUpdates.Available(_settings))
        {
            // Not an installed build (a developer running from bin\Debug, or
            // a portable copy), or the club turned checks off. Nothing is
            // wired, nothing is downloaded, and nothing is said - an app
            // that cannot update itself has no business mentioning updates.
            return;
        }

        // The console in place of the session log now there is a window to
        // show it in - and the log file still gets everything, because
        // OnClientRawMessage writes to both.
        AppUpdates.Log = line => OnClientRawMessage($"{AppTime.LogStampNow()}  {line}");

        // All three go through TryAsk rather than Invoke: this window may be
        // closing when the updater's background thread asks, and a bare
        // Invoke then throws on that thread with nobody to catch it. Each
        // one's fallback is the cautious answer - nothing is in progress is
        // answered "something is", which holds the update back, and the
        // question nobody can be shown is answered "not now".
        // A race-days file that could not be read counts as a day being
        // open, the same as the held-update check at start-up does: it
        // cannot be told that one is not.
        AppUpdates.NothingInProgressNow = () => TryAsk(() => AppUpdates.NothingInProgress(
            raceDayOpen: _raceEvents.LoadedUnreadable || _raceEvents.Active is { IsLegacy: false, IsOpen: true },
            raceState: _race.State,
            unfinishedRaceInJournal: RaceJournal.Unfinished() is not null), whenGone: false);

        AppUpdates.Ask = (newVersion, thisVersion) => TryAsk(() => (AppUpdates.Answer)AppDialog.Choose(
            this,
            AppUpdates.Question(newVersion, thisVersion),
            "A new version of HeatCaller",
            MessageBoxIcon.Information,
            defaultIndex: (int)AppUpdates.Answer.AfterTonight,
            AppUpdates.AnswerLabels), whenGone: AppUpdates.Answer.NotNow);

        // On this thread for the same reason: the settings are this thread's
        // to change and to save. A window going away is a promise not kept,
        // and AppUpdates says so.
        AppUpdates.HoldForNextStart = newVersion => TryAsk(() =>
        {
            _settings.UpdateWaitingForNextStart = newVersion;
            return _settings.Save();
        }, whenGone: false);

        // "Update now" ends the process from inside Velopack, so FormClosing
        // never runs. Its last saves are made here first, on this thread,
        // the same way it makes them - then everything is frozen, as before
        // a restore, so nothing lands after them - and only then the
        // install, which restarts into the new version.
        AppUpdates.RunInstallOnUiThread = install => TryAsk(() =>
        {
            try
            {
                _transponderHealth.Save();
            }
            catch
            {
                // Best effort - a few minutes of readings are all that is lost.
            }

            try
            {
                _raceControlControl.FlushJournal();
            }
            catch
            {
                // Best effort - the journal from a moment ago still stands.
            }

            // Not "maximized" just because Race Mode is - see FormClosing.
            var windowState = _raceMode ? _preRaceModeState : WindowState;

            // Not "not maximized" just because it is minimised (17 Oct 2026,
            // Bug Hunt 8, A32-1) - see StartMaximizedFor.
            if (StartMaximizedFor(windowState) is { } startMaximized)
            {
                _settings.StartMaximized = startMaximized;
            }

            // WHY (23 Oct 2026, bug hunt 10): FormClosing never runs here, so
            // a PIN email run or night send under way is recorded now - see
            // FormClosing.
            RecordPinMailOnClose();
            RecordNightEmailsOnClose();

            _settings.Save();

            SafeFile.Freeze();
            install();
            return true;
        }, whenGone: false);

        // The secrets have to be taken on this thread (BackupSecrets.Capture
        // reads the live settings, which this thread is free to be changing);
        // the zip itself is then free to run wherever it was called from, and
        // it is the slow part.
        AppUpdates.BackUp = () =>
        {
            // Both reads in one hop, guarded like the two above. A backup
            // that could not be taken has to THROW rather than hand back
            // something that looks like a path: AppUpdates.TryBackUp catches
            // it and refuses to install anything without one.
            var taken = TryAsk<(string Folder, BackupSecrets? Secrets)?>(
                () => (BackupService.SafeBackupFolder(_settings.AutoBackupFolder), BackupService.CaptureSecrets()),
                whenGone: null);

            if (taken is not { } ready)
            {
                throw new InvalidOperationException(
                    "HeatCaller is closing down, so the backup before the update could not be taken.");
            }

            return BackupService.CreateBackup(ready.Folder, automatic: false, ready.Secrets);
        };

        // Not at startup: two minutes in, so an update check is never part
        // of why the app was slow to open, and never the thing a club is
        // waiting on while they are trying to start a race night.
        _updateCheck = new System.Windows.Forms.Timer { Interval = (int)AppUpdates.FirstCheckAfter.TotalMilliseconds };

        _updateCheck.Tick += (_, _) =>
        {
            // The first tick is the short one; every tick after it is the
            // slow interval.
            if (_updateCheck is { } timer && timer.Interval != (int)AppUpdates.CheckEvery.TotalMilliseconds)
            {
                timer.Interval = (int)AppUpdates.CheckEvery.TotalMilliseconds;
            }

            // Started and forgotten. AppUpdates refuses to run two at once,
            // and says nothing at all when there is no internet.
            _ = AppUpdates.CheckAsync(_settings);
        };

        _updateCheck.Start();
    }

    /// <summary>The slow update check - see WireUpdates. Null on a build that cannot update itself.</summary>
    private System.Windows.Forms.Timer? _updateCheck;

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
        //
        // Only noted here; the dialog itself waits until the window has been
        // revealed and the startup screen has gone - see ShowDroppedSecrets. Shown
        // from here, it opened while this window was still fully transparent
        // and the TopMost startup screen was still up, so it sat underneath
        // that screen and the app looked as if it had hung on startup.
        List<string>? droppedSecrets = null;

        if (_settings.DroppedPlainTextSecrets.Count > 0)
        {
            droppedSecrets = _settings.DroppedPlainTextSecrets.Distinct(StringComparer.Ordinal).ToList();
            _settings.DroppedPlainTextSecrets.Clear();

            OnClientRawMessage($"{AppTime.LogStampNow()}  [secrets] Plain-text {string.Join(", ", droppedSecrets)} found in settings.json and discarded - re-enter and they will be sealed.");
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

        if (SessionLog.FilePath is { } logFile)
        {
            OnClientRawMessage($"{AppTime.LogStampNow()}  [log] This run's console is also being saved to {logFile}");
        }

        // From here on, a window that stops answering for ten seconds is
        // noted in that file, with a dump of the frozen app beside it. See
        // HangWatchdog.
        HangWatchdog.Start(this, OnClientRawMessage);

        // The re-enter-your-keys message noted above is shown by the first
        // of the startup steps Ready runs (ShowDroppedSecrets), once this
        // window is visible and the startup screen has gone.
        _droppedSecrets = droppedSecrets;

        // The decoders, by themselves, now the window is up and its log can
        // be read - see ConnectAtStartup.
        ConnectAtStartup();

        // Updates, quietly, from here on - see WireUpdates and AppUpdates.
        WireUpdates();

        Ready?.Invoke(this, EventArgs.Empty);
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
        //
        // Nor when settings.json was there but could not be read this
        // launch (held by a sync or backup tool, say). The settings in use
        // are then only the defaults - backups on, never made, the default
        // folder, keep 14, no backup password - so the club's real choices
        // were ignored: a backup it had switched off was made anyway, with
        // no password-locked copy of the keys, and pruning to 14 deleted
        // older backups the club meant to keep, some of them holding the
        // keys. Nothing can be saved this session either, so the backup
        // waits for a launch that reads the real settings.
        if (!_settings.AutoBackupEnabled || SecretRecovery.Pending || _settings.LoadedUnreadable)
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
                var cloudResults = CloudBackup.Copy(path, clouds, keep);
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

                // What the cloud copies did, written down for the cloud
                // backup light to say later - see CloudBackupStatus. The
                // console lines below used to be the only record, and they
                // had scrolled away long before anyone needed them. Not
                // from the rehearsal copy, which deliberately copies
                // nothing: its "not copied" lines are not a fault to show.
                if (!AppPaths.IsRehearsal)
                {
                    CloudBackupStatus.Record(_settings, task.Result.Cloud, DateTime.UtcNow);
                    _cloudBackupLight = null;
                }

                _settings.Save();

                OnClientRawMessage($"{AppTime.LogStampNow()}  [backup] Automatic backup created: {Path.GetFileName(task.Result.Path)}");

                foreach (var outcome in task.Result.Cloud)
                {
                    OnClientRawMessage($"{AppTime.LogStampNow()}  [backup] {outcome.Line}");
                }
            },
            uiThread);
    }

    /// <summary>
    /// What closing in this window state says about "start maximised": null
    /// for minimised, which says nothing either way - closing from the
    /// taskbar or at Windows logoff used to save "don't start maximised"
    /// for someone who always runs it maximised (17 Oct 2026, Bug Hunt 8,
    /// A32-1).
    /// </summary>
    internal static bool? StartMaximizedFor(FormWindowState state) =>
        state switch
        {
            FormWindowState.Minimized => null,
            FormWindowState.Maximized => true,
            _ => false,
        };
}
