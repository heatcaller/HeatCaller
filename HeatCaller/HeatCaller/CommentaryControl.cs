namespace HeatCaller;

/// <summary>
/// Settings for local LLM colour commentary.
///
/// The page leads with what this actually requires - a separate Ollama
/// install - because the most likely reason it won't work is that the model
/// isn't running, and that should be obvious from here rather than
/// discovered mid-race.
/// </summary>
public sealed class CommentaryControl : UserControl
{
    private readonly AppSettings _settings;
    private readonly CommentaryService _commentary;

    private readonly CheckBox _chkEnabled = new ThemedCheckBox();
    private readonly TextBox _txtEndpoint = new();
    private readonly TextBox _txtModel = new();
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
    /// Ollama's own error text can run longer, and then it grows.
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
    private (Font Font, int Count, int Width)? _honoursVoiceWidth;

    /// <summary>The usual captions of the two buttons that say so while they work - see BuildLayout's Steady.</summary>
    private const string TestCaption = "Test Connection";
    private const string LoadVoicesCaption = "Load Voices";
    private const string TestShowCaption = "Play It";

    /// <summary>The boxes' and pickers' widths at the normal text size - see SizeFields.</summary>
    private const int EndpointWidth = 300;
    private const int TonePickerWidth = 220;
    private const int ShowNameWidth = 280;

    /// <summary>The library drop-downs. Wide enough for "Good Morning America 2" and "Opening Drive (132 BPM)" without cutting either off.</summary>
    private const int ShowPieceWidth = 220;
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

    /// <summary>
    /// Plays a show open with a test line over it, as a real segment
    /// would, and hands back what was wrong or null. Set by the host,
    /// which has the announcer.
    /// </summary>
    internal Func<Task<string?>>? TestShowMusic { get; set; }

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
                 + "which needs nothing installed here, or by an Ollama install on this machine.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        var lblRequirement = _textSize.Wear(
            new Label
            {
                Text = "The relay writes for clubs whose plan includes commentary - set the Club Website page up and leave the "
                     + "box below ticked. Ollama is what writes otherwise, and it needs ollama.com running locally with a model "
                     + "pulled - e.g. \"ollama pull llama3.2:3b\". If neither answers, commentary is skipped and the normal "
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
            "The relay writes with a hosted model no race laptop could run, and needs nothing installed here. Ollama below "
            + "covers whatever the relay can't write - the internet down, the club's lines for the month used up, or "
            + "commentary not included in the club's plan yet. Untick this and Ollama writes everything.");

        _lblRelayState.Wordings = new[]
        {
            "Everything is written by Ollama below.",
            "The Club Website page isn't set up yet, so everything is written by Ollama below.",
            "The Club Website page is set up, so the relay writes first and Ollama below covers what it can't.",
        };

        // ------------------------------------------------------------------
        // Connection
        // ------------------------------------------------------------------

        _txtEndpoint.Text = _settings.CommentaryEndpoint;
        _txtEndpoint.TextChanged += (_, _) =>
        {
            _settings.CommentaryEndpoint = _txtEndpoint.Text.Trim();
            _settings.Save();
            ApplyToService();
        };
        _txtEndpoint.Leave += (_, _) => TriggerWarmUp();

        _txtModel.Text = _settings.CommentaryModel;
        _txtModel.TextChanged += (_, _) =>
        {
            _settings.CommentaryModel = _txtModel.Text.Trim();
            _settings.Save();
            ApplyToService();
        };
        _txtModel.Leave += (_, _) => TriggerWarmUp();

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

        Flush(connectionGrid.AddRow("Endpoint:", _txtEndpoint));
        Flush(connectionGrid.AddRow("Model:", _txtModel));
        connectionGrid.AddHint("A small model is the right choice here - it only has to rephrase facts, and a big one is too slow to be useful.");
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
            "No response - check Ollama is running and the model name is right. "
                + "Commentary will be skipped and normal announcements used.",
            "Working - replied in 99.9s. That's comfortably within the gap between races.",
        };

        // The result beside the button, wrapping to the room to its right -
        // Ollama's own error text can run to two lines and more. The button
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
            "Ollama on this machine. It writes everything when the relay box above is off, and covers whatever the relay "
            + "can't write when it is on."));
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

        Flush(styleGrid.AddRow("Writing tone:", _cmbTone));
        styleGrid.AddHint("Enthusiastic also marks every sentence of the recap as excited on a v3 voice, so the energy holds to the end.");
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

        var showGrid = new FieldGrid();

        Stepped(showGrid.AddRow("Show name:", Line(_txtShowName, Suffix("named in the opening and the sign-off - leave it blank for none"))));

        Stepped(showGrid.AddRow("Announcer's name:", Line(_txtAnnouncerName, Suffix("he introduces himself at the cold open and the top of the recap - blank and he is never named"))));

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

        var segments = new Section("Segments");

        segments.Add(Hint(
            "What the announcer covers beyond the intros and recaps above. Each one only speaks when the race actually "
            + "gives it something to say, so switching them all on does not mean hearing them all every race."));

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

        var coHostGrid = new FieldGrid();

        Stepped(coHostGrid.AddRow("Co-host's name:", Line(_txtCoHostName, Suffix("blank and they are talked to but never named - the model never makes one up"))));
        Stepped(coHostGrid.AddRow("Co-host's voice:", Line(_cmbCoHostVoice)));
        coHostGrid.AddHint(
            "With no voice picked for the engine in use, the announcer's own voice reads both halves - still a conversation, "
            + "and better than nothing. The list is filled from the same engine as the mechanic's, beside Load Voices above.");

        var coHost = new Section("The co-host");

        coHost.Add(Hint(
            "A second commentator, for the segments that suit one. The announcer makes a remark and the co-host answers - "
            + "written as one request and split in two, so the answer actually answers the question. Segments marked above "
            + "as a two-hander use them; the rest stay single-voice, and anything that can't be split cleanly is simply "
            + "said in one voice instead."));
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

        foreach (var label in new[] { _lblOpenerFile, _lblBedFile, _lblStingFile, _lblShowResult })
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
        page.Add(style);
        page.Add(recap);
        page.Add(broadcast);
        page.Add(show, gapBelow: 0);

        Controls.Add(page);

        // Once everything is on the page and wears its face - what these are
        // measured in.
        //
        // One caption column down every card, so the fields line up down the
        // whole page - see Stepped and Flush.
        _textSize.Keep(() => FieldGrid.LineUp(new[]
        {
            connectionGrid, introsGrid, summariesGrid, talkingGrid, styleGrid,
            pauseGrid, teaserGrid, voiceGrid, showGrid, mechanicGrid, honoursGrid, coHostGrid,
            showMusicGrid,
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
        Fit(_txtEndpoint, UiLayout.AtTextSize(EndpointWidth));
        Fit(_txtModel, UiLayout.AtTextSize(EndpointWidth));
        Fit(_txtShowName, UiLayout.AtTextSize(ShowNameWidth));
        Fit(_txtAnnouncerName, UiLayout.AtTextSize(ShowNameWidth));
        Fit(_cmbOpenerPiece, UiLayout.AtTextSize(ShowPieceWidth));
        Fit(_cmbBedPiece, UiLayout.AtTextSize(ShowPieceWidth));
        Fit(_cmbStingPiece, UiLayout.AtTextSize(ShowPieceWidth));
        Fit(_txtMechanicName, UiLayout.AtTextSize(MechanicNameWidth));
        Fit(_txtCoHostName, UiLayout.AtTextSize(MechanicNameWidth));
        Fit(_txtHonoursName, UiLayout.AtTextSize(MechanicNameWidth));
        Fit(_txtTeaser, UiLayout.AtTextSize(TeaserWidth), Pieces.LineHeight(_txtTeaser.Font) * TeaserLines + UiLayout.HintGap * 2);

        Fit(_cmbTone, Math.Max(UiLayout.AtTextSize(TonePickerWidth), Pieces.PickerWidth(_cmbTone, ref _toneWidth)));
        Fit(_cmbMechanicVoice, Math.Max(UiLayout.AtTextSize(VoicePickerWidth), Pieces.PickerWidth(_cmbMechanicVoice, ref _voiceWidth)));
        Fit(_cmbCoHostVoice, Math.Max(UiLayout.AtTextSize(VoicePickerWidth), Pieces.PickerWidth(_cmbCoHostVoice, ref _coHostVoiceWidth)));
        Fit(_cmbHonoursVoice, Math.Max(UiLayout.AtTextSize(VoicePickerWidth), Pieces.PickerWidth(_cmbHonoursVoice, ref _honoursVoiceWidth)));

        foreach (var number in new[]
                 {
                     _numTimeout, _numFinishers, _numInterval, _numObservations, _numMaxWords, _numVariety,
                     _numSummaryDelay, _numTeaserGap, _numStability, _numStyleEx, _numDuckPercent,
                     _numOpenerLevel, _numBedLevel, _numBedDuck, _numVoiceEnters,
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
        _honoursVoiceWidth = null;
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
    private async Task TestShowMusicAsync()
    {
        if (TestShowMusic is null)
        {
            return;
        }

        // The button stays down for the length of the show, so nobody
        // starts a second one over the first.
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
        var on = _chkEnabled.Checked;
        _chkUseRelay.Enabled = on;
        _txtEndpoint.Enabled = on;
        _txtModel.Enabled = on;
        _numTimeout.Enabled = on;
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

        // Load Voices fills all three lists in one pass
        // (FillMechanicVoices), so it is live while ANY of the three
        // speakers is set up. Dark only when none of them is, which is when
        // there is nothing for a voice list to be for.
        _btnLoadVoices.Enabled = interviewOn || honoursOn || (on && _chkCoHost.Checked);

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
        _txtCoHostName.Enabled = on && anyExchange && _chkCoHost.Checked;
        _cmbCoHostVoice.Enabled = on && anyExchange && _chkCoHost.Checked;

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
        _btnTestShow.Enabled = musicOn && (_showMusic.OpenerPath is not null || _showMusic.BedPath is not null);

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
        if (!_chkUseRelay.Checked)
        {
            _lblRelayState.Text = "Everything is written by Ollama below.";
            _lblRelayState.ForeColor = AppTheme.TextMuted;
            return;
        }

        var ready = new RelayCommentaryClient(_settings).RelayConfigured;

        _lblRelayState.Text = ready
            ? "The Club Website page is set up, so the relay writes first and Ollama below covers what it can't."
            : "The Club Website page isn't set up yet, so everything is written by Ollama below.";

        _lblRelayState.ForeColor = ready ? AppTheme.TextMuted : AppTheme.Accent;
    }

    /// <summary>
    /// Rebuilds the service's client from current settings. Called on every
    /// change so edits take effect immediately rather than at next launch.
    /// </summary>
    /// <summary>
    /// The most recent detail from the client itself - e.g. "Request failed
    /// (404): model 'llama3.2:3b' not found, try pulling it first". Ollama's
    /// own error text is almost always more useful than anything generic
    /// this page could show instead, so the Test button surfaces this
    /// directly rather than guessing at a cause.
    /// </summary>
    private string? _lastClientDetail;

    private void ApplyToService()
    {
        if (!_settings.CommentaryEnabled)
        {
            _commentary.Client = null;
            return;
        }

        var local = new LocalLlmClient
        {
            Endpoint = _settings.CommentaryEndpoint,
            Model = _settings.CommentaryModel,
            Timeout = TimeSpan.FromSeconds(Math.Clamp(_settings.CommentaryTimeoutSeconds, 2, 60)),
        };

        // The relay goes in front of Ollama when the Club Website page is
        // set up and the club hasn't turned it off: it writes through a
        // model no race laptop could run, and needs nothing installed.
        // Ollama stays behind it rather than being replaced - it is what
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

        ICommentaryBrain client = relay is { RelayConfigured: true }
            ? new FallbackCommentaryBrain(relay, local)
            : local;

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
    }

    /// <summary>
    /// Fires a background warm-up, guarded so it only happens at moments
    /// that plausibly mean "the configuration just settled" - not on every
    /// keystroke while editing the endpoint or model, which would spam
    /// Ollama with a load request per character typed.
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
                // The client already knows exactly why - a 404 with Ollama's
                // own "model not found, try pulling it first", a timeout, a
                // refused connection - showing that beats guessing at a
                // cause the client has already ruled in or out.
                _lblTestResult.Text = _lastClientDetail is { Length: > 0 }
                    ? $"{_lastClientDetail} Commentary will be skipped and normal announcements used."
                    : "No response - check Ollama is running and the model name is right. "
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
