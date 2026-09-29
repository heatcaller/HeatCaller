namespace HeatCaller;

/// <summary>
/// Manages the club's loaner transponders.
///
/// The question this page exists to answer is "what hasn't come back yet?",
/// so that's what it leads with: units on loan sort to the top and are
/// highlighted, and a running count sits where it can't be missed. Everything
/// else - adding units, editing tags, loan history - is secondary to not
/// losing hardware at the end of the day.
/// </summary>
public sealed class RentalTransponderControl : UserControl
{
    private readonly RentalTransponderStore _store;
    private readonly ProfileStore _profiles;
    private readonly UndoService _undo;

    private readonly DataGridView _grid = new();
    private readonly Panel _bannerOutstanding = new();
    private readonly Label _lblOutstanding = new();

    /// <summary>Drives the slow pulse while units are out. Stopped when everything is back, so a settled page isn't animating for no reason.</summary>
    private readonly System.Windows.Forms.Timer _pulse = new() { Interval = 600 };
    private bool _pulseOn;

    /// <summary>
    /// Raised whenever the outstanding count changes, so the sidebar badge
    /// can track it. An event rather than the control reaching into the
    /// nav directly - this page shouldn't need to know the shell exists.
    /// </summary>
    public event Action<int>? OutstandingCountChanged;
    private readonly Label _lblStatus = new();

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    public RentalTransponderControl(RentalTransponderStore store, ProfileStore profiles, UndoService undo)
    {
        _store = store;
        _profiles = profiles;
        _undo = undo;

        Dock = DockStyle.Fill;
        ForeColor = AppTheme.TextDark;

        // The body face, worn rather than set once, so it follows the text
        // size - see TextSize.
        //
        // No design size and no MinimumSize: the 900x540 floor was honoured
        // over the size the page was given, and a small window lost the
        // buttons off the bottom. A short window scrolls the page instead
        // (FillPage).
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout();
        RefreshGrid();
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
    /// The intro, the count of units still out, then one card: the units
    /// taking the height that is left, the buttons under them and the
    /// status line. On the layout pieces (LayoutPieces.cs), so nothing is
    /// placed by hand.
    ///
    /// It used to be: the grid at a fixed height, the buttons placed from
    /// its bottom and the count in a banner 64px tall, over a 900x540 floor
    /// the page kept however small its window - and the count's 26pt face
    /// set once, so it never followed the text size.
    /// </summary>
    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "Club transponders loaned to drivers without their own. Units still out are listed first and highlighted - "
                 + "check the count before packing up.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        // The count, as big as the stat tiles' numbers, in a band as tall as
        // it needs. Its colours are the page's to set (ApplyBannerColors),
        // not the theme pass's - left to it, the band went the page's
        // colour whenever the look or the text size changed, until the next
        // refresh.
        _bannerOutstanding.Tag = AppTheme.KeepBackColor;

        _lblOutstanding.Dock = DockStyle.Fill;
        _lblOutstanding.TextAlign = ContentAlignment.MiddleCenter;
        _textSize.Wear(_lblOutstanding, () => AppTheme.StatFont);
        _textSize.Keep(() =>
        {
            var height = AppTheme.StatFont.Height + UiLayout.RowGap * 2;

            if (_bannerOutstanding.Height != height)
            {
                _bannerOutstanding.Height = height;
            }
        });

        _bannerOutstanding.Controls.Add(_lblOutstanding);

        _pulse.Tick += (_, _) =>
        {
            _pulseOn = !_pulseOn;
            ApplyBannerColors();
        };

        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "tag", HeaderText = "Asset Tag", FillWeight = 50 },
            new DataGridViewTextBoxColumn { Name = "transponder", HeaderText = "Transponder", FillWeight = 60 },
            new DataGridViewTextBoxColumn { Name = "status", HeaderText = "Status", FillWeight = 45 },
            new DataGridViewTextBoxColumn { Name = "holder", HeaderText = "With", FillWeight = 75 },
            new DataGridViewTextBoxColumn { Name = "deposit", HeaderText = "Deposit Held", FillWeight = 70 },
            new DataGridViewTextBoxColumn { Name = "outfor", HeaderText = "Out For", FillWeight = 45 },
            new DataGridViewTextBoxColumn { Name = "loans", HeaderText = "Loans", FillWeight = 35 },
            new DataGridViewTextBoxColumn { Name = "notes", HeaderText = "Notes", FillWeight = 90 }
        );

        // Every heading whole, at the text size in use - see Pieces.KeepHeadingsWhole.
        _textSize.Keep(() => Pieces.KeepHeadingsWhole(_grid));
        _grid.CellDoubleClick += (_, args) =>
        {
            if (args.RowIndex >= 0)
            {
                ToggleSelected();
            }
        };

        var btnCheckOut = new Button { Text = "Check Out...", };
        btnCheckOut.Click += (_, _) => CheckOutSelected();

        var btnCheckIn = new Button { Text = "Check In...", };
        btnCheckIn.Click += (_, _) => CheckInSelected();

        var btnAdd = new Button { Text = "Add Unit...", };
        btnAdd.Click += (_, _) => AddOrEdit(null);

        var btnEdit = new Button { Text = "Edit...", };
        btnEdit.Click += (_, _) => AddOrEdit(Selected());

        var btnHistory = new Button { Text = "Loan History...", };
        btnHistory.Click += (_, _) => ShowHistory();

        var btnDelete = new Button { Text = "Delete", };
        btnDelete.Click += (_, _) => DeleteSelected();

        // What the last command did.
        _lblStatus.AutoSize = true;
        _lblStatus.ForeColor = AppTheme.TextMuted;

        var units = new Section("Transponders");

        units.AddFill(_grid, () => _grid.ColumnHeadersHeight + 3 * _grid.RowTemplate.Height);
        units.Add(new ButtonBar().With(btnCheckOut, btnCheckIn, btnAdd, btnEdit, btnHistory, btnDelete));
        units.Add(_lblStatus, gapBelow: 0);

        var page = new FillPage();

        page.Add(lblIntro);
        page.Add(_bannerOutstanding);
        page.AddFill(units, units.LeastHeight, gapBelow: 0);

        Controls.Add(page);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _pulse.Stop();
            _pulse.Dispose();
        }

        base.Dispose(disposing);
    }

    private RentalTransponder? Selected() =>
        _grid.CurrentRow?.Tag is string id ? _store.Get(id) : null;

    public void RefreshAll() => RefreshGrid();

    private void RefreshGrid()
    {
        var selectedId = _grid.CurrentRow?.Tag as string;
        _grid.Rows.Clear();

        // On-loan units first: the whole point of the page is spotting what
        // hasn't come back, so they shouldn't be mixed in alphabetically.
        var ordered = _store.All
            .OrderByDescending(u => u.IsOut)
            .ThenBy(u => u.Retired)
            .ThenBy(u => u.AssetTag)
            .ThenBy(u => u.Transponder)
            .ToList();

        foreach (var unit in ordered)
        {
            var status = unit.Retired ? "Retired" : unit.IsOut ? "OUT" : "Available";
            var outFor = unit.OutFor is { } span ? FormatSpan(span) : "-";

            var rowIndex = _grid.Rows.Add(
                string.IsNullOrWhiteSpace(unit.AssetTag) ? "-" : unit.AssetTag,
                unit.Transponder,
                status,
                string.IsNullOrWhiteSpace(unit.CurrentHolderName) ? "-" : unit.CurrentHolderName,
                string.IsNullOrWhiteSpace(unit.DepositHeld) ? "-" : unit.DepositHeld,
                outFor,
                unit.History.Count,
                unit.Notes.Replace('\r', ' ').Replace('\n', ' '));

            _grid.Rows[rowIndex].Tag = unit.Transponder;

            if (unit.IsOut)
            {
                _grid.Rows[rowIndex].Cells[2].Style.ForeColor = AppTheme.Accent;
                _grid.Rows[rowIndex].Cells[2].Style.Font = AppTheme.BoldFont;
            }
            else if (unit.Retired)
            {
                _grid.Rows[rowIndex].DefaultCellStyle.ForeColor = AppTheme.TextMuted;
            }

            if (selectedId is not null && string.Equals(selectedId, unit.Transponder, StringComparison.OrdinalIgnoreCase))
            {
                _grid.Rows[rowIndex].Selected = true;
                _grid.CurrentCell = _grid.Rows[rowIndex].Cells[0];
            }
        }

        var out_ = _store.OutNow.Count;

        _lblOutstanding.Text = out_ == 0
            ? "ALL UNITS RETURNED"
            : out_ == 1
                ? "1 UNIT STILL OUT"
                : $"{out_} UNITS STILL OUT";

        // Only animate when there's something to chase. A page that pulses
        // when everything is fine trains people to ignore it.
        if (out_ > 0 && !_pulse.Enabled)
        {
            _pulse.Start();
        }
        else if (out_ == 0 && _pulse.Enabled)
        {
            _pulse.Stop();
            _pulseOn = false;
        }

        ApplyBannerColors();
        OutstandingCountChanged?.Invoke(out_);
    }


    /// <summary>
    /// Colours the banner. Outstanding units alternate between two shades of
    /// the warning colour rather than blinking on and off - a hard flash is
    /// hard to read, and the text needs to stay legible while it moves.
    /// </summary>
    private void ApplyBannerColors()
    {
        if (_bannerOutstanding.IsDisposed)
        {
            return;
        }

        var anyOut = _store.OutNow.Count > 0;

        if (!anyOut)
        {
            _bannerOutstanding.BackColor = Color.FromArgb(40, AppTheme.Success);
            _lblOutstanding.ForeColor = AppTheme.Success;
            return;
        }

        var accent = AppTheme.Accent;
        _bannerOutstanding.BackColor = _pulseOn
            ? Color.FromArgb(80, accent.R, accent.G, accent.B)
            : Color.FromArgb(45, accent.R, accent.G, accent.B);
        _lblOutstanding.ForeColor = accent;
    }

    private static string FormatSpan(TimeSpan span) =>
        span.TotalHours >= 1 ? $"{(int)span.TotalHours}h {span.Minutes}m" : $"{(int)span.TotalMinutes}m";

    /// <summary>Double-click loans out an available unit, or takes back one that's out.</summary>
    private void ToggleSelected()
    {
        var unit = Selected();
        if (unit is null)
        {
            return;
        }

        if (unit.IsOut)
        {
            CheckInSelected();
        }
        else
        {
            CheckOutSelected();
        }
    }

    private void CheckOutSelected()
    {
        var unit = Selected();
        if (unit is null)
        {
            AppDialog.Show(this, "Select a unit first.", "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (unit.Retired)
        {
            AppDialog.Show(this, $"{unit.DisplayName} is retired and can't be loaned out.", "Retired unit",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (unit.IsOut)
        {
            // Issuing the same transponder twice would mix two drivers' laps
            // under one ID, so this is refused rather than warned about.
            AppDialog.Show(this,
                $"{unit.DisplayName} is already out with {unit.CurrentHolderName}. Check it in first.",
                "Already on loan", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var form = new RentalCheckOutForm(unit, _profiles);
        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        // The store, not this page, in the undo: the page may be gone by
        // then, and it re-reads the store when it is next shown.
        var store = _store;
        var transponder = unit.Transponder;
        var name = unit.DisplayName;
        DateTime? outAt = null;

        _undo.Capture($"Check out {name} to {form.HolderName}", () =>
        {
            if (outAt is not { } at || !store.UndoCheckOut(transponder, at))
            {
                throw new InvalidOperationException($"{name} has been checked in or changed since, so its loan was left as it is.");
            }
        });

        _store.CheckOut(unit.Transponder, form.HolderName, form.Deposit);
        outAt = _store.Get(transponder)?.CheckedOutUtc;
        RefreshGrid();
        _lblStatus.Text = $"{unit.DisplayName} checked out to {form.HolderName}.";
    }

    private void CheckInSelected()
    {
        var unit = Selected();
        if (unit is null)
        {
            AppDialog.Show(this, "Select a unit first.", "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!unit.IsOut)
        {
            AppDialog.Show(this, $"{unit.DisplayName} isn't currently out.", "Not on loan",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var notes = Prompt("Check in",
            $"Returning {unit.DisplayName} from {unit.CurrentHolderName}.\r\n\r\n"
            + $"Deposit to give back: {(string.IsNullOrWhiteSpace(unit.DepositHeld) ? "(none recorded)" : unit.DepositHeld)}\r\n\r\n"
            + "Any notes on its condition?", string.Empty);

        if (notes is null)
        {
            return;
        }

        var store = _store;
        var transponder = unit.Transponder;
        var name = unit.DisplayName;
        RentalLoan? loan = null;

        _undo.Capture($"Check in {name}", () =>
        {
            if (loan is null || !store.UndoCheckIn(transponder, loan))
            {
                throw new InvalidOperationException($"{name} has been loaned out or changed since, so it was left as it is.");
            }
        });

        _store.CheckIn(unit.Transponder, notes);
        loan = _store.Get(transponder)?.History.LastOrDefault();
        RefreshGrid();
        _lblStatus.Text = $"{unit.DisplayName} returned.";
    }

    private void AddOrEdit(RentalTransponder? existing)
    {
        // Taken BEFORE the dialog opens: the dialog used to edit the stored
        // unit in place, so a snapshot taken afterwards already held the
        // edit and Undo put back the edited version. The dialog now edits a
        // copy, and the copy of the old details is taken before anything can
        // touch the store either way.
        var before = existing is null
            ? null
            : System.Text.Json.JsonSerializer.Deserialize<RentalTransponder>(System.Text.Json.JsonSerializer.Serialize(existing));

        using var form = new RentalUnitEditForm(existing);
        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        if (existing is null && _store.Get(form.Result.Transponder) is not null)
        {
            AppDialog.Show(this,
                $"Transponder {form.Result.Transponder} is already registered as a rental.",
                "Already registered", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var store = _store;
        var added = form.Result.Transponder;
        var name = form.Result.DisplayName;

        _undo.Capture(existing is null ? $"Add rental unit {name}" : $"Edit {name}", () =>
        {
            if (before is not null)
            {
                if (!store.RevertDetails(before))
                {
                    throw new InvalidOperationException($"{name} has been deleted since, so there is nothing to put back.");
                }

                return;
            }

            // Undoing an add. A unit out on loan now is left alone: removing
            // it would lose who has it and what deposit was taken.
            if (store.Get(added) is { IsOut: true })
            {
                throw new InvalidOperationException($"{name} is out on loan now. Check it in first, then delete it.");
            }

            store.Delete(added);
        });

        _store.Upsert(form.Result);
        RefreshGrid();
    }

    private void DeleteSelected()
    {
        var unit = Selected();
        if (unit is null)
        {
            return;
        }

        var warning = unit.IsOut
            ? $"\r\n\r\nWARNING: this unit is currently out with {unit.CurrentHolderName}."
            : string.Empty;

        if (AppDialog.Show(this,
                $"Delete {unit.DisplayName} and its loan history?{warning}",
                "Delete unit", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        var store = _store;
        var index = _store.IndexOf(unit.Transponder);

        _undo.Capture($"Delete rental unit {unit.DisplayName}", () =>
        {
            if (!store.RestoreDeleted(unit, index))
            {
                throw new InvalidOperationException($"a rental unit on {unit.Transponder} has been added since, so {unit.DisplayName} was not put back over it.");
            }
        });

        _store.Delete(unit.Transponder);
        RefreshGrid();
    }

    private void ShowHistory()
    {
        var unit = Selected();
        if (unit is null)
        {
            return;
        }

        if (unit.History.Count == 0)
        {
            AppDialog.Show(this, $"{unit.DisplayName} has no completed loans yet.", "No history",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var lines = unit.History
            .OrderByDescending(h => h.InUtc)
            .Select(h =>
                $"{AppTime.DateAndTime(h.OutUtc.ToLocalTime())} -> {AppTime.TimeOfDay(h.InUtc.ToLocalTime())}"
                + $"   {h.HolderName}"
                + (string.IsNullOrWhiteSpace(h.ReturnNotes) ? "" : $"   [{h.ReturnNotes}]"));

        AppDialog.Show(this,
            $"{unit.DisplayName} - {Plural.Of(unit.History.Count, "loan")}\r\n\r\n" + string.Join("\r\n", lines),
            "Loan history", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private string? Prompt(string title, string prompt, string defaultValue)
    {
        using var dialog = new Form
        {
            Text = title,
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(440, 220),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            Font = Font,
            BackColor = AppTheme.Background,
            ForeColor = AppTheme.TextDark,
        };

        var lbl = new Label { Text = prompt, Location = new Point(12, 12), AutoSize = true, MaximumSize = new Size(416, 0) };
        var txt = new TextBox { Location = new Point(12, 140), Width = 412, Text = defaultValue };
        var ok = new Button { Text = "OK", Location = new Point(246, 176), Width = 84, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Location = new Point(338, 176), Width = 86, DialogResult = DialogResult.Cancel };

        dialog.Controls.AddRange(new Control[] { lbl, txt, ok, cancel });
        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;

        AppTheme.ApplyRecursively(dialog);
        return dialog.ShowDialog(this) == DialogResult.OK ? txt.Text.Trim() : null;
    }
}
