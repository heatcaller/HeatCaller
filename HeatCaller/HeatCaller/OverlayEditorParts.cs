namespace HeatCaller;

/// <summary>
/// The graphics of a layout, down the left of the Stream overlays editor
/// (17 Nov 2026, option A): every one, none cut off, each with an eye that
/// turns it on and off and four dots for the four parts of the night -
/// lit where it shows, the part the picture is showing ringed.
/// </summary>
internal sealed class OverlayLayersList : ListBox
{
    /// <summary>What a row says about a graphic: on, and the parts of the night it shows in.</summary>
    public Func<OverlayElementKind, (bool On, OverlayDuring Shows)>? StateOf { get; set; }

    /// <summary>The part the picture is showing, ringed in every row's dots.</summary>
    public OverlayDuring Showing { get; set; } = OverlayDuring.Racing;

    /// <summary>The eye was clicked (or Space pressed) on a row.</summary>
    public event Action<OverlayElementKind>? EyeClicked;

    public static readonly OverlayDuring[] Parts = { OverlayDuring.Staging, OverlayDuring.Racing, OverlayDuring.Finished, OverlayDuring.Between };

    public OverlayLayersList()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        IntegralHeight = false;
        BorderStyle = BorderStyle.None;
        Font = AppTheme.BodyFont;
        ItemHeight = Math.Max(26, AppTheme.BodyFont.Height + 10);
        FontChanged += (_, _) => ItemHeight = Math.Max(26, Font.Height + 10);

        foreach (var kind in Enum.GetValues<OverlayElementKind>())
        {
            Items.Add(kind);
        }
    }

    public OverlayElementKind? Picked => SelectedItem is OverlayElementKind k ? k : null;

    private int EyeWidth => ItemHeight;

    private int DotsWidth => DotSize * 4 + DotGap * 3 + UiLayout.AtTextSize(8);

    private int DotSize => Math.Max(8, ItemHeight / 4);

    private int DotGap => Math.Max(5, DotSize * 2 / 3);

    protected override void OnMouseDown(MouseEventArgs e)
    {
        var at = IndexFromPoint(e.Location);

        if (e.Button == MouseButtons.Left && at >= 0 && e.X < EyeWidth && Items[at] is OverlayElementKind kind)
        {
            SelectedIndex = at;
            EyeClicked?.Invoke(kind);
            return;
        }

        base.OnMouseDown(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space && Picked is { } kind)
        {
            EyeClicked?.Invoke(kind);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count || Items[e.Index] is not OverlayElementKind kind)
        {
            return;
        }

        var g = e.Graphics;
        var r = e.Bounds;
        var picked = (e.State & DrawItemState.Selected) != 0;
        var (on, shows) = StateOf?.Invoke(kind) ?? (false, OverlayDuring.Always);

        using (var back = new SolidBrush(picked ? AppTheme.SecondaryButton : BackColor))
        {
            g.FillRectangle(back, r);
        }

        if (picked)
        {
            using var bar = new SolidBrush(AppTheme.Primary);
            g.FillRectangle(bar, r.X, r.Y, Math.Max(3, r.Height / 9), r.Height);
        }

        // The eye: an open eye for on, a dashed ring for off, a padlock for the mark that stays on.
        var eye = new Rectangle(r.X + 4, r.Y, EyeWidth - 4, r.Height);
        var eyeText = kind == OverlayElementKind.Mark ? "\U0001F512" : on ? "\U0001F441" : "◌";
        TextRenderer.DrawText(g, eyeText, Font, eye, on ? AppTheme.TextDark : AppTheme.TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        var words = OverlayEditorForm.KindWords(kind).Split(" (")[0];
        var name = new Rectangle(r.X + EyeWidth + 4, r.Y, Math.Max(1, r.Width - EyeWidth - DotsWidth - 8), r.Height);
        TextRenderer.DrawText(g, words, Font, name, on ? AppTheme.TextDark : AppTheme.TextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        var x = r.Right - DotsWidth + UiLayout.AtTextSize(4);
        var y = r.Y + (r.Height - DotSize) / 2;
        var old = g.SmoothingMode;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        foreach (var part in Parts)
        {
            var dot = new Rectangle(x, y, DotSize, DotSize);
            var lit = on && shows.HasFlag(part);
            using (var fill = new SolidBrush(lit ? AppTheme.Primary : AppTheme.Border))
            {
                g.FillRectangle(fill, dot);
            }

            if (part == Showing)
            {
                // A thin ring a step out from the dot, so a lit dot stays lit inside it.
                using var ring = new Pen(AppTheme.TextMuted, 1);
                g.DrawRectangle(ring, dot.X - 3, dot.Y - 3, dot.Width + 5, dot.Height + 5);
            }

            x += DotSize + DotGap;
        }

        g.SmoothingMode = old;

        if ((e.State & DrawItemState.Focus) != 0 && Focused)
        {
            using var ring = new Pen(AppTheme.Border);
            g.DrawRectangle(ring, r.X, r.Y, r.Width - 1, r.Height - 1);
        }
    }
}

/// <summary>
/// The nine places a graphic can sit, as a 3 by 3 of small buttons
/// (17 Nov 2026, option A's "Where"), in place of a list of nine names.
/// Default - where the page has always put it - lights none; the editor's
/// "Usual place" button picks that.
/// </summary>
internal sealed class NinePlaces : Control
{
    private static readonly OverlayAnchor[] Grid =
    {
        OverlayAnchor.TopLeft, OverlayAnchor.Top, OverlayAnchor.TopRight,
        OverlayAnchor.Left, OverlayAnchor.Middle, OverlayAnchor.Right,
        OverlayAnchor.BottomLeft, OverlayAnchor.Bottom, OverlayAnchor.BottomRight,
    };

    private OverlayAnchor _anchor = OverlayAnchor.Default;
    private int _hot = -1;
    private int _focus = 4;

    public event Action<OverlayAnchor>? Picked;

    public NinePlaces()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.Selectable, true);
        TabStop = true;
        Size = new Size(UiLayout.AtTextSize(132), UiLayout.AtTextSize(84));
        AccessibleName = "Where it sits";
    }

    /// <summary>Where the page puts it at its usual place - outlined dashed while it is there.</summary>
    public OverlayAnchor Usual { get; set; } = OverlayAnchor.Default;

    public OverlayAnchor Anchor9
    {
        get => _anchor;
        set
        {
            _anchor = value;
            Invalidate();
        }
    }

    private Rectangle Cell(int i)
    {
        var pad = 4;
        var w = (Width - pad * 4) / 3;
        var h = (Height - pad * 4) / 3;
        return new Rectangle(pad + i % 3 * (w + pad), pad + i / 3 * (h + pad), w, h);
    }

    private int CellAt(Point p)
    {
        for (var i = 0; i < 9; i++)
        {
            if (Cell(i).Contains(p))
            {
                return i;
            }
        }

        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var at = CellAt(e.Location);

        if (at != _hot)
        {
            _hot = at;
            Invalidate();
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hot = -1;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        var at = CellAt(e.Location);

        if (Enabled && at >= 0)
        {
            _focus = at;
            Choose(Grid[at]);
        }

        base.OnMouseDown(e);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var move = e.KeyCode switch { Keys.Left => -1, Keys.Right => 1, Keys.Up => -3, Keys.Down => 3, _ => 0 };

        if (move != 0)
        {
            var next = _focus + move;

            if (next >= 0 && next < 9 && !(Math.Abs(move) == 1 && next / 3 != _focus / 3))
            {
                _focus = next;
                Invalidate();
            }

            e.Handled = true;
        }
        else if (e.KeyCode is Keys.Space or Keys.Enter)
        {
            Choose(Grid[_focus]);
            e.Handled = true;
        }

        base.OnKeyDown(e);
    }

    private void Choose(OverlayAnchor anchor)
    {
        _anchor = anchor;
        Invalidate();
        Picked?.Invoke(anchor);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        Invalidate();
        base.OnGotFocus(e);
    }

    protected override void OnLostFocus(EventArgs e)
    {
        Invalidate();
        base.OnLostFocus(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? AppTheme.Background);

        using (var frame = new SolidBrush(AppTheme.Surface))
        using (var edge = new Pen(AppTheme.Border))
        {
            g.FillRectangle(frame, 0, 0, Width - 1, Height - 1);
            g.DrawRectangle(edge, 0, 0, Width - 1, Height - 1);
        }

        for (var i = 0; i < 9; i++)
        {
            var c = Cell(i);
            var chosen = Grid[i] == _anchor;
            var fill = !Enabled ? AppTheme.Background : chosen ? AppTheme.Primary : i == _hot ? AppTheme.SecondaryButtonHover : AppTheme.Background;

            using (var b = new SolidBrush(fill))
            {
                g.FillRectangle(b, c);
            }

            var usualHere = _anchor == OverlayAnchor.Default && Grid[i] == Usual;

            using (var edge = new Pen(chosen ? AppTheme.Primary : usualHere ? AppTheme.TextDark : AppTheme.Border) { DashStyle = usualHere ? System.Drawing.Drawing2D.DashStyle.Dash : System.Drawing.Drawing2D.DashStyle.Solid })
            {
                g.DrawRectangle(edge, c.X, c.Y, c.Width - 1, c.Height - 1);
            }

            var mark = Rectangle.Inflate(new Rectangle(c.X + c.Width / 2, c.Y + c.Height / 2, 0, 0), Math.Max(3, c.Width / 5), Math.Max(2, c.Height / 5));

            using (var m = new SolidBrush(chosen ? AppTheme.OnPrimary : AppTheme.TextMuted))
            {
                g.FillRectangle(m, mark);
            }

            if (Focused && i == _focus)
            {
                using var ring = new Pen(AppTheme.TextDark) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
                g.DrawRectangle(ring, c.X - 2, c.Y - 2, c.Width + 3, c.Height + 3);
            }
        }
    }
}
