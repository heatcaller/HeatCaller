namespace HeatCaller;

/// <summary>
/// Shows a driver's personal-best lap time per track, separate from the
/// single all-time track record - "your own best ever here", which matters
/// most for someone tracking their own progress over many sessions rather
/// than chasing the single fastest lap anyone has ever set.
///
/// 13 Nov 2026, the Race Engineer rework (see RaceCoach): each PB now says
/// how real it is. A headline names the one that is real pace and the one
/// that is soft; the table puts a pill on each (Solid, In reach, One-off,
/// Fresh, from RaceCoach.ReadPb), dots for the recent races that came near
/// it, the gap to the track record in seconds and car lengths, and what it
/// means in a sentence; and a "Try this" closes the page. The resets are
/// where they were, under the table.
/// </summary>
public sealed class PersonalBestsControl : UserControl
{
    /// <summary>The track layouts, for a layout's length - the car lengths beside each gap (RaceCoach). Null says no car lengths.</summary>
    public TrackLayoutStore? Layouts { get; set; }

    /// <summary>
    /// The driver profiles, for the record rule (rentals and excluded guests
    /// hold no records - AchievementEvaluator.CanHoldTrackRecord). Null
    /// leaves only ghosts out, the rule's own answer with no profiles.
    /// </summary>
    public ProfileStore? Profiles { get; set; }

    private readonly SessionHistoryStore _history;
    private readonly PersonalBestResetStore _resets;
    private readonly UndoService _undo;
    private readonly ComboBox _cmbDriverFilter = new();
    private readonly DataGridView _grid = new();
    private readonly Label _lblSummary = new();
    private readonly Label _headline;
    private readonly DrillNote _drill = new();

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

        _headline = CoachPieces.Headline(_textSize);

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

    /// <summary>The headline sentence - for the tests.</summary>
    internal string HeadlineText => _headline.Text;

    /// <summary>The "Try this" advice, or empty - for the tests.</summary>
    internal string DrillText => _drill.Visible ? _drill.Advice : string.Empty;

    /// <summary>The table - for the tests.</summary>
    internal DataGridView Grid => _grid;

    /// <summary>Picks a driver, as the picker would - for the tests.</summary>
    internal void SelectDriver(string driver)
    {
        var index = _cmbDriverFilter.Items.IndexOf(driver);

        if (index >= 0)
        {
            _cmbDriverFilter.SelectedIndex = index;
        }
    }

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
    /// The driver picker with the count beside it, the headline, then one
    /// card: the bests taking the height that is left, and the reset buttons
    /// with what resetting does under them; the "Try this" under the card.
    /// On the layout pieces (LayoutPieces.cs), so nothing is placed by hand.
    /// </summary>
    private void BuildLayout()
    {
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

        // A track's name is text: an '&' in it is not a mnemonic.
        _headline.UseMnemonic = false;
        _drill.UseMnemonic = false;

        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.AllowUserToResizeRows = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;

        // "What it means" wraps, and each row is as tall as it needs.
        _grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells;

        _grid.Columns.AddRange(
            // The driver is the picker's; the column stays, hidden, for the resets (SelectedRow).
            new DataGridViewTextBoxColumn { Name = "driver", HeaderText = "Driver", Visible = false },
            new DataGridViewTextBoxColumn { Name = "track", HeaderText = "Track", FillWeight = 80 },
            new DataGridViewTextBoxColumn { Name = "best", HeaderText = "PB", FillWeight = 45 },
            new DataGridViewTextBoxColumn { Name = "real", HeaderText = "How real is it?", FillWeight = 60 },
            new DataGridViewTextBoxColumn { Name = "near", HeaderText = "Races near it (last 5)", FillWeight = 75 },
            new DataGridViewTextBoxColumn { Name = "gap", HeaderText = "Gap to the record", FillWeight = 100 },
            new DataGridViewTextBoxColumn { Name = "means", HeaderText = "What it means", FillWeight = 180 }
        );

        _grid.Columns["means"]!.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _grid.Columns["gap"]!.DefaultCellStyle.WrapMode = DataGridViewTriState.True;
        _grid.CellPainting += PaintReading;

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
            Text = "A reset ignores laps set before now when working out a best. Race history, results, records and season points "
                 + "are untouched. Clear Reset brings the full history back.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _textSize.Wear(lblResetHint, () => AppTheme.SmallFont);

        var bests = new Section("Personal bests");

        bests.AddFill(_grid, () => _grid.ColumnHeadersHeight + 3 * _grid.RowTemplate.Height);
        bests.Add(new ButtonBar().With(btnResetTrack, btnResetDriver, btnClearReset), gapBelow: UiLayout.HintGap);
        bests.Add(lblResetHint, gapBelow: 0);

        var page = new FillPage();

        page.Add(filterRow, gapBelow: UiLayout.RowGap);
        page.Add(_headline, gapBelow: UiLayout.RowGap);
        page.AddFill(bests, bests.LeastHeight, gapBelow: UiLayout.RowGap);
        page.Add(_drill, gapBelow: 0);

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
            if (session.IsNotScored() || string.IsNullOrWhiteSpace(session.TrackLayoutName))
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
        foreach (var d in drivers)
        {
            _cmbDriverFilter.Items.Add(d);
        }

        // The longest name may have come or gone: measured again, and the
        // line with it.
        _driverWidth = null;
        _filterRow?.PerformLayout();

        // One driver at a time: the headline and the "Try this" are said to
        // them. The one picked before, when they are still on the list.
        var idx = previous is not null ? _cmbDriverFilter.Items.IndexOf(previous) : -1;

        if (_cmbDriverFilter.Items.Count == 0)
        {
            RefreshGrid();
        }
        else
        {
            _cmbDriverFilter.SelectedIndex = idx >= 0 ? idx : 0;
        }
    }

    /// <summary>
    /// The reading painted on a row as a pill and dots (PaintReading). It
    /// rides in the row's Tag, not by row index, so a header-click sort keeps
    /// each pill on its own track. A row waiting after a reset has none.
    /// </summary>
    internal RaceCoach.PbRead? ReadingAt(int rowIndex) =>
        rowIndex >= 0 && rowIndex < _grid.Rows.Count ? _grid.Rows[rowIndex].Tag as RaceCoach.PbRead : null;

    private void RefreshGrid()
    {
        var driver = _cmbDriverFilter.SelectedItem as string;
        var everything = _history.All.Where(s => !s.IsNotScored()).ToList();

        var bests = driver is null
            ? new List<BestRecord>()
            : ComputeBests()
                .Where(b => string.Equals(b.Driver, driver, StringComparison.OrdinalIgnoreCase))
                .OrderBy(b => b.Track, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        _grid.Rows.Clear();

        var lines = new List<RaceCoach.PbLine>();

        foreach (var b in bests)
        {
            var cutoff = _resets.EffectiveCutoff(b.Driver, b.Track);

            // A row kept only so its reset can be cleared: no lap since the
            // reset, so nothing to read.
            if (b.IsWaitingAfterReset)
            {
                _grid.Rows.Add(b.Driver, b.Track, "-", string.Empty, string.Empty, "-",
                    cutoff is { } since
                        ? $"No laps here since the reset on {since.ToLocalTime():d MMM}."
                        : "No laps here since the reset.");
                continue;
            }

            var races = RaceCoach.RacesAt(everything, b.Driver, b.Track, cutoff);
            var read = RaceCoach.ReadPb(b.BestLapMs, b.SetAtUtc, races);
            var means = read.Meaning + (cutoff is { } cut ? $" Counting since the reset on {cut.ToLocalTime():d MMM}." : string.Empty);

            var row = _grid.Rows.Add(
                b.Driver,
                b.Track,
                PaceAnalysis.Time(b.BestLapMs),
                read.Label,
                $"{read.Near} of {RaceCoach.PbWindow}",
                GapToRecord(everything, b),
                means);

            _grid.Rows[row].Tag = read;
            lines.Add(new RaceCoach.PbLine(b.Track, b.BestLapMs, b.SetAtUtc, read));
        }

        _headline.Text = driver is null
            ? "No personal bests yet. Race a lap on any track and they start here."
            : RaceCoach.PbHeadline(lines);

        _drill.Say(RaceCoach.PbDrill(lines));

        // Only real bests are counted - a row waiting for a lap after its
        // reset is not a personal best on record.
        var count = bests.Count(b => !b.IsWaitingAfterReset);
        _lblSummary.Text = $"{count} personal best{(count == 1 ? "" : "s")} on record";
    }

    /// <summary>
    /// How far a PB is off the track record, in seconds and, when the
    /// layout has a length, car lengths: "+0.263s (7 car lengths)". The
    /// record is the Overview's (RaceCoach.RecordLaps).
    /// </summary>
    private string GapToRecord(IReadOnlyList<SessionRecord> everything, BestRecord best)
    {
        var record = RaceCoach.RecordLaps(
                everything.Where(s => string.Equals(s.TrackLayoutName.Trim(), best.Track.Trim(), StringComparison.OrdinalIgnoreCase)),
                Profiles)
            .FirstOrDefault();

        if (record.Result is null)
        {
            return "-";
        }

        var gap = best.BestLapMs - record.Result.FastLapMs;

        if (gap <= 0)
        {
            return "You hold the record";
        }

        var lengths = RaceCoach.SayLengths(gap, record.Result.FastLapMs, RaceCoach.TrackCars(Layouts, best.Track));

        return $"+{RaceCoach.Gap(gap)}" + (lengths is null ? string.Empty : $" ({lengths})");
    }

    /// <summary>
    /// The reading drawn rather than written: "How real is it?" as a pill in
    /// its colour, and "Races near it" as five dots, the near ones filled.
    /// The cell's text stays for a screen reader and the tests.
    /// </summary>
    private void PaintReading(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.Graphics is null || ReadingAt(e.RowIndex) is not { } read)
        {
            return;
        }

        var column = _grid.Columns[e.ColumnIndex].Name;

        if (column != "real" && column != "near")
        {
            return;
        }

        e.PaintBackground(e.CellBounds, (e.State & DataGridViewElementStates.Selected) != 0);

        var cell = Rectangle.Inflate(e.CellBounds, -UiLayout.AtDpi(4), 0);

        if (column == "real")
        {
            var middle = new Rectangle(cell.Left, e.CellBounds.Top + (e.CellBounds.Height - (AppTheme.SmallFont.Height + UiLayout.AtDpi(14))) / 2,
                cell.Width, AppTheme.SmallFont.Height + UiLayout.AtDpi(14));

            RosterInk.DrawChip(e.Graphics, middle, read.Label, Tone(read.Reality), AppTheme.SmallFont, dot: false);
        }
        else
        {
            var side = UiLayout.AtTextSize(11);
            var gap = UiLayout.AtTextSize(4);
            var top = e.CellBounds.Top + (e.CellBounds.Height - side) / 2;
            var smoothing = e.Graphics.SmoothingMode;

            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            using var near = new SolidBrush(AppTheme.Success);
            using var far = new SolidBrush(RosterInk.Blend(AppTheme.TextMuted, AppTheme.Surface, 0.75f));

            for (var k = 0; k < RaceCoach.PbWindow; k++)
            {
                e.Graphics.FillEllipse(k < read.Near ? near : far, cell.Left + k * (side + gap), top, side, side);
            }

            e.Graphics.SmoothingMode = smoothing;
        }

        e.Paint(e.CellBounds, DataGridViewPaintParts.Border);
        e.Handled = true;
    }

    /// <summary>A reading's colour: good for solid, warning for in reach, bad for a one-off, quiet for fresh.</summary>
    private static Color Tone(RaceCoach.PbReality reality) => reality switch
    {
        RaceCoach.PbReality.Solid => AppTheme.Success,
        RaceCoach.PbReality.InReach => AppTheme.WarningDark,
        RaceCoach.PbReality.OneOff => AppTheme.Danger,
        _ => AppTheme.TextMuted,
    };
}
