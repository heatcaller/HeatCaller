namespace HeatCaller;

/// <summary>
/// The live leaderboard: drawing it, lap details and the lap popup, the
/// right-click menu, and changing a driver's laps, penalties and name.
///
/// Moved here unchanged from RaceControlControl.cs on 26 Sep 2026, when the
/// file was split into parts. Fields with initializers stayed there.
/// </summary>
public sealed partial class RaceControlControl
{
    private void RenderLeaderboard()
    {
        if (IsDisposed || Disposing)
        {
            return; // window is closing/closed - nothing to render into
        }

        var ordered = _race.GetOrderedStandings();

        // Before the early return below, so an open lap breakdown still
        // updates - including to "no longer in the standings" - when the
        // board itself has nothing to draw.
        RefreshLapPopup(ordered);

        // Remembered before the rebuild below throws the rows away.
        //
        // Clearing and refilling a DataGridView makes it set CurrentCell to
        // the first cell, which with FullRowSelect selects P1's whole row -
        // and a selected row paints in the SELECTION colours, which override
        // the per-cell ForeColor the best-lap highlight is made of. So every
        // crossing re-selected P1 and, whenever P1 held the fastest lap, the
        // yellow vanished. Restoring the real selection afterwards (or none)
        // is what keeps it visible.
        var selectedTransponder = SelectedTransponder();

        _dgvLeaderboard.Rows.Clear();

        if (ordered.Count == 0)
        {
            return;
        }

        _kpiTracker.Observe(ordered, _race.SessionGeneration);

        var context = new KpiContext
        {
            Ordered = ordered,
            Profiles = _profiles,
            Tracker = _kpiTracker,
            FastestOverall = ordered.Where(s => s.FastestLapMs > 0).Select(s => s.FastestLapMs).DefaultIfEmpty(0).Min(),
            StreakThreshold = _race.StreakThreshold,
            Achievements = _achievements,

            // Null outside a qualifier, which is what makes the Qual column
            // show a dash rather than a wrong number during a main.
            Projection = _projector?.Current()?.Slots,
        };

        for (var i = 0; i < ordered.Count; i++)
        {
            var s = ordered[i];
            var values = _kpiColumns.Select(k => k.Compute(s, i, context)).ToList();

            var rowIndex = _dgvLeaderboard.Rows.Add(values.Select(v => (object)v.Text).ToArray());
            _dgvLeaderboard.Rows[rowIndex].Tag = s.TransponderId;

            // Jump starters stay marked for the whole race, whether or not
            // a penalty was applied - the race director needs to see it at
            // a glance, not just catch the announcement when it happened.
            if (_race.HasJumpStarted(s.TransponderId))
            {
                // A tint rather than full Danger, so the row still reads
                // as data instead of becoming a solid red block.
                _dgvLeaderboard.Rows[rowIndex].DefaultCellStyle.BackColor =
                    Color.FromArgb(70, AppTheme.Danger.R, AppTheme.Danger.G, AppTheme.Danger.B);
            }

            // A staggered or hand-released car that hasn't gone yet. Greyed
            // rather than hidden: on a hand-released start these rows are
            // the only way to see - and right-click - who is still waiting.
            if (_race.State == RaceState.Racing && !_race.IsReleased(s.TransponderId))
            {
                _dgvLeaderboard.Rows[rowIndex].DefaultCellStyle.ForeColor = AppTheme.TextMuted;
            }

            for (var col = 0; col < values.Count; col++)
            {
                if (values[col].Emphasis == KpiEmphasis.None)
                {
                    continue;
                }

                var emphasisColor = values[col].Emphasis switch
                {
                    KpiEmphasis.Good => AppTheme.Success,
                    KpiEmphasis.Bad => AppTheme.Danger,
                    KpiEmphasis.Highlight => AppTheme.Accent,
                    _ => _dgvLeaderboard.DefaultCellStyle.ForeColor,
                };

                var cell = _dgvLeaderboard.Rows[rowIndex].Cells[col];
                cell.Style.ForeColor = emphasisColor;

                // Set on the selection colour too, or clicking a driver -
                // or the grid selecting one by itself - repaints the row in
                // the selection palette and the emphasis disappears on that
                // row alone. The best-lap yellow has to survive being
                // selected; it means the same thing either way.
                cell.Style.SelectionForeColor = emphasisColor;
            }

            if (_expandedTransponders.Contains(s.TransponderId))
            {
                AddLapDetailRow(s);
            }
        }

        RestoreLeaderboardSelection(selectedTransponder);
    }

    /// <summary>
    /// Puts the selection back on the driver who had it before the grid was
    /// rebuilt, or leaves nothing selected when nobody did.
    ///
    /// Leaving nothing selected is the important half: without it the grid
    /// selects P1 on every single crossing, which is both a selection the
    /// user never made and the reason the best-lap highlight kept vanishing.
    /// </summary>
    private void RestoreLeaderboardSelection(string? transponder)
    {
        if (!string.IsNullOrEmpty(transponder))
        {
            foreach (DataGridViewRow row in _dgvLeaderboard.Rows)
            {
                // A driver's own row, never the lap strip underneath it -
                // that one carries the same id behind a prefix.
                if (row.Tag is string tag
                    && !tag.StartsWith(DetailRowPrefix, StringComparison.Ordinal)
                    && string.Equals(tag, transponder, StringComparison.OrdinalIgnoreCase)
                    && row.Cells.Count > 0)
                {
                    // CurrentCell, not Selected: SelectedTransponder reads
                    // CurrentRow, and setting the cell selects the row too.
                    _dgvLeaderboard.CurrentCell = row.Cells[0];
                    return;
                }
            }
        }

        // Either nothing was selected, or that driver is no longer on the
        // board - clear rather than leave the grid's own guess standing.
        _dgvLeaderboard.CurrentCell = null;
        _dgvLeaderboard.ClearSelection();
    }

    /// <summary>
    /// The lap-by-lap strip that opens under a driver on double-click.
    ///
    /// One lap per column, oldest on the left, so it reads the way the race
    /// happened. A star marks a lap that was part of a clean run at or past
    /// the streak threshold; a broken circle marks the lap where a run
    /// ended, which is the one worth looking at.
    /// </summary>
    private void AddLapDetailRow(RacerStanding standing)
    {
        var columns = _dgvLeaderboard.Columns.Count;

        if (columns < 2)
        {
            return;
        }

        var lapCount = standing.LapTimesMs.Count;

        if (lapCount == 0)
        {
            var emptyCells = new object[columns];
            emptyCells[0] = "No laps yet";
            FinishLapDetailRow(standing, emptyCells, 26);
            return;
        }

        var best = standing.FastestLapMs;
        // The floor lives on the property now, so this is whatever the
        // announcer would call a streak at.
        var threshold = _race.StreakThreshold;

        // Each lap is one entry in a single run of text rather than one
        // value per column. Spreading them across the leaderboard's columns
        // made every lap line up under an unrelated heading - a lap time
        // sitting under "Gap" reads as a gap - and the uneven column widths
        // left ragged holes between them.
        var entries = new List<string>(lapCount);
        var detail = new List<string>(lapCount);

        for (var lapIndex = 0; lapIndex < lapCount; lapIndex++)
        {
            var lapMs = standing.LapTimesMs[lapIndex];

            // The streak count recorded against this lap. Older sessions -
            // and any lap added by hand - have no entry, so the marker is
            // simply left off rather than guessed at.
            var streak = lapIndex < standing.StreakAtLap.Count ? standing.StreakAtLap[lapIndex] : -1;
            var previousStreak = lapIndex > 0 && lapIndex - 1 < standing.StreakAtLap.Count ? standing.StreakAtLap[lapIndex - 1] : -1;

            var brokeARun = streak == 1 && previousStreak >= threshold;
            var inARun = streak >= threshold;

            var marker = brokeARun ? "\u25CC" : inARun ? "\u2605" : string.Empty;

            // The first counted pass runs from the green to the line, so it
            // is not a lap time and is labelled as what it is.
            var isStartLap = lapIndex == 0;

            entries.Add(isStartLap
                ? $"start {lapMs / 1000.0:0.000}"
                : $"L{lapIndex + 1} {lapMs / 1000.0:0.000}{marker}");

            // Spelled out rather than done with a three-part numeric
            // format: the "zero" section of one of those is read as format
            // characters, and "best" contains an 'e', which would come back
            // as scientific notation.
            var deltaToBest = string.Empty;

            if (best > 0 && !isStartLap)
            {
                var delta = (lapMs - best) / 1000.0;
                deltaToBest = delta <= 0 ? ", their best" : $", +{delta:0.000} off their best";
            }

            detail.Add(isStartLap
                ? $"Green flag to the line: {lapMs / 1000.0:0.000}s. Not a lap time, so it never counts as a best lap."
                : $"Lap {lapIndex + 1}: {lapMs / 1000.0:0.000}s{deltaToBest}"
                  + (streak >= 0 ? $" - clean run of {streak}{(inARun ? " (counting)" : string.Empty)}" : string.Empty)
                  + (brokeARun ? " - this lap ended the previous run" : string.Empty));
        }

        // The laps WRAP across up to three lines rather than being cut to
        // whatever fits on one. A single line held about four laps on a
        // typical window, which is not a lap history.
        var available = Math.Max(160, _dgvLeaderboard.ClientSize.Width - LapDetailPadding * 2);

        var shown = entries.Count;
        var text = string.Join(LapDetailSeparator, entries);
        var height = MeasureLapDetail(text, available);

        // Too many laps to show even wrapped: drop the oldest until it fits,
        // because the recent end is the one being read.
        while (shown > 1 && height > MaxLapDetailHeight)
        {
            shown--;
            text = string.Join(LapDetailSeparator, entries.Skip(entries.Count - shown));
            height = MeasureLapDetail(text, available);
        }

        var cells = new object[columns];
        cells[0] = text;

        FinishLapDetailRow(standing, cells, Math.Clamp(height + 10, 26, MaxLapDetailHeight + 10));
    }

    private const int MaxLapDetailHeight = 78;

    private static int MeasureLapDetail(string text, int available) =>
        TextRenderer.MeasureText(text, AppTheme.BodyFont, new Size(available, int.MaxValue), LapDetailFormat).Height;

    private const TextFormatFlags LapDetailFormat =
        TextFormatFlags.WordBreak | TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPrefix;

    /// <summary>
    /// Shared tail of <see cref="AddLapDetailRow"/> - adds the row and tags
    /// it so the rest of the page can tell a detail row from a driver row.
    /// No pop-up: it only repeated the laps already painted across the row.
    /// </summary>
    private void FinishLapDetailRow(RacerStanding standing, object[] cells, int height)
    {
        var detailIndex = _dgvLeaderboard.Rows.Add(cells);
        var row = _dgvLeaderboard.Rows[detailIndex];

        row.Tag = DetailRowPrefix + standing.TransponderId;
        row.DefaultCellStyle.BackColor = AppTheme.Surface;
        row.DefaultCellStyle.ForeColor = AppTheme.TextMuted;
        row.DefaultCellStyle.Font = AppTheme.BodyFont;
        row.Height = height;
    }

    private const int LapDetailPadding = 10;
    private const string LapDetailSeparator = "   ";

    /// <summary>
    /// Paints a lap-detail row as one left-justified line spanning the whole
    /// grid instead of one value per column.
    ///
    /// DataGridView has no row spanning, so the text is drawn from the first
    /// cell with the clip widened to the full row - the graphics handed to
    /// CellPainting is clipped to the grid's data area, not to the cell, so
    /// widening it is allowed as long as it is restored afterwards.
    /// </summary>
    private void LeaderboardDetailPaint(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _dgvLeaderboard.Rows.Count || e.ColumnIndex < 0 || e.Graphics is not { } g)
        {
            return;
        }

        if (_dgvLeaderboard.Rows[e.RowIndex].Tag is not string tag
            || !tag.StartsWith(DetailRowPrefix, StringComparison.Ordinal))
        {
            return;
        }

        using (var back = new SolidBrush(AppTheme.Surface))
        {
            g.FillRectangle(back, e.CellBounds);
        }

        using (var rule = new Pen(AppTheme.Border))
        {
            g.DrawLine(rule, e.CellBounds.Left, e.CellBounds.Bottom - 1, e.CellBounds.Right, e.CellBounds.Bottom - 1);
        }

        if (e.ColumnIndex == 0)
        {
            var width = _dgvLeaderboard.Columns.GetColumnsWidth(DataGridViewElementStates.Visible);
            var span = new Rectangle(e.CellBounds.Left, e.CellBounds.Top, Math.Max(e.CellBounds.Width, width), e.CellBounds.Height);

            var saved = g.Save();
            g.SetClip(span);

            // Body weight in the normal text colour, not small-and-muted:
            // these are lap times, which is the one thing on the page worth
            // squinting at, and they were the hardest thing on it to read.
            var text = new Rectangle(
                span.Left + LapDetailPadding,
                span.Top + 5,
                Math.Max(20, span.Width - LapDetailPadding * 2),
                Math.Max(10, span.Height - 8));

            TextRenderer.DrawText(g, e.FormattedValue as string ?? string.Empty, AppTheme.BodyFont, text,
                AppTheme.TextDark, LapDetailFormat);

            g.Restore(saved);
        }

        e.Handled = true;
    }

    /// <summary>Rebuilds the leaderboard's columns from the saved KPI selection.</summary>
    private void RebuildLeaderboardColumns()
    {
        _kpiColumns = KpiCatalog.Resolve(_settings.RaceControlKpis, KpiCatalog.DefaultRaceControl);

        _dgvLeaderboard.Columns.Clear();
        foreach (var kpi in _kpiColumns)
        {
            _dgvLeaderboard.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = kpi.Key,
                HeaderText = kpi.Label,
                FillWeight = kpi.Weight,
            });
        }
    }

    /// <summary>The transponder of the currently-selected leaderboard row, or null if nothing is selected.</summary>
    private string? SelectedTransponder() =>
        TransponderForRow(_dgvLeaderboard.CurrentRow?.Index ?? -1);

    private void AdjustSelected(int laps, long penaltyMs)
    {
        var transponder = SelectedTransponder();
        if (transponder is null)
        {
            AppDialog.Show(this, "Select a racer in the leaderboard first.", "No racer selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Made through EditStanding so the app's Undo (the footer button and
        // Ctrl+Z) can take this one correction back - and only this one,
        // leaving any laps counted since on the board.
        var edit = _race.EditStanding(transponder, () =>
        {
            var changed = false;

            if (laps != 0)
            {
                changed |= _race.AdjustLaps(transponder, laps);
            }
            if (penaltyMs != 0)
            {
                changed |= _race.AdjustPenaltyTime(transponder, penaltyMs);
            }

            return changed;
        });

        if (edit is not null)
        {
            var what = laps > 0 ? (laps == 1 ? "Add a lap" : $"Add {laps} laps")
                : laps < 0 ? (laps == -1 ? "Remove a lap" : $"Remove {-laps} laps")
                : penaltyMs >= 0 ? $"{penaltyMs / 1000.0:0.#}s penalty"
                : $"{-penaltyMs / 1000.0:0.#}s credit";

            CaptureStandingEditUndo($"{what} for {DriverNameFor(transponder)}", edit);
        }

        RenderLeaderboard();
    }

    /// <summary>
    /// Puts a correction made by hand on the app's undo stack. Pushed just
    /// after the change rather than before, because EditStanding needs the
    /// driver's figures from both sides of it - nothing can happen in
    /// between, it is all on this thread. The undo works on the session,
    /// which lives as long as the app, and the board follows through the
    /// session's StandingAdjusted.
    /// </summary>
    private void CaptureStandingEditUndo(string description, RaceControlSession.StandingEdit edit) =>
        _undo.Capture(description, () => _race.UndoStandingEdit(edit));

    private void ClearSelectedPenalty()
    {
        var transponder = SelectedTransponder();
        if (transponder is null)
        {
            AppDialog.Show(this, "Select a racer in the leaderboard first.", "No racer selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var standing = _race.GetOrderedStandings().FirstOrDefault(s => s.TransponderId == transponder);

        // A jump-start penalty still owed counts as something to clear. It is
        // only booked into PenaltyMs on the driver's next counted lap, and
        // ClearPenalties drops it - but this used to return first whenever
        // PenaltyMs was zero, so a jumper credited with the start lap could
        // not be let off, and the penalty landed a lap later regardless.
        if (standing is null || (standing.PenaltyMs == 0 && _race.OwedJumpStartPenaltyMs(transponder) <= 0))
        {
            return;
        }

        // Through the session's own clear rather than a matching credit, so
        // the time noted on each warning goes too - see ClearPenalties. Undo
        // gives back the time, the warnings' notes and any jump-start
        // penalty still owed.
        var edit = _race.EditStanding(transponder, () =>
        {
            _race.ClearPenalties(transponder);
            return true;
        });

        if (edit is not null)
        {
            CaptureStandingEditUndo($"Clear penalties for {DriverNameFor(transponder)}", edit);
        }

        RenderLeaderboard();
    }

    /// <summary>
    /// Notes a transponder nobody has a profile for, so it can be named
    /// once the racing stops - instead of leaving a bare hex id sitting in
    /// the leaderboard for somebody to notice and fix later.
    ///
    /// It goes on the list behind the banner (UnknownTransponders), which
    /// shows above the board between races and on Driver Hub, with the
    /// sidebar's count. Nothing modal: this is called from the lap pipeline,
    /// and a dialog here once let the race finish behind a question nobody
    /// had answered - see the history in UnknownTransponders.
    /// </summary>
    private void NoteUnknownTransponder(string transponderId)
    {
        if (_promptedTransponders.Contains(transponderId) || _profiles.Get(transponderId) is not null)
        {
            return;
        }

        // A demo's placeholder cars (DEMO01...) are nobody's and never will
        // be - nothing to ask about.
        if (_race.IsDemoMode)
        {
            return;
        }

        _promptedTransponders.Add(transponderId);
        _unnamedTransponders.Add(transponderId);

        string where;

        try
        {
            where = RaceInHandTitle();
        }
        catch (Exception)
        {
            where = string.Empty;
        }

        _unknowns?.Note(transponderId, where);

        StatusMessage?.Invoke(
            $"{AppTime.LogStampNow()}  [race] New transponder {transponderId} is on track - it's listed above the board once the session ends, to say whose it is.");
    }

    /// <summary>
    /// Once the racing has stopped: what each unrecognised transponder did
    /// this session, for its line on the banner - and the banner shown.
    ///
    /// Always reached through BeginInvoke, never straight from an event:
    /// the thing that ends a race is usually a lap, and the standings are
    /// final once it is off the stack.
    /// </summary>
    private void NoteUnnamedTransponderLaps()
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (_race.State is RaceState.Racing or RaceState.Staging or RaceState.Countdown)
        {
            return; // a new session started while this was queued
        }

        var pending = _unnamedTransponders.ToList();
        _unnamedTransponders.Clear();

        foreach (var transponderId in pending)
        {
            if (FindStanding(transponderId) is { } standing)
            {
                _unknowns?.SetLaps(transponderId, standing.Laps, standing.FastestLapMs);
            }
        }

        _unknownBanner.RefreshVisibility();
    }

    /// <summary>Draws the board again - after a transponder was given a driver from outside this page.</summary>
    public void RefreshNames() => RenderLeaderboard();

    /// <summary>
    /// The drivers whose lap detail is open beneath their row. Survives the
    /// grid being rebuilt on every lap.
    ///
    /// A set rather than a single id, so several drivers can be open at
    /// once - comparing two or three people's recent laps side by side is
    /// the whole reason to open the strip, and having each new one close the
    /// last made that impossible.
    ///
    /// Case-insensitive to match every other transponder comparison in this
    /// file; ids arrive from the decoder with inconsistent casing.
    ///
    /// Ids are kept even for drivers not currently in the standings - a
    /// transponder that drops out and comes back reopens where it was, and a
    /// stale id simply never matches.
    /// </summary>
    private readonly HashSet<string> _expandedTransponders = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Double-click opens the driver's lap breakdown rather than the profile
    /// editor - during a race the laps are what you want, and editing a
    /// profile mid-race is the rarer job. That's on the right-click menu.
    /// </summary>
    private void DgvLeaderboard_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _dgvLeaderboard.Rows.Count)
        {
            return;
        }

        var transponder = TransponderForRow(e.RowIndex);

        if (string.IsNullOrEmpty(transponder))
        {
            return;
        }

        ShowLapPopup(transponder, e.RowIndex);
    }

    /// <summary>
    /// Opens or closes one driver's inline lap strip, leaving every other
    /// open strip exactly as it was.
    ///
    /// Still here, on the right-click menu, because the strip and the popup
    /// answer different questions: the strip is for keeping two or three
    /// drivers' recent laps visible next to each other on the board, the
    /// popup is for reading one driver's race properly.
    /// </summary>
    private void ToggleLapDetail(string transponder)
    {
        if (!_expandedTransponders.Remove(transponder))
        {
            _expandedTransponders.Add(transponder);
        }

        RenderLeaderboard();
    }

    /// <summary>
    /// The floating lap breakdown, when one is open. One at a time - a
    /// second popup over the first would cover the board it is describing.
    /// </summary>
    private LapDetailPopup? _lapPopup;

    /// <summary>
    /// Opens the custom-drawn lap breakdown for a driver, anchored under
    /// their row so it reads as belonging to that row rather than appearing
    /// wherever the pointer happened to be.
    /// </summary>
    private void ShowLapPopup(string transponder, int rowIndex)
    {
        CloseLapPopup();

        var profile = _profiles.Get(transponder);
        var name = string.IsNullOrWhiteSpace(profile?.DriverName) ? transponder : profile!.DriverName;

        // Whatever of the car, number and class is actually filled in -
        // an empty profile should not produce a line of stray separators.
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(profile?.RaceNumber))
        {
            parts.Add($"#{profile!.RaceNumber.Trim()}");
        }

        // The car this row's transponder is fitted to, or their active car.
        if (_profiles.CarModelFor(transponder) is { Length: > 0 } car && car.Trim().Length > 0)
        {
            parts.Add(car.Trim());
        }

        if (!string.IsNullOrWhiteSpace(profile?.ClassName))
        {
            parts.Add(profile!.ClassName.Trim());
        }

        parts.Add(transponder);

        var popup = new LapDetailPopup(transponder, name, string.Join("  ·  ", parts), _race.StreakThreshold);

        popup.Closed += (_, _) =>
        {
            // Only the popup closing ITSELF - a click elsewhere, Escape -
            // gets here still current. CloseLapPopup clears the field first
            // and disposes the popup itself.
            if (!ReferenceEquals(_lapPopup, popup))
            {
                return;
            }

            _lapPopup = null;

            // A drop-down is a window of its own, and an auto-closed one was
            // never disposed - one leaked per double-click all evening.
            // Posted rather than disposed here, because this handler runs
            // inside the popup's own closing.
            if (IsHandleCreated && !IsDisposed && !Disposing)
            {
                BeginInvoke(new Action(() =>
                {
                    if (!popup.IsDisposed)
                    {
                        popup.Dispose();
                    }
                }));
            }
        };

        _lapPopup = popup;

        RefreshLapPopup(_race.GetOrderedStandings());

        // Under the driver's own row, left-aligned with the grid. Falls back
        // to the top of the grid when the row can't be located - which can
        // happen if the board rebuilt between the click and here.
        var y = 0;

        try
        {
            if (rowIndex >= 0 && rowIndex < _dgvLeaderboard.Rows.Count)
            {
                var rect = _dgvLeaderboard.GetRowDisplayRectangle(rowIndex, false);
                y = rect.Bottom;
            }
        }
        catch
        {
            // Row scrolled out from under us - the fallback position is fine.
        }

        popup.Show(_dgvLeaderboard, new Point(0, y));
    }

    private void CloseLapPopup()
    {
        if (_lapPopup is not { } popup)
        {
            return;
        }

        _lapPopup = null;

        popup.Close();
        popup.Dispose();
    }

    /// <summary>
    /// Feeds the open popup the driver's current standing on every board
    /// refresh, so it updates lap by lap instead of freezing on whatever was
    /// true when it was opened.
    /// </summary>
    private void RefreshLapPopup(IReadOnlyList<RacerStanding> ordered)
    {
        if (_lapPopup is not { } popup)
        {
            return;
        }

        RacerStanding? standing = null;
        var position = 0;

        for (var i = 0; i < ordered.Count; i++)
        {
            if (string.Equals(ordered[i].TransponderId, popup.Transponder, StringComparison.OrdinalIgnoreCase))
            {
                standing = ordered[i];
                position = i + 1;
                break;
            }
        }

        popup.Apply(standing, position);
    }

    /// <summary>
    /// The driver a row belongs to - the driver's own row, or the lap detail
    /// sitting under it, which carries the same id prefixed so it can be
    /// told apart.
    /// </summary>
    private string? TransponderForRow(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _dgvLeaderboard.Rows.Count)
        {
            return null;
        }

        return _dgvLeaderboard.Rows[rowIndex].Tag switch
        {
            string tag when tag.StartsWith(DetailRowPrefix, StringComparison.Ordinal) => tag[DetailRowPrefix.Length..],
            string tag => tag,
            _ => null,
        };
    }

    private const string DetailRowPrefix = "detail:";

    private ContextMenuStrip BuildLeaderboardMenu()
    {
        var menu = new ContextMenuStrip();

        var editDriver = new ToolStripMenuItem("Edit driver...");
        editDriver.Click += (_, _) => EditSelectedDriver();

        // For a transponder nobody has: give it to a driver already on file,
        // rather than making a new one - a second car, a spare, a borrowed
        // transponder. The same as Belongs To on the banner.
        var giveToDriver = new ToolStripMenuItem("Give to an existing driver...");
        giveToDriver.Click += (_, _) =>
        {
            if (SelectedTransponder() is { Length: > 0 } transponder)
            {
                _unknowns?.Request(transponder, UnknownTransponders.Choice.BelongsTo);
            }
        };

        var lapBreakdown = new ToolStripMenuItem("Lap breakdown...");
        lapBreakdown.Click += (_, _) =>
        {
            if (SelectedTransponder() is { Length: > 0 } transponder)
            {
                ShowLapPopup(transponder, _dgvLeaderboard.CurrentRow?.Index ?? -1);
            }
        };

        var toggleLaps = new ToolStripMenuItem("Show recent laps");
        toggleLaps.Click += (_, _) =>
        {
            var transponder = SelectedTransponder();

            if (transponder is null)
            {
                return;
            }

            ToggleLapDetail(transponder);
        };

        // Only worth offering once more than one strip is open - see the
        // Opening handler, which hides it otherwise.
        var hideAllLaps = new ToolStripMenuItem("Hide all lap strips");
        hideAllLaps.Click += (_, _) =>
        {
            _expandedTransponders.Clear();
            RenderLeaderboard();
        };

        var releaseDriver = new ToolStripMenuItem("Release");
        releaseDriver.Click += (_, _) =>
        {
            if (SelectedTransponder() is { Length: > 0 } transponder)
            {
                _race.ReleaseDriver(transponder);
                RenderLeaderboard();
            }
        };

        var releaseAll = new ToolStripMenuItem("Release everyone still waiting");
        releaseAll.Click += (_, _) =>
        {
            _race.ReleaseAll();
            RenderLeaderboard();
        };

        var addLap = new ToolStripMenuItem("Add a lap (+1)");
        addLap.Click += (_, _) => AdjustSelected(1, 0);

        var removeLap = new ToolStripMenuItem("Remove a lap (-1)");
        removeLap.Click += (_, _) => AdjustSelected(-1, 0);

        var penaltyFive = new ToolStripMenuItem("Penalty +5s");
        penaltyFive.Click += (_, _) => AdjustSelected(0, 5000);

        var penaltyTen = new ToolStripMenuItem("Penalty +10s");
        penaltyTen.Click += (_, _) => AdjustSelected(0, 10000);

        var penaltyCustom = new ToolStripMenuItem("Penalty...");
        penaltyCustom.Click += (_, _) => PenaltySelectedByHand();

        var clearPenalty = new ToolStripMenuItem("Clear penalty");
        clearPenalty.Click += (_, _) => ClearSelectedPenalty();

        // Filled each time the menu opens, for the driver under the pointer
        // - see FillWarningItems. The placeholder stops WinForms treating
        // it as a plain item with no arrow before the first fill.
        var warn = new ToolStripMenuItem("Warning");
        warn.DropDownItems.Add(new ToolStripMenuItem("..."));

        var excludeFromRecords = new ToolStripMenuItem("Exclude from records") { CheckOnClick = false };
        excludeFromRecords.Click += (_, _) => ToggleExcludeFromRecords();

        var copyId = new ToolStripMenuItem("Copy transponder ID");
        copyId.Click += (_, _) =>
        {
            if (SelectedTransponder() is { Length: > 0 } transponder)
            {
                // Clipboard access throws when another app has it open -
                // never worth an error dialog over a copy.
                try { Clipboard.SetText(transponder); } catch { }
            }
        };

        menu.Items.AddRange(new ToolStripItem[]
        {
            editDriver,
            giveToDriver,
            lapBreakdown,
            toggleLaps,
            hideAllLaps,
            releaseDriver,
            releaseAll,
            new ToolStripSeparator(),
            addLap,
            removeLap,
            new ToolStripSeparator(),
            penaltyFive,
            penaltyTen,
            penaltyCustom,
            clearPenalty,
            new ToolStripSeparator(),
            warn,
            new ToolStripSeparator(),
            excludeFromRecords,
            copyId,
        });

        menu.Opening += (_, e) =>
        {
            var transponder = SelectedTransponder();

            if (transponder is null)
            {
                e.Cancel = true;
                return;
            }

            var profile = _profiles.Get(transponder);
            var name = string.IsNullOrWhiteSpace(profile?.DriverName) ? transponder : profile!.DriverName;

            editDriver.Text = profile is null ? $"New driver on {transponder}..." : $"Edit {name}...";
            giveToDriver.Visible = profile is null && _unknowns is not null && !GhostService.IsGhost(transponder);
            giveToDriver.Text = $"Give {transponder} to an existing driver...";

            lapBreakdown.Text = $"{name}'s lap breakdown...";

            toggleLaps.Text = _expandedTransponders.Contains(transponder)
                ? $"Hide {name}'s laps on the board"
                : $"Show {name}'s laps on the board";

            // Shown only when it would actually do something more than the
            // item above it.
            hideAllLaps.Visible = _expandedTransponders.Count > 1;
            hideAllLaps.Text = $"Hide all {_expandedTransponders.Count} lap strips";

            // Releasing is only ever a hand-released thing, and only while
            // the race is actually running - a scheduled stagger releases
            // itself, and there is nothing to release before the green.
            var canRelease = _race.StartType == RaceStartType.HandReleased
                             && _race.State == RaceState.Racing;

            releaseDriver.Visible = canRelease && !_race.IsReleased(transponder);
            releaseDriver.Text = $"Release {name} now";

            releaseAll.Visible = canRelease;

            var standing = _race.GetOrderedStandings().FirstOrDefault(st =>
                string.Equals(st.TransponderId, transponder, StringComparison.OrdinalIgnoreCase));

            // A jump-start penalty not yet booked can be cleared too - see
            // ClearSelectedPenalty.
            var owedJumpStartMs = standing is null ? 0 : _race.OwedJumpStartPenaltyMs(standing.TransponderId);

            clearPenalty.Enabled = standing is { PenaltyMs: not 0 } || owedJumpStartMs > 0;
            clearPenalty.Text = standing is { PenaltyMs: not 0 }
                ? $"Clear penalty ({standing.PenaltyMs / 1000.0:0.0}s)"
                : owedJumpStartMs > 0
                    ? $"Clear penalty (jump start, {owedJumpStartMs / 1000.0:0.0}s still to come)"
                    : "Clear penalty";

            excludeFromRecords.Enabled = profile is not null;
            excludeFromRecords.Checked = profile?.ExcludeFromRecords == true;

            var warnings = standing?.Warnings.Count ?? 0;
            warn.Text = warnings == 0 ? $"Warn {name}" : $"Warn {name} ({Plural.Of(warnings, "warning")} so far)";
            warn.Enabled = standing is not null;
            FillWarningItems(warn.DropDownItems, transponder);
        };

        // Drawn by AppTheme rather than by Windows. Without this the menu is
        // the one piece of the race screen still wearing stock grey chrome,
        // and it is the piece you look at while deciding to give somebody a
        // penalty.
        AppTheme.StyleContextMenu(menu);

        return menu;
    }

    /// <summary>
    /// Assigns a driver profile to the EXACT transponder ID this row is
    /// using, taken directly from the live decoder rather than typed by
    /// hand. This matters specifically for the direct-decoder path: the
    /// decoder reports the raw 3-byte hex ID (e.g. "8467BE"), which is a
    /// completely different value from the decimal ID Next Level Timing
    /// displays for the same physical transponder (e.g. "48743") - there's
    /// no reliable conversion between the two. A profile created against
    /// NLT's decimal ID will never match here, silently, with no error -
    /// it just won't find a name. Assigning it from this exact row
    /// guarantees the key matches.
    /// </summary>
    private void EditSelectedDriver()
    {
        var transponder = SelectedTransponder();

        if (transponder is null)
        {
            return;
        }

        var existing = _profiles.Get(transponder);

        // The driver as on file before the form opens - the form may edit
        // the record in place - so Undo can put every field back.
        var before = existing is null ? null : System.Text.Json.JsonSerializer.Serialize(existing);

        using var form = new ProfileForm(existing, _raceClasses, fixedTransponder: transponder, undo: _undo, profiles: _profiles);

        if (form.ShowDialog(this) == DialogResult.OK)
        {
            var profiles = _profiles;
            var saved = form.Result;
            var after = System.Text.Json.JsonSerializer.Serialize(saved);
            var key = saved.Transponder;

            if (before != after)
            {
                _undo.Capture(before is null ? $"Add driver {saved.DriverName}" : $"Edit driver {saved.DriverName}", () =>
                {
                    // Only while the driver is still as this edit left them -
                    // otherwise a later edit would be thrown away with it.
                    var current = profiles.Get(key);

                    if (current is null || System.Text.Json.JsonSerializer.Serialize(current) != after)
                    {
                        throw new InvalidOperationException(
                            $"{saved.DriverName} has been changed or removed since, and undoing this now would undo that too. Nothing was changed.");
                    }

                    if (before is null)
                    {
                        profiles.Delete(key);
                    }
                    else if (System.Text.Json.JsonSerializer.Deserialize<DriverProfile>(before) is { } restored)
                    {
                        // The alt transponder the old details had may be
                        // somebody else's by now - one car, two owners.
                        if (profiles.DescribeClash(restored, editing: restored.Transponder) is { } clash)
                        {
                            throw new InvalidOperationException($"the old details can't come back - {clash} Nothing was changed.");
                        }

                        profiles.Upsert(restored);

                        // The form retires a replaced photo rather than
                        // deleting it when it has an undo, so it can come back.
                        if (!string.Equals(current.PhotoFileName, restored.PhotoFileName, StringComparison.OrdinalIgnoreCase))
                        {
                            DriverPhotos.Unretire(restored.PhotoFileName);
                            DriverPhotos.Retire(current.PhotoFileName);
                        }
                    }
                });
            }

            _profiles.Upsert(saved);
            RenderLeaderboard();
        }
    }

    /// <summary>Keeps a guest or a demo car out of the club's records without banning them from the race.</summary>
    private void ToggleExcludeFromRecords()
    {
        var transponder = SelectedTransponder();

        if (transponder is null || _profiles.Get(transponder) is not { } profile)
        {
            return;
        }

        // Only the flag goes back, onto the driver as on file then.
        var profiles = _profiles;
        var excluded = !profile.ExcludeFromRecords;

        _undo.Capture($"{(excluded ? "Exclude" : "Include")} {profile.DriverName} {(excluded ? "from" : "in")} records", () =>
        {
            if (profiles.Get(transponder) is { } current && current.ExcludeFromRecords == excluded)
            {
                current.ExcludeFromRecords = !excluded;
                profiles.Upsert(current);
            }
        });

        profile.ExcludeFromRecords = excluded;
        _profiles.Upsert(profile);

        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] {profile.DriverName} "
            + (profile.ExcludeFromRecords
                ? "excluded from track records and championship points (places close up behind them in the standings)."
                : "counts toward track records and championship points again."));
    }

    /// <summary>A penalty of any size, for the ones that aren't five or ten seconds.</summary>
    private void PenaltySelectedByHand()
    {
        if (SelectedTransponder() is null)
        {
            AppDialog.Show(this, "Select a racer in the leaderboard first.", "No racer selected", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new TimePenaltyForm();

        if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Seconds != 0)
        {
            AdjustSelected(0, (long)(dialog.Seconds * 1000));
        }
    }

    /// <summary>
    /// The window the leaderboard's Penalty... opens: how many seconds to
    /// add to the selected driver - or, less than nothing, to take off. A
    /// class of its own rather than built inside PenaltySelectedByHand, and
    /// internal, so the layout test can build it (WindowCatalog) without a
    /// race on the board.
    ///
    /// On the layout pieces (DialogFit): the caption and the seconds beside
    /// it on one line (FieldRow), and Apply and Cancel at the foot; as tall
    /// as that. It used to be placed in pixels in a 300x116 window: the box
    /// at a fixed 128px from the left beside a caption that a larger text
    /// size ran into, and the buttons a fixed 80px wide at fixed places.
    /// </summary>
    internal sealed class TimePenaltyForm : Form
    {
        /// <summary>
        /// How wide the window is inside its frame at the normal text size.
        /// Wider than the 300 it was made at: Apply and Cancel are standard
        /// buttons now, and two of them side by side need 316px between the
        /// page's margins - 348 in all. At 300, Apply would wrap onto a line
        /// of its own under Cancel.
        /// </summary>
        private const int DialogWidth = 360;

        /// <summary>The seconds box's width at the normal text size, as it was - wider if its widest value needs more.</summary>
        private const int SecondsWidth = 90;

        private readonly NumericUpDown _seconds = new()
        {
            DecimalPlaces = 1,
            Increment = 0.5m,
            Minimum = -600m,
            Maximum = 600m,
            Value = 5m,
        };

        /// <summary>The seconds to add - less than nothing takes time off. Only meaningful after DialogResult.OK.</summary>
        public decimal Seconds => _seconds.Value;

        public TimePenaltyForm()
        {
            Text = "Time penalty";
            BackColor = AppTheme.Background;
            Font = AppTheme.BodyFont;

            var ok = new Button { Text = "Apply", DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

            // A FieldRow rather than a field grid: it keeps the pixel round
            // the box that the theme draws its ring in (Pieces.RingRoom), top
            // and bottom as well - a grid's last row loses the bottom of it.
            var row = new FieldRow();
            row.Add(new Label { Text = "Seconds to add:" }, _seconds);

            var page = new FillPage();
            page.Add(row);

            // Against the right-hand edge, Cancel furthest right, as they
            // were - and Tab still reaches Apply first: the bar takes them
            // right to left, in the order they were added.
            page.Add(new ButtonBar(alignRight: true).With(cancel, ok), gapBelow: 0);
            ok.TabIndex = 0;
            cancel.TabIndex = 1;

            Controls.Add(page);

            AcceptButton = ok;
            CancelButton = cancel;

            AppTheme.ApplyRecursively(this);

            // After the theme pass, in the face it leaves the box in: as wide
            // as it was at the normal text size, at the size in use, and never
            // too narrow for its widest value, "-600.0", with its arrows.
            var widest = _seconds.Minimum.ToString("F" + _seconds.DecimalPlaces, System.Globalization.CultureInfo.CurrentCulture);

            _seconds.Width = Math.Max(
                UiLayout.AtTextSize(SecondsWidth),
                TextRenderer.MeasureText(widest, _seconds.Font).Width + SystemInformation.VerticalScrollBarWidth + 12 + UpDownButtonsSkin.Extra);

            DialogFit.Keep(this, page, width: DialogWidth);
        }
    }
}
