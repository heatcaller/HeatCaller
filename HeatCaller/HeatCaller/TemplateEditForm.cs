namespace HeatCaller;

/// <summary>
/// A night template, edited where it is kept (14 Nov 2026, Setup's Templates
/// tab), with what the night would come to along the bottom.
///
/// ONE SET OF FIELDS. The fields are Tonight's own - NightFields, the same
/// ones Setup's Tonight tab puts on its cards - working on a scratch race day
/// made from the template (NightTemplates.ApplyTo) and turned back into one
/// on Save (NightTemplates.From). So a night setting added to NightFields is
/// on both, and Breaks... and Order... open the same windows they do there.
///
/// Works on a copy: <see cref="Result"/> is the template as saved, and the
/// one handed in is never changed. Its nights (Weekdays, Dates) are carried
/// over untouched - When... on the card's menu sets those.
/// </summary>
internal sealed class TemplateEditForm : Form
{
    private readonly NightTemplate _original;
    private readonly IReadOnlyCollection<string> _takenNames;
    private readonly Func<NightTemplate, (string Line, bool Late)?>? _forecast;
    private readonly TextSize _textSize = new();
    private readonly NightFields _fields;

    /// <summary>The race day the fields edit: the template's settings, never saved as a day.</summary>
    private readonly RaceEvent _scratch = new();

    private readonly TextBox _txtName = new();
    private readonly Label _lblForecast = new() { AutoSize = true, UseMnemonic = false };
    private readonly Label _lblProblem = new() { AutoSize = true, UseMnemonic = false, Visible = false };

    /// <summary>The template as saved. Null until Save.</summary>
    public NightTemplate? Result { get; private set; }

    /// <param name="template">The template to edit; a fresh one for New Template.</param>
    /// <param name="isNew">Whether it's a new template - the window's caption says so.</param>
    /// <param name="takenNames">The other templates' names, so a rename can't land on one of them.</param>
    /// <param name="classes">The club's classes, in the Classes page's order.</param>
    /// <param name="venues">Venues to suggest.</param>
    /// <param name="layouts">Track layouts to suggest.</param>
    /// <param name="seasons">Championships.</param>
    /// <param name="forecast">What a night from the template as it stands would come to, or null when there's nothing to go by.</param>
    public TemplateEditForm(
        NightTemplate template,
        bool isNew,
        IReadOnlyCollection<string> takenNames,
        IReadOnlyList<string> classes,
        IEnumerable<string> venues,
        IEnumerable<string> layouts,
        IEnumerable<Season> seasons,
        Func<NightTemplate, (string Line, bool Late)?>? forecast)
    {
        _original = template;
        _takenNames = takenNames;
        _forecast = forecast;

        Text = isNew ? "New Template" : $"Edit Template - {template.Name}";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        // Built with its layout held, and laid out once when it is sized
        // (14 Nov 2026) - see LayoutFreeze.HoldWhileBuilding, and the Setup
        // Sheet, which does the same. Built live, five cards of fields laid
        // themselves out again for every field added and every face the
        // theme pass changed: 1.4 to 3 seconds to open.
        PageStack body;
        FillPage page;

        using (LayoutFreeze.HoldWhileBuilding())
        {
            _textSize.Wear(this, () => AppTheme.BodyFont);
            _fields = new NightFields(_textSize);

            // The template, as a day. A template that never set its rounds or heat
            // size keeps the day's defaults - 3, and 8 shown.
            NightTemplates.ApplyTo(template, _scratch);
            _fields.FillLists(classes, venues, layouts, seasons);
            _fields.Load(_scratch, template.DriversPerHeat > 0 ? template.DriversPerHeat : 8);

            _fields.EditBreaks = EditBreaks;
            _fields.EditClassOrder = EditClassOrder;
            _fields.Changed += ShowForecast;

            _txtName.Text = template.Name;

            var nightGrid = new FieldGrid();
            nightGrid.AddRow("Name:", _txtName);
            _fields.AddNightRows(nightGrid);

            var timesGrid = new FieldGrid();
            _fields.AddTimesRows(timesGrid);

            var heatsGrid = new FieldGrid();
            _fields.AddHeatsRows(heatsGrid);

            var moneyGrid = new FieldGrid();
            _fields.AddMoneyRows(moneyGrid);

            var runGrid = new FieldGrid();
            _fields.AddRunRows(runGrid);

            body = new PageStack();
            body.Add(Card("The night", nightGrid));
            body.Add(Card("Times", timesGrid));
            body.Add(Card("Heats", heatsGrid));
            body.Add(Card("Money", moneyGrid));
            body.Add(Card("Running the night", runGrid), gapBelow: 0);

            var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
            var btnSave = new Button { Text = "Save", AutoSize = true };
            btnSave.Click += (_, _) => Save();

            _lblForecast.ForeColor = AppTheme.TextMuted;
            _lblProblem.ForeColor = AppTheme.Danger;

            page = new FillPage();
            page.AddFill(body, 200, gapBelow: UiLayout.RowGap);
            page.Add(_lblForecast, stretch: true, gapBelow: UiLayout.HintGap);
            page.Add(_lblProblem, stretch: true, gapBelow: UiLayout.HintGap);
            page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnSave), gapBelow: 0);

            Controls.Add(page);
            AcceptButton = btnSave;
            CancelButton = btnCancel;

            AppTheme.ApplyRecursively(this);
            AppTheme.StyleButton(btnSave, AppTheme.ButtonKind.Primary);

            // Measured once everything wears the window's face, as on Tonight.
            _textSize.Keep(() => FieldGrid.LineUp(new[] { nightGrid, timesGrid, heatsGrid, moneyGrid, runGrid },
                NightFields.HeatsPerDriverCaption, NightFields.RacesPerDriverCaption));
            _textSize.Keep(() =>
            {
                _txtName.Width = UiLayout.AtTextSize(320);
                _fields.SizeFields();
            });

            ShowForecast();
        }

        DialogFit.Keep(this, page, width: 760, height: 720, body: body);

        // Held until DialogFit has sized the window, then laid out once at
        // that size, children first - as the Setup Sheet is.
        LayoutFreeze.Freeze(page);

        // Opened with the name not highlighted: it selects all as it takes
        // the focus. On Shown - after the focus has gone to it - and before
        // DialogFit makes the window visible, so it is never seen that way.
        // (Venue and Track layout: AppTheme.StyleComboBox.)
        Shown += (_, _) => _txtName.Select(_txtName.TextLength, 0);
    }

    private static Section Card(string caption, FieldGrid grid)
    {
        var card = new Section(caption);
        card.Add(grid, gapBelow: 0);
        return card;
    }

    /// <summary>Breaks...: the same window as on Tonight, over the template's breaks.</summary>
    private void EditBreaks()
    {
        _scratch.Shape = _fields.SelectedShape;

        using var form = new BreaksForm(_scratch.Breaks, (int)_fields.Rounds.Value, _scratch.IsClubNight);

        if (form.ShowDialog(this) == DialogResult.OK)
        {
            _scratch.Breaks = form.Breaks;
            _fields.ShowBreaks(_scratch);
            ShowForecast();
        }
    }

    /// <summary>Order...: the same window as on Tonight, over the classes ticked here.</summary>
    private void EditClassOrder()
    {
        _scratch.ClassNames = _fields.CheckedClasses;

        var ticked = _fields.ClassesOn(_scratch);

        if (_scratch.ClassNames.Count < 2)
        {
            AppDialog.Show(this, "Tick at least two classes under Classes racing first: with one class there is no order to set.",
                "Class running order", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var form = new ClassOrderForm(ClassRunningOrder.Arrange(ticked, n => n, _scratch.ClassRunningOrder, rotate: false, round: 0), _scratch.RotateClassOrder);

        if (form.ShowDialog(this) == DialogResult.OK)
        {
            _scratch.ClassRunningOrder = form.Order.ToList();
            _scratch.RotateClassOrder = form.Rotate;
            _fields.ShowClassOrder(_scratch);
        }
    }

    private void ShowForecast()
    {
        if (IsDisposed || _forecast is null)
        {
            _lblForecast.Visible = false;
            return;
        }

        var said = _forecast(Current(_txtName.Text.Trim()));

        _lblForecast.Visible = said is not null;
        _lblForecast.Text = said?.Line ?? string.Empty;
        _lblForecast.ForeColor = said is { Late: true } ? AppTheme.Warning : AppTheme.TextMuted;
    }

    /// <summary>The template as the window has it now, under <paramref name="name"/> - the fields written onto a copy of the scratch day.</summary>
    private NightTemplate Current(string name)
    {
        var day = System.Text.Json.JsonSerializer.Deserialize<RaceEvent>(System.Text.Json.JsonSerializer.Serialize(_scratch)) ?? new RaceEvent();

        _fields.WriteTo(day);

        var made = NightTemplates.From(day, name);

        // The running order keeps the classes still ticked, in their order.
        made.ClassRunningOrder = made.ClassRunningOrder.Where(c => made.ClassNames.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
        made.Weekdays = (_original.Weekdays ?? new List<DayOfWeek>()).ToList();
        made.Dates = (_original.Dates ?? new List<DateTime>()).ToList();

        return made;
    }

    /// <summary>Why the template as it stands can't be saved, or null when it can. Pure, for the tests.</summary>
    internal static string? Problem(string name, int classesTicked, IReadOnlyCollection<string> takenNames)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Give the template a name - \"Tuesday club night\", \"Points round\".";
        }

        if (takenNames.Any(t => string.Equals(t.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)))
        {
            return $"There's already a template called \"{name.Trim()}\". Pick another name.";
        }

        return classesTicked == 0 ? "Tick at least one class - a night with none races nobody." : null;
    }

    private void Save()
    {
        var name = _txtName.Text.Trim();

        if (Problem(name, _fields.CheckedClasses.Count, _takenNames) is { } problem)
        {
            _lblProblem.Text = problem;
            _lblProblem.Visible = true;
            return;
        }

        Result = Current(name);
        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>The template as the window has it now, without saving - for the tests.</summary>
    internal NightTemplate AsItStands => Current(_txtName.Text.Trim());

    /// <summary>The shared fields - for the tests.</summary>
    internal NightFields Fields => _fields;
}
