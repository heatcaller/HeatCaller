namespace HeatCaller;

/// <summary>
/// A small time-of-day picker drawn entirely with GDI+.
///
/// WinForms' own DateTimePicker wraps a native Windows control that
/// ignores BackColor/ForeColor almost completely, so it renders in system
/// colors and looks pasted-on against the dark and synthwave themes. This
/// paints every pixel itself, so it themes like everything else.
///
/// Interaction mirrors what people expect from a stock time field: click a
/// segment to select it, then use the arrows, the mouse wheel, up/down
/// keys, or just type digits. Left/right move between segments.
/// </summary>
public sealed class TimePicker : Control
{
    private enum Segment { Hour, Minute, Meridiem }

    private int _hour = 12;      // 1-12 as displayed
    private int _minute;
    private bool _isPm;
    private Segment _selected = Segment.Hour;

    // Tracks consecutive digits so typing "1" then "0" gives 10, not 0.
    private int _typedDigits;
    private int _typedValue;

    private Rectangle _hourRect;
    private Rectangle _minuteRect;
    private Rectangle _meridiemRect;
    private Rectangle _upRect;
    private Rectangle _downRect;

    public event Action? ValueChanged;

    public TimePicker()
    {
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint
                 | ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        Size = new Size(120, 26);
        Font = AppTheme.BodyFont;
        Cursor = Cursors.Hand;
    }

    /// <summary>The picked time, as a TimeSpan since midnight.</summary>
    public TimeSpan Value
    {
        get
        {
            var hour24 = _hour % 12 + (_isPm ? 12 : 0);
            return new TimeSpan(hour24, _minute, 0);
        }
        set
        {
            var hour24 = Math.Clamp(value.Hours, 0, 23);
            _isPm = hour24 >= 12;
            _hour = hour24 % 12 == 0 ? 12 : hour24 % 12;
            _minute = Math.Clamp(value.Minutes, 0, 59);
            Invalidate();
        }
    }

    public string FormattedValue => AppTime.TimeOfDay(Value);

    /// <summary>In 24-hour mode there is no AM/PM to pick, so that segment is hidden and the hour runs 0-23.</summary>
    private static bool Use24Hour => AppTime.Use24Hour;

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        LayoutSegments();
    }

    private void LayoutSegments()
    {
        const int arrowWidth = 18;
        var textWidth = Math.Max(10, Width - arrowWidth - 6);

        // Hour and minute get equal space; the meridiem needs a little more
        // to fit "AM"/"PM" comfortably.
        var unit = textWidth / 5;
        _hourRect = new Rectangle(4, 0, unit * 3 / 2, Height);
        _minuteRect = new Rectangle(_hourRect.Right + 8, 0, unit * 3 / 2, Height);
        _meridiemRect = new Rectangle(_minuteRect.Right + 6, 0, unit * 2, Height);

        var arrowX = Width - arrowWidth - 2;
        _upRect = new Rectangle(arrowX, 2, arrowWidth, Height / 2 - 2);
        _downRect = new Rectangle(arrowX, Height / 2, arrowWidth, Height / 2 - 2);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        var back = Enabled ? AppTheme.Surface : AppTheme.SecondaryButton;
        var text = Enabled ? AppTheme.TextDark : AppTheme.TextMuted;

        using (var backBrush = new SolidBrush(back))
        {
            g.FillRectangle(backBrush, ClientRectangle);
        }

        using (var borderPen = new Pen(Focused && Enabled ? AppTheme.Primary : AppTheme.Border))
        {
            g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
        }

        var hourText = Use24Hour
            ? $"{(_hour % 12 + (_isPm ? 12 : 0)):00}"
            : $"{_hour}";
        DrawSegment(g, _hourRect, hourText, Segment.Hour, text);
        DrawSegment(g, _minuteRect, $"{_minute:00}", Segment.Minute, text);
        if (!Use24Hour)
        {
            DrawSegment(g, _meridiemRect, _isPm ? "PM" : "AM", Segment.Meridiem, text);
        }

        // Colon between hour and minute, vertically centered.
        TextRenderer.DrawText(g, ":", Font,
            new Rectangle(_hourRect.Right, 0, 8, Height), text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        DrawArrow(g, _upRect, up: true, text);
        DrawArrow(g, _downRect, up: false, text);
    }

    private void DrawSegment(Graphics g, Rectangle rect, string value, Segment segment, Color textColor)
    {
        if (_selected == segment && Enabled && Focused)
        {
            using var highlight = new SolidBrush(AppTheme.PrimaryTint);
            g.FillRectangle(highlight, rect.X, 3, rect.Width, Height - 6);
        }

        TextRenderer.DrawText(g, value, Font, rect, textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private void DrawArrow(Graphics g, Rectangle rect, bool up, Color color)
    {
        using var brush = new SolidBrush(color);
        var cx = rect.X + rect.Width / 2f;
        var cy = rect.Y + rect.Height / 2f;
        const float w = 4f;
        const float h = 3f;

        var points = up
            ? new[] { new PointF(cx, cy - h), new PointF(cx - w, cy + h), new PointF(cx + w, cy + h) }
            : new[] { new PointF(cx, cy + h), new PointF(cx - w, cy - h), new PointF(cx + w, cy - h) };

        g.FillPolygon(brush, points);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!Enabled)
        {
            return;
        }

        Focus();
        _typedDigits = 0;

        if (_upRect.Contains(e.Location)) { Step(1); return; }
        if (_downRect.Contains(e.Location)) { Step(-1); return; }

        if (_hourRect.Contains(e.Location)) { _selected = Segment.Hour; }
        else if (_minuteRect.Contains(e.Location)) { _selected = Segment.Minute; }
        else if (!Use24Hour && _meridiemRect.Contains(e.Location)) { _selected = Segment.Meridiem; }

        Invalidate();
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (Enabled && Focused)
        {
            Step(e.Delta > 0 ? 1 : -1);
        }
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Up or Keys.Down or Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!Enabled)
        {
            return;
        }

        switch (e.KeyCode)
        {
            case Keys.Up: Step(1); e.Handled = true; break;
            case Keys.Down: Step(-1); e.Handled = true; break;
            case Keys.Left: MoveSegment(-1); e.Handled = true; break;
            case Keys.Right: MoveSegment(1); e.Handled = true; break;
        }
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        base.OnKeyPress(e);
        if (!Enabled)
        {
            return;
        }

        // A/P jump the meridiem directly, matching stock time fields.
        if (!Use24Hour && char.ToUpperInvariant(e.KeyChar) is 'A' or 'P')
        {
            _isPm = char.ToUpperInvariant(e.KeyChar) == 'P';
            Invalidate();
            ValueChanged?.Invoke();
            return;
        }

        if (!char.IsDigit(e.KeyChar) || _selected == Segment.Meridiem)
        {
            return;
        }

        var digit = e.KeyChar - '0';
        _typedValue = _typedDigits == 0 ? digit : _typedValue * 10 + digit;
        _typedDigits++;

        if (_selected == Segment.Hour && Use24Hour)
        {
            var typed = Math.Clamp(_typedValue, 0, 23);
            _isPm = typed >= 12;
            _hour = typed % 12 == 0 ? 12 : typed % 12;

            if (_typedDigits >= 2 || _typedValue > 2)
            {
                _typedDigits = 0;
                MoveSegment(1);
            }
        }
        else if (_selected == Segment.Hour)
        {
            _hour = Math.Clamp(_typedValue == 0 ? 12 : _typedValue, 1, 12);
            // Two digits typed, or a value that can't grow further - commit
            // and move on, the way a stock time field does.
            if (_typedDigits >= 2 || _typedValue > 1)
            {
                _typedDigits = 0;
                MoveSegment(1);
            }
        }
        else
        {
            _minute = Math.Clamp(_typedValue, 0, 59);
            if (_typedDigits >= 2 || _typedValue > 5)
            {
                _typedDigits = 0;
                MoveSegment(1);
            }
        }

        Invalidate();
        ValueChanged?.Invoke();
    }

    private void MoveSegment(int direction)
    {
        // Meridiem isn't shown in 24-hour mode, so it isn't selectable.
        var maxSegment = Use24Hour ? 1 : 2;
        var next = (int)_selected + direction;
        _selected = (Segment)Math.Clamp(next, 0, maxSegment);
        _typedDigits = 0;
        Invalidate();
    }

    private void Step(int delta)
    {
        switch (_selected)
        {
            case Segment.Hour when Use24Hour:
            {
                // Step through all 24 hours, carrying the AM/PM flag rather
                // than exposing it as a separate segment.
                var hour24 = (_hour % 12 + (_isPm ? 12 : 0) + delta + 24) % 24;
                _isPm = hour24 >= 12;
                _hour = hour24 % 12 == 0 ? 12 : hour24 % 12;
                break;
            }

            case Segment.Hour:
                _hour += delta;
                if (_hour > 12) { _hour = 1; }
                if (_hour < 1) { _hour = 12; }
                break;

            case Segment.Minute:
                _minute += delta;
                if (_minute > 59) { _minute = 0; }
                if (_minute < 0) { _minute = 59; }
                break;

            case Segment.Meridiem:
                _isPm = !_isPm;
                break;
        }

        _typedDigits = 0;
        Invalidate();
        ValueChanged?.Invoke();
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        Invalidate();
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        _typedDigits = 0;
        Invalidate();
    }
}
