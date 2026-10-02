namespace HeatCaller;

/// <summary>
/// A tuning log: what the car was set up like, and how it went. The app
/// tracks lap performance thoroughly but until now nothing about the car
/// that produced it, so "what did I have in it the day it was quick" had
/// no answer. Linking an entry to a session pulls that session's best lap
/// in beside the setup, which is what makes the log worth keeping.
/// </summary>
public sealed class SetupLogControl : UserControl
{
    private readonly SetupLogStore _store;
    private readonly SessionHistoryStore _history;
    private readonly ProfileStore _profiles;
    private readonly TrackLayoutStore _trackLayouts;
    private readonly UndoService _undo;
    private readonly ChassisTemplateStore _chassisTemplates;

    private readonly ComboBox _cmbDriverFilter = new();
    private readonly DataGridView _grid = new();
    private readonly Label _lblSummary = new();

    public SetupLogControl(SetupLogStore store, SessionHistoryStore history, ProfileStore profiles, TrackLayoutStore trackLayouts, UndoService undo, ChassisTemplateStore chassisTemplates)
    {
        _store = store;
        _history = history;
        _profiles = profiles;
        _trackLayouts = trackLayouts;
        _undo = undo;
        _chassisTemplates = chassisTemplates;

        Dock = DockStyle.Fill;
        MinimumSize = new Size(920, 560);
        Font = AppTheme.BodyFont;

        BuildLayout();
        RefreshAll();
    }

    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "Record what the car had in it, and link it to the session it ran in - the session's best lap then shows " +
                   "right beside the setup, so you can see which changes actually made it quicker.",
            Location = new Point(12, 12),
            AutoSize = true,
            MaximumSize = new Size(880, 0),
            ForeColor = AppTheme.TextMuted,
        };

        var lblFilter = new Label { Text = "Driver:", Location = new Point(12, 62), AutoSize = true };
        _cmbDriverFilter.Location = new Point(70, 59);
        _cmbDriverFilter.Width = 220;
        _cmbDriverFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbDriverFilter.SelectedIndexChanged += (_, _) => RefreshGrid();

        _lblSummary.Location = new Point(310, 62);
        _lblSummary.AutoSize = true;
        _lblSummary.ForeColor = AppTheme.TextMuted;

        _grid.Location = new Point(12, 94);
        _grid.Size = new Size(880, 366);
        // Top/Left/Right only - Bottom anchoring stretched the grid over
        // the action buttons below it.
        _grid.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        // Ctrl-click for individual rows, Shift-click for a range.
        _grid.MultiSelect = true;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "date", HeaderText = "Date", FillWeight = 40 },
            new DataGridViewTextBoxColumn { Name = "driver", HeaderText = "Driver", FillWeight = 55 },
            new DataGridViewTextBoxColumn { Name = "track", HeaderText = "Track", FillWeight = 50 },
            new DataGridViewTextBoxColumn { Name = "chassis", HeaderText = "Chassis Sheet", FillWeight = 60 },
            new DataGridViewTextBoxColumn { Name = "filled", HeaderText = "Filled", FillWeight = 30 },
            new DataGridViewTextBoxColumn { Name = "bestlap", HeaderText = "Best Lap", FillWeight = 45 },
            new DataGridViewTextBoxColumn { Name = "summary", HeaderText = "Setup", FillWeight = 180 }
        );
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) EditSelected(); };

        var btnAdd = new Button { Text = "Log a Setup..." };
        btnAdd.Click += (_, _) => AddNew();

        var btnEdit = new Button { Text = "Edit..." };
        btnEdit.Click += (_, _) => EditSelected();

        var btnDuplicate = new Button { Text = "Duplicate" };
        btnDuplicate.Click += (_, _) => DuplicateSelected();

        var btnDelete = new Button { Text = "Delete" };
        btnDelete.Click += (_, _) => DeleteSelected();

        var btnTemplates = new Button { Text = "Chassis Sheets..." };
        btnTemplates.Click += (_, _) => ManageTemplates();

        var lblDupHint = new Label
        {
            Text = "Duplicate copies the last setup as a starting point for the next one - most changes are small tweaks, not fresh starts.",
            AutoSize = true,
            MaximumSize = new Size(880, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        // Sized to their captions at the theme's button height - they were
        // left at WinForms' default 23px, which cut the bottoms off "g" and
        // "p" - and the hint goes on its own line underneath rather than
        // beside them, where it ran off the edge of the page.
        UiLayout.ButtonRow(12, 470, btnAdd, btnEdit, btnDuplicate, btnDelete, btnTemplates);
        lblDupHint.Location = new Point(12, 470 + UiLayout.ButtonHeight + UiLayout.HintGap + 2);

        Controls.Add(lblIntro);
        Controls.Add(lblFilter);
        Controls.Add(_cmbDriverFilter);
        Controls.Add(_lblSummary);
        Controls.Add(_grid);
        Controls.Add(btnAdd);
        Controls.Add(btnEdit);
        Controls.Add(btnDuplicate);
        Controls.Add(btnDelete);
        Controls.Add(btnTemplates);
        Controls.Add(lblDupHint);
    }

    /// <summary>Call when this page becomes visible - drivers and sessions can change elsewhere while it's not active.</summary>
    public void RefreshAll()
    {
        var previous = _cmbDriverFilter.SelectedItem as string;

        var drivers = _store.All.Select(e => e.DriverName)
            .Concat(_profiles.All.Select(p => p.DriverName))
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .ToList();

        _cmbDriverFilter.Items.Clear();
        _cmbDriverFilter.Items.Add("(all drivers)");
        foreach (var d in drivers)
        {
            _cmbDriverFilter.Items.Add(d);
        }

        var idx = previous is not null ? _cmbDriverFilter.Items.IndexOf(previous) : -1;
        _cmbDriverFilter.SelectedIndex = idx >= 0 ? idx : 0;
    }


    /// <summary>
    /// Opens the chassis sheet manager (ChassisSheetsForm - the window the
    /// Garage's Chassis Sheets... opens too). Editing a sheet's fields
    /// changes what every entry using it can record, so the log is
    /// refreshed afterwards to pick up renamed sheets and re-summarise rows.
    /// </summary>
    private void ManageTemplates()
    {
        using var picker = new ChassisSheetsForm(_chassisTemplates, _store, _undo);
        picker.ShowDialog(this);

        RefreshGrid();
    }

    private void RefreshGrid()
    {
        var filter = _cmbDriverFilter.SelectedIndex > 0 ? _cmbDriverFilter.SelectedItem as string : null;
        var entries = _store.All
            .Where(e => filter is null || string.Equals(e.DriverName, filter, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var previouslySelected = _grid.CurrentRow?.Tag as string;

        _grid.Rows.Clear();
        DataGridViewRow? restoredRow = null;
        foreach (var entry in entries)
        {
            var template = _chassisTemplates.Get(entry.ChassisTemplateId);

            var rowIndex = _grid.Rows.Add(
                entry.RecordedAtUtc.ToLocalTime().ToString("d"),
                entry.DriverName,
                string.IsNullOrWhiteSpace(entry.TrackLayoutName) ? "-" : entry.TrackLayoutName,
                template?.Name ?? "(legacy entry)",
                entry.FilledCount > 0 ? entry.FilledCount.ToString() : "-",
                BestLapFor(entry),
                entry.Summarise(template).Replace('\n', ' ').Replace('\r', ' '));

            _grid.Rows[rowIndex].Tag = entry.Id;

            if (entry.Id == previouslySelected)
            {
                restoredRow = _grid.Rows[rowIndex];
            }
        }

        // Adding rows to an empty grid makes the first row current and selected, and
        // MultiSelect is on, so only marking the old row Selected used to leave two rows
        // highlighted while CurrentRow stayed on row 0. Edit and Duplicate read CurrentRow
        // and Delete reads SelectedRows, so after editing the third setup, Edit opened the
        // first one and Delete offered to remove two. Clearing the selection and moving the
        // current cell onto the restored row makes what is highlighted and what the buttons
        // act on the same single row (and scrolls it into view). If the entry is gone or
        // filtered out, the grid keeps its usual first-row default.
        if (restoredRow is not null)
        {
            _grid.ClearSelection();
            _grid.CurrentCell = restoredRow.Cells[0];
            restoredRow.Selected = true;
        }

        _lblSummary.Text = $"{entries.Count} setup{(entries.Count == 1 ? "" : "s")} logged";
    }

    /// <summary>Pulls the linked session's best lap for this driver, which is the whole point of linking one.</summary>
    private string BestLapFor(SetupEntry entry)
    {
        if (string.IsNullOrEmpty(entry.LinkedSessionId))
        {
            return "-";
        }

        var session = _history.Get(entry.LinkedSessionId);
        var result = session?.Results.FirstOrDefault(r =>
            string.Equals(r.DriverName, entry.DriverName, StringComparison.OrdinalIgnoreCase));

        return result is { FastLapMs: > 0 } ? $"{result.FastLapMs / 1000.0:0.000}s" : "-";
    }

    private SetupEntry? SelectedEntry() =>
        _grid.CurrentRow?.Tag is string id ? _store.Get(id) : null;

    private void AddNew()
    {
        using var form = new SetupEntryForm(null, _chassisTemplates, _history, _profiles, _trackLayouts);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            SaveNewEntry(form.Result);
            RefreshAll();
        }
    }

    /// <summary>
    /// Adds a brand-new entry (Log a Setup, or Duplicate) with its own undo
    /// step. Without one, the next Ctrl+Z reached past the new entry and took
    /// back whatever came before it - a delete or an edit - and the new entry
    /// stayed. The Garage logs a setup the same way, so the two pages agree.
    /// </summary>
    private void SaveNewEntry(SetupEntry entry)
    {
        var store = _store;
        var id = entry.Id;
        var label = $"Log a setup ({entry.DriverName}, {entry.RecordedAtUtc.ToLocalTime():d})";

        _undo.Capture(label, () =>
        {
            // A null "before" tells the store the entry was new, so undoing it
            // removes it. Said out loud when it has been deleted since, as the
            // edit's undo does, rather than reporting an undo that did nothing.
            if (!store.UndoUpsert(id, null))
            {
                throw new InvalidOperationException("that setup entry has been deleted since, so there is nothing to take back.");
            }
        });

        _store.Upsert(entry);
    }

    private void EditSelected()
    {
        var entry = SelectedEntry();
        if (entry is null)
        {
            return;
        }

        // The form writes onto what it is given as it goes, so it is given a
        // copy: handed the stored entry, a Cancel left the half-made changes
        // in memory (saved with the next edit), and the old values an undo
        // needs were gone.
        var before = SetupLogStore.CopyOf(entry);

        using var form = new SetupEntryForm(SetupLogStore.CopyOf(entry), _chassisTemplates, _history, _profiles, _trackLayouts);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            var store = _store;

            _undo.Capture($"Edit setup entry ({before.DriverName}, {before.RecordedAtUtc.ToLocalTime():d})", () =>
            {
                if (!store.UndoUpsert(before.Id, before))
                {
                    throw new InvalidOperationException("that setup entry has been deleted since, so there is nothing to put back.");
                }
            });

            _store.Upsert(form.Result);
            RefreshGrid();
        }
    }

    private void DuplicateSelected()
    {
        var entry = SelectedEntry();
        if (entry is null)
        {
            AppDialog.Show(this, "Select a setup to duplicate first.", "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // The model owns what carries over and what doesn't.
        var copy = entry.CloneForNewEntry();

        using var form = new SetupEntryForm(copy, _chassisTemplates, _history, _profiles, _trackLayouts);
        if (form.ShowDialog(this) == DialogResult.OK)
        {
            SaveNewEntry(form.Result);
            RefreshGrid();
        }
    }

    private void DeleteSelected()
    {
        var removed = _grid.SelectedRows
            .Cast<DataGridViewRow>()
            .OrderBy(r => r.Index)
            .Select(r => r.Tag as string)
            .Where(id => id is not null)
            .Select(id => _store.Get(id!))
            .Where(e => e is not null)
            .Select(e => e!)
            .ToList();

        if (removed.Count == 0)
        {
            return;
        }

        var what = removed.Count == 1 ? "Delete this setup entry?" : $"Delete these {removed.Count} setup entries?";

        if (AppDialog.Show(this, what, "Confirm delete", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        var store = _store;

        _undo.Capture(removed.Count == 1 ? "Delete setup entry" : $"Delete {removed.Count} setup entries", () =>
        {
            store.RestoreDeleted(removed);
        });

        foreach (var entry in removed)
        {
            _store.Delete(entry.Id);
        }

        RefreshGrid();
    }
}

