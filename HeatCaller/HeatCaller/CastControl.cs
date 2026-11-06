namespace HeatCaller;

/// <summary>
/// Voice > Cast (17 Oct 2026): everyone on the show with a name and a voice,
/// one card each - the announcer, the race recap host, the co-host, the
/// winner's mechanic and the badges reader - with ONE Load Voices at the top
/// that fills every voice list.
///
/// MOVED FROM THE COMMENTARY PAGE, where the names and voices were spread
/// across its Broadcast, Race recap and co-host cards - the Voice regroup
/// put who speaks on a tab of its own and left AI Commentary with what the
/// AI writes. Same settings and the same rules: nothing a club has saved
/// changes, only where it is shown. Every hook is set by MainForm
/// (MainForm.Commentary.cs, WireRecapBroadcast), as CommentaryControl's were.
///
/// The cards grey out by the rules they always had: most of the cast only
/// speaks with AI commentary on, the recap's people only with recaps on, and
/// the co-host only while a two-hander segment is ticked on AI Commentary.
/// Those switches are on another tab, so the greying is worked out again
/// whenever this one is shown.
/// </summary>
public sealed class CastControl : UserControl
{
    private readonly AppSettings _settings;

    // --- The announcer ---

    /// <summary>
    /// What the main announcer calls himself - see AppSettings.RecapAnnouncerName.
    /// </summary>
    private readonly TextBox _txtAnnouncerName = new();

    // THE ANNOUNCER'S VOICE AT THE TOP OF THE PAGE (22 Oct 2026). Its
    // picker and Test Voice were on the Voice tab, a different spot for
    // each engine, while every other voice on the show was picked here. The
    // rows are still the Voice tab's controls, with all of their logic - it
    // builds them (VoiceSettingsControl.AnnouncerVoiceGrid) and the host
    // hands them over (HostAnnouncerVoice), the way the announcement rows
    // are built there and shown on Race Calls.

    /// <summary>The Announcer card, first on the page - where the voice rows are put.</summary>
    private Section? _announcerCard;

    /// <summary>The announcer's name, first on the Announcer card - the voice rows go straight under it (25 Oct 2026).</summary>
    private Control? _announcerName;

    /// <summary>The voice rows the Voice tab handed over, while they are here.</summary>
    private FieldGrid? _announcerVoice;

    /// <summary>This page's own grids, for the one caption column - see LineUpGrids.</summary>
    private FieldGrid[] _ownGrids = Array.Empty<FieldGrid>();

    // --- The race recap host (16 Oct 2026) - see AppSettings.RecapHostVoice ---
    private readonly TextBox _txtRecapHostName = new();
    private readonly ComboBox _cmbRecapHostVoice = new();
    private readonly Button _btnTestRecapHost = new();

    /// <summary>How the recap SOUNDS - its own stability and style (AppSettings.SummaryUseBroadcastVoice).</summary>
    private readonly CheckBox _chkBroadcastVoice = new ThemedCheckBox();
    private readonly NumericUpDown _numStability = new();
    private readonly NumericUpDown _numStyleEx = new();

    // --- The co-host ---
    private readonly CheckBox _chkCoHost = new ThemedCheckBox();
    private readonly TextBox _txtCoHostName = new();
    private readonly ComboBox _cmbCoHostVoice = new();
    private readonly Button _btnTestCoHost = new();

    // What the co-host is LIKE (11 Oct 2026) - see CoHostStance. The line
    // under the picker says what the club has just chosen in its
    // consequences rather than its label.
    private readonly ComboBox _cmbCoHostStance = new();
    private readonly Label _lblCoHostStance = new();

    // How much the two of them actually talk (14 Oct 2026).
    private readonly ComboBox _cmbExchangeTurns = new();
    private readonly CheckBox _chkTwoVoicesInHeats = new ThemedCheckBox();

    // --- The winner's mechanic (27 Sep 2026) ---
    private readonly CheckBox _chkInterview = new ThemedCheckBox();

    /// <summary>The Technical Lead after heats too, not only mains (21 Oct 2026).</summary>
    private readonly CheckBox _chkInterviewHeats = new ThemedCheckBox();
    private readonly TextBox _txtMechanicName = new();
    private readonly ComboBox _cmbMechanicVoice = new();
    private readonly Button _btnTestMechanic = new();

    // --- The Team Principal (17 Oct 2026) ---
    private readonly CheckBox _chkPrincipalBefore = new ThemedCheckBox();
    private readonly CheckBox _chkPrincipalAfter = new ThemedCheckBox();
    private readonly CheckBox _chkPrincipalHeats = new ThemedCheckBox();
    private readonly CheckBox _chkPrincipalTeamsOnly = new ThemedCheckBox();
    private readonly TextBox _txtPrincipalName = new();
    private readonly ComboBox _cmbPrincipalVoice = new();
    private readonly Button _btnTestPrincipal = new();

    // --- The badges reader (10 Oct 2026) - see RaceHonours ---
    private readonly CheckBox _chkBadgeHonours = new ThemedCheckBox();
    private readonly TextBox _txtHonoursName = new();
    private readonly ComboBox _cmbHonoursVoice = new();
    private readonly Button _btnTestHonours = new();

    // --- The Kokoro voice each of them switches to when ElevenLabs runs
    // out (5 Nov 2026, asked for by Asher: "each cast member should have
    // their own kokoro voice that is configurable underneath the eleven
    // labs"). One under each voice picker - see SecondVoice.StandInKokoroVoice.
    private readonly ComboBox _cmbRecapHostStandIn = new();
    private readonly ComboBox _cmbCoHostStandIn = new();
    private readonly ComboBox _cmbMechanicStandIn = new();
    private readonly ComboBox _cmbPrincipalStandIn = new();
    private readonly ComboBox _cmbHonoursStandIn = new();

    // Test Voice beside each of them (6 Nov 2026, asked for by Asher), so a
    // club can hear the stand-in without running ElevenLabs dry first.
    private readonly Button _btnTestRecapHostStandIn = new();
    private readonly Button _btnTestCoHostStandIn = new();
    private readonly Button _btnTestMechanicStandIn = new();
    private readonly Button _btnTestPrincipalStandIn = new();
    private readonly Button _btnTestHonoursStandIn = new();

    /// <summary>Each stand-in picker with the voice picker above it, what it shows and where a pick is saved - set up in BuildLayout.</summary>
    private StandInRow[] _standIns = Array.Empty<StandInRow>();

    /// <summary>The stand-in pickers' widths as last measured, one each - see Pieces.PickerWidth.</summary>
    private readonly (Font Font, int Count, int Width)?[] _standInWidths = new (Font Font, int Count, int Width)?[5];

    /// <summary>
    /// One "If ElevenLabs runs out" picker: its own list and Test Voice, the
    /// voice picker above it, the voice it shows, the setting a pick goes
    /// to, and the host's hook that says that speaker's test line.
    /// </summary>
    private sealed record StandInRow(
        ComboBox Picker, Button Test, ComboBox VoicePicker, Func<SecondVoice> Voice, Action<string> Save, Func<Action<SecondVoice>?> Speak);

    /// <summary>The caption of every stand-in row, kept in one place.</summary>
    internal const string StandInCaption = "If ElevenLabs runs out:";

    // --- Load Voices, at the top, for every list ---
    private readonly Button _btnLoadVoices = new();

    /// <summary>Which engine's voices the lists hold - see FillVoices.</summary>
    private readonly Label _lblVoiceSource = new();

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>The pickers' widths as last measured - see Pieces.PickerWidth. Forgotten whenever the lists are filled.</summary>
    private (Font Font, int Count, int Width)? _mechanicVoiceWidth;
    private (Font Font, int Count, int Width)? _principalVoiceWidth;
    private (Font Font, int Count, int Width)? _coHostVoiceWidth;
    private (Font Font, int Count, int Width)? _coHostStanceWidth;
    private (Font Font, int Count, int Width)? _exchangeTurnsWidth;
    private (Font Font, int Count, int Width)? _honoursVoiceWidth;
    private (Font Font, int Count, int Width)? _recapHostVoiceWidth;

    /// <summary>The usual caption of the button that says so while it works - see BuildLayout's Steady.</summary>
    private const string LoadVoicesCaption = "Load Voices";

    /// <summary>The boxes' and pickers' widths at the normal text size - see SizeFields. The ones they had on the Commentary page.</summary>
    private const int VoicePickerWidth = 260;

    /// <summary>The engine the lists were last filled for - what a pick is saved against.</summary>
    private VoiceEngine _voiceListEngine = VoiceEngine.ElevenLabs;

    /// <summary>
    /// The voices Load Voices last brought back, and for which engine - kept
    /// so the lists can be set out again (Kokoro downloaded, the page shown
    /// again) without the loaded voices going from them.
    /// </summary>
    private (VoiceEngine Engine, IReadOnlyList<VoiceChoice> Voices)? _loaded;

    /// <summary>Whether the lists were last filled with the Kokoro voices on them - see FillVoices.</summary>
    private bool _kokoroOffered;

    /// <summary>Set while the lists are being filled, so filling them isn't taken for a pick.</summary>
    private bool _fillingVoices;

    // Guards against a second click while the voices load. Used INSTEAD of
    // disabling the button: disabling the focused control pushes focus to
    // the next tab stop, and the page's scrolling then scrolls to it.
    private bool _loadingVoices;

    /// <summary>A voice one of the cast can be given - what is saved for it, and what the list shows.</summary>
    /// <param name="Id">The ElevenLabs voice id, the HeatCaller voice's id, or Kokoro's name for the voice. Empty for the announcer's own voice.</param>
    /// <param name="Rev">The HeatCaller catalogue's fingerprint for the voice; empty for the other engines.</param>
    /// <param name="IsKokoro">
    /// A Kokoro voice on the list of another engine (25 Oct 2026): picked,
    /// the speaker talks in it whatever engine the announcer uses - see
    /// SecondVoice.KokoroAlways. False for the engine's own voices, Kokoro's
    /// included when Kokoro is the engine.
    /// </param>
    public sealed record VoiceChoice(string Id, string Name, string Rev, string Display, bool IsKokoro = false)
    {
        public override string ToString() => Display;
    }

    private static readonly VoiceChoice AnnouncersVoice = new(string.Empty, string.Empty, string.Empty, "(the announcer's own voice)");

    /// <summary>Which engine speaks, as chosen on the Voice tab. Set by the host; without it the lists offer ElevenLabs.</summary>
    internal Func<VoiceEngine>? EngineInUse { get; set; }

    /// <summary>Fetches the voices one engine offers. Set by the host, which has the clients. Throws with a sentence for the page when it can't.</summary>
    internal Func<VoiceEngine, Task<IReadOnlyList<VoiceChoice>>>? LoadVoices { get; set; }

    /// <summary>
    /// Fills the announcer's own ElevenLabs voice list, on the Announcer card
    /// (VoiceSettingsControl.LoadAnnouncerElevenLabsVoicesAsync) - Load Voices
    /// fills every list on the page since its own button beside that list
    /// went (25 Oct 2026). Never throws: it says what happened under the list.
    /// </summary>
    internal Func<Task>? LoadAnnouncerVoices { get; set; }

    /// <summary>Says a test line in the mechanic's voice, even while the announcer is muted. Set by the host.</summary>
    internal Action<SecondVoice>? SpeakAsMechanic { get; set; }

    /// <summary>The same for the Team Principal, with a line of the kind they say. Set by the host.</summary>
    internal Action<SecondVoice>? SpeakAsPrincipal { get; set; }

    /// <summary>
    /// The same for the badges reader, with a line of the kind they
    /// actually say - its own hook, because the hook carries the test LINE
    /// as well as the voice.
    /// </summary>
    internal Action<SecondVoice>? SpeakAsHonours { get; set; }

    /// <summary>The same for the race recap host, with a line of the kind a recap opens on. Set by the host.</summary>
    internal Action<SecondVoice>? SpeakAsRecapHost { get; set; }

    /// <summary>The same for the co-host, with a line of the kind they answer the announcer with. Set by the host.</summary>
    internal Action<SecondVoice>? SpeakAsCoHost { get; set; }

    public CastControl(AppSettings settings)
    {
        _settings = settings;

        Dock = DockStyle.Fill;
        ForeColor = AppTheme.TextDark;

        // The body face, worn rather than set once, so it follows the text
        // size - see TextSize. The page sits in a tab whose row is drawn in
        // the bold face, which it would otherwise take.
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout();
    }

    /// <summary>The page's own face changed - handed the body face at a new text size - so everything else it keeps in step follows. See TextSize.</summary>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>A new text size, reaching this page through its parent's font. See TextSize.</summary>
    protected override void OnParentFontChanged(EventArgs e)
    {
        base.OnParentFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>
    /// The engine and the switches that grey this page are on other tabs,
    /// so the lists are set out again for the engine in use and the greying
    /// worked out again whenever this page is shown.
    /// </summary>
    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);

        if (!Visible)
        {
            return;
        }

        // Again, too, when Kokoro has come or gone since (downloaded on the
        // Voice tab), so its voices are on the lists or off them.
        if ((EngineInUse?.Invoke() is { } engine && engine != _voiceListEngine)
            || KokoroOffered(_voiceListEngine) != _kokoroOffered)
        {
            FillVoices(EngineInUse?.Invoke());
        }

        // The AI Commentary tab's "Throw to pit lane" is this switch too
        // (18 Oct 2026), so it is read again in case it was changed there.
        if (_chkPrincipalBefore.Checked != _settings.PrincipalBeforeRace)
        {
            _chkPrincipalBefore.Checked = _settings.PrincipalBeforeRace;
        }

        // And the after-the-flag interviews (18 Oct 2026), on its Segments too.
        if (_chkInterview.Checked != _settings.RecapMechanicInterview)
        {
            _chkInterview.Checked = _settings.RecapMechanicInterview;
        }

        if (_chkPrincipalAfter.Checked != _settings.PrincipalAfterRace)
        {
            _chkPrincipalAfter.Checked = _settings.PrincipalAfterRace;
        }

        // And the achievements, which AI Commentary's After the flag switch
        // turns on and off in Simple mode (1 Nov 2026 - CommentarySections).
        if (_chkBadgeHonours.Checked != _settings.RecapBadgeHonours)
        {
            _chkBadgeHonours.Checked = _settings.RecapBadgeHonours;
        }

        RefreshEnabledState();
    }

    private bool IsCommentaryOn => _settings.CommentaryEnabled;

    /// <summary>
    /// A card per speaker on the layout pieces (LayoutPieces.cs), under the
    /// one Load Voices. One caption column down every card
    /// (FieldGrid.LineUp), a setting that belongs to a switch a step in
    /// (Stepped), and the page scrolls when it is taller than the window
    /// (PageStack) - the way they were laid out on the Commentary page.
    /// </summary>
    private void BuildLayout()
    {
        Label Hint(string text) => _textSize.Wear(
            new Label { Text = text, AutoSize = true, ForeColor = AppTheme.TextMuted },
            () => AppTheme.SmallFont);

        Label Suffix(string text)
        {
            var label = new Label { Text = text, AutoSize = true, ForeColor = AppTheme.TextMuted };
            _textSize.Wear(label, () => AppTheme.SmallFont);
            return label;
        }

        static CheckBox Check(CheckBox box, string text)
        {
            box.Text = text;
            box.AutoSize = true;
            return box;
        }

        // A setting that belongs to the switch above it has its caption a
        // step in; one that is not keeps the step on its right, so the
        // fields still line up down the page.
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

        static FlowLayoutPanel Line(params Control[] controls) => FieldGrid.Inline(controls);

        // A button whose caption is swapped while it works ("Loading..."),
        // in a panel of its own that is always as big as its usual caption
        // needs - see CommentaryControl.BuildLayout's Steady.
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

        var lblIntro = new Label
        {
            Text = "Everyone on the show with a name and a voice. Apart from the announcer's race calls, they are only "
                 + "heard in AI commentary, so most of this page grays out while it is off.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        // ------------------------------------------------------------------
        // Load Voices - ONE, for every list on the page
        // ------------------------------------------------------------------

        _btnLoadVoices.Text = LoadVoicesCaption;
        _btnLoadVoices.Click += async (_, _) => await LoadVoicesAsync();

        // Which engine's voices the lists hold - the one chosen on the Voice
        // tab. Last on the line, so its text can change without moving
        // anything.
        _lblVoiceSource.AutoSize = true;
        _lblVoiceSource.ForeColor = AppTheme.TextMuted;
        _textSize.Wear(_lblVoiceSource, () => AppTheme.SmallFont);

        var loadGrid = new FieldGrid();

        Flush(loadGrid.AddRow("Voices:", Line(Steady(_btnLoadVoices, LoadVoicesCaption), _lblVoiceSource)));
        loadGrid.AddHint(
            "Fills every voice list on this tab, the announcer's included, from the engine chosen on the Voice tab. Each engine keeps its own picks; "
            + "with none picked, the announcer's own voice speaks. The Kokoro voices on this PC are on every list too, "
            + "whatever the engine: pick one and that speaker talks in Kokoro's free voice even beside an ElevenLabs "
            + "or HeatCaller announcer. Under each voice is the Kokoro voice they switch to if ElevenLabs runs out - "
            + "and while it is out no saved ElevenLabs line plays, so the night stays in one set of voices.");

        var voices = new Section("Voices");

        voices.Add(loadGrid, gapBelow: 0);

        // ------------------------------------------------------------------
        // The announcer
        // ------------------------------------------------------------------

        _txtAnnouncerName.MaxLength = 60;
        _txtAnnouncerName.Text = _settings.RecapAnnouncerName ?? string.Empty;
        _txtAnnouncerName.TextChanged += (_, _) =>
        {
            _settings.RecapAnnouncerName = _txtAnnouncerName.Text.Trim();
            _settings.Save();
        };

        var announcerGrid = new FieldGrid();

        Flush(announcerGrid.AddRow("Announcer's name:", Line(_txtAnnouncerName, Suffix("said in the On Air openings and the cold open; blank and the announcer is never named"))));

        var announcer = new Section("Announcer");

        // ONE ORDER ON EVERY CARD (25 Oct 2026, asked for by Asher): the
        // speaker's name first, then their voice, then what they do and the
        // settings for it.
        // The voice rows go under the name when the Voice tab hands them
        // over (HostAnnouncerVoice).
        _announcerName = announcer.Add(announcerGrid, gapBelow: UiLayout.RowGap);
        announcer.Add(Hint(
            "The voice carrying the whole program, from the engine chosen on the Voice tab."), gapBelow: 0);
        _announcerCard = announcer;

        // ------------------------------------------------------------------
        // The race recap host
        // ------------------------------------------------------------------

        // The recap host's own name - see AppSettings.RecapHostName.
        _txtRecapHostName.MaxLength = 60;
        _txtRecapHostName.Text = _settings.RecapHostName ?? string.Empty;
        _txtRecapHostName.TextChanged += (_, _) =>
        {
            _settings.RecapHostName = _txtRecapHostName.Text.Trim();
            _settings.Save();
        };

        _cmbRecapHostVoice.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbRecapHostVoice.SelectedIndexChanged += (_, _) => SaveRecapHostVoice();

        _btnTestRecapHost.Text = "Test Voice";
        _btnTestRecapHost.Click += (_, _) => SpeakAsRecapHost?.Invoke(_settings.RecapHostVoice);

        Check(_chkBroadcastVoice, "Recap voice delivery - give the recap its own stability and style");
        _chkBroadcastVoice.Checked = _settings.SummaryUseBroadcastVoice;
        _chkBroadcastVoice.CheckedChanged += (_, _) =>
        {
            _settings.SummaryUseBroadcastVoice = _chkBroadcastVoice.Checked;
            _settings.Save();
            RefreshEnabledState();
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

        var recapHostGrid = new FieldGrid();

        Flush(recapHostGrid.AddRow("Recap host's name:", Line(_txtRecapHostName, Suffix("said when the recap opens and at its sign-off; blank and the host is never named"))));
        Flush(recapHostGrid.AddRow("Recap host's voice:", Line(_cmbRecapHostVoice, _btnTestRecapHost)));
        Stepped(recapHostGrid.AddRow(StandInCaption, Line(_cmbRecapHostStandIn, _btnTestRecapHostStandIn)));

        // Stability and style on lines of their own, so neither is ever
        // placed from the width of the other's note.
        var deliveryGrid = new FieldGrid();

        Stepped(deliveryGrid.AddRow("Stability:", Line(_numStability, Suffix("lower = more emotional range"))));
        Stepped(deliveryGrid.AddRow("Style:", Line(_numStyleEx, Suffix("higher = more dramatic; past ~0.6 it can distort"))));

        var recapHost = new Section("Recap host");

        // ONE ORDER ON EVERY CARD (25 Oct 2026, asked for by Asher): the
        // speaker's name first, then their voice, then what they do and the
        // settings for it.
        recapHost.Add(recapHostGrid);
        // What they do under the card's fields, from its left edge, as every
        // other card has it - it used to sit in the field column (6 Nov 2026).
        recapHost.Add(Hint(
            "Reads the recap, the questions to the Technical Lead and the sign-off; the announcer keeps the race calls. "
            + "Heard only when race recaps are switched on, on the AI Commentary tab's Recap show card."), gapBelow: UiLayout.SectionGap);
        recapHost.Add(_chkBroadcastVoice, stretch: false, gapBelow: UiLayout.HintGap);
        recapHost.Add(Hint(
            "Overrides the Voice tab's stability and style for the recap only, so lap calls aren't over-acted. "
            + "Untick it to use the Voice tab's values everywhere."));
        recapHost.Add(deliveryGrid, gapBelow: 0);

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
            RefreshEnabledState();
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

        // Read as it is saved, so the test is the voice the list now shows.
        _btnTestCoHost.Text = "Test Voice";
        _btnTestCoHost.Click += (_, _) => SpeakAsCoHost?.Invoke(_settings.CoHostVoice);

        // THE STANCE. Filled from CoHostStance.All, and the handler attached
        // AFTER the list is filled and the club's own choice selected, for
        // the reason ShowTreatmentCard.FillLibrary gives - filling a box
        // raises SelectedIndexChanged, and a club whose stance had gone
        // would have it quietly saved as the first one in the list.
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

        _lblCoHostStance.AutoSize = true;
        _lblCoHostStance.ForeColor = AppTheme.TextMuted;
        _textSize.Wear(_lblCoHostStance, () => AppTheme.SmallFont);
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
            RefreshEnabledState();
        };

        // The name and voice first, above the switch (25 Oct 2026) - see the card below.
        var coHostVoiceGrid = new FieldGrid();

        Flush(coHostVoiceGrid.AddRow("Co-host's name:", Line(_txtCoHostName, Suffix("blank and they are talked to but never named - the model never makes one up"))));
        Flush(coHostVoiceGrid.AddRow("Co-host's voice:", Line(_cmbCoHostVoice, _btnTestCoHost)));
        Stepped(coHostVoiceGrid.AddRow(StandInCaption, Line(_cmbCoHostStandIn, _btnTestCoHostStandIn)));

        var coHostGrid = new FieldGrid();

        Stepped(coHostGrid.AddRow("What they're like:", Line(_cmbCoHostStance)));
        coHostGrid.AddHint(_lblCoHostStance);

        Stepped(coHostGrid.AddRow("How much they talk:", Line(_cmbExchangeTurns)));
        coHostGrid.AddHint(
            "Each turn is another line the AI writes - and, on the relay, another line of the club's month.");

        Stepped(coHostGrid.AddRow(string.Empty, _chkTwoVoicesInHeats));
        coHostGrid.AddHint(
            "Normally kept for the mains, which helps a main sound like one. Ticking this doesn't make the night any chattier.");

        var coHost = new Section("Co-host");

        // ONE ORDER ON EVERY CARD (25 Oct 2026, asked for by Asher): the
        // speaker's name first, then their voice, then what they do and the
        // settings for it.
        coHost.Add(coHostVoiceGrid);
        coHost.Add(Hint(
            "A second commentator who answers the announcer in the segments marked as a two-hander on the AI "
            + "Commentary tab, and asks the questions in the Technical Lead's and the Team Principal's interviews. "
            + "Grayed out while every one of those is switched off."));
        coHost.Add(_chkCoHost, stretch: false);
        coHost.Add(coHostGrid, gapBelow: 0);

        // ------------------------------------------------------------------
        // The winner's Technical Lead (called the mechanic until 17 Oct 2026)
        // ------------------------------------------------------------------

        Check(_chkInterview, "After a main, down to pit lane for a word with the winner's Technical Lead");
        _chkInterview.Checked = _settings.RecapMechanicInterview;
        _chkInterview.CheckedChanged += (_, _) =>
        {
            _settings.RecapMechanicInterview = _chkInterview.Checked;
            _settings.Save();
            RefreshEnabledState();
        };

        Check(_chkInterviewHeats, "After heats too, not only mains");
        _chkInterviewHeats.Checked = _settings.RecapMechanicInterviewHeats;
        _chkInterviewHeats.CheckedChanged += (_, _) =>
        {
            _settings.RecapMechanicInterviewHeats = _chkInterviewHeats.Checked;
            _settings.Save();
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

        _btnTestMechanic.Text = "Test Voice";
        _btnTestMechanic.Click += (_, _) => SpeakAsMechanic?.Invoke(_settings.MechanicVoice);

        var mechanicGrid = new FieldGrid();

        Flush(mechanicGrid.AddRow("Technical Lead's name:", Line(_txtMechanicName, Suffix("blank for \"the team's technical lead\" - the model never makes a name up"))));
        Flush(mechanicGrid.AddRow("Technical Lead's voice:", Line(_cmbMechanicVoice, _btnTestMechanic)));
        Stepped(mechanicGrid.AddRow(StandInCaption, Line(_cmbMechanicStandIn, _btnTestMechanicStandIn)));

        var mechanic = new Section("Technical Lead");

        // ONE ORDER ON EVERY CARD (25 Oct 2026, asked for by Asher): the
        // speaker's name first, then their voice, then what they do and the
        // settings for it.
        mechanic.Add(mechanicGrid);
        mechanic.Add(_chkInterview, stretch: false, gapBelow: UiLayout.HintGap);
        mechanic.Add(_chkInterviewHeats, stretch: false, gapBelow: UiLayout.HintGap);
        mechanic.Add(Hint(
            "Straight after the finish call, the co-host in pit lane asks two questions about the race and any setup "
            + "changes logged in the Garage for the winning car, then hands back before the break. Needs race recaps on."), gapBelow: 0);

        // ------------------------------------------------------------------
        // The Team Principal (17 Oct 2026)
        // ------------------------------------------------------------------

        Check(_chkPrincipalBefore, "Before a race, a quick word with a Team Principal");
        _chkPrincipalBefore.Checked = _settings.PrincipalBeforeRace;
        _chkPrincipalBefore.CheckedChanged += (_, _) =>
        {
            _settings.PrincipalBeforeRace = _chkPrincipalBefore.Checked;
            _settings.Save();
            RefreshEnabledState();
        };

        Check(_chkPrincipalAfter, "On the recap show, the winner's Team Principal in the booth");
        _chkPrincipalAfter.Checked = _settings.PrincipalAfterRace;
        _chkPrincipalAfter.CheckedChanged += (_, _) =>
        {
            _settings.PrincipalAfterRace = _chkPrincipalAfter.Checked;
            _settings.Save();
            RefreshEnabledState();
        };

        Check(_chkPrincipalHeats, "Heats as well as mains");
        _chkPrincipalHeats.Checked = _settings.PrincipalInHeats;
        _chkPrincipalHeats.CheckedChanged += (_, _) =>
        {
            _settings.PrincipalInHeats = _chkPrincipalHeats.Checked;
            _settings.Save();
        };

        Check(_chkPrincipalTeamsOnly, "Only for drivers with a team on their profile");
        _chkPrincipalTeamsOnly.Checked = _settings.PrincipalTeamsOnly;
        _chkPrincipalTeamsOnly.CheckedChanged += (_, _) =>
        {
            _settings.PrincipalTeamsOnly = _chkPrincipalTeamsOnly.Checked;
            _settings.Save();
        };

        _txtPrincipalName.MaxLength = 40;
        _txtPrincipalName.Text = _settings.PrincipalName ?? string.Empty;
        _txtPrincipalName.TextChanged += (_, _) =>
        {
            _settings.PrincipalName = _txtPrincipalName.Text.Trim();
            _settings.Save();
        };

        _cmbPrincipalVoice.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbPrincipalVoice.SelectedIndexChanged += (_, _) => SavePrincipalVoice();

        _btnTestPrincipal.Text = "Test Voice";
        _btnTestPrincipal.Click += (_, _) => SpeakAsPrincipal?.Invoke(_settings.PrincipalVoice);

        var principalGrid = new FieldGrid();

        Flush(principalGrid.AddRow("Team Principal's name:", Line(_txtPrincipalName, Suffix("blank for \"the team principal\" - the model never makes a name up"))));
        Flush(principalGrid.AddRow("Team Principal's voice:", Line(_cmbPrincipalVoice, _btnTestPrincipal)));
        Stepped(principalGrid.AddRow(StandInCaption, Line(_cmbPrincipalStandIn, _btnTestPrincipalStandIn)));

        var principal = new Section("Team Principal");

        // ONE ORDER ON EVERY CARD (25 Oct 2026, asked for by Asher): the
        // speaker's name first, then their voice, then what they do and the
        // settings for it.
        principal.Add(principalGrid);
        principal.Add(Hint(
            "The big picture, where the Technical Lead has the car: the result, the points, the championship and the "
            + "rivalry - never the setup. A driver with a team gets that team's principal; one without gets their own."),
            gapBelow: UiLayout.RowGap);
        principal.Add(_chkPrincipalBefore, stretch: false, gapBelow: UiLayout.HintGap);
        principal.Add(Hint(
            "When a race is staged: two questions about how the team is feeling and what it is hoping for, for the "
            + "driver with the most riding on it - each driver's principal once a night. The countdown waits for it, and "
            + "it is skipped when the night is running ten minutes or more behind."));
        principal.Add(_chkPrincipalAfter, stretch: false, gapBelow: UiLayout.HintGap);
        principal.Add(Hint(
            "The recap host has them in the booth later that night for two questions about what the win means to the "
            + "team. Part of the recap show, so it needs race recaps on."));
        principal.Add(_chkPrincipalHeats, stretch: false, gapBelow: UiLayout.HintGap);
        principal.Add(_chkPrincipalTeamsOnly, stretch: false, gapBelow: 0);

        // ------------------------------------------------------------------
        // The badges reader
        // ------------------------------------------------------------------

        Check(_chkBadgeHonours, "After a race, a third voice reads out the achievements it unlocked");
        _chkBadgeHonours.Checked = _settings.RecapBadgeHonours;
        _chkBadgeHonours.CheckedChanged += (_, _) =>
        {
            _settings.RecapBadgeHonours = _chkBadgeHonours.Checked;
            _settings.Save();
            RefreshEnabledState();
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

        var honoursGrid = new FieldGrid();

        Flush(honoursGrid.AddRow("Reader's name:", Line(_txtHonoursName, Suffix("blank and they are thrown to but never named - no name is made up"))));
        Flush(honoursGrid.AddRow("Reader's voice:", Line(_cmbHonoursVoice, _btnTestHonours)));
        Stepped(honoursGrid.AddRow(StandInCaption, Line(_cmbHonoursStandIn, _btnTestHonoursStandIn)));

        var honours = new Section("Achievements reader");

        // ONE ORDER ON EVERY CARD (25 Oct 2026, asked for by Asher): the
        // speaker's name first, then their voice, then what they do and the
        // settings for it.
        honours.Add(honoursGrid);
        honours.Add(_chkBadgeHonours, stretch: false, gapBelow: UiLayout.HintGap);
        honours.Add(Hint(
            "Ticking this MOVES the achievement calls from the flag into the recap, so nobody hears an achievement twice. Untick it "
            + "and the calls at the flag come back, as set up on Race Calls."), gapBelow: 0);

        // ------------------------------------------------------------------
        // The page
        // ------------------------------------------------------------------

        var page = new PageStack();

        // The one Load Voices first (25 Oct 2026, asked for by Asher): it fills
        // every list on the page, the announcer's included since that list's
        // own Load Voices went, so it goes above all of them. Then the
        // announcer, the voice everyone hears all night (22 Oct 2026).
        // Simple mode (1 Nov 2026, Asher's pick): Voices, the Announcer and the
        // Co-host's tick, name, voice and "in the heats too"; the rest of the
        // cast, and what the co-host is like and how much they talk, are
        // Advanced. See SimpleMode.
        page.Add(lblIntro, gapBelow: UiLayout.RowGap);
        page.Add(voices);
        page.Add(announcer);
        page.Add(SimpleMode.Hide(recapHost));
        page.Add(coHost);
        page.Add(SimpleMode.Hide(mechanic));
        page.Add(SimpleMode.Hide(principal));
        page.Add(SimpleMode.Hide(honours), gapBelow: 0);
        page.Add(SimpleMode.MoreLine(), stretch: false, gapBelow: 0);

        SimpleMode.HideRow(coHostGrid, _cmbCoHostStance);
        SimpleMode.HideRow(coHostGrid, _cmbExchangeTurns);

        // The Kokoro voice for when ElevenLabs runs out is a refinement, so
        // Advanced (5 Nov 2026) - in Simple mode each of the cast still has a
        // voice of their own, CastStandIns' default for them.
        SimpleMode.HideRow(coHostVoiceGrid, _cmbCoHostStandIn);

        _standIns = new[]
        {
            new StandInRow(_cmbRecapHostStandIn, _btnTestRecapHostStandIn, _cmbRecapHostVoice, () => _settings.RecapHostVoice, name => _settings.RecapHostKokoroStandInVoiceName = name, () => SpeakAsRecapHost),
            new StandInRow(_cmbCoHostStandIn, _btnTestCoHostStandIn, _cmbCoHostVoice, () => _settings.CoHostVoice, name => _settings.CoHostKokoroStandInVoiceName = name, () => SpeakAsCoHost),
            new StandInRow(_cmbMechanicStandIn, _btnTestMechanicStandIn, _cmbMechanicVoice, () => _settings.MechanicVoice, name => _settings.MechanicKokoroStandInVoiceName = name, () => SpeakAsMechanic),
            new StandInRow(_cmbPrincipalStandIn, _btnTestPrincipalStandIn, _cmbPrincipalVoice, () => _settings.PrincipalVoice, name => _settings.PrincipalKokoroStandInVoiceName = name, () => SpeakAsPrincipal),
            new StandInRow(_cmbHonoursStandIn, _btnTestHonoursStandIn, _cmbHonoursVoice, () => _settings.HonoursVoice, name => _settings.HonoursKokoroStandInVoiceName = name, () => SpeakAsHonours),
        };

        foreach (var row in _standIns)
        {
            row.Picker.DropDownStyle = ComboBoxStyle.DropDownList;
            row.Picker.SelectedIndexChanged += (_, _) => SaveStandIn(row);

            row.Test.Text = "Test Voice";
            row.Test.Click += (_, _) => TestStandIn(row);

            // Whether the row means anything follows the voice picked above it.
            row.VoicePicker.SelectedIndexChanged += (_, _) => RefreshEnabledState();
        }

        FillStandIns();

        Controls.Add(page);

        // One caption column down every card - every grid on the page, since
        // a grid left out of this still looks correct on its own.
        _ownGrids = new[]
        {
            loadGrid, announcerGrid, recapHostGrid, deliveryGrid, coHostVoiceGrid, coHostGrid, mechanicGrid, principalGrid, honoursGrid,
        };

        _textSize.Keep(LineUpGrids);

        _textSize.Keep(SizeFields);

        FillVoices(null);
        RefreshEnabledState();
    }

    /// <summary>
    /// One caption column down every card - every grid on the page, the
    /// announcer's voice rows from the Voice tab included while they are
    /// here, since a grid left out of this still looks correct on its own.
    /// </summary>
    private void LineUpGrids() =>
        FieldGrid.LineUp(_announcerVoice is { } voice ? _ownGrids.Append(voice).ToArray() : _ownGrids);

    /// <summary>
    /// Puts the announcer's voice rows - the Voice tab's pickers and Tests
    /// (VoiceSettingsControl.AnnouncerVoiceGrid) - at the top of the
    /// Announcer card, under its first line. The host's to call, once the
    /// Voice tab is built and again whenever it is built anew; give the old
    /// rows back first (ReleaseAnnouncerVoice).
    /// </summary>
    internal void HostAnnouncerVoice(FieldGrid voice)
    {
        if (_announcerCard is null || _announcerName is null || ReferenceEquals(_announcerVoice, voice))
        {
            return;
        }

        ReleaseAnnouncerVoice();

        _announcerVoice = voice;
        _announcerCard.AddAfter(_announcerName, voice, gapBelow: UiLayout.RowGap);

        // Built on the Voice tab, outside this page, so the theming pass
        // that walked this one has never seen it - as the Race Calls page is
        // themed by the Voice tab for the same reason.
        AppTheme.ApplyRecursively(voice);
        LineUpGrids();
    }

    /// <summary>
    /// Takes the announcer's voice rows back out, for the host to dispose
    /// with the Voice tab that built them - a soundscape rebuilds that tab,
    /// and disposing this page's card must never be what disposes them.
    /// Null when there were none.
    /// </summary>
    internal FieldGrid? ReleaseAnnouncerVoice()
    {
        if (_announcerVoice is not { } voice)
        {
            return null;
        }

        _announcerVoice = null;
        _announcerCard?.RemovePart(voice);
        LineUpGrids();

        return voice;
    }

    /// <summary>
    /// The fields' sizes at the text size in use: the boxes the widths they
    /// had at the normal text size, at this size; the pickers as wide as
    /// their longest choice and never narrower than they were then; each
    /// number box as wide as its largest value and its arrows need. Run
    /// again when the text size changes and when the lists are filled.
    /// </summary>
    private void SizeFields()
    {
        var voice = UiLayout.AtTextSize(VoicePickerWidth);

        // EVERY VOICE PICKER ONE WIDTH (6 Nov 2026, asked for by Asher): each
        // speaker's voice and their "If ElevenLabs runs out" voice, on every
        // card, as wide as the longest choice on any of them - so every
        // Test Voice sits in one column down the page instead of wherever
        // its own list happened to end.
        var widest = new[]
        {
            Pieces.PickerWidth(_cmbMechanicVoice, ref _mechanicVoiceWidth),
            Pieces.PickerWidth(_cmbPrincipalVoice, ref _principalVoiceWidth),
            Pieces.PickerWidth(_cmbCoHostVoice, ref _coHostVoiceWidth),
            Pieces.PickerWidth(_cmbHonoursVoice, ref _honoursVoiceWidth),
            Pieces.PickerWidth(_cmbRecapHostVoice, ref _recapHostVoiceWidth),
        }.Concat(_standIns.Select((row, i) => Pieces.PickerWidth(row.Picker, ref _standInWidths[i])))
            .Append(voice)
            .Max();

        foreach (var picker in new[] { _cmbMechanicVoice, _cmbPrincipalVoice, _cmbCoHostVoice, _cmbHonoursVoice, _cmbRecapHostVoice }
                     .Concat(_standIns.Select(row => row.Picker)))
        {
            Fit(picker, widest);
        }

        // Every name box the same width as the voices under it (6 Nov 2026):
        // they were two widths, so each note after a name started somewhere
        // else, and no name box ended where its card's voice pickers did.
        foreach (var name in new[] { _txtAnnouncerName, _txtRecapHostName, _txtCoHostName, _txtMechanicName, _txtPrincipalName, _txtHonoursName })
        {
            Fit(name, widest);
        }

        // The co-host's settings line up with the voices above them too, and
        // only grow past them for a longer choice of their own - left at the
        // default width, "Conversational - three turns" was cut off.
        Fit(_cmbCoHostStance, Math.Max(widest, Pieces.PickerWidth(_cmbCoHostStance, ref _coHostStanceWidth)));
        Fit(_cmbExchangeTurns, Math.Max(widest, Pieces.PickerWidth(_cmbExchangeTurns, ref _exchangeTurnsWidth)));

        Fit(_numStability, NumberWidth(_numStability));
        Fit(_numStyleEx, NumberWidth(_numStyleEx));

        // Only when it changed: a new size lays the grid or the line out again.
        static void Fit(Control field, int width)
        {
            if (field.Width != width)
            {
                field.Width = width;
            }
        }
    }

    /// <summary>
    /// A number box as wide as its largest value - every digit a nought, with
    /// its decimals - and its arrows need in its own font, and never narrower
    /// than a number box was at the normal text size, at this size.
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
    /// Greys the page by the rules it had on the Commentary page. Called
    /// when anything here changes, and whenever the page is shown - the
    /// master switch, the recaps and the segments are all on AI Commentary.
    /// </summary>
    internal void RefreshEnabledState()
    {
        var on = IsCommentaryOn;
        var summariesOn = on && _settings.CommentaryRaceSummaries;

        // NOT tied to the recap. This name is also used by the COLD OPEN,
        // which is a segment of its own, so a club running the cold open
        // with the recap's broadcast dressing switched off still needs
        // somewhere to type it.
        _txtAnnouncerName.Enabled = on;

        // The recap host reads the recap show, so it goes dark with recaps -
        // and so does how the recap sounds.
        _cmbRecapHostVoice.Enabled = summariesOn;
        _btnTestRecapHost.Enabled = summariesOn;
        _txtRecapHostName.Enabled = summariesOn;
        _chkBroadcastVoice.Enabled = summariesOn;
        _numStability.Enabled = summariesOn && _chkBroadcastVoice.Checked;
        _numStyleEx.Enabled = summariesOn && _chkBroadcastVoice.Checked;

        // The interview is part of the recap's broadcast.
        _chkInterview.Enabled = summariesOn;

        var interviewOn = summariesOn && _chkInterview.Checked;
        _chkInterviewHeats.Enabled = interviewOn;
        _txtMechanicName.Enabled = interviewOn;
        _cmbMechanicVoice.Enabled = interviewOn;
        _btnTestMechanic.Enabled = interviewOn;

        // The Team Principal before a race needs only the commentary; after
        // one, the recap it is part of (17 Oct 2026).
        _chkPrincipalBefore.Enabled = on;
        _chkPrincipalAfter.Enabled = summariesOn;

        var principalOn = (on && _chkPrincipalBefore.Checked) || (summariesOn && _chkPrincipalAfter.Checked);
        _chkPrincipalHeats.Enabled = principalOn;
        _chkPrincipalTeamsOnly.Enabled = principalOn;
        _txtPrincipalName.Enabled = principalOn;
        _cmbPrincipalVoice.Enabled = principalOn;
        _btnTestPrincipal.Enabled = principalOn;

        // The honours ride on the recap, so they go dark with recaps - but
        // their reader's rows stay live when only the interview is off,
        // because the two are separate speakers with separate switches.
        _chkBadgeHonours.Enabled = summariesOn;

        var honoursOn = summariesOn && _chkBadgeHonours.Checked;
        _txtHonoursName.Enabled = honoursOn;
        _cmbHonoursVoice.Enabled = honoursOn;
        _btnTestHonours.Enabled = honoursOn;

        // The co-host is only worth setting up when something is going to
        // use them, so the row goes dark when every two-hander segment is
        // switched off on AI Commentary as well as when commentary is. Read
        // from the saved switches, which that page writes as each box is
        // ticked - see CommentaryControl.AnyTwoHanderSwitchedOn.
        //
        // And the interviews (22 Oct 2026, bug hunt): since 18 Oct the
        // co-host asks the Technical Lead's questions at the flag and the
        // Team Principal's before a race and in the booth, so with any of
        // those on the co-host is in use even with no two-hander ticked.
        var anyExchange = CommentaryControl.AnyTwoHanderSwitchedOn(_settings)
            || (on && _chkPrincipalBefore.Checked)
            || (summariesOn && (_chkInterview.Checked || _chkPrincipalAfter.Checked));

        _chkCoHost.Enabled = on && anyExchange;

        var coHostOn = on && _chkCoHost.Checked;
        _cmbExchangeTurns.Enabled = coHostOn;
        _chkTwoVoicesInHeats.Enabled = coHostOn;
        _txtCoHostName.Enabled = coHostOn && anyExchange;
        _cmbCoHostVoice.Enabled = coHostOn && anyExchange;
        _btnTestCoHost.Enabled = coHostOn && anyExchange;
        _cmbCoHostStance.Enabled = coHostOn && anyExchange;

        // Each stand-in picker with the voice picker above it, and only while
        // it would ever be heard (5 Nov 2026 - see StandInApplies).
        foreach (var row in _standIns)
        {
            row.Picker.Enabled = row.VoicePicker.Enabled && StandInApplies(row);
            row.Test.Enabled = row.Picker.Enabled;
        }

        // Load Voices fills every list in one pass (FillVoices) - and the
        // announcer's (25 Oct 2026), who is always on the show - so it is
        // never dark. It used to go dark with every speaker below switched
        // off, when it had nothing else to fill.
        _btnLoadVoices.Enabled = true;
    }

    // ==================== The voice lists ====================

    private static string EngineLabel(VoiceEngine engine) => engine switch
    {
        VoiceEngine.HeatCaller => "HeatCaller",
        VoiceEngine.Kokoro => "Kokoro",
        _ => "ElevenLabs",
    };

    /// <summary>
    /// Which voice a speaker has saved for an engine, as the picker shows
    /// it - the announcer's own when none is. Shared between every speaker
    /// rather than written for each: they are the same settings in a
    /// different prefix, and copies would drift the first time an engine
    /// was added or retired.
    /// </summary>
    private static VoiceChoice SavedSecondVoice(SecondVoice voice, VoiceEngine engine)
    {
        static string Shown(string name, string id) => string.IsNullOrWhiteSpace(name) ? id : name;

        // A Kokoro voice picked for every engine (25 Oct 2026) is what this
        // engine's list shows for them, ahead of whatever it holds of its own.
        //
        // Said when Kokoro can't speak on this PC (the model deleted, or
        // never downloaded on a PC the settings were restored to): the line
        // then goes out as it did before, so the list says so rather than
        // name a voice nobody will hear. See VoiceProfile.CloneWithVoice.
        if (voice.SpeaksKokoroWith(engine))
        {
            var name = voice.KokoroVoiceName.Trim();

            return KokoroChoice(name, KokoroSpeech.IsModelAvailable
                ? KokoroSpeech.Describe(name)
                : $"{KokoroSpeech.Describe(name)} - not downloaded");
        }

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

    /// <summary>A Kokoro voice on another engine's list, marked as Kokoro's so nobody takes it for one of that engine's.</summary>
    private static VoiceChoice KokoroChoice(string name, string described) =>
        new(name, name, string.Empty, $"Kokoro: {described}", IsKokoro: true);

    /// <summary>
    /// Whether the lists offer the Kokoro voices beside an engine's own: for
    /// every engine but Kokoro (whose own list they are), once the model is
    /// on this PC - a voice offered and then never heard is worse than one
    /// not offered.
    /// </summary>
    private static bool KokoroOffered(VoiceEngine engine) =>
        engine != VoiceEngine.Kokoro && KokoroSpeech.IsModelAvailable;

    /// <summary>
    /// Sets every list out for an engine, in ONE pass: the announcer's own
    /// voice, the voice saved for that engine, and - once loaded - every
    /// voice it offers. A saved voice stays on its list even when the
    /// engine no longer offers it: it is what is set, and the page
    /// shouldn't show otherwise. One pass, so there is no way for two of the
    /// lists to be showing different engines.
    /// </summary>
    /// <param name="engine">The engine to show; null for the one in use.</param>
    /// <param name="loaded">The engine's voices, or null before they have been loaded.</param>
    private void FillVoices(VoiceEngine? engine, IReadOnlyList<VoiceChoice>? loaded = null)
    {
        var shown = engine ?? EngineInUse?.Invoke() ?? VoiceEngine.ElevenLabs;
        _voiceListEngine = shown;

        // Kept for the next fill of the same engine, and used by one with
        // none given - so setting the lists out again never empties them.
        if (loaded is not null)
        {
            _loaded = (shown, loaded);
        }
        else if (_loaded is { } kept && kept.Engine == shown)
        {
            loaded = kept.Voices;
        }

        // Kokoro's voices are the names of files beside the exe - nothing to
        // load or ask for - so with Kokoro the engine they are there from the
        // start, Load Voices or not.
        if (loaded is null && shown == VoiceEngine.Kokoro)
        {
            loaded = KokoroSpeech.InstalledVoices()
                .Select(v => new VoiceChoice(v.Name, v.Name, string.Empty, v.Display))
                .ToList();
        }

        var choices = (loaded ?? Array.Empty<VoiceChoice>()).ToList();

        // And every other engine's lists end with them (25 Oct 2026), so any
        // of the cast can be given a Kokoro voice whatever the announcer uses.
        _kokoroOffered = KokoroOffered(shown);

        if (_kokoroOffered)
        {
            choices.AddRange(KokoroSpeech.InstalledVoices().Select(v => KokoroChoice(v.Name, v.Display)));
        }

        _fillingVoices = true;

        try
        {
            // Through ONE local function rather than a block each - copies
            // are how lists start disagreeing about which entry means the
            // announcer's own voice and where the saved-but-no-longer-offered
            // one goes.
            void Fill(ComboBox picker, VoiceChoice savedForThem)
            {
                var theirs = choices.ToList();

                // The same voice is the same id from the same list - a Kokoro
                // voice on an ElevenLabs list is never one of ElevenLabs'.
                bool Same(VoiceChoice c) =>
                    c.IsKokoro == savedForThem.IsKokoro && string.Equals(c.Id, savedForThem.Id, StringComparison.Ordinal);

                if (savedForThem.Id.Length > 0 && !theirs.Any(Same))
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
                    : theirs.FindIndex(Same) + 1;

                picker.SelectedIndex = Math.Max(0, at);
            }

            Fill(_cmbMechanicVoice, SavedSecondVoice(_settings.MechanicVoice, shown));
            Fill(_cmbPrincipalVoice, SavedSecondVoice(_settings.PrincipalVoice, shown));
            Fill(_cmbCoHostVoice, SavedSecondVoice(_settings.CoHostVoice, shown));
            Fill(_cmbHonoursVoice, SavedSecondVoice(_settings.HonoursVoice, shown));
            Fill(_cmbRecapHostVoice, SavedSecondVoice(_settings.RecapHostVoice, shown));
        }
        finally
        {
            _fillingVoices = false;
        }

        _lblVoiceSource.Text = (loaded is null
                ? $"{EngineLabel(shown)} voices"
                : Plural.Of(loaded.Count, $"{EngineLabel(shown)} voice"))
            + (_kokoroOffered ? ", and the Kokoro voices" : string.Empty);

        // The longest voice may have come or gone: the pickers measured again.
        _mechanicVoiceWidth = null;
        _principalVoiceWidth = null;
        _coHostVoiceWidth = null;
        _coHostStanceWidth = null;
        _exchangeTurnsWidth = null;
        _honoursVoiceWidth = null;
        _recapHostVoiceWidth = null;
        SizeFields();
    }

    // ==================== If ElevenLabs runs out ====================

    /// <summary>Set while the stand-in lists are being filled, so filling them isn't taken for a pick.</summary>
    private bool _fillingStandIns;

    /// <summary>
    /// Fills every "If ElevenLabs runs out" list with the Kokoro voices and
    /// selects the one each speaker stands in with - their own pick, their
    /// Kokoro voice, or their default (CastStandIns.Resolve), which is what
    /// they will actually be heard in. Kokoro's voices are files beside the
    /// exe, so the lists are full whether or not Load Voices was pressed.
    /// </summary>
    private void FillStandIns()
    {
        var installed = KokoroSpeech.InstalledVoices();

        _fillingStandIns = true;

        try
        {
            foreach (var row in _standIns)
            {
                var saved = row.Voice().StandInKokoroVoice;
                var choices = installed.Select(v => new VoiceChoice(v.Name, v.Name, string.Empty, v.Display)).ToList();
                var at = choices.FindIndex(c => string.Equals(c.Id, saved, StringComparison.OrdinalIgnoreCase));

                // A voice saved on another PC that this one hasn't got stays
                // on the list, saying who speaks instead - as the announcer's does.
                if (at < 0 && saved.Length > 0)
                {
                    choices.Insert(0, new VoiceChoice(saved, saved, string.Empty,
                        $"{KokoroSpeech.Describe(saved)} (not on this PC - {KokoroSpeech.Describe(KokoroSpeech.DefaultVoice)} speaks instead)"));
                    at = 0;
                }

                row.Picker.Items.Clear();

                foreach (var choice in choices)
                {
                    row.Picker.Items.Add(choice);
                }

                row.Picker.SelectedIndex = Math.Max(0, at);
            }
        }
        finally
        {
            _fillingStandIns = false;
        }

        Array.Clear(_standInWidths);
        SizeFields();
    }

    /// <summary>Saves the Kokoro voice picked for when ElevenLabs runs out.</summary>
    private void SaveStandIn(StandInRow row)
    {
        if (_fillingStandIns || row.Picker.SelectedItem is not VoiceChoice choice)
        {
            return;
        }

        row.Save(choice.Id);
        _settings.Save();
    }

    /// <summary>
    /// Says the speaker's test line in the Kokoro voice picked on the row -
    /// their voice with that Kokoro voice for every engine, which is how
    /// they sound once ElevenLabs has run out.
    /// </summary>
    private void TestStandIn(StandInRow row)
    {
        if (row.Picker.SelectedItem is not VoiceChoice choice || choice.Id.Length == 0)
        {
            return;
        }

        row.Speak()?.Invoke(row.Voice() with { KokoroVoiceName = choice.Id, KokoroAlways = true });
    }

    /// <summary>
    /// Whether a speaker's stand-in voice is ever heard: with ElevenLabs or
    /// the HeatCaller voice speaking (with Kokoro chosen there is nothing to
    /// stand in for), and a voice of their own picked above it. With the
    /// announcer's own voice they share the announcer's stand-in voice too,
    /// and with a Kokoro voice picked for every engine they are on Kokoro
    /// already.
    /// </summary>
    private bool StandInApplies(StandInRow row) =>
        _voiceListEngine != VoiceEngine.Kokoro
        && row.VoicePicker.SelectedItem is VoiceChoice { IsKokoro: false } picked
        && picked.Id.Length > 0;

    /// <summary>Saves the mechanic's picked voice against the engine the list is showing.</summary>
    private void SaveMechanicVoice()
    {
        if (_fillingVoices || _cmbMechanicVoice.SelectedItem is not VoiceChoice choice)
        {
            return;
        }

        // A Kokoro voice for every engine - see SecondVoice.KokoroAlways.
        if (choice.IsKokoro)
        {
            _settings.MechanicKokoroVoiceName = choice.Id;
            _settings.MechanicKokoroAlways = true;
            _settings.Save();

            // Loaded now, so the Test beside it and the first line on the
            // night don't wait for the model to be read.
            KokoroSpeech.WarmUp();
            return;
        }

        // Any other pick from another engine's list is that engine's voice
        // for them again. From Kokoro's own, a voice keeps the choice as it
        // was; the announcer's own voice ends it.
        if (_voiceListEngine != VoiceEngine.Kokoro || choice.Id.Length == 0)
        {
            _settings.MechanicKokoroAlways = false;
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

    /// <summary>Saves the Team Principal's picked voice against the engine the list is showing.</summary>
    private void SavePrincipalVoice()
    {
        if (_fillingVoices || _cmbPrincipalVoice.SelectedItem is not VoiceChoice choice)
        {
            return;
        }

        // A Kokoro voice for every engine - see SecondVoice.KokoroAlways.
        if (choice.IsKokoro)
        {
            _settings.PrincipalKokoroVoiceName = choice.Id;
            _settings.PrincipalKokoroAlways = true;
            _settings.Save();

            // Loaded now, so the Test beside it and the first line on the
            // night don't wait for the model to be read.
            KokoroSpeech.WarmUp();
            return;
        }

        // Any other pick from another engine's list is that engine's voice
        // for them again. From Kokoro's own, a voice keeps the choice as it
        // was; the announcer's own voice ends it.
        if (_voiceListEngine != VoiceEngine.Kokoro || choice.Id.Length == 0)
        {
            _settings.PrincipalKokoroAlways = false;
        }

        switch (_voiceListEngine)
        {
            case VoiceEngine.Kokoro:
                _settings.PrincipalKokoroVoiceName = choice.Id;
                break;

            case VoiceEngine.HeatCaller:
                _settings.PrincipalHeatCallerVoice = choice.Id;
                _settings.PrincipalHeatCallerVoiceName = choice.Name;
                _settings.PrincipalHeatCallerRev = choice.Rev;
                break;

            default:
                _settings.PrincipalElevenLabsVoiceId = choice.Id;
                _settings.PrincipalElevenLabsVoiceName = choice.Name;
                break;
        }

        _settings.Save();
    }

    /// <summary>Saves the co-host's picked voice against the engine the list is showing.</summary>
    private void SaveCoHostVoice()
    {
        if (_fillingVoices || _cmbCoHostVoice.SelectedItem is not VoiceChoice choice)
        {
            return;
        }

        // A Kokoro voice for every engine - see SecondVoice.KokoroAlways.
        if (choice.IsKokoro)
        {
            _settings.CoHostKokoroVoiceName = choice.Id;
            _settings.CoHostKokoroAlways = true;
            _settings.Save();

            // Loaded now, so the Test beside it and the first line on the
            // night don't wait for the model to be read.
            KokoroSpeech.WarmUp();
            return;
        }

        // Any other pick from another engine's list is that engine's voice
        // for them again. From Kokoro's own, a voice keeps the choice as it
        // was; the announcer's own voice ends it.
        if (_voiceListEngine != VoiceEngine.Kokoro || choice.Id.Length == 0)
        {
            _settings.CoHostKokoroAlways = false;
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
    /// Saves the badges reader's picked voice against the engine the list is
    /// showing - per engine, exactly as the others are, because a voice id
    /// from one engine means nothing to another.
    /// </summary>
    private void SaveHonoursVoice()
    {
        if (_fillingVoices || _cmbHonoursVoice.SelectedItem is not VoiceChoice choice)
        {
            return;
        }

        // A Kokoro voice for every engine - see SecondVoice.KokoroAlways.
        if (choice.IsKokoro)
        {
            _settings.HonoursKokoroVoiceName = choice.Id;
            _settings.HonoursKokoroAlways = true;
            _settings.Save();

            // Loaded now, so the Test beside it and the first line on the
            // night don't wait for the model to be read.
            KokoroSpeech.WarmUp();
            return;
        }

        // Any other pick from another engine's list is that engine's voice
        // for them again. From Kokoro's own, a voice keeps the choice as it
        // was; the announcer's own voice ends it.
        if (_voiceListEngine != VoiceEngine.Kokoro || choice.Id.Length == 0)
        {
            _settings.HonoursKokoroAlways = false;
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

        // A Kokoro voice for every engine - see SecondVoice.KokoroAlways.
        if (choice.IsKokoro)
        {
            _settings.RecapHostKokoroVoiceName = choice.Id;
            _settings.RecapHostKokoroAlways = true;
            _settings.Save();

            // Loaded now, so the Test beside it and the first line on the
            // night don't wait for the model to be read.
            KokoroSpeech.WarmUp();
            return;
        }

        // Any other pick from another engine's list is that engine's voice
        // for them again. From Kokoro's own, a voice keeps the choice as it
        // was; the announcer's own voice ends it.
        if (_voiceListEngine != VoiceEngine.Kokoro || choice.Id.Length == 0)
        {
            _settings.RecapHostKokoroAlways = false;
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

    /// <summary>
    /// Asks the engine in use for its voices, and fills every list with
    /// them. The button's caption says it is working rather than the button
    /// being disabled - see _loadingVoices for why.
    /// </summary>
    private async Task LoadVoicesAsync()
    {
        if (_loadingVoices)
        {
            return;
        }

        var engine = EngineInUse?.Invoke() ?? VoiceEngine.ElevenLabs;

        if (LoadVoices is null)
        {
            AppDialog.Show(this, "The voice list isn't available in this window.", "Cast", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _loadingVoices = true;
        _btnLoadVoices.Text = "Loading...";

        // The announcer's list alongside, at the same time (it never throws).
        // Awaited in finally, so the button says Loading... until both are in.
        var announcer = LoadAnnouncerVoices?.Invoke() ?? Task.CompletedTask;

        try
        {
            var voices = await LoadVoices(engine);

            if (!IsDisposed)
            {
                FillVoices(engine, voices);
            }
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                AppDialog.Show(this, $"Couldn't load the {EngineLabel(engine)} voices - {ex.Message.TrimEnd('.')}.", "Cast",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            try
            {
                await announcer;
            }
            catch (Exception)
            {
                // It says what went wrong under its own list.
            }

            _loadingVoices = false;

            if (!IsDisposed)
            {
                _btnLoadVoices.Text = LoadVoicesCaption;
            }
        }
    }
}
