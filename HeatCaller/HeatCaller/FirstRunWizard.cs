namespace HeatCaller;

/// <summary>
/// The guided setup shown the first time the app starts.
///
/// It covers what a club needs before its first race - how the app looks,
/// the decoder, a track, the classes and a few drivers - and nothing else.
/// Every step can be skipped, the whole thing can be skipped from any step,
/// any step can be jumped to from the rail on the left, and everything it
/// sets can be changed later on the normal pages, which each step names so
/// people know where to find it again.
///
/// Each step is saved as it is left, not at the end, so closing the window
/// part-way keeps what was already entered.
///
/// It does not start the app tour itself: <see cref="TakeTour"/> tells the
/// caller the box was ticked, and MainForm starts it once the wizard has
/// gone (after the restart, when a new look needs one).
///
/// On the layout pieces (DialogFit), laid out as Set Up Tonight is: the
/// rail down the left, flush with the window's edges (a Beside, the rail
/// its column), and beside it the step's title and what it is about, the
/// step itself taking the height between those and the footer - Skip setup
/// and the progress on the left, Back and Next on the right. Each step is a
/// page of its own, built once and shown while it is the step; a step
/// taller than the room it has scrolls, rather than the window growing, and
/// the footer stays in view. The window opens as wide as Set Up Tonight and
/// as tall as it always was, and can be resized. It used to be placed in
/// pixels: a fixed 770x580 window, the rail 190px wide at every text size,
/// the step's title and what it is about at fixed heights over a 520x310
/// box that each step's controls were placed in by coordinates, and the
/// footer 58px from the bottom - which a larger text size ran off the
/// bottom and the right of, and which a long step's text ran into.
/// </summary>
public sealed partial class FirstRunWizard : Form
{
    /// <summary>The rail's width at the normal text size - wider only when its step names or the brand need it.</summary>
    private const int RailWidth = 190;

    /// <summary>
    /// How wide and tall the window opens inside its frame, at the normal
    /// text size. As wide as Set Up Tonight, whose rail and footer are this
    /// one's: at the 770 it always was, Skip setup and the progress took a
    /// line of their own over Back and Next at the normal text size - the
    /// footer's buttons are the app's standard width now, not the 100 to
    /// 130px they were placed at (layout pictures, 30 Sep). The height it
    /// always was.
    /// </summary>
    private const int WindowWidth = 850;

    private const int WindowHeight = 580;

    /// <summary>A step has at least this much height at the normal text size - a few lines - before the window scrolls instead of the step.</summary>
    private const int StepLeastHeight = 120;

    /// <summary>A step on the rail: its least height, and the room before its name, at the normal text size.</summary>
    private const int RailStepHeight = 34;

    private const int RailStepInset = 12;

    private const string BrandText = "HEATCALLER";

    /// <summary>The decoder drop-down's width at the normal text size - wider when a choice needs it - and the track name's.</summary>
    private const int ConnectionWidth = 340;

    private const int TrackNameWidth = 340;

    /// <summary>The COM port and baud drop-downs' width at the normal text size - wider when a choice needs it.</summary>
    private const int PortWidth = 130;

    /// <summary>The classes box: its width at the normal text size, and how many lines of it show.</summary>
    private const int ClassesBoxWidth = 260;

    private const int ClassesBoxLines = 6;

    /// <summary>The driver rows' boxes, and the class drop-down under them, at the normal text size - the drop-down wider when a class's name needs it.</summary>
    private const int DriverNameWidth = 256;

    private const int TransponderWidth = 180;

    private const int DriverClassWidth = 200;

    /// <summary>The looks list's least width at the normal text size - wider when a look's name needs it - and the least of its rows it shows before the step scrolls instead.</summary>
    private const int LookListWidth = 200;

    private const int LookListLeastRows = 5;

    /// <summary>How tall the picture of a look is at the normal text size. It is as wide as the room beside the list.</summary>
    private const int PreviewHeight = 130;

    private const int DriverRows = 6;

    private static readonly string[] StepNames =
    {
        "Welcome", "Look", "Timing", "Track", "Classes", "Drivers", "All set",
    };

    /// <summary>The look step's name on the rail when it offers the sound as well - see SoundOffer.</summary>
    private const string LookAndSoundName = "Look and sound";

    private static readonly string[] SuggestedClasses = { "Stock", "Modified", "Open", "Novice", "Spec" };

    private readonly AppSettings _settings;
    private readonly TrackLayoutStore _trackLayouts;
    private readonly RaceClassStore _raceClasses;
    private readonly ProfileStore _profiles;

    // The frame

    /// <summary>The steps down the left - a page with no part that fills, as tall as the window (see BuildLayout).</summary>
    private readonly FillPage _rail = new();
    private readonly Label _lblBrand = new();
    private readonly Label _lblRailCaption = new();
    private readonly Label[] _railLabels = new Label[StepNames.Length];

    /// <summary>Beside the rail: the step's title and what it is about, the step, and the footer.</summary>
    private readonly FillPage _stepPage = new();
    private readonly Label _lblStepTitle = new();
    private readonly Label _lblStepBody = new();

    /// <summary>Where the steps are, one on show at a time - see ShowStep.</summary>
    private readonly Panel _stepHolder = new();

    private readonly ButtonBar _footerLeft = new();
    private readonly Label _lblProgress = new();
    private readonly Button _btnSkipAll = new();
    private readonly Button _btnBack = new();
    private readonly Button _btnNext = new();

    // Welcome
    private readonly PageStack _welcomeStep = new();
    private readonly Label _lblWelcomeRestore = new();
    private readonly Button _btnRestore = new();

    // Look
    private readonly FillPage _lookStep = new();
    private readonly ListBox _lstLooks = new();
    private readonly Panel _lookPreview = new();
    private readonly Label _lblLookDescription = new();
    private readonly CheckBox _chk24Hour = new ThemedCheckBox();

    /// <summary>The sound, under the look - null when there is no host to apply it, or the club said not to ask (AppSettings.AskAboutSoundInWizards).</summary>
    private readonly SoundOffer? _sound;

    /// <summary>The looks list's width at the text size in use - see SizeFields.</summary>
    private int _lookListWidth = UiLayout.AtTextSize(LookListWidth);

    // Timing
    private readonly PageStack _timingStep = new();
    private readonly ComboBox _cmbConnection = new();

    /// <summary>The COM port and baud rows, shown only while I-Lap is the chosen connection - see ShowILapFields.</summary>
    private readonly FieldGrid _iLapFields = new();
    private readonly ComboBox _cmbSerialPort = new();
    private readonly Button _btnRescan = new();
    private readonly ComboBox _cmbBaud = new();
    private readonly Label _lblTimingHint = new();

    // Track
    private readonly PageStack _trackStep = new();
    private readonly TextBox _txtTrackName = new();
    private readonly Label _lblTrackHint = new();

    /// <summary>Where you race (AppSettings.RacingAt): which badges are on offer, and whether practice earns any.</summary>
    private readonly RadioButton _rdoClub = new ThemedRadioButton();
    private readonly RadioButton _rdoHome = new ThemedRadioButton();
    private readonly Label _lblWhereHint = new();

    // Classes
    private readonly PageStack _classesStep = new();
    private readonly TextBox _txtClasses = new();
    private readonly Label _lblChipsCaption = new();

    /// <summary>The suggested classes, each only as wide as the widest of their captions needs, on one line where there is room.</summary>
    private readonly ButtonBar _classChips = new(tight: true, sameWidth: true);
    private readonly Label _lblClassesHint = new();

    /// <summary>The race length and finish every class typed here gets (12 Nov 2026, setup plan step 8) - see ClassLengthPicker. Made in the constructor, from the app default.</summary>
    private ClassLengthPicker _classLength = new(null);

    /// <summary>The grid the race length rows are in, lined up on its own.</summary>
    private FieldGrid? _classLengthGrid;

    // Drivers
    private readonly PageStack _driversStep = new();
    private readonly Label _lblNameHeader = new();
    private readonly Label _lblTransponderHeader = new();
    private readonly TextBox[] _txtDriverNames = new TextBox[DriverRows];
    private readonly TextBox[] _txtTransponders = new TextBox[DriverRows];
    private readonly ComboBox _cmbDriverClass = new();
    private readonly Label _lblDriversHint = new();

    // All set
    private readonly PageStack _doneStep = new();

    // What each step came to, an answer to each caption - see FillSummary.
    private readonly Label _lblDoneLook = new();
    private readonly Label _lblDoneDecoder = new();
    private readonly Label _lblDoneTrack = new();
    private readonly Label _lblDoneWhere = new();
    private readonly Label _lblDoneClasses = new();
    private readonly Label _lblDoneDrivers = new();
    private readonly Label _lblDoneSound = new();

    /// <summary>Under the drivers' answer when rows on the drivers step were not saved - see FillSummary.</summary>
    private readonly Label _lblDoneSkipped = new();

    private readonly Label _lblRaceNight = new();
    private readonly CheckBox _chkTour = new ThemedCheckBox();

    /// <summary>
    /// The beta tier's question (22 Oct 2026) - see
    /// AppSettings.HideUntestedPages. On the last step, under the tour.
    /// </summary>
    private readonly CheckBox _chkHideUntested = new ThemedCheckBox();

    // What each step actually did, for the summary on the last page.
    private string? _doneLook;
    private string? _doneTiming;
    private string? _doneTrack;
    private readonly List<string> _doneClasses = new();
    private int _doneDrivers;
    private int _skippedDrivers;

    private readonly string _startingLookKey;
    private int _step;

    /// <summary>
    /// The wizard, at the welcome page. With <paramref name="sound"/>, the
    /// look step offers the sound too (SoundOffer) - unless the club has
    /// said not to ask.
    /// </summary>
    internal FirstRunWizard(AppSettings settings, TrackLayoutStore trackLayouts, RaceClassStore raceClasses, ProfileStore profiles, SoundOfferHost? sound = null)
        : this(settings, trackLayouts, raceClasses, profiles, startStep: 0, sound)
    {
    }

    /// <summary>
    /// The wizard opened at <paramref name="startStep"/> - an index into
    /// StepNames - rather than at the welcome page. For the layout test,
    /// which measures the window as its constructor leaves it and so can
    /// only see the step shown then (WindowCatalog.FirstRunWindows). The
    /// app always opens it at the welcome page.
    /// </summary>
    internal FirstRunWizard(AppSettings settings, TrackLayoutStore trackLayouts, RaceClassStore raceClasses, ProfileStore profiles, int startStep, SoundOfferHost? sound = null)
    {
        _settings = settings;
        _trackLayouts = trackLayouts;
        _raceClasses = raceClasses;
        _profiles = profiles;
        _startingLookKey = Looks.ForApp(settings).Key;
        _sound = sound is not null && settings.AskAboutSoundInWizards ? new SoundOffer(settings, sound) : null;
        _classLength = new ClassLengthPicker(raceClasses.AppDefaultFormat?.Invoke());

        Text = "Welcome to HeatCaller";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        var page = BuildLayout();
        AppTheme.ApplyRecursively(this);
        SizeFields();
        ShowStep(startStep);

        // The size it always opened at; it can be made bigger, or smaller
        // down to what the rail, the step's title, the footer and a few
        // lines of a step need - the step scrolls below that.
        DialogFit.Keep(this, page, width: WindowWidth, height: WindowHeight);
    }

    /// <summary>True when the "show me around" box was ticked on the last page.</summary>
    public bool TakeTour { get; private set; }

    /// <summary>True when a different look was chosen - it only takes effect after a restart.</summary>
    public bool RestartForLook { get; private set; }

    /// <summary>True when "Restore a backup instead" was chosen - the caller opens Backup &amp; Restore.</summary>
    public bool OpenBackups { get; private set; }

    /// <summary>Each step's own page, in the order of StepNames - see ShowStep.</summary>
    private ScrollableControl[] StepPages => new ScrollableControl[]
    {
        _welcomeStep, _lookStep, _timingStep, _trackStep, _classesStep, _driversStep, _doneStep,
    };

    // --- Layout ----------------------------------------------------------

    /// <summary>
    /// The window, returned as the page that fills it: the rail down the
    /// left, flush with the window's top, bottom and left edges, and beside
    /// it the step - its title, what it is about, the step's own page (all
    /// of them are built here, and one is shown at a time) and the footer.
    /// </summary>
    private FillPage BuildLayout()
    {
        BuildRail();

        _lblStepTitle.AutoSize = true;
        _lblStepTitle.Font = AppTheme.TitleFont;

        _lblStepBody.AutoSize = true;

        _lblProgress.AutoSize = true;
        _lblProgress.Font = AppTheme.SmallFont;

        // Level with the middle of Skip setup beside it, not its top - as a
        // label among buttons is in a row of them (FieldGrid.Inline).
        _lblProgress.Anchor = AnchorStyles.Left;

        _btnSkipAll.Text = "Skip setup";
        _btnSkipAll.Click += (_, _) => SkipEverything();

        _btnBack.Text = "Back";
        _btnBack.Click += (_, _) =>
        {
            SaveCurrentStep();
            ShowStep(_step - 1);
        };

        _btnNext.Text = "Next";
        _btnNext.Click += (_, _) => NextClicked();

        BuildWelcome();
        BuildLook();
        BuildTiming();
        BuildTrack();
        BuildClasses();
        BuildDrivers();
        BuildDone();

        // Every step's page fills the holder; only the one on show is
        // visible, so only it takes the room. None has margins of its own -
        // the page they are in gives them - nor any room under its last part
        // when it scrolls, for the reason DialogFit.Body gives.
        _stepHolder.Tag = AppTheme.KeepBackColor;

        foreach (var stepPage in StepPages)
        {
            stepPage.Padding = Padding.Empty;
            stepPage.Visible = false;
            ScrollTheming.NoRoomBelow(stepPage);

            _stepHolder.Controls.Add(stepPage);
        }

        // Skip setup and the progress on the left, Back and Next against the
        // right-hand edge, Next furthest right - on one line, or Back and
        // Next under the other two when a narrow window has no room for both
        // (SplitBar). The bar is handed Next first, as its first button goes
        // furthest right; Tab still goes Back before Next, as it did, from
        // the tab order set here.
        var footer = new SplitBar(
            _footerLeft.With(_btnSkipAll, _lblProgress),
            new ButtonBar(alignRight: true).With(_btnNext, _btnBack));

        _btnBack.TabIndex = 0;
        _btnNext.TabIndex = 1;

        // More room above and to the right than a dialog's usual margin, as
        // the step always had, and the rail's own gap on the left (Beside).
        _stepPage.Padding = new Padding(UiLayout.PageLeft, UiLayout.SectionGap, UiLayout.SectionGap, UiLayout.SectionGap);
        ScrollTheming.NoRoomBelow(_stepPage);

        _stepPage.Add(_lblStepTitle, gapBelow: UiLayout.RowGap);
        _stepPage.Add(_lblStepBody);

        // The step takes the height the rest leave, and scrolls inside it
        // when it needs more - down to a few lines of it, below which the
        // window scrolls instead.
        _stepPage.AddFill(_stepHolder, _ => UiLayout.AtTextSize(StepLeastHeight));
        _stepPage.Add(footer, gapBelow: 0);

        var band = new Beside(MeasureRailWidth()) { SideOnLeft = true };

        band.AddSideFill(_rail, _rail.LeastHeight, gapBelow: 0);
        band.AddFill(_stepPage, _stepPage.LeastHeight, gapBelow: 0);

        // No margin round the window: the rail runs to its edges.
        var page = new FillPage { Padding = Padding.Empty };

        page.AddFill(band, band.LeastHeight, gapBelow: 0);
        Controls.Add(page);

        return page;
    }

    /// <summary>
    /// The rail: the brand, what the window is for, and every step by name,
    /// the current one highlighted, so it is always clear how much is left.
    /// A page of its own in the sidebar's colour, the column of a Beside
    /// that takes the window's whole height (AddSideFill) - so it runs from
    /// the top of the window to the bottom however tall that is.
    /// </summary>
    private void BuildRail()
    {
        // Every name on the rail starts at the same place.
        var inset = UiLayout.AtTextSize(RailStepInset);

        _lblBrand.Text = BrandText;
        _lblBrand.AutoSize = true;
        _lblBrand.Font = AppTheme.NavFont;
        _lblBrand.Padding = new Padding(inset, 0, 0, 0);

        _lblRailCaption.Text = "First-time setup";
        _lblRailCaption.AutoSize = true;
        _lblRailCaption.Font = AppTheme.SmallFont;
        _lblRailCaption.Padding = new Padding(inset, 0, 0, 0);

        // Placed by the Beside it is the column of, not docked; and as tall
        // as it needs to be with nothing under its last step (LeastHeight).
        _rail.Dock = DockStyle.None;
        _rail.Padding = new Padding(inset, UiLayout.SectionGap, inset, UiLayout.PageTop);
        ScrollTheming.NoRoomBelow(_rail);

        _rail.Add(_lblBrand, gapBelow: UiLayout.HintGap);
        _rail.Add(_lblRailCaption);

        // Tall enough for the current step's name in the bold face.
        var stepHeight = Math.Max(
            UiLayout.AtTextSize(RailStepHeight),
            Pieces.LineHeight(AppTheme.BoldFont) + UiLayout.AtTextSize(UiLayout.RowGap + 2));

        for (var i = 0; i < StepNames.Length; i++)
        {
            var index = i;
            var label = new Label
            {
                Text = i == 1 && _sound is not null ? LookAndSoundName : StepNames[i],
                AutoSize = false,
                Height = stepHeight,
                Padding = new Padding(inset, 0, 0, 0),
                TextAlign = ContentAlignment.MiddleLeft,
                Cursor = Cursors.Hand,
            };

            // Any step can be jumped to - it is a checklist, not a gate.
            label.Click += (_, _) =>
            {
                SaveCurrentStep();
                ShowStep(index);
            };

            _railLabels[i] = label;
            _rail.Add(label, gapBelow: i == StepNames.Length - 1 ? 0 : UiLayout.HintGap);
        }
    }

    /// <summary>
    /// How wide the rail is: the width it always had at the normal text
    /// size, at the size in use - or, if they need more, the widest step's
    /// name in the bold face, or the brand, with the rail's margins either
    /// side and the room before the name.
    /// </summary>
    private static int MeasureRailWidth()
    {
        var inset = UiLayout.AtTextSize(RailStepInset);

        // The look step's longer name too, whether or not it is on the rail -
        // a few pixels at most, and the rail the same width either way.
        var names = StepNames.Append(LookAndSoundName).Max(name => TextRenderer.MeasureText(name, AppTheme.BoldFont).Width);
        var brand = TextRenderer.MeasureText(BrandText, AppTheme.NavFont).Width;

        return Math.Max(UiLayout.AtTextSize(RailWidth), Math.Max(names, brand) + 3 * inset);
    }

    /// <summary>The welcome step: the way to a backup instead, under what the app is.</summary>
    private void BuildWelcome()
    {
        _lblWelcomeRestore.Text = "Moving from another laptop? Restore your backup instead - it brings your drivers, "
                                  + "classes, results and settings with it.";
        _lblWelcomeRestore.AutoSize = true;
        _lblWelcomeRestore.Font = AppTheme.SmallFont;

        _btnRestore.Text = "Restore a backup instead";
        _btnRestore.Click += (_, _) =>
        {
            OpenBackups = true;
            Finish(takeTour: false);
        };

        _welcomeStep.Add(_lblWelcomeRestore, gapBelow: UiLayout.RowGap);
        _welcomeStep.Add(new ButtonBar().With(_btnRestore), gapBelow: 0);
    }

    /// <summary>
    /// The look step: the looks down the left, taking the height the step
    /// has, and beside them a picture of the one selected and what it is
    /// (a Beside, the list its column) - then the 24-hour clock under both.
    /// </summary>
    private void BuildLook()
    {
        // Given the height the step has, not snapped to whole rows - see FillPage.AddFill.
        _lstLooks.IntegralHeight = false;

        foreach (var look in Looks.BuiltIn)
        {
            _lstLooks.Items.Add(new LookItem(look));
        }

        for (var i = 0; i < _lstLooks.Items.Count; i++)
        {
            if (_lstLooks.Items[i] is LookItem item
                && string.Equals(item.Look.Key, _startingLookKey, StringComparison.OrdinalIgnoreCase))
            {
                _lstLooks.SelectedIndex = i;
            }
        }

        if (_lstLooks.SelectedIndex < 0 && _lstLooks.Items.Count > 0)
        {
            _lstLooks.SelectedIndex = 0;
        }

        _lstLooks.SelectedIndexChanged += (_, _) =>
        {
            _lblLookDescription.Text = SelectedLook?.Description ?? string.Empty;
            _lookPreview.Invalidate();
        };

        _lookPreview.Paint += (_, e) => PaintLookPreview(e.Graphics, _lookPreview.ClientRectangle, SelectedLook);

        // The picture is drawn to the preview's size, which now follows the
        // window's - so all of it again when that changes, not only the part
        // that was uncovered.
        _lookPreview.Resize += (_, _) => _lookPreview.Invalidate();

        _lblLookDescription.AutoSize = true;
        _lblLookDescription.Font = AppTheme.SmallFont;
        _lblLookDescription.Text = SelectedLook?.Description ?? string.Empty;

        _chk24Hour.Text = "Show times on a 24-hour clock (14:30 rather than 2:30 PM)";
        _chk24Hour.AutoSize = true;
        _chk24Hour.Checked = _settings.Use24HourTime;

        // The list as wide as the longest look's name needs, or the width it
        // always had if that is more (SizeFields).
        var band = new Beside(() => _lookListWidth) { SideOnLeft = true };

        band.Add(_lookPreview, gapBelow: UiLayout.RowGap);
        band.Add(_lblLookDescription, gapBelow: 0);
        band.AddSideFill(_lstLooks, _ => LookListLeastRows * _lstLooks.ItemHeight + 4, gapBelow: 0);

        _lookStep.AddFill(band, band.LeastHeight, gapBelow: UiLayout.SectionGap);

        if (_sound is null)
        {
            _lookStep.Add(_chk24Hour, stretch: false, gapBelow: 0);
            return;
        }

        _lookStep.Add(_chk24Hour, stretch: false, gapBelow: UiLayout.SectionGap);

        // The sound under the look, in a grid of its own: its caption beside
        // the drop-down, what the choice means under it, then the tick.
        var soundGrid = new FieldGrid();
        _sound.AddTo(soundGrid);
        _lookStep.Add(soundGrid, gapBelow: 0);
    }

    /// <summary>
    /// The timing step: the decoder, then - for I-Lap - its COM port and
    /// baud rate lined up in a grid, then what to do next.
    /// </summary>
    private void BuildTiming()
    {
        _cmbConnection.DropDownStyle = ComboBoxStyle.DropDownList;

        // The Connection page's own list, so the two never offer different
        // choices.
        _cmbConnection.Items.AddRange(DecoderConnection.Items());
        _cmbConnection.SelectedIndex = DecoderConnection.IndexOf(_settings.LastConnectionType);
        _cmbConnection.SelectedIndexChanged += (_, _) => ShowILapFields();

        // I-Lap: its COM port and baud rate, saved to the same settings the
        // Connection page reads. Nothing connects from here - the port is
        // only remembered, so Connect finds it chosen.
        _cmbSerialPort.DropDownStyle = ComboBoxStyle.DropDownList;

        _btnRescan.Text = "Rescan";
        _btnRescan.Click += (_, _) => FillSerialPorts();

        _cmbBaud.DropDownStyle = ComboBoxStyle.DropDownList;

        foreach (var rate in ILapClient.CommonBaudRates)
        {
            _cmbBaud.Items.Add(rate);
        }

        // A rate saved earlier that is not one of the usual ones is kept,
        // as the Connection page does, rather than quietly replaced.
        if (_settings.ILapBaud > 0 && !ILapClient.CommonBaudRates.Contains(_settings.ILapBaud))
        {
            _cmbBaud.Items.Add(_settings.ILapBaud);
        }

        _cmbBaud.SelectedItem = _settings.ILapBaud > 0 ? _settings.ILapBaud : ILapClient.CommonBaudRates[0];

        _lblTimingHint.AutoSize = true;
        _lblTimingHint.Font = AppTheme.SmallFont;

        _iLapFields.AddRow("COM port:", FieldGrid.Inline(_cmbSerialPort, _btnRescan));
        _iLapFields.AddRow("Baud:", _cmbBaud);

        _timingStep.Add(_cmbConnection, stretch: false, gapBelow: UiLayout.RowGap);
        Pieces.KeepHeightTold(_cmbConnection);

        _timingStep.Add(_iLapFields, gapBelow: UiLayout.RowGap);
        _timingStep.Add(_lblTimingHint, gapBelow: 0);
    }

    /// <summary>The track step: the layout's name, and where the rest of a track is set up.</summary>
    private void BuildTrack()
    {
        _txtTrackName.PlaceholderText = "e.g. Garage Oval, Shop Layout A";
        _txtTrackName.Text = _settings.LastTrackLayoutName;

        _lblTrackHint.AutoSize = true;
        _lblTrackHint.Font = AppTheme.SmallFont;

        // In a row of its own (FieldGrid.Inline), which keeps the pixel round
        // the box that the theme draws its outline in - against the step's
        // edge, that side of the outline would be cut off.
        _trackStep.Add(FieldGrid.Inline(_txtTrackName), gapBelow: UiLayout.RowGap);
        _trackStep.Add(_lblTrackHint, gapBelow: UiLayout.SectionGap);

        // Where you race - the badges on offer, and whether practice earns
        // any. Both in the step itself, so WinForms groups them.
        _rdoClub.Text = "At a club";
        _rdoClub.AutoSize = true;
        _rdoHome.Text = "At home, with family or friends";
        _rdoHome.AutoSize = true;
        _rdoClub.Checked = _settings.RacingAt != RacingSetting.Home;
        _rdoHome.Checked = _settings.RacingAt == RacingSetting.Home;

        _lblWhereHint.AutoSize = true;
        _lblWhereHint.Font = AppTheme.SmallFont;
        _lblWhereHint.Text = "At home, practice earns the achievements you can earn on your own, and the club-sized ones "
                             + "(a full grid, fifty opponents) aren't offered. Change it any time on the Achievements page.";

        _trackStep.Add(new Label { Text = "Where do you race?", AutoSize = true, Font = AppTheme.BoldFont }, stretch: false, gapBelow: UiLayout.HintGap);
        _trackStep.Add(_rdoClub, stretch: false, gapBelow: UiLayout.HintGap);
        _trackStep.Add(_rdoHome, stretch: false, gapBelow: UiLayout.RowGap);
        _trackStep.Add(_lblWhereHint, gapBelow: 0);
    }

    /// <summary>
    /// The classes step: a box of class names, one to a line, the suggested
    /// classes under it with their caption above them, and where the rest of
    /// a class is set up.
    /// </summary>
    private void BuildClasses()
    {
        _txtClasses.Multiline = true;
        _txtClasses.AcceptsReturn = true;
        _txtClasses.ScrollBars = ScrollBars.Vertical;
        _txtClasses.PlaceholderText = "One class per line";

        _lblChipsCaption.Text = "Quick add:";
        _lblChipsCaption.AutoSize = true;

        foreach (var name in SuggestedClasses)
        {
            var chip = new Button { Text = name };
            chip.Click += (_, _) => AddClassLine(name);
            _classChips.Controls.Add(chip);
        }

        _lblClassesHint.AutoSize = true;
        _lblClassesHint.Font = AppTheme.SmallFont;

        // The box in a row of its own for its outline, as the track's name is.
        _classesStep.Add(FieldGrid.Inline(_txtClasses), gapBelow: UiLayout.RowGap);
        _classesStep.Add(_lblChipsCaption, stretch: false, gapBelow: UiLayout.HintGap);
        _classesStep.Add(_classChips, gapBelow: UiLayout.RowGap);

        // How long the new classes race and how they finish (setup plan step 8):
        // one answer for every class typed here, as a club's classes mostly share it.
        _classLengthGrid = _classesStep.Add(_classLength.Grid(), gapBelow: UiLayout.RowGap);
        _classesStep.Add(_lblClassesHint, gapBelow: 0);
    }

    /// <summary>
    /// The drivers step: a heading over each column, a row per driver - the
    /// name box and the transponder box side by side - and the class they
    /// are all saved in, with what happens to a blank row beside it.
    /// </summary>
    private void BuildDrivers()
    {
        _lblNameHeader.Text = "Driver name";
        _lblNameHeader.AutoSize = true;
        _lblNameHeader.Font = AppTheme.SmallFont;

        _lblTransponderHeader.Text = "Transponder number";
        _lblTransponderHeader.AutoSize = true;
        _lblTransponderHeader.Font = AppTheme.SmallFont;

        // The headings in a row laid out as the driver rows are, the name
        // heading as wide as a name box and its outline (SizeFields) - so
        // the transponder heading starts where the transponder boxes do.
        _driversStep.Add(FieldGrid.Inline(_lblNameHeader, _lblTransponderHeader), gapBelow: UiLayout.HintGap);

        for (var i = 0; i < DriverRows; i++)
        {
            _txtDriverNames[i] = new TextBox
            {
                PlaceholderText = i == 0 ? "e.g. Sam Carter" : string.Empty,
            };

            _txtTransponders[i] = new TextBox
            {
                PlaceholderText = i == 0 ? "e.g. 8467BE" : string.Empty,
            };

            _driversStep.Add(FieldGrid.Inline(_txtDriverNames[i], _txtTransponders[i]), gapBelow: UiLayout.RowGap);
        }

        _cmbDriverClass.DropDownStyle = ComboBoxStyle.DropDownList;

        _lblDriversHint.AutoSize = true;
        _lblDriversHint.Font = AppTheme.SmallFont;
        _lblDriversHint.Text = "Rows left blank are skipped.";

        // The note beside the class, or under it when a long class's name
        // leaves no room beside it.
        var classRow = new FieldGrid();
        classRow.AddRow("Class:", FieldGrid.Inline(_cmbDriverClass, _lblDriversHint));

        _driversStep.Add(classRow, gapBelow: UiLayout.RowGap);

        // Bringing a club over (12 Nov 2026, setup plan C2): a whole list at
        // once - RCSignup or LiveTime export, a LiveRC entry list, any CSV.
        _driversStep.Add(BuildImportRow(), gapBelow: 0);
    }

    /// <summary>
    /// The last step: what is ready - a caption and its answer on each line,
    /// the answers lined up and wrapping in a column of their own - then
    /// what to do on race night, and the tour's box under it. It was one
    /// label, each answer a few spaces after its caption, so no two answers
    /// started in the same place, and a long one wrapped back under the
    /// captions (layout pictures, 30 Sep).
    /// </summary>
    private void BuildDone()
    {
        var ready = new FieldGrid();

        foreach (var (caption, answer) in new[]
        {
            ("Look:", _lblDoneLook),
            ("Sound:", _lblDoneSound),
            ("Decoder:", _lblDoneDecoder),
            ("Track:", _lblDoneTrack),
            ("Racing:", _lblDoneWhere),
            ("Classes:", _lblDoneClasses),
            ("Drivers:", _lblDoneDrivers),
        })
        {
            // The sound only when the look step offered it.
            if (answer == _lblDoneSound && _sound is null)
            {
                continue;
            }

            answer.AutoSize = true;

            // A track or a class is named by the club - an "&" in it is
            // meant, not a shortcut key.
            answer.UseMnemonic = false;

            var captionLabel = ready.AddRow(caption, answer, stretch: true);

            // A list of what is ready, not fields to fill in: its lines as
            // close as a hint's to what it is under.
            captionLabel.Margin = new Padding(captionLabel.Margin.Left, captionLabel.Margin.Top, captionLabel.Margin.Right, UiLayout.HintGap);
            answer.Margin = new Padding(answer.Margin.Left, answer.Margin.Top, answer.Margin.Right, UiLayout.HintGap);
        }

        // In the answers' column, under the drivers' answer it is about.
        _lblDoneSkipped.AutoSize = true;
        _lblDoneSkipped.Visible = false;
        ready.AddField(_lblDoneSkipped, stretch: true);

        // The route a club actually takes on the night, and the reason this
        // is not the old one-liner.
        //
        // It used to say "open Connection and press Connect, then run it
        // from Race Control" - which works, and teaches a club to run the
        // whole evening without ever opening Race Day. The heats never get
        // built, nothing is filed against a day, and qualifying has no
        // boundary. The page that plans a night was reachable only by
        // clicking the sidebar out of curiosity.
        _lblRaceNight.AutoSize = true;
        // And that the app opens in Simple mode (1 Nov 2026, Simple and
        // Advanced mode, round 5), so a club that goes looking for a tuning
        // setting knows where the rest are.
        _lblRaceNight.Text = string.Join(Environment.NewLine,
            "On race night: open Connection and press Connect, then go to Race Day.",
            "Its Next Steps tab says the one next thing to do and has the button that does it.",
            "No decoder here yet? Open Demo Mode and start a simulated race.",
            string.Empty,
            SettingsModes.WizardLine);

        _chkTour.Text = "Show me around the app now (about two minutes)";
        _chkTour.AutoSize = true;
        _chkTour.Checked = true;
        _chkTour.Font = AppTheme.BoldFont;

        _doneStep.Add(ready, gapBelow: UiLayout.SectionGap);
        _doneStep.Add(_lblRaceNight, gapBelow: UiLayout.SectionGap);
        // Ticked for a new club, which is who this wizard is for: a beta club
        // starts with the pages that have been run on a night and finds the
        // rest with a search. Opened again later by a club that already has a
        // sidebar, it shows what the club has now rather than ticking it for
        // them.
        _chkHideUntested.Text = "Keep pages not yet tested at a real event out of the sidebar for now ("
            + string.Join(", ", NavTiers.Untested.OrderBy(n => n, StringComparer.CurrentCulture))
            + "). A search still finds them, and Manage Navigation brings them back.";
        _chkHideUntested.AutoSize = true;
        _chkHideUntested.Checked = !_settings.HasCompletedFirstRun || _settings.HideUntestedPages;

        _doneStep.Add(_chkTour, stretch: false, gapBelow: UiLayout.RowGap);
        _doneStep.Add(_chkHideUntested, gapBelow: 0);
    }

    /// <summary>
    /// The fields' sizes, in the faces the theme pass left them in: the
    /// boxes the width they were made at, at the text size in use; the
    /// drop-downs as wide as their longest choice when that is wider (and
    /// again when their lists are refilled - see FitPicker); the classes box
    /// six of its lines tall; the preview its height; the looks list as
    /// wide as its longest name.
    /// </summary>
    private void SizeFields()
    {
        FitPicker(_cmbConnection, ConnectionWidth);
        FitPicker(_cmbSerialPort, PortWidth);
        FitPicker(_cmbBaud, PortWidth);
        FitPicker(_cmbDriverClass, DriverClassWidth);
        _sound?.SizeFields();
        _classLength.SizeFields();

        if (_classLengthGrid is not null)
        {
            FieldGrid.LineUp(new[] { _classLengthGrid });
        }

        _txtTrackName.Width = UiLayout.AtTextSize(TrackNameWidth);

        _txtClasses.Size = new Size(
            UiLayout.AtTextSize(ClassesBoxWidth),
            ClassesBoxLines * Pieces.LineHeight(_txtClasses.Font) + UiLayout.HintGap * 2);

        var nameWidth = UiLayout.AtTextSize(DriverNameWidth);
        var transponderWidth = UiLayout.AtTextSize(TransponderWidth);

        for (var i = 0; i < DriverRows; i++)
        {
            _txtDriverNames[i].Width = nameWidth;
            _txtTransponders[i].Width = transponderWidth;
        }

        // A name box's row keeps a pixel either side of it for its outline
        // (Pieces.RingRoom); the heading row keeps none round a label, so
        // the heading is that much wider than the box.
        _lblNameHeader.MinimumSize = new Size(nameWidth + Pieces.RingRoom(_txtDriverNames[0]).Horizontal, 0);

        _lookPreview.Height = UiLayout.AtTextSize(PreviewHeight);

        // The longest name, and room for the list's scrollbar and either
        // side of the text.
        var longestLook = _lstLooks.Items
            .Cast<object>()
            .Select(item => TextRenderer.MeasureText(_lstLooks.GetItemText(item), _lstLooks.Font).Width)
            .DefaultIfEmpty(0)
            .Max();

        _lookListWidth = Math.Max(UiLayout.AtTextSize(LookListWidth), longestLook + SystemInformation.VerticalScrollBarWidth + 8);
    }

    /// <summary>
    /// A drop-down as wide as it was made at the normal text size
    /// (<paramref name="least"/>), at the size in use, or as its longest
    /// choice if that is wider - measured afresh, for a list just refilled.
    /// </summary>
    private static void FitPicker(ComboBox picker, int least)
    {
        (Font Font, int Count, int Width)? measured = null;

        var width = Math.Max(UiLayout.AtTextSize(least), Pieces.PickerWidth(picker, ref measured));

        // Only when it changed: a new width lays the row out again.
        if (picker.Width != width)
        {
            picker.Width = width;
        }
    }

    // --- Steps -----------------------------------------------------------

    private void ShowStep(int step)
    {
        _step = Math.Clamp(step, 0, StepNames.Length - 1);

        switch (_step)
        {
            case 0:
                _lblStepTitle.Text = "Welcome to HeatCaller";
                _lblStepBody.Text =
                    "HeatCaller runs an RC race night from one laptop: it reads your timing decoder, runs the race, "
                    + "calls it out loud, and keeps every result.\r\n\r\n"
                    + "This setup takes about three minutes. Every step can be skipped, and everything here can be "
                    + "changed later from the sidebar.";
                break;

            case 1:
                _lblStepTitle.Text = _sound is null ? "Pick a look" : "Pick a look and a sound";
                _lblStepBody.Text = _sound is null
                    ? "How the app looks on this laptop. The screens and web pages your racers see can have a look of "
                      + "their own - set that, and your club logo, on the Themes page later."
                    : "How the app looks on this laptop, and how race night sounds. The screens and web pages your racers "
                      + "see can have a look of their own - set that, and your club logo, on the Themes page later.";
                break;

            case 2:
                _lblStepTitle.Text = "Your timing decoder";
                _lblStepBody.Text =
                    "HeatCaller reads the decoder directly and runs race control itself. Pick the one you have - "
                    + "you connect to it on the Connection page when you're ready to race.";
                ShowILapFields();
                break;

            case 3:
                _lblStepTitle.Text = "Name your track";
                _lblStepBody.Text =
                    "Each track layout keeps its own lap record and average pace, so a rebuild doesn't mix up the "
                    + "records. Give today's layout a name you'll recognize.";
                _lblTrackHint.Text = _trackLayouts.All.Count > 0
                    ? $"Already set up: {string.Join(", ", _trackLayouts.All.Select(t => t.Name).Take(6))}. "
                      + "Add photos, marshal posts and more on the Track Layouts page."
                    : "Add a photo of the layout, marshal posts and more on the Track Layouts page later.";
                break;

            case 4:
                _lblStepTitle.Text = "Your racing classes";
                _lblStepBody.Text =
                    "Classes group drivers into heats and standings. If everyone races together, a single class "
                    + "such as \"Open\" is all you need.";
                _lblClassesHint.Text = _raceClasses.All.Count > 0
                    ? $"Already set up: {string.Join(", ", _raceClasses.All.Select(c => c.Name).Take(8))}. "
                      + "Race formats, lap limits and rules are on the Classes page."
                    : "Race formats, lap limits and rules for each class are on the Classes page.";
                break;

            case 5:
                _lblStepTitle.Text = "Add a few drivers";
                _lblStepBody.Text =
                    "Enter anyone you know now, or skip this: when a transponder the app doesn't know crosses the "
                    + "line, it asks you who it is. The number is printed on the transponder.";
                FillDriverClassChoices();
                break;

            case 6:
                _lblStepTitle.Text = "You're all set";
                _lblStepBody.Text = "Here's what's ready. Everything can be changed later from the sidebar.";
                FillSummary();
                break;
        }

        // This step's page on show, and every other one hidden - which takes
        // it out of the layout as well as out of sight.
        var pages = StepPages;

        for (var i = 0; i < pages.Length; i++)
        {
            pages[i].Visible = i == _step;
        }

        var last = _step == StepNames.Length - 1;

        _btnBack.Visible = _step > 0;
        _btnSkipAll.Visible = !last;
        _btnNext.Text = last ? "Finish" : (_step == 0 ? "Get started" : "Next");
        _lblProgress.Text = _step == 0 || last ? string.Empty : $"Step {_step} of {StepNames.Length - 2}";

        // Nothing to say on the welcome page or the last one - and on the
        // last, nothing on the left at all, so no empty row is left there.
        _lblProgress.Visible = _lblProgress.Text.Length > 0;
        _footerLeft.Visible = !last;

        ApplyFrameColours();

        // Enter moves on - except in the classes box, where it starts a
        // new line.
        AcceptButton = _step == 4 ? null : _btnNext;
        _btnNext.Focus();
    }

    /// <summary>
    /// The colours the theme pass would otherwise flatten: the rail keeps
    /// the sidebar's, the title the accent, hints the muted text, and the
    /// buttons their kinds.
    /// </summary>
    private void ApplyFrameColours()
    {
        _rail.BackColor = AppTheme.SidebarBackground;
        _lblBrand.BackColor = AppTheme.SidebarBackground;
        _lblBrand.ForeColor = AppTheme.Primary;
        _lblRailCaption.BackColor = AppTheme.SidebarBackground;
        _lblRailCaption.ForeColor = AppTheme.SidebarTextMuted;

        for (var i = 0; i < _railLabels.Length; i++)
        {
            var current = i == _step;
            _railLabels[i].BackColor = current ? AppTheme.SidebarHover : AppTheme.SidebarBackground;
            _railLabels[i].ForeColor = current ? AppTheme.Primary : AppTheme.SidebarText;
            _railLabels[i].Font = current ? AppTheme.BoldFont : AppTheme.BodyFont;
        }

        _lblStepTitle.ForeColor = AppTheme.Primary;
        _lblStepBody.ForeColor = AppTheme.TextDark;
        _lblProgress.ForeColor = AppTheme.TextMuted;

        foreach (var hint in new[] { _lblWelcomeRestore, _lblLookDescription, _lblTimingHint, _lblTrackHint, _lblClassesHint, _lblDriversHint, _lblNameHeader, _lblTransponderHeader })
        {
            hint.ForeColor = AppTheme.TextMuted;
        }

        if (_sound is not null)
        {
            _sound.Hint.ForeColor = AppTheme.TextMuted;
        }

        AppTheme.StyleButton(_btnNext, AppTheme.ButtonKind.Primary);
        AppTheme.StyleButton(_btnBack, AppTheme.ButtonKind.Secondary);
        AppTheme.StyleButton(_btnSkipAll, AppTheme.ButtonKind.Secondary);
        AppTheme.StyleButton(_btnRestore, AppTheme.ButtonKind.Secondary);
        AppTheme.StyleButton(_btnRescan, AppTheme.ButtonKind.Secondary);

        foreach (Control chip in _classChips.Controls)
        {
            if (chip is Button button)
            {
                AppTheme.StyleButton(button, AppTheme.ButtonKind.Secondary);
            }
        }
    }

    private void NextClicked()
    {
        SaveCurrentStep();

        if (_step < StepNames.Length - 1)
        {
            ShowStep(_step + 1);
            return;
        }

        // Only from the last step, where the question was on screen - Skip
        // setup and the title-bar X leave the setting as it was.
        _settings.HideUntestedPages = _chkHideUntested.Checked;
        Finish(_chkTour.Checked);
    }

    private void SkipEverything()
    {
        // What was already typed on this step is kept - skipping the rest
        // is not the same as throwing this one away.
        SaveCurrentStep();
        Finish(takeTour: false);
    }

    private void Finish(bool takeTour)
    {
        TakeTour = takeTour;
        _settings.HasCompletedFirstRun = true;
        _settings.Save();
        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>
    /// Closed with the title-bar X: the step on screen is kept, as Back, Next
    /// and Skip setup keep it. Nothing did, and MainForm marks the first run
    /// done however the window went, so the drivers or classes typed on the
    /// open step were lost and the wizard never came back to ask again.
    /// Finish has already saved, and SaveCurrentStep is safe twice anyway.
    /// </summary>
    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (DialogResult != DialogResult.OK)
        {
            SaveCurrentStep();
        }

        base.OnFormClosing(e);
    }

    /// <summary>Commits the step being left. Safe to call more than once for the same step.</summary>
    private void SaveCurrentStep()
    {
        switch (_step)
        {
            case 1:
                SaveLook();
                break;
            case 2:
                SaveTiming();
                break;
            case 3:
                SaveTrack();
                break;
            case 4:
                SaveClasses();
                break;
            case 5:
                SaveDrivers();
                break;
        }
    }

    private void SaveLook()
    {
        _settings.Use24HourTime = _chk24Hour.Checked;
        AppTime.Use24Hour = _chk24Hour.Checked;

        if (SelectedLook is { } look)
        {
            _settings.AppLook = look.Key;

            // The family as the old Light/Dark/Synthwave setting too - the
            // same pairing LooksControl saves.
            _settings.ThemeMode = look.Mode switch
            {
                AppTheme.Mode.Dark => 1,
                AppTheme.Mode.Synthwave => 2,
                _ => 0,
            };

            RestartForLook = !string.Equals(look.Key, _startingLookKey, StringComparison.OrdinalIgnoreCase);
            _doneLook = RestartForLook ? $"{look.Name} (the app restarts to apply it)" : look.Name;
        }

        _settings.Save();

        // Applied now, as every step's answer is saved as the step is left.
        _sound?.Save();
    }

    private void SaveTiming()
    {
        // The saved number, not the picker position - see DecoderConnection.
        _settings.LastConnectionType = DecoderConnection.ValueAt(_cmbConnection.SelectedIndex);
        _doneTiming = _cmbConnection.SelectedItem?.ToString();

        if (_settings.LastConnectionType == DecoderConnection.ILap)
        {
            // Only a port that was actually chosen. None listed means the
            // decoder is not plugged in yet, and whatever was remembered
            // before is better kept than cleared.
            if (_cmbSerialPort.SelectedItem is string port)
            {
                _settings.ILapPort = port;
                _doneTiming += $" on {port}";
            }

            if (_cmbBaud.SelectedItem is int baud)
            {
                _settings.ILapBaud = baud;
            }
        }

        _settings.Save();
    }

    private void SaveTrack()
    {
        // Where you race first: it is saved whether or not a track is named.
        var where = _rdoHome.Checked ? RacingSetting.Home : RacingSetting.Club;

        if (_settings.RacingAt != where)
        {
            _settings.RacingAt = where;
            _settings.Save();
        }

        var trackName = _txtTrackName.Text.Trim();
        if (trackName.Length == 0)
        {
            return;
        }

        if (!_trackLayouts.All.Any(t => string.Equals(t.Name, trackName, StringComparison.OrdinalIgnoreCase)))
        {
            _trackLayouts.Upsert(new TrackLayout { Name = trackName });
        }

        _settings.LastTrackLayoutName = trackName;
        _settings.Save();
        _doneTrack = trackName;
    }

    private void SaveClasses()
    {
        foreach (var name in TypedClasses())
        {
            if (!_raceClasses.All.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                // With a race length (setup plan A5): see RaceFormat.ForNewClass.
                // And with the length and finish asked for on this step (setup plan step 8).
                _raceClasses.Upsert(new RaceClass { Name = name, Format = _classLength.Format() });
            }

            if (!_doneClasses.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                _doneClasses.Add(name);
            }
        }
    }

    private void SaveDrivers()
    {
        var className = _cmbDriverClass.SelectedItem as string ?? string.Empty;

        // Counted afresh on every pass. This runs each time the step is left
        // (Back, Next or a click on the rail), and a skipped row is left
        // filled in on purpose - so a running total counted the same row
        // again on every pass, and kept warning about it after it was fixed
        // and saved. Every row still unsaved is on screen, so this pass sees
        // them all: the count is the rows that are still not saved now.
        _skippedDrivers = 0;

        for (var i = 0; i < DriverRows; i++)
        {
            var name = _txtDriverNames[i].Text.Trim();
            var transponder = _txtTransponders[i].Text.Trim();

            if (name.Length == 0 && transponder.Length == 0)
            {
                continue;
            }

            // A name with no number (or the other way round) cannot be
            // matched to a crossing, and a number already on file belongs
            // to someone - neither is saved, and the summary says so. The
            // row is left filled in, so going Back shows what to fix.
            if (name.Length == 0 || transponder.Length == 0 || _profiles.Get(transponder) is not null)
            {
                _skippedDrivers++;
                continue;
            }

            _profiles.Upsert(new DriverProfile
            {
                Transponder = transponder,
                DriverName = name,
                ClassName = className,
            });

            _doneDrivers++;

            // Cleared once saved, so going Back and Next again cannot count
            // (or refuse) the same driver twice.
            _txtDriverNames[i].Text = string.Empty;
            _txtTransponders[i].Text = string.Empty;
        }
    }

    // --- Helpers ---------------------------------------------------------

    private Look? SelectedLook => (_lstLooks.SelectedItem as LookItem)?.Look;

    private IEnumerable<string> TypedClasses() =>
        _txtClasses.Lines
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private void AddClassLine(string name)
    {
        if (TypedClasses().Contains(name, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        var text = _txtClasses.Text.TrimEnd();
        _txtClasses.Text = text.Length == 0 ? name : text + Environment.NewLine + name;
    }

    private void FillDriverClassChoices()
    {
        var previous = _cmbDriverClass.SelectedItem as string;
        var names = _raceClasses.All.Select(c => c.Name)
            .Concat(TypedClasses())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        _cmbDriverClass.Items.Clear();
        _cmbDriverClass.Items.Add(string.Empty);

        foreach (var name in names)
        {
            _cmbDriverClass.Items.Add(name);
        }

        // As wide as the longest class's name, now the list holds them.
        FitPicker(_cmbDriverClass, DriverClassWidth);

        var wanted = !string.IsNullOrEmpty(previous) ? previous : _doneClasses.FirstOrDefault() ?? names.FirstOrDefault() ?? string.Empty;
        _cmbDriverClass.SelectedItem = _cmbDriverClass.Items.Contains(wanted) ? wanted : string.Empty;
        _cmbDriverClass.Enabled = names.Count > 0;
    }

    /// <summary>Shows the COM port and baud rows only while I-Lap is the chosen connection.</summary>
    private void ShowILapFields()
    {
        var isILap = DecoderConnection.ValueAt(_cmbConnection.SelectedIndex) == DecoderConnection.ILap;

        // The grid the two rows are in, so the hint under them closes up
        // when they go.
        _iLapFields.Visible = isILap;

        if (isILap && _cmbSerialPort.Items.Count == 0)
        {
            FillSerialPorts();
        }

        // A Beta decoder is reached by a COM port, the network or a simulated
        // one, and that is chosen on the Connection page - it was told to
        // "plug the EasyLap in by USB" (2 Nov 2026).
        var isBeta = DecoderConnection.IsStream(DecoderConnection.ValueAt(_cmbConnection.SelectedIndex));

        _lblTimingHint.Text = isILap
            ? "Leave the baud rate at 9600 unless the decoder has been set differently. No port listed? Plug the "
              + "decoder in and press Rescan - or pick it later on the Connection page.\r\n\r\n"
              + "No decoder here yet? You can still try everything with a simulated race in Demo Mode."
            : isBeta
            ? "Choose how HeatCaller reaches it - a COM port, the network, or a simulated decoder to rehearse with - "
              + "on the Connection page once this is done. It's in Beta: run your usual timing alongside it the first time.\r\n\r\n"
              + "No decoder here yet? You can still try everything with a simulated race in Demo Mode."
            : "Plug the EasyLap in by USB before you connect - there is nothing else to set.\r\n\r\n"
              + "No decoder here yet? You can still try everything with a simulated race in Demo Mode.";
    }

    /// <summary>Lists the COM ports present now and picks one of them, or none (see PortToSelect).</summary>
    private void FillSerialPorts()
    {
        var ports = ILapClient.AvailablePorts();
        var current = _cmbSerialPort.SelectedItem as string;

        _cmbSerialPort.Items.Clear();
        _cmbSerialPort.Items.AddRange(ports);

        // As wide as the longest port's name, now the list holds them.
        FitPicker(_cmbSerialPort, PortWidth);

        _cmbSerialPort.SelectedIndex = PortToSelect(ports, _settings.ILapPort, current, _listedPorts);
        _listedPorts = ports;
    }

    /// <summary>The ports the last FillSerialPorts listed - null before the first - so a Rescan can tell which one just appeared.</summary>
    private string[]? _listedPorts;

    /// <summary>
    /// Where the COM port picker lands: on the port already picked while
    /// it is still listed; else on the one port that appeared since the last
    /// listing - the decoder just plugged in, as the hint asks before
    /// Rescan; else on the remembered port; else on nothing (-1).
    ///
    /// Never on a port nobody chose. It used to fall back to the last port
    /// listed, as the Connection page does - but whatever is selected here
    /// is saved as the I-Lap's port when the step is left (SaveTiming), and
    /// "Connect when HeatCaller starts" then opened that port by itself: a
    /// laptop's Bluetooth port, taken for the I-Lap before it was plugged
    /// in. So the first listing, with nothing to compare, picks no new port,
    /// and nor does a Rescan that found two. A Rescan used to go by the
    /// remembered port alone, which left the picker blank once the decoder
    /// was plugged in, and dropped a port already picked by hand.
    /// </summary>
    internal static int PortToSelect(IReadOnlyList<string> ports, string? remembered, string? current = null, IReadOnlyList<string>? listedBefore = null)
    {
        int IndexOf(string? port)
        {
            for (var i = 0; i < ports.Count; i++)
            {
                if (string.Equals(ports[i], port, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        if (!string.IsNullOrEmpty(current) && IndexOf(current) is var kept and >= 0)
        {
            return kept;
        }

        if (listedBefore is not null)
        {
            var appeared = ports.Where(p => !listedBefore.Contains(p, StringComparer.OrdinalIgnoreCase)).ToList();
            if (appeared.Count == 1)
            {
                return IndexOf(appeared[0]);
            }
        }

        return IndexOf(remembered);
    }

    /// <summary>What each step came to, as the last step shows it - see BuildDone.</summary>
    private void FillSummary()
    {
        var decoder = _doneTiming ?? DecoderConnection.Items()[DecoderConnection.IndexOf(_settings.LastConnectionType)].ToString();
        var track = _doneTrack
                    ?? (_settings.LastTrackLayoutName.Length > 0 ? _settings.LastTrackLayoutName : "none yet - add one on Track Layouts");
        var classes = _doneClasses.Count > 0
            ? string.Join(", ", _doneClasses)
            : _raceClasses.All.Count > 0 ? $"{_raceClasses.All.Count} already set up" : "none yet - add them on Classes";
        var drivers = _doneDrivers > 0
            ? $"{_doneDrivers} added"
            : "none yet - they can be added as they cross the line";

        _lblDoneLook.Text = _doneLook ?? Looks.ForApp(_settings).Name;
        _lblDoneDecoder.Text = decoder;
        _lblDoneTrack.Text = track;
        _lblDoneWhere.Text = _settings.RacingAt == RacingSetting.Home ? "At home" : "At a club";
        _lblDoneClasses.Text = classes;
        _lblDoneDrivers.Text = drivers;
        _lblDoneSound.Text = _sound?.Summary ?? string.Empty;

        _lblDoneSkipped.Text = _skippedDrivers > 0
            ? $"{_skippedDrivers} driver row(s) weren't saved - each needs a name and a transponder number "
              + "that isn't already on file. Go Back to fix them."
            : string.Empty;

        _lblDoneSkipped.Visible = _skippedDrivers > 0;
    }

    private static void PaintLookPreview(Graphics g, Rectangle bounds, Look? look)
    {
        if (look is null)
        {
            return;
        }

        static Color Pick(string? hex, Color fallback)
        {
            try
            {
                return string.IsNullOrWhiteSpace(hex) ? fallback : ColorTranslator.FromHtml(hex);
            }
            catch
            {
                return fallback;
            }
        }

        var c = look.Colors;
        var bg = Pick(c.Bg, Color.Black);
        var surface = Pick(c.Surface, bg);
        var sidebar = Pick(c.Sidebar, bg);
        var primary = Pick(c.Primary, Color.OrangeRed);
        var accent = Pick(c.Accent, primary);
        var text = Pick(c.Text, Color.White);
        var muted = Pick(c.Muted, Color.Gray);

        using (var b = new SolidBrush(bg))
        {
            g.FillRectangle(b, bounds);
        }

        // A tiny window: a sidebar, a card with a heading, and two buttons.
        //
        // Every measure in it at the text size in use, as the preview's own
        // height is (SizeFields gives it AtTextSize(PreviewHeight)), so the
        // picture keeps its proportions: at fixed pixels the sidebar, the
        // insets and the bars stayed their normal-size selves in a preview
        // twice as tall at 200%, huddled in its top-left corner. Worked out
        // on every paint.
        static int At(int pixels) => UiLayout.AtTextSize(pixels);

        var inset = At(14);
        var side = new Rectangle(bounds.Left, bounds.Top, At(60), bounds.Height);
        using (var b = new SolidBrush(sidebar))
        {
            g.FillRectangle(b, side);
        }

        for (var i = 0; i < 5; i++)
        {
            using var b = new SolidBrush(i == 0 ? primary : muted);
            g.FillRectangle(b, side.Left + At(10), side.Top + inset + i * At(18), i == 0 ? At(38) : At(30), At(6));
        }

        var card = new Rectangle(side.Right + inset, bounds.Top + inset, bounds.Width - side.Width - inset * 2, bounds.Height - inset * 2);

        void Fill(Color colour, int x, int y, int w, int h)
        {
            using var b = new SolidBrush(colour);
            g.FillRectangle(b, x, y, w, h);
        }

        var cardInset = At(12);

        Fill(surface, card.Left, card.Top, card.Width, card.Height);
        Fill(text, card.Left + cardInset, card.Top + inset, At(110), At(8));
        Fill(muted, card.Left + cardInset, card.Top + At(30), Math.Min(At(150), card.Width - cardInset * 2), At(5));
        Fill(muted, card.Left + cardInset, card.Top + At(40), Math.Min(At(130), card.Width - cardInset * 2), At(5));
        Fill(primary, card.Left + cardInset, card.Bottom - At(30), At(70), At(18));
        Fill(accent, card.Left + At(92), card.Bottom - At(30), At(40), At(18));

        using var edge = new Pen(Color.FromArgb(80, text));
        g.DrawRectangle(edge, bounds.Left, bounds.Top, bounds.Width - 1, bounds.Height - 1);
    }

    private sealed class LookItem
    {
        public LookItem(Look look) => Look = look;

        public Look Look { get; }

        public override string ToString() => Look.Name;
    }
}
