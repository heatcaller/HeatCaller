namespace HeatCaller;

/// <summary>
/// Where chassis diagrams are configured: upload an image, place and label
/// the callout points, and list the tuning fields that don't belong on a
/// picture.
///
/// Separate from the setup entry form on purpose. Placing and naming points
/// is work you do once per chassis; filling in their values is work you do
/// every session. Mixing the two would mean re-doing the setup every time.
/// </summary>
public sealed class ChassisTemplateForm : Form
{
    private readonly ChassisTemplateStore _store;
    private readonly ChassisTemplate _template;

    private readonly ChassisCanvas _canvas = new();
    private readonly TextBox _txtName = new();
    private readonly TextBox _txtPointLabel = new();
    private readonly ListBox _lstExtras = new();
    private readonly Label _lblSelected = new();

    public ChassisTemplateForm(ChassisTemplateStore store, ChassisTemplate template)
    {
        _store = store;

        // Work on a copy so Cancel genuinely discards - the canvas mutates
        // points in place as they're dragged, which would otherwise be
        // impossible to undo.
        _template = template.Clone();
        _template.Id = template.Id;
        _template.Name = template.Name;

        Text = "Chassis Diagram";
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(940, 700);
        MinimumSize = new Size(860, 560);
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        BuildLayout();
        AppTheme.ApplyRecursively(this);

        _canvas.SetTemplate(_template, _store.ResolveImagePath(_template));
    }

    /// <summary>The edited template, valid after the dialog returns OK.</summary>
    public ChassisTemplate Result => _template;

    private void BuildLayout()
    {
        var lblName = new Label { Text = "Chassis name:", Location = new Point(12, 16), AutoSize = true };
        _txtName.Location = new Point(110, 13);
        _txtName.Width = 260;
        _txtName.Text = _template.Name;

        var btnImage = new Button { Text = "Set Diagram Image...", Location = new Point(384, 12), Width = 170 };
        btnImage.Click += (_, _) => ChooseImage();

        var btnClearImage = new Button { Text = "Use Built-in Outline", Location = new Point(562, 12), Width = 170 };
        btnClearImage.Click += (_, _) =>
        {
            _store.ClearImage(_template);
            _canvas.SetTemplate(_template, null);
        };

        // --- Canvas ---
        _canvas.Location = new Point(12, 56);
        _canvas.Size = new Size(600, 500);
        _canvas.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        _canvas.EditMode = true;
        _canvas.SelectionChanged += SyncSelection;
        _canvas.PointMoved += SyncSelection;
        _canvas.PointActivated += _ => _txtPointLabel.Focus();

        // --- Point editing ---
        var grpPoint = new GroupBox
        {
            Text = "Selected point",
            Location = new Point(624, 56),
            Size = new Size(300, 190),
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
        };

        _lblSelected.Location = new Point(12, 24);
        _lblSelected.AutoSize = true;
        _lblSelected.MaximumSize = new Size(276, 0);
        _lblSelected.ForeColor = AppTheme.TextMuted;
        _lblSelected.Text = "Nothing selected.";

        var lblPointLabel = new Label { Text = "Label:", Location = new Point(12, 58), AutoSize = true };
        _txtPointLabel.Location = new Point(12, 78);
        _txtPointLabel.Width = 276;
        _txtPointLabel.Enabled = false;
        _txtPointLabel.TextChanged += (_, _) =>
        {
            var point = SelectedPoint();
            if (point is not null)
            {
                point.Label = _txtPointLabel.Text;
                _canvas.Invalidate();
            }
        };

        var btnDeletePoint = new Button { Text = "Delete Point", Location = new Point(12, 110), Width = 130 };
        btnDeletePoint.Click += (_, _) =>
        {
            var point = SelectedPoint();
            if (point is not null)
            {
                foreach (var section in _template.Sections) { section.Fields.Remove(point); }
                _canvas.SetTemplate(_template, _store.ResolveImagePath(_template));
                SyncSelection();
            }
        };

        var btnEditPointField = new Button { Text = "Edit Field...", Location = new Point(12, 146), Width = 268 };
        btnEditPointField.Click += (_, _) =>
        {
            if (SelectedPoint() is not { } field)
            {
                AppDialog.Show(this, "Select a callout on the diagram first.", "Nothing selected",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var editor = new SetupFieldEditorForm(field);
            if (editor.ShowDialog(this) == DialogResult.OK)
            {
                _store.Upsert(_template);
                SyncSelection();
                _canvas.Invalidate();
            }
        };

        var btnAddPoint = new Button { Text = "Add Point", Location = new Point(150, 110), Width = 130 };
        btnAddPoint.Click += (_, _) =>
        {
            // Lands in the middle; the user drags it where it belongs.
            DiagramSection().Fields.Add(new SetupField { Label = "New point", OnDiagram = true });
            _canvas.SetTemplate(_template, _store.ResolveImagePath(_template));
        };

        grpPoint.Controls.AddRange(new Control[] { _lblSelected, lblPointLabel, _txtPointLabel, btnDeletePoint, btnAddPoint, btnEditPointField });

        // --- Extra fields ---
        var grpExtras = new GroupBox
        {
            Text = "Other tuning fields (not on the diagram)",
            Location = new Point(624, 256),
            Size = new Size(300, 356),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Right,
        };

        var lblExtrasHint = new Label
        {
            Text = "Things with no sensible place on a picture - motor, gearing, battery. These appear as a list under the diagram.",
            Location = new Point(12, 22),
            AutoSize = true,
            MaximumSize = new Size(276, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        _lstExtras.Location = new Point(12, 74);
        _lstExtras.Size = new Size(276, 190);
        _lstExtras.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        RefreshExtras();

        var btnAddExtra = new Button { Text = "Add...", Location = new Point(12, 276), Width = 88, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        btnAddExtra.Click += (_, _) =>
        {
            var name = PromptForText("Add tuning field", "Field name:", string.Empty);
            if (!string.IsNullOrWhiteSpace(name))
            {
                ExtraSection().Fields.Add(new SetupField { Label = name.Trim(), Kind = SetupFieldKind.Text });
                RefreshExtras();
            }
        };

        var btnRenameExtra = new Button { Text = "Rename...", Location = new Point(106, 276), Width = 88, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        btnRenameExtra.Click += (_, _) =>
        {
            var index = _lstExtras.SelectedIndex;
            if (index < 0)
            {
                return;
            }

            var target = ExtraFields.ElementAtOrDefault(index);
            if (target is null) { return; }
            var name = PromptForText("Rename field", "Field name:", target.Label);
            if (!string.IsNullOrWhiteSpace(name))
            {
                target.Label = name.Trim();
                RefreshExtras();
            }
        };

        var btnEditExtra = new Button { Text = "Edit Field...", Location = new Point(12, 312), Width = 268, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        btnEditExtra.Click += (_, _) =>
        {
            var target = ExtraFields.ElementAtOrDefault(_lstExtras.SelectedIndex);
            if (target is null)
            {
                AppDialog.Show(this, "Select a field first.", "Nothing selected",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using var editor = new SetupFieldEditorForm(target);
            if (editor.ShowDialog(this) == DialogResult.OK)
            {
                _store.Upsert(_template);
                RefreshExtras();
                _canvas.Invalidate();
            }
        };

        var btnRemoveExtra = new Button { Text = "Remove", Location = new Point(200, 276), Width = 88, Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        btnRemoveExtra.Click += (_, _) =>
        {
            var index = _lstExtras.SelectedIndex;
            if (index >= 0)
            {
                var doomed = ExtraFields.ElementAtOrDefault(index);
                if (doomed is not null) { foreach (var section in _template.Sections) { section.Fields.Remove(doomed); } }
                RefreshExtras();
            }
        };

        grpExtras.Controls.AddRange(new Control[] { lblExtrasHint, _lstExtras, btnAddExtra, btnRenameExtra, btnRemoveExtra, btnEditExtra });

        var btnSave = new Button { Text = "Save", Location = new Point(742, 638), Width = 88, Anchor = AnchorStyles.Bottom | AnchorStyles.Right };
        btnSave.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_txtName.Text))
            {
                AppDialog.Show(this, "Give the chassis a name.", "Missing name", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _template.Name = _txtName.Text.Trim();
            _store.Upsert(_template);
            DialogResult = DialogResult.OK;
            Close();
        };

        var btnCancel = new Button { Text = "Cancel", Location = new Point(838, 638), Width = 88, Anchor = AnchorStyles.Bottom | AnchorStyles.Right, DialogResult = DialogResult.Cancel };

        Controls.AddRange(new Control[]
        {
            lblName, _txtName, btnImage, btnClearImage, _canvas, grpPoint, grpExtras, btnSave, btnCancel,
        });

        AcceptButton = btnSave;
        CancelButton = btnCancel;
    }


    /// <summary>
    /// The section diagram callouts live in. The new model groups fields
    /// into sections rather than keeping a flat point list, so this editor
    /// keeps its pinned points together in one section, created on demand.
    /// </summary>
    private SetupSection DiagramSection()
    {
        var existing = _template.Sections.FirstOrDefault(s => s.Name == "Diagram callouts");
        if (existing is not null)
        {
            return existing;
        }

        var section = new SetupSection { Name = "Diagram callouts" };
        _template.Sections.Insert(0, section);
        return section;
    }

    /// <summary>The section for tuning items that aren't pinned to the picture.</summary>
    private SetupSection ExtraSection()
    {
        var existing = _template.Sections.FirstOrDefault(s => s.Name == "Other tuning");
        if (existing is not null)
        {
            return existing;
        }

        var section = new SetupSection { Name = "Other tuning" };
        _template.Sections.Add(section);
        return section;
    }

    private List<SetupField> DiagramPoints => _template.DiagramFields.ToList();
    private List<SetupField> ExtraFields => _template.AllFields.Where(f => !f.OnDiagram).ToList();

    private SetupField? SelectedPoint() =>
        _canvas.SelectedPointId is { } id ? _template.FindField(id) : null;

    private void SyncSelection()
    {
        var point = SelectedPoint();

        if (point is null)
        {
            _lblSelected.Text = "Nothing selected. Click a dot on the diagram, or double-click empty space to add one.";
            _txtPointLabel.Enabled = false;
            _txtPointLabel.Text = string.Empty;
            return;
        }

        _lblSelected.Text = $"Position: {point.X:0.00}, {point.Y:0.00}";
        _txtPointLabel.Enabled = true;

        if (_txtPointLabel.Text != point.Label)
        {
            _txtPointLabel.Text = point.Label;
        }
    }

    private void RefreshExtras()
    {
        var selected = _lstExtras.SelectedIndex;
        _lstExtras.Items.Clear();
        foreach (var label in ExtraFields.Select(f => f.Label))
        {
            _lstExtras.Items.Add(label);
        }
        if (selected >= 0 && selected < _lstExtras.Items.Count)
        {
            _lstExtras.SelectedIndex = selected;
        }
    }

    private void ChooseImage()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose a chassis diagram or photo",
            Filter = "Images (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files (*.*)|*.*",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        // Sized up from its length and its header before it is copied in
        // (see ImageProbe). Any file used to be copied in unlooked-at, and a
        // diagram is decoded whole as this editor and the setup form open -
        // so a huge photo, or a small file whose header claims tens of
        // thousands of pixels each way, froze them while GDI+ built the
        // whole bitmap, or crashed them out of memory.
        string? problem;

        try
        {
            problem = ImageProbe.PickedProblem(dialog.FileName, "use as a chassis diagram", "Choose a smaller copy.", out _);
        }
        catch
        {
            // Gone or locked since it was picked: the same answer a copy
            // that failed has always had.
            AppDialog.Show(this, "Couldn't copy that image in.", "Image not set", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        if (problem is not null)
        {
            AppDialog.Show(this, problem, "Image not set", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_store.SetImage(_template, dialog.FileName))
        {
            // Told to forget the old picture first. A new picture with the
            // same file name as the last one is copied in under the same
            // stored name, and the canvas only reloads when the path
            // changes - so it went on showing the old picture until the
            // editor was closed and opened again.
            _canvas.SetTemplate(_template, null);
            _canvas.SetTemplate(_template, _store.ResolveImagePath(_template));
        }
        else
        {
            AppDialog.Show(this, "Couldn't copy that image in.", "Image not set", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private string? PromptForText(string title, string prompt, string defaultValue)
    {
        using var dialog = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(420, 176),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            Font = Font,
            BackColor = AppTheme.Background,
        };

        var lbl = new Label { Text = prompt, Location = new Point(12, 16), AutoSize = true };
        var txt = new TextBox { Location = new Point(12, 40), Width = 380, Text = defaultValue };
        var ok = new Button { Text = "OK", Location = new Point(228, 78), Width = 80, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Location = new Point(312, 78), Width = 80, DialogResult = DialogResult.Cancel };

        dialog.Controls.AddRange(new Control[] { lbl, txt, ok, cancel });
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;
        AppTheme.ApplyRecursively(dialog);

        return dialog.ShowDialog(this) == DialogResult.OK ? txt.Text : null;
    }
}
