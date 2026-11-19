namespace HeatCaller;

/// <summary>
/// Creates or edits one virtual opponent.
///
/// The pace source drives what the rest of the form means, so the fields
/// that don't apply are hidden rather than greyed - a form where half the
/// boxes are permanently dead is harder to read than one that changes.
///
/// On the layout pieces (DialogFit): the name, the car and the Enabled tick
/// lined up in a field grid; under the PACE heading a second grid, its
/// captions in one column with the first's - what the pace is based on,
/// then only the rows that source uses (the driver, the date range, what a
/// live ghost follows, the lap time), the adjustment with the line spelling
/// it out under it, and the consistency; and Save and Cancel at the foot.
/// The fields are the window's body, which scrolls on a screen too short for
/// them while Save and Cancel stay in view. It opens tall enough for the
/// source that shows the most rows, so picking another never scrolls them
/// (see OnLoad), and as wide as it was made at the normal text size, at the
/// size in use - wider when a drop-down's longest choice needs it. It used
/// to be placed in pixels in a 520x498 window: every caption in a 128px
/// column, and the drop-downs and text boxes 350px wide beside it -
/// narrower than the longest consistency; the lap time and the driver
/// sharing one place, on top of each other for a live ghost of a driver's
/// own pace, and "seconds" there whatever the source, over the driver
/// picker when it showed; and a hidden row leaving its gap behind.
/// </summary>
public sealed class GhostEditForm : Form
{
    /// <summary>How wide the window is inside its frame at the normal text size, as it was made.</summary>
    private const int DialogWidth = 520;

    /// <summary>The field column at the normal text size - the drop-downs and text boxes ran this wide - wider when a drop-down's longest choice needs it.</summary>
    private const int FieldColumnWidth = 350;

    /// <summary>The lap time's and the adjustment's boxes at the normal text size, as they were - wider if either's widest value needs more.</summary>
    private const int NumberWidth = 90;

    /// <summary>Each end of the date range at the normal text size, as it was.</summary>
    private const int DateWidth = 150;

    /// <summary>
    /// The pace source whose rows the window opens tall enough for: the one
    /// that shows the most - the driver, what their pace is followed on, and
    /// the lap time to run at until their laps come in. See OnLoad.
    /// </summary>
    private const GhostPaceSource TallestSource = GhostPaceSource.LiveOwnPace;

    private readonly ProfileStore _profiles;
    private readonly List<DriverProfile> _drivers;

    private readonly TextBox _txtName = new();
    private readonly TextBox _txtCar = new();
    private readonly CheckBox _chkEnabled = new ThemedCheckBox();

    private readonly ComboBox _cmbPaceSource = new();
    private readonly ComboBox _cmbDriver = new();
    private readonly NumericUpDown _numFixedLap = new();
    private readonly NumericUpDown _numPacePercent = new();
    private readonly ComboBox _cmbConsistency = new();

    // The captions of the rows the pace source shows and hides are the
    // field grid's own (FieldGrid.AddRow), hidden and shown with their
    // fields - see ShowPaceFields.
    private readonly Label _lblDriver;
    private readonly Label _lblFixedLap;

    /// <summary>The line under the adjustment, as tall as the longest thing it can say - see FillFields.</summary>
    private readonly SteadyLabel _lblPaceHint = new();

    private readonly ComboBox _cmbLiveBasis = new();
    private readonly Label _lblLiveBasis;
    private readonly DateTimePicker _dtFrom = new();
    private readonly DateTimePicker _dtTo = new();
    private readonly Label _lblRange;
    private readonly Label _lblRangeTo = new();

    /// <summary>The lap time and "seconds" after it: one row of the grid, hidden and shown whole.</summary>
    private readonly FlowLayoutPanel _fixedLapRow;

    /// <summary>The two dates and "and" between them: one row of the grid, hidden and shown whole.</summary>
    private readonly FlowLayoutPanel _rangeRow;

    /// <summary>
    /// The pace sources in the order the dropdown shows them.
    ///
    /// A list rather than a switch on SelectedIndex: the index-based version
    /// mapped anything that wasn't 0 or 2 onto "personal best", so adding a
    /// source here would have silently turned saved ghosts into the wrong
    /// kind.
    /// </summary>
    /// <summary>
    /// The consistency levels in the order the dropdown shows them - the
    /// same fix as PaceSources above, applied to the one field it missed.
    ///
    /// This used (GhostConsistency)SelectedIndex, two fields below the
    /// comment explaining why that was wrong. A fifth level inserted
    /// anywhere but the end would have changed how every saved ghost
    /// drives. Now the list is filled from this table and read back
    /// through it, so the two cannot drift.
    /// </summary>
    private static readonly (GhostConsistency Level, string Label)[] ConsistencyLevels =
    {
        (GhostConsistency.Metronome, "Metronome - identical every lap, good as a pace target"),
        (GhostConsistency.Steady, "Steady - small variation, a quick driver on a good run"),
        (GhostConsistency.Human, "Human - the odd scrappy lap, the most race-like"),
        (GhostConsistency.Erratic, "Erratic - fast laps and bad ones, good for practicing recovery"),
    };

    private static readonly (GhostPaceSource Source, string Label)[] PaceSources =
    {
        (GhostPaceSource.FixedLapTime, "A fixed lap time"),
        (GhostPaceSource.DriverPersonalBest, "A driver's personal best on this track"),
        (GhostPaceSource.OwnBestTonight, "A driver's best tonight (or ever, before tonight's)"),
        (GhostPaceSource.PersonalBestInRange, "A driver's personal best within a date range"),
        (GhostPaceSource.YourRecentAverage, "A driver's recent average pace"),
        (GhostPaceSource.TrackRecord, "The track record for this layout"),
        (GhostPaceSource.LiveOwnPace, "Live: a driver's pace in this session"),
        (GhostPaceSource.LiveLeaderPace, "Live: whatever the leader is running"),
    };

    private GhostPaceSource SelectedSource =>
        PaceSources[Math.Clamp(_cmbPaceSource.SelectedIndex, 0, PaceSources.Length - 1)].Source;

    public GhostCar Result { get; private set; }

    public GhostEditForm(GhostCar? existing, ProfileStore profiles)
    {
        // Built with its layout held - see LayoutFreeze.HoldWhileBuilding
        // (18 Nov 2026): built live, nearly every part had its window before
        // the theme pass, which made the drop-downs' again.
        using var building = LayoutFreeze.HoldWhileBuilding();

        _profiles = profiles;

        // Ghosts excluded - a ghost built from another ghost's pace is a
        // copy of a copy, and the drift would be confusing.
        _drivers = profiles.All
            .Where(p => !GhostService.IsGhost(p.Transponder))
            .OrderBy(p => p.DriverName)
            .ToList();

        Result = existing is null
            ? new GhostCar()
            : new GhostCar
            {
                Id = existing.Id,
                Transponder = existing.Transponder,
                Name = existing.Name,
                Enabled = existing.Enabled,
                PaceSource = existing.PaceSource,
                Consistency = existing.Consistency,
                FixedLapMs = existing.FixedLapMs,
                SourceTransponder = existing.SourceTransponder,
                PacePercent = existing.PacePercent,
                CarModel = existing.CarModel,
                RangeFromUtc = existing.RangeFromUtc,
                RangeToUtc = existing.RangeToUtc,
                LiveBasis = existing.LiveBasis,
                LiveAverageLaps = existing.LiveAverageLaps,

                // Kept through an edit, or a running own-best ghost would
                // take the copy's revision as a request to re-read its pace.
                PaceRevision = existing.PaceRevision,
            };

        Text = existing is null ? "Add Ghost Car" : "Edit Ghost Car";
        Font = AppTheme.BodyFont;

        // Background set BEFORE theming. ApplyRecursively gives controls the
        // dark theme's light text, so a form left at the default light
        // background renders every label invisible - which is exactly what
        // happened here.
        BackColor = AppTheme.Background;

        FillFields();

        // Who the ghost is: the name and the car running to the right-hand
        // edge, as they always did, and the Enabled tick under them in the
        // fields' column.
        var identity = new FieldGrid();
        var captions = new List<Label>
        {
            identity.AddRow("Name:", _txtName, stretch: true),
            identity.AddRow("Car:", _txtCar, stretch: true),
        };

        identity.AddField(_chkEnabled);

        var lblPaceHeading = new Label
        {
            Text = "PACE",
            AutoSize = true,
            Font = AppTheme.BoldFont,
            ForeColor = AppTheme.Primary,
        };

        var lblFixedSuffix = new Label { Text = "seconds", ForeColor = AppTheme.TextMuted };
        var lblAdjustSuffix = new Label { Text = "%", ForeColor = AppTheme.TextMuted };

        // A box and its units, and the two dates with "and" between them,
        // each one row of the grid (FieldGrid.Inline) - hidden and shown as
        // a whole, so a hidden row leaves nothing behind.
        _fixedLapRow = FieldGrid.Inline(_numFixedLap, lblFixedSuffix);
        _rangeRow = FieldGrid.Inline(_dtFrom, _lblRangeTo, _dtTo);

        // Where the pace comes from, then the rows that source uses - the
        // driver first, as the thing the rest is about, and the lap time
        // last, as what a live ghost runs at until that driver's laps come
        // in (ShowPaceFields) - then what is done to it. The drop-downs run
        // to the right-hand edge, as the 350px fields did; the number boxes
        // and the dates keep their own widths.
        var pace = new FieldGrid();
        captions.Add(pace.AddRow("Based on:", _cmbPaceSource, stretch: true));

        _lblDriver = pace.AddRow("Driver:", _cmbDriver, stretch: true);
        _lblRange = pace.AddRow("Laps set between:", _rangeRow);
        _lblLiveBasis = pace.AddRow("Follows:", _cmbLiveBasis, stretch: true);
        _lblFixedLap = pace.AddRow("Lap time:", _fixedLapRow);
        captions.AddRange(new[] { _lblDriver, _lblRange, _lblLiveBasis, _lblFixedLap });

        captions.Add(pace.AddRow("Pace adjust:", FieldGrid.Inline(_numPacePercent, lblAdjustSuffix)));
        pace.AddHint(_lblPaceHint);
        captions.Add(pace.AddRow("Consistency:", _cmbConsistency, stretch: true));

        // The fields are the body: on a screen too short for all of them -
        // a small one at a large text size - they scroll, and Save and
        // Cancel under them stay in view.
        var page = new FillPage();
        var body = DialogFit.Body(page);
        body.Add(identity, gapBelow: UiLayout.SectionGap);
        body.Add(lblPaceHeading, stretch: false, gapBelow: UiLayout.HeadingGap);
        body.Add(pace, gapBelow: 0);

        var btnSave = new Button { Text = "Save" };
        btnSave.Click += BtnSave_Click;

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        // Against the right-hand edge, Cancel furthest right, as they were -
        // and Tab still reaches Save first: the bar takes them right to
        // left, in the order they were added.
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnSave), gapBelow: 0);
        btnSave.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = btnSave;
        CancelButton = btnCancel;

        UpdatePaceFields();

        AppTheme.ApplyRecursively(this);

        // The captions in one column down both grids, as wide as the longest
        // of them - the lap time's other wording included, so picking another
        // pace source never moves the fields sideways.
        FieldGrid.LineUp(new[] { identity, pace }, "Lap time:", "Until laps come in:");

        // After the theme pass, in the faces it leaves them in: the two
        // number boxes one width - as they were at the normal text size, at
        // the size in use, and never too narrow for either's widest value
        // with its arrows - and each date as wide as it was.
        var numberWidth = Math.Max(
            UiLayout.AtTextSize(NumberWidth),
            Math.Max(WidestValueWidth(_numFixedLap), WidestValueWidth(_numPacePercent)));

        _numFixedLap.Width = numberWidth;
        _numPacePercent.Width = numberWidth;
        _dtFrom.Width = UiLayout.AtTextSize(DateWidth);
        _dtTo.Width = UiLayout.AtTextSize(DateWidth);

        // As wide as it was at the normal text size, at the size in use - or
        // wider when the fields' column needs more: never narrower than the
        // 350px the fields had, and wide enough for each drop-down's longest
        // choice. At 350px the longest consistency came out cut short, and a
        // long driver's name with their transponder only just fitted. With
        // room for the body's scrollbar, which takes its width from the
        // fields on a screen too short for them. The caption column beside
        // it as the grids work it out: the widest caption and the gap after
        // it. Kept in locals, never read back from the drop-downs - they are
        // in the page already, which cuts them to the width of a window not
        // sized yet.
        (Font Font, int Count, int Width)? sourceWidth = null;
        (Font Font, int Count, int Width)? driverWidth = null;
        (Font Font, int Count, int Width)? basisWidth = null;
        (Font Font, int Count, int Width)? consistencyWidth = null;

        var fieldColumn = new[]
        {
            UiLayout.AtTextSize(FieldColumnWidth),
            Pieces.PickerWidth(_cmbPaceSource, ref sourceWidth),
            Pieces.PickerWidth(_cmbDriver, ref driverWidth),
            Pieces.PickerWidth(_cmbLiveBasis, ref basisWidth),
            Pieces.PickerWidth(_cmbConsistency, ref consistencyWidth),
        }.Max();

        var captionColumn = captions.Max(caption => caption.GetPreferredSize(Size.Empty).Width + caption.Margin.Horizontal);

        var width = Math.Max(
            UiLayout.AtTextSize(DialogWidth),
            UiLayout.PageLeft + captionColumn + fieldColumn + SystemInformation.VerticalScrollBarWidth + UiLayout.PageRight);

        DialogFit.Keep(this, page, width, body: body, widthMeasured: true);

        // What a number box needs across for its largest value and its arrows.
        static int WidestValueWidth(NumericUpDown number) =>
            TextRenderer.MeasureText(
                number.Maximum.ToString("F" + number.DecimalPlaces, System.Globalization.CultureInfo.CurrentCulture),
                number.Font).Width
            + SystemInformation.VerticalScrollBarWidth + 12 + UpDownButtonsSkin.Extra;
    }

    /// <summary>
    /// The window is sized as it loads, to what is showing then (DialogFit).
    /// So the rows of the source that shows the most of them (TallestSource)
    /// are put in view for it, and the chosen source's put back after -
    /// every source then fits the window it opened as, as every one fitted
    /// the old fixed-size window, and picking one with more rows never
    /// scrolls them through a window made for fewer. As RaceFormatForm does
    /// with its fields.
    /// </summary>
    protected override void OnLoad(EventArgs e)
    {
        ShowPaceFields(TallestSource);

        base.OnLoad(e);

        ShowPaceFields(SelectedSource);
    }

    /// <summary>Each field filled in from the ghost, and the handlers that keep the rows following the pace source and the line following the adjustment.</summary>
    private void FillFields()
    {
        _txtName.Text = Result.Name;
        _txtName.PlaceholderText = "e.g. Rival, or Sam's ghost";

        _txtCar.Text = Result.CarModel;
        _txtCar.PlaceholderText = "optional - gives the announcer something to say";

        _chkEnabled.Text = "Enabled";
        _chkEnabled.AutoSize = true;
        _chkEnabled.Checked = Result.Enabled;

        // --- Pace -----------------------------------------------------------

        _cmbPaceSource.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbPaceSource.Items.AddRange(PaceSources.Select(p => (object)p.Label).ToArray());

        var sourceIndex = Array.FindIndex(PaceSources, p => p.Source == Result.PaceSource);
        // YourPersonalBest is the older twin of DriverPersonalBest and has no
        // row of its own, so it lands on the personal-best row.
        _cmbPaceSource.SelectedIndex = sourceIndex >= 0 ? sourceIndex : 1;
        _cmbPaceSource.SelectedIndexChanged += (_, _) => UpdatePaceFields();

        _numFixedLap.DecimalPlaces = 3;
        _numFixedLap.Increment = 0.1m;
        _numFixedLap.Minimum = 0.5m;
        _numFixedLap.Maximum = 120m;
        _numFixedLap.Value = (decimal)Math.Clamp(Result.FixedLapMs / 1000.0, 0.5, 120);

        _cmbDriver.DropDownStyle = ComboBoxStyle.DropDownList;

        foreach (var driver in _drivers)
        {
            _cmbDriver.Items.Add(string.IsNullOrWhiteSpace(driver.DriverName)
                ? driver.Transponder
                : $"{driver.DriverName}  ({driver.Transponder})");
        }

        var existingIndex = _drivers.FindIndex(d =>
            string.Equals(d.Transponder, Result.SourceTransponder, StringComparison.OrdinalIgnoreCase));

        // Not a driver's main transponder - perhaps their alternate one, so
        // looked up as everywhere else does (ProfileStore.Get).
        var hasSource = !string.IsNullOrWhiteSpace(Result.SourceTransponder);
        if (existingIndex < 0 && hasSource && _profiles.Get(Result.SourceTransponder) is { } owner)
        {
            existingIndex = _drivers.FindIndex(d =>
                string.Equals(d.Transponder, owner.Transponder, StringComparison.OrdinalIgnoreCase));
        }

        // A ghost whose driver can't be found any more is left with nobody
        // picked (and keeps its transponder when saved - see BtnSave_Click)
        // rather than quietly moved onto whoever is first in the list.
        if (_cmbDriver.Items.Count > 0 && (existingIndex >= 0 || !hasSource))
        {
            _cmbDriver.SelectedIndex = existingIndex >= 0 ? existingIndex : 0;
        }

        // --- Date range, for a personal best from a chosen window ------------

        _dtFrom.Format = DateTimePickerFormat.Short;
        _dtFrom.Value = (Result.RangeFromUtc ?? DateTime.UtcNow.AddMonths(-3)).ToLocalTime();

        _lblRangeTo.Text = "and";
        _lblRangeTo.ForeColor = AppTheme.TextMuted;

        _dtTo.Format = DateTimePickerFormat.Short;
        _dtTo.Value = (Result.RangeToUtc ?? DateTime.UtcNow).ToLocalTime();

        // --- What a live ghost follows --------------------------------------

        _cmbLiveBasis.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbLiveBasis.Items.AddRange(new object[]
        {
            "Their best lap so far this session",
            "Their recent average lap this session",
        });
        _cmbLiveBasis.SelectedIndex = Result.LiveBasis == GhostLiveBasis.RecentAverage ? 1 : 0;

        // --- Adjustment ------------------------------------------------------

        _numPacePercent.Minimum = 50;
        _numPacePercent.Maximum = 200;
        _numPacePercent.Value = Math.Clamp(Result.PacePercent, 50, 200);
        _numPacePercent.ValueChanged += (_, _) => UpdatePaceHint();

        // Its longest wordings - the most it can be quicker by, and slower by
        // - so the line keeps one height whatever the adjustment, and nothing
        // under it moves as the number changes. The grid gives it the small
        // muted type of a hint (FieldGrid.AddHint).
        _lblPaceHint.Wordings = new[]
        {
            PaceHint(100),
            PaceHint((int)_numPacePercent.Minimum),
            PaceHint((int)_numPacePercent.Maximum),
        };

        // --- Consistency ------------------------------------------------------

        _cmbConsistency.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbConsistency.Items.AddRange(ConsistencyLevels.Select(c => (object)c.Label).ToArray());
        _cmbConsistency.SelectedIndex = Math.Max(0, Array.FindIndex(ConsistencyLevels, c => c.Level == Result.Consistency));
    }

    /// <summary>Shows only the fields the chosen pace source actually uses.</summary>
    private void UpdatePaceFields()
    {
        ShowPaceFields(SelectedSource);
        UpdatePaceHint();
    }

    /// <summary>
    /// Shows the rows <paramref name="source"/> uses and hides the rest -
    /// each row whole, its caption with it, so the grid closes up behind a
    /// hidden one rather than leaving a gap.
    ///
    /// The lap time and the driver have a row each. They are mostly
    /// alternatives, but a live ghost of a driver's own pace uses both - and
    /// in the one place the two used to share, they sat on top of each
    /// other.
    /// </summary>
    private void ShowPaceFields(GhostPaceSource source)
    {
        // A live ghost needs a lap time too: it has nothing to read until
        // the session produces laps, and that is what it runs at until then.
        var wantsFixedLap = source is GhostPaceSource.FixedLapTime
            or GhostPaceSource.LiveOwnPace or GhostPaceSource.LiveLeaderPace;

        var wantsDriver = source is GhostPaceSource.DriverPersonalBest
            or GhostPaceSource.YourPersonalBest or GhostPaceSource.YourRecentAverage
            or GhostPaceSource.PersonalBestInRange or GhostPaceSource.LiveOwnPace
            or GhostPaceSource.OwnBestTonight;

        _lblFixedLap.Text = source == GhostPaceSource.FixedLapTime ? "Lap time:" : "Until laps come in:";
        _lblFixedLap.Visible = wantsFixedLap;
        _fixedLapRow.Visible = wantsFixedLap;

        _lblDriver.Visible = wantsDriver;
        _cmbDriver.Visible = wantsDriver;

        var wantsRange = source == GhostPaceSource.PersonalBestInRange;
        _lblRange.Visible = wantsRange;
        _rangeRow.Visible = wantsRange;

        var wantsBasis = source == GhostPaceSource.LiveOwnPace;
        _lblLiveBasis.Visible = wantsBasis;
        _cmbLiveBasis.Visible = wantsBasis;
    }

    /// <summary>Spells out what the adjustment actually does, in plain terms.</summary>
    private void UpdatePaceHint()
    {
        _lblPaceHint.Text = PaceHint((int)_numPacePercent.Value);
    }

    /// <summary>What an adjustment of <paramref name="percent"/> does - see UpdatePaceHint.</summary>
    private static string PaceHint(int percent) => percent switch
    {
        100 => "Exactly the pace above.",
        < 100 => $"{100 - percent}% QUICKER than the pace above - something to chase.",
        _ => $"{percent - 100}% slower than the pace above - something to stay ahead of.",
    };

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        var name = _txtName.Text.Trim();

        if (name.Length == 0)
        {
            AppDialog.Show(this, "Give the ghost a name - it's what the announcer will call it.",
                "Name required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var source = SelectedSource;

        var needsDriver = source is GhostPaceSource.DriverPersonalBest
            or GhostPaceSource.YourPersonalBest or GhostPaceSource.YourRecentAverage
            or GhostPaceSource.PersonalBestInRange or GhostPaceSource.LiveOwnPace
            or GhostPaceSource.OwnBestTonight;

        if (needsDriver && _drivers.Count == 0)
        {
            AppDialog.Show(this,
                "There are no driver profiles to base a pace on. Use a fixed lap time instead, or add a driver first.",
                "Nobody to copy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (source == GhostPaceSource.PersonalBestInRange && _dtTo.Value.Date < _dtFrom.Value.Date)
        {
            AppDialog.Show(this, "The end of the date range is before the start.",
                "Check the dates", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        Result.Name = name;
        Result.CarModel = _txtCar.Text.Trim();
        Result.Enabled = _chkEnabled.Checked;
        Result.PacePercent = (int)_numPacePercent.Value;
        Result.Consistency = ConsistencyLevels[Math.Clamp(_cmbConsistency.SelectedIndex, 0, ConsistencyLevels.Length - 1)].Level;

        Result.PaceSource = source;

        Result.FixedLapMs = (int)(_numFixedLap.Value * 1000);

        // Nobody picked (its driver couldn't be found when the form opened):
        // the transponder it had is kept, not lost.
        Result.SourceTransponder = !needsDriver
            ? string.Empty
            : _cmbDriver.SelectedIndex >= 0
                ? _drivers[_cmbDriver.SelectedIndex].Transponder
                : Result.SourceTransponder;

        // Stored as UTC, and the end date covers the whole day rather than
        // midnight at its start - "up to the 14th" ought to include the 14th.
        Result.RangeFromUtc = source == GhostPaceSource.PersonalBestInRange
            ? _dtFrom.Value.Date.ToUniversalTime()
            : null;

        Result.RangeToUtc = source == GhostPaceSource.PersonalBestInRange
            ? _dtTo.Value.Date.AddDays(1).AddSeconds(-1).ToUniversalTime()
            : null;

        Result.LiveBasis = _cmbLiveBasis.SelectedIndex == 1
            ? GhostLiveBasis.RecentAverage
            : GhostLiveBasis.BestLapSoFar;

        DialogResult = DialogResult.OK;
    }
}
