namespace HeatCaller;

/// <summary>
/// Plots a driver's best lap per session over time, so improvement (or a
/// plateau, or a bad stretch) is visible across weeks and months.
///
/// Distinct from the Lap Times chart, which shows pace *within* a single
/// race. This is the long view: one point per session, x-axis is calendar
/// time rather than lap number.
///
/// Filtered by track, because comparing a fast layout against a slow one
/// produces a sawtooth that hides any real trend - progress only means
/// something against the same piece of track.
/// </summary>
public sealed class TrendChartControl : UserControl
{
    private readonly SessionHistoryStore _history;
    private readonly AppSettings _settings;

    private readonly ComboBox _cmbDriver = new();
    private readonly ComboBox _cmbTrack = new();
    private readonly CheckBox _chkIncludePractice = new();
    private readonly Panel _chartPanel = new();
    private readonly Label _lblSummary = new();

    public TrendChartControl(SessionHistoryStore history, AppSettings settings)
    {
        _history = history;
        _settings = settings;

        Dock = DockStyle.Fill;
        MinimumSize = new Size(900, 560);
        Font = AppTheme.BodyFont;

        BuildLayout();
        RefreshAll();
    }

    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "Best lap per session over time, for one driver on one track. Comparing across different layouts would " +
                   "just show which track is faster, not whether anyone's improving - so pick a track to see a real trend.",
            Location = new Point(12, 12),
            AutoSize = true,
            MaximumSize = new Size(860, 0),
            ForeColor = AppTheme.TextMuted,
        };

        // Run left to right, not stepped 72 / 292 / 344 / 564. Those were
        // spaced for a smaller font, so "Track:" sat on the driver box and
        // its own box sat on "Track:".
        var lblDriver = new Label { Text = "Driver:", Location = new Point(12, 62), AutoSize = true };
        _cmbDriver.Width = 200;
        _cmbDriver.Location = new Point(UiLayout.After(lblDriver), 59);
        _cmbDriver.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbDriver.SelectedIndexChanged += (_, _) => Redraw();

        var lblTrack = new Label
        {
            Text = "Track:",
            Location = new Point(_cmbDriver.Right + UiLayout.SectionGap, 62),
            AutoSize = true,
        };

        _cmbTrack.Width = 200;
        _cmbTrack.Location = new Point(UiLayout.After(lblTrack), 59);
        _cmbTrack.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbTrack.SelectedIndexChanged += (_, _) => Redraw();

        _chkIncludePractice.Text = "Include practice sessions";
        _chkIncludePractice.Location = new Point(_cmbTrack.Right + UiLayout.SectionGap, 61);
        _chkIncludePractice.AutoSize = true;
        _chkIncludePractice.Checked = true;
        _chkIncludePractice.CheckedChanged += (_, _) => Redraw();

        _lblSummary.Location = new Point(12, 96);
        _lblSummary.AutoSize = true;
        _lblSummary.Font = AppTheme.BoldFont;

        _chartPanel.Location = new Point(12, 126);
        _chartPanel.Size = new Size(860, 360);
        // Top/Left/Right only - Bottom anchoring made the chart grow
        // over the export button below it.
        _chartPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _chartPanel.BackColor = AppTheme.Surface;
        _chartPanel.Paint += (_, e) => DrawChart(e.Graphics, _chartPanel.ClientRectangle, ChartPalette.Screen());

        var btnExport = new Button { Text = "Save Chart as Image...", Location = new Point(12, 498), Width = 180 };
        btnExport.Click += (_, _) => ExportChart();

        Controls.Add(lblIntro);
        Controls.Add(lblDriver);
        Controls.Add(_cmbDriver);
        Controls.Add(lblTrack);
        Controls.Add(_cmbTrack);
        Controls.Add(_chkIncludePractice);
        Controls.Add(_lblSummary);
        Controls.Add(_chartPanel);
        Controls.Add(btnExport);
    }

    private sealed record TrendPoint(DateTime When, long BestLapMs, string Label);

    /// <summary>Call when this page becomes visible - new sessions can be recorded while it's not active.</summary>
    public void RefreshAll()
    {
        var previousDriver = _cmbDriver.SelectedItem as string;
        var previousTrack = _cmbTrack.SelectedItem as string;

        // Saved demo races are left out throughout: their lap times are
        // invented, so they would draw a trend nobody actually drove, and
        // the names in them are made up.
        //
        // Ghost cars are left out too. Sessions saved before ghosts were
        // stripped from results still carry them, and a ghost's lap times
        // are generated - a trend for one is a line somebody typed in, not
        // anyone's improvement. They are told apart by transponder, the
        // same test every other page uses, because a ghost can be given
        // any name - including a real driver's.
        var drivers = _history.All
            .Where(s => !s.IsDemoSession())
            .SelectMany(s => s.Results)
            .Where(r => !GhostService.IsGhost(r.Transponder))
            .Select(r => r.DriverName)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n)
            .ToList();

        _cmbDriver.Items.Clear();
        _cmbDriver.Items.Add("(select a driver)");
        foreach (var d in drivers)
        {
            _cmbDriver.Items.Add(d);
        }

        var tracks = _history.All
            .Where(s => !s.IsDemoSession())
            .Select(s => s.TrackLayoutName)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(t => t)
            .ToList();

        _cmbTrack.Items.Clear();
        _cmbTrack.Items.Add("(all tracks)");
        foreach (var t in tracks)
        {
            _cmbTrack.Items.Add(t);
        }

        Restore(_cmbDriver, previousDriver);
        Restore(_cmbTrack, previousTrack);
    }

    private static void Restore(ComboBox combo, string? previous)
    {
        var idx = previous is not null ? combo.Items.IndexOf(previous) : -1;
        combo.SelectedIndex = idx >= 0 ? idx : 0;
    }

    private List<TrendPoint> BuildPoints()
    {
        var driver = _cmbDriver.SelectedIndex > 0 ? _cmbDriver.SelectedItem as string : null;
        if (driver is null)
        {
            return new List<TrendPoint>();
        }

        var trackFilter = _cmbTrack.SelectedIndex > 0 ? _cmbTrack.SelectedItem as string : null;

        var points = new List<TrendPoint>();

        // Oldest first so the line reads left-to-right chronologically.
        foreach (var session in _history.All.Reverse())
        {
            if (session.IsDemoSession())
            {
                continue;
            }

            if (!_chkIncludePractice.Checked && session.SessionType == RaceSessionType.Practice)
            {
                continue;
            }

            if (trackFilter is not null && !string.Equals(session.TrackLayoutName, trackFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            // Ghosts skipped here as well as in the list: a ghost named
            // after the driver would otherwise be picked up as them.
            var result = session.Results.FirstOrDefault(r =>
                !GhostService.IsGhost(r.Transponder)
                && string.Equals(r.DriverName, driver, StringComparison.OrdinalIgnoreCase));

            if (result is null || result.FastLapMs <= 0)
            {
                continue;
            }

            points.Add(new TrendPoint(
                session.StartedAtUtc.ToLocalTime(),
                result.FastLapMs,
                session.StartedAtUtc.ToLocalTime().ToString("M/d")));
        }

        return points;
    }

    private void Redraw()
    {
        var points = BuildPoints();

        if (points.Count == 0)
        {
            // Only the "(select a driver)" placeholder in the list means
            // there is nobody to pick at all - say why, rather than asking
            // for a choice that can't be made.
            _lblSummary.Text = _cmbDriver.SelectedIndex > 0
                ? "No sessions match those filters."
                : _cmbDriver.Items.Count <= 1
                    ? "No saved races for real drivers yet - demo races and ghost cars aren't counted."
                    : "Select a driver to see their trend.";
            _lblSummary.ForeColor = AppTheme.TextMuted;
        }
        else
        {
            var best = points.Min(p => p.BestLapMs);
            var first = points[0].BestLapMs;
            var latest = points[^1].BestLapMs;
            var delta = latest - first;

            var direction = points.Count < 2
                ? "only one session so far"
                : delta < 0
                    ? $"{Math.Abs(delta) / 1000.0:0.000}s faster than the first session"
                    : delta > 0
                        ? $"{delta / 1000.0:0.000}s slower than the first session"
                        : "level with the first session";

            _lblSummary.Text = $"{Plural.Of(points.Count, "session")}   -   all-time best {best / 1000.0:0.000}s   -   latest is {direction}";
            _lblSummary.ForeColor = delta < 0 && points.Count > 1 ? AppTheme.Success : AppTheme.TextDark;
        }

        _chartPanel.Invalidate();
    }

    private void DrawChart(Graphics g, Rectangle bounds, ChartPalette palette)
    {
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using (var bg = new SolidBrush(palette.Background))
        {
            g.FillRectangle(bg, bounds);
        }

        var points = BuildPoints();
        if (points.Count == 0)
        {
            using var msgBrush = new SolidBrush(palette.Muted);
            const string msg = "No data for these filters.";
            var size = g.MeasureString(msg, AppTheme.BodyFont);
            g.DrawString(msg, AppTheme.BodyFont, msgBrush, bounds.Left + (bounds.Width - size.Width) / 2, bounds.Top + bounds.Height / 2f - 10);
            return;
        }

        const int padLeft = 70, padRight = 20, padTop = 20, padBottom = 44;
        var plot = new Rectangle(
            bounds.Left + padLeft, bounds.Top + padTop,
            Math.Max(10, bounds.Width - padLeft - padRight),
            Math.Max(10, bounds.Height - padTop - padBottom));

        var minMs = points.Min(p => p.BestLapMs);
        var maxMs = points.Max(p => p.BestLapMs);
        var span = Math.Max(1, maxMs - minMs);
        var low = minMs - span * 0.12;
        var high = maxMs + span * 0.12;
        var range = Math.Max(1, high - low);

        using var gridPen = new Pen(palette.Grid);
        using var axisPen = new Pen(palette.Axis);
        using var labelBrush = new SolidBrush(palette.Muted);

        for (var i = 0; i <= 4; i++)
        {
            var y = plot.Bottom - plot.Height * i / 4f;
            g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            g.DrawString($"{(low + range * i / 4.0) / 1000.0:0.00}s", AppTheme.SmallFont, labelBrush, bounds.Left + 8, y - 7);
        }

        g.DrawLine(axisPen, plot.Left, plot.Top, plot.Left, plot.Bottom);
        g.DrawLine(axisPen, plot.Left, plot.Bottom, plot.Right, plot.Bottom);

        // Horizontal marker at the all-time best, so it's obvious whether a
        // session actually beat it or just came close.
        var bestY = (float)(plot.Bottom - (minMs - low) / range * plot.Height);
        using (var bestPen = new Pen(palette.Highlight, 1.4f) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash })
        {
            g.DrawLine(bestPen, plot.Left, bestY, plot.Right, bestY);
        }
        using (var bestBrush = new SolidBrush(palette.Highlight))
        {
            g.DrawString("all-time best", AppTheme.SmallFont, bestBrush, plot.Right - 76, bestY - 16);
        }

        var xFor = new Func<int, float>(i =>
            plot.Left + (points.Count == 1 ? plot.Width / 2f : plot.Width * i / (float)(points.Count - 1)));
        var yFor = new Func<long, float>(ms =>
            (float)(plot.Bottom - (ms - low) / range * plot.Height));

        var linePoints = points.Select((p, i) => new PointF(xFor(i), yFor(p.BestLapMs))).ToArray();

        using (var linePen = new Pen(palette.Series[0], 2.4f))
        {
            if (linePoints.Length > 1)
            {
                g.DrawLines(linePen, linePoints);
            }
        }

        using var dotBrush = new SolidBrush(palette.Series[0]);
        using var pbBrush = new SolidBrush(palette.Highlight);
        for (var i = 0; i < linePoints.Length; i++)
        {
            var isBest = points[i].BestLapMs == minMs;
            var brush = isBest ? pbBrush : dotBrush;
            var size = isBest ? 8f : 5f;
            g.FillEllipse(brush, linePoints[i].X - size / 2, linePoints[i].Y - size / 2, size, size);
        }

        // Thin out date labels so they never overlap on a long history.
        var labelStep = Math.Max(1, points.Count / 12);
        for (var i = 0; i < points.Count; i += labelStep)
        {
            g.DrawString(points[i].Label, AppTheme.SmallFont, labelBrush, xFor(i) - 14, plot.Bottom + 8);
        }
    }

    private void ExportChart()
    {
        if (BuildPoints().Count == 0)
        {
            AppDialog.Show(this, "Nothing to export - select a driver with session history first.", "Nothing to export",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var optionsForm = new ChartExportForm(_settings, DrawChart);
        if (optionsForm.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        // Only spaces used to be dealt with here, so a driver entered as
        // "AC/DC" or "Bob: The Builder" got a suggested file name Windows
        // won't take, from a dialog that sits outside the try below.
        var driver = PlainFileName.SafePart(_cmbDriver.SelectedItem as string, "driver").Replace(' ', '-');

        using var dialog = new SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png",
            FileName = $"trend-{driver}-{DateTime.Now:yyyy-MM-dd}.png",
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var size = optionsForm.Options.ImageSize;
            var palette = ChartPalette.For(optionsForm.Options.StyleKey);

            using var bitmap = new Bitmap(size.Width, size.Height);
            using (var g = Graphics.FromImage(bitmap))
            {
                DrawChart(g, new Rectangle(0, 0, size.Width, size.Height), palette);
            }
            bitmap.Save(dialog.FileName, System.Drawing.Imaging.ImageFormat.Png);

            AppDialog.Show(this, $"Saved to:\n{dialog.FileName}", "Chart saved", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppDialog.Show(this, $"Couldn't save the chart: {ex.Message}", "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

}
