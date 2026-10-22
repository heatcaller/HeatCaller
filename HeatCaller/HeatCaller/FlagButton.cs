using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// One button on Race Control's flag row, painted by itself.
///
/// Three looks, and nothing else:
///
///   resting   the ordinary button colour, like every other button
///   hovered   its own colour - green, yellow, red, the chequers - so the
///             mouse shows what a click is about to throw
///   lit       its own colour, held, while that flag (or for Stage and
///             Countdown, that part of the start) is in effect
///
/// Painted here rather than styled, because styling cannot do it. The
/// theming pass sets every button's colours and its hover colour after a
/// page is built, and again whenever the theme changes, so a hover colour
/// set on an ordinary button is overwritten before anyone moves a mouse.
/// And the lit flag used to be marked with a white outline, which on a
/// saturated colour reads as a fault, not a highlight - so nothing here
/// draws a border or a focus ring at all.
/// </summary>
internal sealed class FlagButton : Button
{
    private bool _lit;
    private bool _hover;
    private bool _pressed;

    public FlagButton()
    {
        // Every pixel is ours: without UserPaint the flat style still draws
        // its border and focus rectangle underneath.
        SetStyle(
            ControlStyles.UserPaint
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw,
            true);
    }

    private Color _flagColour = Color.FromArgb(47, 111, 237);
    private Color _flagText = Color.White;

    /// <summary>
    /// The colour this button shows when hovered or lit. Settable after it
    /// is built (21 Oct 2026) - On Air turns cyan while the show is live.
    /// </summary>
    public Color FlagColour
    {
        get => _flagColour;
        set
        {
            if (_flagColour != value)
            {
                _flagColour = value;
                Invalidate();
            }
        }
    }

    /// <summary>The caption's colour on top of <see cref="FlagColour"/>.</summary>
    public Color FlagText
    {
        get => _flagText;
        set
        {
            if (_flagText != value)
            {
                _flagText = value;
                Invalidate();
            }
        }
    }

    /// <summary>Paints black-and-white chequers instead of a flat colour.</summary>
    public bool Chequered { get; init; }

    /// <summary>Whether this flag is the one in effect. Only repaints when it actually changes.</summary>
    public bool Lit
    {
        get => _lit;
        set
        {
            if (_lit != value)
            {
                _lit = value;
                Invalidate();
            }
        }
    }

    private float? _progress;

    /// <summary>
    /// How far along a job the button is standing for, 0 to 1, or null for
    /// none - drawn as a fill from the left. Only On Air uses it, while its
    /// opening is being got ready.
    /// </summary>
    public float? Progress
    {
        get => _progress;
        set
        {
            if (_progress != value)
            {
                _progress = value;
                Invalidate();
            }
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hover = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = false;
        _pressed = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs mevent)
    {
        base.OnMouseDown(mevent);

        if (mevent.Button == MouseButtons.Left)
        {
            _pressed = true;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs mevent)
    {
        base.OnMouseUp(mevent);
        _pressed = false;
        Invalidate();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs pevent)
    {
        var g = pevent.Graphics;
        var bounds = ClientRectangle;
        var coloured = Enabled && (_lit || _hover);

        if (!coloured)
        {
            PaintResting(g, bounds);
        }
        else if (Chequered)
        {
            PaintThrownChequers(g, bounds);
        }
        else
        {
            using var fill = new SolidBrush(FlagColour);
            g.FillRectangle(fill, bounds);
        }

        // A press darkens whatever is showing, so a click feels like one.
        // Kept inside the smooth outline, so it can't tint the corners the
        // thrown chequers leave to the page behind.
        if (_pressed && Enabled)
        {
            using var shade = new SolidBrush(Color.FromArgb(46, 0, 0, 0));
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var outline = SmoothOutline(bounds);
            g.FillPath(shade, outline);
        }

        // ON AIR'S PROGRESS (21 Oct 2026): while the opening is being got
        // ready, the button fills with its flag colour from the left as the
        // work gets done - see RaceControlControl.OnAir. The caption is drawn
        // twice, in the flag's ink over the filled part and the resting ink
        // over the rest, so it reads the whole way across.
        if (Progress is { } done && Enabled)
        {
            PaintResting(g, bounds);

            var filled = new Rectangle(bounds.Left, bounds.Top, (int)Math.Round(bounds.Width * Math.Clamp(done, 0f, 1f)), bounds.Height);

            if (filled.Width > 0)
            {
                using var fill = new SolidBrush(FlagColour);
                g.FillRectangle(fill, filled);
            }

            var saved = g.Save();
            g.SetClip(filled);
            PaintCaption(g, bounds, coloured: true);
            g.Restore(saved);

            saved = g.Save();
            g.SetClip(new Rectangle(filled.Right, bounds.Top, Math.Max(0, bounds.Right - filled.Right), bounds.Height));
            PaintCaption(g, bounds, coloured: false);
            g.Restore(saved);

            return;
        }

        PaintCaption(g, bounds, coloured);
    }

    private void PaintResting(Graphics g, Rectangle bounds)
    {
        using (var fill = new SolidBrush(AppTheme.SecondaryButton))
        {
            g.FillRectangle(fill, bounds);
        }

        // At rest the chequered flag still says what it is: the same
        // chequers, faint, in the button's own tones - readable as a
        // chequered flag without shouting the way the thrown one does.
        if (Chequered)
        {
            PaintChequers(g, bounds, Color.FromArgb(28, AppTheme.TextDark), Color.Transparent);
        }

        if (!Enabled)
        {
            using var dim = new SolidBrush(Color.FromArgb(90, AppTheme.Background));
            g.FillRectangle(dim, bounds);
        }
    }

    /// <summary>
    /// The thrown (hovered or lit) chequered flag: charcoal and white squares
    /// inside a smooth, anti-aliased outline.
    ///
    /// The button's rounded shape is a window Region, and a Region has hard,
    /// stair-stepped edges - with white squares running straight into them,
    /// the corners and edges read as a white ring around the button. So the
    /// corners are handed back to the page's own colour and the chequers are
    /// laid inside a slightly smaller outline drawn with anti-aliasing,
    /// which lets the light squares be properly white.
    /// </summary>
    private void PaintThrownChequers(Graphics g, Rectangle bounds)
    {
        using (var behind = new SolidBrush(Parent?.BackColor ?? AppTheme.Background))
        {
            g.FillRectangle(behind, bounds);
        }

        var cell = Math.Max(4, bounds.Height / 4);

        // One 2x2 tile of the pattern, repeated by the brush, so the fill
        // follows the smooth outline exactly instead of stopping at square
        // edges.
        using var tile = new Bitmap(cell * 2, cell * 2);
        using (var tg = Graphics.FromImage(tile))
        using (var dark = new SolidBrush(Color.FromArgb(24, 24, 30)))
        using (var light = new SolidBrush(Color.FromArgb(244, 244, 246)))
        {
            tg.FillRectangle(light, 0, 0, cell * 2, cell * 2);
            tg.FillRectangle(dark, 0, 0, cell, cell);
            tg.FillRectangle(dark, cell, cell, cell, cell);
        }

        using var chequers = new TextureBrush(tile, WrapMode.Tile);
        chequers.TranslateTransform(bounds.Left, bounds.Top);

        g.SmoothingMode = SmoothingMode.AntiAlias;

        using var outline = SmoothOutline(bounds);
        g.FillPath(chequers, outline);
    }

    /// <summary>
    /// The button's outline, drawn a pixel inside the window Region so its
    /// anti-aliased edge is never cut off by the Region's hard one.
    /// </summary>
    private static GraphicsPath SmoothOutline(Rectangle bounds)
    {
        var radius = Math.Max(1, Math.Min(6, (bounds.Height - 2) / 2));
        return AppTheme.RoundedRect(new RectangleF(bounds.Left + 1, bounds.Top + 1, bounds.Width - 2, bounds.Height - 2), radius);
    }

    /// <summary>
    /// Square chequers sized from the button's height - four rows - and
    /// laid from the top-left, so the pattern sits the same way on every
    /// button whatever its width.
    /// </summary>
    private static void PaintChequers(Graphics g, Rectangle bounds, Color dark, Color light)
    {
        var cell = Math.Max(4, bounds.Height / 4);

        if (light.A > 0)
        {
            using var back = new SolidBrush(light);
            g.FillRectangle(back, bounds);
        }

        using var squares = new SolidBrush(dark);

        for (var row = 0; row * cell < bounds.Height; row++)
        {
            for (var col = 0; col * cell < bounds.Width; col++)
            {
                if ((row + col) % 2 == 0)
                {
                    g.FillRectangle(squares, bounds.Left + col * cell, bounds.Top + row * cell, cell, cell);
                }
            }
        }
    }

    private void PaintCaption(Graphics g, Rectangle bounds, bool coloured)
    {
        // The theming pass lifts captions with bottom padding so they look
        // centred; honoured here so these match every other button.
        var area = new Rectangle(
            bounds.Left + Padding.Left,
            bounds.Top + Padding.Top,
            Math.Max(0, bounds.Width - Padding.Horizontal),
            Math.Max(0, bounds.Height - Padding.Vertical));

        const TextFormatFlags flags = TextFormatFlags.HorizontalCenter
            | TextFormatFlags.VerticalCenter
            | TextFormatFlags.SingleLine
            | TextFormatFlags.NoPrefix
            | TextFormatFlags.NoPadding;

        if (coloured && Chequered)
        {
            PaintOutlinedCaption(g, area);
            return;
        }

        var ink = !Enabled ? AppTheme.TextMuted : coloured ? FlagText : AppTheme.TextDark;
        TextRenderer.DrawText(g, Text, Font, area, ink, flags);
    }

    /// <summary>
    /// The caption on the thrown chequered flag: white letters wrapped in a
    /// thin charcoal outline, with no plate behind them.
    ///
    /// A plate the width of the word covered most of the button, so the
    /// flag read as a dark bar with a checked fringe. Outlining each letter
    /// instead keeps it readable on both colours of square while the
    /// chequers - true white and charcoal - run right up to the lettering.
    /// </summary>
    private void PaintOutlinedCaption(Graphics g, Rectangle area)
    {
        if (string.IsNullOrEmpty(Text))
        {
            return;
        }

        // GraphicsPath sizes text in pixels, the font in points.
        var emPixels = Font.SizeInPoints * g.DpiY / 72f;

        using var letters = new GraphicsPath();
        using (var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap })
        {
            letters.AddString(Text, Font.FontFamily, (int)Font.Style, emPixels, PointF.Empty, format);
        }

        // Centred on the letters' own ink rather than the font's line box,
        // so it sits in the middle the way the eye judges it.
        var ink = letters.GetBounds();

        using (var centre = new Matrix())
        {
            centre.Translate(
                area.Left + (area.Width - ink.Width) / 2f - ink.Left,
                area.Top + (area.Height - ink.Height) / 2f - ink.Top);
            letters.Transform(centre);
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;

        // Stroke first, fill on top: half the pen width lands outside each
        // letter as its outline, and the fill covers the half inside.
        var outlineWidth = Math.Max(3f, emPixels * 0.28f);

        using (var outline = new Pen(Color.FromArgb(24, 24, 30), outlineWidth) { LineJoin = LineJoin.Round })
        {
            g.DrawPath(outline, letters);
        }

        using (var fill = new SolidBrush(Color.White))
        {
            g.FillPath(fill, letters);
        }
    }
}
