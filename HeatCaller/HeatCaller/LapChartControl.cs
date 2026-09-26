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
    private readonly CheckedListBox _clbDrivers = new();
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
        MinimumSize = new Size(900, 640);
        Font = AppTheme.BodyFont;

        BuildLayout();
        RefreshAll();
    }

    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "Lap-by-lap pace for a saved race. Tick drivers on the left to compare them. Sessions recorded before " +
                   "lap-by-lap tracking was added won't have chart data - only races run from now on will.",
            Location = new Point(12, 12),
            AutoSize = true,
            MaximumSize = new Size(860, 0),
            ForeColor = AppTheme.TextMuted,
        };

        // Measured off the caption - see UiLayout.After.
        var lblSession = new Label { Text = "Session:", Location = new Point(12, 62), AutoSize = true };
        _cmbSession.Width = 420;
        _cmbSession.Location = new Point(UiLayout.After(lblSession), 59);
        _cmbSession.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbSession.SelectedIndexChanged += (_, _) => LoadSelectedSession();

        // After the box rather than at a flat 520, which assumed the box
        // began at 100 - it begins wherever "Session:" actually ends.
        _lblInfo.Location = new Point(_cmbSession.Right + UiLayout.SectionGap, 62);
        _lblInfo.AutoSize = true;
        _lblInfo.ForeColor = AppTheme.TextMuted;

        var lblDrivers = new Label { Text = "Drivers:", Location = new Point(12, 96), AutoSize = true, Font = AppTheme.BoldFont };
        _clbDrivers.Location = new Point(12, 118);
        _clbDrivers.Size = new Size(220, 340);
        _clbDrivers.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        _clbDrivers.CheckOnClick = true;
        _clbDrivers.ItemCheck += (_, _) =>
        {
            if (!IsHandleCreated || IsDisposed)
            {
                return;
            }

            BeginInvoke(() => { _chartPanel.Invalidate(); RefreshStats(); });
        };

        _chartPanel.Location = new Point(248, 118);
        _chartPanel.Size = new Size(640, 340);
        // Top/Left/Right only - with Bottom anchoring the chart grew
        // downward on a taller window and painted over the export button.
        _chartPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
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

        var btnExport = new Button { Text = "Save Chart as Image...", Location = new Point(12, 470), Width = 220, Anchor = AnchorStyles.Top | AnchorStyles.Left };
        btnExport.Click += (_, _) => ExportChart();

        // The font is set here so the caption's height - which is what the
        // first row inside has to clear - is measured against the face this
        // box is actually drawn in. The table sat at a flat 22, which was
        // under a smaller caption than this one, and the box was a pixel
        // short of holding it.
        var grpStats = new GroupBox
        {
            Text = "Consistency",
            Location = new Point(448, 466),
            Size = new Size(440, 150),
            Font = AppTheme.BodyFont,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
        };

        _statsTable.Location = new Point(12, UiLayout.InsideGroup(grpStats));
        _statsTable.Size = new Size(416, 118);
        grpStats.Height = Math.Max(grpStats.Height, UiLayout.GroupHeightFor(_statsTable.Bottom));
        _statsTable.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
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
        grpStats.Controls.Add(_statsTable);

        Controls.Add(lblIntro);
        Controls.Add(lblSession);
        Controls.Add(_cmbSession);
        Controls.Add(_lblInfo);
        Controls.Add(lblDrivers);
        Controls.Add(_clbDrivers);
        Controls.Add(_chartPanel);
        Controls.Add(btnExport);
        Controls.Add(grpStats);
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

    private void DrawChart(Graphics g, Rectangle bounds, ChartPalette palette)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(palette.Background))
        {
            g.FillRectangle(bg, bounds);
        }

        var drivers = CheckedDrivers();
        if (_currentSession is null || drivers.Count == 0)
        {
            using var msgBrush = new SolidBrush(palette.Muted);
            var msg = _currentSession is null ? "Select a session." : "Tick a driver to plot their laps.";
            var size = g.MeasureString(msg, AppTheme.BodyFont);
            g.DrawString(msg, AppTheme.BodyFont, msgBrush, bounds.Left + (bounds.Width - size.Width) / 2, bounds.Top + bounds.Height / 2f - 10);
            return;
        }

        const int padLeft = 64;
        const int padRight = 16;
        const int padTop = 16;
        const int padBottom = 40;

        var plot = new Rectangle(
            bounds.Left + padLeft, bounds.Top + padTop,
            Math.Max(10, bounds.Width - padLeft - padRight),
            Math.Max(10, bounds.Height - padTop - padBottom));

        var maxLaps = drivers.Max(d => d.LapTimesMs.Count);
        var allTimes = drivers.SelectMany(d => d.LapTimesMs).ToList();
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

        // Horizontal gridlines + y-axis time labels.
        for (var i = 0; i <= 4; i++)
        {
            var y = plot.Bottom - (plot.Height * i / 4f);
            g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            var valueMs = lowMs + range * i / 4.0;
            g.DrawString($"{valueMs / 1000.0:0.00}s", AppTheme.SmallFont, labelBrush, bounds.Left + 6, y - 7);
        }

        g.DrawLine(axisPen, plot.Left, plot.Top, plot.Left, plot.Bottom);
        g.DrawLine(axisPen, plot.Left, plot.Bottom, plot.Right, plot.Bottom);

        // X-axis lap labels, thinned out so they never overlap.
        var labelStep = Math.Max(1, maxLaps / 10);
        for (var lap = 1; lap <= maxLaps; lap += labelStep)
        {
            var x = plot.Left + (maxLaps == 1 ? plot.Width / 2f : plot.Width * (lap - 1f) / (maxLaps - 1f));
            g.DrawString(lap.ToString(), AppTheme.SmallFont, labelBrush, x - 6, plot.Bottom + 6);
        }

        using var axisLabelBrush = new SolidBrush(palette.Muted);
        g.DrawString("Lap", AppTheme.SmallFont, axisLabelBrush, plot.Left + plot.Width / 2f - 12, plot.Bottom + 22);

        // Rebuilt every paint so hover data can never describe a stale
        // chart - only the on-screen render is hit-tested.
        _plotted.Clear();

        // One polyline per driver.
        for (var d = 0; d < drivers.Count; d++)
        {
            var driver = drivers[d];
            var color = palette.Series[d % palette.Series.Length];
            using var linePen = new Pen(color, 2f);
            using var dotBrush = new SolidBrush(color);

            var points = new List<PointF>();
            for (var lap = 0; lap < driver.LapTimesMs.Count; lap++)
            {
                var x = plot.Left + (maxLaps == 1 ? plot.Width / 2f : plot.Width * lap / (maxLaps - 1f));
                var y = (float)(plot.Bottom - (driver.LapTimesMs[lap] - lowMs) / range * plot.Height);
                points.Add(new PointF(x, y));
                _plotted.Add((new PointF(x, y), driver.DriverName, lap + 1, driver.LapTimesMs[lap], color));
            }

            if (points.Count > 1)
            {
                g.DrawLines(linePen, points.ToArray());
            }

            foreach (var p in points)
            {
                g.FillEllipse(dotBrush, p.X - 2.5f, p.Y - 2.5f, 5f, 5f);
            }

            // Legend entry, stacked down the top-left inside the plot.
            var legendY = plot.Top + 4 + d * 18;
            g.FillRectangle(dotBrush, plot.Left + 8, legendY + 4, 14, 4);
            using var legendBrush = new SolidBrush(palette.Text);
            g.DrawString(driver.DriverName, AppTheme.SmallFont, legendBrush, plot.Left + 26, legendY);
        }

        DrawHoverReadout(g, bounds, palette);
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
        using var font = new Font(AppTheme.BoldFont.FontFamily, 9F, FontStyle.Bold);
        var size = TextRenderer.MeasureText(text, font);

        var width = size.Width + 18;
        var height = size.Height + 10;

        // Sits just above and to the right of the point so the value reads
        // next to the data it describes, rather than parked in a corner.
        var x = (int)h.At.X + 12;
        var y = (int)h.At.Y - height - 10;

        // Flip sides or below when that would run off the plot, so the box
        // stays fully visible near the edges.
        if (x + width > bounds.Right - 4)
        {
            x = (int)h.At.X - width - 12;
        }
        if (y < bounds.Top + 4)
        {
            y = (int)h.At.Y + 14;
        }

        x = Math.Clamp(x, bounds.Left + 4, Math.Max(bounds.Left + 4, bounds.Right - width - 4));
        y = Math.Clamp(y, bounds.Top + 4, Math.Max(bounds.Top + 4, bounds.Bottom - height - 4));

        var box = new Rectangle(x, y, width, height);

        // Ring the hovered point so it's obvious which one is being read.
        using (var marker = new Pen(h.Color, 2f))
        {
            g.DrawEllipse(marker, h.At.X - 5.5f, h.At.Y - 5.5f, 11f, 11f);
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
            g.FillEllipse(swatch, box.Left + 6, box.Top + box.Height / 2 - 3, 6, 6);
        }

        TextRenderer.DrawText(g, text, font,
            new Rectangle(box.Left + 16, box.Top, box.Width - 18, box.Height),
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

        using var optionsForm = new ChartExportForm(_settings, DrawChart);
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
            using (var g = Graphics.FromImage(bitmap))
            {
                DrawChart(g, new Rectangle(0, 0, size.Width, size.Height), palette);
            }

            _hover = savedHover;
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
