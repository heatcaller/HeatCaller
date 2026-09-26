using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// A mixer fader.
///
/// This is a control of its own rather than a skinned TrackBar, and that is
/// the whole point of it.
///
/// What was here before was a native Windows trackbar with a NativeWindow
/// subclass attached that swallowed WM_PAINT and drew a different picture
/// over the top. That arrangement has two separate ideas of where the handle
/// is: the one the skin DRAWS, and the one the native control HIT-TESTS. The
/// handle you reach for is the drawn one. The handle that decides whether you
/// grabbed anything is the other one. There are several ways for those to
/// disagree - a vertical trackbar's value axis runs top-to-bottom while a
/// fader reads bottom-to-top, changing the tick style moves the channel and
/// reshapes the thumb, and the control recreates its own window when either
/// of those is set - and when they do disagree the fader looks completely
/// normal and cannot be grabbed.
///
/// Four separate attempts at reconciling the two failed, each of them
/// plausible, each of them leaving the fader just as dead. So there is now
/// only one of them. <see cref="ThumbRect"/> is used to draw the handle AND
/// to decide whether a click landed on it, so what you can see and what you
/// can grab cannot drift apart - they are the same rectangle.
///
/// <see cref="Percent"/> means what it says: 100 is the top, 0 is the bottom,
/// loud at the top like every mixer ever built. Nothing is flipped anywhere.
/// </summary>
public sealed class FaderControl : Control
{
    /// <summary>Raised whenever <see cref="Percent"/> changes, however it changed.</summary>
    public event EventHandler? PercentChanged;

    private const int ThumbHeight = 20;
    private const int ThumbWidth = 28;
    private const int TrackWidth = 6;

    /// <summary>How far either side of the handle still counts as grabbing it.</summary>
    private const int GrabSlack = 5;

    private int _percent = 100;
    private bool _dragging;
    private bool _hot;

    /// <summary>
    /// Where in the handle it was picked up, so a drag doesn't jump the
    /// handle's centre to the pointer on the first pixel of movement.
    /// </summary>
    private int _grabOffset;

    public FaderControl()
    {
        SetStyle(
            ControlStyles.UserPaint
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.Selectable,
            true);

        TabStop = true;
        Size = new Size(48, 200);
    }

    public int Percent
    {
        get => _percent;
        set
        {
            var clamped = Math.Clamp(value, 0, 100);

            if (clamped == _percent)
            {
                return;
            }

            _percent = clamped;
            Invalidate();
            PercentChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>A nudge: one arrow key, or one notch of the wheel.</summary>
    public int SmallChange { get; set; } = 1;

    /// <summary>A jump: clicking the track above or below the handle.</summary>
    public int LargeChange { get; set; } = 10;

    // --- Geometry ---------------------------------------------------------
    //
    // Everything below reads from these. Nothing anywhere else works out a
    // handle position of its own, which is the property that makes this
    // control grabbable and kept the last one from being.

    /// <summary>The highest the handle's CENTRE can go.</summary>
    private int TravelTop => ThumbHeight / 2;

    /// <summary>The lowest the handle's centre can go.</summary>
    private int TravelBottom => Math.Max(TravelTop + 1, Height - ThumbHeight / 2);

    private int CentreFor(int percent) =>
        TravelBottom - (int)Math.Round((TravelBottom - TravelTop) * (Math.Clamp(percent, 0, 100) / 100f));

    private int PercentAt(int y)
    {
        var travel = TravelBottom - TravelTop;

        if (travel <= 0)
        {
            return _percent;
        }

        return Math.Clamp((int)Math.Round((TravelBottom - y) * 100f / travel), 0, 100);
    }

    private Rectangle ThumbRect
    {
        get
        {
            var centre = CentreFor(_percent);

            return new Rectangle(
                (Width - ThumbWidth) / 2,
                centre - ThumbHeight / 2,
                ThumbWidth,
                ThumbHeight);
        }
    }

    /// <summary>
    /// A more forgiving version of the handle, for hit-testing only.
    ///
    /// The drawn handle is twenty pixels tall, which is a small target. This
    /// widens the grab to the full width of the control and a few pixels
    /// past each end. It costs nothing - the track above and below still
    /// pages - and it is the difference between a fader that invites a drag
    /// and one that merely permits it.
    /// </summary>
    private Rectangle GrabRect
    {
        get
        {
            var thumb = ThumbRect;
            return new Rectangle(0, thumb.Top - GrabSlack, Width, thumb.Height + GrabSlack * 2);
        }
    }

    // --- Mouse ------------------------------------------------------------

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != MouseButtons.Left || !Enabled)
        {
            return;
        }

        Focus();

        if (GrabRect.Contains(e.Location))
        {
            _dragging = true;

            // Measured against the handle's centre, so picking it up by its
            // bottom edge and dragging does not snap it up first.
            _grabOffset = e.Y - CentreFor(_percent);

            Capture = true;
            Invalidate();
            return;
        }

        // On the track: a jump towards the click. Above the handle is
        // louder, below is quieter - which is which way a fader moves.
        Percent += e.Y < ThumbRect.Top ? LargeChange : -LargeChange;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_dragging)
        {
            Percent = PercentAt(e.Y - _grabOffset);
            return;
        }

        var over = GrabRect.Contains(e.Location);

        if (over != _hot)
        {
            _hot = over;
            Cursor = over ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (_dragging)
        {
            _dragging = false;
            Capture = false;
            Invalidate();
        }
    }

    /// <summary>
    /// Capture can be taken away without a mouse-up ever arriving - another
    /// window stealing focus, or a modal dialog opening mid-drag. Without
    /// this the fader stays convinced it is being dragged and follows the
    /// pointer around afterwards.
    /// </summary>
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);

        if (!Capture && _dragging)
        {
            _dragging = false;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);

        if (_hot)
        {
            _hot = false;
            Cursor = Cursors.Default;
            Invalidate();
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        if (Enabled)
        {
            Percent += Math.Sign(e.Delta) * SmallChange;
        }
    }

    // --- Keyboard ---------------------------------------------------------

    /// <summary>
    /// Claims the arrow keys. Without this the containing form treats them
    /// as "move to the next control" and the fader never sees them.
    /// </summary>
    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) switch
        {
            Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End => true,
            _ => base.IsInputKey(keyData),
        };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        switch (e.KeyCode)
        {
            case Keys.Up:
                Percent += SmallChange;
                break;
            case Keys.Down:
                Percent -= SmallChange;
                break;
            case Keys.PageUp:
                Percent += LargeChange;
                break;
            case Keys.PageDown:
                Percent -= LargeChange;
                break;
            case Keys.Home:
                Percent = 100;
                break;
            case Keys.End:
                Percent = 0;
                break;
            default:
                return;
        }

        e.Handled = true;
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

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Invalidate();
    }

    // --- Painting ---------------------------------------------------------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        // The card behind, so the fader sits on it rather than in a box of
        // its own.
        using (var canvas = new SolidBrush(Parent?.BackColor ?? AppTheme.Surface))
        {
            g.FillRectangle(canvas, ClientRectangle);
        }

        var accent = Enabled ? AppTheme.Primary : AppTheme.TextMuted;
        var thumb = ThumbRect;
        var centre = CentreFor(_percent);
        var trackLeft = (Width - TrackWidth) / 2;

        // The track, full length.
        var trackRect = new Rectangle(trackLeft, TravelTop, TrackWidth, TravelBottom - TravelTop);

        using (var path = Rounded(trackRect, TrackWidth / 2))
        using (var brush = new SolidBrush(AppTheme.Border))
        {
            g.FillPath(brush, path);
        }

        // ...and the part of it that is "on", which is everything below the
        // handle. A fader reads as a column of level, not as a slot with a
        // marker in it.
        var filledHeight = TravelBottom - centre;

        if (filledHeight > 0)
        {
            var filled = new Rectangle(trackLeft, centre, TrackWidth, filledHeight);

            using var path = Rounded(filled, TrackWidth / 2);
            using var brush = new SolidBrush(accent);
            g.FillPath(brush, path);
        }

        // The handle.
        using (var path = Rounded(thumb, 5))
        {
            using (var brush = new SolidBrush(_dragging || _hot ? Lighten(accent, 0.18f) : accent))
            {
                g.FillPath(brush, path);
            }

            if (Focused)
            {
                using var ring = new Pen(AppTheme.TextDark, 1.5f);
                g.DrawPath(ring, path);
            }
        }

        // Grip lines, so it reads as something to take hold of.
        using (var grip = new Pen(Color.FromArgb(110, Color.Black), 1f))
        {
            for (var offset = -3; offset <= 3; offset += 3)
            {
                g.DrawLine(grip, thumb.Left + 7, centre + offset, thumb.Right - 7, centre + offset);
            }
        }
    }

    private static Color Lighten(Color colour, float amount) =>
        Color.FromArgb(
            colour.A,
            (int)Math.Clamp(colour.R + (255 - colour.R) * amount, 0, 255),
            (int)Math.Clamp(colour.G + (255 - colour.G) * amount, 0, 255),
            (int)Math.Clamp(colour.B + (255 - colour.B) * amount, 0, 255));

    private static GraphicsPath Rounded(Rectangle r, int radius)
    {
        var path = new GraphicsPath();

        // A radius bigger than the box itself produces a path that will not
        // fill - worth guarding, since the track is only a few pixels wide.
        var d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));

        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        return path;
    }
}
