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

    private readonly CheckBox _chkEnabled = new();
    private readonly TextBox _txtEndpoint = new();
    private readonly TextBox _txtModel = new();
    private readonly NumericUpDown _numTimeout = new();
    private readonly CheckBox _chkIntros = new();
    private readonly CheckBox _chkIntrosSelective = new();
    private readonly CheckBox _chkSummaries = new();
    private readonly CheckBox _chkTalkingPoints = new();
    private readonly NumericUpDown _numFinishers = new();
    private readonly CheckBox _chkMentionFastestLap = new();
    private readonly NumericUpDown _numInterval = new();
    private readonly NumericUpDown _numObservations = new();
    private readonly ComboBox _cmbTone = new();
    private readonly NumericUpDown _numMaxWords = new();
    private readonly NumericUpDown _numVariety = new();
    private readonly CheckBox _chkBroadcastVoice = new();
    private readonly NumericUpDown _numStability = new();
    private readonly NumericUpDown _numStyleEx = new();
    private readonly NumericUpDown _numSummaryDelay = new();
    private readonly CheckBox _chkTeaser = new();
    private readonly TextBox _txtTeaser = new();
    private readonly NumericUpDown _numTeaserGap = new();
    private readonly CheckBox _chkDeepDuck = new();
    private readonly NumericUpDown _numDuckPercent = new();

    /// <summary>
    /// What the last test found, beside the Test Connection button. As tall
    /// as the longest of the things it usually says at the width it has, so
    /// a result coming back never moves the cards under it (SteadyLabel);
    /// Ollama's own error text can run longer, and then it grows.
    /// </summary>
    private readonly SteadyLabel _lblTestResult = new();

    // --- Broadcast (27 Sep 2026) ---
    private readonly CheckBox _chkOpening = new();
    private readonly CheckBox _chkClosing = new();
    private readonly TextBox _txtShowName = new();
    private readonly CheckBox _chkInterview = new();
    private readonly TextBox _txtMechanicName = new();
    private readonly ComboBox _cmbMechanicVoice = new();
    private readonly Button _btnLoadVoices = new();
    private readonly Button _btnTestMechanic = new();

    /// <summary>Which engine's voices the mechanic's list holds - see FillMechanicVoices.</summary>
    private readonly Label _lblVoiceSource = new();

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>The pickers' widths as last measured - see Pieces.PickerWidth. The mechanic's is forgotten whenever its list is filled.</summary>
    private (Font Font, int Count, int Width)? _toneWidth;
    private (Font Font, int Count, int Width)? _voiceWidth;

    /// <summary>The usual captions of the two buttons that say so while they work - see BuildLayout's Steady.</summary>
    private const string TestCaption = "Test Connection";
    private const string LoadVoicesCaption = "Load Voices";

    /// <summary>The boxes' and pickers' widths at the normal text size - see SizeFields.</summary>
    private const int EndpointWidth = 300;
    private const int TonePickerWidth = 220;
    private const int ShowNameWidth = 280;
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
    /// <param name="Id">The ElevenLabs voice id, the HeatCaller voice's id, or the Windows voice's name. Empty for the announcer's own voice.</param>
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
            Text = "Optional colour commentary from a language model running on this machine. Nothing is sent over the "
                 + "internet and nothing ships with this app - it talks to a local Ollama install.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        var lblRequirement = _textSize.Wear(
            new Label
            {
                Text = "Requires Ollama (ollama.com) running locally with a model pulled - e.g. \"ollama pull llama3.2:3b\". "
                     + "If it isn't running, commentary is skipped and the normal announcement lines are used instead.",
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
            "flag  ->  pause  ->  teaser  ->  wait  ->  recap.   Everything here is timing and sound; the recap's "
            + "words come from Writing style above."));
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

        var mechanicGrid = new FieldGrid();

        Stepped(mechanicGrid.AddRow("Mechanic's name:", Line(_txtMechanicName, Suffix("blank for \"the team's mechanic\" - the model never makes a name up"))));
        Stepped(mechanicGrid.AddRow("Mechanic's voice:", Line(_cmbMechanicVoice, Steady(_btnLoadVoices, LoadVoicesCaption), _btnTestMechanic, _lblVoiceSource)));
        mechanicGrid.AddHint(
            "The list is the voice engine chosen on Voice Settings. A voice is kept for each engine, so switching engine "
            + "there needs one picked here for the new engine too - with none picked, the announcer's own voice answers.");

        var broadcast = new Section("Broadcast");

        broadcast.Add(Hint(
            "recap  ->  interview (after a main)  ->  sign-off.   Dresses the recap up as a TV broadcast. The model writes "
            + "every part from the race's facts, like the recap itself, and the recap and intros say whether a race was a "
            + "heat or a main."));
        broadcast.Add(_chkOpening, stretch: false);
        broadcast.Add(_chkClosing, stretch: false);
        broadcast.Add(showGrid);
        broadcast.Add(_chkInterview, stretch: false, gapBelow: UiLayout.HintGap);
        broadcast.Add(Hint(
            "The announcer asks, the mechanic answers, twice. The mechanic talks about the race and about the setup changes "
            + "logged for the winning car in the Garage on the day - and says nothing about the car's settings when none "
            + "are logged. Heats don't get one."));
        broadcast.Add(mechanicGrid, gapBelow: 0);

        // ------------------------------------------------------------------
        // The page
        // ------------------------------------------------------------------

        var page = new PageStack();

        page.Add(lblIntro, gapBelow: UiLayout.HintGap);
        page.Add(lblRequirement, gapBelow: UiLayout.RowGap);
        page.Add(_chkEnabled, stretch: false);
        page.Add(connection);
        page.Add(writes);
        page.Add(style);
        page.Add(recap);
        page.Add(broadcast, gapBelow: 0);

        Controls.Add(page);

        // Once everything is on the page and wears its face - what these are
        // measured in.
        //
        // One caption column down every card, so the fields line up down the
        // whole page - see Stepped and Flush.
        _textSize.Keep(() => FieldGrid.LineUp(new[]
        {
            connectionGrid, introsGrid, summariesGrid, talkingGrid, styleGrid,
            pauseGrid, teaserGrid, voiceGrid, showGrid, mechanicGrid,
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

        FillMechanicVoices(null);

        // Voice Settings can change the engine while this page is hidden,
        // so the list is set out again for it whenever the page is shown.
        VisibleChanged += (_, _) =>
        {
            if (Visible && EngineInUse?.Invoke() is { } engine && engine != _voiceListEngine)
            {
                FillMechanicVoices(engine);
            }
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
        Fit(_txtMechanicName, UiLayout.AtTextSize(MechanicNameWidth));
        Fit(_txtTeaser, UiLayout.AtTextSize(TeaserWidth), Pieces.LineHeight(_txtTeaser.Font) * TeaserLines + UiLayout.HintGap * 2);

        Fit(_cmbTone, Math.Max(UiLayout.AtTextSize(TonePickerWidth), Pieces.PickerWidth(_cmbTone, ref _toneWidth)));
        Fit(_cmbMechanicVoice, Math.Max(UiLayout.AtTextSize(VoicePickerWidth), Pieces.PickerWidth(_cmbMechanicVoice, ref _voiceWidth)));

        foreach (var number in new[]
                 {
                     _numTimeout, _numFinishers, _numInterval, _numObservations, _numMaxWords, _numVariety,
                     _numSummaryDelay, _numTeaserGap, _numStability, _numStyleEx, _numDuckPercent,
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
            TextRenderer.MeasureText(widest, number.Font).Width + SystemInformation.VerticalScrollBarWidth + 12);
    }

    // ==================== The mechanic's voice ====================

    private static string EngineLabel(VoiceEngine engine) => engine switch
    {
        VoiceEngine.Windows => "Windows",
        VoiceEngine.HeatCaller => "HeatCaller",
        _ => "ElevenLabs",
    };

    /// <summary>The voice saved for the mechanic on this engine, as a list entry - the announcer's own when none is.</summary>
    private VoiceChoice SavedMechanicVoice(VoiceEngine engine)
    {
        var voice = _settings.MechanicVoice;

        static string Shown(string name, string id) => string.IsNullOrWhiteSpace(name) ? id : name;

        return engine switch
        {
            VoiceEngine.Windows => string.IsNullOrWhiteSpace(voice.WindowsVoiceName)
                ? AnnouncersVoice
                : new VoiceChoice(voice.WindowsVoiceName, voice.WindowsVoiceName, string.Empty, voice.WindowsVoiceName),

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
        }
        finally
        {
            _fillingVoices = false;
        }

        _lblVoiceSource.Text = loaded is null
            ? $"{EngineLabel(shown)} voices"
            : Plural.Of(loaded.Count, $"{EngineLabel(shown)} voice");

        // The longest voice may have come or gone: the picker measured again.
        _voiceWidth = null;
        SizeFields();
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
            case VoiceEngine.Windows:
                _settings.MechanicWindowsVoiceName = choice.Id;
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

    private void UpdateEnabledState()
    {
        var on = _chkEnabled.Checked;
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
        _chkInterview.Enabled = summariesOn;

        var interviewOn = summariesOn && _chkInterview.Checked;
        _txtMechanicName.Enabled = interviewOn;
        _cmbMechanicVoice.Enabled = interviewOn;
        _btnLoadVoices.Enabled = interviewOn;
        _btnTestMechanic.Enabled = interviewOn;

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

        var client = new LocalLlmClient
        {
            Endpoint = _settings.CommentaryEndpoint,
            Model = _settings.CommentaryModel,
            Timeout = TimeSpan.FromSeconds(Math.Clamp(_settings.CommentaryTimeoutSeconds, 2, 60)),
        };
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
