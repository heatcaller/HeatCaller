namespace HeatCaller;

/// <summary>
/// Where chassis diagrams are configured: upload an image, place and label
/// the callout points, and list the tuning fields that don't belong on a
/// picture.
///
/// Separate from the setup entry form on purpose. Placing and naming points
/// is work you do once per chassis; filling in their values is work you do
/// every session. Mixing the two would mean re-doing the setup every time.
///
/// On the layout pieces (DialogFit): the name and the two buttons that set
/// the diagram's picture along the top (a field grid); under them the
/// diagram taking the room left, with a column down its right (Beside) of
/// two cards - the selected point, as tall as it needs, and the other
/// tuning fields, whose list takes the height the column has left - and
/// Save and Cancel at the foot. Each card's buttons are in a tight row, and
/// the column is never narrower than those rows need on one line. It used
/// to be placed in pixels: a 940x700 window, the diagram a 600x500 box
/// anchored to every edge, the two cards group boxes of fixed sizes in a
/// 300px column, and every button at a fixed place and width.
/// </summary>
public sealed class ChassisTemplateForm : Form
{
    /// <summary>How wide the window opens at the normal text size: the diagram about the 600px it had, and the column beside it.</summary>
    private const int DialogWidth = 1020;

    /// <summary>How tall it opens at the normal text size - the height it had. It can be made bigger.</summary>
    private const int DialogHeight = 700;

    /// <summary>
    /// The column beside the diagram at the normal text size, or wider when
    /// the cards' buttons need more (SideWidth). It was 300px; the buttons
    /// in each card are wider than they were, and at 300 they went onto two
    /// lines each, which made the column too tall for a 1280x720 screen.
    /// </summary>
    private const int SideLeastWidth = 380;

    /// <summary>The diagram never shorter than this at the normal text size - below it, the window scrolls instead.</summary>
    private const int CanvasLeastHeight = 300;

    /// <summary>The name box's width at the normal text size, as it was made.</summary>
    private const int NameWidth = 260;

    /// <summary>The other tuning fields show at least this many before the window scrolls instead.</summary>
    private const int ExtrasLeastRows = 3;

    /// <summary>What the selected point's card says when no point is selected - the longest thing it says, so the card is made that tall.</summary>
    private const string NothingSelectedHint =
        "Nothing selected. Click a dot on the diagram, or double-click empty space to add one.";

    private readonly ChassisTemplateStore _store;
    private readonly ChassisTemplate _template;

    private readonly ChassisCanvas _canvas = new();
    private readonly TextBox _txtName = new();
    private readonly TextBox _txtPointLabel = new();
    private readonly ListBox _lstExtras = new();
    private readonly SteadyLabel _lblSelected = new();

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
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        var page = BuildLayout();
        AppTheme.ApplyRecursively(this);

        _canvas.SetTemplate(_template, _store.ResolveImagePath(_template));

        // The size it had at the normal text size, a little wider for the
        // column beside the diagram. It can be made bigger - and the diagram
        // and the list of other fields take what it is made.
        DialogFit.Keep(this, page, width: DialogWidth, height: DialogHeight);
    }

    /// <summary>The edited template, valid after the dialog returns OK.</summary>
    public ChassisTemplate Result => _template;

    /// <summary>Builds the window's content, as the class notes describe, and returns its page.</summary>
    private FillPage BuildLayout()
    {
        _txtName.Width = UiLayout.AtTextSize(NameWidth);
        _txtName.Text = _template.Name;

        var btnImage = new Button { Text = "Set Diagram Image..." };
        btnImage.Click += (_, _) => ChooseImage();

        var btnClearImage = new Button { Text = "Use Built-in Outline" };
        btnClearImage.Click += (_, _) =>
        {
            _store.ClearImage(_template);
            _canvas.SetTemplate(_template, null);

            // SetTemplate lets go of the selected point without saying so
            // (it raises no SelectionChanged), so the card is brought back in
            // step here - see SyncSelection. Left alone, it went on showing
            // the old point with its Label box still enabled, typing there
            // reached nothing, and Edit Field... said nothing was selected.
            SyncSelection();
        };

        // --- Canvas ---
        _canvas.EditMode = true;
        _canvas.SelectionChanged += SyncSelection;
        _canvas.PointMoved += SyncSelection;
        _canvas.PointActivated += _ => _txtPointLabel.Focus();

        // --- Point editing ---
        // As tall as the longest thing it says, so selecting a point and
        // letting it go never moves the label box and the buttons under it.
        _lblSelected.ForeColor = AppTheme.TextMuted;
        _lblSelected.Text = "Nothing selected.";
        _lblSelected.Wordings = new[] { NothingSelectedHint, "Position: 0.00, 0.00" };

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

        var btnDeletePoint = new Button { Text = "Delete Point" };
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

        var btnEditPointField = new Button { Text = "Edit Field..." };
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
                // The field editor changes the field on this window's working
                // copy, and that is all. It used to write the copy to the
                // store here, which saved every other unsaved change with it
                // (dragged and deleted points, renamed extras) and left the
                // store holding the very object this window goes on editing
                // - so Cancel no longer threw anything away. The copy is
                // written by Save alone.

                // The editor can take the field off the diagram, which makes
                // it an extra field. The extras list is rebuilt so it shows
                // what its buttons act on: left as it was, every row past the
                // moved field pointed at the field after it, and Remove,
                // Rename and Edit Field... there hit the wrong one.
                RefreshExtras();
                SyncSelection();
                _canvas.Invalidate();
            }
        };

        var btnAddPoint = new Button { Text = "Add Point" };
        btnAddPoint.Click += (_, _) =>
        {
            // Lands in the middle; the user drags it where it belongs.
            DiagramSection().Fields.Add(new SetupField { Label = "New point", OnDiagram = true });
            _canvas.SetTemplate(_template, _store.ResolveImagePath(_template));

            // The selection went with SetTemplate - as Use Built-in Outline above.
            SyncSelection();
        };

        var pointFields = new FieldGrid();
        pointFields.AddRow("Label:", _txtPointLabel, stretch: true);

        // Only as wide as their captions, so the three stay on one line in
        // the column - see SideWidth.
        var pointButtons = new ButtonBar(tight: true).With(btnDeletePoint, btnAddPoint, btnEditPointField);

        var grpPoint = new Section("Selected point");
        grpPoint.Add(_lblSelected);
        grpPoint.Add(pointFields);
        grpPoint.Add(pointButtons, gapBelow: 0);

        // --- Extra fields ---
        var lblExtrasHint = new Label
        {
            Text = "Things with no sensible place on a picture - motor, gearing, battery. These appear as a list under the diagram.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        // Given the height the card has, not snapped to whole rows - see Section.AddFill.
        _lstExtras.IntegralHeight = false;
        RefreshExtras();

        var btnAddExtra = new Button { Text = "Add..." };
        btnAddExtra.Click += (_, _) =>
        {
            var name = PromptForText("Add tuning field", "Field name:", string.Empty);
            if (!string.IsNullOrWhiteSpace(name))
            {
                ExtraSection().Fields.Add(new SetupField { Label = name.Trim(), Kind = SetupFieldKind.Text });
                RefreshExtras();
            }
        };

        var btnRenameExtra = new Button { Text = "Rename..." };
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

        var btnEditExtra = new Button { Text = "Edit Field..." };
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
                // Only the working copy changes; Save writes it, Cancel
                // drops it - as with Edit Field... on a point above.
                RefreshExtras();
                _canvas.Invalidate();
            }
        };

        var btnRemoveExtra = new Button { Text = "Remove" };
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

        // Add..., Rename... and Remove on one line, as they were, and Edit
        // Field... after them - it used to be a line of its own under them.
        var extraButtons = new ButtonBar(tight: true).With(btnAddExtra, btnRenameExtra, btnRemoveExtra, btnEditExtra);

        var grpExtras = new Section("Other tuning fields (not on the diagram)");
        grpExtras.Add(lblExtrasHint);
        grpExtras.AddFill(_lstExtras, () => ExtrasLeastRows * _lstExtras.ItemHeight + 4);
        grpExtras.Add(extraButtons, gapBelow: 0);

        var btnSave = new Button { Text = "Save" };
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

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        // The name, and the picture's two buttons beside it - or under it,
        // when the line is too narrow for all three.
        var nameRow = new FieldGrid();
        nameRow.AddRow("Chassis name:", FieldGrid.Inline(_txtName, btnImage, btnClearImage));

        // The diagram in the width the column leaves, the two cards in the
        // column - the point's as tall as it needs, the other fields' taking
        // the rest of the column's height.
        var band = new Beside(() => SideWidth(pointButtons, extraButtons, grpExtras));
        band.AddFill(_canvas, _ => UiLayout.AtTextSize(CanvasLeastHeight));
        band.AddSideCard(grpPoint);
        band.AddSideFill(grpExtras, grpExtras.LeastHeight, gapBelow: 0);

        var page = new FillPage();
        page.Add(nameRow);
        page.AddFill(band, band.LeastHeight);

        // Against the right-hand edge, Cancel furthest right, as they were -
        // and Tab still reaches Save first: the bar takes them right to
        // left, in the order they were added.
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnSave), gapBelow: 0);
        btnSave.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = btnSave;
        CancelButton = btnCancel;

        return page;
    }

    /// <summary>
    /// The column beside the diagram: as wide as it was made, at the text
    /// size in use - or as wide as either card's row of buttons needs on one
    /// line, or the other fields' card's caption, with the card's inset
    /// either side, when that is more. Asked whenever the column is placed,
    /// so it follows the buttons' face.
    /// </summary>
    private static int SideWidth(ButtonBar pointButtons, ButtonBar extraButtons, Section extras) =>
        Math.Max(
            UiLayout.AtTextSize(SideLeastWidth),
            Math.Max(Math.Max(pointButtons.OneLineWidth, extraButtons.OneLineWidth), extras.CaptionWidth) + Section.Inset.Horizontal);


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
            _lblSelected.Text = NothingSelectedHint;
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
            // same file name as the last one used to be copied in under the
            // same stored name, and the canvas only reloads when the path
            // changes - so it went on showing the old picture until the
            // editor was closed and opened again. SetImage now always picks
            // a name nothing uses yet, but forgetting first costs nothing
            // and keeps this right whatever name it hands back.
            _canvas.SetTemplate(_template, null);
            _canvas.SetTemplate(_template, _store.ResolveImagePath(_template));

            // The selection went with SetTemplate - as Use Built-in Outline.
            SyncSelection();
        }
        else
        {
            AppDialog.Show(this, "Couldn't copy that image in.", "Image not set", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    /// <summary>Asks for one line of text over this window (TextPromptForm). Null when cancelled; the answer as typed otherwise.</summary>
    private string? PromptForText(string title, string prompt, string defaultValue)
    {
        using var dialog = new TextPromptForm(title, prompt, defaultValue);

        return dialog.ShowDialog(this) == DialogResult.OK ? dialog.Answer : null;
    }
}

/// <summary>
/// The chassis setup sheets: every sheet the club has, and Edit..., New...,
/// Duplicate and Delete for them - what Chassis Sheets... opens on the
/// Garage page (GarageControl) and on the setup log (SetupLogControl). Each
/// of the two used to build this window inside the method that showed it,
/// line for line the same; it is one class now, so the two cannot drift
/// apart, and internal so the layout test can build it (WindowCatalog).
/// Whoever opens it refreshes what it shows once it closes.
///
/// The undo here has two awkward corners - the editor saves as it goes, so
/// the entry is captured afterwards by comparing JSON, and deleting the
/// LAST sheet seeds a replacement that the undo then has to take away
/// again. Both were got right once, and the code for them is the code the
/// two windows had, moved here unchanged.
///
/// On the layout pieces (DialogFit): the sheets taking the height, their
/// four buttons in a column beside them (Beside), and Close at the foot
/// against the right-hand edge. It used to be built in pixels in a 460x340
/// window: a 300x280 list, the buttons a fixed 120px wide at fixed places
/// beside it, and Close at the bottom of that column.
/// </summary>
internal sealed class ChassisSheetsForm : Form
{
    /// <summary>The sheets show at least this many before the window scrolls instead - or as many as the button column beside them is tall, when that is more.</summary>
    private const int LeastRows = 4;

    private readonly ChassisTemplateStore _templates;
    private readonly SetupLogStore _setups;
    private readonly UndoService _undo;
    private readonly ListBox _list = new();

    /// <param name="templateStore">The sheets, edited in place.</param>
    /// <param name="setupLog">The setup log - asked, when a delete is undone, whether a setup has been saved against the sheet the delete seeded.</param>
    /// <param name="undo">Where each edit and delete is captured.</param>
    public ChassisSheetsForm(ChassisTemplateStore templateStore, SetupLogStore setupLog, UndoService undo)
    {
        // Named apart from the locals the undo captures below (templates,
        // entries), which are the code the two windows had, unchanged.
        _templates = templateStore;
        _setups = setupLog;
        _undo = undo;

        Text = "Chassis Setup Sheets";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        // Given the height the window has, not snapped to whole rows - see Beside.AddFill.
        _list.IntegralHeight = false;

        var btnEdit = new Button { Text = "Edit..." };
        btnEdit.Click += (_, _) =>
        {
            if (Selected() is not { } t) { return; }

            // The undo is captured once the editor closes, if the stored
            // sheet actually changed - whichever button closed it. The
            // editor works on a copy and writes it only on Save, so after
            // Cancel the comparison finds nothing and no undo is recorded;
            // comparing rather than trusting the button keeps that true
            // however the editor came to write.
            var before = ChassisTemplateStore.CopyOf(t);
            var beforeJson = System.Text.Json.JsonSerializer.Serialize(before);

            using var editor = new ChassisTemplateForm(_templates, t);
            editor.ShowDialog(this);

            if (_templates.Get(before.Id) is { } after
                && System.Text.Json.JsonSerializer.Serialize(after) != beforeJson)
            {
                var templates = _templates;

                _undo.Capture($"Edit chassis sheet '{before.Name}'", () =>
                {
                    if (!templates.PutBack(before))
                    {
                        throw new InvalidOperationException($"the chassis sheet '{before.Name}' has been deleted since, so there is nothing to put back.");
                    }
                });
            }

            Reload();
        };

        var btnNew = new Button { Text = "New..." };
        btnNew.Click += (_, _) =>
        {
            var created = ChassisTemplate.CreateKyoshoDefault();
            created.Name = "New chassis sheet";

            // Not stored until the editor's Save stores it. It used to be
            // written to the store before the editor opened, so Cancel - which
            // only throws away the editor's own copy - left a "New chassis
            // sheet" behind in the list and in every Setup Sheet's drop-down.
            // The editor works on a copy with this sheet's id and Upserts it
            // on Save, so this sheet is in the store exactly when Save was
            // pressed.
            using var editor = new ChassisTemplateForm(_templates, created);
            editor.ShowDialog(this);

            if (_templates.Get(created.Id) is { } added)
            {
                CaptureAddUndo($"New chassis sheet '{added.Name}'", added.Id, added.Name);
            }

            Reload();
        };

        var btnDuplicate = new Button { Text = "Duplicate" };
        btnDuplicate.Click += (_, _) =>
        {
            if (Selected() is not { } t) { return; }

            var copy = t.Clone();
            _templates.Upsert(copy);

            // Undoable like Edit and Delete - it used to record nothing, so
            // Ctrl+Z after a Duplicate reversed whatever came before it.
            CaptureAddUndo($"Duplicate chassis sheet '{t.Name}'", copy.Id, copy.Name);

            Reload();
        };

        var btnDelete = new Button { Text = "Delete" };
        btnDelete.Click += (_, _) =>
        {
            if (Selected() is not { } t) { return; }

            if (AppDialog.Show(this,
                    $"Delete the '{t.Name}' sheet?\r\n\r\nSetup entries that used it are kept, but will show as legacy entries. This can be reversed with Undo.",
                    "Confirm delete", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                // The template carries every section, field and option, so
                // the whole object goes into the undo capture - back in its
                // old place. The stores, not this window, in the undo: the
                // window is long gone by then.
                var templates = _templates;
                var entries = _setups;
                var index = _templates.IndexOf(t.Id);
                var wasLast = _templates.All.Count == 1;
                string? seededId = null;

                _undo.Capture($"Delete chassis sheet '{t.Name}'", () =>
                {
                    templates.RestoreDeleted(t, index, seededId, seededId is not null && entries.UsesTemplate(seededId));
                });

                _templates.Delete(t.Id);

                // Deleting the last sheet seeds a default in its place; the
                // undo takes that one away again.
                if (wasLast && _templates.All.Count == 1)
                {
                    seededId = _templates.All[0].Id;
                }

                Reload();
            }
        };

        var btnClose = new Button { Text = "Close", DialogResult = DialogResult.OK };

        // The four buttons in one column down the right of the list, all as
        // wide as the widest caption needs, in the order they were; the list
        // takes the width the column leaves and the height down to Close.
        var sheets = new Beside();
        sheets.AddFill(_list, _ => LeastRows * _list.ItemHeight + 4);
        sheets.AddSide(btnEdit, gapBelow: UiLayout.ButtonGap);
        sheets.AddSide(btnNew, gapBelow: UiLayout.ButtonGap);
        sheets.AddSide(btnDuplicate, gapBelow: UiLayout.ButtonGap);
        sheets.AddSide(btnDelete, gapBelow: 0);

        var page = new FillPage();
        page.AddFill(sheets, sheets.LeastHeight);

        // Close under the column, against the right-hand edge - it used to
        // be the column's last button, level with the bottom of the list.
        page.Add(new ButtonBar(alignRight: true).With(btnClose), gapBelow: 0);

        Controls.Add(page);

        AcceptButton = btnClose;

        Reload();
        AppTheme.ApplyRecursively(this);

        // Wider than the 460 it was made at: the buttons are as wide as a
        // standard button now, 148px rather than 120, and this keeps the
        // list the 300px it had. The height it had. It can be made bigger,
        // for a club with many sheets.
        DialogFit.Keep(this, page, width: 490, height: 340);
    }

    /// <summary>
    /// The undo for a sheet New... or Duplicate added: the sheet comes out
    /// again. Not when a setup entry has been saved against it since - taking
    /// it away then would turn that entry into a legacy one, which is more
    /// than undoing an add should do - and not when it has been deleted
    /// already. Either way the undo says so rather than quietly doing
    /// nothing. The stores, not this window, in the undo: the window is long
    /// gone by then.
    /// </summary>
    private void CaptureAddUndo(string description, string id, string name)
    {
        var templates = _templates;
        var entries = _setups;

        _undo.Capture(description, () =>
        {
            if (templates.IndexOf(id) < 0)
            {
                throw new InvalidOperationException($"the chassis sheet '{name}' has been deleted since, so there is nothing to take away.");
            }

            if (entries.UsesTemplate(id))
            {
                throw new InvalidOperationException($"a setup has been saved on the chassis sheet '{name}' since, so it is kept. Delete it from Chassis Sheets if it is not wanted.");
            }

            templates.Delete(id);
        });
    }

    /// <summary>Fills the list from the store, and selects the first sheet when there is one.</summary>
    private void Reload()
    {
        _list.Items.Clear();

        foreach (var t in _templates.All)
        {
            _list.Items.Add(t.Name);
        }

        if (_list.Items.Count > 0)
        {
            _list.SelectedIndex = 0;
        }
    }

    /// <summary>The sheet selected in the list, or null.</summary>
    private ChassisTemplate? Selected() =>
        _list.SelectedIndex >= 0 ? _templates.All.ElementAtOrDefault(_list.SelectedIndex) : null;
}
