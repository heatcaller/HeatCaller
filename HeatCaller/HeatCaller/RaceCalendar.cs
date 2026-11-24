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
    /// <summary>
    /// One dot under a night: whose colour, and whether it is drawn as a
    /// ring - Championship's Calendar marks each race day in its own colour,
    /// and a day started but not yet raced as a ring (14 Nov 2026, mock-up A).
    /// </summary>
    public readonly record struct NightMark(Color Colour, bool Hollow);

    /// <summary>Each night with sessions (local dates), and how many.</summary>
    private IReadOnlyDictionary<DateTime, int> _nights = new Dictionary<DateTime, int>();

    /// <summary>The dots each night shows, when the page gives its own. A night not in here shows the one dot in the theme's colour.</summary>
    private IReadOnlyDictionary<DateTime, IReadOnlyList<NightMark>> _marks = new Dictionary<DateTime, IReadOnlyList<NightMark>>();

    /// <summary>
    /// Nights with nothing on them can be picked too - the Calendar tab, where
    /// picking tonight's empty night offers a new race day. Race History
    /// leaves it off: a night without sessions has nothing to show there.
    /// </summary>
    public bool EmptyNightsPickable { get; init; }

    /// <summary>The first day of the month on show.</summary>
    // WHY (23 Nov 2026, bug hunt 24): the month of tonight's race night, not
    // the calendar day's - opened at 00:30 on the 1st it used to start on the
    // new month, away from the night still being raced.
    private DateTime _month = new(TonightAtTheTrack.NightOf(DateTime.Now).Year, TonightAtTheTrack.NightOf(DateTime.Now).Month, 1);

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

    /// <summary>The nights as SetNights takes them, each with the dots it shows - one per race day on it, in that day's colour.</summary>
    public void SetMarkedNights(IReadOnlyDictionary<DateTime, IReadOnlyList<NightMark>> marks, DateTime? night)
    {
        _marks = marks;
        SetNights(marks.ToDictionary(pair => pair.Key.Date, pair => pair.Value.Count), night);
    }

    private bool CanPick(DateTime day) => EmptyNightsPickable || _nights.ContainsKey(day.Date);

    private void Pick(DateTime night)
    {
        if (!CanPick(night))
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

            if (picked || (CanPick(day) && _hover == day))
            {
                using var path = Rounded(box, 8);
                using var fill = new SolidBrush(picked ? AppTheme.Primary : AppTheme.PrimaryTint);
                g.FillPath(fill, path);
            }

            // WHY (23 Nov 2026, bug hunt 24): tonight is the race night (6am
            // to 6am). On the calendar day the outline jumped to tomorrow's
            // square at midnight while the page still labelled tonight's
            // night "Tonight".
            if (day == TonightAtTheTrack.NightOf(DateTime.Now) && !picked)
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
                DrawDots(g, box, day, picked);
            }
        }

        if (Focused && ShowFocusCues)
        {
            using var focus = new Pen(AppTheme.Primary, 1.4f) { DashStyle = DashStyle.Dot };
            g.DrawRectangle(focus, 0, 0, Width - 1, Height - 1);
        }
    }

    /// <summary>
    /// The dots under a night, side by side and centred: the page's own
    /// (SetMarkedNights), else one in the theme's colour. On the picked
    /// night they are all drawn in the ink on the fill, as its number is.
    /// </summary>
    private void DrawDots(Graphics g, RectangleF box, DateTime day, bool picked)
    {
        var marks = _marks.TryGetValue(day, out var own) && own.Count > 0
            ? own
            : new[] { new NightMark(AppTheme.Primary, Hollow: false) };

        // No more than fit under the number; three race days on one night is already a lot.
        var shown = Math.Min(marks.Count, 4);
        var size = Math.Max(4f, Font.Height / 4f);
        var gap = Math.Max(2f, size / 2);
        var width = shown * size + (shown - 1) * gap;
        var x = box.X + (box.Width - width) / 2;
        var y = box.Bottom - size - 3;

        for (var i = 0; i < shown; i++, x += size + gap)
        {
            var colour = picked ? AppTheme.OnPrimary : marks[i].Colour;

            if (marks[i].Hollow)
            {
                using var ring = new Pen(colour, Math.Max(1.2f, size / 4));
                var inset = ring.Width / 2;
                g.DrawEllipse(ring, x + inset, y + inset, size - ring.Width, size - ring.Width);
            }
            else
            {
                using var dot = new SolidBrush(colour);
                g.FillEllipse(dot, x, y, size, size);
            }
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

        var over = PreviousZone.Contains(e.Location) || NextZone.Contains(e.Location) || (day is { } d && CanPick(d));
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
