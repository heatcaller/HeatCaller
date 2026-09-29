namespace HeatCaller;

/// <summary>
/// Assigns each driver a distinct lap cue.
///
/// The problem this solves: with several cars circulating, a single shared
/// chime tells you a lap happened but not whose - useless to someone
/// driving. A tone per driver means they can hear their own lap without
/// looking up.
/// </summary>
public sealed class LapBeepControl : UserControl
{
    private readonly LapBeepStore _store;
    private readonly ProfileStore _profiles;
    private readonly SoundEffectsStore _sounds;
    private readonly UndoService _undo;

    private readonly CheckBox _chkEnabled = new();
    private readonly DataGridView _grid = new();
    private readonly Label _lblStatus = new();

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>The fewest drivers the list shows before the page scrolls instead.</summary>
    private const int GridLeastRows = 3;

    public LapBeepControl(LapBeepStore store, ProfileStore profiles, SoundEffectsStore sounds, UndoService undo)
    {
        _store = store;
        _profiles = profiles;
        _sounds = sounds;
        _undo = undo;

        Dock = DockStyle.Fill;
        ForeColor = AppTheme.TextDark;

        // The body face, worn rather than set once, so it follows the text
        // size - see TextSize. Pages in tabs otherwise wear the tab row's
        // bold face.
        //
        // No design size and no MinimumSize: the 900x540 floor was honoured
        // over the size the page was given, and a small window lost the
        // buttons off the bottom. A short window scrolls the page instead
        // (FillPage).
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout();
        RefreshAll();

        // Undo changes the cues in the store, never on this page - see
        // OnStoreRestored.
        _store.Restored += OnStoreRestored;
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

    private void OnStoreRestored()
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        RefreshGrid();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _store.Restored -= OnStoreRestored;
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Puts an undo on the stack that gives these drivers back the cues they
    /// have now (or none, for a driver who has none). Call BEFORE changing
    /// them. Only these drivers are touched when it runs, so it never undoes
    /// a cue set on anyone else since - the whole-file snapshot it replaces
    /// did.
    /// </summary>
    private void CaptureCues(string description, IEnumerable<string> transponders)
    {
        var before = new Dictionary<string, DriverLapBeep?>(StringComparer.OrdinalIgnoreCase);

        foreach (var transponder in transponders)
        {
            before[transponder] = _store.Get(transponder) is { } beep ? LapBeepStore.Copy(beep) : null;
        }

        var store = _store;
        _undo.Capture(description, () =>
        {
            store.PutBack(before);
            store.NotifyRestored();
        });
    }

    private string DriverName(string transponder) =>
        _profiles.AllRaceable.FirstOrDefault(p => string.Equals(p.Transponder, transponder, StringComparison.OrdinalIgnoreCase))?.DriverName
        ?? transponder;

    /// <summary>
    /// The intro, the switch with what it means under it, then one card: the
    /// drivers and their cues taking the height that is left, the buttons
    /// under them and the status line. On the layout pieces
    /// (LayoutPieces.cs), so nothing is placed by hand.
    ///
    /// It used to be placed in pixels: the switch's note at x=230 whatever
    /// the switch's caption came to, the grid 956x368 whatever the window,
    /// the buttons placed from its bottom, and a 900x540 floor the page kept
    /// however small its window - every face set once, so none of it
    /// followed the text size.
    /// </summary>
    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "Gives each driver their own short cue when they cross the line, so they can hear their lap without "
                 + "watching the screen. Cues differ in shape as well as pitch - pitch alone is hard to pick out over track noise.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _chkEnabled.Text = "Per-driver lap cues ON";
        _chkEnabled.AutoSize = true;
        _textSize.Wear(_chkEnabled, () => AppTheme.BoldFont);
        _chkEnabled.Checked = _store.Enabled;
        _chkEnabled.CheckedChanged += (_, _) =>
        {
            _store.Enabled = _chkEnabled.Checked;
            UpdateEnabledLook();
        };

        // Under the switch rather than beside it, wrapping to the room it
        // has - beside it, it sat at a fixed x the switch's caption could
        // grow past.
        var lblEnabledHint = new Label
        {
            Text = "Off by default - a cue per lap per driver is a lot of noise if you didn't ask for it.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _textSize.Wear(lblEnabledHint, () => AppTheme.SmallFont);

        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "driver", HeaderText = "Driver", FillWeight = 90 },
            new DataGridViewTextBoxColumn { Name = "transponder", HeaderText = "Transponder", FillWeight = 60 },
            new DataGridViewTextBoxColumn { Name = "class", HeaderText = "Class", FillWeight = 60 },
            new DataGridViewTextBoxColumn { Name = "tone", HeaderText = "Cue", FillWeight = 70 },
            new DataGridViewTextBoxColumn { Name = "state", HeaderText = "", FillWeight = 40 }
        );
        _grid.CellDoubleClick += (_, args) => { if (args.RowIndex >= 0) { CycleTone(); } };

        // Every heading whole, at the text size in use - see Pieces.KeepHeadingsWhole.
        _textSize.Keep(() => Pieces.KeepHeadingsWhole(_grid));

        var btnTone = new Button { Text = "Change Cue", };
        btnTone.Click += (_, _) => CycleTone();

        var btnTest = new Button { Text = "Test Cue", };
        btnTest.Click += (_, _) => TestSelected();

        var btnToggle = new Button { Text = "Turn On / Off", };
        btnToggle.Click += (_, _) => ToggleSelected();

        var btnAuto = new Button { Text = "Auto-Assign All", };
        btnAuto.Click += (_, _) => AutoAssign();

        var btnClear = new Button { Text = "Clear All", };
        btnClear.Click += (_, _) => ClearAll();

        // How many drivers have a cue, and whether any share one - wrapping
        // rather than cut short.
        _lblStatus.AutoSize = true;
        _lblStatus.ForeColor = AppTheme.TextMuted;

        var drivers = new Section("Drivers");

        drivers.AddFill(_grid, () => _grid.ColumnHeadersHeight + GridLeastRows * _grid.RowTemplate.Height + 2);
        drivers.Add(new ButtonBar().With(btnTone, btnTest, btnToggle, btnAuto, btnClear));
        drivers.Add(_lblStatus, gapBelow: 0);

        var page = new FillPage();

        page.Add(lblIntro, gapBelow: UiLayout.RowGap);
        page.Add(_chkEnabled, stretch: false, gapBelow: UiLayout.HintGap);
        page.Add(lblEnabledHint);
        page.AddFill(drivers, drivers.LeastHeight, gapBelow: 0);

        Controls.Add(page);

        UpdateEnabledLook();
    }

    private string? SelectedTransponder => _grid.CurrentRow?.Tag as string;

    private void UpdateEnabledLook()
    {
        _chkEnabled.Text = _store.Enabled ? "Per-driver lap cues ON" : "Per-driver lap cues OFF";
        _chkEnabled.ForeColor = _store.Enabled ? AppTheme.Success : AppTheme.TextMuted;
    }

    public void RefreshAll()
    {
        _chkEnabled.Checked = _store.Enabled;
        UpdateEnabledLook();
        RefreshGrid();
    }

    private void RefreshGrid()
    {
        var selected = SelectedTransponder;
        _grid.Rows.Clear();

        var drivers = _profiles.AllRaceable.OrderBy(p => p.DriverName).ToList();

        foreach (var driver in drivers)
        {
            var beep = _store.Get(driver.Transponder);
            var assigned = beep is not null;

            var rowIndex = _grid.Rows.Add(
                string.IsNullOrWhiteSpace(driver.DriverName) ? driver.Transponder : driver.DriverName,
                driver.Transponder,
                string.IsNullOrWhiteSpace(driver.ClassName) ? "-" : driver.ClassName,
                assigned ? LapBeepStore.ToneNames[Math.Clamp(beep!.ToneIndex, 0, LapBeepStore.ToneNames.Length - 1)] : "-",
                assigned ? (beep!.Enabled ? "on" : "off") : string.Empty);

            _grid.Rows[rowIndex].Tag = driver.Transponder;

            if (assigned && beep!.Enabled)
            {
                _grid.Rows[rowIndex].Cells[3].Style.ForeColor = AppTheme.Success;
            }
            else if (assigned)
            {
                _grid.Rows[rowIndex].DefaultCellStyle.ForeColor = AppTheme.TextMuted;
            }

            if (selected is not null && string.Equals(selected, driver.Transponder, StringComparison.OrdinalIgnoreCase))
            {
                _grid.Rows[rowIndex].Selected = true;
                _grid.CurrentCell = _grid.Rows[rowIndex].Cells[0];
            }
        }

        // Two drivers sharing a cue defeats the point, so it's called out
        // rather than left for someone to notice mid-race.
        var duplicates = _store.All
            .Where(b => b.Enabled)
            .GroupBy(b => b.ToneIndex)
            .Where(g => g.Count() > 1)
            .ToList();

        var assignedCount = _store.All.Count(b => b.Enabled);

        // Hoisted out of the line below so the verb can agree with it
        // without summing the same groups twice.
        var sharing = duplicates.Sum(g => g.Count());

        _lblStatus.Text = duplicates.Count > 0
            ? $"{Plural.Of(assignedCount, "driver")} with a cue - but {sharing} {(sharing == 1 ? "shares" : "share")} a cue with someone else."
            : $"{assignedCount} of {Plural.Of(drivers.Count, "driver")} {(assignedCount == 1 ? "has" : "have")} a cue.";

        _lblStatus.ForeColor = duplicates.Count > 0 ? AppTheme.Accent : AppTheme.TextMuted;
    }

    /// <summary>Steps to the next cue and plays it, so the choice is made by ear rather than by name.</summary>
    private void CycleTone()
    {
        if (SelectedTransponder is not { } transponder)
        {
            AppDialog.Show(this, "Select a driver first.", "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        CaptureCues($"Change lap cue for {DriverName(transponder)}", new[] { transponder });

        var beep = _store.Get(transponder) ?? new DriverLapBeep { Transponder = transponder, ToneIndex = -1 };
        beep.ToneIndex = (beep.ToneIndex + 1) % LapBeepStore.ToneNames.Length;
        beep.Enabled = true;

        _store.Upsert(beep);
        RefreshGrid();

        _ = ToneGenerator.PlayDriverLapAsync(beep.ToneIndex, (float)_sounds.Current.Volume);
    }

    private void TestSelected()
    {
        if (SelectedTransponder is not { } transponder)
        {
            return;
        }

        var beep = _store.Get(transponder);
        if (beep is null)
        {
            AppDialog.Show(this, "This driver has no cue yet - use Change Cue to give them one.", "No cue",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _ = ToneGenerator.PlayDriverLapAsync(beep.ToneIndex, (float)_sounds.Current.Volume);
    }

    private void ToggleSelected()
    {
        if (SelectedTransponder is not { } transponder || _store.Get(transponder) is not { } beep)
        {
            return;
        }

        CaptureCues($"Turn lap cue {(beep.Enabled ? "off" : "on")} for {DriverName(transponder)}", new[] { transponder });

        beep.Enabled = !beep.Enabled;
        _store.Upsert(beep);
        RefreshGrid();
    }

    private void AutoAssign()
    {
        var drivers = _profiles.AllRaceable.OrderBy(p => p.DriverName).Select(p => p.Transponder).ToList();

        if (drivers.Count == 0)
        {
            return;
        }

        if (drivers.Count > LapBeepStore.ToneNames.Length
            && AppDialog.Show(this,
                $"There are {drivers.Count} drivers and only {LapBeepStore.ToneNames.Length} cues, so some will share.\r\n\r\n"
                + "That's usually fine if the sharing drivers aren't on track together - assign those by hand afterwards.\r\n\r\nContinue?",
                "More drivers than cues", MessageBoxButtons.YesNo, MessageBoxIcon.Information) != DialogResult.Yes)
        {
            return;
        }

        CaptureCues($"Auto-assign lap cues ({Plural.Of(drivers.Count, "driver")})", drivers);

        _store.AutoAssign(drivers);
        RefreshGrid();
    }

    private void ClearAll()
    {
        if (_store.All.Count == 0)
        {
            return;
        }

        if (AppDialog.Show(this, $"Remove cues from all {Plural.Of(_store.All.Count, "driver")}?", "Clear cues",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        CaptureCues($"Clear lap cues ({Plural.Of(_store.All.Count, "driver")})", _store.All.Select(b => b.Transponder).ToList());

        foreach (var beep in _store.All)
        {
            _store.Delete(beep.Transponder);
        }

        RefreshGrid();
    }
}
