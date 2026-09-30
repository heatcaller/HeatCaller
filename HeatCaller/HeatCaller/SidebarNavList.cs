namespace HeatCaller;

/// <summary>
/// The sidebar's nav list, built from scratch as a Panel rather than a
/// ListBox. ListBox's owner-draw mode wraps a real native Win32 window,
/// and its flicker turned out to not be reliably fixable - two different
/// standard tricks (DoubleBuffered+aggressive styles, and suppressing
/// WM_ERASEBKGND) each caused worse problems (broken rendering, then
/// visual corruption) than the flicker they were meant to fix, because
/// they fight with that underlying native window in different ways.
///
/// A Panel has no such native window to fight with - its entire rendering
/// goes through the standard, fully-managed WM_PAINT pipeline that
/// DoubleBuffered is actually designed for, so double buffering here is
/// straightforward and reliable rather than a workaround.
/// </summary>
public sealed class SidebarNavList : Panel, IMessageFilter
{
    private const int WM_MOUSEWHEEL = 0x020A;

    /// <summary>
    /// A page's row, at the text size in use. The names are drawn in the nav
    /// face, which grows with the text size; a row that stayed 44px had them
    /// filling it edge to edge at the larger sizes, beside an icon and a
    /// count pill too small for them (the count's text no longer fitted its
    /// pill). The rows' positions are worked out again when the text size
    /// changes - see OnParentFontChanged.
    /// </summary>
    public static int ItemHeight => UiLayout.AtTextSize(44);

    /// <summary>The height of a group heading ("ESSENTIALS", "CLUB TOOLS") above its first page, at the text size in use - it is drawn in the micro face, which grows with it.</summary>
    public static int HeaderHeight => UiLayout.AtTextSize(30);

    /// <summary>
    /// The selected and hovered plate's inset from the list's left edge, and
    /// how much narrower than the list it is. Part of the sidebar's frame,
    /// which is a fixed width (MainForm.Designer) that follows the screen and
    /// not the text size - so these do too, and what the frame has left over
    /// goes to the page names, which end in "..." when it is not enough.
    /// </summary>
    private static int PlateLeft => UiLayout.AtDpi(7);

    private static int PlateNarrowerBy => UiLayout.AtDpi(16);

    /// <summary>
    /// Where the icon starts inside the plate. A heading's text starts here
    /// too, so it lines up with the icons under it. On the screen's scale but
    /// not the text size, like the plate's other edges: the sidebar keeps its
    /// width at every text size, so every gap that grew with the text would be
    /// taken from the room the larger names need.
    /// </summary>
    private static int IconInset => UiLayout.AtDpi(14);

    private readonly List<string> _items = new();

    /// <summary>Where each item's row starts, in content pixels - headings push the rows below them down.</summary>
    private readonly List<int> _itemTops = new();

    /// <summary>The group headings, and where each is drawn, in content pixels.</summary>
    private readonly List<(int Top, string Text)> _headers = new();

    private int _contentHeight;
    private Func<string, string?>? _sectionOf;

    /// <summary>
    /// Counts to show as a pill against a nav item - "3 rentals still out"
    /// needs to be visible from whatever page you're on, not only once you
    /// navigate to it. Keyed by item name; absent or zero draws nothing.
    /// </summary>
    private readonly Dictionary<string, int> _badges = new(StringComparer.OrdinalIgnoreCase);
    private int _selectedIndex = -1;
    private int _hoverIndex = -1;
    private int _dragSourceIndex = -1;
    private Point _dragStartPoint;

    /// <summary>
    /// How far the list is scrolled down, in pixels. This control paints
    /// itself rather than hosting child controls, so WinForms' built-in
    /// AutoScroll has nothing to work with - the offset has to be applied
    /// manually to both drawing and hit-testing.
    /// </summary>
    private int _scrollOffset;

    private int TotalContentHeight => _contentHeight;

    /// <summary>
    /// The group each item belongs to, or null for none. A heading is drawn
    /// wherever the group changes from one row to the next, so the items
    /// have to arrive already grouped - MainForm orders them that way.
    /// </summary>
    public Func<string, string?>? SectionOf
    {
        get => _sectionOf;
        set
        {
            _sectionOf = value;
            RecalculateRows();
            Invalidate();
        }
    }

    /// <summary>Works out where every heading and row sits. The only place row positions come from.</summary>
    private void RecalculateRows()
    {
        _itemTops.Clear();
        _headers.Clear();

        var y = 0;
        string? current = null;

        foreach (var item in _items)
        {
            var section = _sectionOf?.Invoke(item);

            if (section is not null && !string.Equals(section, current, StringComparison.Ordinal))
            {
                _headers.Add((y, section));
                y += HeaderHeight;
            }

            current = section;
            _itemTops.Add(y);
            y += ItemHeight;
        }

        _contentHeight = y;
    }

    private int MaxScrollOffset => Math.Max(0, TotalContentHeight - Height);

    /// <summary>Fired whenever the selection changes, whether by click or programmatically via SelectedItem's setter.</summary>
    public event Action? SelectionChanged;

    /// <summary>Fired after a drag-and-drop reorder completes, with the full new item order.</summary>
    public event Action<List<string>>? ItemsReordered;

    public SidebarNavList()
    {
        DoubleBuffered = true;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        Cursor = Cursors.Hand;
        AllowDrop = true;
        TabStop = false;

        // WinForms sends wheel events to the FOCUSED control, not the
        // hovered one - and this list never takes focus (that would steal
        // it from the nav search box above). A message filter lets us
        // intercept the wheel whenever the cursor is over us, which is
        // what people actually expect from a scrollable list.
        Application.AddMessageFilter(this);
    }

    public IReadOnlyList<string> Items => _items;

    public string? SelectedItem
    {
        get => _selectedIndex >= 0 && _selectedIndex < _items.Count ? _items[_selectedIndex] : null;
        set
        {
            var index = value is null ? -1 : _items.IndexOf(value);
            if (index == _selectedIndex)
            {
                return;
            }
            _selectedIndex = index;
            EnsureVisible(_selectedIndex);
            Invalidate();
            SelectionChanged?.Invoke();
        }
    }

    /// <summary>Replaces the full item list. Selection is preserved by name if the previously-selected item still exists, otherwise cleared.</summary>
    public void SetItems(IEnumerable<string> items)
    {
        var previouslySelected = SelectedItem;
        _items.Clear();
        _items.AddRange(items);
        RecalculateRows();
        _selectedIndex = previouslySelected is not null ? _items.IndexOf(previouslySelected) : -1;

        // The list may have shrunk (nav search filtering, hiding pages) -
        // re-clamp so we're never scrolled past the new end.
        _scrollOffset = Math.Clamp(_scrollOffset, 0, MaxScrollOffset);
        Invalidate();
    }

    private int ItemTop(int index) => index >= 0 && index < _itemTops.Count ? _itemTops[index] : index * ItemHeight;

    private Rectangle GetItemRect(int index) => new(0, ItemTop(index) - _scrollOffset, Width, ItemHeight);

    /// <summary>The item under a y position, or -1 - a heading is not an item.</summary>
    private int IndexFromY(int y)
    {
        var adjusted = y + _scrollOffset;
        if (adjusted < 0)
        {
            return -1;
        }

        for (var i = 0; i < _items.Count; i++)
        {
            var top = ItemTop(i);
            if (adjusted >= top && adjusted < top + ItemHeight)
            {
                return i;
            }
        }

        return -1;
    }

    private void InvalidateItem(int index)
    {
        if (index >= 0 && index < _items.Count)
        {
            Invalidate(GetItemRect(index));
        }
    }

    /// <summary>Intercepts wheel messages app-wide and handles them only when the cursor is over this list.</summary>
    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg != WM_MOUSEWHEEL || !Visible || IsDisposed || MaxScrollOffset == 0)
        {
            return false;
        }

        if (!RectangleToScreen(ClientRectangle).Contains(Cursor.Position))
        {
            return false;
        }

        // Delta lives in the high word of WParam, and is signed.
        var delta = (short)((long)m.WParam >> 16);

        // Scroll by whole rows rather than raw pixels so the list always
        // comes to rest on a clean row boundary instead of mid-item.
        SetScrollOffset(_scrollOffset + (delta > 0 ? -ItemHeight : ItemHeight));
        return true; // handled - don't let it reach whatever has focus
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Application.RemoveMessageFilter(this);
        }
        base.Dispose(disposing);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        // A taller window can reveal blank space below the last item if the
        // list was scrolled to the bottom - re-clamp so that can't happen.
        SetScrollOffset(_scrollOffset);
    }

    /// <summary>
    /// A new text size, reaching the list through the sidebar's font:
    /// MainForm.ApplyFontScale gives the window the new body face, and the
    /// sidebar, which wears no face of its own, passes it on. The list has a
    /// face of its own (MainForm.Designer's NavFont), so it is told only this
    /// way and never gets OnFontChanged for it.
    /// </summary>
    protected override void OnParentFontChanged(EventArgs e)
    {
        base.OnParentFontChanged(e);
        FollowTextSize();
    }

    /// <summary>The list's own face set again - as for OnParentFontChanged.</summary>
    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        FollowTextSize();
    }

    /// <summary>
    /// The rows and headings placed again at the text size in use - they are
    /// kept in _itemTops, worked out at the size in use when they were - and
    /// the scrolling range with them, keeping the selected page in view.
    /// </summary>
    private void FollowTextSize()
    {
        RecalculateRows();

        // The range is the content's height less the list's, so it changed
        // with the rows; clamped straight into the field, as SetItems does,
        // since SetScrollOffset leaves an unchanged offset alone.
        _scrollOffset = Math.Clamp(_scrollOffset, 0, MaxScrollOffset);
        EnsureVisible(_selectedIndex);
        Invalidate();
    }

    private void SetScrollOffset(int value)
    {
        var clamped = Math.Clamp(value, 0, MaxScrollOffset);
        if (clamped == _scrollOffset)
        {
            return;
        }

        _scrollOffset = clamped;
        Invalidate();
    }

    /// <summary>Scrolls the given item into view if it's currently off-screen - used when selection changes programmatically (nav search, restoring the last page) rather than by a click.</summary>
    private void EnsureVisible(int index)
    {
        if (index < 0 || index >= _items.Count || Height <= 0)
        {
            return;
        }

        var top = ItemTop(index);
        var bottom = top + ItemHeight;

        if (top < _scrollOffset)
        {
            SetScrollOffset(top);
        }
        else if (bottom > _scrollOffset + Height)
        {
            SetScrollOffset(bottom - Height);
        }
    }


    /// <summary>
    /// Sets the count pill for a nav item. Zero or less clears it, so
    /// callers can push a raw count without special-casing "none".
    /// </summary>
    public void SetBadge(string itemName, int count)
    {
        var had = _badges.TryGetValue(itemName, out var existing) ? existing : 0;
        if (had == count)
        {
            return; // nothing changed - don't repaint the whole sidebar
        }

        if (count > 0)
        {
            _badges[itemName] = count;
        }
        else
        {
            _badges.Remove(itemName);
        }

        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        using (var backBrush = new SolidBrush(AppTheme.SidebarBackground))
        {
            g.FillRectangle(backBrush, e.ClipRectangle);
        }

        for (var i = 0; i < _items.Count; i++)
        {
            var bounds = GetItemRect(i);
            if (!bounds.IntersectsWith(e.ClipRectangle))
            {
                continue;
            }
            DrawItem(g, bounds, i);
        }

        for (var h = 0; h < _headers.Count; h++)
        {
            DrawHeader(g, _headers[h].Top - _scrollOffset, _headers[h].Text, first: h == 0);
        }

        DrawScrollIndicator(g);
    }

    /// <summary>
    /// A slim thumb on the right edge when the list overflows. Without some
    /// visible cue, items scrolled off the bottom are simply invisible with
    /// no hint that scrolling would reveal them.
    /// </summary>
    private void DrawScrollIndicator(Graphics g)
    {
        if (MaxScrollOffset == 0 || Height <= 0 || TotalContentHeight <= 0)
        {
            return;
        }

        // A scrollbar: it follows the screen, not the text size, as the
        // sidebar's width does.
        var trackWidth = UiLayout.AtDpi(4);
        var x = Width - trackWidth - UiLayout.AtDpi(2);

        using (var trackBrush = new SolidBrush(Color.FromArgb(40, AppTheme.SidebarText)))
        {
            g.FillRectangle(trackBrush, x, 0, trackWidth, Height);
        }

        var thumbHeight = Math.Max(UiLayout.AtDpi(24), (int)(Height * (Height / (float)TotalContentHeight)));
        var travel = Height - thumbHeight;
        var thumbY = MaxScrollOffset == 0 ? 0 : (int)(travel * (_scrollOffset / (float)MaxScrollOffset));

        // Synthwave keeps its cyan. Elsewhere the selected-page colour rather
        // than Primary: the two are the same on most looks, but a light look
        // with a near-black primary would draw a thumb nobody can see on the
        // dark rail - SidebarSelected already swaps in something visible.
        var thumbColor = AppTheme.CurrentMode == AppTheme.Mode.Synthwave ? AppTheme.SynthCyan : AppTheme.SidebarSelected;
        using var thumbBrush = new SolidBrush(Color.FromArgb(150, thumbColor));
        g.FillRectangle(thumbBrush, x, thumbY, trackWidth, thumbHeight);
    }


    /// <summary>
    /// A group heading: small, spaced, muted capitals, with a hairline above
    /// every group but the first. Deliberately quiet - it sorts the list
    /// without competing with the pages in it.
    /// </summary>
    private void DrawHeader(Graphics g, int top, string text, bool first)
    {
        var bounds = new Rectangle(0, top, Width, HeaderHeight);
        if (bounds.Bottom < 0 || bounds.Top > Height)
        {
            return;
        }

        using (var back = new SolidBrush(AppTheme.SidebarBackground))
        {
            g.FillRectangle(back, bounds);
        }

        // In line with the icons under it (PlateLeft + IconInset), and run to
        // the frame's margins on the right. Down the heading, at the text
        // size in use, as the heading's height is.
        var left = PlateLeft + IconInset;

        if (!first)
        {
            using var line = new Pen(Color.FromArgb(50, AppTheme.SidebarText));
            var lineY = bounds.Top + UiLayout.AtTextSize(6);
            g.DrawLine(line, left, lineY, Width - UiLayout.AtDpi(16), lineY);
        }

        var textTop = UiLayout.AtTextSize(8);
        var textBounds = new Rectangle(left, bounds.Top + textTop, Width - left - UiLayout.AtDpi(11), bounds.Height - textTop);

        // Ended in "..." rather than cut off mid-letter if a heading ever
        // outgrows the sidebar's fixed width at a large text size.
        TextRenderer.DrawText(g, text, AppTheme.MicroFont, textBounds, AppTheme.SidebarTextMuted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
    }

    /// <summary>Rounded rectangle path for the count pill.</summary>
    private static System.Drawing.Drawing2D.GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        var d = radius * 2;

        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();

        return path;
    }

    private void DrawItem(Graphics g, Rectangle bounds, int index)
    {
        var name = _items[index];
        var selected = index == _selectedIndex;
        var hovered = !selected && index == _hoverIndex;

        using (var backBrush = new SolidBrush(AppTheme.SidebarBackground))
        {
            g.FillRectangle(backBrush, bounds);
        }

        // The selected and hovered states are an inset rounded plate rather
        // than a full-bleed gradient band, and there are no longer dividers
        // between rows. A gradient across the full width reads as a button,
        // which these are not; and a band that touches both edges leaves
        // the list no margin, so 25 of them stacked looked like a striped
        // wall instead of a menu.
        //
        // Across, the plate keeps to the sidebar's frame (PlateLeft); down,
        // it keeps its share of the row, and its rounding with it, at the
        // text size in use.
        var plateRadius = UiLayout.AtTextSize(9);
        var plate = new Rectangle(
            bounds.Left + PlateLeft,
            bounds.Top + UiLayout.AtTextSize(3),
            Math.Max(10, bounds.Width - PlateNarrowerBy),
            bounds.Height - UiLayout.AtTextSize(6));

        if (selected || hovered)
        {
            using var path = RoundedRect(plate, plateRadius);
            using var fill = new SolidBrush(selected
                ? Color.FromArgb(AppTheme.CurrentMode == AppTheme.Mode.Light ? 46 : 38, AppTheme.SidebarSelected)
                : AppTheme.SidebarHover);
            g.FillPath(fill, path);
        }

        if (selected)
        {
            // A short accent bar at the left of the plate, clipped to the
            // plate so it takes the same corner radius. This is the only
            // saturated colour in the rail, which is what makes it findable.
            // The bar is a line's thickness: it follows the screen, not the
            // text size.
            using var path = RoundedRect(plate, plateRadius);
            var saved = g.Save();
            g.SetClip(path);

            using (var accentBrush = new SolidBrush(AppTheme.SidebarSelected))
            {
                g.FillRectangle(accentBrush, plate.Left, plate.Top, UiLayout.AtDpi(3), plate.Height);
            }

            g.Restore(saved);
        }

        // White, NOT OnPrimary. The selected plate is only a faint wash of
        // the selected colour (alpha 38-46 of 255) over the sidebar, which is
        // dark in every look - so the text is sitting on dark chrome, not on
        // a Primary fill. OnPrimary is near-black on a yellow primary and
        // would vanish here.
        var textColor = selected ? Color.White : (hovered ? AppTheme.SidebarText : AppTheme.SidebarTextMuted);
        var iconColor = selected ? AppTheme.SidebarSelected : textColor;

        // Everything inside the plate - the icon, the pill and the room round
        // them - at the text size in use, so it stays in proportion to the
        // name drawn beside it.
        var iconSize = UiLayout.AtTextSize(18);
        var iconBounds = new Rectangle(plate.Left + IconInset, bounds.Top + (bounds.Height - iconSize) / 2, iconSize, iconSize);
        AppTheme.DrawNavIcon(g, iconBounds, name, iconColor);

        // Between the name, or the pill, and the plate's right-hand end. A
        // gap, on the screen's scale only - see IconInset.
        var endGap = UiLayout.AtDpi(10);

        // A count pill, when this item has one. Measured first so the label
        // can be given the remaining width rather than running underneath.
        var pillWidth = 0;
        var hasCount = _badges.TryGetValue(name, out var count) && count > 0;

        if (hasCount)
        {
            var countText = count > 99 ? "99+" : count.ToString();
            var measured = TextRenderer.MeasureText(countText, AppTheme.SmallFont);
            pillWidth = Math.Max(UiLayout.AtTextSize(22), measured.Width + UiLayout.AtTextSize(12));

            var pillHeight = UiLayout.AtTextSize(18);
            var pillRect = new Rectangle(
                plate.Right - pillWidth - endGap,
                bounds.Top + (bounds.Height - pillHeight) / 2,
                pillWidth,
                pillHeight);

            // Rounded by half its height, so it stays a capsule at every size.
            using (var path = RoundedRect(pillRect, pillHeight / 2))
            using (var fill = new SolidBrush(AppTheme.Accent))
            {
                g.FillPath(fill, path);
            }

            TextRenderer.DrawText(g, countText, AppTheme.SmallFont, pillRect,
                AppTheme.OnAccent, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            pillWidth += UiLayout.AtDpi(8); // breathing room between label and pill - a gap, see IconInset
        }

        // The name gets what is left of the plate, and ends in "..." when
        // that is not enough - the sidebar keeps its width at every text
        // size, so at the larger ones the longer names are shortened.
        var textLeft = iconBounds.Right + UiLayout.AtDpi(12);
        var textRect = new Rectangle(textLeft, bounds.Top,
            Math.Max(10, plate.Right - textLeft - endGap - pillWidth), bounds.Height);

        TextRenderer.DrawText(g, name, AppTheme.NavFont, textRect, textColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var index = IndexFromY(e.Y);
        if (index != _hoverIndex)
        {
            InvalidateItem(_hoverIndex);
            _hoverIndex = index;
            InvalidateItem(_hoverIndex);
        }

        if (e.Button != MouseButtons.Left || _dragSourceIndex < 0)
        {
            return;
        }

        var dragSize = SystemInformation.DragSize;
        var dragZone = new Rectangle(
            _dragStartPoint.X - dragSize.Width / 2,
            _dragStartPoint.Y - dragSize.Height / 2,
            dragSize.Width, dragSize.Height);

        if (!dragZone.Contains(e.Location))
        {
            DoDragDrop(_dragSourceIndex, DragDropEffects.Move);
            _dragSourceIndex = -1;
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        var index = IndexFromY(e.Y);
        _dragSourceIndex = index;
        _dragStartPoint = e.Location;

        if (index >= 0 && index != _selectedIndex)
        {
            _selectedIndex = index;
            Invalidate();
            SelectionChanged?.Invoke();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        InvalidateItem(_hoverIndex);
        _hoverIndex = -1;
    }

    protected override void OnDragOver(DragEventArgs drgevent)
    {
        base.OnDragOver(drgevent);
        drgevent.Effect = DragDropEffects.Move;
    }

    protected override void OnDragDrop(DragEventArgs drgevent)
    {
        base.OnDragDrop(drgevent);

        var sourceIndex = _dragSourceIndex;
        _dragSourceIndex = -1;
        if (sourceIndex < 0 || sourceIndex >= _items.Count)
        {
            return;
        }

        var point = PointToClient(new Point(drgevent.X, drgevent.Y));
        var targetIndex = IndexFromY(point.Y);
        if (targetIndex < 0)
        {
            targetIndex = _items.Count - 1;
        }
        if (targetIndex == sourceIndex)
        {
            return;
        }

        var sourceName = _items[sourceIndex];

        // Nothing is pinned any more. This used to refuse to move "Live
        // Session" and never let anything land above it - but that page is
        // Connection now and not pinned, so the rule only stopped every
        // page from being dragged to the top of the list.
        _items.RemoveAt(sourceIndex);
        var insertAt = Math.Clamp(targetIndex, 0, _items.Count);
        _items.Insert(insertAt, sourceName);
        RecalculateRows();
        _selectedIndex = _items.IndexOf(sourceName);

        Invalidate();
        ItemsReordered?.Invoke(new List<string>(_items));
    }
}
