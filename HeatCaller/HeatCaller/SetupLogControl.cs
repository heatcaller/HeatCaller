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
    /// Opens the chassis sheet manager. Editing a sheet's fields changes
    /// what every entry using it can record, so the log is refreshed
    /// afterwards to pick up renamed sheets and re-summarise rows.
    /// </summary>
    private void ManageTemplates()
    {
        using var picker = new Form
        {
            Text = "Chassis Setup Sheets",
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(460, 340),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            Font = Font,
            BackColor = AppTheme.Background,
        };

        var list = new ListBox { Location = new Point(12, 12), Size = new Size(300, 280) };

        void Reload()
        {
            list.Items.Clear();
            foreach (var t in _chassisTemplates.All)
            {
                list.Items.Add(t.Name);
            }
            if (list.Items.Count > 0)
            {
                list.SelectedIndex = 0;
            }
        }

        ChassisTemplate? Selected() =>
            list.SelectedIndex >= 0 ? _chassisTemplates.All.ElementAtOrDefault(list.SelectedIndex) : null;

        var btnEdit = new Button { Text = "Edit...", Location = new Point(322, 12), Width = 120 };
        btnEdit.Click += (_, _) =>
        {
            if (Selected() is not { } t) { return; }

            // The editor saves as it goes (a field edit is written straight
            // away), so the undo is captured once it closes, if anything
            // actually changed - whichever button closed it.
            var before = ChassisTemplateStore.CopyOf(t);
            var beforeJson = System.Text.Json.JsonSerializer.Serialize(before);

            using var editor = new ChassisTemplateForm(_chassisTemplates, t);
            editor.ShowDialog(picker);

            if (_chassisTemplates.Get(before.Id) is { } after
                && System.Text.Json.JsonSerializer.Serialize(after) != beforeJson)
            {
                var templates = _chassisTemplates;

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

        var btnNew = new Button { Text = "New...", Location = new Point(322, 50), Width = 120 };
        btnNew.Click += (_, _) =>
        {
            var created = ChassisTemplate.CreateKyoshoDefault();
            created.Name = "New chassis sheet";
            _chassisTemplates.Upsert(created);

            using var editor = new ChassisTemplateForm(_chassisTemplates, created);
            editor.ShowDialog(picker);
            Reload();
        };

        var btnDuplicate = new Button { Text = "Duplicate", Location = new Point(322, 88), Width = 120 };
        btnDuplicate.Click += (_, _) =>
        {
            if (Selected() is not { } t) { return; }
            _chassisTemplates.Upsert(t.Clone());
            Reload();
        };

        var btnDelete = new Button { Text = "Delete", Location = new Point(322, 126), Width = 120 };
        btnDelete.Click += (_, _) =>
        {
            if (Selected() is not { } t) { return; }

            if (AppDialog.Show(picker,
                    $"Delete the '{t.Name}' sheet?\r\n\r\nSetup entries that used it are kept, but will show as legacy entries. This can be reversed with Undo.",
                    "Confirm delete", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                // The template carries every section, field and option, so
                // the whole object goes into the undo capture - back in its
                // old place. The store, not this page, in the undo: the page
                // may be gone by then, and re-reads the store when shown.
                var templates = _chassisTemplates;
                var entries = _store;
                var index = _chassisTemplates.IndexOf(t.Id);
                var wasLast = _chassisTemplates.All.Count == 1;
                string? seededId = null;

                _undo.Capture($"Delete chassis sheet '{t.Name}'", () =>
                {
                    templates.RestoreDeleted(t, index, seededId, seededId is not null && entries.UsesTemplate(seededId));
                });

                _chassisTemplates.Delete(t.Id);

                // Deleting the last sheet seeds a default in its place; the
                // undo takes that one away again.
                if (wasLast && _chassisTemplates.All.Count == 1)
                {
                    seededId = _chassisTemplates.All[0].Id;
                }

                Reload();
            }
        };

        var btnClose = new Button { Text = "Close", Location = new Point(322, 262), Width = 120, DialogResult = DialogResult.OK };

        picker.Controls.AddRange(new Control[] { list, btnEdit, btnNew, btnDuplicate, btnDelete, btnClose });
        picker.AcceptButton = btnClose;

        Reload();
        AppTheme.ApplyRecursively(picker);
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
                _grid.Rows[rowIndex].Selected = true;
            }
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
            _store.Upsert(form.Result);
            RefreshAll();
        }
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
            _store.Upsert(form.Result);
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

