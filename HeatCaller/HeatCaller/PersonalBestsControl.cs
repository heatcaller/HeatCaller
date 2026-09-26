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
        MinimumSize = new Size(860, 520);
        Font = AppTheme.BodyFont;

        var lblIntro = new Label
        {
            Text = "Each driver's best-ever lap time per track, across every saved session - separate from the single all-time track " +
                   "record on the Track Layouts page, since this tracks your own progress rather than the fastest lap anyone's set.",
            Location = new Point(12, 12),
            AutoSize = true,
            MaximumSize = new Size(820, 0),
            ForeColor = AppTheme.TextMuted,
        };

        // Measured off the caption - see UiLayout.After.
        var lblFilter = new Label { Text = "Driver:", Location = new Point(12, 66), AutoSize = true };
        _cmbDriverFilter.Width = 220;
        _cmbDriverFilter.Location = new Point(UiLayout.After(lblFilter), 63);
        _cmbDriverFilter.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbDriverFilter.SelectedIndexChanged += (_, _) => RefreshGrid();

        // After the box rather than at a flat 300. The caption before it is
        // measured, so the box no longer starts where 300 assumed it did.
        _lblSummary.Location = new Point(_cmbDriverFilter.Right + UiLayout.SectionGap, 66);
        _lblSummary.AutoSize = true;
        _lblSummary.ForeColor = AppTheme.TextMuted;

        _grid.Location = new Point(12, 94);
        _grid.Size = new Size(820, 340);
        _grid.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
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

        var btnResetTrack = new Button { Text = "Reset This Track", Location = new Point(12, 446), Width = 140 };
        btnResetTrack.Click += (_, _) => ResetSelected(allTracks: false);

        var btnResetDriver = new Button { Text = "Reset All Tracks", Location = new Point(160, 446), Width = 140 };
        btnResetDriver.Click += (_, _) => ResetSelected(allTracks: true);

        var btnClearReset = new Button { Text = "Clear Reset", Location = new Point(308, 446), Width = 120 };
        btnClearReset.Click += (_, _) => ClearSelectedReset();

        var lblResetHint = new Label
        {
            Text = "Resetting ignores laps set before now when working out a best - it never deletes race history, "
                 + "and results, records and season points are untouched. Clear Reset brings the full history back.",
            Location = new Point(438, 444),
            AutoSize = true,
            MaximumSize = new Size(394, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.SmallFont,
        };

        Controls.Add(btnResetTrack);
        Controls.Add(btnResetDriver);
        Controls.Add(btnClearReset);
        Controls.Add(lblResetHint);
        Controls.Add(lblIntro);
        Controls.Add(lblFilter);
        Controls.Add(_cmbDriverFilter);
        Controls.Add(_lblSummary);
        Controls.Add(_grid);

        RefreshAll();
    }


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

    private sealed record BestRecord(string Driver, string Track, long BestLapMs, string Car, DateTime SetAtUtc);

    private List<BestRecord> ComputeBests()
    {
        var bests = new Dictionary<(string Driver, string Track), BestRecord>();

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
                var cutoff = _resets.EffectiveCutoff(result.DriverName, session.TrackLayoutName);
                if (cutoff is not null && session.StartedAtUtc <= cutoff)
                {
                    continue;
                }

                // Case-blind, like the driver list built from these. "Sam" in
                // one session and "sam" in another are one driver on one
                // track, and keyed as two they each kept a best - the list
                // showed the driver once but the grid showed them twice,
                // with the slower one presented as a personal best.
                var key = (result.DriverName.ToUpperInvariant(), session.TrackLayoutName.ToUpperInvariant());
                if (!bests.TryGetValue(key, out var existing) || result.FastLapMs < existing.BestLapMs)
                {
                    bests[key] = new BestRecord(result.DriverName, session.TrackLayoutName, result.FastLapMs, result.CarModel, session.StartedAtUtc);
                }
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
            _grid.Rows.Add(
                b.Driver,
                b.Track,
                $"{b.BestLapMs / 1000.0:0.000}s",
                string.IsNullOrWhiteSpace(b.Car) ? "-" : b.Car,
                b.SetAtUtc.ToLocalTime().ToString("d"),
                _resets.EffectiveCutoff(b.Driver, b.Track) is { } cut
                    ? cut.ToLocalTime().ToString("d")
                    : "all history");
        }

        _lblSummary.Text = $"{bests.Count} personal best{(bests.Count == 1 ? "" : "s")} on record";
    }
}
