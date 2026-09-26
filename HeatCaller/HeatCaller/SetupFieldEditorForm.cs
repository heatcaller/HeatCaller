namespace HeatCaller;

/// <summary>
/// Edits one setup field: its label, how it's filled in, its unit, and
/// crucially its list of options.
///
/// The seeded Kyosho options are a starting point, not gospel - parts get
/// discontinued, new spring colours appear, and every club has its own
/// shorthand. Rather than shipping a fixed list that slowly goes stale,
/// every option is editable here.
/// </summary>
public sealed class SetupFieldEditorForm : Form
{
    private readonly SetupField _field;

    private readonly TextBox _txtLabel = new();
    private readonly ComboBox _cmbKind = new();
    private readonly TextBox _txtUnit = new();
    private readonly CheckBox _chkOnDiagram = new();
    private readonly ListBox _lstOptions = new();
    private readonly Label _lblOptionsHint = new();

    public SetupFieldEditorForm(SetupField field)
    {
        _field = field;

        Text = "Edit Field";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(470, 470);
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        var lblLabel = new Label { Text = "Label:", Location = new Point(16, 20), AutoSize = true };
        _txtLabel.Location = new Point(110, 17);
        _txtLabel.Width = 340;
        _txtLabel.Text = _field.Label;

        var lblKind = new Label { Text = "Filled in as:", Location = new Point(16, 56), AutoSize = true };
        _cmbKind.Location = new Point(110, 53);
        _cmbKind.Width = 340;
        _cmbKind.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbKind.Items.AddRange(new object[]
        {
            new KindItem(SetupFieldKind.Choice, "Pick one option (dropdown)"),
            new KindItem(SetupFieldKind.MultiChoice, "Tick any options (checklist)"),
            new KindItem(SetupFieldKind.Number, "Number with a unit"),
            new KindItem(SetupFieldKind.Text, "Free text"),
            new KindItem(SetupFieldKind.Boolean, "Yes / no tickbox"),
        });
        SelectKind(_field.Kind);
        _cmbKind.SelectedIndexChanged += (_, _) => UpdateOptionsEnabled();

        var lblUnit = new Label { Text = "Unit:", Location = new Point(16, 92), AutoSize = true };
        _txtUnit.Location = new Point(110, 89);
        _txtUnit.Width = 120;
        _txtUnit.Text = _field.Unit;
        _txtUnit.PlaceholderText = "mm, \u00B0, pcs";

        _chkOnDiagram.Text = "Show as a callout on the chassis diagram";
        _chkOnDiagram.Location = new Point(110, 120);
        _chkOnDiagram.AutoSize = true;
        _chkOnDiagram.Checked = _field.OnDiagram;

        var lblOptions = new Label { Text = "Options:", Location = new Point(16, 156), AutoSize = true, Font = AppTheme.BoldFont };

        _lstOptions.Location = new Point(16, 180);
        _lstOptions.Size = new Size(300, 210);
        _lstOptions.BackColor = AppTheme.Surface;
        foreach (var option in _field.Options)
        {
            _lstOptions.Items.Add(option);
        }

        var btnAdd = new Button { Text = "Add...", Location = new Point(326, 180), Width = 124 };
        btnAdd.Click += (_, _) =>
        {
            var name = Prompt("Add option", "Option text:", string.Empty);
            if (!string.IsNullOrWhiteSpace(name))
            {
                _lstOptions.Items.Add(name.Trim());
                _lstOptions.SelectedIndex = _lstOptions.Items.Count - 1;
            }
        };

        var btnEdit = new Button { Text = "Rename...", Location = new Point(326, 216), Width = 124 };
        btnEdit.Click += (_, _) =>
        {
            if (_lstOptions.SelectedIndex < 0) { return; }

            var index = _lstOptions.SelectedIndex;
            var name = Prompt("Rename option", "Option text:", _lstOptions.Items[index]?.ToString() ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(name))
            {
                _lstOptions.Items[index] = name.Trim();
                _lstOptions.SelectedIndex = index;
            }
        };

        var btnRemove = new Button { Text = "Remove", Location = new Point(326, 252), Width = 124 };
        btnRemove.Click += (_, _) =>
        {
            if (_lstOptions.SelectedIndex >= 0)
            {
                _lstOptions.Items.RemoveAt(_lstOptions.SelectedIndex);
            }
        };

        var btnUp = new Button { Text = "Move Up", Location = new Point(326, 292), Width = 124 };
        btnUp.Click += (_, _) => MoveOption(-1);

        var btnDown = new Button { Text = "Move Down", Location = new Point(326, 328), Width = 124 };
        btnDown.Click += (_, _) => MoveOption(1);

        _lblOptionsHint.Location = new Point(16, 396);
        _lblOptionsHint.Size = new Size(434, 32);
        _lblOptionsHint.ForeColor = AppTheme.TextMuted;
        _lblOptionsHint.Font = AppTheme.SmallFont;

        var btnOk = new Button { Text = "OK", Location = new Point(272, 432), Width = 84 };
        btnOk.Click += BtnOk_Click;

        var btnCancel = new Button { Text = "Cancel", Location = new Point(364, 432), Width = 86, DialogResult = DialogResult.Cancel };

        Controls.AddRange(new Control[]
        {
            lblLabel, _txtLabel, lblKind, _cmbKind, lblUnit, _txtUnit, _chkOnDiagram,
            lblOptions, _lstOptions, btnAdd, btnEdit, btnRemove, btnUp, btnDown,
            _lblOptionsHint, btnOk, btnCancel,
        });

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        UpdateOptionsEnabled();
        AppTheme.ApplyRecursively(this);
    }

    private SetupFieldKind CurrentKind() =>
        (_cmbKind.SelectedItem as KindItem)?.Kind ?? SetupFieldKind.Choice;

    private void SelectKind(SetupFieldKind kind)
    {
        for (var i = 0; i < _cmbKind.Items.Count; i++)
        {
            if (_cmbKind.Items[i] is KindItem item && item.Kind == kind)
            {
                _cmbKind.SelectedIndex = i;
                return;
            }
        }
        _cmbKind.SelectedIndex = 0;
    }

    /// <summary>Options only apply to the two list-based kinds, so they're disabled rather than hidden for the others.</summary>
    private void UpdateOptionsEnabled()
    {
        var kind = CurrentKind();
        var usesOptions = kind is SetupFieldKind.Choice or SetupFieldKind.MultiChoice;

        _lstOptions.Enabled = usesOptions;
        _txtUnit.Enabled = kind == SetupFieldKind.Number;

        foreach (var control in Controls.OfType<Button>().Where(b => b.Text is "Add..." or "Rename..." or "Remove" or "Move Up" or "Move Down"))
        {
            control.Enabled = usesOptions;
        }

        _lblOptionsHint.Text = usesOptions
            ? "Editing an option does not change entries already saved - a value no longer in the list is kept and still shown."
            : "This field type doesn't use an option list.";
    }

    private void MoveOption(int direction)
    {
        var index = _lstOptions.SelectedIndex;
        var target = index + direction;
        if (index < 0 || target < 0 || target >= _lstOptions.Items.Count)
        {
            return;
        }

        var item = _lstOptions.Items[index];
        _lstOptions.Items.RemoveAt(index);
        _lstOptions.Items.Insert(target, item);
        _lstOptions.SelectedIndex = target;
    }

    private void BtnOk_Click(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_txtLabel.Text))
        {
            AppDialog.Show(this, "Enter a label for this field.", "Missing label", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        _field.Label = _txtLabel.Text.Trim();
        _field.Kind = CurrentKind();
        _field.Unit = _txtUnit.Text.Trim();
        _field.OnDiagram = _chkOnDiagram.Checked;
        _field.Options = _lstOptions.Items.Cast<object>().Select(o => o.ToString() ?? string.Empty).ToList();

        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>Small inline text prompt - not worth a dedicated form for a single line.</summary>
    private string? Prompt(string title, string prompt, string defaultValue)
    {
        using var dialog = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(400, 130),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            Font = Font,
            BackColor = AppTheme.Background,
        };

        var lbl = new Label { Text = prompt, Location = new Point(12, 16), AutoSize = true };
        var txt = new TextBox { Location = new Point(12, 40), Width = 372, Text = defaultValue };
        var ok = new Button { Text = "OK", Location = new Point(206, 82), Width = 84, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Location = new Point(298, 82), Width = 86, DialogResult = DialogResult.Cancel };

        dialog.Controls.AddRange(new Control[] { lbl, txt, ok, cancel });
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;

        AppTheme.ApplyRecursively(dialog);
        return dialog.ShowDialog(this) == DialogResult.OK ? txt.Text : null;
    }

    private sealed record KindItem(SetupFieldKind Kind, string Label)
    {
        public override string ToString() => Label;
    }
}
