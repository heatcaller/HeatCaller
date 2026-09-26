namespace HeatCaller;

/// <summary>
/// What kind of night it is, and who is racing. Nothing else.
///
/// WHY THIS EXISTS. "New Race Day" used to make a day with NO CLASSES
/// TICKED and drop the director into a form of eight fields with the name
/// selected. Nothing worked from that state: the programme cannot be built
/// for a day that names no classes, and the banner that says so sends you
/// back to the same tab you are already on. The commonest first action in
/// the app produced a day that could not be used, at the one moment - a
/// room filling up - when there is least time to work out why.
///
/// So the dialog asks the two things only the person starting the day can
/// answer, and every other field takes a sensible default and stays
/// editable on The Day afterwards:
///
///   - the name is today's date (EventSpine.DefaultDayName)
///   - venue, track layout and championship come from the last real day
///   - rounds and heat size come from the last real day
///   - the check-in rule comes from the shape (EventSpine.StartEvent)
///
/// Carrying the last day's settings forward is the whole point of the
/// defaults: a club runs the same shape of night most weeks, and typing the
/// same venue in every Tuesday is not a decision, it is a toll.
/// </summary>
public sealed class NewRaceDayForm : Form
{
    private readonly ComboBox _cmbShape = new();
    private readonly Label _lblShapeHint = new();
    private readonly CheckedListBox _lstClasses = new();
    private readonly Label _lblNoClasses = new();

    /// <summary>One entry in the shape picker. ToString is what the combo draws.</summary>
    private sealed record ShapeChoice(RaceDayShape Shape, string Caption)
    {
        public override string ToString() => Caption;
    }

    /// <summary>What kind of night was chosen. Only meaningful after DialogResult.OK.</summary>
    public RaceDayShape Shape =>
        _cmbShape.SelectedItem is ShapeChoice choice ? choice.Shape : RaceDayShape.RaceDay;

    /// <summary>The classes ticked, in the order they were offered. Only meaningful after DialogResult.OK.</summary>
    public List<string> ClassNames => _lstClasses.CheckedItems
        .Cast<object>()
        .Select(item => item as string ?? string.Empty)
        .Where(name => name.Length > 0)
        .ToList();

    /// <param name="classNames">Every class on file, in the order the club keeps them.</param>
    /// <param name="alreadyRacing">
    /// Classes to start ticked - the last real day's, so a club running the
    /// same three classes every week ticks nothing at all. Empty on a first
    /// run, which is the one case where the list genuinely has to be read.
    /// </param>
    /// <param name="shape">The shape to open on, again from the last real day.</param>
    public NewRaceDayForm(
        IReadOnlyList<string> classNames,
        IReadOnlyCollection<string> alreadyRacing,
        RaceDayShape shape)
    {
        Text = "Start a race day";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;

        var y = UiLayout.PageTop;

        var lblIntro = new Label
        {
            Text = "Two questions, and the day is ready to build. Everything else - the name, the venue, "
                 + "the track and the numbers - starts from your last race day and can be changed on Race Day > Setup.",
            AutoSize = true,
            MaximumSize = new Size(520, 0),
            Font = AppTheme.BodyFont,
            ForeColor = AppTheme.TextMuted,
            Location = new Point(UiLayout.PageLeft, y),
        };

        Controls.Add(lblIntro);

        y = UiLayout.Below(lblIntro, UiLayout.SectionGap);

        // --- What kind of night ---

        var lblShape = new Label
        {
            Text = "What kind of night is it?",
            AutoSize = true,
            Font = AppTheme.BoldFont,
            Location = new Point(UiLayout.PageLeft, y),
        };

        Controls.Add(lblShape);

        y = UiLayout.Below(lblShape, UiLayout.LabelGap);

        _cmbShape.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbShape.Font = AppTheme.BodyFont;
        _cmbShape.Width = 320;
        _cmbShape.Location = new Point(UiLayout.PageLeft, y);
        _cmbShape.Items.Add(new ShapeChoice(RaceDayShape.RaceDay, "Race day - heats, then finals"));
        _cmbShape.Items.Add(new ShapeChoice(RaceDayShape.ClubNight, "Club night - just races"));
        _cmbShape.SelectedIndex = shape == RaceDayShape.ClubNight ? 1 : 0;
        _cmbShape.SelectedIndexChanged += (_, _) => ApplyShapeHint();

        Controls.Add(_cmbShape);

        y = UiLayout.Below(_cmbShape, UiLayout.HintGap);

        // Fixed at two lines: the text is swapped as the box is used, and an
        // AutoSize label would grow by a line and push the class list down
        // the dialog mid-use.
        _lblShapeHint.AutoSize = false;
        _lblShapeHint.Font = AppTheme.SmallFont;
        _lblShapeHint.ForeColor = AppTheme.TextMuted;
        _lblShapeHint.Location = new Point(UiLayout.PageLeft, y);
        _lblShapeHint.Size = new Size(520, AppTheme.SmallFont.Height * 2 + 4);

        Controls.Add(_lblShapeHint);

        y = UiLayout.Below(_lblShapeHint, UiLayout.SectionGap);

        // --- Who is racing ---

        var lblClasses = new Label
        {
            Text = "Which classes are racing?",
            AutoSize = true,
            Font = AppTheme.BoldFont,
            Location = new Point(UiLayout.PageLeft, y),
        };

        Controls.Add(lblClasses);

        y = UiLayout.Below(lblClasses, UiLayout.LabelGap);

        _lstClasses.CheckOnClick = true;
        _lstClasses.BorderStyle = BorderStyle.FixedSingle;
        _lstClasses.IntegralHeight = false;
        _lstClasses.Font = AppTheme.BodyFont;
        _lstClasses.Width = 320;
        _lstClasses.Height = AppTheme.BodyFont.Height * 6 + 12;
        _lstClasses.Location = new Point(UiLayout.PageLeft, y);

        foreach (var name in classNames)
        {
            var index = _lstClasses.Items.Add(name);

            _lstClasses.SetItemChecked(index,
                alreadyRacing.Contains(name, StringComparer.OrdinalIgnoreCase));
        }

        // Every class on a first run. A club with three classes and no
        // previous day would otherwise have to tick all three to get the
        // only outcome they could have wanted, and a day with none ticked is
        // the trap this dialog exists to close.
        if (alreadyRacing.Count == 0)
        {
            for (var i = 0; i < _lstClasses.Items.Count; i++)
            {
                _lstClasses.SetItemChecked(i, true);
            }
        }

        Controls.Add(_lstClasses);

        // The empty state. A club with no classes yet can still start the
        // day - the Today checklist's next step is setting them up, and
        // refusing here would be a dead end with the same words on it.
        _lblNoClasses.AutoSize = false;
        _lblNoClasses.Font = AppTheme.SmallFont;
        _lblNoClasses.ForeColor = AppTheme.TextMuted;
        _lblNoClasses.Location = new Point(UiLayout.PageLeft, UiLayout.Below(_lstClasses, UiLayout.HintGap));
        _lblNoClasses.Size = new Size(520, AppTheme.SmallFont.Height * 2 + 4);

        // "programme", not "heats": this label is written once, while the
        // shape picker above it can still be changed, and a club night has
        // races where a race day has heats. A word that is true of both
        // beats a handler on the picker keeping a hint honest.
        _lblNoClasses.Text = _lstClasses.Items.Count == 0
            ? "There are no classes on file yet. Start the day anyway - Next Steps will walk you through setting them up."
            : "A class is the rulebook a group of drivers races under. Tonight's program is built for the ones ticked here.";

        Controls.Add(_lblNoClasses);

        y = UiLayout.Below(_lblNoClasses, UiLayout.SectionGap);

        // --- The actions ---

        var btnOk = new Button { Text = "Start the Day", Font = AppTheme.BodyFont, DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "Cancel", Font = AppTheme.BodyFont, DialogResult = DialogResult.Cancel };

        btnOk.Size = new Size(UiLayout.WidthFor(btnOk), UiLayout.ButtonHeight);
        btnCancel.Size = new Size(UiLayout.WidthFor(btnCancel), UiLayout.ButtonHeight);

        var right = UiLayout.PageLeft + 520;

        btnCancel.Location = new Point(right - btnCancel.Width, y);
        btnOk.Location = new Point(btnCancel.Left - UiLayout.ButtonGap - btnOk.Width, y);

        Controls.Add(btnOk);
        Controls.Add(btnCancel);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        ApplyShapeHint();

        AppTheme.ApplyRecursively(this);

        // Measured from what ended up on the form rather than set in
        // numbers, so a larger text size grows the dialog with it instead of
        // cutting the buttons off.
        ClientSize = new Size(
            right + UiLayout.PageLeft,
            UiLayout.Below(btnOk, UiLayout.PageTop));
    }

    private void ApplyShapeHint() =>
        _lblShapeHint.Text = Shape == RaceDayShape.ClubNight
            ? "A flat list of races - no finals, no seeding, no bump-ups. Everyone in a racing class is in them."
            : "Qualifying heats, then finals built from them. Heats are built from drivers who have checked in.";
}
