using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// A SMALL CIRCLE WITH AN "i" IN IT, on the top right-hand corner of the
/// thing it explains, that holds that thing's help.
///
/// WHY (25 Nov 2026, asked for by Asher: "for all the tool tips here can we
/// put them behind a small circle with an i in it?"): at the foot of Race
/// Control's board the long tips sat on the pills, the format line, its pen
/// and Focus themselves, so they popped up whenever the mouse crossed a
/// button on its way to a press, and nothing showed the help was there.
///
/// WHY (25 Nov 2026, Asher: "info circle should be on the upper right of the
/// pill. if you click it it should bring up the tip and if you click again it
/// kills the tip. make sure that it's quick and snappy"): the first circles
/// sat beside each part, took about 95 px of the row - enough to push the
/// format line off it at 1280 wide - and opened on hover after a delay. Now
/// the circle is a badge on the part's corner, taking no room of its own, and
/// it opens on the press of the mouse button, not its release, with no hover
/// wait. A second press closes it. The tip keeps its fade.
///
/// Not a tab stop: a screen reader hears the same words as the circle's
/// description, and the part under it carries its own name.
/// </summary>
internal sealed class InfoDot : Control
{
    private readonly ToolTip _tip = new();
    private readonly Stopwatch _openFor = new();
    private string _text = string.Empty;
    private bool _hot;
    private bool _open;
    private Control? _over;

    /// <summary>The one circle whose tip is up, so opening another closes it.</summary>
    private static InfoDot? _current;

    /// <summary>The circle's width and height at 100%.</summary>
    // WHY (25 Nov 2026, Asher: "make the badges a bit smaller"): at 18 they
    // stood out over the pills more than help you have to ask for should.
    internal const int SizeAt100 = 14;

    /// <summary>How long a tip stays up if it is never clicked shut.</summary>
    internal const int OpenMs = 60_000;

    public InfoDot()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                 | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);

        // A quick second press is a second press - it closes the tip - not a
        // double-click that would swallow it.
        SetStyle(ControlStyles.Selectable | ControlStyles.StandardDoubleClick, false);

        BackColor = Color.Transparent;
        TabStop = false;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.HelpBalloon;
        AccessibleName = "More about this";

        AppTheme.StyleToolTip(_tip, keep: true);

        Size = new Size(UiLayout.AtDpi(SizeAt100), UiLayout.AtDpi(SizeAt100));
    }

    /// <summary>What the circle explains.</summary>
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
            AccessibleDescription = value;

            // Up already: the new words straight away (Focus becoming Restore).
            if (IsOpen)
            {
                Show();
            }
        }
    }

    /// <summary>The tip - for the tests.</summary>
    internal ToolTip Tip => _tip;

    /// <summary>The tip is up: opened, and not yet closed or timed out.</summary>
    internal bool IsOpen => _open && _openFor.ElapsedMilliseconds < OpenMs;

    /// <summary>
    /// The part the circle sits on the corner of. It is drawn under the
    /// circle, so the circle's round edge is smooth over it - WinForms only
    /// paints a see-through control's parent behind it, never a neighbour.
    /// </summary>
    internal Control? Over
    {
        get => _over;
        set
        {
            if (ReferenceEquals(_over, value))
            {
                return;
            }

            if (_over is not null)
            {
                _over.Invalidated -= OverInvalidated;
            }

            _over = value;

            if (_over is not null)
            {
                _over.Invalidated += OverInvalidated;
            }

            Invalidate();
        }
    }

    private void OverInvalidated(object? sender, InvalidateEventArgs e) => Invalidate();

    /// <summary>Opens the tip, or closes it when it is up. A press of the circle.</summary>
    internal void Toggle()
    {
        if (IsOpen)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    private void Open()
    {
        if (_current is not null && !ReferenceEquals(_current, this))
        {
            _current.Close();
        }

        _current = this;
        _open = true;
        _openFor.Restart();
        Show();
        Invalidate();
    }

    private void Show()
    {
        if (_text.Length > 0 && IsHandleCreated)
        {
            AppTheme.ShowToolTip(_tip, _text, this, 0, Height + UiLayout.AtDpi(4), OpenMs);
        }
    }

    /// <summary>Closes the tip, if it is up.</summary>
    internal void Close()
    {
        if (ReferenceEquals(_current, this))
        {
            _current = null;
        }

        if (!_open)
        {
            return;
        }

        _open = false;
        _openFor.Reset();

        if (IsHandleCreated)
        {
            _tip.Hide(this);
        }

        Invalidate();
    }

    /// <summary>On the press, not the release - so it answers at once.</summary>
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button == MouseButtons.Left)
        {
            Toggle();
        }
    }

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

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);

        if (!Visible)
        {
            Close();
        }
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        Close();
        base.OnHandleDestroyed(e);
    }

    /// <summary>The row behind, then the part the circle sits on.</summary>
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        base.OnPaintBackground(e);

        if (_over is not { Visible: true, Width: > 0, Height: > 0 } over || !ReferenceEquals(over.Parent, Parent)
            || !over.Bounds.IntersectsWith(Bounds))
        {
            return;
        }

        try
        {
            using var under = new Bitmap(over.Width, over.Height);
            over.DrawToBitmap(under, new Rectangle(Point.Empty, over.Size));
            e.Graphics.DrawImageUnscaled(under, over.Left - Left, over.Top - Top);
        }
        catch (ArgumentException)
        {
            // A part mid-resize: the row's own background will do for a frame.
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var saved = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;

        try
        {
            var line = Math.Max(1.1f, 1.15f * UiLayout.DpiScale);
            var side = Math.Min(Width, Height) - 1f;
            var circle = new RectangleF((Width - side) / 2f, (Height - side) / 2f, side, side);
            var lit = _open || _hot;

            // A badge: filled, so it reads clearly over the part's edge, and
            // ringed in the card's colour so it stands off the part. Light
            // while its tip is up or the mouse is on it - not pink, which
            // would vanish against Focus.
            var fill = lit ? AppTheme.TextDark : AppTheme.Surface;
            var ink = lit ? AppTheme.Surface : AppTheme.TextDark;

            using (var brush = new SolidBrush(fill))
            {
                g.FillEllipse(brush, circle);
            }

            var cutout = Parent?.BackColor is { A: 255 } back ? back : AppTheme.Background;
            using (var ring = new Pen(cutout, line))
            {
                g.DrawEllipse(ring, RectangleF.Inflate(circle, -line / 2f, -line / 2f));
            }

            if (!lit)
            {
                using var edge = new Pen(AppTheme.TextMuted, Math.Max(1f, UiLayout.DpiScale));
                g.DrawEllipse(edge, RectangleF.Inflate(circle, -line - 0.5f, -line - 0.5f));
            }

            // The "i" drawn, not typed, so it sits in the middle at any font:
            // a dot, and a stem under it.
            var cx = circle.X + circle.Width / 2f;
            var unit = side / 16f;
            var dot = 2.2f * unit;

            using (var brush = new SolidBrush(ink))
            {
                g.FillEllipse(brush, cx - dot / 2f, circle.Y + 3.6f * unit, dot, dot);
            }

            using var stem = new Pen(ink, Math.Max(1.3f, 2.0f * unit)) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(stem, cx, circle.Y + 7.6f * unit, cx, circle.Y + 12.0f * unit);
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
            Over = null;
            Close();
            _tip.Dispose();
        }

        base.Dispose(disposing);
    }
}
