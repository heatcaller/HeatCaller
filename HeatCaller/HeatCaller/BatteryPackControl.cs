namespace HeatCaller;

/// <summary>
/// Battery packs and their effect on pace.
///
/// Two halves, because there are two jobs: keeping the pack list, and
/// tagging sessions with the pack that was in the car. The pace comparison
/// is the payoff for doing the second.
/// </summary>
public sealed class BatteryPackControl : UserControl
{
    private readonly BatteryPackStore _store;
    private readonly SessionHistoryStore _history;
    private readonly ProfileStore _profiles;
    private readonly UndoService _undo;

    private readonly DataGridView _gridPacks = new();
    private readonly DataGridView _gridSessions = new();
    private readonly DataGridView _gridPace = new();
    private readonly ComboBox _cmbDriver = new();
    private readonly Label _lblStatus = new();

    public BatteryPackControl(BatteryPackStore store, SessionHistoryStore history,
        ProfileStore profiles, UndoService undo)
    {
        _store = store;
        _history = history;
        _profiles = profiles;
        _undo = undo;

        Dock = DockStyle.Fill;
        Font = AppTheme.BodyFont;
        ForeColor = AppTheme.TextDark;

        Size = new Size(980, 600);
        MinimumSize = new Size(900, 560);

        BuildLayout();
        RefreshAll();
    }

    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "Tag each session with the pack that was in the car, and the comparison below shows whether any pack "
                 + "is costing you time. One choice per session - no charge logging.",
            Location = new Point(12, 12),
            AutoSize = true,
            MaximumSize = new Size(876, 0),  // capped to the page's own minimum width, not a wider guess
            ForeColor = AppTheme.TextMuted,

            // Set here because the heading row below is measured from this
            // label's height, and an unparented label measures at the
            // WinForms default font rather than the one it is drawn in.
            Font = AppTheme.BodyFont,
        };

        // Both section headings share a row, and that row is measured off
        // the intro rather than parked at y=58. The intro wraps, and how
        // many lines it wraps to depends on the text size - so a fixed 58
        // is only far enough down at one of them, and at the others the
        // headings sat hard against the paragraph above.
        var headingY = UiLayout.Below(lblIntro, UiLayout.SectionGap);

        // --- Packs ---
        var lblPacks = new Label { Text = "PACKS", AutoSize = true, Font = AppTheme.BoldFont, ForeColor = AppTheme.Primary };

        // --- Pace comparison, whose heading shares the row with a picker ---
        var lblPace = new Label { Text = "PACE BY PACK", AutoSize = true, Font = AppTheme.BoldFont, ForeColor = AppTheme.Primary };

        _cmbDriver.Font = AppTheme.BodyFont;
        _cmbDriver.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbDriver.SelectedIndexChanged += (_, _) => RefreshPace();

        var headingHeight = Math.Max(_cmbDriver.Height, AppTheme.BoldFont.Height);

        lblPacks.Location = new Point(12, UiLayout.CentreOn(lblPacks, headingY, headingHeight));
        lblPace.Location = new Point(430, UiLayout.CentreOn(lblPace, headingY, headingHeight));

        // Placed AFTER its heading, measured, instead of at a remembered
        // x=556 that assumed "PACE BY PACK" was 126px wide. It is wider
        // than that as soon as the text grows, and the heading ran into
        // the picker.
        _cmbDriver.Location = new Point(UiLayout.After(lblPace) + UiLayout.RowGap, headingY);
        _cmbDriver.Width = Math.Clamp(968 - _cmbDriver.Left, 140, 260);

        var gridTop = UiLayout.Below(_cmbDriver, UiLayout.RowGap);

        // Everything below the heading row moves with it.
        var shift = gridTop - 82;

        _gridPacks.Location = new Point(12, gridTop);
        _gridPacks.Size = new Size(400, 190);
        _gridPacks.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        _gridPacks.ReadOnly = true;
        _gridPacks.AllowUserToAddRows = false;
        _gridPacks.RowHeadersVisible = false;
        _gridPacks.MultiSelect = false;
        _gridPacks.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _gridPacks.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _gridPacks.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "label", HeaderText = "Pack", FillWeight = 40 },
            new DataGridViewTextBoxColumn { Name = "chem", HeaderText = "Type", FillWeight = 35 },
            new DataGridViewTextBoxColumn { Name = "used", HeaderText = "Sessions", FillWeight = 35 },
            new DataGridViewTextBoxColumn { Name = "notes", HeaderText = "Notes", FillWeight = 70 }
        );
        _gridPacks.CellDoubleClick += (_, args) => { if (args.RowIndex >= 0) { AddOrEditPack(SelectedPack()); } };

        var btnAdd = new Button { Text = "Add Pack..." };
        btnAdd.Click += (_, _) => AddOrEditPack(null);

        var btnEdit = new Button { Text = "Edit..." };
        btnEdit.Click += (_, _) => AddOrEditPack(SelectedPack());

        var btnDelete = new Button { Text = "Delete" };
        btnDelete.Click += (_, _) => DeletePack();

        AppTheme.LayoutButtonRow(12, 280 + shift, 8, btnAdd, btnEdit, btnDelete);

        _gridPace.Location = new Point(430, gridTop);
        _gridPace.Size = new Size(538, 190);
        _gridPace.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _gridPace.ReadOnly = true;
        _gridPace.AllowUserToAddRows = false;
        _gridPace.RowHeadersVisible = false;
        _gridPace.MultiSelect = false;
        _gridPace.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _gridPace.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _gridPace.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "pack", HeaderText = "Pack", FillWeight = 35 },
            new DataGridViewTextBoxColumn { Name = "sessions", HeaderText = "Sessions", FillWeight = 30 },
            new DataGridViewTextBoxColumn { Name = "best", HeaderText = "Best lap", FillWeight = 35 },
            new DataGridViewTextBoxColumn { Name = "avg", HeaderText = "Avg best lap", FillWeight = 40 },
            new DataGridViewTextBoxColumn { Name = "gap", HeaderText = "Gap", FillWeight = 30 }
        );

        // --- Session assignment ---
        var lblSessions = new Label
        {
            Text = "TAG A SESSION - double-click a row to set which pack was in the car",
            Location = new Point(12, 322 + shift),
            AutoSize = true,
            Font = AppTheme.BoldFont,
            ForeColor = AppTheme.Primary,
        };

        _gridSessions.Location = new Point(12, 346 + shift);
        _gridSessions.Size = new Size(956, 190);
        // Top/Left/Right only - NOT Bottom. Bottom anchoring stretches the
        // grid downward on a taller window, painting over whatever sits
        // below it and making those controls invisible.
        _gridSessions.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _gridSessions.ReadOnly = true;
        _gridSessions.AllowUserToAddRows = false;
        _gridSessions.RowHeadersVisible = false;
        _gridSessions.MultiSelect = false;
        _gridSessions.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _gridSessions.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _gridSessions.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "when", HeaderText = "When", FillWeight = 60 },
            new DataGridViewTextBoxColumn { Name = "label", HeaderText = "Session", FillWeight = 80 },
            new DataGridViewTextBoxColumn { Name = "track", HeaderText = "Track", FillWeight = 55 },
            new DataGridViewTextBoxColumn { Name = "drivers", HeaderText = "Drivers", FillWeight = 35 },
            new DataGridViewTextBoxColumn { Name = "pack", HeaderText = "Pack used", FillWeight = 60 }
        );
        _gridSessions.CellDoubleClick += (_, args) => { if (args.RowIndex >= 0) { AssignPack(); } };

        // NOT anchored to the bottom, which is what used to draw these
        // over the grid.
        //
        // The grid above is fixed at y=346 with a height of 190, and these
        // sat at 548 - twelve pixels of clearance, measured once, at one
        // text size. Bottom anchoring then moves them relative to the
        // PAGE's bottom edge: on a window shorter than the height those
        // literals assumed - which at 150% text scaling it always is -
        // they ride up, straight over the last rows of the grid.
        //
        // Placed from the grid instead, so the clearance is a fact rather
        // than a coincidence, and anchored to the top like the grid is.
        var btnAssign = new Button { Text = "Set Pack..." };
        btnAssign.Click += (_, _) => AssignPack();

        var btnClearAssign = new Button { Text = "Clear Pack" };
        btnClearAssign.Click += (_, _) => ClearAssignment();

        var buttonsTop = UiLayout.Below(_gridSessions, UiLayout.RowGap);
        AppTheme.LayoutButtonRow(12, buttonsTop, 8, btnAssign, btnClearAssign);

        _lblStatus.Location = new Point(12, buttonsTop + UiLayout.ButtonHeight + UiLayout.RowGap);
        _lblStatus.AutoSize = true;
        _lblStatus.ForeColor = AppTheme.TextMuted;

        Controls.AddRange(new Control[]
        {
            lblIntro, lblPacks, _gridPacks, btnAdd, btnEdit, btnDelete,
            lblPace, _cmbDriver, _gridPace,
            lblSessions, _gridSessions, btnAssign, btnClearAssign, _lblStatus,
        });
    }

    private BatteryPack? SelectedPack() =>
        _gridPacks.CurrentRow?.Tag is string id ? _store.Get(id) : null;

    private SessionRecord? SelectedSession() =>
        _gridSessions.CurrentRow?.Tag is string id ? _history.Get(id) : null;

    public void RefreshAll()
    {
        RefreshDriverCombo();
        RefreshPacks();
        RefreshSessions();
        RefreshPace();
    }

    private void RefreshDriverCombo()
    {
        var previous = (_cmbDriver.SelectedItem as DriverItem)?.Transponder;

        _cmbDriver.Items.Clear();
        foreach (var profile in _profiles.All.OrderBy(p => p.DriverName))
        {
            _cmbDriver.Items.Add(new DriverItem(profile.Transponder, profile.DriverName));
        }

        if (previous is not null)
        {
            for (var i = 0; i < _cmbDriver.Items.Count; i++)
            {
                if (_cmbDriver.Items[i] is DriverItem item
                    && string.Equals(item.Transponder, previous, StringComparison.OrdinalIgnoreCase))
                {
                    _cmbDriver.SelectedIndex = i;
                    return;
                }
            }
        }

        if (_cmbDriver.Items.Count > 0)
        {
            _cmbDriver.SelectedIndex = 0;
        }
    }

    private void RefreshPacks()
    {
        _gridPacks.Rows.Clear();

        foreach (var pack in _store.All)
        {
            var used = _history.All.Count(s => _store.PackForSession(s.Id)?.Id == pack.Id);

            var rowIndex = _gridPacks.Rows.Add(
                pack.DisplayName + (pack.Retired ? "  (retired)" : string.Empty),
                pack.Chemistry,
                used,
                pack.Notes.Replace('\r', ' ').Replace('\n', ' '));

            _gridPacks.Rows[rowIndex].Tag = pack.Id;

            if (pack.Retired)
            {
                _gridPacks.Rows[rowIndex].DefaultCellStyle.ForeColor = AppTheme.TextMuted;
            }
        }
    }

    private void RefreshSessions()
    {
        _gridSessions.Rows.Clear();

        foreach (var session in _history.All.Take(60))
        {
            var pack = _store.PackForSession(session.Id);

            var rowIndex = _gridSessions.Rows.Add(
                AppTime.DateAndTime(session.StartedAtUtc.ToLocalTime()),
                session.Label,
                string.IsNullOrWhiteSpace(session.TrackLayoutName) ? "-" : session.TrackLayoutName,
                session.Results.Count,
                pack?.DisplayName ?? "-");

            _gridSessions.Rows[rowIndex].Tag = session.Id;

            if (pack is not null)
            {
                _gridSessions.Rows[rowIndex].Cells[4].Style.ForeColor = AppTheme.Success;
            }
        }

        var tagged = _history.All.Count(s => _store.PackForSession(s.Id) is not null);
        _lblStatus.Text = $"{tagged} of {Plural.Of(_history.All.Count, "session")} tagged with a pack.";
    }

    private void RefreshPace()
    {
        _gridPace.Rows.Clear();

        if (_cmbDriver.SelectedItem is not DriverItem driver)
        {
            return;
        }

        var rows = _store.ComparePace(driver.Transponder, _history);

        if (rows.Count == 0)
        {
            return;
        }

        foreach (var row in rows)
        {
            var rowIndex = _gridPace.Rows.Add(
                row.Pack.DisplayName,
                row.Sessions,
                $"{row.BestLapSeconds:0.000}s",
                $"{row.AverageBestLapSeconds:0.000}s",
                row.GapToBestPackSeconds <= 0 ? "-" : $"+{row.GapToBestPackSeconds:0.000}s");

            // Only a gap worth acting on gets highlighted. Flagging every
            // difference would make normal run-to-run variation look like a
            // tired pack.
            if (row.GapToBestPackSeconds >= 0.1)
            {
                _gridPace.Rows[rowIndex].Cells[4].Style.ForeColor = AppTheme.Danger;
                _gridPace.Rows[rowIndex].Cells[4].Style.Font = AppTheme.BoldFont;
            }
            else if (row.GapToBestPackSeconds <= 0)
            {
                _gridPace.Rows[rowIndex].Cells[0].Style.ForeColor = AppTheme.Success;
            }
        }
    }

    private void AddOrEditPack(BatteryPack? existing)
    {
        // The dialog writes onto whatever it is given, so it gets a copy.
        // Handed the stored pack, it changed it before the undo could note
        // the old values - Undo put back the edited pack and did nothing -
        // and Cancel left half an edit in memory.
        var before = existing is null ? null : BatteryPackStore.CopyOf(existing);

        using var form = new BatteryPackEditForm(existing is null ? null : BatteryPackStore.CopyOf(existing));
        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        // The store, not this page, in the undo: the page may be gone by
        // then, and re-reads the store when it is next shown.
        var store = _store;
        var id = form.Result.Id;
        var name = form.Result.DisplayName;

        _undo.Capture(existing is null ? $"Add battery pack {name}" : $"Edit pack {name}", () =>
        {
            if (!store.UndoUpsert(id, before))
            {
                throw new InvalidOperationException($"pack {name} has been deleted since, so there is nothing to put back.");
            }
        });

        _store.Upsert(form.Result);
        RefreshAll();
    }

    private void DeletePack()
    {
        if (SelectedPack() is not { } pack)
        {
            return;
        }

        var used = _history.All.Count(s => _store.PackForSession(s.Id)?.Id == pack.Id);

        if (AppDialog.Show(this,
                $"Delete pack \"{pack.DisplayName}\"?"
                + (used > 0
                    ? $"\r\n\r\nIt's tagged on {Plural.Of(used, "session")} - {(used == 1 ? "that tag is" : "those tags are")} removed, so its pace history is lost. "
                      + "Retiring it instead keeps the history."
                    : string.Empty),
                "Delete pack", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        var store = _store;
        var index = _store.IndexOf(pack.Id);
        var tagged = _store.SessionsTaggedWith(pack.Id);

        _undo.Capture($"Delete pack {pack.DisplayName}", () => store.RestoreDeleted(pack, index, tagged));

        _store.Delete(pack.Id);
        RefreshAll();
    }

    private void AssignPack()
    {
        if (SelectedSession() is not { } session)
        {
            AppDialog.Show(this, "Select a session first.", "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var packs = _store.InRotation;
        if (packs.Count == 0)
        {
            AppDialog.Show(this, "Add a pack first.", "No packs", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var picker = new Form
        {
            Text = "Which pack?",
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(320, 300),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            Font = Font,
            BackColor = AppTheme.Background,
            ForeColor = AppTheme.TextDark,
        };

        var list = new ListBox { Location = new Point(12, 12), Size = new Size(296, 220), BackColor = AppTheme.Surface };
        foreach (var pack in packs)
        {
            list.Items.Add(new PackItem(pack));
        }

        var current = _store.PackForSession(session.Id);
        list.SelectedIndex = current is null
            ? 0
            : Math.Max(0, packs.ToList().FindIndex(p => p.Id == current.Id));

        var ok = new Button { Text = "Set", Location = new Point(126, 246), Width = 84, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Location = new Point(218, 246), Width = 90, DialogResult = DialogResult.Cancel };

        picker.Controls.AddRange(new Control[] { list, ok, cancel });
        picker.AcceptButton = ok;
        picker.CancelButton = cancel;
        AppTheme.ApplyRecursively(picker);

        if (picker.ShowDialog(this) != DialogResult.OK || list.SelectedItem is not PackItem chosen)
        {
            return;
        }

        var store = _store;
        var sessionId = session.Id;
        var previous = _store.PackIdForSession(sessionId);

        // Only this session's tag goes back.
        _undo.Capture($"Tag session with pack {chosen.Pack.DisplayName}", () => store.AssignSession(sessionId, previous));

        _store.AssignSession(session.Id, chosen.Pack.Id);
        RefreshAll();
    }

    private void ClearAssignment()
    {
        if (SelectedSession() is not { } session)
        {
            return;
        }

        var store = _store;
        var sessionId = session.Id;
        var previous = _store.PackIdForSession(sessionId);

        if (previous is null)
        {
            return;
        }

        _undo.Capture("Clear session pack", () => store.AssignSession(sessionId, previous));

        _store.AssignSession(session.Id, null);
        RefreshAll();
    }

    private sealed record DriverItem(string Transponder, string DriverName)
    {
        public override string ToString() =>
            string.IsNullOrWhiteSpace(DriverName) ? Transponder : DriverName;
    }

    private sealed record PackItem(BatteryPack Pack)
    {
        public override string ToString() =>
            $"{Pack.DisplayName}   {Pack.Chemistry}"
            + (string.IsNullOrWhiteSpace(Pack.Notes) ? string.Empty : $"   - {Pack.Notes}");
    }
}

/// <summary>Adds or edits a pack.</summary>
public sealed class BatteryPackEditForm : Form
{
    private readonly TextBox _txtLabel = new();
    private readonly ComboBox _cmbChemistry = new();
    private readonly TextBox _txtNotes = new();
    private readonly CheckBox _chkRetired = new();

    public BatteryPack Result { get; }

    public BatteryPackEditForm(BatteryPack? existing)
    {
        Result = existing ?? new BatteryPack();

        Text = existing is null ? "Add Pack" : $"Edit {Result.DisplayName}";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(400, 290);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;
        ForeColor = AppTheme.TextDark;

        var lblLabel = new Label { Text = "Label:", Location = new Point(16, 20), AutoSize = true };
        _txtLabel.Location = new Point(100, 17);
        _txtLabel.Width = 284;
        _txtLabel.Text = Result.Label;
        _txtLabel.PlaceholderText = "3, Blue tape, A2...";

        var lblLabelHint = new Label
        {
            Text = "Whatever's written on the pack.",
            Location = new Point(100, 46),
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        var lblChem = new Label { Text = "Type:", Location = new Point(16, 76), AutoSize = true };
        _cmbChemistry.Location = new Point(100, 73);
        _cmbChemistry.Width = 160;
        _cmbChemistry.DropDownStyle = ComboBoxStyle.DropDown;
        _cmbChemistry.Items.AddRange(new object[] { "NiMH", "LiPo", "LiFe", "Alkaline" });
        _cmbChemistry.Text = string.IsNullOrWhiteSpace(Result.Chemistry) ? "NiMH" : Result.Chemistry;

        var lblNotes = new Label { Text = "Notes:", Location = new Point(16, 112), AutoSize = true };
        _txtNotes.Location = new Point(100, 109);
        _txtNotes.Size = new Size(284, 80);
        _txtNotes.Multiline = true;
        _txtNotes.ScrollBars = ScrollBars.Vertical;
        _txtNotes.Text = Result.Notes;

        _chkRetired.Text = "Retired (out of rotation, keeps its history)";
        _chkRetired.Location = new Point(100, 200);
        _chkRetired.AutoSize = true;
        _chkRetired.Checked = Result.Retired;

        var btnSave = new Button { Text = "Save" };
        btnSave.Click += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(_txtLabel.Text))
            {
                AppDialog.Show(this, "Give the pack a label.", "Missing label", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            Result.Label = _txtLabel.Text.Trim();
            Result.Chemistry = _cmbChemistry.Text.Trim();
            Result.Notes = _txtNotes.Text;
            Result.Retired = _chkRetired.Checked;

            DialogResult = DialogResult.OK;
            Close();
        };

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };
        AppTheme.LayoutButtonRow(190, 244, 8, btnSave, btnCancel);

        Controls.AddRange(new Control[]
        {
            lblLabel, _txtLabel, lblLabelHint, lblChem, _cmbChemistry,
            lblNotes, _txtNotes, _chkRetired, btnSave, btnCancel,
        });

        AcceptButton = btnSave;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);
    }
}
