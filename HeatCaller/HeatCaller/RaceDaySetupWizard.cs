using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// Everything a race day needs, asked once, in order.
///
/// The app already tells you what is missing - the Today checklist and the
/// programme banner both do - but only one thing at a time, and each fix
/// is on a different page. On a club night that is fine; the first time
/// somebody sets a night up it is a scavenger hunt. This walks the same
/// ground in one window: the classes, the drivers, the day itself, who is
/// here, and the programme.
///
/// IT IS NOT A SECOND WAY TO DO ANY OF IT. Every step calls the command
/// that already exists - the class store, the profile store, The Day's own
/// StartNewDayFromWizard and ChangeDayFromElsewhere, the programme's own
/// build. Nothing here writes a race day itself. That is deliberate and it
/// is the whole reason this file is as thin as it is: a wizard that wrote
/// its own version of "start a day" would be a second implementation to
/// keep in step, with its own undo entry and its own bugs, and TodayControl
/// already says out loud that it refuses to be one.
///
/// Every step can be skipped, jumped to from the rail, or abandoned with
/// Skip Setup, and each one saves as you leave it rather than at the end -
/// so a wizard closed halfway still did the half it got through. The same
/// arrangement FirstRunWizard makes, for the same reason.
///
/// On the layout pieces (DialogFit): the rail down the left, flush with
/// the window's edges (a Beside, the rail its column), and beside it the
/// step's title and what it is about, the step itself taking the height
/// between those and the footer - Skip setup and the progress on the left,
/// Back and Next on the right. Each step is a page of its own, its fields
/// lined up in a field grid, shown while it is the step and hidden
/// otherwise; a step taller than the room it has scrolls, rather than the
/// window growing. The window opens at the size it always had and can be
/// resized. It used to be placed in pixels: a fixed 850x640 window, the
/// rail 190px wide at every text size and each step's fields at
/// coordinates in a 600x400 box, which a larger text size ran off the
/// bottom and the right of.
/// </summary>
internal sealed class RaceDaySetupWizard : Form
{
    /// <summary>The rail's width at the normal text size - wider only when its step names and the marks beside them need it.</summary>
    private const int RailWidth = 190;

    /// <summary>How wide and tall the window opens inside its frame, at the normal text size - the size it always was.</summary>
    private const int WindowWidth = 850;

    private const int WindowHeight = 640;

    /// <summary>A step has at least this much height at the normal text size - a few lines - before the window scrolls instead of the step.</summary>
    private const int StepLeastHeight = 120;

    /// <summary>The width the day's fields and the classes list were made at, at the normal text size.</summary>
    private const int FieldWidth = 300;

    /// <summary>The class filter's width at the normal text size - wider when a class's name needs it.</summary>
    private const int ClassFilterWidth = 200;

    /// <summary>The classes list is this many of its rows tall.</summary>
    private const int ClassRows = 4;

    /// <summary>Who's here shows at least this many drivers before the step scrolls instead.</summary>
    private const int WhosHereLeastRows = 4;

    /// <summary>A step on the rail: its least height, and the room before its name, at the normal text size.</summary>
    private const int RailStepHeight = 34;

    private const int RailStepInset = 12;

    /// <summary>The done/needed mark beside a step on the rail, and the room to its right, at the normal text size.</summary>
    private const int MarkSize = 18;

    private const int MarkInset = 12;

    private const string BrandText = "HEATCALLER";

    private const string RaceDayShapeHint =
        "A race day qualifies in heats and then runs finals built from how qualifying went.";

    private const string ClubNightShapeHint =
        "A club night is just races - everyone races, several times, and that is the night. No finals.";

    // The rounds caption says races on a club night and heats on a race day
    // - see LoadRacingStep. The caption column is as wide as the longer, so
    // changing it never moves the fields (FieldGrid.LineUp).
    private const string HeatsCaption = "Heats each driver gets:";
    private const string RacesCaption = "Races each driver gets:";

    // Drivers and Who's here were two steps, and not even next to each
    // other: adding a new driver came before the day existed, ticking who
    // had turned up came after - the same people, two screens apart. They
    // are one step now, AFTER the day (check-ins are per day, so there is
    // nowhere to record one before it exists): tick who is here, and add
    // anyone new at the bottom - checked straight in.
    //
    // Classes and Racing are one step as well. "Classes" only ever typed new
    // class names in; "Racing" ticked which ones were out tonight - the same
    // list twice. The one step ticks tonight's classes, adds a new one with
    // Add Class, and sets how they race.
    private static readonly string[] StepNames =
    { "Tonight", "The day", "Classes", "Drivers", "Program", "All set" };

    // The steps by name, for opening the wizard part-way through - the
    // Ready to Race bar on Next Steps opens it at the first one not done.
    // Indexes into StepNames, so they move if that list ever does.
    public const int StepDay = 1;
    public const int StepClasses = 2;

    /// <summary>The same step as <see cref="StepClasses"/> - tonight's classes are ticked on it.</summary>
    public const int StepRacing = StepClasses;

    public const int StepDrivers = 3;

    /// <summary>The same step as <see cref="StepDrivers"/> - who is here is ticked on it.</summary>
    public const int StepWhosHere = StepDrivers;

    public const int StepProgramme = 4;

    private readonly AppSettings _settings;
    private readonly RaceClassStore _raceClasses;
    private readonly ProfileStore _profiles;
    private readonly TrackLayoutStore _trackLayouts;
    private readonly VenueStore _venues;
    private readonly RaceEventStore _events;
    private readonly CheckInStore _checkIns;
    private readonly RaceDaySetupHost _host;

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

    private int _step;

    // --- the drivers step: adding someone new, in a small window of its own ---
    private readonly Button _btnAddDriver = new();

    // --- the day ---
    private readonly PageStack _dayStep = new();
    private readonly ComboBox _cmbShape = new();

    /// <summary>What the kind of night means - as tall as the longer wording, so the fields under it never move as the box is used.</summary>
    private readonly SteadyLabel _lblShapeHint = new() { Wordings = new[] { RaceDayShapeHint, ClubNightShapeHint } };

    private readonly TextBox _txtDayName = new();
    private readonly ComboBox _cmbVenue = new();
    private readonly ComboBox _cmbTrack = new();
    private readonly Label _lblDayState = new();

    // --- racing ---
    private readonly PageStack _racingStep = new();
    private readonly FieldGrid _racingGrid = new();
    private readonly CheckedListBox _lstClasses = new ThemedCheckedListBox();
    private readonly NumericUpDown _numRounds = new();
    private readonly NumericUpDown _numHeatSize = new();
    private readonly CheckBox _chkFirstRace = new ThemedCheckBox();
    private readonly TimePicker _dtpFirstRace = new();

    /// <summary>The rounds caption, reworded for the kind of night - see LoadRacingStep. Made by the grid it is in.</summary>
    private Label? _lblRoundsCaption;

    /// <summary>
    /// The race format each ticked class runs - laps or minutes, the start,
    /// the warm-up - one line per class with a button to change it.
    ///
    /// It was the one thing about a night the wizard never asked: the
    /// format lives on the class, and the first place anyone met it was
    /// Race Control, with the field already on the grid.
    /// </summary>
    private readonly Label _lblFormatsCaption = new();
    private readonly Label _lblNoFormats = new();
    private readonly List<FormatRow> _formatRows = new();
    private readonly Button _btnAddClass = new();

    // --- the drivers step: who's here ---
    private readonly FillPage _driversStep = new();
    private readonly ComboBox _cmbWhoClass = new();
    private readonly CheckBox _chkOnlyMissing = new ThemedCheckBox();
    private readonly CheckedListBox _lstWhosHere = new ThemedCheckedListBox();
    private readonly Button _btnCheckAllShown = new();
    private readonly Button _btnClearAll = new();

    /// <summary>
    /// True while the list is being filled.
    ///
    /// Ticking a box raises ItemCheck, and filling the list ticks a box per
    /// driver already checked in - so without this, opening the step would
    /// write every one of them back to the store as a fresh check-in.
    /// </summary>
    private bool _fillingWhosHere;

    /// <summary>One driver in the list. The transponder is the key; the caption is what a person reads.</summary>
    private sealed record DriverItem(string Transponder, string Caption)
    {
        public override string ToString() => Caption;
    }

    private readonly Label _lblCheckInState = new();
    private readonly CheckBox _chkCheckedInOnly = new ThemedCheckBox();
    private readonly Button _btnGoToCheckIn = new();

    // --- the programme ---
    private readonly PageStack _programStep = new();
    private readonly Label _lblProgrammeState = new();

    /// <summary>
    /// Whether the programme step has something to build. When it does, the
    /// wizard's own Next button is "Build It" and builds on the way to the
    /// last page - there used to be a separate "Build it now" button beside
    /// a Next that did not build, two presses for the one thing.
    /// </summary>
    private bool _programmeBuildable;

    // --- "can't build yet": the steps that still need you ---
    //
    // A sentence in the step's body was too easy to read past, and one
    // warning card still left you working out which step it meant. Now the
    // three steps a program is made from each get a card - DONE or NEEDED -
    // with a button to the step on every one that isn't done, and the rail
    // on the left says the same thing: a tick, or a red "!".
    private readonly Label _lblNeedsTitle = new();
    private readonly Label _lblNeedsNote = new();
    private readonly ReadinessCard _cardDay = new("The day", "Go to The Day");
    private readonly ReadinessCard _cardClasses = new("Classes", "Go to Classes");
    private readonly ReadinessCard _cardDrivers = new("Drivers", "Go to Drivers");
    private readonly List<(Button Button, AppTheme.ButtonKind Kind)> _fixButtons = new();

    /// <summary>What the rail shows beside a step.</summary>
    private enum RailMark
    {
        None,
        Done,
        Needed,
    }

    private readonly RailMark[] _railMarks = new RailMark[StepNames.Length];

    /// <summary>
    /// What building would do right now. Read afresh each time a step is
    /// shown - see RefreshReadiness - so it is always after the last save.
    /// </summary>
    private ProgramAnswer _plan = new(false, string.Empty, string.Empty);

    // --- step 7: all set ---
    private readonly PageStack _summaryStep = new();
    private readonly Label _lblSummary = new();

    /// <summary>A page to go to once this closes, or null. Set by "Take me to check-in".</summary>
    public string? GoToPageOnClose { get; private set; }

    public RaceDaySetupWizard(
        AppSettings settings,
        RaceClassStore raceClasses,
        ProfileStore profiles,
        TrackLayoutStore trackLayouts,
        VenueStore venues,
        RaceEventStore events,
        CheckInStore checkIns,
        RaceDaySetupHost host,
        int startStep = 0)
    {
        _settings = settings;
        _raceClasses = raceClasses;
        _profiles = profiles;
        _trackLayouts = trackLayouts;
        _venues = venues;
        _events = events;
        _checkIns = checkIns;
        _host = host;

        Text = "Set Up Tonight";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        var page = BuildLayout();
        AppTheme.ApplyRecursively(this);
        SizeFields();

        // Straight to the step that needs doing when opened for one, rather
        // than the welcome page and a walk through what is already done.
        ShowStep(startStep);

        // The size it always opened at; it can be made bigger, or smaller
        // down to what the steps' titles, the footer and a few lines of a
        // step need - the step scrolls below that.
        DialogFit.Keep(this, page, width: WindowWidth, height: WindowHeight);
    }

    private RaceEvent? OpenDay => _events.Active is { IsLegacy: false, IsOpen: true } day ? day : null;

    /// <summary>The steps' own pages, in order - the welcome step has none. See ShowStep.</summary>
    private Control[] StepPages => new Control[] { _dayStep, _racingStep, _driversStep, _programStep, _summaryStep };

    /// <summary>The three cards on the program step, in the order of the steps they stand for.</summary>
    private ReadinessCard[] Cards => new[] { _cardDay, _cardClasses, _cardDrivers };

    // --- Layout ---------------------------------------------------------

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
        _btnSkipAll.Click += (_, _) => Finish();

        _btnBack.Text = "Back";
        _btnBack.Click += (_, _) =>
        {
            SaveCurrentStep();
            ShowStep(_step - 1);
        };

        _btnNext.Text = "Next";
        _btnNext.Click += (_, _) => NextClicked();

        BuildDayStep();
        BuildRacingStep();
        BuildCheckInStep();
        BuildProgrammeStep();
        BuildSummaryStep();

        // Every step's page fills the holder; only the one on show is
        // visible, so only it takes the room. None has margins of its own -
        // the page they are in gives them - nor any room under its last part
        // when it scrolls, for the reason DialogFit.Body gives.
        _stepHolder.Tag = AppTheme.KeepBackColor;

        foreach (var stepPage in StepPages)
        {
            stepPage.Padding = Padding.Empty;
            stepPage.Visible = false;

            if (stepPage is ScrollableControl scrolls)
            {
                ScrollTheming.NoRoomBelow(scrolls);
            }

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
    /// The rail: the brand, what the window is for, and the steps, each
    /// clickable, with a mark beside it once it is done or still needed.
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

        _lblRailCaption.Text = "Setting up a race day";
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

        // Tall enough for the current step's name in the bold face, whose
        // mark is beside it (PaintRailMark).
        var stepHeight = Math.Max(
            UiLayout.AtTextSize(RailStepHeight),
            Pieces.LineHeight(AppTheme.BoldFont) + UiLayout.AtTextSize(UiLayout.RowGap + 2));

        for (var i = 0; i < StepNames.Length; i++)
        {
            var index = i;

            var label = new Label
            {
                Text = StepNames[i],
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

            // Done or still needed, beside the step's name - see RefreshReadiness.
            label.Paint += (_, e) => PaintRailMark(e.Graphics, label, _railMarks[index]);

            _railLabels[i] = label;
            _rail.Add(label, gapBelow: i == StepNames.Length - 1 ? 0 : UiLayout.HintGap);
        }
    }

    /// <summary>
    /// How wide the rail is: the width it always had at the normal text
    /// size, at the size in use - or, if they need more, the widest step's
    /// name in the bold face with its mark beside it, or the brand, with the
    /// rail's margins either side.
    /// </summary>
    private static int MeasureRailWidth()
    {
        var inset = UiLayout.AtTextSize(RailStepInset);

        var names = StepNames.Max(name => TextRenderer.MeasureText(name, AppTheme.BoldFont).Width);
        var steps = inset + names + UiLayout.LabelGap + UiLayout.AtTextSize(MarkSize) + UiLayout.AtTextSize(MarkInset);
        var brand = inset + TextRenderer.MeasureText(BrandText, AppTheme.NavFont).Width;

        return Math.Max(UiLayout.AtTextSize(RailWidth), Math.Max(steps, brand) + 2 * inset);
    }

    /// <summary>
    /// The day: what kind of night, what it is called, where and on which
    /// layout - lined up in a grid - and whether a day is open.
    /// </summary>
    private void BuildDayStep()
    {
        _cmbShape.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbShape.Items.Add("Race day - heats, then finals");
        _cmbShape.Items.Add("Club night - just races");
        _cmbShape.SelectedIndex = 0;
        _cmbShape.SelectedIndexChanged += (_, _) => ApplyShapeHint();

        _cmbVenue.DropDownStyle = ComboBoxStyle.DropDown;
        _cmbTrack.DropDownStyle = ComboBoxStyle.DropDown;

        _lblDayState.AutoSize = true;
        _lblDayState.Font = AppTheme.SmallFont;

        var grid = new FieldGrid();

        // The kind of night first, and what it means under it. Captioned as
        // on Race Day's Setup: with the other fields' captions beside them,
        // a drop-down in their column with none of its own read as a field
        // whose caption had gone missing (layout pictures, 29 Sep).
        grid.AddRow("What kind of night:", _cmbShape);
        grid.AddHint(_lblShapeHint);

        grid.AddRow("Call it:", _txtDayName);
        grid.AddRow("Venue:", _cmbVenue);
        grid.AddRow("Track layout:", _cmbTrack);

        _dayStep.Add(grid, gapBelow: UiLayout.RowGap);
        _dayStep.Add(_lblDayState, gapBelow: 0);
    }

    /// <summary>
    /// Tonight's classes and how they race: the classes ticked from a list
    /// with Add Class under it, the numbers, then a line per ticked class
    /// with its race format.
    /// </summary>
    private void BuildRacingStep()
    {
        _lstClasses.CheckOnClick = true;

        // Its own height, in whole rows or not - see SizeFields.
        _lstClasses.IntegralHeight = false;

        // A class the club has not raced before - the old Classes step's
        // whole job, now a button under the list it adds to.
        _btnAddClass.Text = "Add Class...";
        _btnAddClass.Click += (_, _) => AddClass();

        // The format rows follow the ticks: tick a class and its row
        // appears. Posted, because ItemCheck fires before the tick lands.
        _lstClasses.ItemCheck += (_, _) =>
        {
            if (IsHandleCreated && !IsDisposed)
            {
                BeginInvoke(new Action(FillFormatRows));
            }
        };

        _numRounds.Minimum = 1;
        _numRounds.Maximum = 10;
        _numRounds.Value = 3;

        _numHeatSize.Minimum = 2;
        _numHeatSize.Maximum = 20;
        _numHeatSize.Value = 8;

        _chkFirstRace.Text = "First race at";
        _chkFirstRace.AutoSize = true;

        // As big as its time needs in its font (TimePicker.GetPreferredSize) -
        // at 120x26 it held the time at the normal text size and clipped it
        // at any larger one.
        _dtpFirstRace.AutoSize = true;
        _dtpFirstRace.Value = TimeSpan.FromHours(10);

        // To the right-hand edge: a class's name can be long, and in the
        // column's width it was cut off ("Kyosho Mini-Z MR-03 Stock 70T
        // Tourin", layout pictures, 29 Sep).
        _racingGrid.AddRow("Classes racing:", _lstClasses, stretch: true);
        _racingGrid.AddField(new ButtonBar().With(_btnAddClass));
        _lblRoundsCaption = _racingGrid.AddRow(HeatsCaption, _numRounds);
        _racingGrid.AddRow("Drivers per heat:", _numHeatSize);
        _racingGrid.AddField(FieldGrid.Inline(_chkFirstRace, _dtpFirstRace));

        _lblFormatsCaption.Text = "Race format - what each class runs tonight";
        _lblFormatsCaption.AutoSize = true;
        _lblFormatsCaption.Font = AppTheme.SmallFont;

        _lblNoFormats.Text = "Tick a class above to see the format it runs.";
        _lblNoFormats.AutoSize = true;
        _lblNoFormats.Font = AppTheme.SmallFont;

        _racingStep.Add(_racingGrid);
        _racingStep.Add(_lblFormatsCaption, gapBelow: UiLayout.RowGap);

        // Then a line per ticked class, added as they are needed - see
        // FillFormatRows - or this, when none is ticked.
        _racingStep.Add(_lblNoFormats, gapBelow: 0);
    }

    /// <summary>
    /// One class's line in the formats: its name over its format, and the
    /// button that changes it against the right-hand edge (a Beside, the
    /// button its column). Kept, and given another class to show when the
    /// ticks change - see FillFormatRows.
    /// </summary>
    private sealed class FormatRow
    {
        public FormatRow(Beside line, Label name, Label format)
        {
            Line = line;
            Name = name;
            Format = format;
        }

        public Beside Line { get; }

        public Label Name { get; }

        public Label Format { get; }

        /// <summary>The class the line is showing now - what its button edits.</summary>
        public string ClassName { get; set; } = string.Empty;
    }

    /// <summary>
    /// A line per ticked class, in the list's order: its name, its format,
    /// and a button that opens the same editor the Classes page does.
    ///
    /// The lines are kept rather than made afresh each time, and handed the
    /// classes in order - the first ticked class to the first line - with any
    /// left over hidden. A page built from the layout pieces is added to,
    /// not taken apart; a new line is made only when there are more ticked
    /// classes than there have been before.
    /// </summary>
    private void FillFormatRows()
    {
        if (IsDisposed)
        {
            return;
        }

        var ticked = _lstClasses.CheckedItems.Cast<object>()
            .Select(item => item as string)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToList();

        var fallback = RaceFormat.FromSettings(_settings);
        var shown = 0;

        foreach (var name in ticked)
        {
            if (_raceClasses.All.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) is not { } raceClass)
            {
                continue;
            }

            var row = shown < _formatRows.Count ? _formatRows[shown] : AddFormatRow();
            shown++;

            // The class's own format, or the default it follows - said
            // either way, so "nothing set" still reads as a real answer.
            var describe = raceClass.Format is { } own
                ? own.Describe()
                : "app default: " + fallback.Describe();

            if (raceClass.QualifierFormat is not null)
            {
                describe += "  (own qualifier format)";
            }

            row.ClassName = raceClass.Name;
            row.Name.Text = raceClass.Name;
            row.Format.Text = describe;
            row.Format.ForeColor = raceClass.Format is null ? AppTheme.TextMuted : AppTheme.TextDark;
            row.Line.Visible = true;
        }

        for (var i = shown; i < _formatRows.Count; i++)
        {
            _formatRows[i].Line.Visible = false;
        }

        _lblNoFormats.Visible = ticked.Count == 0;
    }

    /// <summary>A new line for the formats, at the end of the step, themed as the rest of the window was.</summary>
    private FormatRow AddFormatRow()
    {
        var name = new Label
        {
            AutoSize = true,
            Font = AppTheme.BoldFont,
            ForeColor = AppTheme.TextDark,
        };

        var format = new Label
        {
            AutoSize = true,
            Font = AppTheme.SmallFont,
        };

        var button = new Button { Text = "Race Format..." };

        // The name over the format, wrapping to the room left of the button
        // rather than cut short - two lines of text about as tall as the
        // button beside them.
        var line = new Beside
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        line.Add(name, gapBelow: 0);
        line.Add(format, gapBelow: 0);
        line.AddSide(button, gapBelow: 0);

        _racingStep.Add(line, gapBelow: UiLayout.RowGap);

        var row = new FormatRow(line, name, format);

        button.Click += (_, _) => EditClassFormat(row.ClassName);

        AppTheme.ApplyRecursively(line);
        AppTheme.StyleButton(button, AppTheme.ButtonKind.Secondary);

        _formatRows.Add(row);

        return row;
    }

    /// <summary>
    /// The class's format, in the one editor every format is edited in, and
    /// saved on the class - the same thing Classes and Race Control do.
    /// </summary>
    private void EditClassFormat(string className)
    {
        if (_raceClasses.All.FirstOrDefault(c => string.Equals(c.Name, className, StringComparison.OrdinalIgnoreCase)) is not { } raceClass)
        {
            return;
        }

        using var form = new RaceFormatForm(
            raceClass.Format,
            $"Race Format - {raceClass.Name}",
            allowUseDefault: true,
            fallback: RaceFormat.FromSettings(_settings));

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        raceClass.Format = form.Result;
        _raceClasses.Upsert(raceClass);

        FillFormatRows();
    }

    /// <summary>
    /// Who is here: the class filter over the list, which takes the height
    /// the step has, and the buttons - Add Driver among them - what the
    /// count is and the check-in rule under it.
    /// </summary>
    private void BuildCheckInStep()
    {
        // Ticking people in happens HERE rather than only on the Race Day
        // page. "Who is here" is the question this step asks, and sending
        // somebody to another page to answer it - then back - is the kind of
        // errand the wizard exists to remove.
        _cmbWhoClass.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbWhoClass.SelectedIndexChanged += (_, _) => FillWhosHere();

        _chkOnlyMissing.Text = "Only those not in yet";
        _chkOnlyMissing.AutoSize = true;
        _chkOnlyMissing.CheckedChanged += (_, _) => FillWhosHere();

        // Level with each other's middles on their line, not their tops.
        _cmbWhoClass.Anchor = AnchorStyles.Left;
        _chkOnlyMissing.Anchor = AnchorStyles.Left;

        // Someone new: a button under the list they will land in, opening a
        // small window for their details. It was a row of three boxes and
        // an Add button along the bottom of the step, which crowded it.
        _btnAddDriver.Text = "Add Driver...";
        _btnAddDriver.Click += (_, _) => AddNewDriver();

        _lstWhosHere.CheckOnClick = true;

        // Given the height the step has, not snapped to whole rows - see FillPage.AddFill.
        _lstWhosHere.IntegralHeight = false;
        _lstWhosHere.ItemCheck += WhosHereTicked;

        _btnCheckAllShown.Text = "Check In All Shown";
        _btnCheckAllShown.Click += (_, _) => SetAllShown(true);

        _btnClearAll.Text = "Clear All";
        _btnClearAll.Click += (_, _) => SetAllShown(false);

        _btnGoToCheckIn.Text = "Open the full check-in page";
        _btnGoToCheckIn.Click += (_, _) =>
        {
            SaveCurrentStep();
            GoToPageOnClose = "Race Day";
            Finish();
        };

        // Rewritten as people are ticked in and added; two lines kept for it
        // (SizeFields), so the list above does not hop when it grows a line.
        _lblCheckInState.AutoSize = true;

        _chkCheckedInOnly.Text = "Only build races from drivers who have checked in";
        _chkCheckedInOnly.AutoSize = true;

        // The filter along the top, and every button under the list: Add
        // Driver with the ones that tick people in, the full check-in page
        // against the right-hand edge - under them, still on the right, when
        // there is no room beside them (SplitBar). Add Driver was up beside
        // the filter, and next to a filter as wide as a long class's name it
        // went onto a line of its own, taking a row from the list (layout
        // pictures, 29 Sep).
        var filter = new ButtonBar().With(_cmbWhoClass, _chkOnlyMissing);

        var actions = new SplitBar(
            new ButtonBar().With(_btnCheckAllShown, _btnClearAll, _btnAddDriver),
            new ButtonBar(alignRight: true).With(_btnGoToCheckIn));

        // The list takes the room the new-driver row used to - taller, so
        // more of the club is on screen at once.
        _driversStep.Add(filter, gapBelow: UiLayout.RowGap);
        _driversStep.AddFill(_lstWhosHere, _ => WhosHereLeastRows * _lstWhosHere.ItemHeight + 4, gapBelow: UiLayout.RowGap);
        _driversStep.Add(actions, gapBelow: UiLayout.RowGap);
        _driversStep.Add(_lblCheckInState, gapBelow: UiLayout.RowGap);
        _driversStep.Add(_chkCheckedInOnly, stretch: false, gapBelow: 0);
    }

    /// <summary>
    /// Who is on the list, after the class filter and the "not in yet" box.
    ///
    /// Profiles rather than AllRaceable: check-in is about a car that
    /// physically turned up, and a ghost cannot walk through the door -
    /// which is the rule ProfileStore states for itself.
    /// </summary>
    private void FillWhosHere()
    {
        _fillingWhosHere = true;

        _lstWhosHere.BeginUpdate();
        _lstWhosHere.Items.Clear();

        var chosenClass = _cmbWhoClass.SelectedIndex > 0 ? _cmbWhoClass.SelectedItem as string : null;

        foreach (var profile in _profiles.All
                     .Where(p => chosenClass is null
                                 || string.Equals(p.ClassName, chosenClass, StringComparison.OrdinalIgnoreCase))
                     .OrderBy(p => p.DriverName, StringComparer.CurrentCultureIgnoreCase))
        {
            var here = _checkIns.IsCheckedIn(profile.Transponder);

            if (_chkOnlyMissing.Checked && here)
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(profile.DriverName) ? profile.Transponder : profile.DriverName;
            var className = string.IsNullOrWhiteSpace(profile.ClassName) ? "no class" : profile.ClassName;

            _lstWhosHere.Items.Add(new DriverItem(profile.Transponder, $"{name}   ({className})"), here);
        }

        _lstWhosHere.EndUpdate();
        _fillingWhosHere = false;

        RefreshCheckInCount();
    }

    private void WhosHereTicked(object? sender, ItemCheckEventArgs e)
    {
        if (_fillingWhosHere || _lstWhosHere.Items[e.Index] is not DriverItem item)
        {
            return;
        }

        // NewValue, not the item's current state: ItemCheck runs before the
        // tick actually changes, so reading the list here would write back
        // the value being replaced.
        _checkIns.SetCheckedIn(item.Transponder, e.NewValue == CheckState.Checked);

        // Posted rather than run now, for the same reason - the count has to
        // be taken after the tick has landed.
        BeginInvoke(new Action(RefreshCheckInCount));
    }

    private void SetAllShown(bool here)
    {
        // Only what is on screen. "Check In All Shown" under a class filter
        // means that class, which is the whole point of the filter; Clear
        // All is the same promise in reverse and deliberately does not
        // reach past the filter either.
        foreach (var item in _lstWhosHere.Items.Cast<object>().OfType<DriverItem>().ToList())
        {
            _checkIns.SetCheckedIn(item.Transponder, here);
        }

        FillWhosHere();
    }

    private void RefreshCheckInCount()
    {
        if (IsDisposed)
        {
            return;
        }

        var here = _checkIns.SnapshotCheckedIn().Count;
        var onFile = _profiles.All.Count;

        _lblCheckInState.Text = here == 0
            ? $"Nobody is checked in yet, out of {Plural.Of(onFile, "driver")} on file."
            : $"{here} of {Plural.Of(onFile, "driver")} checked in.";
    }

    /// <summary>
    /// The program: what building would make, or - when nothing can be built
    /// - how many steps still need you and a card for each of the three the
    /// program is made from. Which of the two shows is LoadProgrammeStep's.
    /// </summary>
    private void BuildProgrammeStep()
    {
        _lblProgrammeState.AutoSize = true;

        _lblNeedsTitle.AutoSize = true;
        _lblNeedsTitle.Font = AppTheme.HeadingFont;

        _lblNeedsNote.AutoSize = true;
        _lblNeedsNote.Font = AppTheme.BodyFont;

        _cardDay.Fix.Click += (_, _) => GoToStep(StepDay);
        _cardClasses.Fix.Click += (_, _) => GoToStep(StepClasses);
        _cardDrivers.Fix.Click += (_, _) => GoToStep(StepDrivers);

        _programStep.Add(_lblProgrammeState, gapBelow: 0);
        _programStep.Add(_lblNeedsTitle, gapBelow: UiLayout.HintGap);
        _programStep.Add(_lblNeedsNote);
        _programStep.Add(_cardDay, gapBelow: UiLayout.ButtonGap);
        _programStep.Add(_cardClasses, gapBelow: UiLayout.ButtonGap);
        _programStep.Add(_cardDrivers, gapBelow: 0);
    }

    private void GoToStep(int step)
    {
        if (step < 0)
        {
            return;
        }

        SaveCurrentStep();
        ShowStep(step);
    }

    /// <summary>
    /// The fields' sizes, in the faces the theme pass left them in: the boxes
    /// the width they were made at, at the text size in use; the drop-downs
    /// as wide as their longest choice when that is wider (and again when
    /// their lists are refilled - see FitPicker); the classes list four of
    /// its rows tall (and across to the step's edge - BuildRacingStep); the
    /// counts for two digits; two lines kept for the
    /// check-in count. And one caption column for both wordings of the
    /// rounds caption.
    /// </summary>
    private void SizeFields()
    {
        var wide = UiLayout.AtTextSize(FieldWidth);

        FitPicker(_cmbShape, FieldWidth);
        FitPicker(_cmbVenue, FieldWidth);
        FitPicker(_cmbTrack, FieldWidth);
        FitPicker(_cmbWhoClass, ClassFilterWidth);

        _txtDayName.Width = wide;
        _lstClasses.Size = new Size(wide, ClassRows * _lstClasses.ItemHeight + 4);
        _numRounds.Width = UiLayout.AtTextSize(UiLayout.NumericWidth);
        _numHeatSize.Width = UiLayout.AtTextSize(UiLayout.NumericWidth);

        _lblCheckInState.MinimumSize = new Size(0, 2 * Pieces.LineHeight(_lblCheckInState.Font) + _lblCheckInState.Padding.Vertical);

        FieldGrid.LineUp(new[] { _racingGrid }, HeatsCaption, RacesCaption);
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

    // --- What still needs you --------------------------------------------

    /// <summary>
    /// Reads what building would do now, and sets the rail's marks from it:
    /// a tick on each step the program has what it needs from, a red "!" on
    /// each one it is still waiting on.
    /// </summary>
    private void RefreshReadiness()
    {
        _plan = _host.PlanBuild();

        var (dayDone, classesDone, driversDone) = StepsDone();

        // Only a refusal one of these steps can fix puts a "!" anywhere. A
        // heat out on track, say, is nothing the Drivers step would change.
        var setupBlocks = BlockedBySetup();

        Mark(StepDay, dayDone);
        Mark(StepClasses, classesDone);
        Mark(StepDrivers, driversDone);

        foreach (var label in _railLabels)
        {
            label.Invalidate();
        }

        void Mark(int step, bool done)
        {
            // Not flagged until it has been walked past, or the program step
            // reached - opening the wizard shouldn't greet anyone in red.
            var reached = step < _step || _step >= StepProgramme;

            _railMarks[step] = done ? RailMark.Done
                : setupBlocks && reached ? RailMark.Needed
                : RailMark.None;
        }
    }

    /// <summary>True when the program can't be built for a reason one of the set-up steps fixes.</summary>
    private bool BlockedBySetup() =>
        !_plan.Ok && (OpenDay is null || _plan.Title == RaceProgramControl.NothingToBuildTitle);

    /// <summary>
    /// Whether the day, the classes and the drivers are each done, as far
    /// as building the program goes.
    /// </summary>
    private (bool Day, bool Classes, bool Drivers) StepsDone()
    {
        var day = OpenDay;

        if (day is null)
        {
            // Classes are ticked for a day and check-ins are kept per day,
            // so with no day open neither can be done yet.
            return (false, false, false);
        }

        // With nothing ticked the program falls back to every class that has
        // drivers, so Classes counts as done only when some are actually
        // ticked. Drivers are done unless a lack of them is what stopped
        // the build.
        var classes = day.ClassNames.Count > 0;
        var drivers = _plan.Ok || _plan.Title != RaceProgramControl.NothingToBuildTitle;

        return (true, classes, drivers);
    }

    /// <summary>
    /// Fills the program step when nothing can be built: how many steps
    /// still need you, then a card for each of the three the program is
    /// made from, DONE or NEEDED, with the way to every one that isn't done.
    /// </summary>
    private void ShowNeedsYou()
    {
        // What the cards say changes from one visit to the next, and which
        // of their buttons is the loud one with it.
        _fixButtons.Clear();

        _lblNeedsTitle.ForeColor = AppTheme.Warning;

        if (!BlockedBySetup())
        {
            // Nothing a step here would fix - a heat on track, usually. Said
            // plainly, with no cards to send anyone the wrong way.
            _lblNeedsTitle.Text = "The program can't be built right now";
            _lblNeedsNote.Text = _plan.Message;
            _lblNeedsNote.ForeColor = AppTheme.TextDark;

            foreach (var card in Cards)
            {
                card.Visible = false;
            }

            return;
        }

        var day = OpenDay;
        var (dayDone, classesDone, driversDone) = StepsDone();

        var cards = new List<(ReadinessCard Card, bool Done, string Detail)>
        {
            (_cardDay, dayDone,
                dayDone
                    ? $"{day!.DisplayName} is open."
                    : _plan.Title == RaceProgramControl.ClosedDayTitle
                        ? "Tonight's day has been closed. Open a new one to build a program for it."
                        : "No race day is open yet - the program is built for one."),

            (_cardClasses, classesDone,
                classesDone
                    ? "Racing tonight: " + ClassList(day!.ClassNames)
                    : dayDone
                        ? "No classes are ticked for tonight."
                        : "Tick tonight's classes once the day is open."),

            (_cardDrivers, driversDone,
                driversDone
                    ? "Drivers are ready to race."
                    : !dayDone
                        ? "Check in who's here once the day is open."
                        : classesDone
                            ? _plan.Message
                            : day!.CheckedInDriversOnly
                                ? "Nobody is checked in yet - races are built from checked-in drivers only."
                                : "None of your classes has any drivers in it yet."),
        };

        var needed = cards.Count(c => !c.Done);

        _lblNeedsTitle.Text = $"{Plural.Of(needed, "step")} still {(needed == 1 ? "needs" : "need")} you "
                            + "before the program can be built";

        _lblNeedsNote.Text = needed == 1
            ? "It's marked with a red ! on the left. Or Skip for Now, and build the program later."
            : "They're marked with a red ! on the left. Or Skip for Now, and build the program later.";
        _lblNeedsNote.ForeColor = AppTheme.TextMuted;

        var first = true;

        foreach (var (card, done, detail) in cards)
        {
            card.SetState(detail, done);
            card.Visible = true;

            if (!done)
            {
                // The first step to fix is the loud button; the rest wait
                // their turn.
                _fixButtons.Add((card.Fix, first ? AppTheme.ButtonKind.Primary : AppTheme.ButtonKind.Secondary));
                first = false;
            }
        }
    }

    /// <summary>Tonight's classes for a card - the first few by name, then how many more.</summary>
    private static string ClassList(IReadOnlyCollection<string> names)
    {
        const int shown = 4;

        var list = string.Join(", ", names.Take(shown));

        return names.Count > shown ? $"{list} and {names.Count - shown} more." : list + ".";
    }

    /// <summary>
    /// A tick in the success colour, or a red disc with a "!" - drawn at the
    /// right of a rail step, at the text size in use: the shape was worked
    /// out on an 18px square, and is drawn at that in proportion.
    /// </summary>
    private static void PaintRailMark(Graphics g, Label label, RailMark mark)
    {
        if (mark == RailMark.None)
        {
            return;
        }

        var size = UiLayout.AtTextSize(MarkSize);
        var scale = size / (float)MarkSize;
        var box = new Rectangle(label.Width - size - UiLayout.AtTextSize(MarkInset), (label.Height - size) / 2, size, size);

        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (mark == RailMark.Done)
        {
            using var pen = new Pen(AppTheme.Success, 2.2f * scale)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round,
            };

            g.DrawLines(pen, new[]
            {
                new PointF(box.Left + 3f * scale, box.Top + 9.5f * scale),
                new PointF(box.Left + 7.5f * scale, box.Top + 14f * scale),
                new PointF(box.Right - 3f * scale, box.Top + 4.5f * scale),
            });

            return;
        }

        using (var fill = new SolidBrush(AppTheme.Danger))
        {
            g.FillEllipse(fill, box);
        }

        TextRenderer.DrawText(g, "!", AppTheme.BoldFont, box, AppTheme.OnDanger,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
            | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
    }

    /// <summary>
    /// One step on the "still needs you" list: a DONE or NEEDED pill, the
    /// step's name, a line on where it stands, and - when it isn't done -
    /// the button that goes to it.
    ///
    /// Painted rather than put together from labels, so the pill, the text
    /// and the border line up exactly, and the theme pass has nothing on it
    /// to recolour.
    ///
    /// It sizes itself (AutoSize), as the layout pieces do: as tall as what
    /// it says needs at the width it is given, measured in the faces in use,
    /// the pill as big as its word needs at the text size in use - so the
    /// program step it is part of places it. One card is kept for each step
    /// and told what to say each time the step is shown (SetState). Its
    /// button is the one thing on it that is not painted, and it places
    /// that itself: against the right-hand edge, level with the middle.
    /// </summary>
    private sealed class ReadinessCard : Panel
    {
        /// <summary>Room inside the card's edge, as a Section keeps.</summary>
        private const int Pad = 16;

        /// <summary>The pill, the gap after it and the card's least height, at the normal text size.</summary>
        private const int PillWidth = 78;

        private const int PillHeight = 22;
        private const int PillGap = 14;
        private const int MinimumHeight = 66;

        private const TextFormatFlags TitleFlags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;

        private const TextFormatFlags DetailFlags =
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding;

        private readonly string _title;
        private string _detail = string.Empty;
        private bool _done;

        public ReadinessCard(string title, string fixCaption)
        {
            _title = title;
            Fix = new Button { Text = fixCaption };

            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            DoubleBuffered = true;
            ResizeRedraw = true;

            // The card's own colour is what the button sits on; the corners
            // outside the rounded box are painted in the page's colour below.
            // Kept through the theme pass, which would otherwise set every
            // panel to the page background.
            BackColor = AppTheme.Surface;
            Tag = AppTheme.KeepBackColor;

            Controls.Add(Fix);
        }

        /// <summary>The button that goes to the step - on the card while the step is not done.</summary>
        public Button Fix { get; }

        /// <summary>
        /// What the card says, and whether the step is done. The program
        /// step is told to place it again: how tall it is follows what it
        /// says, and nothing else tells the step.
        /// </summary>
        public void SetState(string detail, bool done)
        {
            _detail = detail;
            _done = done;
            Fix.Visible = !done;

            Invalidate();
            Parent?.PerformLayout(this, nameof(PreferredSize));
        }

        /// <summary>The pill's size at the text size in use: as it was at the normal size, or its longer word with room either side if that is more.</summary>
        private static Size Pill()
        {
            var word = Math.Max(Measure("DONE"), Measure("NEEDED"));

            return new Size(
                Math.Max(UiLayout.AtTextSize(PillWidth), word + UiLayout.AtTextSize(Pad)),
                Math.Max(UiLayout.AtTextSize(PillHeight), Pieces.LineHeight(AppTheme.MicroFont) + 6));

            static int Measure(string text) =>
                TextRenderer.MeasureText(text, AppTheme.MicroFont, Size.Empty, TitleFlags).Width;
        }

        /// <summary>Where the step's name and what it says start - after the pill.</summary>
        private static int TextLeft => Pad + Pill().Width + UiLayout.AtTextSize(PillGap);

        private int TitleHeight => TextRenderer.MeasureText(_title, AppTheme.BoldFont, Size.Empty, TitleFlags).Height;

        /// <summary>The line the pill and the step's name share - as tall as the taller.</summary>
        private int TitleLine => Math.Max(TitleHeight, Pill().Height);

        /// <summary>The button's size: its caption's, in the face buttons are drawn in (UiLayout.ButtonSizeFor).</summary>
        private Size FixSize => UiLayout.ButtonSizeFor(Fix.Text);

        /// <summary>Where the detail text has to stop in a card <paramref name="width"/> wide - short of the button, while there is one.</summary>
        private int TextWidth(int width) => Math.Max(60, width - Pad - TextLeft - (_done ? 0 : FixSize.Width + Pad));

        /// <summary>How tall the card is at <paramref name="width"/>: its text, or its button, with the room round them.</summary>
        private int HeightAt(int width)
        {
            var detail = TextRenderer.MeasureText(_detail, AppTheme.SmallFont,
                new Size(TextWidth(width), int.MaxValue), DetailFlags).Height;

            var text = Pad + TitleLine + 4 + detail + Pad;
            var button = _done ? 0 : FixSize.Height + 2 * Pad;

            return Math.Max(UiLayout.AtTextSize(MinimumHeight), Math.Max(text, button));
        }

        /// <summary>As wide as it is given, and as tall as it needs to be at that width.</summary>
        public override Size GetPreferredSize(Size proposedSize)
        {
            var width = proposedSize.Width is > 0 and < int.MaxValue / 4 ? proposedSize.Width : Width;

            return new Size(width, HeightAt(width));
        }

        /// <summary>The button against the right-hand edge, level with the middle.</summary>
        protected override void OnLayout(LayoutEventArgs levent)
        {
            base.OnLayout(levent);

            var size = FixSize;
            var bounds = new Rectangle(
                Math.Max(0, Width - Pad - size.Width),
                Math.Max(0, (Height - size.Height) / 2),
                size.Width,
                size.Height);

            if (Fix.Bounds != bounds)
            {
                Fix.Bounds = bounds;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // The page's colour in the corners, then the rounded card.
            using (var page = new SolidBrush(Parent?.BackColor ?? AppTheme.Background))
            {
                g.FillRectangle(page, ClientRectangle);
            }

            var box = new Rectangle(0, 0, Width - 1, Height - 1);

            using (var path = Rounded(box, 8))
            using (var fill = new SolidBrush(AppTheme.Surface))
            {
                g.FillPath(fill, path);
            }

            // A hairline round a step that's done; a 2px border in the danger
            // colour round one that isn't.
            if (_done)
            {
                using var path = Rounded(box, 8);
                using var pen = new Pen(AppTheme.Border, 1f);
                g.DrawPath(pen, path);
            }
            else
            {
                using var path = Rounded(new Rectangle(1, 1, Width - 3, Height - 3), 7);
                using var pen = new Pen(AppTheme.Danger, 2f);
                g.DrawPath(pen, path);
            }

            // The pill and the step's name, level on one line.
            var line = TitleLine;
            var pillSize = Pill();
            var pill = new Rectangle(Pad, Pad + (line - pillSize.Height) / 2, pillSize.Width, pillSize.Height);

            using (var path = Rounded(pill, pillSize.Height / 2))
            using (var fill = new SolidBrush(_done ? Looks.Mix(AppTheme.Surface, AppTheme.Success, 0.2) : AppTheme.Danger))
            {
                g.FillPath(fill, path);
            }

            TextRenderer.DrawText(g, _done ? "DONE" : "NEEDED", AppTheme.MicroFont, pill,
                _done ? AppTheme.Success : AppTheme.OnDanger,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            // The step's name, and where it stands. A step that's done reads
            // quieter, so the one that isn't is what the eye lands on.
            TextRenderer.DrawText(g, _title, AppTheme.BoldFont, new Point(TextLeft, Pad + (line - TitleHeight) / 2),
                _done ? AppTheme.TextMuted : AppTheme.TextDark, TitleFlags);

            var detailBox = new Rectangle(TextLeft, Pad + line + 4, TextWidth(Width), Height);

            TextRenderer.DrawText(g, _detail, AppTheme.SmallFont, detailBox, AppTheme.TextMuted, DetailFlags);
        }

        private static GraphicsPath Rounded(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            var d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));

            path.AddArc(r.Left, r.Top, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();

            return path;
        }
    }

    private void BuildSummaryStep()
    {
        _lblSummary.AutoSize = true;

        _summaryStep.Add(_lblSummary, gapBelow: 0);
    }

    // --- Moving between steps -------------------------------------------

    private void ShowStep(int step)
    {
        _step = Math.Clamp(step, 0, StepNames.Length - 1);

        // Before anything is shown: the program step and the rail both read it.
        RefreshReadiness();

        // The step's own page - none on the welcome step.
        Control? content = null;

        switch (_step)
        {
            case 0:
                _lblStepTitle.Text = "Let's get tonight ready";
                _lblStepBody.Text = "Four short steps: the day itself, tonight's classes and how they race, who's here, "
                                  + "and the program. Skip any of them - nothing here is a gate, and you can run this again "
                                  + "from the Setup tab whenever you like.";
                break;

            case StepDay:
                _lblStepTitle.Text = "Tonight's day";
                _lblStepBody.Text = "What kind of night it is, and where. Moving on from here opens the day.";
                LoadDayStep();
                content = _dayStep;
                break;

            case StepClasses:
                _lblStepTitle.Text = "Tonight's classes";
                _lblStepBody.Text = "Tick the classes out tonight - Add Class for a new one. Then how many times each driver "
                                  + "races, how many cars go out at once, and the race each class runs.";
                LoadRacingStep();
                content = _racingStep;
                break;

            case StepDrivers:
                _lblStepTitle.Text = "Who's here tonight?";
                _lblStepBody.Text = "Tick everyone who has turned up - filter by class to work through them a class at a "
                                  + "time. Someone new? Add Driver puts them on file and checks them in.";
                LoadCheckInStep();
                content = _driversStep;
                break;

            case StepProgramme:
                _lblStepTitle.Text = "The program";
                _lblStepBody.Text = _plan.Ok
                    ? "The races themselves, built from the classes and the drivers above. "
                      + "Build It makes them; Back changes anything first."
                    : "The races themselves, built from tonight's classes and the drivers in them.";
                LoadProgrammeStep();
                content = _programStep;
                break;

            default:
                _lblStepTitle.Text = "That's tonight ready";
                _lblStepBody.Text = "Anything left is on the Next Steps tab, which keeps track of what is next.";
                LoadSummaryStep();
                content = _summaryStep;
                break;
        }

        // This step's page on show, and every other one hidden - which takes
        // it out of the layout as well as out of sight.
        foreach (var stepPage in StepPages)
        {
            stepPage.Visible = ReferenceEquals(stepPage, content);
        }

        var last = _step == StepNames.Length - 1;

        _btnBack.Visible = _step > 0;
        _btnSkipAll.Visible = !last;
        _btnNext.Text = last ? "Done"
            : _step == 0 ? "Start"
            : _step == StepProgramme && _programmeBuildable ? "Build It"
            : _step == StepProgramme ? "Skip for Now"
            : "Next";

        _lblProgress.Text = _step is 0 || last ? string.Empty : $"Step {_step} of {StepNames.Length - 2}";

        // Nothing to say on the welcome page or the last one - and on the
        // last, nothing on the left at all, so no empty row is left there.
        _lblProgress.Visible = _lblProgress.Text.Length > 0;
        _footerLeft.Visible = !last;

        ApplyFrameColours();

        // Enter moves on.
        AcceptButton = _btnNext;
        _btnNext.Focus();
    }

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

        foreach (var hint in new[]
                 {
                     _lblShapeHint, _lblDayState, _lblFormatsCaption, _lblNoFormats,
                 })
        {
            hint.ForeColor = AppTheme.TextMuted;
        }

        AppTheme.StyleButton(_btnNext, AppTheme.ButtonKind.Primary);
        AppTheme.StyleButton(_btnBack, AppTheme.ButtonKind.Secondary);
        AppTheme.StyleButton(_btnSkipAll, AppTheme.ButtonKind.Secondary);
        AppTheme.StyleButton(_btnGoToCheckIn, AppTheme.ButtonKind.Secondary);

        // Unticks the list shown, nothing more - the theme reads "Clear" as
        // destructive and would paint it red.
        AppTheme.StyleButton(_btnClearAll, AppTheme.ButtonKind.Secondary);
        AppTheme.StyleButton(_btnAddDriver, AppTheme.ButtonKind.Secondary);
        AppTheme.StyleButton(_btnAddClass, AppTheme.ButtonKind.Secondary);

        // The way to each step that still needs you - the first one loud,
        // any after it quieter - and Next (reading "Skip for Now" there)
        // goes quiet beside them.
        foreach (var (button, kind) in _fixButtons)
        {
            if (!button.IsDisposed)
            {
                AppTheme.StyleButton(button, kind);
            }
        }

        if (_step == StepProgramme && !_programmeBuildable)
        {
            AppTheme.StyleButton(_btnNext, AppTheme.ButtonKind.Secondary);
        }
    }

    private void NextClicked()
    {
        SaveCurrentStep();

        // "Build It" on the programme step builds, then moves on. A build
        // that is refused stays here with the reason, which is usually
        // something to fix a step or two back.
        if (_step == StepProgramme && _programmeBuildable)
        {
            var answer = _host.Build();

            if (!answer.Ok)
            {
                _lblProgrammeState.Text = answer.Message;
                _lblProgrammeState.ForeColor = AppTheme.Accent;
                return;
            }
        }

        if (_step < StepNames.Length - 1)
        {
            ShowStep(_step + 1);
            return;
        }

        Finish();
    }

    /// <summary>
    /// Closes, and remembers that this club has seen the wizard.
    ///
    /// Set whether it was finished or skipped, on purpose: the flag means
    /// "do not offer this unprompted again", not "this was completed". A
    /// club that skipped it once has answered the question.
    /// </summary>
    private void Finish()
    {
        _settings.HasSeenRaceDaySetup = true;
        _settings.Save();

        DialogResult = DialogResult.OK;
        Close();
    }

    // --- Loading and saving each step ------------------------------------

    private void SaveCurrentStep()
    {
        switch (_step)
        {
            case StepDay: SaveDay(); break;
            case StepClasses: SaveRacing(); break;

            case StepDrivers: SaveCheckIn(); break;
        }
    }

    /// <summary>
    /// Adds a class the club has not got on file, ticked for tonight.
    /// </summary>
    private void AddClass()
    {
        var onFile = _raceClasses.All.Select(c => c.Name).ToList();

        using var form = new QuickClassForm(name =>
            onFile.Any(existing => string.Equals(existing, name, StringComparison.OrdinalIgnoreCase)));

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _raceClasses.Upsert(new RaceClass { Name = form.ClassName });

        // Ticked: somebody adding a class tonight is adding it to race.
        _lstClasses.Items.Add(form.ClassName, true);
        FillFormatRows();
    }

    /// <summary>
    /// Adds someone new, from a small window, and checks them in.
    ///
    /// The window does the checking - a name, a transponder, and one that
    /// is not already somebody's - so what comes back is ready to save.
    /// </summary>
    private void AddNewDriver()
    {
        var classes = _raceClasses.All.Select(c => c.Name).ToList();

        // The class filter's choice when there is one - somebody working
        // through Stock who meets a new face is adding a Stock driver.
        var startClass = _cmbWhoClass.SelectedIndex > 0 ? _cmbWhoClass.SelectedItem as string : null;

        using var form = new QuickDriverForm(classes, startClass, transponder =>
            _profiles.Get(transponder) is { } existing
                ? (string.IsNullOrWhiteSpace(existing.DriverName) ? "someone else" : existing.DriverName)
                : null);

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _profiles.Upsert(new DriverProfile
        {
            Transponder = form.Transponder,
            DriverName = form.DriverName,
            ClassName = form.ClassName,
        });

        // Checked in as well - they are standing at the desk. Only with a
        // day open: check-ins are per day, and there is none to record it on.
        if (OpenDay is null)
        {
            _lblCheckInState.Text = $"{form.DriverName} added. Open a day to check them in.";
            return;
        }

        _checkIns.SetCheckedIn(form.Transponder, true);

        // A class filter that would hide them is taken off, so the person
        // just added is in the list to be seen.
        if (_cmbWhoClass.SelectedIndex > 0
            && !string.Equals(_cmbWhoClass.SelectedItem as string, form.ClassName, StringComparison.OrdinalIgnoreCase))
        {
            _cmbWhoClass.SelectedIndex = 0;
        }

        FillWhosHere();

        for (var i = 0; i < _lstWhosHere.Items.Count; i++)
        {
            if (_lstWhosHere.Items[i] is DriverItem item
                && string.Equals(item.Transponder, form.Transponder, StringComparison.OrdinalIgnoreCase))
            {
                _lstWhosHere.SelectedIndex = i;
                break;
            }
        }

        _lblCheckInState.Text = $"{form.DriverName} added and checked in. " + _lblCheckInState.Text;
    }

    private void LoadDayStep()
    {
        var day = OpenDay;

        // The lists FIRST, and the text after.
        //
        // Both boxes are editable combos, and Items.Clear() on one of those
        // wipes whatever is typed in it. Filling them after setting the text
        // left the track box looking empty on a day that had a track - and
        // the venue box had nothing in it at all, because this wizard was
        // never given the venue store to fill it from.
        _cmbVenue.Items.Clear();

        foreach (var venue in _venues.All)
        {
            _cmbVenue.Items.Add(venue.Name);
        }

        _cmbTrack.Items.Clear();

        foreach (var layout in _trackLayouts.All)
        {
            _cmbTrack.Items.Add(layout.Name);
        }

        // As wide as their longest choice now - measured before the text goes
        // in, for the same reason as above: resizing an editable combo can
        // select what is typed in it.
        FitPicker(_cmbVenue, FieldWidth);
        FitPicker(_cmbTrack, FieldWidth);

        if (day is not null)
        {
            _cmbShape.SelectedIndex = day.IsClubNight ? 1 : 0;
            _txtDayName.Text = day.Name;
            _cmbVenue.Text = day.VenueName;
            _cmbTrack.Text = day.TrackLayoutName;
        }
        else if (_txtDayName.Text.Length == 0)
        {
            _txtDayName.Text = EventSpine.DefaultDayName(
                DateTime.Now,
                _cmbShape.SelectedIndex == 1 ? RaceDayShape.ClubNight : RaceDayShape.RaceDay);
        }

        _lblDayState.Text = day is null
            ? "No race day is open. One is started when you move on from here."
            : $"\"{day.DisplayName}\" is open. Changes here are made to it.";

        ApplyShapeHint();
    }

    private void ApplyShapeHint() =>
        _lblShapeHint.Text = _cmbShape.SelectedIndex == 1 ? ClubNightShapeHint : RaceDayShapeHint;

    private void SaveDay()
    {
        var shape = _cmbShape.SelectedIndex == 1 ? RaceDayShape.ClubNight : RaceDayShape.RaceDay;

        if (OpenDay is null)
        {
            // Every class on file, the way NewRaceDayForm ticks them all on
            // a first run: a day with no classes is the trap that dialog
            // exists to close, and the next step is where they are chosen
            // properly anyway.
            _host.StartDay(shape, _raceClasses.All.Select(c => c.Name).ToList());
        }

        // Through The Day, never straight at the store - see
        // RaceDayEventControl.ChangeDayFromElsewhere.
        _host.ChangeDay(day =>
        {
            day.Shape = shape;

            if (_txtDayName.Text.Trim().Length > 0)
            {
                day.Name = _txtDayName.Text.Trim();
            }

            day.VenueName = _cmbVenue.Text.Trim();
            day.TrackLayoutName = _cmbTrack.Text.Trim();
        });
    }

    private void LoadRacingStep()
    {
        var day = OpenDay;

        _lstClasses.Items.Clear();

        foreach (var raceClass in _raceClasses.All)
        {
            var racing = day is null || day.ClassNames.Count == 0
                || day.ClassNames.Contains(raceClass.Name, StringComparer.OrdinalIgnoreCase);

            _lstClasses.Items.Add(raceClass.Name, racing);
        }

        // A club night races rather than qualifies, and the caption is the
        // only thing that says so on this step.
        if (_lblRoundsCaption is not null)
        {
            _lblRoundsCaption.Text = day is { IsClubNight: true } ? RacesCaption : HeatsCaption;
        }

        if (day is not null)
        {
            _numRounds.Value = Math.Clamp(day.QualifyingRounds, _numRounds.Minimum, _numRounds.Maximum);

            // A day with no heat size of its own builds with the last race
            // day's (RaceEventStore.HeatSizeFor, as the program does), so that
            // is what the box starts at.
            _numHeatSize.Value = Math.Clamp(_events.HeatSizeFor(day), (int)_numHeatSize.Minimum, (int)_numHeatSize.Maximum);

            _chkFirstRace.Checked = day.FirstRaceLocalTime is not null;
            _dtpFirstRace.Value = day.FirstRaceLocalTime ?? TimeSpan.FromHours(10);
        }

        _dtpFirstRace.Enabled = _chkFirstRace.Checked;
        _chkFirstRace.CheckedChanged -= FirstRaceToggled;
        _chkFirstRace.CheckedChanged += FirstRaceToggled;
        FillFormatRows();
    }

    private void FirstRaceToggled(object? sender, EventArgs e) => _dtpFirstRace.Enabled = _chkFirstRace.Checked;

    private void SaveRacing()
    {
        if (OpenDay is null)
        {
            return;
        }

        _host.SetClassesRacing(_lstClasses.CheckedItems.Cast<string>().ToList());

        var rounds = (int)_numRounds.Value;
        var heatSize = (int)_numHeatSize.Value;
        var firstRace = _chkFirstRace.Checked ? _dtpFirstRace.Value : (TimeSpan?)null;

        _host.ChangeDay(day =>
        {
            day.QualifyingRounds = rounds;
            day.DriversPerHeat = heatSize;
            day.FirstRaceLocalTime = firstRace;
        });
    }

    private void LoadCheckInStep()
    {
        var day = OpenDay;
        var haveDay = day is not null;

        _lblCheckInState.ForeColor = AppTheme.TextMuted;

        foreach (var control in new Control[]
                 { _cmbWhoClass, _chkOnlyMissing, _lstWhosHere, _btnCheckAllShown, _btnClearAll, _chkCheckedInOnly, _btnGoToCheckIn })
        {
            control.Enabled = haveDay;
        }

        if (day is null)
        {
            // The check-in sheet is per race day - CheckInStore.CurrentEventId
            // follows the open one - so there is nowhere to record this yet.
            _lblCheckInState.Text = "No race day is open, so nobody can be checked in yet. Go back to The day to open "
                                  + "one - new drivers can still be added with Add Driver.";

            _lstWhosHere.Items.Clear();
            return;
        }

        _chkCheckedInOnly.Checked = day.CheckedInDriversOnly;

        var previous = _cmbWhoClass.SelectedIndex > 0 ? _cmbWhoClass.SelectedItem as string : null;

        _cmbWhoClass.Items.Clear();
        _cmbWhoClass.Items.Add("All classes");

        foreach (var raceClass in _raceClasses.All)
        {
            _cmbWhoClass.Items.Add(raceClass.Name);
        }

        // As wide as the longest class's name, now the list holds them.
        FitPicker(_cmbWhoClass, ClassFilterWidth);

        _cmbWhoClass.SelectedItem = previous is not null && _cmbWhoClass.Items.Contains(previous)
            ? previous
            : _cmbWhoClass.Items[0];

        FillWhosHere();
    }

    private void SaveCheckIn()
    {
        if (OpenDay is null)
        {
            return;
        }

        var only = _chkCheckedInOnly.Checked;

        _host.ChangeDay(day => day.CheckedInDriversOnly = only);
    }

    private void LoadProgrammeStep()
    {
        // Read by ShowStep a moment ago, after the last step saved.
        var plan = _plan;

        _programmeBuildable = plan.Ok;

        // Something to build: the plan, and Build It on the button. Nothing
        // to build: the steps that still need you instead, each with the way
        // to it - the cards only when one of the steps is what stands in the
        // way (ShowNeedsYou).
        _lblProgrammeState.Visible = plan.Ok;
        _lblNeedsTitle.Visible = !plan.Ok;
        _lblNeedsNote.Visible = !plan.Ok;

        foreach (var card in Cards)
        {
            card.Visible = false;
        }

        _lblProgrammeState.Text = plan.Message;
        _lblProgrammeState.ForeColor = AppTheme.TextDark;

        if (!plan.Ok)
        {
            ShowNeedsYou();
        }
    }

    private void LoadSummaryStep()
    {
        var day = OpenDay;
        var lines = new List<string>();

        lines.Add(day is null
            ? "No race day is open."
            : $"{day.DisplayName} is open" + (day.IsClubNight ? ", as a club night." : "."));

        lines.Add($"{Plural.Of(_raceClasses.All.Count, "class", "classes")} on file, "
                + $"{Plural.Of(_profiles.All.Count, "driver")} on file.");

        if (day is not null)
        {
            lines.Add(day.ClassNames.Count == 0
                ? "No classes are marked as racing tonight."
                : "Racing tonight: " + string.Join(", ", day.ClassNames));
        }

        lines.Add(string.Empty);
        lines.Add("Next Steps keeps track of whatever is left - it always shows the next thing to do.");

        _lblSummary.Text = string.Join("\r\n", lines);
    }
}

/// <summary>
/// The small window behind the wizard's Add Class button: one name,
/// checked before it closes - not empty, and not a class already on file.
///
/// On the layout pieces (DialogFit): what it is for, the name under its
/// caption, two lines kept for what is wrong with it, and Add Class and
/// Cancel at the foot; as tall as that. It used to be placed in pixels in a
/// column 360px wide at every text size, the problem's two lines of the
/// small type kept by hand.
/// </summary>
internal sealed class QuickClassForm : Form
{
    /// <summary>How wide it is inside its frame at the normal text size: the 360px column it was made with, and the page's margins.</summary>
    private const int DialogWidth = 360 + UiLayout.PageLeft + UiLayout.PageRight;

    private readonly TextBox _txtName = new();
    private readonly Label _lblProblem = new();
    private readonly Func<string, bool> _exists;

    public string ClassName => _txtName.Text.Trim();

    public QuickClassForm(Func<string, bool> exists)
    {
        _exists = exists;

        Text = "Add a class";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        var lblIntro = new Label
        {
            Text = "Saved with the club's classes, and ticked for tonight. Its race format can be set right after.",
            AutoSize = true,
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
        };

        var lblName = new Label
        {
            Text = "Class name",
            AutoSize = true,
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
        };

        var nameField = RoundedField.Wrap(_txtName);

        // Room for two lines, always - so the window does not jump when it
        // has something to say.
        _lblProblem.AutoSize = true;
        _lblProblem.Font = AppTheme.SmallFont;
        _lblProblem.ForeColor = AppTheme.Accent;
        _lblProblem.MinimumSize = new Size(0, 2 * Pieces.LineHeight(AppTheme.SmallFont));

        var ok = new Button { Text = "Add Class" };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        ok.Click += (_, _) => TryAccept();

        var page = new FillPage();
        page.Add(lblIntro, gapBelow: UiLayout.SectionGap - UiLayout.HintGap);
        page.Add(lblName, stretch: false, gapBelow: UiLayout.HintGap);
        page.Add(nameField, gapBelow: UiLayout.RowGap);
        page.Add(_lblProblem, gapBelow: UiLayout.RowGap);

        // Against the right-hand edge, Cancel furthest right, as they were -
        // the bar is handed Cancel first, as its first button goes furthest
        // right - and Tab still reaches Add Class first, from the tab order
        // set here.
        page.Add(new ButtonBar(alignRight: true).With(cancel, ok), gapBelow: 0);
        ok.TabIndex = 0;
        cancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = ok;
        CancelButton = cancel;

        AppTheme.ApplyRecursively(this);
        AppTheme.StyleButton(ok, AppTheme.ButtonKind.Primary);
        AppTheme.StyleButton(cancel, AppTheme.ButtonKind.Secondary);

        // As tall as a line of its text and room round it, at the text size
        // in use; the page gives it its width.
        nameField.Height = AppTheme.BodyFont.Height + 16;

        DialogFit.Keep(this, page, width: DialogWidth);
    }

    private void TryAccept()
    {
        var problem = ClassName.Length == 0
            ? "Enter the class's name."
            : _exists(ClassName)
                ? $"{ClassName} is already on file - tick it in the list instead."
                : null;

        if (problem is not null)
        {
            _lblProblem.Text = problem;
            _txtName.Focus();
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}

/// <summary>
/// The small window behind the wizard's Add Driver button: a name, a
/// transponder and a class, checked before it closes.
///
/// It hands the answers back rather than saving them itself - the wizard
/// owns the save and the check-in, so this has nothing to get out of step.
///
/// On the layout pieces (DialogFit): what it is for, each field under its
/// caption, two lines kept for what is wrong, and Add Driver and Cancel at
/// the foot; as tall as that, and wider than it was made when a class's
/// name needs the drop-down wider. It used to be placed in pixels in a
/// column 360px wide at every text size, the class drop-down 240px wide
/// however long a class's name.
/// </summary>
internal sealed class QuickDriverForm : Form
{
    private const string NoClass = "(no class)";

    /// <summary>How wide it is inside its frame at the normal text size: the 360px column it was made with, and the page's margins.</summary>
    private const int DialogWidth = 360 + UiLayout.PageLeft + UiLayout.PageRight;

    /// <summary>The class drop-down's width at the normal text size - wider when a class's name needs it - and the transponder's with it.</summary>
    private const int ClassPickerWidth = 240;

    private readonly TextBox _txtName = new();
    private readonly TextBox _txtTransponder = new();
    private readonly ComboBox _cmbClass = new();
    private readonly Label _lblProblem = new();

    /// <summary>Who already has a transponder, or null when nobody does.</summary>
    private readonly Func<string, string?> _ownerOf;

    public string DriverName => _txtName.Text.Trim();
    public string Transponder => _txtTransponder.Text.Trim();
    public string ClassName => _cmbClass.SelectedItem is string chosen && chosen != NoClass ? chosen : string.Empty;

    public QuickDriverForm(IReadOnlyList<string> classes, string? startClass, Func<string, string?> ownerOf)
    {
        _ownerOf = ownerOf;

        Text = "Add a driver";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        var lblIntro = new Label
        {
            Text = "Saved with the drivers on file, and checked in for tonight.",
            AutoSize = true,
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
        };

        Label Caption(string text) => new()
        {
            Text = text,
            AutoSize = true,
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
        };

        var lblName = Caption("Name");
        var lblTransponder = Caption("Transponder");
        var lblClass = Caption("Class");

        // Rounded, like the Class drop-down under them - not the square
        // native boxes, which looked like they belonged to another app.
        var nameField = RoundedField.Wrap(_txtName);
        var transponderField = RoundedField.Wrap(_txtTransponder);

        _cmbClass.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbClass.Items.Add(NoClass);

        foreach (var name in classes)
        {
            _cmbClass.Items.Add(name);
        }

        _cmbClass.SelectedItem = startClass is not null && _cmbClass.Items.Contains(startClass) ? startClass : NoClass;

        // Room for two lines, always - so the window does not jump when it
        // has something to say.
        _lblProblem.AutoSize = true;
        _lblProblem.Font = AppTheme.SmallFont;
        _lblProblem.ForeColor = AppTheme.Accent;
        _lblProblem.MinimumSize = new Size(0, 2 * Pieces.LineHeight(AppTheme.SmallFont));

        var ok = new Button { Text = "Add Driver" };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        ok.Click += (_, _) => TryAccept();

        // Each field under its caption: the name across the page, the
        // transponder and the class at the class drop-down's width.
        var page = new FillPage();
        page.Add(lblIntro, gapBelow: UiLayout.SectionGap - UiLayout.HintGap);

        page.Add(lblName, stretch: false, gapBelow: UiLayout.HintGap);
        page.Add(nameField, gapBelow: UiLayout.RowGap + UiLayout.HintGap);

        page.Add(lblTransponder, stretch: false, gapBelow: UiLayout.HintGap);
        page.Add(transponderField, stretch: false, gapBelow: UiLayout.RowGap + UiLayout.HintGap);

        page.Add(lblClass, stretch: false, gapBelow: UiLayout.HintGap);
        page.Add(_cmbClass, stretch: false, gapBelow: UiLayout.RowGap + UiLayout.HintGap);
        Pieces.KeepHeightTold(_cmbClass);

        page.Add(_lblProblem, gapBelow: UiLayout.RowGap);

        // Against the right-hand edge, Cancel furthest right, as they were -
        // the bar is handed Cancel first, as its first button goes furthest
        // right - and Tab still reaches Add Driver first, from the tab order
        // set here.
        page.Add(new ButtonBar(alignRight: true).With(cancel, ok), gapBelow: 0);
        ok.TabIndex = 0;
        cancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = ok;
        CancelButton = cancel;

        // Themed before the fields are sized: the drop-down's height is its
        // themed one, which the text fields match.
        AppTheme.ApplyRecursively(this);
        AppTheme.StyleButton(ok, AppTheme.ButtonKind.Primary);
        AppTheme.StyleButton(cancel, AppTheme.ButtonKind.Secondary);

        // Kept here, and never read back from the drop-down: it is in the
        // page already, whose table cuts a part down to the column it has
        // now - the width of a window not sized yet. Read back, a long
        // class's name left the window at its usual width and the drop-down
        // too narrow for the name (the window test, "Add a driver"), and the
        // transponder box narrower than the drop-down at a larger text size.
        (Font Font, int Count, int Width)? classWidth = null;
        var pickerWidth = Math.Max(UiLayout.AtTextSize(ClassPickerWidth), Pieces.PickerWidth(_cmbClass, ref classWidth));
        _cmbClass.Width = pickerWidth;

        // One height for all three fields: the drop-down's, which is set by
        // its font, so the two text fields match it exactly.
        var fieldHeight = Math.Max(_cmbClass.Height, AppTheme.BodyFont.Height + 12);

        nameField.Height = fieldHeight;
        transponderField.Size = new Size(pickerWidth, fieldHeight);

        // The column it was made with, at the text size in use - or wider,
        // when a class's name makes the drop-down wider than that column.
        var width = Math.Max(
            UiLayout.AtTextSize(DialogWidth),
            pickerWidth + UiLayout.PageLeft + UiLayout.PageRight);

        DialogFit.Keep(this, page, width: width, widthMeasured: true);
    }

    private void TryAccept()
    {
        var problem = DriverName.Length == 0
            ? "Enter their name."
            : Transponder.Length == 0
                ? $"{DriverName} needs a transponder number, or their laps can't be matched to them."
                : _ownerOf(Transponder) is { } owner
                    ? $"Transponder {Transponder} is already {owner}'s - tick them in the list instead."
                    : null;

        if (problem is not null)
        {
            _lblProblem.Text = problem;
            (DriverName.Length == 0 ? _txtName : _txtTransponder).Focus();
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}

/// <summary>
/// The commands the wizard drives, handed in rather than reached for.
///
/// Every one of these already exists somewhere and is already the only
/// implementation of what it does. Passing them in is what keeps the
/// wizard from growing its own copy - see the note on RaceDaySetupWizard.
/// </summary>
/// <param name="StartDay">The Day's StartNewDayFromWizard.</param>
/// <param name="ChangeDay">The Day's ChangeDayFromElsewhere.</param>
/// <param name="SetClassesRacing">The Day's SetRacingClassesFromElsewhere.</param>
/// <param name="PlanBuild">The programme's PlanQualifyingBuild - what building would do, changing nothing.</param>
/// <param name="Build">The programme's BuildQualifying.</param>
internal sealed record RaceDaySetupHost(
    Func<RaceDayShape, IReadOnlyList<string>, bool> StartDay,
    Action<Action<RaceEvent>> ChangeDay,
    Action<IReadOnlyList<string>> SetClassesRacing,
    Func<ProgramAnswer> PlanBuild,
    Func<ProgramAnswer> Build);
