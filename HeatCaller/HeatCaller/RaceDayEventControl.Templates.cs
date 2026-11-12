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
        return FieldGrid.Inline(_cmbTemplates, _btnStartFromTemplate, _btnSaveTemplate, _btnDeleteTemplate);
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
        _btnSaveTemplate.Enabled = _settings is not null && Selected is { IsLegacy: false };
    }

    /// <summary>Starts a race day from the picked template: New Race Day's own path, then the template's settings over it.</summary>
    private void StartFromTemplate()
    {
        if (PickedTemplate is not { } template)
        {
            return;
        }

        if (_events.Active is { IsLegacy: false, IsOpen: true } open && !(open.RoundIds.Count == 0 && open.ClassNames.Count == 0)
            && AppDialog.Show(this, $"\"{open.DisplayName}\" is open already. Start a new race day from \"{template.Name}\" anyway?",
                "Start tonight from a template", MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            return;
        }

        StartDayFrom(LastRealDay(), template.Shape, template.ClassNames.ToList());

        if (_events.Active is { IsLegacy: false } day)
        {
            NightTemplates.ApplyTo(template, day);
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

        _settings.NightTemplates = NightTemplates.Saved(_settings.NightTemplates, NightTemplates.From(day, name));
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
