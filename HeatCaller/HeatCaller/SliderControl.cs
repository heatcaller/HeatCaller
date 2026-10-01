using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// A horizontal slider, drawn and hit-tested by one piece of code.
///
/// The skinned TrackBar it replaces had two ideas of where the handle was:
/// the one TrackBarSkin painted, and the narrower one the native control
/// still hit-tested - in a slightly different place, because turning the
/// ticks off moves the native channel. Grabbing the pink handle often
/// grabbed nothing, so a drag took several attempts. FaderControl solved
/// the same problem for the Mixer; this is its horizontal sibling.
///
/// It also behaves the way people expect a volume slider to: pressing
/// anywhere on the track moves the handle straight there and carries on as
/// a drag, so one press-and-slide always works, handle or not.
///
/// The members mirror TrackBar's (Minimum, Maximum, Value, SmallChange,
/// LargeChange, Scroll, ValueChanged) so it drops in where one was.
/// </summary>
public sealed class SliderControl : Control
{
    /// <summary>Raised whenever <see cref="Value"/> changes, however it changed.</summary>
    public event EventHandler? ValueChanged;

    /// <summary>Raised when the user moves the value with the mouse, wheel or keyboard.</summary>
    public event EventHandler? Scroll;

    // The handle and the track are at the text size in use, and at the
    // screen's DPI once the app is started --dpi-aware - exactly these sizes
    // at the normal text size and 96 DPI. At 200% they stayed a 14x20 handle
    // on a 5px track beside words twice their old size (Asher, 30 Sep). The
    // host sizes the slider, with the text; the handle is never taller than
    // it is given, and narrows with it.
    private int ThumbHeight => Math.Clamp(UiLayout.AtTextSize(20), 1, Math.Max(1, Height));
    private int ThumbWidth => Math.Max(1, ThumbHeight * 14 / 20);
    private int TrackHeight => Math.Max(1, Math.Min(UiLayout.AtTextSize(5), ThumbHeight / 2));

    private int _minimum;
    private int _maximum = 100;
    private int _value;
    private bool _dragging;
    private bool _hot;

    public SliderControl()
    {
        SetStyle(
            ControlStyles.UserPaint
            | ControlStyles.AllPaintingInWmPaint
            | ControlStyles.OptimizedDoubleBuffer
            | ControlStyles.ResizeRedraw
            | ControlStyles.Selectable,
            true);

        TabStop = true;
        Size = new Size(200, 32);
        Cursor = Cursors.Hand;
    }

    public int Minimum
    {
        get => _minimum;
        set
        {
            _minimum = value;
            _maximum = Math.Max(_maximum, value);
            SetValue(_value, fromUser: false);
            Invalidate();
        }
    }

    public int Maximum
    {
        get => _maximum;
        set
        {
            _maximum = value;
            _minimum = Math.Min(_minimum, value);
            SetValue(_value, fromUser: false);
            Invalidate();
        }
    }

    public int Value
    {
        get => _value;
        set => SetValue(value, fromUser: false);
    }

    /// <summary>One arrow key, or one notch of the wheel.</summary>
    public int SmallChange { get; set; } = 1;

    /// <summary>Page Up / Page Down.</summary>
    public int LargeChange { get; set; } = 10;

    private void SetValue(int value, bool fromUser)
    {
        var clamped = Math.Clamp(value, _minimum, _maximum);

        if (clamped == _value)
        {
            return;
        }

        _value = clamped;
        Invalidate();

        if (fromUser)
        {
            Scroll?.Invoke(this, EventArgs.Empty);
        }

        ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    // --- Geometry: the only place a handle position is worked out --------

    private int TravelLeft => ThumbWidth / 2;

    private int TravelRight => Math.Max(TravelLeft + 1, Width - ThumbWidth / 2);

    private float Fraction =>
        _maximum == _minimum ? 0f : (_value - _minimum) / (float)(_maximum - _minimum);

    private int CentreX => TravelLeft + (int)Math.Round((TravelRight - TravelLeft) * Fraction);

    private int ValueAt(int x)
    {
        var travel = TravelRight - TravelLeft;
        if (travel <= 0)
        {
            return _value;
        }

        var fraction = Math.Clamp((x - TravelLeft) / (float)travel, 0f, 1f);
        return _minimum + (int)Math.Round((_maximum - _minimum) * fraction);
    }

    private Rectangle ThumbRect =>
        new(CentreX - ThumbWidth / 2, (Height - ThumbHeight) / 2, ThumbWidth, ThumbHeight);

    // --- Mouse ----------------------------------------------------------

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != MouseButtons.Left || !Enabled)
        {
            return;
        }

        Focus();
        _dragging = true;
        Capture = true;

        // Pressed on the handle: leave it where it is until the mouse moves,
        // so a click on it never nudges the value. Anywhere else on the
        // track: jump there at once.
        if (!ThumbRect.Contains(e.Location))
        {
            SetValue(ValueAt(e.X), fromUser: true);
        }

        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (_dragging)
        {
            SetValue(ValueAt(e.X), fromUser: true);
            return;
        }

        var over = ThumbRect.Contains(e.Location);
        if (over != _hot)
        {
            _hot = over;
            Invalidate();
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        EndDrag();
    }

    /// <summary>Capture can be lost without a mouse-up (a dialog opening mid-drag).</summary>
    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);

        if (!Capture)
        {
            EndDrag();
        }
    }

    private void EndDrag()
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        Capture = false;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);

        if (_hot)
        {
            _hot = false;
            Invalidate();
        }
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);

        if (Enabled)
        {
            SetValue(_value + Math.Sign(e.Delta) * SmallChange, fromUser: true);
        }
    }

    // --- Keyboard -------------------------------------------------------

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) switch
        {
            Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End => true,
            _ => base.IsInputKey(keyData),
        };

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        int? target = e.KeyCode switch
        {
            Keys.Right or Keys.Up => _value + SmallChange,
            Keys.Left or Keys.Down => _value - SmallChange,
            Keys.PageUp => _value + LargeChange,
            Keys.PageDown => _value - LargeChange,
            Keys.Home => _minimum,
            Keys.End => _maximum,
            _ => null,
        };

        if (target is { } value)
        {
            SetValue(value, fromUser: true);
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

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    // --- Painting -------------------------------------------------------

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var canvas = new SolidBrush(Parent?.BackColor ?? AppTheme.Background))
        {
            g.FillRectangle(canvas, ClientRectangle);
        }

        var accent = Enabled ? AppTheme.Primary : AppTheme.TextMuted;
        var trackTop = (Height - TrackHeight) / 2;
        var centre = CentreX;

        using (var path = Rounded(new Rectangle(TravelLeft, trackTop, TravelRight - TravelLeft, TrackHeight), TrackHeight / 2))
        using (var brush = new SolidBrush(AppTheme.Border))
        {
            g.FillPath(brush, path);
        }

        if (centre > TravelLeft)
        {
            using var path = Rounded(new Rectangle(TravelLeft, trackTop, centre - TravelLeft, TrackHeight), TrackHeight / 2);
            using var brush = new SolidBrush(accent);
            g.FillPath(brush, path);
        }

        using (var path = Rounded(ThumbRect, ThumbWidth / 2))
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
        var d = Math.Max(1, Math.Min(radius * 2, Math.Min(r.Width, r.Height)));

        path.AddArc(r.Left, r.Top, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Top, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.Left, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        return path;
    }
}
