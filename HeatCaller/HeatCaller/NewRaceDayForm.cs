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
///
/// On the layout pieces (DialogFit): the two questions one under the other,
/// the classes taking the height, and Start the Day and Cancel at the foot.
/// The hint under the kind of night is as tall as the longer of its two
/// wordings (SteadyLabel), so the classes never move as the box is used. It
/// used to be placed in pixels in a column 520px wide at every text size,
/// the two hints in boxes kept two lines tall by hand - at a larger text
/// size they needed more - and the classes six rows tall.
/// </summary>
public sealed class NewRaceDayForm : Form
{
    /// <summary>The classes show at least this many before the window scrolls instead.</summary>
    private const int LeastRows = 4;

    private const string RaceDayShapeHint =
        "Qualifying heats, then finals built from them. Heats are built from drivers who have checked in.";

    private const string ClubNightShapeHint =
        "A flat list of races - no finals, no seeding, no bump-ups. Everyone in a racing class is in them.";

    private readonly ComboBox _cmbShape = new();

    /// <summary>The hint under the shape picker, worded from it - as tall as the longer wording, so the class list never moves as the box is used.</summary>
    private readonly SteadyLabel _lblShapeHint = new() { Wordings = new[] { RaceDayShapeHint, ClubNightShapeHint } };

    private readonly CheckedListBox _lstClasses = new ThemedCheckedListBox();
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
        : this(StartTonight.Options(null, null), 0, classNames, alreadyRacing, shape)
    {
    }

    /// <summary>The choices at the top, in order - see StartTonight.Options.</summary>
    private readonly IReadOnlyList<StartTonightOption> _options;

    /// <summary>One radio button per choice, in the same order as _options.</summary>
    private readonly List<RadioButton> _choiceButtons = new();

    /// <summary>What was chosen at the top. Only meaningful after DialogResult.OK.</summary>
    public StartTonightOption Choice
    {
        get
        {
            var picked = _choiceButtons.FindIndex(b => b.Checked);
            return _options[picked >= 0 ? picked : _options.Count - 1];
        }
    }

    /// <summary>
    /// The Start Tonight window (12 Nov 2026, setup plan step 8): the ways to
    /// start tonight at the top - like last time, each night template, or
    /// something different - and the two questions under them, which only
    /// "Something different..." uses. Every door that starts a race day opens
    /// this - see StartTonight. With only "Something different..." to offer (a
    /// club's first night), the choices aren't shown and it is the two
    /// questions, as it always was.
    /// </summary>
    /// <param name="options">The choices, from StartTonight.Options. The last is always "Something different...".</param>
    /// <param name="selected">The choice picked when it opens, from StartTonight.Preselect.</param>
    /// <param name="openDay">A race day still open with something in it, which starting tonight leaves as it is - said in the window rather than asked in a second one. Null for none.</param>
    public NewRaceDayForm(
        IReadOnlyList<StartTonightOption> options,
        int selected,
        IReadOnlyList<string> classNames,
        IReadOnlyCollection<string> alreadyRacing,
        RaceDayShape shape,
        string? openDay = null)
    {
        _options = options.Count > 0 ? options : StartTonight.Options(null, null);

        Text = "Start Tonight";
        Font = AppTheme.BodyFont;

        var choosing = _options.Count > 1;

        var lblIntro = new Label
        {
            Text = choosing
                ? "How would you like to start tonight? Whichever you pick, everything can be changed afterwards on Race Day > Setup, "
                  + "and Undo takes the new day away again."
                : "Two questions, and the day is ready to build. Everything else - the name, the venue, "
                  + "the track and the numbers - starts from your last race day and can be changed on Race Day > Setup.",
            AutoSize = true,
            Font = AppTheme.BodyFont,
            ForeColor = AppTheme.TextMuted,
        };

        // The choices: a radio button each, with what it would start under it.
        var choiceParts = new List<(Control Part, int GapBelow)>();

        if (choosing)
        {
            for (var i = 0; i < _options.Count; i++)
            {
                var option = _options[i];
                var button = new ThemedRadioButton { Text = option.Caption, AutoSize = true, Font = AppTheme.BoldFont, Checked = i == Math.Clamp(selected, 0, _options.Count - 1) };
                button.CheckedChanged += (_, _) => ApplyChoice();
                _choiceButtons.Add(button);

                var detail = new Label { Text = option.Detail, AutoSize = true, Font = AppTheme.SmallFont, ForeColor = AppTheme.TextMuted };

                choiceParts.Add((button, UiLayout.HintGap));
                choiceParts.Add((detail, UiLayout.RowGap));
            }
        }

        // A day already open is said here, not asked about in a second window.
        if (!string.IsNullOrWhiteSpace(openDay))
        {
            choiceParts.Add((new Label
            {
                Text = $"\"{openDay}\" is still open. Starting tonight makes the new day current and leaves that one as it is - close it on Setup or the Calendar.",
                AutoSize = true,
                Font = AppTheme.SmallFont,
                ForeColor = AppTheme.Warning,
            }, UiLayout.RowGap));
        }

        // --- What kind of night ---

        var lblShape = new Label
        {
            Text = "What kind of night is it?",
            AutoSize = true,
            Font = AppTheme.BoldFont,
        };

        _cmbShape.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbShape.Font = AppTheme.BodyFont;
        _cmbShape.Items.Add(new ShapeChoice(RaceDayShape.RaceDay, "Race day - heats, then finals"));
        _cmbShape.Items.Add(new ShapeChoice(RaceDayShape.ClubNight, "Club night - just races"));
        _cmbShape.SelectedIndex = shape == RaceDayShape.ClubNight ? 1 : 0;
        _cmbShape.SelectedIndexChanged += (_, _) => ApplyShapeHint();

        // The text is swapped as the box is used; the label is as tall as the
        // longer wording at the width it has (see the field), so it never
        // grows by a line and pushes the class list down the dialog mid-use.
        _lblShapeHint.Font = AppTheme.SmallFont;
        _lblShapeHint.ForeColor = AppTheme.TextMuted;

        // --- Who is racing ---

        var lblClasses = new Label
        {
            Text = "Which classes are racing?",
            AutoSize = true,
            Font = AppTheme.BoldFont,
        };

        _lstClasses.CheckOnClick = true;
        _lstClasses.BorderStyle = BorderStyle.FixedSingle;

        // Given the height the window has, not snapped to whole rows - see FillPage.AddFill.
        _lstClasses.IntegralHeight = false;
        _lstClasses.Font = AppTheme.BodyFont;

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

        // The empty state. A club with no classes yet can still start the
        // day - the Today checklist's next step is setting them up, and
        // refusing here would be a dead end with the same words on it.
        _lblNoClasses.AutoSize = true;
        _lblNoClasses.Font = AppTheme.SmallFont;
        _lblNoClasses.ForeColor = AppTheme.TextMuted;

        // "programme", not "heats": this label is written once, while the
        // shape picker above it can still be changed, and a club night has
        // races where a race day has heats. A word that is true of both
        // beats a handler on the picker keeping a hint honest.
        _lblNoClasses.Text = _lstClasses.Items.Count == 0
            ? "There are no classes on file yet. Start the day anyway - Next Steps will walk you through setting them up."
            : "A class is the rulebook a group of drivers races under. Tonight's program is built for the ones ticked here.";

        // --- The actions ---

        var btnOk = new Button { Text = "Start Tonight", Font = AppTheme.BodyFont, DialogResult = DialogResult.OK };
        var btnCancel = new Button { Text = "Cancel", Font = AppTheme.BodyFont, DialogResult = DialogResult.Cancel };

        var page = new FillPage();
        page.Add(lblIntro);

        foreach (var (part, gap) in choiceParts)
        {
            page.Add(part, stretch: part is not RadioButton, gapBelow: gap);
        }

        _questionParts = new Control[] { lblShape, _cmbShape, _lblShapeHint, lblClasses, _lstClasses, _lblNoClasses };

        page.Add(lblShape, gapBelow: UiLayout.LabelGap);

        // Its own width (see below), not the page's.
        page.Add(_cmbShape, stretch: false, gapBelow: UiLayout.HintGap);
        Pieces.KeepHeightTold(_cmbShape);

        page.Add(_lblShapeHint);
        page.Add(lblClasses, gapBelow: UiLayout.LabelGap);
        page.AddFill(_lstClasses, _ => LeastRows * _lstClasses.ItemHeight + 4, gapBelow: UiLayout.HintGap);
        page.Add(_lblNoClasses);

        // Against the right-hand edge, Cancel furthest right, as a dialog's
        // buttons go - and Tab still reaches Start Tonight first, as it did:
        // the bar takes them right to left, in the order they were added.
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnOk), gapBelow: 0);
        btnOk.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        ApplyShapeHint();

        AppTheme.ApplyRecursively(this);
        ApplyChoice();

        // After the theme pass, in the face it leaves it in: as wide as it
        // was made at the normal text size, at the size in use, or as its
        // longer choice if that is wider.
        (Font Font, int Count, int Width)? shapeWidth = null;
        _cmbShape.Width = Math.Max(UiLayout.AtTextSize(320), Pieces.PickerWidth(_cmbShape, ref shapeWidth));

        // The width it had at the normal text size, and about the height -
        // the classes about six rows tall. It can be made taller, for a club
        // with more classes than that.
        // Taller by the choices at the top, when there are any.
        DialogFit.Keep(this, page, width: 552, height: choosing ? 500 + 52 * _options.Count : 500);
    }

    /// <summary>The two questions and what they are shown with - only "Something different..." uses them.</summary>
    private Control[] _questionParts = Array.Empty<Control>();

    /// <summary>
    /// The two questions greyed unless "Something different..." is picked:
    /// like last time and a template bring their own kind of night and
    /// classes. Greyed rather than hidden, so the window doesn't change shape
    /// as the choices are tried.
    /// </summary>
    private void ApplyChoice()
    {
        var asking = Choice.Kind == StartTonightKind.Different;

        foreach (var part in _questionParts)
        {
            part.Enabled = asking;
        }
    }

    private void ApplyShapeHint() =>
        _lblShapeHint.Text = Shape == RaceDayShape.ClubNight ? ClubNightShapeHint : RaceDayShapeHint;
}
