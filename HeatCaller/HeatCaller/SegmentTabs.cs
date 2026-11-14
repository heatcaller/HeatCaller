using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>
/// A row of segments in one rounded frame, the chosen one filled - the way a
/// page switches between its own parts without a third row of tabs (14 Nov
/// 2026, Setup's Templates / Tonight / Club rules). Drawn as SegmentedSwitch
/// is, with any number of segments, each as wide as its own caption.
///
/// Click a segment, or with it focused press an arrow key, Home or End.
/// Colours are read at paint time, so it follows a theme change.
/// </summary>
internal sealed class SegmentTabs : Control
{
    private readonly string[] _items;
    private int _selected;

    /// <summary>The segment under the mouse, or -1.</summary>
    private int _hot = -1;

    public SegmentTabs(params string[] items)
    {
        if (items.Length == 0)
        {
            throw new ArgumentException("A segment strip needs at least one segment.", nameof(items));
        }

        _items = items;

        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw
                 | ControlStyles.UserPaint | ControlStyles.Selectable | ControlStyles.SupportsTransparentBackColor, true);

        BackColor = Color.Transparent;
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.PageTabList;
        SyncAccessible();
    }

    /// <summary>Raised when <see cref="SelectedIndex"/> changes, however it was changed.</summary>
    public event EventHandler? SelectedIndexChanged;

    public IReadOnlyList<string> Items => _items;

    /// <summary>The chosen segment.</summary>
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            var index = Math.Clamp(value, 0, _items.Length - 1);

            if (_selected == index)
            {
                return;
            }

            _selected = index;
            SyncAccessible();
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SyncAccessible() => AccessibleName = _items[_selected];

    private int SegmentPad => UiLayout.AtDpi(14);
    private int VerticalPad => UiLayout.AtDpi(6);

    private Size Measure(string text) =>
        TextRenderer.MeasureText(text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);

    /// <summary>Every segment as wide as the widest caption, as SegmentedSwitch's two are.</summary>
    private int SegmentWidth(int index) => _items.Max(item => Measure(item).Width) + SegmentPad * 2;

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = 0;
        var height = 0;

        for (var i = 0; i < _items.Length; i++)
        {
            width += SegmentWidth(i);
            height = Math.Max(height, Measure(_items[i]).Height);
        }

        return new Size(width + 1, height + VerticalPad * 2);
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        AutoSizeNow();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        AutoSizeNow();
    }

    private void AutoSizeNow()
    {
        Size = GetPreferredSize(Size.Empty);
        Invalidate();
    }

    private Rectangle Segment(int index)
    {
        var x = 0;

        for (var i = 0; i < index; i++)
        {
            x += SegmentWidth(i);
        }

        return new Rectangle(x, 0, SegmentWidth(index), Height);
    }

    private int SegmentAt(Point p)
    {
        for (var i = 0; i < _items.Length; i++)
        {
            if (Segment(i).Contains(p))
            {
                return i;
            }
        }

        return -1;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var smoothing = g.SmoothingMode;
        var offset = g.PixelOffsetMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

        try
        {
            var line = Math.Max(1f, UiLayout.AtDpi(1));
            var radius = UiLayout.AtDpi(8);
            var outline = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);

            using (var path = AppTheme.RoundedRect(outline, radius))
            {
                using (var fill = new SolidBrush(AppTheme.Surface))
                {
                    g.FillPath(fill, path);
                }

                var clip = g.Clip;
                g.SetClip(path, CombineMode.Intersect);

                // Who's Racing's Roster / Desk mode look (14 Nov 2026, Asher):
                // the chosen segment in the quiet secondary fill, not pink.
                if (Enabled && _hot >= 0 && _hot != _selected)
                {
                    using var hover = new SolidBrush(RosterInk.Blend(AppTheme.SecondaryButtonHover, AppTheme.Surface, 0.5f));
                    g.FillRectangle(hover, Segment(_hot));
                }

                using (var fill = new SolidBrush(Enabled ? AppTheme.SecondaryButton : AppTheme.Border))
                {
                    g.FillRectangle(fill, Segment(_selected));
                }

                g.Clip = clip;

                var edge = Focused && ShowFocusCues ? AppTheme.Primary : AppTheme.Border;
                using var pen = new Pen(edge, line);
                g.DrawPath(pen, path);
            }

            for (var i = 0; i < _items.Length; i++)
            {
                var colour = i == _selected && Enabled ? AppTheme.TextDark : AppTheme.TextMuted;

                TextRenderer.DrawText(g, _items[i], Font, Segment(i), colour,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            }
        }
        finally
        {
            g.SmoothingMode = smoothing;
            g.PixelOffsetMode = offset;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hot = SegmentAt(e.Location);

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

        if (e.Button == MouseButtons.Left && Enabled && SegmentAt(e.Location) is var at and >= 0)
        {
            Focus();
            SelectedIndex = at;
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

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        switch (e.KeyCode)
        {
            case Keys.Left:
                SelectedIndex = Math.Max(0, _selected - 1);
                e.Handled = true;
                break;
            case Keys.Right:
                SelectedIndex = Math.Min(_items.Length - 1, _selected + 1);
                e.Handled = true;
                break;
            case Keys.Home:
                SelectedIndex = 0;
                e.Handled = true;
                break;
            case Keys.End:
                SelectedIndex = _items.Length - 1;
                e.Handled = true;
                break;
        }
    }
}
