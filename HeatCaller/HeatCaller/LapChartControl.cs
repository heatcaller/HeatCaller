namespace HeatCaller;

/// <summary>
/// Plots lap-by-lap pace for every driver in a chosen session, so you can
/// see consistency, fade, and where time was found or lost - things a
/// single fastest-lap number can't show.
///
/// Drawn with plain GDI+ rather than pulling in a charting library: the
/// requirements here are a handful of polylines with axes, and this keeps
/// the app dependency-free and consistent with how the results card and
/// sidebar icons are already rendered.
/// </summary>
public sealed class LapChartControl : UserControl
{
    private readonly SessionHistoryStore _history;
    private readonly AppSettings _settings;

    private readonly ComboBox _cmbSession = new();
    private readonly CheckedListBox _clbDrivers = new ThemedCheckedListBox();
    private readonly Panel _chartPanel = new();
    private readonly Label _lblInfo = new();
    private readonly DataGridView _statsTable = new();

    /// <summary>
    /// Every plotted point with the lap it represents, captured during
    /// paint so hovering can report an exact time. Recomputing the
    /// geometry on mouse-move would risk drifting out of step with what's
    /// actually drawn.
    /// </summary>
    private readonly List<(PointF At, string Driver, int Lap, long Ms, Color Color)> _plotted = new();

    private (string Driver, int Lap, long Ms, Color Color, PointF At)? _hover;

    private SessionRecord? _currentSession;

    public LapChartControl(SessionHistoryStore history, AppSettings settings)
    {
        _history = history;
        _settings = settings;

        Dock = DockStyle.Fill;

        // The body face, worn rather than set once: this page sits in a tab
        // whose row is drawn in the bold face, which it would otherwise
        // take. No MinimumSize: the 900x640 floor was honoured over the size
        // the page was given. A short window scrolls the page instead
        // (FillPage).
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout();
        RefreshAll();
    }

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>The session picker's width at the normal text size.</summary>
    private const int SessionPickerWidth = 420;

    /// <summary>The drivers' card's width at the normal text size.</summary>
    private const int DriversWidth = 220;

    /// <summary>The least the chart gets at the normal text size - below that the page scrolls.</summary>
    private const int ChartLeastHeight = 220;

    /// <summary>Drivers shown in the consistency table without scrolling it.</summary>
    private const int StatsRowsShown = 4;

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
    /// The intro and the session picker, then the drivers down the left and
    /// beside them the chart - taking the height that is left - over the
    /// consistency table. On the layout pieces (LayoutPieces.cs), so nothing
    /// is placed by hand.
    ///
    /// It used to be placed by hand over a 900x640 floor: the list and the
    /// chart at a fixed 340px, and the export button and the consistency box
    /// at fixed places under them, in faces handed out once.
    /// </summary>
    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "Lap-by-lap pace for a saved race. Tick drivers on the left to compare them.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _cmbSession.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbSession.SelectedIndexChanged += (_, _) => LoadSelectedSession();

        // What the session holds, beside the picker - on a line of its own
        // when it no longer fits there (FieldRow).
        _lblInfo.AutoSize = false;
        _lblInfo.AutoEllipsis = true;
        _lblInfo.ForeColor = AppTheme.TextMuted;

        var sessionRow = new FieldRow();

        sessionRow.Add(new Label { Text = "Session:" }, _cmbSession, () => UiLayout.AtTextSize(SessionPickerWidth));
        sessionRow.AddLast(null, _lblInfo, () => _lblInfo.GetPreferredSize(Size.Empty).Width);

        // --- The drivers ---
        _clbDrivers.CheckOnClick = true;
        _clbDrivers.IntegralHeight = false;
        _clbDrivers.ItemCheck += (_, _) =>
        {
            if (!IsHandleCreated || IsDisposed)
            {
                return;
            }

            BeginInvoke(() => { _chartPanel.Invalidate(); RefreshStats(); });
        };

        var drivers = new Section("Drivers");

        drivers.AddFill(_clbDrivers, () => 4 * _clbDrivers.ItemHeight + 6, gapBelow: 0);

        // --- The chart ---
        _chartPanel.BackColor = AppTheme.Surface;

        // The hover readout repaints the chart, so this has to be buffered
        // or moving the mouse across it flickers the whole plot.
        AppTheme.EnableDoubleBuffering(_chartPanel);
        _chartPanel.Paint += ChartPanel_Paint;
        _chartPanel.MouseMove += ChartPanel_MouseMove;
        _chartPanel.MouseLeave += (_, _) =>
        {
            if (_hover is not null)
            {
                _hover = null;
                _chartPanel.Invalidate();
            }
        };

        // It draws its own axes and labels to its size, so a resize is a
        // repaint.
        _chartPanel.Resize += (_, _) => _chartPanel.Invalidate();

        var btnExport = new Button { Text = "Save Chart as Image..." };
        btnExport.Click += (_, _) => ExportChart();

        var chart = new Section("Lap times");

        chart.AddFill(_chartPanel, () => UiLayout.AtTextSize(ChartLeastHeight));
        chart.Add(new ButtonBar().With(btnExport), gapBelow: 0);

        // --- Consistency ---
        _statsTable.ReadOnly = true;
        _statsTable.AllowUserToAddRows = false;
        _statsTable.AllowUserToDeleteRows = false;
        _statsTable.AllowUserToResizeRows = false;
        _statsTable.RowHeadersVisible = false;
        _statsTable.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _statsTable.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _statsTable.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "driver", HeaderText = "Driver", FillWeight = 80 },
            new DataGridViewTextBoxColumn { Name = "best", HeaderText = "Best", FillWeight = 45 },
            new DataGridViewTextBoxColumn { Name = "avg", HeaderText = "Average", FillWeight = 50 },
            new DataGridViewTextBoxColumn { Name = "spread", HeaderText = "Avg-Best", FillWeight = 55 },
            new DataGridViewTextBoxColumn { Name = "consistency", HeaderText = "Std Dev", FillWeight = 50 }
        );

        // Every heading whole, and a few drivers tall, at the text size in
        // use - see Pieces.KeepHeadingsWhole.
        _textSize.Keep(() =>
        {
            Pieces.KeepHeadingsWhole(_statsTable);

            var height = _statsTable.ColumnHeadersHeight + StatsRowsShown * _statsTable.RowTemplate.Height + 2;

            if (_statsTable.Height != height)
            {
                _statsTable.Height = height;
            }
        });

        var consistency = new Section("Consistency");

        consistency.Add(_statsTable, gapBelow: 0);

        // --- The page ---
        //
        // The drivers down the left, ticked from; the chart and the table
        // beside them, taking the rest of the width.
        var band = new Beside(() => UiLayout.AtTextSize(DriversWidth)) { SideOnLeft = true };

        band.AddFill(chart, chart.LeastHeight, gapBelow: UiLayout.RowGap);
        band.Add(consistency, gapBelow: 0);
        band.AddSideFill(drivers, drivers.LeastHeight, gapBelow: 0);

        var page = new FillPage();

        page.Add(lblIntro);
        page.Add(sessionRow);
        page.AddFill(band, band.LeastHeight, gapBelow: 0);

        Controls.Add(page);
    }

    /// <summary>
    /// Std dev is the headline consistency number: how tightly a driver's
    /// laps cluster. Avg-Best complements it by showing how much time is
    /// being left on the table relative to what they've proven they can
    /// do - a driver can be very consistent but consistently slow, and
    /// these two numbers together tell those apart.
    /// </summary>
    private void RefreshStats()
    {
        _statsTable.Rows.Clear();

        foreach (var driver in CheckedDrivers())
        {
            // Flying laps only, as PaceAnalysis.CountedLaps counts them: the
            // first entry is the run from the green to the first crossing,
            // and a car parked just behind the line made it the "Best" here
            // and dragged the average and spread with it.
            var laps = driver.LapTimesMs.Skip(1).Where(l => l > 0).ToList();
            if (laps.Count == 0)
            {
                continue;
            }

            var best = laps.Min();
            var avg = laps.Average();
            var variance = laps.Sum(t => Math.Pow(t - avg, 2)) / laps.Count;
            var stdDev = Math.Sqrt(variance);

            _statsTable.Rows.Add(
                driver.DriverName,
                $"{best / 1000.0:0.000}s",
                $"{avg / 1000.0:0.000}s",
                $"+{(avg - best) / 1000.0:0.000}s",
                $"{stdDev / 1000.0:0.000}s");
        }
    }

    /// <summary>Call whenever this page becomes visible - new sessions can be recorded while it's not active.</summary>
    public void RefreshAll()
    {
        var previousId = (_cmbSession.SelectedItem as SessionComboItem)?.Session.Id;

        _cmbSession.Items.Clear();
        foreach (var session in _history.All)
        {
            _cmbSession.Items.Add(new SessionComboItem(session));
        }

        if (_cmbSession.Items.Count == 0)
        {
            _lblInfo.Text = "No saved sessions yet.";
            _clbDrivers.Items.Clear();
            _currentSession = null;
            _chartPanel.Invalidate();

            // Clearing the driver list raises no ItemCheck, so nothing else
            // would empty the Consistency table - it kept the rows of a
            // session that had just been deleted. RefreshStats with no
            // drivers ticked leaves it empty.
            RefreshStats();
            return;
        }

        var index = 0;
        if (previousId is not null)
        {
            for (var i = 0; i < _cmbSession.Items.Count; i++)
            {
                if (_cmbSession.Items[i] is SessionComboItem item && item.Session.Id == previousId)
                {
                    index = i;
                    break;
                }
            }
        }

        _cmbSession.SelectedIndex = index;
    }

    private void LoadSelectedSession()
    {
        _currentSession = (_cmbSession.SelectedItem as SessionComboItem)?.Session;
        _clbDrivers.Items.Clear();

        if (_currentSession is null)
        {
            _chartPanel.Invalidate();

            // As in RefreshAll: the list was just cleared without raising
            // ItemCheck, so the table has to be emptied here or it goes on
            // showing the previous session's drivers.
            RefreshStats();
            return;
        }

        // Ghost cars left out, by transponder: sessions saved before ghosts
        // were stripped from results still carry them, and their laps are
        // generated rather than driven.
        var withLaps = _currentSession.Results
            .Where(r => r.LapTimesMs.Count > 0 && !GhostService.IsGhost(r.Transponder))
            .ToList();

        foreach (var result in withLaps)
        {
            var i = _clbDrivers.Items.Add(new DriverComboItem(result));
            // Default to the first few checked - showing eight overlapping
            // lines at once by default would be unreadable.
            _clbDrivers.SetItemChecked(i, i < 3);
        }

        _lblInfo.Text = withLaps.Count == 0
            ? _currentSession.Results.Any(r => r.LapTimesMs.Count > 0)
                ? "Only ghost cars ran in this session - there are no real driver laps to chart."
                : "This session has no lap-by-lap data recorded."
            : $"{withLaps.Count} driver{(withLaps.Count == 1 ? "" : "s")} with lap data";

        _chartPanel.Invalidate();
        RefreshStats();
    }

    private List<SessionResultEntry> CheckedDrivers() =>
        _clbDrivers.CheckedItems.Cast<DriverComboItem>().Select(i => i.Result).ToList();

    private void ChartPanel_Paint(object? sender, PaintEventArgs e) =>
        DrawChart(e.Graphics, _chartPanel.ClientRectangle, ChartPalette.Screen());

    // WHY (20 Oct 2026): onScreen says whether this draw is the panel's own
    // paint. The export preview and the exported bitmap draw through here
    // too, at their own sizes, and they used to clear and refill _plotted -
    // so after an export the hover hit-tested the panel's cursor against
    // the bitmap's coordinates and showed the wrong lap over empty space.
    // Only the on-screen draw now touches the hover data.
    private void DrawChart(Graphics g, Rectangle bounds, ChartPalette palette, bool onScreen = true)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(palette.Background))
        {
            g.FillRectangle(bg, bounds);
        }

        var drivers = CheckedDrivers();

        // Flying laps only, as RefreshStats and PaceAnalysis.CountedLaps
        // count them. Index 0 of LapTimesMs is the run from the green to the
        // first crossing, not a lap: with the grid just behind the line it is
        // a second or so against laps of nine or ten, so plotting it pulled
        // the time axis down to fit it and squashed every real lap into the
        // top band of the chart - and the hover called it a "lap 1" quicker
        // than the Best in the table underneath. A lap of zero or less (a
        // jump start's placeholder) says nothing about pace either.
        var maxLaps = drivers.Count == 0 ? 0 : drivers.Max(d => d.LapTimesMs.Count);
        var allTimes = drivers.SelectMany(d => d.LapTimesMs.Skip(1)).Where(ms => ms > 0).ToList();

        if (_currentSession is null || drivers.Count == 0 || allTimes.Count == 0)
        {
            using var msgBrush = new SolidBrush(palette.Muted);
            var msg = _currentSession is null ? "Select a session."
                : drivers.Count == 0 ? "Tick a driver to plot their laps."
                : "No full laps to plot yet - only the start pass was recorded.";
            var size = g.MeasureString(msg, AppTheme.BodyFont);

            // Lifted by about half a line of the body face, at the text size
            // in use, so it stays centred as the face grows.
            g.DrawString(msg, AppTheme.BodyFont, msgBrush, bounds.Left + (bounds.Width - size.Width) / 2, bounds.Top + bounds.Height / 2f - UiLayout.AtTextSize(10));

            // Nothing is plotted, so nothing is left to hover over - the
            // points of the chart that was showing before would otherwise
            // still turn the cursor into a hand over empty space.
            if (onScreen)
            {
                _plotted.Clear();
            }
            return;
        }

        // The room round the plot holds the time labels down the left and
        // the lap numbers and "Lap" under it, all in the small face - so it
        // grows with the text size, or at 200% the labels run into the plot
        // and off the bottom. Worked out on every paint, so a new text size
        // is picked up by the repaint it brings.
        var padLeft = UiLayout.AtTextSize(64);
        var padRight = UiLayout.AtTextSize(16);
        var padTop = UiLayout.AtTextSize(16);
        var padBottom = UiLayout.AtTextSize(40);

        var plot = new Rectangle(
            bounds.Left + padLeft, bounds.Top + padTop,
            Math.Max(10, bounds.Width - padLeft - padRight),
            Math.Max(10, bounds.Height - padTop - padBottom));

        var minMs = allTimes.Min();
        var maxMs = allTimes.Max();

        // Pad the range slightly so lines never sit exactly on the frame,
        // and guard against a flat range (every lap identical) dividing by zero.
        var span = Math.Max(1, maxMs - minMs);
        var lowMs = minMs - span * 0.1;
        var highMs = maxMs + span * 0.1;
        var range = Math.Max(1, highMs - lowMs);

        using var axisPen = new Pen(palette.Axis);
        using var gridPen = new Pen(palette.Grid);
        using var labelBrush = new SolidBrush(palette.Muted);

        // The labels' offsets from their lines are fractions of a line of
        // the small face they are drawn in, so they follow the text size
        // with it - a time label lifted a fixed 7px sits below its gridline
        // once the face is twice as tall.
        var labelGap = UiLayout.AtTextSize(6);
        var halfLabel = UiLayout.AtTextSize(7);

        // Horizontal gridlines + y-axis time labels.
        for (var i = 0; i <= 4; i++)
        {
            var y = plot.Bottom - (plot.Height * i / 4f);
            g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            var valueMs = lowMs + range * i / 4.0;
            g.DrawString($"{valueMs / 1000.0:0.00}s", AppTheme.SmallFont, labelBrush, bounds.Left + labelGap, y - halfLabel);
        }

        g.DrawLine(axisPen, plot.Left, plot.Top, plot.Left, plot.Bottom);
        g.DrawLine(axisPen, plot.Left, plot.Bottom, plot.Right, plot.Bottom);

        // The x axis runs over the flying laps, LapTimesMs index 1 to the
        // last, so the first real lap sits on the left edge. Each is labeled
        // index + 1, the same lap number the leaderboard and the lap windows
        // give it (they show index 0 as the start pass), so "lap 5" here is
        // "L5" there.
        var flyingCount = Math.Max(1, maxLaps - 1);
        float LapX(int lapIndex) =>
            plot.Left + (flyingCount == 1 ? plot.Width / 2f : plot.Width * (lapIndex - 1f) / (flyingCount - 1f));

        // X-axis lap labels, thinned out so they never overlap.
        var labelStep = Math.Max(1, flyingCount / 10);
        for (var lapIndex = 1; lapIndex <= flyingCount; lapIndex += labelStep)
        {
            var x = LapX(lapIndex);
            g.DrawString((lapIndex + 1).ToString(), AppTheme.SmallFont, labelBrush, x - labelGap, plot.Bottom + labelGap);
        }

        // "Lap" a line below the lap numbers, at the text size in use.
        using var axisLabelBrush = new SolidBrush(palette.Muted);
        g.DrawString("Lap", AppTheme.SmallFont, axisLabelBrush,
            plot.Left + plot.Width / 2f - UiLayout.AtTextSize(12), plot.Bottom + UiLayout.AtTextSize(22));

        // Rebuilt every paint so hover data can never describe a stale
        // chart - only the on-screen render is hit-tested.
        if (onScreen)
        {
            _plotted.Clear();
        }

        // A lap's dot, and the legend's rows, at the text size in use: the
        // legend's names are in the small face, so rows a fixed 18px apart
        // overlapped once it grew, and the dots and swatches beside them
        // grow in step so the chart keeps its proportions.
        var dot = (float)UiLayout.AtTextSize(5f);
        var legendTop = UiLayout.AtTextSize(4);
        var legendRow = UiLayout.AtTextSize(18);
        var swatchLeft = UiLayout.AtTextSize(8);
        var swatchDrop = UiLayout.AtTextSize(4);
        var swatchWidth = UiLayout.AtTextSize(14);
        var swatchHeight = UiLayout.AtTextSize(4);
        var legendTextLeft = UiLayout.AtTextSize(26);

        // One polyline per driver.
        for (var d = 0; d < drivers.Count; d++)
        {
            var driver = drivers[d];
            var color = palette.Series[d % palette.Series.Length];
            using var linePen = new Pen(color, 2f);
            using var dotBrush = new SolidBrush(color);

            var points = new List<PointF>();
            for (var lap = 1; lap < driver.LapTimesMs.Count; lap++)
            {
                // Same filter as the axis range above, so no point can fall
                // outside the plot.
                if (driver.LapTimesMs[lap] <= 0)
                {
                    continue;
                }

                var x = LapX(lap);
                var y = (float)(plot.Bottom - (driver.LapTimesMs[lap] - lowMs) / range * plot.Height);
                points.Add(new PointF(x, y));
                if (onScreen)
                {
                    _plotted.Add((new PointF(x, y), driver.DriverName, lap + 1, driver.LapTimesMs[lap], color));
                }
            }

            if (points.Count > 1)
            {
                g.DrawLines(linePen, points.ToArray());
            }

            foreach (var p in points)
            {
                g.FillEllipse(dotBrush, p.X - dot / 2f, p.Y - dot / 2f, dot, dot);
            }

            // Legend entry, stacked down the top-left inside the plot.
            var legendY = plot.Top + legendTop + d * legendRow;
            g.FillRectangle(dotBrush, plot.Left + swatchLeft, legendY + swatchDrop, swatchWidth, swatchHeight);
            using var legendBrush = new SolidBrush(palette.Text);
            g.DrawString(driver.DriverName, AppTheme.SmallFont, legendBrush, plot.Left + legendTextLeft, legendY);
        }

        // WHY (20 Oct 2026): the hover point is in the panel's coordinates,
        // so on the export preview it ringed a spot that had nothing to do
        // with the smaller chart there. It is an on-screen aid only.
        if (onScreen)
        {
            DrawHoverReadout(g, bounds, palette);
        }
    }

    /// <summary>
    /// Draws the hover readout: driver, lap number and exact lap time.
    /// Added because reading a precise value off the plot was guesswork -
    /// which made hand-checking the qualifying maths impractical.
    /// </summary>

    /// <summary>
    /// Finds the nearest plotted point to the cursor and shows its exact
    /// time. Only repaints when the highlighted lap actually changes -
    /// invalidating on every mouse move would redraw the whole chart
    /// dozens of times a second.
    /// </summary>
    private void ChartPanel_MouseMove(object? sender, MouseEventArgs e)
    {
        if (_plotted.Count == 0)
        {
            return;
        }

        const float maxDistance = 14f;
        var bestDistance = float.MaxValue;
        (string Driver, int Lap, long Ms, Color Color, PointF At)? best = null;

        foreach (var (at, driver, lap, ms, color) in _plotted)
        {
            var dx = at.X - e.X;
            var dy = at.Y - e.Y;
            var distance = MathF.Sqrt(dx * dx + dy * dy);

            if (distance < bestDistance && distance <= maxDistance)
            {
                bestDistance = distance;
                best = (driver, lap, ms, color, at);
            }
        }

        if (best?.Driver == _hover?.Driver && best?.Lap == _hover?.Lap)
        {
            return;
        }

        _hover = best;
        _chartPanel.Cursor = best is null ? Cursors.Default : Cursors.Hand;
        _chartPanel.Invalidate();
    }

    private void DrawHoverReadout(Graphics g, Rectangle bounds, ChartPalette palette)
    {
        if (_hover is not { } h)
        {
            return;
        }

        var text = $"{h.Driver}  -  lap {h.Lap}  -  {h.Ms / 1000.0:0.000}s";

        // Nine points at the text size in use - no shared face is nine
        // points at the normal size, and a fixed 9pt stayed small at 200%
        // beside axis labels twice its size.
        using var font = new Font(AppTheme.BoldFont.FontFamily, 9F * AppTheme.FontScale, FontStyle.Bold);
        var size = TextRenderer.MeasureText(text, font);

        // The box's padding, its distance from the point and from the
        // chart's edges, and the ring and swatch in it, all at the text size
        // in use, so the box grows round the larger text rather than
        // pinching it.
        var padX = UiLayout.AtTextSize(18);
        var padY = UiLayout.AtTextSize(10);
        var offsetX = UiLayout.AtTextSize(12);
        var offsetY = UiLayout.AtTextSize(10);
        var dropBelow = UiLayout.AtTextSize(14);
        var edge = UiLayout.AtTextSize(4);
        var ring = (float)UiLayout.AtTextSize(11f);
        var swatchLeft = UiLayout.AtTextSize(6);
        var swatchSize = UiLayout.AtTextSize(6);
        var textLeft = UiLayout.AtTextSize(16);

        var width = size.Width + padX;
        var height = size.Height + padY;

        // Sits just above and to the right of the point so the value reads
        // next to the data it describes, rather than parked in a corner.
        var x = (int)h.At.X + offsetX;
        var y = (int)h.At.Y - height - offsetY;

        // Flip sides or below when that would run off the plot, so the box
        // stays fully visible near the edges.
        if (x + width > bounds.Right - edge)
        {
            x = (int)h.At.X - width - offsetX;
        }
        if (y < bounds.Top + edge)
        {
            y = (int)h.At.Y + dropBelow;
        }

        x = Math.Clamp(x, bounds.Left + edge, Math.Max(bounds.Left + edge, bounds.Right - width - edge));
        y = Math.Clamp(y, bounds.Top + edge, Math.Max(bounds.Top + edge, bounds.Bottom - height - edge));

        var box = new Rectangle(x, y, width, height);

        // Ring the hovered point so it's obvious which one is being read.
        using (var marker = new Pen(h.Color, 2f))
        {
            g.DrawEllipse(marker, h.At.X - ring / 2f, h.At.Y - ring / 2f, ring, ring);
        }

        // Leader line from the point to the box.
        using (var leader = new Pen(Color.FromArgb(150, h.Color), 1f))
        {
            g.DrawLine(leader, h.At.X, h.At.Y,
                box.Left + box.Width / 2f < h.At.X ? box.Right : box.Left,
                box.Top + box.Height / 2f);
        }

        using (var back = new SolidBrush(palette.Background))
        {
            g.FillRectangle(back, box);
        }
        using (var border = new Pen(h.Color, 1.6f))
        {
            g.DrawRectangle(border, box);
        }
        using (var swatch = new SolidBrush(h.Color))
        {
            g.FillEllipse(swatch, box.Left + swatchLeft, box.Top + box.Height / 2 - swatchSize / 2, swatchSize, swatchSize);
        }

        TextRenderer.DrawText(g, text, font,
            new Rectangle(box.Left + textLeft, box.Top, box.Width - padX, box.Height),
            palette.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }

    private void ExportChart()
    {
        if (_currentSession is null || CheckedDrivers().Count == 0)
        {
            AppDialog.Show(this, "Select a session and tick at least one driver first.", "Nothing to export",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var optionsForm = new ChartExportForm(_settings, (g, b, p) => DrawChart(g, b, p, onScreen: false));
        if (optionsForm.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png",
            FileName = $"lap-chart-{_currentSession.StartedAtUtc.ToLocalTime():yyyy-MM-dd_HH-mm}.png",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var size = optionsForm.Options.ImageSize;
            var palette = ChartPalette.For(optionsForm.Options.StyleKey);

            // Clear the hover box first - it's an on-screen aid, not
            // something that belongs in an exported image.
            var savedHover = _hover;
            _hover = null;

            using var bitmap = new Bitmap(size.Width, size.Height);

            // A new bitmap takes the screen's DPI, and in a DPI-aware run
            // (144 DPI at 150%) that would draw the chart's point-size text
            // half as big again inside the same pixels. At 96 DPI, which is
            // what a DPI-unaware run always sees, this changes nothing.
            bitmap.SetResolution(96, 96);

            // WHY (20 Oct 2026): the hover is put back in a finally, so a
            // draw that throws no longer leaves the on-screen readout lost.
            try
            {
                using var g = Graphics.FromImage(bitmap);
                DrawChart(g, new Rectangle(0, 0, size.Width, size.Height), palette, onScreen: false);
            }
            finally
            {
                _hover = savedHover;
            }

            bitmap.Save(dialog.FileName, System.Drawing.Imaging.ImageFormat.Png);

            AppDialog.Show(this, $"Saved to:\n{dialog.FileName}", "Chart saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, $"Couldn't save the chart: {ex.Message}", "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private sealed record SessionComboItem(SessionRecord Session)
    {
        public override string ToString()
        {
            var track = string.IsNullOrWhiteSpace(Session.TrackLayoutName) ? "" : $" - {Session.TrackLayoutName}";
            var label = string.IsNullOrWhiteSpace(Session.Label) ? "" : $" - {Session.Label}";
            return $"{AppTime.DateAndTime(Session.StartedAtUtc.ToLocalTime())}{track}{label}";
        }
    }

    private sealed record DriverComboItem(SessionResultEntry Result)
    {
        public override string ToString() => $"{Result.DriverName} ({Plural.Of(Result.LapTimesMs.Count, "lap")})";
    }
}
