namespace HeatCaller;

/// <summary>
/// Everything that describes HOW a race is run, as one object: limits,
/// start procedure, streak rules, finish rule.
///
/// These twelve knobs used to be twelve controls on Race Control, each
/// saving itself into AppSettings - one global answer for every race. But
/// they are not global: a Stock heat and a Modified A-main legitimately
/// differ on nearly all of them, and on race day the person at Race
/// Control should not be re-dialling the countdown between heats. So the
/// format lives on the CLASS (overridable per round), Race Control just
/// reads whichever one applies, and the old AppSettings fields carry on
/// as the app default - the format used when nothing more specific has
/// been chosen, and by Quick Race always.
///
/// The precedence is: round override, else class format, else app
/// default. Resolved in RaceControlControl.EffectiveFormat.
/// </summary>
public sealed class RaceFormat
{
    /// <summary>Race ends after this many leader laps. 0 = no lap limit.</summary>
    public int LapLimit { get; set; }

    /// <summary>Race ends after this many minutes. 0 = no time limit.</summary>
    public int TimeLimitMinutes { get; set; }

    /// <summary>Passes quicker than this are decoder noise, not laps.</summary>
    public int MinLapTimeMs { get; set; } = 3000;

    /// <summary>How far a lap may sit either side of the last one and still extend a streak.</summary>
    public double StreakWindowPercent { get; set; } = 25;

    /// <summary>Whether a much FASTER lap also breaks a streak, not just a slower one.</summary>
    public bool StreakFasterBreaks { get; set; } = true;

    public RaceStartType StartType { get; set; } = RaceStartType.Standard;

    /// <summary>Seconds between staggered releases.</summary>
    public int StaggerGapSeconds { get; set; } = 5;

    public StaggerClock StaggerClockMode { get; set; } = StaggerClock.OwnRelease;

    /// <summary>Stage carries straight on into Countdown and Green by itself.</summary>
    public bool AutoAdvance { get; set; } = true;

    public int StagingSeconds { get; set; } = 15;

    public int CountdownSeconds { get; set; } = 5;

    /// <summary>
    /// Timed free laps before the field is staged. 0 = no warm-up.
    ///
    /// Pressing Stage (or Quick Race) opens the warm-up first; when it runs
    /// out the race is staged as normal. Passes during it are not counted.
    /// Pressing Stage again during the warm-up skips the rest of it.
    ///
    /// Defaults to 0 so every format saved before this existed - classes,
    /// round overrides, the app default - loads exactly as it ran before.
    /// </summary>
    public int WarmUpSeconds { get; set; }

    /// <summary>Leader takes the flag at the limit, everyone else finishes their lap.</summary>
    public bool FinishOnNextCrossing { get; set; } = true;

    /// <summary>
    /// The app default: materialised from the same AppSettings fields the
    /// old per-control saving wrote, so nobody's dialled-in settings reset
    /// on upgrade and older builds still read the file correctly.
    /// </summary>
    public static RaceFormat FromSettings(AppSettings settings) => new()
    {
        LapLimit = Math.Clamp(settings.RaceLapLimit, 0, 9999),
        TimeLimitMinutes = Math.Clamp(settings.RaceTimeLimitMinutes, 0, 180),
        MinLapTimeMs = Math.Clamp(settings.RaceMinLapTimeMs, 100, 60000),
        StreakWindowPercent = Math.Clamp(settings.RaceStreakWindowPercent, 1, 200),
        StreakFasterBreaks = settings.RaceStreakFasterBreaks,
        StartType = settings.RaceStartType,
        StaggerGapSeconds = Math.Clamp(settings.RaceStaggerGapSeconds, 1, 120),
        StaggerClockMode = settings.RaceStaggerClock,
        AutoAdvance = settings.ChainStartSequence,
        StagingSeconds = Math.Clamp(settings.AutoStartStagingSeconds, 1, 300),
        CountdownSeconds = Math.Clamp(settings.AutoStartCountdownSeconds, 1, 60),
        WarmUpSeconds = Math.Clamp(settings.RaceWarmUpSeconds, 0, MaxWarmUpSeconds),
        FinishOnNextCrossing = settings.RaceFinishOnNextCrossing,
    };

    /// <summary>Longest warm-up the editor allows - 30 minutes.</summary>
    public const int MaxWarmUpSeconds = 1800;

    /// <summary>A warm-up length as people say it: "90s", "2 min", "2:30".</summary>
    public static string DescribeWarmUp(int seconds) =>
        seconds < 60 ? $"{seconds}s"
        : seconds % 60 == 0 ? $"{seconds / 60} min"
        : $"{seconds / 60}:{seconds % 60:00}";

    /// <summary>Writes this format back as the app default. Save() is the caller's, so one edit is one write.</summary>
    public void SaveTo(AppSettings settings)
    {
        settings.RaceLapLimit = LapLimit;
        settings.RaceTimeLimitMinutes = TimeLimitMinutes;
        settings.RaceMinLapTimeMs = MinLapTimeMs;
        settings.RaceStreakWindowPercent = StreakWindowPercent;
        settings.RaceStreakFasterBreaks = StreakFasterBreaks;
        settings.RaceStartType = StartType;
        settings.RaceStaggerGapSeconds = StaggerGapSeconds;
        settings.RaceStaggerClock = StaggerClockMode;
        settings.ChainStartSequence = AutoAdvance;
        settings.AutoStartStagingSeconds = StagingSeconds;
        settings.AutoStartCountdownSeconds = CountdownSeconds;
        settings.RaceWarmUpSeconds = WarmUpSeconds;
        settings.RaceFinishOnNextCrossing = FinishOnNextCrossing;
    }

    public RaceFormat Clone() => (RaceFormat)MemberwiseClone();

    /// <summary>The format in one readable line, for the chip on Race Control.</summary>
    public string Describe()
    {
        var length = (LapLimit > 0, TimeLimitMinutes > 0) switch
        {
            (true, true) => $"{Plural.Of(LapLimit, "lap")} / {TimeLimitMinutes} min",
            (true, false) => Plural.Of(LapLimit, "lap"),
            (false, true) => $"{TimeLimitMinutes} min",
            _ => "open-ended",
        };

        var start = StartType switch
        {
            RaceStartType.StaggeredGrid => $"staggered +{StaggerGapSeconds}s",
            RaceStartType.StaggeredReverse => $"reverse stagger +{StaggerGapSeconds}s",
            RaceStartType.StaggeredHandicap => "handicap stagger",
            RaceStartType.HandReleased => "hand released",
            RaceStartType.Qualifier => "own clock from 1st crossing",
            _ => "all away together",
        };

        var warmUp = WarmUpSeconds > 0 ? $" · {DescribeWarmUp(WarmUpSeconds)} warm-up" : string.Empty;

        return AutoAdvance
            ? $"{length} · {start}{warmUp} · auto {StagingSeconds}+{CountdownSeconds}s"
            : $"{length} · {start}{warmUp}";
    }
}

/// <summary>
/// The one editor for a RaceFormat, wherever it is being edited from: the
/// app default (from Race Control), a class's format (from Manage
/// Classes), or a round's override.
///
/// One form rather than three, because the moment there are two copies of
/// a twelve-field editor they disagree about a clamp or a caption and the
/// same format means different things depending on where it was typed.
/// </summary>
public sealed class RaceFormatForm : Form
{
    /// <summary>
    /// The edited format, or null for "use the default" - only ever null
    /// when the form was opened with a default to fall back to.
    /// </summary>
    public RaceFormat? Result { get; private set; }

    private readonly CheckBox _chkCustom = new();
    private readonly NumericUpDown _numLapLimit = new();
    private readonly NumericUpDown _numTimeLimit = new();
    private readonly NumericUpDown _numMinLap = new();
    private readonly NumericUpDown _numStreakWindow = new();
    private readonly CheckBox _chkStreakFaster = new();
    private readonly ComboBox _cboStartType = new();
    private readonly NumericUpDown _numStaggerGap = new();
    private readonly ComboBox _cboStaggerClock = new();
    private readonly CheckBox _chkAutoAdvance = new();
    private readonly NumericUpDown _numStaging = new();
    private readonly NumericUpDown _numCountdown = new();
    private readonly NumericUpDown _numWarmUp = new();
    private readonly CheckBox _chkFinishOnCrossing = new();

    private readonly bool _allowUseDefault;

    /// <summary>What "following the default" actually means right now - shown in place of the fields when custom is unticked.</summary>
    private readonly Label _lblFollowing = new();

    /// <summary>Everything that only exists for a custom format - hidden outright when following the default, because a page of disabled ghost text reads as broken, not as off.</summary>
    private readonly List<Control> _customControls = new();

    private readonly RaceFormat? _fallback;

    /// <param name="current">The format to edit. Null means "currently on the default".</param>
    /// <param name="title">Whose format this is - "Race Format - Stock", "App Default Race Format".</param>
    /// <param name="allowUseDefault">
    /// Whether "use the app default" is an available answer. True for a
    /// class or a round; false when editing the default itself, where the
    /// option would mean nothing.
    /// </param>
    /// <param name="fallback">
    /// The format that applies while "custom" is unticked - the app
    /// default, when the caller has it to hand. Shown so that "follow the
    /// default" is a sentence with numbers in it rather than an act of
    /// faith. Null just drops the numbers.
    /// </param>
    public RaceFormatForm(RaceFormat? current, string title, bool allowUseDefault, RaceFormat? fallback = null)
    {
        _allowUseDefault = allowUseDefault;
        _fallback = fallback;

        Text = title;
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = AppTheme.BodyFont;

        var seed = current?.Clone() ?? new RaceFormat();

        BuildLayout(seed, current is not null);
        AppTheme.ApplyRecursively(this);

        // One full repaint after everything has settled. The theme pass
        // recreates the combos' handles (owner-draw changes do that) and
        // nudges sizes by a pixel or two, and the ghost of a control's
        // pre-theme border can survive on the form surface at its OLD
        // bounds - which is exactly the stray frame that showed up around
        // the two dropdowns here. Invalidate(true) at Shown redraws the
        // form and every child once, after the last of that has happened.
        Shown += (_, _) => Invalidate(true);
    }

    private void BuildLayout(RaceFormat seed, bool startsCustom)
    {
        var y = UiLayout.PageTop;

        if (_allowUseDefault)
        {
            _chkCustom.Text = "Use a custom format (untick to follow the app default)";
            _chkCustom.AutoSize = true;
            _chkCustom.Font = AppTheme.BodyFont;
            _chkCustom.Checked = startsCustom;
            _chkCustom.Location = new Point(UiLayout.PageLeft, y);
            _chkCustom.CheckedChanged += (_, _) => SetCustomVisible(_chkCustom.Checked);
            Controls.Add(_chkCustom);

            y = UiLayout.Below(_chkCustom, UiLayout.RowGap);

            // Sits where the field rows sit; the two are never visible at
            // once, so they can share the space.
            _lblFollowing.AutoSize = true;
            _lblFollowing.Font = AppTheme.BodyFont;
            _lblFollowing.ForeColor = AppTheme.TextMuted;
            _lblFollowing.MaximumSize = new Size(430, 0);
            _lblFollowing.Location = new Point(UiLayout.PageLeft, y);
            _lblFollowing.Text = _fallback is null
                ? "Following the app default. Tick the box above to give this a format of its own."
                : "Following the app default:\n\n" + _fallback.Describe()
                  + "\n\nTick the box above to give this a format of its own.";

            Controls.Add(_lblFollowing);
        }

        // A caption and its field on one row, flowing downward. Every
        // control is measured in the font it will be drawn in - see the
        // Check-In rewrite for what happens otherwise.
        //
        // The field column starts at a shared x but gives way to a caption
        // that needs more - a fixed column against these captions is how
        // the old panel ended up with "Lap limit" and the rest cut off.
        // The dialog's own width is settled at the end from the widest row.
        var widest = 0;

        int Row(string caption, Control field, Control? trailing = null)
        {
            var lbl = new Label { Text = caption, AutoSize = true, Font = AppTheme.BodyFont };

            lbl.Location = new Point(UiLayout.PageLeft, y);

            field.Font = AppTheme.BodyFont;
            field.Location = new Point(Math.Max(250, UiLayout.After(lbl)), y);
            lbl.Location = new Point(UiLayout.PageLeft, UiLayout.CentreOn(lbl, y, field.Height));

            Controls.Add(lbl);
            Controls.Add(field);
            _customControls.Add(lbl);
            _customControls.Add(field);

            widest = Math.Max(widest, field.Right);

            if (trailing is not null)
            {
                trailing.Font = AppTheme.BodyFont;
                trailing.Location = trailing is CheckBox or Label
                    ? new Point(UiLayout.After(field) + 4, UiLayout.CentreOn(trailing, y, field.Height))
                    : new Point(UiLayout.After(field) + 4, y);
                Controls.Add(trailing);
                _customControls.Add(trailing);

                widest = Math.Max(widest, trailing.Right);
            }

            y = UiLayout.Below(field, UiLayout.RowGap);

            return y;
        }

        _numLapLimit.Width = 70;
        _numLapLimit.Maximum = 9999;
        _numLapLimit.Value = Math.Clamp(seed.LapLimit, 0, 9999);
        Row("Lap limit (0 = none):", _numLapLimit);

        _numTimeLimit.Width = 70;
        _numTimeLimit.Maximum = 180;
        _numTimeLimit.Value = Math.Clamp(seed.TimeLimitMinutes, 0, 180);
        Row("Time limit, min (0 = none):", _numTimeLimit);

        _numMinLap.Width = 80;
        _numMinLap.Minimum = 100;
        _numMinLap.Maximum = 60000;
        _numMinLap.Increment = 100;
        _numMinLap.Value = Math.Clamp(seed.MinLapTimeMs, 100, 60000);
        Row("Min valid lap (ms):", _numMinLap);

        _numStreakWindow.Width = 70;
        _numStreakWindow.Minimum = 1;
        _numStreakWindow.Maximum = 200;
        _numStreakWindow.Value = (decimal)Math.Clamp(seed.StreakWindowPercent, 1, 200);
        _chkStreakFaster.Text = "faster too";
        _chkStreakFaster.AutoSize = true;
        _chkStreakFaster.Checked = seed.StreakFasterBreaks;
        Row("Streak window ±%:", _numStreakWindow, _chkStreakFaster);

        _cboStartType.Width = 200;
        _cboStartType.DropDownStyle = ComboBoxStyle.DropDownList;
        // The two qualifying philosophies are named rather than left for
        // somebody to work out from "Standard". They are both simultaneous
        // starts and they differ in ONE thing - whether the run from the
        // grid down to the timing loop is on your clock - and picking the
        // wrong one changes every driver's qualifying time by that much,
        // silently, with a perfectly plausible result at the end of it.
        // ITEMS THAT CARRY THEIR OWN VALUE, not a list whose position in
        // the box is silently the enum's number.
        //
        // That coupling is why this could not simply have an entry deleted
        // from it: removing one caption shifts every caption after it down
        // a place, and the dialog would then save a start type nobody
        // picked - a data change with no symptom until a race starts the
        // wrong way. Each row now names the value it means.
        _cboStartType.Items.Add(new StartItem(RaceStartType.Standard, "Standard - all away together, one clock"));
        _cboStartType.Items.Add(new StartItem(RaceStartType.StaggeredGrid, "Staggered - grid order"));
        _cboStartType.Items.Add(new StartItem(RaceStartType.StaggeredReverse, "Staggered - reverse grid"));
        _cboStartType.Items.Add(new StartItem(RaceStartType.StaggeredHandicap, "Staggered - handicap"));

        // Hand released is no longer offered. It stays in the list ONLY for
        // a format that is already set to it, so that opening this dialog
        // on such a format and pressing OK does not quietly change how that
        // class starts. Nothing new can be set to it, and anything already
        // set to it still runs - Race Control's release path is untouched.
        if (seed.StartType == RaceStartType.HandReleased)
        {
            _cboStartType.Items.Add(new StartItem(RaceStartType.HandReleased, "Hand released (no longer offered)"));
        }

        _cboStartType.Items.Add(new StartItem(RaceStartType.Qualifier, "Qualifying - own clock from 1st crossing"));

        SelectStartType(seed.StartType);

        _cboStartType.SelectedIndexChanged += (_, _) => UpdateStaggerEnabled();
        Row("Start:", _cboStartType);

        var lblStartHint = new Label
        {
            Text = "For qualifying, the two that matter are the first and the last. \"All away together\" is a "
                 + "simultaneous grid start on one shared clock - PN Racing's PNWC qualifiers work this way, and "
                 + "the run to the loop is on your time. \"Own clock from 1st crossing\" is the IFMAR convention, "
                 + "where each driver is measured over their own laps alone.",
            AutoSize = true,
            MaximumSize = new Size(440, 0),
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
            Location = new Point(UiLayout.PageLeft, y),
        };

        Controls.Add(lblStartHint);
        _customControls.Add(lblStartHint);

        y = UiLayout.Below(lblStartHint, UiLayout.RowGap);

        _numStaggerGap.Width = 70;
        _numStaggerGap.Minimum = 1;
        _numStaggerGap.Maximum = 120;
        _numStaggerGap.Value = Math.Clamp(seed.StaggerGapSeconds, 1, 120);
        _cboStaggerClock.Width = 190;
        _cboStaggerClock.DropDownStyle = ComboBoxStyle.DropDownList;
        // Items carry their value, the same as the start-type list above.
        //
        // This read (StaggerClock)SelectedIndex and restored with
        // Math.Clamp(..., 0, 1), which is the fault the start-type box was
        // fixed for, left behind one row further down. A third clock mode
        // would have been mis-read by position - and the clamp would have
        // quietly rewritten any format already set to it back to "shared
        // green" the moment somebody opened this dialog and pressed OK.
        _cboStaggerClock.Items.Add(new ClockItem(StaggerClock.OwnRelease, "Clock: own release"));
        _cboStaggerClock.Items.Add(new ClockItem(StaggerClock.SharedGreen, "Clock: shared green"));
        SelectStaggerClock(seed.StaggerClockMode);
        Row("Stagger gap (s):", _numStaggerGap, _cboStaggerClock);

        // Before the start-sequence rows because it happens before them:
        // warm-up, then Stage -> Countdown -> Green.
        _numWarmUp.Width = 80;
        _numWarmUp.Minimum = 0;
        _numWarmUp.Maximum = RaceFormat.MaxWarmUpSeconds;
        _numWarmUp.Increment = 30;
        _numWarmUp.Value = Math.Clamp(seed.WarmUpSeconds, 0, RaceFormat.MaxWarmUpSeconds);
        var lblWarmUpHint = new Label
        {
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        void UpdateWarmUpHint() =>
            lblWarmUpHint.Text = _numWarmUp.Value > 0
                ? RaceFormat.DescribeWarmUp((int)_numWarmUp.Value)
                : "off";

        UpdateWarmUpHint();
        _numWarmUp.ValueChanged += (_, _) => UpdateWarmUpHint();
        Row("Warm-up (s, 0 = none):", _numWarmUp, lblWarmUpHint);

        _chkAutoAdvance.Text = "Run the start sequence automatically (Stage \u2192 Countdown \u2192 Green)";
        _chkAutoAdvance.AutoSize = true;
        _chkAutoAdvance.Font = AppTheme.BodyFont;
        _chkAutoAdvance.Checked = seed.AutoAdvance;
        _chkAutoAdvance.Location = new Point(UiLayout.PageLeft, y);
        Controls.Add(_chkAutoAdvance);
        _customControls.Add(_chkAutoAdvance);
        y = UiLayout.Below(_chkAutoAdvance, UiLayout.RowGap);

        _numStaging.Width = 70;
        _numStaging.Minimum = 1;
        _numStaging.Maximum = 300;
        _numStaging.Value = Math.Clamp(seed.StagingSeconds, 1, 300);
        _numCountdown.Width = 70;
        _numCountdown.Minimum = 1;
        _numCountdown.Maximum = 60;
        _numCountdown.Value = Math.Clamp(seed.CountdownSeconds, 1, 60);
        Row("Stage (s):", _numStaging);
        Row("Countdown (s):", _numCountdown);

        _chkFinishOnCrossing.Text = "Leader takes the flag, others finish their lap";
        _chkFinishOnCrossing.AutoSize = true;
        _chkFinishOnCrossing.Font = AppTheme.BodyFont;
        _chkFinishOnCrossing.Checked = seed.FinishOnNextCrossing;
        _chkFinishOnCrossing.Location = new Point(UiLayout.PageLeft, y);
        Controls.Add(_chkFinishOnCrossing);
        _customControls.Add(_chkFinishOnCrossing);
        y = UiLayout.Below(_chkFinishOnCrossing, UiLayout.SectionGap);

        var btnOk = new Button { Text = "OK", Font = AppTheme.BodyFont };
        btnOk.Size = new Size(UiLayout.WidthFor(btnOk), UiLayout.ButtonHeight);
        btnOk.Location = new Point(UiLayout.PageLeft, y);
        btnOk.Click += (_, _) =>
        {
            Result = _allowUseDefault && !_chkCustom.Checked ? null : Collect();
            DialogResult = DialogResult.OK;
        };

        var btnCancel = new Button { Text = "Cancel", Font = AppTheme.BodyFont, DialogResult = DialogResult.Cancel };
        btnCancel.Size = new Size(UiLayout.WidthFor(btnCancel), UiLayout.ButtonHeight);
        btnCancel.Location = new Point(UiLayout.After(btnOk) + UiLayout.RowGap, y);

        Controls.Add(btnOk);
        Controls.Add(btnCancel);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        ClientSize = new Size(
            Math.Max(470, widest + UiLayout.PageRight),
            UiLayout.Below(btnOk, UiLayout.PageTop));

        UpdateStaggerEnabled();
        SetCustomVisible(!_allowUseDefault || _chkCustom.Checked);
    }

    /// <summary>One row of the stagger-clock list, and the value it stands for.</summary>
    private sealed record ClockItem(StaggerClock Mode, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Picks the row for a clock mode, falling back to the first row. A
    /// mode this dialog does not list - from a newer build, say - shows
    /// as own-release rather than as a blank box, and is only changed if
    /// somebody actually saves.
    /// </summary>
    private void SelectStaggerClock(StaggerClock mode)
    {
        for (var i = 0; i < _cboStaggerClock.Items.Count; i++)
        {
            if (_cboStaggerClock.Items[i] is ClockItem item && item.Mode == mode)
            {
                _cboStaggerClock.SelectedIndex = i;
                return;
            }
        }

        if (_cboStaggerClock.Items.Count > 0)
        {
            _cboStaggerClock.SelectedIndex = 0;
        }
    }

    /// <summary>One row of the start-type list, and the value it stands for.</summary>
    private sealed record StartItem(RaceStartType Type, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>The start type currently chosen, from the item rather than from its position.</summary>
    private RaceStartType SelectedStartType() =>
        (_cboStartType.SelectedItem as StartItem)?.Type ?? RaceStartType.Standard;

    /// <summary>
    /// Picks the row for a value. Falls back to the first row rather than
    /// leaving nothing selected - a start-type box with no selection would
    /// collect as Standard anyway, and showing that is better than hiding
    /// it.
    /// </summary>
    private void SelectStartType(RaceStartType type)
    {
        for (var i = 0; i < _cboStartType.Items.Count; i++)
        {
            if (_cboStartType.Items[i] is StartItem item && item.Type == type)
            {
                _cboStartType.SelectedIndex = i;
                return;
            }
        }

        _cboStartType.SelectedIndex = 0;
    }

    private void UpdateStaggerEnabled()
    {
        var type = SelectedStartType();

        _numStaggerGap.Enabled = type is RaceStartType.StaggeredGrid or RaceStartType.StaggeredReverse;

        // A qualifying start has no clock to choose either: own-clock-from-
        // first-crossing IS the format.
        _cboStaggerClock.Enabled = type is not (RaceStartType.Standard or RaceStartType.Qualifier);
    }

    /// <summary>
    /// Custom on: the fields. Custom off: one readable sentence about the
    /// default. HIDDEN, not disabled - a dialog of grayed-out ghost text
    /// is unreadable in a dark theme and looks broken rather than off,
    /// which is exactly how it looked before this.
    /// </summary>
    private void SetCustomVisible(bool custom)
    {
        foreach (var control in _customControls)
        {
            control.Visible = custom;
        }

        _lblFollowing.Visible = _allowUseDefault && !custom;

        if (custom)
        {
            UpdateStaggerEnabled();
        }
    }

    private RaceFormat Collect() => new()
    {
        LapLimit = (int)_numLapLimit.Value,
        TimeLimitMinutes = (int)_numTimeLimit.Value,
        MinLapTimeMs = (int)_numMinLap.Value,
        StreakWindowPercent = (double)_numStreakWindow.Value,
        StreakFasterBreaks = _chkStreakFaster.Checked,
        StartType = SelectedStartType(),
        StaggerGapSeconds = (int)_numStaggerGap.Value,
        StaggerClockMode = (_cboStaggerClock.SelectedItem as ClockItem)?.Mode ?? StaggerClock.OwnRelease,
        AutoAdvance = _chkAutoAdvance.Checked,
        StagingSeconds = (int)_numStaging.Value,
        CountdownSeconds = (int)_numCountdown.Value,
        WarmUpSeconds = (int)_numWarmUp.Value,
        FinishOnNextCrossing = _chkFinishOnCrossing.Checked,
    };
}
