using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>What the pack chart shows.</summary>
internal enum PackChartMode
{
    /// <summary>Each pack's fade profile: how far off its median it runs through each tenth of a race.</summary>
    Fade,

    /// <summary>Each pack's median lap race by race, over time.</summary>
    Trend,
}

/// <summary>
/// A driver's packs side by side on the Batteries tab (DriverBatteriesPanel),
/// from PackAnalytics: in Fade, a line per pack across the ten tenths of a
/// race, in seconds against that pack's own median - so a pack that drops
/// off at the end climbs on the right, whatever its pace; in Trend, each
/// pack's median lap race by race, over time.
///
/// Plain GDI+ in the app's chart palette (ChartPalette.Screen), as the Lap
/// Times and Trend charts are, with every size at the text size in use. A
/// pack keeps its colour in both modes: the colour is its place in the
/// list it is given.
/// </summary>
internal sealed class PackChartControl : UserControl
{
    private IReadOnlyList<PackStats> _packs = Array.Empty<PackStats>();
    private PackChartMode _mode = PackChartMode.Fade;

    public PackChartControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = AppTheme.Surface;
    }

    /// <summary>The packs drawn, in the order their colours are given.</summary>
    public IReadOnlyList<PackStats> Packs
    {
        get => _packs;
        set
        {
            _packs = value ?? Array.Empty<PackStats>();
            Invalidate();
        }
    }

    public PackChartMode Mode
    {
        get => _mode;
        set
        {
            if (_mode != value)
            {
                _mode = value;
                Invalidate();
            }
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Draw(e.Graphics, ClientRectangle, ChartPalette.Screen());
    }

    /// <summary>Draws the chart into <paramref name="bounds"/> - on screen, or into an image.</summary>
    public void Draw(Graphics g, Rectangle bounds, ChartPalette palette)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var bg = new SolidBrush(palette.Background))
        {
            g.FillRectangle(bg, bounds);
        }

        var colours = _packs.Select((p, i) => (Pack: p, Colour: palette.Series[i % palette.Series.Length])).ToList();
        var shown = _mode == PackChartMode.Fade
            ? colours.Where(c => c.Pack.Profile.Count == PackAnalytics.ProfileBuckets).ToList()
            : colours.Where(c => c.Pack.Runs.Any(r => r.MedianLap > 0)).ToList();

        if (shown.Count == 0)
        {
            Message(g, bounds, palette, _packs.Count == 0
                ? "Nothing to chart yet - shown once races are recorded on their packs."
                : "Not enough laps yet - a race needs three clean laps to chart.");
            return;
        }

        var legendBottom = Legend(g, bounds, palette, shown.Select(s => (s.Pack.Pack.DisplayName, s.Colour)).ToList());

        var padLeft = UiLayout.AtTextSize(64);
        var padRight = UiLayout.AtTextSize(16);
        var padBottom = UiLayout.AtTextSize(30);
        var top = legendBottom + UiLayout.AtTextSize(8);
        var plot = new Rectangle(
            bounds.Left + padLeft, top,
            Math.Max(10, bounds.Width - padLeft - padRight),
            Math.Max(10, bounds.Bottom - padBottom - top));

        if (_mode == PackChartMode.Fade)
        {
            DrawFade(g, bounds, plot, palette, shown);
        }
        else
        {
            DrawTrend(g, bounds, plot, palette, shown);
        }
    }

    private static void Message(Graphics g, Rectangle bounds, ChartPalette palette, string text)
    {
        using var brush = new SolidBrush(palette.Muted);
        using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString(text, AppTheme.BodyFont, brush, bounds, format);
    }

    /// <summary>The packs' names with their colours along the top, wrapping; gives where it ends.</summary>
    private static int Legend(Graphics g, Rectangle bounds, ChartPalette palette, List<(string Name, Color Colour)> entries)
    {
        var font = AppTheme.SmallFont;
        var lineHeight = (int)Math.Ceiling(font.GetHeight(g));
        var swatch = UiLayout.AtTextSize(14);
        var gap = UiLayout.AtTextSize(6);
        var spacing = UiLayout.AtTextSize(18);
        var left = bounds.Left + UiLayout.AtTextSize(12);
        var x = left;
        var y = bounds.Top + UiLayout.AtTextSize(8);

        using var textBrush = new SolidBrush(palette.Text);

        foreach (var (name, colour) in entries)
        {
            var width = swatch + gap + (int)Math.Ceiling(g.MeasureString(name, font).Width);

            if (x > left && x + width > bounds.Right - UiLayout.AtTextSize(12))
            {
                x = left;
                y += lineHeight + UiLayout.AtTextSize(4);
            }

            using (var pen = new Pen(colour, (float)UiLayout.AtTextSize(3f)))
            {
                var mid = y + lineHeight / 2f;
                g.DrawLine(pen, x, mid, x + swatch, mid);
            }

            g.DrawString(name, font, textBrush, x + swatch + gap, y);
            x += width + spacing;
        }

        return y + lineHeight;
    }

    private static void DrawFade(Graphics g, Rectangle bounds, Rectangle plot, ChartPalette palette, List<(PackStats Pack, Color Colour)> shown)
    {
        var values = shown.SelectMany(s => s.Pack.Profile).ToList();
        var reach = Math.Max(0.05, values.Max(v => Math.Abs(v))) * 1.15;
        var low = -reach;
        var range = reach * 2;

        float YFor(double v) => (float)(plot.Bottom - (v - low) / range * plot.Height);
        float XFor(int bucket) => plot.Left + plot.Width * bucket / (float)(PackAnalytics.ProfileBuckets - 1);

        Grid(g, bounds, plot, palette, low, range, v => v > 0 ? $"+{v:0.00}s" : v < 0 ? $"{v:0.00}s" : "0");

        using (var zero = new Pen(palette.Axis, 1.4f) { DashStyle = DashStyle.Dash })
        {
            g.DrawLine(zero, plot.Left, YFor(0), plot.Right, YFor(0));
        }

        using var labelBrush = new SolidBrush(palette.Muted);
        var labelGap = UiLayout.AtTextSize(6);
        g.DrawString("Start of the race", AppTheme.SmallFont, labelBrush, plot.Left, plot.Bottom + labelGap);

        const string end = "End  (slower is up)";
        var endWidth = g.MeasureString(end, AppTheme.SmallFont).Width;
        g.DrawString(end, AppTheme.SmallFont, labelBrush, plot.Right - endWidth, plot.Bottom + labelGap);

        var dot = (float)UiLayout.AtTextSize(5f);

        foreach (var (pack, colour) in shown)
        {
            var points = pack.Profile.Select((v, i) => new PointF(XFor(i), YFor(v))).ToArray();

            using var pen = new Pen(colour, (float)UiLayout.AtTextSize(2.2f));
            g.DrawLines(pen, points);

            using var brush = new SolidBrush(colour);

            foreach (var p in points)
            {
                g.FillEllipse(brush, p.X - dot / 2, p.Y - dot / 2, dot, dot);
            }
        }
    }

    private static void DrawTrend(Graphics g, Rectangle bounds, Rectangle plot, ChartPalette palette, List<(PackStats Pack, Color Colour)> shown)
    {
        var runs = shown.SelectMany(s => s.Pack.Runs.Where(r => r.MedianLap > 0)).ToList();
        var minTime = runs.Min(r => r.StartedUtc);
        var maxTime = runs.Max(r => r.StartedUtc);
        var span = (maxTime - minTime).TotalSeconds;

        var minLap = runs.Min(r => r.MedianLap);
        var maxLap = runs.Max(r => r.MedianLap);
        var pad = Math.Max(0.05, (maxLap - minLap) * 0.12);
        var low = minLap - pad;
        var range = maxLap + pad - low;

        float XFor(DateTime at) => span <= 0
            ? plot.Left + plot.Width / 2f
            : plot.Left + (float)((at - minTime).TotalSeconds / span * plot.Width);
        float YFor(double v) => (float)(plot.Bottom - (v - low) / range * plot.Height);

        Grid(g, bounds, plot, palette, low, range, v => $"{v:0.00}s");

        // The dates of the first and last race, and one between when there is room.
        using var labelBrush = new SolidBrush(palette.Muted);
        var labelGap = UiLayout.AtTextSize(6);
        var dates = span <= 0 ? new[] { minTime } : new[] { minTime, minTime.AddSeconds(span / 2), maxTime };

        foreach (var at in dates)
        {
            var text = DateLabel(at.ToLocalTime(), System.Globalization.CultureInfo.CurrentCulture);
            var width = g.MeasureString(text, AppTheme.SmallFont).Width;
            var x = DateLabelLeft(XFor(at), width, plot.Left, plot.Right);
            g.DrawString(text, AppTheme.SmallFont, labelBrush, x, plot.Bottom + labelGap);
        }

        var dot = (float)UiLayout.AtTextSize(6f);

        foreach (var (pack, colour) in shown)
        {
            var points = pack.Runs
                .Where(r => r.MedianLap > 0)
                .OrderBy(r => r.StartedUtc)
                .Select(r => new PointF(XFor(r.StartedUtc), YFor(r.MedianLap)))
                .ToArray();

            using var pen = new Pen(colour, (float)UiLayout.AtTextSize(2.2f));

            if (points.Length > 1)
            {
                g.DrawLines(pen, points);
            }

            using var brush = new SolidBrush(colour);

            foreach (var p in points)
            {
                g.FillEllipse(brush, p.X - dot / 2, p.Y - dot / 2, dot, dot);
            }
        }
    }

    // WHY (23 Nov 2026, bug hunt 24): the Trend dates were a fixed "M/d", so
    // on a PC that writes the day first 3 November showed as "11/3" - read
    // there as 11 March. They now follow the PC's own day/month order, the
    // same rule as the Trend chart next door (TrendChartControl.DayLabel).
    /// <summary>A Trend date label for a local time, day and month in the order this PC writes dates.</summary>
    internal static string DateLabel(DateTime local, System.Globalization.CultureInfo culture) =>
        TrendChartControl.DayLabel(local, culture);

    /// <summary>Five gridlines with their values down the left, and the axes.</summary>
    /// <summary>
    /// Where a Trend date label starts: centred on its date, kept inside the
    /// plot. (10 Nov 2026, bug hunt 16) This was Math.Clamp(centre - width / 2,
    /// left, right - width), which throws when the plot is narrower than the
    /// label - a narrow card at a large text size - and the throw came out of
    /// OnPaint. When the label does not fit it now starts at the plot's left
    /// edge and draws cramped instead.
    /// </summary>
    internal static float DateLabelLeft(float centre, float width, float left, float right) =>
        Math.Max(left, Math.Min(centre - width / 2, right - width));

    private static void Grid(Graphics g, Rectangle bounds, Rectangle plot, ChartPalette palette, double low, double range, Func<double, string> text)
    {
        using var gridPen = new Pen(palette.Grid);
        using var axisPen = new Pen(palette.Axis);
        using var labelBrush = new SolidBrush(palette.Muted);

        var halfLabel = AppTheme.SmallFont.GetHeight(g) / 2;

        for (var i = 0; i <= 4; i++)
        {
            var y = plot.Bottom - plot.Height * i / 4f;
            g.DrawLine(gridPen, plot.Left, y, plot.Right, y);
            g.DrawString(text(Math.Round(low + range * i / 4.0, 3)), AppTheme.SmallFont, labelBrush, bounds.Left + UiLayout.AtTextSize(6), y - halfLabel);
        }

        g.DrawLine(axisPen, plot.Left, plot.Top, plot.Left, plot.Bottom);
        g.DrawLine(axisPen, plot.Left, plot.Bottom, plot.Right, plot.Bottom);
    }
}
