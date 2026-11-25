using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// PLAY AGAIN's split button, at the right end of Race Control's On Air bar
/// (25 Nov 2026, asked for by Asher: "A bar, with the split button from B,
/// and a change the flags like in C" - board G, plans/play-again-plan.md).
///
/// The main part plays what the race calls for now - "Play winner" after the
/// flag, "Play call-up" between races, "Play again" during one - with what
/// it plays under it, and the S key cap that does the same. The narrow part
/// opens the list of tonight's lines. Drawn as the mockup has it: the
/// secondary button's ground, a pink edge, the pink replay arrow.
/// </summary>
internal sealed class PlayAgainButton : Control
{
    private string _caption = "Play again";
    private string _detail = string.Empty;
    private bool _ready;
    private int _hot = -1;
    private int _down = -1;
    private readonly ToolTip _tip = new();

    public PlayAgainButton()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                 | ControlStyles.UserPaint | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);

        BackColor = Color.Transparent;
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.SplitButton;
        AppTheme.StyleToolTip(_tip, keep: true);
        ShowPress("Play again", "nothing said yet", false);
    }

    /// <summary>The main part was pressed (and was ready to play).</summary>
    public event Action? Pressed;

    /// <summary>The narrow part was pressed: the list is wanted, opening under the button.</summary>
    public event Action? ListWanted;

    /// <summary>What it says, and whether the main part can play.</summary>
    public void ShowPress(string caption, string? detail, bool ready)
    {
        detail ??= string.Empty;

        if (_caption == caption && _detail == detail && _ready == ready)
        {
            return;
        }

        _caption = caption;
        _detail = detail;
        _ready = ready;
        AccessibleName = detail.Length > 0 ? $"{caption}: {detail}" : caption;
        AccessibleDescription = "S does the same. The arrow lists what was said tonight.";
        _tip.SetToolTip(this, ready
            ? $"{caption} ({detail}) - the same audio the room heard. S does the same; the arrow lists the last 20 lines."
            : $"{caption} - {detail}. The arrow still lists what was said tonight.");
        SizeToFit();
        Invalidate();
    }

    /// <summary>The words on the main part - for the tests.</summary>
    internal string Caption => _caption;

    /// <summary>The line under them - for the tests.</summary>
    internal string Detail => _detail;

    /// <summary>Whether the main part plays - for the tests.</summary>
    internal bool Ready => _ready;

    /// <summary>A press of the main part, from the S key, the phone or a test.</summary>
    internal void PerformPress()
    {
        if (_ready)
        {
            Pressed?.Invoke();
        }
    }

    private static int PadLeft => UiLayout.AtDpi(10);
    private static int PadRight => UiLayout.AtDpi(8);
    private static int IconSize => UiLayout.AtDpi(15);
    private static int IconGap => UiLayout.AtDpi(7);
    private static int ArrowWidth => UiLayout.AtDpi(28);
    private static int KeyCap => UiLayout.AtDpi(16);

    /// <summary>The button's own height: one bold line and one small line, and room round them.</summary>
    internal static int ButtonHeight => Math.Max(UiLayout.AtDpi(30), CaptionFont.Height + DetailFont.Height);

    private static Font CaptionFont => AppTheme.BoldFont;
    private static Font DetailFont => AppTheme.SmallFont;

    private static int Measure(string text, Font font) =>
        TextRenderer.MeasureText(text, font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;

    private int MainWidth => PadLeft + IconSize + IconGap + Math.Max(Measure(_caption, CaptionFont), Measure(_detail, DetailFont))
                             + UiLayout.AtDpi(8) + KeyCap + PadRight;

    private Rectangle MainPart => new(0, 0, Math.Max(0, Width - ArrowWidth - 1), Height);
    private Rectangle ArrowPart => new(Width - ArrowWidth, 0, ArrowWidth, Height);

    private void SizeToFit()
    {
        var size = new Size(MainWidth + 1 + ArrowWidth, ButtonHeight);

        if (Size != size)
        {
            Size = size;
        }
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        SizeToFit();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        var radius = UiLayout.AtDpi(6);
        var whole = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
        var edgeColour = _ready ? AppTheme.Primary : AppTheme.Border;

        using var outline = AppTheme.RoundedRect(whole, radius);
        var clip = g.Clip;
        g.SetClip(outline);

        var main = AppTheme.SecondaryButton;
        var arrow = AppTheme.PrimaryTint;

        if (_hot == 0 && _ready)
        {
            main = Blend(main, Color.White, _down == 0 ? 0.04f : 0.08f);
        }

        if (_hot == 1)
        {
            arrow = Blend(arrow, Color.White, _down == 1 ? 0.04f : 0.08f);
        }

        using (var brush = new SolidBrush(main))
        {
            g.FillRectangle(brush, MainPart);
        }

        using (var brush = new SolidBrush(arrow))
        {
            g.FillRectangle(brush, ArrowPart.X - 1, 0, ArrowPart.Width + 1, Height);
        }

        using (var divider = new Pen(edgeColour, 1f))
        {
            g.DrawLine(divider, ArrowPart.X - 0.5f, 0, ArrowPart.X - 0.5f, Height);
        }

        g.Clip = clip;

        using (var edge = new Pen(edgeColour, Math.Max(1f, UiLayout.DpiScale)))
        {
            g.DrawPath(edge, outline);
        }

        if (Focused && ShowFocusCues)
        {
            using var ring = new Pen(AppTheme.Accent, Math.Max(1.5f, 2f * UiLayout.DpiScale));
            using var inner = AppTheme.RoundedRect(new RectangleF(1.5f, 1.5f, Width - 3f, Height - 3f), radius - 1);
            g.DrawPath(ring, inner);
        }

        var ink = _ready ? AppTheme.TextDark : AppTheme.TextMuted;
        var iconInk = _ready ? AppTheme.Primary : AppTheme.TextMuted;
        var stroke = Math.Max(1.6f, 2.2f * UiLayout.DpiScale);

        // The replay arrow: most of a circle, its head at the top right.
        var icon = new RectangleF(PadLeft, (Height - IconSize) / 2f, IconSize, IconSize);

        using (var pen = new Pen(iconInk, stroke) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
        {
            var ring = RectangleF.Inflate(icon, -stroke / 2, -stroke / 2);
            g.DrawArc(pen, ring, -45, 315);
            var head = new PointF(ring.Right, ring.Top + ring.Height * 0.18f);
            var w = IconSize * 0.3f;
            g.DrawLines(pen, new[] { new PointF(head.X, head.Y - w), head, new PointF(head.X - w, head.Y) });
        }

        // The caption over what it plays.
        var textLeft = PadLeft + IconSize + IconGap;
        var textWidth = MainPart.Width - textLeft - UiLayout.AtDpi(8) - KeyCap - PadRight;
        var lines = _detail.Length > 0 ? CaptionFont.Height + DetailFont.Height - UiLayout.AtDpi(2) : CaptionFont.Height;
        var top = (Height - lines) / 2;
        const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;

        TextRenderer.DrawText(g, _caption, CaptionFont, new Rectangle(textLeft, top, textWidth, CaptionFont.Height), ink, flags);

        if (_detail.Length > 0)
        {
            var detailInk = _ready ? Blend(AppTheme.TextDark, AppTheme.TextMuted, 0.35f) : AppTheme.TextMuted;
            TextRenderer.DrawText(g, _detail, DetailFont, new Rectangle(textLeft, top + CaptionFont.Height - UiLayout.AtDpi(2), textWidth, DetailFont.Height), detailInk, flags);
        }

        // The S key cap: the key that does the same.
        var cap = new Rectangle(MainPart.Right - PadRight - KeyCap, (Height - KeyCap) / 2, KeyCap, KeyCap);

        using (var path = AppTheme.RoundedRect(new RectangleF(cap.X + 0.5f, cap.Y + 0.5f, cap.Width - 1, cap.Height - 1), UiLayout.AtDpi(3)))
        using (var capEdge = new Pen(AppTheme.TextMuted, 1f))
        {
            g.DrawPath(capEdge, path);
        }

        TextRenderer.DrawText(g, "S", AppTheme.MicroFont, cap, AppTheme.TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

        // The arrow part: a chevron pointing down - the list opens under it.
        var c = new PointF(ArrowPart.X + ArrowPart.Width / 2f, Height / 2f + (_down == 1 ? 1 : 0));
        var size = UiLayout.AtDpi(4);

        using (var pen = new Pen(AppTheme.TextDark, Math.Max(1.4f, 1.8f * UiLayout.DpiScale)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
        {
            g.DrawLines(pen, new[] { new PointF(c.X - size, c.Y - size / 2f), new PointF(c.X, c.Y + size / 2f), new PointF(c.X + size, c.Y - size / 2f) });
        }
    }

    private static Color Blend(Color from, Color to, float amount) => Color.FromArgb(
        (int)Math.Round(from.R + (to.R - from.R) * amount),
        (int)Math.Round(from.G + (to.G - from.G) * amount),
        (int)Math.Round(from.B + (to.B - from.B) * amount));

    private int PartAt(Point p) => MainPart.Contains(p) ? 0 : ArrowPart.Contains(p) ? 1 : -1;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hot = PartAt(e.Location);

        if (hot != _hot)
        {
            _hot = hot;
            Cursor = hot == 0 && !_ready ? Cursors.Default : Cursors.Hand;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hot = -1;
        _down = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button == MouseButtons.Left && Enabled)
        {
            _down = PartAt(e.Location);
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var down = _down;
        _down = -1;
        Invalidate();

        if (e.Button != MouseButtons.Left || !Enabled || down < 0 || PartAt(e.Location) != down)
        {
            return;
        }

        if (down == 0)
        {
            PerformPress();
        }
        else
        {
            ListWanted?.Invoke();
        }
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            PerformPress();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Down)
        {
            ListWanted?.Invoke();
            e.Handled = true;
        }
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tip.Dispose();
        }

        base.Dispose(disposing);
    }
}
