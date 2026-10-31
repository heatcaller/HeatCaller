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

    // --- And how far down the segments go (22 Oct 2026) ---
    //
    // AppSettings.CommentaryLowestTier was stored, handed to the budget
    // (CommentaryDirector.NewMeeting) and offered nowhere - a club that
    // never wanted the furniture had no way to say so short of unticking
    // every box in the group, every one of which came back on with an
    // update that added another. One choice, read off the tiers the boxes
    // below are already grouped by. See TierChoices.
    private readonly ComboBox _cmbLowestTier = new();
    private readonly Label _lblLowestTier = new();

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

    /// <summary>The pickers' widths as last measured - see Pieces.PickerWidth.</summary>
    private (Font Font, int Count, int Width)? _toneWidth;
    private (Font Font, int Count, int Width)? _chatterWidth;

    /// <summary>The pacing picker's measured width - see _chatterWidth.</summary>
    private (Font Font, int Count, int Width)? _densityWidth;

    /// <summary>The "Segments run" picker's measured width - see _chatterWidth.</summary>
    private (Font Font, int Count, int Width)? _lowestTierWidth;

    /// <summary>
    /// The choices for how far down the segments go, most first. Each is the
    /// LOWEST tier that still runs - AppSettings.CommentaryLowestTier - and
    /// what it means said in the page's own group names, since nobody
    /// setting up a race night knows the tiers by their names in the code.
    /// Essential is never silenced by this (CommentaryBudget), which is why
    /// the last choice still has the intros and recaps.
    /// </summary>
    internal static readonly IReadOnlyList<(SegmentTier Tier, string Name, string Says)> TierChoices = new[]
    {
        (SegmentTier.Flourish, "Everything that is ticked",
            "Every ticked segment below can run, the top and tail of the evening included."),
        (SegmentTier.Colour, "All but the top and tail",
            "Nothing under \"The show\" runs, whatever is ticked there - no cold open, halfway standings or sign-off numbers."),
        (SegmentTier.Core, "The live ones only",
            "Only \"During the race\" runs. Nothing under \"Around the race\" or \"The show\", whatever is ticked."),
        (SegmentTier.Essential, "Intros and recaps only",
            "None of the segments below run. The race intros, recaps and achievements still do."),
    };

    /// <summary>
    /// Where a saved setting sits in <see cref="TierChoices"/> - the first
    /// choice (everything) for anything out of range, which is what the
    /// budget does with it too (CommentaryDirector.LowestTierOf).
    /// </summary>
    internal static int TierChoiceIndex(int saved)
    {
        var tier = CommentaryDirector.LowestTierOf(saved);

        for (var i = 0; i < TierChoices.Count; i++)
        {
            if (TierChoices[i].Tier == tier)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>
    /// Raised when the club changes how far down the segments go, so the
    /// director's budget - on the announcer screen, which this page cannot
    /// reach - takes it tonight rather than at the next race day. The host
    /// joins them up (MainForm.Commentary).
    /// </summary>
    internal event Action? LowestTierChanged;

    // Club notes, the room's vote and listening along - and the three
    // events that told the relay when one was switched off - moved to the
    // Club Website page on 17 Oct 2026 (RoomAndWebCard).

    /// <summary>The usual caption of the button that says so while it works - see BuildLayout's Steady.</summary>
    private const string TestCaption = "Test Connection";

    /// <summary>The boxes' and pickers' widths at the normal text size - see SizeFields.</summary>
    private const int TonePickerWidth = 220;
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
    ///   Writing style      how every line is WORDED.
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
        driverIntrosGrid.AddHint(
            "Grid spot, number, home club and a line of form for each driver. The automatic start waits for them.");

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
        styleGrid.AddHint("Needs an ElevenLabs v3 voice and the Enthusiastic tone.");

        // Each figure's meaning beside it (22 Oct 2026, page tidy), rather
        // than in a paragraph under the card explaining both.
        Flush(styleGrid.AddRow("Max length:", Line(_numMaxWords, Suffix("words - 30 is about a sentence, 70 is two"))));
        Flush(styleGrid.AddRow("Phrasing variety:", Line(_numVariety, Suffix("low is steady, high is livelier but now and then odd"))));

        var style = new Section("Writing style");

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

        // HOW FAR DOWN THE SEGMENTS GO (22 Oct 2026, known issues), with the
        // other two dials: all three are about how much the night hears,
        // and the boxes below go grey past the club's choice so the page
        // says why a ticked segment never runs. Built as the two above.
        _cmbLowestTier.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbLowestTier.Items.Clear();

        foreach (var choice in TierChoices)
        {
            _cmbLowestTier.Items.Add(choice.Name);
        }

        _cmbLowestTier.SelectedIndex = TierChoiceIndex(_settings.CommentaryLowestTier);

        _cmbLowestTier.SelectedIndexChanged += (_, _) =>
        {
            var at = _cmbLowestTier.SelectedIndex;

            _settings.CommentaryLowestTier = at >= 0 && at < TierChoices.Count
                ? (int)TierChoices[at].Tier
                : (int)SegmentTier.Flourish;

            _settings.Save();
            _lblLowestTier.Text = TierChoices[TierChoiceIndex(_settings.CommentaryLowestTier)].Says;
            UpdateEnabledState();
            LowestTierChanged?.Invoke();
        };

        _lblLowestTier.Text = TierChoices[TierChoiceIndex(_settings.CommentaryLowestTier)].Says;

        // The three dials on the left caption column with every other field
        // (Flush): they were a step in, as if they belonged to a switch above
        // them, and there is none.
        var chatterGrid = new FieldGrid();

        Flush(chatterGrid.AddRow("How chatty:", Line(_cmbChatter)));
        chatterGrid.AddHint(_lblChatter);

        Flush(chatterGrid.AddRow("Pacing:", Line(_cmbDensity)));
        chatterGrid.AddHint(_lblDensity);

        Flush(chatterGrid.AddRow("Segments run:", Line(_cmbLowestTier)));
        chatterGrid.AddHint(_lblLowestTier);

        chatterGrid.AddHint(Hint(
            "On Race Control, \"held\" timing calls are this dial leaving a gap; \"lost\" ones are the announcer "
            + "running out of room."));

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

        segments.Add(Hint(
            "Not written lap by lap - a model takes longer than a lap. Each segment speaks only when the race gives it "
            + "something to say, and the night saves itself for the mains."));
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

        page.Add(lblIntro, gapBelow: UiLayout.RowGap);
        page.Add(_chkEnabled, stretch: false);
        page.Add(connection);
        page.Add(before);
        page.Add(segments);
        page.Add(after);
        page.Add(recap);

        // --- Between races (31 Oct 2026): the gap after the recap and before
        // the next race is called - see BetweenRacesCard.
        var (betweenRaces, betweenGrids) = BetweenRacesCard.Build(_settings, _textSize,
            () => TonightsGap?.Invoke(), lines => PlayGap?.Invoke(lines), () => _settings.CommentaryEnabled,
            () => SponsorReadsBetweenRaces?.Invoke() ?? false);
        page.Add(betweenRaces);
        page.Add(style, gapBelow: 0);

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
        Fit(_cmbChatter, Math.Max(UiLayout.AtTextSize(ShowPieceWidth), Pieces.PickerWidth(_cmbChatter, ref _chatterWidth)));
        // The pacing dial beside it, sized the same way. Left out when the
        // dial was added (14 Oct 2026), so it kept the default width and
        // "Room to breathe" was cut off at every size the layout test checks.
        Fit(_cmbDensity, Math.Max(UiLayout.AtTextSize(ShowPieceWidth), Pieces.PickerWidth(_cmbDensity, ref _densityWidth)));
        Fit(_cmbLowestTier, Math.Max(UiLayout.AtTextSize(ShowPieceWidth), Pieces.PickerWidth(_cmbLowestTier, ref _lowestTierWidth)));

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
        _chkVoiceTags.Enabled = on && voiceReadsTags && _settings.CommentaryToneStyle == CommentaryTone.Enthusiastic;

        _chkIntros.Enabled = on;
        _chkIntrosSelective.Enabled = on && _chkIntros.Checked;
        _chkDriverIntros.Enabled = on;
        _chkDriverIntrosMains.Enabled = on && _chkDriverIntros.Checked;
        _chkQualifyingRecap.Enabled = on && _chkDriverIntros.Checked;
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
        _numMaxWords.Enabled = on;
        _numVariety.Enabled = on;

        // Grey past the club's "Segments run" choice too, so a ticked box
        // that will never run says so (22 Oct 2026). Still ticked: the
        // tick is kept for the day the club turns the dial back up.
        var lowest = CommentaryDirector.LowestTierOf(_settings.CommentaryLowestTier);

        foreach (var (kind, box) in _segmentBoxes)
        {
            box.Enabled = on && CommentaryBudget.Spec(kind).Tier <= lowest;
        }

        _cmbLowestTier.Enabled = on;

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

        _lblRelayState.Text = RelayStateText(
            useRelay,
            ready,
            useLocal,
            modelHere: BundledLlmClient.IsModelAvailableWith(_settings.CommentaryLocalModelPath));

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
    private static string RelayStateText(bool useRelay, bool ready, bool useLocal, bool modelHere)
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
    private static string[] AllRelayStates()
    {
        var all = new List<string>();

        foreach (var useLocal in new[] { true, false })
        {
            foreach (var modelHere in new[] { true, false })
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
