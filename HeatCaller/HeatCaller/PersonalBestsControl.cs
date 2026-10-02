namespace HeatCaller;

/// <summary>
/// Shows each driver's personal-best lap time per track, separate from the
/// single all-time track record tracked elsewhere - this is "your own best
/// ever here," which matters most for someone tracking their own progress
/// over many sessions rather than chasing the single fastest lap anyone's
/// ever set.
/// </summary>
public sealed class PersonalBestsControl : UserControl
{
    private readonly SessionHistoryStore _history;
    private readonly PersonalBestResetStore _resets;
    private readonly UndoService _undo;
    private readonly ComboBox _cmbDriverFilter = new();
    private readonly DataGridView _grid = new();
    private readonly Label _lblSummary = new();

    public PersonalBestsControl(SessionHistoryStore history, PersonalBestResetStore resets, UndoService undo)
    {
        _history = history;
        _resets = resets;
        _undo = undo;

        Dock = DockStyle.Fill;

        // The body face, worn rather than set once: this page sits in a tab
        // whose row is drawn in the bold face, which it would otherwise
        // take. No MinimumSize: the 860x520 floor was honoured over the size
        // the page was given, and a short window lost the reset buttons off
        // the bottom. A short window scrolls the page instead (FillPage).
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout();
        RefreshAll();
    }

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>The driver picker's width at the normal text size - it is never narrower, and is as wide as the longest name.</summary>
    private const int DriverPickerWidth = 220;

    /// <summary>The driver picker's width as last measured - see Pieces.PickerWidth. Forgotten when the list is refilled.</summary>
    private (Font Font, int Count, int Width)? _driverWidth;

    /// <summary>The picker and the summary beside it - held so a refilled list can re-measure the line.</summary>
    private FieldRow? _filterRow;

    /// <summary>The page's own face changed - handed the body face at a new text size - so everything else it keeps in step follows. See TextSize.</summary>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>A new text size, reaching this page through its tab's bold face - the only way it does, since the page wears a face of its own. See TextSize.</summary>
    protected override void OnParentFontChanged(EventArgs e)
    {
        base.OnParentFontChanged(e);
        _textSize.Apply();
    }

    /// <summary>
    /// The intro, then one card: the driver picker with the count beside it,
    /// the bests taking the height that is left, and the reset buttons with
    /// what resetting does under them. On the layout pieces
    /// (LayoutPieces.cs), so nothing is placed by hand.
    ///
    /// It used to be placed by hand over an 860x520 floor: the grid at a
    /// fixed 340px, the buttons at y=446 and their hint squeezed beside them
    /// into 394px.
    /// </summary>
    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "Each driver's best-ever lap time per track, across every saved session - separate from the single all-time track " +
                   "record on the Track Layouts page, since this tracks your own progress rather than the fastest lap anyone's set.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _cmbDriverFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbDriverFilter.SelectedIndexChanged += (_, _) => RefreshGrid();

        // How many bests, beside the picker - on a line of its own when it
        // no longer fits there (FieldRow).
        _lblSummary.AutoSize = false;
        _lblSummary.AutoEllipsis = true;
        _lblSummary.ForeColor = AppTheme.TextMuted;

        var filterRow = new FieldRow();

        filterRow.Add(new Label { Text = "Driver:" }, _cmbDriverFilter, DriverWidth);
        filterRow.AddLast(null, _lblSummary, () => _lblSummary.GetPreferredSize(Size.Empty).Width);
        _filterRow = filterRow;

        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "driver", HeaderText = "Driver", FillWeight = 90 },
            new DataGridViewTextBoxColumn { Name = "track", HeaderText = "Track", FillWeight = 90 },
            new DataGridViewTextBoxColumn { Name = "best", HeaderText = "Best Lap", FillWeight = 60 },
            new DataGridViewTextBoxColumn { Name = "car", HeaderText = "Car", FillWeight = 80 },
            new DataGridViewTextBoxColumn { Name = "date", HeaderText = "Date Set", FillWeight = 70 },
            new DataGridViewTextBoxColumn { Name = "since", HeaderText = "Counting Since", FillWeight = 80 }
        );

        // Every heading whole, at the text size in use - see Pieces.KeepHeadingsWhole.
        _textSize.Keep(() => Pieces.KeepHeadingsWhole(_grid));

        var btnResetTrack = new Button { Text = "Reset This Track" };
        btnResetTrack.Click += (_, _) => ResetSelected(allTracks: false);

        var btnResetDriver = new Button { Text = "Reset All Tracks" };
        btnResetDriver.Click += (_, _) => ResetSelected(allTracks: true);

        var btnClearReset = new Button { Text = "Clear Reset" };
        btnClearReset.Click += (_, _) => ClearSelectedReset();

        var lblResetHint = new Label
        {
            Text = "Resetting ignores laps set before now when working out a best - it never deletes race history, "
                 + "and results, records and season points are untouched. Clear Reset brings the full history back.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _textSize.Wear(lblResetHint, () => AppTheme.SmallFont);

        var bests = new Section("Personal bests");

        bests.Add(filterRow);
        bests.AddFill(_grid, () => _grid.ColumnHeadersHeight + 3 * _grid.RowTemplate.Height);
        bests.Add(new ButtonBar().With(btnResetTrack, btnResetDriver, btnClearReset), gapBelow: UiLayout.HintGap);
        bests.Add(lblResetHint, gapBelow: 0);

        var page = new FillPage();

        page.Add(lblIntro);
        page.AddFill(bests, bests.LeastHeight, gapBelow: 0);

        Controls.Add(page);
    }

    /// <summary>The driver picker: as wide as the longest name, and never under its width at the normal text size.</summary>
    private int DriverWidth() =>
        Math.Max(UiLayout.AtTextSize(DriverPickerWidth), Pieces.PickerWidth(_cmbDriverFilter, ref _driverWidth));

    private (string Driver, string Track)? SelectedRow()
    {
        if (_grid.CurrentRow is null || _grid.CurrentRow.Index < 0)
        {
            return null;
        }

        var driver = _grid.CurrentRow.Cells["driver"].Value?.ToString();
        var track = _grid.CurrentRow.Cells["track"].Value?.ToString();
        return string.IsNullOrWhiteSpace(driver) ? null : (driver, track ?? string.Empty);
    }

    private void ResetSelected(bool allTracks)
    {
        if (SelectedRow() is not { } row)
        {
            AppDialog.Show(this, "Select a row first.", "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var scope = allTracks ? "every track" : $"'{row.Track}'";
        if (AppDialog.Show(this,
                $"Reset {row.Driver}'s personal best on {scope}?\r\n\r\n"
                + "Race history is not deleted - laps set before now simply stop counting toward their best.",
                "Confirm reset", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
        {
            return;
        }

        // Every cutoff this driver has, not just this one - the undo then
        // restores their exact prior state whichever scopes were in play,
        // without touching anyone else's resets made since.
        var resets = _resets;
        var driver = row.Driver;
        var snapshot = resets.Snapshot();
        _undo.Capture($"Reset personal best for {row.Driver}", () =>
        {
            resets.RestoreForDriver(driver, snapshot);
            RefreshAfterUndo();
        });

        _resets.SetCutoff(row.Driver, allTracks ? string.Empty : row.Track, DateTime.UtcNow);
        RefreshAll();
    }

    private void ClearSelectedReset()
    {
        if (SelectedRow() is not { } row)
        {
            AppDialog.Show(this, "Select a row first.", "Nothing selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (!_resets.HasAnyFor(row.Driver))
        {
            AppDialog.Show(this, $"{row.Driver} has no reset in place - their full history already counts.",
                "No reset to clear", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var resets = _resets;
        var driver = row.Driver;
        var snapshot = resets.Snapshot();
        _undo.Capture($"Clear PB reset for {row.Driver}", () =>
        {
            resets.RestoreForDriver(driver, snapshot);
            RefreshAfterUndo();
        });

        _resets.ClearAllForDriver(row.Driver);
        RefreshAll();
    }

    /// <summary>Redraws after an undo, if the page is still there - the undo itself works on the store.</summary>
    private void RefreshAfterUndo()
    {
        if (!IsDisposed)
        {
            RefreshAll();
        }
    }

    /// <summary>
    /// One row of the page. A BestLapMs of zero is a driver and track whose
    /// laps all fall before a reset - see ComputeBests.
    /// </summary>
    private sealed record BestRecord(string Driver, string Track, long BestLapMs, string Car, DateTime SetAtUtc)
    {
        public bool IsWaitingAfterReset => BestLapMs <= 0;
    }

    private List<BestRecord> ComputeBests()
    {
        var bests = new Dictionary<(string Driver, string Track), BestRecord>();

        // Drivers and tracks with laps that a reset is hiding. Without these
        // a reset made every one of that driver's rows vanish until they
        // raced there again - and with no row to select, Clear Reset could
        // not be reached, so "Reset All Tracks" was permanent short of the
        // undo button. Each one left with no best since its reset gets a
        // row of its own (no lap, counting since the reset), so the driver
        // stays in the list and the reset can be cleared from the page.
        var hidden = new Dictionary<(string Driver, string Track), (string Driver, string Track)>();

        foreach (var session in _history.All)
        {
            // A saved demo race's lap times are invented, so it can never
            // set or beat anybody's personal best.
            if (session.IsDemoSession() || string.IsNullOrWhiteSpace(session.TrackLayoutName))
            {
                continue;
            }

            foreach (var result in session.Results)
            {
                // Ghost cars left out: older saved sessions still carry them,
                // and a ghost's lap is generated, so it can never be anyone's
                // personal best.
                if (result.FastLapMs <= 0 || string.IsNullOrWhiteSpace(result.DriverName)
                    || GhostService.IsGhost(result.Transponder))
                {
                    continue;
                }

                // A reset means "ignore everything before this point" for
                // this driver, so skip sessions at or before the cutoff.
                // Case-blind, like the driver list built from these. "Sam" in
                // one session and "sam" in another are one driver on one
                // track, and keyed as two they each kept a best - the list
                // showed the driver once but the grid showed them twice,
                // with the slower one presented as a personal best.
                var key = (result.DriverName.ToUpperInvariant(), session.TrackLayoutName.ToUpperInvariant());

                var cutoff = _resets.EffectiveCutoff(result.DriverName, session.TrackLayoutName);
                if (cutoff is not null && session.StartedAtUtc <= cutoff)
                {
                    hidden.TryAdd(key, (result.DriverName, session.TrackLayoutName));
                    continue;
                }
                if (!bests.TryGetValue(key, out var existing) || result.FastLapMs < existing.BestLapMs)
                {
                    bests[key] = new BestRecord(result.DriverName, session.TrackLayoutName, result.FastLapMs, result.CarModel, session.StartedAtUtc);
                }
            }
        }

        foreach (var (key, names) in hidden)
        {
            if (!bests.ContainsKey(key))
            {
                bests[key] = new BestRecord(names.Driver, names.Track, 0, string.Empty, DateTime.MinValue);
            }
        }

        return bests.Values.ToList();
    }

    /// <summary>Call whenever this page becomes visible - session history can change elsewhere while it's not active.</summary>
    public void RefreshAll()
    {
        var previous = _cmbDriverFilter.SelectedItem as string;
        var drivers = ComputeBests().Select(b => b.Driver).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(d => d).ToList();

        _cmbDriverFilter.Items.Clear();
        _cmbDriverFilter.Items.Add("(all drivers)");
        foreach (var d in drivers)
        {
            _cmbDriverFilter.Items.Add(d);
        }

        // The longest name may have come or gone: measured again, and the
        // line with it.
        _driverWidth = null;
        _filterRow?.PerformLayout();

        var idx = previous is not null ? _cmbDriverFilter.Items.IndexOf(previous) : -1;
        _cmbDriverFilter.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private void RefreshGrid()
    {
        var driverFilter = _cmbDriverFilter.SelectedIndex > 0 ? _cmbDriverFilter.SelectedItem as string : null;
        var bests = ComputeBests()
            .Where(b => driverFilter is null || string.Equals(b.Driver, driverFilter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(b => b.Driver).ThenBy(b => b.Track)
            .ToList();

        _grid.Rows.Clear();
        foreach (var b in bests)
        {
            // A row kept only so its reset can be cleared: no lap since the
            // reset, so no best, car or date to show.
            _grid.Rows.Add(
                b.Driver,
                b.Track,
                b.IsWaitingAfterReset ? "-" : $"{b.BestLapMs / 1000.0:0.000}s",
                string.IsNullOrWhiteSpace(b.Car) ? "-" : b.Car,
                b.IsWaitingAfterReset ? "-" : b.SetAtUtc.ToLocalTime().ToString("d"),
                _resets.EffectiveCutoff(b.Driver, b.Track) is { } cut
                    ? cut.ToLocalTime().ToString("d")
                    : "all history");
        }

        // Only real bests are counted - a row waiting for a lap after its
        // reset is not a personal best on record.
        var count = bests.Count(b => !b.IsWaitingAfterReset);
        _lblSummary.Text = $"{count} personal best{(count == 1 ? "" : "s")} on record";
    }
}
