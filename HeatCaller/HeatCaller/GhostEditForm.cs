namespace HeatCaller;

/// <summary>
/// Creates or edits one virtual opponent.
///
/// The pace source drives what the rest of the form means, so the fields
/// that don't apply are hidden rather than greyed - a form where half the
/// boxes are permanently dead is harder to read than one that changes.
/// </summary>
public sealed class GhostEditForm : Form
{
    private readonly ProfileStore _profiles;
    private readonly List<DriverProfile> _drivers;

    private readonly TextBox _txtName = new();
    private readonly TextBox _txtCar = new();
    private readonly CheckBox _chkEnabled = new();

    private readonly ComboBox _cmbPaceSource = new();
    private readonly ComboBox _cmbDriver = new();
    private readonly NumericUpDown _numFixedLap = new();
    private readonly NumericUpDown _numPacePercent = new();
    private readonly ComboBox _cmbConsistency = new();

    private readonly Label _lblDriver = new();
    private readonly Label _lblFixedLap = new();
    private readonly Label _lblPaceHint = new();

    private readonly ComboBox _cmbLiveBasis = new();
    private readonly Label _lblLiveBasis = new();
    private readonly DateTimePicker _dtFrom = new();
    private readonly DateTimePicker _dtTo = new();
    private readonly Label _lblRange = new();
    private readonly Label _lblRangeTo = new();

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
        (GhostConsistency.Erratic, "Erratic - fast laps and bad ones, good for practising recovery"),
    };

    private static readonly (GhostPaceSource Source, string Label)[] PaceSources =
    {
        (GhostPaceSource.FixedLapTime, "A fixed lap time"),
        (GhostPaceSource.DriverPersonalBest, "A driver's personal best on this track"),
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
            };

        Text = existing is null ? "Add Ghost Car" : "Edit Ghost Car";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        // Two rows taller than it was: the date range and the live-pace
        // basis sit between the driver picker and the adjustment, and a
        // fixed-size dialog doesn't grow to fit them on its own.
        ClientSize = new Size(520, 498);
        Font = AppTheme.BodyFont;

        // Background set BEFORE theming. ApplyRecursively gives controls the
        // dark theme's light text, so a form left at the default light
        // background renders every label invisible - which is exactly what
        // happened here.
        BackColor = AppTheme.Background;

        BuildLayout();
        UpdatePaceFields();

        AppTheme.ApplyRecursively(this);
    }

    private void BuildLayout()
    {
        var y = 16;

        AddRow("Name:", _txtName, ref y, Result.Name);
        _txtName.PlaceholderText = "e.g. Rival, or Sam's ghost";

        AddRow("Car:", _txtCar, ref y, Result.CarModel);
        _txtCar.PlaceholderText = "optional - gives the announcer something to say";

        _chkEnabled.Text = "Enabled";
        _chkEnabled.Location = new Point(150, y);
        _chkEnabled.AutoSize = true;
        _chkEnabled.Checked = Result.Enabled;
        Controls.Add(_chkEnabled);
        y += 36;

        // --- Pace -----------------------------------------------------------

        var lblPaceHeading = new Label
        {
            Text = "PACE",
            Location = new Point(16, y),
            AutoSize = true,
            Font = AppTheme.BoldFont,
            ForeColor = AppTheme.Primary,
        };
        Controls.Add(lblPaceHeading);
        y += 26;

        var lblSource = new Label { Text = "Based on:", Location = new Point(16, y + 3), AutoSize = false, Width = 128 };
        _cmbPaceSource.Location = new Point(150, y);
        _cmbPaceSource.Width = 350;
        _cmbPaceSource.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbPaceSource.Items.AddRange(PaceSources.Select(p => (object)p.Label).ToArray());

        var sourceIndex = Array.FindIndex(PaceSources, p => p.Source == Result.PaceSource);
        // YourPersonalBest is the older twin of DriverPersonalBest and has no
        // row of its own, so it lands on the personal-best row.
        _cmbPaceSource.SelectedIndex = sourceIndex >= 0 ? sourceIndex : 1;
        _cmbPaceSource.SelectedIndexChanged += (_, _) => UpdatePaceFields();

        Controls.Add(lblSource);
        Controls.Add(_cmbPaceSource);
        y += 34;

        _lblFixedLap.Text = "Lap time:";
        _lblFixedLap.Location = new Point(16, y + 3);
        _lblFixedLap.AutoSize = false;
        _lblFixedLap.Width = 128;

        _numFixedLap.Location = new Point(150, y);
        _numFixedLap.Width = 90;
        _numFixedLap.DecimalPlaces = 3;
        _numFixedLap.Increment = 0.1m;
        _numFixedLap.Minimum = 0.5m;
        _numFixedLap.Maximum = 120m;
        _numFixedLap.Value = (decimal)Math.Clamp(Result.FixedLapMs / 1000.0, 0.5, 120);

        var lblFixedSuffix = new Label { Text = "seconds", Location = new Point(248, y + 3), AutoSize = true, ForeColor = AppTheme.TextMuted };

        _lblDriver.Text = "Driver:";
        _lblDriver.Location = new Point(16, y + 3);
        _lblDriver.AutoSize = false;
        _lblDriver.Width = 128;

        _cmbDriver.Location = new Point(150, y);
        _cmbDriver.Width = 350;
        _cmbDriver.DropDownStyle = ComboBoxStyle.DropDownList;

        foreach (var driver in _drivers)
        {
            _cmbDriver.Items.Add(string.IsNullOrWhiteSpace(driver.DriverName)
                ? driver.Transponder
                : $"{driver.DriverName}  ({driver.Transponder})");
        }

        var existingIndex = _drivers.FindIndex(d =>
            string.Equals(d.Transponder, Result.SourceTransponder, StringComparison.OrdinalIgnoreCase));

        if (_cmbDriver.Items.Count > 0)
        {
            _cmbDriver.SelectedIndex = existingIndex >= 0 ? existingIndex : 0;
        }

        Controls.Add(_lblFixedLap);
        Controls.Add(_numFixedLap);
        Controls.Add(lblFixedSuffix);
        Controls.Add(_lblDriver);
        Controls.Add(_cmbDriver);
        y += 34;

        // --- Date range, for a personal best from a chosen window ------------

        _lblRange.Text = "Laps set between:";
        _lblRange.Location = new Point(16, y + 3);
        _lblRange.AutoSize = false;
        _lblRange.Width = 128;

        _dtFrom.Location = new Point(150, y);
        _dtFrom.Width = 150;
        _dtFrom.Format = DateTimePickerFormat.Short;
        _dtFrom.Value = (Result.RangeFromUtc ?? DateTime.UtcNow.AddMonths(-3)).ToLocalTime();

        _lblRangeTo.Text = "and";
        _lblRangeTo.Location = new Point(308, y + 3);
        _lblRangeTo.AutoSize = true;
        _lblRangeTo.ForeColor = AppTheme.TextMuted;

        _dtTo.Location = new Point(344, y);
        _dtTo.Width = 150;
        _dtTo.Format = DateTimePickerFormat.Short;
        _dtTo.Value = (Result.RangeToUtc ?? DateTime.UtcNow).ToLocalTime();

        Controls.Add(_lblRange);
        Controls.Add(_dtFrom);
        Controls.Add(_lblRangeTo);
        Controls.Add(_dtTo);
        y += 34;

        // --- What a live ghost follows --------------------------------------

        _lblLiveBasis.Text = "Follows:";
        _lblLiveBasis.Location = new Point(16, y + 3);
        _lblLiveBasis.AutoSize = false;
        _lblLiveBasis.Width = 128;

        _cmbLiveBasis.Location = new Point(150, y);
        _cmbLiveBasis.Width = 350;
        _cmbLiveBasis.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbLiveBasis.Items.AddRange(new object[]
        {
            "Their best lap so far this session",
            "Their recent average lap this session",
        });
        _cmbLiveBasis.SelectedIndex = Result.LiveBasis == GhostLiveBasis.RecentAverage ? 1 : 0;

        Controls.Add(_lblLiveBasis);
        Controls.Add(_cmbLiveBasis);
        y += 34;

        // --- Adjustment ------------------------------------------------------

        var lblAdjust = new Label { Text = "Pace adjust:", Location = new Point(16, y + 3), AutoSize = false, Width = 128 };
        _numPacePercent.Location = new Point(150, y);
        _numPacePercent.Width = 90;
        _numPacePercent.Minimum = 50;
        _numPacePercent.Maximum = 200;
        _numPacePercent.Value = Math.Clamp(Result.PacePercent, 50, 200);
        _numPacePercent.ValueChanged += (_, _) => UpdatePaceHint();

        var lblAdjustSuffix = new Label { Text = "%", Location = new Point(248, y + 3), AutoSize = true, ForeColor = AppTheme.TextMuted };

        Controls.Add(lblAdjust);
        Controls.Add(_numPacePercent);
        Controls.Add(lblAdjustSuffix);
        y += 28;

        _lblPaceHint.Location = new Point(150, y);
        _lblPaceHint.AutoSize = true;
        _lblPaceHint.MaximumSize = new Size(350, 0);
        _lblPaceHint.ForeColor = AppTheme.TextMuted;
        _lblPaceHint.Font = AppTheme.SmallFont;
        Controls.Add(_lblPaceHint);
        y += 34;

        // --- Consistency ------------------------------------------------------

        var lblConsistency = new Label { Text = "Consistency:", Location = new Point(16, y + 3), AutoSize = false, Width = 128 };
        _cmbConsistency.Location = new Point(150, y);
        _cmbConsistency.Width = 350;
        _cmbConsistency.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbConsistency.Items.AddRange(ConsistencyLevels.Select(c => (object)c.Label).ToArray());
        _cmbConsistency.SelectedIndex = Math.Max(0, Array.FindIndex(ConsistencyLevels, c => c.Level == Result.Consistency));

        Controls.Add(lblConsistency);
        Controls.Add(_cmbConsistency);
        y += 44;

        var btnSave = new Button { Text = "Save", Location = new Point(328, y), Width = 84 };
        btnSave.Click += BtnSave_Click;

        var btnCancel = new Button { Text = "Cancel", Location = new Point(420, y), Width = 84, DialogResult = DialogResult.Cancel };

        Controls.Add(btnSave);
        Controls.Add(btnCancel);

        AcceptButton = btnSave;
        CancelButton = btnCancel;

        UpdatePaceHint();
    }

    private void AddRow(string label, TextBox box, ref int y, string value)
    {
        var lbl = new Label { Text = label, Location = new Point(16, y + 3), AutoSize = false, Width = 128 };
        box.Location = new Point(150, y);
        box.Width = 350;
        box.Text = value;

        Controls.Add(lbl);
        Controls.Add(box);
        y += 32;
    }

    /// <summary>
    /// Shows only the fields the chosen pace source actually uses.
    ///
    /// Both live at the same position - a lap-time box and a driver picker
    /// are alternatives, not two separate settings, and stacking them would
    /// make the form taller for no gain.
    /// </summary>
    private void UpdatePaceFields()
    {
        var source = SelectedSource;

        // A live ghost needs a lap time too: it has nothing to read until
        // the session produces laps, and that is what it runs at until then.
        var wantsFixedLap = source is GhostPaceSource.FixedLapTime
            or GhostPaceSource.LiveOwnPace or GhostPaceSource.LiveLeaderPace;

        var wantsDriver = source is GhostPaceSource.DriverPersonalBest
            or GhostPaceSource.YourPersonalBest or GhostPaceSource.YourRecentAverage
            or GhostPaceSource.PersonalBestInRange or GhostPaceSource.LiveOwnPace;

        _lblFixedLap.Text = source == GhostPaceSource.FixedLapTime ? "Lap time:" : "Until laps come in:";
        _lblFixedLap.Visible = wantsFixedLap;
        _numFixedLap.Visible = wantsFixedLap;

        _lblDriver.Visible = wantsDriver;
        _cmbDriver.Visible = wantsDriver;

        var wantsRange = source == GhostPaceSource.PersonalBestInRange;
        _lblRange.Visible = wantsRange;
        _dtFrom.Visible = wantsRange;
        _lblRangeTo.Visible = wantsRange;
        _dtTo.Visible = wantsRange;

        var wantsBasis = source == GhostPaceSource.LiveOwnPace;
        _lblLiveBasis.Visible = wantsBasis;
        _cmbLiveBasis.Visible = wantsBasis;

        UpdatePaceHint();
    }

    /// <summary>Spells out what the adjustment actually does, in plain terms.</summary>
    private void UpdatePaceHint()
    {
        var percent = (int)_numPacePercent.Value;

        _lblPaceHint.Text = percent switch
        {
            100 => "Exactly the pace above.",
            < 100 => $"{100 - percent}% QUICKER than the pace above - something to chase.",
            _ => $"{percent - 100}% slower than the pace above - something to stay ahead of.",
        };
    }

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
            or GhostPaceSource.PersonalBestInRange or GhostPaceSource.LiveOwnPace;

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

        Result.SourceTransponder = needsDriver && _cmbDriver.SelectedIndex >= 0
            ? _drivers[_cmbDriver.SelectedIndex].Transponder
            : string.Empty;

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
