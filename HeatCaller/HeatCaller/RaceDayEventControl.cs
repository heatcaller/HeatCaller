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
public sealed class RaceDayEventControl : UserControl
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
    private readonly Label _lblStatus = new();
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
    private readonly CheckedListBox _lstClasses = new();
    private readonly NumericUpDown _numHeatSize = new();
    private readonly CheckBox _chkCheckedInOnly = new();
    private readonly CheckBox _chkAutoAdvance = new();
    private readonly ComboBox _cmbAutoAdvanceScope = new();
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
    private readonly CheckBox _chkFirstRace = new();

    private readonly TimePicker _dtpFirstRace = new();

    /// <summary>One entry in the shape picker. ToString is what the combo draws.</summary>
    private sealed record ShapeChoice(RaceDayShape Shape, string Caption)
    {
        public override string ToString() => Caption;
    }

    /// <summary>What kind of night this day is - see <see cref="RaceDayShape"/>. The most consequential field on the page, so it sits near the top.</summary>
    private readonly ComboBox _cmbShape = new();

    private readonly Label _lblShapeHint = new();

    /// <summary>The rounds-per-driver caption, held because its wording comes from the shape.</summary>
    private Label? _lblQualRounds;

    /// <summary>The hint under the class tick list - also worded from the shape.</summary>
    private Label? _lblClassHint;

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
        Font = AppTheme.BodyFont;

        BuildLayout();
        RefreshAll();
    }

    private void BuildLayout()
    {
        var y = UiLayout.PageTop;

        var lblIntro = new Label
        {
            Text = "The race day everything on this page belongs to. Check-ins, scrutineering, the heats and the "
                 + "timetable are all filed against the current one, so starting a new race day starts them all fresh - "
                 + "and last week's are kept under last week's day instead of being cleared away. Changes here save "
                 + "themselves as you make them.",
            AutoSize = true,
            MaximumSize = new Size(880, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.BodyFont,
        };

        lblIntro.Location = new Point(UiLayout.PageLeft, y);

        y = UiLayout.Below(lblIntro, UiLayout.RowGap);

        // --- The picker row ---
        var lblWorking = new Label { Text = "Working on:", AutoSize = true, Font = AppTheme.BodyFont };

        _cmbEvent.Font = AppTheme.BodyFont;
        _cmbEvent.Width = 420;
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

        lblWorking.Location = new Point(UiLayout.PageLeft, UiLayout.CentreOn(lblWorking, y, _cmbEvent.Height));
        _cmbEvent.Location = new Point(UiLayout.After(lblWorking), y);

        _btnNew.Text = "New Race Day";
        _btnNew.Font = AppTheme.BodyFont;
        _btnNew.Size = new Size(UiLayout.WidthFor(_btnNew), UiLayout.ButtonHeight);
        _btnNew.Location = new Point(UiLayout.After(_cmbEvent) + UiLayout.RowGap, y);
        _btnNew.Click += (_, _) => _ = StartNewDay();

        // The wizard's front door, beside the button that starts a day the
        // short way. Both are here because both are "I am setting up
        // tonight" - one for somebody who knows what they want, one for
        // somebody who would rather be asked.
        _btnWizard.Text = "Set Up Tonight...";
        _btnWizard.Font = AppTheme.BodyFont;
        _btnWizard.Size = new Size(UiLayout.WidthFor(_btnWizard), UiLayout.ButtonHeight);
        _btnWizard.Click += (_, _) => SetUpTonightRequested?.Invoke();

        // Measured from the TALLEST thing on the row above, not from the
        // combo box. A button is 36 high and the combo box 28, so clearing
        // the box left this row four pixels inside "New Race Day" and the
        // doctor shunted it down ten every startup.
        y = Math.Max(
            UiLayout.Below(_cmbEvent, UiLayout.RowGap),
            UiLayout.Below(_btnNew, UiLayout.RowGap));

        // Its own row, under the picker, rather than fourth on the row
        // above. That row is already a caption, a 420px combo and a button,
        // which lands this one at about x=720 - inside the page at the
        // default text size and off the end of it at the next notch up, or
        // on a narrower window. A button nobody can see is the same as no
        // button, which is what happened.
        _btnWizard.Location = new Point(UiLayout.PageLeft, y);

        y = UiLayout.Below(_btnWizard, UiLayout.RowGap);

        // Two lines reserved whether or not the status needs them, so the
        // fields below do not hop when a different event is picked.
        _lblStatus.AutoSize = false;
        _lblStatus.Font = AppTheme.BodyFont;
        _lblStatus.ForeColor = AppTheme.TextMuted;
        _lblStatus.Location = new Point(UiLayout.PageLeft, y);
        _lblStatus.Size = new Size(880, AppTheme.BodyFont.Height * 2 + 4);

        y = UiLayout.Below(_lblStatus, UiLayout.RowGap);

        // --- The fields ---
        //
        // The field column used to be the literal 190, which was wide enough
        // for "Championship:" and for nothing longer. "Heats each driver
        // gets:" - the plain-English replacement for "Qualifying rounds:" -
        // is half as wide again, and at one notch up on the text-size setting
        // the old column clipped it. Measured from the captions themselves,
        // with the old 190 kept as a floor so the page looks unchanged at the
        // default size.
        var fieldLeft = 190;

        foreach (var caption in new[]
                 {
                     "Name:", ShapeCaption, "Venue:", "Track layout:", "Championship:",
                     HeatsPerDriverCaption, RacesPerDriverCaption, FirstRaceCaption,
                     "Classes racing:", "Drivers per heat:", "Next race is:", "Notes:",
                 })
        {
            var measured = TextRenderer.MeasureText(
                caption,
                AppTheme.BodyFont,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            fieldLeft = Math.Max(fieldLeft, UiLayout.PageLeft + measured.Width + UiLayout.LabelGap * 2);
        }

        // Returns the CAPTION, not the new y: two of these rows are worded
        // from the day's shape, and a caption created as a local inside here
        // could never be reworded afterwards. No caller used the y.
        Label Row(string caption, Control field, int width)
        {
            var lbl = new Label { Text = caption, AutoSize = true, Font = AppTheme.BodyFont };

            field.Font = AppTheme.BodyFont;
            field.Width = width;
            field.Location = new Point(fieldLeft, y);
            lbl.Location = new Point(UiLayout.PageLeft, UiLayout.CentreOn(lbl, y, field.Height));

            Controls.Add(lbl);
            Controls.Add(field);

            y = UiLayout.Below(field, UiLayout.RowGap);

            return lbl;
        }

        Row("Name:", _txtName, 320);

        // --- What kind of night ---
        //
        // Second, right under the name, because it decides more about what
        // the rest of the app shows than every field below it put together.
        // A day saved before this existed has no shape in its file and reads
        // back as a race day, which is what it has always behaved as.
        _cmbShape.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbShape.Items.Add(new ShapeChoice(RaceDayShape.RaceDay, "Race day - heats, then finals"));
        _cmbShape.Items.Add(new ShapeChoice(RaceDayShape.ClubNight, "Club night - just races"));
        _cmbShape.SelectedIndex = 0;

        Row(ShapeCaption, _cmbShape, 320);

        // Two lines reserved whatever the wording needs, the same as the
        // status line above: this label is rewritten when the shape changes,
        // and an AutoSize one would grow by a line and push every field
        // below it down the page as the box was used.
        _lblShapeHint.AutoSize = false;
        _lblShapeHint.ForeColor = AppTheme.TextMuted;
        _lblShapeHint.Font = AppTheme.SmallFont;
        _lblShapeHint.Location = new Point(fieldLeft, y);
        _lblShapeHint.Size = new Size(560, AppTheme.SmallFont.Height * 2 + 4);

        Controls.Add(_lblShapeHint);

        y = UiLayout.Below(_lblShapeHint, UiLayout.RowGap);

        // Editable combos: the venue and layout lists are suggestions, not
        // gatekeepers - a club night at a friend's garage should not need a
        // venue profile made first.
        _cmbVenue.DropDownStyle = ComboBoxStyle.DropDown;
        Row("Venue:", _cmbVenue, 320);

        _cmbLayout.DropDownStyle = ComboBoxStyle.DropDown;
        Row("Track layout:", _cmbLayout, 320);

        _cmbChampionship.DropDownStyle = ComboBoxStyle.DropDownList;
        Row("Championship:", _cmbChampionship, 320);

        _numQualRounds.Minimum = 1;
        _numQualRounds.Maximum = 10;
        _numQualRounds.Value = 3;
        _lblQualRounds = Row(HeatsPerDriverCaption, _numQualRounds, 70);

        // --- When it starts ---
        //
        // A tick and a picker on one row: the property is nullable, and a
        // time picker cannot say "not set". Placed by hand rather than
        // through Row, which takes one field.
        // TimePicker, not WinForms' DateTimePicker. That one wraps a native
        // Windows control which ignores BackColor and ForeColor almost
        // entirely, so its spinner came out in system grey against the dark
        // theme - a pasted-on white block - and clipped its own text at any
        // sensible width. TimePicker paints every pixel itself and is what
        // the rest of the app already uses for a time of day.
        _dtpFirstRace.Size = new Size(130, 26);
        _dtpFirstRace.Value = TimeSpan.FromHours(10);
        _dtpFirstRace.Location = new Point(fieldLeft, y);

        _chkFirstRace.Text = "Set";
        _chkFirstRace.AutoSize = true;
        _chkFirstRace.Font = AppTheme.BodyFont;
        _chkFirstRace.Location = new Point(
            UiLayout.After(_dtpFirstRace) + UiLayout.RowGap,
            UiLayout.CentreOn(_chkFirstRace, y, _dtpFirstRace.Height));

        var lblFirstRace = new Label { Text = FirstRaceCaption, AutoSize = true, Font = AppTheme.BodyFont };

        // Centred on itself against the picker's height, the same as Row
        // does for the fields above and below.
        lblFirstRace.Location = new Point(
            UiLayout.PageLeft,
            UiLayout.CentreOn(lblFirstRace, y, _dtpFirstRace.Height));

        Controls.Add(lblFirstRace);
        Controls.Add(_dtpFirstRace);
        Controls.Add(_chkFirstRace);

        y = UiLayout.Below(_dtpFirstRace, UiLayout.RowGap);

        // --- What the Program tab builds from ---

        _lstClasses.CheckOnClick = true;
        _lstClasses.BorderStyle = BorderStyle.None;
        _lstClasses.IntegralHeight = false;
        _lstClasses.Height = AppTheme.BodyFont.Height * 5 + 12;
        Row("Classes racing:", _lstClasses, 320);

        // Two lines reserved, for the same reason as the shape hint above:
        // the wording changes with the shape.
        var lblClassHint = new Label
        {
            AutoSize = false,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
            Location = new Point(fieldLeft, y),
            Size = new Size(560, AppTheme.SmallFont.Height * 2 + 4),
        };

        _lblClassHint = lblClassHint;

        Controls.Add(lblClassHint);

        y = UiLayout.Below(lblClassHint, UiLayout.RowGap);

        _numHeatSize.Minimum = 2;
        _numHeatSize.Maximum = 20;
        _numHeatSize.Value = 8;
        Row("Drivers per heat:", _numHeatSize, 70);

        _chkCheckedInOnly.Text = "Only include drivers who have checked in";
        _chkCheckedInOnly.AutoSize = true;
        _chkCheckedInOnly.Font = AppTheme.BodyFont;
        _chkCheckedInOnly.Location = new Point(fieldLeft, y);

        Controls.Add(_chkCheckedInOnly);

        y = UiLayout.Below(_chkCheckedInOnly, UiLayout.RowGap);

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
        _chkAutoAdvance.Font = AppTheme.BodyFont;
        _chkAutoAdvance.Location = new Point(fieldLeft, y);

        Controls.Add(_chkAutoAdvance);

        y = UiLayout.Below(_chkAutoAdvance, UiLayout.RowGap);

        _cmbAutoAdvanceScope.DropDownStyle = ComboBoxStyle.DropDownList;
        _lblAutoAdvanceScope = Row("Next race is:", _cmbAutoAdvanceScope, 260);

        _lblAutoAdvanceHint.Text = "Which race it reaches for when the one that just finished is recorded.";
        _lblAutoAdvanceHint.AutoSize = false;
        _lblAutoAdvanceHint.Size = new Size(Math.Max(240, 880 - fieldLeft), AppTheme.SmallFont.Height + 4);
        _lblAutoAdvanceHint.Font = AppTheme.SmallFont;
        _lblAutoAdvanceHint.ForeColor = AppTheme.TextMuted;
        _lblAutoAdvanceHint.Location = new Point(fieldLeft, y);

        Controls.Add(_lblAutoAdvanceHint);

        y = UiLayout.Below(_lblAutoAdvanceHint, UiLayout.RowGap);

        _txtNotes.Multiline = true;
        _txtNotes.AcceptsReturn = true;
        _txtNotes.ScrollBars = ScrollBars.Vertical;

        // Three lines of the font it is drawn in, so Ctrl +/- takes it
        // with everything else rather than leaving a box measured once.
        _txtNotes.Height = AppTheme.BodyFont.Height * 3 + UiLayout.HintGap * 2;
        Row("Notes:", _txtNotes, 320);

        y += UiLayout.RowGap;

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

        AppTheme.LayoutButtonRow(UiLayout.PageLeft, y, UiLayout.ButtonGap, _btnCloseDay, _btnDeleteDay);

        _lblSaved.AutoSize = true;
        _lblSaved.Font = AppTheme.BodyFont;
        _lblSaved.ForeColor = AppTheme.Success;
        _lblSaved.Visible = false;
        _lblSaved.Location = new Point(
            UiLayout.After(_btnDeleteDay) + UiLayout.RowGap,
            UiLayout.CentreOn(_lblSaved, y, UiLayout.ButtonHeight));

        y = UiLayout.Below(_btnCloseDay, UiLayout.RowGap);

        var lblHint = new Label
        {
            Text = "Picking a day above points check-in, scrutineering, the heats and the timetable at it. "
                 + "Close Day stops new rounds joining it - results and standings keep reading it forever. "
                 + "The legacy day holds everything recorded before race days were tracked, and is read-only here.",
            AutoSize = true,
            MaximumSize = new Size(880, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
            Location = new Point(UiLayout.PageLeft, y),
        };

        Controls.Add(lblIntro);
        Controls.Add(lblWorking);
        Controls.Add(_cmbEvent);
        Controls.Add(_btnNew);
        Controls.Add(_btnWizard);
        Controls.Add(_lblStatus);
        Controls.Add(_lblSaved);
        Controls.Add(_btnCloseDay);
        Controls.Add(_btnDeleteDay);
        Controls.Add(lblHint);

        // The two shape-worded captions and the two hints, filled once here
        // so the page is never briefly blank in those spots - LoadFields
        // does it again for whichever day is picked, and it is only wording.
        ApplyShapeWording();

        WireAutoSave();
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
        _numHeatSize.ValueChanged += (_, _) => CommitNow();
        _chkCheckedInOnly.CheckedChanged += (_, _) => CommitNow();
        _chkAutoAdvance.CheckedChanged += (_, _) => CommitNow();
        _cmbAutoAdvanceScope.SelectedIndexChanged += (_, _) => CommitNow();

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

        if (Selected is not { IsLegacy: false } raceEvent)
        {
            return;
        }

        if (!WriteFieldsTo(raceEvent))
        {
            return;
        }

        CaptureUndo(raceEvent, before);

        _events.Upsert(raceEvent);

        // NOT RefreshAll: that rebuilds the picker and reloads every field
        // from the store, which would take the caret out of the box being
        // typed in. Only the parts that report the day are redrawn.
        RefreshStatus();
        ShowSavedNote();
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

        var keep = Selected?.Id ?? _events.CurrentId;

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
    /// Only wording: no control moves, and both variants of every caption
    /// are in the column measurement at the top of BuildLayout, so the field
    /// column is the same width either way and nothing shifts as the box is
    /// used. The two hints are fixed at two lines for the same reason.
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
        _lblShapeHint.Text = clubNight
            ? "A flat list of races - no finals, no seeding, no bump-ups. Everyone in a racing class is in them."
            : "Qualifying heats, then finals built from them. Heats are built from drivers who have checked in.";

        if (_lblClassHint is not null)
        {
            _lblClassHint.Text = clubNight
                ? "Tick the classes running today. Next Steps builds all of tonight's races for every one of them in one press."
                : "Tick the classes running today. Next Steps builds all of tonight's heats for every one of them in one press.";
        }
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

        // Zero means "whatever was used last" and there is nothing sensible
        // to show for that, so the box shows the club default instead.
        // Shown, not written: this runs with _loading set, so the 8 goes on
        // screen without being saved onto a day that never asked for it.
        // It becomes real the moment somebody touches the box.
        _numHeatSize.Value = Math.Clamp(raceEvent.DriversPerHeat <= 0 ? 8 : raceEvent.DriversPerHeat, 2, 20);
        _chkCheckedInOnly.Checked = raceEvent.CheckedInDriversOnly;

        // The club-wide setting is the answer for a day that never said -
        // which is every day written before this moved here. Shown, not
        // written: this runs with _loading set, so the day only gains its
        // own answer when somebody actually touches the box.
        _chkAutoAdvance.Checked = raceEvent.AutoAdvanceOn(_settings?.AutoAdvanceRounds ?? true);

        LoadAutoAdvanceScope(
            raceEvent.AutoAdvanceScopeOr(_settings?.AutoAdvanceScope ?? "drivers"),
            raceEvent.AutoAdvanceClassOr(_settings?.AutoAdvanceClassName ?? string.Empty));

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
        _txtNotes.Enabled = editable;
        _cmbVenue.Enabled = editable;
        _cmbLayout.Enabled = editable;
        _cmbChampionship.Enabled = editable;
        _numQualRounds.Enabled = editable;
        _lstClasses.Enabled = editable;
        _numHeatSize.Enabled = editable;
        _chkCheckedInOnly.Enabled = editable;
        _chkAutoAdvance.Enabled = editable;
        _cmbAutoAdvanceScope.Enabled = editable && _chkAutoAdvance.Checked;

        // The caption and the hint go quiet with the box they describe, so
        // an unticked setting does not read as a live choice.
        if (_lblAutoAdvanceScope is not null)
        {
            _lblAutoAdvanceScope.ForeColor = _cmbAutoAdvanceScope.Enabled ? AppTheme.TextDark : AppTheme.TextMuted;
        }
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
            ? "CURRENT - the whole page is about this race day.  "
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

            if (wasCurrent && (previousId is null || events.Get(previousId) is { IsOpen: true }))
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
        raceEvent.DriversPerHeat = (int)_numHeatSize.Value;

        // Deliberately NOT re-derived from the shape here. It is set from the
        // shape once, when the day is started (EventSpine.StartEvent); after
        // that it is the director's to set, and a club night that does run a
        // sign-on desk must be able to say so without the next keystroke
        // undoing it.
        raceEvent.CheckedInDriversOnly = _chkCheckedInOnly.Checked;

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
                    current.QualifyingRounds = old.QualifyingRounds;
                    current.DriversPerHeat = old.DriversPerHeat;
                    current.CheckedInDriversOnly = old.CheckedInDriversOnly;
                    current.ClassNames = (old.ClassNames ?? new List<string>()).ToList();
                    current.ChampionshipId = old.ChampionshipId;
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
        && a.QualifyingRounds == b.QualifyingRounds
        && a.DriversPerHeat == b.DriversPerHeat
        && a.CheckedInDriversOnly == b.CheckedInDriversOnly
        && (a.ClassNames ?? new List<string>()).SequenceEqual(b.ClassNames ?? new List<string>(), StringComparer.Ordinal)
        && a.ChampionshipId == b.ChampionshipId;

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

            if (previousId != newId)
            {
                _undo?.Capture($"Make '{raceEvent.DisplayName}' the current race day", () =>
                {
                    // SetActive, so check-in, tech and the run sheet follow.
                    // Not onto a day closed or deleted since, and not once
                    // another day has been made current since - that later
                    // choice stands.
                    if (events.Active?.Id == newId
                        && (previousId is null || events.Get(previousId) is { IsOpen: true }))
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

            _undo?.Capture($"Reopen race day '{raceEvent.DisplayName}'", () =>
            {
                // Close, with the time it was really closed.
                events.Close(dayId, closedAtUtc);
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
