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

    /// <summary>Width of the clock column down the right-hand side - the clock, the three buttons under it, and the stat tiles all share it.</summary>
    private const int ClockWidth = 260;

    /// <summary>Set once the five controls in the clock column exist, so a size change during construction is ignored rather than crashing.</summary>
    private bool _rightColumnBuilt;

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

    /// <summary>The flags-and-format group and the board, held so the layout pass can reach them. Null until BuildLayout makes them.</summary>
    private GroupBox? _grpRaceControl;
    private GroupBox? _grpLeaderboard;

    /// <summary>
    /// Re-lays the correction strip and the board grid against the
    /// group's CURRENT width - LayoutRightColumn calls it after every
    /// board resize, so the strip can never stay laid out against a
    /// width the board no longer has.
    /// </summary>
    private Action? _reflowToolbar;

    private readonly Button _btnFormat = new();
    private readonly Button _btnQuickRace = new();

    // --- The Next Up bar ---
    private readonly NextUpBar _nextUpBar = new();
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

    /// <summary>
    /// Puts the clock column where it belongs, against the width the page
    /// actually has right now.
    ///
    /// This used to be done once in the constructor, from
    /// Math.Max(700, Width - PageRight), with every control in the column
    /// anchored to the right afterwards. Both halves of that are wrong.
    /// Width in the constructor is the UserControl's default, not the width
    /// this page ends up with - it has no parent yet - so the floor of 700
    /// is what actually decided the layout, and then anchoring preserved a
    /// distance from a right edge that never existed. The column came out
    /// hard against the window edge with no margin at all, and the stat
    /// tiles ended up on a different left edge from the clock and buttons
    /// above them, which is the part that shows.
    ///
    /// Anchors are gone with it. Anchoring and explicit placement are two
    /// authorities on the same coordinate, and the one that ran last won -
    /// which is exactly the kind of disagreement that makes a column look
    /// hand-assembled.
    /// </summary>
    private void LayoutRightColumn()
    {
        // The clock and the stat strip are built partway down the
        // constructor. A size change before that point would arrive here
        // with them still unset, which is a crash on startup rather than a
        // layout glitch.
        if (!_rightColumnBuilt)
        {
            return;
        }

        // ClientSize, not Width: with a scrollbar showing, Width counts the
        // strip nothing can be placed in, and the column would sit under
        // it.
        //
        // The floor is only for the window that has not been given its real
        // width yet - a page laid out at its default 150px would stack this
        // column on top of the Race Control group beside it, and the repair
        // pass would then spend its budget shoving them apart. As soon as
        // there is a real width, the real width wins.
        var right = Math.Max(700, ClientSize.Width - UiLayout.PageRight);
        var left = right - ClockWidth;

        // The bar first, spanning the full width, with its two buttons
        // parked in its right end and the text told where to stop.
        _nextUpBar.Location = new Point(UiLayout.PageLeft, 8);
        _nextUpBar.Width = right - UiLayout.PageLeft;

        var buttonY = (_nextUpBar.Height - _btnStartNext.Height) / 2;

        _btnReseed.Location = new Point(_nextUpBar.Width - _btnReseed.Width - 8, buttonY);
        _btnStartNext.Location = new Point(_btnReseed.Left - UiLayout.ButtonGap - _btnStartNext.Width, buttonY);
        _nextUpBar.ReservedRight = _nextUpBar.Width - _btnStartNext.Left + 8;

        var contentTop = _nextUpBar.Bottom + 10;

        _clock.Location = new Point(left, contentTop);
        _btnQuickRace.Location = new Point(left, _clock.Bottom + 8);
        _btnBigScreen.Location = new Point(left, _btnQuickRace.Bottom + 8);
        _btnAnnouncer.Location = new Point(left, _btnBigScreen.Bottom + 8);
        _btnProjection.Location = new Point(left, _btnAnnouncer.Bottom + 8);

        // Last of the buttons: it is run once, before the first race, and
        // then not again all night - the ones above it are pressed during
        // the racing. The stat strip under it is a readout, not a button.
        _btnReadiness.Location = new Point(left, _btnProjection.Bottom + 8);
        _statStrip.Location = new Point(left, _btnReadiness.Bottom + 8);

        // The two groups share the space to the LEFT of the clock column:
        // flags on top, and the board taking everything else down to the
        // bottom edge. This is what makes the leaderboard the biggest thing
        // on the page at any window size, which is what a leaderboard is
        // for.
        if (_grpRaceControl is { } flags && _grpLeaderboard is { } board)
        {
            var innerRight = left - UiLayout.RowGap;

            flags.Location = new Point(UiLayout.PageLeft, contentTop);
            flags.Width = Math.Max(600, innerRight - flags.Left);

            board.Location = new Point(flags.Left, flags.Bottom + 10);
            board.Size = new Size(
                Math.Max(600, innerRight - board.Left),
                Math.Max(180, ClientSize.Height - board.Top - 12));

            // And the board's INSIDES against those bounds, in the same
            // breath. The docked strip has already taken the new width by
            // the time Size returns; this rewraps its buttons to it and
            // hands the grid everything above the strip.
            _reflowToolbar?.Invoke();
        }
    }

    /// <summary>The clock column is placed by hand, so it has to be re-placed whenever the page changes width.</summary>
    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutRightColumn();
    }

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
    /// Raised when the track layout or session label changes here.
    ///
    /// These used to exist twice - once on the connection page and once
    /// here - which meant two fields for one value. The duplicates are
    /// hidden now, and these keep the saving code's copies in step.
    /// </summary>
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

        Dock = DockStyle.Fill;
        AutoScroll = true;
        MinimumSize = new Size(900, 560);
        Font = AppTheme.BodyFont;

        BuildLayout();
        WireRaceControl();

        // The journal says when it cannot write, and when it can again -
        // the one moment the race is running without its safety net is
        // worth a line in the console.
        _journal.StatusChanged += message =>
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] {message}");

        _clockRefreshTimer.Tick += (_, _) =>
        {
            UpdateClockDisplay();

            // Polled on the clock tick rather than a timer per car: this
            // already runs four times a second, which is close enough for a
            // release call, and it can't leak a timer into the next race.
            _race.PollReleases();

            // The Next Up bar rides the same tick, throttled to once a
            // second - what it says changes on the scale of minutes.
            if (DateTime.UtcNow >= _nextUpDueUtc)
            {
                _nextUpDueUtc = DateTime.UtcNow.AddSeconds(1);
                UpdateNextUpBar();
            }
        };
        _clockRefreshTimer.Start();

        RefreshTrackLayoutCombo();
        _cmbTrackLayout.SelectedIndexChanged += (_, _) =>
        {
            var selected = _cmbTrackLayout.SelectedItem as string;

            if (selected is not null)
            {
                TrackLayoutChanged?.Invoke(selected);
            }

            _settings.LastRaceControlTrackName = selected is null or "(none)" ? string.Empty : selected;
            _settings.Save();
        };
    }

    /// <summary>Called by the host (MainForm) when this control's tab is being torn down - e.g. only really relevant on app shutdown, since tabs otherwise stay alive for the app's lifetime.</summary>
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

            return RemoteCommandOutcome.Done(action switch
            {
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
        var sessionStart = _sessionStartedAtUtc;
        var prompted = _promptedTransponders.ToList();
        var previousFlag = _previousFlag;

        CancelAutoStart();
        _race.Reset();

        // Which session the reset left behind - see RefuseStaleResetUndo.
        var generationAfterReset = _race.SessionGeneration;

        // As the Reset button does. Left behind, the journal offered the
        // reset race back at the next launch, as though it had been lost.
        _journal.Clear();

        _promptedTransponders.Clear();
        _unnamedTransponders.Clear();
        _previousFlag = FlagState.None;
        _currentRaceIsDemo = false;

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

            // Flag announcements suppressed while restoring, or undoing a
            // reset would re-announce the flags it passes back through.
            _suppressFlagAnnouncements = true;
            try
            {
                _race.RestoreSnapshot(snapshot);
            }
            finally
            {
                _suppressFlagAnnouncements = false;
            }

            _sessionStartedAtUtc = sessionStart;
            _promptedTransponders.Clear();
            foreach (var t in prompted)
            {
                _promptedTransponders.Add(t);
            }

            _previousFlag = previousFlag;

            // The race is back, so its safety net is too - written now
            // rather than at its next crossing. Record itself skips an idle
            // or finished session, which has nothing to protect.
            WriteJournal(force: true);

            RenderLeaderboard();
            UpdateClockDisplay(force: true);
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

    /// <summary>Opens the Big Screen. Routed through the same button the Race Control page uses, so the window is only ever constructed in one place.</summary>

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

    public void TriggerOpenBigScreen() => _actBigScreen?.Invoke();

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

    private RaceClass? ClassFor(RoundDefinition? round) =>
        round is null || string.IsNullOrEmpty(round.ClassName)
            ? null
            : _raceClasses.All.FirstOrDefault(c =>
                string.Equals(c.Name, round.ClassName, StringComparison.OrdinalIgnoreCase));

    // ==================== Tech warning ====================

    /// <summary>
    /// The Tech Log to check a heat against.
    ///
    /// The shared store when the host passed one - that is the copy the
    /// Tech Log page writes to and the event binding keeps pointed at the
    /// current event. Without it, a fresh copy is read from disk (every
    /// inspection is saved the moment it is recorded) and pointed at the
    /// current event by hand, so the warning still works rather than
    /// silently never firing.
    /// </summary>
    private TechInspectionStore TechStoreForCheck()
    {
        if (_techInspections is not null)
        {
            return _techInspections;
        }

        var fresh = TechInspectionStore.Load();
        fresh.CurrentEventId = _events?.CurrentId ?? RaceEventStore.LegacyEventId;
        return fresh;
    }

    /// <summary>
    /// Whether scrutineering is actually being run for this class.
    ///
    /// "Warn about uninspected drivers" is on by default for every class,
    /// including all the ones at clubs that never run tech - and there,
    /// every heat would open with a list of the whole field. So a class
    /// counts as running tech when it has a checklist (an empty one is
    /// documented as "tech isn't run for this class"), or when somebody has
    /// recorded an inspection for it that could still clear a car here -
    /// at this race day, or recently (TechInspectionStore.NoRaceDayWindow)
    /// when it has none - the Tech Log
    /// also allows a plain pass/fail with no checklist.
    /// </summary>
    private static bool TechIsRunFor(RaceClass raceClass, TechInspectionStore tech)
    {
        if (raceClass.TechCheckItems.Count > 0)
        {
            return true;
        }

        bool SameClass(TechInspection i) =>
            string.Equals(i.ClassName, raceClass.Name, StringComparison.OrdinalIgnoreCase);

        // The legacy holder is every inspection from before events existed,
        // months of them - one pass-or-fail from last spring must not switch
        // the warning on today. Only a real event spans more than a day.
        var atThisEvent = !string.Equals(tech.CurrentEventId, RaceEventStore.LegacyEventId, StringComparison.Ordinal)
                          && tech.ForEvent(tech.CurrentEventId).Any(SameClass);

        // Otherwise, by the same rule as TechInspectionStore.Latest - which
        // IsClearedToday asks - an inspection with no event, or filed under
        // the legacy holder, counts while it is recent. This asked for
        // today's calendar date instead, so a club night running past
        // midnight switched the warning off while the clearances it relies
        // on were still good, and one from another race day, which clears
        // nobody here, could switch it on.
        var recentFrom = DateTime.UtcNow - TechInspectionStore.NoRaceDayWindow;
        var recentHere = tech.All.Any(i => SameClass(i)
                                           && i.InspectedAtUtc >= recentFrom
                                           && (string.IsNullOrEmpty(i.EventId)
                                               || string.Equals(i.EventId, RaceEventStore.LegacyEventId, StringComparison.Ordinal)
                                               || string.Equals(i.EventId, tech.CurrentEventId, StringComparison.Ordinal)));

        return atThisEvent || recentHere;
    }

    /// <summary>
    /// Drivers on a round's roster who are not cleared by tech, as
    /// "name - not inspected" / "name - Failed" lines. Empty when everyone
    /// is cleared, or when the check does not apply: no round, no roster,
    /// a class with "Warn about uninspected drivers" off, or a class that
    /// is not running tech at all.
    ///
    /// "Cleared" is exactly the Tech Log's own test (TechInspectionStore
    /// .IsClearedToday, class-scoped), so this and the Tech Log's
    /// Outstanding list can never disagree about who still needs doing.
    /// </summary>
    private List<string> UninspectedDrivers(RoundDefinition? round, TechInspectionStore tech)
    {
        var lines = new List<string>();

        if (round is null || ClassFor(round) is not { WarnOnUninspected: true } raceClass)
        {
            return lines;
        }

        if (!TechIsRunFor(raceClass, tech))
        {
            return lines;
        }

        var roster = round.AssignedTransponders
            .Where(t => !string.IsNullOrWhiteSpace(t) && !GhostService.IsGhost(t))
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var transponder in roster)
        {
            if (tech.IsClearedToday(transponder, raceClass.Name))
            {
                continue;
            }

            var profileName = _profiles.Get(transponder)?.DriverName;
            var name = string.IsNullOrWhiteSpace(profileName) ? transponder : profileName;
            var state = tech.Latest(transponder, raceClass.Name) is { } latest ? latest.ResultText : "not inspected";

            lines.Add($"{name} - {state}");
        }

        return lines;
    }

    /// <summary>
    /// Says, in the Race Control log, who in the heat being started has not
    /// passed tech.
    ///
    /// A warning and never a block, as the class setting promises: the
    /// race director decides who races, and a scrutineer running behind
    /// must not stop an event. Deliberately not a dialog either - this runs
    /// as the heat is staged, often inside the automatic Stage, Countdown,
    /// Green sequence, and a box waiting for OK would hold that sequence
    /// up. The same count is shown ahead of time on the Next Up bar.
    ///
    /// Skipped for practice and demo races, which are not heats.
    /// </summary>
    private void WarnAboutUninspectedDrivers()
    {
        if (_race.IsPracticeMode || _race.IsDemoMode)
        {
            return;
        }

        var round = _activeRound?.Invoke();

        if (round is null)
        {
            return;
        }

        List<string> lines;

        try
        {
            lines = UninspectedDrivers(round, TechStoreForCheck());
        }
        catch (Exception ex)
        {
            // A warning must never be the reason a heat fails to start.
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [tech] Could not check the Tech Log for this heat: {ex.Message}");
            return;
        }

        if (lines.Count == 0)
        {
            return;
        }

        StatusMessage?.Invoke(
            $"{AppTime.LogStampNow()}  [tech] WARNING: {Plural.Of(lines.Count, "driver")} in {RoundTitle(round)} "
            + $"{(lines.Count == 1 ? "has" : "have")} not passed tech today: {string.Join("; ", lines)}. "
            + "The heat is not held up - record their inspection on Race Day \u2192 Who's Racing.");
    }

    /// <summary>Round override, else class format, else app default - and which of those it was, for the chip.</summary>
    private RaceFormat EffectiveFormat(out string source)
    {
        var round = _activeRound?.Invoke();

        if (round?.FormatOverride is { } overridden)
        {
            source = $"{round.RoundLabel} override";
            return overridden;
        }

        // A qualifier runs under the class's qualifier format when it has
        // one; everything else - and a qualifier without one - runs under
        // the class format. Both fall through to the app default.
        if (round?.Kind == RoundKind.Qualifier && ClassFor(round)?.QualifierFormat is { } qualifierFormat)
        {
            source = $"{round.ClassName} qualifiers";
            return qualifierFormat;
        }

        if (ClassFor(round)?.Format is { } classFormat)
        {
            source = $"{round!.ClassName} class";
            return classFormat;
        }

        source = "app default";
        return RaceFormat.FromSettings(_settings);
    }

    /// <summary>
    /// Re-resolves the format and pushes it into the session.
    ///
    /// The session is only written between races. Mid-race, the chip still
    /// updates - so the operator can see what the NEXT race will run
    /// under - but the race being run keeps the rules it started with;
    /// changing the lap limit under a moving field is not a feature.
    /// </summary>
    /// <param name="startingNewRace">
    /// True from Stage, Countdown and a first Green: a new race is about to
    /// begin, so its format is written whatever the state - including over
    /// a race the person has just agreed to abandon. Without this the
    /// session kept whatever the last writer left in it, and after a
    /// practice (which clears the limits) the next race ran with no lap or
    /// time limit at all.
    /// </param>
    private void ApplyEffectiveFormat(bool startingNewRace = false)
    {
        _fmt = EffectiveFormat(out var source);

        if (startingNewRace || _race.State is RaceState.Idle or RaceState.Finished)
        {
            _race.LapLimit = _fmt.LapLimit == 0 ? null : _fmt.LapLimit;
            _race.TimeLimit = _fmt.TimeLimitMinutes == 0 ? null : TimeSpan.FromMinutes(_fmt.TimeLimitMinutes);
            _race.MinLapTimeMs = _fmt.MinLapTimeMs;
            _race.StreakWindowPercent = _fmt.StreakWindowPercent;
            _race.StreakFasterLapBreaks = _fmt.StreakFasterBreaks;
            _race.StartType = _fmt.StartType;
            _race.StaggerClockMode = _fmt.StaggerClockMode;
            _race.FinishOnNextCrossing = _fmt.FinishOnNextCrossing;

            // The format has just been written in whole, so there is no
            // handicap-start override left to put back.
            _clockBeforeHandicapOverride = null;
        }

        _lblFormat.Text = $"Format ({source}):  {_fmt.Describe()}";
    }

    /// <summary>For MainForm to call when the round selection changes - the format follows the round.</summary>
    public void RefreshFormat() => ApplyEffectiveFormat();

    // ==================== The Next Up bar ====================

    /// <summary>The program's not-yet-run rounds in running order, scoped to the current event when there is a real one.</summary>
    private List<RoundDefinition> OrderedProgram()
    {
        if (_rounds is null)
        {
            return new List<RoundDefinition>();
        }

        var all = _rounds.All;

        // A real current event bounds the day; the legacy holder does not -
        // on it, the whole program is the day, which is how the app worked
        // before events existed.
        if (_events?.Current is { IsLegacy: false } current)
        {
            var members = new HashSet<string>(current.RoundIds, StringComparer.Ordinal);
            return all.Where(r => members.Contains(r.Id)).ToList();
        }

        return all.ToList();
    }

    /// <summary>
    /// What runs after the round in hand: the first not-yet-recorded round
    /// that isn't the one already selected. Null when the program is done
    /// or there is no program.
    /// </summary>
    private RoundDefinition? NextRound()
    {
        var active = _activeRound?.Invoke();

        return OrderedProgram()
            .FirstOrDefault(r => !r.IsRecorded && !string.Equals(r.Id, active?.Id, StringComparison.Ordinal));
    }

    private static string RoundTitle(RoundDefinition round) =>
        string.IsNullOrWhiteSpace(round.ClassName)
            ? round.RoundLabel
            : $"{round.RoundLabel} ({round.ClassName})";

    private void UpdateNextUpBar()
    {
        var program = OrderedProgram();
        var active = _activeRound?.Invoke();

        // --- NOW ---
        var running = active is null
            ? (string.IsNullOrWhiteSpace(_txtSessionLabel.Text) ? "freeform session" : _txtSessionLabel.Text)
            : RoundTitle(active);

        var now = $"{running} · {_race.State.ToString().ToLowerInvariant()}";

        // --- NEXT ---
        var next = program.FirstOrDefault(r => !r.IsRecorded && !string.Equals(r.Id, active?.Id, StringComparison.Ordinal));
        var warning = false;
        string nextText;

        if (next is null)
        {
            nextText = program.Count == 0
                ? "no program - Quick Race is one press away"
                : "program complete";
        }
        else
        {
            var roster = next.AssignedTransponders;

            var missing = _checkIns is null
                ? 0
                : roster.Count(t => !_checkIns.IsCheckedIn(t));

            // Only from the shared Tech Log: this runs every second, and the
            // fallback in TechStoreForCheck reads the file from disk.
            var notThroughTech = _techInspections is null
                ? 0
                : UninspectedDrivers(next, _techInspections).Count;

            warning = missing > 0 || notThroughTech > 0;

            var parts = $"{RoundTitle(next)} · {Plural.Of(roster.Count, "driver")}";

            if (missing > 0)
            {
                parts += $" · {missing} not checked in";
            }

            if (notThroughTech > 0)
            {
                parts += $" · {notThroughTech} not through tech";
            }

            // The convention every club already runs: the heat before you
            // marshals your heat. Said here so nobody has to be found and
            // told - the bar IS the telling.
            var index = program.FindIndex(r => string.Equals(r.Id, next.Id, StringComparison.Ordinal));

            if (index > 0)
            {
                parts += $" · marshals: {program[index - 1].RoundLabel}";
            }

            nextText = parts;
        }

        // --- DAY ---
        var total = program.Count;
        var done = program.Count(r => r.IsRecorded);
        var day = total == 0 ? string.Empty : $"{done}/{total} recorded";

        // Drift, when the run sheet gave this round a parseable time.
        // Freeform times ("~10:15", "TBD") simply produce no drift figure,
        // which is the right amount of certainty about them.
        if (next is not null && _daySchedule is not null)
        {
            var line = _daySchedule.All.FirstOrDefault(e =>
                string.Equals(e.RoundId, next.Id, StringComparison.Ordinal));

            // Shared with the Timetable's own reading rather than worked
            // out again here. This copy took the slot on today's date
            // flat, so once a club night ran past midnight a 23:55 heat
            // reached at 00:10 read as "1425 min ahead" - and the
            // Timetable, three feet away on the same laptop, said 15
            // behind. See ScheduleDrift.MinutesOff.
            if (line is not null && ScheduleDrift.MinutesOff(line.ScheduledTime, DateTime.Now) is { } driftMinutes)
            {
                if (driftMinutes >= 3)
                {
                    day += $" · {driftMinutes} min behind";
                }
                else if (driftMinutes <= -3)
                {
                    day += $" · {-driftMinutes} min ahead";
                }
            }
        }

        // --- Is anything owed before the next race can go out? ---
        //
        // The rule lives in ProgramNextStep, which puts bump-ups ahead of
        // running the next round and says why: "a bump-up changes who is
        // in the main above, so doing it after that main has gone out is
        // not a late correction - it is too late, full stop."
        //
        // This bar did not know. It offered Start Next on the A-Main, with
        // no warning, the instant the B-Main was recorded - while Today
        // and Heats & Mains both sat holding "Apply Bump-Ups". The one
        // screen the director actually watches was the one screen that
        // could get it wrong. Asking the same authority is the fix; the
        // bar still decides nothing.
        var idle = _race.State is RaceState.Idle or RaceState.Finished;

        if (!idle)
        {
            // Nothing is owed while a race is on: the button says "Race
            // Running" either way, and this is the busiest the app gets.
            _barStep = null;
        }
        else if (DateTime.UtcNow - _barStepReadUtc >= TimeSpan.FromSeconds(1))
        {
            _barStepReadUtc = DateTime.UtcNow;

            _barStep = NextStepProvider?.Invoke() is { } step
                       && step.Stage is ProgramStage.BumpUps or ProgramStage.SeedMains
                ? step
                : null;
        }

        if (_barStep is { } owed)
        {
            warning = true;
            nextText = owed.Headline + "  ·  " + owed.ActionCaption.TrimEnd('.');
        }

        _nextUpBar.ShowInfo(new NextUpBar.Info(now, nextText, day, warning));

        // While a session is under way the button starts nothing - that is
        // deliberate, the next heat must not be launchable by a stray
        // click; the checkered flag or Reset in Race Control comes first.
        // So the caption says what is true instead of promising a start:
        // "Race Running", greyed, until the session is over.
        var racing = !idle;

        _btnStartNext.Text = racing
            ? ProgramCaptions.RaceRunning
            : _barStep?.ActionCaption ?? ProgramCaptions.StartNext;

        _btnStartNext.Enabled = !racing && (_barStep is not null || next is not null);
    }

    /// <summary>
    /// Rebuilds the rosters of every not-yet-run qualifier from who is
    /// checked in RIGHT NOW - the one-press answer to a no-show or a late
    /// entry, instead of editing heats by hand while the room waits.
    ///
    /// Recorded rounds are never touched, and mains are left alone too:
    /// they are seeded from qualifying results, which re-seeding cannot
    /// improve on.
    /// </summary>
    private void ReseedRemaining()
    {
        if (_rounds is null || _checkIns is null)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [day] Redo Remaining Heats needs the program and check-in stores - not available here.");
            return;
        }

        var pending = OrderedProgram()
            .Where(r => r.Kind == RoundKind.Qualifier && !r.IsRecorded)
            .ToList();

        if (pending.Count == 0)
        {
            AppDialog.Show(FindForm(), "Every heat on the program has already been run, so there is nothing to redo.",
                "Nothing left to redo", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var confirm = AppDialog.Show(FindForm(),
            $"Rebuild the rosters of {Plural.Of(pending.Count, "not-yet-run qualifier heat")} from who is checked in right now? "
            + "Recorded heats and mains are untouched. Undo puts the old rosters back.",
            "Redo remaining heats", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

        if (confirm != DialogResult.Yes)
        {
            return;
        }

        // The old rosters, captured before anything moves, so one Undo puts
        // the whole re-seed back rather than one heat of it.
        var before = pending.ToDictionary(r => r.Id, r => r.AssignedTransponders.ToList(), StringComparer.Ordinal);

        _undo.Capture("Redo the remaining heats", () =>
        {
            foreach (var round in pending)
            {
                if (before.TryGetValue(round.Id, out var old))
                {
                    round.AssignedTransponders = old;
                    _rounds.Update(round);
                }
            }
        });

        var touched = 0;
        var skipped = new List<string>();

        foreach (var classGroup in pending.GroupBy(r => r.ClassName, StringComparer.OrdinalIgnoreCase))
        {
            var roster = _profiles.All
                .Where(p => string.Equals(p.ClassName, classGroup.Key, StringComparison.OrdinalIgnoreCase)
                            && _checkIns.IsCheckedIn(p.Transponder))
                .Select(p => p.Transponder)
                .ToList();

            if (roster.Count == 0)
            {
                skipped.Add(classGroup.Key);
                continue;
            }

            // Heats regrouped by the round they belong to - "Q1 Heat 2"
            // groups under "Q1", a hand-made "Heat 2" under the blank
            // prefix - so the per-round rotation survives the re-seed.
            var groups = classGroup
                .GroupBy(r =>
                {
                    var cut = r.RoundLabel.IndexOf("Heat", StringComparison.OrdinalIgnoreCase);
                    return cut < 0 ? r.RoundLabel : r.RoundLabel[..cut].Trim();
                })
                .ToList();

            var groupIndex = 0;

            foreach (var group in groups)
            {
                var heats = group.ToList();
                var heatSize = (int)Math.Ceiling(roster.Count / (double)heats.Count);
                var shift = roster.Count == 0 ? 0 : groupIndex * heatSize % roster.Count;
                var rotated = roster.Skip(shift).Concat(roster.Take(shift)).ToList();

                var buckets = Enumerable.Range(0, heats.Count).Select(_ => new List<string>()).ToList();

                for (var i = 0; i < rotated.Count; i++)
                {
                    buckets[i % heats.Count].Add(rotated[i]);
                }

                for (var i = 0; i < heats.Count; i++)
                {
                    heats[i].AssignedTransponders = buckets[i];
                    _rounds.Update(heats[i]);
                    touched++;
                }

                groupIndex++;
            }
        }

        var note = $"{AppTime.LogStampNow()}  [day] Rebuilt {Plural.Of(touched, "heat")} from the live check-in sheet.";

        if (skipped.Count > 0)
        {
            note += $" Skipped {string.Join(", ", skipped)} - nobody checked in.";
        }

        StatusMessage?.Invoke(note);
        UpdateNextUpBar();
    }

    /// <summary>
    /// Opens the editor on the format that is actually in force, at its
    /// source: the round's override if that is what is running, else the
    /// class's format, else the app default. Editing what you can see
    /// beats a menu asking which of three formats you meant.
    /// </summary>
    private void EditFormat()
    {
        var round = _activeRound?.Invoke();

        if (round?.FormatOverride is not null && _rounds is not null)
        {
            using var overrideForm = new RaceFormatForm(
                round.FormatOverride, $"Race Format - {round.RoundLabel} (round override)", allowUseDefault: true,
                fallback: RaceFormat.FromSettings(_settings));

            if (overrideForm.ShowDialog(FindForm()) == DialogResult.OK)
            {
                // The old override, for Undo - put back onto the round as it
                // is on file then, by id, so an undo from further back does
                // not bring back a stale copy of the rest of the round.
                RoundDefinitionStore rounds = _rounds;
                var roundId = round.Id;
                var oldOverride = round.FormatOverride?.Clone();
                var newOverride = overrideForm.Result;

                _undo.Capture($"Edit race format for {round.RoundLabel}", () =>
                {
                    if (rounds.Get(roundId) is not { } current)
                    {
                        throw new InvalidOperationException("that round has been deleted since. Nothing was changed.");
                    }

                    if (!ReferenceEquals(current.FormatOverride, newOverride))
                    {
                        throw new InvalidOperationException(
                            $"{current.RoundLabel}'s format has been changed again since, and undoing this now would undo that too. Nothing was changed.");
                    }

                    current.FormatOverride = oldOverride;
                    rounds.Update(current);
                    ReapplyFormatAfterUndo();
                });

                round.FormatOverride = newOverride;
                _rounds.Update(round);
                ApplyEffectiveFormat();
            }

            return;
        }

        if (ClassFor(round) is { Format: not null } cls)
        {
            using var classForm = new RaceFormatForm(cls.Format, $"Race Format - {cls.Name}", allowUseDefault: true,
                fallback: RaceFormat.FromSettings(_settings));

            if (classForm.ShowDialog(FindForm()) == DialogResult.OK)
            {
                // As for the round above: only the format goes back, onto
                // the class as it is on file then.
                var classes = _raceClasses;
                var classId = cls.Id;
                var oldFormat = cls.Format?.Clone();
                var newFormat = classForm.Result;

                _undo.Capture($"Edit race format for {cls.Name}", () =>
                {
                    if (classes.Get(classId) is not { } current)
                    {
                        throw new InvalidOperationException("that class has been deleted since. Nothing was changed.");
                    }

                    if (!ReferenceEquals(current.Format, newFormat))
                    {
                        throw new InvalidOperationException(
                            $"{current.Name}'s format has been changed again since, and undoing this now would undo that too. Nothing was changed.");
                    }

                    current.Format = oldFormat;
                    classes.Upsert(current);
                    ReapplyFormatAfterUndo();
                });

                cls.Format = newFormat;
                _raceClasses.Upsert(cls);
                ApplyEffectiveFormat();
            }

            return;
        }

        using var defaultForm = new RaceFormatForm(
            RaceFormat.FromSettings(_settings), "App Default Race Format", allowUseDefault: false);

        if (defaultForm.ShowDialog(FindForm()) == DialogResult.OK && defaultForm.Result is { } edited)
        {
            var settings = _settings;
            var oldDefault = RaceFormat.FromSettings(settings);

            _undo.Capture("Edit the app default race format", () =>
            {
                oldDefault.SaveTo(settings);
                settings.Save();
                ReapplyFormatAfterUndo();
            });

            edited.SaveTo(_settings);
            _settings.Save();
            ApplyEffectiveFormat();
        }
    }

    /// <summary>
    /// Puts the page's format (and an idle race's numbers) back in line
    /// after an undone format edit, exactly as the edit itself did.
    /// ApplyEffectiveFormat already leaves a race under way alone.
    /// </summary>
    private void ReapplyFormatAfterUndo()
    {
        if (!IsDisposed)
        {
            ApplyEffectiveFormat();
        }
    }

    private void BuildLayout()
    {
        // --- Session row --------------------------------------------------
        // Track and label on one aligned row, with the clock pinned right.
        // These used to sit at four different left edges with the group
        // below starting at a fifth.
        // Track Layout and Session Name live in Session Setup above this
        // page now. These controls stay - track-record checks read
        // _cmbTrackLayout - but they're hidden and driven from there, so
        // there's one place to set each value rather than two.
        var lblTrack = new Label { Text = "Track Layout:", Visible = false };

        _cmbTrackLayout.Visible = false;
        _cmbTrackLayout.Width = 200;
        _cmbTrackLayout.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbTrackLayout.SelectedIndexChanged += (_, _) =>
        {
            var selected = _cmbTrackLayout.SelectedItem as string;

            if (selected is not null)
            {
                TrackLayoutChanged?.Invoke(selected);
            }

            _settings.LastTrackLayoutName = selected ?? string.Empty;
            _settings.Save();
        };

        var lblLabel = new Label { Text = "Session Name:", Visible = false };

        _txtSessionLabel.Visible = false;
        _txtSessionLabel.Width = 220;
        _txtSessionLabel.PlaceholderText = "optional, e.g. \"Friday Main\"";
        _txtSessionLabel.TextChanged += (_, _) => SessionLabelChanged?.Invoke(_txtSessionLabel.Text);

        // The clock column: sizes and behaviour here, positions in
        // LayoutRightColumn, which runs again every time the page changes
        // width. Nothing in this block sets a Location or an Anchor - that
        // split is the whole point, and putting one back here is how the
        // column came to disagree with itself.
        const int clockWidth = ClockWidth;

        // Still wanted for the group box below, which is anchored on both
        // sides and so only needs a starting width. Note the floor of 700:
        // during a constructor this control has no parent and Width is the
        // UserControl default, which is why it is not to be trusted as a
        // right edge for anything placed by hand.
        var contentRight = Math.Max(700, Width - UiLayout.PageRight);

        // --- The Next Up bar ---
        //
        // Lives INSIDE the bar as children, so painting order is never a
        // question - a child always draws over its parent.
        // Sized for the WIDEST caption it ever wears, because swapping
        // text must not change the bar's geometry. It says "Race Running"
        // while a session is under way, and when the programme owes
        // something before the next race it wears that step's own caption
        // instead - see UpdateNextUpBar.
        _btnStartNext.Font = AppTheme.BodyFont;

        var startNextWide = 0;

        // Taken from ProgramCaptions rather than written out again here. The
        // same four strings used to be literals in both files, and renaming
        // one of them left this list behind - which does not break anything
        // loudly, it just clips the new caption on the one screen the
        // director watches all night.
        foreach (var caption in ProgramCaptions.NextUpBarWidest)
        {
            _btnStartNext.Text = caption;
            startNextWide = Math.Max(startNextWide, UiLayout.WidthFor(_btnStartNext));
        }

        _btnStartNext.Text = ProgramCaptions.StartNext;
        _btnStartNext.Size = new Size(startNextWide, UiLayout.ButtonHeight);
        _btnStartNext.Click += (_, _) =>
        {
            if (_race.State is not (RaceState.Idle or RaceState.Finished))
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [day] Start Next: a session is already under way - finish or reset it first.");
                return;
            }

            // Something is owed before the next race can go out - see
            // UpdateNextUpBar. The button is captioned for it, so it does
            // it.
            if (_barStep is { } step)
            {
                RunProgramStepRequested?.Invoke(step);
                return;
            }

            if (NextRound() is { } next)
            {
                NextRoundStartRequested?.Invoke(next);
            }
            else
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [day] Start Next: nothing left to run - the program is complete. Quick Race still works.");
            }
        };

        // Was "Re-seed Remaining...". It rebuilds the rosters of the heats
        // that have not run yet from who is checked in now, which is what
        // the caption says and what "re-seed" did not.
        _btnReseed.Text = "Redo Remaining Heats...";
        _btnReseed.Font = AppTheme.BodyFont;
        _btnReseed.Size = new Size(UiLayout.WidthFor(_btnReseed), UiLayout.ButtonHeight);
        _btnReseed.Click += (_, _) => ReseedRemaining();

        _nextUpBar.Controls.Add(_btnStartNext);
        _nextUpBar.Controls.Add(_btnReseed);

        _clock.Size = new Size(clockWidth, 200);

        // The escape hatch. Whoever is on the track right now, the app
        // default format, no event, no program, no check-in - staged,
        // counted down and green in one press. A structured race day is
        // only acceptable when "just go" is always one button away.
        _btnQuickRace.Text = "Quick Race";
        _btnQuickRace.Size = new Size(clockWidth, UiLayout.ButtonHeight + 4);

        // A delegate, like the flag actions: F8 used to reach this through
        // PerformClick, which does nothing while the button is not visible -
        // so in Race Mode showing any page but this one, F8 was silently
        // ignored while F1-F7 worked.
        _actQuickRace = () =>
        {
            // Pressed again during its own warm-up: skip the rest of it and
            // go straight on to the start.
            if (SkipWarmUpIfRunning())
            {
                return;
            }

            if (_race.State is not (RaceState.Idle or RaceState.Finished))
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Quick Race: a session is already under way - finish or reset it first.");
                return;
            }

            // MainForm drops the pickers back to Race + freeform, so the
            // session neither runs under some selected round's format nor
            // records itself against that round.
            QuickRaceSetupRequested?.Invoke();

            _txtSessionLabel.Text = $"Quick Race {DateTime.Now:HH:mm}";

            ApplyEffectiveFormat();
            ApplyJumpStartRules();

            // A warm-up in the app default format runs first. The format is
            // applied again at the end of it, in case anything moved on.
            if (TryBeginWarmUp(() =>
                {
                    ApplyEffectiveFormat();
                    ApplyJumpStartRules();
                    _ = RunAutoStartSequenceAsync();
                }))
            {
                return;
            }

            _ = RunAutoStartSequenceAsync();
        };
        _btnQuickRace.Click += (_, _) => _actQuickRace?.Invoke();

        _btnBigScreen.Text = "Open Big Screen";
        _btnBigScreen.Size = new Size(clockWidth, UiLayout.ButtonHeight + 4);

        // At the foot of the button column - see LayoutRightColumn. It is
        // run once before racing starts and then left alone, so it gives up
        // the higher slots to the buttons pressed during the night.
        _btnReadiness.Text = "Pre-race check...";
        _btnReadiness.Size = new Size(clockWidth, UiLayout.ButtonHeight + 4);
        _btnReadiness.Click += (_, _) => ShowReadinessCheck();

        // Fills the dead space that sat between the Big Screen button and
        // the leaderboard, and puts the leader's lap count and the session
        // best where they can be read without hunting for a row.
        // Opens the announcer's own window; there is a button on it that
        // brings this page back to the front.
        //
        // It was a tab under Voice, and this button switched the whole app
        // over to it. During a race these are the only two screens anybody
        // is on, and one replacing the other is the wrong shape: whoever is
        // on the microphone wants the running order in front of them WHILE
        // the director works the flags. A window can sit beside this one or
        // own a second monitor. A tab could do neither, and every glance at
        // the announcer's notes took Race Control off the screen.
        _btnAnnouncer.Text = "Open Announcer Screen";
        _btnAnnouncer.Size = new Size(clockWidth, UiLayout.ButtonHeight + 4);
        _btnAnnouncer.Click += (_, _) => AnnouncerRequested?.Invoke();

        _btnProjection.Text = "Qualifying Projection";
        _btnProjection.Size = new Size(clockWidth, UiLayout.ButtonHeight + 4);
        _btnProjection.Click += (_, _) => ToggleProjectionWindow();

        _statStrip.Size = new Size(clockWidth, 50);

        // Placed, rather than anchored. See LayoutRightColumn.
        _rightColumnBuilt = true;
        LayoutRightColumn();

        // (continues below - see LayoutRightColumn for where the five
        // controls above are actually put.)

        // Restored: my layout rewrite dropped this handler entirely, so the
        // button did nothing and TriggerOpenBigScreen - used by the remote
        // and the day schedule - silently no-opped. The compiler only
        // warned about the unassigned field, not the missing behaviour.
        _actBigScreen = () => new BigScreenForm(_race, _announcer, _profiles, _daySchedule, _sponsors, _settings, _projector).Show();
        _btnBigScreen.Click += (_, _) => _actBigScreen();

        var grpRaceControl = new GroupBox
        {
            Text = "Race Control",
            Location = new Point(UiLayout.PageLeft, UiLayout.PageTop),

            // A starting width only - no anchor. The layout pass sets the
            // real width against the page's real size on every resize, and
            // anchoring as well would make two authorities on the same
            // edge; whichever ran last would win. That lesson is written on
            // the clock column, and it applies here identically.
            Size = new Size(Math.Max(600, contentRight - clockWidth - 36), 292),
        };

        // The flag row is measured, not placed.
        //
        // These were seven buttons pinned to 86x30 at x = 12, 104, 196 and
        // so on. "Green (Start)" does not fit 86px at this text size, so
        // every launch the repair pass widened three of them and shoved the
        // four after them right - twenty-odd adjustments on the page the
        // app spends its life on, redone from scratch each time the window
        // opened. Measuring the row here means there is nothing left to
        // repair.
        //
        // Laid out after the captions are set, at the bottom of this block.
        _btnStage.Text = "Stage";
        _actStage = (confirmedOverRunningRace, restoreHostOnUndo) =>
        {
            // Before anything else - Stage clears the standings, and the
            // shared session is holding a practice's laps.
            if (RefuseIfPracticeRunning("stage a race"))
            {
                return;
            }

            // Stage during a warm-up skips the rest of it - the warm-up's
            // own continuation then stages (or runs Quick Race's sequence).
            if (SkipWarmUpIfRunning())
            {
                return;
            }

            // Between races, a format with a warm-up opens it first and
            // stages when it runs out. Not when this Stage IS the end of a
            // warm-up, and never over a race that is staged or running.
            if (!_warmUpJustFinished
                && TryBeginWarmUp(() => _actStage?.Invoke(false, restoreHostOnUndo)))
            {
                return;
            }

            // Stage over a running race wipes every lap it has counted, and
            // F1 is one key away from the flag keys - so it is asked about,
            // and can be undone, rather than simply done.
            var abandoningRace = _race.State == RaceState.Racing;

            if (abandoningRace && !confirmedOverRunningRace && !ConfirmAbandonRunningRace("stage a new race"))
            {
                return;
            }

            // A demo on the session is ended rather than abandoned, and not
            // put back by Undo: it is a rehearsal, and a demo race restored
            // without the simulator driving it would sit there as a "real"
            // race with fake drivers in it.
            var tookOverDemo = EndDemoForTakeover("stage a race");

            // Filled in once the new race is staged - the undo reads it
            // then, not now.
            var generationAfterStage = -1;

            if (abandoningRace && !tookOverDemo)
            {
                CaptureAbandonedRaceUndo("Stage over a running race", () => generationAfterStage, restoreHostOnUndo);
            }

            CancelAutoStart();

            // The format for the race about to run, written now. Nothing
            // else is guaranteed to have written it since the last race or
            // practice - see ApplyEffectiveFormat.
            ApplyEffectiveFormat(startingNewRace: true);
            ApplyJumpStartRules();
            SpeakRoundIntro();
            _race.Stage();
            generationAfterStage = _race.SessionGeneration;

            // Chain onward once the staging call has finished speaking.
            // Driven by the audio rather than a timer, so the sequence
            // always follows the announcements instead of talking over
            // them or leaving dead air.
            if (_fmt.AutoAdvance)
            {
                ChainAfterAudio(RaceState.Staging, () => _actCountdown?.Invoke(false));
            }
        };
        _btnStage.Click += (_, _) => _actStage(false, null);

        _btnCountdown.Text = "Countdown";
        _actCountdown = confirmedOverRunningRace =>
        {
            if (RefuseIfPracticeRunning("start a countdown"))
            {
                return;
            }

            // A countdown over a running race used to go straight ahead:
            // every car crossing the line was then booked as a jump start,
            // and the green that followed wiped the race. Asked about, and
            // undoable until that green, exactly as Stage is.
            var abandoningRace = _race.State == RaceState.Racing;

            if (abandoningRace && !confirmedOverRunningRace && !ConfirmAbandonRunningRace("start a new countdown"))
            {
                return;
            }

            // A demo is ended, not abandoned - see Stage.
            var tookOverDemo = EndDemoForTakeover("start a countdown");

            // A countdown does not start a new session - the green does -
            // so this is the generation of the race being abandoned, and
            // the undo holds until the new race goes green.
            var generationAfterCountdown = _race.SessionGeneration;

            if (abandoningRace && !tookOverDemo)
            {
                CaptureAbandonedRaceUndo("Countdown over a running race", () => generationAfterCountdown);
            }

            CancelAutoStart();
            ApplyEffectiveFormat(startingNewRace: true);

            // A countdown that did not come through Stage gets the class's
            // jump-start rules here, since Stage is where they were applied
            // and it was skipped - otherwise the race was judged on the
            // LAST race's rules. So does one taken over from a demo, whose
            // staging never applied any. A countdown restarted from a
            // countdown keeps what the first one set.
            if (tookOverDemo || _race.State is not (RaceState.Staging or RaceState.Countdown))
            {
                ApplyJumpStartRules();
            }

            _race.StartCountdown();

            if (_fmt.AutoAdvance)
            {
                ChainAfterAudio(RaceState.Countdown, () => _actGreen?.Invoke());
            }
        };
        _btnCountdown.Click += (_, _) => _actCountdown(false);

        _btnGreen.Text = "Green (Start)";
        _actGreen = () =>
        {
            if (RefuseIfPracticeRunning("start a race"))
            {
                return;
            }

            // A race already under green has nothing to start or resume, so
            // the press changes nothing at all. It used to throw the green
            // again, and the start was called out a second time in the middle
            // of the race - see GreenFlag in RaceControlSession. Checked before
            // anything is touched, and said only in the log: nothing needs
            // doing, and the phone remote is told it is already green.
            if (_race.State == RaceState.Racing && _race.Flag == FlagState.Green)
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Green pressed - the race is already green, so nothing changed.");
                return;
            }

            // Nor can a green take back the checkered on a race's last lap.
            // Refused, with the reason, before anything is touched.
            if (GreenRefusal(_race.State, _race.Flag) is { } noGreen)
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Green not thrown. {noGreen}");
                NoteRemoteRefusal(noGreen);
                return;
            }

            CancelAutoStart();

            var resuming = _race.State == RaceState.Racing;

            // A green over a demo's staging or countdown starts a real race
            // in its place, so the demo is ended first. Over a demo that is
            // already racing it is a restart after a caution, and the demo
            // carries on.
            var tookOverDemo = !resuming && EndDemoForTakeover("start a race");

            // A first green - from idle, staging or the countdown - starts a
            // race, so the race gets its format now. A restart after a
            // caution keeps the rules the race started with.
            if (!resuming)
            {
                ApplyEffectiveFormat(startingNewRace: true);

                // As for a countdown: a green with no staging (or taken over
                // from a demo's) never had the class's jump-start rules
                // applied, and would otherwise run on the last race's.
                if (tookOverDemo || _race.State is not (RaceState.Staging or RaceState.Countdown))
                {
                    ApplyJumpStartRules();
                }
            }

            // Straightforward now: the audio wait lives in the session gate
            // that every start path shares, so there's nothing to await
            // here. This used to wrap a Task.Run that no longer awaited
            // anything, which is what the compiler was warning about.
            StartGreenNow(resuming);
        };
        _btnGreen.Click += (_, _) => _actGreen();

        _btnYellow.Text = "Yellow";
        _actYellow = () => { CancelAutoStart(); _race.YellowFlag(); };
        _btnYellow.Click += (_, _) => _actYellow();

        _btnRed.Text = "Red";
        _actRed = () => { CancelAutoStart(); _race.RedFlag(); };
        _btnRed.Click += (_, _) => _actRed();

        _btnCheckered.Text = "Checkered";
        _actCheckered = () =>
        {
            // Practice runs on this session in the Racing state, and a
            // checkered from here finished it as a race - leaving the
            // Practice page "running" over a finished session, with its own
            // save and teardown never run.
            if (RefuseIfPracticeRunning("throw the checkered flag"))
            {
                return;
            }

            // Only a race that is running can be finished. Nothing staged, a
            // start still on its way to the green and a race that is already
            // over are all refused, and left exactly as they were - see
            // RefuseCheckeredWithoutARace.
            if (RefuseCheckeredWithoutARace())
            {
                return;
            }

            CancelAutoStart();
            _race.CheckeredFlag();
        };
        _btnCheckered.Click += (_, _) => _actCheckered();

        _btnReset.Text = "Reset";
        _actReset = () =>
        {
            // Refused during practice, as Stage is. A reset from here put the
            // session back to Idle under a practice the Practice page still
            // showed as running, with no lap counted until it was stopped
            // and started again - and practice has its own Stop, which saves
            // and tidies up, and its own Clear for starting the figures over.
            if (RefuseIfPracticeRunning("reset the race"))
            {
                return;
            }

            // A demo is ended, and the reset has nothing of it to undo - a
            // demo race put back without the simulator driving it would sit
            // on the board as a "real" race with fake drivers in it.
            var tookOverDemo = EndDemoForTakeover("reset");

            // Reset now also unbinds the round and clears the session name.
            // That was a second button - "Reset Round", in the session row a
            // few inches from this one - whose body was this one's plus
            // those two lines, and which took an undo entry of its own.
            // Null when there was no round and no name to clear, so an
            // already-clear session does not gain an undo step that undoes
            // nothing.
            var restoreBinding = ClearRoundBinding?.Invoke();

            // Snapshot first - Reset clears the live standings, so this has
            // to happen before anything is torn down. Only worth capturing
            // if there's actually something to lose.
            //
            // The generation is filled in once the reset has happened - the
            // undo action reads it then, not now.
            var generationAfterReset = -1;

            if (!tookOverDemo && (_race.GetOrderedStandings().Count > 0 || _race.State != RaceState.Idle))
            {
                var snapshot = _race.CreateSnapshot();
                var sessionStart = _sessionStartedAtUtc;
                var prompted = _promptedTransponders.ToList();

                _undo.Capture("Reset race", () =>
                {
                    RefuseStaleResetUndo(generationAfterReset);

                    _suppressFlagAnnouncements = true;
                    try
                    {
                        _race.RestoreSnapshot(snapshot);
                    }
                    finally
                    {
                        _suppressFlagAnnouncements = false;
                    }

                    _sessionStartedAtUtc = sessionStart;
                    _promptedTransponders.Clear();
                    foreach (var t in prompted)
                    {
                        _promptedTransponders.Add(t);
                    }
                    _previousFlag = _race.Flag;

                    // The reset cleared the journal; the race is back, so it
                    // is written again now rather than at its next crossing.
                    WriteJournal(force: true);

                    RenderLeaderboard();
                    UpdateClockDisplay();

                    // Last, so the round and the name come back with the
                    // race rather than as a separate press of Undo.
                    restoreBinding?.Invoke();
                });
            }
            else if (restoreBinding is not null)
            {
                // Nothing of the race to put back - it was already idle and
                // empty - but the round binding still is.
                _undo.Capture("Reset race", restoreBinding);
            }

            CancelAutoStart();
            CancelWarmUp();
            _race.Reset();
            generationAfterReset = _race.SessionGeneration;

            // Reset is a decision, not an accident - there is nothing left
            // to recover, and leaving the journal behind would offer the
            // race back on the next launch.
            _journal.Clear();

            _toldAboutRecordPace.Clear();
            _promptedTransponders.Clear();
            _unnamedTransponders.Clear();
            _previousFlag = FlagState.None;
            _currentRaceIsDemo = false;

            // The reset race's start goes with it (Undo puts it back). Left
            // here, a start that does not stamp its own - a demo's green -
            // kept it through the "??=" at Racing, and was saved as having
            // begun when the abandoned race did.
            _sessionStartedAtUtc = null;
            _announcer.StopAllPendingAudio();
            _dgvLeaderboard.Rows.Clear();
            UpdateClockDisplay(force: true);
        };
        _btnReset.Click += (_, _) => _actReset();

        var shortcutTips = new ToolTip();
        AppTheme.StyleToolTip(shortcutTips, keep: true);
        shortcutTips.SetToolTip(_btnStage, "Shortcut: F1 (while this page is active)");
        shortcutTips.SetToolTip(_btnCountdown, "Shortcut: F2");
        shortcutTips.SetToolTip(_btnGreen, "Shortcut: F3");
        shortcutTips.SetToolTip(_btnYellow, "Shortcut: F4");
        shortcutTips.SetToolTip(_btnRed, "Shortcut: F5");
        shortcutTips.SetToolTip(_btnCheckered, "Shortcut: F6");
        shortcutTips.SetToolTip(_btnReset, "Shortcut: F7 (asks first if a race is running)");
        shortcutTips.SetToolTip(_btnQuickRace, "Shortcut: F8");
        shortcutTips.SetToolTip(_btnBigScreen, "Shortcut: B");
        shortcutTips.SetToolTip(_btnReadiness, "Shortcut: R");
        shortcutTips.SetToolTip(_btnAnnouncer, "Shortcut: A  -  press ? for every shortcut");

        var lblStateCaption = new Label { Text = "State:", Location = new Point(12, 72), AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
        _lblRaceState.Text = "Idle";
        _lblRaceState.Location = new Point(96, 72);
        _lblRaceState.AutoSize = true;

        var lblFlagCaption = new Label { Text = "Flag:", Location = new Point(200, 72), AutoSize = true, Font = new Font(Font, FontStyle.Bold) };
        _lblFlag.Text = "None";
        _lblFlag.ForeColor = AppTheme.TextMuted;
        _lblFlag.Location = new Point(246, 72);
        _lblFlag.AutoSize = true;

        // --- The race format chip ----------------------------------------
        //
        // Twelve setup knobs used to fill this group - limits, start type,
        // stagger, streak rules, the auto-start timings. They are the race
        // FORMAT, they live on the class now (overridable per round, app
        // default underneath - see RaceFormat), and on race day this page
        // is for RUNNING races, not for re-dialling the countdown between
        // heats. What remains is a chip saying which format is in force
        // and one button that edits it at its source.

        // Kept so anything still calling TriggerAutoStart keeps working -
        // the remote control and the day schedule both use it.
        //
        // Straight to Stage, which applies the jump-start rules itself -
        // AFTER its checks. Applying them here first changed the rules of a
        // race that was still running, even when the person then chose not
        // to abandon it.
        _actAutoStart = (confirmedOverRunningRace, restoreHostOnUndo) => _actStage?.Invoke(confirmedOverRunningRace, restoreHostOnUndo);

        // One row, left to right, each button as wide as its own caption.
        // A tight row rather than the standard one: seven buttons at the
        // usual minimum width would come out wider than the group holding
        // them.
        var flagRowRight = UiLayout.TightButtonRow(
            12, 26, _btnStage, _btnCountdown, _btnGreen, _btnYellow, _btnRed, _btnCheckered, _btnReset);

        // And if the captions still need more room than the group has - a
        // longer language, a bigger text size - the group grows rather than
        // clipping the last flag off the end.
        grpRaceControl.Width = Math.Max(grpRaceControl.Width, flagRowRight + 12);

        // Attached after every Click above, so the action runs and then
        // focus moves off. A flag button that keeps focus keeps answering
        // to Space and Enter, which on this row means a stray keypress can
        // re-start a race that is already running - and the focus ring it
        // draws over a flag colour looks like a fault besides.
        AppTheme.ReleaseFocusAfterClick(
            _btnStage, _btnCountdown, _btnGreen, _btnYellow, _btnRed, _btnCheckered, _btnReset);

        _lblFormat.AutoSize = true;
        _lblFormat.Font = AppTheme.BodyFont;
        _lblFormat.ForeColor = AppTheme.TextMuted;
        _lblFormat.MaximumSize = new Size(560, 0);
        _lblFormat.Location = new Point(12, 108);

        var formatTip = new ToolTip();
        AppTheme.StyleToolTip(formatTip, keep: true);
        formatTip.SetToolTip(_lblFormat,
            "Which race format is in force for what's about to run: the round's override if it has one, "
            + "else its class's format, else the app default. Class formats are set on the Classes page.");

        _btnFormat.Text = "Race Format...";
        _btnFormat.Font = AppTheme.BodyFont;
        _btnFormat.Size = new Size(UiLayout.WidthFor(_btnFormat), UiLayout.ButtonHeight);
        _btnFormat.Location = new Point(12, 148);
        _btnFormat.Click += (_, _) => EditFormat();

        grpRaceControl.Height = _btnFormat.Bottom + 16;

        grpRaceControl.Controls.AddRange(new Control[]
        {
            _btnStage, _btnCountdown, _btnGreen, _btnYellow, _btnRed, _btnCheckered, _btnReset,
            lblStateCaption, _lblRaceState, lblFlagCaption, _lblFlag,
            _lblFormat, _btnFormat,
        });

        ApplyEffectiveFormat();

        // BESIDE the clock column, not below it. The board used to run the
        // full width of the page, which meant it could only start below the
        // clock, Quick Race, the buttons and the stat tiles - half a screen
        // of dead space above it, and a board that never dominated the page
        // it is the whole point of. Now it starts directly under the flags
        // and stops just left of the clock column, and the layout pass
        // gives it every remaining pixel down to the bottom edge - see
        // LayoutRightColumn, which owns all of this geometry against the
        // page's real width. The placeholder numbers here are overwritten
        // by that pass before the page is ever seen.
        var leaderboardTop = grpRaceControl.Bottom + 10;

        var grpLeaderboard = new GroupBox
        {
            Text = "Leaderboard (double-click a driver for their lap breakdown - right-click for names, penalties and corrections)",
            Location = new Point(12, leaderboardTop),
            Size = new Size(876, 320),
        };
        // The correction toolbar gets a strip of its own, pinned to the
        // bottom of the group, and the board gets everything above it.
        //
        // It used to be the other way about: the buttons were placed at a
        // remembered y inside the group, and the board was given a fixed
        // height and anchored to the TOP only - because anchoring it to the
        // bottom as well made it grow straight down over them. That kept
        // the buttons visible at the price of a board that never got any
        // taller, so on a big screen there was a band of empty group box
        // under a board still scrolling at eight drivers.
        //
        // A strip of its own fixes both. The board can be anchored on all
        // four sides, because the space it grows into now stops where the
        // strip begins.
        // PLACED, not docked - see FlowToolbar, which puts both this strip
        // and the board inside the group's display rectangle in one pass.
        var toolbar = new Panel { Height = 1, BackColor = AppTheme.Surface, Tag = AppTheme.KeepBackColor };

        // Neither placed nor anchored here: FlowToolbar sets this board's
        // whole rectangle, and the strip's, from the group's display
        // rectangle on every pass. Anything written here would be
        // overwritten a few lines later, which is worse than nothing
        // because it reads as though it meant something.
        _dgvLeaderboard.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        _dgvLeaderboard.ReadOnly = true;

        // No pop-ups on the board at all - not even the notes it used to put
        // on a cell. Hovering a live leaderboard mid-race kept putting boxes
        // over the rows being read. What those notes said is on the row
        // itself: a jump start is tinted red, a car waiting to be released is
        // greyed. Attach is kept only for what it switches OFF - the grid's
        // own Windows pop-up for cut-off text.
        GridTips.Attach(_dgvLeaderboard);
        _dgvLeaderboard.AllowUserToAddRows = false;
        _dgvLeaderboard.AllowUserToDeleteRows = false;
        _dgvLeaderboard.RowHeadersVisible = false;

        // Row heights are not the user's to drag. The board sets them
        // itself - a lap strip is measured to its own wrapped text, and a
        // driver row is sized to the theme's font - so a hand-dragged
        // height is only ever wrong by the next refresh. Both levers are
        // turned off: the rows themselves, and the column header's bottom
        // edge, which resizes on drag by default and is the one that is
        // actually reachable with the row headers hidden.
        _dgvLeaderboard.AllowUserToResizeRows = false;
        _dgvLeaderboard.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        _dgvLeaderboard.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _dgvLeaderboard.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _dgvLeaderboard.CellDoubleClick += DgvLeaderboard_CellDoubleClick;

        // Right-click acts on the row under the pointer, not on whatever
        // happened to be selected before - clicking a row and getting the
        // previous driver's menu is how the wrong car gets a penalty.
        _dgvLeaderboard.MouseDown += (_, e) =>
        {
            if (e.Button != MouseButtons.Right)
            {
                return;
            }

            var hit = _dgvLeaderboard.HitTest(e.X, e.Y);

            if (hit.RowIndex >= 0 && hit.RowIndex < _dgvLeaderboard.Rows.Count)
            {
                _dgvLeaderboard.CurrentCell = _dgvLeaderboard.Rows[hit.RowIndex].Cells[Math.Max(0, hit.ColumnIndex)];
            }
        };

        _dgvLeaderboard.CellPainting += LeaderboardDetailPaint;
        _dgvLeaderboard.ContextMenuStrip = BuildLeaderboardMenu();
        RebuildLeaderboardColumns();
        grpLeaderboard.Controls.Add(_dgvLeaderboard);

        // --- Manual corrections toolbar ---
        var lblFix = new Label { Text = "Selected racer:", AutoSize = true, Font = AppTheme.BoldFont };

        var btnLapPlus = new Button { Text = "+1 Lap" };
        btnLapPlus.Click += (_, _) => AdjustSelected(laps: 1, penaltyMs: 0);

        var btnLapMinus = new Button { Text = "-1 Lap" };
        btnLapMinus.Click += (_, _) => AdjustSelected(laps: -1, penaltyMs: 0);

        var btnPenalty5 = new Button { Text = "+5s Penalty" };
        btnPenalty5.Click += (_, _) => AdjustSelected(laps: 0, penaltyMs: 5000);

        var btnPenalty10 = new Button { Text = "+10s Penalty" };
        btnPenalty10.Click += (_, _) => AdjustSelected(laps: 0, penaltyMs: 10000);

        var btnClearPenalty = new Button { Text = "Clear Penalties" };
        btnClearPenalty.Click += (_, _) => ClearSelectedPenalty();

        // Opens the reasons list under the button - see RaceControlWarnings.
        // W does the same from the keyboard, and the tooltip below says so,
        // as it does for every other key on this page. This was the one
        // button carrying its key in its caption as well - "(W)" - which
        // no other did, and which said less than the tooltip already does.
        var btnWarning = new Button { Text = "\u26A0 Warning" };
        btnWarning.Click += (_, _) => ShowWarningMenu(btnWarning, new Point(0, btnWarning.Height));

        // The keys for the selected driver - see ShortcutCatalog.
        var driverKeyTips = new ToolTip();
        AppTheme.StyleToolTip(driverKeyTips, keep: true);
        driverKeyTips.SetToolTip(btnLapPlus, "Shortcut: +");
        driverKeyTips.SetToolTip(btnLapMinus, "Shortcut: -");
        driverKeyTips.SetToolTip(btnPenalty5, "Shortcut: P");
        driverKeyTips.SetToolTip(btnPenalty10, "Shortcut: Shift+P");
        driverKeyTips.SetToolTip(btnClearPenalty, "Shortcut: C");
        driverKeyTips.SetToolTip(btnWarning, "Shortcut: W, then 1-7 for the reason.  Shift+W takes the last one back.");

        var btnColumns = new Button { Text = "Columns..." };
        btnColumns.Click += (_, _) =>
        {
            using var form = new KpiPickerForm("Race Control", _settings.RaceControlKpis, KpiCatalog.DefaultRaceControl);
            if (form.ShowDialog(this) == DialogResult.OK)
            {
                // The picker's Restore Defaults lands here too, so the old
                // list is kept for Undo. This page lives as long as the app.
                var before = _settings.RaceControlKpis.ToList();
                if (!before.SequenceEqual(form.SelectedKeys))
                {
                    _undo.Capture("Change the Race Control columns", () =>
                    {
                        _settings.RaceControlKpis = before.ToList();
                        _settings.Save();

                        if (!IsDisposed)
                        {
                            RebuildLeaderboardColumns();
                            RenderLeaderboard();
                        }
                    });
                }

                _settings.RaceControlKpis = form.SelectedKeys;
                _settings.Save();
                RebuildLeaderboardColumns();
                RenderLeaderboard();
            }
        };

        var lblFixHint = new Label
        {
            Text = "For missed or double-counted passes, on-track penalties and warnings. Press ? for keyboard shortcuts.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        // The hint goes on its own line above the buttons rather than after
        // them. It used to sit at x=734 with six buttons in front of it,
        // and since every one of those buttons widens with the font, the
        // hint was being pushed off the right-hand edge of an 876-wide
        // group before anyone had even resized the window.
        lblFixHint.Location = new Point(12, 6);

        // PreferredHeight/PreferredWidth rather than Height/Width: an
        // AutoSize label has not measured its text until it is shown, so
        // reading Height here gives the default 23 and everything placed
        // under it lands too high. Asking for the preferred size forces the
        // measurement now.
        var buttonsTop = lblFixHint.Top + lblFixHint.PreferredHeight + 6;

        // "Selected racer:" sits ABOVE the buttons now instead of in front
        // of the first one.
        //
        // In the row it cost the first line about 110px, which is what made
        // the strip wrap onto two lines - and the second line then started
        // at the margin while the first started past the label, so the two
        // rows of buttons did not line up with each other. Above, every
        // button starts at the same left edge, and the row usually fits on
        // one line again: the label's line costs the height the wrap was
        // costing anyway.
        lblFix.Location = new Point(12, buttonsTop);

        var buttonRowTop = buttonsTop + lblFix.PreferredHeight + 4;

        // Sized here (ButtonRow measures each caption); POSITIONED by the
        // flow below, which re-runs on every toolbar resize.
        UiLayout.ButtonRow(
            12,
            buttonRowTop,
            btnLapPlus, btnLapMinus, btnPenalty5, btnPenalty10, btnClearPenalty, btnWarning, btnColumns);

        toolbar.Controls.AddRange(new Control[]
        {
            lblFixHint, lblFix, btnLapPlus, btnLapMinus, btnPenalty5, btnPenalty10, btnClearPenalty, btnWarning, btnColumns,
        });

        // The row WRAPS. It was laid out once against the old full-width
        // board; now that the board stops at the clock column, a narrower
        // window ran the last buttons straight off the group's right edge.
        // Flowing on resize means the row uses one line when it fits and
        // takes a second when it doesn't - and the board above gives up
        // exactly that row's height, no more.
        var flowItems = new Control[]
        {
            btnLapPlus, btnLapMinus, btnPenalty5, btnPenalty10, btnClearPenalty, btnWarning, btnColumns,
        };

        // Lays the strip out, then puts BOTH it and the board inside the
        // group in one pass.
        //
        // This is the third attempt at this rectangle and the first one
        // that cannot drift. The strip used to be docked to the bottom of
        // the group while the board's height was worked out from the
        // group's height, the board's own Top and the strip's height - the
        // same answer arrived at two different ways, in two places, with a
        // GroupBox's caption reserve (which follows the FONT, not the
        // painted caption) as an unstated third term. Whenever those three
        // disagreed the result was a band of empty group under the strip,
        // or the board painted over the strip's first row.
        //
        // Now there is one rectangle - the group's DisplayRectangle - and
        // the two children are placed from its bottom and its top. They
        // meet in the middle by construction, and the group's own insets
        // are whatever they are rather than something to be guessed at.
        void FlowToolbar()
        {
            var inner = grpLeaderboard.DisplayRectangle;

            // One width for all of them - the widest caption sets it - so
            // the two rows read as columns instead of ragging out to the
            // length of each caption.
            //
            // Re-measured from the captions here rather than taken from the
            // widths already on the buttons: reading those back and keeping
            // the largest would ratchet, so the row could grow with the font
            // but never shrink again.
            var uniform = 0;

            foreach (var item in flowItems)
            {
                if (item is Button measured)
                {
                    uniform = Math.Max(uniform, UiLayout.WidthFor(measured));
                }
            }

            foreach (var item in flowItems)
            {
                if (item is Button sized)
                {
                    sized.Size = new Size(uniform, UiLayout.ButtonHeight);
                }
            }

            var x = 12;
            var y = buttonRowTop;
            var limit = Math.Max(240, inner.Width - 12);

            foreach (var item in flowItems)
            {
                var width = item is Label label ? label.PreferredWidth : item.Width;

                if (x > 12 && x + width > limit)
                {
                    x = 12;
                    y += UiLayout.ButtonHeight + 6;
                }

                item.Location = item is Label fixLabel
                    ? new Point(x, y + Math.Max(0, (UiLayout.ButtonHeight - fixLabel.PreferredHeight) / 2))
                    : new Point(x, y);

                x += width + UiLayout.ButtonGap;
            }

            var needed = y + UiLayout.ButtonHeight + 6;

            // Flush to the bottom of the group, full width of it.
            toolbar.Bounds = new Rectangle(inner.Left, inner.Bottom - needed, inner.Width, needed);

            // And the board takes everything above, meeting the strip with
            // one gap that is stated once.
            _dgvLeaderboard.Bounds = new Rectangle(
                inner.Left,
                inner.Top,
                Math.Max(200, inner.Width),
                Math.Max(120, toolbar.Top - inner.Top - UiLayout.HintGap));
        }

        // No Resize hook. The strip's width is now set by FlowToolbar
        // itself, so a hook on its own Resize would be this method
        // re-entering itself - and the group's resize already runs
        // LayoutRightColumn, which calls this.
        _reflowToolbar = FlowToolbar;

        grpLeaderboard.Controls.Add(toolbar);

        FlowToolbar();

        // Two children placed by hand from one rectangle. The repair pass
        // is told to keep out, because a third opinion on this rectangle is
        // exactly what produced the band of dead space under the buttons.
        LayoutDoctor.LeaveAlone(grpLeaderboard);

        Controls.Add(lblTrack);
        Controls.Add(_cmbTrackLayout);
        Controls.Add(lblLabel);
        Controls.Add(_txtSessionLabel);
        Controls.Add(_clock);
        Controls.Add(_statStrip);
        Controls.Add(_nextUpBar);
        Controls.Add(_btnQuickRace);
        Controls.Add(_btnBigScreen);
        Controls.Add(_btnAnnouncer);
        Controls.Add(_btnProjection);
        Controls.Add(_btnReadiness);
        Controls.Add(grpRaceControl);
        Controls.Add(grpLeaderboard);

        _grpRaceControl = grpRaceControl;
        _grpLeaderboard = grpLeaderboard;

        // Now that both groups exist, the layout pass places them against
        // the real width - and keeps doing so on every resize.
        LayoutRightColumn();
    }

    /// <summary>
    /// Cancels any auto-start sequence currently waiting between phases.
    /// Called at the top of every manual race-control button so a human
    /// clicking any button - including Stage or Countdown themselves -
    /// always overrides a pending automated step rather than racing against it.
    /// </summary>

    /// <summary>
    /// Drops the green flag and stamps the session start.
    ///
    /// Split out so the audio wait above can hand back to the UI thread -
    /// the race session and the controls bound to it are not thread-safe.
    /// </summary>
    private void StartGreenNow(bool resuming)
    {
        // Only stamp the session start when this green actually starts the
        // race. Resuming from a yellow/red must not move it, or the saved
        // session would claim it began at the restart.
        if (!resuming)
        {
            _sessionStartedAtUtc = DateTime.UtcNow;
        }

        // The start-of-race work lives in the session's own green-flag gate
        // now - see where BeforeGreenFlagAsync is wired - so every start
        // path gets it whether or not it came through this method.
        _ = _race.GreenFlagAfterAudioAsync();
    }


    /// <summary>
    /// Runs the next step of the start sequence once the current step's
    /// audio has finished.
    ///
    /// A yellow, a red or a reset during the wait must abandon the chain -
    /// the whole point is that it advances a normal start, never that it
    /// overrides a human decision. It used to check the race STATE alone,
    /// and a yellow or red does not change the state: thrown during
    /// staging or the countdown, the chain carried straight on to the green
    /// under it. So the step only runs when all of these still hold:
    ///
    /// - nothing has cancelled it - every flag button that acts, Reset and
    ///   any other manual step cancels through CancelAutoStart (a checkered
    ///   refused before the green does not - see RefuseCheckeredWithoutARace);
    /// - no yellow or red is out (see AutoStartMayContinue);
    /// - it is still the same session - no reset or new stage in between;
    /// - and the race is still at the step this chain was waiting on.
    /// </summary>
    /// <param name="waitingIn">The state this step leaves the race in, which it must still be in for the chain to go on.</param>
    private void ChainAfterAudio(RaceState waitingIn, Action next)
    {
        // A token of its own, held where CancelAutoStart can reach it. The
        // wait below used to take CancellationToken.None, so pressing a
        // flag button during it cancelled nothing.
        _autoStartCts?.Cancel();
        var cts = new CancellationTokenSource();
        _autoStartCts = cts;
        var token = cts.Token;

        var generation = _race.SessionGeneration;

        void Advance()
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            if (ReferenceEquals(_autoStartCts, cts))
            {
                _autoStartCts = null;
            }

            if (token.IsCancellationRequested || !AutoStartMayContinue(generation, waitingIn))
            {
                return;
            }

            next();
        }

        _ = Task.Run(async () =>
        {
            try
            {
                // Wait for the current step's audio, then move on promptly.
                //
                // There used to be an extra 400ms here on top of the gate's
                // own settle window, so the chained path paid both and the
                // countdown-to-green gap dragged - noticeably worse than
                // the demo, which pays neither.
                await _announcer.WaitForAudioAsync(TimeSpan.FromSeconds(20), token);
            }
            catch
            {
                // Fall through - a failed wait should not strand a race
                // mid-sequence, and a cancelled one is caught by the token
                // check in Advance.
            }

            if (IsDisposed || Disposing)
            {
                return;
            }

            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(Advance));
                    return;
                }

                Advance();
            }
            catch (InvalidOperationException)
            {
                // The window handle went away between the check above and
                // the post - the page is closing, and there is no start
                // left to advance.
            }
        });
    }

    /// <summary>
    /// Asks whether to abandon the race that is running, for a Stage or
    /// Countdown pressed in the middle of it. True when nothing is running,
    /// or the person said yes.
    ///
    /// Public so the host can ask BEFORE it sets up a round and speaks its
    /// grid call - asking afterwards would have already announced a heat
    /// that then does not run. Pass the answer on as
    /// confirmedOverRunningRace so it is not asked twice.
    /// </summary>
    /// <param name="action">What is about to happen, as it reads after "If you" - "stage a new race".</param>
    public bool ConfirmAbandonRunningRace(string action)
    {
        if (_race.State != RaceState.Racing)
        {
            return true;
        }

        // What was running when the question was put. The dialog is modal,
        // but its message loop still runs everything posted to this thread
        // - a phone remote's Checkered, Reset or Stage, a demo finishing -
        // so by the time it is answered the race it asked about may be gone.
        var isDemo = _race.IsDemoMode;
        var generationAsked = _race.SessionGeneration;
        var stateAsked = _race.State;

        // A demo is a rehearsal: nothing is lost by ending it, but the
        // question still has to say what is being ended. It used to say "a
        // race is running" over a demo.
        var answer = isDemo
            ? AppDialog.Show(this,
                $"A demo is running. If you {action} now, the demo ends: it is not saved to Race History "
                + "(even with \"Save demo races to history\" ticked), and Undo does not bring it back.\n\n"
                + "End the demo?",
                "Demo in progress",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question)
            : AppDialog.Show(this,
                $"A race is running. If you {action} now, that race is abandoned: every lap it has counted is "
                + "cleared from the board, and it is not saved to Race History.\n\n"
                + "To end the race and keep its result, choose No and use Checkered instead.\n\n"
                + "If you go ahead by mistake, Undo puts the race back - as long as the new race has not gone green.\n\n"
                + "Abandon the running race?",
                "Race in progress",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

        if (answer != DialogResult.Yes)
        {
            StatusMessage?.Invoke(isDemo
                ? $"{AppTime.LogStampNow()}  [race] Did not {action} - the demo was left running."
                : $"{AppTime.LogStampNow()}  [race] Did not {action} - the running race was left as it is.");
            NoteRemoteRefusal(isDemo
                ? "The laptop said no - the demo was left running."
                : "The laptop said no - the running race was left as it is.");
            return false;
        }

        // The yes was for the race that was running when it was asked. If
        // that race has since finished, been reset, been replaced, or a
        // demo has ended, the answer no longer applies - acting on it
        // anyway staged over whatever is there now, and captured an undo
        // of a race that was never the one agreed to.
        if (_race.SessionGeneration != generationAsked || _race.State != stateAsked || _race.IsDemoMode != isDemo)
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [race] Did not {action} - the session changed while the question was open "
                + $"(it is now {_race.State}). Nothing was done; do it again if it is still wanted.");
            NoteRemoteRefusal("Not done - the race changed while the laptop was asking about it.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Ends the demo on the Demo page, wired by the host. Race Control calls
    /// it through <see cref="StopDemoForTakeover"/> just before it stages,
    /// starts or resets over a demo, so the demo lets go of the session
    /// instead of carrying on inside the real race.
    /// </summary>
    public Action? StopRunningDemo { get; set; }

    /// <summary>
    /// Ends a demo that is driving the session so Race Control can take it
    /// over, and says so. True when there was one.
    ///
    /// Stage, Countdown, Green and Reset used to guard against practice
    /// only. Over a demo, the simulator kept injecting fake laps into the
    /// real race and the session kept its demo flag, so the real race was
    /// taken for a demo - not saved, or saved as [DEMO], and never written
    /// to the crash journal - and a later Stop on the Demo page reset it.
    ///
    /// Public so the host can end the demo BEFORE it sets up a round and
    /// speaks its grid call: ending it clears the demo's backlog of queued
    /// lap calls, which would take the grid call with it if it came after.
    /// </summary>
    /// <param name="action">What Race Control is about to do, as it reads after "so Race Control can" - "stage a race".</param>
    public bool StopDemoForTakeover(string action)
    {
        if (!_race.IsDemoMode)
        {
            return false;
        }

        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Demo ended so Race Control can {action} - a demo ended this way is not saved to Race History.");

        if (StopRunningDemo is { } stopDemo)
        {
            stopDemo();
        }
        else
        {
            // Not wired: the simulator still lets go by itself once the demo
            // flag is cleared (below) or the session moves on, but its backlog
            // of lap calls is not cleared for it - so clear it here.
            _announcer.StopAllPendingAudio();
        }

        // Whatever the host did, the next race is not a demo.
        _race.IsDemoMode = false;
        _currentRaceIsDemo = false;
        _demoTakenOverGeneration = _race.SessionGeneration;
        return true;
    }

    /// <summary>
    /// The session generation in which a demo was last ended by a takeover,
    /// or -1. The host ends a demo itself before a program start, so by the
    /// time Stage runs there is no demo left for it to see - but the race
    /// still showing is that demo's, and it must not be offered back as an
    /// undo (it would return as a "real" race with nothing driving it).
    ///
    /// Used up by the Race Control action that takes the session over - see
    /// EndDemoForTakeover.
    /// </summary>
    private int _demoTakenOverGeneration = -1;

    /// <summary>
    /// Ends a running demo, or reports that one was just ended in this same
    /// session by the host - either way the race on screen is a demo's and
    /// is not captured for undo.
    ///
    /// Only Stage, Countdown, a first Green and Reset (and Reset Round) call
    /// this, and each of them IS the takeover, so the note is used up here:
    /// from this action on, the race on the board is that action's. It used
    /// to stand until the session generation moved on, which Stage, the
    /// green and Reset do but a countdown does not - so a Reset pressed
    /// straight after a Countdown had taken a demo over still read the race
    /// as the demo's and could not be undone, although by then it was a
    /// real race's countdown. The same went for a Reset during a green's
    /// wait for the countdown call. A Reset that ends a demo that is still
    /// running is unchanged: StopDemoForTakeover finds the demo, and that
    /// Reset is still not undoable.
    /// </summary>
    private bool EndDemoForTakeover(string action)
    {
        var endedDemo = StopDemoForTakeover(action) || _demoTakenOverGeneration == _race.SessionGeneration;

        _demoTakenOverGeneration = -1;
        return endedDemo;
    }

    /// <summary>
    /// Captures an undo for a race that Stage or Countdown is about to
    /// abandon. Called before anything is changed - the format and
    /// jump-start rules included, since the new race writes its own.
    ///
    /// Only good until the new race goes green: after that there is a new
    /// race with its own laps, and putting the old one back over it would
    /// lose those instead. The same rule as undoing Reset.
    /// </summary>
    /// <param name="generationAfter">The session generation once the new start has been made, read when Undo is pressed.</param>
    /// <param name="restoreHostOnUndo">
    /// Puts back what the HOST changed for the new race - the active round,
    /// its track and label - captured by the host before it changed them.
    /// Without it, undoing a program start restored the race but left the
    /// new round selected, so the restored race's result was filed under the
    /// wrong heat and that heat was marked as run.
    /// </param>
    private void CaptureAbandonedRaceUndo(string description, Func<int> generationAfter, Action? restoreHostOnUndo = null)
    {
        var snapshot = _race.CreateSnapshot();
        var sessionStart = _sessionStartedAtUtc;
        var prompted = _promptedTransponders.ToList();

        // The rules the abandoned race was running under. The new start
        // writes its own format over these, and the race put back must not
        // finish under the next race's lap limit.
        var lapLimit = _race.LapLimit;
        var timeLimit = _race.TimeLimit;
        var minLapTimeMs = _race.MinLapTimeMs;
        var streakWindowPercent = _race.StreakWindowPercent;
        var streakFasterLapBreaks = _race.StreakFasterLapBreaks;
        var startType = _race.StartType;
        var staggerClockMode = _race.StaggerClockMode;
        var finishOnNextCrossing = _race.FinishOnNextCrossing;
        var clockBeforeHandicapOverride = _clockBeforeHandicapOverride;
        var detectJumpStarts = _race.DetectJumpStarts;
        var jumpStartPenaltyMs = _race.JumpStartPenaltyMs;
        var jumpStartCostsTheLap = _race.JumpStartCostsTheLap;
        var warningLadder = _race.WarningLadder;

        _undo.Capture(description, () =>
        {
            if (_race.SessionGeneration != generationAfter()
                || _race.State is not (RaceState.Staging or RaceState.Countdown))
            {
                throw new InvalidOperationException(
                    "the new race has already gone green (or been reset), and undoing now would overwrite it. "
                    + "Nothing was changed.");
            }

            // Or the start sequence would carry on from under the race
            // that has just been put back.
            CancelAutoStart();

            // The host's side FIRST, while the session is still at the new
            // race's Staging or Countdown (checked above). RestoreSnapshot
            // below raises Racing, and what listens for it reads the host's
            // setup there and then: the run sheet stamps the active round as
            // started, and the ghosts start on the selected track. Put back
            // afterwards, the new round - which never went green - was
            // stamped as started, and the ghosts ran on its track.
            //
            // Safe to do before the limits: at Staging or Countdown the
            // format re-read a round change triggers only refreshes the
            // format line (ApplyEffectiveFormat writes to the session at
            // Idle or Finished only), and the limits are written back
            // straight after in any case. In a try, so the race still comes
            // back if this goes wrong.
            if (restoreHostOnUndo is not null)
            {
                try
                {
                    restoreHostOnUndo();
                }
                catch (Exception ex)
                {
                    StatusMessage?.Invoke(
                        $"{AppTime.LogStampNow()}  [race] The race is being put back, but its round, track and label could not be: {ex.Message} "
                        + "Check the Round box before the race finishes.");
                }
            }

            _race.LapLimit = lapLimit;
            _race.TimeLimit = timeLimit;
            _race.MinLapTimeMs = minLapTimeMs;
            _race.StreakWindowPercent = streakWindowPercent;
            _race.StreakFasterLapBreaks = streakFasterLapBreaks;
            _race.StartType = startType;
            _race.StaggerClockMode = staggerClockMode;
            _race.FinishOnNextCrossing = finishOnNextCrossing;
            _clockBeforeHandicapOverride = clockBeforeHandicapOverride;
            _race.DetectJumpStarts = detectJumpStarts;
            _race.JumpStartPenaltyMs = jumpStartPenaltyMs;
            _race.JumpStartCostsTheLap = jumpStartCostsTheLap;
            _race.WarningLadder = warningLadder;

            _suppressFlagAnnouncements = true;
            try
            {
                _race.RestoreSnapshot(snapshot);
            }
            finally
            {
                _suppressFlagAnnouncements = false;
            }

            _sessionStartedAtUtc = sessionStart;
            _promptedTransponders.Clear();
            foreach (var t in prompted)
            {
                _promptedTransponders.Add(t);
            }

            _previousFlag = _race.Flag;

            // The format line (and the auto-advance setting read from it)
            // back to the restored race's round. Racing, so nothing is
            // written to the session - see ApplyEffectiveFormat.
            ApplyEffectiveFormat();

            WriteJournal(force: true);
            RenderLeaderboard();
            UpdateClockDisplay(force: true);
        });
    }

    /// <summary>
    /// True - after saying why - when open practice is running.
    ///
    /// Practice runs on the same race session as this page, in the Racing
    /// state, so Stage, Countdown or Green from here would take it over:
    /// Stage clears every lap the drivers have done, and the page would
    /// then treat the practice as a race. Refused outright rather than
    /// asked about, because the Practice page has its own save and
    /// teardown that stopping it there runs and taking it over here would
    /// skip.
    ///
    /// Public so the host can check before the programme or the Next Up
    /// bar sets up a round and speaks its grid call.
    /// </summary>
    public bool RefuseIfPracticeRunning(string action)
    {
        if (!_race.IsPracticeMode)
        {
            return false;
        }

        CancelAutoStart();

        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Can't {action} - practice is running. Stop practice first on the Practice page.");

        // From the phone remote the reason goes back to the phone instead.
        // The box used to go up on the laptop regardless - in front of
        // whoever was there, for a press nobody there had made - while the
        // phone, told "sent", never heard that it was refused.
        if (_runningRemoteCommand)
        {
            NoteRemoteRefusal("Practice is running - stop it on the Practice page first.");
            return true;
        }

        AppDialog.Show(this,
            $"Practice is running, so HeatCaller can't {action} yet - it would end the practice or wipe its laps.\n\n"
            + "Stop practice first on the Practice page, then try again.",
            "Practice is running", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return true;
    }

    /// <summary>
    /// True - after saying why - when there is no race running for the
    /// checkered flag to end: nothing staged, a start still on its way to the
    /// green, or a race that has already finished.
    ///
    /// The checkered used to finish the session whatever state it was in, and
    /// a finish is what saves - see RaceControlSession.CheckeredFlag for what
    /// that did. It is refused here first, so that whoever pressed it hears
    /// why and nothing at all is touched.
    ///
    /// A start is left exactly as it was, its automatic green included. A
    /// refused press has done nothing, so it overrides nothing: a mis-hit F6
    /// must not stall a start the room is waiting on, and a checkered is not
    /// how a start is called off - Reset is, or a red holds it, and the
    /// refusal says so.
    ///
    /// Said in the log rather than in a box. A box put up during a start
    /// would sit in front of the flag buttons and the function keys just as
    /// the green dropped - the reason the tech warning at staging is not a
    /// box either. From the phone remote, the reason goes back to the phone.
    /// </summary>
    private bool RefuseCheckeredWithoutARace()
    {
        if (CheckeredRefusal(_race.State) is not { } reason)
        {
            return false;
        }

        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Checkered not thrown. {reason}");
        NoteRemoteRefusal(reason);
        return true;
    }

    /// <summary>
    /// Why the checkered flag can't be thrown with the race in this state, in
    /// a sentence short enough for a phone - or null when it can, which is
    /// only while a race is running.
    ///
    /// Public for the race-day manager, which has to refuse before it hands
    /// a flag over, as it cannot hear a refusal afterwards - so the two never
    /// disagree about the rule or the words.
    /// </summary>
    public static string? CheckeredRefusal(RaceState state)
    {
        if (state == RaceState.Racing)
        {
            return null;
        }

        return state switch
        {
            RaceState.Staging or RaceState.Countdown => "The race hasn't gone green yet. To call off this start, press Reset.",
            RaceState.Finished => "The race has already finished.",
            _ => "No race is running.",
        };
    }

    /// <summary>
    /// Why a green can't be thrown with the race in this state and under this
    /// flag, in a sentence short enough for a phone - or null when it can.
    ///
    /// The one refusal is a race on its last lap, under the checkered: a green
    /// there cannot take the flag back in (see RaceControlSession.GreenFlag).
    /// A green over a green is not a refusal - nothing needs doing, and the
    /// Green action simply leaves it. Public for the race-day manager, for
    /// the same reason as <see cref="CheckeredRefusal"/>.
    /// </summary>
    public static string? GreenRefusal(RaceState state, FlagState flag) =>
        state == RaceState.Racing && flag == FlagState.Checkered
            ? "The checkered flag is out - the race is finishing."
            : null;

    private void CancelAutoStart()
    {
        _autoStartCts?.Cancel();
        _autoStartCts = null;
    }

    // ==================== Warm-up ====================

    /// <summary>
    /// Opens the warm-up for the race about to run, if its format has one,
    /// and arranges for <paramref name="then"/> to run when it is over.
    /// Returns false - and does nothing - when there is no warm-up to run:
    /// the format has none, practice is on, or a race is already staged,
    /// counting down or running.
    /// </summary>
    private bool TryBeginWarmUp(Action then)
    {
        if (_race.IsPracticeMode || _race.State is not (RaceState.Idle or RaceState.Finished))
        {
            return false;
        }

        var seconds = Math.Clamp(EffectiveFormat(out _).WarmUpSeconds, 0, RaceFormat.MaxWarmUpSeconds);
        if (seconds <= 0)
        {
            return false;
        }

        // Nothing of an earlier warm-up (or a half-started sequence) may
        // carry on underneath this one.
        CancelAutoStart();
        CancelWarmUp();

        var length = TimeSpan.FromSeconds(seconds);
        if (!_race.BeginWarmUp(length))
        {
            return false;
        }

        var cts = new CancellationTokenSource();
        _warmUpCts = cts;
        _afterWarmUp = then;

        var described = RaceFormat.DescribeWarmUp(seconds);
        _announcer.SpeakReminder($"Warm-up is open. {SpokenWarmUpLength(seconds)} of free laps before the race.");
        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Warm-up open - {described}. Laps are not counted. Press Stage again to skip the rest.");

        _lblRaceState.Text = "Warm-up";
        UpdateClockDisplay(force: true);

        _ = RunWarmUpTimerAsync(cts, length);
        return true;
    }

    /// <summary>
    /// Waits out the warm-up on the UI thread's context (so everything after
    /// each await is back on it), with a thirty-second call when there is
    /// time for one, then moves on.
    /// </summary>
    private async Task RunWarmUpTimerAsync(CancellationTokenSource cts, TimeSpan length)
    {
        var warning = TimeSpan.FromSeconds(30);

        try
        {
            if (length > TimeSpan.FromSeconds(60))
            {
                await Task.Delay(length - warning, cts.Token);

                if (ReferenceEquals(_warmUpCts, cts) && _race.IsWarmingUp)
                {
                    _announcer.SpeakReminder("Thirty seconds left in warm-up.");
                }

                await Task.Delay(warning, cts.Token);
            }
            else
            {
                await Task.Delay(length, cts.Token);
            }
        }
        catch (TaskCanceledException)
        {
            return; // skipped, reset, or replaced
        }

        if (IsDisposed || Disposing || !ReferenceEquals(_warmUpCts, cts))
        {
            return;
        }

        FinishWarmUp(skipped: false);
    }

    /// <summary>
    /// Ends the warm-up and runs what was waiting on it - but only if the
    /// warm-up was still the thing going on. If somebody pressed Countdown
    /// or Green (or the phone did) in the meantime, the session already
    /// moved on, and staging over the top of that would be wrong.
    /// </summary>
    private void FinishWarmUp(bool skipped)
    {
        var then = _afterWarmUp;
        var wasWarmingUp = _race.IsWarmingUp;

        _warmUpCts?.Cancel();
        _warmUpCts = null;
        _afterWarmUp = null;

        _race.EndWarmUp(skipped ? "Warm-up skipped." : "Warm-up over.");

        if (!wasWarmingUp || then is null || _race.State is not (RaceState.Idle or RaceState.Finished))
        {
            return;
        }

        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] {(skipped ? "Warm-up skipped" : "Warm-up over")} - staging.");

        _warmUpJustFinished = true;
        try
        {
            then();
        }
        finally
        {
            _warmUpJustFinished = false;
        }
    }

    /// <summary>Stage or Quick Race pressed during the warm-up: cut it short and start. True when there was one to skip.</summary>
    private bool SkipWarmUpIfRunning()
    {
        if (_warmUpCts is null || !_race.IsWarmingUp)
        {
            return false;
        }

        FinishWarmUp(skipped: true);
        return true;
    }

    /// <summary>Drops the warm-up without running what was waiting on it - Reset, or the page closing.</summary>
    private void CancelWarmUp()
    {
        _warmUpCts?.Cancel();
        _warmUpCts = null;
        _afterWarmUp = null;
        _race.EndWarmUp("Warm-up cancelled.");
    }

    /// <summary>The warm-up length as the announcer should say it.</summary>
    private static string SpokenWarmUpLength(int seconds)
    {
        var minutes = seconds / 60;
        var rest = seconds % 60;

        if (minutes == 0)
        {
            return Plural.Of(rest, "second");
        }

        return rest == 0
            ? Plural.Of(minutes, "minute")
            : $"{Plural.Of(minutes, "minute")} and {Plural.Of(rest, "second")}";
    }

    /// <summary>
    /// The session's warm-up opened or closed - from here, or because
    /// something moved the race on. Keeps the state label and the lit
    /// Stage button honest, and lets go of a timer whose warm-up is gone.
    /// </summary>
    private void OnWarmUpChanged()
    {
        if (!_race.IsWarmingUp && _warmUpCts is not null)
        {
            _warmUpCts.Cancel();
            _warmUpCts = null;
            _afterWarmUp = null;
        }

        if (IsDisposed || Disposing)
        {
            return;
        }

        _lblRaceState.Text = _race.IsWarmingUp ? "Warm-up" : _race.State.ToString();
        UpdateClockDisplay(force: true);
    }

    /// <summary>
    /// Runs Stage -> (wait) -> Countdown -> (wait) -> Green as one sequence
    /// instead of three separate manual clicks. Cancellable at any point by
    /// CancelAutoStart - if the person clicks any manual race-control button
    /// while a wait is in progress, the sequence simply stops where it is
    /// rather than firing a stale step on top of what they just did. (Not a
    /// checkered refused before the green, which does nothing - see
    /// RefuseCheckeredWithoutARace.)
    /// </summary>
    /// <summary>
    /// How long until the green, while the automatic start is opening its
    /// countdown - the one path that knows. Null otherwise: a green pressed
    /// by hand could come at any moment, and one that follows the
    /// countdown's audio comes when the ticks end anyway.
    /// </summary>
    private TimeSpan? _countdownGreenIn;

    private async Task RunAutoStartSequenceAsync()
    {
        CancelAutoStart();
        var cts = new CancellationTokenSource();
        _autoStartCts = cts;

        try
        {
            _race.Stage();
            var generation = _race.SessionGeneration;
            await Task.Delay(TimeSpan.FromSeconds(_fmt.StagingSeconds), cts.Token);

            // The same test as the chained start (see ChainAfterAudio): a
            // flag thrown, or the session changed, from anywhere that does
            // not go through CancelAutoStart still stops the sequence.
            if (!AutoStartMayContinue(generation, RaceState.Staging))
            {
                return;
            }

            // The countdown ticks end on the green, so the announcer is told
            // when that is - for this one call only. The state change that
            // passes it on runs inside StartCountdown, on this thread.
            _countdownGreenIn = TimeSpan.FromSeconds(_fmt.CountdownSeconds);
            try
            {
                _race.StartCountdown();
            }
            finally
            {
                _countdownGreenIn = null;
            }

            await Task.Delay(TimeSpan.FromSeconds(_fmt.CountdownSeconds), cts.Token);

            if (!AutoStartMayContinue(generation, RaceState.Countdown))
            {
                return;
            }

            // Let the countdown cue finish before the clock starts. The
            // delay above is just the configured countdown length and has
            // no relationship to how long the sound actually plays, so the
            // race used to go green while the cue was still sounding.
            _sessionStartedAtUtc = DateTime.UtcNow;

            // Nothing start-of-race here either: the gate below does it for
            // every path. This method is a second implementation of
            // StartGreenNow, and duplicating that work is what left a demo
            // with no release schedule.
            //
            // The session's own gate waits for audio - no separate wait here,
            // or the cue would be waited on twice.
            await _race.GreenFlagAfterAudioAsync();
        }
        catch (TaskCanceledException)
        {
            // Superseded by a manual button press or a second auto-start click - nothing further to do.
        }
        finally
        {
            if (_autoStartCts == cts)
            {
                _autoStartCts = null;
            }
        }
    }

    /// <summary>
    /// Whether an automatic start may take its next step: still the same
    /// session, still at the step it was waiting on, and no yellow or red
    /// out.
    ///
    /// The flag has to be checked on its own. A yellow or red does not
    /// change the race state, so a start that checked the state alone went
    /// green under a caution thrown during staging or the countdown.
    ///
    /// Only a caution stops it, not "any flag": a countdown started
    /// straight from a finished race still shows that race's checkered,
    /// since only Stage clears the flag there - and that start must still go
    /// green.
    ///
    /// A countdown started over a race that is running clears that race's
    /// flag (see RaceControlSession.StartCountdown), so a yellow or red seen
    /// here was thrown during this start. It used to read the abandoned
    /// race's own yellow as one, and a countdown confirmed over a race under
    /// caution stopped before the green every time.
    /// </summary>
    private bool AutoStartMayContinue(int generation, RaceState waitingIn)
    {
        if (IsDisposed || Disposing
            || _race.SessionGeneration != generation
            || _race.State != waitingIn)
        {
            // Somebody did something - a reset, a new stage, a start by
            // hand. That decision stands.
            return false;
        }

        if (_race.Flag is FlagState.Yellow or FlagState.Red)
        {
            // Said, because otherwise the start just seems to stall.
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [race] Automatic start stopped at {waitingIn} - the {_race.Flag.ToString().ToLowerInvariant()} flag is out. "
                + "Press Green when the track is ready, or Stage again to restart the sequence.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Refreshes the big clock's text. Only updates while the race is
    /// actually Racing - it naturally freezes at whatever it last showed
    /// once the race finishes, and force=true (used by Reset) explicitly
    /// snaps it back to zero rather than waiting for the next tick.
    /// </summary>
    private void UpdateClockDisplay(bool force = false)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        // The clock panel reads the session directly, so there's nothing to
        // push into it - it just needs repainting. Keeping the state in one
        // place is what let the elapsed/remaining split collapse into a
        // single display.
        _clock.Invalidate();
        _statStrip.Invalidate();
        UpdateFlagButtonColors(_race.Flag);
    }

    private void RefreshTrackLayoutCombo()
    {
        var previous = _cmbTrackLayout.SelectedItem as string ?? (_settings.LastRaceControlTrackName.Length > 0 ? _settings.LastRaceControlTrackName : null);
        _cmbTrackLayout.Items.Clear();
        _cmbTrackLayout.Items.Add("(none)");
        foreach (var t in _trackLayouts.All)
        {
            _cmbTrackLayout.Items.Add(t.Name);
        }
        var idx = previous is not null ? _cmbTrackLayout.Items.IndexOf(previous) : -1;
        _cmbTrackLayout.SelectedIndex = idx >= 0 ? idx : 0;
    }

    // Stored so they can be unsubscribed from the shared, long-lived
    // RaceControlSession when this window closes. Without this, closing
    // Race Control leaves these delegates subscribed forever - the session
    // outlives the window by design (it keeps running even with Race
    // Control closed), so a closed-and-disposed window would otherwise
    // keep silently receiving every future lap event and crash trying to
    // update controls that no longer exist.
    private Action<RaceState>? _stateChangedHandler;
    private Action<FlagState>? _flagChangedHandler;
    private Action? _warmUpChangedHandler;
    private Action<RacerStanding, bool, bool>? _lapProcessedHandler;
    private Action<RacerStanding>? _personalBestHandler;
    private Action<RacerStanding>? _standingAdjustedHandler;
    private Action<string, long>? _jumpStartHandler;
    private Action<RacerStanding, RaceWarning>? _warningIssuedHandler;
    private Action? _timeLimitReachedHandler;
    private Action? _raceCompletedHandler;

    /// <summary>
    /// The start gate that was wired before this page added its own - the
    /// host's wait for the announcer to finish speaking. Chained onto, and
    /// put back on teardown, rather than overwritten.
    /// </summary>
    private Func<Task>? _gateBeforeThisPage;
    private Action<string, long>? _driverReleasedHandler;

    private void WireRaceControl()
    {
        _stateChangedHandler = state =>
        {
            if (InvokeRequired) { BeginInvoke(() => OnRaceStateChanged(state)); return; }
            OnRaceStateChanged(state);
        };
        _race.StateChanged += _stateChangedHandler;

        _flagChangedHandler = flag =>
        {
            if (InvokeRequired) { BeginInvoke(() => OnFlagChanged(flag)); return; }
            OnFlagChanged(flag);
        };
        _race.FlagChanged += _flagChangedHandler;

        _warmUpChangedHandler = () =>
        {
            if (InvokeRequired) { BeginInvoke(OnWarmUpChanged); return; }
            OnWarmUpChanged();
        };
        _race.WarmUpChanged += _warmUpChangedHandler;

        _lapProcessedHandler = (standing, isValid, isFastestLap) =>
        {
            if (InvokeRequired) { BeginInvoke(() => OnLapProcessed(standing, isValid, isFastestLap)); return; }
            OnLapProcessed(standing, isValid, isFastestLap);
        };
        _race.LapProcessed += _lapProcessedHandler;

        _personalBestHandler = standing =>
        {
            if (InvokeRequired) { BeginInvoke(() => _announcer.NotifyPersonalBestLap(standing.TransponderId, standing.FastestLapMs)); return; }
            _announcer.NotifyPersonalBestLap(standing.TransponderId, standing.FastestLapMs);
        };
        _race.PersonalBestLap += _personalBestHandler;

        _standingAdjustedHandler = standing =>
        {
            if (InvokeRequired) { BeginInvoke(() => OnStandingAdjusted(standing)); return; }
            OnStandingAdjusted(standing);
        };
        _race.StandingAdjusted += _standingAdjustedHandler;

        _jumpStartHandler = (transponder, penaltyMs) =>
        {
            if (InvokeRequired) { BeginInvoke(() => OnJumpStart(transponder, penaltyMs)); return; }
            OnJumpStart(transponder, penaltyMs);
        };
        _race.JumpStartDetected += _jumpStartHandler;

        _warningIssuedHandler = (standing, warning) =>
        {
            if (InvokeRequired) { BeginInvoke(() => OnWarningIssued(standing, warning)); return; }
            OnWarningIssued(standing, warning);
        };
        _race.WarningIssued += _warningIssuedHandler;

        _timeLimitReachedHandler = () => _announcer.NotifyRaceTimeOver();
        _race.TimeLimitReached += _timeLimitReachedHandler;

        // The call first, the save whatever the call does - see RaceFiling.AfterTheFlag.
        _raceCompletedHandler = () => RaceFiling.AfterTheFlag(
            _announcer.NotifyRaceCompleted,
            () =>
            {
                if (InvokeRequired) { BeginInvoke(SaveCompletedSessionToHistory); return; }
                SaveCompletedSessionToHistory();
            },
            message => StatusMessage?.Invoke(message));
        _race.RaceCompleted += _raceCompletedHandler;

        // The one place every start path meets.
        //
        // The Green button, the automated Stage->Countdown->Green sequence,
        // a demo and a quick race all reach the flag through
        // GreenFlagAfterAudioAsync, and it awaits this gate before dropping
        // it. Start-of-race work belongs here rather than in any one
        // caller: it used to sit in the Green button's own handler, and a
        // demo - which never presses that button - silently started without
        // a release schedule.
        //
        // Only ever runs for a REAL start. The session skips the gate when
        // it is already racing, so a restart after a caution can't
        // re-stagger a field that is already spread around the track.
        // CHAINED onto whatever was already there, not assigned over it.
        //
        // BeforeGreenFlagAsync is a single delegate, not an event, and
        // MainForm wires it first - to wait for the announcer to finish
        // speaking before the green, so a start never talks over its own
        // countdown call. This page is built afterwards, and assigning its
        // own gate simply REPLACED that one. The wait for audio had not
        // run before a green flag since this was written, and nothing
        // said so: the only symptom is a start that tramples the call.
        var hostGate = _race.BeforeGreenFlagAsync;
        _gateBeforeThisPage = hostGate;

        _race.BeforeGreenFlagAsync = async () =>
        {
            _lastUnlocks = new List<AchievementUnlock>();

            // Reads on-screen values - the gap box, the selected round - so
            // it has to be on the UI thread. The gate is awaited from
            // wherever the start happened to be triggered from.
            //
            // Never for practice. Open lapping has no grid and no start
            // procedure, and running this there handed it the race
            // format's staggered or hand-released start - cars held at the
            // line, or their laps timed from a release nobody called.
            if (!IsDisposed && !Disposing && !_race.IsPracticeMode)
            {
                if (InvokeRequired)
                {
                    Invoke(new Action(ApplyStartRules));
                }
                else
                {
                    ApplyStartRules();
                }
            }

            // Then the host's gate - the audio wait. Rules first because
            // they are instant and read what is on screen now; the wait
            // is the part that takes time.
            if (hostGate is not null)
            {
                await hostGate();
            }
        };

        _driverReleasedHandler = (transponder, offsetMs) =>
        {
            if (InvokeRequired) { BeginInvoke(() => OnDriverReleased(transponder, offsetMs)); return; }
            OnDriverReleased(transponder, offsetMs);
        };
        _race.DriverReleased += _driverReleasedHandler;
    }


    /// <summary>
    /// Speaks an LLM-written wrap-up line for a finished race.
    ///
    /// Deliberately fire-and-forget and deliberately AFTER the save: the
    /// results are already safely in history before this runs, so a slow or
    /// broken model can't delay or endanger them. The announcer's own
    /// "race completed" call has already gone out too, meaning this is
    /// strictly additive - if it never arrives, nothing is missing.
    /// </summary>
    /// <summary>
    /// Writes the recap AND renders it to audio as one job, so "ready"
    /// means there is something to play rather than something still to
    /// synthesise. Returns nulls when the model gave nothing back.
    /// </summary>
    private async Task<(string? Spoken, AnnouncementService.PreparedSpeech? Audio)> BuildRecapAsync(
        Task<string?> generation, (double, double)? delivery)
    {
        var line = await generation;

        if (string.IsNullOrWhiteSpace(line))
        {
            return (null, null);
        }

        // Tags added here rather than by the model, so an Enthusiastic
        // recap is excited every time instead of only when the model
        // decides the race deserved it.
        var spoken = _commentary!.PrepareSummaryForSpeech(line);

        // Null when voice isn't configured or the render failed - the
        // caller falls back to speaking it live.
        var audio = await _announcer.PrepareReminderAsync(spoken, delivery);

        return (spoken, audio);
    }

    /// <summary>
    /// How many badge calls a single race may make out loud.
    ///
    /// A driver's first ever race can unlock several at once, and reading
    /// out six in a row turns a celebration into a list. The rest are still
    /// earned and still shown - they just aren't announced.
    /// </summary>
    private const int MaxSpokenAchievements = 3;

    /// <summary>
    /// Works out what the race just earned everyone and says the best of it.
    ///
    /// Deliberately after the result is saved and before the wrap-up: the
    /// evaluator needs the finished result, and the calls should land with
    /// the finish rather than a minute later behind the recap.
    ///
    /// Never allowed to interrupt the end of a race - badges are decoration,
    /// and a bad one must not be the reason a result goes unsaved.
    /// </summary>
    private void AwardAchievements(SessionRecord record)
    {
        try
        {
            // Loaded per race rather than held: this is the only place in
            // Race Control that touches it, and once a race is not a cost
            // worth widening the constructor for.
            var store = AchievementStore.Load();

            var unlocks = AchievementEvaluator.EvaluateFinishedRace(
                record, _sessionHistory.All, store, RivalryStore.Load());

            if (unlocks.Count == 0)
            {
                return;
            }

            foreach (var unlock in unlocks)
            {
                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [badge] {unlock.DriverName} unlocked \"{unlock.Badge.Name}\""
                    + (string.IsNullOrWhiteSpace(unlock.Detail) ? "." : $" ({unlock.Detail})."));
            }

            _lastUnlocks = unlocks;
            _achievements = store;

            AchievementsUnlocked?.Invoke(unlocks);

            foreach (var unlock in unlocks.Take(MaxSpokenAchievements))
            {
                _announcer.NotifyAchievement(unlock.Transponder, unlock.Badge.Name);
            }

            if (unlocks.Count > MaxSpokenAchievements)
            {
                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [badge] {unlocks.Count - MaxSpokenAchievements} more unlocked but not announced - they're on the driver's record.");
            }
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [badge] Couldn't work out achievements for this race: {ex.Message}");
        }
    }

    /// <summary>Raised with everything a finished race unlocked, for anything that wants to show it.</summary>
    public event Action<List<AchievementUnlock>>? AchievementsUnlocked;

    /// <summary>
    /// What the race just finished unlocked, held so the results card can
    /// print it. Cleared at each green, or a card would carry the last
    /// race's badges.
    /// </summary>
    private List<AchievementUnlock> _lastUnlocks = new();

    /// <summary>
    /// Reloaded whenever badges change, so the Badges column on the board
    /// shows the number people just heard announced rather than the one from
    /// when the page was built.
    /// </summary>
    private AchievementStore _achievements = AchievementStore.Load();

    private void SpeakRaceSummary(SessionRecord record)
    {
        // The teaser is fixed text and needs nothing from the LLM, so it is
        // NOT behind the commentary guard. It used to be, which meant an
        // unreachable Ollama silently killed the teaser too - the actual
        // reason it was never heard.
        // One of the configured teasers, picked at random - the same line
        // after every race is what makes an announcer sound canned.
        var teaserLines = _settings.EffectiveTeaserLines;
        var wantsTeaser = _settings.SummaryTeaserEnabled && teaserLines.Count > 0;

        var commentaryAvailable = _commentary is not null
                                  && _commentary.IsAvailable
                                  && _settings.CommentaryRaceSummaries;

        if (!wantsTeaser && !commentaryAvailable)
        {
            // Nothing more to say - but the broadcast is only over once the
            // finish call has been spoken. Returning without the signal left
            // music set to fade or stop "after the recap" playing for the
            // rest of the night whenever both teaser and recap were off.
            SignalSessionAudioFinishedAfterFinishCall();
            return;
        }

        // Worked out HERE, on the UI thread, rather than inside the
        // background job below. It reads the history, the programme and the
        // event stores, and the caller writes to the programme the moment
        // this method returns - which it does immediately, since everything
        // after this line is queued work.
        // Decoration for the recap, and worked out over every stored season -
        // so guarded. This runs on the save path BEFORE the heat is linked to
        // its round and the crash journal is cleared; a fault here used to
        // skip both, leaving a saved race that looked unrun and was offered
        // back for "recovery" (and a second save) at the next launch.
        ChampionshipSwing? championship = null;

        if (commentaryAvailable)
        {
            try
            {
                championship = Championship.SwingFor(record, _seasons, _rounds, _events, _sessionHistory, _profiles);
            }
            catch (Exception ex)
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] Recap goes without the standings - they could not be worked out: {ex.Message}");
            }
        }

        if (championship is not null)
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [commentary] Recap has the {championship.SeasonName} standings: "
                + string.Join(", ", championship.Moves.Select(m => $"P{m.Position} {m.DriverName}")));
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var sequenceStarted = DateTime.UtcNow;

                // The model starts work at the flag, not after the teaser.
                // Everything below - the finish call, the pause, the teaser -
                // is time the recap can be generating in, and it used to sit
                // idle through all of it.
                var generation = commentaryAvailable
                    ? _commentary!.BuildRaceSummaryAsync(record, championship)
                    : null;

                if (generation is not null)
                {
                    StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] Asking the model for a recap...");
                }

                var generationStarted = DateTime.UtcNow;

                // 1. Let the finish call and the cheering actually FINISH.
                //
                // The configured pause is a pause after the announcer stops
                // talking - that is what it reads as on the Commentary tab.
                // It used to start the instant the race ended, so it ran out
                // while the finish line was still being spoken and the teaser
                // simply queued up behind it. A 4 second setting could
                // comfortably play twenty seconds later without anything
                // being wrong except the thing being measured.
                await _announcer.WaitForAudioAsync(TimeSpan.FromSeconds(30), CancellationToken.None);

                // 2. Render the teaser DURING the pause, not after it.
                //
                // Synthesis is a network round trip, and it used to land on
                // top of the configured pause rather than inside it. Starting
                // it here means the pause is the only thing being waited for.
                var teaser = wantsTeaser ? teaserLines[Random.Shared.Next(teaserLines.Count)] : null;
                var teaserRender = teaser is null ? null : _announcer.PrepareReminderAsync(teaser);

                var flagPause = Math.Clamp(_settings.SummaryDelaySeconds, 0, 120);
                if (flagPause > 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(flagPause));
                }

                // Punchier delivery for this one line. The words were
                // already broadcast-shaped; the flatness came from the
                // voice settings, where style exaggeration sits at 0 by
                // default and stability at a middling 0.5.
                //
                // Applied as-is, not merged with the profile. Merging took
                // the more EXPRESSIVE of the two, which pulled a cranked
                // announcer profile straight back in - and an
                // over-expressive recap is the cheesiness this setting now
                // exists to avoid.
                (double, double)? delivery = _settings.SummaryUseBroadcastVoice
                    ? (_settings.SummaryVoiceStability, _settings.SummaryVoiceStyle)
                    : null;

                // Written AND rendered as one job, so "the recap is ready"
                // means there is audio in hand, not just text.
                var recapTask = generation is null
                    ? null
                    : BuildRecapAsync(generation, delivery);

                // 3. The teaser, straight after the finish call.
                //
                // Deliberately NOT held back until the recap is ready. That
                // was tried: it does make teaser and recap run back to back,
                // but it buys that by moving the silence in FRONT of the
                // teaser, and a teaser arriving long after the checkered
                // flag reads as the app having stalled. Right after the
                // finish call is where it belongs - the wait for the model
                // then sits behind it, which is what a "stand by" line is
                // there to cover in the first place.
                if (teaser is not null)
                {
                    var prepared = teaserRender is null ? null : await teaserRender;

                    if (prepared is not null)
                    {
                        _announcer.SpeakPrepared(prepared);
                    }
                    else
                    {
                        // Voice not configured, muted, or the render failed -
                        // the teaser still speaks, just with the round trip
                        // in front of it.
                        _announcer.SpeakReminder(teaser);
                    }

                    StatusMessage?.Invoke(
                        $"{AppTime.LogStampNow()}  [commentary] Teaser spoken {(DateTime.UtcNow - sequenceStarted).TotalSeconds:0.0}s after the flag "
                        + $"({flagPause}s pause, {(prepared is null ? "rendered live" : "rendered during the pause")}): \"{teaser}\"");
                }
                else
                {
                    StatusMessage?.Invoke(_settings.SummaryTeaserEnabled
                        ? $"{AppTime.LogStampNow()}  [commentary] No teaser - every teaser line is empty on the Commentary tab."
                        : $"{AppTime.LogStampNow()}  [commentary] No teaser - it's switched off on the Commentary tab.");
                }

                if (!commentaryAvailable)
                {
                    StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] No recap - the local model isn't reachable.");

                    // The end is signalled in the finally below, once the
                    // teaser has actually been spoken.
                    await _announcer.WaitForAudioAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
                    return;
                }

                // 4. A beat after the teaser has been SPOKEN, not after it
                // was queued. Queuing it only means the audio exists; the
                // words still have to play, and starting the gap at the queue
                // meant the gap ran out during the teaser and the recap
                // followed it with no air at all.
                await _announcer.WaitForAudioAsync(TimeSpan.FromSeconds(30), CancellationToken.None);

                // Started, not awaited. Anything still outstanding on the
                // recap runs alongside the gap rather than after it - the
                // gap is the only thing actually waited for.
                var teaserEnded = DateTime.UtcNow;
                var teaserGap = Math.Clamp(_settings.SummaryTeaserGapSeconds, 0, 60);
                var gap = teaser is not null && teaserGap > 0
                    ? Task.Delay(TimeSpan.FromSeconds(teaserGap))
                    : Task.CompletedTask;

                var recap = await recapTask!;
                var tookSeconds = (DateTime.UtcNow - generationStarted).TotalSeconds;

                if (recap.Spoken is null)
                {
                    // Silence was the only symptom before: the recap simply
                    // never arrived and nothing said why.
                    StatusMessage?.Invoke(
                        $"{AppTime.LogStampNow()}  [commentary] No recap after {tookSeconds:0.0}s - the model returned nothing. "
                        + "Any [llm] line above says whether it timed out, refused, or wasn't reachable.");

                    // The end is signalled in the finally below, once the
                    // teaser has actually been spoken.
                    await _announcer.WaitForAudioAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
                    return;
                }

                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [commentary] Recap written and rendered in {tookSeconds:0.0}s.");

                await gap;

                // Deep duck for the recap. Raised BEFORE it's queued and
                // released after the voice queue falls quiet, so it covers
                // the whole segment rather than flapping per sentence.
                DeepDuckRequested?.Invoke(true);

                if (recap.Audio is not null)
                {
                    // Long tail: this is the longest line of the night and
                    // its last words were being clipped.
                    _announcer.SpeakPrepared(recap.Audio, AnnouncementService.SpeechTail.Long);
                }
                else
                {
                    // Render failed, or voice isn't configured - the recap
                    // still speaks, just with the round trip in front of it.
                    _announcer.SpeakReminder(recap.Spoken, delivery);
                }

                StatusMessage?.Invoke(
                    $"{AppTime.LogStampNow()}  [commentary] Recap queued {(DateTime.UtcNow - teaserEnded).TotalSeconds:0.0}s after the teaser finished "
                    + $"({teaserGap}s gap set, {(recap.Audio is null ? "rendered live" : "already rendered")}).");

                // Music handled AFTER the recap, not at the flag. Waiting
                // for the voice queue means the fade starts when the
                // broadcast actually ends rather than cutting across it.
                await _announcer.WaitForAudioAsync(TimeSpan.FromSeconds(60), CancellationToken.None);

                DeepDuckRequested?.Invoke(false);
            }
            catch
            {
                // Released here as well: a synthesis failure mid-recap would
                // otherwise leave the bed ducked for the rest of the night.
                DeepDuckRequested?.Invoke(false);

                // Colour commentary is decoration - never let it surface as
                // an error on a page that just finished a race.
            }
            finally
            {
                // Exactly once, however the sequence above ended - recap
                // spoken, no recap, or a failure part-way. Music set to fade
                // after the recap has nothing else to wait for, so any path
                // that skipped this left it playing for the rest of the
                // night; and raising it in more than one place risked a
                // second fade or stop landing on the next race's music.
                RaiseSessionAudioFinished();
            }
        });
    }

    /// <summary>
    /// Signals the end of the broadcast for a race that has no recap
    /// sequence to run - teaser and recap both off, or a race that finished
    /// without being saved. Waits for the finish call to be spoken first, so
    /// music set to fade afterwards doesn't cut across it. Runs in the
    /// background: the caller is on the UI thread and must not block.
    /// </summary>
    private void SignalSessionAudioFinishedAfterFinishCall()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _announcer.WaitForAudioAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
            }
            catch
            {
                // The wait is only there to be polite to the finish call -
                // if it fails, the music still has to be told.
            }
            finally
            {
                RaiseSessionAudioFinished();
            }
        });
    }

    /// <summary>
    /// Raises SessionAudioFinished without letting a failing handler escape
    /// into a background task, where nobody would ever see it.
    /// </summary>
    private void RaiseSessionAudioFinished()
    {
        try
        {
            SessionAudioFinished?.Invoke();
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [audio] End-of-broadcast signal failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Speaks an LLM-written "coming up" line for a round about to start.
    /// Called from the staging path, where there's time for it before the
    /// countdown begins.
    /// </summary>
    private void SpeakRoundIntro()
    {
        if (_commentary is null || !_commentary.IsAvailable || !_settings.CommentaryRaceIntros)
        {
            return;
        }

        var round = _activeRound?.Invoke();
        if (round is null)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                var line = await _commentary.BuildRoundIntroAsync(round);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    _announcer.SpeakReminder(line);
                }
            }
            catch
            {
                // As above - decoration only.
            }
        });
    }

    private void UnwireRaceControl()
    {
        if (_stateChangedHandler is not null) _race.StateChanged -= _stateChangedHandler;
        if (_flagChangedHandler is not null) _race.FlagChanged -= _flagChangedHandler;
        if (_warmUpChangedHandler is not null) _race.WarmUpChanged -= _warmUpChangedHandler;
        if (_lapProcessedHandler is not null) _race.LapProcessed -= _lapProcessedHandler;
        if (_personalBestHandler is not null) _race.PersonalBestLap -= _personalBestHandler;
        if (_standingAdjustedHandler is not null) _race.StandingAdjusted -= _standingAdjustedHandler;
        if (_jumpStartHandler is not null) _race.JumpStartDetected -= _jumpStartHandler;
        if (_warningIssuedHandler is not null) _race.WarningIssued -= _warningIssuedHandler;
        if (_timeLimitReachedHandler is not null) _race.TimeLimitReached -= _timeLimitReachedHandler;
        if (_raceCompletedHandler is not null) _race.RaceCompleted -= _raceCompletedHandler;
        if (_driverReleasedHandler is not null) _race.DriverReleased -= _driverReleasedHandler;

        // Handed back, not cleared. The session outlives this page, and a
        // gate pointing at a disposed control would run start-of-race work
        // against controls that are gone - but setting it to null also
        // threw away the host's audio wait, which belongs to the session
        // rather than to this page.
        _race.BeforeGreenFlagAsync = _gateBeforeThisPage;
    }


    /// <summary>Announces a jump start, and refreshes so the flag shows on the leaderboard.</summary>
    private void OnJumpStart(string transponder, long penaltyMs)
    {
        _announcer.NotifyJumpStart(transponder, penaltyMs);
        RenderLeaderboard();
    }

    /// <summary>
    /// Pushes the racing class's jump-start rules into the session. The
    /// session is class-agnostic, so this has to happen each time a race
    /// starts rather than once at construction.
    /// </summary>
    private void ApplyJumpStartRules()
    {
        var round = _activeRound?.Invoke();
        var raceClass = round is not null && _raceClasses is not null
            ? _raceClasses.All.FirstOrDefault(rc => string.Equals(rc.Name, round.ClassName, StringComparison.OrdinalIgnoreCase))
            : null;

        _race.DetectJumpStarts = raceClass?.DetectJumpStarts ?? true;
        _race.JumpStartPenaltyMs = Math.Max(0, (raceClass?.JumpStartPenaltySeconds ?? 0) * 1000L);

        // Default false: a jump start is a time penalty, not a lost lap,
        // unless the class says otherwise.
        _race.JumpStartCostsTheLap = raceClass?.JumpStartCostsTheLap ?? false;

        // Class-agnostic session again: this class's warning ladder, or the
        // default when there is no class or it never set one.
        _race.WarningLadder = raceClass?.WarningLadder;

        if (_race.DetectJumpStarts)
        {
            StatusMessage?.Invoke(_race.JumpStartPenaltyMs > 0
                ? $"[race] Jump-start detection on, {_race.JumpStartPenaltyMs / 1000.0:0.0}s automatic penalty."
                : "[race] Jump-start detection on, flagging only.");
        }
    }

    /// <summary>
    /// A staggered driver's turn to go - their delay is up, or they were
    /// just released by hand.
    /// </summary>
    private void OnDriverReleased(string transponder, long offsetMs)
    {
        _announcer.NotifyDriverRelease(transponder);

        var profile = _profiles.Get(transponder);
        var name = string.IsNullOrWhiteSpace(profile?.DriverName) ? transponder : profile!.DriverName;

        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Released {name} at +{offsetMs / 1000.0:0.0}s.");

        // The board shows who is still waiting, so it needs redrawing the
        // moment that changes.
        RenderLeaderboard();
    }


    /// <summary>
    /// Everything the start needs settled before the flag drops: who waits
    /// how long, and their release calls rendered ready to play.
    ///
    /// Called on a real green only - a restart after a caution must not
    /// re-stagger a field that is already spread around the track.
    /// </summary>
    private void ApplyStartRules()
    {
        // A green thrown straight from idle never went through staging,
        // where the tech warning is normally given - so give it here. Read
        // before the flag drops, so State is still what the start came from.
        if (_race.State is not (RaceState.Staging or RaceState.Countdown))
        {
            WarnAboutUninspectedDrivers();
        }

        BuildStartSchedule();

        // Rendered now, while the cars are still coming to the line. The
        // grid is known the moment the schedule is, so every release call
        // can be audio in memory before the first one is needed - a release
        // is the one line where a second of synthesis is a second of a car
        // sitting still waiting to be told.
        if (_race.StartType != RaceStartType.Standard)
        {
            _announcer.PrimeReleaseLines(_race.ReleaseRoster());
        }
    }

    /// <summary>
    /// The grid a staggered start orders itself by.
    ///
    /// The selected round's roster first, since that is someone stating who
    /// is in this race. Failing that, the last race's finishing order -
    /// which is the natural grid for the next heat anyway, and is what makes
    /// a staggered start work at a club night that isn't running a formal
    /// race program. Requiring a round was silently turning every staggered
    /// start into a standard one.
    /// </summary>
    private List<string> GridForStagger()
    {
        var roster = _activeRound?.Invoke()?.AssignedTransponders?
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();

        if (roster.Count > 0)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Grid from the selected round: {Plural.Of(roster.Count, "car")}.");
            return roster;
        }

        // Never a saved demo: its grid is the simulator's made-up cars.
        var lastRace = _sessionHistory.All
            .FirstOrDefault(r => r.SessionType == RaceSessionType.Race && !r.IsDemoSession() && r.Results.Count > 0);

        if (lastRace is null)
        {
            return new List<string>();
        }

        var fromResult = lastRace.Results
            .OrderBy(r => r.Position)
            .Select(r => r.Transponder)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (fromResult.Count > 0)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] No round selected - grid taken from the last race ({lastRace.Label}): {Plural.Of(fromResult.Count, "car")}.");
        }

        return fromResult;
    }

    /// <summary>
    /// The clock a handicap start replaced with the shared one, so it can
    /// be put back for the next race. Null when nothing was replaced.
    /// </summary>
    private StaggerClock? _clockBeforeHandicapOverride;

    /// <summary>
    /// Works out who waits how long, and hands the session the schedule.
    ///
    /// The grid order is the round's assigned transponders, in the order
    /// they were assigned. With no round selected there is no grid, so a
    /// fixed-gap stagger has nothing to order by and falls back to a
    /// standard start rather than inventing an order from whoever happens
    /// to cross first.
    /// </summary>
    private void BuildStartSchedule()
    {
        // A handicap start moves the session onto one shared clock for
        // that race only. Put back whatever it replaced before this race is
        // set up, or the NEXT race - a grid stagger on each driver's own
        // clock, say - would quietly run on the handicap's shared one.
        if (_clockBeforeHandicapOverride is { } clockBefore)
        {
            _race.StaggerClockMode = clockBefore;
            _clockBeforeHandicapOverride = null;
        }

        var type = _race.StartType;

        // Resolved for EVERY start type, staggered or not.
        //
        // A standard start needs no schedule, but it still has a grid, and
        // knowing where everyone started is what makes "won from the back"
        // answerable afterwards. Empty when nothing could supply one, which
        // leaves start positions at zero rather than invented.
        var startingGrid = GridForStagger();
        _race.SetGrid(startingGrid);

        if (type == RaceStartType.Standard)
        {
            _race.SetReleaseSchedule(new Dictionary<string, long>());
            return;
        }

        if (type == RaceStartType.Qualifier)
        {
            // Everyone goes on the green; the per-driver clocks zero at
            // each driver's own first crossing, inside the session.
            _race.SetReleaseSchedule(new Dictionary<string, long>());
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Qualifying start - everyone on the green, "
                + "each driver's clock runs from their own first crossing.");
            return;
        }

        if (type == RaceStartType.HandReleased)
        {
            // Nothing scheduled: every driver waits until released by hand.
            _race.SetReleaseSchedule(new Dictionary<string, long>());

            var handGrid = startingGrid;

            StatusMessage?.Invoke(handGrid.Count > 0
                ? $"{AppTime.LogStampNow()}  [race] Hand-released start - {Plural.Of(handGrid.Count, "car")} held at the line. Right-click a driver on the leaderboard to release them."
                : $"{AppTime.LogStampNow()}  [race] Hand-released start needs a round with an assigned grid - there's nobody to hold, so everyone goes on the green.");
            return;
        }

        if (type == RaceStartType.StaggeredHandicap)
        {
            // Loaded here rather than injected: this is the only place in
            // Race Control that needs handicaps, and once per race start is
            // not a cost worth widening the constructor for.
            var handicaps = HandicapStore.Load();
            var schedule = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

            // A time credit is what a SLOWER driver is given - Suggest
            // works it out as their gap to the quickest driver over a race,
            // and the quickest gets none. So this is a pursuit start: the
            // biggest credit goes first, on the green, and everyone else
            // waits by however much less credit they have, the scratch
            // drivers longest of all.
            //
            // It used to hold each driver back BY their credit, which is the
            // exact opposite: the slowest car waited longest and the fastest
            // went on the green, so the handicap widened the gap it was set
            // to close - by twice the credit on a shared clock.
            //
            // The field is the grid when there is one, so a driver with no
            // handicap entry at all is still held as scratch. Without a grid
            // only the drivers with a handicap entry can be scheduled, and
            // anyone else goes on the green.
            var credits = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in handicaps.All)
            {
                if (!string.IsNullOrWhiteSpace(entry.Transponder))
                {
                    credits[entry.Transponder] = (long)Math.Round(Math.Max(0, entry.TimeCreditSeconds) * 1000);
                }
            }

            var pursuitField = startingGrid.Count > 0
                ? startingGrid
                : credits.Keys.ToList();

            long CreditOf(string transponder) => credits.TryGetValue(transponder, out var ms) ? ms : 0;

            var maxCredit = pursuitField.Count > 0 ? pursuitField.Max(t => CreditOf(t)) : 0;

            if (maxCredit > 0)
            {
                foreach (var transponder in pursuitField)
                {
                    schedule[transponder] = maxCredit - CreditOf(transponder);
                }
            }

            // A handicap start only means anything on ONE clock. On each
            // driver's own clock, every car is timed from its own release,
            // so a head start is worth nothing and the result is decided on
            // raw pace - the handicap cancels itself out. Shared green is
            // what makes the head start count, and first across the line win.
            // Set for this race only: the format keeps whatever it says, and
            // is pushed back in between races as usual.
            if (maxCredit > 0 && _race.StaggerClockMode != StaggerClock.SharedGreen)
            {
                _clockBeforeHandicapOverride = _race.StaggerClockMode;
                _race.StaggerClockMode = StaggerClock.SharedGreen;
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Handicap start runs on one clock from the green - "
                    + "on each driver's own clock the head start would count for nothing.");
            }

            _race.SetReleaseSchedule(schedule);
            StatusMessage?.Invoke(maxCredit > 0
                ? $"{AppTime.LogStampNow()}  [race] Handicap start (pursuit) - biggest credit away first, "
                  + $"scratch drivers held {maxCredit / 1000.0:0.0}s"
                  + (startingGrid.Count > 0 ? "." : ". No grid known - anyone without a handicap entry goes on the green.")
                : $"{AppTime.LogStampNow()}  [race] Handicap start selected, but no driver has a time credit - everyone goes on the green.");

            // The same credit taken off the result as well would count it
            // twice - once as the head start, once off the total.
            if (maxCredit > 0
                && _activeRound?.Invoke() is { } handicapRound
                && handicaps.ModeFor(handicapRound.ClassName) == HandicapMode.TimeCredit)
            {
                StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Note: {handicapRound.ClassName} also takes time credits off "
                    + "the result - with a handicap start as well, each credit counts twice. Set the class's handicap mode to None "
                    + "for a handicap start.");
            }

            return;
        }

        var grid = startingGrid;

        if (grid.Count == 0)
        {
            _race.SetReleaseSchedule(new Dictionary<string, long>());
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Staggered start has no grid to order by - "
                + "select a round with an assigned roster on Race Day \u2192 Program, or run one race so the last "
                + "result can be used. Starting everyone together instead.");
            return;
        }

        var gapMs = Math.Max(1, _fmt.StaggerGapSeconds) * 1000L;
        var staggered = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < grid.Count; i++)
        {
            // Reverse grid is a pursuit start: the back of the grid goes
            // first and the quick drivers have to come through the field.
            var place = _race.StartType == RaceStartType.StaggeredReverse
                ? grid.Count - 1 - i
                : i;

            staggered[grid[i]] = place * gapMs;
        }

        _race.SetReleaseSchedule(staggered);
        StatusMessage?.Invoke(
            $"{AppTime.LogStampNow()}  [race] {(_race.StartType == RaceStartType.StaggeredReverse ? "Reverse-grid" : "Grid-order")} start - "
            + $"{Plural.Of(grid.Count, "car")}, {gapMs / 1000.0:0.#}s apart, "
            + $"{(_race.StaggerClockMode == StaggerClock.OwnRelease ? "each clock from their own release" : "one clock from the green")}.");
    }

    private void OnRaceStateChanged(RaceState state)
    {
        // Stage and Countdown light with the start's own progress.
        UpdateFlagButtonColors(_race.Flag);

        // Staging already clears the session's standings, but the grid only
        // redraws on a lap event - so the previous race's rows stayed on
        // screen through staging and the countdown, right when everyone is
        // looking at the board. Clearing here empties it the moment cars
        // are called to the grid.
        if (state == RaceState.Staging)
        {
            _dgvLeaderboard.Rows.Clear();

            // The announcer keeps its own copy of the standings for leader
            // changes and position reports, and nothing cleared it between
            // races on this path - only a Next Level Timing connect did. The
            // last race's drivers stayed in its running order with their
            // final lap counts, so a new race could open with a false
            // "new leader" call and a position report naming someone who
            // wasn't even on track (or never fire, stuck on an old lap).
            _announcer.ResetSession();

            // A new race is being called, so no start time from before it -
            // a practice, a reset or an abandoned race - may carry into it.
            // The Green button and the automatic start stamp their own; a
            // demo's green does not, and took the stale one through the
            // "??=" below. (Undoing an abandon puts the old one back.)
            _sessionStartedAtUtc = null;
        }

        // Captured here rather than in the Green button handler: a demo
        // drives the session directly and never presses that button, so
        // a button-only capture always read false for simulations.
        if (state == RaceState.Racing)
        {
            _currentRaceIsDemo = _race.IsDemoMode;

            // A fresh start clears the standings in the session, but the
            // grid only redraws on a lap event - so last race's rows sat
            // there until someone crossed the line. Clear it now, and only
            // for a genuine start: resuming from a caution must keep the
            // laps already counted.
            if (_race.GetOrderedStandings().Count == 0)
            {
                _dgvLeaderboard.Rows.Clear();

                // Practice and quick starts skip staging - same reset.
                _announcer.ResetSession();
            }

            // Also covers the demo and remote paths, which don't go through
            // the Green button where this used to be set.
            _sessionStartedAtUtc ??= DateTime.UtcNow;
        }

        _lblRaceState.Text = state.ToString();

        // Posted, not called. A lap is what ends a race, so this handler
        // can be sitting inside RecordLap right now - see
        // PromptForUnnamedTransponders.
        if (state is (RaceState.Finished or RaceState.Idle) && IsHandleCreated && !IsDisposed)
        {
            BeginInvoke(() => PromptForUnnamedTransponders());
        }

        switch (state)
        {
            case RaceState.Staging:
                _announcer.NotifyRaceStaging();
                UpdateClockDisplay(force: true);

                // Here rather than in the Stage button, so the automatic
                // sequence (Quick Race, Start Next) is covered too.
                WarnAboutUninspectedDrivers();
                break;
            case RaceState.Countdown:
                // Set only while the automatic start opens the countdown -
                // it alone knows when the green will drop. See
                // _countdownGreenIn.
                _announcer.NotifyCountdownStarted(_countdownGreenIn);
                break;
        }
    }

    /// <summary>
    /// Writes the crash journal now rather than at the next throttled
    /// moment - for the window closing mid-race, so the last crossings
    /// before the app went away are the ones offered back next time.
    /// </summary>
    public void FlushJournal() => WriteJournal(force: true);

    private void OnFlagChanged(FlagState flag)
    {
        // A flag is a moment worth being able to come back to, so this one
        // is written straight away rather than waiting for the next lap.
        WriteJournal(force: true);

        _lblFlag.Text = flag.ToString();
        // Bright variants, not the dark ones these used to be: this label
        // sits on the page background, which is near-black in Dark and
        // Synthwave modes - Color.Black for Checkered and the system text
        // color for None were both effectively invisible there.
        _lblFlag.ForeColor = flag switch
        {
            FlagState.Green => Color.FromArgb(61, 220, 132),
            FlagState.Yellow => Color.FromArgb(255, 209, 71),
            FlagState.Red => Color.FromArgb(255, 92, 92),
            FlagState.Checkered => AppTheme.TextDark,
            _ => AppTheme.TextMuted,
        };

        // Practice and demo runs drive the same shared session, and this
        // control stays subscribed for the app's lifetime - announcing
        // flags for those would be wrong, so skip them here.
        // Demo is no longer excluded. It's how the announcer actually gets
        // tested - suppressing the green and checkered calls there meant
        // the two most important announcements could never be heard while
        // setting the thing up. Practice still skips them: an open lapping
        // session has no flags to call.
        if (!_race.IsPracticeMode && !_suppressFlagAnnouncements)
        {
            switch (flag)
            {
                // A green that started the race is the start, whatever flag
                // was out before it. This used to go by the flag before
                // alone, so a start held with a yellow or a red during
                // staging or the countdown was called as a restart - no
                // horn, and nothing said with that call switched off, as it
                // is by default. See RaceControlSession.GreenStartedTheRace.
                case FlagState.Green when _race.GreenStartedTheRace:
                    _announcer.NotifyRaceStarted();
                    break;
                case FlagState.Green when _previousFlag is FlagState.Yellow or FlagState.Red:
                    _announcer.NotifyGreenFlagResumed();
                    break;
                case FlagState.Green:
                    _announcer.NotifyRaceStarted();
                    break;
                case FlagState.Yellow:
                    _announcer.NotifyYellowFlag();
                    break;
                case FlagState.Red:
                    _announcer.NotifyRedFlag();
                    break;

                // No Checkered case, on purpose. The finish is called from
                // the time limit and the race completing, not from the flag
                // - and a checkered put back out by green after a yellow or
                // red on the last lap (see RaceControlSession) is the same
                // finish, already called. Saying nothing beats "racing
                // resumes" or a second "time's up".
            }
        }

        UpdateFlagButtonColors(flag);
        _previousFlag = flag;
    }

    /// <summary>
    /// Lights up whichever flag is currently in effect - and Stage or
    /// Countdown while the start is at that point - and leaves the rest at
    /// rest. Driven from the actual race state rather than from clicks, so
    /// it stays correct when a flag is set from the phone remote, a
    /// keyboard shortcut, or the auto-start sequence. Hover is the
    /// buttons' own business (see FlagButton); this only says what is lit.
    /// </summary>
    private void UpdateFlagButtonColors(FlagState flag)
    {
        var state = _race.State;

        // Lit through the warm-up too: pressing it again is how to skip it.
        _btnStage.Lit = state == RaceState.Staging || _race.IsWarmingUp;
        _btnCountdown.Lit = state == RaceState.Countdown;
        _btnGreen.Lit = flag == FlagState.Green;
        _btnYellow.Lit = flag == FlagState.Yellow;
        _btnRed.Lit = flag == FlagState.Red;
        _btnCheckered.Lit = flag == FlagState.Checkered;
    }

    /// <summary>
    /// Bridges a race-control lap update into the shared AnnouncementService,
    /// which has no idea whether the data came from Next Level Timing or
    /// straight from the decoder - it just needs the same shapes it already
    /// understands.
    /// </summary>
    private void OnLapProcessed(RacerStanding standing, bool isValid, bool isFastestLap)
    {
        NoteUnknownTransponder(standing.TransponderId);

        var profile = _profiles.Get(standing.TransponderId);

        var miniZStanding = new RaceStanding
        {
            Transponder = standing.TransponderId,
            Name = profile?.DriverName,
            Laps = standing.Laps,
            FastLapMs = standing.FastestLapMs,
            ElapsedMs = standing.ElapsedMs,
            Status = isValid ? "active" : "invalid",
            Id = standing.TransponderId,
        };
        _announcer.OnStandingUpdated(miniZStanding);

        // Track record check, against the selected layout's all-time best.
        // Checked on any lap that just became this driver's best, not only
        // on a "fastest lap of the race" - that flag is deliberately false
        // for the first flying lap of the race (nothing to beat yet), which
        // meant a record set on that lap was never noticed.
        // CheckAndAnnounceTrackRecord itself only acts on a genuine record.
        var thisLapMs = isValid && standing.LapTimesMs.Count > 1 ? standing.LapTimesMs[^1] : 0;
        if (thisLapMs > 0 && thisLapMs == standing.FastestLapMs)
        {
            CheckAndAnnounceTrackRecord(miniZStanding);
        }

        var gate = new GatePassEvent
        {
            Transponder = standing.TransponderId,
            Gate = "1",
            GateType = "finish",
            Valid = isValid,
            // Streak below is unused now that StreakCountOverride is set -
            // RaceControlSession computes the real streak count itself
            // (including breaking it on unusually slow laps), so the
            // announcer should use that directly rather than re-deriving
            // its own simpler approximation.
            Streak = isValid,
            Fast = isFastestLap,
            StreakCountOverride = standing.ConsecutiveValidLaps,

            // The session's own measured lap. The announcer used to rebuild
            // it from changes in elapsed time, which read a penalty as part
            // of the next lap and, on a rejected pass, announced the whole
            // race time as a "lap time". Null for the start lap.
            LapTimeMs = thisLapMs > 0 ? (long?)thisLapMs : null,
        };
        _announcer.OnGatePassed(gate);

        // The safety net. Written after every crossing, so that whatever
        // happens next - a crash, a power cut, a closed laptop - the race
        // can be picked up from this lap.
        WriteJournal();

        CheckRecordPace(standing, isValid, thisLapMs);

        RenderLeaderboard();
    }

    /// <summary>
    /// Compares this lap against the selected track layout's all-time
    /// record (independent of race-specific "fastest lap" tracking) and,
    /// if it's a new record, persists it and fires the special announcement.
    /// No-ops if no track layout is selected - track records only make
    /// sense once laps are attributed to a specific physical track.
    /// </summary>
    /// <summary>
    /// A lap added or removed, or a penalty, by hand. Updates the board and
    /// the announcer's running order - but it is not a pass, so no lap,
    /// lap-time, streak or invalid-lap announcement is made for it.
    /// </summary>
    private void OnStandingAdjusted(RacerStanding standing)
    {
        var profile = _profiles.Get(standing.TransponderId);

        _announcer.OnStandingUpdated(new RaceStanding
        {
            Transponder = standing.TransponderId,
            Name = profile?.DriverName,
            Laps = standing.Laps,
            FastLapMs = standing.FastestLapMs,
            ElapsedMs = standing.ElapsedMs,
            Status = "adjusted",
            Id = standing.TransponderId,
        });

        // Forced: a correction by hand is rare and is exactly the sort of
        // thing nobody wants to do twice.
        WriteJournal(force: true);

        // After the flag the result has already gone to the history, and
        // this only changes the board. Said plainly, because the board then
        // disagrees with the saved result, the points and the qualifying
        // figures - and nothing else would say which one is right. Rewriting
        // the saved session from here would also have to undo and redo its
        // points, achievements and round link, which is not something to do
        // quietly from a leaderboard right-click.
        if (_race.State == RaceState.Finished && !_race.IsPracticeMode)
        {
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [race] Adjustment after the flag - history not updated. "
                + "The saved result still has the figures it had at the checkered flag.");
        }

        RenderLeaderboard();
    }

    /// <summary>
    /// Drivers already told they're on record pace this race.
    ///
    /// Once each: the whole value of the call is that it's rare. Somebody
    /// circulating a tenth off the record for eight laps should be called
    /// out once and then left alone, not announced every time round.
    /// </summary>
    private readonly HashSet<string> _toldAboutRecordPace = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The session generation the set above belongs to. Stage, a fresh
    /// green and Reset each move the generation on, so a new race starts
    /// with nobody told. The set used to be emptied by Reset alone, and
    /// races normally follow one another without one - so the call went
    /// out once per driver per evening rather than once per race.
    /// </summary>
    private int _toldAboutRecordPaceGeneration = -1;

    /// <summary>
    /// How close to the record counts as being on for it.
    ///
    /// Two percent is about a tenth of a second on a five second lap -
    /// close enough that the next lap could genuinely take it, which is the
    /// only thing worth saying out loud.
    /// </summary>
    private const double RecordPaceWindow = 1.02;

    /// <summary>
    /// Watches for somebody running at the pace of the track record.
    ///
    /// The record itself is already announced when it falls. This is the
    /// bit before that: the lap that says it's ABOUT to, which is the part
    /// worth watching and the part an announcer would actually call.
    ///
    /// Deliberately not fired on the first flying lap - one quick lap is a
    /// tow or a lucky line, and the interesting claim is that the pace is
    /// real.
    /// </summary>
    private void CheckRecordPace(RacerStanding standing, bool isValid, long lapMs)
    {
        if (_toldAboutRecordPaceGeneration != _race.SessionGeneration)
        {
            _toldAboutRecordPace.Clear();
            _toldAboutRecordPaceGeneration = _race.SessionGeneration;
        }

        if (!isValid || lapMs <= 0 || _race.IsPracticeMode)
        {
            return;
        }

        // Only for somebody who could actually take the record - the same
        // drivers CheckAndAnnounceTrackRecord lets set one. Calling a ghost,
        // a rental or an excluded guest "on record pace" promises a record
        // that cannot fall to them.
        if (GhostService.IsGhost(standing.TransponderId)
            || _profiles.Get(standing.TransponderId) is { ExcludeFromRecords: true } or { IsRental: true })
        {
            return;
        }

        // Two counted laps is the green-to-line pass plus one real lap;
        // three means they have actually strung a pace together.
        if (standing.LapTimesMs.Count < 3 || _toldAboutRecordPace.Contains(standing.TransponderId))
        {
            return;
        }

        var trackName = SelectedTrackName();

        if (string.IsNullOrWhiteSpace(trackName))
        {
            return;
        }

        var track = _trackLayouts.All.FirstOrDefault(t => t.Name == trackName);

        if (track is null || track.RecordLapMs <= 0)
        {
            return;
        }

        // Beating it is a different event, announced elsewhere - this is
        // only for the laps that get close without taking it.
        if (lapMs <= track.RecordLapMs || lapMs > track.RecordLapMs * RecordPaceWindow)
        {
            return;
        }

        _toldAboutRecordPace.Add(standing.TransponderId);

        var profile = _profiles.Get(standing.TransponderId);
        var name = string.IsNullOrWhiteSpace(profile?.DriverName) ? standing.TransponderId : profile!.DriverName;
        var off = (lapMs - track.RecordLapMs) / 1000.0;

        StatusMessage?.Invoke(
            $"{AppTime.LogStampNow()}  [pace] {name} is on record pace - {lapMs / 1000.0:0.000} "
            + $"against the {track.Name} record of {track.RecordLapMs / 1000.0:0.000} ({off:0.000} off, "
            + $"held by {(string.IsNullOrWhiteSpace(track.RecordHolderName) ? "nobody on record" : track.RecordHolderName)}).");

        _announcer.NotifyRecordPace(new RaceStanding
        {
            Transponder = standing.TransponderId,
            Name = profile?.DriverName,
            Laps = standing.Laps,
            FastLapMs = standing.FastestLapMs,
            ElapsedMs = standing.ElapsedMs,
            Status = "active",
            Id = standing.TransponderId,
        }, lapMs);
    }

    private void CheckAndAnnounceTrackRecord(RaceStanding standing)
    {
        if (_race.IsDemoMode || _race.IsPracticeMode)
        {
            return; // simulated lap times are fake - never let them set a real all-time record
        }

        // Honour the profile's exclusion flag. Guests, staff demo cars and
        // rental transponders shouldn't own a club's all-time record - the
        // flag exists precisely so that can be prevented without banning
        // them from racing.
        //
        // Ghosts are checked by transponder as well as by their profile's
        // flag, so a ghost whose profile is missing can't take one either.
        var recordProfile = _profiles.Get(standing.Transponder);
        if (GhostService.IsGhost(standing.Transponder)
            || recordProfile is { ExcludeFromRecords: true } or { IsRental: true })
        {
            return;
        }

        var selectedTrackName = _cmbTrackLayout.SelectedItem as string;
        if (string.IsNullOrWhiteSpace(selectedTrackName))
        {
            return;
        }

        var track = _trackLayouts.All.FirstOrDefault(t => t.Name == selectedTrackName);
        if (track is null)
        {
            return;
        }

        if (track.RecordLapMs != 0 && standing.FastLapMs >= track.RecordLapMs)
        {
            return; // not a new record
        }

        if (!IsPlausibleRecord(standing.FastLapMs, standing.Transponder, track, out var doubt))
        {
            var who = string.IsNullOrWhiteSpace(standing.Name) ? standing.Transponder : standing.Name;

            // Said out loud in the log rather than dropped quietly. If the
            // lap really was driven, the operator needs to know the record
            // was left alone - and the driver is on the pace, so they will
            // do it again in a moment.
            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [race] All-time record NOT taken from {who}'s {standing.FastLapMs / 1000.0:0.000}s - {doubt}. "
                + "The lap still counts towards the race; only the record is left as it was.");
            return;
        }

        track.RecordLapMs = standing.FastLapMs;
        track.RecordHolderName = string.IsNullOrWhiteSpace(standing.Name) ? standing.Transponder : standing.Name;
        track.RecordSetUtc = DateTime.UtcNow;
        _trackLayouts.Upsert(track);

        _announcer.NotifyTrackRecord(standing);
    }

    /// <summary>
    /// Whether a lap that beats the all-time record is believable as a lap.
    ///
    /// The two possible mistakes do not cost the same. Refusing a real
    /// record costs one lap: the driver is on the pace and will do it
    /// again, and the log line above says it happened. Accepting a phantom
    /// writes a time nobody drove into the club's permanent record, where
    /// it becomes a ghost pace source, an achievement and an announcement -
    /// and nothing in the app ever questions it again. The only way back is
    /// to clear the record by hand on the track layout.
    ///
    /// A transponder left sitting under the bridge retriggers, and any two
    /// of those reads far enough apart to clear the minimum lap time are
    /// counted as a lap. That is what the minimum is for, and the operator
    /// sets it. But a lap wildly off the pace of everything around it is
    /// not a record, whatever the decoder said.
    ///
    /// Three questions, any one of which is enough to leave the record
    /// alone. They are deliberately loose - each is asking whether the time
    /// is possible, not whether it is impressive.
    /// </summary>
    private bool IsPlausibleRecord(long lapMs, string transponder, TrackLayout track, out string doubt)
    {
        doubt = string.Empty;

        // 1. Sitting on top of the minimum is the shape of a retrigger, not
        //    of a lap. The operator sets the minimum just under a real lap
        //    for the class, so a tenth above it is inside the very noise
        //    that setting exists to describe.
        if (_race.MinLapTimeMs > 0 && lapMs < _race.MinLapTimeMs * 1.1)
        {
            doubt = $"it is within a tenth of the {_race.MinLapTimeMs / 1000.0:0.000}s minimum lap time, which is what a retriggering transponder looks like";
            return false;
        }

        // 2. Against the record it claims to beat. Track records fall by
        //    hundredths and occasionally by tenths. A quarter of a lap is
        //    not an improvement, it is a different event.
        if (track.RecordLapMs > 0 && lapMs < track.RecordLapMs * 0.75)
        {
            doubt = $"it beats the standing record of {track.RecordLapMs / 1000.0:0.000}s by more than a quarter";
            return false;
        }

        // 3. Against the driver's own laps - which is what catches a single
        //    bad read on a layout that has no record yet. A median rather
        //    than a best, so one phantom cannot drag down the figure it is
        //    being judged against, and only once there are enough laps for
        //    a median to mean anything. Index 0 is the start lap, which is
        //    not a lap of the circuit.
        var own = _race.GetOrderedStandings()
            .FirstOrDefault(s => string.Equals(s.TransponderId, transponder, StringComparison.OrdinalIgnoreCase));

        // Recovery laps are left out too (RaceGaps.PaceLaps): one spans a
        // whole stoppage, and a few of them would lift the median until an
        // ordinary lap looked like a phantom.
        var theirLaps = own is null ? null : RaceGaps.PaceLaps(own).Where(t => t > 0).OrderBy(t => t).ToList();

        if (theirLaps is { Count: >= 3 })
        {
            var median = theirLaps[theirLaps.Count / 2];

            if (lapMs < median * 0.5)
            {
                doubt = $"it is less than half this driver's typical {median / 1000.0:0.000}s lap this race";
                return false;
            }
        }

        return true;
    }

    private void RenderLeaderboard()
    {
        if (IsDisposed || Disposing)
        {
            return; // window is closing/closed - nothing to render into
        }

        var ordered = _race.GetOrderedStandings();

        // Before the early return below, so an open lap breakdown still
        // updates - including to "no longer in the standings" - when the
        // board itself has nothing to draw.
        RefreshLapPopup(ordered);

        // Remembered before the rebuild below throws the rows away.
        //
        // Clearing and refilling a DataGridView makes it set CurrentCell to
        // the first cell, which with FullRowSelect selects P1's whole row -
        // and a selected row paints in the SELECTION colours, which override
        // the per-cell ForeColor the best-lap highlight is made of. So every
        // crossing re-selected P1 and, whenever P1 held the fastest lap, the
        // yellow vanished. Restoring the real selection afterwards (or none)
        // is what keeps it visible.
        var selectedTransponder = SelectedTransponder();

        _dgvLeaderboard.Rows.Clear();

        if (ordered.Count == 0)
        {
            return;
        }

        _kpiTracker.Observe(ordered, _race.SessionGeneration);

        var context = new KpiContext
        {
            Ordered = ordered,
            Profiles = _profiles,
            Tracker = _kpiTracker,
            FastestOverall = ordered.Where(s => s.FastestLapMs > 0).Select(s => s.FastestLapMs).DefaultIfEmpty(0).Min(),
            StreakThreshold = _race.StreakThreshold,
            Achievements = _achievements,

            // Null outside a qualifier, which is what makes the Qual column
            // show a dash rather than a wrong number during a main.
            Projection = _projector?.Current()?.Slots,
        };

        for (var i = 0; i < ordered.Count; i++)
        {
            var s = ordered[i];
            var values = _kpiColumns.Select(k => k.Compute(s, i, context)).ToList();

            var rowIndex = _dgvLeaderboard.Rows.Add(values.Select(v => (object)v.Text).ToArray());
            _dgvLeaderboard.Rows[rowIndex].Tag = s.TransponderId;

            // Jump starters stay marked for the whole race, whether or not
            // a penalty was applied - the race director needs to see it at
            // a glance, not just catch the announcement when it happened.
            if (_race.HasJumpStarted(s.TransponderId))
            {
                // A tint rather than full Danger, so the row still reads
                // as data instead of becoming a solid red block.
                _dgvLeaderboard.Rows[rowIndex].DefaultCellStyle.BackColor =
                    Color.FromArgb(70, AppTheme.Danger.R, AppTheme.Danger.G, AppTheme.Danger.B);
            }

            // A staggered or hand-released car that hasn't gone yet. Greyed
            // rather than hidden: on a hand-released start these rows are
            // the only way to see - and right-click - who is still waiting.
            if (_race.State == RaceState.Racing && !_race.IsReleased(s.TransponderId))
            {
                _dgvLeaderboard.Rows[rowIndex].DefaultCellStyle.ForeColor = AppTheme.TextMuted;
            }

            for (var col = 0; col < values.Count; col++)
            {
                if (values[col].Emphasis == KpiEmphasis.None)
                {
                    continue;
                }

                var emphasisColor = values[col].Emphasis switch
                {
                    KpiEmphasis.Good => AppTheme.Success,
                    KpiEmphasis.Bad => AppTheme.Danger,
                    KpiEmphasis.Highlight => AppTheme.Accent,
                    _ => _dgvLeaderboard.DefaultCellStyle.ForeColor,
                };

                var cell = _dgvLeaderboard.Rows[rowIndex].Cells[col];
                cell.Style.ForeColor = emphasisColor;

                // Set on the selection colour too, or clicking a driver -
                // or the grid selecting one by itself - repaints the row in
                // the selection palette and the emphasis disappears on that
                // row alone. The best-lap yellow has to survive being
                // selected; it means the same thing either way.
                cell.Style.SelectionForeColor = emphasisColor;
            }

            if (_expandedTransponders.Contains(s.TransponderId))
            {
                AddLapDetailRow(s);
            }
        }

        RestoreLeaderboardSelection(selectedTransponder);
    }

    /// <summary>
    /// Puts the selection back on the driver who had it before the grid was
    /// rebuilt, or leaves nothing selected when nobody did.
    ///
    /// Leaving nothing selected is the important half: without it the grid
    /// selects P1 on every single crossing, which is both a selection the
    /// user never made and the reason the best-lap highlight kept vanishing.
    /// </summary>
    private void RestoreLeaderboardSelection(string? transponder)
    {
        if (!string.IsNullOrEmpty(transponder))
        {
            foreach (DataGridViewRow row in _dgvLeaderboard.Rows)
            {
                // A driver's own row, never the lap strip underneath it -
                // that one carries the same id behind a prefix.
                if (row.Tag is string tag
                    && !tag.StartsWith(DetailRowPrefix, StringComparison.Ordinal)
                    && string.Equals(tag, transponder, StringComparison.OrdinalIgnoreCase)
                    && row.Cells.Count > 0)
                {
                    // CurrentCell, not Selected: SelectedTransponder reads
                    // CurrentRow, and setting the cell selects the row too.
                    _dgvLeaderboard.CurrentCell = row.Cells[0];
                    return;
                }
            }
        }

        // Either nothing was selected, or that driver is no longer on the
        // board - clear rather than leave the grid's own guess standing.
        _dgvLeaderboard.CurrentCell = null;
        _dgvLeaderboard.ClearSelection();
    }

    /// <summary>
    /// The lap-by-lap strip that opens under a driver on double-click.
    ///
    /// One lap per column, oldest on the left, so it reads the way the race
    /// happened. A star marks a lap that was part of a clean run at or past
    /// the streak threshold; a broken circle marks the lap where a run
    /// ended, which is the one worth looking at.
    /// </summary>
    private void AddLapDetailRow(RacerStanding standing)
    {
        var columns = _dgvLeaderboard.Columns.Count;

        if (columns < 2)
        {
            return;
        }

        var lapCount = standing.LapTimesMs.Count;

        if (lapCount == 0)
        {
            var emptyCells = new object[columns];
            emptyCells[0] = "No laps yet";
            FinishLapDetailRow(standing, emptyCells, 26);
            return;
        }

        var best = standing.FastestLapMs;
        // The floor lives on the property now, so this is whatever the
        // announcer would call a streak at.
        var threshold = _race.StreakThreshold;

        // Each lap is one entry in a single run of text rather than one
        // value per column. Spreading them across the leaderboard's columns
        // made every lap line up under an unrelated heading - a lap time
        // sitting under "Gap" reads as a gap - and the uneven column widths
        // left ragged holes between them.
        var entries = new List<string>(lapCount);
        var detail = new List<string>(lapCount);

        for (var lapIndex = 0; lapIndex < lapCount; lapIndex++)
        {
            var lapMs = standing.LapTimesMs[lapIndex];

            // The streak count recorded against this lap. Older sessions -
            // and any lap added by hand - have no entry, so the marker is
            // simply left off rather than guessed at.
            var streak = lapIndex < standing.StreakAtLap.Count ? standing.StreakAtLap[lapIndex] : -1;
            var previousStreak = lapIndex > 0 && lapIndex - 1 < standing.StreakAtLap.Count ? standing.StreakAtLap[lapIndex - 1] : -1;

            var brokeARun = streak == 1 && previousStreak >= threshold;
            var inARun = streak >= threshold;

            var marker = brokeARun ? "\u25CC" : inARun ? "\u2605" : string.Empty;

            // The first counted pass runs from the green to the line, so it
            // is not a lap time and is labelled as what it is.
            var isStartLap = lapIndex == 0;

            entries.Add(isStartLap
                ? $"start {lapMs / 1000.0:0.000}"
                : $"L{lapIndex + 1} {lapMs / 1000.0:0.000}{marker}");

            // Spelled out rather than done with a three-part numeric
            // format: the "zero" section of one of those is read as format
            // characters, and "best" contains an 'e', which would come back
            // as scientific notation.
            var deltaToBest = string.Empty;

            if (best > 0 && !isStartLap)
            {
                var delta = (lapMs - best) / 1000.0;
                deltaToBest = delta <= 0 ? ", their best" : $", +{delta:0.000} off their best";
            }

            detail.Add(isStartLap
                ? $"Green flag to the line: {lapMs / 1000.0:0.000}s. Not a lap time, so it never counts as a best lap."
                : $"Lap {lapIndex + 1}: {lapMs / 1000.0:0.000}s{deltaToBest}"
                  + (streak >= 0 ? $" - clean run of {streak}{(inARun ? " (counting)" : string.Empty)}" : string.Empty)
                  + (brokeARun ? " - this lap ended the previous run" : string.Empty));
        }

        // The laps WRAP across up to three lines rather than being cut to
        // whatever fits on one. A single line held about four laps on a
        // typical window, which is not a lap history.
        var available = Math.Max(160, _dgvLeaderboard.ClientSize.Width - LapDetailPadding * 2);

        var shown = entries.Count;
        var text = string.Join(LapDetailSeparator, entries);
        var height = MeasureLapDetail(text, available);

        // Too many laps to show even wrapped: drop the oldest until it fits,
        // because the recent end is the one being read.
        while (shown > 1 && height > MaxLapDetailHeight)
        {
            shown--;
            text = string.Join(LapDetailSeparator, entries.Skip(entries.Count - shown));
            height = MeasureLapDetail(text, available);
        }

        var cells = new object[columns];
        cells[0] = text;

        FinishLapDetailRow(standing, cells, Math.Clamp(height + 10, 26, MaxLapDetailHeight + 10));
    }

    private const int MaxLapDetailHeight = 78;

    private static int MeasureLapDetail(string text, int available) =>
        TextRenderer.MeasureText(text, AppTheme.BodyFont, new Size(available, int.MaxValue), LapDetailFormat).Height;

    private const TextFormatFlags LapDetailFormat =
        TextFormatFlags.WordBreak | TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix;

    /// <summary>
    /// Shared tail of <see cref="AddLapDetailRow"/> - adds the row and tags
    /// it so the rest of the page can tell a detail row from a driver row.
    /// No pop-up: it only repeated the laps already painted across the row.
    /// </summary>
    private void FinishLapDetailRow(RacerStanding standing, object[] cells, int height)
    {
        var detailIndex = _dgvLeaderboard.Rows.Add(cells);
        var row = _dgvLeaderboard.Rows[detailIndex];

        row.Tag = DetailRowPrefix + standing.TransponderId;
        row.DefaultCellStyle.BackColor = AppTheme.Surface;
        row.DefaultCellStyle.ForeColor = AppTheme.TextMuted;
        row.DefaultCellStyle.Font = AppTheme.BodyFont;
        row.Height = height;
    }

    private const int LapDetailPadding = 10;
    private const string LapDetailSeparator = "   ";

    /// <summary>
    /// Paints a lap-detail row as one left-justified line spanning the whole
    /// grid instead of one value per column.
    ///
    /// DataGridView has no row spanning, so the text is drawn from the first
    /// cell with the clip widened to the full row - the graphics handed to
    /// CellPainting is clipped to the grid's data area, not to the cell, so
    /// widening it is allowed as long as it is restored afterwards.
    /// </summary>
    private void LeaderboardDetailPaint(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _dgvLeaderboard.Rows.Count || e.ColumnIndex < 0 || e.Graphics is not { } g)
        {
            return;
        }

        if (_dgvLeaderboard.Rows[e.RowIndex].Tag is not string tag
            || !tag.StartsWith(DetailRowPrefix, StringComparison.Ordinal))
        {
            return;
        }

        using (var back = new SolidBrush(AppTheme.Surface))
        {
            g.FillRectangle(back, e.CellBounds);
        }

        using (var rule = new Pen(AppTheme.Border))
        {
            g.DrawLine(rule, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1);
        }

        if (e.ColumnIndex == 0)
        {
            var width = _dgvLeaderboard.Columns.GetColumnsWidth(DataGridViewElementStates.Visible);
            var span = new Rectangle(e.CellBounds.Left, e.CellBounds.Top, Math.Max(e.CellBounds.Width, width), e.CellBounds.Height);

            var saved = g.Save();
            g.SetClip(span);

            // Body weight in the normal text colour, not small-and-muted:
            // these are lap times, which is the one thing on the page worth
            // squinting at, and they were the hardest thing on it to read.
            var text = new Rectangle(
                span.Left + LapDetailPadding,
                span.Top + 5,
                Math.Max(20, span.Width - LapDetailPadding * 2),
                Math.Max(10, span.Height - 8));

            TextRenderer.DrawText(g, e.FormattedValue as string ?? string.Empty, AppTheme.BodyFont, text,
                AppTheme.TextDark, LapDetailFormat);

            g.Restore(saved);
        }

        e.Handled = true;
    }

    /// <summary>Rebuilds the leaderboard's columns from the saved KPI selection.</summary>
    private void RebuildLeaderboardColumns()
    {
        _kpiColumns = KpiCatalog.Resolve(_settings.RaceControlKpis, KpiCatalog.DefaultRaceControl);

        _dgvLeaderboard.Columns.Clear();
        foreach (var kpi in _kpiColumns)
        {
            _dgvLeaderboard.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = kpi.Key,
                HeaderText = kpi.Label,
                FillWeight = kpi.Weight,
            });
        }
    }

    /// <summary>
    /// Assigns a driver profile to the EXACT transponder ID this row is
    /// using, taken directly from the live decoder rather than typed by
    /// hand. This matters specifically for the direct-decoder path: the
    /// decoder reports the raw 3-byte hex ID (e.g. "8467BE"), which is a
    /// completely different value from the decimal ID Next Level Timing
    /// displays for the same physical transponder (e.g. "48743") - there's
    /// no reliable conversion between the two. A profile created against
    /// NLT's decimal ID will never match here, silently, with no error -
    /// it just won't find a name. Assigning it from this exact row
    /// guarantees the key matches.
    /// </summary>
    /// <summary>The transponder of the currently-selected leaderboard row, or null if nothing is selected.</summary>
    private string? SelectedTransponder() =>
        TransponderForRow(_dgvLeaderboard.CurrentRow?.Index ?? -1);

    private void AdjustSelected(int laps, long penaltyMs)
    {
        var transponder = SelectedTransponder();
        if (transponder is null)
        {
            AppDialog.Show(this, "Select a racer in the leaderboard first.", "No racer selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Made through EditStanding so the app's Undo (the footer button and
        // Ctrl+Z) can take this one correction back - and only this one,
        // leaving any laps counted since on the board.
        var edit = _race.EditStanding(transponder, () =>
        {
            var changed = false;

            if (laps != 0)
            {
                changed |= _race.AdjustLaps(transponder, laps);
            }
            if (penaltyMs != 0)
            {
                changed |= _race.AdjustPenaltyTime(transponder, penaltyMs);
            }

            return changed;
        });

        if (edit is not null)
        {
            var what = laps > 0 ? (laps == 1 ? "Add a lap" : $"Add {laps} laps")
                : laps < 0 ? (laps == -1 ? "Remove a lap" : $"Remove {-laps} laps")
                : penaltyMs >= 0 ? $"{penaltyMs / 1000.0:0.#}s penalty"
                : $"{-penaltyMs / 1000.0:0.#}s credit";

            CaptureStandingEditUndo($"{what} for {DriverNameFor(transponder)}", edit);
        }

        RenderLeaderboard();
    }

    /// <summary>
    /// Puts a correction made by hand on the app's undo stack. Pushed just
    /// after the change rather than before, because EditStanding needs the
    /// driver's figures from both sides of it - nothing can happen in
    /// between, it is all on this thread. The undo works on the session,
    /// which lives as long as the app, and the board follows through the
    /// session's StandingAdjusted.
    /// </summary>
    private void CaptureStandingEditUndo(string description, RaceControlSession.StandingEdit edit) =>
        _undo.Capture(description, () => _race.UndoStandingEdit(edit));

    private void ClearSelectedPenalty()
    {
        var transponder = SelectedTransponder();
        if (transponder is null)
        {
            AppDialog.Show(this, "Select a racer in the leaderboard first.", "No racer selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var standing = _race.GetOrderedStandings().FirstOrDefault(s => s.TransponderId == transponder);

        // A jump-start penalty still owed counts as something to clear. It is
        // only booked into PenaltyMs on the driver's next counted lap, and
        // ClearPenalties drops it - but this used to return first whenever
        // PenaltyMs was zero, so a jumper credited with the start lap could
        // not be let off, and the penalty landed a lap later regardless.
        if (standing is null || (standing.PenaltyMs == 0 && _race.OwedJumpStartPenaltyMs(transponder) <= 0))
        {
            return;
        }

        // Through the session's own clear rather than a matching credit, so
        // the time noted on each warning goes too - see ClearPenalties. Undo
        // gives back the time, the warnings' notes and any jump-start
        // penalty still owed.
        var edit = _race.EditStanding(transponder, () =>
        {
            _race.ClearPenalties(transponder);
            return true;
        });

        if (edit is not null)
        {
            CaptureStandingEditUndo($"Clear penalties for {DriverNameFor(transponder)}", edit);
        }

        RenderLeaderboard();
    }

    /// <summary>
    /// Notes a transponder nobody has a profile for, so it can be named
    /// once the racing stops - instead of leaving a bare hex id sitting in
    /// the leaderboard for somebody to notice and fix later.
    ///
    /// This used to ask right here, with a modal dialog, and it is called
    /// from the lap pipeline. A modal pumps the message loop: every queued
    /// lap, every clock tick and every state change went on running INSIDE
    /// it, so the next lap was processed while the previous one was still
    /// on the stack, and the race could finish behind a question box nobody
    /// had answered yet. It also froze the flag buttons at the one moment
    /// an unknown car is most likely to need a marshal call.
    ///
    /// So nothing modal happens during a session now. The operator gets a
    /// line in the race log and the question afterwards.
    /// </summary>
    private void NoteUnknownTransponder(string transponderId)
    {
        if (_promptedTransponders.Contains(transponderId) || _profiles.Get(transponderId) is not null)
        {
            return;
        }

        _promptedTransponders.Add(transponderId);
        _unnamedTransponders.Add(transponderId);

        StatusMessage?.Invoke(
            $"{AppTime.LogStampNow()}  [race] New transponder {transponderId} is on track - you'll be asked to name it when the session ends.");
    }

    /// <summary>
    /// Offers to name whatever turned up unrecognised, once the racing has
    /// actually stopped.
    ///
    /// Always reached through BeginInvoke, never called straight from an
    /// event: the thing that ends a race is usually a lap, so a dialog
    /// opened inline would sit on top of the very pipeline it interrupted.
    /// Posting it means the lap, the finish and everything behind them are
    /// finished and off the stack before anything modal opens.
    /// </summary>
    private void PromptForUnnamedTransponders()
    {
        if (IsDisposed || Disposing || _unnamedTransponders.Count == 0)
        {
            return;
        }

        if (_race.State is RaceState.Racing or RaceState.Staging or RaceState.Countdown)
        {
            return; // a new session started while this was queued
        }

        // Taken and cleared BEFORE the first dialog. A modal still pumps
        // the message loop, so a queued state change can call back in here
        // while one is open - and would otherwise hand out the same
        // transponder a second time. Anyone named in the meantime (from the
        // profiles page, say) drops out here.
        var pending = _unnamedTransponders
            .Where(t => _profiles.Get(t) is null)
            .ToList();

        _unnamedTransponders.Clear();

        if (pending.Count == 0)
        {
            return;
        }

        var question = pending.Count == 1
            ? $"New transponder detected: {pending[0]}\n\nAssign a driver name and car to it now?"
            : $"{Plural.Of(pending.Count, "new transponder")} were on track this session:\n\n   "
              + string.Join("\n   ", pending)
              + "\n\nName them now?";

        if (AppDialog.Show(this, question, "Unknown transponder", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
            != DialogResult.Yes)
        {
            return;
        }

        foreach (var transponderId in pending)
        {
            // Named elsewhere while an earlier form in this loop was open
            // (the profiles page, a phone check-in) - nothing left to add,
            // and TryAdd below would only refuse it as a clash with itself.
            if (_profiles.Get(transponderId) is not null)
            {
                continue;
            }

            using var form = new ProfileForm(null, _raceClasses, fixedTransponder: transponderId, undo: _undo, profiles: _profiles);

            // A NEW driver, so TryAdd rather than Upsert: Upsert would
            // silently replace anybody already stored on this transponder.
            // The form checks the same thing before it closes, so this only
            // refuses if someone claimed the transponder (or the alt one) on
            // another page while the form was open.
            if (form.ShowDialog(this) == DialogResult.OK
                && !_profiles.TryAdd(form.Result, out var refusal))
            {
                AppDialog.Show(this, refusal, "Transponder already in use", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        RenderLeaderboard();
    }

    /// <summary>
    /// The drivers whose lap detail is open beneath their row. Survives the
    /// grid being rebuilt on every lap.
    ///
    /// A set rather than a single id, so several drivers can be open at
    /// once - comparing two or three people's recent laps side by side is
    /// the whole reason to open the strip, and having each new one close the
    /// last made that impossible.
    ///
    /// Case-insensitive to match every other transponder comparison in this
    /// file; ids arrive from the decoder with inconsistent casing.
    ///
    /// Ids are kept even for drivers not currently in the standings - a
    /// transponder that drops out and comes back reopens where it was, and a
    /// stale id simply never matches.
    /// </summary>
    private readonly HashSet<string> _expandedTransponders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Double-click opens the driver's lap breakdown rather than the profile
    /// editor - during a race the laps are what you want, and editing a
    /// profile mid-race is the rarer job. That's on the right-click menu.
    /// </summary>
    private void DgvLeaderboard_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _dgvLeaderboard.Rows.Count)
        {
            return;
        }

        var transponder = TransponderForRow(e.RowIndex);

        if (string.IsNullOrEmpty(transponder))
        {
            return;
        }

        ShowLapPopup(transponder, e.RowIndex);
    }

    /// <summary>
    /// Opens or closes one driver's inline lap strip, leaving every other
    /// open strip exactly as it was.
    ///
    /// Still here, on the right-click menu, because the strip and the popup
    /// answer different questions: the strip is for keeping two or three
    /// drivers' recent laps visible next to each other on the board, the
    /// popup is for reading one driver's race properly.
    /// </summary>
    private void ToggleLapDetail(string transponder)
    {
        if (!_expandedTransponders.Remove(transponder))
        {
            _expandedTransponders.Add(transponder);
        }

        RenderLeaderboard();
    }

    /// <summary>
    /// The floating lap breakdown, when one is open. One at a time - a
    /// second popup over the first would cover the board it is describing.
    /// </summary>
    private LapDetailPopup? _lapPopup;

    /// <summary>
    /// Opens the custom-drawn lap breakdown for a driver, anchored under
    /// their row so it reads as belonging to that row rather than appearing
    /// wherever the pointer happened to be.
    /// </summary>
    private void ShowLapPopup(string transponder, int rowIndex)
    {
        CloseLapPopup();

        var profile = _profiles.Get(transponder);
        var name = string.IsNullOrWhiteSpace(profile?.DriverName) ? transponder : profile!.DriverName;

        // Whatever of the car, number and class is actually filled in -
        // an empty profile should not produce a line of stray separators.
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(profile?.RaceNumber))
        {
            parts.Add($"#{profile!.RaceNumber.Trim()}");
        }

        if (!string.IsNullOrWhiteSpace(profile?.CarModel))
        {
            parts.Add(profile!.CarModel.Trim());
        }

        if (!string.IsNullOrWhiteSpace(profile?.ClassName))
        {
            parts.Add(profile!.ClassName.Trim());
        }

        parts.Add(transponder);

        var popup = new LapDetailPopup(transponder, name, string.Join("  ·  ", parts), _race.StreakThreshold);

        popup.Closed += (_, _) =>
        {
            // Only the popup closing ITSELF - a click elsewhere, Escape -
            // gets here still current. CloseLapPopup clears the field first
            // and disposes the popup itself.
            if (!ReferenceEquals(_lapPopup, popup))
            {
                return;
            }

            _lapPopup = null;

            // A drop-down is a window of its own, and an auto-closed one was
            // never disposed - one leaked per double-click all evening.
            // Posted rather than disposed here, because this handler runs
            // inside the popup's own closing.
            if (IsHandleCreated && !IsDisposed && !Disposing)
            {
                BeginInvoke(new Action(() =>
                {
                    if (!popup.IsDisposed)
                    {
                        popup.Dispose();
                    }
                }));
            }
        };

        _lapPopup = popup;

        RefreshLapPopup(_race.GetOrderedStandings());

        // Under the driver's own row, left-aligned with the grid. Falls back
        // to the top of the grid when the row can't be located - which can
        // happen if the board rebuilt between the click and here.
        var y = 0;

        try
        {
            if (rowIndex >= 0 && rowIndex < _dgvLeaderboard.Rows.Count)
            {
                var rect = _dgvLeaderboard.GetRowDisplayRectangle(rowIndex, false);
                y = rect.Bottom;
            }
        }
        catch
        {
            // Row scrolled out from under us - the fallback position is fine.
        }

        popup.Show(_dgvLeaderboard, new Point(0, y));
    }

    private void CloseLapPopup()
    {
        if (_lapPopup is not { } popup)
        {
            return;
        }

        _lapPopup = null;

        popup.Close();
        popup.Dispose();
    }

    /// <summary>
    /// Feeds the open popup the driver's current standing on every board
    /// refresh, so it updates lap by lap instead of freezing on whatever was
    /// true when it was opened.
    /// </summary>
    private void RefreshLapPopup(IReadOnlyList<RacerStanding> ordered)
    {
        if (_lapPopup is not { } popup)
        {
            return;
        }

        RacerStanding? standing = null;
        var position = 0;

        for (var i = 0; i < ordered.Count; i++)
        {
            if (string.Equals(ordered[i].TransponderId, popup.Transponder, StringComparison.OrdinalIgnoreCase))
            {
                standing = ordered[i];
                position = i + 1;
                break;
            }
        }

        popup.Apply(standing, position);
    }

    /// <summary>
    /// The driver a row belongs to - the driver's own row, or the lap detail
    /// sitting under it, which carries the same id prefixed so it can be
    /// told apart.
    /// </summary>
    private string? TransponderForRow(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _dgvLeaderboard.Rows.Count)
        {
            return null;
        }

        return _dgvLeaderboard.Rows[rowIndex].Tag switch
        {
            string tag when tag.StartsWith(DetailRowPrefix, StringComparison.Ordinal) => tag[DetailRowPrefix.Length..],
            string tag => tag,
            _ => null,
        };
    }

    private const string DetailRowPrefix = "detail:";

    private ContextMenuStrip BuildLeaderboardMenu()
    {
        var menu = new ContextMenuStrip();

        var editDriver = new ToolStripMenuItem("Edit driver...");
        editDriver.Click += (_, _) => EditSelectedDriver();

        var lapBreakdown = new ToolStripMenuItem("Lap breakdown...");
        lapBreakdown.Click += (_, _) =>
        {
            if (SelectedTransponder() is { Length: > 0 } transponder)
            {
                ShowLapPopup(transponder, _dgvLeaderboard.CurrentRow?.Index ?? -1);
            }
        };

        var toggleLaps = new ToolStripMenuItem("Show recent laps");
        toggleLaps.Click += (_, _) =>
        {
            var transponder = SelectedTransponder();

            if (transponder is null)
            {
                return;
            }

            ToggleLapDetail(transponder);
        };

        // Only worth offering once more than one strip is open - see the
        // Opening handler, which hides it otherwise.
        var hideAllLaps = new ToolStripMenuItem("Hide all lap strips");
        hideAllLaps.Click += (_, _) =>
        {
            _expandedTransponders.Clear();
            RenderLeaderboard();
        };

        var releaseDriver = new ToolStripMenuItem("Release");
        releaseDriver.Click += (_, _) =>
        {
            if (SelectedTransponder() is { Length: > 0 } transponder)
            {
                _race.ReleaseDriver(transponder);
                RenderLeaderboard();
            }
        };

        var releaseAll = new ToolStripMenuItem("Release everyone still waiting");
        releaseAll.Click += (_, _) =>
        {
            _race.ReleaseAll();
            RenderLeaderboard();
        };

        var addLap = new ToolStripMenuItem("Add a lap (+1)");
        addLap.Click += (_, _) => AdjustSelected(1, 0);

        var removeLap = new ToolStripMenuItem("Remove a lap (-1)");
        removeLap.Click += (_, _) => AdjustSelected(-1, 0);

        var penaltyFive = new ToolStripMenuItem("Penalty +5s");
        penaltyFive.Click += (_, _) => AdjustSelected(0, 5000);

        var penaltyTen = new ToolStripMenuItem("Penalty +10s");
        penaltyTen.Click += (_, _) => AdjustSelected(0, 10000);

        var penaltyCustom = new ToolStripMenuItem("Penalty...");
        penaltyCustom.Click += (_, _) => PenaltySelectedByHand();

        var clearPenalty = new ToolStripMenuItem("Clear penalty");
        clearPenalty.Click += (_, _) => ClearSelectedPenalty();

        // Filled each time the menu opens, for the driver under the pointer
        // - see FillWarningItems. The placeholder stops WinForms treating
        // it as a plain item with no arrow before the first fill.
        var warn = new ToolStripMenuItem("Warning");
        warn.DropDownItems.Add(new ToolStripMenuItem("..."));

        var excludeFromRecords = new ToolStripMenuItem("Exclude from records") { CheckOnClick = false };
        excludeFromRecords.Click += (_, _) => ToggleExcludeFromRecords();

        var copyId = new ToolStripMenuItem("Copy transponder ID");
        copyId.Click += (_, _) =>
        {
            if (SelectedTransponder() is { Length: > 0 } transponder)
            {
                // Clipboard access throws when another app has it open -
                // never worth an error dialog over a copy.
                try { Clipboard.SetText(transponder); } catch { }
            }
        };

        menu.Items.AddRange(new ToolStripItem[]
        {
            editDriver,
            lapBreakdown,
            toggleLaps,
            hideAllLaps,
            releaseDriver,
            releaseAll,
            new ToolStripSeparator(),
            addLap,
            removeLap,
            new ToolStripSeparator(),
            penaltyFive,
            penaltyTen,
            penaltyCustom,
            clearPenalty,
            new ToolStripSeparator(),
            warn,
            new ToolStripSeparator(),
            excludeFromRecords,
            copyId,
        });

        menu.Opening += (_, e) =>
        {
            var transponder = SelectedTransponder();

            if (transponder is null)
            {
                e.Cancel = true;
                return;
            }

            var profile = _profiles.Get(transponder);
            var name = string.IsNullOrWhiteSpace(profile?.DriverName) ? transponder : profile!.DriverName;

            editDriver.Text = profile is null ? $"Assign a driver to {transponder}..." : $"Edit {name}...";

            lapBreakdown.Text = $"{name}'s lap breakdown...";

            toggleLaps.Text = _expandedTransponders.Contains(transponder)
                ? $"Hide {name}'s laps on the board"
                : $"Show {name}'s laps on the board";

            // Shown only when it would actually do something more than the
            // item above it.
            hideAllLaps.Visible = _expandedTransponders.Count > 1;
            hideAllLaps.Text = $"Hide all {_expandedTransponders.Count} lap strips";

            // Releasing is only ever a hand-released thing, and only while
            // the race is actually running - a scheduled stagger releases
            // itself, and there is nothing to release before the green.
            var canRelease = _race.StartType == RaceStartType.HandReleased
                             && _race.State == RaceState.Racing;

            releaseDriver.Visible = canRelease && !_race.IsReleased(transponder);
            releaseDriver.Text = $"Release {name} now";

            releaseAll.Visible = canRelease;

            var standing = _race.GetOrderedStandings().FirstOrDefault(st =>
                string.Equals(st.TransponderId, transponder, StringComparison.OrdinalIgnoreCase));

            // A jump-start penalty not yet booked can be cleared too - see
            // ClearSelectedPenalty.
            var owedJumpStartMs = standing is null ? 0 : _race.OwedJumpStartPenaltyMs(standing.TransponderId);

            clearPenalty.Enabled = standing is { PenaltyMs: not 0 } || owedJumpStartMs > 0;
            clearPenalty.Text = standing is { PenaltyMs: not 0 }
                ? $"Clear penalty ({standing.PenaltyMs / 1000.0:0.0}s)"
                : owedJumpStartMs > 0
                    ? $"Clear penalty (jump start, {owedJumpStartMs / 1000.0:0.0}s still to come)"
                    : "Clear penalty";

            excludeFromRecords.Enabled = profile is not null;
            excludeFromRecords.Checked = profile?.ExcludeFromRecords == true;

            var warnings = standing?.Warnings.Count ?? 0;
            warn.Text = warnings == 0 ? $"Warn {name}" : $"Warn {name} ({Plural.Of(warnings, "warning")} so far)";
            warn.Enabled = standing is not null;
            FillWarningItems(warn.DropDownItems, transponder);
        };

        // Drawn by AppTheme rather than by Windows. Without this the menu is
        // the one piece of the race screen still wearing stock grey chrome,
        // and it is the piece you look at while deciding to give somebody a
        // penalty.
        AppTheme.StyleContextMenu(menu);

        return menu;
    }

    private void EditSelectedDriver()
    {
        var transponder = SelectedTransponder();

        if (transponder is null)
        {
            return;
        }

        var existing = _profiles.Get(transponder);

        // The driver as on file before the form opens - the form may edit
        // the record in place - so Undo can put every field back.
        var before = existing is null ? null : System.Text.Json.JsonSerializer.Serialize(existing);

        using var form = new ProfileForm(existing, _raceClasses, fixedTransponder: transponder, undo: _undo, profiles: _profiles);

        if (form.ShowDialog(this) == DialogResult.OK)
        {
            var profiles = _profiles;
            var saved = form.Result;
            var after = System.Text.Json.JsonSerializer.Serialize(saved);
            var key = saved.Transponder;

            if (before != after)
            {
                _undo.Capture(before is null ? $"Add driver {saved.DriverName}" : $"Edit driver {saved.DriverName}", () =>
                {
                    // Only while the driver is still as this edit left them -
                    // otherwise a later edit would be thrown away with it.
                    var current = profiles.Get(key);

                    if (current is null || System.Text.Json.JsonSerializer.Serialize(current) != after)
                    {
                        throw new InvalidOperationException(
                            $"{saved.DriverName} has been changed or removed since, and undoing this now would undo that too. Nothing was changed.");
                    }

                    if (before is null)
                    {
                        profiles.Delete(key);
                    }
                    else if (System.Text.Json.JsonSerializer.Deserialize<DriverProfile>(before) is { } restored)
                    {
                        // The alt transponder the old details had may be
                        // somebody else's by now - one car, two owners.
                        if (profiles.DescribeClash(restored, editing: restored.Transponder) is { } clash)
                        {
                            throw new InvalidOperationException($"the old details can't come back - {clash} Nothing was changed.");
                        }

                        profiles.Upsert(restored);

                        // The form retires a replaced photo rather than
                        // deleting it when it has an undo, so it can come back.
                        if (!string.Equals(current.PhotoFileName, restored.PhotoFileName, StringComparison.OrdinalIgnoreCase))
                        {
                            DriverPhotos.Unretire(restored.PhotoFileName);
                            DriverPhotos.Retire(current.PhotoFileName);
                        }
                    }
                });
            }

            _profiles.Upsert(saved);
            RenderLeaderboard();
        }
    }

    /// <summary>Keeps a guest or a demo car out of the club's records without banning them from the race.</summary>
    private void ToggleExcludeFromRecords()
    {
        var transponder = SelectedTransponder();

        if (transponder is null || _profiles.Get(transponder) is not { } profile)
        {
            return;
        }

        // Only the flag goes back, onto the driver as on file then.
        var profiles = _profiles;
        var excluded = !profile.ExcludeFromRecords;

        _undo.Capture($"{(excluded ? "Exclude" : "Include")} {profile.DriverName} {(excluded ? "from" : "in")} records", () =>
        {
            if (profiles.Get(transponder) is { } current && current.ExcludeFromRecords == excluded)
            {
                current.ExcludeFromRecords = !excluded;
                profiles.Upsert(current);
            }
        });

        profile.ExcludeFromRecords = excluded;
        _profiles.Upsert(profile);

        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] {profile.DriverName} "
            + (profile.ExcludeFromRecords
                ? "excluded from track records and championship points (places close up behind them in the standings)."
                : "counts toward track records and championship points again."));
    }

    /// <summary>A penalty of any size, for the ones that aren't five or ten seconds.</summary>
    private void PenaltySelectedByHand()
    {
        if (SelectedTransponder() is null)
        {
            AppDialog.Show(this, "Select a racer in the leaderboard first.", "No racer selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new Form
        {
            Text = "Time penalty",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(300, 116),
            BackColor = AppTheme.Background,
            Font = AppTheme.BodyFont,
        };

        var label = new Label { Text = "Seconds to add:", Location = new Point(12, 18), AutoSize = true };

        var seconds = new NumericUpDown
        {
            Location = new Point(128, 15),
            Width = 90,
            DecimalPlaces = 1,
            Increment = 0.5m,
            Minimum = -600m,
            Maximum = 600m,
            Value = 5m,
        };

        var ok = new Button { Text = "Apply", Location = new Point(118, 66), Width = 80, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Location = new Point(206, 66), Width = 80, DialogResult = DialogResult.Cancel };

        dialog.Controls.AddRange(new Control[] { label, seconds, ok, cancel });
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        AppTheme.ApplyRecursively(dialog);

        if (dialog.ShowDialog(this) == DialogResult.OK && seconds.Value != 0)
        {
            AdjustSelected(0, (long)(seconds.Value * 1000));
        }
    }


    /// <summary>
    /// Whether finishing a race should arm the next one.
    ///
    /// The open race day's own answer, falling back to the club-wide
    /// setting for a day that never gave one - which is every day written
    /// before this moved onto the race day. See RaceEvent.AutoAdvanceOn.
    /// </summary>
    private bool AutoAdvanceWanted =>
        _events?.Active is { IsLegacy: false } day
            ? day.AutoAdvanceOn(_settings.AutoAdvanceRounds)
            : _settings.AutoAdvanceRounds;

    /// <summary>Which race to reach for - the day's, or the club's when the day never said.</summary>
    private string AutoAdvanceScope =>
        _events?.Active is { IsLegacy: false } day
            ? day.AutoAdvanceScopeOr(_settings.AutoAdvanceScope)
            : _settings.AutoAdvanceScope;

    /// <summary>The class the scope is pinned to, read together with the scope above.</summary>
    private string AutoAdvancePinnedClass =>
        _events?.Active is { IsLegacy: false } day
            ? day.AutoAdvanceClassOr(_settings.AutoAdvanceClassName)
            : _settings.AutoAdvanceClassName;

    /// <summary>Which round a finished race should complete when none was picked. See RoundPicker.</summary>
    private RoundDefinition? PickNextRound(IReadOnlyList<RacerStanding> ordered) =>
        _rounds is null
            ? null
            : RoundPicker.Next(
                _rounds,
                _events,
                AutoAdvanceScope,
                AutoAdvancePinnedClass,
                ordered.Select(s => _profiles.Get(s.TransponderId)?.ClassName),
                message => StatusMessage?.Invoke(message));


    /// <summary>
    /// Names the saved session. Uses the round's label when the race is tied
    /// to one, so the session pickers elsewhere show "Heat 1" instead of a
    /// generic "Race" - without that there's no way to match a qualifying
    /// figure back to the heat that produced it.
    /// </summary>
    private string BuildSessionLabel(RoundDefinition? round)
    {
        var typed = _txtSessionLabel.Text.Trim();

        var baseLabel = !string.IsNullOrWhiteSpace(typed)
            ? typed
            : !string.IsNullOrWhiteSpace(round?.RoundLabel)
                ? round!.RoundLabel
                : "Race";

        return _currentRaceIsDemo ? $"{SessionRecord.DemoLabelPrefix} {baseLabel}" : baseLabel;
    }

    private void SaveCompletedSessionToHistory()
    {
        if (_race.IsPracticeMode)
        {
            StatusMessage?.Invoke("[session] Practice session - not auto-saved (use Save Practice Session).");

            // Every way out of here clears the journal: the race is over
            // either way, and one left behind is offered back for recovery
            // on the next launch as if it had never finished.
            _journal.Clear();
            return;
        }

        // Demo races are simulated, so they stay out of history unless the
        // user opts in. Even then the saved copy is marked as a demo and
        // scores nothing - no round marked as run, no championship points,
        // no badges, no personal bests or records. Every lap time in it was
        // invented, and a rehearsal quietly filling in a real heat (or
        // handing out a real driver's win) is worse than it not being saved.
        if (_currentRaceIsDemo && !_settings.SaveDemoSessions)
        {
            StatusMessage?.Invoke(
                "[session] Demo race finished but was NOT saved - tick \"Save demo races to history\" "
                + "on the Demo Mode page to keep a copy in Race History (marked [DEMO], never scored).");

            // Never reaches the recap, which is what normally signals the end
            // of the broadcast - so it is signalled here, or music set to
            // fade after the recap would play on after every unsaved demo.
            SignalSessionAudioFinishedAfterFinishCall();
            _journal.Clear();
            return;
        }

        var ordered = _race.GetOrderedStandings();

        // Ghosts belong on the live board - being there is the entire point
        // of them - but they are a pace reference, not entrants. A saved
        // result is what feeds finishing positions into the programme, the
        // championship and the qualifying figures, so a ghost taking third
        // did not just score the points for third: it pushed every real
        // driver behind it down a place and took a place off each of them.
        var racers = ordered.Where(s => !GhostService.IsGhost(s.TransponderId)).ToList();

        // Both of these used to return silently, which made a race that
        // finished but never appeared in history impossible to diagnose.
        if (racers.Count == 0)
        {
            StatusMessage?.Invoke(ordered.Count == 0
                ? "[session] Not saved - no racers had any recorded laps."
                : "[session] Not saved - only ghost cars were on track, and a ghost is not a result.");

            // Same as the unsaved demo above: no recap will follow, so the
            // end of the broadcast is signalled here instead.
            SignalSessionAudioFinishedAfterFinishCall();

            // Finished, and nothing to save - a ghost-only race's journal
            // was left behind and offered back on the next launch.
            _journal.Clear();
            return;
        }

        if (racers.Count != ordered.Count)
        {
            StatusMessage?.Invoke(
                $"[session] {Plural.Of(ordered.Count - racers.Count, "ghost car")} left out of the saved result - "
                + "they set the pace, they don't score.");
        }

        if (_sessionStartedAtUtc is null)
        {
            // The start time is stamped when the race enters the Racing
            // state. If it's missing, this control never saw that
            // transition - so fall back to the session's own start rather
            // than throwing the results away.
            _sessionStartedAtUtc = _race.RaceStartUtc ?? DateTime.UtcNow;
            StatusMessage?.Invoke(
                $"[session] Start time was missing - fell back to {AppTime.LogStamp(_sessionStartedAtUtc.Value.ToLocalTime())}.");
        }

        var results = racers.Select((s, i) =>
        {
            var profile = _profiles.Get(s.TransponderId);
            return new SessionResultEntry
            {
                Position = i + 1,
                Transponder = s.TransponderId,
                DriverName = !string.IsNullOrWhiteSpace(profile?.DriverName) ? profile!.DriverName : s.TransponderId,
                CarModel = profile?.CarModel ?? string.Empty,
                Laps = s.Laps,
                FastLapMs = s.FastestLapMs,
                ElapsedMs = s.ElapsedMs,
                Status = "active",
                LapTimesMs = s.LapTimesMs.ToList(),
                RejectedLaps = s.RejectedLaps,
                LapsLed = s.LapsLed,
                StartPosition = s.StartPosition,

                // Copies, not the live warnings. The board keeps its
                // standings after the flag, and Clear Penalty there zeroes
                // each warning's time - which, through the shared objects,
                // quietly rewrote the saved result's warnings the next time
                // the history was written, although the log says a change
                // after the flag leaves the history alone.
                Warnings = s.Warnings.Select(w => new RaceWarning
                {
                    AtUtc = w.AtUtc,
                    RaceTimeMs = w.RaceTimeMs,
                    Number = w.Number,
                    Reason = w.Reason,
                    Action = w.Action,
                    PenaltyMs = w.PenaltyMs,
                }).ToList(),
            };
        }).ToList();

        // The Round box's choice, or the round a restored race was run for.
        var round = RoundForThisRace();
        _restoredRoundId = null;

        // Nothing picked by hand? Take the next unrecorded round in program
        // order, so running races simply works through the heats. Without
        // this, a finished race quietly attached to nothing unless the user
        // remembered to choose a round first.
        //
        // Never for a demo: it would mark a real heat as run with invented
        // results, and seeding and the mains would then be built from them.
        var autoAdvanced = false;
        if (round is null && _rounds is not null && AutoAdvanceWanted && !_currentRaceIsDemo)
        {
            round = PickNextRound(racers);
            autoAdvanced = round is not null;
        }

        var record = new SessionRecord
        {
            SessionType = RaceSessionType.Race,

            // A demo keeps the round's name in its label (so it is clear
            // what was being rehearsed) but is never linked to the round.
            RoundId = _currentRaceIsDemo ? null : round?.Id,
            IsDemo = _currentRaceIsDemo,
            TrackLayoutName = _cmbTrackLayout.SelectedItem as string == "(none)" ? string.Empty : (string)(_cmbTrackLayout.SelectedItem ?? string.Empty),
            Label = BuildSessionLabel(round),
            StartedAtUtc = _sessionStartedAtUtc.Value,
            EndedAtUtc = DateTime.UtcNow,
            Results = results,

            // Says this result carries rejected laps, laps led and grid
            // positions. Without it an older session looks identical to a
            // race nobody put a wheel wrong in.
            RecordsIncidents = true,
        };

        // A black flag from the warning ladder becomes a DQ on the saved
        // result, logged as a correction so it can be seen and reversed in
        // Race History like any other.
        ApplyBlackFlags(record);

        // The history write, and only if it landed, the round linked and the
        // journal let go - in RaceFiling, where the order is tested. What
        // this page does around it runs through the two callbacks, at the
        // points it always ran.
        var filed = RaceFiling.Commit(
            record,
            round,
            autoAdvanced,
            AutoAdvanceWanted,
            _sessionHistory,
            _rounds,
            _journal,
            afterSaved: () =>
            {
                _sessionStartedAtUtc = null;

                if (record.IsDemo)
                {
                    // Cleared rather than left over from the last real race, so the
                    // results card never shows somebody else's badges.
                    _lastUnlocks = new List<AchievementUnlock>();

                    StatusMessage?.Invoke(
                        "[session] Demo race - kept in Race History marked [DEMO] only. It is not linked to a heat and "
                        + "earns no points, badges, personal bests or records.");
                }
                else
                {
                    AwardAchievements(record);
                }

                SpeakRaceSummary(record);
            },
            beforeJournalCleared: () =>
            {
                TrySaveAutoResultsCard(record);

                // Once, on every path that saved: a demo race and a race with
                // no round both still need the page refreshed. After the round
                // is linked - see RaceFiling.Commit.
                SessionSaved?.Invoke();
            },
            log: message => StatusMessage?.Invoke(message),
            raceDayOpen: RoundPicker.RaceDayOpen(_events));

        if (filed == RaceFiling.Outcome.NotSaved)
        {
            StatusMessage?.Invoke(
                $"[session] COULD NOT SAVE \"{record.Label}\" - race-history.json could not be written "
                + "(see the [storage] line above). The race is still in the crash journal and has NOT been lost.");

            // The session start is cleared either way: the race is over
            // whatever the disk did.
            _sessionStartedAtUtc = null;

            AppDialog.Show(
                this,
                $"\"{record.Label}\" could not be written to the race history - something else on this PC has the "
                + "file open, the disk is full, or the history could not be read when HeatCaller started (and so "
                + "is not being written over).\r\n\r\nThe race has NOT been lost: it is still in the crash "
                + "journal, and HeatCaller will offer it back the next time it starts. Close anything that might be "
                + "reading the HeatCaller folder (a backup or sync tool is the usual cause) and restart HeatCaller "
                + "before running the next race.",
                "Race not saved",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Opens the pre-race check.
    ///
    /// The roster is the selected round's if there is one, because that is
    /// the list somebody has already agreed to. Failing that it is whoever
    /// is already on the board, which is at least a list of real drivers
    /// rather than every profile ever saved - checking a car that is not in
    /// this race and reporting it missing would train people to ignore this
    /// within an evening.
    /// </summary>
    private void ShowReadinessCheck()
    {
        if (_transponderWatch is null)
        {
            AppDialog.Show(this,
                "Nothing is listening to the decoder, so there is nothing to check against.",
                "Pre-race check", MessageBoxButtons.OK, MessageBoxIcon.Information);

            return;
        }

        var round = _activeRound?.Invoke();

        List<string> roster;
        string source;

        if (round is { AssignedTransponders.Count: > 0 })
        {
            roster = round.AssignedTransponders.ToList();
            source = string.IsNullOrWhiteSpace(round.RoundLabel) ? "round roster" : $"roster for {round.RoundLabel}";
        }
        else
        {
            roster = _race.GetOrderedStandings().Select(s => s.TransponderId).ToList();
            source = "drivers already on the board";
        }

        using var form = new ReadinessCheckForm(roster, _transponderWatch, _profiles, source);
        form.ShowDialog(this);
    }

    /// <summary>
    /// Writes the crash journal, with the round and the typed race name the
    /// race is being run under, so a restore can put those back too.
    /// </summary>
    private void WriteJournal(bool force = false) =>
        _journal.Record(_race, _sessionStartedAtUtc, SelectedTrackName(), force,
            RoundForThisRace()?.Id, _txtSessionLabel.Text);

    /// <summary>
    /// The round a race brought back from the crash journal was being run
    /// for, and the session generation it came back in. The Round box
    /// belongs to the host, which knows nothing of the restore, so after a
    /// crash it is usually empty - and the result was then filed against
    /// whichever round auto-advance guessed. Only honoured while that
    /// restored race is the one on the board.
    /// </summary>
    private string? _restoredRoundId;
    private int _restoredRoundGeneration = -1;

    /// <summary>
    /// The round a race brought back from the crash journal was being run
    /// for, while that race is still the one on the board - so the host can
    /// show it in its Round box. Null when there is none.
    /// </summary>
    public string? RestoredRoundId =>
        _restoredRoundId is { } id && _restoredRoundGeneration == _race.SessionGeneration ? id : null;

    /// <summary>
    /// The round this race is for: the one picked in the Round box, or -
    /// when none is picked - the one a restored race was being run for.
    /// </summary>
    private RoundDefinition? RoundForThisRace()
    {
        if (_activeRound?.Invoke() is { } picked)
        {
            return picked;
        }

        return _restoredRoundId is { } id && _restoredRoundGeneration == _race.SessionGeneration
            ? _rounds?.Get(id)
            : null;
    }

    /// <summary>The track layout chosen for this race, or empty when none is.</summary>
    private string SelectedTrackName() =>
        _cmbTrackLayout.SelectedItem as string is { } name && name != "(none)" ? name : string.Empty;

    /// <summary>
    /// Offers back a race that was running when the app last stopped.
    ///
    /// Called once, after the window is up. The journal file existing at all
    /// means the last race never reached the history - so if there is one,
    /// something went wrong, and the race is still recoverable.
    /// </summary>
    public void OfferRaceRecovery()
    {
        if (RaceJournal.Unfinished() is not { } entry)
        {
            return;
        }

        if (_race.State != RaceState.Idle)
        {
            // Something is already running - never interrupt that.
            return;
        }

        // A practice is not offered back as a race. Restored here it came
        // back in practice mode with the Practice page not running it - so
        // nothing could stop it, and Race Control refused every start
        // because "practice is running". Set aside rather than deleted, as
        // a declined race is, so the laps are still on disk.
        if (entry.IsPractice)
        {
            RaceJournal.Discard();
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [practice] The app closed during practice - that session was set aside, not restored.");
            return;
        }

        var answer = AppDialog.Show(this,
            entry.Describe()
            + "\r\n\r\nIt never reached the race history, so it can still be recovered. Restore it?"
            + "\r\n\r\nLaps, penalties, positions and the fastest lap come back as they were, and the race "
            + "clock picks up where it stopped rather than counting the time the app was shut."
            + "\r\n\r\nIt comes back STOPPED, under a red flag, so nothing counts while the cars are being "
            + "put back on the track. Throw the green flag when everyone is ready."
            + "\r\n\r\nOne thing to know: each car's first lap after the green is timed from its last "
            + "crossing before the app closed, so it includes the stoppage and looks long. It counts as a "
            + "lap, but never as a best lap."
            + "\r\n\r\nChoosing No keeps a copy on disk either way, so this is not your last chance.",
            "Restore the unfinished race?",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (answer != DialogResult.Yes)
        {
            RaceJournal.Discard();
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Unfinished race set aside, not restored.");
            return;
        }

        try
        {
            _suppressFlagAnnouncements = true;

            try
            {
                RaceJournal.Restore(_race, entry);
            }
            finally
            {
                _suppressFlagAnnouncements = false;
            }

            _sessionStartedAtUtc = entry.SessionStartedAtUtc ?? entry.RaceStartUtc ?? DateTime.UtcNow;
            _previousFlag = _race.Flag;

            // The round and the typed name the race was run under. Null in a
            // journal written before they were stored, which leaves both as
            // they are now.
            _restoredRoundId = string.IsNullOrWhiteSpace(entry.RoundId) ? null : entry.RoundId;
            _restoredRoundGeneration = _race.SessionGeneration;

            if (entry.RaceLabel is { } raceLabel)
            {
                _txtSessionLabel.Text = raceLabel;
            }

            if (!string.IsNullOrWhiteSpace(entry.TrackLayoutName))
            {
                var index = _cmbTrackLayout.Items.IndexOf(entry.TrackLayoutName);

                if (index >= 0)
                {
                    _cmbTrackLayout.SelectedIndex = index;
                }
            }

            // Written again now that the start time and the track are the
            // restored race's own. RestoreSnapshot raises Racing and the red
            // flag before either was set, and the journal written on that
            // flag carried whatever start time and track were here before -
            // so a second crash before the next crossing came back with the
            // wrong ones.
            WriteJournal(force: true);

            RenderLeaderboard();
            UpdateClockDisplay(force: true);

            StatusMessage?.Invoke(
                $"{AppTime.LogStampNow()}  [race] Restored the unfinished race - {Plural.Of(entry.Standings.Count, "driver")}, "
                + $"{Plural.Of(entry.LapCount, "lap")}, held under a red flag. Green when the cars are back on the track, "
                + "or checkered to save it as it stands.");
        }
        catch (Exception ex)
        {
            AppDialog.Show(this,
                $"That race couldn't be restored: {ex.Message}",
                "Restore failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>
    /// Automatically renders and saves a shareable results card PNG the
    /// moment a race completes, so there's always one ready to share
    /// without needing a manual trip to Race History afterward. Silently
    /// skipped if it fails for any reason - this is a convenience, not
    /// something that should ever block or interrupt finishing a race.
    /// </summary>
    private void TrySaveAutoResultsCard(SessionRecord record)
    {
        try
        {
            var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "HeatCaller Results");
            Directory.CreateDirectory(folder);

            var safeLabel = string.Concat(record.Label.Select(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' ? c : '_')).Trim();
            var fileName = $"{record.StartedAtUtc.ToLocalTime():yyyy-MM-dd_HH-mm}_{(string.IsNullOrWhiteSpace(safeLabel) ? "Race" : safeLabel)}_results.png";
            var path = Path.Combine(folder, fileName);

            using var bitmap = ResultsCardRenderer.Render(record, _lastUnlocks, ChartPalette.For(_settings.ResultsCardStyle));
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);

            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [results] Shareable results card saved to {path}");
        }
        catch
        {
            // Never let a failure here interrupt finishing a race - the
            // same card can still be generated manually from Race History.
        }
    }
}
