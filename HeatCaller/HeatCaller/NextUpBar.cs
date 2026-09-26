namespace HeatCaller;

/// <summary>
/// The strip across the top of Race Control that always knows where the
/// day is: what is running NOW, what is NEXT and whether its drivers are
/// actually here, and how far through - and behind - the DAY is.
///
/// This is the part of running an event that every timing package leaves
/// in the race director's head or on a paper heat sheet. The information
/// all exists in the app already; this puts it where the person calling
/// the races is already looking.
///
/// Draw-only. The bar computes nothing - Race Control feeds it a finished
/// Info four times a second and it paints the latest one, so there is
/// exactly one place deciding what "next" means.
/// </summary>
public sealed class NextUpBar : Control
{
    /// <summary>One refresh's worth of text. Records compare by value, which is what makes the repaint-only-on-change below cheap.</summary>
    public sealed record Info(string Now, string Next, string Day, bool Warning);

    private Info _info = new(string.Empty, string.Empty, string.Empty, false);

    /// <summary>How much of the right end the caller has covered with buttons, so text stops before them.</summary>
    public int ReservedRight { get; set; }

    public NextUpBar()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer
                 | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw, true);

        DoubleBuffered = true;
        TabStop = false;
        Height = 52;
    }

    public void ShowInfo(Info info)
    {
        if (info == _info)
        {
            return;
        }

        _info = info;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;

        using (var back = new SolidBrush(AppTheme.Surface))
        {
            g.FillRectangle(back, ClientRectangle);
        }

        using (var edge = new Pen(AppTheme.Border))
        {
            g.DrawLine(edge, 0, Height - 1, Width, Height - 1);
        }

        var right = Width - Math.Max(0, ReservedRight) - 12;
        var x = 12;

        x = Block(g, x, right, "NOW", _info.Now, AppTheme.TextDark);
        x = Block(g, x, right, "NEXT", _info.Next, _info.Warning ? AppTheme.Warning : AppTheme.TextDark);
        Block(g, x, right, "DAY", _info.Day, AppTheme.TextMuted);
    }

    /// <summary>
    /// One captioned segment: a micro label over its text. Returns where
    /// the next segment starts, and simply stops drawing at the right
    /// boundary - a bar that wraps is a bar that pushed the flags off the
    /// page.
    /// </summary>
    private int Block(Graphics g, int x, int right, string caption, string text, Color textColour)
    {
        if (string.IsNullOrEmpty(text) || x >= right)
        {
            return x;
        }

        TextRenderer.DrawText(
            g, AppTheme.Spaced(caption), AppTheme.MicroFont, new Point(x, 7), AppTheme.Primary,
            TextFormatFlags.NoPadding);

        var bounds = new Rectangle(x, 24, right - x, Height - 26);

        TextRenderer.DrawText(
            g, text, AppTheme.BodyFont, bounds, textColour,
            TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.SingleLine);

        var width = TextRenderer.MeasureText(
            g, text, AppTheme.BodyFont, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;

        var captionWidth = TextRenderer.MeasureText(
            g, AppTheme.Spaced(caption), AppTheme.MicroFont, new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.NoPadding).Width;

        return Math.Min(right, x + Math.Max(width, captionWidth) + 28);
    }
}
