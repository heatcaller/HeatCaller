namespace HeatCaller;

/// <summary>
/// Standalone race control window: staging/countdown/flags, the race clock,
/// and the live leaderboard. Operates entirely on a shared RaceControlSession
/// instance owned by MainForm, so it works identically no matter which
/// connection is feeding it lap events (currently only the direct EasyLap
/// decoder actually drives RaceControlSession - Next Level Timing runs its
/// own physical race control and this app stays read-only for that path -
/// but this window itself has no idea which one it is, by design).
///
/// Completed races are saved to the same SessionHistoryStore used
/// elsewhere in the app, tagged as Race sessions.
/// </summary>
public sealed partial class RaceControlControl : UserControl
{
    private readonly RaceControlSession _race;
    private readonly AnnouncementService _announcer;
    private readonly ProfileStore _profiles;
    private readonly RaceClassStore _raceClasses;
    private readonly TrackLayoutStore _trackLayouts;
    private readonly SessionHistoryStore _sessionHistory;
    private readonly AppSettings _settings;
    private readonly DayScheduleStore? _daySchedule;
    private readonly SponsorStore? _sponsors;
    private readonly UndoService _undo;
    private readonly RoundDefinitionStore? _rounds;

    /// <summary>Supplies whichever race-program round is currently picked in the Round: box on the Race Control page (owned by MainForm), so a finished race can be tied back to its heat.</summary>
    private readonly Func<RoundDefinition?>? _activeRound;
    private readonly CommentaryService? _commentary;
    private readonly KpiTracker _kpiTracker = new();
    private List<KpiDefinition> _kpiColumns = new();

    /// <summary>Transponders already prompted about this session - so declining the "name this driver?" prompt doesn't make it reappear on every subsequent lap.</summary>
    private readonly HashSet<string> _promptedTransponders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Transponders heard this session that nobody has a profile for,
    /// waiting to be named once the racing stops.
    ///
    /// A list rather than a set, so they are offered in the order they
    /// first appeared; <see cref="_promptedTransponders"/> is what keeps
    /// each one out of here twice.
    /// </summary>
    private readonly List<string> _unnamedTransponders = new();

    /// <summary>The list of transponders nobody has - see UnknownTransponders. Null until MainForm attaches it.</summary>
    private UnknownTransponders? _unknowns;

    /// <summary>The banner above the board listing them, between races.</summary>
    private readonly UnknownTransponderBanner _unknownBanner = new();

    /// <summary>
    /// Connects the page to the app's list of unknown transponders: noted
    /// here as they cross the line, shown on the banner between races.
    /// </summary>
    public void AttachUnknownTransponders(UnknownTransponders unknowns)
    {
        _unknowns = unknowns;
        _unknownBanner.ShowWhen = () =>
            !_race.IsPracticeMode && _race.State is not (RaceState.Staging or RaceState.Countdown or RaceState.Racing);
        _unknownBanner.Attach(unknowns);
    }

    /// <summary>The flag before the current one - the only way to tell an initial green apart from a restart after a caution, since both leave the race in the Racing state.</summary>
    private FlagState _previousFlag = FlagState.None;

    /// <summary>
    /// Whether the race currently running is a simulation, captured when it
    /// starts. Read at completion time instead, this depended on handler
    /// order: the demo clears IsDemoMode inside its own RaceCompleted
    /// handler, so whether this control saw true or false came down to who
    /// subscribed first.
    /// </summary>
    private bool _currentRaceIsDemo;

    /// <summary>Set while restoring a snapshot: RestoreSnapshot re-raises StateChanged/FlagChanged, which would otherwise re-announce "race started" just for undoing a reset.</summary>
    private bool _suppressFlagAnnouncements;

    // Only refreshes the clock's displayed text - the actual race-ending
    // Tick() logic (time limits etc.) runs continuously in MainForm
    // regardless of whether this window is even open, so a race in
    // progress isn't affected by someone closing this window.
    private readonly System.Windows.Forms.Timer _clockRefreshTimer = new() { Interval = 250 };

    private DateTime? _sessionStartedAtUtc;

    /// <summary>
    /// The running race, written to disk as it happens.
    ///
    /// Everything else here can be done again; a race cannot. See
    /// <see cref="RaceJournal"/>.
    /// </summary>
    private readonly RaceJournal _journal = new();

    /// <summary>What the decoder has been hearing, for the pre-race check. Null when nothing is feeding it.</summary>
    private readonly TransponderWatch? _transponderWatch;

    // --- UI ---
    private readonly ComboBox _cmbTrackLayout = new();
    private readonly TextBox _txtSessionLabel = new();

    private readonly RaceClockPanel _clock;
    private readonly RaceStatStrip _statStrip;

    /// <summary>
    /// The narrowest the clock column down the right-hand side gets - the
    /// clock, the buttons under it and the stat tiles all share its width.
    /// Wider when a button's caption needs it (a larger text size). At the
    /// screen's DPI, as the clock's point-size text is.
    /// </summary>
    private static int ClockWidth => UiLayout.AtDpi(260);

    /// <summary>The clock's height. It is drawn, and drawn to fit whatever it is given. (The stat tiles under the column size themselves - see RaceStatStrip.) At the screen's DPI, as the clock's point-size text is.</summary>
    private static int ClockHeight => UiLayout.AtDpi(200);

    /// <summary>
    /// The page: the host's Session row (see ShowSessionRow), the Next Up
    /// bar, and the flags and the leaderboard beside the clock column,
    /// the leaderboard taking the height that is left. See BuildLayout.
    /// </summary>
    private readonly FillPage _page = new();

    /// <summary>
    /// The labels here that wear a face of their own - the bold captions and
    /// the small hints - and the face each wears.
    ///
    /// Everything else here takes the window's font, and follows it when
    /// the text size changes. These were given theirs outright, which is
    /// the old one once the size has changed, so they are handed the new
    /// one - see OnFontChanged.
    /// </summary>
    private readonly List<(Label Label, Func<Font> Face)> _ownFaces = new();

    /// <summary>
    /// The format in force for whatever is about to run: the active
    /// round's override, else its class's format, else the app default.
    ///
    /// Refreshed by ApplyEffectiveFormat - at build, when the page is
    /// shown, and again at the moment a start begins, because the person
    /// picking a different round between races must not race under the
    /// previous round's rules.
    /// </summary>
    private RaceFormat _fmt = new();

    /// <summary>The chip that says which format is in force and where it came from.</summary>
    private readonly Label _lblFormat = new();

    private readonly Button _btnFormat = new();
    private readonly Button _btnQuickRace = new();

    // --- The Next Up bar ---
    private readonly NextUpBar _nextUpBar = new();

    /// <summary>
    /// What the announcer is about to do, under the Next Up bar.
    ///
    /// Off the page until a club actually has commentary running - see
    /// AnnouncerStrip, which also explains why the race director needs
    /// this at all when everything else reports to the console.
    /// </summary>
    private readonly AnnouncerStrip _announcerStrip = new();

    /// <summary>
    /// The director, once MainForm has one. A function rather than the
    /// thing itself because the announcer screen builds it and this page
    /// is made first.
    /// </summary>
    public Func<CommentaryDirector?>? Commentary { get; set; }

    /// <summary>
    /// Whether the PA is saying anything right now, from the announcer's
    /// own counted signal.
    ///
    /// Volatile and nothing else: SpeakingChanged is raised from whichever
    /// thread finished a line, and the strip reads it on the UI tick a
    /// moment later. Marshalling it would mean a BeginInvoke per spoken
    /// line for a lamp that is redrawn once a second anyway.
    /// </summary>
    private volatile bool _speaking;
    private readonly Button _btnStartNext = new();
    private readonly Button _btnReseed = new();

    /// <summary>Throttle for the bar: rebuilt once a second on the clock tick, not four times.</summary>
    private DateTime _nextUpDueUtc = DateTime.MinValue;

    private readonly RaceEventStore? _events;
    private readonly CheckInStore? _checkIns;

    /// <summary>
    /// The championships, for the recap only. Optional like the rest of
    /// these: a club with no season set up gets a recap with no title race
    /// in it, which is the same recap it got before.
    /// </summary>
    private readonly SeasonStore? _seasons;

    /// <summary>
    /// The Tech Log, for the "not through tech" warning when a heat is
    /// staged. Optional like the stores above; see TechStoreForCheck for
    /// what happens when the host does not pass it.
    /// </summary>
    private readonly TechInspectionStore? _techInspections;

    /// <summary>Raised when Start Next is pressed with a round in hand - MainForm owns the session pickers, so it applies the round and triggers the start.</summary>
    public event Action<RoundDefinition>? NextRoundStartRequested;

    /// <summary>
    /// What the programme says the next thing to do is - the same reading
    /// Today's next-step card shows.
    ///
    /// Internal because ProgramStep is. Asked rather than worked out here:
    /// the bar used to decide "next" entirely on its own, which is how it
    /// came to offer Start Next on a main whose grid was about to change.
    /// </summary>
    internal Func<ProgramStep?>? NextStepProvider { get; set; }

    /// <summary>
    /// What is wrong with timing right now, or null - see TimingWatch. Shown
    /// first on the Next Up bar, under TIMING. Asked by the bar, worked out
    /// by the host, which owns the decoders.
    /// </summary>
    public Func<string?>? TimingProblem { get; set; }

    /// <summary>
    /// The live track map - the line of the layout being raced on and every
    /// car placed on it - for the Big Screen this page opens. Null leaves
    /// the Big Screen without one. See MainForm.TrackMap.
    /// </summary>
    public Func<(TrackLine? Line, IReadOnlyList<TrackPositions.Car> Cars)>? TrackMap { get; set; }

    /// <summary>
    /// What the Garage has on the winning car, for the mechanic in the
    /// recap's interview - see MechanicBrief. Given the saved race and the
    /// round it was run for (null for a freeform race). Worked out by the
    /// host, which owns the Garage; asked on the UI thread. Null leaves the
    /// mechanic with nothing to say about the car's setup.
    /// </summary>
    public Func<SessionRecord, RoundDefinition?, IReadOnlyList<string>>? MechanicSetupFacts { get; set; }

    /// <summary>Raised when the bar's button is offering a programme step rather than a start. The host runs it through the programme tab.</summary>
    internal event Action<ProgramStep>? RunProgramStepRequested;

    /// <summary>
    /// The step the bar's button is currently offering, or null when it is
    /// an ordinary Start Next. Held so the click runs exactly what the
    /// caption promised.
    /// </summary>
    private ProgramStep? _barStep;

    /// <summary>
    /// When the step above was last worked out.
    ///
    /// The bar refreshes four times a second and the reading behind it is
    /// not free - it walks the day's rounds, every class's roster and
    /// every driver profile. Once a second is far faster than anybody can
    /// finish a race, and it is only asked at all while nothing is running.
    /// </summary>
    private DateTime _barStepReadUtc = DateTime.MinValue;

    // The flag row paints itself - see FlagButton. Each shows its own
    // colour under the mouse and while it is in effect, and the ordinary
    // button colour otherwise.
    private readonly FlagButton _btnStage = new() { FlagColour = Color.FromArgb(47, 111, 237), FlagText = Color.White };
    private readonly FlagButton _btnCountdown = new() { FlagColour = Color.FromArgb(242, 120, 32), FlagText = Color.FromArgb(20, 20, 20) };
    private readonly FlagButton _btnGreen = new() { FlagColour = Color.FromArgb(31, 157, 85), FlagText = Color.White };
    // The start-sequence timings now come from the race format - see
    // EffectiveFormat.
    private CancellationTokenSource? _autoStartCts;

    // The warm-up's timer, and what runs when it is over (Stage, or Quick
    // Race's whole sequence). See TryBeginWarmUp.
    private CancellationTokenSource? _warmUpCts;
    private Action? _afterWarmUp;

    /// <summary>
    /// True when the warm-up now open was opened by Quick Race, false when
    /// Stage opened it. Set each time one opens, and only read while one is
    /// open. Quick Race pressed during its OWN warm-up skips the rest of it;
    /// pressed during Stage's, it used to skip that one too - which ran
    /// Stage's continuation and staged the heat in the Round box under its
    /// own format, the opposite of the freeform race that was asked for.
    /// </summary>
    private bool _warmUpOpenedByQuickRace;

    /// <summary>True only while the warm-up's own continuation is running, so the Stage it calls doesn't open a second warm-up.</summary>
    private bool _warmUpJustFinished;
    private readonly FlagButton _btnYellow = new() { FlagColour = Color.FromArgb(240, 200, 40), FlagText = Color.Black };
    private readonly FlagButton _btnRed = new() { FlagColour = Color.FromArgb(204, 46, 77), FlagText = Color.White };
    private readonly FlagButton _btnCheckered = new() { Chequered = true };
    private readonly Button _btnReset = new();
    private readonly Button _btnBigScreen = new();

    /// <summary>
    /// Where the class would qualify if the running heat ended now.
    ///
    /// Built here rather than handed in, because everything it needs is
    /// already a field on this control - and it is cached, so the
    /// leaderboard, the Big Screen and the projection window all asking on
    /// the same tick costs one calculation between them.
    /// </summary>
    private QualifyingProjector? _projector;

    /// <summary>The qualifying projection, shared with Practice so a Big Screen opened there shows the Qual column too.</summary>
    internal QualifyingProjector? Projector => _projector;

    /// <summary>
    /// The badges as they are now. AwardAchievements replaces the store after
    /// every unlock, so a page that shows them reads this each time rather
    /// than keeping the store it was given.
    /// </summary>
    internal AchievementStore Achievements => _achievements;

    /// <summary>Badges were awarded somewhere else (a practice at home): the board, the Big Screen and the projections read them afresh.</summary>
    public void ReloadAchievements()
    {
        var store = AchievementStore.Load();

        if (!store.LoadedUnreadable)
        {
            _achievements = store;
        }
    }

    /// <summary>The projection window, when it is open. One at a time.</summary>
    private QualifyingProjectionForm? _projectionWindow;
    private readonly Button _btnReadiness = new();
    private readonly Button _btnAnnouncer = new();

    /// <summary>
    /// Opens the live qualifying projection.
    ///
    /// In the right-hand column with Big Screen, Pre-race check and
    /// Announcer, because that is the column of things that OPEN A WINDOW.
    /// It spent one build in the fix toolbar, which is captioned "for
    /// missed or double-counted passes, and on-track penalties" and is
    /// nothing to do with it - and being an eighth item in a row of seven
    /// pushed that strip onto a third line.
    /// </summary>
    private readonly Button _btnProjection = new();

    // Each flag action stored as a delegate. Triggers used to call
    // Button.PerformClick(), which silently does nothing when the control
    // isn't visible - and this page is hidden whenever the user is on any
    // other page. That quietly broke the phone remote, the Quick Race
    // button and the Open Big Screen button. Invoking the delegate runs the
    // same logic regardless of what's on screen.
    //
    // Stage and Countdown take one argument: whether the person has
    // ALREADY agreed to abandon a race that is running (the phone asks on
    // its own screen). False everywhere else, which makes them ask here.
    //
    // Stage also takes what the host needs to put back if the race it
    // abandons is restored with Undo - see CaptureAbandonedRaceUndo.
    private Action<bool, Action?>? _actStage;
    private Action<bool>? _actCountdown;
    private Action? _actGreen;
    private Action? _actYellow;
    private Action? _actRed;
    private Action? _actCheckered;
    private Action? _actReset;
    private Action? _actQuickRace;
    private Action<bool, Action?>? _actAutoStart;
    private Action? _actBigScreen;
    private readonly Label _lblRaceState = new();
    private readonly Label _lblFlag = new();

    private readonly DataGridView _dgvLeaderboard = new();

    /// <summary>Lets the host (MainForm) route messages from this control into the console on the Connection page, without this control needing direct access to MainForm itself.</summary>
    public event Action<string>? StatusMessage;

    /// <summary>
    /// Raised when everything a session had to say has been said - the
    /// recap included, or straight after the finish calls when there is no
    /// recap. This is the real end of the broadcast, which is when music
    /// should fade rather than at the checkered flag.
    /// </summary>
    public event Action? SessionAudioFinished;

    /// <summary>
    /// Raised with true while the AI recap is speaking, false when it ends.
    ///
    /// Separate from the normal speaking duck: the recap runs for half a
    /// minute, and a bed sitting at the usual announcement level competes
    /// with it the whole way through.
    /// </summary>
    public event Action<bool>? DeepDuckRequested;

    /// <summary>
    /// Raised when the track layout or session label changes here.
    ///
    /// These used to exist twice - once on the connection page and once
    /// here - which meant two fields for one value. The duplicates are
    /// hidden now, and these keep the saving code's copies in step.
    /// </summary>
    public event Action<string>? TrackLayoutChanged;

    public event Action<string>? SessionLabelChanged;

    /// <summary>Raised after a session is saved, so pages showing race-program state can refresh instead of waiting for navigation.</summary>
    public event Action? SessionSaved;

    /// <summary>
    /// Raised when the announcer's page is wanted.
    ///
    /// The control does not know how to get there - it does not know what
    /// pages exist, and should not. MainForm owns the navigation and wires
    /// this to it.
    /// </summary>
    public event Action? AnnouncerRequested;

    public RaceControlControl(RaceControlSession race, AnnouncementService announcer, ProfileStore profiles, RaceClassStore raceClasses, TrackLayoutStore trackLayouts, SessionHistoryStore sessionHistory, AppSettings settings, UndoService undo, DayScheduleStore? daySchedule = null, SponsorStore? sponsors = null,
        RoundDefinitionStore? rounds = null, Func<RoundDefinition?>? activeRound = null, CommentaryService? commentary = null,
        TransponderWatch? transponderWatch = null, RaceEventStore? events = null, CheckInStore? checkIns = null,
        SeasonStore? seasons = null, TechInspectionStore? techInspections = null)
    {
        _race = race;
        _transponderWatch = transponderWatch;
        _announcer = announcer;
        _profiles = profiles;
        _raceClasses = raceClasses;
        _trackLayouts = trackLayouts;
        _sessionHistory = sessionHistory;
        _settings = settings;
        _daySchedule = daySchedule;
        _sponsors = sponsors;
        _undo = undo;
        _clock = new RaceClockPanel(race);
        _statStrip = new RaceStatStrip(race);
        _rounds = rounds;
        _events = events;
        _checkIns = checkIns;
        _seasons = seasons;
        _techInspections = techInspections;
        _activeRound = activeRound;
        _commentary = commentary;

        _projector = new QualifyingProjector(
            race, profiles, raceClasses, sessionHistory, rounds, events, activeRound);

        // No scrolling, minimum size or font of its own. The page inside
        // (_page, a FillPage) scrolls when the window is too short for it,
        // and needs no minimum to fall back on. And the font is the
        // window's, so a new text size reaches everything here the way it
        // reaches every other page - see OnFontChanged.
        Dock = DockStyle.Fill;

        BuildLayout();
        WireRaceControl();

        // The journal says when it cannot write, and when it can again -
        // the one moment the race is running without its safety net is
        // worth a line in the console.
        _journal.StatusChanged += message =>
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] {message}");

        // Any voice line, not just a segment: somebody about to pick up
        // the mic cares that the speakers are busy, not whose line it is.
        _announcer.SpeakingChanged += speaking => _speaking = speaking;

        _clockRefreshTimer.Tick += (_, _) =>
        {
            UpdateClockDisplay();

            // Polled on the clock tick rather than a timer per car: this
            // already runs four times a second, which is close enough for a
            // release call, and it can't leak a timer into the next race.
            _race.PollReleases();

            // A crossing the journal's throttle held back is written here, at
            // most a tick late, rather than waiting for the next crossing or
            // flag - the last laps before a crash were lost that way. Not
            // forced: Record keeps its own throttle. See RaceJournal.IsBehind.
            if (_journal.IsBehind)
            {
                WriteJournal();
            }

            // The Next Up bar rides the same tick, throttled to once a
            // second - what it says changes on the scale of minutes.
            if (DateTime.UtcNow >= _nextUpDueUtc)
            {
                _nextUpDueUtc = DateTime.UtcNow.AddSeconds(1);
                UpdateNextUpBar();

                // On the same second. "Writing" is the state worth
                // catching and a line takes seconds to write, so once a
                // second is a warning rather than a record of one.
                _announcerStrip.Show(Commentary?.Invoke(), _settings.CommentaryEnabled, _speaking);
            }
        };
        _clockRefreshTimer.Start();

        // The one handler for the hidden track list (BuildLayout used to wire
        // a second). Wired after the first fill on purpose, so filling the
        // list at start-up does not write over the remembered tracks before
        // Session Setup has read its own.
        //
        // "(none)" is the list's placeholder, never a track name, so it is
        // stored as empty - as Session Setup's picker stores it. Both last-
        // track settings are kept, as the two handlers kept them: this page
        // reads its own back when the list is refilled, and
        // LastTrackLayoutName is what Session Setup and the first-run wizard
        // start from.
        RefreshTrackLayoutCombo();
        _cmbTrackLayout.SelectedIndexChanged += (_, _) =>
        {
            var selected = _cmbTrackLayout.SelectedItem as string;

            if (selected is not null)
            {
                TrackLayoutChanged?.Invoke(selected);
            }

            var trackName = selected is null or "(none)" ? string.Empty : selected;
            _settings.LastRaceControlTrackName = trackName;
            _settings.LastTrackLayoutName = trackName;
            _settings.Save();
        };
    }

    /// <summary>
    /// Keyboard-shortcut entry points (see MainForm's KeyDown handler) -
    /// each just performs the same click as the corresponding button, so
    /// there's exactly one place the actual logic lives.
    /// </summary>
    /// <param name="confirmedOverRunningRace">
    /// True only when the person has already agreed, somewhere else, to
    /// abandon a race that is running - the phone remote's "yes, really".
    /// Otherwise a running race is asked about here before it is wiped.
    /// </param>
    /// <param name="restoreHostOnUndo">
    /// Puts back the host's own session setup - the round, track and label
    /// it changed for the new race - if the race this Stage abandons is
    /// restored with Undo. Captured by the host BEFORE it changed them.
    /// Ignored when no running race is abandoned.
    /// </param>
    public void TriggerStage(bool confirmedOverRunningRace = false, Action? restoreHostOnUndo = null) =>
        _actStage?.Invoke(confirmedOverRunningRace, restoreHostOnUndo);

    /// <param name="confirmedOverRunningRace">As for <see cref="TriggerStage"/>.</param>
    public void TriggerCountdown(bool confirmedOverRunningRace = false) => _actCountdown?.Invoke(confirmedOverRunningRace);
    public void TriggerGreen() => _actGreen?.Invoke();
    public void TriggerYellow() => _actYellow?.Invoke();
    public void TriggerRed() => _actRed?.Invoke();
    public void TriggerCheckered() => _actCheckered?.Invoke();
    public void TriggerReset() => _actReset?.Invoke();

    /// <summary>
    /// True while <see cref="RunRemoteCommand"/> is running a command from
    /// the phone remote. The refusals it can meet on the way record why (see
    /// NoteRemoteRefusal), and practice's refusal is said to the phone rather
    /// than put up in a box on the laptop.
    /// </summary>
    private bool _runningRemoteCommand;

    /// <summary>Why the phone's command was not carried out, or null while nothing has refused it.</summary>
    private string? _remoteRefusal;

    /// <summary>
    /// Runs a race-control command from the phone remote and says what
    /// became of it, for the phone to show.
    ///
    /// The phone used to be told "sent" as soon as the command had been
    /// handed to this thread, whatever Race Control then did with it - a
    /// Stage refused during practice (behind a box on the laptop that nobody
    /// there had asked for), or a Stage over a race that had gone green since
    /// the phone last looked, still read as sent. This runs exactly what the
    /// buttons and the function keys run - the Trigger methods above - and
    /// listens for the refusals on the way.
    ///
    /// UI thread only. Returns once the command has been dealt with. A green
    /// from the start is requested here and drops when the countdown call
    /// has finished, as every green does - so "race on" is what happens next
    /// unless somebody calls it off in the meantime, which the log then says.
    /// A failure inside the command is reported to the phone and the console
    /// rather than thrown.
    /// </summary>
    /// <param name="action">stage, countdown, green, yellow, red, checkered or reset - anything else is refused.</param>
    /// <param name="confirmedOverRunningRace">
    /// The phone has asked its user whether to abandon the running race and
    /// been told yes (its force=1). Without it, a Stage or Countdown over a
    /// running race is not run: the phone is asked instead (NeedsConfirm) and
    /// sends the command again with this set on a yes. The laptop does not
    /// ask on the phone's behalf.
    /// </param>
    /// <param name="confirmedRaceGeneration">
    /// The race the phone's yes was about - the session generation it was
    /// shown - when the host knows it. A yes about one race must not abandon
    /// another that went green while the question was open; if the race has
    /// changed, the phone is asked again. Null - a page that does not send
    /// it - trusts the yes as before.
    /// </param>
    public RemoteCommandOutcome RunRemoteCommand(string action, bool confirmedOverRunningRace, int? confirmedRaceGeneration = null)
    {
        if (IsDisposed || Disposing)
        {
            return RemoteCommandOutcome.NotDelivered("The laptop is closing - it was NOT sent.");
        }

        // A slip in the host rather than anything the phone did - but the
        // race and this page are not thread-safe, so nothing is touched from
        // any other thread.
        if (InvokeRequired)
        {
            return RemoteCommandOutcome.NotDelivered("The laptop could not take that command just now - it was NOT sent. Try again.");
        }

        if (action is not ("stage" or "countdown" or "green" or "yellow" or "red" or "checkered" or "reset"))
        {
            return RemoteCommandOutcome.Refused("Unknown command.");
        }

        if (confirmedRaceGeneration is { } shownRace && shownRace != _race.SessionGeneration)
        {
            confirmedOverRunningRace = false;
        }

        // Checked here, on the thread that owns the race: the server's own
        // check read the state from a request thread, and the race may have
        // gone green since. Staging over a running race wipes its standings,
        // so it needs the phone's "yes, really" to go ahead. The host used to
        // make this check itself before calling in, and the phone was told
        // "sent" either way.
        //
        // Not for practice, which runs in the Racing state too: Stage and
        // Countdown are refused outright then (below), and that refusal is
        // the one that says why.
        //
        // The phone is asked rather than told no: the page sends every press
        // without the "yes" first, puts the question to its user and sends the
        // command again, confirmed, on a yes - the same round trip as the
        // server's own check, which usually catches it first. A plain refusal
        // left it saying "confirm on the phone" with nothing to press.
        if (!confirmedOverRunningRace && (action is "stage" or "countdown")
            && _race.State == RaceState.Racing && !_race.IsPracticeMode)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [web] Asked the phone to confirm a remote {action.ToUpperInvariant()} - a race went green while the command was on its way.");
            return RemoteCommandOutcome.NeedsConfirm(_race.IsDemoMode
                ? "A demo is running - this would end it."
                : "A race is running - this would wipe its standings.");
        }

        // Read before the command runs - the phone's answer says which it was.
        var wasRacing = _race.State == RaceState.Racing;

        // A green over a green changes nothing now (see the Green action),
        // and the phone is told it was already green. It used to be told
        // "racing resumes", while the start was called out all over again.
        var wasGreen = wasRacing && _race.Flag == FlagState.Green;

        // Kept and put back rather than simply cleared: a box open on the
        // laptop still runs this thread's queue, so one phone command can
        // arrive inside another. Each gets its own answer.
        var outerRunning = _runningRemoteCommand;
        var outerRefusal = _remoteRefusal;

        _runningRemoteCommand = true;
        _remoteRefusal = null;

        try
        {
            // The same entry points as the function keys, so the phone runs
            // exactly what they run.
            switch (action)
            {
                case "stage": TriggerStage(confirmedOverRunningRace); break;
                case "countdown": TriggerCountdown(confirmedOverRunningRace); break;
                case "green": TriggerGreen(); break;
                case "yellow": TriggerYellow(); break;
                case "red": TriggerRed(); break;
                case "checkered": TriggerCheckered(); break;
                case "reset": TriggerReset(); break;
            }

            if (_remoteRefusal is { } refusal)
            {
                return RemoteCommandOutcome.Refused(refusal);
            }

            // Read from what the command left behind, not assumed from the
            // button pressed: between races, a format with a warm-up makes
            // Stage open the warm-up and stage only when it runs out (or on
            // a second Stage). The phone used to be told "Staged." then,
            // while the laptop sat in free laps for up to several minutes,
            // and an operator going by the phone believed the grid had been
            // called. A Stage pressed DURING a warm-up skips it and stages
            // straight away, so the warm-up is over by the time this is read
            // and that press is still answered "Staged.".
            return RemoteCommandOutcome.Done(action switch
            {
                "stage" when _race.IsWarmingUp => "Warm-up open - free laps first. Press Stage again to skip it and stage now.",
                "stage" => "Staged.",
                "countdown" => "Countdown started.",
                // A resume on the last lap puts the checkered back out, not a
                // green - see RaceControlSession.GreenFlag.
                "green" => wasGreen ? "Already green."
                    : wasRacing && _race.Flag == FlagState.Checkered ? "Checkered back out - the race is finishing."
                    : wasRacing ? "Green - racing resumes." : "Green - race on.",
                "yellow" => "Yellow flag out.",
                "red" => "Red flag out.",
                "checkered" => "Checkered flag out.",
                "reset" => "Race reset.",
                _ => "Done.",
            });
        }
        catch (Exception ex)
        {
            // As the race-day manager does with its own commands: said on the
            // phone and in the console, rather than left to escape as an
            // error box on a laptop whose operator pressed nothing.
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [web] A remote {action.ToUpperInvariant()} failed: {ex.Message}");
            return RemoteCommandOutcome.Refused($"The laptop hit a problem doing that: {ex.Message}");
        }
        finally
        {
            _runningRemoteCommand = outerRunning;
            _remoteRefusal = outerRefusal;
        }
    }

    /// <summary>
    /// Notes why a command from the phone was not carried out, for
    /// RunRemoteCommand's answer. Does nothing for a command from the laptop
    /// itself, whose refusals are said on the laptop as they always were.
    /// The first reason noted is the one kept.
    /// </summary>
    private void NoteRemoteRefusal(string reason)
    {
        if (_runningRemoteCommand)
        {
            _remoteRefusal ??= reason;
        }
    }

    /// <summary>
    /// Resets the race and hands back a single action that restores it,
    /// instead of capturing its own undo entry.
    ///
    /// Lets a caller fold the race reset into ONE undo step alongside its
    /// own changes - two separate captures would mean two presses of Undo
    /// to put back what felt like a single action.
    /// </summary>
    public Action ResetForCombinedUndo()
    {
        // A demo is ended first, as the Reset button does, so the simulator
        // lets go of the session instead of resetting it again later.
        var tookOverDemo = EndDemoForTakeover("reset");

        var snapshot = _race.CreateSnapshot();

        // When the race stopped counting, so the undo can leave the time it
        // was gone off the race clock - see RestoreSnapshotAfterGap.
        var snapshotTakenUtc = DateTime.UtcNow;
        var sessionStart = _sessionStartedAtUtc;
        var prompted = _promptedTransponders.ToList();

        // What kind of race it was, as the Reset button takes it: put back
        // without it, a Quick Race came back looking like any freeform race
        // and auto-advance filed it against the next heat.
        var wasQuick = _currentRaceIsQuick;
        var wasRerun = _currentRaceIsRerun;

        // The rules the race ran under, as the Reset button takes them (see
        // CaptureRaceRulesForUndo): a format written while the session sat
        // idle must not be what the restored race finishes under.
        var restoreRules = CaptureRaceRulesForUndo();

        CancelAutoStart();
        _race.Reset();

        // Which session the reset left behind - see RefuseStaleResetUndo.
        var generationAfterReset = _race.SessionGeneration;

        // As the Reset button does. Left behind, the journal offered the
        // reset race back at the next launch, as though it had been lost.
        // Not while an earlier race is still waiting to be saved - see
        // FileUnfiledRaces.
        if (_unfiledRaces.Count == 0)
        {
            _journal.Clear();
        }

        _promptedTransponders.Clear();
        _unnamedTransponders.Clear();
        _previousFlag = FlagState.None;
        _currentRaceIsDemo = false;
        _currentRaceIsQuick = false;
        _currentRaceIsRerun = false;
        _rerunPending = false;

        // As the Reset button does - see there. The undo below puts it back.
        _sessionStartedAtUtc = null;
        _dgvLeaderboard.Rows.Clear();
        RenderLeaderboard();
        UpdateClockDisplay(force: true);

        // Nothing of a demo to put back - restored without the simulator
        // driving it, it would sit on the board as a "real" race with fake
        // drivers in it. The caller's own part of the undo still runs.
        if (tookOverDemo)
        {
            return () => { };
        }

        return () =>
        {
            RefuseStaleResetUndo(generationAfterReset);

            // At Idle, before the snapshot raises Racing again.
            restoreRules();

            // Flag announcements suppressed while restoring, or undoing a
            // reset would re-announce the flags it passes back through.
            bool heldUnderRed;
            _suppressFlagAnnouncements = true;
            try
            {
                heldUnderRed = _race.RestoreSnapshotAfterGap(snapshot, snapshotTakenUtc);
            }
            finally
            {
                _suppressFlagAnnouncements = false;
            }

            _sessionStartedAtUtc = sessionStart;
            _currentRaceIsQuick = wasQuick;
            _currentRaceIsRerun = wasRerun;
            _promptedTransponders.Clear();
            foreach (var t in prompted)
            {
                _promptedTransponders.Add(t);
            }

            // The flag the race came back under, not the one it had before
            // the reset: a running race comes back under red, and the green
            // that restarts it has to be heard as a restart - which is
            // decided from this. The other two undo paths do the same.
            _previousFlag = _race.Flag;

            // The race is back, so its safety net is too - written now
            // rather than at its next crossing. Record itself skips an idle
            // or finished session, which has nothing to protect.
            WriteJournal(force: true);

            RenderLeaderboard();
            UpdateClockDisplay(force: true);

            // Held under red if it was running - see RestoreSnapshotAfterGap
            // - so the director is told why, and what to press.
            if (heldUnderRed)
            {
                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [race] The race is back, held under a red flag - no laps were counted while it was gone. "
                    + "Green when the cars are ready to go again.");
            }
        };
    }

    /// <summary>
    /// Stops an undo of Reset from landing on a race that came after it.
    ///
    /// The undo entry outlives the moment - it sits on the stack, three
    /// deep, for as long as nothing pushes it off. Pressed after the next
    /// race has been staged, run or even finished, it put the OLD race
    /// back over the new one: every lap the new heat had counted, gone,
    /// with the previous heat's standings on the board in their place.
    /// Throwing is how an undo says it could not be done - UndoService
    /// reports it through UndoFailed rather than pretending it worked.
    /// </summary>
    private void RefuseStaleResetUndo(int generationAfterReset)
    {
        if (_race.SessionGeneration != generationAfterReset || _race.State != RaceState.Idle)
        {
            throw new InvalidOperationException(
                "a new race has been started since the reset, and undoing it now would overwrite that race. "
                + "Nothing was changed.");
        }
    }
    /// <param name="confirmedOverRunningRace">As for <see cref="TriggerStage"/>.</param>
    /// <param name="restoreHostOnUndo">As for <see cref="TriggerStage"/>.</param>
    public void TriggerAutoStart(bool confirmedOverRunningRace = false, Action? restoreHostOnUndo = null) =>
        _actAutoStart?.Invoke(confirmedOverRunningRace, restoreHostOnUndo);

    /// <summary>
    /// Opens the live qualifying projection, or closes it if it is already
    /// up - a toggle, because the button is the only way back out of a
    /// window somebody has pushed onto a second screen and lost.
    ///
    /// Shown non-modally and owned by this page's form, so it stays above
    /// the app without being in the way of it, and goes when the app goes.
    /// </summary>
    private void ToggleProjectionWindow()
    {
        if (_projectionWindow is { IsDisposed: false } open)
        {
            open.Close();
            _projectionWindow = null;
            return;
        }

        if (_projector is null)
        {
            return;
        }

        var window = new QualifyingProjectionForm(_projector);

        // Cleared on close however it was closed - the button, the X, or
        // the app shutting down. Without this, closing it with the X left a
        // disposed form behind and the button did nothing until it was
        // pressed twice.
        window.FormClosed += (_, _) => _projectionWindow = null;

        _projectionWindow = window;

        window.Show(FindForm());
    }


    /// <summary>Sets the track layout from Session Setup, which owns the visible control.</summary>
    public void SetTrackLayout(string name)
    {
        // This list is only filled at construction, so a layout added since
        // - in the first-run wizard, say - was not in it, and the name was
        // silently ignored: the race then saved against no track, or the
        // previous one. A miss re-reads the layouts before giving up.
        if (!_cmbTrackLayout.Items.Contains(name))
        {
            RefreshTrackLayoutCombo();
        }

        // And if it still isn't there, "(none)" rather than whatever was
        // selected before - a race filed against the wrong track puts its
        // laps into the wrong track's records, which is worse than none.
        _cmbTrackLayout.SelectedItem = _cmbTrackLayout.Items.Contains(name) ? name : "(none)";
    }

    /// <summary>
    /// Re-reads the track layouts into the hidden list this page records
    /// races against, keeping the current choice where it still exists.
    /// For the host to call when layouts are added, renamed or removed.
    /// </summary>
    public void RefreshTrackLayouts() => RefreshTrackLayoutCombo();

    /// <summary>Sets the session name from Session Setup.</summary>
    public void SetSessionLabel(string label) => _txtSessionLabel.Text = label;

    /// <summary>Opens the Big Screen. Routed through the same button the Race Control page uses, so the window is only ever constructed in one place.</summary>
    public void TriggerOpenBigScreen() => _actBigScreen?.Invoke();

    /// <summary>Called by the host (MainForm) when this control's tab is being torn down - e.g. only really relevant on app shutdown, since tabs otherwise stay alive for the app's lifetime.</summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _clockRefreshTimer.Stop();

            // Stopped AND disposed - stopping alone still leaves the timer
            // holding a reference to this control through its Tick handler.
            _clockRefreshTimer.Dispose();

            CancelAutoStart();

            // Before unwiring, so the session's warm-up flag is cleared
            // along with the timer that would have ended it.
            CancelWarmUp();
            UnwireRaceControl();

            // A drop-down is a window of its own, not a child of this
            // control - closing the page does not close it.
            CloseLapPopup();
        }
        base.Dispose(disposing);
    }

    /// <summary>
    /// Pushes new race-control defaults into the live numeric controls -
    /// used when applying a Venue profile. Setting .Value cascades through
    /// each control's existing ValueChanged handler, which is what
    /// actually updates _race and persists to _settings, so this doesn't
    /// need to duplicate that logic.
    /// </summary>
    public void ApplyRaceDefaults(int lapLimit, int timeLimitMinutes, int minLapTimeMs, double streakSlowMultiplier)
    {
        _settings.RaceLapLimit = Math.Clamp(lapLimit, 0, 9999);
        _settings.RaceTimeLimitMinutes = Math.Clamp(timeLimitMinutes, 0, 180);
        _settings.RaceMinLapTimeMs = Math.Clamp(minLapTimeMs, 100, 60000);

        // Venue profiles still carry the old multiplier, so it's converted
        // rather than dropped: 1.5x the last lap is the same tolerance as
        // +50%, which is what the window now asks for.
        _settings.RaceStreakWindowPercent = Math.Clamp((streakSlowMultiplier - 1.0) * 100.0, 1, 200);
        _settings.Save();

        // Venue numbers land in the APP DEFAULT. A class or round format
        // still outranks them, which is right: the venue says what the
        // building needs, the class says how its races are run.
        ApplyEffectiveFormat();
    }

    /// <summary>Raised when Quick Race needs the session dropped back to a plain Race with no program round - MainForm owns the picker.</summary>
    public event Action? QuickRaceSetupRequested;

    /// <summary>
    /// Called by Reset to unbind the race-program round and clear the
    /// session name, as part of the same undo step as the race itself.
    /// Returns the action that puts them back, or null if there was nothing
    /// bound to clear.
    /// </summary>
    /// <remarks>
    /// A hook rather than an event because Reset needs an ANSWER - the undo
    /// action - and an event with several listeners has no single one to
    /// give back. MainForm sets it, because MainForm owns the round picker
    /// and the name box; this page knows only that something out there may
    /// have a binding to let go of.
    /// </remarks>
    public Func<Action?>? ClearRoundBinding { get; set; }

    /// <summary>
    /// Reloaded whenever badges change, so the Badges column on the board
    /// shows the number people just heard announced rather than the one from
    /// when the page was built.
    /// </summary>
    private AchievementStore _achievements = AchievementStore.Load();
}
