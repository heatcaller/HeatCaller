namespace HeatCaller;

/// <summary>
/// Settings for local LLM colour commentary.
///
/// The page leads with what actually writes the lines - the club relay, or
/// the small model built into HeatCaller - because the most likely question
/// anyone has here is which of the two is doing the work tonight, and that
/// should be obvious from this page rather than discovered mid-race.
///
/// Neither needs anything installed any more. Until 14 Oct 2026 the local
/// path was Ollama, which asked a club volunteer for a command prompt and an
/// "ollama pull" before the best feature in the app did anything; it is now
/// one choice among three rather than the only way.
/// </summary>
public sealed class CommentaryControl : UserControl
{
    private readonly AppSettings _settings;
    private readonly CommentaryService _commentary;

    private readonly CheckBox _chkEnabled = new ThemedCheckBox();
    // --- The model on this PC (14 Oct 2026) ---
    //
    // Was Ollama, which meant a separate install and a command prompt
    // before the best feature in the app did anything - a thing no club
    // volunteer should be asked for. A small model now ships inside the
    // installer and runs in this process, so there is nothing to set up and
    // nothing to point at: one tick box, and the Endpoint and Model fields
    // that configured Ollama are gone with it.
    private readonly CheckBox _chkLocalModel = new ThemedCheckBox();
    private readonly NumericUpDown _numTimeout = new();
    private readonly CheckBox _chkUseRelay = new ThemedCheckBox();

    /// <summary>
    /// One tick box per broadcast segment, built from CommentaryBudget's
    /// own table rather than declared here.
    ///
    /// A segment is meant to be one row in that table and nothing else;
    /// thirteen fields and thirteen handlers on this page would mean two
    /// places to add a segment and one of them getting forgotten. A
    /// segment added later turns up here on its own, switched on.
    /// </summary>
    private readonly Dictionary<SegmentKind, CheckBox> _segmentBoxes = new();

    private readonly CheckBox _chkCoHost = new ThemedCheckBox();
    private readonly TextBox _txtCoHostName = new();
    private readonly ComboBox _cmbCoHostVoice = new();

    // --- Recording the night (11 Oct 2026) ---
    //
    // The recording and the per-driver clips have been written after every
    // race since they were built, and no page had a single control for any
    // of it: a club could not turn it off, could not find the files, and
    // was never told that a season of broadcast was accumulating in
    // Documents. See RaceAudioStore.
    //
    // The store is this card's own rather than the one Race Control holds.
    // Both load the same small file and neither caches a decision from it -
    // Race Control reads its switches at each green - so a change made
    // here is in force at the next race without anything being told.
    private readonly RaceAudioStore _audio = RaceAudioStore.Load();
    private readonly CheckBox _chkRecord = new ThemedCheckBox();
    private readonly CheckBox _chkClips = new ThemedCheckBox();
    private readonly TextBox _txtAudioFolder = new();
    private readonly Button _btnAudioFolder = new();
    private readonly Button _btnAudioOpen = new();
    private readonly NumericUpDown _numKeepNights = new();
    private readonly Label _lblOnDisk = new();

    // --- What the co-host is LIKE (11 Oct 2026) ---
    //
    // The exchange worked and still sounded like one commentator cut in
    // half, because the two of them agreed with each other. A disposition
    // is what makes a booth pair a pair - see CoHostStance. The line under
    // the picker says what the club has just chosen in its consequences
    // rather than its label, the way the show music's Describe lines do.
    private readonly ComboBox _cmbCoHostStance = new();

    // --- How much the two of them actually talk (14 Oct 2026) ---
    //
    // A two-hander was always two turns - a remark and a reply - which is a
    // call-and-response rather than a conversation, and the segments that
    // can be exchanges are the ones with something worth talking about. And
    // the co-host was held for the mains unless the whole evening was made
    // chattier, which is a blunt way to ask for one thing.
    private readonly ComboBox _cmbExchangeTurns = new();

    /// <summary>
    /// Delivery tags on spoken commentary. Was the recap's alone and not a
    /// choice at all - segments were never tagged, so the show's energy
    /// dropped the moment the recap ended.
    /// </summary>
    private readonly CheckBox _chkVoiceTags = new ThemedCheckBox();
    private readonly CheckBox _chkTwoVoicesInHeats = new ThemedCheckBox();
    private readonly Label _lblCoHostStance = new();

    // --- How chatty the night is (12 Oct 2026) ---
    //
    // The pacing gives an evening a shape - quiet through the heats,
    // everything saved for the mains - and this is the volume that shape
    // sits at. One dial, five stops, and the curve survives all of them.
    // See ShowChatter. The line under it works its figures out from the
    // real tables rather than repeating them, so it cannot end up lying.
    private readonly ComboBox _cmbChatter = new();
    private readonly Label _lblChatter = new();

    // --- And how fast it says it (14 Oct 2026) ---
    //
    // The dial above is HOW MUCH. This one is HOW FAST, and they had to be
    // two controls: the chattiness dial scales a per-DRIVER cap, so three
    // cars at a cap of two are six calls a lap and ten cars are twenty,
    // every one of them inside the cap the club set. Nothing had an
    // opinion about the field until this. See AnnouncerDensity, which has
    // the console log the report came with.
    private readonly ComboBox _cmbDensity = new();
    private readonly Label _lblDensity = new();

    // --- Club notes (12 Oct 2026) ---
    //
    // The asides that are not about the racing, and the two ways the room
    // can send one in. Both OFF until a club asks for them: each writes to
    // this PC's disk on a stranger's request, and nothing from either can
    // be said without the race director ticking it off (ClubNotes).
    private readonly CheckBox _chkNotesWifi = new ThemedCheckBox();
    private readonly CheckBox _chkNotesRelay = new ThemedCheckBox();
    private readonly CheckBox _chkListenAlong = new ThemedCheckBox();
    private readonly Label _lblListenAlong = new();
    private readonly Label _lblNotesWhere = new();

    // --- The room's vote (12 Oct 2026) ---
    //
    // Off until a club asks for it, for the one reason every public route
    // here is: it writes to this PC's disk on a stranger's request. Unlike
    // the notes above it needs no approval gate, because a vote is a tap
    // on a name the club itself put on the ballot - nothing a phone sends
    // can become a word the announcer says. See RoomVote.
    private readonly CheckBox _chkVoteWifi = new ThemedCheckBox();
    private readonly CheckBox _chkVoteRelay = new ThemedCheckBox();
    private readonly Label _lblVoteWhere = new();
    private readonly CheckBox _chkIntros = new ThemedCheckBox();
    private readonly CheckBox _chkIntrosSelective = new ThemedCheckBox();
    private readonly CheckBox _chkSummaries = new ThemedCheckBox();
    private readonly CheckBox _chkTalkingPoints = new ThemedCheckBox();
    private readonly NumericUpDown _numFinishers = new();
    private readonly CheckBox _chkMentionFastestLap = new ThemedCheckBox();
    private readonly NumericUpDown _numInterval = new();
    private readonly NumericUpDown _numObservations = new();
    private readonly ComboBox _cmbTone = new();
    private readonly NumericUpDown _numMaxWords = new();
    private readonly NumericUpDown _numVariety = new();
    private readonly CheckBox _chkBroadcastVoice = new ThemedCheckBox();
    private readonly NumericUpDown _numStability = new();
    private readonly NumericUpDown _numStyleEx = new();
    private readonly NumericUpDown _numSummaryDelay = new();
    private readonly CheckBox _chkTeaser = new ThemedCheckBox();
    private readonly TextBox _txtTeaser = new();
    private readonly NumericUpDown _numTeaserGap = new();
    private readonly CheckBox _chkDeepDuck = new ThemedCheckBox();
    private readonly NumericUpDown _numDuckPercent = new();

    /// <summary>
    /// What the last test found, beside the Test Connection button. As tall
    /// as the longest of the things it usually says at the width it has, so
    /// a result coming back never moves the cards under it (SteadyLabel);
    /// the brain's own error text can run longer, and then it grows.
    /// </summary>
    private readonly SteadyLabel _lblTestResult = new();

    /// <summary>
    /// Which brain the page expects to write, under the relay box. As tall
    /// as the longest of the things it says, so ticking the box or filling
    /// the Club Website page in never moves the Connection card under it
    /// (SteadyLabel).
    /// </summary>
    private readonly SteadyLabel _lblRelayState = new();

    // --- Broadcast (27 Sep 2026) ---
    private readonly CheckBox _chkOpening = new ThemedCheckBox();
    private readonly CheckBox _chkClosing = new ThemedCheckBox();
    private readonly TextBox _txtShowName = new();

    /// <summary>
    /// What the main announcer calls himself. The one voice carrying the
    /// whole programme, and until 11 Oct 2026 the only one on this page
    /// that could not be named - see AppSettings.RecapAnnouncerName.
    /// </summary>
    private readonly TextBox _txtAnnouncerName = new();
    private readonly CheckBox _chkInterview = new ThemedCheckBox();
    private readonly TextBox _txtMechanicName = new();
    private readonly ComboBox _cmbMechanicVoice = new();
    private readonly Button _btnLoadVoices = new();
    private readonly Button _btnTestMechanic = new();

    // --- The honours (10 Oct 2026) ---
    //
    // The badges a race unlocked, read out as a segment of the recap by a
    // THIRD voice: the announcer throws to them, they go over who got
    // what, and the announcer takes the programme back. Beside the
    // mechanic's rows because it is the same two questions - what do we
    // call them, and which voice are they - answered the same way, and
    // because both belong to the recap's broadcast. See RaceHonours.
    private readonly CheckBox _chkBadgeHonours = new ThemedCheckBox();
    private readonly TextBox _txtHonoursName = new();
    private readonly ComboBox _cmbHonoursVoice = new();
    private readonly Button _btnTestHonours = new();

    // The race recap host - the voice that reads the recap show and only
    // the recap show (16 Oct 2026). See AppSettings.RecapHostVoice.
    private readonly ComboBox _cmbRecapHostVoice = new();
    private readonly Button _btnTestRecapHost = new();
    private readonly TextBox _txtRecapHostName = new();

    // --- The commercial break (10 Oct 2026) ---
    //
    // One switch and no timings: how long the break runs is however long
    // the model took, which is the whole point of it. See
    // AppSettings.RecapAdBreak for why there is nothing else here and why
    // it is not on the Sponsors page.
    private readonly CheckBox _chkAdBreak = new ThemedCheckBox();

    /// <summary>Which engine's voices the mechanic's list holds - see FillMechanicVoices.</summary>
    private readonly Label _lblVoiceSource = new();

    // --- The show's music (8 Oct 2026) ---
    //
    // An opener in front of the voice and a bed under it, for the segments
    // that open and close the evening. The levels are the part that needs
    // an ear rather than a number, so there is a Test button that runs a
    // real show rather than a preview of one.
    private readonly CheckBox _chkShowMusic = new ThemedCheckBox();

    // The shipped library, one list per part (11 Oct 2026) - see
    // ShowMusicLibrary. Modelled on the Sounds page's rows: a drop-down of
    // what ships, beside a Choose... for the club's own file, and the
    // club's own file wins wherever one is set.
    private readonly ComboBox _cmbOpenerPiece = new();
    private readonly ComboBox _cmbBedPiece = new();
    private readonly ComboBox _cmbStingPiece = new();

    private readonly NumericUpDown _numOpenerLevel = new();
    private readonly NumericUpDown _numBedLevel = new();
    private readonly NumericUpDown _numBedDuck = new();
    private readonly NumericUpDown _numVoiceEnters = new();
    private readonly Button _btnOpenerPick = new();
    private readonly Button _btnOpenerClear = new();
    private readonly Button _btnBedPick = new();
    private readonly Button _btnBedClear = new();
    private readonly Button _btnTestShow = new();

    /// <summary>
    /// Writes and plays the night's own recap - see NightRecap. On the
    /// recording card rather than the show's, because what a club wants
    /// out of it is the FILE: the piece plays once in a hall that is
    /// packing up, and then sits in the night's folder for the people who
    /// were not there.
    /// </summary>
    private readonly Button _btnNightRecap = new();

    /// <summary>What happened when it was pressed, in the club's words.</summary>
    private readonly Label _lblNightRecap = new();

    // --- A driver's own night, and the proof of a line (12 Oct 2026) ---

    /// <summary>
    /// Which driver's piece to write - tonight's drivers, filled when the
    /// box is opened so it is never a list from an hour ago.
    /// </summary>
    private readonly ComboBox _cmbDriverCut = new();

    private readonly Button _btnDriverCut = new();
    private readonly Label _lblDriverCut = new();

    /// <summary>
    /// Tonight's drivers in the order the box lists them, so the box's own
    /// index gives back a transponder.
    ///
    /// A LIST BESIDE THE BOX rather than objects in it. A ComboBox holding
    /// a record needs DisplayMember and a ToString nobody can see, and the
    /// box here is redrawn by the theme pass; an index into a list the page
    /// owns is the shape every other picker on this page uses.
    /// </summary>
    private readonly List<(string Transponder, string Name)> _driversTonight = new();

    private readonly Button _btnProof = new();
    private readonly Label _lblProof = new();
    private readonly Button _btnNightBefore = new();
    private readonly Label _lblNightBefore = new();
    private readonly CheckBox _chkNightBeforeRelay = new ThemedCheckBox();
    private readonly Label _lblOpenerFile = new();
    private readonly Label _lblBedFile = new();
    private readonly Label _lblShowResult = new();

    // --- The sting (10 Oct 2026) ---
    //
    // The third piece, and the only one that is not music to listen to: it
    // is the mark BETWEEN two items of a block - see ShowBlock. Beside the
    // opener and the bed because it is chosen the same way and lives in the
    // same folder, and below them because it is the one a club can leave
    // out without losing anything: with no sting a swell of the bed does
    // the same job, and the line beside the row says so.
    private readonly Button _btnStingPick = new();
    private readonly Button _btnStingClear = new();
    private readonly Label _lblStingFile = new();

    // --- The running order (10 Oct 2026) ---
    //
    // A show is no longer the same show every time: the first of the night
    // plays the opener whole and the ones after it come in on its ring-out
    // - see ShowRunningOrder. Where a later show comes in is MEASURED off
    // the club's own file, so the line under the box says what their file
    // is going to do, the way the two Describe lines above it do.
    private readonly CheckBox _chkShowRejoin = new ThemedCheckBox();
    private readonly Label _lblRunningOrder = new();

    /// <summary>The boxes for which segments get the music, by segment - see ShowSegments.Candidates.</summary>
    private readonly Dictionary<SegmentKind, CheckBox> _showBoxes = new();

    /// <summary>
    /// The club's show music, for choosing and describing the files. The
    /// announcer screen keeps one of its own and is told when this one
    /// changes anything - see ShowMusicChanged.
    /// </summary>
    private readonly ShowMusic _showMusic;

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>The pickers' widths as last measured - see Pieces.PickerWidth. The mechanic's is forgotten whenever its list is filled.</summary>
    private (Font Font, int Count, int Width)? _toneWidth;
    private (Font Font, int Count, int Width)? _voiceWidth;
    private (Font Font, int Count, int Width)? _coHostVoiceWidth;
    private (Font Font, int Count, int Width)? _coHostStanceWidth;
    private (Font Font, int Count, int Width)? _exchangeTurnsWidth;
    private (Font Font, int Count, int Width)? _chatterWidth;

    /// <summary>The pacing picker's measured width - see _chatterWidth.</summary>
    private (Font Font, int Count, int Width)? _densityWidth;

    /// <summary>
    /// Raised when the club switches shout-outs from its web page OFF, so
    /// MainForm can tell the relay to stop taking them and throw away
    /// whatever it is holding.
    ///
    /// An event rather than this page reaching for the publisher: a
    /// settings page knows what was ticked and nothing about how the relay
    /// is reached - see RelayPublisher.CloseClubNotesOnTheRelay, which
    /// also explains why switching it off has to be SENT rather than
    /// inferred from the polling stopping.
    /// </summary>
    internal event Action? NotesClosedOnTheRelay;

    /// <summary>
    /// Raised when the club switches the relay's vote route off, so the
    /// relay is TOLD rather than left taking votes nobody will read. The
    /// mirror of <see cref="NotesClosedOnTheRelay"/>.
    /// </summary>
    internal event Action? VoteClosedOnTheRelay;

    /// <summary>
    /// Raised when the club switches listening along OFF, so the relay is
    /// told to stop offering the announcer and to throw away the lines it
    /// is holding.
    ///
    /// The most important of these three to actually send. The other two
    /// leave the relay collecting things nobody reads; this one leaves the
    /// club's own announcer - possibly a named member's cloned voice -
    /// playing to anybody with the link, from a club that has just said it
    /// does not want that. See RelayPublisher.CloseListeningOnTheRelay.
    /// </summary>
    internal event Action? ListeningClosedOnTheRelay;

    /// <summary>
    /// Raised when the club unticks putting the night-before preview on its
    /// page, so a preview already published comes down then rather than
    /// standing until the first race is filed. The same reason as the
    /// three above: a setting saved is not a page changed.
    /// </summary>
    internal event Action? NightBeforeClosedOnTheRelay;
    private (Font Font, int Count, int Width)? _honoursVoiceWidth;
    private (Font Font, int Count, int Width)? _recapHostVoiceWidth;

    /// <summary>The usual captions of the two buttons that say so while they work - see BuildLayout's Steady.</summary>
    private const string TestCaption = "Test Connection";
    private const string LoadVoicesCaption = "Load Voices";
    private const string TestShowCaption = "Play It";
    private const string NightRecapCaption = "Write And Play It";
    private const string DriverCutCaption = "Write And Play It";
    private const string ProofCaption = "Why It Said That";

    /// <summary>The night-before button's caption, held while it says "Writing..." - see Steady.</summary>
    private const string NightBeforeCaption = "Write The Preview";

    /// <summary>The boxes' and pickers' widths at the normal text size - see SizeFields.</summary>
    private const int TonePickerWidth = 220;
    private const int ShowNameWidth = 280;

    /// <summary>The library drop-downs. Wide enough for "Good Morning America 2" and "Opening Drive (132 BPM)" without cutting either off.</summary>
    private const int ShowPieceWidth = 220;

    /// <summary>
    /// How wide the driver picker is at the normal text size.
    ///
    /// Two hundred and sixty, like the mechanic's voice picker, and it has
    /// to be SET: left to itself a ComboBox is 121 pixels and would cut
    /// most club members' names in half. The Sponsors page shipped exactly
    /// that fault on 12 Oct 2026 and LayoutTests caught it at every text
    /// size - see SponsorsControl.SponsorPictureWidth.
    ///
    /// FIXED RATHER THAN GROWN TO ITS LONGEST ITEM, which is what the
    /// mechanic's voice picker does (Pieces.PickerWidth). That list ships
    /// with the app and its longest name is known; this one is the club's
    /// own members, so growing to fit would let one driver with a long name
    /// widen the card - and at 200% text that is how a page stops fitting
    /// on a 1280 screen. A name past this is clipped in the box and is
    /// still right in the piece and in the file name.
    /// </summary>
    private const int DriverPickerWidth = 260;
    private const int MechanicNameWidth = 200;
    private const int VoicePickerWidth = 260;
    private const int TeaserWidth = 420;

    /// <summary>The teasers the box shows before it scrolls, a line each.</summary>
    private const int TeaserLines = 4;

    /// <summary>The engine the mechanic's list was last filled for - what a pick is saved against.</summary>
    private VoiceEngine _voiceListEngine = VoiceEngine.ElevenLabs;

    /// <summary>Set while the list is being filled, so filling it isn't taken for a pick.</summary>
    private bool _fillingVoices;

    private bool _loadingVoices;

    /// <summary>A voice the mechanic can be given - what is saved for it, and what the list shows.</summary>
    /// <param name="Id">The ElevenLabs voice id, the HeatCaller voice's id, or Kokoro's name for the voice. Empty for the announcer's own voice.</param>
    /// <param name="Rev">The HeatCaller catalogue's fingerprint for the voice; empty for the other engines.</param>
    public sealed record VoiceChoice(string Id, string Name, string Rev, string Display)
    {
        public override string ToString() => Display;
    }

    private static readonly VoiceChoice AnnouncersVoice = new(string.Empty, string.Empty, string.Empty, "(the announcer's own voice)");

    /// <summary>Which engine speaks, as chosen on Voice Settings. Set by the host; without it the list offers ElevenLabs.</summary>
    internal Func<VoiceEngine>? EngineInUse { get; set; }

    /// <summary>Fetches the voices one engine offers. Set by the host, which has the clients. Throws with a sentence for the page when it can't.</summary>
    internal Func<VoiceEngine, Task<IReadOnlyList<VoiceChoice>>>? LoadVoices { get; set; }

    /// <summary>Says a test line in the mechanic's voice, even while the announcer is muted. Set by the host.</summary>
    internal Action<SecondVoice>? SpeakAsMechanic { get; set; }

    /// <summary>
    /// The same for the honours reader, with a line of the kind they
    /// actually say.
    ///
    /// Its own hook and not the mechanic's reused: the hook carries the
    /// test LINE as well as the voice, and hearing a prospective honours
    /// reader say "we went up a tooth on the pinion" tells a club nothing
    /// about whether they sound right reading out badges.
    /// </summary>
    internal Action<SecondVoice>? SpeakAsHonours { get; set; }

    /// <summary>The same for the race recap host, with a line of the kind a recap opens on. Set by the host.</summary>
    internal Action<SecondVoice>? SpeakAsRecapHost { get; set; }

    /// <summary>
    /// Plays a show open with a test line over it, as a real segment
    /// would, and hands back what was wrong or null. Set by the host,
    /// which has the announcer.
    /// </summary>
    internal Func<Task<string?>>? TestShowMusic { get; set; }

    /// <summary>
    /// Writes tonight's recap, plays it out once and keeps it as a file -
    /// AnnouncerControl.PlayTonightsRecapAsync. Null until wired, and then
    /// the button does nothing.
    /// </summary>
    internal Func<Task<string?>>? PlayTonightsRecap { get; set; }

    /// <summary>
    /// Tonight's drivers, for the picker - whoever has a race filed for the
    /// night, as MainForm reads them. Null until wired, and then the picker
    /// stays empty and says so.
    /// </summary>
    internal Func<IReadOnlyList<(string Transponder, string Name)>>? DriversTonight { get; set; }

    /// <summary>
    /// Writes one driver's night, plays it out once and keeps it as a file -
    /// AnnouncerControl.PlayADriversNightAsync. Takes their transponder and
    /// their name. Null until wired, and then the button does nothing.
    /// </summary>
    internal Func<string, string, Task<string?>>? PlayADriversNight { get; set; }

    /// <summary>
    /// Why the announcer said what it last said, written to the developer
    /// console - AnnouncerControl.TheProofOfTheLastLine. Hands back a
    /// sentence for the page either way.
    /// </summary>
    internal Func<string>? ProofOfTheLastLine { get; set; }

    /// <summary>
    /// Writes a preview of the next race day and saves it where the club
    /// can get at it - see <see cref="NightBefore"/>.
    ///
    /// Hands back a sentence either way: where the file went, or why there
    /// is nothing to preview. The host owns this because the preview is
    /// built from the programme, the seasons and the club's own history,
    /// and those stores belong to the form rather than to this page.
    /// </summary>
    internal Func<Task<string?>>? WriteTheNightBefore { get; set; }

    /// <summary>
    /// Tells the announcer screen that the show's music has changed, so it
    /// reads the files again rather than playing the take it loaded at the
    /// start of the evening. Set by the host.
    /// </summary>
    internal Action? ShowMusicChanged { get; set; }

    /// <summary>
    /// The club's commentary count for the month, as the relay reports it
    /// on the way past - how many lines are spent, and the cap.
    ///
    /// Raised here because this page is what builds the relay client, and
    /// it builds a new one every time a setting changes. The budget that
    /// wants these numbers lives on the announcer screen's director, which
    /// this page cannot reach; the host joins them up (MainForm.Pages).
    ///
    /// Without it the budget paces a night against a month it knows
    /// nothing about, and falls back to running everything.
    /// </summary>
    internal event Action<int, int>? AllowanceSeen;

    // Guards against a second click while a test is running. Used INSTEAD
    // of disabling the button: disabling the focused control pushes focus
    // to the next tab stop (wrapping to the top of the page), and the
    // page's scrolling then scrolls to it - which is why the page jumped up.
    private bool _testRunning;

    // True while Play It, the night recap or a driver's night is running.
    // Their buttons are held down for the length of the run, and almost
    // anything else on the card calls UpdateEnabledState, which would
    // otherwise put them back up mid-run and let a second one start over
    // the first.
    private bool _testShowRunning;
    private bool _nightRecapRunning;
    private bool _driverCutRunning;

    // The same for the night-before preview, now that UpdateEnabledState
    // gates its button on the announcer too.
    private bool _nightBeforeRunning;

    /// <summary>
    /// The model client's own diagnostic lines ("[llm] Timed out after
    /// 20s..."), for the host's console. Raised on whatever thread the
    /// request finished on - the handler must be safe to call from any
    /// thread, as the console's is.
    /// </summary>
    public event Action<string>? ClientStatusMessage;

    public CommentaryControl(AppSettings settings, CommentaryService commentary)
    {
        _settings = settings;
        _commentary = commentary;
        _showMusic = new ShowMusic(settings);

        Dock = DockStyle.Fill;
        ForeColor = AppTheme.TextDark;

        // The body face, worn rather than set once, so it follows the text
        // size - see TextSize. The page sits in a tab whose row is drawn in
        // the bold face, which it would otherwise take.
        //
        // No design size, no MinimumSize and no scrolling of its own: the
        // 900px floor was kept however narrow the window was, and the page
        // now scrolls when it is taller than the window (PageStack).
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout();
        ApplyToService();

        // Covers the case this page loads with commentary already enabled
        // from a previous session - the model gets warmed immediately
        // rather than waiting for someone to visit this page and click Test.
        if (_settings.CommentaryEnabled)
        {
            TriggerWarmUp();
        }
    }

    /// <summary>The page's own face changed - handed the body face at a new text size - so everything else it keeps in step follows. See TextSize.</summary>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>A new text size, reaching this page through its parent's font - the only way it does, since the page wears a face of its own. See TextSize.</summary>
    protected override void OnParentFontChanged(EventArgs e)
    {
        base.OnParentFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>
    /// Builds the page on the layout pieces (LayoutPieces.cs): the intro,
    /// what it needs and the master switch, then a card for each part of the
    /// job, in the order someone setting this up needs them:
    ///
    ///   Connection      where the model is, with Test Connection and what it
    ///                   found right under the fields it checks (it used to
    ///                   be at the very bottom, a whole page away from them).
    ///   What it writes  intros, recaps, talking points - each switch with
    ///                   its own settings a step in under it.
    ///   Writing style   how those lines are WORDED. The tone here was once
    ///                   headed plain "Style", right under a "Style:" voice
    ///                   setting, so the two read as duplicates.
    ///   Race recap      what happens after the flag, in the order it
    ///                   happens, ending with how the recap SOUNDS.
    ///   Broadcast       the recap dressed up as TV: the opening, the
    ///                   sign-off, and the interview with the mechanic.
    ///
    /// One caption column down every card (FieldGrid.LineUp), so every field
    /// on the page starts at the same x; a setting that belongs to a switch
    /// has its caption a step in (see Stepped), and every hint wraps to the
    /// room it has. Nothing is placed by hand, and the page scrolls when it
    /// is taller than the window (PageStack).
    ///
    /// It used to be placed in pixels: one column at a running y, the text
    /// wrapped at a fixed 900px and a 900px floor on the page, the fields at
    /// widths chosen by eye at the normal text size, and each row's suffix
    /// and second field at an x worked out from the widths of what came
    /// before it on the row.
    /// </summary>
    private void BuildLayout()
    {
        // A line of help in the small muted type the app's hints use,
        // wrapping to the room it is given.
        Label Hint(string text) => _textSize.Wear(
            new Label { Text = text, AutoSize = true, ForeColor = AppTheme.TextMuted },
            () => AppTheme.SmallFont);

        // What follows a field on its line - "seconds", "words". Muted and in
        // the small type when it explains the field; in the body face when it
        // finishes the sentence the caption starts. It never wraps: the line
        // puts it under the field when there is no room beside it (Line).
        Label Suffix(string text, bool muted = true)
        {
            var label = new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = muted ? AppTheme.TextMuted : AppTheme.TextDark,
            };

            if (muted)
            {
                _textSize.Wear(label, () => AppTheme.SmallFont);
            }

            return label;
        }

        static CheckBox Check(CheckBox box, string text)
        {
            box.Text = text;
            box.AutoSize = true;
            return box;
        }

        // Every caption column on the page is one width (FieldGrid.LineUp, at
        // the end), and the caption of a setting that belongs to the switch
        // above it sits a step in, as it always has. The step is kept on the
        // right of a caption that is not in, so the fields still line up down
        // the whole page. The grid keeps a caption's sides as they are set
        // here whenever it moves one.
        static Label Stepped(Label caption)
        {
            caption.Margin = new Padding(UiLayout.Indent, caption.Margin.Top, caption.Margin.Right, caption.Margin.Bottom);
            return caption;
        }

        static Label Flush(Label caption)
        {
            caption.Margin = new Padding(caption.Margin.Left, caption.Margin.Top, caption.Margin.Right + UiLayout.Indent, caption.Margin.Bottom);
            return caption;
        }

        // Several controls along one line, wrapping onto another when the
        // room runs out - each box with a pixel round it for the ring the
        // theme draws outside it (FieldGrid.Inline, Pieces.RingRoom).
        static FlowLayoutPanel Line(params Control[] controls) => FieldGrid.Inline(controls);

        // A button whose caption is swapped while it works ("Testing...",
        // "Loading..."), in a panel of its own that is always as big as its
        // usual caption needs. A button handed straight to a row of buttons
        // or to a Beside is measured again whenever its caption changes, and
        // the measuring takes the dots off (UiLayout.WidthFor): the caption
        // would read "Testing", and the button would shrink while it worked.
        Panel Steady(Button button, string caption)
        {
            var holder = new Panel
            {
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                Tag = AppTheme.KeepBackColor,
            };

            button.Dock = DockStyle.Fill;
            holder.Controls.Add(button);

            _textSize.Keep(() =>
            {
                var size = UiLayout.ButtonSizeFor(caption);

                if (holder.Size != size)
                {
                    holder.Size = size;
                }
            });

            return holder;
        }

        // ------------------------------------------------------------------
        // Intro and master switch
        // ------------------------------------------------------------------

        var lblIntro = new Label
        {
            Text = "Optional colour commentary from a language model. HeatCaller can have it written by the club relay, "
                 + "or by a small model built into HeatCaller that runs on this PC. Neither needs anything installed.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        var lblRequirement = _textSize.Wear(
            new Label
            {
                Text = "The relay writes for clubs whose plan includes commentary - set the Club Website page up and leave the "
                     + "box below ticked. The built-in model covers everything else, including nights with no internet at all, "
                     + "and there is nothing to install for it. If neither writes, commentary is skipped and the normal "
                     + "announcement lines are used instead.",
                AutoSize = true,
                ForeColor = AppTheme.Accent,
            },
            () => AppTheme.SmallFont);

        Check(_chkEnabled, "Enable local LLM commentary");
        _textSize.Wear(_chkEnabled, () => AppTheme.BoldFont);
        _chkEnabled.Checked = _settings.CommentaryEnabled;
        _chkEnabled.CheckedChanged += (_, _) =>
        {
            _settings.CommentaryEnabled = _chkEnabled.Checked;
            _settings.Save();
            ApplyToService();
            UpdateEnabledState();

            if (_chkEnabled.Checked)
            {
                TriggerWarmUp();
            }
        };

        // ------------------------------------------------------------------
        // Where the writing happens
        //
        // Above the Connection card rather than in it: it decides which
        // brain writes, and the card under it is only one of the two.
        // ------------------------------------------------------------------

        Check(_chkUseRelay, "Write through the relay when it can");
        _chkUseRelay.Checked = _settings.CommentaryUseRelay;
        _chkUseRelay.CheckedChanged += (_, _) =>
        {
            _settings.CommentaryUseRelay = _chkUseRelay.Checked;
            _settings.Save();
            ApplyToService();
            UpdateEnabledState();
        };

        var relayHint = Hint(
            "The relay writes with a hosted model no race laptop could run, and needs nothing installed here. The model "
            + "on this PC covers whatever the relay can't write - the internet down, the club's lines for the month used up, or "
            + "commentary not included in the club's plan yet. Untick this and the model on this PC writes everything.");

        // Every sentence UpdateRelayState can produce, built by the same
        // method that produces it. The list is only here so the label can
        // size itself to the longest; typing them out a second time is how
        // it silently stopped matching last time the wording changed.
        _lblRelayState.Wordings = AllRelayStates();

        // ------------------------------------------------------------------
        // Connection
        // ------------------------------------------------------------------

        Check(_chkLocalModel, "Let the model on this PC write when the relay can't");
        _chkLocalModel.Checked = _settings.CommentaryUseLocalModel;
        _chkLocalModel.CheckedChanged += (_, _) =>
        {
            _settings.CommentaryUseLocalModel = _chkLocalModel.Checked;
            _settings.Save();
            ApplyToService();
            UpdateEnabledState();
            UpdateRelayState();
            TriggerWarmUp();
        };

        _numTimeout.Minimum = 2;
        _numTimeout.Maximum = 60;
        _numTimeout.Value = Math.Clamp(_settings.CommentaryTimeoutSeconds, 2, 60);
        _numTimeout.ValueChanged += (_, _) =>
        {
            _settings.CommentaryTimeoutSeconds = (int)_numTimeout.Value;
            _settings.Save();
            ApplyToService();
        };

        var connectionGrid = new FieldGrid();

        Stepped(connectionGrid.AddRow(string.Empty, _chkLocalModel));
        connectionGrid.AddHint(
            "It ships inside HeatCaller - nothing to install, and it works with the internet down. It is small, so its "
            + "lines are plainer than the relay's, which is the point: it only has to cover the races the relay cannot. "
            + "Untick it and those races are simply quieter.");

        Flush(connectionGrid.AddRow("Give up after:", Line(_numTimeout, Suffix("seconds - after this the normal announcement line is used instead"))));

        // Right under the fields it checks. It used to be the last thing on
        // the page, a whole scroll away from the endpoint and model.
        var btnTest = new Button { Text = TestCaption };
        btnTest.Click += async (_, _) => await RunTestAsync(btnTest);

        _lblTestResult.ForeColor = AppTheme.TextMuted;
        _lblTestResult.Wordings = new[]
        {
            "Enable commentary first.",
            "Testing...",
            "No response - any [llm] line in the console says why. "
                + "Commentary will be skipped and normal announcements used.",
            "Working - replied in 99.9s. That's comfortably within the gap between races.",
        };

        // The result beside the button, wrapping to the room to its right -
        // the brain's own error text can run to two lines and more. The button
        // is a column of its own (Beside), so a long result never runs under
        // it, and is level with its caption while it is one line (the Keep
        // at the end).
        var testRow = new Beside(() => UiLayout.ButtonSizeFor(TestCaption).Width)
        {
            SideOnLeft = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        testRow.Add(_lblTestResult, gapBelow: 0);
        testRow.AddSide(Steady(btnTest, TestCaption), gapBelow: 0);

        var connection = new Section("Connection");

        connection.Add(Hint(
            "What writes on this PC. It writes everything when the relay box above is off, and covers whatever the relay "
            + "can't write when it is on - which is what keeps the announcer talking through an internet outage."));
        connection.Add(connectionGrid);
        connection.Add(testRow, gapBelow: 0);

        // ------------------------------------------------------------------
        // What it writes
        // ------------------------------------------------------------------

        Check(_chkIntros, "Race intros - a \"coming up\" line before a round starts");
        _chkIntros.Checked = _settings.CommentaryRaceIntros;
        _chkIntros.CheckedChanged += (_, _) => { _settings.CommentaryRaceIntros = _chkIntros.Checked; _settings.Save(); UpdateEnabledState(); };

        Check(_chkIntrosSelective, "Only for a main, or a race with a known rivalry on the grid");
        _chkIntrosSelective.Checked = _settings.CommentaryIntrosMainsOrRivalriesOnly;
        _chkIntrosSelective.CheckedChanged += (_, _) =>
        {
            _settings.CommentaryIntrosMainsOrRivalriesOnly = _chkIntrosSelective.Checked;
            _settings.Save();
        };

        // No caption of its own - it narrows the switch above it - but in the
        // field column all the same, a step in under that switch.
        var introsGrid = new FieldGrid();

        Stepped(introsGrid.AddRow(string.Empty, _chkIntrosSelective));
        introsGrid.AddHint(
            "Every other race starts without one - fewer words between heats, and the intro saved for the races that "
            + "matter. Rivalries are set on the Rivalries tab.");

        Check(_chkSummaries, "Race recaps - a wrap-up after the checkered flag");
        _chkSummaries.Checked = _settings.CommentaryRaceSummaries;
        _chkSummaries.CheckedChanged += (_, _) => { _settings.CommentaryRaceSummaries = _chkSummaries.Checked; _settings.Save(); UpdateEnabledState(); };

        _numFinishers.Minimum = 1;
        _numFinishers.Maximum = 20;
        _numFinishers.Value = Math.Clamp(_settings.CommentarySummaryFinishers, 1, 20);
        _numFinishers.ValueChanged += (_, _) => { _settings.CommentarySummaryFinishers = (int)_numFinishers.Value; _settings.Save(); };

        // Same as BackupRestore's "day(s)": the number is in the spin box,
        // so the label carries the noun alone and follows it. Last thing on
        // its line, so re-measuring moves nothing.
        string FinishersSuffix() => $"{Plural.Word((long)_numFinishers.Value, "finisher")} in the recap";

        var lblFinishersSuffix = Suffix(FinishersSuffix(), muted: false);
        _numFinishers.ValueChanged += (_, _) => lblFinishersSuffix.Text = FinishersSuffix();

        Check(_chkMentionFastestLap, "Also mention the fastest lap of the race");
        _chkMentionFastestLap.Checked = _settings.CommentarySummaryMentionFastestLap;
        _chkMentionFastestLap.CheckedChanged += (_, _) =>
        {
            _settings.CommentarySummaryMentionFastestLap = _chkMentionFastestLap.Checked;
            _settings.Save();
        };

        var summariesGrid = new FieldGrid();

        Stepped(summariesGrid.AddRow("Mention up to:", Line(_numFinishers, lblFinishersSuffix)));
        summariesGrid.AddField(_chkMentionFastestLap);
        summariesGrid.AddHint("Off if this feels repetitive - the fastest lap already gets its own announcement live during the race.");

        Check(_chkTalkingPoints, "Announcer talking points - suggested lines on screen, never spoken automatically");
        _chkTalkingPoints.Checked = _settings.CommentaryTalkingPoints;
        _chkTalkingPoints.CheckedChanged += (_, _) => { _settings.CommentaryTalkingPoints = _chkTalkingPoints.Checked; _settings.Save(); UpdateEnabledState(); };

        _numInterval.Minimum = 5;
        _numInterval.Maximum = 120;
        _numInterval.Value = Math.Clamp(_settings.CommentaryTalkingPointIntervalSeconds, 5, 120);
        _numInterval.ValueChanged += (_, _) =>
        {
            _settings.CommentaryTalkingPointIntervalSeconds = (int)_numInterval.Value;
            _settings.Save();
        };

        _numObservations.Minimum = 1;
        _numObservations.Maximum = 12;
        _numObservations.Value = Math.Clamp(_settings.CommentaryTalkingPointObservationCount, 1, 12);
        _numObservations.ValueChanged += (_, _) =>
        {
            _settings.CommentaryTalkingPointObservationCount = (int)_numObservations.Value;
            _settings.Save();
        };

        var talkingGrid = new FieldGrid();

        Stepped(talkingGrid.AddRow("Refresh every:", Line(_numInterval, Suffix("seconds - lower feels fresher, higher means less repeated wording"))));
        Stepped(talkingGrid.AddRow("Consider up to:", Line(_numObservations, Suffix("of the current talking points to pick from"))));

        var writes = new Section("What it writes");

        writes.Add(_chkIntros, stretch: false);
        writes.Add(introsGrid);
        writes.Add(_chkSummaries, stretch: false);
        writes.Add(summariesGrid);
        writes.Add(_chkTalkingPoints, stretch: false);
        writes.Add(talkingGrid);

        // Belongs with what it writes - it is the answer to "why not a line
        // every lap?", which is a question about this list.
        writes.Add(
            Hint("Commentary is written between races, not lap by lap. A lap takes a few seconds and a local model usually "
                 + "takes longer than that to answer, so a live per-lap line would describe a moment that had already passed."),
            gapBelow: 0);

        // ------------------------------------------------------------------
        // Writing style - the WORDS
        // ------------------------------------------------------------------

        var toneItems = new[]
        {
            new ToneItem(CommentaryTone.Enthusiastic, "Enthusiastic - trackside hype"),
            new ToneItem(CommentaryTone.Neutral, "Neutral - calm play-by-play"),
            new ToneItem(CommentaryTone.DryUnderstated, "Dry - understated, deadpan"),
        };

        _cmbTone.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbTone.Items.AddRange(toneItems);
        SelectTone(_settings.CommentaryToneStyle);
        _cmbTone.SelectedIndexChanged += (_, _) =>
        {
            if (_cmbTone.SelectedItem is ToneItem item)
            {
                _settings.CommentaryToneStyle = item.Tone;
                _settings.Save();

                // The tag box lives or dies by this - see UpdateEnabledState.
                UpdateEnabledState();
            }
        };

        _numMaxWords.Minimum = 8;
        // Was capped at 60, which silently clamped anything higher - so
        // asking for a long summary appeared to do nothing. 300 is past
        // any useful spoken length while still bounding the request.
        _numMaxWords.Maximum = 300;
        _numMaxWords.Increment = 5;
        _numMaxWords.Value = Math.Clamp(_settings.CommentaryMaxWords, 8, 300);
        _numMaxWords.ValueChanged += (_, _) => { _settings.CommentaryMaxWords = (int)_numMaxWords.Value; _settings.Save(); };

        _numVariety.DecimalPlaces = 1;
        _numVariety.Increment = 0.1m;
        _numVariety.Minimum = 0.2m;
        _numVariety.Maximum = 1.3m;
        _numVariety.Value = (decimal)Math.Clamp(_settings.CommentaryVariety, 0.2, 1.3);
        _numVariety.ValueChanged += (_, _) => { _settings.CommentaryVariety = (double)_numVariety.Value; _settings.Save(); };

        // Length and variety on lines of their own, each under its own
        // caption. Side by side, "Phrasing variety:" sat at an x worked out
        // from the width of "words" before it.
        var styleGrid = new FieldGrid();

        Check(_chkVoiceTags, "Mark the lines excited when they're spoken");
        _chkVoiceTags.Checked = _settings.CommentaryVoiceTags;
        _chkVoiceTags.CheckedChanged += (_, _) =>
        {
            _settings.CommentaryVoiceTags = _chkVoiceTags.Checked;
            _settings.Save();
            UpdateEnabledState();
        };

        Flush(styleGrid.AddRow("Writing tone:", _cmbTone));
        Flush(styleGrid.AddRow(string.Empty, _chkVoiceTags));
        styleGrid.AddHint(
            "Marks every sentence of the recap AND the segments as excited, so the energy holds past the recap - "
            + "and opens the cold open and the sign-off on a shout. Needs an ElevenLabs v3 voice and the "
            + "Enthusiastic tone; it does nothing otherwise, and a club that picked Dry is left alone.");
        Flush(styleGrid.AddRow("Max length:", Line(_numMaxWords, Suffix("words", muted: false))));
        Flush(styleGrid.AddRow("Phrasing variety:", _numVariety));

        var style = new Section("Writing style");

        style.Add(Hint(
            "How the lines are worded - intros, recaps and talking points alike. This changes the words, not the "
            + "voice; how the recap sounds is set under Race recap below."));
        style.Add(styleGrid);
        style.Add(
            Hint("Length: up to 30 words gives one sentence, up to 70 gives two, beyond that a full paragraph - and it gets "
                 + "spoken aloud, so a 200-word recap takes a while to read out.  Variety: low = consistent, safe wording; "
                 + "high = more varied, occasionally an odd sentence."),
            gapBelow: 0);

        // ------------------------------------------------------------------
        // Race recap - after the flag, in the order it happens
        // ------------------------------------------------------------------

        _numSummaryDelay.Minimum = 0;
        _numSummaryDelay.Maximum = 120;
        _numSummaryDelay.Value = Math.Clamp(_settings.SummaryDelaySeconds, 0, 120);
        _numSummaryDelay.ValueChanged += (_, _) => { _settings.SummaryDelaySeconds = (int)_numSummaryDelay.Value; _settings.Save(); };

        Check(_chkTeaser, "Teasers before the recap:");
        _chkTeaser.Checked = _settings.SummaryTeaserEnabled;
        _chkTeaser.CheckedChanged += (_, _) =>
        {
            _settings.SummaryTeaserEnabled = _chkTeaser.Checked;
            _settings.Save();
            UpdateEnabledState();
        };

        // One line per teaser, picked at random each race - the same
        // sentence after every single race is what makes it sound canned.
        _txtTeaser.Multiline = true;
        _txtTeaser.ScrollBars = ScrollBars.Vertical;
        _txtTeaser.WordWrap = false;
        _txtTeaser.Text = string.Join(Environment.NewLine, _settings.EffectiveTeaserLines);
        _txtTeaser.BackColor = AppTheme.Surface;
        _txtTeaser.ForeColor = AppTheme.TextDark;
        _txtTeaser.BorderStyle = BorderStyle.FixedSingle;
        _txtTeaser.TextChanged += (_, _) =>
        {
            _settings.SummaryTeaserLines = _txtTeaser.Text
                .Split('\n')
                .Select(line => line.Trim('\r', ' '))
                .Where(line => line.Length > 0)
                .ToList();

            // Kept in step so anything still reading the single-line setting
            // gets a real teaser rather than yesterday's.
            _settings.SummaryTeaserText = _settings.SummaryTeaserLines.FirstOrDefault() ?? string.Empty;
            _settings.Save();
        };

        _numTeaserGap.Minimum = 0;
        _numTeaserGap.Maximum = 60;
        _numTeaserGap.Value = Math.Clamp(_settings.SummaryTeaserGapSeconds, 0, 60);
        _numTeaserGap.ValueChanged += (_, _) => { _settings.SummaryTeaserGapSeconds = (int)_numTeaserGap.Value; _settings.Save(); };

        Check(_chkBroadcastVoice, "Recap voice delivery - give the recap its own stability and style");
        _chkBroadcastVoice.Checked = _settings.SummaryUseBroadcastVoice;
        _chkBroadcastVoice.CheckedChanged += (_, _) =>
        {
            _settings.SummaryUseBroadcastVoice = _chkBroadcastVoice.Checked;
            _settings.Save();
            UpdateEnabledState();
        };

        _numStability.DecimalPlaces = 2;
        _numStability.Increment = 0.05m;
        _numStability.Minimum = 0m;
        _numStability.Maximum = 1m;
        _numStability.Value = (decimal)Math.Clamp(_settings.SummaryVoiceStability, 0, 1);
        _numStability.ValueChanged += (_, _) => { _settings.SummaryVoiceStability = (double)_numStability.Value; _settings.Save(); };

        _numStyleEx.DecimalPlaces = 2;
        _numStyleEx.Increment = 0.05m;
        _numStyleEx.Minimum = 0m;
        _numStyleEx.Maximum = 1m;
        _numStyleEx.Value = (decimal)Math.Clamp(_settings.SummaryVoiceStyle, 0, 1);
        _numStyleEx.ValueChanged += (_, _) => { _settings.SummaryVoiceStyle = (double)_numStyleEx.Value; _settings.Save(); };

        Check(_chkDeepDuck, "Duck music and crowd further during the recap, down to");
        _chkDeepDuck.Checked = _settings.SummaryDeepDuck;
        _chkDeepDuck.CheckedChanged += (_, _) =>
        {
            _settings.SummaryDeepDuck = _chkDeepDuck.Checked;
            _settings.Save();
            UpdateEnabledState();
        };

        _numDuckPercent.Minimum = 2;
        _numDuckPercent.Maximum = 100;
        _numDuckPercent.Value = Math.Clamp(_settings.SummaryDuckPercent, 2, 100);
        _numDuckPercent.ValueChanged += (_, _) =>
        {
            _settings.SummaryDuckPercent = (int)_numDuckPercent.Value;
            _settings.Save();
        };

        var pauseGrid = new FieldGrid();

        Flush(pauseGrid.AddRow("Pause after the flag:", Line(_numSummaryDelay, Suffix("seconds - lets the finish call and the cheering land before the recap starts"))));

        // The teasers' box under the switch that turns them on, a step in,
        // and the wait after them under the box - "teasers before the recap:
        // [the lines], then wait: [n] seconds". The box used to sit beside
        // the switch, at an x worked out from the width of its caption.
        var teaserGrid = new FieldGrid();

        teaserGrid.AddField(_txtTeaser);
        teaserGrid.AddHint(
            "One line per teaser - one is picked at random each race, so it isn't the same sentence every time. "
            + "It's fixed text rather than something the AI writes, so it plays even when the local model is unreachable.");
        Stepped(teaserGrid.AddRow("then wait:", Line(_numTeaserGap, Suffix("seconds"))));

        // Stability and style on lines of their own, so neither is ever
        // placed from the width of the other's note - "Stability: 0.00 lower
        // = more emotional range" once ran straight into "Style:".
        var voiceGrid = new FieldGrid();

        Stepped(voiceGrid.AddRow("Stability:", Line(_numStability, Suffix("lower = more emotional range"))));
        Stepped(voiceGrid.AddRow("Style:", Line(_numStyleEx, Suffix("higher = more dramatic; past ~0.6 it can distort"))));

        var recap = new Section("Race recap");

        recap.Add(Hint(
            "flag  ->  pause  ->  teaser  ->  wait  ->  commercial break  ->  recap.   Everything here is timing and "
            + "sound; the recap's words come from Writing style above, and the commercial break is switched on under "
            + "Broadcast below. The wait and the break cover the same pause rather than following one another, so "
            + "turning the break on does not add to the time before the recap."));
        recap.Add(pauseGrid);
        recap.Add(_chkTeaser, stretch: false);
        recap.Add(teaserGrid);
        recap.Add(_chkBroadcastVoice, stretch: false, gapBelow: UiLayout.HintGap);
        recap.Add(Hint(
            "How the recap SOUNDS, not what it says. Overrides the stability and style on the Voice Settings tab for "
            + "the recap only, so routine lap calls don't become over-acted. Untick it to use your Voice Settings "
            + "values for everything - if you've tuned them there and heard no change in the recap, this was why."));
        recap.Add(voiceGrid);

        // The check box's caption ends the sentence the spinner completes
        // ("...down to  [ 25 ] %"), so the three share a line - the spinner
        // going under the caption when there is no room beside it.
        recap.Add(Line(_chkDeepDuck, _numDuckPercent, Suffix("%", muted: false)), gapBelow: UiLayout.HintGap);
        recap.Add(
            Hint("The recap runs for half a minute, so the usual announcement duck leaves the music and crowd competing with it."),
            gapBelow: 0);

        // ------------------------------------------------------------------
        // Broadcast - the recap dressed up as TV (27 Sep 2026)
        // ------------------------------------------------------------------

        Check(_chkOpening, "Open the recap like a TV broadcast coming back on air");
        _chkOpening.Checked = _settings.RecapBroadcastOpening;
        _chkOpening.CheckedChanged += (_, _) =>
        {
            _settings.RecapBroadcastOpening = _chkOpening.Checked;
            _settings.Save();
            UpdateEnabledState();
        };

        Check(_chkClosing, "End with a TV-style sign-off that tees up what's next on the program");
        _chkClosing.Checked = _settings.RecapBroadcastClosing;
        _chkClosing.CheckedChanged += (_, _) =>
        {
            _settings.RecapBroadcastClosing = _chkClosing.Checked;
            _settings.Save();
            UpdateEnabledState();
        };

        _txtShowName.MaxLength = 60;
        _txtShowName.Text = _settings.RecapShowName ?? string.Empty;
        _txtShowName.TextChanged += (_, _) =>
        {
            _settings.RecapShowName = _txtShowName.Text.Trim();
            _settings.Save();
        };

        _txtAnnouncerName.MaxLength = 60;
        _txtAnnouncerName.Text = _settings.RecapAnnouncerName ?? string.Empty;
        _txtAnnouncerName.TextChanged += (_, _) =>
        {
            _settings.RecapAnnouncerName = _txtAnnouncerName.Text.Trim();
            _settings.Save();
        };

        Check(_chkInterview, "After a main, a quick interview with the winner's mechanic");
        _chkInterview.Checked = _settings.RecapMechanicInterview;
        _chkInterview.CheckedChanged += (_, _) =>
        {
            _settings.RecapMechanicInterview = _chkInterview.Checked;
            _settings.Save();
            UpdateEnabledState();
        };

        _txtMechanicName.MaxLength = 40;
        _txtMechanicName.Text = _settings.RecapMechanicName ?? string.Empty;
        _txtMechanicName.TextChanged += (_, _) =>
        {
            _settings.RecapMechanicName = _txtMechanicName.Text.Trim();
            _settings.Save();
        };

        _cmbMechanicVoice.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbMechanicVoice.SelectedIndexChanged += (_, _) => SaveMechanicVoice();

        _btnLoadVoices.Text = LoadVoicesCaption;
        _btnLoadVoices.Click += async (_, _) => await LoadMechanicVoicesAsync();

        _btnTestMechanic.Text = "Test Voice";
        _btnTestMechanic.Click += (_, _) => SpeakAsMechanic?.Invoke(_settings.MechanicVoice);

        // Which engine's voices the list holds - the one chosen on Voice
        // Settings. Last on the line, so its text can change without moving
        // anything.
        _lblVoiceSource.AutoSize = true;
        _lblVoiceSource.ForeColor = AppTheme.TextMuted;
        _textSize.Wear(_lblVoiceSource, () => AppTheme.SmallFont);

        _cmbRecapHostVoice.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbRecapHostVoice.SelectedIndexChanged += (_, _) => SaveRecapHostVoice();

        _btnTestRecapHost.Text = "Test Voice";
        _btnTestRecapHost.Click += (_, _) => SpeakAsRecapHost?.Invoke(_settings.RecapHostVoice);

        // The recap host's own name - see AppSettings.RecapHostName.
        _txtRecapHostName.MaxLength = 60;
        _txtRecapHostName.Text = _settings.RecapHostName ?? string.Empty;
        _txtRecapHostName.TextChanged += (_, _) =>
        {
            _settings.RecapHostName = _txtRecapHostName.Text.Trim();
            _settings.Save();
        };

        var showGrid = new FieldGrid();

        Stepped(showGrid.AddRow("Show name:", Line(_txtShowName, Suffix("named in the opening and the sign-off - leave it blank for none"))));

        Stepped(showGrid.AddRow("Announcer's name:", Line(_txtAnnouncerName, Suffix("the race announcer, not the recap host - he says it a line into each On Air opening, going into the stop set and at the cold open; blank and he is never named"))));

        Stepped(showGrid.AddRow("Recap host's voice:", Line(_cmbRecapHostVoice, _btnTestRecapHost)));
        showGrid.AddHint(
            "The race recap show has a host of its own: this voice reads the recap, the colour, the questions to the "
            + "mechanic and the sign-off, and is heard nowhere else - the announcer keeps the race calls, the teaser and "
            + "the openings. Filled from the same engine as the mechanic's, beside Load Voices below, and kept per engine "
            + "the same way. With none picked, the announcer hosts the recap too.");

        Stepped(showGrid.AddRow("Recap host's name:", Line(_txtRecapHostName, Suffix("said when the recap opens and at the sign-off; blank and the host is never named"))));

        Check(_chkAdBreak, "Play the sponsors as a commercial break while the recap is being written");
        _chkAdBreak.Checked = _settings.RecapAdBreak;
        _chkAdBreak.CheckedChanged += (_, _) =>
        {
            _settings.RecapAdBreak = _chkAdBreak.Checked;
            _settings.Save();
            UpdateEnabledState();
        };

        Check(_chkBadgeHonours, "After a race, a third voice reads out the badges it unlocked");
        _chkBadgeHonours.Checked = _settings.RecapBadgeHonours;
        _chkBadgeHonours.CheckedChanged += (_, _) =>
        {
            _settings.RecapBadgeHonours = _chkBadgeHonours.Checked;
            _settings.Save();
            UpdateEnabledState();
        };

        _txtHonoursName.MaxLength = 40;
        _txtHonoursName.Text = _settings.RecapHonoursName ?? string.Empty;
        _txtHonoursName.TextChanged += (_, _) =>
        {
            _settings.RecapHonoursName = _txtHonoursName.Text.Trim();
            _settings.Save();
        };

        _cmbHonoursVoice.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbHonoursVoice.SelectedIndexChanged += (_, _) => SaveHonoursVoice();

        _btnTestHonours.Text = "Test Voice";
        _btnTestHonours.Click += (_, _) => SpeakAsHonours?.Invoke(_settings.HonoursVoice);

        var mechanicGrid = new FieldGrid();

        Stepped(mechanicGrid.AddRow("Mechanic's name:", Line(_txtMechanicName, Suffix("blank for \"the team's mechanic\" - the model never makes a name up"))));
        Stepped(mechanicGrid.AddRow("Mechanic's voice:", Line(_cmbMechanicVoice, Steady(_btnLoadVoices, LoadVoicesCaption), _btnTestMechanic, _lblVoiceSource)));
        mechanicGrid.AddHint(
            "The list is the voice engine chosen on Voice Settings. A voice is kept for each engine, so switching engine "
            + "there needs one picked here for the new engine too - with none picked, the announcer's own voice answers.");

        var honoursGrid = new FieldGrid();

        Stepped(honoursGrid.AddRow("Honours reader:", Line(_txtHonoursName, Suffix("blank and they are thrown to but never named - no name is made up"))));
        Stepped(honoursGrid.AddRow("Reader's voice:", Line(_cmbHonoursVoice, _btnTestHonours)));
        honoursGrid.AddHint(
            "Filled from the same engine as the mechanic's, beside Load Voices above, and kept per engine the same way - "
            + "with none picked, the announcer's own voice reads the honours too, which still hands them out and is better "
            + "than nothing.");

        var broadcast = new Section("Broadcast");

        // THE RUNNING ORDER HAS TO BE THE REAL ONE. This card used to say
        // "recap -> honours -> interview -> sign-off", which was wrong three
        // times over: it left out the TEASER, which is a spoken line with a
        // wait setting of its own; it left out the BADGE CALLS, which still
        // go out at the flag unless the honours box below is ticked; and it
        // never mentioned the SPONSORS at all - so a club reading the
        // Sponsors page would think that page owned the paid reads, when
        // they are dispatched from here and go quiet with the Commentary
        // switch at the top of this one. A running order on a settings page
        // is the only map a volunteer has of what their club is about to
        // hear, and a map that leaves three things out is worse than none.
        broadcast.Add(Hint(
            "finish call  ->  badge calls  ->  teaser  ->  commercial break  ->  recap  ->  honours  ->  interview (after "
            + "a main)  ->  sign-off.   That is the whole of what a club hears after a flag. The badge calls go out at the "
            + "flag as they always have, unless the honours box below is ticked - that MOVES them into the recap rather "
            + "than adding to them. The teaser and the waits either side of it are set under Race recap above. The model "
            + "writes every part from the race's facts, like the recap itself - except the honours, which are built from "
            + "the badges and need no model at all. The recap and intros say whether a race was a heat or a main."));
        broadcast.Add(_chkAdBreak, stretch: false, gapBelow: UiLayout.HintGap);
        broadcast.Add(Hint(
            "The club's sponsor slides play as a commercial break on the Big Screen, straight after the teaser and before "
            + "the recap - so the half-minute or more the model takes to write the recap is advert time instead of silence. "
            + "It runs until the recap is written and rendered, never less than 8 seconds and never more than 90, and it "
            + "ends the moment the next race is called to the grid. It needs a Big Screen open with the sponsor slides "
            + "switched on: without one, nothing changes and the recap goes out exactly as it does now."));
        broadcast.Add(Hint(
            "WHERE THE SPONSORS SIT. The slides, the rotation and who has bought a read are all on the Sponsors page. The "
            + "paid reads themselves are SPOKEN from here, over the end of a sponsor video and in the gaps between races, "
            + "so they need a race day open, the Commentary switch at the top of this page on, and the local model "
            + "reachable - even though nothing writes them and the words are the ones you typed."));
        broadcast.Add(_chkOpening, stretch: false);
        broadcast.Add(_chkClosing, stretch: false);
        broadcast.Add(showGrid);
        broadcast.Add(_chkInterview, stretch: false, gapBelow: UiLayout.HintGap);
        broadcast.Add(Hint(
            "The announcer asks, the mechanic answers, twice. The mechanic talks about the race and about the setup changes "
            + "logged for the winning car in the Garage on the day - and says nothing about the car's settings when none "
            + "are logged. Heats don't get one."));
        broadcast.Add(mechanicGrid);
        broadcast.Add(_chkBadgeHonours, stretch: false, gapBelow: UiLayout.HintGap);
        broadcast.Add(Hint(
            "The badges the race just unlocked, read out as a segment rather than called out one at a time at the flag: "
            + "the announcer throws to the reader, they go over who got what, and it comes back. This one needs no model "
            + "at all - the words are built from the badges themselves - so it is there on an evening the AI is switched "
            + "off. Ticking it MOVES the badge calls into the recap instead of adding to them, so nobody hears a badge "
            + "twice; untick it and the individual calls at the flag come back, exactly as they are set up on Voice "
            + "Settings -> Announcements."));
        broadcast.Add(honoursGrid, gapBelow: 0);

        // ------------------------------------------------------------------
        // Which segments to run
        //
        // Built from CommentaryBudget.AllSpecs, grouped by tier, with the
        // tier's own heading saying when it goes quiet - because "why did
        // it stop doing the cold open" is otherwise unanswerable from
        // this page. Intros and recaps are not here on purpose: they have
        // their own boxes above, and a second switch for the same thing
        // in a different place is how a settings page starts lying.
        // ------------------------------------------------------------------

        // HOW CHATTY, at the top of the Segments card because it governs
        // every box below it: a club that turns this down hears fewer of
        // the things it has switched on, rather than different ones. Filled
        // from ShowChatter.All and the handler attached after the club's own
        // choice is selected, for the reason FillLibrary gives.
        _cmbChatter.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbChatter.Items.Clear();

        foreach (var level in ShowChatter.All)
        {
            _cmbChatter.Items.Add(level.Name);
        }

        var chatterNow = ShowChatter.For(_settings.CommentaryChatter);
        var chatterAt = 0;

        for (var i = 0; i < ShowChatter.All.Count; i++)
        {
            if (ShowChatter.All[i].Key == chatterNow.Key)
            {
                chatterAt = i;
                break;
            }
        }

        _cmbChatter.SelectedIndex = chatterAt;

        _cmbChatter.SelectedIndexChanged += (_, _) =>
        {
            var at = _cmbChatter.SelectedIndex;

            _settings.CommentaryChatter = at >= 0 && at < ShowChatter.All.Count
                ? ShowChatter.All[at].Key
                : string.Empty;

            _settings.Save();
            _lblChatter.Text = ShowChatter.Describe(_settings.CommentaryChatter);
        };

        _lblChatter.Text = ShowChatter.Describe(_settings.CommentaryChatter);

        // PACING, directly under chattiness because the two are read
        // together and a club looking for one will find the other. Built
        // the same way for the same reason - filled from the table, the
        // handler attached after the club's own choice is selected, and
        // the line beneath worked out from the real gaps rather than
        // typed.
        _cmbDensity.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbDensity.Items.Clear();

        foreach (var level in AnnouncerDensity.All)
        {
            _cmbDensity.Items.Add(level.Name);
        }

        var densityNow = AnnouncerDensity.For(_settings.AnnouncerDensity);
        var densityAt = 0;

        for (var i = 0; i < AnnouncerDensity.All.Count; i++)
        {
            if (AnnouncerDensity.All[i].Key == densityNow.Key)
            {
                densityAt = i;
                break;
            }
        }

        _cmbDensity.SelectedIndex = densityAt;

        _cmbDensity.SelectedIndexChanged += (_, _) =>
        {
            var at = _cmbDensity.SelectedIndex;

            _settings.AnnouncerDensity = at >= 0 && at < AnnouncerDensity.All.Count
                ? AnnouncerDensity.All[at].Key
                : string.Empty;

            _settings.Save();
            _lblDensity.Text = AnnouncerDensity.Describe(_settings.AnnouncerDensity);
        };

        _lblDensity.Text = AnnouncerDensity.Describe(_settings.AnnouncerDensity);

        var chatterGrid = new FieldGrid();

        Stepped(chatterGrid.AddRow("How chatty:", Line(_cmbChatter)));
        chatterGrid.AddHint(_lblChatter);

        Stepped(chatterGrid.AddRow("Pacing:", Line(_cmbDensity)));
        chatterGrid.AddHint(_lblDensity);

        chatterGrid.AddHint(Hint(
            "Race Control shows how many of the announcer's own timing calls got out while a race is on - "
            + "\"held\" is this dial leaving a gap on purpose, and \"lost\" is the announcer running out of "
            + "room for them. The full count goes to the console at the end of every race."));

        var segments = new Section("Segments");

        segments.Add(Hint(
            "What the announcer covers beyond the intros and recaps above. Each one only speaks when the race actually "
            + "gives it something to say, so switching them all on does not mean hearing them all every race."));

        segments.Add(Hint(
            "The night is paced to be quiet through practice and the heats and to spend what it held back on the "
            + "mains, which is what stops a room tuning the announcer out. How chatty sets the volume that shape sits "
            + "at - it never changes the shape, so the mains are always the loudest part of the evening."));
        segments.Add(chatterGrid, gapBelow: UiLayout.SectionGap);

        var switchedOff = new HashSet<SegmentKind>(CommentaryBudget.ParseSwitches(_settings.CommentarySegmentsOff));

        foreach (var tier in new[] { SegmentTier.Core, SegmentTier.Colour, SegmentTier.Flourish })
        {
            var inTier = CommentaryBudget.AllSpecs.Where(spec => spec.Tier == tier).ToList();

            if (inTier.Count == 0)
            {
                continue;
            }

            var heading = _textSize.Wear(
                new Label { Text = TierHeading(tier), AutoSize = true, ForeColor = AppTheme.TextDark },
                () => AppTheme.BoldFont);

            segments.Add(heading, stretch: false, gapBelow: 0);
            segments.Add(Hint(TierWhen(tier)), gapBelow: UiLayout.HintGap);

            foreach (var spec in inTier)
            {
                var box = new ThemedCheckBox();

                Check(box, spec.Title);
                box.Checked = !switchedOff.Contains(spec.Kind);

                var kind = spec.Kind;

                box.CheckedChanged += (_, _) =>
                {
                    SaveSegmentSwitches();
                    UpdateEnabledState();
                };

                _segmentBoxes[kind] = box;

                // The room under the last one in a group is what separates
                // the groups. Without it the three headings are just bold
                // rows in one long list, and "paused first" stops being
                // attached to anything.
                var last = ReferenceEquals(spec, inTier[^1]);

                segments.Add(box, stretch: false, gapBelow: 0);
                segments.Add(Hint(spec.What + (spec.CanBeExchange ? "  Can be a two-hander with the co-host." : string.Empty)),
                    gapBelow: last ? UiLayout.SectionGap : UiLayout.HintGap);
            }
        }

        // ------------------------------------------------------------------
        // The co-host
        // ------------------------------------------------------------------

        Check(_chkCoHost, "Two voices in the booth");
        _textSize.Wear(_chkCoHost, () => AppTheme.BoldFont);
        _chkCoHost.Checked = _settings.CommentaryCoHost;
        _chkCoHost.CheckedChanged += (_, _) =>
        {
            _settings.CommentaryCoHost = _chkCoHost.Checked;
            _settings.Save();
            UpdateEnabledState();
        };

        _txtCoHostName.MaxLength = 40;
        _txtCoHostName.Text = _settings.CommentaryCoHostName ?? string.Empty;
        _txtCoHostName.TextChanged += (_, _) =>
        {
            _settings.CommentaryCoHostName = _txtCoHostName.Text.Trim();
            _settings.Save();
        };

        _cmbCoHostVoice.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbCoHostVoice.SelectedIndexChanged += (_, _) => SaveCoHostVoice();

        // THE STANCE. Filled from CoHostStance.All rather than written out
        // here, so adding one is a table entry; and the handler is attached
        // AFTER the list is filled and the club's own choice selected, for
        // the reason FillLibrary gives - filling a box raises
        // SelectedIndexChanged, and a club whose stance had gone would have
        // it quietly saved as the first one in the list.
        _cmbCoHostStance.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbCoHostStance.Items.Clear();

        foreach (var stance in CoHostStance.All)
        {
            _cmbCoHostStance.Items.Add(stance.Name);
        }

        var stanceNow = CoHostStance.For(_settings.CoHostStance);
        _cmbCoHostStance.SelectedIndex = Math.Max(0, CoHostStance.All.ToList().FindIndex(one => one.Key == stanceNow.Key));

        _cmbCoHostStance.SelectedIndexChanged += (_, _) =>
        {
            var at = _cmbCoHostStance.SelectedIndex;

            _settings.CoHostStance = at >= 0 && at < CoHostStance.All.Count
                ? CoHostStance.All[at].Key
                : string.Empty;

            _settings.Save();
            _lblCoHostStance.Text = CoHostStance.Describe(_settings.CoHostStance);
        };

        _lblCoHostStance.Text = CoHostStance.Describe(_settings.CoHostStance);

        // Index 0 is two turns, so the index plus two IS the turn count.
        _cmbExchangeTurns.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbExchangeTurns.Items.Clear();
        _cmbExchangeTurns.Items.Add("Brief - a remark and a reply");
        _cmbExchangeTurns.Items.Add("Conversational - three turns");
        _cmbExchangeTurns.Items.Add("Full - four turns");
        _cmbExchangeTurns.SelectedIndex = Math.Clamp(_settings.CommentaryExchangeTurns, 2, CommentaryExchange.MostTurns) - 2;

        _cmbExchangeTurns.SelectedIndexChanged += (_, _) =>
        {
            _settings.CommentaryExchangeTurns = _cmbExchangeTurns.SelectedIndex + 2;
            _settings.Save();
        };

        Check(_chkTwoVoicesInHeats, "Two voices in the heats too, not just the mains");
        _chkTwoVoicesInHeats.Checked = _settings.CommentaryTwoVoicesInHeats;
        _chkTwoVoicesInHeats.CheckedChanged += (_, _) =>
        {
            _settings.CommentaryTwoVoicesInHeats = _chkTwoVoicesInHeats.Checked;
            _settings.Save();
            UpdateEnabledState();
        };

        var coHostGrid = new FieldGrid();

        Stepped(coHostGrid.AddRow("Co-host's name:", Line(_txtCoHostName, Suffix("blank and they are talked to but never named - the model never makes one up"))));
        Stepped(coHostGrid.AddRow("Co-host's voice:", Line(_cmbCoHostVoice)));
        Stepped(coHostGrid.AddRow("What they're like:", Line(_cmbCoHostStance)));
        coHostGrid.AddHint(_lblCoHostStance);

        Stepped(coHostGrid.AddRow("How much they talk:", Line(_cmbExchangeTurns)));
        coHostGrid.AddHint(
            "Longer conversations are more airtime and more writing - each turn is another line the brain writes, "
            + "and on the relay that is another line of the club's month.");

        Stepped(coHostGrid.AddRow(string.Empty, _chkTwoVoicesInHeats));
        coHostGrid.AddHint(
            "Normally the two-hander is saved for the mains, which is part of what makes a main sound like one. "
            + "Tick this to hear them all evening without making the whole night chattier.");
        coHostGrid.AddHint(
            "With no voice picked for the engine in use, the announcer's own voice reads both halves - still a conversation, "
            + "and better than nothing. The list is filled from the same engine as the mechanic's, beside Load Voices above.");

        // ------------------------------------------------------------------
        // Club notes (12 Oct 2026)
        // ------------------------------------------------------------------

        Check(_chkNotesWifi, "Take them from phones on the club's Wi-Fi");
        _chkNotesWifi.Checked = _settings.ClubNotesFromTheRoom;
        _chkNotesWifi.CheckedChanged += (_, _) =>
        {
            _settings.ClubNotesFromTheRoom = _chkNotesWifi.Checked;
            _settings.Save();
            DescribeNoteRoutes();
        };

        Check(_chkNotesRelay, "Take them from the club's own web page");
        _chkNotesRelay.Checked = _settings.ClubNotesFromTheRelay;
        _chkNotesRelay.CheckedChanged += (_, _) =>
        {
            _settings.ClubNotesFromTheRelay = _chkNotesRelay.Checked;
            _settings.Save();

            // THE RELAY IS TOLD WHEN IT IS SWITCHED OFF, not left to work
            // it out. The publisher stops polling the moment this is
            // unticked, so without this the relay would go on taking
            // strangers' messages for a club that had declined them - and
            // holding them. See RelayPublisher.CloseClubNotesOnTheRelay.
            if (!_chkNotesRelay.Checked)
            {
                NotesClosedOnTheRelay?.Invoke();
            }

            DescribeNoteRoutes();
        };

        DescribeNoteRoutes();

        var notes = new Section("Club notes");

        notes.Add(Hint(
            "The things about a night the timing cannot know - a dedication, somebody's birthday, the raffle closing "
            + "at eight. Type them on the Notes button on Race Control's announcer strip, and the announcer works one "
            + "into a sentence of its own between races, says it once, and adds nothing to it."));
        notes.Add(Hint(
            "The room can send them in too, if you let it. Nothing anybody sends is ever said until you have read it "
            + "and approved it on that same screen, so the worst a stranger can do is give you something to delete."));
        notes.Add(_chkNotesWifi, stretch: false, gapBelow: 0);
        notes.Add(_chkNotesRelay, stretch: false, gapBelow: 0);
        notes.Add(_lblNotesWhere);

        // ------------------------------------------------------------------
        // Listening along (13 Oct 2026)
        // ------------------------------------------------------------------

        Check(_chkListenAlong, "Let people listen along on the club's web page");
        _chkListenAlong.Checked = _settings.ListenAlongOnTheRelay;
        _chkListenAlong.CheckedChanged += (_, _) =>
        {
            _settings.ListenAlongOnTheRelay = _chkListenAlong.Checked;
            _settings.Save();

            // SENT when it goes off, not inferred from the announcer going
            // quiet. Without this the relay goes on offering the club's
            // own voice to anybody with the address. See
            // RelayPublisher.CloseListeningOnTheRelay.
            if (!_chkListenAlong.Checked)
            {
                ListeningClosedOnTheRelay?.Invoke();
            }

            DescribeListening();
        };

        DescribeListening();

        var listening = new Section("Listening along");

        listening.Add(Hint(
            "The race called, on your club's own web page, a few seconds behind the room. For a driver's family at "
            + "home, somebody away for the weekend, or the member who had to work. They press Listen and hear the "
            + "announcer; there is nothing for them to set up and nothing for you to do once it is on."));
        listening.Add(Hint(
            "Switch it off and the button goes from the page and the relay throws away what it was holding. Worth "
            + "knowing before you turn it on: this puts the voice you have chosen - including a voice cloned from "
            + "somebody at the club - on an address anyone with the link can play."));
        listening.Add(_chkListenAlong, stretch: false, gapBelow: 0);
        listening.Add(_lblListenAlong);

        // ------------------------------------------------------------------
        // The room's vote (12 Oct 2026)
        // ------------------------------------------------------------------

        Check(_chkVoteWifi, "Let phones on the club's Wi-Fi vote");
        _chkVoteWifi.Checked = _settings.RoomVoteFromTheRoom;
        _chkVoteWifi.CheckedChanged += (_, _) =>
        {
            _settings.RoomVoteFromTheRoom = _chkVoteWifi.Checked;
            _settings.Save();
            DescribeVoteRoutes();
        };

        Check(_chkVoteRelay, "Let the club's own web page vote");
        _chkVoteRelay.Checked = _settings.RoomVoteFromTheRelay;
        _chkVoteRelay.CheckedChanged += (_, _) =>
        {
            _settings.RoomVoteFromTheRelay = _chkVoteRelay.Checked;
            _settings.Save();

            // THE RELAY IS TOLD WHEN IT IS SWITCHED OFF, exactly as the
            // notes route is: the publisher stops polling the moment this
            // is unticked, so without this the relay would go on taking
            // votes for a club that had declined them - and holding them.
            // See RelayPublisher.CloseRoomVoteOnTheRelay.
            if (!_chkVoteRelay.Checked)
            {
                VoteClosedOnTheRelay?.Invoke();
            }

            DescribeVoteRoutes();
        };

        DescribeVoteRoutes();

        var roomVote = new Section("The room's vote");

        roomVote.Add(Hint(
            "The room votes on who wins, and after the race the announcer says what it reckoned - including the one "
            + "line no timing system could produce, which is that nobody in the room picked the driver who won. It is "
            + "the only thing the announcer says that the people on the bank made themselves, and the only one that "
            + "can be wrong."));
        roomVote.Add(Hint(
            "Open and close it on the Votes button on Race Control's announcer strip, beside Notes - open it when the "
            + "race is called to the grid, and it closes itself when the result comes in. The announcer stays quiet "
            + "unless enough people voted to speak for the room, and says nothing at all if the room split evenly. "
            + "It never says who anybody picked."));
        roomVote.Add(_chkVoteWifi, stretch: false, gapBelow: 0);
        roomVote.Add(_chkVoteRelay, stretch: false, gapBelow: 0);
        roomVote.Add(_lblVoteWhere);

        var coHost = new Section("The co-host");

        coHost.Add(Hint(
            "A second commentator, for the segments that suit one. The announcer makes a remark and the co-host answers - "
            + "written as one request and split in two, so the answer actually answers the question. Segments marked above "
            + "as a two-hander use them; the rest stay single-voice, and anything that can't be split cleanly is simply "
            + "said in one voice instead. Give them a point of view and the two of them stop agreeing with each other, "
            + "which is what makes a pair in a booth sound like two people."));
        coHost.Add(_chkCoHost, stretch: false);
        coHost.Add(coHostGrid, gapBelow: 0);

        // ------------------------------------------------------------------
        // The show's music (8 Oct 2026)
        //
        // The club's own opener and bed. Last on the page because it is
        // staging on top of everything above it: what it writes, which
        // segments, who says them, how they sound - and then how the show
        // is dressed.
        // ------------------------------------------------------------------

        Check(_chkShowMusic, "Play an opener and a music bed under the show");
        _textSize.Wear(_chkShowMusic, () => AppTheme.BoldFont);
        _chkShowMusic.Checked = _settings.ShowThemeEnabled;
        _chkShowMusic.CheckedChanged += (_, _) =>
        {
            _settings.ShowThemeEnabled = _chkShowMusic.Checked;
            _settings.Save();
            ShowMusicChanged?.Invoke();
            UpdateEnabledState();
        };

        // THE SHIPPED PIECES, one drop-down per part. Filled from the
        // library rather than written down - adding an mp3 to
        // Assets\ShowMusic adds a choice with no code change. Each one
        // attaches its own handler after it is filled, so filling it does
        // not read as the club choosing something - see FillLibrary.
        FillLibrary(_cmbOpenerPiece, ShowPart.Opener);
        FillLibrary(_cmbBedPiece, ShowPart.Bed);
        FillLibrary(_cmbStingPiece, ShowPart.Sting);

        _btnOpenerPick.Text = "Choose...";
        _btnOpenerPick.Click += (_, _) => PickShowMusic(ShowPart.Opener);

        _btnOpenerClear.Text = "Remove";
        _btnOpenerClear.Click += (_, _) => ClearShowMusic(ShowPart.Opener);

        _btnBedPick.Text = "Choose...";
        _btnBedPick.Click += (_, _) => PickShowMusic(ShowPart.Bed);

        _btnBedClear.Text = "Remove";
        _btnBedClear.Click += (_, _) => ClearShowMusic(ShowPart.Bed);

        _btnStingPick.Text = "Choose...";
        _btnStingPick.Click += (_, _) => PickShowMusic(ShowPart.Sting);

        _btnStingClear.Text = "Remove";
        _btnStingClear.Click += (_, _) => ClearShowMusic(ShowPart.Sting);

        foreach (var label in new[] { _lblOpenerFile, _lblBedFile, _lblStingFile, _lblShowResult, _lblCoHostStance, _lblChatter, _lblNotesWhere, _lblVoteWhere, _lblNightRecap, _lblDriverCut, _lblProof, _lblNightBefore, _lblOnDisk, _lblListenAlong })
        {
            label.AutoSize = true;
            label.ForeColor = AppTheme.TextMuted;
            _textSize.Wear(label, () => AppTheme.SmallFont);
        }

        // A floor of 10%: a show whose opener is at 2% reads as the feature
        // being broken rather than as a quiet opener.
        _numOpenerLevel.Minimum = 10;
        _numOpenerLevel.Maximum = 100;
        _numOpenerLevel.Value = Math.Clamp(_settings.ShowOpenerLevel, 10, 100);
        _numOpenerLevel.ValueChanged += (_, _) =>
        {
            _settings.ShowOpenerLevel = (int)_numOpenerLevel.Value;
            _settings.Save();
        };

        _numBedLevel.Minimum = 2;
        _numBedLevel.Maximum = 100;
        _numBedLevel.Value = Math.Clamp(_settings.ShowBedLevel, 2, 100);
        _numBedLevel.ValueChanged += (_, _) =>
        {
            _settings.ShowBedLevel = (int)_numBedLevel.Value;
            _settings.Save();
        };

        _numBedDuck.Minimum = 5;
        _numBedDuck.Maximum = 100;
        _numBedDuck.Value = Math.Clamp(_settings.ShowBedDuckPercent, 5, 100);
        _numBedDuck.ValueChanged += (_, _) =>
        {
            _settings.ShowBedDuckPercent = (int)_numBedDuck.Value;
            _settings.Save();
        };

        _numVoiceEnters.Minimum = 0;
        _numVoiceEnters.Maximum = 15000;
        _numVoiceEnters.Increment = 100;
        _numVoiceEnters.Value = Math.Clamp(_settings.ShowVoiceEntersMs, 0, 15000);
        _numVoiceEnters.ValueChanged += (_, _) =>
        {
            _settings.ShowVoiceEntersMs = (int)_numVoiceEnters.Value;
            _settings.Save();
        };

        Check(_chkShowRejoin, "Shorten the opener on later shows");
        _chkShowRejoin.Checked = _settings.ShowRejoinEnabled;
        _chkShowRejoin.CheckedChanged += (_, _) =>
        {
            _settings.ShowRejoinEnabled = _chkShowRejoin.Checked;
            _settings.Save();
            RefreshShowMusic();
        };

        _btnTestShow.Text = TestShowCaption;
        _btnTestShow.Click += async (_, _) => await TestShowMusicAsync();

        var showMusicGrid = new FieldGrid();

        // The drop-down FIRST on each row, because it is what most clubs
        // will ever touch: the shipped piece is what they are already on,
        // and Choose... is for the club with a piece of their own.
        Stepped(showMusicGrid.AddRow("Opener:", Line(_cmbOpenerPiece, _btnOpenerPick, _btnOpenerClear, _lblOpenerFile)));
        Stepped(showMusicGrid.AddRow("Music bed:", Line(_cmbBedPiece, _btnBedPick, _btnBedClear, _lblBedFile)));
        Stepped(showMusicGrid.AddRow("Sting:", Line(_cmbStingPiece, _btnStingPick, _btnStingClear, _lblStingFile)));
        showMusicGrid.AddHint(
            "Your own files, copied into the club's data so a backup carries them to the next laptop. The bed is "
            + "looped for as long as the show runs and then finishes on its own fade, so anything from half a minute "
            + "up works - it does not have to be long. The sting is the short mark between one part of the show and "
            + "the next, a second or two at most; leave it out and the bed swells instead, which does the same job.");

        Stepped(showMusicGrid.AddRow("Opener level:", Line(_numOpenerLevel, Suffix("%", muted: false))));
        Stepped(showMusicGrid.AddRow("Bed level:", Line(_numBedLevel, Suffix("% - a bed sits well under the voice, so this is low on purpose"))));
        Stepped(showMusicGrid.AddRow("and drops to:", Line(_numBedDuck, Suffix("% of that while the announcer is actually speaking"))));
        Stepped(showMusicGrid.AddRow("Voice comes in:", Line(_numVoiceEnters, Suffix("ms into the opener - 0 finds it, where the opener stops hitting and starts ringing out"))));

        // The running order. In the field column with no caption of its
        // own, because it belongs to the four rows above it rather than
        // naming a field - FieldGrid.AddField.
        showMusicGrid.AddField(_chkShowRejoin);
        showMusicGrid.AddHint(
            "No broadcast plays its full theme every quarter of an hour. The first show of the night plays your "
            + "opener whole; the ones after it come in on its ring-out, which is the same music in a fraction of "
            + "the time, and the sign-off is played out on the bed. Untick this and the opener is played whole "
            + "every time - the rest of the running order still varies.");
        showMusicGrid.AddHint(_lblRunningOrder);

        Stepped(showMusicGrid.AddRow("Hear it:", Line(Steady(_btnTestShow, TestShowCaption), _lblShowResult)));

        // ------------------------------------------------------------------
        // Which segments get it
        //
        // The set pieces only - see ShowSegments.Candidates for why the
        // race intro and the recap are not on this list.
        // ------------------------------------------------------------------

        var musicOn = new HashSet<SegmentKind>(ShowSegments.Parse(_settings.ShowSegments));

        var show = new Section("The show's music");

        show.Add(Hint(
            "A piece of music in front of the voice and another underneath it, for the segments that open and close "
            + "the evening. The opener lands, the bed comes up under it, and the first words arrive over the opener's "
            + "tail rather than after it - so it sounds like a broadcast coming on air rather than a clip followed by "
            + "an announcement. When the talking stops the bed comes up for a beat and then plays its own ending. "
            + "No two shows in an evening are the same shape: see the running order below. The club's own playlist "
            + "goes quiet while it runs and comes back after."));
        show.Add(_chkShowMusic, stretch: false, gapBelow: UiLayout.HintGap);
        show.Add(showMusicGrid);

        var musicHeading = _textSize.Wear(
            new Label { Text = "Which segments get it", AutoSize = true, ForeColor = AppTheme.TextDark },
            () => AppTheme.BoldFont);

        show.Add(musicHeading, stretch: false, gapBelow: 0);

        // The two that sit beside a race ARE on this list, and have been
        // since each got its own handling - the intro's bed cut short when
        // the countdown starts, the recap's opener in the gap after the
        // teaser. This hint still said they were not, which was true of the
        // list as it first shipped and has been wrong since; see
        // ShowSegments.Candidates for what is actually offered and why.
        show.Add(Hint(
            "The set pieces, where filling time is the point. The cold open and the sign-off come ticked. Race "
            + "intros and recaps are here too: the recap's music lands in the gap after the teaser, and an intro's "
            + "bed is taken away the moment the countdown starts rather than playing over the horn. The honours are "
            + "a segment OF the recap, so on an ordinary night they play under the recap's own music - tick them "
            + "here and they also get a show of their own on a night with no recap, which is the night they are the "
            + "whole programme. Nothing that fires during a race is on the list - a battle call is a remark over a "
            + "race in progress, not a set piece."),
            gapBelow: UiLayout.HintGap);

        for (var i = 0; i < ShowSegments.Candidates.Count; i++)
        {
            var kind = ShowSegments.Candidates[i];
            var box = new ThemedCheckBox();

            Check(box, CommentaryBudget.Spec(kind).Title);
            box.Checked = musicOn.Contains(kind);
            box.CheckedChanged += (_, _) => SaveShowSegments();

            _showBoxes[kind] = box;

            // Nothing under the last one: the section's own room follows it.
            var last = i == ShowSegments.Candidates.Count - 1;

            show.Add(box, stretch: false, gapBelow: last ? 0 : UiLayout.RowGap);
        }

        // ------------------------------------------------------------------
        // Recording the night (11 Oct 2026)
        //
        // Last on the page, after everything that decides what the
        // broadcast IS: this is only about keeping a copy of it.
        // ------------------------------------------------------------------

        Check(_chkRecord, "Record each race, and cut each driver their own clip");
        _textSize.Wear(_chkRecord, () => AppTheme.BoldFont);
        _chkRecord.Checked = _audio.Enabled;
        _chkRecord.CheckedChanged += (_, _) =>
        {
            _audio.Enabled = _chkRecord.Checked;
            UpdateEnabledState();
        };

        Check(_chkClips, "A clip for each driver as well as the race's own recording");
        _chkClips.Checked = _audio.DriverHighlights;
        _chkClips.CheckedChanged += (_, _) => _audio.DriverHighlights = _chkClips.Checked;

        _txtAudioFolder.ReadOnly = true;
        _txtAudioFolder.Text = _audio.Folder;

        _btnAudioFolder.Text = "Choose...";
        _btnAudioFolder.Click += (_, _) => PickAudioFolder();

        _btnAudioOpen.Text = "Open folder";
        _btnAudioOpen.Click += (_, _) => OpenAudioFolder();

        _numKeepNights.Minimum = 1;
        _numKeepNights.Maximum = 104;
        _numKeepNights.Value = Math.Clamp(_audio.KeepNights, 1, 104);
        _numKeepNights.ValueChanged += (_, _) =>
        {
            _audio.KeepNights = (int)_numKeepNights.Value;
            ShowWhatIsOnDisk();
        };

        var audioGrid = new FieldGrid();

        Stepped(audioGrid.AddRow("Keep the last:", Line(_numKeepNights, Suffix("race nights - the oldest night's folder is removed after a race, so this is a ceiling rather than an archive"))));
        Stepped(audioGrid.AddRow("Folder:", Line(_txtAudioFolder, _btnAudioFolder, _btnAudioOpen)));
        audioGrid.AddHint(_lblOnDisk);

        var audio = new Section("Recording the night");

        audio.Add(Hint(
            "Every race is recorded as it goes out - the announcer and the effects, but not the club's music or the crowd bed, so a clip is the call, clean - "
            + "and each driver gets an mp3 of the stretches where the announcer called them. The clips are the half worth "
            + "having: a driver who posts the forty seconds where their pass for the lead was called is the best "
            + "advertisement your club has. They go in a folder per night, with a Highlights folder inside it."));
        audio.Add(_chkRecord, stretch: false);
        audio.Add(_chkClips, stretch: false, gapBelow: UiLayout.HintGap);
        audio.Add(Hint(
            "Recordings are deliberately NOT kept with the club's data, so a backup can never sweep them up - an evening "
            + "is a few hundred megabytes. Copy a night's folder somewhere else to keep it past the count below."));
        audio.Add(audioGrid, gapBelow: UiLayout.SectionGap);

        // THE NIGHT IN FOUR MINUTES, on this card because what a club
        // wants out of it is the file - it lands in the same night's
        // folder as the races and the bookends. See NightRecap.
        _btnNightRecap.Text = NightRecapCaption;
        _btnNightRecap.Click += async (_, _) => await PlayTonightsRecapAsync();

        var recapGrid = new FieldGrid();

        recapGrid.AddHint(
            "After the last race: the whole evening written as a short piece for the people who were not there, "
            + "played out once with your opener and bed round it, and kept in tonight's folder as \"The Night\". "
            + "It is the one thing this app makes that somebody outside the building would choose to listen to.");
        Stepped(recapGrid.AddRow("Tonight's recap:", Line(Steady(_btnNightRecap, NightRecapCaption), _lblNightRecap)));

        audio.Add(recapGrid, gapBelow: UiLayout.SectionGap);

        // ONE DRIVER'S NIGHT, on this card for the recap's reason: what a
        // club wants out of it is the file, and it lands in the same
        // night's folder. See DriverCut.
        _cmbDriverCut.DropDownStyle = ComboBoxStyle.DropDownList;

        // FILLED WHEN IT IS OPENED, so it is never a list from an hour
        // ago - a club runs races between pressing this and pressing it
        // again. Filled once below as well, so the box is not empty the
        // first time somebody looks at the card.
        _cmbDriverCut.DropDown += (_, _) => FillDriversTonight();

        _btnDriverCut.Text = DriverCutCaption;
        _btnDriverCut.Click += async (_, _) => await PlayADriversNightAsync();

        var cutGrid = new FieldGrid();

        cutGrid.AddHint(
            "One driver's own night, written for them: the races they ran, how their pace went and what happened "
            + "to them, in about ninety seconds. It plays out once and lands in tonight's folder under their "
            + "name, so a driver can be handed it. It is the first thing this app makes that a driver takes "
            + "home - and it is deliberately about the driving rather than about where they finished, because "
            + "most of a club does not win.");

        Stepped(cutGrid.AddRow(
            "A driver's night:",
            Line(_cmbDriverCut, Steady(_btnDriverCut, DriverCutCaption), _lblDriverCut)));

        audio.Add(cutGrid, gapBelow: UiLayout.SectionGap);

        // AND WHY IT SAID THAT. Every claim the announcer makes is worked
        // out from the club's own results, and until now none of that was
        // visible to the club that bought it. See NightAccount.Proof.
        _btnProof.Text = ProofCaption;
        _btnProof.Click += (_, _) => ShowTheProof();

        var proofGrid = new FieldGrid();

        proofGrid.AddHint(
            "Writes out, in the developer console, every fact the announcer's last segment was built from - and "
            + "nothing else. Each one is worked out from this club's own saved results, so anything it said can "
            + "be checked against them. The console is under Help.");

        Stepped(proofGrid.AddRow("Check a line:", Line(Steady(_btnProof, ProofCaption), _lblProof)));

        audio.Add(proofGrid, gapBelow: UiLayout.SectionGap);

        // THE NIGHT BEFORE. The one thing this app writes on a day it is
        // otherwise shut, and the only one whose job is getting people to
        // the track rather than entertaining the people already at it.
        // See NightBefore.
        _btnNightBefore.Text = NightBeforeCaption;
        _btnNightBefore.Click += async (_, _) => await WriteTheNightBeforeAsync();

        var beforeGrid = new FieldGrid();

        beforeGrid.AddHint(
            "Writes a short preview of the next race day from the programme and the entry list, and saves it as "
            + "a text file you can paste into the club's page or group. Everything in it is worded as a plan "
            + "rather than a result - who is entered rather than who is racing - because an entry list is what "
            + "the club means to happen and some of it will not. It says nothing about who will win and nothing "
            + "about any final's grid, because those do not exist yet.");

        Stepped(beforeGrid.AddRow("The night before:", Line(Steady(_btnNightBefore, NightBeforeCaption), _lblNightBefore)));

        // PUBLISHING IS A SEPARATE YES. The button has saved a file since
        // this shipped, and a club that has been pressing it all season
        // for something to paste into its own group must not find that an
        // update started putting the same words on a public address. See
        // AppSettings.PublishNightBeforeToTheRelay.
        Check(_chkNightBeforeRelay, "Also put it on the club's own web page");
        _chkNightBeforeRelay.Checked = _settings.PublishNightBeforeToTheRelay;
        _chkNightBeforeRelay.CheckedChanged += (_, _) =>
        {
            _settings.PublishNightBeforeToTheRelay = _chkNightBeforeRelay.Checked;
            _settings.Save();

            // TAKEN DOWN when it goes off, like the notes, the vote and
            // listening along. Otherwise a preview the club has just said
            // it does not want public stays on its page.
            if (!_chkNightBeforeRelay.Checked)
            {
                NightBeforeClosedOnTheRelay?.Invoke();
            }
        };

        beforeGrid.AddHint(
            "The file is written either way. Ticked, the preview also appears at the top of your club's page "
            + "until the meeting starts - and it comes down by itself a few hours after the first race even if "
            + "this PC is switched off, so one left behind cannot sit there describing last week in the future "
            + "tense.");

        Stepped(beforeGrid.AddRow(string.Empty, _chkNightBeforeRelay));

        audio.Add(beforeGrid, gapBelow: 0);

        FillDriversTonight();

        ShowWhatIsOnDisk();

        // ------------------------------------------------------------------
        // The page
        // ------------------------------------------------------------------

        var page = new PageStack();

        page.Add(lblIntro, gapBelow: UiLayout.HintGap);
        page.Add(lblRequirement, gapBelow: UiLayout.RowGap);
        page.Add(_chkEnabled, stretch: false);
        page.Add(_chkUseRelay, stretch: false, gapBelow: UiLayout.HintGap);
        page.Add(relayHint, gapBelow: UiLayout.HintGap);
        page.Add(_lblRelayState, gapBelow: UiLayout.RowGap);
        page.Add(connection);
        page.Add(writes);
        page.Add(segments);
        page.Add(coHost);
        page.Add(notes);
        page.Add(listening);
        page.Add(roomVote);
        page.Add(style);
        page.Add(recap);
        page.Add(broadcast);
        page.Add(show);
        page.Add(audio, gapBelow: 0);

        Controls.Add(page);

        // Once everything is on the page and wears its face - what these are
        // measured in.
        //
        // One caption column down every card, so the fields line up down the
        // whole page - see Stepped and Flush.
        // EVERY GRID ON THE PAGE, and five of them were missing (13 Oct
        // 2026). The 12 Oct bug hunt recorded chatterGrid and recapGrid;
        // cutGrid, proofGrid and beforeGrid were missing too, which is
        // five cards whose caption column was its own width while the
        // other fourteen shared one. It reads as the page having been
        // assembled rather than laid out, and it is silent - a grid left
        // out of this still looks perfectly correct on its own.
        //
        // A list that has to be added to is the shape of this bug, and the
        // layout audit is what would catch the next one.
        _textSize.Keep(() => FieldGrid.LineUp(new[]
        {
            connectionGrid, introsGrid, summariesGrid, talkingGrid, styleGrid,
            pauseGrid, teaserGrid, voiceGrid, showGrid, mechanicGrid, honoursGrid, coHostGrid,
            showMusicGrid, audioGrid, chatterGrid, recapGrid, cutGrid, proofGrid, beforeGrid,
        }));

        _textSize.Keep(SizeFields);

        // The test's result level with the button's caption while it is one
        // line, as it always sat.
        _textSize.Keep(() =>
        {
            var top = Math.Max(0, (UiLayout.ButtonSizeFor(TestCaption).Height - _lblTestResult.Font.Height) / 2);

            if (_lblTestResult.Margin.Top != top)
            {
                _lblTestResult.Margin = new Padding(0, top, 0, 0);
            }
        });

        RefreshShowMusic();

        FillMechanicVoices(null);

        // Voice Settings can change the engine while this page is hidden,
        // so the list is set out again for it whenever the page is shown.
        VisibleChanged += (_, _) =>
        {
            if (!Visible)
            {
                return;
            }

            if (EngineInUse?.Invoke() is { } engine && engine != _voiceListEngine)
            {
                FillMechanicVoices(engine);
            }

            // The Club Website page can be filled in after this one, and
            // what the relay line says depends on it.
            UpdateRelayState();

            // So does whether listening along is actually going out: the
            // club website is switched on or off on that page, and this
            // line must not go on saying nothing is sent once it is.
            DescribeListening();
        };

        UpdateEnabledState();
    }

    /// <summary>
    /// The fields' sizes at the text size in use. The boxes are the widths
    /// they were at the normal text size, at this size - capped, not
    /// stretched: an endpoint or a name is a few words - and the teasers'
    /// box four lines of its font tall. The pickers are as wide as their
    /// longest choice, and never narrower than they were then (a flat 220
    /// once cut "Enthusiastic - trackside hype" short). Each number box is
    /// as wide as its largest value and its arrows need (NumberWidth).
    ///
    /// Run again when the text size changes (TextSize.Keep) and when the
    /// mechanic's list is filled (FillMechanicVoices).
    /// </summary>
    private void SizeFields()
    {
        Fit(_txtShowName, UiLayout.AtTextSize(ShowNameWidth));
        Fit(_txtAnnouncerName, UiLayout.AtTextSize(ShowNameWidth));
        Fit(_txtRecapHostName, UiLayout.AtTextSize(ShowNameWidth));
        Fit(_cmbOpenerPiece, UiLayout.AtTextSize(ShowPieceWidth));
        Fit(_cmbBedPiece, UiLayout.AtTextSize(ShowPieceWidth));
        Fit(_cmbStingPiece, UiLayout.AtTextSize(ShowPieceWidth));
        Fit(_cmbDriverCut, UiLayout.AtTextSize(DriverPickerWidth));
        Fit(_txtMechanicName, UiLayout.AtTextSize(MechanicNameWidth));
        Fit(_txtCoHostName, UiLayout.AtTextSize(MechanicNameWidth));
        Fit(_txtHonoursName, UiLayout.AtTextSize(MechanicNameWidth));
        Fit(_txtTeaser, UiLayout.AtTextSize(TeaserWidth), Pieces.LineHeight(_txtTeaser.Font) * TeaserLines + UiLayout.HintGap * 2);

        Fit(_cmbTone, Math.Max(UiLayout.AtTextSize(TonePickerWidth), Pieces.PickerWidth(_cmbTone, ref _toneWidth)));
        Fit(_cmbMechanicVoice, Math.Max(UiLayout.AtTextSize(VoicePickerWidth), Pieces.PickerWidth(_cmbMechanicVoice, ref _voiceWidth)));
        Fit(_cmbCoHostVoice, Math.Max(UiLayout.AtTextSize(VoicePickerWidth), Pieces.PickerWidth(_cmbCoHostVoice, ref _coHostVoiceWidth)));
        Fit(_cmbCoHostStance, Math.Max(UiLayout.AtTextSize(VoicePickerWidth), Pieces.PickerWidth(_cmbCoHostStance, ref _coHostStanceWidth)));
        // Sized to its longest choice like the stance picker above - left at
        // the default width, "Conversational - three turns" was cut off.
        Fit(_cmbExchangeTurns, Math.Max(UiLayout.AtTextSize(VoicePickerWidth), Pieces.PickerWidth(_cmbExchangeTurns, ref _exchangeTurnsWidth)));
        Fit(_cmbChatter, Math.Max(UiLayout.AtTextSize(ShowPieceWidth), Pieces.PickerWidth(_cmbChatter, ref _chatterWidth)));
        // The pacing dial beside it, sized the same way. Left out when the
        // dial was added (14 Oct 2026), so it kept the default width and
        // "Room to breathe" was cut off at every size the layout test checks.
        Fit(_cmbDensity, Math.Max(UiLayout.AtTextSize(ShowPieceWidth), Pieces.PickerWidth(_cmbDensity, ref _densityWidth)));
        Fit(_cmbHonoursVoice, Math.Max(UiLayout.AtTextSize(VoicePickerWidth), Pieces.PickerWidth(_cmbHonoursVoice, ref _honoursVoiceWidth)));
        Fit(_cmbRecapHostVoice, Math.Max(UiLayout.AtTextSize(VoicePickerWidth), Pieces.PickerWidth(_cmbRecapHostVoice, ref _recapHostVoiceWidth)));

        foreach (var number in new[]
                 {
                     _numTimeout, _numFinishers, _numInterval, _numObservations, _numMaxWords, _numVariety,
                     _numSummaryDelay, _numTeaserGap, _numStability, _numStyleEx, _numDuckPercent,
                     _numOpenerLevel, _numBedLevel, _numBedDuck, _numVoiceEnters, _numKeepNights,
                 })
        {
            Fit(number, NumberWidth(number));
        }

        // Only when it changed: a new size lays the grid or the line out
        // again. The height only for the box of several lines - the others'
        // heights are their own (Pieces.KeepHeightTold).
        static void Fit(Control field, int width, int? height = null)
        {
            if (height is { } tall)
            {
                var size = new Size(width, tall);

                if (field.Size != size)
                {
                    field.Size = size;
                }
            }
            else if (field.Width != width)
            {
                field.Width = width;
            }
        }
    }

    /// <summary>
    /// A number box as wide as its largest value - every digit a nought, with
    /// its decimals - and its arrows need in its own font, and never narrower
    /// than a number box was at the normal text size, at this size. They
    /// were 50 to 60px whatever the text size.
    /// </summary>
    private static int NumberWidth(NumericUpDown number)
    {
        var digits = decimal.Truncate(number.Maximum).ToString(System.Globalization.CultureInfo.InvariantCulture).Length;

        var widest = new string('0', digits)
            + (number.DecimalPlaces > 0 ? "." + new string('0', number.DecimalPlaces) : string.Empty);

        return Math.Max(
            UiLayout.AtTextSize(UiLayout.NumericWidth),
            TextRenderer.MeasureText(widest, number.Font).Width + SystemInformation.VerticalScrollBarWidth + 12 + UpDownButtonsSkin.Extra);
    }

    // ==================== The mechanic's voice ====================

    private static string EngineLabel(VoiceEngine engine) => engine switch
    {
        VoiceEngine.HeatCaller => "HeatCaller",
        VoiceEngine.Kokoro => "Kokoro",
        _ => "ElevenLabs",
    };

    /// <summary>The voice saved for the mechanic on this engine, as a list entry - the announcer's own when none is.</summary>
    private VoiceChoice SavedMechanicVoice(VoiceEngine engine) => SavedSecondVoice(_settings.MechanicVoice, engine);

    /// <summary>The same reading, for the co-host - held exactly as the mechanic's is, one voice per engine.</summary>
    private VoiceChoice SavedCoHostVoice(VoiceEngine engine) => SavedSecondVoice(_settings.CoHostVoice, engine);

    /// <summary>And for the honours reader, the third voice. Same shape again - see SavedSecondVoice.</summary>
    private VoiceChoice SavedHonoursVoice(VoiceEngine engine) => SavedSecondVoice(_settings.HonoursVoice, engine);

    /// <summary>And for the race recap host. Same shape again - see SavedSecondVoice.</summary>
    private VoiceChoice SavedRecapHostVoice(VoiceEngine engine) => SavedSecondVoice(_settings.RecapHostVoice, engine);

    /// <summary>
    /// Which voice a second speaker has saved for an engine, as the
    /// picker shows it.
    ///
    /// Shared between the mechanic and the co-host rather than written
    /// twice: they are the same settings in a different prefix, and two
    /// copies would drift the first time an engine was added or retired.
    /// </summary>
    private static VoiceChoice SavedSecondVoice(SecondVoice voice, VoiceEngine engine)
    {
        static string Shown(string name, string id) => string.IsNullOrWhiteSpace(name) ? id : name;

        return engine switch
        {
            VoiceEngine.Kokoro => string.IsNullOrWhiteSpace(voice.KokoroVoiceName)
                ? AnnouncersVoice
                : new VoiceChoice(voice.KokoroVoiceName, voice.KokoroVoiceName, string.Empty, KokoroSpeech.Describe(voice.KokoroVoiceName)),

            VoiceEngine.HeatCaller => string.IsNullOrWhiteSpace(voice.HeatCallerVoice)
                ? AnnouncersVoice
                : new VoiceChoice(voice.HeatCallerVoice, voice.HeatCallerVoiceName, voice.HeatCallerRev, Shown(voice.HeatCallerVoiceName, voice.HeatCallerVoice)),

            _ => string.IsNullOrWhiteSpace(voice.ElevenLabsVoiceId)
                ? AnnouncersVoice
                : new VoiceChoice(voice.ElevenLabsVoiceId, voice.ElevenLabsVoiceName, string.Empty, Shown(voice.ElevenLabsVoiceName, voice.ElevenLabsVoiceId)),
        };
    }

    /// <summary>
    /// Sets the mechanic's list out for an engine: the announcer's own
    /// voice, the voice saved for that engine, and - once loaded - every
    /// voice it offers. The saved one stays on the list even when the engine
    /// no longer offers it: it is what is set, and the page shouldn't show
    /// otherwise.
    ///
    /// And the co-host's list, and the honours reader's, in the SAME pass -
    /// they are the same voices off the same engine, so one Load Voices
    /// fills all three and there is no way for two of the lists to be
    /// showing different engines. The name is the mechanic's because the
    /// mechanic came first and the Load Voices button sits on their row.
    /// </summary>
    /// <param name="engine">The engine to show; null for the one in use.</param>
    /// <param name="loaded">The engine's voices, or null before they have been loaded.</param>
    private void FillMechanicVoices(VoiceEngine? engine, IReadOnlyList<VoiceChoice>? loaded = null)
    {
        var shown = engine ?? EngineInUse?.Invoke() ?? VoiceEngine.ElevenLabs;
        _voiceListEngine = shown;

        var saved = SavedMechanicVoice(shown);
        var choices = (loaded ?? Array.Empty<VoiceChoice>()).ToList();

        if (saved.Id.Length > 0 && !choices.Any(c => string.Equals(c.Id, saved.Id, StringComparison.Ordinal)))
        {
            choices.Insert(0, saved);
        }

        _fillingVoices = true;

        try
        {
            _cmbMechanicVoice.Items.Clear();
            _cmbMechanicVoice.Items.Add(AnnouncersVoice);

            foreach (var choice in choices)
            {
                _cmbMechanicVoice.Items.Add(choice);
            }

            var index = saved.Id.Length == 0 ? 0 : choices.FindIndex(c => string.Equals(c.Id, saved.Id, StringComparison.Ordinal)) + 1;
            _cmbMechanicVoice.SelectedIndex = Math.Max(0, index);

            // The other two speakers pick from the same engine's voices
            // and are filled here, in the same pass - see the remarks.
            //
            // Through ONE local function rather than a block each. The
            // co-host's was a copy of the mechanic's, and a third copy for
            // the honours reader is how three lists start disagreeing about
            // which entry means the announcer's own voice and where the
            // saved-but-no-longer-offered one goes.
            void FillSecondVoice(ComboBox picker, VoiceChoice savedForThem)
            {
                var theirs = choices.ToList();

                if (savedForThem.Id.Length > 0 && !theirs.Any(c => string.Equals(c.Id, savedForThem.Id, StringComparison.Ordinal)))
                {
                    theirs.Insert(0, savedForThem);
                }

                picker.Items.Clear();
                picker.Items.Add(AnnouncersVoice);

                foreach (var choice in theirs)
                {
                    picker.Items.Add(choice);
                }

                var at = savedForThem.Id.Length == 0
                    ? 0
                    : theirs.FindIndex(c => string.Equals(c.Id, savedForThem.Id, StringComparison.Ordinal)) + 1;

                picker.SelectedIndex = Math.Max(0, at);
            }

            FillSecondVoice(_cmbCoHostVoice, SavedCoHostVoice(shown));
            FillSecondVoice(_cmbHonoursVoice, SavedHonoursVoice(shown));
            FillSecondVoice(_cmbRecapHostVoice, SavedRecapHostVoice(shown));
        }
        finally
        {
            _fillingVoices = false;
        }

        _lblVoiceSource.Text = loaded is null
            ? $"{EngineLabel(shown)} voices"
            : Plural.Of(loaded.Count, $"{EngineLabel(shown)} voice");

        // The longest voice may have come or gone: the pickers measured again.
        _voiceWidth = null;
        _coHostVoiceWidth = null;
        _coHostStanceWidth = null;
        _chatterWidth = null;
        _densityWidth = null;
        _honoursVoiceWidth = null;
        _recapHostVoiceWidth = null;
        SizeFields();
    }

    /// <summary>
    /// What a tier is called on the settings page.
    ///
    /// The tier names in the code - Core, Colour, Flourish - are about
    /// what goes quiet first. On the page they have to say what the
    /// segments in them ARE, because nobody setting up a race night cares
    /// about the ladder until something stops happening.
    /// </summary>
    private static string TierHeading(SegmentTier tier) => tier switch
    {
        SegmentTier.Core => "During the race",
        SegmentTier.Colour => "Around the race",
        _ => "The show",
    };

    /// <summary>And when it goes quiet, which is the part that is otherwise unanswerable from this page.</summary>
    private static string TierWhen(SegmentTier tier) => tier switch
    {
        SegmentTier.Core => "The live ones. The last to be paused if the month's writing runs short.",
        SegmentTier.Colour => "Depth between the races. Paused before the live ones are.",
        _ => "The top and tail of the evening. The first to be paused.",
    };

    /// <summary>
    /// Writes the switched-off segments to settings.
    ///
    /// The OFF list, so a segment added in a later version arrives
    /// switched on rather than needing somebody to come and find its box.
    /// See CommentaryBudget.SaveSwitches.
    /// </summary>
    private void SaveSegmentSwitches()
    {
        _settings.CommentarySegmentsOff = CommentaryBudget.SaveSwitches(
            _segmentBoxes.Where(pair => !pair.Value.Checked).Select(pair => pair.Key));

        _settings.Save();
    }

    /// <summary>Saves the co-host's picked voice against the engine the list is showing.</summary>
    /// <summary>
    /// Which segments get the music, saved by name. An empty choice is
    /// saved as "none" rather than blank - see ShowSegments.Save.
    /// </summary>
    private void SaveShowSegments()
    {
        _settings.ShowSegments = ShowSegments.Save(
            _showBoxes.Where(pair => pair.Value.Checked).Select(pair => pair.Key));

        _settings.Save();
    }

    /// <summary>
    /// Takes a file as the opener or the bed. The file is checked before
    /// it is copied, so a piece that is no use as a loop is turned away
    /// here rather than on a race night - see ShowMusic.Install.
    /// </summary>
    private void PickShowMusic(ShowPart part)
    {
        var what = WhatItIs(part);

        using var dialog = new OpenFileDialog
        {
            Title = $"Choose the {what}",
            Filter = $"Audio ({string.Join(";", ShowMusic.Extensions.Select(e => "*" + e))})"
                + $"|{string.Join(";", ShowMusic.Extensions.Select(e => "*" + e))}|All files (*.*)|*.*",

            // The folder the files end up in, so a club swapping one out
            // can see what is already there. Blank - not null - for the
            // usual Windows choice when it isn't there yet.
            InitialDirectory = Directory.Exists(ShowMusic.Folder) ? ShowMusic.Folder : string.Empty,
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        if (!_showMusic.Install(dialog.FileName, part, out var problem))
        {
            AppDialog.Show(
                this,
                problem ?? $"That file couldn't be used as the {what}.",
                $"Couldn't use that {what}",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return;
        }

        _lblShowResult.Text = string.Empty;

        ShowMusicChanged?.Invoke();
        RefreshShowMusic();
    }

    /// <summary>Takes one of the three pieces away, so the show runs without it.</summary>
    private void ClearShowMusic(ShowPart part)
    {
        var what = WhatItIs(part);

        // The sting is the one whose absence is not a loss - a swell of the
        // bed marks the breaks instead - so it is not offered the same
        // warning as the two pieces that really do go quiet.
        var warning = part == ShowPart.Sting
            ? $"Remove the {what}? The breaks in the show will be marked by the bed swelling instead."
            : $"Remove the {what}? The show will be read without it until you choose another.";

        if (AppDialog.Show(
                this,
                warning,
                $"Remove the {what}",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        _showMusic.Remove(part);
        _lblShowResult.Text = string.Empty;

        ShowMusicChanged?.Invoke();
        RefreshShowMusic();
    }

    /// <summary>What a piece is called in front of a club - the pickers, the warnings and the titles all use this one.</summary>
    private static string WhatItIs(ShowPart part) => part switch
    {
        ShowPart.Bed => "music bed",
        ShowPart.Sting => "sting",
        _ => "opener",
    };

    /// <summary>
    /// What the three files are, and what the measurement made of them -
    /// how much of the bed loops and whether it has an ending of its own.
    /// Measured rather than set, so this is the only way a club can see
    /// what their file is going to do.
    /// </summary>
    /// <summary>
    /// What a part's drop-down says when the club is on nothing.
    ///
    /// Not blank, and not "Default": blank reads as a list that failed to
    /// load, and "Default" is wrong because none IS a choice here - a club
    /// who wants no opener picks this on purpose, and the page has to be
    /// able to show them that is what they have.
    /// </summary>
    private const string NoPiece = "None";

    /// <summary>
    /// Fills one part's drop-down from the shipped library and selects
    /// whatever the club is on.
    ///
    /// THE HANDLER GOES ON LAST, after the items and the selection. A
    /// SelectedIndexChanged attached first fires while this is filling -
    /// once as the first item lands and again as the selection is set -
    /// and each of those would be written to the settings and counted as
    /// the club having chosen, which is exactly what
    /// AppSettings.ShowMusicStarted must not be told by accident.
    ///
    /// An empty library leaves the row with None in it and nothing else,
    /// which is the honest state of a damaged install - the club's own
    /// Choose... still works.
    /// </summary>
    /// <summary>
    /// Where the recordings go. A folder the club picks, or the default
    /// under Documents.
    ///
    /// The box is READ-ONLY and this is the only way to change it, which is
    /// deliberate: a typed path can be a network share, and the store's own
    /// note explains at length why merely asking Windows whether one exists
    /// hands this account's credentials to whoever runs that server. The
    /// store refuses one anyway; this means a club never gets as far as
    /// typing it.
    /// </summary>
    private void PickAudioFolder()
    {
        try
        {
            using var dialog = new FolderBrowserDialog
            {
                SelectedPath = _audio.Folder,
                Description = "Choose a folder for the race recordings",
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                return;
            }

            _audio.FolderSetting = dialog.SelectedPath;
            _txtAudioFolder.Text = _audio.Folder;

            ShowWhatIsOnDisk();
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, $"Couldn't change the recordings folder: {ex.Message}",
                "Folder not changed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// Opens the recordings folder, making it first if no race has been
    /// recorded yet - which is the usual state of it the first time
    /// somebody presses this, and an error about a missing folder would
    /// read as the feature being broken.
    /// </summary>
    private void OpenAudioFolder()
    {
        var folder = _audio.Folder;

        try
        {
            Directory.CreateDirectory(folder);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = folder,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, $"Couldn't open {folder}: {ex.Message}",
                "Folder not opened", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    /// <summary>
    /// What is in the recordings folder now.
    ///
    /// A club cannot decide how many nights to keep without knowing what a
    /// night costs, and this is the only place in the app that has ever
    /// said so. Asked when the page is built and when the count changes -
    /// never on a timer: it walks the folder, and a settings page that
    /// measured a few gigabytes twice a second would be the reason a club
    /// PC was busy.
    /// </summary>
    private void ShowWhatIsOnDisk()
    {
        var (nights, megabytes) = _audio.OnDisk();

        _lblOnDisk.Text = nights == 0
            ? "Nothing recorded yet. A folder appears here after the first race of a night."
            : $"On disk now: {Plural.Of(nights, "night")}, about {megabytes:n0} MB.";
    }

    /// <summary>
    /// Says where the room can send one from, and what the address is -
    /// which is the part a club actually needs, because an address nobody
    /// is given is a route nobody uses.
    /// </summary>
    /// <summary>
    /// What the box means right now, under the box.
    ///
    /// Says the TWO things a club gets wrong about this: that the club
    /// website has to be switched on as well (this rides on the relay, and
    /// a club can have the announcer without the relay), and that what
    /// goes out is whatever voice is chosen on Voice Settings.
    /// </summary>
    private void DescribeListening()
    {
        if (!_chkListenAlong.Checked)
        {
            _lblListenAlong.Text = "Nobody can listen in at the moment. The club's page shows the results as usual.";
            return;
        }

        _lblListenAlong.Text = _settings.RelayEnabled
            ? "A Listen button appears on your club's page while the announcer is speaking. It plays the last few "
              + "lines in whatever voice you have chosen, and it starts from whatever is being said when they press it."
            : "This needs your club website switched on as well - it is on the Club Website page. Until then nothing "
              + "is sent and nobody can listen.";
    }

    private void DescribeNoteRoutes()
    {
        var wifi = _chkNotesWifi.Checked;
        var relay = _chkNotesRelay.Checked;

        if (!wifi && !relay)
        {
            _lblNotesWhere.Text = "Only you can add one at the moment, on the Notes button on Race Control.";
            return;
        }

        var said = new List<string>();

        if (wifi)
        {
            // The same address the Club Website page gives out for the
            // live results, with the page's own path on the end. Said
            // rather than linked because somebody is going to write it on
            // a whiteboard.
            said.Add($"on the club Wi-Fi at {WebServerService.NotesPath} under this PC's own address");
        }

        if (relay)
        {
            said.Add("from your club's web page, for the people who could not make it");
        }

        _lblNotesWhere.Text = "Messages can come in " + string.Join(", and ", said)
            + ". Every one waits on Race Control until you approve it.";
    }

    /// <summary>
    /// Where the room votes, said rather than linked - somebody is going
    /// to write it on a whiteboard.
    /// </summary>
    private void DescribeVoteRoutes()
    {
        var wifi = _chkVoteWifi.Checked;
        var relay = _chkVoteRelay.Checked;

        if (!wifi && !relay)
        {
            _lblVoteWhere.Text = "Nobody can vote at the moment. You can still open a vote on Race Control to see "
                + "the grid, but no phone has a way in.";
            return;
        }

        var said = new List<string>();

        if (wifi)
        {
            said.Add($"on the club Wi-Fi at {WebServerService.VotePath} under this PC's own address");
        }

        if (relay)
        {
            said.Add("from your club's web page, for the people who could not make it");
        }

        _lblVoteWhere.Text = "The room can vote " + string.Join(", and ", said)
            + ". No running tally is ever shown to a phone, so nobody votes with the crowd.";
    }

    private void FillLibrary(ComboBox box, ShowPart part)
    {
        box.DropDownStyle = ComboBoxStyle.DropDownList;
        box.Items.Clear();
        box.Items.Add(NoPiece);

        var pieces = ShowMusicLibrary.For(part);

        foreach (var piece in pieces)
        {
            box.Items.Add(piece.Name);
        }

        var on = (part switch
        {
            ShowPart.Bed => _settings.ShowBedBuiltIn,
            ShowPart.Sting => _settings.ShowStingBuiltIn,
            _ => _settings.ShowOpenerBuiltIn,
        } ?? string.Empty).Trim();

        // A name the library no longer has is ADDED to the list and
        // selected, rather than silently falling back to None: the club
        // chose it, the file has gone, and Describe says so beside the
        // row. Dropping it from the list would make the page claim they
        // had chosen nothing.
        if (on.Length > 0 && !ShowMusicLibrary.Has(part, on))
        {
            box.Items.Add(on);
        }

        box.SelectedItem = on.Length > 0 ? on : NoPiece;

        if (box.SelectedIndex < 0)
        {
            box.SelectedIndex = 0;
        }

        box.SelectedIndexChanged += (_, _) =>
        {
            var chosen = box.SelectedItem as string;

            _showMusic.ChooseFromLibrary(part, string.Equals(chosen, NoPiece, StringComparison.Ordinal) ? null : chosen);

            // Both, exactly as PickShowMusic and ClearShowMusic do: the
            // announcer screen holds a ShowMusic of its own over the same
            // static clip cache, and this page's labels are stale until
            // they are redrawn.
            ShowMusicChanged?.Invoke();
            RefreshShowMusic();
        };
    }

    private void RefreshShowMusic()
    {
        _lblOpenerFile.Text = _showMusic.Describe(ShowPart.Opener);
        _lblBedFile.Text = _showMusic.Describe(ShowPart.Bed);
        _lblStingFile.Text = _showMusic.Describe(ShowPart.Sting);

        // And what the running order is going to do with their opener,
        // which is measured off the file in the same way and so is just as
        // impossible to guess from the outside. Only worth saying while
        // the shortening is on: unticked, every show plays it whole and
        // there is nothing to report.
        _lblRunningOrder.Text = _chkShowRejoin.Checked
            ? _showMusic.DescribeRunningOrder()
            : "every show plays the opener whole.";

        UpdateEnabledState();
    }

    /// <summary>
    /// Plays a show open with a test line over it - a real one, through
    /// the announcer, because the levels are the whole point and they can
    /// only be judged against an actual voice.
    /// </summary>
    /// <summary>
    /// Presses it: writes the recap, plays it, and says what happened.
    ///
    /// The button stays down for the whole thing - the writing AND the
    /// playing, which together is a couple of minutes - so nobody starts
    /// a second recap over the first. The same shape as Play It above,
    /// and the caption says which half it is on, because a button that
    /// says "Playing..." through forty seconds of silence while a model
    /// writes looks broken.
    /// </summary>
    private async Task PlayTonightsRecapAsync()
    {
        if (PlayTonightsRecap is null)
        {
            return;
        }

        _nightRecapRunning = true;
        _btnNightRecap.Enabled = false;
        _btnNightRecap.Text = "Writing...";
        _lblNightRecap.Text = string.Empty;
        _lblNightRecap.ForeColor = AppTheme.TextMuted;

        try
        {
            var problem = await PlayTonightsRecap.Invoke();

            if (IsDisposed)
            {
                return;
            }

            _lblNightRecap.Text = problem
                ?? "Played, and saved in tonight's folder as \"The Night\". Open folder above to find it.";
            _lblNightRecap.ForeColor = problem is null ? AppTheme.TextMuted : AppTheme.Accent;
        }
        catch (Exception ex)
        {
            _lblNightRecap.Text = ex.Message;
            _lblNightRecap.ForeColor = AppTheme.Accent;
        }
        finally
        {
            _nightRecapRunning = false;

            if (!IsDisposed)
            {
                // The switch, not simply back on: commentary may have been
                // turned off while it ran.
                _btnNightRecap.Enabled = _chkEnabled.Checked;
                _btnNightRecap.Text = NightRecapCaption;
            }
        }
    }

    /// <summary>
    /// Fills the driver picker with tonight's drivers, keeping whoever was
    /// picked where they are still in the list.
    ///
    /// KEEPS THE SELECTION, because this runs every time the box is opened:
    /// a club that picked Bill, read the hint and opened the box again must
    /// not find it back on the first name. The list beside the box is
    /// rebuilt with it, so an index never points at the wrong driver.
    /// </summary>
    private void FillDriversTonight()
    {
        var wanted = _cmbDriverCut.SelectedIndex >= 0 && _cmbDriverCut.SelectedIndex < _driversTonight.Count
            ? _driversTonight[_cmbDriverCut.SelectedIndex].Transponder
            : string.Empty;

        var tonight = DriversTonight?.Invoke() ?? Array.Empty<(string Transponder, string Name)>();

        var fresh = tonight
            .Where(one => !string.IsNullOrWhiteSpace(one.Transponder) && !string.IsNullOrWhiteSpace(one.Name))
            .ToList();

        // Only when it has actually changed: refilling a ComboBox closes
        // its own drop-down, and this runs from the event that opens it.
        if (fresh.Count == _driversTonight.Count
            && fresh.Select(one => one.Transponder).SequenceEqual(
                _driversTonight.Select(one => one.Transponder), StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        _driversTonight.Clear();
        _driversTonight.AddRange(fresh);

        _cmbDriverCut.Items.Clear();

        foreach (var one in _driversTonight)
        {
            _cmbDriverCut.Items.Add(one.Name.Trim());
        }

        if (_cmbDriverCut.Items.Count == 0)
        {
            return;
        }

        var back = wanted.Length == 0
            ? -1
            : _driversTonight.FindIndex(one =>
                string.Equals(one.Transponder, wanted, StringComparison.OrdinalIgnoreCase));

        _cmbDriverCut.SelectedIndex = back >= 0 ? back : 0;
    }

    /// <summary>
    /// Writes and plays the picked driver's own night - see
    /// <see cref="DriverCut"/>.
    ///
    /// The same shape as the recap's handler above: the button says what it
    /// is doing while it does it, and the label says what happened either
    /// way, because a button that goes quiet is a button a club presses
    /// again.
    /// </summary>
    private async Task PlayADriversNightAsync()
    {
        if (PlayADriversNight is null)
        {
            return;
        }

        if (_cmbDriverCut.SelectedIndex < 0 || _cmbDriverCut.SelectedIndex >= _driversTonight.Count)
        {
            _lblDriverCut.Text = _driversTonight.Count == 0
                ? "Nobody has a race filed for tonight yet."
                : "Pick a driver first.";
            _lblDriverCut.ForeColor = AppTheme.Accent;

            return;
        }

        var (transponder, name) = _driversTonight[_cmbDriverCut.SelectedIndex];

        _driverCutRunning = true;
        _btnDriverCut.Enabled = false;
        _cmbDriverCut.Enabled = false;
        _btnDriverCut.Text = "Writing...";
        _lblDriverCut.Text = string.Empty;
        _lblDriverCut.ForeColor = AppTheme.TextMuted;

        try
        {
            var problem = await PlayADriversNight.Invoke(transponder, name);

            if (IsDisposed)
            {
                return;
            }

            _lblDriverCut.Text = problem
                ?? $"Played, and saved in tonight's folder as \"{DriverCut.LabelFor(name)}\". "
                   + "Open folder above to find it.";
            _lblDriverCut.ForeColor = problem is null ? AppTheme.TextMuted : AppTheme.Accent;
        }
        catch (Exception ex)
        {
            _lblDriverCut.Text = ex.Message;
            _lblDriverCut.ForeColor = AppTheme.Accent;
        }
        finally
        {
            _driverCutRunning = false;

            if (!IsDisposed)
            {
                _btnDriverCut.Enabled = _chkEnabled.Checked;
                _cmbDriverCut.Enabled = _chkEnabled.Checked;
                _btnDriverCut.Text = DriverCutCaption;
            }
        }
    }

    /// <summary>
    /// Writes the last segment's facts out to the developer console - see
    /// <see cref="NightAccount.Proof"/>.
    ///
    /// NOT INTO THE LABEL, which is one line beside a button: the proof is
    /// several facts and the console is where this app puts anything a club
    /// reads a few lines of. The label says where it went.
    /// </summary>
    private void ShowTheProof()
    {
        if (ProofOfTheLastLine is null)
        {
            return;
        }

        try
        {
            _lblProof.Text = ProofOfTheLastLine.Invoke();
            _lblProof.ForeColor = AppTheme.TextMuted;
        }
        catch (Exception ex)
        {
            _lblProof.Text = ex.Message;
            _lblProof.ForeColor = AppTheme.Accent;
        }
    }

    /// <summary>
    /// Writes the next race day's preview and says where it went.
    ///
    /// The button is held at its own width while the caption says
    /// "Writing..." - see Steady - because a button that shrinks under the
    /// pointer re-lays the row it is in.
    /// </summary>
    private async Task WriteTheNightBeforeAsync()
    {
        if (WriteTheNightBefore is null)
        {
            return;
        }

        _nightBeforeRunning = true;
        _btnNightBefore.Enabled = false;
        _btnNightBefore.Text = "Writing...";
        _lblNightBefore.Text = string.Empty;

        try
        {
            var said = await WriteTheNightBefore.Invoke();

            _lblNightBefore.Text = said ?? "The preview was written.";
            _lblNightBefore.ForeColor = AppTheme.TextMuted;
        }
        catch (Exception ex)
        {
            _lblNightBefore.Text = ex.Message;
            _lblNightBefore.ForeColor = AppTheme.Accent;
        }
        finally
        {
            _nightBeforeRunning = false;
            _btnNightBefore.Text = NightBeforeCaption;

            // Back to what UpdateEnabledState says rather than to true, so
            // commentary unticked while it was writing leaves it greyed.
            _btnNightBefore.Enabled = _chkEnabled.Checked;
        }
    }

    private async Task TestShowMusicAsync()
    {
        if (TestShowMusic is null)
        {
            return;
        }

        // The button stays down for the length of the show, so nobody
        // starts a second one over the first.
        _testShowRunning = true;
        _btnTestShow.Enabled = false;
        _btnTestShow.Text = "Playing...";
        _lblShowResult.Text = string.Empty;
        _lblShowResult.ForeColor = AppTheme.TextMuted;

        try
        {
            var problem = await TestShowMusic.Invoke();

            if (IsDisposed)
            {
                return;
            }

            // The press itself counts as a show of the evening, so a
            // second one is a rejoin and sounds different on purpose -
            // said here, because otherwise it reads as the test having
            // half failed the second time.
            _lblShowResult.Text = problem ?? "That is how it will sound. Press it again to hear a later show in the night.";
            _lblShowResult.ForeColor = problem is null ? AppTheme.TextMuted : AppTheme.Accent;
        }
        catch (Exception ex)
        {
            _lblShowResult.Text = ex.Message;
            _lblShowResult.ForeColor = AppTheme.Accent;
        }
        finally
        {
            _testShowRunning = false;

            if (!IsDisposed)
            {
                _btnTestShow.Text = TestShowCaption;
                UpdateEnabledState();
            }
        }
    }

    private void SaveCoHostVoice()
    {
        if (_fillingVoices || _cmbCoHostVoice.SelectedItem is not VoiceChoice choice)
        {
            return;
        }

        switch (_voiceListEngine)
        {
            case VoiceEngine.Kokoro:
                _settings.CoHostKokoroVoiceName = choice.Id;
                break;

            case VoiceEngine.HeatCaller:
                _settings.CoHostHeatCallerVoice = choice.Id;
                _settings.CoHostHeatCallerVoiceName = choice.Name;
                _settings.CoHostHeatCallerRev = choice.Rev;
                break;

            default:
                _settings.CoHostElevenLabsVoiceId = choice.Id;
                _settings.CoHostElevenLabsVoiceName = choice.Name;
                break;
        }

        _settings.Save();
    }

    /// <summary>
    /// Saves the honours reader's picked voice against the engine the list
    /// is showing - per engine, exactly as the mechanic's and the co-host's
    /// are, because a voice id from one engine means nothing to another.
    /// </summary>
    private void SaveHonoursVoice()
    {
        if (_fillingVoices || _cmbHonoursVoice.SelectedItem is not VoiceChoice choice)
        {
            return;
        }

        switch (_voiceListEngine)
        {
            case VoiceEngine.Kokoro:
                _settings.HonoursKokoroVoiceName = choice.Id;
                break;

            case VoiceEngine.HeatCaller:
                _settings.HonoursHeatCallerVoice = choice.Id;
                _settings.HonoursHeatCallerVoiceName = choice.Name;
                _settings.HonoursHeatCallerRev = choice.Rev;
                break;

            default:
                _settings.HonoursElevenLabsVoiceId = choice.Id;
                _settings.HonoursElevenLabsVoiceName = choice.Name;
                break;
        }

        _settings.Save();
    }

    /// <summary>Saves the recap host's picked voice against the engine the list is showing - per engine, as the others are.</summary>
    private void SaveRecapHostVoice()
    {
        if (_fillingVoices || _cmbRecapHostVoice.SelectedItem is not VoiceChoice choice)
        {
            return;
        }

        switch (_voiceListEngine)
        {
            case VoiceEngine.Kokoro:
                _settings.RecapHostKokoroVoiceName = choice.Id;
                break;

            case VoiceEngine.HeatCaller:
                _settings.RecapHostHeatCallerVoice = choice.Id;
                _settings.RecapHostHeatCallerVoiceName = choice.Name;
                _settings.RecapHostHeatCallerRev = choice.Rev;
                break;

            default:
                _settings.RecapHostElevenLabsVoiceId = choice.Id;
                _settings.RecapHostElevenLabsVoiceName = choice.Name;
                break;
        }

        _settings.Save();
    }

    /// <summary>Saves the picked voice against the engine the list is showing.</summary>
    private void SaveMechanicVoice()
    {
        if (_fillingVoices || _cmbMechanicVoice.SelectedItem is not VoiceChoice choice)
        {
            return;
        }

        switch (_voiceListEngine)
        {
            case VoiceEngine.Kokoro:
                _settings.MechanicKokoroVoiceName = choice.Id;
                break;

            case VoiceEngine.HeatCaller:
                _settings.MechanicHeatCallerVoice = choice.Id;
                _settings.MechanicHeatCallerVoiceName = choice.Name;
                _settings.MechanicHeatCallerRev = choice.Rev;
                break;

            default:
                _settings.MechanicElevenLabsVoiceId = choice.Id;
                _settings.MechanicElevenLabsVoiceName = choice.Name;
                break;
        }

        _settings.Save();
    }

    /// <summary>
    /// Asks the engine in use for its voices. The button's caption says it
    /// is working rather than the button being disabled - see _testRunning
    /// for why.
    /// </summary>
    private async Task LoadMechanicVoicesAsync()
    {
        if (_loadingVoices)
        {
            return;
        }

        var engine = EngineInUse?.Invoke() ?? VoiceEngine.ElevenLabs;

        if (LoadVoices is null)
        {
            AppDialog.Show(this, "The voice list isn't available in this window.", "Mechanic's voice", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _loadingVoices = true;
        _btnLoadVoices.Text = "Loading...";

        try
        {
            var voices = await LoadVoices(engine);

            if (!IsDisposed)
            {
                FillMechanicVoices(engine, voices);
            }
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                AppDialog.Show(this, $"Couldn't load the {EngineLabel(engine)} voices - {ex.Message.TrimEnd('.')}.", "Mechanic's voice",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            _loadingVoices = false;

            if (!IsDisposed)
            {
                _btnLoadVoices.Text = LoadVoicesCaption;
            }
        }
    }

    private void SelectTone(CommentaryTone tone)
    {
        for (var i = 0; i < _cmbTone.Items.Count; i++)
        {
            if (_cmbTone.Items[i] is ToneItem item && item.Tone == tone)
            {
                _cmbTone.SelectedIndex = i;
                return;
            }
        }

        if (_cmbTone.Items.Count > 0)
        {
            _cmbTone.SelectedIndex = 0;
        }
    }

    private sealed record ToneItem(CommentaryTone Tone, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// The spoken race intros, recaps and teaser as settings now have them -
    /// changed from outside this page by lap times only, and by leaving it
    /// (see LapTimesOnly). Only a tick that differs is set, so nothing is
    /// saved for nothing; each one's own handler greys what goes with it.
    /// </summary>
    public void ShowSpokenTicks()
    {
        if (_chkIntros.Checked != _settings.CommentaryRaceIntros)
        {
            _chkIntros.Checked = _settings.CommentaryRaceIntros;
        }

        if (_chkSummaries.Checked != _settings.CommentaryRaceSummaries)
        {
            _chkSummaries.Checked = _settings.CommentaryRaceSummaries;
        }

        if (_chkTeaser.Checked != _settings.SummaryTeaserEnabled)
        {
            _chkTeaser.Checked = _settings.SummaryTeaserEnabled;
        }
    }

    private void UpdateEnabledState()
    {
        // RECORDING IS NOT GATED ON THE ANNOUNCER (11 Oct 2026). A club
        // that runs no commentary at all still has music, effects, flag
        // calls and lap times going out of this PC, and a recording of that
        // is a recording of their night. The card is on this page because
        // this is the page about what goes out over the PA, not because it
        // depends on anything else on it.
        //
        // Open folder stays live whatever the switches say: somebody
        // turning recording off is quite likely to want the files they
        // already have.
        var recording = _chkRecord.Checked;

        _chkClips.Enabled = recording;
        _txtAudioFolder.Enabled = recording;
        _btnAudioFolder.Enabled = recording;
        _numKeepNights.Enabled = recording;

        var on = _chkEnabled.Checked;
        _chkUseRelay.Enabled = on;
        _chkLocalModel.Enabled = on;
        _numTimeout.Enabled = on;

        // The tag pass does nothing unless the tone is Enthusiastic, so the
        // box says so by going grey rather than by quietly doing nothing.
        _chkVoiceTags.Enabled = on && _settings.CommentaryToneStyle == CommentaryTone.Enthusiastic;

        var coHost = on && _chkCoHost.Checked;
        _cmbExchangeTurns.Enabled = coHost;
        _chkTwoVoicesInHeats.Enabled = coHost;
        _chkIntros.Enabled = on;
        _chkIntrosSelective.Enabled = on && _chkIntros.Checked;
        _chkSummaries.Enabled = on;
        _chkTalkingPoints.Enabled = on;

        // Sub-controls track their own feature's checkbox too - no point
        // letting someone set "mention 8 finishers" while summaries
        // themselves are switched off.
        var summariesOn = on && _chkSummaries.Checked;
        _numFinishers.Enabled = summariesOn;
        _chkMentionFastestLap.Enabled = summariesOn;

        // The broadcast is the recap's, so it goes dark with recaps.
        _chkOpening.Enabled = summariesOn;
        _chkClosing.Enabled = summariesOn;
        _txtShowName.Enabled = summariesOn && (_chkOpening.Checked || _chkClosing.Checked);

        // NOT tied to the recap's opening the way the show name is. This
        // name is also used by the COLD OPEN, which is a segment of its own
        // with its own tick box further down the page, so a club running the
        // cold open with the recap's broadcast dressing switched off still
        // needs somewhere to type it.
        _txtAnnouncerName.Enabled = on;
        _chkInterview.Enabled = summariesOn;

        var interviewOn = summariesOn && _chkInterview.Checked;
        _txtMechanicName.Enabled = interviewOn;
        _cmbMechanicVoice.Enabled = interviewOn;
        _btnTestMechanic.Enabled = interviewOn;

        // The honours ride on the recap, so they go dark with recaps like
        // the rest of the broadcast - but their reader's rows stay live
        // when only the interview is off, because the two are separate
        // speakers with separate switches.
        // The break fills the time the RECAP takes to write, so there is
        // nothing for it to fill when recaps are off - it goes dark with
        // the rest of the broadcast.
        _chkAdBreak.Enabled = summariesOn;

        _chkBadgeHonours.Enabled = summariesOn;

        var honoursOn = summariesOn && _chkBadgeHonours.Checked;
        _txtHonoursName.Enabled = honoursOn;
        _cmbHonoursVoice.Enabled = honoursOn;
        _btnTestHonours.Enabled = honoursOn;

        // The recap host reads the recap show, so it goes dark with recaps.
        _cmbRecapHostVoice.Enabled = summariesOn;
        _btnTestRecapHost.Enabled = summariesOn;
        _txtRecapHostName.Enabled = summariesOn;

        // Load Voices fills all three lists in one pass
        // (FillMechanicVoices), so it is live while ANY of the three
        // speakers is set up. Dark only when none of them is, which is when
        // there is nothing for a voice list to be for.
        _btnLoadVoices.Enabled = interviewOn || honoursOn || summariesOn || (on && _chkCoHost.Checked);

        var talkingOn = on && _chkTalkingPoints.Checked;
        _numInterval.Enabled = talkingOn;
        _numObservations.Enabled = talkingOn;

        // Style applies to whichever features are on, so it only fully goes
        // dark when the master switch itself is off.
        _chkBroadcastVoice.Enabled = on;
        _numStability.Enabled = on && _chkBroadcastVoice.Checked;
        _numStyleEx.Enabled = on && _chkBroadcastVoice.Checked;
        _chkDeepDuck.Enabled = on;
        _numDuckPercent.Enabled = on && _chkDeepDuck.Checked;
        _numSummaryDelay.Enabled = on;
        _chkTeaser.Enabled = on;
        _txtTeaser.Enabled = on && _chkTeaser.Checked;
        _numTeaserGap.Enabled = on && _chkTeaser.Checked;

        _cmbTone.Enabled = on;
        _numMaxWords.Enabled = on;
        _numVariety.Enabled = on;

        foreach (var box in _segmentBoxes.Values)
        {
            box.Enabled = on;
        }

        // The co-host is only worth setting up when something is going to
        // use them, so the row goes dark when every two-hander segment is
        // switched off as well as when commentary is.
        var anyExchange = _segmentBoxes.Any(pair => pair.Value.Checked && CommentaryBudget.Spec(pair.Key).CanBeExchange);

        _chkCoHost.Enabled = on && anyExchange;
        _cmbChatter.Enabled = on;

        // NOT GATED ON THE ANNOUNCER being switched on, deliberately - the
        // same argument the recording card makes. A club can collect
        // messages all evening with the AI off and read them out itself;
        // what it cannot do is have the announcer say them, which is
        // already true because there is no announcer.
        _chkNotesWifi.Enabled = true;
        _chkNotesRelay.Enabled = true;

        // Listening along is NOT gated on the announcer either, and for a
        // better reason than the notes boxes have: with the AI announcer
        // off, the things this sends are the lap calls, flag calls and
        // results reads, which are the ones a listener at home most wants.
        _chkListenAlong.Enabled = true;
        _txtCoHostName.Enabled = on && anyExchange && _chkCoHost.Checked;
        _cmbCoHostVoice.Enabled = on && anyExchange && _chkCoHost.Checked;
        _cmbCoHostStance.Enabled = on && anyExchange && _chkCoHost.Checked;

        // The show's music dresses the segments, so it goes dark with
        // commentary itself. Remove is dark when there is nothing there to
        // remove, which is also how the page says a file never arrived.
        _chkShowMusic.Enabled = on;

        var musicOn = on && _chkShowMusic.Checked;

        _btnOpenerPick.Enabled = musicOn;
        _btnOpenerClear.Enabled = musicOn && _showMusic.OpenerPath is not null;
        _btnBedPick.Enabled = musicOn;
        _btnBedClear.Enabled = musicOn && _showMusic.BedPath is not null;
        _btnStingPick.Enabled = musicOn;
        _btnStingClear.Enabled = musicOn && _showMusic.StingPath is not null;

        // The library lists go dark with the rest of the card, and also
        // while the club is on a file of their OWN for that part: the
        // shipped piece is not what is playing then, so a list that still
        // looked live would be claiming otherwise. Remove puts them back.
        _cmbOpenerPiece.Enabled = musicOn && !_showMusic.IsTheirOwn(ShowPart.Opener);
        _cmbBedPiece.Enabled = musicOn && !_showMusic.IsTheirOwn(ShowPart.Bed);
        _cmbStingPiece.Enabled = musicOn && !_showMusic.IsTheirOwn(ShowPart.Sting);
        _numOpenerLevel.Enabled = musicOn;
        _numBedLevel.Enabled = musicOn;
        _numBedDuck.Enabled = musicOn;
        _numVoiceEnters.Enabled = musicOn;

        // The running order shortens the OPENER, so it has nothing to do
        // for a club who have only ever put a bed in.
        _chkShowRejoin.Enabled = musicOn && _showMusic.OpenerPath is not null;

        // Nothing to play: a Test button that does nothing reads as the
        // feature being broken rather than as a club with no files in yet.
        _btnTestShow.Enabled = !_testShowRunning && musicOn && (_showMusic.OpenerPath is not null || _showMusic.BedPath is not null);

        // THE RECAP NEEDS THE ANNOUNCER, not the music. It is read plainly
        // where a club has unticked the sign-off's music (the handler says
        // so), but with no commentary at all there is nothing to write it
        // with - and a button that answers "the commentary isn't set up"
        // every time is a button that should have been greyed out.
        _btnNightRecap.Enabled = !_nightRecapRunning && _chkEnabled.Checked;

        // THE SAME RULE for the two beside it: both need the announcer and
        // neither needs the music. A driver with no race filed and a night
        // with no segment said yet are both answered in words by the
        // handlers rather than by a greyed-out button, because "nothing has
        // been said yet tonight" is a state a club should be able to read
        // rather than guess at from a button that will not press.
        _btnDriverCut.Enabled = !_driverCutRunning && _chkEnabled.Checked;
        _cmbDriverCut.Enabled = !_driverCutRunning && _chkEnabled.Checked;
        _btnProof.Enabled = _chkEnabled.Checked;

        // THE SAME RULE for the preview. It is never spoken, but it is
        // written by the announcer's model, and with commentary unticked
        // there is no model to write it with - the button used to stay
        // live and answer "isn't set up" to a club whose set-up was fine.
        _btnNightBefore.Enabled = !_nightBeforeRunning && _chkEnabled.Checked;

        foreach (var box in _showBoxes.Values)
        {
            box.Enabled = musicOn;
        }

        UpdateRelayState();
    }

    /// <summary>
    /// What the relay box says underneath it. The relay rides on the Club
    /// Website page's address, club and token - the same ones the results
    /// publishing uses - so a club that has publishing set up has this with
    /// nothing more to do, and a club that hasn't should be told that here
    /// rather than finding out from a race that wrote nothing.
    ///
    /// Worked out again whenever the page is shown, not once at startup:
    /// the Club Website page is usually filled in after this one.
    /// </summary>
    private void UpdateRelayState()
    {
        var useRelay = _chkUseRelay.Checked;
        var useLocal = _settings.CommentaryUseLocalModel;

        // Only asked when it matters - building a client to answer it is
        // cheap, but pointless when the relay is switched off anyway.
        var ready = useRelay && new RelayCommentaryClient(_settings).RelayConfigured;

        _lblRelayState.Text = RelayStateText(useRelay, ready, useLocal);

        // Amber whenever nothing would be written, which is the one state
        // worth interrupting someone over: commentary is ticked on and the
        // page is quietly telling them it will never say a word.
        var willWrite = ready || useLocal;

        _lblRelayState.ForeColor = willWrite ? AppTheme.TextMuted : AppTheme.Accent;
    }

    /// <summary>
    /// What the line under the relay box says. Pure, so the sizing list and
    /// the live text cannot drift apart - see AllRelayStates.
    /// </summary>
    private static string RelayStateText(bool useRelay, bool ready, bool useLocal)
    {
        var local = useLocal ? "the built-in model" : null;

        if (!useRelay)
        {
            return local is null
                ? "Nothing is set to write - the relay is off and so is the model on this PC."
                : $"Everything is written by {local} below.";
        }

        if (!ready)
        {
            return local is null
                ? "The Club Website page isn't set up yet, and nothing on this PC is set to write."
                : $"The Club Website page isn't set up yet, so everything is written by {local} below.";
        }

        return local is null
            ? "The Club Website page is set up, so the relay writes - and nothing covers the races it can't."
            : $"The Club Website page is set up, so the relay writes first and {local} below covers what it can't.";
    }

    /// <summary>
    /// Every sentence RelayStateText can return, for the label to size to.
    /// Enumerated rather than typed out, so a reworded sentence cannot end
    /// up missing from the list and clipping on the night.
    /// </summary>
    private static string[] AllRelayStates()
    {
        var all = new List<string>();

        foreach (var useLocal in new[] { true, false })
        {
            all.Add(RelayStateText(useRelay: false, ready: false, useLocal));
            all.Add(RelayStateText(useRelay: true, ready: false, useLocal));
            all.Add(RelayStateText(useRelay: true, ready: true, useLocal));
        }

        return all.Distinct().ToArray();
    }

    /// <summary>
    /// Rebuilds the service's client from current settings. Called on every
    /// change so edits take effect immediately rather than at next launch.
    /// </summary>
    /// <summary>
    /// The most recent detail from the client itself - e.g. "no model file
    /// is installed", or a timeout with the seconds it waited. The brain's
    /// own account of a failure is almost always more useful than anything
    /// generic this page could show instead, so the Test button surfaces it
    /// directly rather than guessing at a cause.
    /// </summary>
    private string? _lastClientDetail;

    /// <summary>
    /// The local brain this page last built, kept so it can be let go when a
    /// different one is built - and, far more importantly, so it is NOT
    /// rebuilt when nothing about it changed. BundledLlmClient holds the
    /// model's weights in memory.
    /// </summary>
    private ICommentaryBrain? _localBrain;

    /// <summary>
    /// What the client currently handed to the service was built from. Null
    /// when there isn't one.
    ///
    /// ApplyToService is called from six places and on every settings
    /// change, which was free when the local brain was an HTTP client with
    /// an endpoint on it. The built-in model is not free: rebuilding meant
    /// reading 440 MB off disk and holding a second copy of the weights
    /// while the old one was collected, and the console showed it - two
    /// "Loaded commentary.gguf" lines a minute apart, from nothing more than
    /// opening the page. So the work is done when the configuration actually
    /// differs, and skipped when it doesn't.
    /// </summary>
    private string? _clientKey;

    private void ApplyToService()
    {
        if (!_settings.CommentaryEnabled)
        {
            ReleaseClient();
            _commentary.Client = null;
            return;
        }

        var budget = TimeSpan.FromSeconds(Math.Clamp(_settings.CommentaryTimeoutSeconds, 2, 60));

        // Everything below is built from these. The relay's own address,
        // club and token are deliberately absent: RelayCommentaryClient
        // asks the settings again on every line, so a change there needs no
        // new client - only turning the relay off or on does.
        var key = string.Join(
            '\u001f',
            _settings.CommentaryUseLocalModel,
            _settings.CommentaryLocalModelPath,
            budget.Ticks,
            _settings.CommentaryUseRelay);

        if (_clientKey == key && _commentary.Client is not null)
        {
            return;
        }

        ReleaseClient();

        // The model that ships inside the installer, run in this process.
        // It is only what writes when the relay cannot - see below - and a
        // club that would rather a quiet segment than a plainly-written one
        // unticks it and gets null here.
        ICommentaryBrain? local = _settings.CommentaryUseLocalModel
            ? new BundledLlmClient
            {
                ModelPathOverride = string.IsNullOrWhiteSpace(_settings.CommentaryLocalModelPath)
                    ? null
                    : _settings.CommentaryLocalModelPath,
                Timeout = budget,
            }
            : null;

        _localBrain = local;

        // The relay goes in front of the local model when the Club Website
        // page is set up and the club hasn't turned it off: it writes with a
        // model no race laptop could run, and needs nothing installed. The
        // local model stays behind it rather than being replaced - it is what
        // writes with the internet down, and what writes for a club the
        // relay has not included for commentary yet. The choice is made
        // per line, not here; see FallbackCommentaryBrain.
        var relay = _settings.CommentaryUseRelay ? new RelayCommentaryClient(_settings) : null;

        if (relay is not null)
        {
            // The club's month, as the relay counts it - passed straight
            // through to whoever is pacing the night against it. Attached
            // to each new client, since a settings change builds one.
            relay.AllowanceSeen += (spent, cap) =>
            {
                try
                {
                    AllowanceSeen?.Invoke(spent, cap);
                }
                catch
                {
                    // A listener's failure must not become a write failure.
                }
            };
        }

        // Wrapped whenever the box is ticked, set up or not. The Club
        // Website page is usually filled in after this one, and a restored
        // relay address is only let through once it is checked; deciding
        // here would leave a club on the local model alone all night while
        // the page tells them the relay writes first. The relay client asks the
        // settings again on every line and steps aside when there is
        // nothing to ask.
        // Both, one, or neither. FallbackCommentaryBrain needs two brains, so
        // it is only built when there are two; with the local brain turned
        // off the relay stands alone, and with no relay the local one does.
        // Neither leaves Client null, which CommentaryService already treats
        // as "no brain" everywhere.
        //
        // Written out rather than as a switch on the pair: a tuple pattern
        // does not narrow the nullability of the variables it matched, so the
        // tidier version warns on both arguments under <Nullable>enable</Nullable>.
        ICommentaryBrain client;

        if (relay is not null && local is not null)
        {
            client = new FallbackCommentaryBrain(relay, local);
        }
        else if (relay is not null)
        {
            client = relay;
        }
        else if (local is not null)
        {
            client = local;
        }
        else
        {
            // Relay off and local off. Nothing can write, and saying so
            // plainly beats a client that fails on every line.
            _commentary.Client = null;
            return;
        }

        client.StatusMessage += msg =>
        {
            _lastClientDetail = msg;

            // Passed on for the console. Nothing else ever heard the
            // client's "[llm] ..." lines - a timeout, a model not pulled -
            // because each settings change builds a new client, and this
            // was its only subscriber; Race Control's "any [llm] line above
            // says why" pointed at a line that was never written.
            try
            {
                ClientStatusMessage?.Invoke(msg);
            }
            catch
            {
                // A listener's failure must not become a commentary failure.
            }
        };

        _commentary.Client = client;
        _clientKey = key;
    }

    /// <summary>
    /// Lets go of the client currently in use, so the next one can be built.
    ///
    /// Only the local brain is disposable, and only the built-in one holds
    /// anything worth releasing - the model's weights, several hundred
    /// megabytes of them. Called before building a replacement and when
    /// commentary is switched off, which is the one case where nothing is
    /// built afterwards and the memory would otherwise sit there all night.
    /// </summary>
    private void ReleaseClient()
    {
        (_localBrain as IDisposable)?.Dispose();
        _localBrain = null;
        _clientKey = null;
    }

    /// <summary>
    /// Fires a background warm-up, guarded so it only happens at moments
    /// that plausibly mean "the configuration just settled" - loading the
    /// model is seconds of work, and firing it on every keystroke in a
    /// settings box would be seconds of work per character typed.
    /// </summary>
    private void TriggerWarmUp() => _ = _commentary.Client?.WarmUpAsync();

    private async Task RunTestAsync(Button btnTest)
    {
        if (!_chkEnabled.Checked)
        {
            _lblTestResult.Text = "Enable commentary first.";
            _lblTestResult.ForeColor = AppTheme.Accent;
            return;
        }

        if (_testRunning)
        {
            return;
        }

        ApplyToService();
        _lastClientDetail = null;

        _testRunning = true;
        btnTest.Text = "Testing...";
        _lblTestResult.Text = "Testing...";
        _lblTestResult.ForeColor = AppTheme.TextMuted;

        try
        {
            var started = DateTime.UtcNow;
            var reply = await (_commentary.Client?.TestAsync() ?? Task.FromResult<string?>(null));
            var elapsed = (DateTime.UtcNow - started).TotalSeconds;

            if (reply is null)
            {
                // The client already knows exactly why - no model file, a
                // timeout with the seconds it waited, a PC too slow to keep
                // up - showing that beats guessing at a cause the client has
                // already ruled in or out.
                _lblTestResult.Text = _lastClientDetail is { Length: > 0 }
                    ? $"{_lastClientDetail} Commentary will be skipped and normal announcements used."
                    : "No response - any [llm] line in the console says why. "
                      + "Commentary will be skipped and normal announcements used.";
                _lblTestResult.ForeColor = AppTheme.Danger;
            }
            else
            {
                // Reporting the round-trip time matters more than the reply
                // itself: a working-but-slow model is the failure mode most
                // likely to disappoint on a race day, and this is where
                // that's discoverable.
                _lblTestResult.Text = $"Working - replied in {elapsed:0.0}s. "
                                      + (elapsed > 8
                                          ? "That's slow; consider a smaller model."
                                          : "That's comfortably within the gap between races.");
                _lblTestResult.ForeColor = elapsed > 8 ? AppTheme.Accent : AppTheme.Success;
            }
        }
        finally
        {
            _testRunning = false;
            btnTest.Text = TestCaption;
        }
    }
}
