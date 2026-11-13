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
    private readonly CheckBox _chkIncludePractice = new ThemedCheckBox();
    private readonly Panel _chartPanel = new();
    private readonly Label _lblSummary = new();

    public TrendChartControl(SessionHistoryStore history, AppSettings settings)
    {
        _history = history;
        _settings = settings;

        Dock = DockStyle.Fill;

        // The body face, worn rather than set once: this page sits in a tab
        // whose row is drawn in the bold face, which it would otherwise
        // take. No MinimumSize: the 900x560 floor was honoured over the size
        // the page was given. A short window scrolls the page instead
        // (FillPage).
        _textSize.Wear(this, () => AppTheme.BodyFont);

        BuildLayout();
        RefreshAll();
    }

    /// <summary>What this page keeps in step with the text size - see TextSize.</summary>
    private readonly TextSize _textSize = new();

    /// <summary>The pickers' width at the normal text size - never narrower, and as wide as their longest choice.</summary>
    private const int PickerLeastWidth = 200;

    /// <summary>The least the chart gets at the normal text size - below that the page scrolls.</summary>
    private const int ChartLeastHeight = 240;

    /// <summary>The pickers' widths as last measured - see Pieces.PickerWidth. Forgotten when the lists are refilled.</summary>
    private (Font Font, int Count, int Width)? _driverWidth;
    private (Font Font, int Count, int Width)? _trackWidth;

    /// <summary>The pickers' line - held so refilled lists can re-measure it.</summary>
    private FieldRow? _pickRow;

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
    /// The intro, then one card: the driver and track pickers, the summary,
    /// the chart taking the height that is left, and the export button. On
    /// the layout pieces (LayoutPieces.cs), so nothing is placed by hand.
    ///
    /// It used to be placed by hand over a 900x560 floor, the chart at a
    /// fixed 360px with the button placed under it.
    /// </summary>
    private void BuildLayout()
    {
        var lblIntro = new Label
        {
            Text = "Best lap per session over time, for one driver. Pick a track to see a real trend - across layouts it only " +
                   "shows which track is faster.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _cmbDriver.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbDriver.SelectedIndexChanged += (_, _) => Redraw();

        _cmbTrack.DropDownStyle = ComboBoxStyle.DropDownList;
        _cmbTrack.SelectedIndexChanged += (_, _) => Redraw();

        _chkIncludePractice.Text = "Include practice sessions";
        _chkIncludePractice.AutoSize = true;
        _chkIncludePractice.Checked = true;
        _chkIncludePractice.CheckedChanged += (_, _) => Redraw();

        var pickRow = new FieldRow();

        pickRow.Add(new Label { Text = "Driver:" }, _cmbDriver, () => PickerWidth(_cmbDriver, ref _driverWidth));
        pickRow.Add(new Label { Text = "Track:" }, _cmbTrack, () => PickerWidth(_cmbTrack, ref _trackWidth));
        pickRow.Add(null, _chkIncludePractice);
        _pickRow = pickRow;

        // What the points add up to, in the bold face - wrapping rather
        // than cut short.
        _lblSummary.AutoSize = true;
        _textSize.Wear(_lblSummary, () => AppTheme.BoldFont);

        _chartPanel.BackColor = AppTheme.Surface;
        _chartPanel.Paint += (_, e) => DrawChart(e.Graphics, _chartPanel.ClientRectangle, ChartPalette.Screen());

        // It draws its axes and labels to its size, so a resize is a
        // repaint - and buffered, so that repaint does not flicker.
        AppTheme.EnableDoubleBuffering(_chartPanel);
        _chartPanel.Resize += (_, _) => _chartPanel.Invalidate();

        var btnExport = new Button { Text = "Save Chart as Image..." };
        btnExport.Click += (_, _) => ExportChart();

        var trend = new Section("Trend");

        trend.Add(pickRow);
        trend.Add(_lblSummary);
        trend.AddFill(_chartPanel, () => UiLayout.AtTextSize(ChartLeastHeight));
        trend.Add(new ButtonBar().With(btnExport), gapBelow: 0);

        var page = new FillPage();

        page.Add(lblIntro);
        page.AddFill(trend, trend.LeastHeight, gapBelow: 0);

        Controls.Add(page);
    }

    /// <summary>A picker: as wide as its longest choice, and never under its width at the normal text size.</summary>
    private static int PickerWidth(ComboBox picker, ref (Font Font, int Count, int Width)? kept) =>
        Math.Max(UiLayout.AtTextSize(PickerLeastWidth), Pieces.PickerWidth(picker, ref kept));

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
            .Where(s => !s.IsNotScored())
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
            .Where(s => !s.IsNotScored())
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

        // The longest choice may have come or gone: measured again, and the
        // line with it.
        _driverWidth = null;
        _trackWidth = null;
        _pickRow?.PerformLayout();

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
            if (session.IsNotScored())
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
                DayLabel(session.StartedAtUtc.ToLocalTime(), System.Globalization.CultureInfo.CurrentCulture)));
        }

        return points;
    }

    /// <summary>
    /// A session's day under its point, day and month in the order this PC
    /// writes dates: a fixed month/day read as the wrong date wherever the
    /// day comes first - 3 November as "11/3", which is 11 March there.
    /// </summary>
    internal static string DayLabel(DateTime local, System.Globalization.CultureInfo culture)
    {
        var pattern = culture.DateTimeFormat.ShortDatePattern;
        var day = pattern.IndexOf('d');
        var month = pattern.IndexOf('M');

        return local.ToString(day >= 0 && month >= 0 && day < month ? "d/M" : "M/d", culture);
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

            // Lifted by about half a line of the body face, at the text size
            // in use, so it stays centred as the face grows.
            g.DrawString(msg, AppTheme.BodyFont, msgBrush, bounds.Left + (bounds.Width - size.Width) / 2, bounds.Top + bounds.Height / 2f - UiLayout.AtTextSize(10));
            return;
        }

        // The room round the plot holds the time labels down the left and
        // the dates under it, all in the small face - so it grows with the
        // text size, or at 200% the labels run into the plot and off the
        // bottom. Worked out on every paint, so a new text size is picked up
        // by the repaint it brings.
        var padLeft = UiLayout.AtTextSize(70);
        var padRight = UiLayout.AtTextSize(20);
        var padTop = UiLayout.AtTextSize(20);
        var padBottom = UiLayout.AtTextSize(44);
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

        // The labels' offsets from their lines are fractions of a line of
        // the small face they are drawn in, so they follow the text size
        // with it - a time label lifted a fixed 7px sits below its gridline
        // once the face is twice as tall.
        var labelGap = UiLayout.AtTextSize(8);
        var halfLabel = UiLayout.AtTextSize(7);

        for (var i = 0; i <= 4; i++)
        {
            var y = plot.Bottom - plot.Height * i / 4f;
            g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            g.DrawString($"{(low + range * i / 4.0) / 1000.0:0.00}s", AppTheme.SmallFont, labelBrush, bounds.Left + labelGap, y - halfLabel);
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
            // Its room back from the right edge and up from the line, at the
            // text size in use - the words grow with the small face, and a
            // fixed 76px cut them off at the plot's edge at 200%.
            g.DrawString("all-time best", AppTheme.SmallFont, bestBrush,
                plot.Right - UiLayout.AtTextSize(76), bestY - UiLayout.AtTextSize(16));
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

        // The dots at the text size in use, in step with the labels round
        // them, so the chart keeps its proportions as the text grows.
        var bestDot = (float)UiLayout.AtTextSize(8f);
        var dot = (float)UiLayout.AtTextSize(5f);

        for (var i = 0; i < linePoints.Length; i++)
        {
            var isBest = points[i].BestLapMs == minMs;
            var brush = isBest ? pbBrush : dotBrush;
            var size = isBest ? bestDot : dot;
            g.FillEllipse(brush, linePoints[i].X - size / 2, linePoints[i].Y - size / 2, size, size);
        }

        // Thin out date labels so they never overlap on a long history.
        // Centred under their points by about half a date's width, and
        // dropped clear of the axis, at the text size in use.
        var labelStep = Math.Max(1, points.Count / 12);
        var halfDate = UiLayout.AtTextSize(14);
        for (var i = 0; i < points.Count; i += labelStep)
        {
            g.DrawString(points[i].Label, AppTheme.SmallFont, labelBrush, xFor(i) - halfDate, plot.Bottom + labelGap);
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

            // A new bitmap takes the screen's DPI, and in a DPI-aware run
            // (144 DPI at 150%) that would draw the chart's point-size text
            // half as big again inside the same pixels. At 96 DPI, which is
            // what a DPI-unaware run always sees, this changes nothing.
            bitmap.SetResolution(96, 96);

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
