using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>Which way a zone on a gauge reads: good, middling or bad. Coloured from the theme as it is drawn.</summary>
internal enum ZoneKind
{
    Good,
    Middling,
    Bad,
}

/// <summary>One stretch of a gauge: up to a value, and what a figure in it means ("tidy", "scrappy").</summary>
internal sealed record GaugeZone(double UpTo, ZoneKind Kind, string Label);

/// <summary>
/// One figure on a card of its own, read against zones (13 Nov 2026, the
/// Race Engineer rework of the Pace tab): the caption and the figure along
/// the top, a bar split into its zones with a marker where the figure falls,
/// each zone's name under it, and a line saying what it means.
///
/// The zones share the bar equally, whatever the numbers they cover, and the
/// marker moves through each one in proportion. Drawn to scale, the "at your
/// limit" stretch of consistency is a quarter of the bar and its name does
/// not fit under it at a normal window width; the figure itself is printed
/// above the bar, so nothing is lost.
///
/// Everything is measured from the fonts and drawn in the theme's colours as
/// it paints, so a new text size or theme needs only a repaint and a
/// re-measure. GaugeRow puts three across the page.
/// </summary>
internal sealed class ZoneGauge : Control
{
    private string _caption = string.Empty;
    private string _figure = "-";
    private string _reading = string.Empty;
    private double? _value;
    private IReadOnlyList<GaugeZone> _zones = Array.Empty<GaugeZone>();

    public ZoneGauge()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
        Tag = AppTheme.KeepBackColor;
        AccessibleRole = AccessibleRole.StaticText;
    }

    /// <summary>What is measured, in the card's caption: "Consistency".</summary>
    public string Caption => _caption;

    /// <summary>The figure as shown: "2.6%".</summary>
    public string Figure => _figure;

    /// <summary>The line under the bar saying what the figure means.</summary>
    public string Reading => _reading;

    /// <summary>Where the marker sits, or null for no marker (too few laps to read).</summary>
    public double? Value => _value;

    /// <summary>The zone the value falls in, or null without a value.</summary>
    public GaugeZone? Zone => _value is double v && _zones.Count > 0
        ? _zones.FirstOrDefault(z => v < z.UpTo) ?? _zones[^1]
        : null;

    /// <summary>Sets up the gauge's caption and its zones, lowest first.</summary>
    public void Configure(string caption, IReadOnlyList<GaugeZone> zones)
    {
        _caption = caption;
        _zones = zones;
        AccessibleName = caption;
        Changed();
    }

    /// <summary>Shows a figure: where it falls, how it is written, and what it means.</summary>
    public void Show(double? value, string figure, string reading)
    {
        _value = value;
        _figure = figure;
        _reading = reading;
        AccessibleDescription = $"{figure}. {reading}";
        Changed();
    }

    /// <summary>Room inside the card's edge, at the screen's DPI.</summary>
    private static Padding Inset => new(UiLayout.AtDpi(14), UiLayout.AtDpi(10), UiLayout.AtDpi(14), UiLayout.AtDpi(12));

    private static int Gap => UiLayout.AtTextSize(6);

    private static int BarHeight => UiLayout.AtTextSize(10);

    private static int MarkerHeight => UiLayout.AtTextSize(7);

    private static readonly TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.TextBoxControl;
    private static readonly TextFormatFlags Line = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;

    /// <summary>
    /// The figure: never cut short. It is a few characters and is given the
    /// room it measures, but with an ellipsis allowed the heading face's
    /// overhang tipped "2.6%" into "2..." however wide its box was.
    /// </summary>
    private static readonly TextFormatFlags FigureFlags = TextFormatFlags.SingleLine | TextFormatFlags.Right | TextFormatFlags.NoClipping;

    /// <summary>The caption in the card's caption style: upper case, spaced.</summary>
    private string CaptionText => AppTheme.Spaced(_caption.ToUpperInvariant());

    /// <summary>How tall the card is at <paramref name="width"/>: everything in it, the caption and the reading wrapped to fit.</summary>
    public int HeightAt(int width)
    {
        var parts = Parts(width);

        return parts.Bottom;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = proposedSize.Width is > 0 and < int.MaxValue / 4 ? proposedSize.Width : Math.Max(Width, UiLayout.AtTextSize(240));

        return new Size(width, HeightAt(width));
    }

    /// <summary>Where each part goes at a width - one sum shared by the measuring and the drawing.</summary>
    private (Rectangle Caption, Rectangle Figure, Rectangle Bar, Rectangle Labels, Rectangle Reading, int Bottom) Parts(int width)
    {
        var inset = Inset;
        var inside = Math.Max(1, width - inset.Horizontal);

        // A few pixels over the measured width: the heading face's last
        // glyph overhangs what TextRenderer measures, and drawn in exactly
        // that box "2.6%" came out as "2..." with an ellipsis.
        var figureSize = TextRenderer.MeasureText(_figure, AppTheme.HeadingFont, Size.Empty, FigureFlags);
        figureSize.Width += UiLayout.AtTextSize(8);
        var captionWidth = Math.Max(1, inside - figureSize.Width - Gap * 2);
        var captionSize = TextRenderer.MeasureText(CaptionText, AppTheme.MicroFont, new Size(captionWidth, 0), Wrap);
        var top = inset.Top;
        var headHeight = Math.Max(figureSize.Height, captionSize.Height);

        var caption = new Rectangle(inset.Left, top + Math.Max(0, (figureSize.Height - captionSize.Height) / 2), captionWidth, captionSize.Height);
        var figure = new Rectangle(inset.Left + inside - figureSize.Width, top, figureSize.Width, figureSize.Height);

        top += headHeight + Gap + MarkerHeight;
        var bar = new Rectangle(inset.Left, top, inside, BarHeight);

        top += BarHeight + Gap / 2;
        var labelHeight = Pieces.LineHeight(AppTheme.SmallFont);
        var labels = new Rectangle(inset.Left, top, inside, labelHeight);

        top += labelHeight + Gap;
        var readingSize = string.IsNullOrEmpty(_reading)
            ? Size.Empty
            : TextRenderer.MeasureText(_reading, AppTheme.BodyFont, new Size(inside, 0), Wrap);
        var reading = new Rectangle(inset.Left, top, inside, readingSize.Height);

        return (caption, figure, bar, labels, reading, top + readingSize.Height + inset.Bottom);
    }

    /// <summary>Where on the bar a value falls: each zone an equal share, the value in proportion inside its own.</summary>
    internal static float Position(double value, IReadOnlyList<GaugeZone> zones)
    {
        if (zones.Count == 0)
        {
            return 0;
        }

        var from = 0.0;

        for (var i = 0; i < zones.Count; i++)
        {
            if (value < zones[i].UpTo || i == zones.Count - 1)
            {
                var span = zones[i].UpTo - from;
                var within = span <= 0 ? 0 : Math.Clamp((value - from) / span, 0, 1);

                return (float)((i + within) / zones.Count);
            }

            from = zones[i].UpTo;
        }

        return 1;
    }

    private static Color Colour(ZoneKind kind) => kind switch
    {
        ZoneKind.Good => AppTheme.Success,
        ZoneKind.Middling => AppTheme.Warning,
        _ => AppTheme.Danger,
    };

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using (var behind = new SolidBrush(Parent?.BackColor ?? AppTheme.Background))
        {
            e.Graphics.FillRectangle(behind, ClientRectangle);
        }

        AppTheme.DrawCard(e.Graphics, new RectangleF(0, 0, Width, Height));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var parts = Parts(Width);

        TextRenderer.DrawText(g, CaptionText, AppTheme.MicroFont, parts.Caption, AppTheme.TextMuted, Wrap);
        TextRenderer.DrawText(g, _figure, AppTheme.HeadingFont, parts.Figure, AppTheme.TextDark, FigureFlags);

        if (_zones.Count > 0)
        {
            var share = parts.Bar.Width / (float)_zones.Count;
            var current = Zone;

            for (var i = 0; i < _zones.Count; i++)
            {
                var zone = _zones[i];
                var left = parts.Bar.Left + share * i;
                var colour = Colour(zone.Kind);

                // The zone the figure is in at full strength, the others
                // faded into the card, so the eye lands on the one that
                // counts. Blended rather than drawn see-through, which reads
                // the same on a light card and a dark one.
                var lit = current is not null && ReferenceEquals(zone, current);
                var fill = RosterInk.Blend(AppTheme.Surface, colour, lit ? 0.85f : 0.35f);

                using (var brush = new SolidBrush(fill))
                {
                    g.FillRectangle(brush, left, parts.Bar.Top, share - (i < _zones.Count - 1 ? UiLayout.AtDpi(2) : 0), parts.Bar.Height);
                }

                var labelBox = new Rectangle((int)left, parts.Labels.Top, (int)share, parts.Labels.Height);

                TextRenderer.DrawText(g, zone.Label, AppTheme.SmallFont, labelBox,
                    lit ? AppTheme.TextDark : AppTheme.TextMuted,
                    Line | TextFormatFlags.HorizontalCenter);
            }

            if (_value is double value)
            {
                var x = parts.Bar.Left + parts.Bar.Width * Position(value, _zones);
                var half = MarkerHeight * 0.8f;

                g.SmoothingMode = SmoothingMode.AntiAlias;

                using var ink = new SolidBrush(AppTheme.TextDark);

                g.FillPolygon(ink, new[]
                {
                    new PointF(x - half, parts.Bar.Top - MarkerHeight),
                    new PointF(x + half, parts.Bar.Top - MarkerHeight),
                    new PointF(x, parts.Bar.Top),
                });

                var stem = Math.Max(2, UiLayout.AtDpi(3));
                g.FillRectangle(ink, x - stem / 2f, parts.Bar.Top - UiLayout.AtDpi(2), stem, parts.Bar.Height + UiLayout.AtDpi(4));
            }
        }

        if (_reading.Length > 0)
        {
            TextRenderer.DrawText(g, _reading, AppTheme.BodyFont, parts.Reading, AppTheme.TextDark, Wrap);
        }
    }

    /// <summary>What it shows has changed: measured again, and the page told, as a hint that rewraps tells it.</summary>
    private void Changed()
    {
        Invalidate();
        (Parent as GaugeRow)?.Remeasure();
    }
}

/// <summary>
/// Gauges across the page in one row, sharing its width equally, all as tall
/// as the tallest needs to be at that width. A part that sizes itself, so a
/// page built from the layout pieces measures it like a wrapping hint.
/// </summary>
internal sealed class GaugeRow : Panel
{
    private readonly IReadOnlyList<ZoneGauge> _gauges;

    public GaugeRow(IReadOnlyList<ZoneGauge> gauges)
    {
        // Held while the window is being built - see LayoutFreeze.
        LayoutFreeze.Adopt(this);

        _gauges = gauges;

        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Margin = Padding.Empty;
        Padding = Padding.Empty;
        Tag = AppTheme.KeepBackColor;

        foreach (var gauge in gauges)
        {
            Controls.Add(gauge);
        }
    }

    /// <summary>Room between two gauges.</summary>
    private static int Gap => UiLayout.RowGap;

    private int ColumnWidth(int width) =>
        _gauges.Count == 0 ? width : Math.Max(1, (width - Gap * (_gauges.Count - 1)) / _gauges.Count);

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = proposedSize.Width is > 0 and < int.MaxValue / 4 ? proposedSize.Width : Math.Max(Width, UiLayout.AtTextSize(720));
        var column = ColumnWidth(width);

        return new Size(width, _gauges.Count == 0 ? 0 : _gauges.Max(g => g.HeightAt(column)));
    }

    protected override void OnLayout(LayoutEventArgs levent)
    {
        base.OnLayout(levent);

        if (_gauges.Count == 0)
        {
            return;
        }

        var column = ColumnWidth(ClientSize.Width);
        var height = _gauges.Max(g => g.HeightAt(column));

        for (var i = 0; i < _gauges.Count; i++)
        {
            // Shared out to the pixel: the last gauge ends on the edge.
            var left = i * (column + Gap);
            var width = i == _gauges.Count - 1 ? ClientSize.Width - left : column;

            _gauges[i].Bounds = new Rectangle(left, 0, Math.Max(1, width), height);
        }
    }

    /// <summary>
    /// A gauge's words or the text size have changed, and with them perhaps
    /// the height this row wants. The page is told first, then the table
    /// holding the row - which does not re-measure a part on its own.
    /// </summary>
    public void Remeasure()
    {
        Pieces.FillPageOf(this)?.LeastHeightMayHaveChanged();
        Parent?.PerformLayout(this, nameof(Bounds));
        PerformLayout();

        foreach (var gauge in _gauges)
        {
            gauge.Invalidate();
        }
    }
}

/// <summary>
/// A stage's name on a soft pill in its colour - the "Where you are on the
/// curve" card's Learning, Refining and Plateau. Sized to its words.
/// </summary>
internal sealed class StagePill : Label
{
    public StagePill()
    {
        AutoSize = true;
        Tag = AppTheme.KeepBackColor;
        Padding = new Padding(UiLayout.AtDpi(8), UiLayout.AtDpi(2), UiLayout.AtDpi(8), UiLayout.AtDpi(2));
        UseMnemonic = false;
    }

    /// <summary>The pill's colour, as a zone's.</summary>
    public ZoneKind Kind { get; set; }

    /// <summary>Drawn at full strength for the stage the driver is in, faded for the others.</summary>
    public bool Lit { get; set; }

    protected override void OnPaintBackground(PaintEventArgs pevent)
    {
        using (var behind = new SolidBrush(Parent?.BackColor ?? AppTheme.Surface))
        {
            pevent.Graphics.FillRectangle(behind, ClientRectangle);
        }

        var colour = Kind switch
        {
            ZoneKind.Good => AppTheme.Success,
            ZoneKind.Middling => AppTheme.Warning,
            _ => AppTheme.Danger,
        };

        pevent.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        using var path = new GraphicsPath();
        var r = Math.Max(2, Height / 2f);
        var box = new RectangleF(0, 0, Width - 1, Height - 1);

        path.AddArc(box.Left, box.Top, r * 2, box.Height, 90, 180);
        path.AddArc(box.Right - r * 2, box.Top, r * 2, box.Height, 270, 180);
        path.CloseFigure();

        using var tint = new SolidBrush(RosterInk.Blend(AppTheme.Surface, colour, Lit ? 0.30f : 0.12f));
        pevent.Graphics.FillPath(tint, path);
    }
}
