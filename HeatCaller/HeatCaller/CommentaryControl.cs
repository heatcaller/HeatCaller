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
    private readonly Label _lblTestResult = new();

    // --- Broadcast (27 Sep 2026) ---
    private readonly CheckBox _chkOpening = new();
    private readonly CheckBox _chkClosing = new();
    private readonly TextBox _txtShowName = new();
    private readonly CheckBox _chkInterview = new();
    private readonly TextBox _txtMechanicName = new();
    private readonly ComboBox _cmbMechanicVoice = new();
    private readonly Button _btnLoadVoices = new();
    private readonly Button _btnTestMechanic = new();
    private Label? _lblVoiceSource;

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
    // to the next tab stop (wrapping to the top of the page), and
    // AutoScroll then scrolls to it - which is why the page jumped up.
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
        AutoScroll = true;
        Font = AppTheme.BodyFont;
        ForeColor = AppTheme.TextDark;

// Height grown for the new per-feature and style rows - AutoScroll
        // handles overflow regardless, but starting closer to the real
        // content height means less immediate scrolling to see it all.
        // Taller for the teaser list - a dozen lines need more than the one
        // row the single teaser used to sit in.
        Size = new Size(980, 1100);
        MinimumSize = new Size(900, 520);

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

    private void BuildLayout()
    {
        // ONE COLUMN, TOP TO BOTTOM, EACH ROW PLACED UNDER THE ONE ABOVE IT.
        //
        // This page used to be drawn at remembered y values with a Clear()
        // pass shunting rows down wherever a wrapped hint ran into them. The
        // page has now been reorganised into four sections, so rather than
        // re-number forty rows it flows: every control is added to the page
        // as it is built - so it wears the font it will be drawn in - and
        // placed at a running y that moves down by what that control
        // ACTUALLY measured. A hint that wraps to three lines at a bigger
        // text size pushes the rest of the page down by exactly that much.
        //
        // The sections, in the order someone setting this up needs them:
        //
        //   Connection      - where the model is, and a Test button right
        //                     beside it (it used to be at the very bottom,
        //                     a whole page away from the fields it tests).
        //   What it writes  - intros, recaps, talking points.
        //   Writing style   - how those lines are WORDED. The tone here was
        //                     headed plain "Style", right under a "Style:"
        //                     voice slider, so the two read as duplicates.
        //   Race recap      - what happens after the flag, in the order it
        //                     happens, ending with how the recap SOUNDS.
        const int L0 = UiLayout.PageLeft;
        const int L1 = L0 + UiLayout.Indent;
        const int L2 = L1 + UiLayout.Indent;

        // Where wrapped text stops. The page's own minimum width is 900.
        const int TextRight = 900;

        // Between one row and the next, and between a row and its hint.
        const int Gap = 8;

        var y = UiLayout.PageTop;

        T Add<T>(T control) where T : Control
        {
            Controls.Add(control);
            return control;
        }

        // Places a single-line row - a caption, a field, a suffix - with
        // everything centred on the tallest item, and returns the y below it.
        // A closed drop-down only settles on its final height once it has a
        // handle and has been skinned, so it is given at least the height it
        // will grow to - a few pixels of extra air rather than a hint that
        // ends up under its bottom edge.
        int HeightIn(Control item) => item is ComboBox
            ? Math.Max(item.Height, item.Font.Height + 10)
            : UiLayout.HeightOf(item);

        int Row(int top, params Control[] items)
        {
            var height = items.Max(HeightIn);

            foreach (var item in items)
            {
                item.Top = top + Math.Max(0, (height - HeightIn(item)) / 2);
            }

            return top + height + Gap;
        }

        // Places a block (a paragraph, a check box) at its own height.
        int Block(int top, Control item)
        {
            item.Top = top;
            return top + UiLayout.HeightOf(item) + Gap;
        }

        // A hint belongs to the row above it, so it sits closer to that row
        // than the next row does.
        int HintUnder(int top, Label hint) => Block(top - Gap + UiLayout.HintGap, hint);

        Label Hint(string text, int left) => Add(new Label
        {
            Text = text,
            Left = left,
            AutoSize = true,
            MaximumSize = new Size(TextRight - left, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        });

        Label Caption(string text, int left) => Add(new Label { Text = text, Left = left, AutoSize = true });

        // An inline suffix after a field - "seconds", "words" - never wraps.
        Label Suffix(string text, Control after, bool muted = true) => Add(new Label
        {
            Text = text,
            Left = after.Right + UiLayout.HintGap,
            AutoSize = true,
            ForeColor = muted ? AppTheme.TextMuted : AppTheme.TextDark,
            Font = muted ? AppTheme.SmallFont : AppTheme.BodyFont,
        });

        int Heading(int top, string text)
        {
            var heading = Add(new Label
            {
                Text = text,
                Left = L0,
                AutoSize = true,
                Font = AppTheme.BoldFont,
                ForeColor = AppTheme.Primary,
            });

            return Block(top, heading);
        }

        // The x just past a check box whose caption a field continues. Never
        // less than what the layout pass will widen the box to (its text
        // plus 26), or the field would be reported as overlapping it.
        int AfterCheck(CheckBox box)
        {
            var text = TextRenderer.MeasureText(
                box.Text,
                box.Font,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;

            return Math.Max(UiLayout.After(box), box.Left + text + 26 + UiLayout.LabelGap);
        }

        CheckBox Check(CheckBox box, string text, int left, Font? font = null)
        {
            box.Text = text;
            box.Left = left;
            box.AutoSize = true;

            if (font is not null)
            {
                box.Font = font;
            }

            return Add(box);
        }

        // ------------------------------------------------------------------
        // Intro and master switch
        // ------------------------------------------------------------------

        var lblIntro = Add(new Label
        {
            Text = "Optional colour commentary from a language model running on this machine. Nothing is sent over the "
                 + "internet and nothing ships with this app - it talks to a local Ollama install.",
            Left = L0,
            AutoSize = true,
            MaximumSize = new Size(TextRight - L0, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.BodyFont,
        });

        y = Block(y, lblIntro) - Gap + UiLayout.HintGap;

        var lblRequirement = Add(new Label
        {
            Text = "Requires Ollama (ollama.com) running locally with a model pulled - e.g. \"ollama pull llama3.2:3b\". "
                 + "If it isn't running, commentary is skipped and the normal announcement lines are used instead.",
            Left = L0,
            AutoSize = true,
            MaximumSize = new Size(TextRight - L0, 0),
            ForeColor = AppTheme.Accent,
            Font = AppTheme.SmallFont,
        });

        y = Block(y, lblRequirement) + UiLayout.HintGap;

        Check(_chkEnabled, "Enable local LLM commentary", L0, AppTheme.BoldFont);
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

        y = Block(y, _chkEnabled) + UiLayout.SectionGap - Gap;

        // ------------------------------------------------------------------
        // Connection
        // ------------------------------------------------------------------

        y = Heading(y, "Connection");

        var lblEndpoint = Caption("Endpoint:", L0);
        var lblModel = Caption("Model:", L0);

        // One column for both boxes, cut for the longer caption.
        var fieldLeft = Math.Max(
            UiLayout.After(lblEndpoint, AppTheme.BodyFont),
            UiLayout.After(lblModel, AppTheme.BodyFont));

        _txtEndpoint.Width = 300;
        _txtEndpoint.Left = fieldLeft;
        _txtEndpoint.Text = _settings.CommentaryEndpoint;
        _txtEndpoint.TextChanged += (_, _) =>
        {
            _settings.CommentaryEndpoint = _txtEndpoint.Text.Trim();
            _settings.Save();
            ApplyToService();
        };
        _txtEndpoint.Leave += (_, _) => TriggerWarmUp();
        Add(_txtEndpoint);

        y = Row(y, lblEndpoint, _txtEndpoint);

        _txtModel.Width = 300;
        _txtModel.Left = fieldLeft;
        _txtModel.Text = _settings.CommentaryModel;
        _txtModel.TextChanged += (_, _) =>
        {
            _settings.CommentaryModel = _txtModel.Text.Trim();
            _settings.Save();
            ApplyToService();
        };
        _txtModel.Leave += (_, _) => TriggerWarmUp();
        Add(_txtModel);

        y = Row(y, lblModel, _txtModel);

        y = HintUnder(y, Hint(
            "A small model is the right choice here - it only has to rephrase facts, and a big one is too slow to be useful.",
            fieldLeft));

        var lblTimeout = Caption("Give up after:", L0);
        _numTimeout.Width = 60;
        _numTimeout.Left = UiLayout.After(lblTimeout, AppTheme.BodyFont);
        _numTimeout.Minimum = 2;
        _numTimeout.Maximum = 60;
        _numTimeout.Value = Math.Clamp(_settings.CommentaryTimeoutSeconds, 2, 60);
        _numTimeout.ValueChanged += (_, _) =>
        {
            _settings.CommentaryTimeoutSeconds = (int)_numTimeout.Value;
            _settings.Save();
            ApplyToService();
        };
        Add(_numTimeout);

        var lblTimeoutHint = Suffix("seconds - after this the normal announcement line is used instead", _numTimeout);

        y = Row(y, lblTimeout, _numTimeout, lblTimeoutHint);

        // Right under the fields it checks. It used to be the last thing on
        // the page, a whole scroll away from the endpoint and model.
        var btnTest = Add(new Button { Text = "Test Connection", Left = L0 });
        btnTest.Size = new Size(UiLayout.WidthFor(btnTest), UiLayout.ButtonHeight);
        btnTest.Click += async (_, _) => await RunTestAsync(btnTest);

        _lblTestResult.AutoSize = true;
        _lblTestResult.Left = btnTest.Right + UiLayout.RowGap;
        _lblTestResult.MaximumSize = new Size(TextRight - _lblTestResult.Left, 0);
        _lblTestResult.ForeColor = AppTheme.TextMuted;
        _lblTestResult.Font = AppTheme.BodyFont;
        Add(_lblTestResult);

        btnTest.Top = y;
        CentreTestResult(btnTest);

        // The result can run to two lines (Ollama's own error text), so it
        // re-centres on the button whenever its size changes.
        _lblTestResult.SizeChanged += (_, _) => CentreTestResult(btnTest);

        // Room for a two-line result without it touching the next heading.
        y = btnTest.Top + Math.Max(btnTest.Height, AppTheme.BodyFont.Height * 2) + UiLayout.SectionGap;

        // ------------------------------------------------------------------
        // What it writes
        // ------------------------------------------------------------------

        y = Heading(y, "What it writes");

        Check(_chkIntros, "Race intros - a \"coming up\" line before a round starts", L0);
        _chkIntros.Checked = _settings.CommentaryRaceIntros;
        _chkIntros.CheckedChanged += (_, _) => { _settings.CommentaryRaceIntros = _chkIntros.Checked; _settings.Save(); UpdateEnabledState(); };

        y = Block(y, _chkIntros);

        Check(_chkIntrosSelective, "Only for a main, or a race with a known rivalry on the grid", L1);
        _chkIntrosSelective.Checked = _settings.CommentaryIntrosMainsOrRivalriesOnly;
        _chkIntrosSelective.CheckedChanged += (_, _) =>
        {
            _settings.CommentaryIntrosMainsOrRivalriesOnly = _chkIntrosSelective.Checked;
            _settings.Save();
        };

        y = Block(y, _chkIntrosSelective);

        y = HintUnder(y, Hint(
            "Every other race starts without one - fewer words between heats, and the intro saved for the races that "
            + "matter. Rivalries are set on the Rivalries tab.",
            L2));

        Check(_chkSummaries, "Race recaps - a wrap-up after the checkered flag", L0);
        _chkSummaries.Checked = _settings.CommentaryRaceSummaries;
        _chkSummaries.CheckedChanged += (_, _) => { _settings.CommentaryRaceSummaries = _chkSummaries.Checked; _settings.Save(); UpdateEnabledState(); };

        y = Block(y, _chkSummaries);

        var lblFinishers = Caption("Mention up to:", L1);
        _numFinishers.Width = 50;
        _numFinishers.Left = UiLayout.After(lblFinishers, AppTheme.BodyFont);
        _numFinishers.Minimum = 1;
        _numFinishers.Maximum = 20;
        _numFinishers.Value = Math.Clamp(_settings.CommentarySummaryFinishers, 1, 20);
        _numFinishers.ValueChanged += (_, _) => { _settings.CommentarySummaryFinishers = (int)_numFinishers.Value; _settings.Save(); };
        Add(_numFinishers);

        // Same as BackupRestore's "day(s)": the number is in the spin box,
        // so the label carries the noun alone and follows it. Last thing on
        // the row, so re-measuring moves nothing.
        string FinishersSuffix() => $"{Plural.Word((long)_numFinishers.Value, "finisher")} in the recap";

        var lblFinishersSuffix = Suffix(FinishersSuffix(), _numFinishers, muted: false);
        _numFinishers.ValueChanged += (_, _) => lblFinishersSuffix.Text = FinishersSuffix();

        y = Row(y, lblFinishers, _numFinishers, lblFinishersSuffix);

        Check(_chkMentionFastestLap, "Also mention the fastest lap of the race", L1);
        _chkMentionFastestLap.Checked = _settings.CommentarySummaryMentionFastestLap;
        _chkMentionFastestLap.CheckedChanged += (_, _) =>
        {
            _settings.CommentarySummaryMentionFastestLap = _chkMentionFastestLap.Checked;
            _settings.Save();
        };

        y = Block(y, _chkMentionFastestLap);

        y = HintUnder(y, Hint(
            "Off if this feels repetitive - the fastest lap already gets its own announcement live during the race.",
            L2));

        Check(_chkTalkingPoints, "Announcer talking points - suggested lines on screen, never spoken automatically", L0);
        _chkTalkingPoints.Checked = _settings.CommentaryTalkingPoints;
        _chkTalkingPoints.CheckedChanged += (_, _) => { _settings.CommentaryTalkingPoints = _chkTalkingPoints.Checked; _settings.Save(); UpdateEnabledState(); };

        y = Block(y, _chkTalkingPoints);

        var lblInterval = Caption("Refresh every:", L1);
        _numInterval.Width = 55;
        _numInterval.Left = UiLayout.After(lblInterval, AppTheme.BodyFont);
        _numInterval.Minimum = 5;
        _numInterval.Maximum = 120;
        _numInterval.Value = Math.Clamp(_settings.CommentaryTalkingPointIntervalSeconds, 5, 120);
        _numInterval.ValueChanged += (_, _) =>
        {
            _settings.CommentaryTalkingPointIntervalSeconds = (int)_numInterval.Value;
            _settings.Save();
        };
        Add(_numInterval);

        var lblIntervalSuffix = Suffix("seconds - lower feels fresher, higher means less repeated wording", _numInterval);

        y = Row(y, lblInterval, _numInterval, lblIntervalSuffix);

        var lblObservations = Caption("Consider up to:", L1);
        _numObservations.Width = 50;
        _numObservations.Left = UiLayout.After(lblObservations, AppTheme.BodyFont);
        _numObservations.Minimum = 1;
        _numObservations.Maximum = 12;
        _numObservations.Value = Math.Clamp(_settings.CommentaryTalkingPointObservationCount, 1, 12);
        _numObservations.ValueChanged += (_, _) =>
        {
            _settings.CommentaryTalkingPointObservationCount = (int)_numObservations.Value;
            _settings.Save();
        };
        Add(_numObservations);

        var lblObservationsSuffix = Suffix("of the current talking points to pick from", _numObservations);

        y = Row(y, lblObservations, _numObservations, lblObservationsSuffix);

        // Belongs with what it writes - it is the answer to "why not a line
        // every lap?", which is a question about this list.
        y = Block(y + UiLayout.HintGap, Hint(
            "Commentary is written between races, not lap by lap. A lap takes a few seconds and a local model usually "
            + "takes longer than that to answer, so a live per-lap line would describe a moment that had already passed.",
            L0));

        y += UiLayout.SectionGap - Gap;

        // ------------------------------------------------------------------
        // Writing style - the WORDS
        // ------------------------------------------------------------------

        y = Heading(y, "Writing style");

        y = HintUnder(y, Hint(
            "How the lines are worded - intros, recaps and talking points alike. This changes the words, not the "
            + "voice; how the recap sounds is set under Race recap below.",
            L0));

        var lblTone = Caption("Writing tone:", L0);

        var toneItems = new[]
        {
            new ToneItem(CommentaryTone.Enthusiastic, "Enthusiastic - trackside hype"),
            new ToneItem(CommentaryTone.Neutral, "Neutral - calm play-by-play"),
            new ToneItem(CommentaryTone.DryUnderstated, "Dry - understated, deadpan"),
        };

        // Wide enough for the longest choice plus the arrow. A flat 220 cut
        // "Enthusiastic - trackside hype" short at this text size.
        _cmbTone.Width = Math.Max(220, toneItems.Max(t => TextRenderer.MeasureText(t.Label, AppTheme.BodyFont).Width) + 48);
        _cmbTone.Left = UiLayout.After(lblTone, AppTheme.BodyFont);
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
        Add(_cmbTone);

        y = Row(y, lblTone, _cmbTone);

        y = HintUnder(y, Hint(
            "Enthusiastic also marks every sentence of the recap as excited on a v3 voice, so the energy holds to the end.",
            _cmbTone.Left));

        var lblMaxWords = Caption("Max length:", L0);
        _numMaxWords.Width = 60;
        _numMaxWords.Left = UiLayout.After(lblMaxWords, AppTheme.BodyFont);
        _numMaxWords.Minimum = 8;
        // Was capped at 60, which silently clamped anything higher - so
        // asking for a long summary appeared to do nothing. 300 is past
        // any useful spoken length while still bounding the request.
        _numMaxWords.Maximum = 300;
        _numMaxWords.Increment = 5;
        _numMaxWords.Value = Math.Clamp(_settings.CommentaryMaxWords, 8, 300);
        _numMaxWords.ValueChanged += (_, _) => { _settings.CommentaryMaxWords = (int)_numMaxWords.Value; _settings.Save(); };
        Add(_numMaxWords);

        var lblMaxWordsSuffix = Suffix("words", _numMaxWords, muted: false);

        var lblVariety = Caption("Phrasing variety:", UiLayout.After(lblMaxWordsSuffix, AppTheme.BodyFont) + UiLayout.SectionGap);
        _numVariety.Width = 60;
        _numVariety.Left = UiLayout.After(lblVariety, AppTheme.BodyFont);
        _numVariety.DecimalPlaces = 1;
        _numVariety.Increment = 0.1m;
        _numVariety.Minimum = 0.2m;
        _numVariety.Maximum = 1.3m;
        _numVariety.Value = (decimal)Math.Clamp(_settings.CommentaryVariety, 0.2, 1.3);
        _numVariety.ValueChanged += (_, _) => { _settings.CommentaryVariety = (double)_numVariety.Value; _settings.Save(); };
        Add(_numVariety);

        y = Row(y, lblMaxWords, _numMaxWords, lblMaxWordsSuffix, lblVariety, _numVariety);

        y = HintUnder(y, Hint(
            "Length: up to 30 words gives one sentence, up to 70 gives two, beyond that a full paragraph - and it gets "
            + "spoken aloud, so a 200-word recap takes a while to read out.  Variety: low = consistent, safe wording; "
            + "high = more varied, occasionally an odd sentence.",
            L0));

        y += UiLayout.SectionGap - Gap;

        // ------------------------------------------------------------------
        // Race recap - after the flag, in the order it happens
        // ------------------------------------------------------------------

        y = Heading(y, "Race recap");

        y = HintUnder(y, Hint(
            "flag  ->  pause  ->  teaser  ->  wait  ->  recap.   Everything here is timing and sound; the recap's "
            + "words come from Writing style above.",
            L0));

        var lblDelay = Caption("Pause after the flag:", L0);
        _numSummaryDelay.Width = 55;
        _numSummaryDelay.Left = UiLayout.After(lblDelay, AppTheme.BodyFont);
        _numSummaryDelay.Minimum = 0;
        _numSummaryDelay.Maximum = 120;
        _numSummaryDelay.Value = Math.Clamp(_settings.SummaryDelaySeconds, 0, 120);
        _numSummaryDelay.ValueChanged += (_, _) => { _settings.SummaryDelaySeconds = (int)_numSummaryDelay.Value; _settings.Save(); };
        Add(_numSummaryDelay);

        var lblDelayHint = Suffix("seconds - lets the finish call and the cheering land before the recap starts", _numSummaryDelay);

        y = Row(y, lblDelay, _numSummaryDelay, lblDelayHint);

        // The teaser row is not a single line: the list is several lines
        // tall, and the check box and "then wait" sit level with its FIRST
        // line rather than floating in the middle of it.
        Check(_chkTeaser, "Teasers before the recap:", L0);
        _chkTeaser.Checked = _settings.SummaryTeaserEnabled;
        _chkTeaser.CheckedChanged += (_, _) =>
        {
            _settings.SummaryTeaserEnabled = _chkTeaser.Checked;
            _settings.Save();
            UpdateEnabledState();
        };

        // One line per teaser, picked at random each race - the same
        // sentence after every single race is what makes it sound canned.
        _txtTeaser.Size = new Size(420, 86);
        _txtTeaser.Left = AfterCheck(_chkTeaser) + UiLayout.RowGap;
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
        Add(_txtTeaser);

        var lblGap = Caption("then wait:", _txtTeaser.Right + UiLayout.SectionGap);

        _numTeaserGap.Width = 55;
        _numTeaserGap.Left = UiLayout.After(lblGap, AppTheme.BodyFont);
        _numTeaserGap.Minimum = 0;
        _numTeaserGap.Maximum = 60;
        _numTeaserGap.Value = Math.Clamp(_settings.SummaryTeaserGapSeconds, 0, 60);
        _numTeaserGap.ValueChanged += (_, _) => { _settings.SummaryTeaserGapSeconds = (int)_numTeaserGap.Value; _settings.Save(); };
        Add(_numTeaserGap);

        var lblGapSuffix = Suffix("seconds", _numTeaserGap);

        _txtTeaser.Top = y;
        Row(y, _chkTeaser, lblGap, _numTeaserGap, lblGapSuffix);

        y = _txtTeaser.Top + _txtTeaser.Height + Gap;

        y = HintUnder(y, Hint(
            "One line per teaser - one is picked at random each race, so it isn't the same sentence every time. "
            + "It's fixed text rather than something the AI writes, so it plays even when the local model is unreachable.",
            _txtTeaser.Left));

        Check(_chkBroadcastVoice, "Recap voice delivery - give the recap its own stability and style", L0);
        _chkBroadcastVoice.Checked = _settings.SummaryUseBroadcastVoice;
        _chkBroadcastVoice.CheckedChanged += (_, _) =>
        {
            _settings.SummaryUseBroadcastVoice = _chkBroadcastVoice.Checked;
            _settings.Save();
            UpdateEnabledState();
        };

        y = Block(y, _chkBroadcastVoice);

        y = HintUnder(y, Hint(
            "How the recap SOUNDS, not what it says. Overrides the stability and style on the Voice Settings tab for "
            + "the recap only, so routine lap calls don't become over-acted. Untick it to use your Voice Settings "
            + "values for everything - if you've tuned them there and heard no change in the recap, this was why.",
            L1));

        var lblStability = Caption("Stability:", L1);
        _numStability.Width = 55;
        _numStability.Left = UiLayout.After(lblStability, AppTheme.BodyFont);
        _numStability.DecimalPlaces = 2;
        _numStability.Increment = 0.05m;
        _numStability.Minimum = 0m;
        _numStability.Maximum = 1m;
        _numStability.Value = (decimal)Math.Clamp(_settings.SummaryVoiceStability, 0, 1);
        _numStability.ValueChanged += (_, _) => { _settings.SummaryVoiceStability = (double)_numStability.Value; _settings.Save(); };
        Add(_numStability);

        var lblStabilityHint = Suffix("lower = more emotional range", _numStability);

        // Measured along the row, never at a remembered column - "Stability:
        // 0.00 lower = more emotional range" once ran straight into "Style:".
        var lblStyleEx = Caption("Style:", UiLayout.After(lblStabilityHint, AppTheme.SmallFont) + UiLayout.SectionGap);

        _numStyleEx.Width = 55;
        _numStyleEx.Left = UiLayout.After(lblStyleEx, AppTheme.BodyFont);
        _numStyleEx.DecimalPlaces = 2;
        _numStyleEx.Increment = 0.05m;
        _numStyleEx.Minimum = 0m;
        _numStyleEx.Maximum = 1m;
        _numStyleEx.Value = (decimal)Math.Clamp(_settings.SummaryVoiceStyle, 0, 1);
        _numStyleEx.ValueChanged += (_, _) => { _settings.SummaryVoiceStyle = (double)_numStyleEx.Value; _settings.Save(); };
        Add(_numStyleEx);

        var lblStyleExHint = Suffix("higher = more dramatic; past ~0.6 it can distort", _numStyleEx);

        y = Row(y, lblStability, _numStability, lblStabilityHint, lblStyleEx, _numStyleEx, lblStyleExHint);

        Check(_chkDeepDuck, "Duck music and crowd further during the recap, down to", L0);
        _chkDeepDuck.Checked = _settings.SummaryDeepDuck;
        _chkDeepDuck.CheckedChanged += (_, _) =>
        {
            _settings.SummaryDeepDuck = _chkDeepDuck.Checked;
            _settings.Save();
            UpdateEnabledState();
        };

        // The check box's caption ends the sentence this spinner completes
        // ("...down to  [ 25 ] %"), so it is measured, not guessed.
        _numDuckPercent.Width = 55;
        _numDuckPercent.Left = AfterCheck(_chkDeepDuck) + UiLayout.HintGap;
        _numDuckPercent.Minimum = 2;
        _numDuckPercent.Maximum = 100;
        _numDuckPercent.Value = Math.Clamp(_settings.SummaryDuckPercent, 2, 100);
        _numDuckPercent.ValueChanged += (_, _) =>
        {
            _settings.SummaryDuckPercent = (int)_numDuckPercent.Value;
            _settings.Save();
        };
        Add(_numDuckPercent);

        var lblDuckSuffix = Suffix("%", _numDuckPercent, muted: false);

        y = Row(y, _chkDeepDuck, _numDuckPercent, lblDuckSuffix);

        y = HintUnder(y, Hint(
            "The recap runs for half a minute, so the usual announcement duck leaves the music and crowd competing with it.",
            L1));

        y += UiLayout.SectionGap - Gap;

        // ------------------------------------------------------------------
        // Broadcast - the recap dressed up as TV (27 Sep 2026)
        // ------------------------------------------------------------------

        y = Heading(y, "Broadcast");

        y = HintUnder(y, Hint(
            "recap  ->  interview (after a main)  ->  sign-off.   Dresses the recap up as a TV broadcast. The model writes "
            + "every part from the race's facts, like the recap itself, and the recap and intros say whether a race was a "
            + "heat or a main.",
            L0));

        Check(_chkOpening, "Open the recap like a TV broadcast coming back on air", L0);
        _chkOpening.Checked = _settings.RecapBroadcastOpening;
        _chkOpening.CheckedChanged += (_, _) =>
        {
            _settings.RecapBroadcastOpening = _chkOpening.Checked;
            _settings.Save();
            UpdateEnabledState();
        };

        y = Block(y, _chkOpening);

        Check(_chkClosing, "End with a TV-style sign-off that tees up what's next on the program", L0);
        _chkClosing.Checked = _settings.RecapBroadcastClosing;
        _chkClosing.CheckedChanged += (_, _) =>
        {
            _settings.RecapBroadcastClosing = _chkClosing.Checked;
            _settings.Save();
            UpdateEnabledState();
        };

        y = Block(y, _chkClosing);

        var lblShow = Caption("Show name:", L1);
        _txtShowName.Width = 280;
        _txtShowName.Left = UiLayout.After(lblShow, AppTheme.BodyFont);
        _txtShowName.MaxLength = 60;
        _txtShowName.Text = _settings.RecapShowName ?? string.Empty;
        _txtShowName.TextChanged += (_, _) =>
        {
            _settings.RecapShowName = _txtShowName.Text.Trim();
            _settings.Save();
        };
        Add(_txtShowName);

        var lblShowSuffix = Suffix("named in the opening and the sign-off - leave it blank for none", _txtShowName);

        y = Row(y, lblShow, _txtShowName, lblShowSuffix);

        Check(_chkInterview, "After a main, a quick interview with the winner's mechanic", L0);
        _chkInterview.Checked = _settings.RecapMechanicInterview;
        _chkInterview.CheckedChanged += (_, _) =>
        {
            _settings.RecapMechanicInterview = _chkInterview.Checked;
            _settings.Save();
            UpdateEnabledState();
        };

        y = Block(y, _chkInterview);

        y = HintUnder(y, Hint(
            "The announcer asks, the mechanic answers, twice. The mechanic talks about the race and about the setup changes "
            + "logged for the winning car in the Garage on the day - and says nothing about the car's settings when none "
            + "are logged. Heats don't get one.",
            L1));

        var lblMechanic = Caption("Mechanic's name:", L1);
        var lblVoice = Caption("Mechanic's voice:", L1);

        var mechanicLeft = Math.Max(
            UiLayout.After(lblMechanic, AppTheme.BodyFont),
            UiLayout.After(lblVoice, AppTheme.BodyFont));

        _txtMechanicName.Width = 200;
        _txtMechanicName.Left = mechanicLeft;
        _txtMechanicName.MaxLength = 40;
        _txtMechanicName.Text = _settings.RecapMechanicName ?? string.Empty;
        _txtMechanicName.TextChanged += (_, _) =>
        {
            _settings.RecapMechanicName = _txtMechanicName.Text.Trim();
            _settings.Save();
        };
        Add(_txtMechanicName);

        var lblMechanicSuffix = Suffix("blank for \"the team's mechanic\" - the model never makes a name up", _txtMechanicName);

        y = Row(y, lblMechanic, _txtMechanicName, lblMechanicSuffix);

        _cmbMechanicVoice.Width = 260;
        _cmbMechanicVoice.Left = mechanicLeft;
        _cmbMechanicVoice.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbMechanicVoice.SelectedIndexChanged += (_, _) => SaveMechanicVoice();
        Add(_cmbMechanicVoice);

        // Added before they are measured, so each is sized in the font it
        // is drawn in - see the note at the top of this method.
        _btnLoadVoices.Text = "Load Voices";
        Add(_btnLoadVoices);
        _btnLoadVoices.Size = new Size(UiLayout.WidthFor(_btnLoadVoices), UiLayout.ButtonHeight);
        _btnLoadVoices.Left = _cmbMechanicVoice.Right + UiLayout.RowGap;
        _btnLoadVoices.Click += async (_, _) => await LoadMechanicVoicesAsync();

        _btnTestMechanic.Text = "Test Voice";
        Add(_btnTestMechanic);
        _btnTestMechanic.Size = new Size(UiLayout.WidthFor(_btnTestMechanic), UiLayout.ButtonHeight);
        _btnTestMechanic.Left = _btnLoadVoices.Right + UiLayout.ButtonGap;
        _btnTestMechanic.Click += (_, _) => SpeakAsMechanic?.Invoke(_settings.MechanicVoice);

        // Which engine's voices the list holds - the one chosen on Voice
        // Settings. Last on the row, so its text can change without moving
        // anything.
        _lblVoiceSource = Suffix(string.Empty, _btnTestMechanic);

        y = Row(y, lblVoice, _cmbMechanicVoice, _btnLoadVoices, _btnTestMechanic, _lblVoiceSource);

        y = HintUnder(y, Hint(
            "The list is the voice engine chosen on Voice Settings. A voice is kept for each engine, so switching engine "
            + "there needs one picked here for the new engine too - with none picked, the announcer's own voice answers.",
            mechanicLeft));

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

        // Whatever the wrapping above actually came to.
        Height = Math.Max(MinimumSize.Height, y + UiLayout.PageTop);

        UpdateEnabledState();
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

        if (_lblVoiceSource is not null)
        {
            _lblVoiceSource.Text = loaded is null
                ? $"{EngineLabel(shown)} voices"
                : Plural.Of(loaded.Count, $"{EngineLabel(shown)} voice");
        }
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
                _btnLoadVoices.Text = "Load Voices";
            }
        }
    }

    /// <summary>Keeps the test result level with the button, however many lines it wraps to.</summary>
    private void CentreTestResult(Button button)
    {
        var top = button.Top + Math.Max(0, (button.Height - UiLayout.HeightOf(_lblTestResult)) / 2);

        if (_lblTestResult.Top != top)
        {
            _lblTestResult.Top = top;
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
            btnTest.Text = "Test Connection";
        }
    }
}
