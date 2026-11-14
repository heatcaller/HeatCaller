namespace HeatCaller;

/// <summary>
/// Night templates on the Setup tab (12 Nov 2026, setup plan B4): pick one
/// and Start Tonight starts a race day from it; Save As keeps the day on show
/// as a template. See NightTemplates.
/// </summary>
public sealed partial class RaceDayEventControl
{
    private readonly ComboBox _cmbTemplates = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 260 };
    private readonly Button _btnStartFromTemplate = new() { Text = "Start Tonight From It", AutoSize = true };
    private readonly Button _btnSaveTemplate = new() { Text = "Save This Day As...", AutoSize = true };
    private readonly Button _btnDeleteTemplate = new() { Text = "Delete", AutoSize = true };
    private readonly Button _btnTemplateDays = new() { Text = "When...", AutoSize = true };

    private sealed record TemplateChoice(NightTemplate Template)
    {
        public override string ToString() => NightTemplates.Describe(Template);
    }

    private Control BuildTemplatesRow()
    {
        _btnStartFromTemplate.Click += (_, _) => StartFromTemplate();
        _btnSaveTemplate.Click += (_, _) => SaveAsTemplate();
        _btnDeleteTemplate.Click += (_, _) => DeleteTemplate();
        _cmbTemplates.SelectedIndexChanged += (_, _) => ShowTemplateButtons();

        ShowTemplates();
        _btnTemplateDays.Click += (_, _) => EditTemplateDays();

        return FieldGrid.Inline(_cmbTemplates, _btnStartFromTemplate, _btnTemplateDays, _btnSaveTemplate, _btnDeleteTemplate);
    }

    private NightTemplate? PickedTemplate => (_cmbTemplates.SelectedItem as TemplateChoice)?.Template;

    private void ShowTemplates(string? select = null)
    {
        _cmbTemplates.Items.Clear();

        foreach (var template in _settings?.NightTemplates ?? new List<NightTemplate>())
        {
            _cmbTemplates.Items.Add(new TemplateChoice(template));

            if (select is not null && string.Equals(template.Name, select, StringComparison.OrdinalIgnoreCase))
            {
                _cmbTemplates.SelectedIndex = _cmbTemplates.Items.Count - 1;
            }
        }

        if (_cmbTemplates.SelectedIndex < 0 && _cmbTemplates.Items.Count > 0)
        {
            _cmbTemplates.SelectedIndex = 0;
        }

        ShowTemplateButtons();
    }

    private void ShowTemplateButtons()
    {
        var picked = PickedTemplate is not null;
        _cmbTemplates.Enabled = _cmbTemplates.Items.Count > 0;
        _btnStartFromTemplate.Enabled = picked;
        _btnDeleteTemplate.Enabled = picked;
        _btnTemplateDays.Enabled = picked;
        _btnSaveTemplate.Enabled = _settings is not null && Selected is { IsLegacy: false };
    }

    /// <summary>
    /// Start Tonight From It: the Start Tonight window, opened on the picked
    /// template (12 Nov 2026, setup plan step 8) - one way to start a night.
    /// </summary>
    private void StartFromTemplate()
    {
        if (PickedTemplate is { } template)
        {
            StartTonightWindow(StartTonightKind.Template, template.Name);
        }
    }

    /// <summary>
    /// Next Steps' "Start tonight's ..." (setup plan step 7): the Start Tonight
    /// window, opened on that template (setup plan step 8). False when there
    /// is no such template or the window was cancelled.
    /// </summary>
    public bool StartFromTemplateNamed(string name) =>
        _settings?.NightTemplates.Any(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)) == true
        && StartTonightWindow(StartTonightKind.Template, name);

    /// <summary>
    /// Starts a race day from a template: New Race Day's own path, then the
    /// template's settings over it. Only from the Start Tonight window
    /// (StartTonightAs), which says when a day is open already - it used to
    /// be asked here, in a second window.
    /// </summary>
    private void StartFromTemplate(NightTemplate template)
    {
        StartDayFrom(LastRealDay(), template.Shape, template.ClassNames.ToList());

        if (_events.Active is { IsLegacy: false } day)
        {
            NightTemplates.ApplyTo(template, day);
            day.TemplateName = template.Name.Trim();
            _events.Upsert(day);
        }

        RefreshAll();
        NightSetupChanged?.Invoke();
    }

    private void SaveAsTemplate()
    {
        if (_settings is null || Selected is not { IsLegacy: false } day)
        {
            return;
        }

        var name = AskForName(PickedTemplate?.Name ?? (day.IsClubNight ? "Club night" : "Race day"));

        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        // A template saved again keeps the nights it was set for (setup plan step 7).
        var fresh = NightTemplates.From(day, name);

        if (_settings.NightTemplates.FirstOrDefault(t => string.Equals(t.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)) is { } existing)
        {
            fresh.Weekdays = (existing.Weekdays ?? new List<DayOfWeek>()).ToList();
            fresh.Dates = (existing.Dates ?? new List<DateTime>()).ToList();
        }

        _settings.NightTemplates = NightTemplates.Saved(_settings.NightTemplates, fresh);
        _settings.Save();
        ShowTemplates(name);
    }

    private void DeleteTemplate()
    {
        if (_settings is null || PickedTemplate is not { } template
            || AppDialog.Show(this, $"Delete the template \"{template.Name}\"? Race days started from it aren't changed.", "Delete template",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        _settings.NightTemplates = _settings.NightTemplates.Where(t => !ReferenceEquals(t, template)).ToList();
        _settings.Save();
        ShowTemplates();
    }

    /// <summary>A small window asking for the template's name. Null when cancelled.</summary>
    private string? AskForName(string suggested)
    {
        using var form = new Form
        {
            Text = "Save as Template",
            Font = AppTheme.BodyFont,
            BackColor = AppTheme.Background,
            ShowInTaskbar = false,
            StartPosition = FormStartPosition.CenterParent,
        };

        var lbl = new Label
        {
            Text = "What to call it - \"Tuesday club night\", \"Points round\". It keeps this day's kind of night, classes and running order, "
                   + "rounds, heat size, seeding, fees, times, breaks and between-race talk. A template of the same name is replaced.",
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(420, 0),
        };

        var box = new TextBox { Text = suggested, Width = 300 };
        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var btnSave = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };

        var page = new FillPage();
        page.Add(lbl, stretch: false, gapBelow: UiLayout.RowGap);
        page.Add(box, stretch: false, gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnSave), gapBelow: 0);
        form.Controls.Add(page);
        form.AcceptButton = btnSave;
        form.CancelButton = btnCancel;

        AppTheme.ApplyRecursively(form);
        DialogFit.Keep(form, page, width: 480, height: 240);

        return form.ShowDialog(this) == DialogResult.OK ? box.Text.Trim() : null;
    }
}

/// <summary>Which nights a template is (12 Nov 2026, setup plan step 7): days of the week, and particular dates.</summary>
public sealed partial class RaceDayEventControl
{
    private void EditTemplateDays()
    {
        if (_settings is null || PickedTemplate is not { } template)
        {
            return;
        }

        using var form = new TemplateDaysForm(template);

        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        template.Weekdays = form.Weekdays.ToList();
        template.Dates = form.Dates.ToList();
        _settings.Save();
        ShowTemplates(template.Name);
        NightSetupChanged?.Invoke();
    }
}

/// <summary>A template's nights: a tick per day of the week, and a list of dates.</summary>
internal sealed class TemplateDaysForm : Form
{
    private readonly List<(DayOfWeek Day, CheckBox Box)> _days = new();
    private readonly ListBox _lstDates = new() { Height = 110, Width = 200, IntegralHeight = false };
    private readonly DateTimePicker _dtpDate = new() { Format = DateTimePickerFormat.Short, Width = 130 };

    public IEnumerable<DayOfWeek> Weekdays => _days.Where(d => d.Box.Checked).Select(d => d.Day);

    public IEnumerable<DateTime> Dates => _lstDates.Items.Cast<DateTime>();

    public TemplateDaysForm(NightTemplate template)
    {
        Text = $"When - {template.Name}";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;

        var lblHow = new Label
        {
            Text = "The nights this template is for. On one of them, with no race day open, Next Steps offers to start tonight from it. "
                   + "A date wins over a day of the week, so a monthly points round can fall on a club night's Tuesday.",
            AutoSize = true,
            MaximumSize = new System.Drawing.Size(460, 0),
            ForeColor = AppTheme.TextMuted,
        };

        var format = System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat;
        var week = new FlowLayoutPanel { AutoSize = true, WrapContents = true, MaximumSize = new System.Drawing.Size(460, 0) };

        foreach (var day in new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday })
        {
            var box = new ThemedCheckBox { Text = format.GetAbbreviatedDayName(day), AutoSize = true, Checked = (template.Weekdays ?? new List<DayOfWeek>()).Contains(day) };
            _days.Add((day, box));
            week.Controls.Add(box);
        }

        foreach (var date in (template.Dates ?? new List<DateTime>()).Select(d => d.Date).Where(d => d >= DateTime.Today).Distinct().OrderBy(d => d))
        {
            _lstDates.Items.Add(date);
        }

        _lstDates.FormatString = "ddd d MMM yyyy";
        _lstDates.FormattingEnabled = true;
        _dtpDate.MinDate = DateTime.Today;

        var btnAdd = new Button { Text = "Add Date", AutoSize = true };
        btnAdd.Click += (_, _) =>
        {
            var date = _dtpDate.Value.Date;

            if (!_lstDates.Items.Cast<DateTime>().Contains(date))
            {
                var at = _lstDates.Items.Cast<DateTime>().Count(d => d < date);
                _lstDates.Items.Insert(at, date);
            }
        };

        var btnRemove = new Button { Text = "Remove", AutoSize = true };
        btnRemove.Click += (_, _) =>
        {
            if (_lstDates.SelectedIndex >= 0)
            {
                _lstDates.Items.RemoveAt(_lstDates.SelectedIndex);
            }
        };

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var btnSave = new Button { Text = "Save", DialogResult = DialogResult.OK, AutoSize = true };

        var page = new FillPage();
        page.Add(lblHow, stretch: false, gapBelow: UiLayout.RowGap);
        page.Add(new Label { Text = "Every:", AutoSize = true }, stretch: false, gapBelow: UiLayout.HintGap);
        page.Add(week, stretch: false, gapBelow: UiLayout.RowGap);
        page.Add(new Label { Text = "And on these dates:", AutoSize = true }, stretch: false, gapBelow: UiLayout.HintGap);
        page.Add(_lstDates, stretch: false, gapBelow: UiLayout.HintGap);
        page.Add(new ButtonBar().With(_dtpDate, btnAdd, btnRemove), gapBelow: UiLayout.RowGap);
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnSave), gapBelow: 0);

        Controls.Add(page);
        AcceptButton = btnSave;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);
        DialogFit.Keep(this, page, width: 520, height: 460);
    }
}
