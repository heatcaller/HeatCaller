namespace HeatCaller;

/// <summary>
/// Manages per-driver handicaps for mixed-ability grids.
///
/// The page makes two things explicit that a handicap system can easily
/// obscure: what each driver's handicap actually is, and where it came from.
/// A handicap decides who wins, so it shouldn't be a number that appeared
/// from somewhere without explanation.
/// </summary>
public sealed class HandicapControl : UserControl
{
    private readonly HandicapStore _store;
    private readonly ProfileStore _profiles;
    private readonly RaceClassStore _classes;
    private readonly SessionHistoryStore _history;
    private readonly UndoService _undo;

    /// <summary>
    /// The race programme, to tell which class a saved race was - see
    /// HandicapStore.SessionsFor. Null shows every class's laps, as before.
    /// (17 Oct 2026, Bug Hunt 8, A26-1.)
    /// </summary>
    private readonly RoundDefinitionStore? _rounds;

    private readonly ComboBox _cmbClass = new();
    private readonly DataGridView _grid = new();
    private readonly ComboBox _cmbMode = new();
    private readonly Label _lblMode = new();
    private readonly Label _lblStatus = new();

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>The class and mode pickers and the mode warning - held so a rebuilt class list can re-measure the row.</summary>
    private FieldRow? _pickerRow;

    /// <summary>The pickers' widths as last measured - see Pieces.PickerWidth. The class one is forgotten when the list is rebuilt.</summary>
    private (Font Font, int Count, int Width)? _classWidth;
    private (Font Font, int Count, int Width)? _modeWidth;

    /// <summary>The pickers' widths at the normal text size - they are never narrower.</summary>
    private const int ClassPickerWidth = UiLayout.FieldWidth;
    private const int ModePickerWidth = 250;

    public HandicapControl(HandicapStore store, ProfileStore profiles, RaceClassStore classes,
        SessionHistoryStore history, UndoService undo, RoundDefinitionStore? rounds = null)
    {
        _store = store;
        _profiles = profiles;
        _classes = classes;
        _history = history;
        _undo = undo;
        _rounds = rounds;

        Dock = DockStyle.Fill;
        ForeColor = AppTheme.TextDark;

        // The body face, worn rather than set once, so it follows the text
        // size - see TextSize.
        //
        // No design size and no MinimumSize: the 900x540 floor was honoured
        // over the size the page was given, and a small window lost the
        // buttons and the status line off the bottom. A short window
        // scrolls the page instead (FillPage).
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
    /// The intro, then one card: the class and mode pickers along a line,
    /// the drivers taking the height that is left, the buttons under them
    /// and the status line. On the layout pieces (LayoutPieces.cs), so
    /// nothing is placed by hand.
    ///
    /// It used to be measured top to bottom on every resize: captions
    /// measured, the pickers after them, the grid between the row and the
    /// buttons, over a 900x540 floor the page kept however small its
    /// window - and every control handed the body face once, at build, so
    /// none of it followed the text size.
    /// </summary>
    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "Handicaps even out a mixed-ability grid. They're applied when results are shown - recorded lap times, "
                 + "personal bests and track records are never altered.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _cmbClass.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbClass.SelectedIndexChanged += (_, _) => RefreshGrid();

        _cmbMode.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbMode.Items.AddRange(new object[]
        {
            new ModeItem(HandicapMode.None, "Off - scratch racing"),
            new ModeItem(HandicapMode.TimeCredit, "Time credit (seconds off total)"),
            new ModeItem(HandicapMode.LapCredit, "Lap credit (whole laps added)"),
        });
        _cmbMode.SelectedIndexChanged += (_, _) =>
        {
            if (SelectedClassName is { } name && _cmbMode.SelectedItem is ModeItem item
                && _store.ModeFor(name) != item.Mode)
            {
                _store.SetMode(name, item.Mode);
                RefreshGrid();
            }
        };

        // "Handicaps are OFF for this class", at the end of the pickers' line
        // - or on a line of its own when it no longer fits there (FieldRow).
        _lblMode.AutoSize = false;
        _lblMode.AutoEllipsis = true;
        _textSize.Wear(_lblMode, () => AppTheme.BoldFont);

        var pickerRow = new FieldRow();

        pickerRow.Add(new Label { Text = "Class:" }, _cmbClass, ClassWidth);
        pickerRow.Add(new Label { Text = "Mode:" }, _cmbMode, ModeWidth);

        // Never cut short: the whole warning, as the label measures itself,
        // or a line of its own.
        pickerRow.AddLast(null, _lblMode, () => _lblMode.GetPreferredSize(Size.Empty).Width);
        _pickerRow = pickerRow;

        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "driver", HeaderText = "Driver", FillWeight = 80 },
            new DataGridViewTextBoxColumn { Name = "transponder", HeaderText = "Transponder", FillWeight = 55 },
            new DataGridViewTextBoxColumn { Name = "time", HeaderText = "Time credit", FillWeight = 55 },
            new DataGridViewTextBoxColumn { Name = "laps", HeaderText = "Lap credit", FillWeight = 50 },
            new DataGridViewTextBoxColumn { Name = "locked", HeaderText = "Locked", FillWeight = 40 },
            new DataGridViewTextBoxColumn { Name = "pace", HeaderText = "Recent best lap", FillWeight = 65 },
            new DataGridViewTextBoxColumn { Name = "notes", HeaderText = "Notes", FillWeight = 95 }
        );

        // Every heading whole, at the text size in use - see Pieces.KeepHeadingsWhole.
        _textSize.Keep(() => Pieces.KeepHeadingsWhole(_grid));
        _grid.CellDoubleClick += (_, args) =>
        {
            if (args.RowIndex >= 0)
            {
                EditSelected();
            }
        };

        var btnEdit = new Button { Text = "Edit..." };
        btnEdit.Click += (_, _) => EditSelected();

        var btnSuggest = new Button { Text = "Suggest from History..." };
        btnSuggest.Click += (_, _) => SuggestFromHistory();

        var btnClear = new Button { Text = "Clear Handicap" };
        btnClear.Click += (_, _) => ClearSelected();

        var btnClearAll = new Button { Text = "Clear All in Class" };
        btnClearAll.Click += (_, _) => ClearAllInClass();

        // How many drivers, and how many of them have a handicap - wrapping
        // rather than cut short.
        _lblStatus.AutoSize = true;
        _lblStatus.ForeColor = AppTheme.TextMuted;

        var drivers = new Section("Drivers");

        drivers.Add(pickerRow);
        drivers.AddFill(_grid, () => _grid.ColumnHeadersHeight + 3 * _grid.RowTemplate.Height);
        drivers.Add(new ButtonBar().With(btnEdit, btnSuggest, btnClear, btnClearAll));
        drivers.Add(_lblStatus, gapBelow: 0);

        var page = new FillPage();

        page.Add(lblIntro);
        page.AddFill(drivers, drivers.LeastHeight, gapBelow: 0);

        Controls.Add(page);
    }

    /// <summary>The class picker: as wide as the longest class name, and never under its width at the normal text size.</summary>
    private int ClassWidth() =>
        Math.Max(UiLayout.AtTextSize(ClassPickerWidth), Pieces.PickerWidth(_cmbClass, ref _classWidth));

    /// <summary>The mode picker: as wide as its longest choice, and never under its width at the normal text size.</summary>
    private int ModeWidth() =>
        Math.Max(UiLayout.AtTextSize(ModePickerWidth), Pieces.PickerWidth(_cmbMode, ref _modeWidth));

    private string? SelectedClassName => _cmbClass.SelectedItem as string;

    private RaceClass? SelectedClass =>
        SelectedClassName is { } name
            ? _classes.All.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase))
            : null;

    private string? SelectedTransponder => _grid.CurrentRow?.Tag as string;

    public void RefreshAll()
    {
        RefreshClassCombo();
        RefreshGrid();
    }

    private void RefreshClassCombo()
    {
        var previous = _cmbClass.SelectedItem as string;

        _cmbClass.Items.Clear();
        foreach (var raceClass in _classes.All)
        {
            _cmbClass.Items.Add(raceClass.Name);
        }

        var index = previous is null ? 0 : _cmbClass.Items.IndexOf(previous);
        if (_cmbClass.Items.Count > 0)
        {
            _cmbClass.SelectedIndex = index >= 0 ? index : 0;
        }

        // The longest class may have come or gone: measured again, and the
        // row with it.
        _classWidth = null;
        _pickerRow?.PerformLayout();
    }

    private void RefreshGrid()
    {
        _grid.Rows.Clear();

        var raceClass = SelectedClass;
        if (raceClass is null)
        {
            _lblMode.Text = string.Empty;
            return;
        }

        var mode = _store.ModeFor(raceClass.Name);
        SelectMode(mode);

        // Say plainly when handicaps are off, so nobody sets numbers that
        // are being ignored.
        _lblMode.Text = mode == HandicapMode.None ? "Handicaps are OFF for this class" : string.Empty;
        _lblMode.ForeColor = AppTheme.Accent;

        var roster = _profiles.AllRaceable
            .Where(p => p.RunsClass(raceClass.Name))
            .OrderBy(p => p.DriverName)
            .ToList();

        // This class's races only (17 Oct 2026, Bug Hunt 8, A26-1).
        var classSessions = HandicapStore.SessionsFor(_history, _rounds, raceClass.Name).ToList();

        foreach (var driver in roster)
        {
            var handicap = _store.Get(driver.Transponder);

            var recentBest = classSessions
                .SelectMany(s => s.Results.Where(r =>
                    string.Equals(r.Transponder, driver.Transponder, StringComparison.OrdinalIgnoreCase)
                    && r.FastLapMs > 0))
                .Take(5)
                .Select(r => r.FastLapMs / 1000.0)
                .ToList();

            var rowIndex = _grid.Rows.Add(
                string.IsNullOrWhiteSpace(driver.DriverName) ? driver.Transponder : driver.DriverName,
                driver.Transponder,
                handicap is { TimeCreditSeconds: not 0 } ? $"-{handicap.TimeCreditSeconds:0.0}s" : "-",
                handicap is { LapCredit: not 0 } ? $"+{handicap.LapCredit}" : "-",
                handicap?.Locked == true ? "yes" : "",
                recentBest.Count > 0 ? $"{recentBest.Average():0.000}s" : "-",
                handicap?.Notes ?? string.Empty);

            _grid.Rows[rowIndex].Tag = driver.Transponder;

            if (handicap?.HasAny == true)
            {
                _grid.Rows[rowIndex].Cells[2].Style.ForeColor = AppTheme.Success;
                _grid.Rows[rowIndex].Cells[3].Style.ForeColor = AppTheme.Success;
            }
        }

        _lblStatus.Text = $"{Plural.Of(roster.Count, "driver")} in this class, "
                          + $"{roster.Count(d => _store.Get(d.Transponder)?.HasAny == true)} with a handicap.";
    }


    private void SelectMode(HandicapMode mode)
    {
        for (var i = 0; i < _cmbMode.Items.Count; i++)
        {
            if (_cmbMode.Items[i] is ModeItem item && item.Mode == mode)
            {
                if (_cmbMode.SelectedIndex != i)
                {
                    _cmbMode.SelectedIndex = i;
                }
                return;
            }
        }
    }

    private sealed record ModeItem(HandicapMode Mode, string Label)
    {
        public override string ToString() => Label;
    }

    private void EditSelected()
    {
        if (SelectedTransponder is not { } transponder)
        {
            AppDialog.Show(this, "Select a driver first.", "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // The dialog writes onto whatever it is given, so it is given a
        // copy: handed the stored handicap, it changed it before the undo
        // could note the old values (and before Save, so Cancel didn't
        // quite cancel either).
        var before = _store.CopiesFor(new[] { transponder });
        var existing = _store.Get(transponder) is { } stored
            ? HandicapStore.CopyOf(stored)
            : new DriverHandicap { Transponder = transponder };
        var profile = _profiles.Get(transponder);
        var who = profile?.DriverName is { Length: > 0 } name ? name : transponder;

        using var form = new HandicapEditForm(existing, profile?.DriverName ?? transponder,
            SelectedClassName is { } cn ? _store.ModeFor(cn) : HandicapMode.None);
        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        // The store, not this page, in the undo: the page may be gone by
        // then, and re-reads the store when it is next shown.
        var store = _store;
        _undo.Capture($"Edit {who}'s handicap", () => store.PutBack(before));

        _store.Upsert(form.Result);
        RefreshGrid();
    }

    private void ClearSelected()
    {
        if (SelectedTransponder is not { } transponder)
        {
            return;
        }

        var store = _store;
        var before = _store.CopiesFor(new[] { transponder });
        var who = _profiles.Get(transponder)?.DriverName is { Length: > 0 } name ? name : transponder;
        _undo.Capture($"Clear {who}'s handicap", () => store.PutBack(before));

        _store.Delete(transponder);
        RefreshGrid();
    }

    private void ClearAllInClass()
    {
        var raceClass = SelectedClass;
        if (raceClass is null)
        {
            return;
        }

        var roster = _profiles.AllRaceable
            .Where(p => p.RunsClass(raceClass.Name))
            .ToList();

        var withHandicap = roster.Count(p => _store.Get(p.Transponder)?.HasAny == true);
        if (withHandicap == 0)
        {
            AppDialog.Show(this, "No drivers in this class have a handicap.", "Nothing to clear",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (AppDialog.Show(this,
                $"Clear handicaps for all {Plural.Of(withHandicap, "driver")} in {raceClass.Name}?\r\n\r\n"
                + "Locked handicaps are cleared too.",
                "Clear handicaps", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
        {
            return;
        }

        var store = _store;
        var before = _store.CopiesFor(roster.Select(p => p.Transponder));
        _undo.Capture($"Clear {Plural.Of(withHandicap, "handicap")} in {raceClass.Name}", () => store.PutBack(before));

        foreach (var driver in roster)
        {
            _store.Delete(driver.Transponder);
        }

        RefreshGrid();
    }

    private void SuggestFromHistory()
    {
        var raceClass = SelectedClass;
        if (raceClass is null)
        {
            return;
        }

        var roster = _profiles.AllRaceable
            .Where(p => p.RunsClass(raceClass.Name))
            .ToList();

        using var form = new HandicapSuggestForm(_store, roster, _history, raceClass.Name, _rounds);
        if (form.ShowDialog(this) != DialogResult.OK || form.Accepted.Count == 0)
        {
            return;
        }

        var store = _store;
        var before = _store.CopiesFor(form.Accepted.Select(a => a.Transponder));
        _undo.Capture($"Apply {Plural.Of(form.Accepted.Count, "suggested handicap")} to {raceClass.Name}", () => store.PutBack(before));

        foreach (var suggestion in form.Accepted)
        {
            var existing = _store.Get(suggestion.Transponder) ?? new DriverHandicap { Transponder = suggestion.Transponder };
            existing.TimeCreditSeconds = suggestion.SuggestedSeconds;
            existing.Notes = $"Suggested from {Plural.Of(suggestion.SessionsUsed, "session")}, avg best {suggestion.AverageBestLapSeconds:0.000}s";
            _store.Upsert(existing);
        }

        RefreshGrid();
        _lblStatus.Text = $"Applied {Plural.Of(form.Accepted.Count, "suggested handicap")}.";
    }
}
