using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// The empty Garage page's invitation (17 Nov 2026, page jazz-up): a car's
/// card drawn as an outline in the look's colour - a dashed plate with a
/// "+" where the photograph would be and "Add a Car" where the name would
/// be. Clicking it (or Enter / Space on it) is the same as Add a Car.
/// Drawn from the theme at paint time, so every look wears it.
/// </summary>
internal sealed class GarageEmptyPlate : Control
{
    private bool _hot;

    public GarageEmptyPlate()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw
                 | ControlStyles.Selectable, true);

        DoubleBuffered = true;
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleName = "Add a Car";
        AccessibleRole = AccessibleRole.PushButton;
        Tag = AppTheme.KeepBackColor;
    }

    /// <summary>What the plate says - the button it stands for, without its dots.</summary>
    private const string Words = "Add a Car";

    /// <summary>
    /// A little wider than a car's card and shorter, at the text size in use -
    /// so the plate and the words under it fit in the least the wall is
    /// given (a heading and a row of cards).
    /// </summary>
    public override Size GetPreferredSize(Size proposedSize) =>
        new(Math.Max(CarCard.CardWidth, UiLayout.AtTextSize(250)), CarCard.CardHeight * 3 / 4);

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

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.KeyCode is Keys.Enter or Keys.Space)
        {
            e.Handled = true;
            OnClick(EventArgs.Empty);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var behind = Parent?.BackColor ?? AppTheme.Background;

        using (var back = new SolidBrush(behind))
        {
            g.FillRectangle(back, ClientRectangle);
        }

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        var line = Math.Max(2f, UiLayout.AtDpi(2));
        var body = new RectangleF(line, line, Width - line * 2 - 1, Height - line * 2 - 1);

        using (var shape = AppTheme.RoundedRect(body, AppTheme.CornerRadius))
        {
            using (var fill = new SolidBrush(RosterInk.Blend(AppTheme.Primary, behind, _hot || Focused ? 0.80f : 0.90f)))
            {
                g.FillPath(fill, shape);
            }

            using var pen = new Pen(AppTheme.Primary, line) { DashStyle = DashStyle.Dash };
            g.DrawPath(pen, shape);
        }

        // The "+" in a ring, where a car's photograph would be.
        var wordsHeight = AppTheme.BoldFont.Height;
        var ringSize = Math.Max(UiLayout.AtTextSize(24), Math.Min(UiLayout.AtTextSize(64), (int)body.Height - wordsHeight - UiLayout.AtTextSize(40)));
        var gap = UiLayout.AtTextSize(12);
        var top = body.Y + (body.Height - ringSize - gap - wordsHeight) / 2f;
        var ring = new RectangleF(body.X + (body.Width - ringSize) / 2f, top, ringSize, ringSize);

        using (var ringFill = new SolidBrush(RosterInk.Blend(AppTheme.Primary, behind, 0.70f)))
        {
            g.FillEllipse(ringFill, ring);
        }

        using (var ringPen = new Pen(AppTheme.Primary, line))
        {
            g.DrawEllipse(ringPen, ring);
        }

        using (var plus = new Pen(AppTheme.Primary, Math.Max(2f, ringSize / 12f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            var cx = ring.X + ring.Width / 2f;
            var cy = ring.Y + ring.Height / 2f;
            var arm = ringSize * 0.24f;

            g.DrawLine(plus, cx - arm, cy, cx + arm, cy);
            g.DrawLine(plus, cx, cy - arm, cx, cy + arm);
        }

        TextRenderer.DrawText(g, Words, AppTheme.BoldFont,
            new Rectangle((int)body.X, (int)(ring.Bottom + gap), (int)body.Width, wordsHeight),
            AppTheme.Primary,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }
}
