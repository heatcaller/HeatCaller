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

    private readonly ComboBox _cmbClass = new();
    private readonly DataGridView _grid = new();
    private readonly ComboBox _cmbMode = new();
    private readonly Label _lblMode = new();
    private readonly Label _lblStatus = new();

    // The page re-measures itself on every resize - see LayoutPage - so
    // everything it has to place is a field rather than a local.
    private readonly Label _lblIntro = new();
    private readonly Label _lblClass = new();
    private readonly Label _lblModeCaption = new();
    private Button[] _actions = Array.Empty<Button>();

    public HandicapControl(HandicapStore store, ProfileStore profiles, RaceClassStore classes,
        SessionHistoryStore history, UndoService undo)
    {
        _store = store;
        _profiles = profiles;
        _classes = classes;
        _history = history;
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
        _lblIntro.Text = "Handicaps even out a mixed-ability grid. They're applied when results are shown - recorded lap times, "
                       + "personal bests and track records are never altered.";
        _lblIntro.AutoSize = true;
        _lblIntro.ForeColor = AppTheme.TextMuted;

        // Explicit, because LayoutPage measures the row below off this
        // label's wrapped height - and a label measures at whatever font
        // it is carrying, not the one its parent will hand it later.
        _lblIntro.Font = AppTheme.BodyFont;
        _lblClass.Font = AppTheme.BodyFont;
        _lblModeCaption.Font = AppTheme.BodyFont;
        _cmbClass.Font = AppTheme.BodyFont;
        _cmbMode.Font = AppTheme.BodyFont;

        _lblClass.Text = "Class:";
        _lblClass.AutoSize = true;

        _cmbClass.Width = UiLayout.FieldWidth;
        _cmbClass.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbClass.SelectedIndexChanged += (_, _) => RefreshGrid();

        _lblModeCaption.Text = "Mode:";
        _lblModeCaption.AutoSize = true;

        _cmbMode.Width = 250;
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

        _lblMode.AutoSize = true;
        _lblMode.Font = AppTheme.BoldFont;

        // Top/Left/Right only - NOT Bottom. Bottom anchoring stretches the
        // grid downward on a taller window, painting over whatever sits
        // below it and making those controls invisible. LayoutPage sizes
        // it instead.
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

        _actions = new[] { btnEdit, btnSuggest, btnClear, btnClearAll };

        _lblStatus.AutoSize = false;
        _lblStatus.ForeColor = AppTheme.TextMuted;

        Controls.AddRange(new Control[]
        {
            _lblIntro, _lblClass, _cmbClass, _lblModeCaption, _cmbMode, _lblMode, _grid,
            btnEdit, btnSuggest, btnClear, btnClearAll, _lblStatus,
        });


        // This page places every one of its children on every resize, so
        // the repair pass is told to keep out - two things moving the same
        // control is worse than neither doing it. See
        // LayoutDoctor.LeaveAlone.
        LayoutDoctor.LeaveAlone(this);
        LayoutPage();
    }

    /// <summary>
    /// Measures the page top to bottom, on every resize.
    ///
    /// It used to be a column of literals: the class caption at x=12 with
    /// its picker at x=60, the mode caption at 320 and its picker at 368,
    /// the grid at y=92 exactly 372 tall, the buttons at y=476 and the
    /// status line at 516. Every one of those is correct at one text size.
    /// At a larger one "Class:" is wider than the 48px gutter it was given,
    /// so it sat on top of its own picker and the repair pass shoved the
    /// whole row sideways to separate them - which is what made this row
    /// look scrambled rather than merely tight.
    ///
    /// Nothing here is a remembered number. Captions are measured, the
    /// grid takes whatever is left between the row above and the buttons
    /// below, and the buttons are measured against their own text.
    /// </summary>
    private void LayoutPage()
    {
        if (_actions.Length == 0 || Width < 80)
        {
            return;
        }

        var right = Math.Max(320, Width - UiLayout.PageRight);
        var content = right - UiLayout.PageLeft;

        _lblIntro.MaximumSize = new Size(content, 0);
        _lblIntro.Location = new Point(UiLayout.PageLeft, UiLayout.PageTop);

        // --- The class / mode row ---
        var row = UiLayout.Below(_lblIntro, UiLayout.RowGap);
        var rowHeight = Math.Max(_cmbClass.Height, _cmbMode.Height);

        _lblClass.Location = new Point(UiLayout.PageLeft, UiLayout.CentreOn(_lblClass, row, rowHeight));
        _cmbClass.Location = new Point(UiLayout.After(_lblClass), UiLayout.CentreOn(_cmbClass, row, rowHeight));

        _lblModeCaption.Location = new Point(
            UiLayout.After(_cmbClass) + UiLayout.SectionGap,
            UiLayout.CentreOn(_lblModeCaption, row, rowHeight));

        _cmbMode.Location = new Point(UiLayout.After(_lblModeCaption), UiLayout.CentreOn(_cmbMode, row, rowHeight));

        _lblMode.Location = new Point(
            UiLayout.After(_cmbMode) + UiLayout.RowGap,
            UiLayout.CentreOn(_lblMode, row, rowHeight));

        // --- The buttons, measured and placed from the bottom up ---
        var x = UiLayout.PageLeft;

        foreach (var button in _actions)
        {
            var text = TextRenderer.MeasureText(
                button.Text,
                AppTheme.BoldFont,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            button.Size = new Size(text.Width + 26, UiLayout.ButtonHeight);
            x += button.Width + UiLayout.ButtonGap;
        }

        var statusHeight = AppTheme.BodyFont.Height + 4;
        var buttonsTop = Math.Max(
            UiLayout.Below(_cmbClass, UiLayout.SectionGap) + 120,
            Height - UiLayout.PageTop - statusHeight - UiLayout.RowGap - UiLayout.ButtonHeight);

        x = UiLayout.PageLeft;

        foreach (var button in _actions)
        {
            button.Location = new Point(x, buttonsTop);
            x += button.Width + UiLayout.ButtonGap;
        }

        _lblStatus.Location = new Point(UiLayout.PageLeft, UiLayout.Below(_actions[0], UiLayout.RowGap));
        _lblStatus.Size = new Size(content, statusHeight);

        // --- The grid fills what is left between them ---
        var gridTop = UiLayout.Below(_cmbClass, UiLayout.RowGap);

        _grid.Location = new Point(UiLayout.PageLeft, gridTop);
        _grid.Size = new Size(content, Math.Max(120, buttonsTop - UiLayout.RowGap - gridTop));
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutPage();
    }

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
            .Where(p => string.Equals(p.ClassName, raceClass.Name, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.DriverName)
            .ToList();

        foreach (var driver in roster)
        {
            var handicap = _store.Get(driver.Transponder);

            var recentBest = _history.All
                .Where(s => !s.IsDemoSession())
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
            .Where(p => string.Equals(p.ClassName, raceClass.Name, StringComparison.OrdinalIgnoreCase))
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
            .Where(p => string.Equals(p.ClassName, raceClass.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        using var form = new HandicapSuggestForm(_store, roster, _history, raceClass.Name);
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
