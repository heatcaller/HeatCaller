namespace HeatCaller;

/// <summary>
/// Scrutineering: recording which cars have been checked and cleared.
///
/// Organised around today rather than all history, because "has this car
/// passed tech?" almost always means "today" - a car that passed last month
/// says nothing about its condition this morning. History is still reachable
/// but it isn't what the page leads with.
/// </summary>
public sealed class TechInspectionControl : UserControl
{
    private readonly TechInspectionStore _store;
    private readonly ProfileStore _profiles;
    private readonly RaceClassStore _classes;
    private readonly UndoService _undo;

    private readonly ComboBox _cmbClass = new();
    private readonly CheckBox _chkTodayOnly = new();
    private readonly DataGridView _grid = new();
    private readonly Label _lblSummary = new();
    private readonly Label _lblStatus = new();

    public TechInspectionControl(TechInspectionStore store, ProfileStore profiles, RaceClassStore classes, UndoService undo)
    {
        _store = store;
        _profiles = profiles;
        _classes = classes;
        _undo = undo;

        Dock = DockStyle.Fill;
        Font = AppTheme.BodyFont;
        ForeColor = AppTheme.TextDark;

        Size = new Size(980, 560);
        MinimumSize = new Size(900, 540);

        BuildLayout();
        RefreshAll();
    }

    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "The full scrutineering record - every inspection, with its car, its checks, who did it and their "
                 + "notes. The gate itself is on Who's Racing: the Tech column per driver, and a filter for the cars "
                 + "still to be looked at. Set each class's checklist on the Classes page. When a heat is staged, Race "
                 + "Control warns about any driver in it who hasn't passed today (a warning only; it can be switched "
                 + "off per class).",
            AutoSize = true,
            MaximumSize = new Size(868, 0),  // capped to the page's own minimum width, not a wider guess
            ForeColor = AppTheme.TextMuted,

            // Pinned rather than inherited: everything below is positioned
            // by measuring what is above it, and until a control is added
            // to this page it measures itself in the smaller WinForms
            // default font.
            Font = AppTheme.BodyFont,
        };

        // The columns were measured already; the ROWS were not. The intro
        // was allowed one line and takes two, so its second line came down
        // on the class filter underneath it.
        var y = UiLayout.PageTop;

        lblIntro.Location = new Point(UiLayout.PageLeft, y);

        y = UiLayout.Below(lblIntro, UiLayout.RowGap);

        var lblClass = new Label { Text = "Class:", AutoSize = true, Font = AppTheme.BodyFont };

        _cmbClass.Font = AppTheme.BodyFont;
        _cmbClass.Width = 220;
        _cmbClass.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbClass.SelectedIndexChanged += (_, _) => RefreshGrid();

        _chkTodayOnly.Text = "Today only";
        _chkTodayOnly.AutoSize = true;
        _chkTodayOnly.Font = AppTheme.BodyFont;
        _chkTodayOnly.Checked = true;
        _chkTodayOnly.CheckedChanged += (_, _) => RefreshGrid();

        _lblSummary.AutoSize = true;
        _lblSummary.Font = AppTheme.BoldFont;

        lblClass.Location = new Point(
            UiLayout.PageLeft, UiLayout.CentreOn(lblClass, y, _cmbClass.Height));

        _cmbClass.Location = new Point(UiLayout.After(lblClass), y);

        _chkTodayOnly.Location = new Point(
            UiLayout.After(_cmbClass) + UiLayout.RowGap,
            UiLayout.CentreOn(_chkTodayOnly, y, _cmbClass.Height));

        // Centred on the font, not on itself - RefreshGrid fills this in
        // later, so right now it is an empty label with nothing to measure.
        _lblSummary.Location = new Point(
            UiLayout.After(_chkTodayOnly) + UiLayout.RowGap,
            y + Math.Max(0, (_cmbClass.Height - AppTheme.BoldFont.Height) / 2));

        y = UiLayout.Below(_cmbClass, UiLayout.RowGap);

        _grid.Location = new Point(UiLayout.PageLeft, y);
        _grid.Size = new Size(Math.Max(600, Width - UiLayout.PageLeft - UiLayout.PageRight), 380);
        // All four sides now. The old warning against Bottom was written
        // when the buttons under the grid sat at fixed positions; they are
        // bottom-anchored themselves these days, so the grid growing down
        // fills exactly the gap that opens above them.
        _grid.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = true;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "when", HeaderText = "When", FillWeight = 55 },
            new DataGridViewTextBoxColumn { Name = "driver", HeaderText = "Driver", FillWeight = 75 },
            new DataGridViewTextBoxColumn { Name = "class", HeaderText = "Class", FillWeight = 60 },
            new DataGridViewTextBoxColumn { Name = "car", HeaderText = "Car", FillWeight = 70 },
            new DataGridViewTextBoxColumn { Name = "result", HeaderText = "Result", FillWeight = 55 },
            new DataGridViewTextBoxColumn { Name = "checks", HeaderText = "Checks", FillWeight = 45 },
            new DataGridViewTextBoxColumn { Name = "inspector", HeaderText = "Inspector", FillWeight = 60 },
            new DataGridViewTextBoxColumn { Name = "notes", HeaderText = "Notes", FillWeight = 90 }
        );
        _grid.CellDoubleClick += (_, args) =>
        {
            if (args.RowIndex >= 0)
            {
                AddOrEdit(Selected());
            }
        };

        var btnInspect = new Button { Text = "New Inspection...", Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        btnInspect.Click += (_, _) => AddOrEdit(null);

        var btnEdit = new Button { Text = "Edit...", Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        btnEdit.Click += (_, _) => AddOrEdit(Selected());

        // "Who's Outstanding?" used to sit here. It made you pick a class
        // first - "outstanding" only meant something within one - and then
        // printed a list of names into a message box, which you then had to
        // go somewhere else to act on.
        //
        // Who's Racing answers the same question better: tick "Only those not
        // through tech" and the roster narrows to exactly those cars, across
        // every class at once, with Inspect Selected Car beside each name. The
        // answer and the thing you do about it are in the same place.
        var btnDelete = new Button { Text = "Delete", Anchor = AnchorStyles.Bottom | AnchorStyles.Left };
        btnDelete.Click += (_, _) => DeleteSelected();

        AppTheme.LayoutButtonRow(
            UiLayout.PageLeft, UiLayout.Below(_grid, UiLayout.RowGap), UiLayout.ButtonGap,
            btnInspect, btnEdit, btnDelete);

        _lblStatus.Location = new Point(UiLayout.PageLeft, UiLayout.Below(btnInspect, UiLayout.RowGap));
        _lblStatus.AutoSize = true;
        _lblStatus.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
        _lblStatus.ForeColor = AppTheme.TextMuted;

        Controls.AddRange(new Control[]
        {
            lblIntro, lblClass, _cmbClass, _chkTodayOnly, _lblSummary, _grid,
            btnInspect, btnEdit, btnDelete, _lblStatus,
        });
    }

    private TechInspection? Selected() =>
        _grid.CurrentRow?.Tag is string id ? _store.Get(id) : null;

    private string? SelectedClass =>
        _cmbClass.SelectedItem as string is { } s && s != "(all classes)" ? s : null;

    public void RefreshAll()
    {
        RefreshClassCombo();
        RefreshGrid();
    }

    private void RefreshClassCombo()
    {
        var previous = _cmbClass.SelectedItem as string;

        _cmbClass.Items.Clear();
        _cmbClass.Items.Add("(all classes)");
        foreach (var raceClass in _classes.All)
        {
            _cmbClass.Items.Add(raceClass.Name);
        }

        var index = previous is null ? 0 : _cmbClass.Items.IndexOf(previous);
        _cmbClass.SelectedIndex = index >= 0 ? index : 0;
    }

    private void RefreshGrid()
    {
        _grid.Rows.Clear();

        var inspections = (_chkTodayOnly.Checked ? _store.ForDay(DateTime.Now) : _store.All)
            .Where(i => SelectedClass is null || string.Equals(i.ClassName, SelectedClass, StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var inspection in inspections)
        {
            var checks = inspection.ItemsFailed.Count > 0
                ? $"{inspection.ItemsPassed.Count} ok, {inspection.ItemsFailed.Count} fail"
                : $"{inspection.ItemsPassed.Count} ok";

            var rowIndex = _grid.Rows.Add(
                AppTime.TimeOfDay(inspection.InspectedAtUtc.ToLocalTime()),
                string.IsNullOrWhiteSpace(inspection.DriverName) ? inspection.Transponder : inspection.DriverName,
                string.IsNullOrWhiteSpace(inspection.ClassName) ? "-" : inspection.ClassName,
                string.IsNullOrWhiteSpace(inspection.CarModel) ? "-" : inspection.CarModel,
                inspection.ResultText,
                checks,
                string.IsNullOrWhiteSpace(inspection.Inspector) ? "-" : inspection.Inspector,
                inspection.Notes.Replace('\r', ' ').Replace('\n', ' '));

            _grid.Rows[rowIndex].Tag = inspection.Id;

            _grid.Rows[rowIndex].Cells[4].Style.ForeColor = inspection.Result switch
            {
                TechResult.Failed => AppTheme.Danger,
                TechResult.PassedWithNote => AppTheme.Accent,
                _ => AppTheme.Success,
            };
            _grid.Rows[rowIndex].Cells[4].Style.Font = AppTheme.BoldFont;
        }

        var failed = inspections.Count(i => !i.IsCleared);
        _lblSummary.Text = inspections.Count == 0
            ? "No inspections recorded."
            : failed == 0
                ? $"{inspections.Count} inspected, all cleared."
                : $"{inspections.Count} inspected, {failed} NOT cleared.";
        _lblSummary.ForeColor = failed == 0 ? AppTheme.TextMuted : AppTheme.Danger;
    }

    private void AddOrEdit(TechInspection? existing)
    {
        // Taken BEFORE the dialog opens. Taken after, it could already hold
        // the edit - the dialog used to write onto the stored inspection
        // directly - and then Undo put back the edited version and appeared
        // to do nothing. The dialog now edits a copy, but the snapshot
        // belongs before anything that could touch the store either way.
        var before = existing is null ? null : _store.CopyOf(existing.Id);

        using var form = new TechInspectionEditForm(existing, _profiles, _classes);
        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        // Just this inspection goes back (or, for a new one, goes away) -
        // not the whole log, which wiped out every inspection made since.
        // The store, not this page, in the undo: the page may be gone by
        // then, and re-reads the store when it is next shown.
        var store = _store;
        var id = form.Result.Id;
        var who = form.Result.DriverName;

        _undo.Capture(existing is null ? $"Record tech inspection for {who}" : $"Edit {who}'s tech inspection", () =>
        {
            if (!store.UndoUpsert(id, before))
            {
                throw new InvalidOperationException($"{who}'s inspection has been deleted since, so there is nothing to put back.");
            }
        });

        _store.Upsert(form.Result);
        RefreshGrid();
        _lblStatus.Text = $"{form.Result.DriverName}: {form.Result.ResultText}.";
    }

    private void DeleteSelected()
    {
        var removed = _grid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(r => r.Tag as string)
            .Where(id => id is not null)
            .Select(id => _store.Get(id!))
            .Where(i => i is not null)
            .Select(i => i!)
            .ToList();

        if (removed.Count == 0)
        {
            return;
        }

        var what = removed.Count == 1
            ? $"Delete {removed[0].DriverName}'s inspection?"
            : $"Delete these {removed.Count} inspections?";

        if (AppDialog.Show(this, what, "Delete inspection", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        var store = _store;
        var placed = removed.Select(i => (Inspection: i, Index: _store.IndexOf(i.Id))).ToList();

        _undo.Capture(removed.Count == 1 ? $"Delete {removed[0].DriverName}'s tech inspection" : $"Delete {removed.Count} tech inspections", () =>
        {
            store.RestoreDeleted(placed);
        });

        foreach (var inspection in removed)
        {
            _store.Delete(inspection.Id);
        }

        RefreshGrid();
    }

}
