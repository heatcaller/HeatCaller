namespace HeatCaller;

/// <summary>
/// The live track map, drawn: the racing line from the Track Designer, the
/// finish line and split loops across it, and a dot for each car moving
/// round at its pace - its race position in the dot - with a dotted ring
/// for each of the leaders' best laps (the best-lap ghost). A car that seems to have
/// stopped turns red and pulses where it stopped (TrackPositions), so a
/// marshal can be sent before anyone has asked where it went.
///
/// Reads its line and cars through <see cref="Source"/> on its own timer,
/// ten times a second while it is shown, so the dots move smoothly between
/// the Big Screen's refreshes.
/// </summary>
internal sealed class TrackMapView : Control
{
    /// <summary>The drivers' colours, the same ten the web pages' charts use, in race order.</summary>
    internal static readonly Color[] DriverColours =
    {
        ColorTranslator.FromHtml("#C8F031"), ColorTranslator.FromHtml("#22D3EE"), ColorTranslator.FromHtml("#FF9F45"),
        ColorTranslator.FromHtml("#4ADE80"), ColorTranslator.FromHtml("#B06CFF"), ColorTranslator.FromHtml("#FFE05C"),
        ColorTranslator.FromHtml("#F0483E"), ColorTranslator.FromHtml("#7CFF5C"), ColorTranslator.FromHtml("#5C9DFF"),
        ColorTranslator.FromHtml("#FF9ED2"),
    };

    private static readonly Color StoppedRed = Color.FromArgb(239, 68, 68);

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 100 };
    private readonly long _started = Environment.TickCount64;

    /// <summary>The line and the cars on it now, or no line when there is no plan to draw.</summary>
    public Func<(TrackLine? Line, IReadOnlyList<TrackPositions.Car> Cars)>? Source { get; set; }

    public Color Canvas { get; set; } = Color.Black;
    public Color Track { get; set; } = Color.FromArgb(60, 60, 60);
    public Color Ink { get; set; } = Color.White;
    public Color Muted { get; set; } = Color.Gray;

    /// <summary>
    /// The best-lap ghosts are drawn for the cars this high up the order - a
    /// faint ring in the car's colour where it would be on its best lap of
    /// the race. Ahead of its ring, a car is on a personal best; behind it,
    /// it isn't. Only the top few, so the map stays readable. 0 for none.
    /// </summary>
    public int GhostsFor { get; set; } = 3;

    public TrackMapView()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw, true);

        DoubleBuffered = true;
        TabStop = false;

        _timer.Tick += (_, _) => Invalidate();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);

        if (Visible)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using (var back = new SolidBrush(Canvas))
        {
            g.FillRectangle(back, ClientRectangle);
        }

        (TrackLine? Line, IReadOnlyList<TrackPositions.Car> Cars) now;

        try
        {
            now = Source?.Invoke() ?? (null, Array.Empty<TrackPositions.Car>());
        }
        catch
        {
            return;
        }

        if (now.Line is not { } line || Width < 40 || Height < 40)
        {
            return;
        }

        // The plan fitted to the panel, kept to its own shape.
        var margin = Math.Max(16f, Math.Min(Width, Height) / 12f);
        var scale = Math.Min((Width - 2 * margin) / line.Bounds.Width, (Height - 2 * margin) / line.Bounds.Height);
        var offset = new PointF(
            (Width - line.Bounds.Width * scale) / 2 - line.Bounds.Left * scale,
            (Height - line.Bounds.Height * scale) / 2 - line.Bounds.Top * scale);

        PointF Screen(PointF p) => new(offset.X + p.X * scale, offset.Y + p.Y * scale);

        var points = line.Points.Select(Screen).ToArray();
        var width = Math.Max(4f, Math.Min(Width, Height) / 28f);

        using (var pen = new Pen(Track, width) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round })
        {
            g.DrawLines(pen, points);
        }

        // The finish line and each split loop, across the line.
        Mark(g, line, 0f, Screen, width, Ink, "S/F");

        for (var i = 0; i < line.SplitAt.Count; i++)
        {
            Mark(g, line, line.SplitAt[i], Screen, width, Muted, $"S{i + 1}");
        }

        // Cars, last place first so the leader is drawn on top.
        var radius = Math.Max(7f, width * 1.15f);
        using var label = new Font(AppTheme.BoldFont.FontFamily, Math.Max(6f, radius * 0.95f), FontStyle.Bold, GraphicsUnit.Pixel);
        var pulse = (float)NextUpBar.PulseAt(Environment.TickCount64 - _started);

        // The ghosts first, under every car.
        foreach (var car in now.Cars.Where(c => c.Ghost >= 0 && !c.Stopped && c.Position <= GhostsFor))
        {
            var ghost = Screen(line.At(car.Ghost));
            var colour = DriverColours[(car.Position - 1) % DriverColours.Length];

            using var ghostPen = new Pen(Color.FromArgb(150, colour), Math.Max(1.5f, radius / 4)) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
            // Wider than a car, so it still shows round one running right on it.
            var ring = radius * 1.5f;
            g.DrawEllipse(ghostPen, ghost.X - ring, ghost.Y - ring, ring * 2, ring * 2);
        }

        foreach (var car in now.Cars.OrderByDescending(c => c.Position))
        {
            var at = Screen(line.At(car.Fraction));
            var colour = car.Stopped ? StoppedRed : DriverColours[(car.Position - 1) % DriverColours.Length];

            if (car.Stopped)
            {
                // A ring that swells round it, so it is found at a glance.
                var ring = radius * (1.6f + pulse * 1.4f);

                using var ringPen = new Pen(Color.FromArgb((int)(220 * (1 - pulse * 0.7f)), StoppedRed), Math.Max(2f, radius / 3));
                g.DrawEllipse(ringPen, at.X - ring, at.Y - ring, ring * 2, ring * 2);
            }

            using (var fill = new SolidBrush(colour))
            {
                g.FillEllipse(fill, at.X - radius, at.Y - radius, radius * 2, radius * 2);
            }

            using (var edge = new Pen(Canvas, Math.Max(1.5f, radius / 6)))
            {
                g.DrawEllipse(edge, at.X - radius, at.Y - radius, radius * 2, radius * 2);
            }

            var text = car.Stopped ? "!" : car.Position.ToString();
            var box = Rectangle.Round(new RectangleF(at.X - radius, at.Y - radius, radius * 2, radius * 2));

            TextRenderer.DrawText(g, text, label, box, car.Stopped ? Color.White : Color.Black,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
    }

    /// <summary>A short bar across the line at <paramref name="fraction"/> of the lap, with its name beside it.</summary>
    private void Mark(Graphics g, TrackLine line, float fraction, Func<PointF, PointF> screen, float width, Color colour, string name)
    {
        var at = screen(line.At(fraction));
        var ahead = screen(line.At(fraction + 0.005f));
        var dx = ahead.X - at.X;
        var dy = ahead.Y - at.Y;
        var length = MathF.Sqrt(dx * dx + dy * dy);

        if (length <= 0)
        {
            return;
        }

        // Square to the direction of travel.
        var nx = -dy / length * width * 1.4f;
        var ny = dx / length * width * 1.4f;

        using (var pen = new Pen(colour, Math.Max(2f, width / 3)))
        {
            g.DrawLine(pen, at.X - nx, at.Y - ny, at.X + nx, at.Y + ny);
        }

        using var font = new Font(AppTheme.BoldFont.FontFamily, Math.Max(6f, width * 0.9f), FontStyle.Bold, GraphicsUnit.Pixel);
        TextRenderer.DrawText(g, name, font, new Point((int)(at.X + nx * 1.3f), (int)(at.Y + ny * 1.3f)), colour, TextFormatFlags.NoPadding);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
        }

        base.Dispose(disposing);
    }
}
