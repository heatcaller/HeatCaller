using System.Drawing.Drawing2D;
using System.Globalization;

namespace HeatCaller;

/// <summary>
/// A month of days for Race History's rail (13 Nov 2026, the redesign that
/// mixed mockups B, C and D): every night with saved sessions is marked
/// with a dot and can be picked; the rest are greyed and cannot. Picking a
/// night lists its sessions under the calendar.
///
/// Drawn by hand rather than a MonthCalendar, which paints itself in the
/// system's colours whatever the theme, and cannot mark days.
///
/// Keys: Left and Right go to the night before or after; Page Up and Page
/// Down a month back or on; Home the latest night.
/// </summary>
internal sealed class RaceCalendar : Control
{
    /// <summary>Each night with sessions (local dates), and how many.</summary>
    private IReadOnlyDictionary<DateTime, int> _nights = new Dictionary<DateTime, int>();

    /// <summary>The first day of the month on show.</summary>
    private DateTime _month = new(DateTime.Today.Year, DateTime.Today.Month, 1);

    private DateTime? _hover;

    public RaceCalendar()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
            | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);

        TabStop = true;
        Tag = AppTheme.KeepBackColor;
        AccessibleName = "Race nights calendar";
        AccessibleRole = AccessibleRole.Table;
        Cursor = Cursors.Hand;
        Height = WantedHeight();
    }

    /// <summary>The night picked, by click or key.</summary>
    public event EventHandler? NightPicked;

    /// <summary>The night picked (a local date), or none.</summary>
    public DateTime? Night { get; private set; }

    /// <summary>The nights with sessions, and the one to show picked - its month comes into view.</summary>
    public void SetNights(IReadOnlyDictionary<DateTime, int> nights, DateTime? night)
    {
        _nights = nights;
        Night = night?.Date;

        if (Night is { } picked)
        {
            _month = new DateTime(picked.Year, picked.Month, 1);
        }

        Invalidate();
    }

    private void Pick(DateTime night)
    {
        if (!_nights.ContainsKey(night.Date))
        {
            return;
        }

        _month = new DateTime(night.Year, night.Month, 1);

        if (Night == night.Date)
        {
            Invalidate();
            return;
        }

        Night = night.Date;
        Invalidate();
        NightPicked?.Invoke(this, EventArgs.Empty);
    }

    private void ShowMonth(int step)
    {
        _month = _month.AddMonths(step);
        Invalidate();
    }

    // --- Sizes, from the face in use ---

    private int HeaderHeight => Font.Height * 2;

    private int WeekdayHeight => Font.Height + 6;

    private int CellHeight => (int)(Font.Height * 1.9f);

    private int WantedHeight() => HeaderHeight + WeekdayHeight + 6 * CellHeight + 2;

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        Height = WantedHeight();
        Invalidate();
    }

    private DayOfWeek FirstDay => CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;

    /// <summary>The day in each of the 42 cells, from the first of the month's week.</summary>
    private DateTime FirstCell
    {
        get
        {
            var back = ((int)_month.DayOfWeek - (int)FirstDay + 7) % 7;
            return _month.AddDays(-back);
        }
    }

    private RectangleF CellBounds(int index)
    {
        var width = Width / 7f;
        return new RectangleF(index % 7 * width, HeaderHeight + WeekdayHeight + index / 7 * CellHeight, width, CellHeight);
    }

    private Rectangle PreviousZone => new(0, 0, HeaderHeight, HeaderHeight);

    private Rectangle NextZone => new(Width - HeaderHeight, 0, HeaderHeight, HeaderHeight);

    private DateTime? DayAt(Point point)
    {
        if (point.Y < HeaderHeight + WeekdayHeight)
        {
            return null;
        }

        var column = Math.Clamp((int)(point.X / (Width / 7f)), 0, 6);
        var row = (point.Y - HeaderHeight - WeekdayHeight) / Math.Max(1, CellHeight);

        if (row is < 0 or > 5)
        {
            return null;
        }

        return FirstCell.AddDays(row * 7 + column);
    }

    // --- Drawing ---

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        using (var ground = new SolidBrush(Parent?.BackColor ?? AppTheme.Surface))
        {
            g.FillRectangle(ground, ClientRectangle);
        }

        var centred = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

        // The month, between its arrows.
        using (var bold = new Font(Font, FontStyle.Bold))
        using (var ink = new SolidBrush(AppTheme.TextDark))
        {
            g.DrawString(_month.ToString("MMMM yyyy", CultureInfo.CurrentCulture), bold, ink,
                new RectangleF(HeaderHeight, 0, Width - HeaderHeight * 2, HeaderHeight), centred);
        }

        DrawArrow(g, PreviousZone, left: true);
        DrawArrow(g, NextZone, left: false);

        // The days of the week.
        using (var micro = new Font(Font.FontFamily, Font.Size * 0.78f, FontStyle.Bold))
        using (var muted = new SolidBrush(AppTheme.TextMuted))
        {
            var names = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames;

            for (var i = 0; i < 7; i++)
            {
                var name = names[((int)FirstDay + i) % 7].ToUpperInvariant();
                var width = Width / 7f;
                g.DrawString(name, micro, muted, new RectangleF(i * width, HeaderHeight, width, WeekdayHeight), centred);
            }
        }

        // The days.
        var first = FirstCell;

        for (var i = 0; i < 42; i++)
        {
            var day = first.AddDays(i);
            var cell = CellBounds(i);
            var inMonth = day.Month == _month.Month;
            var raced = _nights.ContainsKey(day);
            var picked = Night == day;
            var box = RectangleF.Inflate(cell, -2, -2);

            if (picked || (raced && _hover == day))
            {
                using var path = Rounded(box, 8);
                using var fill = new SolidBrush(picked ? AppTheme.Primary : AppTheme.PrimaryTint);
                g.FillPath(fill, path);
            }

            if (day == DateTime.Today && !picked)
            {
                using var path = Rounded(box, 8);
                using var ring = new Pen(AppTheme.Border, 1.2f);
                g.DrawPath(ring, path);
            }

            var colour = picked ? AppTheme.OnPrimary
                : raced ? AppTheme.TextDark
                : inMonth ? Blend(AppTheme.TextMuted, Parent?.BackColor ?? AppTheme.Surface, 0.75f)
                : Blend(AppTheme.TextMuted, Parent?.BackColor ?? AppTheme.Surface, 0.4f);

            using (var face = raced ? new Font(Font, FontStyle.Bold) : new Font(Font, FontStyle.Regular))
            using (var ink = new SolidBrush(colour))
            {
                g.DrawString(day.Day.ToString(CultureInfo.CurrentCulture), face, ink,
                    new RectangleF(box.X, box.Y, box.Width, box.Height - 6), centred);
            }

            if (raced)
            {
                var size = Math.Max(4f, Font.Height / 4f);
                using var dot = new SolidBrush(picked ? AppTheme.OnPrimary : AppTheme.Primary);
                g.FillEllipse(dot, box.X + (box.Width - size) / 2, box.Bottom - size - 3, size, size);
            }
        }

        if (Focused && ShowFocusCues)
        {
            using var focus = new Pen(AppTheme.Primary, 1.4f) { DashStyle = DashStyle.Dot };
            g.DrawRectangle(focus, 0, 0, Width - 1, Height - 1);
        }
    }

    private void DrawArrow(Graphics g, Rectangle zone, bool left)
    {
        var cx = zone.X + zone.Width / 2f;
        var cy = zone.Y + zone.Height / 2f;
        var r = zone.Height / 7f;

        using var pen = new Pen(AppTheme.TextDark, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };

        var tip = left ? cx - r / 2 : cx + r / 2;
        var tail = left ? cx + r / 2 : cx - r / 2;
        g.DrawLines(pen, new[] { new PointF(tail, cy - r), new PointF(tip, cy), new PointF(tail, cy + r) });
    }

    private static GraphicsPath Rounded(RectangleF box, float radius)
    {
        var path = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(box.Width, box.Height));
        path.AddArc(box.X, box.Y, d, d, 180, 90);
        path.AddArc(box.Right - d, box.Y, d, d, 270, 90);
        path.AddArc(box.Right - d, box.Bottom - d, d, d, 0, 90);
        path.AddArc(box.X, box.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Color Blend(Color ink, Color ground, float amount) => Color.FromArgb(
        (int)(ground.R + (ink.R - ground.R) * amount),
        (int)(ground.G + (ink.G - ground.G) * amount),
        (int)(ground.B + (ink.B - ground.B) * amount));

    // --- Pointer and keys ---

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var day = DayAt(e.Location);

        if (_hover != day)
        {
            _hover = day;
            Invalidate();
        }

        var over = PreviousZone.Contains(e.Location) || NextZone.Contains(e.Location) || (day is { } d && _nights.ContainsKey(d));
        Cursor = over ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hover = null;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (PreviousZone.Contains(e.Location))
        {
            ShowMonth(-1);
        }
        else if (NextZone.Contains(e.Location))
        {
            ShowMonth(1);
        }
        else if (DayAt(e.Location) is { } day)
        {
            Pick(day);
        }
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        var ordered = _nights.Keys.OrderBy(d => d).ToList();

        if (ordered.Count == 0)
        {
            return;
        }

        var from = Night ?? ordered[^1];

        switch (e.KeyCode)
        {
            case Keys.Left or Keys.Up:
                if (ordered.LastOrDefault(d => d < from) is var before && before != default)
                {
                    Pick(before);
                }

                e.Handled = true;
                break;

            case Keys.Right or Keys.Down:
                if (ordered.FirstOrDefault(d => d > from) is var after && after != default)
                {
                    Pick(after);
                }

                e.Handled = true;
                break;

            case Keys.PageUp:
                ShowMonth(-1);
                e.Handled = true;
                break;

            case Keys.PageDown:
                ShowMonth(1);
                e.Handled = true;
                break;

            case Keys.Home:
                Pick(ordered[^1]);
                e.Handled = true;
                break;
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
}
