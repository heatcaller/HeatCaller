namespace HeatCaller;

/// <summary>
/// The live leaderboard: drawing it, lap details and the lap windows, the
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
        RefreshLapWindows(ordered);

        // SIDE BY SIDE WITH THEIR GHOST (22 Oct 2026): in practice, each
        // own-best ghost sits just under the driver it chases, so the two are
        // read together on every lap, and the ghost's own gap column reads its
        // gap to them. Practice only: a race's board is its
        // running order, and nothing is moved in it.
        if (_race.IsPracticeMode && OwnBestGhosts?.Invoke() is { Count: > 0 } chasedBy)
        {
            ordered = OwnBestGhost.BesideTheirDriver(ordered, s => s.TransponderId, chasedBy);
        }

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

        // WHY (20 Oct 2026): Rows.Clear threw the scroll position away, so on
        // a board taller than the panel (lap strips open, a big heat) the view
        // snapped back to the top on every crossing and the lower rows could
        // not be read mid-race. Kept here and put back once the rows are refilled.
        var firstVisibleRow = _dgvLeaderboard.FirstDisplayedScrollingRowIndex;

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
            FastestOverall = Ghosts.FastestLapMs(ordered),
            StreakThreshold = _race.StreakThreshold,
            SectorLengthsFeet = _race.SectorLengthsFeet,
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

            // The corrections drawer goes straight under its driver, above
            // any lap strip, so it travels with them when they change place
            // - at the height it has slid to, when it is opening or closing.
            if (string.Equals(_drawerTransponder, s.TransponderId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(_drawerClosing, s.TransponderId, StringComparison.OrdinalIgnoreCase))
            {
                AddDrawerRow(s);
            }

            if (_expandedTransponders.Contains(s.TransponderId))
            {
                AddLapDetailRow(s);
            }
        }

        RestoreLeaderboardSelection(selectedTransponder);

        // After the selection, because setting CurrentCell scrolls the grid
        // on its own. -1 means nothing was showing before, so leave it be.
        if (firstVisibleRow >= 0 && _dgvLeaderboard.Rows.Count > 0)
        {
            try
            {
                _dgvLeaderboard.FirstDisplayedScrollingRowIndex =
                    Math.Min(firstVisibleRow, _dgvLeaderboard.Rows.Count - 1);
            }
            catch (InvalidOperationException)
            {
                // The grid has no room to show a row yet (minimized or zero
                // height), or that row is hidden or frozen. Leave it where it is.
            }
            catch (ArgumentOutOfRangeException)
            {
                // Index outside the rows. The Math.Min above should rule
                // this out; kept so a redraw can never crash the board.
            }
        }
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
                    && !tag.StartsWith(DrawerRowPrefix, StringComparison.Ordinal)
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

        // 22 Oct 2026: laps race control added by hand have no time to show,
        // so the strip says how many there are - see HandCountedLaps.
        var byHand = HandCountedLaps.During(standing);

        if (lapCount == 0)
        {
            var emptyCells = new object[columns];
            emptyCells[0] = byHand > 0 ? $"No timed laps yet  -  {HandCountedLaps.Note(byHand)}" : "No laps yet";
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

        // At the recent end, which is the end that is always shown.
        if (byHand > 0)
        {
            entries.Add($"+{byHand} by hand");
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

        if (_dgvLeaderboard.Rows[e.RowIndex].Tag is not string tag)
        {
            return;
        }

        // The drawer's cells are only its background here; its buttons are
        // drawn across the whole row once every cell is done - see
        // LeaderboardDrawerPaint.
        if (tag.StartsWith(DrawerRowPrefix, StringComparison.Ordinal))
        {
            using (var drawerBack = new SolidBrush(AppTheme.PrimaryTint))
            {
                g.FillRectangle(drawerBack, e.CellBounds);
            }

            e.Handled = true;
            return;
        }

        if (!tag.StartsWith(DetailRowPrefix, StringComparison.Ordinal))
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
                // WHY (23 Oct 2026, bug hunt 10): columns defaulted to Automatic sort, so a header
                // click re-sorted the rows and moved a driver's corrections drawer and lap strip
                // under a different driver - a drawer press could then penalize the wrong car.
                // The board's order is the running order and only RenderLeaderboard sets it.
                SortMode = DataGridViewColumnSortMode.NotSortable,
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
        //
        // AND THE WARNINGS (21 Oct 2026). Clearing used to leave every
        // warning on the record, so the warning badge stayed on the driver's
        // name and the button looked as if it had done nothing - it had
        // taken the time off, which the board does not show unless the
        // Penalty column is on. Clear means clear: the time, any jump-start
        // penalty still owed, and the warnings (a black flag among them).
        // One undo puts all of it back. Shift+W still takes back just the
        // last warning.
        if (standing is null
            || (standing.PenaltyMs == 0 && _race.OwedJumpStartPenaltyMs(transponder) <= 0 && standing.Warnings.Count == 0))
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] {DriverNameFor(transponder)} has no penalties or warnings to clear.");
            return;
        }

        var seconds = standing.PenaltyMs / 1000.0;
        var warnings = standing.Warnings.Count;

        // Through the session's own clear rather than a matching credit, so
        // the time noted on each warning goes too - see ClearPenalties - and
        // then the warnings, which by then carry no time to refund twice.
        // Undo gives back the time, the warnings and any jump-start penalty
        // still owed.
        var edit = _race.EditStanding(transponder, () =>
        {
            _race.ClearPenalties(transponder);

            while (_race.RemoveLastWarning(transponder) is not null)
            {
            }

            return true;
        });

        if (edit is not null)
        {
            CaptureStandingEditUndo($"Clear penalties for {DriverNameFor(transponder)}", edit);
        }

        var cleared = new List<string>();

        if (seconds > 0)
        {
            cleared.Add($"{seconds:0.#}s of penalties");
        }

        if (warnings > 0)
        {
            cleared.Add(Plural.Of(warnings, "warning"));
        }

        StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [race] Cleared {DriverNameFor(transponder)}'s "
            + (cleared.Count > 0 ? string.Join(" and ", cleared) : "jump-start penalty still to come") + ".");

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
    ///
    /// A double-click is two clicks first, and a single click opens or
    /// closes the corrections drawer - so the drawer is put back as it was
    /// before the first of them. Wanting the laps does not also mean
    /// wanting the drawer.
    /// </summary>
    private void DgvLeaderboard_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex < 0 || e.RowIndex >= _dgvLeaderboard.Rows.Count)
        {
            return;
        }

        // The driver the double-click is for (22 Oct 2026, bug hunt). The
        // first click slides drawers open and shut, which moves the rows -
        // a driver below an open drawer moves up under the pointer, and
        // the second click lands on their own new drawer. So the driver of
        // the first click, when it was just now, is the one asked for.
        var recent = _lastClickTransponder is { Length: > 0 }
            && Environment.TickCount64 - _lastClickTicks <= SystemInformation.DoubleClickTime * 2L;

        // Otherwise a double-click on the drawer is two quick presses of its
        // buttons, not a request for the laps.
        if (IsDrawerRow(e.RowIndex) && !recent)
        {
            // WHY (22 Oct 2026, bug hunt 9): WinForms raises DoubleClick, not
            // CellMouseClick, for the second of two quick presses, so a
            // double-tap on "+1 Lap" or "+5s" quietly counted once. The second
            // press is made here. The pointer is tested again rather than
            // reusing the first press's button: that press may already have
            // rebuilt the board. Only the buttons that add up are pressed twice
            // - a second Warning, Share video or Lap breakdown is not wanted.
            var (rowIndex, button) = DrawerButtonAt(_dgvLeaderboard.PointToClient(Control.MousePosition));

            if (button >= 0 && TransponderForRow(rowIndex) is { Length: > 0 } driver)
            {
                var action = DrawerButtons[button].Action;

                if (action is DrawerAction.AddLap or DrawerAction.RemoveLap
                    or DrawerAction.Penalty5 or DrawerAction.Penalty10)
                {
                    var bounds = DrawerLayout(DrawerRowBounds(rowIndex))[button].Bounds;
                    BeginInvoke(() => RunDrawerAction(driver, action, bounds));
                }
            }

            return;
        }

        var transponder = recent ? _lastClickTransponder : TransponderForRow(e.RowIndex);

        if (string.IsNullOrEmpty(transponder))
        {
            return;
        }

        var drawerChanged = false;

        if (string.Equals(_lastClickTransponder, transponder, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(_drawerTransponder, _drawerBeforeClick, StringComparison.OrdinalIgnoreCase))
        {
            // Put straight back, not slid: the first click's slide was not
            // something anybody asked for.
            SetDrawerNow(_drawerBeforeClick);
            drawerChanged = true;
        }

        // Clicks after this one start afresh.
        _lastClickTransponder = null;

        if (!drawerChanged)
        {
            // Under the driver's own row, which may not be the row clicked.
            ToggleLapWindow(transponder, DriverRowIndex(transponder));
            return;
        }

        // Rebuilt after the grid has finished with the click - clearing its
        // rows from inside its own mouse handling is a re-entrant call it
        // refuses - and the popup placed under the driver's row as it stands
        // after the rebuild.
        BeginInvoke(() =>
        {
            if (IsDisposed || Disposing)
            {
                return;
            }

            RenderLeaderboard();
            ToggleLapWindow(transponder, DriverRowIndex(transponder));
        });
    }

    /// <summary>
    /// The driver's highlight video from the last race, selected in its
    /// folder so it can be dragged into a message or a post (21 Oct 2026).
    /// See HighlightVideo. Says why when there is none yet: videos are made
    /// in the background after the race, a minute or so each.
    /// </summary>
    private void ShareVideo(string transponder)
    {
        var name = DriverNameFor(transponder);

        if (HighlightVideo.LatestFor(transponder) is not { } path)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [video] No highlight video for {name} yet. They are made a minute "
                + "or so after the race, for the drivers chosen on Club Website > Recording (every driver, unless that was changed).");
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"")
            {
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            StatusMessage?.Invoke($"{AppTime.LogStampNow()}  [video] Couldn't open the folder for {name}'s video ({ex.Message}). It is at {path}");
        }
    }

    // --- The corrections drawer ---------------------------------------------
    //
    // CLICK TO CORRECT (21 Oct 2026). A single click on a driver opens a strip
    // of buttons right under their row: +1 Lap, -1 Lap, +5s, +10s, Warning,
    // Clear Penalties, and Lap breakdown at the far end. Clicking the same
    // driver again closes it; clicking another driver moves it to them.
    //
    // It replaced the row of the same buttons under the board, which acted on
    // "the selected racer" a screen away from the name - and took a row of
    // the board's height to do it.
    //
    // One drawer at a time. It is a row of the grid like the lap strips, so
    // it is rebuilt with the board on every crossing and stays under its
    // driver when they change place. The buttons are drawn, not controls:
    // a grid row cannot hold real ones, and controls laid over the grid
    // would have to chase its scrolling and its rebuilds.

    /// <summary>The driver whose drawer is open, or null for none. Survives the board being rebuilt.</summary>
    private string? _drawerTransponder;

    /// <summary>The drawer as it was before the last single click, so a double-click can put it back.</summary>
    private string? _drawerBeforeClick;

    /// <summary>The driver and the time of the last single click on a driver row - see DgvLeaderboard_CellMouseClick.</summary>
    private string? _lastClickTransponder;
    private long _lastClickTicks;

    /// <summary>Which of the drawer's buttons the pointer is over, or -1.</summary>
    private int _drawerHover = -1;

    private const string DrawerRowPrefix = "drawer:";

    private enum DrawerAction
    {
        AddLap,
        RemoveLap,
        Penalty5,
        Penalty10,
        Warning,
        ClearPenalties,
        ShareVideo,
        LapBreakdown,
    }

    /// <summary>The drawer's buttons, left to right. Lap breakdown is drawn at the far end, apart from the rest.</summary>
    private static readonly (DrawerAction Action, string Caption)[] DrawerButtons =
    {
        (DrawerAction.AddLap, "+1 Lap"),
        (DrawerAction.RemoveLap, "−1 Lap"),
        (DrawerAction.Penalty5, "+5s"),
        (DrawerAction.Penalty10, "+10s"),
        (DrawerAction.Warning, "⚠ Warning"),
        (DrawerAction.ClearPenalties, "Clear Penalties"),
        (DrawerAction.ShareVideo, "Share video"),
        (DrawerAction.LapBreakdown, "Lap breakdown"),
    };

    private const int DrawerButtonGap = 8;
    private const int DrawerPadding = 8;

    /// <summary>How tall a drawer button is - the text size sets it.</summary>
    private static int DrawerButtonHeight => Math.Max(32, AppTheme.BoldFont.Height + 14);

    /// <summary>A drawer row's height when it is all the way open.</summary>
    private static int DrawerFullHeight => DrawerButtonHeight + DrawerPadding * 2;

    // --- The drawer sliding open and shut (21 Oct 2026) ----------------------
    //
    // It used to appear and vanish in one frame: the row was simply there on
    // the next rebuild, at full height, and the whole board below it jumped
    // down a row's worth in one go - which read as the board glitching, not
    // as a drawer opening. It now slides: the row grows from nothing to its
    // full height (and shrinks back) over DrawerSlideMs, easing out, with its
    // buttons riding down from under the driver's row as it opens.
    //
    // The height is worked out from the CLOCK, not counted in frames, so a
    // board rebuilt mid-slide by a car crossing the line puts the row back
    // at exactly the height the slide has reached rather than starting it
    // again - the board is rebuilt on every crossing, and a slide that
    // restarted on each one would never finish during a race.
    //
    // Opening another driver's drawer slides theirs open while the old one
    // slides shut, at the same time - one movement, not two in a row. A
    // drawer clicked again on its way out turns round where it is.

    /// <summary>How long a drawer takes to slide open or shut.</summary>
    private const int DrawerSlideMs = 180;

    /// <summary>
    /// The thinnest a drawer row is - where it slides open from and shut to.
    /// Two pixels is the least a grid row can be (DataGridViewRow.MinimumHeight).
    /// </summary>
    private const int DrawerShutHeight = 2;

    /// <summary>One drawer's slide: from a height, to a height, starting at a moment on Environment.TickCount64.</summary>
    private readonly record struct DrawerSlide(int From, int To, long Started)
    {
        /// <summary>How far through it is at <paramref name="now"/>, 0 to 1.</summary>
        private double Progress(long now) => Math.Clamp((now - Started) / (double)DrawerSlideMs, 0, 1);

        /// <summary>
        /// The height at <paramref name="now"/>, eased out: quick to start and
        /// settling gently into place, the way a drawer moves when pulled.
        /// </summary>
        public int HeightAt(long now)
        {
            var eased = 1 - Math.Pow(1 - Progress(now), 3);
            return (int)Math.Round(From + (To - From) * eased);
        }

        public bool DoneAt(long now) => Progress(now) >= 1;
    }

    /// <summary>The open drawer's slide while it is still opening; null once it is all the way open.</summary>
    private DrawerSlide? _drawerOpening;

    /// <summary>A drawer on its way shut, or null - see <see cref="_drawerClosingSlide"/>.</summary>
    private string? _drawerClosing;

    private DrawerSlide _drawerClosingSlide;

    /// <summary>Moves the slides along. Runs only while one is moving.</summary>
    private System.Windows.Forms.Timer? _drawerTimer;

    /// <summary>
    /// Opens <paramref name="transponder"/>'s drawer - or, for null, shuts
    /// the open one - sliding. The caller rebuilds the board afterwards, as
    /// it always has.
    /// </summary>
    private void SetDrawer(string? transponder)
    {
        if (string.Equals(_drawerTransponder, transponder, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var now = Environment.TickCount64;
        var full = DrawerFullHeight;

        // The one on its way shut, as it is now - in case it is the one
        // being opened again.
        var wasClosing = _drawerClosing;
        var wasClosingHeight = wasClosing is null ? DrawerShutHeight : _drawerClosingSlide.HeightAt(now);

        // The open one slides shut from wherever it had got to. Only one
        // goes out at a time: one still on its way out goes now.
        if (_drawerTransponder is { } open)
        {
            _drawerClosing = open;
            _drawerClosingSlide = new DrawerSlide(_drawerOpening?.HeightAt(now) ?? full, DrawerShutHeight, now);
        }

        _drawerTransponder = transponder;
        _drawerOpening = null;

        // A button lit under the pointer belongs to the drawer that was
        // open - the next mouse move lights one on this one.
        _drawerHover = -1;

        if (transponder is not null)
        {
            var reopened = string.Equals(transponder, wasClosing, StringComparison.OrdinalIgnoreCase);

            if (string.Equals(transponder, _drawerClosing, StringComparison.OrdinalIgnoreCase))
            {
                _drawerClosing = null;
            }

            _drawerOpening = new DrawerSlide(reopened ? wasClosingHeight : DrawerShutHeight, full, now);
        }

        if (_drawerTimer is null)
        {
            _drawerTimer = new System.Windows.Forms.Timer { Interval = 15 };
            _drawerTimer.Tick += DrawerTick;
            Disposed += (_, _) => _drawerTimer?.Dispose();
        }

        _drawerTimer.Start();
    }

    /// <summary>
    /// Opens <paramref name="transponder"/>'s drawer, or shuts it, at once -
    /// no slide, nothing left moving. For a reset race, and a double-click
    /// putting the drawer back as it was before its first click.
    /// </summary>
    private void SetDrawerNow(string? transponder)
    {
        _drawerTransponder = transponder;
        _drawerOpening = null;
        _drawerClosing = null;
        _drawerTimer?.Stop();
    }

    /// <summary>
    /// A frame of the slide: each drawer row set to the height its slide has
    /// reached, and the board rebuilt once a drawer has finished shutting, to
    /// take its row away and put the selection back as it was.
    /// </summary>
    private void DrawerTick(object? sender, EventArgs e)
    {
        if (IsDisposed || Disposing)
        {
            _drawerTimer?.Stop();
            return;
        }

        var now = Environment.TickCount64;

        if (_drawerOpening is { } opening && opening.DoneAt(now))
        {
            _drawerOpening = null;
        }

        var shut = _drawerClosing is not null && _drawerClosingSlide.DoneAt(now);

        if (shut)
        {
            _drawerClosing = null;
        }

        if (_drawerOpening is null && _drawerClosing is null)
        {
            _drawerTimer?.Stop();
        }

        if (shut)
        {
            RenderLeaderboard();
            return;
        }

        for (var i = 0; i < _dgvLeaderboard.Rows.Count; i++)
        {
            if (IsDrawerRow(i) && TransponderForRow(i) is { } driver)
            {
                var height = DrawerHeightFor(driver, now);
                var row = _dgvLeaderboard.Rows[i];

                if (row.Height != height)
                {
                    row.Height = height;
                }
            }
        }
    }

    /// <summary>The height a driver's drawer row is at <paramref name="now"/>: its slide's, or all the way open.</summary>
    private int DrawerHeightFor(string transponder, long now)
    {
        if (string.Equals(transponder, _drawerTransponder, StringComparison.OrdinalIgnoreCase))
        {
            return Math.Max(DrawerShutHeight, _drawerOpening?.HeightAt(now) ?? DrawerFullHeight);
        }

        if (string.Equals(transponder, _drawerClosing, StringComparison.OrdinalIgnoreCase))
        {
            return Math.Max(DrawerShutHeight, _drawerClosingSlide.HeightAt(now));
        }

        return DrawerFullHeight;
    }

    /// <summary>
    /// Whether the drawer on this row is all the way open - the only time its
    /// buttons can be pressed. Half-open, a button is half under the row
    /// above; on its way shut, it is a drawer somebody has just closed.
    /// </summary>
    private bool DrawerIsOpen(int rowIndex) =>
        IsDrawerRow(rowIndex)
        && _drawerOpening is null
        && string.Equals(TransponderForRow(rowIndex), _drawerTransponder, StringComparison.OrdinalIgnoreCase);

    private void AddDrawerRow(RacerStanding standing)
    {
        var columns = _dgvLeaderboard.Columns.Count;

        if (columns < 1)
        {
            return;
        }

        var index = _dgvLeaderboard.Rows.Add(new object[columns]);
        var row = _dgvLeaderboard.Rows[index];

        row.Tag = DrawerRowPrefix + standing.TransponderId;
        row.MinimumHeight = DrawerShutHeight;
        row.Height = DrawerHeightFor(standing.TransponderId, Environment.TickCount64);
    }

    private bool IsDrawerRow(int rowIndex) =>
        rowIndex >= 0 && rowIndex < _dgvLeaderboard.Rows.Count
        && _dgvLeaderboard.Rows[rowIndex].Tag is string tag
        && tag.StartsWith(DrawerRowPrefix, StringComparison.Ordinal);

    /// <summary>The index of a driver's own row on the board, or -1.</summary>
    private int DriverRowIndex(string transponder)
    {
        for (var i = 0; i < _dgvLeaderboard.Rows.Count; i++)
        {
            if (_dgvLeaderboard.Rows[i].Tag is string tag
                && string.Equals(tag, transponder, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Where each button sits in a drawer row whose bounds are given - the
    /// one answer both the painting and the clicking use, so what is drawn
    /// is what is hit. Lined up under the driver's name, Lap breakdown at
    /// the far right when there is room for it there.
    /// </summary>
    private List<(Rectangle Bounds, DrawerAction Action, string Caption)> DrawerLayout(Rectangle row)
    {
        var height = DrawerButtonHeight;
        var top = row.Top + Math.Max(0, (row.Height - height) / 2);

        // In from the left by the position column, so the buttons start
        // under the name rather than under the position number.
        var indent = _dgvLeaderboard.Columns.Count > 1 && _dgvLeaderboard.Columns[0].Visible
            ? Math.Min(_dgvLeaderboard.Columns[0].Width, 90)
            : LapDetailPadding;

        var x = row.Left + indent;
        var layout = new List<(Rectangle, DrawerAction, string)>(DrawerButtons.Length);

        foreach (var (action, caption) in DrawerButtons)
        {
            var width = TextRenderer.MeasureText(caption, AppTheme.BoldFont).Width + 28;

            if (action == DrawerAction.LapBreakdown)
            {
                var right = row.Right - LapDetailPadding;

                if (right - width >= x + DrawerButtonGap * 2)
                {
                    x = right - width;
                }
            }

            layout.Add((new Rectangle(x, top, width, height), action, caption));
            x += width + DrawerButtonGap;
        }

        return layout;
    }

    /// <summary>
    /// The drawer's face for this row across every visible column, in the
    /// grid's coordinates - see <see cref="DrawerFace"/>.
    /// </summary>
    private Rectangle DrawerRowBounds(int rowIndex)
    {
        var rect = _dgvLeaderboard.GetRowDisplayRectangle(rowIndex, cutOverflow: false);
        var width = _dgvLeaderboard.Columns.GetColumnsWidth(DataGridViewElementStates.Visible);

        return DrawerFace(new Rectangle(rect.Left, rect.Top, Math.Min(Math.Max(width, 1), Math.Max(rect.Width, 1)), rect.Height));
    }

    /// <summary>
    /// The drawer at its full height, with its bottom on the bottom of
    /// <paramref name="row"/> - which is the row itself once it is open.
    /// Part-way through a slide the row is shorter than this and shows only
    /// the bottom of it, so the buttons ride down out from under the driver
    /// as the drawer opens, and back up under them as it shuts.
    /// </summary>
    private static Rectangle DrawerFace(Rectangle row)
    {
        var full = DrawerFullHeight;

        return new Rectangle(row.Left, row.Bottom - full, row.Width, full);
    }

    /// <summary>Draws the drawer's buttons across its row, after its cells have painted the background.</summary>
    private void LeaderboardDrawerPaint(object? sender, DataGridViewRowPostPaintEventArgs e)
    {
        if (!IsDrawerRow(e.RowIndex))
        {
            return;
        }

        var g = e.Graphics;
        var width = _dgvLeaderboard.Columns.GetColumnsWidth(DataGridViewElementStates.Visible);
        var row = new Rectangle(e.RowBounds.Left, e.RowBounds.Top, Math.Min(Math.Max(width, 1), e.RowBounds.Width), e.RowBounds.Height);

        // Kept inside the row: mid-slide the buttons are taller than it, and
        // anything drawn past its edges would land on the drivers either side.
        var saved = g.Save();
        g.SetClip(row, System.Drawing.Drawing2D.CombineMode.Intersect);

        // An accent edge down the left and a rule along the bottom, so the
        // drawer reads as belonging to the driver above it.
        using (var edge = new SolidBrush(AppTheme.Accent))
        {
            g.FillRectangle(edge, row.Left, row.Top, 3, row.Height);
        }

        using (var rule = new Pen(AppTheme.Border))
        {
            g.DrawLine(rule, row.Left, row.Bottom - 1, row.Right, row.Bottom - 1);
        }

        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var buttons = DrawerLayout(DrawerFace(row));

        for (var i = 0; i < buttons.Count; i++)
        {
            var (bounds, action, caption) = buttons[i];
            var hover = i == _drawerHover;
            var danger = action == DrawerAction.ClearPenalties;

            var fill = hover ? AppTheme.SecondaryButtonHover : AppTheme.SecondaryButton;
            var border = danger ? AppTheme.Danger : AppTheme.Border;
            var ink = danger ? AppTheme.Danger : AppTheme.TextDark;

            using (var path = RoundedRect(bounds, 6))
            {
                using (var brush = new SolidBrush(fill))
                {
                    g.FillPath(brush, path);
                }

                using (var pen = new Pen(border))
                {
                    g.DrawPath(pen, path);
                }
            }

            // Only once the button is all the way inside the row (22 Oct
            // 2026). TextRenderer draws through GDI and does not keep to the
            // clip set above, so mid-slide the captions of a half-open drawer
            // were drawn over the driver's own row - "+1 Lap", "Warning",
            // "Clear Penalties" printed across the name and the lap times -
            // and stayed there until something repainted that row.
            if (row.Contains(bounds))
            {
                TextRenderer.DrawText(g, caption, AppTheme.BoldFont, bounds, ink,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
            }
        }

        // The clip and the smoothing both back as they were.
        g.Restore(saved);
    }

    private static System.Drawing.Drawing2D.GraphicsPath RoundedRect(Rectangle r, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        var d = radius * 2;

        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d - 1, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d - 1, r.Bottom - d - 1, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d - 1, d, d, 90, 90);
        path.CloseFigure();

        return path;
    }

    /// <summary>Which drawer button is at a point in the grid, if any, and the row it is on.</summary>
    private (int RowIndex, int Button) DrawerButtonAt(Point point)
    {
        var hit = _dgvLeaderboard.HitTest(point.X, point.Y);

        if (!IsDrawerRow(hit.RowIndex))
        {
            return (-1, -1);
        }

        // Mid-slide, nothing in it can be pressed - see DrawerIsOpen.
        if (!DrawerIsOpen(hit.RowIndex))
        {
            return (hit.RowIndex, -1);
        }

        var buttons = DrawerLayout(DrawerRowBounds(hit.RowIndex));

        for (var i = 0; i < buttons.Count; i++)
        {
            if (buttons[i].Bounds.Contains(point))
            {
                return (hit.RowIndex, i);
            }
        }

        return (hit.RowIndex, -1);
    }

    private void LeaderboardDrawerHover(object? sender, MouseEventArgs e) =>
        SetDrawerHover(DrawerButtonAt(e.Location).Button);

    private void SetDrawerHover(int button)
    {
        _dgvLeaderboard.Cursor = button >= 0 ? Cursors.Hand : Cursors.Default;

        if (button == _drawerHover)
        {
            return;
        }

        _drawerHover = button;

        for (var i = 0; i < _dgvLeaderboard.Rows.Count; i++)
        {
            if (IsDrawerRow(i))
            {
                _dgvLeaderboard.InvalidateRow(i);
            }
        }
    }

    /// <summary>
    /// A left click on the board: on a driver, opens or closes their drawer;
    /// on the drawer, presses the button under the pointer.
    /// </summary>
    private void DgvLeaderboard_CellMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || e.RowIndex < 0 || e.RowIndex >= _dgvLeaderboard.Rows.Count)
        {
            return;
        }

        if (IsDrawerRow(e.RowIndex))
        {
            // WHY (22 Oct 2026, bug hunt 9): this used to drop any drawer press
            // within DoubleClickTime of a driver click, meant for the second
            // click of a double-click on a driver. That second click never
            // comes here - WinForms sends it to CellDoubleClick, which handles
            // it - so the guard only threw away real quick presses, like "+1
            // Lap" right after opening the drawer. Presses mid-slide are still
            // refused by DrawerIsOpen in DrawerButtonAt.
            //
            // A press of the drawer is a click of its own, so it ends the
            // driver click: a double-click that follows is two presses of the
            // buttons, not a double-click on that driver (which shut the
            // drawer and opened their lap breakdown).
            _lastClickTransponder = null;

            // The click's point is the cell's; the buttons are laid out
            // across the grid.
            var cell = _dgvLeaderboard.GetCellDisplayRectangle(Math.Max(0, e.ColumnIndex), e.RowIndex, cutOverflow: false);
            var point = new Point(cell.Left + e.X, cell.Top + e.Y);
            var (rowIndex, button) = DrawerButtonAt(point);

            if (button < 0 || TransponderForRow(rowIndex) is not { Length: > 0 } driver)
            {
                return;
            }

            var bounds = DrawerLayout(DrawerRowBounds(rowIndex))[button].Bounds;
            var action = DrawerButtons[button].Action;

            // After the grid has finished with the click: a correction
            // rebuilds the board, and the grid refuses to have its rows
            // cleared from inside its own mouse handling.
            BeginInvoke(() => RunDrawerAction(driver, action, bounds));
            return;
        }

        // A lap strip is not a driver - nothing to open.
        if (_dgvLeaderboard.Rows[e.RowIndex].Tag is not string tag
            || tag.StartsWith(DetailRowPrefix, StringComparison.Ordinal))
        {
            // WHY (22 Oct 2026, bug hunt 9): a click here is a fresh click (the
            // second half of a double-click goes to CellDoubleClick, not here),
            // so it ends the last driver click. Before, a double-click on
            // another driver's lap strip within about a second opened the
            // first driver's breakdown and shut their drawer.
            _lastClickTransponder = null;
            return;
        }

        var now = Environment.TickCount64;

        // The second click of a double-click on the same driver: the double
        // click is coming, and it decides what happens.
        if (string.Equals(_lastClickTransponder, tag, StringComparison.OrdinalIgnoreCase)
            && now - _lastClickTicks <= SystemInformation.DoubleClickTime)
        {
            return;
        }

        _lastClickTransponder = tag;
        _lastClickTicks = now;
        _drawerBeforeClick = _drawerTransponder;

        // Slid open, or shut - see SetDrawer.
        SetDrawer(string.Equals(_drawerTransponder, tag, StringComparison.OrdinalIgnoreCase) ? null : tag);

        BeginInvoke(() =>
        {
            if (!IsDisposed && !Disposing)
            {
                RenderLeaderboard();
            }
        });
    }

    /// <summary>
    /// What a drawer button does - the same corrections the keys and the
    /// right-click menu make, with the same undo, on the drawer's driver.
    /// </summary>
    private void RunDrawerAction(string transponder, DrawerAction action, Rectangle buttonBounds)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        // The corrections act on the selected driver, so the drawer's driver
        // is made the selected one - the board may have been rebuilt since
        // the click.
        var rowIndex = DriverRowIndex(transponder);

        if (rowIndex < 0)
        {
            return;
        }

        _dgvLeaderboard.CurrentCell = _dgvLeaderboard.Rows[rowIndex].Cells[0];

        switch (action)
        {
            case DrawerAction.AddLap:
                AdjustSelected(laps: 1, penaltyMs: 0);
                break;

            case DrawerAction.RemoveLap:
                AdjustSelected(laps: -1, penaltyMs: 0);
                break;

            case DrawerAction.Penalty5:
                AdjustSelected(laps: 0, penaltyMs: 5000);
                break;

            case DrawerAction.Penalty10:
                AdjustSelected(laps: 0, penaltyMs: 10000);
                break;

            case DrawerAction.ClearPenalties:
                ClearSelectedPenalty();
                break;

            case DrawerAction.Warning:
                ShowWarningMenu(_dgvLeaderboard, new Point(buttonBounds.Left, buttonBounds.Bottom));
                break;

            case DrawerAction.ShareVideo:
                ShareVideo(transponder);
                break;

            case DrawerAction.LapBreakdown:
                ToggleLapWindow(transponder, rowIndex);
                break;
        }
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
            string tag when tag.StartsWith(DrawerRowPrefix, StringComparison.Ordinal) => tag[DrawerRowPrefix.Length..],
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
                OpenLapWindow(transponder, _dgvLeaderboard.CurrentRow?.Index ?? -1);
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

        // The lap windows (30 Oct 2026) - see RaceControlControl.LapWindows.
        // Shown by the Opening handler only while one is open.
        var lineUpLapWindows = new ToolStripMenuItem("Line up lap windows");
        lineUpLapWindows.Click += (_, _) => LineUpLapWindows();

        var closeLapWindows = new ToolStripMenuItem("Close all lap windows");
        closeLapWindows.Click += (_, _) => CloseLapWindows();

        // Coaching (22 Oct 2026): Coach (name), with At the split, Target
        // lap..., Consistency and Tones instead of words - one driver at a
        // time. Filled each time the menu opens, for the driver under the
        // pointer (FillCoachItems, RaceControlControl.Coaching); the
        // placeholder stops WinForms treating it as a plain item with no
        // arrow before the first fill.
        var coach = new ToolStripMenuItem("Coach");
        coach.DropDownItems.Add(new ToolStripMenuItem("..."));

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

        // The decoder is down: the whole field, one tap a lap, full screen
        // (11 Nov 2026) - see HandScoring. Shown only while a race runs.
        var scoreByHand = new ToolStripMenuItem("Score the race by hand (decoder down)...");
        scoreByHand.Click += (_, _) => OpenHandScoring();

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

        // Filled each time the menu opens, for the driver under the pointer
        // - see FillPackItems. The placeholder stops WinForms treating it as
        // a plain item with no arrow before the first fill.
        var pack = new ToolStripMenuItem("Pack");
        pack.DropDownItems.Add(new ToolStripMenuItem("..."));

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
            lineUpLapWindows,
            closeLapWindows,
            toggleLaps,
            hideAllLaps,
            coach,
            releaseDriver,
            releaseAll,
            new ToolStripSeparator(),
            addLap,
            removeLap,
            scoreByHand,
            new ToolStripSeparator(),
            penaltyFive,
            penaltyTen,
            penaltyCustom,
            clearPenalty,
            new ToolStripSeparator(),
            warn,
            new ToolStripSeparator(),
            pack,
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

            var lapWindowsOpen = _lapWindows.Count(w => !w.IsDisposed);
            lineUpLapWindows.Visible = lapWindowsOpen > 1;
            closeLapWindows.Visible = lapWindowsOpen > 0;
            closeLapWindows.Text = lapWindowsOpen > 1 ? $"Close all {lapWindowsOpen} lap windows" : "Close the lap window";

            toggleLaps.Text = _expandedTransponders.Contains(transponder)
                ? $"Hide {name}'s laps on the board"
                : $"Show {name}'s laps on the board";

            // Shown only when it would actually do something more than the
            // item above it.
            hideAllLaps.Visible = _expandedTransponders.Count > 1;
            hideAllLaps.Text = $"Hide all {_expandedTransponders.Count} lap strips";

            // A local rather than reading Visible back: a menu item reports
            // itself hidden until its menu is on screen.
            var canCoach = !GhostService.IsGhost(transponder);
            coach.Visible = canCoach;

            if (canCoach)
            {
                FillCoachItems(coach, transponder, name);
            }

            // Releasing is only ever a hand-released thing, and only while
            // the race is actually running - a scheduled stagger releases
            // itself, and there is nothing to release before the green.
            var canRelease = _race.StartType == RaceStartType.HandReleased
                             && _race.State == RaceState.Racing;

            releaseDriver.Visible = canRelease && !_race.IsReleased(transponder);
            releaseDriver.Text = $"Release {name} now";

            releaseAll.Visible = canRelease;

            scoreByHand.Visible = _race.State == RaceState.Racing && !_race.IsPracticeMode;
            scoreByHand.Text = HandScoringOn ? "Open the hand-scoring grid..." : "Score the race by hand (decoder down)...";

            var standing = _race.GetOrderedStandings().FirstOrDefault(st =>
                string.Equals(st.TransponderId, transponder, StringComparison.OrdinalIgnoreCase));

            // A jump-start penalty not yet booked can be cleared too - see
            // ClearSelectedPenalty.
            var owedJumpStartMs = standing is null ? 0 : _race.OwedJumpStartPenaltyMs(standing.TransponderId);

            clearPenalty.Enabled = standing is { PenaltyMs: not 0 } || owedJumpStartMs > 0 || standing is { Warnings.Count: > 0 };
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

            // Kept in a local: Visible reads false until the menu is on
            // screen, which it is not yet while it is opening.
            var showPack = BatteryPacks is not null && profile is not null && !GhostService.IsGhost(transponder);
            pack.Visible = showPack;

            if (showPack)
            {
                FillPackItems(pack, transponder, profile!, name);
            }
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
