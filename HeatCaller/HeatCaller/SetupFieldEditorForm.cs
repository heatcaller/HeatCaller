namespace HeatCaller;

/// <summary>
/// Edits one setup field: its label, how it's filled in, its unit, and
/// crucially its list of options.
///
/// The seeded Kyosho options are a starting point, not gospel - parts get
/// discontinued, new spring colours appear, and every club has its own
/// shorthand. Rather than shipping a fixed list that slowly goes stale,
/// every option is editable here.
///
/// On the layout pieces (DialogFit): the label, how it is filled in and the
/// unit lined up in a field grid, the diagram tick in the field column under
/// them; the options under their heading taking the height, with the buttons
/// that change them in a column beside them (Beside) - three that change the
/// list and two that reorder it, a section's gap between the groups - and the
/// note on them under the list; OK and Cancel at the foot. It used to be
/// placed in pixels in a 470x470 window: the captions in a fixed 94px, a list
/// of a fixed size with the buttons stepped down beside it at fixed places
/// and a fixed 124px wide, and the note two lines of a fixed height whatever
/// the text size.
/// </summary>
public sealed class SetupFieldEditorForm : Form
{
    /// <summary>The window's inside at the normal text size, as it was. It opens at least this big and can be made bigger, the options taking the height.</summary>
    private const int DialogWidth = 470;
    private const int DialogHeight = 470;

    /// <summary>The drop-down of kinds at the normal text size, as it was - wider when its longest kind needs it.</summary>
    private const int KindWidth = 340;

    /// <summary>The unit box at the normal text size, as it was.</summary>
    private const int UnitWidth = 120;

    /// <summary>The options show at least this many before the window scrolls instead - or as many as the button column beside them is tall, when that is more.</summary>
    private const int LeastRows = 4;

    /// <summary>What the note under the options says - see UpdateOptionsEnabled.</summary>
    private const string OptionsHint = "Editing an option does not change entries already saved - a value no longer in the list is kept and still shown.";
    private const string NoOptionsHint = "This field type doesn't use an option list.";

    private readonly SetupField _field;

    private readonly TextBox _txtLabel = new();
    private readonly ComboBox _cmbKind = new();
    private readonly TextBox _txtUnit = new();
    private readonly CheckBox _chkOnDiagram = new ThemedCheckBox();
    private readonly ListBox _lstOptions = new();

    /// <summary>The note under the options, as tall as the longer of its two wordings so the window does not change shape with the kind.</summary>
    private readonly SteadyLabel _lblOptionsHint = new();

    /// <summary>
    /// The buttons that change the option list, greyed with it - see
    /// UpdateOptionsEnabled. Held here rather than found by their captions:
    /// they sit in the options' column, not on the window, and lose their
    /// trailing "..." as they are sized (AppTheme.Caption).
    /// </summary>
    private readonly Button[] _optionButtons;

    public SetupFieldEditorForm(SetupField field)
    {
        _field = field;

        Text = "Edit Field";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        _txtLabel.Text = _field.Label;

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

        _txtUnit.Text = _field.Unit;
        _txtUnit.PlaceholderText = "mm, \u00B0, pcs";

        _chkOnDiagram.Text = "Show as a callout on the chassis diagram";
        _chkOnDiagram.AutoSize = true;
        _chkOnDiagram.Checked = _field.OnDiagram;

        var lblOptions = new Label { Text = "Options:", AutoSize = true, Font = AppTheme.BoldFont };

        _lstOptions.BackColor = AppTheme.Surface;

        // Given the height the window has, not snapped to whole rows - see FillPage.AddFill.
        _lstOptions.IntegralHeight = false;

        foreach (var option in _field.Options)
        {
            _lstOptions.Items.Add(option);
        }

        var btnAdd = new Button { Text = "Add..." };
        btnAdd.Click += (_, _) =>
        {
            var name = Prompt("Add option", "Option text:", string.Empty);
            if (!string.IsNullOrWhiteSpace(name))
            {
                _lstOptions.Items.Add(name.Trim());
                _lstOptions.SelectedIndex = _lstOptions.Items.Count - 1;
            }
        };

        var btnEdit = new Button { Text = "Rename..." };
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

        var btnRemove = new Button { Text = "Remove" };
        btnRemove.Click += (_, _) =>
        {
            if (_lstOptions.SelectedIndex >= 0)
            {
                _lstOptions.Items.RemoveAt(_lstOptions.SelectedIndex);
            }
        };

        var btnUp = new Button { Text = "Move Up" };
        btnUp.Click += (_, _) => MoveOption(-1);

        var btnDown = new Button { Text = "Move Down" };
        btnDown.Click += (_, _) => MoveOption(1);

        _optionButtons = new[] { btnAdd, btnEdit, btnRemove, btnUp, btnDown };

        _lblOptionsHint.AutoSize = true;
        _lblOptionsHint.ForeColor = AppTheme.TextMuted;
        _lblOptionsHint.Font = AppTheme.SmallFont;
        _lblOptionsHint.Wordings = new[] { OptionsHint, NoOptionsHint };

        var btnOk = new Button { Text = "OK" };
        btnOk.Click += BtnOk_Click;

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        // The label running to the right-hand edge, the kind and the unit
        // their own widths, and the tick in the field column under them, in
        // from the captions as it always was.
        var fields = new FieldGrid();
        fields.AddRow("Label:", _txtLabel, stretch: true);
        fields.AddRow("Filled in as:", _cmbKind);
        fields.AddRow("Unit:", _txtUnit);
        fields.AddField(_chkOnDiagram);

        // The buttons in one column down the right of the options, all as
        // wide as the widest caption needs, in their two groups as before: a
        // button's gap inside a group, a section's between them. The list
        // takes the width the column leaves and the height down to the note.
        var options = new Beside();
        options.AddFill(_lstOptions, _ => LeastRows * _lstOptions.ItemHeight + 4);
        options.AddSide(btnAdd, gapBelow: UiLayout.ButtonGap);
        options.AddSide(btnEdit, gapBelow: UiLayout.ButtonGap);
        options.AddSide(btnRemove, gapBelow: UiLayout.SectionGap);
        options.AddSide(btnUp, gapBelow: UiLayout.ButtonGap);
        options.AddSide(btnDown, gapBelow: 0);

        var page = new FillPage();
        page.Add(fields);
        page.Add(lblOptions, stretch: false, gapBelow: UiLayout.HintGap);
        page.AddFill(options, options.LeastHeight, gapBelow: UiLayout.HintGap);
        page.Add(_lblOptionsHint);

        // Against the right-hand edge, Cancel furthest right, as they were -
        // and Tab still reaches OK first: the bar takes them right to left,
        // in the order they were added.
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnOk), gapBelow: 0);
        btnOk.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        UpdateOptionsEnabled();
        AppTheme.ApplyRecursively(this);

        // After the theme pass, in the faces it leaves them in: the kinds as
        // wide as they were at the normal text size, at the size in use, or
        // as the longest of them needs; the unit box as wide as it was.
        (Font Font, int Count, int Width)? kindWidth = null;
        _cmbKind.Width = Math.Max(UiLayout.AtTextSize(KindWidth), Pieces.PickerWidth(_cmbKind, ref kindWidth));
        _txtUnit.Width = UiLayout.AtTextSize(UnitWidth);

        DialogFit.Keep(this, page, width: DialogWidth, height: DialogHeight);
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

        foreach (var control in _optionButtons)
        {
            control.Enabled = usesOptions;
        }

        _lblOptionsHint.Text = usesOptions ? OptionsHint : NoOptionsHint;
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

    /// <summary>
    /// Asks for one line of text over this window - an option's text, for
    /// Add... and Rename... - in the app's shared prompt (TextPromptForm).
    /// The answer as typed; null when cancelled.
    /// </summary>
    private string? Prompt(string title, string prompt, string defaultValue)
    {
        using var dialog = new TextPromptForm(title, prompt, defaultValue);

        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.Answer : null;
    }

    private sealed record KindItem(SetupFieldKind Kind, string Label)
    {
        public override string ToString() => Label;
    }
}
