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
/// </summary>
internal sealed class RaceDaySetupWizard : Form
{
    private const int RailWidth = 190;
    private const int ContentLeft = RailWidth + 32;
    private const int ContentWidth = 600;

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

    private readonly Panel _rail = new();
    private readonly Label _lblBrand = new();
    private readonly Label _lblRailCaption = new();
    private readonly Label[] _railLabels = new Label[StepNames.Length];

    private readonly Label _lblStepTitle = new();
    private readonly Label _lblStepBody = new();
    private readonly Panel _content = new();
    private readonly Label _lblProgress = new();

    private readonly Button _btnSkipAll = new();
    private readonly Button _btnBack = new();
    private readonly Button _btnNext = new();

    private int _step;

    // --- the drivers step: adding someone new, in a small window of its own ---
    private readonly Button _btnAddDriver = new();

    // --- the day ---
    private readonly ComboBox _cmbShape = new();
    private readonly Label _lblShapeHint = new();
    private readonly TextBox _txtDayName = new();
    private readonly ComboBox _cmbVenue = new();
    private readonly ComboBox _cmbTrack = new();
    private readonly Label _lblDayState = new();

    // --- racing ---
    private readonly CheckedListBox _lstClasses = new();
    private readonly NumericUpDown _numRounds = new();
    private readonly NumericUpDown _numHeatSize = new();
    private readonly CheckBox _chkFirstRace = new();
    private readonly TimePicker _dtpFirstRace = new();
    private readonly Label _lblRoundsCaption = new();

    /// <summary>
    /// The race format each ticked class runs - laps or minutes, the start,
    /// the warm-up - one row per class with a button to change it.
    ///
    /// It was the one thing about a night the wizard never asked: the
    /// format lives on the class, and the first place anyone met it was
    /// Race Control, with the field already on the grid.
    /// </summary>
    private readonly Label _lblFormatsCaption = new();
    private readonly Panel _pnlFormats = new();
    private readonly Button _btnAddClass = new();

    // --- the drivers step: who's here ---
    private readonly ComboBox _cmbWhoClass = new();
    private readonly CheckBox _chkOnlyMissing = new();
    private readonly CheckedListBox _lstWhosHere = new();
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
    private readonly CheckBox _chkCheckedInOnly = new();
    private readonly Button _btnGoToCheckIn = new();

    // --- the programme ---
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
    private readonly Panel _pnlCantBuild = new();
    private readonly Label _lblNeedsTitle = new();
    private readonly Label _lblNeedsNote = new();
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
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(ContentLeft + ContentWidth + 28, 640);
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        BuildLayout();
        AppTheme.ApplyRecursively(this);

        // Straight to the step that needs doing when opened for one, rather
        // than the welcome page and a walk through what is already done.
        ShowStep(startStep);
    }

    private RaceEvent? OpenDay => _events.Active is { IsLegacy: false, IsOpen: true } day ? day : null;

    // --- Layout ---------------------------------------------------------

    private void BuildLayout()
    {
        _rail.Location = new Point(0, 0);
        _rail.Size = new Size(RailWidth, ClientSize.Height);

        _lblBrand.Text = "HEATCALLER";
        _lblBrand.AutoSize = true;
        _lblBrand.Font = AppTheme.NavFont;
        _lblBrand.Location = new Point(22, 26);

        _lblRailCaption.Text = "Setting up a race day";
        _lblRailCaption.AutoSize = true;
        _lblRailCaption.MaximumSize = new Size(RailWidth - 44, 0);
        _lblRailCaption.Font = AppTheme.SmallFont;
        _lblRailCaption.Location = new Point(22, 54);

        _rail.Controls.Add(_lblBrand);
        _rail.Controls.Add(_lblRailCaption);

        for (var i = 0; i < StepNames.Length; i++)
        {
            var index = i;

            var label = new Label
            {
                Text = StepNames[i],
                AutoSize = false,
                Size = new Size(RailWidth - 24, 34),
                Location = new Point(12, 100 + i * 38),
                Padding = new Padding(12, 0, 0, 0),
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
            _rail.Controls.Add(label);
        }

        _lblStepTitle.Location = new Point(ContentLeft, 30);
        _lblStepTitle.AutoSize = true;
        _lblStepTitle.MaximumSize = new Size(ContentWidth, 0);
        _lblStepTitle.Font = AppTheme.TitleFont;

        _lblStepBody.Location = new Point(ContentLeft, 78);
        _lblStepBody.AutoSize = true;
        _lblStepBody.MaximumSize = new Size(ContentWidth, 0);

        _content.Location = new Point(ContentLeft, 160);
        _content.Size = new Size(ContentWidth, 400);

        _lblProgress.AutoSize = true;
        _lblProgress.Font = AppTheme.SmallFont;

        _btnSkipAll.Text = "Skip setup";
        _btnSkipAll.Width = 120;
        _btnSkipAll.Click += (_, _) => Finish();

        _btnBack.Text = "Back";
        _btnBack.Width = 100;
        _btnBack.Click += (_, _) =>
        {
            SaveCurrentStep();
            ShowStep(_step - 1);
        };

        _btnNext.Text = "Next";
        _btnNext.Width = 150;
        _btnNext.Click += (_, _) => NextClicked();

        var buttonTop = ClientSize.Height - 58;

        _btnNext.Location = new Point(ClientSize.Width - 28 - _btnNext.Width, buttonTop);
        _btnBack.Location = new Point(_btnNext.Left - 12 - _btnBack.Width, buttonTop);
        _btnSkipAll.Location = new Point(ContentLeft, buttonTop);
        _lblProgress.Location = new Point(_btnSkipAll.Right + 16, buttonTop + 10);

        BuildDayStep();
        BuildRacingStep();
        BuildCheckInStep();
        BuildProgrammeStep();
        BuildSummaryStep();

        Controls.Add(_rail);
        Controls.Add(_lblStepTitle);
        Controls.Add(_lblStepBody);
        Controls.Add(_content);
        Controls.Add(_lblProgress);
        Controls.Add(_btnSkipAll);
        Controls.Add(_btnBack);
        Controls.Add(_btnNext);
    }

    private void BuildDayStep()
    {
        _cmbShape.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbShape.Location = new Point(0, 0);
        _cmbShape.Width = 300;
        _cmbShape.Items.Add("Race day - heats, then finals");
        _cmbShape.Items.Add("Club night - just races");
        _cmbShape.SelectedIndex = 0;
        _cmbShape.SelectedIndexChanged += (_, _) => ApplyShapeHint();

        // Two lines reserved, so the rest of the step does not jump when
        // the wording changes.
        _lblShapeHint.Location = new Point(0, 40);
        _lblShapeHint.AutoSize = false;
        _lblShapeHint.Size = new Size(ContentWidth, AppTheme.SmallFont.Height * 2 + 4);
        _lblShapeHint.Font = AppTheme.SmallFont;

        var y = 40 + _lblShapeHint.Height + 16;

        _txtDayName.Location = new Point(0, y + 22);
        _txtDayName.Width = 300;

        _cmbVenue.DropDownStyle = ComboBoxStyle.DropDown;
        _cmbVenue.Location = new Point(0, y + 92);
        _cmbVenue.Width = 300;

        _cmbTrack.DropDownStyle = ComboBoxStyle.DropDown;
        _cmbTrack.Location = new Point(0, y + 162);
        _cmbTrack.Width = 300;

        _lblDayState.Location = new Point(0, y + 208);
        _lblDayState.AutoSize = true;
        _lblDayState.MaximumSize = new Size(ContentWidth, 0);
        _lblDayState.Font = AppTheme.SmallFont;
    }

    private void BuildRacingStep()
    {
        // Two columns up top - the classes on the left, the numbers on the
        // right - so the formats underneath get the lower half of the step.
        const int right = 330;

        _lstClasses.Location = new Point(0, 22);
        _lstClasses.Size = new Size(300, 96);
        _lstClasses.CheckOnClick = true;

        // A class the club has not raced before - the old Classes step's
        // whole job, now a button under the list it adds to.
        _btnAddClass.Text = "Add Class...";
        _btnAddClass.Size = new Size(UiLayout.WidthFor(_btnAddClass), UiLayout.ButtonHeight);
        _btnAddClass.Location = new Point(0, _lstClasses.Bottom + 8);
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

        _lblRoundsCaption.Location = new Point(right, 2);
        _lblRoundsCaption.AutoSize = true;
        _lblRoundsCaption.Font = AppTheme.SmallFont;

        _numRounds.Location = new Point(right, 22);
        _numRounds.Width = 70;
        _numRounds.Minimum = 1;
        _numRounds.Maximum = 10;
        _numRounds.Value = 3;

        _numHeatSize.Location = new Point(right, 78);
        _numHeatSize.Width = 70;
        _numHeatSize.Minimum = 2;
        _numHeatSize.Maximum = 20;
        _numHeatSize.Value = 8;

        _chkFirstRace.Text = "First race at";
        _chkFirstRace.Location = new Point(right, 122);
        _chkFirstRace.AutoSize = true;

        _dtpFirstRace.Location = new Point(right + 140, 118);
        _dtpFirstRace.Size = new Size(120, 26);
        _dtpFirstRace.Value = TimeSpan.FromHours(10);

        _lblFormatsCaption.Text = "Race format - what each class runs tonight";
        _lblFormatsCaption.Location = new Point(0, _btnAddClass.Bottom + 14);
        _lblFormatsCaption.AutoSize = true;
        _lblFormatsCaption.Font = AppTheme.SmallFont;

        _pnlFormats.Location = new Point(0, _lblFormatsCaption.Top + 22);
        _pnlFormats.Size = new Size(ContentWidth, 400 - _pnlFormats.Top);
        _pnlFormats.AutoScroll = true;
    }

    /// <summary>
    /// One row per ticked class: its name, its format in a line, and a
    /// button that opens the same editor the Classes page does.
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

        _pnlFormats.SuspendLayout();

        foreach (var old in _pnlFormats.Controls.Cast<Control>().ToList())
        {
            _pnlFormats.Controls.Remove(old);
            old.Dispose();
        }

        var fallback = RaceFormat.FromSettings(_settings);
        var y = 0;

        if (ticked.Count == 0)
        {
            _pnlFormats.Controls.Add(new Label
            {
                Text = "Tick a class above to see the format it runs.",
                Location = new Point(0, 4),
                AutoSize = true,
                Font = AppTheme.SmallFont,
                ForeColor = AppTheme.TextMuted,
            });
        }

        foreach (var name in ticked)
        {
            if (_raceClasses.All.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)) is not { } raceClass)
            {
                continue;
            }

            var button = new Button { Text = "Race Format..." };
            button.Size = new Size(UiLayout.WidthFor(button), UiLayout.ButtonHeight);
            button.Location = new Point(ContentWidth - button.Width - 14, y);
            button.Click += (_, _) => EditClassFormat(raceClass);

            var lblName = new Label
            {
                Text = raceClass.Name,
                AutoSize = false,
                AutoEllipsis = true,
                Size = new Size(120, UiLayout.ButtonHeight),
                Location = new Point(0, y),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = AppTheme.BoldFont,
                ForeColor = AppTheme.TextDark,
            };

            // The class's own format, or the default it follows - said
            // either way, so "nothing set" still reads as a real answer.
            var describe = raceClass.Format is { } own
                ? own.Describe()
                : "app default: " + fallback.Describe();

            if (raceClass.QualifierFormat is not null)
            {
                describe += "  (own qualifier format)";
            }

            var lblFormat = new Label
            {
                Text = describe,
                AutoSize = false,
                AutoEllipsis = true,
                Location = new Point(lblName.Right + UiLayout.RowGap, y),
                Size = new Size(button.Left - UiLayout.RowGap - (lblName.Right + UiLayout.RowGap), UiLayout.ButtonHeight),
                TextAlign = ContentAlignment.MiddleLeft,
                Font = AppTheme.SmallFont,
                ForeColor = raceClass.Format is null ? AppTheme.TextMuted : AppTheme.TextDark,
            };

            _pnlFormats.Controls.Add(lblName);
            _pnlFormats.Controls.Add(lblFormat);
            _pnlFormats.Controls.Add(button);

            y += UiLayout.ButtonHeight + 6;
        }

        _pnlFormats.ResumeLayout();

        AppTheme.ApplyRecursively(_pnlFormats);

        foreach (var button in _pnlFormats.Controls.OfType<Button>())
        {
            AppTheme.StyleButton(button, AppTheme.ButtonKind.Secondary);
        }
    }

    /// <summary>
    /// The class's format, in the one editor every format is edited in, and
    /// saved on the class - the same thing Classes and Race Control do.
    /// </summary>
    private void EditClassFormat(RaceClass raceClass)
    {
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

    private void BuildCheckInStep()
    {
        // Ticking people in happens HERE rather than only on the Race Day
        // page. "Who is here" is the question this step asks, and sending
        // somebody to another page to answer it - then back - is the kind of
        // errand the wizard exists to remove.
        _cmbWhoClass.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbWhoClass.Location = new Point(0, 3);
        _cmbWhoClass.Width = 200;
        _cmbWhoClass.SelectedIndexChanged += (_, _) => FillWhosHere();

        _chkOnlyMissing.Text = "Only those not in yet";
        _chkOnlyMissing.Location = new Point(_cmbWhoClass.Right + UiLayout.SectionGap / 2, 7);
        _chkOnlyMissing.AutoSize = true;
        _chkOnlyMissing.CheckedChanged += (_, _) => FillWhosHere();

        // Someone new: a button over the list they will land in, opening a
        // small window for their details. It was a row of three boxes and
        // an Add button along the bottom of the step, which crowded it.
        _btnAddDriver.Text = "Add Driver...";
        _btnAddDriver.Size = new Size(UiLayout.WidthFor(_btnAddDriver), UiLayout.ButtonHeight);
        _btnAddDriver.Location = new Point(ContentWidth - _btnAddDriver.Width, 0);
        _btnAddDriver.Click += (_, _) => AddNewDriver();

        // The list takes the room the new-driver row used to - taller, so
        // more of the club is on screen at once.
        const int buttonsTop = 250;
        var listTop = UiLayout.ButtonHeight + 8;

        _lstWhosHere.Location = new Point(0, listTop);
        _lstWhosHere.Size = new Size(ContentWidth, buttonsTop - 8 - listTop);
        _lstWhosHere.CheckOnClick = true;
        _lstWhosHere.ItemCheck += WhosHereTicked;

        _btnCheckAllShown.Text = "Check In All Shown";
        _btnCheckAllShown.Size = new Size(UiLayout.WidthFor(_btnCheckAllShown), UiLayout.ButtonHeight);
        _btnCheckAllShown.Location = new Point(0, buttonsTop);
        _btnCheckAllShown.Click += (_, _) => SetAllShown(true);

        _btnClearAll.Text = "Clear All";
        _btnClearAll.Size = new Size(UiLayout.WidthFor(_btnClearAll), UiLayout.ButtonHeight);
        _btnClearAll.Location = new Point(_btnCheckAllShown.Right + UiLayout.ButtonGap, buttonsTop);
        _btnClearAll.Click += (_, _) => SetAllShown(false);

        _btnGoToCheckIn.Text = "Open the full check-in page";
        _btnGoToCheckIn.Size = new Size(UiLayout.WidthFor(_btnGoToCheckIn), UiLayout.ButtonHeight);
        _btnGoToCheckIn.Location = new Point(ContentWidth - _btnGoToCheckIn.Width, buttonsTop);
        _btnGoToCheckIn.Click += (_, _) =>
        {
            SaveCurrentStep();
            GoToPageOnClose = "Race Day";
            Finish();
        };

        _lblCheckInState.Location = new Point(0, buttonsTop + UiLayout.ButtonHeight + 8);
        _lblCheckInState.AutoSize = true;
        _lblCheckInState.MaximumSize = new Size(ContentWidth, 0);

        _chkCheckedInOnly.Text = "Only build races from drivers who have checked in";
        _chkCheckedInOnly.Location = new Point(0, buttonsTop + UiLayout.ButtonHeight + 34);
        _chkCheckedInOnly.AutoSize = true;
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

    private void BuildProgrammeStep()
    {
        _lblProgrammeState.Location = new Point(0, 0);
        _lblProgrammeState.AutoSize = true;
        _lblProgrammeState.MaximumSize = new Size(ContentWidth, 0);

        _pnlCantBuild.Location = new Point(0, 0);
        _pnlCantBuild.Size = new Size(ContentWidth, 0);

        _lblNeedsTitle.AutoSize = true;
        _lblNeedsTitle.MaximumSize = new Size(ContentWidth, 0);
        _lblNeedsTitle.Font = AppTheme.HeadingFont;
        _lblNeedsTitle.Location = new Point(0, 0);

        _lblNeedsNote.AutoSize = true;
        _lblNeedsNote.MaximumSize = new Size(ContentWidth, 0);
        _lblNeedsNote.Font = AppTheme.BodyFont;

        _pnlCantBuild.Controls.Add(_lblNeedsTitle);
        _pnlCantBuild.Controls.Add(_lblNeedsNote);
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
        // Last visit's cards - rebuilt every time, since what they say changes.
        foreach (var old in _pnlCantBuild.Controls.OfType<ReadinessCard>().ToList())
        {
            old.Dispose();
        }

        _fixButtons.Clear();

        _lblNeedsTitle.ForeColor = AppTheme.Warning;

        if (!BlockedBySetup())
        {
            // Nothing a step here would fix - a heat on track, usually. Said
            // plainly, with no cards to send anyone the wrong way.
            _lblNeedsTitle.Text = "The program can't be built right now";
            _lblNeedsNote.Text = _plan.Message;
            _lblNeedsNote.ForeColor = AppTheme.TextDark;
            _lblNeedsNote.Location = new Point(0, UiLayout.Below(_lblNeedsTitle, UiLayout.RowGap));
            _pnlCantBuild.Height = _lblNeedsNote.Bottom + 8;
            return;
        }

        var day = OpenDay;
        var (dayDone, classesDone, driversDone) = StepsDone();

        var cards = new List<(int Step, string Name, string Button, bool Done, string Detail)>
        {
            (StepDay, "The day", "Go to The Day", dayDone,
                dayDone
                    ? $"{day!.DisplayName} is open."
                    : _plan.Title == RaceProgramControl.ClosedDayTitle
                        ? "Tonight's day has been closed. Open a new one to build a program for it."
                        : "No race day is open yet - the program is built for one."),

            (StepClasses, "Classes", "Go to Classes", classesDone,
                classesDone
                    ? "Racing tonight: " + ClassList(day!.ClassNames)
                    : dayDone
                        ? "No classes are ticked for tonight."
                        : "Tick tonight's classes once the day is open."),

            (StepDrivers, "Drivers", "Go to Drivers", driversDone,
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
        _lblNeedsNote.Location = new Point(0, UiLayout.Below(_lblNeedsTitle, 6));

        var y = UiLayout.Below(_lblNeedsNote, UiLayout.SectionGap - 6);
        var first = true;

        foreach (var (step, name, caption, done, detail) in cards)
        {
            Button? fix = null;

            if (!done)
            {
                var target = step;

                fix = new Button { Text = caption };
                fix.Size = new Size(UiLayout.WidthFor(fix), UiLayout.ButtonHeight);
                fix.Click += (_, _) => GoToStep(target);

                // The first step to fix is the loud button; the rest wait
                // their turn.
                _fixButtons.Add((fix, first ? AppTheme.ButtonKind.Primary : AppTheme.ButtonKind.Secondary));
                first = false;
            }

            var card = new ReadinessCard(name, detail, done, fix)
            {
                Location = new Point(0, y),
                Width = ContentWidth,
            };

            card.FitHeight();
            _pnlCantBuild.Controls.Add(card);

            y = card.Bottom + UiLayout.ButtonGap;
        }

        _pnlCantBuild.Height = y;
    }

    /// <summary>Tonight's classes for a card - the first few by name, then how many more.</summary>
    private static string ClassList(IReadOnlyCollection<string> names)
    {
        const int shown = 4;

        var list = string.Join(", ", names.Take(shown));

        return names.Count > shown ? $"{list} and {names.Count - shown} more." : list + ".";
    }

    /// <summary>A tick in the success colour, or a red disc with a "!" - drawn at the right of a rail step.</summary>
    private static void PaintRailMark(Graphics g, Label label, RailMark mark)
    {
        if (mark == RailMark.None)
        {
            return;
        }

        const int size = 18;
        var box = new Rectangle(label.Width - size - 12, (label.Height - size) / 2, size, size);

        g.SmoothingMode = SmoothingMode.AntiAlias;

        if (mark == RailMark.Done)
        {
            using var pen = new Pen(AppTheme.Success, 2.2f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round,
            };

            g.DrawLines(pen, new[]
            {
                new PointF(box.Left + 3f, box.Top + 9.5f),
                new PointF(box.Left + 7.5f, box.Top + 14f),
                new PointF(box.Right - 3f, box.Top + 4.5f),
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
    /// </summary>
    private sealed class ReadinessCard : Panel
    {
        private const int Pad = 16;
        private const int PillWidth = 78;
        private const int PillHeight = 22;
        private const int TextLeft = Pad + PillWidth + 14;
        private const int MinimumHeight = 66;

        private const TextFormatFlags TitleFlags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;

        private const TextFormatFlags DetailFlags =
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPadding;

        private readonly string _title;
        private readonly string _detail;
        private readonly bool _done;
        private readonly Button? _fix;

        public ReadinessCard(string title, string detail, bool done, Button? fix)
        {
            _title = title;
            _detail = detail;
            _done = done;
            _fix = fix;

            DoubleBuffered = true;
            ResizeRedraw = true;

            // The card's own colour is what the button sits on; the corners
            // outside the rounded box are painted in the page's colour below.
            // Kept through the theme pass, which would otherwise set every
            // panel to the page background.
            BackColor = AppTheme.Surface;
            Tag = AppTheme.KeepBackColor;

            if (fix is not null)
            {
                Controls.Add(fix);
            }
        }

        /// <summary>Where the detail text has to stop - short of the button, if there is one.</summary>
        private int TextWidth => Math.Max(60, (_fix is null ? Width : _fix.Left) - Pad - TextLeft);

        private int TitleHeight => TextRenderer.MeasureText(_title, AppTheme.BoldFont, Size.Empty, TitleFlags).Height;

        /// <summary>Puts the button on the right and sizes the card to its text.</summary>
        public void FitHeight()
        {
            if (_fix is not null)
            {
                _fix.Left = Width - Pad - _fix.Width;
            }

            var detailHeight = TextRenderer.MeasureText(_detail, AppTheme.SmallFont,
                new Size(TextWidth, int.MaxValue), DetailFlags).Height;

            Height = Math.Max(MinimumHeight, Pad + TitleHeight + 4 + detailHeight + Pad);

            if (_fix is not null)
            {
                _fix.Top = (Height - _fix.Height) / 2;
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

            // The pill, level with the step's name.
            var titleTop = Pad;
            var pill = new Rectangle(Pad, titleTop + (TitleHeight - PillHeight) / 2, PillWidth, PillHeight);

            using (var path = Rounded(pill, PillHeight / 2))
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
            TextRenderer.DrawText(g, _title, AppTheme.BoldFont, new Point(TextLeft, titleTop),
                _done ? AppTheme.TextMuted : AppTheme.TextDark, TitleFlags);

            var detailBox = new Rectangle(TextLeft, titleTop + TitleHeight + 4, TextWidth, Height);

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
        _lblSummary.Location = new Point(0, 0);
        _lblSummary.AutoSize = true;
        _lblSummary.MaximumSize = new Size(ContentWidth, 0);
    }

    // --- Moving between steps -------------------------------------------

    private void ShowStep(int step)
    {
        _step = Math.Clamp(step, 0, StepNames.Length - 1);

        // Before anything is shown: the program step and the rail both read it.
        RefreshReadiness();

        _content.Controls.Clear();

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
                _content.Controls.Add(_cmbShape);
                _content.Controls.Add(_lblShapeHint);
                AddCaptioned("Call it", _txtDayName);
                AddCaptioned("Venue", _cmbVenue);
                AddCaptioned("Track layout", _cmbTrack);
                _content.Controls.Add(_lblDayState);
                break;

            case StepClasses:
                _lblStepTitle.Text = "Tonight's classes";
                _lblStepBody.Text = "Tick the classes out tonight - Add Class for a new one. Then how many times each driver "
                                  + "races, how many cars go out at once, and the race each class runs.";
                LoadRacingStep();
                AddCaptioned("Classes racing", _lstClasses);
                _content.Controls.Add(_lblRoundsCaption);
                _content.Controls.Add(_numRounds);
                AddCaptioned("Drivers per heat", _numHeatSize);
                _content.Controls.Add(_chkFirstRace);
                _content.Controls.Add(_dtpFirstRace);
                _content.Controls.Add(_btnAddClass);
                _content.Controls.Add(_lblFormatsCaption);
                _content.Controls.Add(_pnlFormats);
                break;

            case StepDrivers:
                _lblStepTitle.Text = "Who's here tonight?";
                _lblStepBody.Text = "Tick everyone who has turned up - filter by class to work through them a class at a "
                                  + "time. Someone new? Add Driver puts them on file and checks them in.";
                LoadCheckInStep();
                _content.Controls.Add(_cmbWhoClass);
                _content.Controls.Add(_chkOnlyMissing);
                _content.Controls.Add(_lstWhosHere);
                _content.Controls.Add(_btnCheckAllShown);
                _content.Controls.Add(_btnClearAll);
                _content.Controls.Add(_btnGoToCheckIn);
                _content.Controls.Add(_lblCheckInState);
                _content.Controls.Add(_chkCheckedInOnly);
                _content.Controls.Add(_btnAddDriver);
                break;

            case StepProgramme:
                _lblStepTitle.Text = "The program";
                _lblStepBody.Text = _plan.Ok
                    ? "The races themselves, built from the classes and the drivers above. "
                      + "Build It makes them; Back changes anything first."
                    : "The races themselves, built from tonight's classes and the drivers in them.";
                LoadProgrammeStep();
                _content.Controls.Add(_lblProgrammeState);
                _content.Controls.Add(_pnlCantBuild);
                break;

            default:
                _lblStepTitle.Text = "That's tonight ready";
                _lblStepBody.Text = "Anything left is on the Next Steps tab, which keeps track of what is next.";
                LoadSummaryStep();
                _content.Controls.Add(_lblSummary);
                break;
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

        AppTheme.ApplyRecursively(_content);
        ApplyFrameColours();

        // Enter moves on.
        AcceptButton = _btnNext;
        _btnNext.Focus();
    }

    /// <summary>
    /// Puts a small caption above a field and adds both.
    ///
    /// The steps are laid out at fixed coordinates the way FirstRunWizard's
    /// are, so the caption is positioned from the field rather than the
    /// other way round - one number per row instead of two.
    /// </summary>
    private void AddCaptioned(string caption, Control field)
    {
        var label = new Label
        {
            Text = caption,
            AutoSize = true,
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
            Location = new Point(field.Left, Math.Max(0, field.Top - 20)),
        };

        _content.Controls.Add(label);
        _content.Controls.Add(field);
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
                     _lblShapeHint, _lblDayState, _lblRoundsCaption, _lblFormatsCaption,
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
        _lblShapeHint.Text = _cmbShape.SelectedIndex == 1
            ? "A club night is just races - everyone races, several times, and that is the night. No finals."
            : "A race day qualifies in heats and then runs finals built from how qualifying went.";

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
        _lblRoundsCaption.Text = day is { IsClubNight: true }
            ? "Races each driver gets"
            : "Heats each driver gets";

        if (day is not null)
        {
            _numRounds.Value = Math.Clamp(day.QualifyingRounds, _numRounds.Minimum, _numRounds.Maximum);

            if (day.DriversPerHeat > 0)
            {
                _numHeatSize.Value = Math.Clamp(day.DriversPerHeat, _numHeatSize.Minimum, _numHeatSize.Maximum);
            }

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
                                  + "one - new drivers can still be added below.";

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
        // to it.
        _lblProgrammeState.Visible = plan.Ok;
        _pnlCantBuild.Visible = !plan.Ok;

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
/// </summary>
internal sealed class QuickClassForm : Form
{
    private readonly TextBox _txtName = new();
    private readonly Label _lblProblem = new();
    private readonly Func<string, bool> _exists;

    public string ClassName => _txtName.Text.Trim();

    public QuickClassForm(Func<string, bool> exists)
    {
        _exists = exists;

        Text = "Add a class";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        const int width = 360;

        var lblIntro = new Label
        {
            Text = "Saved with the club's classes, and ticked for tonight. Its race format can be set right after.",
            AutoSize = true,
            MaximumSize = new Size(width, 0),
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

        _lblProblem.AutoSize = false;
        _lblProblem.Size = new Size(width, AppTheme.SmallFont.Height * 2 + 4);
        _lblProblem.Font = AppTheme.SmallFont;
        _lblProblem.ForeColor = AppTheme.Accent;

        var ok = new Button { Text = "Add Class" };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        ok.Click += (_, _) => TryAccept();

        Controls.AddRange(new Control[] { lblIntro, lblName, nameField, _lblProblem, ok, cancel });

        AcceptButton = ok;
        CancelButton = cancel;

        // Themed BEFORE it is laid out - see QuickDriverForm.
        AppTheme.ApplyRecursively(this);
        AppTheme.StyleButton(ok, AppTheme.ButtonKind.Primary);
        AppTheme.StyleButton(cancel, AppTheme.ButtonKind.Secondary);

        const int pad = UiLayout.PageLeft;
        var y = UiLayout.PageTop;

        lblIntro.Location = new Point(pad, y);
        y = UiLayout.Below(lblIntro, UiLayout.SectionGap - UiLayout.HintGap);

        lblName.Location = new Point(pad, y);
        y = UiLayout.Below(lblName, UiLayout.HintGap);

        nameField.Bounds = new Rectangle(pad, y, width, AppTheme.BodyFont.Height + 16);
        y = nameField.Bottom + UiLayout.RowGap;

        _lblProblem.Location = new Point(pad, y);
        y = _lblProblem.Bottom + UiLayout.RowGap;

        cancel.Size = new Size(UiLayout.WidthFor(cancel), UiLayout.ButtonHeight);
        cancel.Location = new Point(pad + width - cancel.Width, y);

        ok.Size = new Size(UiLayout.WidthFor(ok), UiLayout.ButtonHeight);
        ok.Location = new Point(cancel.Left - UiLayout.ButtonGap - ok.Width, y);

        ClientSize = new Size(width + pad * 2, y + UiLayout.ButtonHeight + UiLayout.PageTop);
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
/// </summary>
internal sealed class QuickDriverForm : Form
{
    private const string NoClass = "(no class)";

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
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        const int width = 360;

        var lblIntro = new Label
        {
            Text = "Saved with the drivers on file, and checked in for tonight.",
            AutoSize = true,
            MaximumSize = new Size(width, 0),
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
        _cmbClass.Width = 240;
        _cmbClass.Items.Add(NoClass);

        foreach (var name in classes)
        {
            _cmbClass.Items.Add(name);
        }

        _cmbClass.SelectedItem = startClass is not null && _cmbClass.Items.Contains(startClass) ? startClass : NoClass;

        // Room for two lines, always - so the window does not jump when it
        // has something to say.
        _lblProblem.AutoSize = false;
        _lblProblem.Size = new Size(width, AppTheme.SmallFont.Height * 2 + 4);
        _lblProblem.Font = AppTheme.SmallFont;
        _lblProblem.ForeColor = AppTheme.Accent;

        var ok = new Button { Text = "Add Driver" };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        ok.Click += (_, _) => TryAccept();

        Controls.AddRange(new Control[]
        {
            lblIntro, lblName, nameField, lblTransponder, transponderField, lblClass, _cmbClass, _lblProblem, ok, cancel,
        });

        AcceptButton = ok;
        CancelButton = cancel;

        // Themed BEFORE it is laid out: the theme takes the text boxes'
        // native borders off, which changes their height.
        AppTheme.ApplyRecursively(this);
        AppTheme.StyleButton(ok, AppTheme.ButtonKind.Primary);
        AppTheme.StyleButton(cancel, AppTheme.ButtonKind.Secondary);

        const int pad = UiLayout.PageLeft;
        var y = UiLayout.PageTop;

        lblIntro.Location = new Point(pad, y);
        y = UiLayout.Below(lblIntro, UiLayout.SectionGap - UiLayout.HintGap);

        // One height for all three fields: the drop-down's, which is set by
        // its font, so the two text fields match it exactly.
        var fieldHeight = Math.Max(_cmbClass.Height, AppTheme.BodyFont.Height + 12);

        nameField.Size = new Size(width, fieldHeight);
        transponderField.Size = new Size(_cmbClass.Width, fieldHeight);

        foreach (var (caption, field) in new (Label, Control)[] { (lblName, nameField), (lblTransponder, transponderField), (lblClass, _cmbClass) })
        {
            caption.Location = new Point(pad, y);
            y = UiLayout.Below(caption, UiLayout.HintGap);

            field.Location = new Point(pad, y);
            y = field.Bottom + UiLayout.RowGap + UiLayout.HintGap;
        }

        _lblProblem.Location = new Point(pad, y);
        y = _lblProblem.Bottom + UiLayout.RowGap;

        cancel.Size = new Size(UiLayout.WidthFor(cancel), UiLayout.ButtonHeight);
        cancel.Location = new Point(pad + width - cancel.Width, y);

        ok.Size = new Size(UiLayout.WidthFor(ok), UiLayout.ButtonHeight);
        ok.Location = new Point(cancel.Left - UiLayout.ButtonGap - ok.Width, y);

        ClientSize = new Size(width + pad * 2, y + UiLayout.ButtonHeight + UiLayout.PageTop);
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
