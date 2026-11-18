namespace HeatCaller;

/// <summary>
/// "When it shows" as pill buttons (17 Nov 2026, Asher: "I want pill
/// buttons", as option A's mock-up drew them): Auto, then the four parts
/// of the night. Several can be on at once, unlike ChipRow's one-of.
///
/// A pill is off, on (filled pink), or "auto" (a dashed pink edge): a part
/// of the night Auto shows the graphic in. The look is ChipRow's, so the
/// two kinds of chip read as one family. Click a pill, or with the row
/// focused move with the arrow keys and press Space.
///
/// It only draws and reports clicks: the owner decides what a click
/// changes, and sets the states again.
/// </summary>
internal sealed class TogglePills : Control
{
    public enum State { Off, On, Auto }

    /// <summary>One pill: what it stands for and what it says.</summary>
    public sealed record Pill(string Key, string Text);

    private IReadOnlyList<Pill> _pills = Array.Empty<Pill>();
    private readonly Dictionary<string, State> _states = new(StringComparer.Ordinal);
    private int _hot = -1;
    private int _focus;

    public TogglePills()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                 | ControlStyles.UserPaint | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);

        BackColor = Color.Transparent;
        Tag = AppTheme.KeepBackColor;
        TabStop = true;
        Cursor = Cursors.Hand;
        AutoSize = true;
        Margin = Padding.Empty;
        AccessibleRole = AccessibleRole.Grouping;
    }

    /// <summary>A pill was clicked, or Space pressed on it.</summary>
    public event Action<string>? Clicked;

    public void SetPills(IReadOnlyList<Pill> pills)
    {
        _pills = pills.ToList();
        Parent?.PerformLayout();
        Invalidate();
    }

    public IReadOnlyList<Pill> Pills => _pills;

    public State StateOf(string key) => _states.TryGetValue(key, out var s) ? s : State.Off;

    public void SetState(string key, State state)
    {
        if (StateOf(key) == state)
        {
            return;
        }

        _states[key] = state;
        Invalidate();
    }

    private int Gap => UiLayout.AtDpi(6);
    private int PadX => UiLayout.AtDpi(11);
    private int PillHeight => Font.Height + UiLayout.AtDpi(10);

    private int PillWidth(Pill pill) =>
        TextRenderer.MeasureText(pill.Text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Width + PadX * 2;

    /// <summary>Where each pill goes in a row <paramref name="width"/> wide, wrapping as it must.</summary>
    private List<Rectangle> Arrange(int width)
    {
        var boxes = new List<Rectangle>(_pills.Count);
        int x = 0, y = 0, h = PillHeight;

        foreach (var pill in _pills)
        {
            var w = PillWidth(pill);

            if (x > 0 && x + w > width)
            {
                x = 0;
                y += h + Gap;
            }

            boxes.Add(new Rectangle(x, y, w, h));
            x += w + Gap;
        }

        return boxes;
    }

    private int OneLineWidth => _pills.Sum(PillWidth) + Gap * Math.Max(0, _pills.Count - 1);

    private int PreferredHeight(int width)
    {
        var boxes = Arrange(Math.Max(1, width));
        return boxes.Count == 0 ? PillHeight : boxes.Max(b => b.Bottom) + 1;
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = proposedSize.Width is > 0 and < int.MaxValue / 4 ? proposedSize.Width : OneLineWidth;
        return new Size(width, PreferredHeight(width));
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);

        // A row that wraps onto one more or one fewer line tells the card it is in.
        if (PreferredHeight(Width) != Height)
        {
            Parent?.PerformLayout();
        }
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        Parent?.PerformLayout();
    }

    protected override void OnEnabledChanged(EventArgs e)
    {
        base.OnEnabledChanged(e);
        Cursor = Enabled ? Cursors.Hand : Cursors.Default;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;

        var boxes = Arrange(Math.Max(1, Width));

        for (var i = 0; i < _pills.Count; i++)
        {
            var pill = _pills[i];
            var box = boxes[i];
            var state = StateOf(pill.Key);
            var rect = new RectangleF(box.X + 0.5f, box.Y + 0.5f, box.Width - 1f, box.Height - 1f);
            var hot = Enabled && i == _hot;

            var fill = state switch
            {
                State.On => RosterInk.Blend(AppTheme.Primary, AppTheme.Surface, 0.55f),
                State.Auto => RosterInk.Blend(AppTheme.Primary, AppTheme.Surface, 0.85f),
                _ => hot ? AppTheme.SecondaryButtonHover : AppTheme.Surface,
            };
            var edge = state == State.Off ? (hot ? AppTheme.TextMuted : AppTheme.Border) : AppTheme.Primary;
            var ink = !Enabled || state == State.Off ? AppTheme.TextMuted : AppTheme.TextDark;

            if (!Enabled)
            {
                fill = RosterInk.Blend(fill, AppTheme.Surface, 0.5f);
                edge = RosterInk.Blend(edge, AppTheme.Surface, 0.5f);
            }

            using (var path = AppTheme.RoundedRect(rect, box.Height / 2))
            {
                using var brush = new SolidBrush(fill);
                g.FillPath(brush, path);
                using var pen = new Pen(edge, Math.Max(1f, UiLayout.AtDpi(state == State.On ? 2 : 1)));

                if (state == State.Auto)
                {
                    pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
                }

                g.DrawPath(pen, path);
            }

            TextRenderer.DrawText(g, pill.Text, Font, box, ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);

            if (i == _focus && Focused && ShowFocusCues)
            {
                ControlPaint.DrawFocusRectangle(g, Rectangle.Inflate(box, -3, -3));
            }
        }
    }

    private int PillAt(Point p) => Arrange(Math.Max(1, Width)).FindIndex(b => b.Contains(p));

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hot = PillAt(e.Location);

        if (hot != _hot)
        {
            _hot = hot;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        _hot = -1;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (Enabled && e.Button == MouseButtons.Left && PillAt(e.Location) is var i and >= 0)
        {
            Focus();
            _focus = i;
            Clicked?.Invoke(_pills[i].Key);
        }
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.KeyCode == Keys.Left && _focus > 0)
        {
            _focus--;
            Invalidate();
            e.Handled = true;
        }
        else if (e.KeyCode == Keys.Right && _focus < _pills.Count - 1)
        {
            _focus++;
            Invalidate();
            e.Handled = true;
        }
        else if (e.KeyCode is Keys.Space or Keys.Enter && Enabled && _focus < _pills.Count)
        {
            Clicked?.Invoke(_pills[_focus].Key);
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
}
