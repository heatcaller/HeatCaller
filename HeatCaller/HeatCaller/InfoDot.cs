using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// A SMALL CIRCLE WITH AN "i" IN IT that holds the explanation of the thing
/// beside it (25 Nov 2026, asked for by Asher: "for all the tool tips here
/// can we put them behind a small circle with an i in it?").
///
/// WHY: at the foot of Race Control's board the long tips sat on the pills,
/// the format line, its pen and Focus themselves, so they popped up whenever
/// the mouse passed over a button on its way to press it - a paragraph over
/// the board in the middle of a race. Nothing showed that the help was
/// there, either. Now the buttons are quiet and the help is asked for: hover
/// the circle, or click it to bring the tip back after it has gone.
///
/// Not a tab stop: a screen reader hears the same words as the circle's
/// description, and the thing beside it carries its own name.
/// </summary>
internal sealed class InfoDot : Control
{
    private readonly ToolTip _tip = new();
    private string _text = string.Empty;
    private bool _hot;

    /// <summary>The circle's width and height at 100%.</summary>
    internal const int SizeAt100 = 16;

    /// <summary>The room between the circle and the thing it explains, at 100%.</summary>
    internal const int GapAt100 = 3;

    public InfoDot()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                 | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        SetStyle(ControlStyles.Selectable, false);

        BackColor = Color.Transparent;
        TabStop = false;
        Cursor = Cursors.Help;
        AccessibleRole = AccessibleRole.HelpBalloon;
        AccessibleName = "More about this";

        // Shown at once, and kept up long enough to read a paragraph.
        _tip.InitialDelay = 150;
        _tip.AutoPopDelay = 30000;
        _tip.ReshowDelay = 100;
        AppTheme.StyleToolTip(_tip, keep: true);

        Size = new Size(UiLayout.AtDpi(SizeAt100), UiLayout.AtDpi(SizeAt100));
    }

    /// <summary>What the circle explains. Empty hides nothing - the circle still shows - but pops up nothing.</summary>
    public string TipText
    {
        get => _text;
        set
        {
            value ??= string.Empty;

            if (_text == value)
            {
                return;
            }

            _text = value;
            _tip.SetToolTip(this, value);
            AccessibleDescription = value;
        }
    }

    /// <summary>The tip - for the tests.</summary>
    internal ToolTip Tip => _tip;

    /// <summary>The room it takes beside the thing it explains: its width and the gap before it.</summary>
    internal static int Room => UiLayout.AtDpi(SizeAt100) + UiLayout.AtDpi(GapAt100);

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

    /// <summary>A click brings the tip back, under the circle, after it has timed out.</summary>
    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);

        if (_text.Length > 0)
        {
            AppTheme.ShowToolTip(_tip, _text, this, 0, Height + UiLayout.AtDpi(4), 30000);
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var saved = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;

        try
        {
            var line = Math.Max(1.2f, 1.25f * UiLayout.DpiScale);
            var side = Math.Min(Width, Height) - line - 1f;
            var circle = new RectangleF((Width - side) / 2f, (Height - side) / 2f, side, side);

            // Muted until the mouse is on it, then the theme's pink - it is
            // help to ask for, not a button that wants pressing.
            var ink = _hot ? AppTheme.Primary : AppTheme.TextMuted;

            if (_hot)
            {
                using var wash = new SolidBrush(Color.FromArgb(40, AppTheme.Primary));
                g.FillEllipse(wash, circle);
            }

            using (var ring = new Pen(ink, line))
            {
                g.DrawEllipse(ring, circle);
            }

            // The "i" drawn, not typed, so it sits in the middle at any font:
            // a dot, and a stem under it.
            var cx = circle.X + circle.Width / 2f;
            var unit = side / 16f;
            var dot = 2.0f * unit;

            using (var brush = new SolidBrush(ink))
            {
                g.FillEllipse(brush, cx - dot / 2f, circle.Y + 3.6f * unit, dot, dot);
            }

            using var stem = new Pen(ink, Math.Max(1.4f, 1.9f * unit)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(stem, cx, circle.Y + 7.4f * unit, cx, circle.Y + 12.2f * unit);
        }
        finally
        {
            g.Restore(saved);
        }
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
