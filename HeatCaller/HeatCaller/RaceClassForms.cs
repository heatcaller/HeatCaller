namespace HeatCaller;

/// <summary>
/// Small add/edit dialog for a single RaceClass.
///
/// On the layout pieces (DialogFit): the settings in three field grids, one
/// under the other, their captions in one column as wide as the longest of
/// them (FieldGrid.LineUp) - the name with the colour swatch beside it, the
/// notes, how qualifying is scored and the note on it; the mains under
/// their heading, and the note on them; then jump starts and the tech
/// warning. All of that is the window's body, which scrolls on a screen too
/// short for it. Under it, always in view, the class's sub-editors in a row
/// that wraps, and Save and Cancel at the foot. It used to be placed in
/// pixels: every caption and field at a fixed place in a window 652px wide
/// whatever the text size, the fields beside a caption at an x chosen for
/// it and the sub-editors flowed at a fixed width - about 674px tall at the
/// normal text size, more than a 1280x720 screen leaves a window, with Save
/// and Cancel under its edge, and at a larger text size the captions ran
/// into their fields.
/// </summary>
public sealed class RaceClassEditForm : Form
{
    /// <summary>
    /// How wide the window is inside its frame at the normal text size - the
    /// width the old one worked out from its row of sub-editors, which it
    /// wrapped at 620px rather than run them along one line eight hundred
    /// pixels long.
    /// </summary>
    private const int DialogWidth = 652;

    /// <summary>
    /// The name box's width at the normal text size: about forty-five
    /// characters, with the swatch beside it inside the field column.
    /// </summary>
    private const int NameWidth = 320;

    /// <summary>The colour swatch's width at the normal text size.</summary>
    private const int SwatchWidth = 38;

    /// <summary>How many lines of the notes show before the box scrolls.</summary>
    private const int NotesLines = 3;

    private readonly TextBox _txtName = new();
    private readonly TextBox _txtNotes = new();
    private readonly ComboBox _cmbQualifying = new();
    private readonly NumericUpDown _numConsecutive = new();

    /// <summary>The caption of the consecutive-laps row, greyed with it - see UpdateConsecutiveEnabled. The field grid makes it.</summary>
    private readonly Label _lblConsecutive;

    /// <summary>
    /// What a round's places score under points per round (12 Nov 2026) -
    /// the club's 1st = 1 point, or IFMAR's or EFRA's top qualifier 0. See
    /// RoundPointsScale for why it is a choice and not a detail.
    /// </summary>
    private readonly ComboBox _cmbPointsScale = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    /// <summary>The caption of the points-scale row, greyed with it - see UpdateConsecutiveEnabled.</summary>
    private readonly Label _lblPointsScale;

    private static readonly (RoundPointsScale Scale, string Words)[] ScaleChoices =
    {
        (RoundPointsScale.Club, "1 point (club)"),
        (RoundPointsScale.Ifmar, "0 points - IFMAR (a missed round scores 500)"),
        (RoundPointsScale.Efra, "0 points - EFRA (a missed round scores last place)"),
    };

    private readonly NumericUpDown _numMainSize = new();
    private readonly NumericUpDown _numBumpUp = new();

    /// <summary>
    /// Single, double or triple A-main and how many legs count (11 Nov 2026,
    /// Phase 5.3) - one picker for the two settings, because only five of
    /// their combinations make sense.
    /// </summary>
    private readonly ComboBox _cmbAMainLegs = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    private static readonly (int Legs, int Counted, string Words)[] LegChoices =
    {
        (1, 0, "Single A-main"),
        (2, 1, "Double A-main - best 1 of 2 legs"),
        (2, 2, "Double A-main - both legs count"),
        (3, 2, "Triple A-main - best 2 of 3 legs"),
        (3, 3, "Triple A-main - all 3 legs count"),
    };

    private readonly NumericUpDown _numCountBest = new();
    private readonly CheckBox _chkJumpStart = new ThemedCheckBox();
    private readonly NumericUpDown _numJumpPenalty = new();
    private readonly CheckBox _chkJumpCostsLap = new ThemedCheckBox();

    /// <summary>The caption of the penalty row, greyed with it - see UpdateJumpStartEnabled. The field grid makes it.</summary>
    private readonly Label _lblJumpPenalty;

    private readonly CheckBox _chkWarnUninspected = new ThemedCheckBox();

    /// <summary>
    /// The class's colour, shown rather than named. A swatch is the only
    /// honest control for a colour - a hex value in a text box tells a race
    /// director nothing about whether two classes' sheets will be telling
    /// one from the other on a pit table at arm's length.
    /// </summary>
    private readonly Panel _swatch = new();

    /// <summary>
    /// Zero means "follow the name", which is the default and stays the
    /// default until somebody deliberately picks something. Held here
    /// rather than written straight onto Result so that Cancel actually
    /// cancels.
    /// </summary>
    private int _colourArgb;

    /// <summary>Built on first use, shared by the swatch and the button.</summary>
    private ContextMenuStrip? _colourMenu;

    public RaceClass Result { get; private set; }

    /// <summary>The other classes, for refusing a name one of them already has. Null skips the check.</summary>
    private readonly RaceClassStore? _store;

    /// <summary>A new class's race length and finish rule (12 Nov 2026, setup plan step 8). Null when editing.</summary>
    private readonly ClassLengthPicker? _length;

    /// <summary>
    /// Set once Format or a sanctioning preset has set the new class's
    /// format, which then wins over the two rows - see SeedFormatFromLength.
    /// </summary>
    private bool _formatSetElsewhere;

    /// <summary>The class's own entry fee and extra-class fee (12 Nov 2026, setup plan step 8). Unticked follows the race day's.</summary>
    private readonly CheckBox _chkOwnFee = new ThemedCheckBox();

    private readonly NumericUpDown _numOwnFee = new() { DecimalPlaces = 2, Maximum = 1000, Increment = 1 };
    private readonly CheckBox _chkOwnExtraFee = new ThemedCheckBox();
    private readonly NumericUpDown _numOwnExtraFee = new() { DecimalPlaces = 2, Maximum = 1000, Increment = 1 };

    public RaceClassEditForm(RaceClass? existing, RaceClassStore? store = null)
    {
        // A new class starts with a race length (setup plan A5): see RaceFormat.ForNewClass.
        Result = existing ?? new RaceClass { Format = RaceFormat.ForNewClass(store?.AppDefaultFormat?.Invoke()) };
        _store = store;

        // And a NEW class's window asks for it, with how the race finishes
        // (12 Nov 2026, setup plan step 8) - see ClassLengthPicker. Not for a
        // class being edited: its format is already its own, under Race
        // Format..., and two places to change one setting is how they drift.
        if (existing is null)
        {
            _length = new ClassLengthPicker(store?.AppDefaultFormat?.Invoke());
        }

        // Its own fees (12 Nov 2026, setup plan step 8) - see RaceClass.EntryFee.
        _chkOwnFee.Text = "Its own:";
        _chkOwnFee.AutoSize = true;
        _chkOwnFee.Checked = Result.EntryFee is not null;
        _numOwnFee.Value = Math.Clamp(Result.EntryFee ?? 0m, _numOwnFee.Minimum, _numOwnFee.Maximum);
        _numOwnExtraFee.Value = Math.Clamp(Result.ExtraClassFee ?? 0m, _numOwnExtraFee.Minimum, _numOwnExtraFee.Maximum);
        _chkOwnExtraFee.Text = "Its own:";
        _chkOwnExtraFee.AutoSize = true;
        _chkOwnExtraFee.Checked = Result.ExtraClassFee is not null;

        Text = existing is null ? "Add Class" : "Edit Class";
        Font = AppTheme.BodyFont;

        _txtName.Text = Result.Name;

        _colourArgb = Result.ColorArgb;

        _swatch.Tag = AppTheme.KeepBackColor;
        _swatch.BorderStyle = BorderStyle.FixedSingle;
        _swatch.Cursor = Cursors.Hand;
        _swatch.Click += (_, _) => ShowColourMenu(_swatch, new Point(0, _swatch.Height));

        // A derived colour follows the name as it is typed, so the swatch
        // is showing what this class will actually look like rather than
        // what it looked like when the dialog opened.
        _txtName.TextChanged += (_, _) =>
        {
            if (_colourArgb == 0)
            {
                UpdateSwatch();
            }
        };

        _txtNotes.Multiline = true;
        _txtNotes.AcceptsReturn = true;
        _txtNotes.ScrollBars = ScrollBars.Vertical;
        _txtNotes.Text = Result.Notes;

        // --- Qualifying rules ---
        _cmbQualifying.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbQualifying.Items.AddRange(new object[]
        {
            new MethodItem(QualifyingMethod.TotalLaps, "Most laps, then lowest time"),
            new MethodItem(QualifyingMethod.BestConsecutiveLaps, "Best consecutive laps (fastest clean run)"),
            // 11 Nov 2026, Phase 5.1: the ROAR Qual-Points style.
            new MethodItem(QualifyingMethod.RoundPoints, "Points per round (1st = 1 point, fewest wins)"),
        });
        SelectMethod(Result.QualifyingMethod);

        _numConsecutive.Minimum = 2;
        _numConsecutive.Maximum = 20;
        _numConsecutive.Value = Math.Clamp(Result.ConsecutiveLapCount, 2, 20);

        _numCountBest.Minimum = 1;
        _numCountBest.Maximum = 20;
        _numCountBest.Value = Math.Clamp(Result.CountBestRounds, 1, 20);

        foreach (var choice in ScaleChoices)
        {
            _cmbPointsScale.Items.Add(choice.Words);
        }

        SelectScale();

        var lblQualHint = new Label
        {
            Text = "Best consecutive scores the quickest unbroken run of N laps. \"Heats that count\" totals each driver's "
                 + "best that many heats and drops the rest - 2 for \"best 2 of 3\", 1 for their single best heat. "
                 + "Points per round ranks each round's heats together (1st = 1 point, a missed round = field + 1); "
                 + "there it counts each driver's best that many ROUNDS, and fewest points wins. For a sanctioned "
                 + "meeting, set the top qualifier to 0 points the way IFMAR and EFRA score it - that can change the "
                 + "order, not just the numbers.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        // --- Mains ---
        var lblMains = new Label { Text = "Mains", AutoSize = true, Font = AppTheme.BoldFont };

        _numMainSize.Minimum = 2;
        _numMainSize.Maximum = 40;
        _numMainSize.Value = Math.Clamp(Result.MainSize, 2, 40);

        _numBumpUp.Minimum = 0;
        _numBumpUp.Maximum = 10;
        _numBumpUp.Value = Math.Clamp(Result.BumpUpCount, 0, 10);

        foreach (var choice in LegChoices)
        {
            _cmbAMainLegs.Items.Add(choice.Words);
        }

        SelectLegs();

        var lblMainsHint = new Label
        {
            Text = "Qualifying is split into A/B/C mains of this size. Bump up is how many finishers move up a tier - set 0 to disable. "
                 + "A double or triple A-main runs the A-main as legs scored on points (1st = 1), fewest wins; "
                 + "ties go to the best single-leg finish, then the last leg.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        // --- Jump starts ---
        _chkJumpStart.Text = "Detect jump starts (crossing before the green)";
        _chkJumpStart.AutoSize = true;
        _chkJumpStart.Checked = Result.DetectJumpStarts;

        _numJumpPenalty.Minimum = 0;
        _numJumpPenalty.Maximum = 120;
        _numJumpPenalty.Value = Math.Clamp(Result.JumpStartPenaltySeconds, 0, 120);

        var lblJumpUnit = new Label { Text = "seconds (0 = flag only)", AutoSize = true };

        _chkJumpCostsLap.Text = "A jump start also costs the lap";
        _chkJumpCostsLap.AutoSize = true;
        _chkJumpCostsLap.Checked = Result.JumpStartCostsTheLap;

        // The setting the Tech Log has always promised and nothing showed:
        // stored on every class (on by default) but with no way to turn it
        // off. Race Control reads it when a heat of this class is staged.
        _chkWarnUninspected.Text = "Warn when a driver in a heat hasn't passed tech today";
        _chkWarnUninspected.AutoSize = true;
        _chkWarnUninspected.Checked = Result.WarnOnUninspected;

        var warnTip = new ToolTip();
        AppTheme.StyleToolTip(warnTip);
        warnTip.SetToolTip(_chkWarnUninspected,
            "When a heat of this class is staged, Race Control's log lists anyone on its roster without a passing "
            + "inspection in the Scrutineering log, and the Next Up bar counts them. A warning only - the heat still runs. "
            + "Only applies once tech is being run for the class: it has a checklist, or someone has been inspected in it today.");

        var btnTechList = new Button { Text = "Tech Checklist..." };
        btnTechList.Click += (_, _) => EditTechChecklist();

        // The class's race format - limits, start procedure, streak rules.
        // Opens the shared editor; "untick custom" hands the class back to
        // the app default rather than freezing a copy of it. The app
        // default goes in as the fallback, so the editor can say what
        // following it means and, when custom is ticked, start the fields
        // from the club's default rather than the built-in values. Null
        // when this editor was opened without the store (the editor then
        // falls back to the built-ins, as it always did).
        var btnFormat = new Button { Text = "Format" };
        btnFormat.Click += (_, _) =>
        {
            // A new class's editor opens on the length and finish typed above.
            SeedFormatFromLength();

            using var form = new RaceFormatForm(
                Result.Format,
                $"Race Format - {(string.IsNullOrWhiteSpace(_txtName.Text) ? "this class" : _txtName.Text)}",
                allowUseDefault: true,
                fallback: _store?.AppDefaultFormat?.Invoke());

            if (form.ShowDialog(this) == DialogResult.OK)
            {
                Result.Format = form.Result;
                FormatSetElsewhere();
            }
        };

        // Qualifiers get their own, falling back to the race format above.
        // Five-minute quals and eight-minute mains is the commonest club
        // shape there is, and it should not take a per-round override.
        // The fallback is the class's race format, or the app default when
        // the class follows that - the same chain qualifiers are run under
        // (QualifierFormat, else Format, else the app default). Without the
        // second step a class on the default left the editor no fallback,
        // and ticking custom started from the built-in values.
        var btnQualFormat = new Button { Text = "Qualifier Format..." };
        btnQualFormat.Click += (_, _) =>
        {
            using var form = new RaceFormatForm(
                Result.QualifierFormat,
                $"Qualifier Format - {(string.IsNullOrWhiteSpace(_txtName.Text) ? "this class" : _txtName.Text)}",
                allowUseDefault: true,
                fallback: Result.Format ?? _store?.AppDefaultFormat?.Invoke());

            if (form.ShowDialog(this) == DialogResult.OK)
            {
                Result.QualifierFormat = form.Result;
            }
        };

        // The visible door to the colour. The swatch beside the name is the
        // shortcut; this is what somebody finds without knowing to click a
        // coloured square.
        var btnColour = new Button { Text = "Color..." };
        btnColour.Click += (_, _) => ShowColourMenu(btnColour, new Point(0, btnColour.Height));

        // What a sanctioning body says, as far as it could be read - and,
        // just as importantly, what it does not say.
        var btnPreset = new Button { Text = "Sanctioning Preset..." };
        btnPreset.Click += (_, _) => ApplySanctioningPreset();

        // What each warning from Race Control costs in this class's races.
        var btnWarnings = new Button { Text = "Warnings..." };
        btnWarnings.Click += (_, _) =>
        {
            using var form = new WarningLadderForm(
                Result.WarningLadder,
                $"Warnings - {(string.IsNullOrWhiteSpace(_txtName.Text) ? "this class" : _txtName.Text)}");

            if (form.ShowDialog(this) == DialogResult.OK)
            {
                Result.WarningLadder = form.Result;
            }
        };

        var btnSave = new Button { Text = "Save" };
        btnSave.Click += BtnSave_Click;
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        // --- Layout ---
        //
        // A caption and its field on each row, the captions in one column
        // down all three grids (LineUp, below) so the fields line up the
        // length of the dialog. The name box keeps a width of its own with
        // the swatch beside it; the notes run to the right-hand edge.
        var about = new FieldGrid();
        about.AddRow("Name:", FieldGrid.Inline(_txtName, _swatch));
        about.AddRow("Notes:", _txtNotes, stretch: true);

        // A new class's race length and how it finishes, under its name - the
        // first thing after the name a club has to decide (setup plan step 8).
        _length?.AddTo(about);

        about.AddRow("Qualifying:", _cmbQualifying);
        _lblConsecutive = about.AddRow("Consecutive laps to count:", _numConsecutive);
        about.AddRow("Heats that count:", _numCountBest);
        _lblPointsScale = about.AddRow("Top qualifier scores:", _cmbPointsScale);

        // Across both columns, under the rows it explains - it is about the
        // two of them together - wrapping to the grid's width.
        about.AddWide(lblQualHint);

        var mains = new FieldGrid();
        mains.AddRow("Drivers per main:", _numMainSize);
        mains.AddRow("Bump up:", _numBumpUp);
        mains.AddRow("A-main:", _cmbAMainLegs);
        mains.AddWide(lblMainsHint);

        // The two ticks run from the left-hand edge, across both columns:
        // they say what they are themselves. What only means anything with
        // jump starts detected is under that tick - the penalty on a row of
        // its own, and the tick about the lap in the field column, in from
        // the tick it depends on as it always was.
        var raceDay = new FieldGrid();
        raceDay.AddWide(_chkJumpStart, stretch: false);
        _lblJumpPenalty = raceDay.AddRow("Automatic penalty:", FieldGrid.Inline(_numJumpPenalty, lblJumpUnit));
        raceDay.AddField(_chkJumpCostsLap);
        raceDay.AddWide(_chkWarnUninspected, stretch: false);

        // The class's own fees (12 Nov 2026, setup plan step 8): each row a
        // tick and the amount, greyed while unticked - unticked, the class
        // pays the race day's fees. See RaceClass.EntryFee and DeskMoney.FeeFor.
        var lblMoney = new Label { Text = "Fees", AutoSize = true, Font = AppTheme.BoldFont };
        var money = new FieldGrid();
        money.AddRow("Entry fee:", FieldGrid.Inline(_chkOwnFee, _numOwnFee));
        money.AddRow("As an extra class:", FieldGrid.Inline(_chkOwnExtraFee, _numOwnExtraFee));
        money.AddWide(new Label
        {
            Text = "Unticked, the class charges the race day's Entry fee and Each extra class (Race Day > Setup). "
                 + "A driver in several classes pays their dearest class's entry fee, and each other class at its extra-class fee - "
                 + "never more than that class's own entry fee.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        });

        // Hooked once the captions they grey out exist - the field grids
        // make them, above.
        _cmbQualifying.SelectedIndexChanged += (_, _) => UpdateConsecutiveEnabled();
        _chkJumpStart.CheckedChanged += (_, _) => UpdateJumpStartEnabled();
        _chkOwnFee.CheckedChanged += (_, _) => UpdateFeesEnabled();
        _chkOwnExtraFee.CheckedChanged += (_, _) => UpdateFeesEnabled();

        var page = new FillPage();

        // Every setting: on a screen too short for all of them - 1280x720,
        // or any at a larger text size - they scroll, and the sub-editors,
        // Save and Cancel under them stay in view.
        var body = DialogFit.Body(page);
        body.Add(about);
        body.Add(lblMains, stretch: false, gapBelow: UiLayout.LabelGap);
        body.Add(mains);
        body.Add(raceDay);
        body.Add(lblMoney, stretch: false, gapBelow: UiLayout.LabelGap);
        body.Add(money, gapBelow: 0);

        // Two rows with two jobs: the class's sub-editors on one, the exit
        // buttons on the other, bottom-right where a dialog keeps them.
        // WRAPPED, not one long row. Six sub-editors at their measured
        // widths come to something like a thousand pixels, and a dialog
        // that wide to hold one column of fields reads as a mistake. The
        // row takes a second line instead and the dialog grows downwards,
        // which is the direction nobody minds.
        // Simple mode (1 Nov 2026) keeps the race format (its lap and time
        // limits - the window hides its own tuning), color and sanctioning
        // preset; the tech list, qualifier format and warning ladder are
        // Advanced. See SimpleMode.
        page.Add(new ButtonBar().With(SimpleMode.Hide(btnTechList), btnFormat, SimpleMode.Hide(btnQualFormat), btnColour, btnPreset, SimpleMode.Hide(btnWarnings)), gapBelow: UiLayout.RowGap);

        // Against the right-hand edge, Cancel furthest right, as they were -
        // and Tab still reaches Save first: the bar takes them right to
        // left, in the order they were added.
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnSave), gapBelow: 0);
        btnSave.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        CancelButton = btnCancel;

        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;
        UpdateConsecutiveEnabled();
        UpdateJumpStartEnabled();
        UpdateFeesEnabled();
        UpdateSwatch();
        AppTheme.ApplyRecursively(this);

        // Once the captions are on the dialog and wear its face - what they
        // are measured in.
        FieldGrid.LineUp(new[] { about, mains, raceDay, money });
        SizeFields();

        DialogFit.Keep(this, page, width: DialogWidth, body: body);
    }

    /// <summary>
    /// The fields' sizes, in the faces the theme pass left them in: the name
    /// box and the swatch the widths they had at the normal text size, at
    /// the size in use, the swatch as tall as the box beside it; the notes
    /// so many lines of their font; the method as wide as its longer
    /// choice, never narrower than a standard field; and the number boxes
    /// all one width, the one with the largest value's - they sit in one
    /// column, and boxes of three widths put their arrows in three places
    /// down it. Once: the dialog is built at the text size in use and is not
    /// kept in step with a change made while it is up (see DialogFit).
    /// </summary>
    private void SizeFields()
    {
        _txtName.Width = UiLayout.AtTextSize(NameWidth);

        // The box's height is the theme's, from its font (InputOutline), and
        // settled by now.
        _swatch.Size = new Size(UiLayout.AtTextSize(SwatchWidth), _txtName.Height);

        _txtNotes.Height = Pieces.LineHeight(_txtNotes.Font) * NotesLines + UiLayout.HintGap * 2;

        (Font Font, int Count, int Width)? qualifyingWidth = null;
        _cmbQualifying.Width = Math.Max(UiLayout.AtTextSize(UiLayout.FieldWidth), Pieces.PickerWidth(_cmbQualifying, ref qualifyingWidth));

        (Font Font, int Count, int Width)? scaleWidth = null;
        _cmbPointsScale.Width = Math.Max(UiLayout.AtTextSize(UiLayout.FieldWidth), Pieces.PickerWidth(_cmbPointsScale, ref scaleWidth));

        (Font Font, int Count, int Width)? legsWidth = null;
        _cmbAMainLegs.Width = Math.Max(UiLayout.AtTextSize(UiLayout.FieldWidth), Pieces.PickerWidth(_cmbAMainLegs, ref legsWidth));

        var numbers = new[] { _numConsecutive, _numCountBest, _numMainSize, _numBumpUp, _numJumpPenalty };

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

        // The fees as wide as their largest amount, decimals and all.
        var feeWidth = Math.Max(
            UiLayout.AtTextSize(UiLayout.NumericWidth),
            TextRenderer.MeasureText("0000.00", _numOwnFee.Font).Width + SystemInformation.VerticalScrollBarWidth + 12 + UpDownButtonsSkin.Extra);
        _numOwnFee.Width = feeWidth;
        _numOwnExtraFee.Width = feeWidth;

        _length?.SizeFields();
    }

    /// <summary>A fee's amount only means anything with its tick, so it is greyed rather than hidden.</summary>
    private void UpdateFeesEnabled()
    {
        _numOwnFee.Enabled = _chkOwnFee.Checked;
        _numOwnExtraFee.Enabled = _chkOwnExtraFee.Checked;
    }

    /// <summary>
    /// For a new class whose format nothing else has set yet: the format from
    /// the race length and finish rows, so Format and a sanctioning
    /// preset start from what was typed there rather than from the 5 minutes
    /// the class was made with (12 Nov 2026, setup plan step 8).
    /// </summary>
    private void SeedFormatFromLength()
    {
        if (_length is not null && !_formatSetElsewhere)
        {
            Result.Format = _length.Format();
        }
    }

    /// <summary>Format or a preset has set the new class's format: that wins, and the rows say so.</summary>
    private void FormatSetElsewhere()
    {
        if (_length is null)
        {
            return;
        }

        _formatSetElsewhere = true;
        _length.ShowSetElsewhere(Result.Format);
    }

    /// <summary>Shows the class's A-main legs on the picker - the closest choice for a hand-edited file.</summary>
    private void SelectLegs()
    {
        var legs = Math.Clamp(Result.AMainLegs, 1, AMainLegs.MaxLegs);
        var counted = AMainLegs.LegsCounting(legs, Result.AMainLegsCounted);
        var index = Array.FindIndex(LegChoices, c => c.Legs == legs && (legs == 1 || c.Counted == counted));

        _cmbAMainLegs.SelectedIndex = index >= 0 ? index : Array.FindIndex(LegChoices, c => c.Legs == legs);
    }

    private QualifyingMethod CurrentMethod() =>
        (_cmbQualifying.SelectedItem as MethodItem)?.Method ?? QualifyingMethod.TotalLaps;

    private void SelectMethod(QualifyingMethod method)
    {
        for (var i = 0; i < _cmbQualifying.Items.Count; i++)
        {
            if (_cmbQualifying.Items[i] is MethodItem item && item.Method == method)
            {
                _cmbQualifying.SelectedIndex = i;
                return;
            }
        }
        _cmbQualifying.SelectedIndex = 0;
    }

    /// <summary>The lap count only means anything for the consecutive method, so it's disabled rather than hidden for the other.</summary>
    private void UpdateConsecutiveEnabled()
    {
        var enabled = CurrentMethod() == QualifyingMethod.BestConsecutiveLaps;
        _numConsecutive.Enabled = enabled;
        _lblConsecutive.ForeColor = enabled ? AppTheme.TextDark : AppTheme.TextMuted;

        // The points scale only means anything for points per round.
        var points = CurrentMethod() == QualifyingMethod.RoundPoints;
        _cmbPointsScale.Enabled = points;
        _lblPointsScale.ForeColor = points ? AppTheme.TextDark : AppTheme.TextMuted;
    }

    private void SelectScale()
    {
        var index = Array.FindIndex(ScaleChoices, c => c.Scale == Result.RoundPointsScale);
        _cmbPointsScale.SelectedIndex = index >= 0 ? index : 0;
    }

    private RoundPointsScale CurrentScale() =>
        ScaleChoices[Math.Clamp(_cmbPointsScale.SelectedIndex, 0, ScaleChoices.Length - 1)].Scale;

    private sealed record MethodItem(QualifyingMethod Method, string Label)
    {
        public override string ToString() => Label;
    }

    /// <summary>
    /// Edits this class's scrutineering checklist. Held on the class because
    /// the rules differ between them - a stock class cares about motor and
    /// tyres where a modified one might only check the body.
    /// </summary>
    private void EditTechChecklist()
    {
        using var dialog = new TechChecklistForm(_txtName.Text, Result.TechCheckItems);

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            Result.TechCheckItems = dialog.Items;
        }
    }

    /// <summary>
    /// Applies a sanctioning body's rules to this class.
    ///
    /// The preset writes onto Result directly, which is the object Save
    /// assigns the dialog's controls onto - so the controls have to be
    /// brought back into step immediately afterwards. Without that, the
    /// preset appeared to work and then Save quietly put every one of the
    /// old values back over the top of it.
    /// </summary>
    private void ApplySanctioningPreset()
    {
        // Named now rather than on Save, so the preset dialog's title and
        // its confirmation say which class they are about - and put back
        // straight afterwards. Result is the STORED class when editing, so
        // leaving the typed name on it meant a rename that was then
        // cancelled still renamed the class, with none of the drivers,
        // rounds or championships following it. Save is the only place a
        // name is committed.
        var savedName = Result.Name;
        Result.Name = _txtName.Text.Trim();

        // From the length and finish typed, for a new class (setup plan step 8).
        SeedFormatFromLength();
        var formatBefore = System.Text.Json.JsonSerializer.Serialize(Result.Format);

        using var form = new SanctioningPresetForm(Result, _store?.AppDefaultFormat?.Invoke());

        var applied = form.ShowDialog(this) == DialogResult.OK;

        Result.Name = savedName;

        if (!applied)
        {
            return;
        }

        SelectMethod(Result.QualifyingMethod);

        _numConsecutive.Value = Math.Clamp(Result.ConsecutiveLapCount, _numConsecutive.Minimum, _numConsecutive.Maximum);
        _numCountBest.Value = Math.Clamp(Result.CountBestRounds, _numCountBest.Minimum, _numCountBest.Maximum);
        SelectScale();
        _numMainSize.Value = Math.Clamp(Result.MainSize, _numMainSize.Minimum, _numMainSize.Maximum);
        _numBumpUp.Value = Math.Clamp(Result.BumpUpCount, _numBumpUp.Minimum, _numBumpUp.Maximum);
        SelectLegs();

        UpdateConsecutiveEnabled();

        if (System.Text.Json.JsonSerializer.Serialize(Result.Format) != formatBefore)
        {
            FormatSetElsewhere();
        }
    }

    /// <summary>What this class's colour is right now, chosen or derived.</summary>
    private Color CurrentColour() =>
        _colourArgb != 0
            ? Color.FromArgb(_colourArgb)
            : RaceClass.DefaultColourFor(_txtName.Text.Trim());

    private void UpdateSwatch() => _swatch.BackColor = CurrentColour();

    /// <summary>
    /// Both colour commands in one list, so the way back to the derived
    /// colour is as visible as the way away from it. A colour picked by
    /// hand and then regretted is otherwise unreachable - there is no
    /// "none" on a colour dialog.
    /// </summary>
    private void ShowColourMenu(Control anchor, Point at)
    {
        // Built once and kept. A menu created per click would have to be
        // disposed somewhere, and the only obvious place - its own Closed
        // event - runs while the menu is still delivering the click that
        // closed it.
        if (_colourMenu is null)
        {
            var menu = new ContextMenuStrip { Font = AppTheme.BodyFont, ShowImageMargin = false };

            var choose = new ToolStripMenuItem("Choose a Color...", null, (_, _) => ChooseColour());

            var derive = new ToolStripMenuItem("Use the Color from the Name", null, (_, _) =>
            {
                _colourArgb = 0;
                UpdateSwatch();
            });

            menu.Items.Add(choose);
            menu.Items.Add(derive);

            // Greyed rather than hidden when there is nothing to revert to,
            // so the menu is the same shape every time it opens.
            menu.Opening += (_, _) => derive.Enabled = _colourArgb != 0;

            AppTheme.StyleContextMenu(menu);

            _colourMenu = menu;
        }

        _colourMenu.Show(anchor, at);
    }

    /// <summary>
    /// The colour menu is not in Controls, so nothing else disposes it -
    /// and it owns a window handle like any other control.
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _colourMenu?.Dispose();
            _colourMenu = null;
        }

        base.Dispose(disposing);
    }

    private void ChooseColour()
    {
        var derived = RaceClass.DefaultColourFor(_txtName.Text.Trim());

        using var dialog = new ColorDialog
        {
            FullOpen = true,
            AnyColor = true,
            Color = CurrentColour(),

            // The name's own colour, one click away in the custom row, so
            // the default is offered rather than only described.
            CustomColors = new[] { ColorTranslator.ToOle(derived) },
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        // Picking exactly the name's own colour is taken as "follow the
        // name" rather than as a choice that happens to match today - so a
        // class renamed later still re-colours itself.
        _colourArgb = dialog.Color.ToArgb() == derived.ToArgb() ? 0 : dialog.Color.ToArgb();

        UpdateSwatch();
    }

    /// <summary>The penalty only means anything when detection is on, so it's disabled rather than hidden.</summary>
    private void UpdateJumpStartEnabled()
    {
        _numJumpPenalty.Enabled = _chkJumpStart.Checked;
        _chkJumpCostsLap.Enabled = _chkJumpStart.Checked;
        _lblJumpPenalty.ForeColor = _chkJumpStart.Checked ? AppTheme.TextDark : AppTheme.TextMuted;
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        var name = _txtName.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            AppDialog.Show(this, "Name is required.", "Missing name", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Refused rather than allowed, because everything that refers to a
        // class does it by name, ignoring case: a second "Stock" would share
        // the first one's drivers, heats and championship.
        if (_store is not null && _store.NameTaken(name, Result.Id))
        {
            AppDialog.Show(this, $"There is already a class called \"{name}\". Choose a different name.",
                "Name already used", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        // Assign onto the existing object rather than building a new one.
        // Rebuilding meant every property had to be re-listed here, and
        // anything missed was silently discarded on save - which is exactly
        // what happened to the tech checklist, edited in its own dialog and
        // then thrown away the moment Save was pressed.
        Result.Name = name;
        Result.Notes = _txtNotes.Text;
        Result.QualifyingMethod = CurrentMethod();
        Result.ConsecutiveLapCount = (int)_numConsecutive.Value;
        Result.CountBestRounds = (int)_numCountBest.Value;
        Result.RoundPointsScale = CurrentScale();
        Result.MainSize = (int)_numMainSize.Value;
        Result.DetectJumpStarts = _chkJumpStart.Checked;
        Result.JumpStartPenaltySeconds = (int)_numJumpPenalty.Value;
        Result.JumpStartCostsTheLap = _chkJumpCostsLap.Checked;
        Result.WarnOnUninspected = _chkWarnUninspected.Checked;
        Result.BumpUpCount = (int)_numBumpUp.Value;

        var legs = LegChoices[Math.Clamp(_cmbAMainLegs.SelectedIndex, 0, LegChoices.Length - 1)];
        Result.AMainLegs = legs.Legs;
        Result.AMainLegsCounted = legs.Counted;
        Result.ColorArgb = _colourArgb;

        // A new class's race length and finish (setup plan step 8), unless
        // Format or a preset has set the format since.
        SeedFormatFromLength();

        // Its own fees, or null to follow the race day's (setup plan step 8).
        Result.EntryFee = _chkOwnFee.Checked ? _numOwnFee.Value : null;
        Result.ExtraClassFee = _chkOwnExtraFee.Checked ? _numOwnExtraFee.Value : null;

        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>
    /// The window Tech Checklist... opens: what a scrutineer checks for the
    /// class, in order - see EditTechChecklist. A class of its own rather
    /// than built inside that method, and internal, so the layout test can
    /// build it (WindowCatalog) without a click.
    ///
    /// On the layout pieces (DialogFit): the hint across the top, the checks
    /// taking the height with their buttons in a column beside them
    /// (Beside) - three that change the list, two that reorder it and the
    /// common checks, a section's gap between the groups - and OK and Cancel
    /// at the foot. It used to be built in pixels in a 420x420 window: a
    /// list of a fixed size, the buttons stepped down beside it at fixed
    /// places and a fixed 116px wide, and OK and Cancel at a fixed height
    /// from the top.
    /// </summary>
    internal sealed class TechChecklistForm : Form
    {
        /// <summary>The checks show at least this many before the window scrolls instead - or as many as the button column beside them is tall, when that is more.</summary>
        private const int LeastRows = 4;

        private readonly ListBox _list = new();

        /// <param name="className">The class's name as typed, for the title - "class" when there is none yet.</param>
        /// <param name="items">The checks to start from, in order.</param>
        public TechChecklistForm(string className, IEnumerable<string> items)
        {
            Text = $"Tech Checklist - {(className.Trim() is { Length: > 0 } n ? n : "class")}";
            Font = AppTheme.BodyFont;
            BackColor = AppTheme.Background;
            ForeColor = AppTheme.TextDark;

            var lblHint = new Label
            {
                Text = "What a scrutineer checks for this class. Each item becomes an OK / FAIL / not-checked row on the inspection form.",
                AutoSize = true,
                ForeColor = AppTheme.TextMuted,
                Font = AppTheme.SmallFont,
            };

            _list.BackColor = AppTheme.Surface;

            // Given the height the window has, not snapped to whole rows - see FillPage.AddFill.
            _list.IntegralHeight = false;

            foreach (var item in items)
            {
                _list.Items.Add(item);
            }

            var btnAdd = new Button { Text = "Add..." };
            btnAdd.Click += (_, _) =>
            {
                var text = PromptForText("Add check", "What should be checked?", string.Empty);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    _list.Items.Add(text.Trim());
                }
            };

            var btnEditItem = new Button { Text = "Rename..." };
            btnEditItem.Click += (_, _) =>
            {
                if (_list.SelectedIndex < 0) { return; }
                var index = _list.SelectedIndex;
                var text = PromptForText("Rename check", "What should be checked?", _list.Items[index]?.ToString() ?? string.Empty);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    _list.Items[index] = text.Trim();
                    _list.SelectedIndex = index;
                }
            };

            var btnRemove = new Button { Text = "Remove" };
            btnRemove.Click += (_, _) =>
            {
                if (_list.SelectedIndex >= 0) { _list.Items.RemoveAt(_list.SelectedIndex); }
            };

            var btnUp = new Button { Text = "Move Up" };
            btnUp.Click += (_, _) => MoveItem(_list, -1);

            var btnDown = new Button { Text = "Move Down" };
            btnDown.Click += (_, _) => MoveItem(_list, 1);

            var btnPreset = new Button { Text = "Add Common" };
            btnPreset.Click += (_, _) =>
            {
                // A starting point rather than a rulebook - every club differs,
                // so these are only suggestions and all of them are editable.
                foreach (var preset in new[]
                         {
                             "Body secure and undamaged",
                             "Motor legal for class",
                             "Tires legal for class",
                             "Ride height",
                             "Transponder fitted and working",
                             "No loose parts",
                         })
                {
                    if (!_list.Items.Cast<object>().Any(i => string.Equals(i?.ToString(), preset, StringComparison.OrdinalIgnoreCase)))
                    {
                        _list.Items.Add(preset);
                    }
                }
            };

            var ok = new Button { Text = "OK", DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

            // The buttons in one column down the right of the list, all as
            // wide as the widest caption needs, in their three groups as
            // before: a button's gap inside a group, a section's between
            // them. The list takes the width the column leaves and the
            // height down to OK and Cancel.
            var checks = new Beside();
            checks.AddFill(_list, _ => LeastRows * _list.ItemHeight + 4);
            checks.AddSide(btnAdd, gapBelow: UiLayout.ButtonGap);
            checks.AddSide(btnEditItem, gapBelow: UiLayout.ButtonGap);
            checks.AddSide(btnRemove, gapBelow: UiLayout.SectionGap);
            checks.AddSide(btnUp, gapBelow: UiLayout.ButtonGap);
            checks.AddSide(btnDown, gapBelow: UiLayout.SectionGap);
            checks.AddSide(btnPreset, gapBelow: 0);

            var page = new FillPage();
            page.Add(lblHint, gapBelow: UiLayout.RowGap);
            page.AddFill(checks, checks.LeastHeight);

            // Against the right-hand edge, Cancel furthest right, as they
            // were - and Tab still reaches OK first: the bar takes them right
            // to left, in the order they were added.
            page.Add(new ButtonBar(alignRight: true).With(cancel, ok), gapBelow: 0);
            ok.TabIndex = 0;
            cancel.TabIndex = 1;

            Controls.Add(page);

            AcceptButton = ok;
            CancelButton = cancel;

            AppTheme.ApplyRecursively(this);

            // Wider than the 420 it was made at: the buttons are as wide as
            // a standard button now, 148px rather than 116, and at 420 that
            // came out of the list - this keeps it the 270px it had. The
            // height it had. It can be made bigger, for a long list.
            DialogFit.Keep(this, page, width: 460, height: 420);
        }

        /// <summary>The checks, in the order the list has them. Only meaningful after DialogResult.OK.</summary>
        public List<string> Items => _list.Items.Cast<object>().Select(i => i.ToString() ?? string.Empty).ToList();

        private static void MoveItem(ListBox list, int direction)
        {
            var index = list.SelectedIndex;
            var target = index + direction;
            if (index < 0 || target < 0 || target >= list.Items.Count)
            {
                return;
            }

            var item = list.Items[index];
            list.Items.RemoveAt(index);
            list.Items.Insert(target, item);
            list.SelectedIndex = target;
        }

        /// <summary>Asks for one line of text over this window. Null when cancelled.</summary>
        private string? PromptForText(string title, string prompt, string defaultValue)
        {
            using var dialog = new TextPromptForm(title, prompt, defaultValue);

            return dialog.ShowDialog(this) == DialogResult.OK ? dialog.Answer : null;
        }
    }
}

/// <summary>
/// The classes list as a dialog, for the places that reach it from
/// somewhere else - Driver Hub, where you are naming a driver's class
/// and realise the class does not exist yet.
///
/// A SHELL around the page, not a second copy of it. There were two lists
/// of classes with two sets of Add/Edit/Delete before this, and the moment
/// the page grew a Sanctioning Preset button the dialog did not have one -
/// which is precisely the kind of drift that makes two doors into two
/// different rooms.
///
/// On the layout pieces (DialogFit): the Classes page taking the height,
/// never less than the least it can do with, and Close under it against the
/// right-hand edge. It used to be two rectangles placed by hand on every
/// resize - the page, and the Close button a fixed band under it - over a
/// 760x480 floor whatever the text size.
/// </summary>
public sealed class RaceClassManagerForm : Form
{
    // handicaps: passed on to the Classes page so a delete here forgets the
    // deleted class's handicap mode, as it does on the page itself.
    public RaceClassManagerForm(RaceClassStore store, UndoService? undo = null, HandicapStore? handicaps = null)
    {
        Text = "Race Classes";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        var classes = new RaceClassesControl(store, undo, handicaps);

        var btnClose = new Button { Text = "Close", DialogResult = DialogResult.OK, Font = AppTheme.BodyFont };

        // No margins round the Classes page: it keeps its own, as it does in
        // the main window, and this page's on top of them would put the list
        // twice as far from the window's edges as from Close. Only under
        // Close, the page's usual bottom margin.
        var page = new FillPage { Padding = new Padding(0, 0, 0, UiLayout.PageTop) };

        // The least the Classes page can do with, at the width it is given:
        // its intro, the count, the grid's headings and three classes, and
        // its buttons (RaceClassesControl.LeastHeight).
        page.AddFill(classes, classes.LeastHeight, gapBelow: 0);

        // Against the right-hand edge, in from it by the page's margin - the
        // Classes page's margin, so Close lines up with the list above it.
        var closeBar = page.Add(new ButtonBar(alignRight: true).With(btnClose), gapBelow: 0);
        closeBar.Margin = new Padding(UiLayout.PageLeft, 0, UiLayout.PageRight, 0);

        Controls.Add(page);

        AcceptButton = btnClose;
        CancelButton = btnClose;

        AppTheme.ApplyRecursively(this);

        // The size it had at the normal text size. It can be made bigger.
        DialogFit.Keep(this, page, width: 940, height: 560);
    }
}
