using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// A LIT PILL (24 Nov 2026, option B of six for the foot of Race Control's
/// board, asked for by Asher: "this is confusing"). One yes/no as one pill:
/// lit - a pink wash, a pink edge and a glowing dot - when it is on, dark with
/// a hollow dot when it is off. It replaces SegmentedSwitch's two halves
/// ("Start: Stage | On Air", "Warm-up: Off | On", "Auto: Off | On"), which
/// read as six buttons for three choices, the pink half looking like
/// "press me".
///
/// The same <see cref="On"/> and <see cref="OnChanged"/> as SegmentedSwitch,
/// so what each one switches is wired as it was. Click it, or with it
/// focused press Space or Enter. Colours are read at paint time, so it
/// follows a theme change.
/// </summary>
internal sealed class LitPill : Control
{
    private bool _on;
    private bool _hot;

    public LitPill(string text)
    {
        Text = text;

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                 | ControlStyles.UserPaint | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);

        BackColor = Color.Transparent;
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.CheckButton;
        SyncAccessible();
    }

    /// <summary>Raised when <see cref="On"/> changes, however it was changed.</summary>
    public event EventHandler? OnChanged;

    /// <summary>Lit.</summary>
    public bool On
    {
        get => _on;
        set
        {
            if (_on == value)
            {
                return;
            }

            _on = value;
            SyncAccessible();
            Invalidate();
            OnChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>"On Air: on" - a screen reader hears the state, which the colour alone gives a sighted reader.</summary>
    private void SyncAccessible()
    {
        AccessibleName = $"{Text}: {(_on ? "on" : "off")}";
        AccessibleDescription = _on ? "On. Press to turn it off." : "Off. Press to turn it on.";
    }

    private int Dot => UiLayout.AtDpi(9);
    private int PadLeft => UiLayout.AtDpi(12);
    private int PadRight => UiLayout.AtDpi(16);
    private int DotGap => UiLayout.AtDpi(9);
    private int VerticalPad => UiLayout.AtDpi(8);

    private Size Measure() =>
        TextRenderer.MeasureText(Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

    public override Size GetPreferredSize(Size proposedSize)
    {
        var text = Measure();
        return new Size(PadLeft + Dot + DotGap + text.Width + PadRight, text.Height + VerticalPad * 2);
    }

    private void AutoSizeNow()
    {
        Size = GetPreferredSize(Size.Empty);
        Invalidate();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        AutoSizeNow();
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        SyncAccessible();
        AutoSizeNow();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        AutoSizeNow();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var smoothing = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        try
        {
            var line = Math.Max(1f, UiLayout.AtDpi(1));
            var outline = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            var radius = Math.Max(1, (Height - 1) / 2);
            var lit = _on && Enabled;

            using (var path = AppTheme.RoundedRect(outline, radius))
            {
                // Lit: the pink washed into the surface. Off: the surface, with
                // a hover wash so it still reads as something to press.
                var fill = lit ? Blend(AppTheme.Primary, AppTheme.Surface, 0.82f)
                    : _hot && Enabled ? Blend(AppTheme.SecondaryButtonHover, AppTheme.Surface, 0.5f)
                    : AppTheme.Surface;

                using (var brush = new SolidBrush(fill))
                {
                    g.FillPath(brush, path);
                }

                var edge = Focused && ShowFocusCues ? AppTheme.Accent
                    : lit ? AppTheme.Primary
                    : AppTheme.Border;

                using var pen = new Pen(edge, line);
                g.DrawPath(pen, path);
            }

            // The dot: filled and glowing when lit, a ring when not.
            var dot = new RectangleF(PadLeft, (Height - Dot) / 2f, Dot, Dot);

            if (lit)
            {
                for (var i = 3; i >= 1; i--)
                {
                    var grow = UiLayout.AtDpi(i * 2);
                    using var glow = new SolidBrush(Color.FromArgb(28, AppTheme.Primary));
                    g.FillEllipse(glow, RectangleF.Inflate(dot, grow, grow));
                }

                using var core = new SolidBrush(AppTheme.Primary);
                g.FillEllipse(core, dot);
            }
            else
            {
                using var ring = new Pen(Enabled ? AppTheme.TextMuted : Blend(AppTheme.TextMuted, AppTheme.Surface, 0.5f), Math.Max(1.2f, 1.5f * UiLayout.DpiScale));
                g.DrawEllipse(ring, RectangleF.Inflate(dot, -0.75f, -0.75f));
            }

            var textColour = !Enabled ? Blend(AppTheme.TextMuted, AppTheme.Surface, 0.5f)
                : lit ? AppTheme.TextDark
                : AppTheme.TextMuted;

            var box = new Rectangle(PadLeft + Dot + DotGap, 0, Width - (PadLeft + Dot + DotGap), Height);
            TextRenderer.DrawText(g, Text, BoldFont, box, textColour,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        }
        finally
        {
            g.SmoothingMode = smoothing;
        }
    }

    private Font? _bold;
    private Font? _boldOf;

    /// <summary>The control's font, semibold - the mockup's pills - made once per font.</summary>
    private Font BoldFont
    {
        get
        {
            if (_bold is null || !ReferenceEquals(_boldOf, Font))
            {
                _bold?.Dispose();
                _bold = new Font(Font, FontStyle.Bold);
                _boldOf = Font;
            }

            return _bold;
        }
    }

    private static Color Blend(Color from, Color to, float amount) => Color.FromArgb(
        (int)Math.Round(from.R + (to.R - from.R) * amount),
        (int)Math.Round(from.G + (to.G - from.G) * amount),
        (int)Math.Round(from.B + (to.B - from.B) * amount));

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        _hot = true;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hot = false;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button == MouseButtons.Left && Enabled)
        {
            On = !On;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (Enabled && e.KeyCode is Keys.Space or Keys.Enter)
        {
            On = !On;
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
            _bold?.Dispose();
        }

        base.Dispose(disposing);
    }
}
