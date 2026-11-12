namespace HeatCaller;

/// <summary>
/// The first tab of Race Day: which event today IS.
///
/// Everything on the tabs to the right of this one - entries, the program,
/// the run sheet - is filed against the current event (see EventSpine), so
/// the day starts here: make an event, and the rest of the page is about
/// it. A club that never bothers stays on the legacy event and loses
/// nothing but the history-keeping.
///
/// This is a picker and an editor, not a wizard. One press starts a race
/// day with sensible blanks; the fields exist for the clubs that name
/// their rounds and tie them to a championship.
/// </summary>
public sealed partial class RaceDayEventControl : UserControl
{
    private readonly RaceEventStore _events;
    private readonly VenueStore _venues;
    private readonly TrackLayoutStore _layouts;
    private readonly SeasonStore _seasons;
    private readonly CheckInStore _checkIns;
    private readonly RoundDefinitionStore _rounds;
    private readonly DayScheduleStore _schedule;
    private readonly RaceClassStore _raceClasses;

    /// <summary>Only for Delete Day - see DeleteDay. Null when the host did not pass one, which disables that button.</summary>
    private readonly SessionHistoryStore? _history;

    /// <summary>Deletes the day in the picker, and as much of what it left behind as the person asks for.</summary>
    private readonly Button _btnDeleteDay = new();
    private readonly UndoService? _undo;

    private readonly ComboBox _cmbEvent = new();

    /// <summary>
    /// What the picked day already holds. As tall as its longest form needs
    /// at the width it has, so the cards below do not hop when a day that is
    /// the current one - which adds a sentence - is picked or left.
    /// </summary>
    private readonly SteadyLabel _lblStatus = new()
    {
        Wordings = new[]
        {
            CurrentDaySentence + "999 checked in · 99 classes racing · 999 rounds in the program, 999 recorded · 999 timetable lines",
        },
    };

    /// <summary>What the status line says first when the picked day is the current one.</summary>
    private const string CurrentDaySentence = "CURRENT - the whole page is about this race day.  ";
    private readonly TextBox _txtName = new();

    /// <summary>
    /// The day's notes.
    ///
    /// Only editable from Championship → Calendar before this, while
    /// venue, layout, classes, qualifying rounds, heat size and
    /// championship were only editable here - two editors over one store,
    /// each holding fields the other could not reach, and no link between
    /// them. Whichever page somebody opened, something they wanted was on
    /// the other one.
    /// </summary>
    private readonly TextBox _txtNotes = new();
    private readonly ComboBox _cmbVenue = new();
    private readonly ComboBox _cmbLayout = new();
    private readonly ComboBox _cmbChampionship = new();
    private readonly NumericUpDown _numQualRounds = new();

    // --- How the programme gets built ---
    //
    // These three used to live on the Program tab, in a box called
    // "Generate Qualifying Heats". They are not really about generating
    // anything: they are the shape of the race day, the same as the venue
    // and the round count sitting above them, and the Program tab is a
    // better page for having nothing on it but the running order and the
    // next thing to do.
    private readonly CheckedListBox _lstClasses = new ThemedCheckedListBox();
    private readonly NumericUpDown _numHeatSize = new();

    /// <summary>How the first round is seeded (RaceEvent.FirstRoundSeeding, 11 Nov 2026).</summary>
    private readonly ComboBox _cmbFirstRound = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    /// <summary>Small classes share a heat under this many drivers (RaceEvent.ShareHeatsUnder, 11 Nov 2026, Phase 4.1). 0 = off.</summary>
    private readonly NumericUpDown _numShareUnder = new();

    /// <summary>The entry fee and the fee for each extra class (RaceEvent.EntryFee, ExtraClassFee - 11 Nov 2026).</summary>
    private readonly NumericUpDown _numEntryFee = new() { DecimalPlaces = 2, Maximum = 1000, Increment = 1 };

    private readonly NumericUpDown _numExtraFee = new() { DecimalPlaces = 2, Maximum = 1000, Increment = 1 };
    private readonly CheckBox _chkCheckedInOnly = new ThemedCheckBox();
    private readonly CheckBox _chkAutoAdvance = new ThemedCheckBox();

    /// <summary>
    /// Close the night with the checklist (11 Nov 2026 - AppSettings.UseCloseNightChecklist).
    /// The club's, not the day's: saved straight to settings, never part of
    /// the race day's edits or undo.
    /// </summary>
    private readonly CheckBox _chkCloseNight = new ThemedCheckBox();

    /// <summary>
    /// Post the heats before the first race (12 Nov 2026, setup plan D2 -
    /// AppSettings.PostHeatsBeforeFirstRace). The club's, like the checklist above.
    /// </summary>
    private readonly CheckBox _chkPostHeats = new ThemedCheckBox();

    /// <summary>Build the heats when entries close (setup plan B2 - AppSettings.BuildHeatsWhenEntriesClose). The club's.</summary>
    private readonly CheckBox _chkBuildAtClose = new ThemedCheckBox();

    /// <summary>Add late check-ins to posted heats without asking (setup plan D6 - AppSettings.AddLateCheckInsWithoutAsking). The club's.</summary>
    private readonly CheckBox _chkLateWithoutAsking = new ThemedCheckBox();

    /// <summary>Papers (setup plan D9 - AppSettings.AskForWaivers, CheckMemberships). The club's.</summary>
    private readonly CheckBox _chkWaivers = new ThemedCheckBox();

    private readonly CheckBox _chkMemberships = new ThemedCheckBox();

    private const string EntriesCloseCaption = "Entries close at:";

    /// <summary>
    /// When entries close (RaceEvent.EntriesCloseLocalTime, 12 Nov 2026, setup
    /// plan D1): the same tick-and-picker pair as the first race's time, for
    /// the same reason.
    /// </summary>
    private readonly CheckBox _chkEntriesClose = new ThemedCheckBox();

    private readonly TimePicker _dtpEntriesClose = new();

    /// <summary>The night's planned breaks on one line (setup plan D5), and the button that changes them.</summary>
    private readonly Label _lblBreaks = new();

    private readonly Button _btnBreaks = new();

    /// <summary>Tonight's class running order on one line (setup plan D7), and the button that changes it.</summary>
    private readonly Label _lblClassOrder = new();

    private readonly Button _btnClassOrder = new();
    private readonly ComboBox _cmbAutoAdvanceScope = new();

    /// <summary>Between-race talk for this day: the club's Between races settings, or one of the card's presets over them (31 Oct 2026).</summary>
    private readonly ComboBox _cmbGapPreset = new();
    private readonly Label _lblAutoAdvanceHint = new();
    private Label? _lblAutoAdvanceScope;
    private readonly AppSettings? _settings;
    private readonly Button _btnNew = new();
    private readonly Button _btnWizard = new();

    /// <summary>
    /// Raised by "Set Up Tonight...". The host opens the wizard, because
    /// the wizard needs the programme and the check-in sheet as well as
    /// this page, and this page should not be reaching for either.
    /// </summary>
    public event Action? SetUpTonightRequested;

    /// <summary>The "Close the night with a checklist" tick changed, so the Next Steps card should say it again.</summary>
    public event Action? CloseNightChecklistChanged;

    /// <summary>The "Post the heats before the first race" tick, or the night's breaks, changed - so Next Steps should say it again.</summary>
    public event Action? NightSetupChanged;

    /// <summary>
    /// The caption on the qualifying-rounds spinner.
    ///
    /// Was "Qualifying rounds:", which is what the sport calls the number and
    /// tells a first-time race director nothing about what they are setting.
    /// The field still writes RaceEvent.QualifyingRounds and the rounds it
    /// produces are still labelled Q1, Q2, Q3 - only the prompt changed. Held
    /// as a constant because the label column below is measured from every
    /// caption on the page, including this one, and a literal written twice
    /// is a column that silently stops fitting.
    /// </summary>
    private const string HeatsPerDriverCaption = "Heats each driver gets:";

    /// <summary>
    /// The same row on a club night, where nothing is qualifying for
    /// anything and a heat is just a race. Both captions are in the column
    /// measurement, so switching between them never moves the fields.
    /// </summary>
    private const string RacesPerDriverCaption = "Races each driver gets:";

    private const string ShapeCaption = "What kind of night:";

    private const string FirstRaceCaption = "First race at:";

    /// <summary>
    /// When the first race goes out, and whether it has been set at all.
    ///
    /// Two controls because RaceEvent.FirstRaceLocalTime is nullable and a
    /// time picker has no way to say "not set". A club that never puts a
    /// time on the day should not have one invented for it, and the
    /// timetable's Fill In the Times reads this as its starting point.
    ///
    /// The property had been on RaceEvent since race days were introduced,
    /// with a comment describing a run sheet measuring drift against it -
    /// and nothing anywhere read or wrote it. This is that UI.
    /// </summary>
    private readonly CheckBox _chkFirstRace = new ThemedCheckBox();

    private readonly TimePicker _dtpFirstRace = new();

    private const string OutByCaption = "Out of the venue by:";

    /// <summary>
    /// When the club has to be out of the venue (RaceEvent.OutByLocalTime),
    /// and whether it has been set: the same pair as the first race's time,
    /// for the same reason. The Next Steps card measures the night's
    /// projected finish against it (11 Nov 2026, race-night build plan,
    /// Phase 2).
    /// </summary>
    private readonly CheckBox _chkOutBy = new ThemedCheckBox();

    private readonly TimePicker _dtpOutBy = new();

    /// <summary>One entry in the shape picker. ToString is what the combo draws.</summary>
    private sealed record ShapeChoice(RaceDayShape Shape, string Caption)
    {
        public override string ToString() => Caption;
    }

    /// <summary>What kind of night this day is - see <see cref="RaceDayShape"/>. The most consequential field on the page, so it sits near the top.</summary>
    private readonly ComboBox _cmbShape = new();

    /// <summary>The hint under the shape picker, worded from it - as tall as the longer wording, so the fields below never move as the box is used.</summary>
    private readonly SteadyLabel _lblShapeHint = new() { Wordings = new[] { RaceDayShapeHint, ClubNightShapeHint } };

    private const string RaceDayShapeHint =
        "Qualifying heats, then finals built from them. Heats are built from drivers who have checked in.";

    private const string ClubNightShapeHint =
        "A flat list of races - no finals, no seeding, no bump-ups. Everyone in a racing class is in them.";

    /// <summary>The rounds-per-driver caption, held because its wording comes from the shape.</summary>
    private Label? _lblQualRounds;

    /// <summary>The hint under the class tick list - also worded from the shape, and as tall as the longer wording.</summary>
    private readonly SteadyLabel _lblClassHint = new() { Wordings = new[] { RaceDayClassHint, ClubNightClassHint } };

    private const string RaceDayClassHint =
        "Tick the classes running today. Next Steps builds all their heats in one press.";

    private const string ClubNightClassHint =
        "Tick the classes running today. Next Steps builds all their races in one press.";

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>
    /// The drop-downs' widths, measured from their choices - see
    /// Pieces.PickerWidth. Forgotten whenever the lists are rebuilt (see
    /// RefitFields).
    /// </summary>
    private (Font Font, int Count, int Width)? _eventWidth, _shapeWidth, _venueWidth, _layoutWidth, _championshipWidth, _scopeWidth, _gapPresetWidth, _firstRoundWidth;

    /// <summary>The widths the fields had at the normal text size, before any of them was measured - see SizeFields.</summary>
    private const int FieldWidth = 320;
    private const int EventPickerWidth = 420;
    private const int ScopePickerWidth = 260;
    private const int NumberWidth = 70;

    /// <summary>Says "Saved" for a moment after a change goes in, so saving-as-you-type is visible rather than assumed.</summary>
    private readonly Label _lblSaved = new();

    /// <summary>
    /// Waits for a pause in typing before writing.
    ///
    /// Without it every keystroke in the name box would be its own save and
    /// its own undo entry, and undoing a rename would take twelve presses.
    /// </summary>
    private readonly System.Windows.Forms.Timer _saveTimer = new() { Interval = 700 };

    /// <summary>
    /// The day as it was when the current run of edits began, for one undo
    /// entry covering the lot. Null when nothing is pending.
    /// </summary>
    private string? _editBaseline;

    /// <summary>Hides the "Saved" note again a moment after it appears.</summary>
    private readonly System.Windows.Forms.Timer _savedNoteTimer = new() { Interval = 1600 };
    private readonly Button _btnCloseDay = new();

    /// <summary>Set while the picker is being rebuilt, so that does not count as a choice.</summary>
    private bool _loading;

    /// <summary>
    /// The current day as of the last RefreshAll, so the next one can tell
    /// whether the current day has changed in between. See RefreshAll.
    /// </summary>
    private string? _lastSeenCurrentId;

    /// <summary>One event in the picker, rendered the way it reads in a list.</summary>
    private sealed record Choice(RaceEvent Event)
    {
        public override string ToString()
        {
            var when = Event.IsLegacy
                ? "before events"
                : Event.StartedUtc.ToLocalTime().ToString("d MMM yyyy");

            var state = Event.IsOpen ? "open" : "closed";

            return $"{Event.DisplayName}  ({when}, {state})";
        }
    }

    public RaceDayEventControl(
        RaceEventStore events,
        VenueStore venues,
        TrackLayoutStore layouts,
        SeasonStore seasons,
        CheckInStore checkIns,
        RoundDefinitionStore rounds,
        DayScheduleStore schedule,
        RaceClassStore raceClasses,
        UndoService? undo = null,

        // Only for Delete Day, which has to reach past the day itself into
        // the races it produced. Optional so nothing else that builds this
        // control has to care.
        SessionHistoryStore? history = null,

        // The club-wide auto-advance values, which answer for any race day
        // that never set its own - see RaceEvent.AutoAdvanceOn. Optional so
        // nothing else that builds this control has to care; without it the
        // fallback is simply "on", which is what the club default has
        // always been.
        AppSettings? settings = null)
    {
        _settings = settings;
        _history = history;
        _undo = undo;
        _events = events;
        _venues = venues;
        _layouts = layouts;
        _seasons = seasons;
        _checkIns = checkIns;
        _rounds = rounds;
        _schedule = schedule;
        _raceClasses = raceClasses;

        Dock = DockStyle.Fill;

        // The body face, worn rather than inherited: this page sits in a tab
        // whose row is drawn in the bold face, which it would otherwise take.
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout();
        RefreshAll();
    }

    /// <summary>The page's own face changed - handed the body face at a new text size - so everything else it keeps in step follows. See TextSize.</summary>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>
    /// A new text size, reaching this page through its tab's bold face -
    /// the only way it does, since the page wears a face of its own. See
    /// TextSize.
    /// </summary>
    protected override void OnParentFontChanged(EventArgs e)
    {
        base.OnParentFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>
    /// Builds the page on the layout pieces (LayoutPieces.cs): the intro,
    /// then three cards - which day, what the day is, and how its program
    /// is built - with their fields lined up down the page, then Close Day
    /// and Delete Day. Nothing is placed by hand, and the page scrolls when
    /// the window is too short for it.
    ///
    /// It was one long column placed row by row from a field column
    /// measured once, with the hints held to two lines at 560px and the
    /// fields at widths chosen by eye at the normal text size - and no way
    /// to scroll, so at 1280x720 the bottom of it was simply not there.
    /// </summary>
    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "Check-ins, scrutineering, the heats and the timetable are all filed against the race day picked here. "
                 + "A new race day starts them fresh and keeps last week's under last week's day. Changes save as you make them.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        // --- Which race day ---
        _cmbEvent.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbEvent.SelectedIndexChanged += (_, _) =>
        {
            if (_loading)
            {
                return;
            }

            // Before the fields are reloaded from the day being moved
            // to, or a pending edit would be written onto the wrong
            // day - or lost, which is what used to happen.
            FlushPendingEdits();
            LoadSelected();

            // Picking a day IS switching to it.
            //
            // There used to be a "Switch to This Day" button beside this
            // box, which made the box mean "the day I am looking at" and
            // the button mean "the day the app is about" - two ideas for
            // one decision, and the second of them only discoverable by
            // finding a button below the fold. Nobody picks a day here to
            // admire it.
            //
            // MakeCurrent captures an undo entry, which is the safety net:
            // a switch made by accident is one Ctrl+Z back, and it is
            // exactly the entry the button used to capture.
            MakeCurrent();
        };

        // A combo box changes its selection on the scroll wheel, and this
        // one now re-points the entire app. Scrolling this page with the
        // pointer happening to be over the box would have switched the race
        // day, silently, mid-evening. Refused outright: the box is set by
        // clicking it, which is a thing somebody meant to do.
        _cmbEvent.MouseWheel += (_, e) =>
        {
            if (e is HandledMouseEventArgs handled)
            {
                handled.Handled = true;
            }
        };

        _btnNew.Text = "New Race Day";
        _btnNew.Click += (_, _) => _ = StartNewDay();

        // The wizard's front door, beside the button that starts a day the
        // short way. Both are here because both are "I am setting up
        // tonight" - one for somebody who knows what they want, one for
        // somebody who would rather be asked. On the same line as the picker
        // when there is room for all three, and wrapping under it when there
        // is not - at a larger text size, or on a narrow window.
        _btnWizard.Text = "Set Up Tonight...";
        _btnWizard.Click += (_, _) => SetUpTonightRequested?.Invoke();

        _lblStatus.ForeColor = AppTheme.TextMuted;

        var dayGrid = new FieldGrid();

        dayGrid.AddRow("Working on:", FieldGrid.Inline(_cmbEvent, _btnNew, _btnWizard));

        var which = new Section("Which race day");

        which.Add(dayGrid);
        which.Add(_lblStatus, gapBelow: 0);

        // --- What the day is ---
        var aboutGrid = new FieldGrid();

        aboutGrid.AddRow("Name:", _txtName);

        // What kind of night: second, right under the name, because it
        // decides more about what the rest of the app shows than every field
        // below it put together. A day saved before this existed has no
        // shape in its file and reads back as a race day, which is what it
        // has always behaved as.
        _cmbShape.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbShape.Items.Add(new ShapeChoice(RaceDayShape.RaceDay, "Race day - heats, then finals"));
        _cmbShape.Items.Add(new ShapeChoice(RaceDayShape.ClubNight, "Club night - just races"));
        _cmbShape.SelectedIndex = 0;

        aboutGrid.AddRow(ShapeCaption, _cmbShape);
        aboutGrid.AddHint(_lblShapeHint);

        // Editable combos: the venue and layout lists are suggestions, not
        // gatekeepers - a club night at a friend's garage should not need a
        // venue profile made first.
        _cmbVenue.DropDownStyle = ComboBoxStyle.DropDown;
        aboutGrid.AddRow("Venue:", _cmbVenue);

        _cmbLayout.DropDownStyle = ComboBoxStyle.DropDown;
        aboutGrid.AddRow("Track layout:", _cmbLayout);

        _cmbChampionship.DropDownStyle = ComboBoxStyle.DropDownList;
        aboutGrid.AddRow("Championship:", _cmbChampionship);

        // --- When it starts ---
        //
        // A tick and a picker on one row: the property is nullable, and a
        // time picker cannot say "not set".
        //
        // TimePicker, not WinForms' DateTimePicker. That one wraps a native
        // Windows control which ignores BackColor and ForeColor almost
        // entirely, so its spinner came out in system grey against the dark
        // theme - a pasted-on white block - and clipped its own text at any
        // sensible width. TimePicker paints every pixel itself and is what
        // the rest of the app already uses for a time of day. It sizes
        // itself to its time in its font (AutoSize), and wears the body face
        // it was built with at the text size in use.
        _dtpFirstRace.AutoSize = true;
        _dtpFirstRace.Value = TimeSpan.FromHours(10);
        _textSize.Wear(_dtpFirstRace, () => AppTheme.BodyFont);

        _chkFirstRace.Text = "Set";
        _chkFirstRace.AutoSize = true;

        aboutGrid.AddRow(FirstRaceCaption, FieldGrid.Inline(_dtpFirstRace, _chkFirstRace));

        _dtpOutBy.AutoSize = true;
        _dtpOutBy.Value = TimeSpan.FromHours(22);
        _textSize.Wear(_dtpOutBy, () => AppTheme.BodyFont);

        _chkOutBy.Text = "Set";
        _chkOutBy.AutoSize = true;

        aboutGrid.AddRow(OutByCaption, FieldGrid.Inline(_dtpOutBy, _chkOutBy));

        // Entries close (12 Nov 2026, setup plan D1): above the first race in
        // the night, but kept below it here so the rows people already know
        // don't move.
        _dtpEntriesClose.AutoSize = true;
        _dtpEntriesClose.Value = TimeSpan.FromHours(18.5);
        _textSize.Wear(_dtpEntriesClose, () => AppTheme.BodyFont);

        _chkEntriesClose.Text = "Set";
        _chkEntriesClose.AutoSize = true;

        aboutGrid.AddRow(EntriesCloseCaption, FieldGrid.Inline(_dtpEntriesClose, _chkEntriesClose));
        aboutGrid.AddHint("After this, a driver who checks in is a late entry. The announcer calls it ten minutes before and as it passes, and Next Steps counts down to it.");

        // The night's planned breaks (setup plan D5): a line saying what they
        // are, and a window to change them.
        _lblBreaks.AutoSize = true;
        _lblBreaks.Margin = new Padding(0, 6, 8, 0);
        _lblBreaks.Text = "None";
        _btnBreaks.Text = "Breaks...";
        _btnBreaks.AutoSize = true;
        _btnBreaks.Click += (_, _) => EditBreaks();

        aboutGrid.AddRow("Breaks:", FieldGrid.Inline(_lblBreaks, _btnBreaks));
        aboutGrid.AddHint("Watering the track, dinner, track repairs. The finish time, every estimated start and the gap before the next race allow for them, and the announcer tells the room.");

        // Tell the drivers (12 Nov 2026, setup plan D13).
        aboutGrid.AddRow("Next night on phones:", BuildNextNightRow());
        aboutGrid.AddHint("Announce the next race night on the Home page of the club's phone pages - the classes on and the times - so drivers can say they're coming. Call It Off tells them it's off, and emails the regulars.");

        _txtNotes.Multiline = true;
        _txtNotes.AcceptsReturn = true;
        _txtNotes.ScrollBars = ScrollBars.Vertical;
        aboutGrid.AddRow("Notes:", _txtNotes);

        var about = new Section("About the day");

        about.Add(aboutGrid, gapBelow: 0);

        // --- What the program is built from ---
        _numQualRounds.Minimum = 1;
        _numQualRounds.Maximum = 10;
        _numQualRounds.Value = 3;

        var programGrid = new FieldGrid();

        _lblQualRounds = programGrid.AddRow(HeatsPerDriverCaption, _numQualRounds);

        _lstClasses.CheckOnClick = true;
        _lstClasses.BorderStyle = BorderStyle.None;
        _lstClasses.IntegralHeight = false;
        programGrid.AddRow("Classes racing:", _lstClasses);
        programGrid.AddHint(_lblClassHint);

        _numHeatSize.Minimum = 2;
        _numHeatSize.Maximum = 20;
        _numHeatSize.Value = 8;
        programGrid.AddRow("Drivers per heat:", _numHeatSize);

        foreach (var mode in Enum.GetValues<FirstRoundSeeding>())
        {
            _cmbFirstRound.Items.Add(FirstRound.Words(mode));
        }

        _cmbFirstRound.SelectedIndex = 0;
        programGrid.AddRow("First round seeded:", _cmbFirstRound);

        // Mixed-class heats (11 Nov 2026, Phase 4.1): 0 is off, which is
        // every race day until somebody sets it.
        _numShareUnder.Minimum = 0;
        _numShareUnder.Maximum = 19;
        _numShareUnder.Value = 0;
        programGrid.AddRow("Share a heat under (drivers):", _numShareUnder);
        programGrid.AddRow("Entry fee:", _numEntryFee);
        programGrid.AddRow("Each extra class:", _numExtraFee);

        _chkCheckedInOnly.Text = "Only include drivers who have checked in";
        _chkCheckedInOnly.AutoSize = true;
        programGrid.AddField(_chkCheckedInOnly);

        // Rolling into the next race. It used to be a strip at the bottom
        // of the programme, among the buttons pressed every ten minutes -
        // and it is not that kind of setting: it is decided once, at the
        // start of a night, beside every other decision about how the
        // night runs. Which is here.
        //
        // NOT called "auto-advance" any more. The race format has a setting
        // of its own by that name - Stage, Countdown, Green running itself
        // within one race - and the two are different things that somebody
        // has to be able to tell apart from the label alone.
        _chkAutoAdvance.Text = "Start the next race automatically";
        _chkAutoAdvance.AutoSize = true;
        programGrid.AddField(_chkAutoAdvance);

        _cmbAutoAdvanceScope.DropDownStyle = ComboBoxStyle.DropDownList;
        _lblAutoAdvanceScope = programGrid.AddRow("Next race is:", _cmbAutoAdvanceScope);

        _lblAutoAdvanceHint.Text = "Which race it reaches for when the one that just finished is recorded.";
        programGrid.AddHint(_lblAutoAdvanceHint);

        // Which race it reaches for is Advanced (1 Nov 2026, Simple mode);
        // the tick above stays.
        SimpleMode.HideRow(programGrid, _cmbAutoAdvanceScope);

        // BETWEEN-RACE TALK FOR THE DAY (31 Oct 2026): a big event can run
        // busier than club night without the club's own settings changing.
        // Over the Between races card on the AI Commentary tab, for this day
        // only.
        _cmbGapPreset.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbGapPreset.Items.Add(new GapPresetChoice(string.Empty, "The club's Between races settings"));

        foreach (var preset in GapPreset.All)
        {
            _cmbGapPreset.Items.Add(new GapPresetChoice(preset.Key, preset.Name));
        }

        _cmbGapPreset.SelectedIndex = 0;

        // The class running order (12 Nov 2026, setup plan D7).
        _lblClassOrder.AutoSize = true;
        _lblClassOrder.Margin = new Padding(0, 6, 8, 0);
        _lblClassOrder.Text = "As on the Classes page";
        _btnClassOrder.Text = "Order...";
        _btnClassOrder.AutoSize = true;
        _btnClassOrder.Click += (_, _) => EditClassOrder();
        programGrid.AddRow("Class running order:", FieldGrid.Inline(_lblClassOrder, _btnClassOrder));
        programGrid.AddHint("Which class goes first in each round, and whether the order turns round each round. It decides who marshals for whom and when a class finishes. Used when the heats are built.");

        programGrid.AddRow("Between-race talk:", _cmbGapPreset);
        programGrid.AddHint("How much the announcer says between races tonight. A preset here is for this day only - the club's own settings are on the AI Commentary tab.");

        // Posting the heats (12 Nov 2026, setup plan D2). The club's, like
        // the closing checklist below.
        _chkPostHeats.Text = "Post the heats before the first race";
        _chkPostHeats.AutoSize = true;
        _chkPostHeats.Checked = _settings?.PostHeatsBeforeFirstRace ?? true;
        _chkPostHeats.Enabled = _settings is not null;
        _chkPostHeats.CheckedChanged += (_, _) =>
        {
            if (_settings is null || _settings.PostHeatsBeforeFirstRace == _chkPostHeats.Checked)
            {
                return;
            }

            _settings.PostHeatsBeforeFirstRace = _chkPostHeats.Checked;
            _settings.Save();
            NightSetupChanged?.Invoke();
        };
        programGrid.AddField(_chkPostHeats);
        programGrid.AddHint("Once the heats are built, Next Steps offers Post the heats... before the first race: it prints the heat sheets and the marshal list, tells the room, and lists any car the loop hasn't heard tonight. After that, moving drivers between heats asks first. For every night, not just this one.");

        // Setup plan B2 and D6: the club's, like the posting tick above.
        ClubTick(_chkBuildAtClose, "Build the heats when entries close",
            () => _settings!.BuildHeatsWhenEntriesClose, on => _settings!.BuildHeatsWhenEntriesClose = on, true);
        programGrid.AddField(_chkBuildAtClose);
        programGrid.AddHint("With Entries close at: set, the heats build themselves as it passes, ready to post. Undo takes the build back.");

        ClubTick(_chkLateWithoutAsking, "Add late check-ins to posted heats without asking",
            () => _settings!.AddLateCheckInsWithoutAsking, on => _settings!.AddLateCheckInsWithoutAsking = on, false);
        programGrid.AddField(_chkLateWithoutAsking);
        programGrid.AddHint("A driver who checks in after the heats are built goes into the heat nearest their pace that has room, and nobody else moves. Before the heats are posted that happens by itself. After, Next Steps asks you first - unless this is ticked.");

        // Papers (setup plan D9): the club's.
        ClubTick(_chkWaivers, "Ask every driver for a signed waiver",
            () => _settings!.AskForWaivers, on => _settings!.AskForWaivers = on, false);
        programGrid.AddField(_chkWaivers);
        ClubTick(_chkMemberships, "Check memberships",
            () => _settings!.CheckMemberships, on => _settings!.CheckMemberships = on, false);
        programGrid.AddField(_chkMemberships);
        programGrid.AddHint("Who's Racing shows a Papers column - \"Waiver missing\", \"Membership expired 3 Oct\" - and Post the heats lists anyone not in order. A junior's waiver is signed by a parent or guardian. Recorded by hand: click a driver's Papers to fill them in. A waiver lasts 12 months.");

        // The closing checklist (11 Nov 2026). Every night, not just this
        // one, so it saves straight to the club's settings.
        _chkCloseNight.Text = "Close the night with a checklist";
        _chkCloseNight.AutoSize = true;
        _chkCloseNight.Checked = _settings?.UseCloseNightChecklist ?? true;
        _chkCloseNight.Enabled = _settings is not null;
        _chkCloseNight.CheckedChanged += (_, _) =>
        {
            if (_settings is null || _settings.UseCloseNightChecklist == _chkCloseNight.Checked)
            {
                return;
            }

            _settings.UseCloseNightChecklist = _chkCloseNight.Checked;
            _settings.Save();
            CloseNightChecklistChanged?.Invoke();
        };
        programGrid.AddField(_chkCloseNight);
        programGrid.AddHint("When every race has run, Next Steps opens a list of what's left - rentals, money, backups, Close Day and your own lines - to tick off. Off, it just says what's left in one line. For every night, not just this one.");

        var program = new Section("Building the program");

        program.Add(programGrid, gapBelow: 0);

        // --- The actions ---
        //
        // There is no Save button. There was, and it was the one step in
        // the whole flow whose omission was both silent and expensive:
        // nothing guarded against leaving the tab with edits pending, so a
        // ticked class was quietly discarded - and a day naming no classes
        // makes the programme fall back to building for EVERY class that
        // has a driver in it. The commonest mistake produced a plausible
        // wrong answer with no warning anywhere. Saving as you go removes
        // the mistake rather than warning about it.
        // "Switch to This Day" is gone. Picking the day in the box at the
        // top of this page is what switches to it - see where that box is
        // wired. Two controls for one decision, the second of them below
        // the fold, was a step that existed only because the box was
        // written first and the switch bolted on afterwards.
        _btnCloseDay.Text = "Close Day";
        _btnCloseDay.Click += (_, _) => CloseOrReopen();

        // Last in the row, and the only destructive thing on this page.
        _btnDeleteDay.Text = "Delete Day...";
        _btnDeleteDay.Click += (_, _) => DeleteDay();

        _lblSaved.ForeColor = AppTheme.Success;
        _lblSaved.Visible = false;

        var lblHint = _textSize.Wear(
            new Label
            {
                Text = "Close Day stops new rounds joining the day; results and standings still read it. "
                     + "The legacy day holds everything recorded before race days were tracked, and is read-only here.",
                AutoSize = true,
                ForeColor = AppTheme.TextMuted,
            },
            () => AppTheme.SmallFont);

        // --- The page ---
        var page = new PageStack();

        page.Add(lblIntro, gapBelow: UiLayout.RowGap);
        page.Add(which);
        page.Add(about);
        page.Add(program);
        page.Add(FieldGrid.Inline(_btnCloseDay, _btnDeleteDay, _lblSaved), gapBelow: UiLayout.RowGap);
        page.Add(lblHint, gapBelow: 0);

        Controls.Add(page);

        // Once the fields are on the page and wear its face - what they are
        // measured in.
        //
        // One caption column down all three cards, wide enough for both
        // wordings of the rounds-per-driver caption, so neither the text
        // size nor the kind of night ever moves the fields sideways.
        _textSize.Keep(() => FieldGrid.LineUp(new[] { dayGrid, aboutGrid, programGrid }, HeatsPerDriverCaption, RacesPerDriverCaption));
        _textSize.Keep(SizeFields);

        // The two shape-worded captions and the two hints, filled once here
        // so the page is never briefly blank in those spots - LoadFields
        // does it again for whichever day is picked, and it is only wording.
        ApplyShapeWording();

        WireAutoSave();
    }

    /// <summary>
    /// The fields' sizes at the text size in use. The drop-downs are as wide
    /// as their longest choice, and never narrower than they were at the
    /// normal text size; the boxes are the width they were then, at this
    /// size; the class list is five lines tall and the notes three, of the
    /// font they are drawn in.
    ///
    /// Run again when the text size changes (TextSize.Keep) and when the
    /// lists are rebuilt (RefitFields).
    /// </summary>
    private void SizeFields()
    {
        var wide = UiLayout.AtTextSize(FieldWidth);
        var number = UiLayout.AtTextSize(NumberWidth);

        Fit(_cmbEvent, Math.Max(UiLayout.AtTextSize(EventPickerWidth), Pieces.PickerWidth(_cmbEvent, ref _eventWidth)));
        Fit(_txtName, wide);
        Fit(_cmbShape, Math.Max(wide, Pieces.PickerWidth(_cmbShape, ref _shapeWidth)));
        Fit(_cmbVenue, Math.Max(wide, Pieces.PickerWidth(_cmbVenue, ref _venueWidth)));
        Fit(_cmbLayout, Math.Max(wide, Pieces.PickerWidth(_cmbLayout, ref _layoutWidth)));
        Fit(_cmbChampionship, Math.Max(wide, Pieces.PickerWidth(_cmbChampionship, ref _championshipWidth)));
        Fit(_txtNotes, wide, Pieces.LineHeight(_txtNotes.Font) * 3 + UiLayout.HintGap * 2);
        Fit(_numQualRounds, number);
        Fit(_lstClasses, wide, _lstClasses.Font.Height * 5 + 12);
        Fit(_numHeatSize, number);
        Fit(_cmbFirstRound, Math.Max(UiLayout.AtTextSize(160), Pieces.PickerWidth(_cmbFirstRound, ref _firstRoundWidth)));
        Fit(_numShareUnder, number);
        Fit(_numEntryFee, number);
        Fit(_numExtraFee, number);
        Fit(_cmbAutoAdvanceScope, Math.Max(UiLayout.AtTextSize(ScopePickerWidth), Pieces.PickerWidth(_cmbAutoAdvanceScope, ref _scopeWidth)));
        Fit(_cmbGapPreset, Math.Max(wide, Pieces.PickerWidth(_cmbGapPreset, ref _gapPresetWidth)));

        // Only when it changed: a new size lays the grid out again.
        static void Fit(Control field, int width, int? height = null)
        {
            var size = new Size(width, height ?? field.Height);

            if (field.Size != size)
            {
                field.Size = size;
            }
        }
    }

    /// <summary>
    /// The drop-downs measured again after their lists were rebuilt, which
    /// can change their longest choice without changing how many they hold.
    /// </summary>
    private void RefitFields()
    {
        _eventWidth = _shapeWidth = _venueWidth = _layoutWidth = _championshipWidth = _scopeWidth = null;
        SizeFields();
    }

    // ------------------------------------------------------------------
    //  Saving as you go
    // ------------------------------------------------------------------

    /// <summary>
    /// Makes every field write itself.
    ///
    /// Two kinds of moment. A field with a discrete value - a tick, a
    /// number, a chosen item - commits the instant it changes, because
    /// there is nothing to wait for. A field being typed into waits for a
    /// pause, so a name is one save and one undo entry rather than one per
    /// letter, and commits on the way out as well in case the pause never
    /// comes.
    ///
    /// The checked list is the awkward one: ItemCheck is raised BEFORE the
    /// tick is applied, so reading CheckedItems from inside it gives the
    /// state before the click. Posting the commit puts it after.
    /// </summary>
    private void WireAutoSave()
    {
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            CommitEdits();
        };

        _savedNoteTimer.Tick += (_, _) =>
        {
            _savedNoteTimer.Stop();
            _lblSaved.Visible = false;
        };

        _txtName.TextChanged += (_, _) => EditStarted();
        _txtNotes.TextChanged += (_, _) => EditStarted();
        _cmbVenue.TextChanged += (_, _) => EditStarted();
        _cmbLayout.TextChanged += (_, _) => EditStarted();

        _txtName.Leave += (_, _) => CommitNow();
        _txtNotes.Leave += (_, _) => CommitNow();
        _cmbVenue.Leave += (_, _) => CommitNow();
        _cmbLayout.Leave += (_, _) => CommitNow();

        _cmbChampionship.SelectedIndexChanged += (_, _) => CommitNow();

        // The wording first, then the save. Re-wording is free and has to
        // happen even while LoadFields is filling the boxes - that is how
        // picking a different day in the top combo gets the right captions -
        // and CommitNow is the one that knows to do nothing then.
        _cmbShape.SelectedIndexChanged += (_, _) =>
        {
            ApplyShapeWording();
            CommitNow();
        };

        _numQualRounds.ValueChanged += (_, _) => CommitNow();

        _chkFirstRace.CheckedChanged += (_, _) =>
        {
            // Greyed rather than hidden: a picker that vanishes makes the
            // row jump, and the rows below it with it.
            _dtpFirstRace.Enabled = _chkFirstRace.Checked && !(Selected?.IsLegacy ?? true);
            CommitNow();
        };

        _dtpFirstRace.ValueChanged += CommitNow;

        _chkOutBy.CheckedChanged += (_, _) =>
        {
            _dtpOutBy.Enabled = _chkOutBy.Checked && !(Selected?.IsLegacy ?? true);
            CommitNow();
        };

        _dtpOutBy.ValueChanged += CommitNow;

        _chkEntriesClose.CheckedChanged += (_, _) =>
        {
            _dtpEntriesClose.Enabled = _chkEntriesClose.Checked && !(Selected?.IsLegacy ?? true);
            CommitNow();
        };

        _dtpEntriesClose.ValueChanged += CommitNow;
        _numHeatSize.ValueChanged += (_, _) => CommitNow();
        _cmbFirstRound.SelectedIndexChanged += (_, _) => CommitNow();
        _numShareUnder.ValueChanged += (_, _) => CommitNow();
        _numEntryFee.ValueChanged += (_, _) => CommitNow();
        _numExtraFee.ValueChanged += (_, _) => CommitNow();
        _chkCheckedInOnly.CheckedChanged += (_, _) => CommitNow();
        _chkAutoAdvance.CheckedChanged += (_, _) =>
        {
            // "Next race is:" only means something while the next race
            // starts by itself: grayed out the moment the box is unticked,
            // not only when the page is next filled (docs check, 30 Sep).
            // Changeable as the day is - the legacy holder never is - read
            // from the day, as the first race time's box is.
            FollowAutoAdvance(!(Selected?.IsLegacy ?? true));
            CommitNow();
        };
        _cmbAutoAdvanceScope.SelectedIndexChanged += (_, _) => CommitNow();
        _cmbGapPreset.SelectedIndexChanged += (_, _) => CommitNow();

        _lstClasses.ItemCheck += (_, _) =>
        {
            // IsHandleCreated as well as the rest: BeginInvoke throws
            // without one, and the list is filled before this control is
            // ever shown.
            if (_loading || IsDisposed || !IsHandleCreated)
            {
                return;
            }

            BeginInvoke(new Action(CommitNow));
        };

        // A day being closed by the window closing still keeps what was
        // typed a moment before it.
        HandleDestroyed += (_, _) => CommitEdits();
    }

    /// <summary>
    /// Notes that an edit is under way and restarts the pause.
    ///
    /// The baseline is taken on the FIRST change of a run, so the one undo
    /// entry that follows puts back what was there before the whole run
    /// rather than before its last keystroke.
    /// </summary>
    private void EditStarted()
    {
        if (_loading || IsDisposed || Selected is not { IsLegacy: false } raceEvent)
        {
            return;
        }

        _editBaseline ??= System.Text.Json.JsonSerializer.Serialize(raceEvent);

        _saveTimer.Stop();
        _saveTimer.Start();
    }

    /// <summary>Writes now, without waiting for the pause.</summary>
    private void CommitNow()
    {
        if (_loading || IsDisposed)
        {
            return;
        }

        _editBaseline ??= Selected is { IsLegacy: false } raceEvent
            ? System.Text.Json.JsonSerializer.Serialize(raceEvent)
            : null;

        _saveTimer.Stop();
        CommitEdits();
    }

    /// <summary>
    /// Writes any pending edit and gives it one undo entry.
    ///
    /// Safe to call when nothing is pending, which is what lets every exit
    /// - a tab change, the picker moving, the window closing - flush
    /// through the same door.
    /// </summary>
    private void CommitEdits()
    {
        _saveTimer.Stop();

        if (_editBaseline is not { } before || IsDisposed)
        {
            _editBaseline = null;
            return;
        }

        _editBaseline = null;

        if (Selected is not { IsLegacy: false } selected)
        {
            return;
        }

        // Write onto the store's own copy of the day where there is one, so
        // a change made to it by something else (the phone, mostly) is the
        // starting point rather than whatever copy the picker happens to
        // hold. A day deleted since falls back to the picker's copy, as
        // before.
        var raceEvent = _events.Get(selected.Id) ?? selected;
        var liveBefore = System.Text.Json.JsonSerializer.Serialize(raceEvent);

        // Only the fields the person actually changed since the run began
        // are written, not every field on the page. The page's controls
        // still show the day as it was when the run began, so copying all
        // of them would quietly put back anything changed underneath in
        // the meantime: a marshal's phone adding a class or changing the
        // heat size while the director types a note was told "Saved." and
        // then reverted by the note's save, with nothing on the laptop to
        // say so. So the controls are written onto a copy of the baseline,
        // and only what differs between the two is carried onto the day.
        var baseline = System.Text.Json.JsonSerializer.Deserialize<RaceEvent>(before);
        var onScreen = System.Text.Json.JsonSerializer.Deserialize<RaceEvent>(before);

        if (baseline is null || onScreen is null)
        {
            // Cannot happen for a baseline this class serialized itself,
            // but if it ever did, the old whole-page write is the fallback.
            WriteFieldsTo(raceEvent);
        }
        else
        {
            WriteFieldsTo(onScreen);
            MergeChangedFields(raceEvent, baseline, onScreen);
        }

        if (System.Text.Json.JsonSerializer.Serialize(raceEvent) == liveBefore)
        {
            return;
        }

        // The undo puts back the day as it was just before THIS write, not
        // the baseline: nothing from this page writes between the two, so
        // they only differ when something else changed the day meanwhile,
        // and undoing the note must not undo the phone's change with it.
        CaptureUndo(raceEvent, liveBefore);

        _events.Upsert(raceEvent);

        // NOT RefreshAll: that rebuilds the picker and reloads every field
        // from the store, which would take the caret out of the box being
        // typed in. Only the parts that report the day are redrawn.
        RefreshStatus();
        ShowSavedNote();
    }

    /// <summary>
    /// Carries onto <paramref name="target"/> each field this tab writes
    /// whose value in <paramref name="onScreen"/> differs from
    /// <paramref name="baseline"/> - that is, each field the person changed
    /// since the edit run began. Every other field keeps the value
    /// <paramref name="target"/> has now, so a change made to the day by
    /// something else in the meantime survives the save.
    ///
    /// The field list matches WriteFieldsTo and SameSettings; a field added
    /// to those belongs here too.
    /// </summary>
    internal static void MergeChangedFields(RaceEvent target, RaceEvent baseline, RaceEvent onScreen)
    {
        if (onScreen.Name != baseline.Name)
        {
            target.Name = onScreen.Name;
        }

        if (onScreen.Notes != baseline.Notes)
        {
            target.Notes = onScreen.Notes;
        }

        if (onScreen.VenueName != baseline.VenueName)
        {
            target.VenueName = onScreen.VenueName;
        }

        if (onScreen.TrackLayoutName != baseline.TrackLayoutName)
        {
            target.TrackLayoutName = onScreen.TrackLayoutName;
        }

        if (onScreen.Shape != baseline.Shape)
        {
            target.Shape = onScreen.Shape;
        }

        if (onScreen.QualifyingRounds != baseline.QualifyingRounds)
        {
            target.QualifyingRounds = onScreen.QualifyingRounds;
        }

        if (onScreen.FirstRoundSeeding != baseline.FirstRoundSeeding)
        {
            target.FirstRoundSeeding = onScreen.FirstRoundSeeding;
        }

        if (onScreen.ShareHeatsUnder != baseline.ShareHeatsUnder)
        {
            target.ShareHeatsUnder = onScreen.ShareHeatsUnder;
        }

        if (onScreen.EntryFee != baseline.EntryFee)
        {
            target.EntryFee = onScreen.EntryFee;
        }

        if (onScreen.ExtraClassFee != baseline.ExtraClassFee)
        {
            target.ExtraClassFee = onScreen.ExtraClassFee;
        }

        if (onScreen.FirstRaceLocalTime != baseline.FirstRaceLocalTime)
        {
            target.FirstRaceLocalTime = onScreen.FirstRaceLocalTime;
        }

        if (onScreen.OutByLocalTime != baseline.OutByLocalTime)
        {
            target.OutByLocalTime = onScreen.OutByLocalTime;
        }

        if (onScreen.EntriesCloseLocalTime != baseline.EntriesCloseLocalTime)
        {
            target.EntriesCloseLocalTime = onScreen.EntriesCloseLocalTime;
        }

        // 0 means the day never set its own heat size; the page then shows
        // the one it would build with (RaceEventStore.HeatSizeFor) without
        // writing it. As with AutoAdvanceRounds below: that displayed value
        // is written as the day takes ownership of it, but never over a size
        // something else (the phone) has written onto the day since.
        if (onScreen.DriversPerHeat != baseline.DriversPerHeat
            && (baseline.DriversPerHeat != 0 || target.DriversPerHeat == 0))
        {
            target.DriversPerHeat = onScreen.DriversPerHeat;
        }

        if (onScreen.CheckedInDriversOnly != baseline.CheckedInDriversOnly)
        {
            target.CheckedInDriversOnly = onScreen.CheckedInDriversOnly;
        }

        if (onScreen.BetweenRacesPreset != baseline.BetweenRacesPreset)
        {
            target.BetweenRacesPreset = onScreen.BetweenRacesPreset;
        }

        // A day that had never set this (null, following the club-wide
        // setting) reads as the club default on the page, so every save
        // used to write that default onto it. That still happens - it is
        // how the day comes to own the setting - but not over a choice
        // something else has written onto the day since: an explicit on or
        // off outranks a default the page merely displayed.
        if (onScreen.AutoAdvanceRounds != baseline.AutoAdvanceRounds
            && (baseline.AutoAdvanceRounds is not null || target.AutoAdvanceRounds is null))
        {
            target.AutoAdvanceRounds = onScreen.AutoAdvanceRounds;
        }

        // Scope and class go together: they are one choice in one list.
        if (onScreen.AutoAdvanceScope != baseline.AutoAdvanceScope
            || onScreen.AutoAdvanceClassName != baseline.AutoAdvanceClassName)
        {
            target.AutoAdvanceScope = onScreen.AutoAdvanceScope;
            target.AutoAdvanceClassName = onScreen.AutoAdvanceClassName;
        }

        // As a set, ignoring case: the page lists the ticks in the club's
        // class order, so a day whose classes were saved in another order
        // (from the phone) read as changed on every save and reverted any
        // class change made meanwhile.
        if (!(onScreen.ClassNames ?? new List<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase)
                .SetEquals(baseline.ClassNames ?? new List<string>()))
        {
            target.ClassNames = (onScreen.ClassNames ?? new List<string>()).ToList();
        }

        if (onScreen.ChampionshipId != baseline.ChampionshipId)
        {
            target.ChampionshipId = onScreen.ChampionshipId;
        }
    }

    /// <summary>Flushes a pending edit before something reloads the fields underneath it.</summary>
    private void FlushPendingEdits() => CommitEdits();

    /// <summary>
    /// Stops both timers on the way out.
    ///
    /// A Forms timer holds a reference to its handler and goes on ticking
    /// into a disposed control otherwise - which for the save timer would
    /// mean a write landing after the page it came from had gone.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _saveTimer.Stop();
            _saveTimer.Dispose();

            _savedNoteTimer.Stop();
            _savedNoteTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    private void ShowSavedNote()
    {
        _lblSaved.Text = "Saved";
        _lblSaved.Visible = true;

        _savedNoteTimer.Stop();
        _savedNoteTimer.Start();
    }

    /// <summary>Reloads the picker and the fields. Called when the tab is opened.</summary>
    public void RefreshAll()
    {
        // Anything typed and not yet written goes in first. RefreshAll runs
        // on every return to this tab and reloads every field from the
        // store, so without this a pause that had not elapsed would be
        // overwritten by the value it was about to replace.
        FlushPendingEdits();

        // Which day to show after the rebuild. Normally the one already on
        // show, so a refresh never yanks the picker off a day the person
        // chose to look at. But when the CURRENT day has changed since the
        // last refresh - New Race Day here, a new day from the wizard or the
        // phone, Make Current, or an undo of any of those - the page follows
        // it. Otherwise the picker stayed on last week's day while the rest
        // of the app had moved on, and New Race Day then put the caret in
        // the OLD day's name box, so typing tonight's name renamed last
        // week's day instead.
        //
        // Only a real day is followed. Closing or deleting the current day
        // leaves none current, which reads as the legacy holder, and that is
        // never something to jump to (see the picker order below).
        var currentId = _events.CurrentId;
        var currentChanged = _lastSeenCurrentId is not null
            && !string.Equals(currentId, _lastSeenCurrentId, StringComparison.Ordinal)
            && !string.Equals(currentId, RaceEventStore.LegacyEventId, StringComparison.Ordinal);

        _lastSeenCurrentId = currentId;

        var keep = currentChanged ? currentId : Selected?.Id ?? currentId;

        _loading = true;

        try
        {
            _cmbEvent.Items.Clear();

            // Real events newest first, the legacy holder always last -
            // present so its records can be looked at, never the default.
            foreach (var raceEvent in _events.Real)
            {
                _cmbEvent.Items.Add(new Choice(raceEvent));
            }

            _cmbEvent.Items.Add(new Choice(_events.EnsureLegacy()));

            var index = _cmbEvent.Items
                .Cast<Choice>()
                .ToList()
                .FindIndex(c => string.Equals(c.Event.Id, keep, StringComparison.Ordinal));

            _cmbEvent.SelectedIndex = index >= 0 ? index : 0;

            // The championship list follows the seasons that exist NOW.
            _cmbChampionship.Items.Clear();
            _cmbChampionship.Items.Add("(standalone - no championship)");

            foreach (var season in _seasons.All)
            {
                _cmbChampionship.Items.Add(new SeasonChoice(season));
            }

            _cmbVenue.Items.Clear();

            foreach (var venue in _venues.All)
            {
                _cmbVenue.Items.Add(venue.Name);
            }

            _cmbLayout.Items.Clear();

            foreach (var layout in _layouts.All)
            {
                _cmbLayout.Items.Add(layout.Name);
            }

            // Rebuilt from the classes that exist NOW, so a class added or
            // renamed on another page shows up here the next time the tab
            // is opened rather than at the next launch.
            _lstClasses.Items.Clear();

            foreach (var raceClass in _raceClasses.All)
            {
                _lstClasses.Items.Add(raceClass.Name);
            }
        }
        finally
        {
            _loading = false;
        }

        LoadSelected();

        // Every list was rebuilt from the stores - a longer venue or
        // championship name, a day added.
        RefitFields();
    }

    private sealed record SeasonChoice(Season Season)
    {
        public override string ToString() => Season.Name;
    }

    /// <summary>One way of choosing the next race. Scope and class travel together - see RaceEvent.AutoAdvanceClassOr.</summary>
    private sealed record ScopeChoice(string Scope, string ClassName, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>One entry in Between-race talk: a preset's key (empty for the club's own settings) and its name.</summary>
    private sealed record GapPresetChoice(string Key, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>Picks the day's preset in Between-race talk - the club's settings for one it does not know.</summary>
    private void ShowGapPreset(string? key)
    {
        for (var i = 0; i < _cmbGapPreset.Items.Count; i++)
        {
            if (_cmbGapPreset.Items[i] is GapPresetChoice choice
                && string.Equals(choice.Key, key ?? string.Empty, StringComparison.Ordinal))
            {
                _cmbGapPreset.SelectedIndex = i;
                return;
            }
        }

        _cmbGapPreset.SelectedIndex = 0;
    }

    /// <summary>
    /// "Next race is:" open only while the next race starts by itself, on a
    /// race day that can be changed (<paramref name="editable"/>) - and its caption goes quiet with it, so
    /// an unticked setting does not read as a live choice.
    /// </summary>
    private void FollowAutoAdvance(bool editable)
    {
        _cmbAutoAdvanceScope.Enabled = editable && _chkAutoAdvance.Checked;

        if (_lblAutoAdvanceScope is not null)
        {
            _lblAutoAdvanceScope.ForeColor = _cmbAutoAdvanceScope.Enabled ? AppTheme.TextDark : AppTheme.TextMuted;
        }
    }

    /// <summary>
    /// Fills the "Next race is:" list and picks the day's own answer.
    ///
    /// Rebuilt each time rather than held, because the club's classes can
    /// change between one day and the next - and a day pinned to a class
    /// that has since been deleted must not silently re-pin itself to
    /// whichever class is now first. It falls back to following the
    /// drivers, which is the safe answer and the one the old strip gave.
    /// </summary>
    private void LoadAutoAdvanceScope(string scope, string className)
    {
        _cmbAutoAdvanceScope.Items.Clear();
        _cmbAutoAdvanceScope.Items.Add(new ScopeChoice("drivers", string.Empty, "the class that was racing"));
        _cmbAutoAdvanceScope.Items.Add(new ScopeChoice("any", string.Empty, "any class, in program order"));

        foreach (var raceClass in _raceClasses.All)
        {
            _cmbAutoAdvanceScope.Items.Add(new ScopeChoice("class", raceClass.Name, $"only \"{raceClass.Name}\""));
        }

        for (var i = 0; i < _cmbAutoAdvanceScope.Items.Count; i++)
        {
            if (_cmbAutoAdvanceScope.Items[i] is ScopeChoice choice
                && string.Equals(choice.Scope, scope, StringComparison.Ordinal)
                && string.Equals(choice.ClassName, className, StringComparison.OrdinalIgnoreCase))
            {
                _cmbAutoAdvanceScope.SelectedIndex = i;
                return;
            }
        }

        _cmbAutoAdvanceScope.SelectedIndex = 0;
    }

    private RaceEvent? Selected => (_cmbEvent.SelectedItem as Choice)?.Event;

    private void LoadSelected()
    {
        if (Selected is not { } raceEvent)
        {
            return;
        }

        // Filling the fields is not editing them.
        //
        // Every assignment below raises the control's own change event,
        // and those now write to the day. Without this, merely LOOKING at
        // a race day would save over it - and the heat-size box in
        // particular, which shows 8 for a day that has never been given
        // one, would write that 8 onto every day anybody opened.
        _loading = true;

        try
        {
            LoadFields(raceEvent);
        }
        finally
        {
            _loading = false;
        }

        RefreshStatus();
    }

    /// <summary>The shape the picker is showing. Never null - the combo is a DropDownList with a selection from the moment it is built.</summary>
    private RaceDayShape SelectedShape =>
        _cmbShape.SelectedItem is ShapeChoice choice ? choice.Shape : RaceDayShape.RaceDay;

    /// <summary>
    /// Puts the captions and hints that depend on the day's shape in line
    /// with the picker.
    ///
    /// Only wording: no control moves. Both variants of the caption are in
    /// the caption column's measurement (FieldGrid.LineUp in BuildLayout),
    /// so the column is the same width either way and nothing shifts as the
    /// box is used; the two hints are as tall as their longer wording
    /// (SteadyLabel) for the same reason.
    /// </summary>
    private void ApplyShapeWording()
    {
        var clubNight = SelectedShape == RaceDayShape.ClubNight;

        if (_lblQualRounds is not null)
        {
            _lblQualRounds.Text = clubNight ? RacesPerDriverCaption : HeatsPerDriverCaption;
        }

        // The check-in rule is named here rather than left to the tick box
        // below, because it is set FROM this choice when the day is started
        // and somebody reading the two options needs to know that.
        _lblShapeHint.Text = clubNight ? ClubNightShapeHint : RaceDayShapeHint;
        _lblClassHint.Text = clubNight ? ClubNightClassHint : RaceDayClassHint;
    }

    private void LoadFields(RaceEvent raceEvent)
    {
        // Before the fields it words: LoadFields runs with _loading set, so
        // the SelectedIndexChanged below writes nothing, and this leaves the
        // captions right for the day just picked.
        for (var i = 0; i < _cmbShape.Items.Count; i++)
        {
            if (_cmbShape.Items[i] is ShapeChoice choice && choice.Shape == raceEvent.Shape)
            {
                _cmbShape.SelectedIndex = i;
                break;
            }
        }

        ApplyShapeWording();

        _txtName.Text = raceEvent.Name;
        _txtNotes.Text = raceEvent.Notes;
        _cmbVenue.Text = raceEvent.VenueName;
        _cmbLayout.Text = raceEvent.TrackLayoutName;
        _numQualRounds.Value = Math.Clamp(raceEvent.QualifyingRounds, 1, 10);

        // Ten o'clock shown, not written, when the day has no time on it -
        // the same idea as the heat size below. It becomes real only when
        // the tick goes on.
        _chkFirstRace.Checked = raceEvent.FirstRaceLocalTime is not null;
        _dtpFirstRace.Value = raceEvent.FirstRaceLocalTime ?? TimeSpan.FromHours(10);
        _chkOutBy.Checked = raceEvent.OutByLocalTime is not null;
        _dtpOutBy.Value = raceEvent.OutByLocalTime ?? TimeSpan.FromHours(22);
        _chkEntriesClose.Checked = raceEvent.EntriesCloseLocalTime is not null;
        _dtpEntriesClose.Value = raceEvent.EntriesCloseLocalTime ?? TimeSpan.FromHours(18.5);
        _lblBreaks.Text = NightBreaks.Summary(raceEvent.Breaks, raceEvent.IsClubNight);
        _lblClassOrder.Text = ClassRunningOrder.Summary(ClassesTonight(raceEvent), raceEvent.ClassRunningOrder, raceEvent.RotateClassOrder);
        ShowNextNight();

        // Zero means "whatever was used last" - the number the program builds
        // with (RaceEventStore.HeatSizeFor), so that is what the box shows. It
        // showed 8 while the program built with a remembered 6, until the box
        // was changed (docs check, 30 Sep). Not written here: this runs with
        // _loading set. It is saved onto the day with the next change to any
        // of the day's fields - the same number, so nothing moves.
        _numHeatSize.Value = _events.HeatSizeFor(raceEvent);
        _cmbFirstRound.SelectedIndex = Math.Clamp((int)raceEvent.FirstRoundSeeding, 0, _cmbFirstRound.Items.Count - 1);
        _numShareUnder.Value = Math.Clamp(raceEvent.ShareHeatsUnder, (int)_numShareUnder.Minimum, (int)_numShareUnder.Maximum);
        _numEntryFee.Value = Math.Clamp(raceEvent.EntryFee, _numEntryFee.Minimum, _numEntryFee.Maximum);
        _numExtraFee.Value = Math.Clamp(raceEvent.ExtraClassFee, _numExtraFee.Minimum, _numExtraFee.Maximum);
        _chkCheckedInOnly.Checked = raceEvent.CheckedInDriversOnly;
        ShowGapPreset(raceEvent.BetweenRacesPreset);

        // The club-wide setting is the answer for a day that never said -
        // which is every day written before this moved here. Shown, not
        // written: this runs with _loading set, so the day only gains its
        // own answer when somebody actually touches the box.
        _chkAutoAdvance.Checked = raceEvent.AutoAdvanceOn(_settings?.AutoAdvanceRounds ?? true);

        LoadAutoAdvanceScope(
            raceEvent.AutoAdvanceScopeOr(_settings?.AutoAdvanceScope ?? "drivers"),
            raceEvent.AutoAdvanceClassOr(_settings?.AutoAdvanceClassName ?? string.Empty));

        // Rebuilt from the classes that exist now, so measured again.
        _scopeWidth = null;
        SizeFields();

        for (var i = 0; i < _lstClasses.Items.Count; i++)
        {
            var name = _lstClasses.Items[i] as string ?? string.Empty;

            _lstClasses.SetItemChecked(i,
                raceEvent.ClassNames.Contains(name, StringComparer.OrdinalIgnoreCase));
        }

        var championshipIndex = 0;

        for (var i = 1; i < _cmbChampionship.Items.Count; i++)
        {
            if (_cmbChampionship.Items[i] is SeasonChoice choice
                && string.Equals(choice.Season.Id, raceEvent.ChampionshipId, StringComparison.Ordinal))
            {
                championshipIndex = i;
                break;
            }
        }

        _cmbChampionship.SelectedIndex = championshipIndex;

        // The legacy event is a filing cabinet, not a race day - it can be
        // looked at and pointed at, never reshaped.
        var editable = !raceEvent.IsLegacy;

        _txtName.Enabled = editable;
        _cmbShape.Enabled = editable;
        _chkFirstRace.Enabled = editable;
        _dtpFirstRace.Enabled = editable && _chkFirstRace.Checked;
        _chkOutBy.Enabled = editable;
        _dtpOutBy.Enabled = editable && _chkOutBy.Checked;
        _chkEntriesClose.Enabled = editable;
        _dtpEntriesClose.Enabled = editable && _chkEntriesClose.Checked;
        _btnBreaks.Enabled = editable;
        _btnClassOrder.Enabled = editable;
        _txtNotes.Enabled = editable;
        _cmbVenue.Enabled = editable;
        _cmbLayout.Enabled = editable;
        _cmbChampionship.Enabled = editable;
        _numQualRounds.Enabled = editable;
        _lstClasses.Enabled = editable;
        _numHeatSize.Enabled = editable;
        _cmbFirstRound.Enabled = editable;
        _numShareUnder.Enabled = editable;
        _numEntryFee.Enabled = editable;
        _numExtraFee.Enabled = editable;
        _chkCheckedInOnly.Enabled = editable;
        _cmbGapPreset.Enabled = editable;
        _chkAutoAdvance.Enabled = editable;
        FollowAutoAdvance(editable);
        _btnCloseDay.Enabled = editable;

        _btnCloseDay.Text = raceEvent.IsOpen ? "Close Day" : "Reopen Day";

        // The legacy holder is not a race day and cannot be deleted - it is
        // where everything from before race days lives. Nor can anything be
        // deleted without the two stores the reach-through needs.
        _btnDeleteDay.Enabled = editable && _history is not null;
    }

    /// <summary>
    /// What the selected day already holds.
    ///
    /// Its own method because a save redraws this and nothing else -
    /// reloading the whole tab after every keystroke would take the caret
    /// out of the box being typed in.
    /// </summary>
    private void RefreshStatus()
    {
        if (Selected is not { } raceEvent)
        {
            return;
        }

        var checkedIn = _checkIns.CheckedInAt(raceEvent.Id).Count;

        var roundCount = raceEvent.RoundIds.Count;
        var recorded = raceEvent.RoundIds.Count(id => _rounds.Get(id)?.IsRecorded == true);

        var scheduleLines = _schedule.Everything.Count(e =>
            string.Equals(e.EventId, raceEvent.Id, StringComparison.Ordinal));

        var current = string.Equals(raceEvent.Id, _events.CurrentId, StringComparison.Ordinal)
            ? CurrentDaySentence
            : string.Empty;

        var classCount = raceEvent.ClassNames.Count;

        _lblStatus.Text = $"{current}{checkedIn} checked in · "
            + $"{Plural.Of(classCount, "class", "classes")} racing · "
            + $"{Plural.Of(roundCount, "round")} in the program, {recorded} recorded · "
            + $"{Plural.Of(scheduleLines, "timetable line")}";
    }

    /// <summary>
    /// Starts a race day from somewhere else in the app - the Today tab's
    /// checklist, or the banner on Who's Racing.
    ///
    /// Routed through this tab's own command rather than copied, so the
    /// undo entry, the refusal to delete a day that has since been used,
    /// and the naming are all the one implementation. Two ways to start a
    /// race day that behaved differently is exactly the kind of thing that
    /// makes a second one appear at eight o'clock on a Saturday.
    /// </summary>
    /// <summary>
    /// Starts a day from somewhere else - the Today checklist, the banner on
    /// Who's Racing. FALSE when the dialog was cancelled, so the caller does
    /// not march the window over to The Day for a day nobody made.
    /// </summary>
    public bool StartNewDayFromElsewhere() => StartNewDay();

    /// <summary>
    /// Sets which classes are racing today, from somewhere else - the
    /// Today tab's checklist.
    ///
    /// Routed through this tab so the tick list, the undo entry and the
    /// write are the one implementation. Today shows the picker; this
    /// saves it.
    /// </summary>
    public void SetRacingClassesFromElsewhere(IReadOnlyList<string> classNames) =>
        ChangeDayFromElsewhere(day => day.ClassNames = classNames.Where(n => !string.IsNullOrWhiteSpace(n)).ToList());

    /// <summary>
    /// Changes the current day's own settings from somewhere else - the
    /// setup wizard.
    ///
    /// Routed through this tab for the reason SetRacingClassesFromElsewhere
    /// is: one writer, one undo entry, and this page redraws rather than
    /// sitting on fields it no longer agrees with. A wizard writing to the
    /// event store itself would be a second way to change a race day, and
    /// the two would drift the first time one of them learned a new field.
    ///
    /// A no-op when nothing actually changed, so stepping back and forward
    /// through a wizard does not fill the undo stack with nothing.
    /// </summary>
    public void ChangeDayFromElsewhere(Action<RaceEvent> change)
    {
        if (_events.Active is not { IsLegacy: false } day)
        {
            return;
        }

        var before = System.Text.Json.JsonSerializer.Serialize(day);

        change(day);

        if (System.Text.Json.JsonSerializer.Serialize(day) == before)
        {
            return;
        }

        CaptureUndo(day, before);
        _events.Upsert(day);

        if (!IsDisposed)
        {
            RefreshAll();
        }
    }

    /// <summary>
    /// Starts a race day when the shape and the classes have already been
    /// asked for somewhere else.
    ///
    /// The setup wizard asks both itself, so opening NewRaceDayForm on top
    /// of it would be the same two questions twice. Everything else is the
    /// same path - StartDayFrom, carry-forward and undo entry included.
    /// </summary>
    /// <summary>
    /// Starts tonight like the last race day (12 Nov 2026, setup plan B1):
    /// its kind of night and its classes, and everything else a new day
    /// carries forward. The same path as New Race Day, undo entry included.
    /// False when there is no last day with classes to go by.
    /// </summary>
    public bool StartLikeLastDay()
    {
        if (LastRealDay() is not { ClassNames.Count: > 0 } last)
        {
            return false;
        }

        StartDayFrom(last, last.Shape, last.ClassNames.ToList());
        return true;
    }

    public bool StartNewDayFromWizard(RaceDayShape shape, IReadOnlyList<string> classNames)
    {
        StartDayFrom(LastRealDay(), shape, classNames);
        return true;
    }

    /// <summary>
    /// The most recently started real race day, or null on a club's first.
    /// What a new day copies its settings from - see NewRaceDayForm.
    /// </summary>
    private RaceEvent? LastRealDay() =>
        _events.Real
            .OrderByDescending(e => e.StartedUtc)
            .FirstOrDefault();

    /// <summary>Asks the two questions and makes the day. False when it was cancelled.</summary>
    private bool StartNewDay()
    {
        var previous = LastRealDay();

        using var ask = new NewRaceDayForm(
            _raceClasses.All.Select(c => c.Name).ToList(),
            previous?.ClassNames ?? new List<string>(),
            previous?.Shape ?? RaceDayShape.RaceDay);

        if (ask.ShowDialog(this) != DialogResult.OK)
        {
            return false;
        }

        StartDayFrom(previous, ask.Shape, ask.ClassNames);
        return true;
    }

    /// <summary>
    /// Makes the day, with everything the dialog did not ask about carried
    /// forward from <paramref name="previous"/>.
    ///
    /// Split out so the undo below reads as one thing. The shape and the
    /// classes come from the person; the rest is what they had last time,
    /// because a club runs the same kind of night most weeks and retyping
    /// the venue every Tuesday is a toll, not a decision.
    /// </summary>
    private void StartDayFrom(RaceEvent? previous, RaceDayShape shape, IReadOnlyList<string> classNames)
    {
        // For the undo: which day was current, and the new one (filled in
        // below). It takes the new day away again - only while nothing has
        // been done with it - and makes the old one current, the way the
        // phone's New Race Day undo does.
        var events = _events;
        var checkIns = _checkIns;
        var schedule = _schedule;
        var previousId = events.Active?.Id;

        // Whether that day was open at the moment the new one was started.
        // A closed day being current is a normal state - it is how its
        // results are looked at, from the picker above or the Calendar - so
        // the undo must tell "closed since" apart from "already closed when
        // it was current". Only the first is refused; the second goes back
        // to being current and closed, exactly as it was, the way the Race
        // Days page's Switch undo does.
        var previousWasOpen = events.Active?.IsOpen ?? false;
        RaceEvent? started = null;

        _undo?.Capture($"New race day {DateTime.Now:d MMM yyyy}", () =>
        {
            if (started is null || events.Get(started.Id) is not { } live)
            {
                throw new InvalidOperationException("that race day has been deleted since. Nothing was changed.");
            }

            // Run sheet lines too, as the phone's undo checks: deleting the
            // day would leave them filed under a day nobody can see.
            if (live.RoundIds.Count > 0 || checkIns.CheckedInAt(live.Id).Count > 0
                || schedule.Everything.Any(e => string.Equals(e.EventId, live.Id, StringComparison.Ordinal)))
            {
                throw new InvalidOperationException(
                    $"\"{live.DisplayName}\" has rounds, check-ins or run sheet lines on it now, and taking it away would lose them. "
                    + "Close it here instead if it isn't wanted. Nothing was changed.");
            }

            var wasCurrent = string.Equals(events.Active?.Id, live.Id, StringComparison.Ordinal);

            events.Delete(live.Id);

            if (wasCurrent
                && (previousId is null
                    || events.Get(previousId) is { } dayBefore && (dayBefore.IsOpen || !previousWasOpen)))
            {
                events.SetActive(previousId);
            }

            RefreshAfterUndo();
        });

        var raceEvent = EventSpine.StartEvent(
            _events,
            EventSpine.DefaultDayName(DateTime.Now, shape),
            previous?.VenueName ?? string.Empty,
            previous?.TrackLayoutName ?? string.Empty,
            previous?.ChampionshipId ?? string.Empty,
            shape,
            classNames,

            // Zero means "keep the default", which is right on a first day.
            previous?.QualifyingRounds ?? 0,
            previous?.DriversPerHeat ?? 0);

        started = raceEvent;

        RefreshAll();

        // Straight into naming it, because "Race Day 19 Sep" is a
        // placeholder, not a name. Everything else on the page is already
        // filled in, so this is the only box worth landing in.
        _txtName.Focus();
        _txtName.SelectAll();
    }

    /// <summary>
    /// Copies every field onto the day. Returns true when something
    /// actually differed - a save that changes nothing earns no undo entry
    /// and no "Saved".
    /// </summary>
    private bool WriteFieldsTo(RaceEvent raceEvent)
    {
        var before = System.Text.Json.JsonSerializer.Serialize(raceEvent);

        raceEvent.Name = _txtName.Text.Trim();
        raceEvent.Notes = _txtNotes.Text.Trim();
        raceEvent.VenueName = _cmbVenue.Text.Trim();
        raceEvent.TrackLayoutName = _cmbLayout.Text.Trim();
        raceEvent.Shape = SelectedShape;
        raceEvent.QualifyingRounds = (int)_numQualRounds.Value;

        raceEvent.FirstRaceLocalTime = _chkFirstRace.Checked ? _dtpFirstRace.Value : null;
        raceEvent.OutByLocalTime = _chkOutBy.Checked ? _dtpOutBy.Value : null;
        raceEvent.EntriesCloseLocalTime = _chkEntriesClose.Checked ? _dtpEntriesClose.Value : null;
        raceEvent.DriversPerHeat = (int)_numHeatSize.Value;
        raceEvent.FirstRoundSeeding = (FirstRoundSeeding)Math.Max(0, _cmbFirstRound.SelectedIndex);
        raceEvent.ShareHeatsUnder = (int)_numShareUnder.Value;
        raceEvent.EntryFee = _numEntryFee.Value;
        raceEvent.ExtraClassFee = _numExtraFee.Value;

        // Deliberately NOT re-derived from the shape here. It is set from the
        // shape once, when the day is started (EventSpine.StartEvent); after
        // that it is the director's to set, and a club night that does run a
        // sign-on desk must be able to say so without the next keystroke
        // undoing it.
        raceEvent.CheckedInDriversOnly = _chkCheckedInOnly.Checked;
        raceEvent.BetweenRacesPreset = _cmbGapPreset.SelectedItem is GapPresetChoice gap ? gap.Key : string.Empty;

        // Written onto the DAY, which is what takes it out of the club-wide
        // setting it used to be. Once written it is never null again for
        // this day, so a club that turns it off for one night keeps it on
        // for the next - see RaceEvent.AutoAdvanceOn.
        raceEvent.AutoAdvanceRounds = _chkAutoAdvance.Checked;

        if (_cmbAutoAdvanceScope.SelectedItem is ScopeChoice scope)
        {
            raceEvent.AutoAdvanceScope = scope.Scope;
            raceEvent.AutoAdvanceClassName = scope.ClassName;
        }

        raceEvent.ClassNames = _lstClasses.CheckedItems
            .Cast<object>()
            .Select(item => item as string ?? string.Empty)
            .Where(name => name.Length > 0)
            .ToList();
        raceEvent.ChampionshipId = _cmbChampionship.SelectedItem is SeasonChoice choice
            ? choice.Season.Id
            : string.Empty;

        return System.Text.Json.JsonSerializer.Serialize(raceEvent) != before;
    }

    /// <summary>
    /// One undo entry for a run of edits, putting the day back to
    /// <paramref name="before"/> - the state it was in when the run began,
    /// not when its last keystroke landed.
    /// </summary>
    private void CaptureUndo(RaceEvent raceEvent, string before)
    {
        var after = System.Text.Json.JsonSerializer.Serialize(raceEvent);

        {
            var events = _events;
            var dayId = raceEvent.Id;

            _undo?.Capture($"Change race day '{raceEvent.DisplayName}'", () =>
            {
                // Only while the day's settings are still as this save left
                // them. Its rounds and whether it is open are not this
                // save's, and are left as they are now.
                if (events.Get(dayId) is not { } current)
                {
                    throw new InvalidOperationException("that race day has been deleted since. Nothing was changed.");
                }

                if (System.Text.Json.JsonSerializer.Deserialize<RaceEvent>(after) is not { } asSaved
                    || !SameSettings(current, asSaved))
                {
                    throw new InvalidOperationException(
                        $"\"{current.DisplayName}\" has been changed again since, and undoing this now would undo that too. Nothing was changed.");
                }

                if (System.Text.Json.JsonSerializer.Deserialize<RaceEvent>(before) is { } old)
                {
                    current.Name = old.Name;
                    current.Notes = old.Notes;
                    current.VenueName = old.VenueName;
                    current.TrackLayoutName = old.TrackLayoutName;
                    current.Shape = old.Shape;
                    current.FirstRaceLocalTime = old.FirstRaceLocalTime;
                    current.OutByLocalTime = old.OutByLocalTime;
                    current.EntriesCloseLocalTime = old.EntriesCloseLocalTime;
                    current.QualifyingRounds = old.QualifyingRounds;
                    current.DriversPerHeat = old.DriversPerHeat;
                    current.FirstRoundSeeding = old.FirstRoundSeeding;
                    current.ShareHeatsUnder = old.ShareHeatsUnder;
                    current.EntryFee = old.EntryFee;
                    current.ExtraClassFee = old.ExtraClassFee;
                    current.CheckedInDriversOnly = old.CheckedInDriversOnly;
                    current.BetweenRacesPreset = old.BetweenRacesPreset;
                    current.ClassNames = (old.ClassNames ?? new List<string>()).ToList();
                    current.ChampionshipId = old.ChampionshipId;

                    // WriteFieldsTo writes these three too, so an undo that
                    // left them out would "succeed" after a change to "Start
                    // the next race automatically" and change nothing. The
                    // null is put back as well: a day that was following the
                    // club-wide setting before this save follows it again.
                    current.AutoAdvanceRounds = old.AutoAdvanceRounds;
                    current.AutoAdvanceScope = old.AutoAdvanceScope;
                    current.AutoAdvanceClassName = old.AutoAdvanceClassName;
                    events.Upsert(current);
                }

                RefreshAfterUndo();
            });
        }
    }

    /// <summary>Whether two copies of a day agree on everything this tab writes.</summary>
    private static bool SameSettings(RaceEvent a, RaceEvent b) =>
        a.Name == b.Name
        && a.Notes == b.Notes
        && a.VenueName == b.VenueName
        && a.TrackLayoutName == b.TrackLayoutName
        && a.Shape == b.Shape
        && a.FirstRaceLocalTime == b.FirstRaceLocalTime
        && a.OutByLocalTime == b.OutByLocalTime
        && a.EntriesCloseLocalTime == b.EntriesCloseLocalTime
        && a.QualifyingRounds == b.QualifyingRounds
        && a.DriversPerHeat == b.DriversPerHeat
        && a.FirstRoundSeeding == b.FirstRoundSeeding
        && a.ShareHeatsUnder == b.ShareHeatsUnder
        && a.EntryFee == b.EntryFee
        && a.ExtraClassFee == b.ExtraClassFee
        && a.CheckedInDriversOnly == b.CheckedInDriversOnly
        && a.BetweenRacesPreset == b.BetweenRacesPreset
        && (a.ClassNames ?? new List<string>()).SequenceEqual(b.ClassNames ?? new List<string>(), StringComparer.Ordinal)
        && a.ChampionshipId == b.ChampionshipId
        && a.AutoAdvanceRounds == b.AutoAdvanceRounds
        && a.AutoAdvanceScope == b.AutoAdvanceScope
        && a.AutoAdvanceClassName == b.AutoAdvanceClassName;

    /// <summary>Redraws after an undo, if this tab is still there - the undo itself works on the store.</summary>
    private void RefreshAfterUndo()
    {
        if (!IsDisposed)
        {
            RefreshAll();
        }
    }

    private void MakeCurrent()
    {
        if (Selected is { } raceEvent)
        {
            var events = _events;
            var previousId = events.Active?.Id;
            var newId = raceEvent.IsLegacy ? null : raceEvent.Id;

            // Whether the day being left was open at the moment of the
            // switch. The picker lists closed days too, and picking one is
            // how its results are looked at, so the day being left may well
            // have been closed already. The undo goes back to such a day;
            // without this it skipped it, yet the entry was still spent and
            // reported as undone while the new day stayed current.
            var previousWasOpen = events.Active?.IsOpen ?? false;

            if (previousId != newId)
            {
                _undo?.Capture($"Make '{raceEvent.DisplayName}' the current race day", () =>
                {
                    // SetActive, so check-in, tech and the run sheet follow.
                    // Not onto a day closed or deleted since, and not once
                    // another day has been made current since - that later
                    // choice stands. A day that was closed already when it
                    // was current goes back to being current and closed;
                    // making it current never reopens it.
                    if (events.Active?.Id == newId
                        && (previousId is null
                            || events.Get(previousId) is { } dayBefore && (dayBefore.IsOpen || !previousWasOpen)))
                    {
                        events.SetActive(previousId);
                    }

                    RefreshAfterUndo();
                });
            }

            // The legacy event is a legitimate thing to point at - that is
            // how pre-event history gets looked at - and SetActive(null)
            // is how "current" falls back to it.
            _events.SetActive(newId);
            RefreshAll();
        }
    }

    /// <summary>
    /// Deletes the day in the picker. The asking and the doing are in
    /// RaceDayDeleter, which the Calendar calls too - one delete, whichever
    /// page it is reached from.
    /// </summary>
    private void DeleteDay()
    {
        if (Selected is not { IsLegacy: false } day || _history is null)
        {
            return;
        }

        RaceDayDeleter.Ask(this, day, _events, _rounds, _history, _seasons, _layouts, _undo, () =>
        {
            if (!IsDisposed)
            {
                RefreshAll();
            }
        });
    }


    /// <summary>
    /// What a volunteer is asked before a day is closed.
    ///
    /// It says what closing does rather than asking whether they are sure,
    /// because what it does is not obvious from the button and not obvious
    /// from the hint under it either: a closed day is no longer current, so
    /// finished races file against no round, check-in and the run sheet drop
    /// onto the legacy day, and the standings and Seed Mains stop seeing
    /// tonight's heats. The count of rounds still to run is in it because
    /// that is the number that says "this is not the end of the night".
    ///
    /// Pure, so the wording can be tested without the page.
    /// </summary>
    internal static string CloseDayQuestion(string dayName, int roundsLeftToRun)
    {
        var left = roundsLeftToRun == 0
            ? string.Empty
            : $"{Plural.Of(roundsLeftToRun, "round")} on the program {(roundsLeftToRun == 1 ? "has" : "have")} not been run yet.\r\n\r\n";

        return $"Close \"{dayName}\"?\r\n\r\n"
            + left
            + "Nothing new joins a closed day: races run after this file against no round, check-in and the run "
            + "sheet drop onto the legacy day, and tonight's heats stop counting toward the standings and Seed "
            + "Mains. Recorded results stay exactly where they are.\r\n\r\n"
            + "Reopen Day puts it back.";
    }

    /// <summary>
    /// Everything that stands between a Close Day button and the close: the
    /// on-track refusal, then the question with No as the default. True when
    /// the day may be closed.
    ///
    /// Shared with the Calendar's Close Day (EventsControl), which used to
    /// ask its own one-line question with Yes as the default and no look at
    /// the track at all - the same close, from a second button, with neither
    /// of the guards this one had been given.
    /// </summary>
    internal static bool AskToCloseDay(IWin32Window owner, RaceEvent raceEvent, RoundDefinitionStore rounds)
    {
        // Closing used to go straight from the Click handler into
        // RaceEventStore.Close - no question and no look at the track,
        // from a button that sits beside Delete Day, which refuses
        // outright while a round is on track for a strictly smaller
        // consequence. One press at 7pm and automatic round picking
        // stopped for the night, every later check-in landed on the
        // legacy day, and tonight's heats dropped out of the standings
        // and out of Seed Mains.
        //
        // The same guard the delete uses, through the pure half of it, so
        // the two cannot disagree about what "on track" means - see
        // RaceDayDeleter.DayWithRoundOnTrack. Refused rather than asked,
        // for its reason: there is no answer that makes it safe, and the
        // day can close as soon as that race has been saved.
        if (RaceDayDeleter.DayWithRoundOnTrack(new[] { raceEvent }, RaceDayDeleter.RoundOnTrackId?.Invoke()) is { } busy)
        {
            AppDialog.Show(owner,
                $"\"{busy.DisplayName}\" has a round on track - its result would have nothing to be saved against. "
                + "Close the day when that race has finished and been saved.",
                "Round on track", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        // Rounds on the day that have not been run, counted the same way
        // the status line counts them - see RefreshStatus.
        var leftToRun = raceEvent.RoundIds.Count(id => rounds.Get(id) is { IsRecorded: false });

        // No is the default: a stray Enter on this page must not end the
        // night. See CloseDayQuestion for what it says and why.
        return AppDialog.Show(owner,
                CloseDayQuestion(raceEvent.DisplayName, leftToRun),
                "Close race day",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }

    private void CloseOrReopen()
    {
        if (Selected is not { IsLegacy: false } raceEvent)
        {
            return;
        }

        var events = _events;
        var dayId = raceEvent.Id;

        if (raceEvent.IsOpen)
        {
            // The on-track refusal and the No-default question - shared with
            // the Calendar's Close Day, see AskToCloseDay.
            if (!AskToCloseDay(this, raceEvent, _rounds))
            {
                return;
            }

            // Closing the current day leaves none current; the undo makes
            // it current again only if nothing else has been since.
            var wasActive = string.Equals(events.Active?.Id, dayId, StringComparison.Ordinal);

            _undo?.Capture($"Close race day '{raceEvent.DisplayName}'", () =>
            {
                events.Reopen(dayId);

                if (wasActive && events.Active is null && events.Get(dayId) is not null)
                {
                    events.SetActive(dayId);
                }

                RefreshAfterUndo();
            });

            _events.Close(raceEvent.Id);
        }
        else
        {
            var closedAtUtc = raceEvent.FinishedUtc;

            // A closed day can already be current here: picking any day in
            // "Working on" makes it current, open or not. Close clears
            // "current" whenever the day it closes is current, so an undo
            // that only closed the day again would leave no day current at
            // all - check-in, tech and the run sheet would drop onto the
            // legacy holder - rather than putting things back the way they
            // were before the reopen. The same rule as EventsControl.
            var wasActive = string.Equals(events.Active?.Id, dayId, StringComparison.Ordinal);

            _undo?.Capture($"Reopen race day '{raceEvent.DisplayName}'", () =>
            {
                // Close, with the time it was really closed.
                events.Close(dayId, closedAtUtc);

                // Current again only if it was current before the reopen
                // and nothing else has been made current in the meantime -
                // that later choice is not this undo's to take back. Close
                // has just cleared "current" if this day held it, so Active
                // is null exactly when nothing else has taken it since.
                if (wasActive && events.Active is null && events.Get(dayId) is not null)
                {
                    events.SetActive(dayId);
                }

                RefreshAfterUndo();
            });

            // And point the app back at it, if nothing else is current.
            // Closing the current day leaves none current, so without this
            // Close Day followed by Reopen Day gave back a day that was open
            // and current-of-nothing: everything scoped still filed against
            // the legacy holder, and the sidebar still not in race-night
            // order. See RaceEventStore.Reopen.
            _events.Reopen(raceEvent.Id, makeCurrentIfNoneIs: true);
        }

        RefreshAll();
    }
}
