using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// A two-way segmented switch: a caption, then two segments side by side
/// in one rounded frame, the chosen one filled (9 Nov 2026, option 1 of ten
/// mock-ups Asher looked at in place of the "Start races On Air" tick box).
/// Both choices are always on show, so it never has to be read as "off".
///
/// <see cref="On"/> is the right-hand segment, which is filled with the
/// theme's pink; the left one, chosen, takes the quieter secondary-button
/// fill. Click either segment, or with it focused press Space, Enter or an
/// arrow key. Colours are read at paint time, so it follows a theme change.
/// </summary>
internal sealed class SegmentedSwitch : Control
{
    private readonly string _offText;
    private readonly string _onText;
    private bool _on;

    /// <summary>The segment under the mouse: -1 none, 0 left, 1 right.</summary>
    private int _hot = -1;

    public SegmentedSwitch(string caption, string offText, string onText)
    {
        Text = caption;
        _offText = offText;
        _onText = onText;

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                 | ControlStyles.UserPaint | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);

        BackColor = Color.Transparent;
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.PushButton;
        SyncAccessible();
    }

    /// <summary>Raised when <see cref="On"/> changes, however it was changed.</summary>
    public event EventHandler? OnChanged;

    /// <summary>Whether the right-hand segment is the chosen one.</summary>
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

    private void SyncAccessible() => AccessibleName = $"{Text} {(_on ? _onText : _offText)}";

    private int Gap => UiLayout.AtDpi(8);
    private int SegmentPad => UiLayout.AtDpi(Compact ? 11 : 14);

    /// <summary>
    /// A little less room either side of each choice (15 Nov 2026) - for Race
    /// Control's three switches at the foot of the board, which with Format
    /// and Expand have to fit on one row at 1280 wide.
    /// </summary>
    public bool Compact { get; init; }
    private int VerticalPad => UiLayout.AtDpi(6);

    private Size Measure(string text) =>
        TextRenderer.MeasureText(text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

    private int CaptionWidth => string.IsNullOrEmpty(Text) ? 0 : Measure(Text).Width + Gap;

    /// <summary>Both segments are as wide as the wider caption, so the switch doesn't jump about.</summary>
    private int SegmentWidth => Math.Max(Measure(_offText).Width, Measure(_onText).Width) + SegmentPad * 2;

    public override Size GetPreferredSize(Size proposedSize)
    {
        var height = Math.Max(Measure(_offText).Height, Measure(Text).Height) + VerticalPad * 2;
        return new Size(CaptionWidth + SegmentWidth * 2, height);
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

    private void AutoSizeNow()
    {
        Size = GetPreferredSize(Size.Empty);
        Invalidate();
    }

    private Rectangle Frame => new(CaptionWidth, 0, SegmentWidth * 2, Height);

    private Rectangle Segment(int index)
    {
        var frame = Frame;
        return new Rectangle(frame.X + SegmentWidth * index, frame.Y, SegmentWidth, frame.Height);
    }

    private int SegmentAt(Point p)
    {
        for (var i = 0; i < 2; i++)
        {
            if (Segment(i).Contains(p))
            {
                return i;
            }
        }

        return -1;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var smoothing = g.SmoothingMode;
        var offset = g.PixelOffsetMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        try
        {
            var muted = Enabled ? AppTheme.TextMuted : Blend(AppTheme.TextMuted, AppTheme.Surface, 0.5f);
            var text = Enabled ? AppTheme.TextDark : muted;

            if (!string.IsNullOrEmpty(Text))
            {
                var captionBox = new Rectangle(0, 0, CaptionWidth - Gap, Height);
                TextRenderer.DrawText(g, Text, Font, captionBox, muted,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            }

            var frame = Frame;
            var line = Math.Max(1f, UiLayout.AtDpi(1));
            var radius = UiLayout.AtDpi(8);
            var outline = new RectangleF(frame.X + 0.5f, frame.Y + 0.5f, frame.Width - 1f, frame.Height - 1f);

            using (var path = AppTheme.RoundedRect(outline, radius))
            {
                using (var fill = new SolidBrush(AppTheme.Surface))
                {
                    g.FillPath(fill, path);
                }

                // The chosen segment's fill, clipped to the frame's rounded corners.
                var chosen = _on ? 1 : 0;
                var fillColour = _on ? AppTheme.Primary : AppTheme.SecondaryButton;

                if (!Enabled)
                {
                    fillColour = Blend(fillColour, AppTheme.Surface, 0.5f);
                }

                var clip = g.Clip;
                g.SetClip(path, CombineMode.Intersect);

                // A hover wash on the segment that isn't chosen, so it reads as clickable.
                if (Enabled && _hot >= 0 && _hot != chosen)
                {
                    using var hover = new SolidBrush(Blend(AppTheme.SecondaryButtonHover, AppTheme.Surface, 0.5f));
                    g.FillRectangle(hover, Segment(_hot));
                }

                using (var fill = new SolidBrush(fillColour))
                {
                    g.FillRectangle(fill, Segment(chosen));
                }

                g.Clip = clip;

                var edge = Focused && ShowFocusCues ? AppTheme.Primary : AppTheme.Border;
                using var pen = new Pen(edge, line);
                g.DrawPath(pen, path);
            }

            DrawSegmentText(g, Segment(0), _offText, !_on ? text : muted);
            DrawSegmentText(g, Segment(1), _onText, _on ? (Enabled ? AppTheme.OnPrimary : muted) : muted);
        }
        finally
        {
            g.SmoothingMode = smoothing;
            g.PixelOffsetMode = offset;
        }
    }

    private void DrawSegmentText(Graphics g, Rectangle box, string text, Color colour) =>
        TextRenderer.DrawText(g, text, Font, box, colour,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

    private static Color Blend(Color from, Color to, float amount) => Color.FromArgb(
        (int)Math.Round(from.R + (to.R - from.R) * amount),
        (int)Math.Round(from.G + (to.G - from.G) * amount),
        (int)Math.Round(from.B + (to.B - from.B) * amount));

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hot = SegmentAt(e.Location);

        if (hot != _hot)
        {
            _hot = hot;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hot = -1;
        Invalidate();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);

        if (!Visible)
        {
            _hot = -1;
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != MouseButtons.Left || !Enabled)
        {
            return;
        }

        // The caption toggles too, as a check box's caption did.
        var segment = SegmentAt(e.Location);
        On = segment < 0 ? !On : segment == 1;
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

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        switch (e.KeyCode)
        {
            case Keys.Left:
                On = false;
                e.Handled = true;
                break;
            case Keys.Right:
                On = true;
                e.Handled = true;
                break;
            case Keys.Space:
            case Keys.Enter:
                On = !On;
                e.Handled = true;
                break;
        }
    }
}
