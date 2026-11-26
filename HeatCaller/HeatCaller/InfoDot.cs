using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// A SMALL CIRCLE WITH AN "i" IN IT that holds a part's help: inside the
/// part just after its words (a pill, Focus), or on its top right-hand corner
/// (the pen, too small to hold it).
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
    // WHY (25 Nov 2026, Asher: "make the badges a bit smaller", then "they're
    // too small. go back to older size. put in the pill upper right of
    // text"): 14 was too small to find, so it is 18 again - but inside the
    // pill, after its words, where it no longer stands out over the edge.
    internal const int SizeAt100 = 18;

    /// <summary>Inside its part (a pill, Focus), not over its edge: no ring cut out in the card's colour.</summary>
    internal bool Inside { get; set; }

    /// <summary>On a pink part (Focus): white, not pink.</summary>
    internal bool OnPink { get; set; }

    /// <summary>How faint a badge at rest is, out of 255: its ring, and its "i".</summary>
    internal const int QuietRingAlpha = 120;
    internal const int QuietInkAlpha = 165;

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
                ShowTip();
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
        ShowTip();
        Invalidate();
    }

    private void ShowTip()
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

            // WHY (25 Nov 2026, Asher: "make the info circles pink and less
            // obvious. the one on the focus button should be white"): the
            // badges were a filled light circle with a dark "i" and a grey
            // edge, which stood out more than help you have to ask for
            // should. Now, at rest, a badge is only a faint ring and "i" in
            // pink - in white on Focus, which is pink itself - and it fills
            // in solid only while the mouse is on it or its tip is up.
            var tone = OnPink ? Color.White : AppTheme.Primary;
            var ink = lit ? (OnPink ? AppTheme.Primary : AppTheme.OnPrimary) : Color.FromArgb(QuietInkAlpha, tone);

            if (!Inside)
            {
                // Over the pen's corner: the card's colour behind, so the pen's edge doesn't run through it.
                var card = Parent?.BackColor is { A: 255 } back ? back : AppTheme.Background;
                using var under = new SolidBrush(card);
                g.FillEllipse(under, circle);
            }

            if (lit)
            {
                using var brush = new SolidBrush(tone);
                g.FillEllipse(brush, circle);
            }
            else
            {
                using var edge = new Pen(Color.FromArgb(QuietRingAlpha, tone), Math.Max(1f, UiLayout.DpiScale));
                g.DrawEllipse(edge, RectangleF.Inflate(circle, -line / 2f, -line / 2f));
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
