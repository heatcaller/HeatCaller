namespace HeatCaller;

/// <summary>
/// Fills in a setup sheet: the chassis diagram with its pinned callouts on
/// the left, and the template's sections of fields on the right.
///
/// The field editors are generated from the template rather than hardcoded,
/// so a chassis with different springs or an extra adjustment needs no code
/// change - just an edit to its template.
/// </summary>
public sealed class SetupEntryForm : Form
{
    private const string NotSet = "(not set)";

    private readonly ChassisTemplateStore _templates;
    private readonly SessionHistoryStore _history;

    private readonly ComboBox _cmbDriver = new();
    private readonly ComboBox _cmbTrack = new();
    private readonly ComboBox _cmbTemplate = new();
    private readonly ComboBox _cmbSession = new();
    private readonly TextBox _txtCar = new();

    private readonly ChassisCanvas _canvas = new();
    private readonly Panel _fieldsPanel = new();

    /// <summary>Editor control per field id, read back on save.</summary>
    private readonly Dictionary<string, Control> _editors = new();

    private ChassisTemplate _template;

    public SetupEntry Result { get; }

    public SetupEntryForm(SetupEntry? existing, ChassisTemplateStore templates,
        SessionHistoryStore history, ProfileStore profiles, TrackLayoutStore trackLayouts)
    {
        _templates = templates;
        _history = history;

        Result = existing ?? new SetupEntry();
        _template = _templates.Get(Result.ChassisTemplateId) ?? _templates.Fallback;

        // Old entries predate templates - fold their fixed fields into the
        // template's matching fields so nothing is stranded.
        if (Result.IsLegacy)
        {
            Result.MigrateLegacyFields(_template);
        }

        Text = "Setup Sheet";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1120, 760);
        MinimumSize = new Size(900, 600);
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        BuildHeader(profiles, trackLayouts);
        BuildCanvas();
        BuildFieldsPanel();
        BuildButtons();

        LoadTemplateIntoUi();
        AppTheme.ApplyRecursively(this);
    }

    private void BuildHeader(ProfileStore profiles, TrackLayoutStore trackLayouts)
    {
        var lblDriver = new Label { Text = "Driver:", Location = new Point(16, 18), AutoSize = true };
        _cmbDriver.Location = new Point(80, 15);
        _cmbDriver.Width = 190;
        _cmbDriver.DropDownStyle = ComboBoxStyle.DropDown;
        foreach (var name in profiles.All.Select(p => p.DriverName).Where(n => !string.IsNullOrWhiteSpace(n)).Distinct())
        {
            _cmbDriver.Items.Add(name);
        }
        _cmbDriver.Text = Result.DriverName;

        var lblTrack = new Label { Text = "Track:", Location = new Point(288, 18), AutoSize = true };
        _cmbTrack.Location = new Point(340, 15);
        _cmbTrack.Width = 180;
        _cmbTrack.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbTrack.Items.Add("(none)");
        foreach (var t in trackLayouts.All)
        {
            _cmbTrack.Items.Add(t.Name);
        }
        _cmbTrack.SelectedItem = string.IsNullOrWhiteSpace(Result.TrackLayoutName) ? "(none)" : Result.TrackLayoutName;
        if (_cmbTrack.SelectedIndex < 0) { _cmbTrack.SelectedIndex = 0; }

        var lblCar = new Label { Text = "Car:", Location = new Point(538, 18), AutoSize = true };
        _txtCar.Location = new Point(578, 15);
        _txtCar.Width = 170;
        _txtCar.Text = Result.CarModel;
        _txtCar.PlaceholderText = "e.g. MR-03 / Audi R8";

        var lblTemplate = new Label { Text = "Chassis sheet:", Location = new Point(16, 52), AutoSize = true };
        _cmbTemplate.Location = new Point(122, 49);
        _cmbTemplate.Width = 250;
        _cmbTemplate.DropDownStyle = ComboBoxStyle.DropDownList;
        RefreshTemplateList();
        _cmbTemplate.SelectedIndexChanged += (_, _) =>
        {
            if (_cmbTemplate.SelectedItem is TemplateItem item && item.Template.Id != _template.Id)
            {
                // Capture what's on screen before swapping, so switching
                // sheets and switching back doesn't lose anything typed.
                ReadEditorsIntoResult();
                _template = item.Template;
                LoadTemplateIntoUi();
            }
        };

        var lblSession = new Label { Text = "Ran in session:", Location = new Point(390, 52), AutoSize = true };
        _cmbSession.Location = new Point(500, 49);
        _cmbSession.Width = 330;
        _cmbSession.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbSession.Items.Add(SessionItem.None);
        foreach (var s in _history.All.Take(60))
        {
            _cmbSession.Items.Add(new SessionItem(s));
        }
        SelectSession(Result.LinkedSessionId);

        Controls.AddRange(new Control[]
        {
            lblDriver, _cmbDriver, lblTrack, _cmbTrack, lblCar, _txtCar,
            lblTemplate, _cmbTemplate, lblSession, _cmbSession,
        });
    }

    private void BuildCanvas()
    {
        _canvas.Location = new Point(16, 88);
        _canvas.Size = new Size(520, 600);
        _canvas.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left;
        _canvas.EditMode = false;

        // Clicking a callout jumps to its editor on the right, which is what
        // ties the picture to the form rather than leaving them separate.
        _canvas.SelectionChanged += FocusSelectedField;
        _canvas.PointActivated += _ => FocusSelectedField();

        Controls.Add(_canvas);
    }

    private void BuildFieldsPanel()
    {
        _fieldsPanel.Location = new Point(548, 88);
        _fieldsPanel.Size = new Size(556, 600);
        _fieldsPanel.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _fieldsPanel.AutoScroll = true;
        _fieldsPanel.BackColor = AppTheme.Background;
        Controls.Add(_fieldsPanel);
    }

    private void BuildButtons()
    {
        var btnSave = new Button { Text = "Save", Location = new Point(916, 706), Width = 90, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        btnSave.Click += BtnSave_Click;

        var btnCancel = new Button { Text = "Cancel", Location = new Point(1014, 706), Width = 90, Anchor = AnchorStyles.Bottom | AnchorStyles.Right, DialogResult = DialogResult.Cancel };

        var lblHint = new Label
        {
            Text = "Click a callout on the diagram to jump to its field. Filled callouts are highlighted.",
            Location = new Point(16, 712),
            AutoSize = true,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        Controls.Add(btnSave);
        Controls.Add(btnCancel);
        Controls.Add(lblHint);

        AcceptButton = btnSave;
        CancelButton = btnCancel;
    }

    private void RefreshTemplateList()
    {
        _cmbTemplate.Items.Clear();
        foreach (var t in _templates.All)
        {
            _cmbTemplate.Items.Add(new TemplateItem(t));
        }

        for (var i = 0; i < _cmbTemplate.Items.Count; i++)
        {
            if (_cmbTemplate.Items[i] is TemplateItem item && item.Template.Id == _template.Id)
            {
                _cmbTemplate.SelectedIndex = i;
                return;
            }
        }

        if (_cmbTemplate.Items.Count > 0)
        {
            _cmbTemplate.SelectedIndex = 0;
        }
    }

    /// <summary>Rebuilds the diagram and the generated field editors for the current template.</summary>
    private void LoadTemplateIntoUi()
    {
        _canvas.Values = Result.Values;
        _canvas.SetTemplate(_template, _templates.ResolveImagePath(_template));

        _fieldsPanel.Controls.Clear();
        _editors.Clear();

        var y = 8;

        foreach (var section in _template.Sections)
        {
            if (section.Fields.Count == 0)
            {
                continue;
            }

            var header = new Label
            {
                Text = section.Name.ToUpperInvariant(),
                Location = new Point(6, y),
                AutoSize = true,
                Font = AppTheme.BoldFont,
                ForeColor = AppTheme.Primary,
            };
            _fieldsPanel.Controls.Add(header);
            y += 24;

            foreach (var field in section.Fields)
            {
                y = AddFieldEditor(field, y);
            }

            y += 10;
        }
    }

    /// <summary>Creates the right editor for a field's kind and returns the next Y position.</summary>
    private int AddFieldEditor(SetupField field, int y)
    {
        var suffix = string.IsNullOrWhiteSpace(field.Unit) ? "" : $"  ({field.Unit})";
        var label = new Label
        {
            Text = field.Label + suffix,
            Location = new Point(10, y + 4),
            AutoSize = true,
            MaximumSize = new Size(210, 0),
        };
        _fieldsPanel.Controls.Add(label);

        // Pinned fields get a marker, so it's clear which ones also appear
        // on the diagram.
        if (field.OnDiagram)
        {
            _fieldsPanel.Controls.Add(new Label
            {
                Text = "\u25CF",
                Location = new Point(2, y + 4),
                AutoSize = true,
                ForeColor = AppTheme.Success,
                Font = AppTheme.SmallFont,
            });
        }

        var current = Result.Values.TryGetValue(field.Id, out var existing) ? existing : string.Empty;
        var editorX = 232;
        var editorWidth = 280;

        switch (field.Kind)
        {
            case SetupFieldKind.Choice:
            {
                var combo = new ComboBox
                {
                    Location = new Point(editorX, y),
                    Width = editorWidth,
                    DropDownStyle = ComboBoxStyle.DropDownList,
                };
                combo.Items.Add(NotSet);
                foreach (var option in field.Options)
                {
                    combo.Items.Add(option);
                }
                combo.SelectedItem = string.IsNullOrWhiteSpace(current) ? NotSet : current;
                if (combo.SelectedIndex < 0)
                {
                    // A value that isn't in the option list any more (the
                    // template was edited) - keep it rather than silently
                    // discarding what was recorded.
                    combo.Items.Add(current);
                    combo.SelectedItem = current;
                }
                combo.SelectedIndexChanged += (_, _) => { ReadEditorsIntoResult(); _canvas.Invalidate(); };
                _fieldsPanel.Controls.Add(combo);
                _editors[field.Id] = combo;
                return y + 32;
            }

            case SetupFieldKind.MultiChoice:
            {
                var height = Math.Min(96, Math.Max(40, field.Options.Count * 18 + 6));
                var list = new CheckedListBox
                {
                    Location = new Point(editorX, y),
                    Size = new Size(editorWidth, height),
                    CheckOnClick = true,
                    BorderStyle = BorderStyle.FixedSingle,
                };
                var selected = current.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                foreach (var option in field.Options)
                {
                    var index = list.Items.Add(option);
                    if (selected.Contains(option, StringComparer.OrdinalIgnoreCase))
                    {
                        list.SetItemChecked(index, true);
                    }
                }
                list.ItemCheck += (_, _) =>
                {
                    if (!IsHandleCreated || IsDisposed) { return; }
                    BeginInvoke(() => { ReadEditorsIntoResult(); _canvas.Invalidate(); });
                };
                _fieldsPanel.Controls.Add(list);
                _editors[field.Id] = list;
                return y + height + 8;
            }

            case SetupFieldKind.Boolean:
            {
                var check = new CheckBox
                {
                    Location = new Point(editorX, y + 2),
                    AutoSize = true,
                    Checked = string.Equals(current, "true", StringComparison.OrdinalIgnoreCase),
                };
                check.CheckedChanged += (_, _) => { ReadEditorsIntoResult(); _canvas.Invalidate(); };
                _fieldsPanel.Controls.Add(check);
                _editors[field.Id] = check;
                return y + 28;
            }

            default:
            {
                var box = new TextBox
                {
                    Location = new Point(editorX, y),
                    Width = editorWidth,
                    Text = current,
                };
                if (field.Kind == SetupFieldKind.Number)
                {
                    box.PlaceholderText = field.Unit;
                }
                box.Leave += (_, _) => { ReadEditorsIntoResult(); _canvas.Invalidate(); };
                _fieldsPanel.Controls.Add(box);
                _editors[field.Id] = box;
                return y + 32;
            }
        }
    }

    /// <summary>Pulls every editor's current state into Result.Values.</summary>
    private void ReadEditorsIntoResult()
    {
        foreach (var (fieldId, editor) in _editors)
        {
            var value = editor switch
            {
                ComboBox combo => combo.SelectedItem as string is { } s && s != NotSet ? s : string.Empty,
                CheckedListBox list => string.Join(", ", list.CheckedItems.Cast<object>().Select(o => o.ToString())),
                CheckBox check => check.Checked ? "true" : string.Empty,
                TextBox box => box.Text.Trim(),
                _ => string.Empty,
            };

            if (string.IsNullOrWhiteSpace(value))
            {
                Result.Values.Remove(fieldId);
            }
            else
            {
                Result.Values[fieldId] = value;
            }
        }
    }

    /// <summary>Scrolls the right-hand panel to the editor for whichever callout is selected.</summary>
    private void FocusSelectedField()
    {
        if (_canvas.SelectedPointId is not { } id || !_editors.TryGetValue(id, out var editor))
        {
            return;
        }

        _fieldsPanel.ScrollControlIntoView(editor);
        editor.Focus();
    }

    private void SelectSession(string? sessionId)
    {
        if (sessionId is not null)
        {
            for (var i = 0; i < _cmbSession.Items.Count; i++)
            {
                if (_cmbSession.Items[i] is SessionItem item && item.Session?.Id == sessionId)
                {
                    _cmbSession.SelectedIndex = i;
                    return;
                }
            }
        }
        _cmbSession.SelectedIndex = 0;
    }

    private void BtnSave_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_cmbDriver.Text))
        {
            AppDialog.Show(this, "Enter a driver name.", "Missing driver", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ReadEditorsIntoResult();

        Result.DriverName = _cmbDriver.Text.Trim();
        var track = _cmbTrack.SelectedItem as string;
        Result.TrackLayoutName = track is null or "(none)" ? string.Empty : track;
        Result.CarModel = _txtCar.Text.Trim();
        Result.ChassisTemplateId = _template.Id;
        Result.LinkedSessionId = (_cmbSession.SelectedItem as SessionItem)?.Session?.Id;

        DialogResult = DialogResult.OK;
        Close();
    }

    private sealed record TemplateItem(ChassisTemplate Template)
    {
        public override string ToString() => Template.Name;
    }

    private sealed record SessionItem(SessionRecord? Session)
    {
        public static readonly SessionItem None = new((SessionRecord?)null);

        public override string ToString()
        {
            if (Session is null)
            {
                return "(not linked to a session)";
            }

            var track = string.IsNullOrWhiteSpace(Session.TrackLayoutName) ? "" : $" - {Session.TrackLayoutName}";
            return $"{AppTime.DateAndTime(Session.StartedAtUtc.ToLocalTime())}{track} - {Session.Label}";
        }
    }
}
