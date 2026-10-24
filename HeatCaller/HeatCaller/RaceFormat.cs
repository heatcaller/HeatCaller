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
    /// <remarks>
    /// WHY (23 Oct 2026, timing layer P6): the words themselves moved into
    /// the timing library (WarmUpText), because the race engine logs them
    /// when a warm-up opens and the library cannot reach RaceFormat - which
    /// reads AppSettings and carries the format editor. Kept here as the
    /// name every screen already calls.
    /// </remarks>
    public static string DescribeWarmUp(int seconds) => WarmUpText.Describe(seconds);

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
///
/// On the layout pieces (DialogFit): the "custom format" tick at the top,
/// the fields under it in one field grid - the captions in a column as wide
/// as the longest of them, the note on the start types and the two ticks
/// across both columns - or, in their place while the default is followed,
/// the sentence about the default; then OK and Cancel at the foot. All but
/// the buttons is the window's body, which scrolls on a screen too short
/// for it. It used to be placed in pixels: the fields at a fixed x that gave
/// way to a longer caption, the window as wide as its widest row and as
/// tall as its last, and nothing to stop that running off a small screen
/// with OK and Cancel under it.
/// </summary>
public sealed class RaceFormatForm : Form
{
    /// <summary>
    /// How wide the window is inside its frame at the normal text size.
    ///
    /// The old one worked 538 out for itself - its widest row, the stagger
    /// gap and its clock, ended at 522, and a page margin past that - with
    /// the start types in a 200px box, too narrow for the longest of them.
    /// Measured, that box needs about 305px, and this leaves it that beside
    /// the captions with the body's scrollbar showing, on a screen too short
    /// for all the fields.
    /// </summary>
    private const int DialogWidth = 560;

    /// <summary>
    /// The edited format, or null for "use the default" - only ever null
    /// when the form was opened with a default to fall back to.
    /// </summary>
    public RaceFormat? Result { get; private set; }

    private readonly CheckBox _chkCustom = new ThemedCheckBox();
    private readonly NumericUpDown _numLapLimit = new();
    private readonly NumericUpDown _numTimeLimit = new();
    private readonly NumericUpDown _numMinLap = new();
    private readonly NumericUpDown _numStreakWindow = new();
    private readonly CheckBox _chkStreakFaster = new ThemedCheckBox();
    private readonly ComboBox _cboStartType = new();
    private readonly NumericUpDown _numStaggerGap = new();
    private readonly ComboBox _cboStaggerClock = new();
    private readonly CheckBox _chkAutoAdvance = new ThemedCheckBox();
    private readonly NumericUpDown _numStaging = new();
    private readonly NumericUpDown _numCountdown = new();
    private readonly NumericUpDown _numWarmUp = new();
    private readonly CheckBox _chkFinishOnCrossing = new ThemedCheckBox();

    private readonly bool _allowUseDefault;

    /// <summary>What "following the default" actually means right now - shown in place of the fields when custom is unticked.</summary>
    private readonly Label _lblFollowing = new();

    /// <summary>Everything that only exists for a custom format - hidden outright when following the default, because a page of disabled ghost text reads as broken, not as off.</summary>
    private readonly FieldGrid _fields = new();

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
        Font = AppTheme.BodyFont;

        // What the fields start from. When this is following the default
        // (current is null), ticking "custom" shows these fields as they
        // are - so they start from the format that applies now, the
        // fallback, and changing one setting changes only that setting.
        // They used to start from the built-in values (a 3000 ms minimum
        // lap, 15 s staging, no limits), which quietly undid the club's own
        // default for everything the director did not touch. The built-ins
        // only when the caller has no fallback to give.
        var seed = current?.Clone() ?? fallback?.Clone() ?? new RaceFormat();

        var (page, body) = BuildLayout(seed, current is not null);
        AppTheme.ApplyRecursively(this);

        // The fields' widths, measured in the faces the theme pass leaves
        // them in.
        SizeFields();

        // One full repaint after everything has settled. The theme pass
        // recreates the combos' handles (owner-draw changes do that) and
        // nudges sizes by a pixel or two, and the ghost of a control's
        // pre-theme border can survive on the form surface at its OLD
        // bounds - which is exactly the stray frame that showed up around
        // the two dropdowns here. Invalidate(true) at Shown redraws the
        // form and every child once, after the last of that has happened.
        Shown += (_, _) => Invalidate(true);

        DialogFit.Keep(this, page, width: DialogWidth, body: body);
    }

    /// <summary>
    /// The page: the fields, or the sentence about the default, in the body,
    /// and the buttons under it. Returns both, for DialogFit.
    /// </summary>
    private (FillPage Page, PageStack Body) BuildLayout(RaceFormat seed, bool startsCustom)
    {
        var page = new FillPage();

        // Everything but the buttons: on a screen too short for all the
        // fields, they scroll and OK and Cancel stay in view.
        var body = DialogFit.Body(page);

        if (_allowUseDefault)
        {
            _chkCustom.Text = "Use a custom format (untick to follow the app default)";
            _chkCustom.AutoSize = true;
            _chkCustom.Checked = startsCustom;
            _chkCustom.CheckedChanged += (_, _) => SetCustomVisible(_chkCustom.Checked);
            body.Add(_chkCustom, stretch: false, gapBelow: UiLayout.RowGap);

            // Sits where the fields sit; the two are never visible at once,
            // so they take turns in the same place. Nothing is under it while
            // it shows, so no gap either.
            _lblFollowing.AutoSize = true;
            _lblFollowing.ForeColor = AppTheme.TextMuted;
            _lblFollowing.Text = _fallback is null
                ? "Following the app default."
                : "Following the app default:\n\n" + _fallback.Describe();

            body.Add(_lblFollowing, gapBelow: 0);
        }

        // A caption and its field on each row. The captions share one
        // column, as wide as the longest of them at the text size in use -
        // a fixed column against these captions is how the old panel ended
        // up with "Lap limit" and the rest cut off - and the fields line up
        // beside them. Their widths are set after the theme pass
        // (SizeFields).
        _numLapLimit.Maximum = 9999;
        _numLapLimit.Value = Math.Clamp(seed.LapLimit, 0, 9999);
        _fields.AddRow("Lap limit (0 = none):", _numLapLimit);

        _numTimeLimit.Maximum = 180;
        _numTimeLimit.Value = Math.Clamp(seed.TimeLimitMinutes, 0, 180);
        _fields.AddRow("Time limit, min (0 = none):", _numTimeLimit);

        _numMinLap.Minimum = 100;
        _numMinLap.Maximum = 60000;
        _numMinLap.Increment = 100;
        _numMinLap.Value = Math.Clamp(seed.MinLapTimeMs, 100, 60000);
        _fields.AddRow("Min valid lap (ms):", _numMinLap);

        _numStreakWindow.Minimum = 1;
        _numStreakWindow.Maximum = 200;
        _numStreakWindow.Value = (decimal)Math.Clamp(seed.StreakWindowPercent, 1, 200);
        _chkStreakFaster.Text = "faster too";
        _chkStreakFaster.AutoSize = true;
        _chkStreakFaster.Checked = seed.StreakFasterBreaks;
        _fields.AddRow("Streak window ±%:", FieldGrid.Inline(_numStreakWindow, _chkStreakFaster));

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
        _fields.AddRow("Start:", _cboStartType);

        // Across both columns, under the start types it explains, wrapping
        // to the grid's width.
        _fields.AddWide(new Label
        {
            Text = "For qualifying, the first and last starts are the ones that matter. \"All away together\" is a grid start on one shared "
                 + "clock (PN Racing's PNWC qualifiers), so the run to the loop is on your time. \"Own clock from "
                 + "1st crossing\" is the IFMAR convention: each driver is timed over their own laps.",
            AutoSize = true,
            Font = AppTheme.SmallFont,
            ForeColor = AppTheme.TextMuted,
        });

        _numStaggerGap.Minimum = 1;
        _numStaggerGap.Maximum = 120;
        _numStaggerGap.Value = Math.Clamp(seed.StaggerGapSeconds, 1, 120);
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
        _fields.AddRow("Stagger gap (s):", FieldGrid.Inline(_numStaggerGap, _cboStaggerClock));

        // Before the start-sequence rows because it happens before them:
        // warm-up, then Stage -> Countdown -> Green.
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
        _fields.AddRow("Warm-up (s, 0 = none):", FieldGrid.Inline(_numWarmUp, lblWarmUpHint));

        // The two ticks run from the left-hand edge, across both columns, as
        // they always have: they say what they are themselves, and the
        // longer of them is wider than the field column.
        _chkAutoAdvance.Text = "Run the start sequence automatically (Stage \u2192 Countdown \u2192 Green)";
        _chkAutoAdvance.AutoSize = true;
        _chkAutoAdvance.Checked = seed.AutoAdvance;
        _fields.AddWide(_chkAutoAdvance, stretch: false);

        _numStaging.Minimum = 1;
        _numStaging.Maximum = 300;
        _numStaging.Value = Math.Clamp(seed.StagingSeconds, 1, 300);
        _numCountdown.Minimum = 1;
        _numCountdown.Maximum = 60;
        _numCountdown.Value = Math.Clamp(seed.CountdownSeconds, 1, 60);
        _fields.AddRow("Stage (s):", _numStaging);
        _fields.AddRow("Countdown (s):", _numCountdown);

        _chkFinishOnCrossing.Text = "Leader takes the flag, others finish their lap";
        _chkFinishOnCrossing.AutoSize = true;
        _chkFinishOnCrossing.Checked = seed.FinishOnNextCrossing;
        _fields.AddWide(_chkFinishOnCrossing, stretch: false);

        // Last in the body whenever it shows - the sentence above it is
        // hidden then.
        body.Add(_fields, gapBelow: 0);

        var btnOk = new Button { Text = "OK" };
        btnOk.Click += (_, _) =>
        {
            Result = _allowUseDefault && !_chkCustom.Checked ? null : Collect();
            DialogResult = DialogResult.OK;
        };

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        // Against the right-hand edge, Cancel furthest right, as a dialog's
        // buttons go - and Tab still reaches OK first, as it did: the bar
        // takes them right to left, in the order they were added.
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnOk), gapBelow: 0);
        btnOk.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        UpdateStaggerEnabled();
        SetCustomVisible(!_allowUseDefault || _chkCustom.Checked);

        return (page, body);
    }

    /// <summary>
    /// The fields' widths, in the faces the theme pass left them in: the
    /// number boxes all as wide as the one with the largest value needs and
    /// never narrower than a number box at the text size in use, each
    /// drop-down as wide as its
    /// longest choice and never narrower than it was at the normal text
    /// size. Once: the dialog is built at the text size in use and is not
    /// kept in step with a change made while it is up (see DialogFit).
    /// </summary>
    private void SizeFields()
    {
        var numbers = new[]
        {
            _numLapLimit, _numTimeLimit, _numMinLap, _numStreakWindow,
            _numStaggerGap, _numWarmUp, _numStaging, _numCountdown,
        };

        // One width for all of them, the widest's: they sit in one column,
        // and boxes of three widths put their arrows in three places down it
        // (layout pictures, 29 Sep).
        var width = numbers.Max(number =>
        {
            var digits = ((int)number.Maximum).ToString(System.Globalization.CultureInfo.InvariantCulture).Length;

            return Math.Max(
                UiLayout.AtTextSize(UiLayout.NumericWidth),
                TextRenderer.MeasureText(new string('0', digits), number.Font).Width + SystemInformation.VerticalScrollBarWidth + 12 + UpDownButtonsSkin.Extra);
        });

        foreach (var number in numbers)
        {
            number.Width = width;
        }

        (Font Font, int Count, int Width)? startWidth = null;
        (Font Font, int Count, int Width)? clockWidth = null;

        _cboStartType.Width = Math.Max(UiLayout.AtTextSize(200), Pieces.PickerWidth(_cboStartType, ref startWidth));
        _cboStaggerClock.Width = Math.Max(UiLayout.AtTextSize(190), Pieces.PickerWidth(_cboStaggerClock, ref clockWidth));
    }

    /// <summary>
    /// The window is sized as it loads, to what is showing then (DialogFit).
    /// While the default is being followed, it is sized with the fields
    /// showing all the same, and the sentence put in their place after - so
    /// it is as tall as the fields whichever is up, as it always was, and
    /// ticking the box shows them in a window made for them rather than
    /// scrolling them through one made for three lines.
    /// </summary>
    protected override void OnLoad(EventArgs e)
    {
        var following = _allowUseDefault && !_chkCustom.Checked;

        if (following)
        {
            SetCustomVisible(true);
        }

        base.OnLoad(e);

        if (following)
        {
            SetCustomVisible(false);
        }
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
    /// which is exactly how it looked before this. The whole grid goes, so
    /// no empty row of it is left behind.
    /// </summary>
    private void SetCustomVisible(bool custom)
    {
        _fields.Visible = custom;
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
