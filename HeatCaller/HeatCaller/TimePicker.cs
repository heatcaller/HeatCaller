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

    /// <summary>
    /// A new font is how a new text size reaches the picker - Race Day's
    /// Setup hands it the body face at the size in use (TextSize.Wear) - and
    /// the insets, gaps and arrows are at the text size in use, so the
    /// segments are laid out again even when the size it is given has not
    /// changed.
    /// </summary>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        LayoutSegments();
    }

    /// <summary>
    /// The size its time needs in the font it is drawn in: each segment
    /// wide enough for the widest thing it shows ("00", "PM") with a little
    /// room either side, and as tall as a text box in the same font.
    ///
    /// Only asked by a layout that sizes it (AutoSize) - Setup's first race
    /// time, which follows the text size. A picker given a size by hand
    /// keeps it: the 120x26 it was built at held the time at the normal
    /// text size and clipped it at any larger one.
    /// </summary>
    public override Size GetPreferredSize(Size proposedSize)
    {
        const TextFormatFlags flags = TextFormatFlags.SingleLine | TextFormatFlags.NoPadding;

        var digits = TextRenderer.MeasureText("00", Font, Size.Empty, flags).Width;
        var meridiem = Math.Max(
            TextRenderer.MeasureText("AM", Font, Size.Empty, flags).Width,
            TextRenderer.MeasureText("PM", Font, Size.Empty, flags).Width);

        // The room either side of the text in its segment.
        var room = Math.Max(6, Font.Height / 2);

        // See LayoutSegments: hour and minute take three tenths of the
        // segments' width each, the meridiem four.
        var unit = Math.Max(
            (int)Math.Ceiling((digits + room) / 1.5),
            (int)Math.Ceiling((meridiem + room) / 2.0));

        return new Size(unit * 5 + FixedWidth + 4, Math.Max(26, Font.Height + 8));
    }

    // The picker's fixed parts - its left inset, the gaps after the hour and
    // the minute, and the arrows' strip - are at the text size in use, as
    // the time beside them is: at a fixed 18px the arrows were a sliver
    // beside a time twice the size at 200%. Worked out each time they are
    // asked, never kept, as the text size can change; OnFontChanged lays the
    // segments out again when it does. Exactly the old pixels at 100%.

    /// <summary>The arrows' strip down the right-hand side: 18px at the normal text size.</summary>
    private static int ArrowWidth => UiLayout.AtTextSize(18);

    /// <summary>The room before the hour: 4px at the normal text size.</summary>
    private static int LeftInset => UiLayout.AtTextSize(4);

    /// <summary>The gap after the hour, which the colon is drawn in: 8px at the normal text size.</summary>
    private static int ColonWidth => UiLayout.AtTextSize(8);

    /// <summary>The gap after the minute: 6px at the normal text size.</summary>
    private static int MinuteGap => UiLayout.AtTextSize(6);

    /// <summary>The room round the arrows' strip, most of it before it: 6px at the normal text size.</summary>
    private static int ArrowRoom => UiLayout.AtTextSize(6);

    /// <summary>
    /// Room across that is not the segments': the left inset (4), the gaps
    /// after the hour (8) and the minute (6), and the arrows with a little
    /// either side of them - 42px at the normal text size, at the size in
    /// use. GetPreferredSize counts it, so a picker that sizes itself is
    /// wide enough for the larger strip.
    /// </summary>
    private static int FixedWidth => LeftInset + ColonWidth + MinuteGap + ArrowWidth + ArrowRoom;

    private void LayoutSegments()
    {
        // The segments share what the insets, the gaps and the arrows leave.
        // They used to share the width less the arrows only, and the gaps
        // came on top - so the meridiem's box ran under the arrows by the
        // width of the gaps, and a wide "PM" was drawn into their strip.
        var textWidth = Math.Max(10, Width - FixedWidth);

        // Hour and minute get equal space; the meridiem needs a little more
        // to fit "AM"/"PM" comfortably.
        var unit = textWidth / 5;
        _hourRect = new Rectangle(LeftInset, 0, unit * 3 / 2, Height);
        _minuteRect = new Rectangle(_hourRect.Right + ColonWidth, 0, unit * 3 / 2, Height);
        _meridiemRect = new Rectangle(_minuteRect.Right + MinuteGap, 0, unit * 2, Height);

        // Two pixels clear of the border on the right, as the strip is above
        // and below: that is the border's clearance, not a size that follows
        // the text.
        var arrowX = Width - ArrowWidth - 2;
        _upRect = new Rectangle(arrowX, 2, ArrowWidth, Height / 2 - 2);
        _downRect = new Rectangle(arrowX, Height / 2, ArrowWidth, Height / 2 - 2);
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

        // Colon between hour and minute, vertically centered - in the gap
        // after the hour, as wide as it is at the text size in use.
        TextRenderer.DrawText(g, ":", Font,
            new Rectangle(_hourRect.Right, 0, ColonWidth, Height), text,
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

        // The glyph's half-width and half-height, at the text size in use,
        // in step with its strip - 4 by 3 at the normal size.
        float w = UiLayout.AtTextSize(4f);
        float h = UiLayout.AtTextSize(3f);

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
