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

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>The packs card's width at the normal text size - about two fifths of the band, between these.</summary>
    private const int PacksLeastWidth = 360;
    private const int PacksMostWidth = 480;

    /// <summary>The driver picker's width at the normal text size - it is never narrower, and is as wide as the longest name.</summary>
    private const int DriverPickerWidth = 240;

    /// <summary>The packs' buttons - while there is room, their card keeps them on one line. See PaceWidth.</summary>
    private ButtonBar? _packButtons;

    /// <summary>The line with the driver picker, and the picker's width as last measured (see Pieces.PickerWidth) - forgotten when the list is refilled.</summary>
    private FieldRow? _driverRow;
    private (Font Font, int Count, int Width)? _driverWidth;

    public BatteryPackControl(BatteryPackStore store, SessionHistoryStore history,
        ProfileStore profiles, UndoService undo)
    {
        _store = store;
        _history = history;
        _profiles = profiles;
        _undo = undo;

        Dock = DockStyle.Fill;
        ForeColor = AppTheme.TextDark;

        // The body face, worn rather than set once, so it follows the text
        // size - see TextSize.
        //
        // No design size and no MinimumSize: the 900x560 floor was honoured
        // over the size the page was given, and a small window lost the
        // session buttons and the status line off the bottom. A short
        // window scrolls the page instead (FillPage).
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout();
        RefreshAll();
    }

    /// <summary>The page's own face changed - handed the body face at a new text size - so everything else it keeps in step follows. See TextSize.</summary>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>A new text size, reaching this page through its parent's font - the only way it does, since the page wears a face of its own. See TextSize.</summary>
    protected override void OnParentFontChanged(EventArgs e)
    {
        base.OnParentFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>
    /// The intro; the packs and the pace by pack, two cards side by side and
    /// the same height; and the sessions to tag, taking the height that is
    /// left. On the layout pieces (LayoutPieces.cs), so nothing is placed by
    /// hand.
    ///
    /// It used to be: a heading row measured from the intro, the two grids
    /// at 190px each with the packs one 400px wide, everything under them
    /// shifted down by however far the heading row had moved, and the
    /// sessions grid at a fixed height with its buttons placed from its
    /// bottom - over a 900x560 floor the page kept however small its
    /// window, and in faces handed out once, at build.
    /// </summary>
    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "Tag each session with the pack that was in the car, and the comparison below shows whether any pack "
                 + "is costing you time. One choice per session - no charge logging.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        // --- The packs ---
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

        // Every heading whole, at the text size in use - see Pieces.KeepHeadingsWhole.
        _textSize.Keep(() => Pieces.KeepHeadingsWhole(_gridPacks));
        _gridPacks.CellDoubleClick += (_, args) => { if (args.RowIndex >= 0) { AddOrEditPack(SelectedPack()); } };

        var btnAdd = new Button { Text = "Add Pack..." };
        btnAdd.Click += (_, _) => AddOrEditPack(null);

        var btnEdit = new Button { Text = "Edit..." };
        btnEdit.Click += (_, _) => AddOrEditPack(SelectedPack());

        var btnDelete = new Button { Text = "Delete" };
        btnDelete.Click += (_, _) => DeletePack();

        var packs = new Section("Packs");

        packs.AddFill(_gridPacks, () => GridLeast(_gridPacks, 4));
        _packButtons = packs.Add(new ButtonBar().With(btnAdd, btnEdit, btnDelete), gapBelow: 0);

        // --- The pace by pack, for one driver ---
        _cmbDriver.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbDriver.SelectedIndexChanged += (_, _) => RefreshPace();

        var driverRow = new FieldRow();

        // As wide as the longest name, not the whole card: a picker
        // stretched across the card read as a text box (layout pictures,
        // 29 Sep).
        driverRow.Add(new Label { Text = "Driver:" }, _cmbDriver, DriverWidth);
        _driverRow = driverRow;

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

        // Every heading whole, at the text size in use - see Pieces.KeepHeadingsWhole.
        _textSize.Keep(() => Pieces.KeepHeadingsWhole(_gridPace));

        var pace = new Section("Pace by pack");

        pace.Add(driverRow);
        pace.AddFill(_gridPace, () => GridLeast(_gridPace, 4), gapBelow: 0);

        // --- The sessions to tag ---
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

        // Every heading whole, at the text size in use - see Pieces.KeepHeadingsWhole.
        _textSize.Keep(() => Pieces.KeepHeadingsWhole(_gridSessions));
        _gridSessions.CellDoubleClick += (_, args) => { if (args.RowIndex >= 0) { AssignPack(); } };

        var btnAssign = new Button { Text = "Set Pack..." };
        btnAssign.Click += (_, _) => AssignPack();

        var btnClearAssign = new Button { Text = "Clear Pack" };
        btnClearAssign.Click += (_, _) => ClearAssignment();

        var sessionsHint = new Label { Text = "Double-click a session to set which pack was in the car.", AutoSize = true, ForeColor = AppTheme.TextMuted };

        _textSize.Wear(sessionsHint, () => AppTheme.SmallFont);

        // How many sessions are tagged.
        _lblStatus.AutoSize = true;
        _lblStatus.ForeColor = AppTheme.TextMuted;

        var sessions = new Section("Tag a session");

        sessions.Add(sessionsHint);
        sessions.AddFill(_gridSessions, () => GridLeast(_gridSessions, 3));
        sessions.Add(new ButtonBar().With(btnAssign, btnClearAssign));
        sessions.Add(_lblStatus, gapBelow: 0);

        // --- The page ---
        //
        // The two cards side by side in a band as tall as the taller needs:
        // the packs down the left, the pace down the right and filling the
        // band's height, so the two end level.
        Beside? band = null;

        band = new Beside(() => PaceWidth(band?.Width ?? 0))
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        band.AddFill(packs, packs.LeastHeight);
        band.AddSideFill(pace, pace.LeastHeight, gapBelow: 0);

        var page = new FillPage();

        page.Add(lblIntro);
        page.Add(band);
        page.AddFill(sessions, sessions.LeastHeight, gapBelow: 0);

        Controls.Add(page);
    }

    /// <summary>
    /// The pace card's width: what the packs card leaves of the band - the
    /// packs card about two fifths of it, between 360px and 480px at the
    /// normal text size, at the size in use - and never under 300px there.
    ///
    /// Too narrow for both at that - a small window at the largest text
    /// size - and each gives up the same share of its width, rather than
    /// the packs card, whose width is only what the pace card leaves,
    /// giving up all of its own. Nothing here scrolls sideways (FillPage).
    ///
    /// And while there is room, the packs card is wide enough for its three
    /// buttons on one line: at 480px Delete went onto a line of its own
    /// (layout pictures, 29 Sep).
    /// </summary>
    private int PaceWidth(int band)
    {
        var packs = Math.Max(
            Math.Clamp((int)(band * 0.4f), UiLayout.AtTextSize(PacksLeastWidth), UiLayout.AtTextSize(PacksMostWidth)),
            (_packButtons?.OneLineWidth ?? 0) + Section.Inset.Horizontal);

        var paceLeast = UiLayout.AtTextSize(300);

        // What the two share: all of the band but the gap a Beside leaves
        // between its parts and its column.
        var room = Math.Max(0, band - UiLayout.RowGap);

        if (packs + paceLeast > room)
        {
            packs = (int)((long)packs * room / (packs + paceLeast));
        }

        return Math.Max(0, room - packs);
    }

    /// <summary>The driver picker: as wide as the longest name, and never under its width at the normal text size.</summary>
    private int DriverWidth() =>
        Math.Max(UiLayout.AtTextSize(DriverPickerWidth), Pieces.PickerWidth(_cmbDriver, ref _driverWidth));

    /// <summary>The least a grid can do with: its headings and <paramref name="rows"/> rows. Below that the page scrolls instead - see FillPage.</summary>
    private static int GridLeast(DataGridView grid, int rows) =>
        grid.ColumnHeadersHeight + rows * grid.RowTemplate.Height;

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

        // The longest name may have come or gone: measured again, and the
        // line with it.
        _driverWidth = null;
        _driverRow?.PerformLayout();

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
