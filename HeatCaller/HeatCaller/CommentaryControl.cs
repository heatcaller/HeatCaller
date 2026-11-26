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

    /// <summary>
    /// Segments with no box on this page (17 Oct 2026), the way the
    /// Essentials have none. Nothing writes them: the director has no
    /// facts for either (CommentaryDirector.Candidates' default), so a
    /// tick did nothing. The mechanic's interview is really the Cast tab's
    /// "quick interview with the winner's mechanic" (RecapMechanicInterview;
    /// on this page's Broadcast card until 17 Oct 2026). Hidden rather than deleted from
    /// SegmentKind, because other code still names them.
    /// </summary>
    internal static readonly HashSet<SegmentKind> NotOffered = new()
    {
        SegmentKind.MechanicInterview,
        SegmentKind.Honours,
    };

    /// <summary>
    /// Delivery tags on spoken commentary. Was the recap's alone and not a
    /// choice at all - segments were never tagged, so the show's energy
    /// dropped the moment the recap ended.
    /// </summary>
    private readonly CheckBox _chkVoiceTags = new ThemedCheckBox();

    // --- How chatty the night is (12 Oct 2026) ---
    //
    // The pacing gives an evening a shape - quiet through the heats,
    // everything saved for the mains - and this is the volume that shape
    // sits at. One dial, five stops, and the curve survives all of them.
    // See ShowChatter. The line under it works its figures out from the
    // real tables rather than repeating them, so it cannot end up lying.
    private readonly ComboBox _cmbChatter = new();
    private readonly Label _lblChatter = new();

    private readonly CheckBox _chkIntros = new ThemedCheckBox();
    private readonly CheckBox _chkIntrosSelective = new ThemedCheckBox();
    private readonly CheckBox _chkDriverIntros = new ThemedCheckBox();
    private readonly CheckBox _chkDriverIntrosMains = new ThemedCheckBox();

    /// <summary>
    /// The pit lane visit before a race (18 Oct 2026) - the throw to the
    /// co-host and the Team Principal's word, said after the opening and
    /// before staging. The same switch as the Cast tab's "Before a race, a
    /// quick word with a Team Principal" (AppSettings.PrincipalBeforeRace),
    /// shown again here with the other segments so it can be found.
    /// </summary>
    private readonly CheckBox _chkPitLane = new ThemedCheckBox();

    /// <summary>The qualifying recap that opens a main's driver introductions - AppSettings.CommentaryQualifyingRecap.</summary>
    private readonly CheckBox _chkQualifyingRecap = new ThemedCheckBox();

    /// <summary>Each driver's name and photo on the Big Screen during the driver introductions - AppSettings.BigScreenIntroCards.</summary>
    private readonly CheckBox _chkIntroCards = new ThemedCheckBox();

    // AFTER THE FLAG (18 Oct 2026) - the race announcer's segments between
    // the finish call and the break, and the recap show's booth interview.
    // The two interviews are the Cast tab's switches too, shown here with
    // the other segments.

    /// <summary>The unofficial result after the finish call - AppSettings.FlagResultRead.</summary>
    private readonly CheckBox _chkFlagResult = new ThemedCheckBox();

    /// <summary>The throw to pit lane for the winner's Technical Lead - AppSettings.RecapMechanicInterview.</summary>
    private readonly CheckBox _chkFlagTechLead = new ThemedCheckBox();

    /// <summary>The Team Principal in the booth on the recap show - AppSettings.PrincipalAfterRace.</summary>
    private readonly CheckBox _chkBoothPrincipal = new ThemedCheckBox();
    private readonly CheckBox _chkSummaries = new ThemedCheckBox();
    private readonly CheckBox _chkTalkingPoints = new ThemedCheckBox();
    private readonly NumericUpDown _numFinishers = new();
    private readonly CheckBox _chkMentionFastestLap = new ThemedCheckBox();
    private readonly NumericUpDown _numInterval = new();
    private readonly NumericUpDown _numObservations = new();
    private readonly ComboBox _cmbTone = new();

    /// <summary>The Sailor-mouth dial (15 Nov 2026): swear words a line, 0 to CommentaryTones.MostSwears. Shown only while Sailor-mouth is the tone.</summary>
    private readonly SliderControl _sldSwears = new() { Minimum = 0, Maximum = CommentaryTones.MostSwears, SmallChange = 1, LargeChange = 1 };

    /// <summary>What the dial's number means - "up to 2 a line".</summary>
    private readonly Label _lblSwears = new() { AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Margin = Padding.Empty };

    /// <summary>
    /// A plain panel round <see cref="_lblSwears"/>, as wide as its longest
    /// text (SizeFields), so a new value is laid out in here and goes no
    /// further. A label straight in the row, sized by its text or not, laid
    /// the whole card out again at every stop of the dial - a third of a
    /// second each, so dragging it stuttered (15 Nov 2026).
    /// </summary>
    private readonly Panel _lblSwearsHost = new() { AutoSize = false, Margin = Padding.Empty, BackColor = Color.Transparent, Tag = AppTheme.KeepBackColor };

    /// <summary>The dial's caption, slider and hint - shown and hidden together (ShowSwearDial).</summary>
    /// <summary>
    /// The room kept for the dial beside the tone picker, the same size
    /// whichever tone is picked - so showing the dial changes nothing
    /// outside it (ShowSwearDial). Sized in SizeFields.
    /// </summary>
    private readonly Panel _swearHost = new() { AutoSize = false, Margin = Padding.Empty, BackColor = Color.Transparent, Tag = AppTheme.KeepBackColor };

    /// <summary>The dial's caption, slider and number, along one line inside <see cref="_swearHost"/> - shown or hidden whole.</summary>
    private readonly FlowLayoutPanel _swearInner = new()
    {
        AutoSize = false,
        Dock = DockStyle.Fill,
        WrapContents = false,
        FlowDirection = FlowDirection.LeftToRight,
        Margin = Padding.Empty,
        Padding = Padding.Empty,
        BackColor = Color.Transparent,
        Tag = AppTheme.KeepBackColor,
    };

    private readonly Label _lblSwearsCaption = new() { AutoSize = true, Anchor = AnchorStyles.Left };

    private readonly ToolTip _swearTips = new();

    /// <summary>Whether the dial's row is meant to be on show - for tests, since a page not on screen reads every control as hidden.</summary>
    internal bool SwearDialShown { get; private set; }
    private readonly NumericUpDown _numMaxWords = new();
    private readonly NumericUpDown _numVariety = new();
    private readonly NumericUpDown _numSummaryDelay = new();
    private readonly CheckBox _chkTeaser = new ThemedCheckBox();
    private readonly TextBox _txtTeaser = new();
    private readonly NumericUpDown _numTeaserGap = new();

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

    // The names and the voices - the announcer's name, the recap host, the
    // co-host, the mechanic and the badges reader - moved to Voice > Cast on
    // 17 Oct 2026 (CastControl), with the recap's voice delivery.

    // --- The commercial break (10 Oct 2026) ---
    //
    // One switch and no timings: how long the break runs is however long
    // the model took, which is the whole point of it. See
    // AppSettings.RecapAdBreak for why there is nothing else here and why
    // it is not on the Sponsors page.
    private readonly CheckBox _chkAdBreak = new ThemedCheckBox();

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>The page at a glance, over its cards (17 Nov 2026) - see ShowTiles.</summary>
    private readonly StatTile[] _tiles = { new(), new(), new(), new() };
    private static readonly string[] TileCaptions = { "AI commentary", "Written by", "Model on this PC", "Writing tone" };

    /// <summary>The pickers' widths as last measured - see Pieces.PickerWidth.</summary>
    private (Font Font, int Count, int Width)? _toneWidth;
    private (Font Font, int Count, int Width)? _chatterWidth;

    // Club notes, the room's vote and listening along - and the three
    // events that told the relay when one was switched off - moved to the
    // Club Website page on 17 Oct 2026 (RoomAndWebCard).

    /// <summary>The usual caption of the button that says so while it works - see BuildLayout's Steady.</summary>
    private const string TestCaption = "Test Connection";

    /// <summary>The boxes' and pickers' widths at the normal text size - see SizeFields.</summary>
    private const int TonePickerWidth = 220;

    /// <summary>The Sailor-mouth dial's width at the normal text size - seven stops, room to land on each.</summary>
    private const int SwearDialWidth = 140;
    private const int ShowNameWidth = 280;

    /// <summary>The least width of the How chatty and Pacing pickers at the normal text size - it was the show music's library drop-downs' width, which moved to Sounds > Imaging on 17 Oct 2026.</summary>
    private const int ShowPieceWidth = 220;

    private const int TeaserWidth = 420;

    /// <summary>The teasers the box shows before it scrolls, a line each.</summary>
    private const int TeaserLines = 4;

    /// <summary>
    /// The club's "Max spoken announcements per lap" from Race Calls
    /// (17 Oct 2026), so the How chatty line counts from the club's own
    /// figure rather than a default profile's. Unset, it reads the default.
    /// </summary>
    internal Func<int>? MaxCallsPerLap { get; set; }

    /// <summary>
    /// Opens the This Month window (MainForm.ShowThisMonth), from the
    /// Details... under this month's relay lines. The window is the host's,
    /// one at a time, so the page asks for it rather than making one.
    /// </summary>
    internal Action? OpenThisMonth { get; set; }

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

    /// <summary>Tonight's next race for Hear the gap (Between races) - null, or one with no race, uses sample names.</summary>
    internal Func<GapFacts?>? TonightsGap { get; set; }

    /// <summary>Speaks Hear the gap's lines back to back, through the announcer.</summary>
    public Action<IReadOnlyList<string>>? PlayGap { get; set; }

    /// <summary>Whether the Sponsors page puts reads on rotation between races - for Hear the gap.</summary>
    public Func<bool>? SponsorReadsBetweenRaces { get; set; }

    public CommentaryControl(AppSettings settings, CommentaryService commentary)
    {
        _settings = settings;
        _commentary = commentary;

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
    ///   How it's written   how every line is WORDED - first, since it covers
    ///                      everything below it (1 Nov 2026; it was the last
    ///                      card, "Writing style", until then).
    ///   Who writes it      the relay, the model on this PC, the month's lines,
    ///                      and Test Connection right under what it checks.
    ///   Before the start   race intros, driver introductions (with the
    ///                      qualifying recap), the throw to pit lane.
    ///   Through the night  how chatty, the pacing, how far down the segments
    ///                      go, talking points and the segments themselves.
    ///   After the flag     the pause, the unofficial result, the Technical
    ///                      Lead, the teasers and the commercial break.
    ///   Recap show         the recap, its opening, the booth interview, the
    ///                      sign-off and the show's name.
    ///   Between races      call-ups and what is said in the gap.
    ///
    /// In the order the night happens since 22 Oct 2026 (page tidy) - see
    /// the note above the cards in BuildLayout.
    ///
    /// Who SAYS it - the names and voices - is on Voice > Cast since 17 Oct
    /// 2026 (CastControl), and the club notes, the room's vote and listening
    /// along are on the Club Website page (RoomAndWebCard).
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

        // ONE LINE OF INTRO (22 Oct 2026, page tidy). There were two - this
        // and an orange requirement line - and the relay box under them had a
        // third saying much the same. What writes is now said once, by the
        // live line in the Who writes it card (UpdateRelayState).
        var lblIntro = new Label
        {
            Text = "Color commentary written by the club relay or by a small model built into HeatCaller. Nothing to "
                 + "install. When neither can write, the normal announcement lines are used.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        Check(_chkEnabled, "Enable AI commentary");
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
        // Who writes it
        //
        // In the same card as the model on this PC since 22 Oct 2026 (page
        // tidy). It sat above the Connection card with a hint of its own,
        // because that card was only about one of the two brains - which left
        // the choice of brain outside any card, and the page's first card
        // starting halfway through the answer.
        // ------------------------------------------------------------------

        Check(_chkUseRelay, "Write through the club relay when it can");
        _chkUseRelay.Checked = _settings.CommentaryUseRelay;
        _chkUseRelay.CheckedChanged += (_, _) =>
        {
            _settings.CommentaryUseRelay = _chkUseRelay.Checked;
            _settings.Save();
            ApplyToService();
            UpdateEnabledState();
        };

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
            "Works with the internet down, but its lines are plainer. Unticked, it still takes over when this "
            + "month's relay lines run out.");

        // FOR THE MODEL ON THIS PC (17 Oct 2026): this sets
        // BundledLlmClient.Timeout only - the relay keeps its own fixed wait
        // (RelayCommentaryClient.Timeout) - so it greys with the box above.
        Flush(connectionGrid.AddRow("Give up after:", Line(_numTimeout, Suffix("seconds (the model on this PC), then the normal line is used"))));

        // THIS MONTH'S RELAY LINES (19 Oct 2026), next to what writes when
        // they run out: the share used, how many nights like tonight are
        // left, and Ask for More... once it runs low (UsageLine). Only while
        // the relay is in use - a club writing everything on this PC has no
        // allowance to watch. Kept live from the store (UsageMeters).
        var usage = new UsageLine(_settings) { OpenDetails = () => OpenThisMonth?.Invoke() };
        var usageCaption = Flush(connectionGrid.AddRow("This month:", usage, stretch: true));
        usage.ShowingChanged += shown => usageCaption.Visible = shown;
        usage.Follow(MeterKind.Commentary, this, () => _chkUseRelay.Checked && new RelayCommentaryClient(_settings).RelayConfigured);
        _chkUseRelay.CheckedChanged += (_, _) => usage.RefreshNow();

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
            "Working - replied in 99.9s. That's slow for live lines - intros and recaps get longer and should still make it.",
        }.Concat(AllSpentTestResults()).ToArray();

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

        var connection = new Section("Who writes it");

        connection.Add(Hint("The club relay, the small model on this PC, or both - and what is used when neither can write."));
        connection.Add(_chkUseRelay, stretch: false, gapBelow: UiLayout.HintGap);
        connection.Add(_lblRelayState, gapBelow: UiLayout.RowGap);
        connection.Add(connectionGrid);
        connection.Add(testRow, gapBelow: 0);

        // ------------------------------------------------------------------
        // What it writes
        // ------------------------------------------------------------------

        Check(_chkIntros, "Race intros - a \"coming up\" line as each race goes to the grid");
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
        introsGrid.AddHint("Rivalries are set at the bottom of the Race Calls tab.");

        // THE DRIVER INTRODUCTIONS (18 Oct 2026) - a roll call of the grid,
        // written by the model from exact facts, after the race intro.
        Check(_chkDriverIntros, "Driver introductions - each driver on the grid, in order");
        _chkDriverIntros.Checked = _settings.CommentaryDriverIntros;
        _chkDriverIntros.CheckedChanged += (_, _) => { _settings.CommentaryDriverIntros = _chkDriverIntros.Checked; _settings.Save(); UpdateEnabledState(); };

        Check(_chkDriverIntrosMains, "Only for a main");
        _chkDriverIntrosMains.Checked = _settings.CommentaryDriverIntrosMainsOnly;
        _chkDriverIntrosMains.CheckedChanged += (_, _) =>
        {
            _settings.CommentaryDriverIntrosMainsOnly = _chkDriverIntrosMains.Checked;
            _settings.Save();
        };

        // The qualifying recap a step in under the driver introductions
        // (22 Oct 2026, page tidy): it is the opening of a main's
        // introductions and greys with them, so it is theirs, not a switch
        // of its own between them and pit lane.
        var driverIntrosGrid = new FieldGrid();

        Stepped(driverIntrosGrid.AddRow(string.Empty, _chkDriverIntrosMains));
        Stepped(driverIntrosGrid.AddRow(string.Empty, _chkQualifyingRecap));

        // THE BIG SCREEN'S CARDS (1 Nov 2026, asked for by Asher): each
        // driver's name and photo up on the Big Screen as the voice reaches
        // them. A step in under the introductions, because they ARE the
        // introductions on the wall and grey with them. A driver's own photo
        // is kept off it on their profile.
        Check(_chkIntroCards, "Show each driver's name and photo on the Big Screen as they're introduced");
        _chkIntroCards.Checked = _settings.BigScreenIntroCards;
        _chkIntroCards.CheckedChanged += (_, _) =>
        {
            _settings.BigScreenIntroCards = _chkIntroCards.Checked;
            _settings.Save();
        };

        Stepped(driverIntrosGrid.AddRow(string.Empty, _chkIntroCards));
        driverIntrosGrid.AddHint(
            "Grid spot, number, home club and a line of form for each driver. The automatic start waits for them. "
            + "To keep a young driver's photo off the Big Screen, tick that on their profile - their name still shows.");

        // THE PIT LANE AND THE QUALIFYING RECAP (18 Oct 2026) - ticks of their
        // own, on the Before the start card with the driver introductions,
        // in the order On Air says them (the qualifying recap a step in
        // under the introductions since 22 Oct 2026).
        Check(_chkPitLane, "Throw to pit lane - the co-host's word with a Team Principal");
        _chkPitLane.Checked = _settings.PrincipalBeforeRace;
        _chkPitLane.CheckedChanged += (_, _) =>
        {
            if (_settings.PrincipalBeforeRace != _chkPitLane.Checked)
            {
                _settings.PrincipalBeforeRace = _chkPitLane.Checked;
                _settings.Save();
            }

            UpdateEnabledState();
        };

        Check(_chkFlagResult, "Unofficial result - who won, who they beat and the fastest lap");
        _chkFlagResult.Checked = _settings.FlagResultRead;
        _chkFlagResult.CheckedChanged += (_, _) =>
        {
            _settings.FlagResultRead = _chkFlagResult.Checked;
            _settings.Save();
        };

        Check(_chkFlagTechLead, "Throw to pit lane - a word with the winner's Technical Lead");
        _chkFlagTechLead.Checked = _settings.RecapMechanicInterview;
        _chkFlagTechLead.CheckedChanged += (_, _) =>
        {
            if (_settings.RecapMechanicInterview != _chkFlagTechLead.Checked)
            {
                _settings.RecapMechanicInterview = _chkFlagTechLead.Checked;
                _settings.Save();
            }
        };

        Check(_chkBoothPrincipal, "Booth interview - the winner's Team Principal on the recap show");
        _chkBoothPrincipal.Checked = _settings.PrincipalAfterRace;
        _chkBoothPrincipal.CheckedChanged += (_, _) =>
        {
            if (_settings.PrincipalAfterRace != _chkBoothPrincipal.Checked)
            {
                _settings.PrincipalAfterRace = _chkBoothPrincipal.Checked;
                _settings.Save();
            }
        };

        Check(_chkQualifyingRecap, "Before a main, open with what qualifying decided");
        _chkQualifyingRecap.Checked = _settings.CommentaryQualifyingRecap;
        _chkQualifyingRecap.CheckedChanged += (_, _) =>
        {
            _settings.CommentaryQualifyingRecap = _chkQualifyingRecap.Checked;
            _settings.Save();
        };

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
        summariesGrid.AddHint("The fastest lap is already called live, so leave this off if it feels repetitive.");

        Check(_chkTalkingPoints, "Talking points - suggested lines on the Announcer screen, never spoken");
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

        Stepped(talkingGrid.AddRow("Refresh every:", Line(_numInterval, Suffix("seconds"))));
        Stepped(talkingGrid.AddRow("Consider up to:", Line(_numObservations, Suffix("talking points at a time"))));

        // The "What it writes" card is gone (22 Oct 2026, page tidy): its
        // intros went to Before the start, its recaps to Recap show and its
        // talking points to Through the night - each beside the rest of
        // what the room hears at that point.

        // ------------------------------------------------------------------
        // How it's written - the WORDS (called "Writing style", and the last
        // card on the page, until 1 Nov 2026)
        // ------------------------------------------------------------------

        _cmbTone.DropDownStyle = ComboBoxStyle.DropDownList;
        FillTones();
        _cmbTone.SelectedIndexChanged += (_, _) =>
        {
            if (_fillingTones || _cmbTone.SelectedItem is not ToneItem item)
            {
                return;
            }

            // Sailor-mouth (15 Nov 2026) swears, so it is asked about
            // every time it is picked rather than switched to by a slip of
            // the arrow keys. No puts the dropdown back where it was.
            if (item.Tone == CommentaryTone.SailorMouth
                && _settings.CommentaryToneStyle != CommentaryTone.SailorMouth
                && AppDialog.Show(this,
                    "Sailor-mouth swears and is crude - out loud, on the speakers, and in the recap show's "
                    + "recording. Screens, phones and the overlay show the swears starred, and what gets posted - "
                    + "a driver's night, the season piece, the night before - stays clean. It never says anything "
                    + "racist, antisemitic or hateful.\n\nUse it only where everyone in the room is fine with that. "
                    + "Switch to Sailor-mouth?",
                    "Sailor-mouth commentary", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                SelectTone(CommentaryTones.Effective(_settings));
                return;
            }

            _settings.CommentaryToneStyle = item.Tone;
            _settings.Save();

            // The tag box lives or dies by this - see UpdateEnabledState.
            UpdateEnabledState();
            ShowSwearDial();
        };

        // The Sailor-mouth dial (15 Nov 2026, Asher: "I want to control how
        // many"): swear words a line, right under the tone, there only while
        // Sailor-mouth is picked.
        _sldSwears.Value = CommentaryTones.SwearsPerLine(_settings);
        _lblSwearsHost.Controls.Add(_lblSwears);
        _lblSwears.Text = SwearsWords(_sldSwears.Value);
        _sldSwears.ValueChanged += (_, _) =>
        {
            _lblSwears.Text = SwearsWords(_sldSwears.Value);

            if (_settings.SailorMouthSwears == _sldSwears.Value)
            {
                return;
            }

            // Kept at once - the next line written uses it - but SAVED once
            // the dial settles: a save is a fifth of a second, and saving at
            // every stop made dragging the dial stutter (15 Nov 2026).
            _settings.SailorMouthSwears = _sldSwears.Value;
            _swearSavePending = true;
            _swearSave.Stop();
            _swearSave.Start();
        };

        _swearSave.Tick += (_, _) => SaveSwearsNow();
        VisibleChanged += (_, _) =>
        {
            if (!Visible)
            {
                SaveSwearsNow();
            }
        };
        Disposed += (_, _) =>
        {
            SaveSwearsNow();
            _swearSave.Dispose();
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

        Check(_chkVoiceTags, "Give the lines their tone's delivery when they're spoken");
        _chkVoiceTags.Checked = _settings.CommentaryVoiceTags;
        _chkVoiceTags.CheckedChanged += (_, _) =>
        {
            _settings.CommentaryVoiceTags = _chkVoiceTags.Checked;
            _settings.Save();
            UpdateEnabledState();
        };

        // The dial on the tone's own line, in room that is always kept for
        // it (_swearHost) - see ShowSwearDial for why it is not a row.
        _lblSwearsCaption.Text = "Swearing:";
        _swearInner.Controls.Add(_lblSwearsCaption);
        _swearInner.Controls.Add(_sldSwears);
        _swearInner.Controls.Add(_lblSwearsHost);
        _swearHost.Controls.Add(_swearInner);

        const string swearHelp = "How many swear words Sailor-mouth may use in a line - longer pieces get this many "
                               + "for every forty words. Lines over it have the spare ones taken out.";
        _swearTips.SetToolTip(_sldSwears, swearHelp);
        _swearTips.SetToolTip(_lblSwearsCaption, swearHelp);
        _swearTips.SetToolTip(_lblSwears, swearHelp);
        _sldSwears.AccessibleName = "Sailor-mouth swear words a line";
        _sldSwears.AccessibleDescription = swearHelp;
        Disposed += (_, _) => _swearTips.Dispose();

        Flush(styleGrid.AddRow("Writing tone:", Line(_cmbTone, _swearHost)));
        ShowSwearDial();
        Flush(styleGrid.AddRow(string.Empty, _chkVoiceTags));
        styleGrid.AddHint("Needs an ElevenLabs v3 voice, and a tone with a delivery of its own - not Neutral or Dry.");

        // Each figure's meaning beside it (22 Oct 2026, page tidy), rather
        // than in a paragraph under the card explaining both.
        Flush(styleGrid.AddRow("Max length:", Line(_numMaxWords, Suffix("words - 30 is about a sentence, 70 is two"))));
        Flush(styleGrid.AddRow("Phrasing variety:", Line(_numVariety, Suffix("low is steady, high is livelier but now and then odd"))));

        var style = new Section("How it's written");

        style.Add(Hint("How every line is worded. How it sounds is set on the Cast tab."));
        style.Add(styleGrid, gapBelow: 0);

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

        var pauseGrid = new FieldGrid();

        Flush(pauseGrid.AddRow("Pause after the flag:", Line(_numSummaryDelay, Suffix("seconds, so the finish call and the cheering land"))));

        // The teasers' box under the switch that turns them on, a step in,
        // and the wait after them under the box - "teasers before the recap:
        // [the lines], then wait: [n] seconds". The box used to sit beside
        // the switch, at an x worked out from the width of its caption.
        var teaserGrid = new FieldGrid();

        teaserGrid.AddField(_txtTeaser);
        teaserGrid.AddHint("The AI writes the line itself each race and uses these for the feel.");
        Stepped(teaserGrid.AddRow("then wait:", Line(_numTeaserGap, Suffix("seconds - the commercial break plays in this wait"))));

        // Recap voice delivery - the recap's own stability and style - moved
        // to the Cast tab's Recap host card on 17 Oct 2026, beside the voice
        // it tunes (CastControl).

        // The recap's deeper duck of the music and crowd moved to the Mixer
        // on 17 Oct 2026, with every other duck - the same settings
        // (SummaryDeepDuck, SummaryDuckPercent).

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

        var showGrid = new FieldGrid();

        Flush(showGrid.AddRow("Show name:", Line(_txtShowName, Suffix("said in the openings and sign-offs - blank for none"))));

        Check(_chkAdBreak, "Play the sponsors as a commercial break while the recap is being written");
        _chkAdBreak.Checked = _settings.RecapAdBreak;
        _chkAdBreak.CheckedChanged += (_, _) =>
        {
            _settings.RecapAdBreak = _chkAdBreak.Checked;
            _settings.Save();
            UpdateEnabledState();
        };

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
        //
        // And (17 Oct 2026) the sign-off only happens when "End with a
        // TV-style sign-off" is ticked, and the bumper and jingle from
        // Sounds > Imaging now sit either side of the break.
        //
        // Round 3 text clean-up (17 Oct 2026): the running order is now
        // explained in ONE place, Sounds > Imaging > Run of show, and this
        // card points there - keep that one the real one.
        //
        // The Broadcast and Race recap cards are After the flag and Recap
        // show since 22 Oct 2026 (page tidy) - assembled below with the rest
        // of the page, in the order the room hears them.

        // The mechanic's interview and the badges reader - their switches,
        // names and voices - moved to the Cast tab on 17 Oct 2026, with the
        // announcer's and the recap host's names (CastControl).

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
        // choice is selected, for the reason ShowTreatmentCard.FillLibrary gives.
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
            DescribeChatter();
        };

        DescribeChatter();

        // ONE DIAL (3 Nov 2026). This card had three: How chatty, Pacing and
        // Segments run, and a club that turned all three up still heard a
        // quiet night - the segments' own limits and gaps answered to none
        // of them, Pacing only ever spaced the built-in timing calls, and
        // Segments run said again what the tick boxes below already say.
        // How chatty now moves every limit (see ShowChatter), Pacing lives
        // on the Voice tab beside the per-lap cap it belongs with, and
        // Segments run was folded into the ticks (AppSettings.FoldLowestTier).
        var chatterGrid = new FieldGrid();

        Flush(chatterGrid.AddRow("How chatty:", Line(_cmbChatter)));
        chatterGrid.AddHint(_lblChatter);
        chatterGrid.AddHint(Hint(
            "The spacing of the built-in timing calls (\"Top spot to Al Gorithim!\") is Pacing, on the Voice tab."));

        // ------------------------------------------------------------------
        // THE CARDS IN THE ORDER THE NIGHT HAPPENS (22 Oct 2026, page tidy).
        //
        // The page was six cards in the order they were built - Connection,
        // What it writes, Segments, Writing style, Race recap, Broadcast -
        // with the pre-race and after-the-flag ticks inside Segments, the
        // recap's switch three cards away from the recap's settings, the
        // throw to pit lane listed before the driver introductions it now
        // follows (On Air: opening, introductions, pit lane), and a gray
        // line under nearly every box, several of them saying the same
        // thing twice. Now: who writes it, then the night in order - before
        // the start, through the night, after the flag, the recap show -
        // then the wording, which covers all of them. Every setting, switch
        // and rule is the same; only where it sits and what it says changed.
        //
        // Since 1 Nov 2026 the wording comes FIRST, as "How it's written",
        // and Between races (31 Oct 2026) is the last card.
        // ------------------------------------------------------------------

        // A tick and its one line of explanation, as every card below has them.
        void Ticked(Section card, CheckBox box, string? hint, bool lastInCard = false)
        {
            if (hint is null)
            {
                card.Add(box, stretch: false, gapBelow: lastInCard ? 0 : UiLayout.HintGap);
                return;
            }

            card.Add(box, stretch: false, gapBelow: 0);
            card.Add(Hint(hint), gapBelow: lastInCard ? 0 : UiLayout.HintGap);
        }

        // A bold group heading inside a card, with its line.
        void Group(Section card, string heading, string hint)
        {
            card.Add(
                _textSize.Wear(new Label { Text = heading, AutoSize = true, ForeColor = AppTheme.TextDark }, () => AppTheme.BoldFont),
                stretch: false,
                gapBelow: 0);
            card.Add(Hint(hint), gapBelow: UiLayout.HintGap);
        }

        // --- Before the start: what On Air says between the show open and staging, in that order.
        var before = new Section("Before the start");

        before.Add(Hint("In the order the room hears them, after the show open and before staging."));
        before.Add(_chkIntros, stretch: false, gapBelow: 0);
        before.Add(introsGrid, gapBelow: UiLayout.HintGap);
        before.Add(_chkDriverIntros, stretch: false, gapBelow: 0);
        before.Add(driverIntrosGrid, gapBelow: UiLayout.HintGap);
        Ticked(before, _chkPitLane,
            "Two questions with the Team Principal of the driver with most riding on it. Who, and which races, are on the Cast tab.",
            lastInCard: true);

        // --- Through the night: how much is said, and the segments.
        var segments = new Section("Through the night");

        // WHY (25 Nov 2026, Asher: "fix the app text too"): the hint said how the
        // night's commentary is rationed (lateness, saving it for the mains),
        // which the patent applications claim. It now says what a club gets.
        segments.Add(Hint("Each segment speaks only when the race gives it something to say."));
        segments.Add(chatterGrid, gapBelow: UiLayout.SectionGap);

        segments.Add(_chkTalkingPoints, stretch: false, gapBelow: 0);
        segments.Add(talkingGrid, gapBelow: UiLayout.SectionGap);

        var switchedOff = new HashSet<SegmentKind>(CommentaryBudget.ParseSwitches(_settings.CommentarySegmentsOff));

        foreach (var tier in new[] { SegmentTier.Core, SegmentTier.Colour, SegmentTier.Flourish })
        {
            var inTier = CommentaryBudget.AllSpecs.Where(spec => spec.Tier == tier && !NotOffered.Contains(spec.Kind)).ToList();

            if (inTier.Count == 0)
            {
                continue;
            }

            Group(segments, TierHeading(tier), TierWhen(tier));

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
                segments.Add(Hint(spec.What + (spec.CanBeExchange ? " Can be a two-hander (Cast tab)." : string.Empty)),
                    gapBelow: last ? UiLayout.SectionGap : UiLayout.HintGap);
            }
        }

        // --- After the flag: the race announcer, from the finish call to the break.
        var after = new Section("After the flag");

        after.Add(Hint(
            "In the order the room hears them. Everything after the result needs race recaps on (Recap show, below). "
            + "The full running order is on Sounds > Imaging > Run of show."));
        after.Add(pauseGrid, gapBelow: UiLayout.HintGap);
        Ticked(after, _chkFlagResult, "Fixed words - no AI needed.");
        Ticked(after, _chkFlagTechLead,
            // Heats too since 21 Oct (22 Oct 2026, bug hunt).
            "After a main (and after heats, when ticked on the Cast tab): the co-host asks the winner's Technical Lead two questions, then hands back.");
        after.Add(_chkTeaser, stretch: false, gapBelow: 0);
        after.Add(teaserGrid, gapBelow: UiLayout.HintGap);
        Ticked(after, _chkAdBreak,
            "Sponsor slides fill the Big Screen while the recap is written, when one is open with slides on. Paid "
            + "sponsor reads (Sponsors page) need AI commentary on too.",
            lastInCard: true);

        // --- Recap show: the recap and what dresses it.
        var recap = new Section("Recap show");

        recap.Add(Hint("The recap after the race, and the opening and sign-off around it."));
        recap.Add(_chkSummaries, stretch: false, gapBelow: 0);
        recap.Add(summariesGrid, gapBelow: UiLayout.HintGap);
        Ticked(recap, _chkOpening, null);
        Ticked(recap, _chkBoothPrincipal, "The recap host asks two questions on what the win means. Who, and which races, are on the Cast tab.");
        Ticked(recap, _chkClosing, null);
        recap.Add(showGrid, gapBelow: 0);

        // The show's music and Recording the night were the last two cards
        // here until 17 Oct 2026. They are on Sounds now - the show's music
        // as Imaging's Show treatment card (ShowTreatmentCard) and the
        // recording as its own Recording tab (RecordingControl) - with the
        // same settings and the same rules.

        // The co-host card moved to the Cast tab, and Club notes, Listening
        // along and The room's vote to the Club Website page's Room & Web
        // card, on 17 Oct 2026 (CastControl, RoomAndWebCard).

        foreach (var label in new[] { _lblChatter })
        {
            label.AutoSize = true;
            label.ForeColor = AppTheme.TextMuted;
            _textSize.Wear(label, () => AppTheme.SmallFont);
        }

        // ------------------------------------------------------------------
        // The page
        // ------------------------------------------------------------------

        var page = new PageStack();

        // THE TILES (17 Nov 2026, page jazz-up, as Venues'): on or off, what
        // writes, whether the model on this PC is here, and the tone - kept
        // in step by ShowTiles. Then the intro and the master switch in a
        // card of their own, where they sat loose on the page.
        VoicePagePieces.Dress(_textSize, _tiles, TileCaptions);
        page.Add(new TileStrip(_tiles));

        var master = new Section("On air");

        master.Add(lblIntro, gapBelow: UiLayout.RowGap);
        master.Add(_chkEnabled, stretch: false, gapBelow: 0);

        page.Add(master);

        // Who writes it first (1 Nov 2026, asked for by Asher: "move who
        // writes it card to the top"), then How it's written, whose wording
        // covers every card below.
        //
        // Side by side on a wide page (17 Nov 2026), in pairs that read in
        // the same order one under another on a narrow one: who writes and
        // how, then after the flag and the recap show that follows it.
        int WideFrom() => UiLayout.AtTextSize(1200);

        var setUp = new CardColumns { TwoColumnsFrom = WideFrom };
        setUp.AddLeft(connection);
        setUp.AddRight(style);

        var afterTheFlag = new CardColumns { TwoColumnsFrom = WideFrom };
        afterTheFlag.AddLeft(after);
        afterTheFlag.AddRight(recap);

        page.Add(setUp);
        page.Add(before);
        page.Add(segments);
        page.Add(afterTheFlag);

        // --- Between races (31 Oct 2026): the gap after the recap and before
        // the next race is called - see BetweenRacesCard.
        var (betweenRaces, betweenGrids) = BetweenRacesCard.Build(_settings, _textSize,
            () => TonightsGap?.Invoke(), lines => PlayGap?.Invoke(lines), () => _settings.CommentaryEnabled,
            () => SponsorReadsBetweenRaces?.Invoke() ?? false);
        page.Add(betweenRaces, gapBelow: 0);

        Controls.Add(page);

        // The tone's tile follows the picker (the rest follow UpdateRelayState).
        _cmbTone.SelectedIndexChanged += (_, _) => UpdateRelayState();

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
            connectionGrid, introsGrid, driverIntrosGrid, summariesGrid, talkingGrid, styleGrid,
            pauseGrid, teaserGrid, showGrid, chatterGrid,
        }.Concat(betweenGrids).ToArray()));

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

        // Worked out again whenever the page is shown: what it says depends
        // on other pages.
        VisibleChanged += (_, _) =>
        {
            if (!Visible)
            {
                return;
            }

            // The Club Website page can be filled in after this one, and
            // what the relay line says depends on it.
            UpdateRelayState();

            // Sailor-mouth comes and goes with a club rule on Setup (15 Nov 2026).
            FillTones();

            // And Race Calls' max calls per lap and the Cast tab's co-host,
            // which the How chatty line counts from, and the Voice tab's
            // voice, which decides whether the excited marks can be read at
            // all (17 Oct 2026).
            DescribeChatter();

            // The pit lane tick is the Cast tab's switch too, so it is read
            // again in case it was changed there.
            if (_chkPitLane.Checked != _settings.PrincipalBeforeRace)
            {
                _chkPitLane.Checked = _settings.PrincipalBeforeRace;
            }

            if (_chkFlagTechLead.Checked != _settings.RecapMechanicInterview)
            {
                _chkFlagTechLead.Checked = _settings.RecapMechanicInterview;
            }

            if (_chkBoothPrincipal.Checked != _settings.PrincipalAfterRace)
            {
                _chkBoothPrincipal.Checked = _settings.PrincipalAfterRace;
            }

            UpdateEnabledState();
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
    /// Run again when the text size changes (TextSize.Keep).
    /// </summary>
    private void SizeFields()
    {
        Fit(_txtShowName, UiLayout.AtTextSize(ShowNameWidth));
        Fit(_txtTeaser, UiLayout.AtTextSize(TeaserWidth), Pieces.LineHeight(_txtTeaser.Font) * TeaserLines + UiLayout.HintGap * 2);

        Fit(_cmbTone, Math.Max(UiLayout.AtTextSize(TonePickerWidth), Pieces.PickerWidth(_cmbTone, ref _toneWidth)));
        Fit(_sldSwears, UiLayout.AtTextSize(SwearDialWidth), Math.Max(UiLayout.AtTextSize(24), _cmbTone.Height));
        Fit(_lblSwearsHost, Enumerable.Range(0, CommentaryTones.MostSwears + 1).Max(n => TextRenderer.MeasureText(SwearsWords(n), _lblSwears.Font).Width) + UiLayout.LabelGap, _sldSwears.Height);

        // The room beside the tone picker: a gap, the caption, the dial and
        // its widest number - the same whichever tone is picked.
        var swearsGap = UiLayout.LabelGap * 2;

        if (_swearInner.Padding.Left != swearsGap)
        {
            _swearInner.Padding = new Padding(swearsGap, 0, 0, 0);
        }

        Fit(_swearHost,
            swearsGap
            + _lblSwearsCaption.PreferredWidth + _lblSwearsCaption.Margin.Horizontal
            + _sldSwears.Width + _sldSwears.Margin.Horizontal
            + _lblSwearsHost.Width + _lblSwearsHost.Margin.Horizontal,
            Math.Max(_sldSwears.Height + _sldSwears.Margin.Vertical, _cmbTone.Height));
        Fit(_cmbChatter, Math.Max(UiLayout.AtTextSize(ShowPieceWidth), Pieces.PickerWidth(_cmbChatter, ref _chatterWidth)));

        foreach (var number in new[]
                 {
                     _numTimeout, _numFinishers, _numInterval, _numObservations, _numMaxWords, _numVariety,
                     _numSummaryDelay, _numTeaserGap,
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
    /// The line under How chatty, from the club's own max calls per lap and
    /// "Two voices in the heats too" (17 Oct 2026) - see ShowChatter.Describe.
    /// </summary>
    private void DescribeChatter() =>
        _lblChatter.Text = ShowChatter.Describe(
            _settings.CommentaryChatter,
            MaxCallsPerLap?.Invoke(),
            _settings.CommentaryTwoVoicesInHeats && _settings.CommentaryCoHost);

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

    /// <summary>Set while the tone list is being filled or a choice put back, so neither saves anything.</summary>
    private bool _fillingTones;

    /// <summary>
    /// The tones the dropdown offers, the club's choice selected. Filled
    /// again whenever the page is shown, because Sailor-mouth comes and goes
    /// with a club rule on Setup (AppSettings.AllowSailorMouth, 15 Nov 2026).
    /// A saved Sailor-mouth the club has since switched off shows as
    /// Enthusiastic, which is what it plays as - the saved choice itself is
    /// left alone, so switching the rule back on brings it back.
    /// </summary>
    internal void FillTones()
    {
        var toneItems = new[]
        {
            new ToneItem(CommentaryTone.Enthusiastic, "Enthusiastic - trackside hype"),
            new ToneItem(CommentaryTone.Neutral, "Neutral - calm play-by-play"),
            new ToneItem(CommentaryTone.DryUnderstated, "Dry - understated, deadpan"),
        }
        // The character tones (8 Nov 2026), labelled with their group -
        // "Dark: Villain - ..." - since a dropdown list has no headings.
        .Concat(CommentaryTones.All
            .Where(tone => tone.Tone != CommentaryTone.SailorMouth || _settings.AllowSailorMouth)
            .Select(tone => new ToneItem(tone.Tone, tone.DisplayLabel)))
        .ToArray();

        _fillingTones = true;

        try
        {
            _cmbTone.BeginUpdate();
            _cmbTone.Items.Clear();
            _cmbTone.Items.AddRange(toneItems);
            _cmbTone.EndUpdate();
        }
        finally
        {
            _fillingTones = false;
        }

        SelectTone(CommentaryTones.Effective(_settings));
        ShowSwearDial();
    }

    /// <summary>
    /// The Sailor-mouth dial shown only while Sailor-mouth is the tone that
    /// plays - not a saved one the club has switched off.
    ///
    /// BESIDE THE TONE PICKER, IN ROOM KEPT FOR IT (15 Nov 2026, Asher: "the
    /// dial is jerky when you flip it sailor"). It was a row of its own, and
    /// a row coming or going changes the card's height - which lays every
    /// card on the page out again, a third of a second to a second each
    /// time, the page jumping as it went. Now the room beside the picker is
    /// the same size whatever the tone, and only what is inside it is shown
    /// or hidden: nothing outside it moves, so the switch is instant.
    /// </summary>
    private void ShowSwearDial()
    {
        var shown = CommentaryTones.Effective(_settings) == CommentaryTone.SailorMouth;
        SwearDialShown = shown;

        // The card's own colour, so the room reads as part of it and the
        // slider - which paints its parent's colour - has one to paint.
        var card = _swearHost.Parent;

        while (card is not null && card.BackColor.A < 255)
        {
            card = card.Parent;
        }

        if (card is not null)
        {
            foreach (var part in new Control[] { _swearHost, _swearInner, _lblSwearsHost })
            {
                if (part.BackColor != card.BackColor)
                {
                    part.BackColor = card.BackColor;
                }
            }
        }

        if (_swearInner.Visible != shown)
        {
            _swearInner.Visible = shown;
        }
    }

    /// <summary>Saves the dial once it has settled - see the dial's ValueChanged.</summary>
    private readonly System.Windows.Forms.Timer _swearSave = new() { Interval = 400 };

    /// <summary>The dial has moved since it was last saved.</summary>
    private bool _swearSavePending;

    /// <summary>Saves the dial's number if it has moved since the last save.</summary>
    private void SaveSwearsNow()
    {
        _swearSave.Stop();

        if (!_swearSavePending)
        {
            return;
        }

        _swearSavePending = false;
        _settings.Save();
    }

    /// <summary>What the dial's number means, beside it.</summary>
    internal static string SwearsWords(int swears) => swears switch
    {
        <= 0 => "none, just crude",
        1 => "1 a line",
        _ => $"up to {swears} a line",
    };

    /// <summary>Selects <paramref name="tone"/> without saving it (or the first tone, if it is not offered).</summary>
    private void SelectTone(CommentaryTone tone)
    {
        _fillingTones = true;

        try
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
        finally
        {
            _fillingTones = false;
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
        // The recording card and the show's music moved to Sounds on
        // 17 Oct 2026 (RecordingControl, ShowTreatmentCard), and read the
        // master switch from the settings when they are shown.
        var on = _chkEnabled.Checked;
        _chkUseRelay.Enabled = on;
        _chkLocalModel.Enabled = on;
        // With the relay on, the model on this PC can write even unticked -
        // when the month's relay lines run out - and this is its wait.
        _numTimeout.Enabled = on && (_chkLocalModel.Checked || _chkUseRelay.Checked);

        // The tag pass does nothing unless the tone is Enthusiastic, so the
        // box says so by going grey rather than by quietly doing nothing.
        //
        // NOR WITHOUT A VOICE THAT READS TAGS (17 Oct 2026) - the same test
        // CommentaryService.Tagged makes. Unknown (nothing wired)
        // reads as supported, so the box is never greyed on a guess.
        var voiceReadsTags = _commentary.VoiceSupportsAudioTagsProvider?.Invoke() ?? true;
        _chkVoiceTags.Enabled = on && voiceReadsTags && CommentaryTones.ReadsTags(CommentaryTones.Effective(_settings));

        _chkIntros.Enabled = on;
        _chkIntrosSelective.Enabled = on && _chkIntros.Checked;
        _chkDriverIntros.Enabled = on;
        _chkDriverIntrosMains.Enabled = on && _chkDriverIntros.Checked;
        _chkQualifyingRecap.Enabled = on && _chkDriverIntros.Checked;
        _chkIntroCards.Enabled = on && _chkDriverIntros.Checked;
        _chkPitLane.Enabled = on;
        _chkFlagTechLead.Enabled = on && _chkSummaries.Checked;
        _chkBoothPrincipal.Enabled = on && _chkSummaries.Checked;
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

        // NOT tied to the opening and closing ticks any more (17 Oct 2026):
        // the end-of-night sign-off names the show whatever they say
        // (CommentaryService.NightRecapSignOff), so a club with both
        // unticked still needs somewhere to type it.
        _txtShowName.Enabled = summariesOn;

        // The announcer's name, the mechanic, the badges reader, the recap
        // host and the co-host grey on the Cast tab now, by the same rules
        // (CastControl.RefreshEnabledState).

        // The break fills the time the RECAP takes to write, so there is
        // nothing for it to fill when recaps are off - it goes dark with
        // the rest of the broadcast.
        _chkAdBreak.Enabled = summariesOn;

        var talkingOn = on && _chkTalkingPoints.Checked;
        _numInterval.Enabled = talkingOn;
        _numObservations.Enabled = talkingOn;

        // THE RACE RECAP CARD GOES DARK WITH RECAPS (17 Oct 2026). These
        // were gated on the master switch, but every one of them is about
        // the recap: the pause before it, and the teaser - which now only
        // plays when a recap will follow it. (Its duck is on the Mixer now,
        // and its voice delivery on the Cast tab, and both grey there by the
        // same rule.)
        _numSummaryDelay.Enabled = summariesOn;
        _chkTeaser.Enabled = summariesOn;
        _txtTeaser.Enabled = summariesOn && _chkTeaser.Checked;
        _numTeaserGap.Enabled = summariesOn && _chkTeaser.Checked;

        _cmbTone.Enabled = on;
        _sldSwears.Enabled = on;
        _numMaxWords.Enabled = on;
        _numVariety.Enabled = on;

        foreach (var box in _segmentBoxes.Values)
        {
            box.Enabled = on;
        }

        // NOT GATED ON COMMENTARY (17 Oct 2026). How chatty also scales the
        // announcer's own lap calls (AnnouncementService reads
        // CommentaryChatter whether or not the AI is on), so greying it with
        // the master switch hid a control that was still doing something.
        _cmbChatter.Enabled = true;

        UpdateRelayState();
    }

    /// <summary>
    /// Whether any segment that can be a two-hander with the co-host is
    /// switched on - what the Cast tab's co-host card greys out by
    /// (17 Oct 2026; it was this page's own rule while the co-host card was
    /// here).
    ///
    /// Read from the SAVED switches rather than this page's boxes, which is
    /// the same answer: every box writes the switches as it is ticked
    /// (SaveSegmentSwitches). The boxes are the offered segments of the three
    /// tiers this page lists, so the hidden ones (NotOffered) - the
    /// mechanic's interview, which nothing writes - never keep the co-host
    /// lit, exactly as they did not here.
    /// </summary>
    internal static bool AnyTwoHanderSwitchedOn(AppSettings settings)
    {
        var off = new HashSet<SegmentKind>(CommentaryBudget.ParseSwitches(settings.CommentarySegmentsOff));

        return CommentaryBudget.AllSpecs.Any(spec =>
            (spec.Tier == SegmentTier.Core || spec.Tier == SegmentTier.Colour || spec.Tier == SegmentTier.Flourish)
            && spec.CanBeExchange
            && !NotOffered.Contains(spec.Kind)
            && !off.Contains(spec.Kind));
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

        // WHY (23 Nov 2026, bug hunt 24): the page used to work from the tick
        // box and whether a model FILE was on the PC, never from whether the
        // model had switched itself off. On a PC too slow for it, or one it
        // cannot run on, the page went on saying "Everything is written by the
        // built-in model below" beside a green "Ready" tile while nothing was
        // written all night. It now asks the model itself.
        var local = CurrentLocalModel();

        _lblRelayState.Text = RelayStateText(useRelay, ready, useLocal, local);

        // Amber whenever nothing would be written, which is the one state
        // worth interrupting someone over: commentary is ticked on and the
        // page is quietly telling them it will never say a word.
        var willWrite = ready || (useLocal && local == LocalModel.Ready);

        _lblRelayState.ForeColor = willWrite ? AppTheme.TextMuted : AppTheme.Accent;

        // The tiles over the page say the same (17 Nov 2026).
        ShowTiles(ready, local);
    }

    /// <summary>
    /// Where the model on this PC stands (23 Nov 2026, bug hunt 24): no
    /// file, switched off as too slow, switched off as unable to run here,
    /// or ready. Pure, so the page's wording can be tested without a form.
    /// </summary>
    internal enum LocalModel
    {
        Ready,
        NotHere,
        TooSlow,
        CannotRun,
    }

    /// <summary>
    /// The rule behind <see cref="LocalModel"/>. No file wins - a missing file
    /// is also reported as "unavailable" by the model, and "not installed" is
    /// the plainer thing to say. Unknown (not tried yet) counts as ready.
    /// </summary>
    internal static LocalModel LocalModelState(bool modelFileHere, bool tooSlow, string? unavailable) =>
        !modelFileHere ? LocalModel.NotHere
        : unavailable is not null ? LocalModel.CannotRun
        : tooSlow ? LocalModel.TooSlow
        : LocalModel.Ready;

    /// <summary>The model on this PC as it is now: the file, and what the built model has found out about this PC.</summary>
    private LocalModel CurrentLocalModel()
    {
        var bundled = _localBrain as BundledLlmClient;

        return LocalModelState(
            BundledLlmClient.IsModelAvailableWith(_settings.CommentaryLocalModelPath),
            bundled?.TooSlow ?? false,
            bundled?.Unavailable);
    }

    /// <summary>
    /// The tiles over the page (17 Nov 2026, page jazz-up, as Venues'):
    /// whether commentary is on, what writes it, whether the model on this
    /// PC is here, and the writing tone - from the page as it is now.
    /// <paramref name="relayReady"/> is UpdateRelayState's answer, asked once.
    /// </summary>
    private void ShowTiles(bool relayReady, LocalModel local)
    {
        if (IsDisposed)
        {
            return;
        }

        var on = _chkEnabled.Checked;

        VoicePagePieces.Show(_tiles[0], on ? "On" : "Off", TileCaptions[0], on ? VoicePagePieces.Tone.Good : VoicePagePieces.Tone.Muted);

        var useLocal = _settings.CommentaryUseLocalModel;
        var (writer, writerTone) = WriterTile(relayReady, useLocal, local);

        VoicePagePieces.Show(_tiles[1], writer, TileCaptions[1], writerTone);

        var (model, modelTone) = ModelTile(useLocal, local);
        VoicePagePieces.Show(_tiles[2], model, TileCaptions[2], modelTone);

        VoicePagePieces.Show(_tiles[3], VoicePagePieces.Short(VoicePagePieces.Picked(_cmbTone)), TileCaptions[3]);
    }

    /// <summary>
    /// The "Written by" tile. WHY (23 Nov 2026, bug hunt 24): it said "This PC"
    /// from the tick box alone, so a model that had switched itself off - or
    /// was never installed - was still named as the writer.
    /// </summary>
    internal static (string Text, VoicePagePieces.Tone Tone) WriterTile(bool relayReady, bool useLocal, LocalModel local)
    {
        var localWrites = useLocal && local == LocalModel.Ready;

        return (relayReady, localWrites) switch
        {
            (true, true) => ("Relay, then this PC", VoicePagePieces.Tone.Plain),
            (true, false) => ("Club relay", VoicePagePieces.Tone.Plain),
            (false, true) => ("This PC", VoicePagePieces.Tone.Plain),
            _ => ("Nothing", VoicePagePieces.Tone.Warn),
        };
    }

    /// <summary>
    /// The "Model on this PC" tile. WHY (23 Nov 2026, bug hunt 24): it said
    /// "Ready" in green whenever the file was there, even after the model had
    /// found this PC too slow or unable to run it and switched itself off.
    /// </summary>
    internal static (string Text, VoicePagePieces.Tone Tone) ModelTile(bool useLocal, LocalModel local)
    {
        var off = useLocal ? VoicePagePieces.Tone.Warn : VoicePagePieces.Tone.Muted;

        return local switch
        {
            LocalModel.Ready => ("Ready", VoicePagePieces.Tone.Good),
            LocalModel.TooSlow => ("Too slow", off),
            LocalModel.CannotRun => ("Can't run", off),
            _ => ("Not here", off),
        };
    }

    /// <summary>
    /// What the line under the relay box says. Pure, so the sizing list and
    /// the live text cannot drift apart - see AllRelayStates.
    /// </summary>
    internal static string RelayStateText(bool useRelay, bool ready, bool useLocal, LocalModel localModel)
    {
        // WHY (23 Nov 2026, bug hunt 24): with the box ticked this used to
        // name the built-in model as the writer whatever state it was in - no
        // file, too slow, or unable to run here - so the page promised lines
        // that were never written. Ticked but unable to write now says why,
        // and that nothing covers for it.
        if (useLocal && localModel != LocalModel.Ready)
        {
            var why = localModel switch
            {
                LocalModel.TooSlow => "the model on this PC has switched itself off as too slow here",
                LocalModel.CannotRun => "the model on this PC can't run here",
                _ => "no model is installed on this PC",
            };

            if (!useRelay)
            {
                return $"Nothing will be written - the relay is off and {why}.";
            }

            return ready
                ? $"The Club Website page is set up, so the relay writes - but {why}, so nothing covers the races it can't."
                : $"The Club Website page isn't set up yet, and {why}, so nothing will be written.";
        }

        var modelHere = localModel == LocalModel.Ready;
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

        if (local is not null)
        {
            return $"The Club Website page is set up, so the relay writes first and {local} below covers what it can't.";
        }

        // Unticked, the model on this PC still takes over when the month's
        // relay lines run out - see FallbackCommentaryBrain - so that is
        // said, when there is a model here to do it.
        return modelHere
            ? "The Club Website page is set up, so the relay writes - nothing covers the races it can't, though the "
              + "model on this PC takes over if the month's relay lines run out."
            : "The Club Website page is set up, so the relay writes - and nothing covers the races it can't.";
    }

    /// <summary>
    /// Every sentence RelayStateText can return, for the label to size to.
    /// Enumerated rather than typed out, so a reworded sentence cannot end
    /// up missing from the list and clipping on the night.
    /// </summary>
    internal static string[] AllRelayStates()
    {
        var all = new List<string>();

        foreach (var useLocal in new[] { true, false })
        {
            // Every state of the model on this PC (23 Nov 2026, bug hunt 24), not
            // just file-or-not, so the new "switched off" sentences size the label too.
            foreach (var modelHere in Enum.GetValues<LocalModel>())
            {
                all.Add(RelayStateText(useRelay: false, ready: false, useLocal, modelHere));
                all.Add(RelayStateText(useRelay: true, ready: false, useLocal, modelHere));
                all.Add(RelayStateText(useRelay: true, ready: true, useLocal, modelHere));
            }
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
    /// The relay client this page last built, or null with the relay off -
    /// kept so the Test button can tell a month that is used up from a
    /// relay that failed, and say so calmly. See RunTestAsync.
    /// </summary>
    private RelayCommentaryClient? _relay;

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
            RefreshRelayStateSoon();
            return;
        }

        var budget = TimeSpan.FromSeconds(Math.Clamp(_settings.CommentaryTimeoutSeconds, 2, 60));

        // Everything below is built from these. The relay's own address,
        // club and token are deliberately absent: RelayCommentaryClient
        // asks the settings again on every line, so a change there needs no
        // new client - only turning the relay off or on does.
        // Whether there is a model on this PC at all is in the key too
        // (19 Oct 2026): with the box unticked it is still built to take
        // over when the relay's month runs out, so a model that turns up
        // after the page was first built - downloaded, or named by hand -
        // has to be picked up by the next rebuild rather than ignored.
        var modelHere = BundledLlmClient.IsModelAvailableWith(_settings.CommentaryLocalModelPath);

        var key = string.Join(
            '\u001f',
            _settings.CommentaryUseLocalModel,
            _settings.CommentaryLocalModelPath,
            budget.Ticks,
            _settings.CommentaryUseRelay,
            modelHere);

        if (_clientKey == key && _commentary.Client is not null)
        {
            return;
        }

        ReleaseClient();

        // The model that ships inside the installer, run in this process.
        // It is only what writes when the relay cannot - see below. A club
        // that would rather a quiet segment than a plainly-written one
        // unticks the box, and still gets it here whenever the relay is on
        // and there is a model to load: unticked, it writes ONLY while the
        // relay's lines for the month are used up (FallbackCommentaryBrain),
        // which is the switch-over the club asked for. With the relay off
        // and the box unticked nothing is set to write, as before.
        var useLocal = _settings.CommentaryUseLocalModel
                       || (_settings.CommentaryUseRelay && modelHere);

        ICommentaryBrain? local = useLocal
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

        _relay = relay;

        if (relay is not null)
        {
            // For the console's "the model on this PC is writing until
            // 1 Nov" - asked when it is said, since the model may be found
            // too slow, or unable to run here, after this is built.
            relay.LocalTakesOver = () =>
                local is not null
                && (local is not BundledLlmClient bundled || (!bundled.TooSlow && bundled.Unavailable is null));

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
            // Ticked: any line the relay doesn't write falls back. Unticked:
            // only while the month's relay lines are used up.
            client = new FallbackCommentaryBrain(relay, local, onAnyFailure: _settings.CommentaryUseLocalModel);
        }
        else if (relay is not null)
        {
            client = relay;
        }
        else if (local is not null && _settings.CommentaryUseLocalModel)
        {
            client = local;
        }
        else
        {
            // Relay off and local off. Nothing can write, and saying so
            // plainly beats a client that fails on every line.
            _commentary.Client = null;
            RefreshRelayStateSoon();
            return;
        }

        client.StatusMessage += msg =>
        {
            _lastClientDetail = msg;

            // WHY (23 Nov 2026, bug hunt 24): the warm-up is where the model
            // finds this PC too slow, or unable to run it, and switches
            // itself off - but nothing redrew the page afterwards, so it went
            // on saying the model writes. Redrawn on the window's thread when
            // the model reports; skipped before the page has a window.
            RefreshRelayStateSoon();

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

        // WHY (23 Nov 2026, bug hunt 24): a new model on this PC has not been
        // measured yet - raising "Give up after" after a "too slow" is meant
        // to let it try again - so the page is redrawn rather than left
        // saying the old one switched itself off.
        RefreshRelayStateSoon();
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
        _relay = null;
        _clientKey = null;
    }

    /// <summary>
    /// Fires a background warm-up, guarded so it only happens at moments
    /// that plausibly mean "the configuration just settled" - loading the
    /// model is seconds of work, and firing it on every keystroke in a
    /// settings box would be seconds of work per character typed.
    /// </summary>
    private void TriggerWarmUp() => _ = _commentary.Client?.WarmUpAsync();

    /// <summary>
    /// Redraws the line under the relay box and the tiles from whatever
    /// thread the model reported on (23 Nov 2026, bug hunt 24). Never makes
    /// the window's handle - a page with no window yet draws it when shown.
    /// </summary>
    private void RefreshRelayStateSoon()
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        try
        {
            BeginInvoke(() =>
            {
                if (!IsDisposed)
                {
                    UpdateRelayState();
                }
            });
        }
        catch (InvalidOperationException)
        {
            // The window closed between the check and the ask.
        }
    }

    /// <summary>
    /// What Test Connection says when the relay's month is used up, and
    /// whether that means something is still writing. Pure, so the label's
    /// sizing list (AllSpentTestResults) is built from the same words.
    /// </summary>
    internal static (string Text, bool Writing) SpentTestResult(long? allowance, bool writtenHere, bool modelHere)
    {
        var lines = allowance is long cap && cap > 0
            ? $"This month's {cap:N0} relay lines are used up"
            : "This month's relay lines are used up";

        if (writtenHere)
        {
            return ($"{lines} - the model on this PC is writing instead.", true);
        }

        return modelHere
            ? ($"{lines}, and the model on this PC didn't answer - normal announcements carry on. Any [llm] line in the console says why.", false)
            : ($"{lines} - no model is installed on this PC, so normal announcements carry on.", false);
    }

    /// <summary>
    /// Why the model on this PC said nothing, when it has switched itself
    /// off (23 Nov 2026, bug hunt 24) - null when it hasn't, so Test
    /// Connection keeps its generic line. Pure, for the tests.
    /// </summary>
    internal static string? SwitchedOffDetail(bool tooSlow, TimeSpan measured, TimeSpan timeout, string? unavailable)
    {
        if (unavailable is { Length: > 0 })
        {
            return $"The model on this PC is switched off: {unavailable}.";
        }

        if (tooSlow)
        {
            return $"The model on this PC is switched off: it took {measured.TotalSeconds:0.#}s to write one line, "
                   + $"past the {timeout.TotalSeconds:0.#}s it is given. Raise \"Give up after\" if you would rather it tried.";
        }

        return null;
    }

    /// <summary>Every SpentTestResult sentence, at a four-figure allowance, for the result label to size to.</summary>
    private static IEnumerable<string> AllSpentTestResults() =>
        new[] { (true, true), (false, true), (false, false) }
            .Select(c => SpentTestResult(9999, c.Item1, c.Item2).Text);

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

            // THE MONTH IS USED UP (19 Oct 2026). Not an error, and it used
            // to read as one: the relay's own refusal in red, "Commentary
            // will be skipped" - when the model on this PC now takes over
            // whether the box below is ticked or not. Said as what it is.
            if (_relay is { MonthSpent: true } spentRelay)
            {
                var (text, writing) = SpentTestResult(
                    spentRelay.MonthAllowance,
                    writtenHere: reply is not null,
                    modelHere: BundledLlmClient.IsModelAvailableWith(_settings.CommentaryLocalModelPath));

                _lblTestResult.Text = text;
                _lblTestResult.ForeColor = writing ? AppTheme.Success : AppTheme.Accent;
                return;
            }

            if (reply is null)
            {
                // The client already knows exactly why - no model file, a
                // timeout with the seconds it waited, a PC too slow to keep
                // up - showing that beats guessing at a cause the client has
                // already ruled in or out.
                // WHY (23 Nov 2026, bug hunt 24): a model that has switched
                // itself off answers null without saying anything, so this
                // fell to the generic "No response" line - the very place a
                // too-slow PC was meant to be named. Its own reason is asked
                // for when the client said nothing.
                var bundled = _localBrain as BundledLlmClient;
                var detail = _lastClientDetail is { Length: > 0 }
                    ? _lastClientDetail
                    : SwitchedOffDetail(
                        bundled?.TooSlow ?? false,
                        bundled?.Measured ?? TimeSpan.Zero,
                        bundled?.Timeout ?? TimeSpan.Zero,
                        bundled?.Unavailable);

                _lblTestResult.Text = detail is { Length: > 0 }
                    ? $"{detail} Commentary will be skipped and normal announcements used."
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
                                          ? "That's slow for live lines - intros and recaps get longer and should still make it."
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
