namespace HeatCaller;

/// <summary>
/// A car's setups, newest first, as a line of cards down the window - the
/// car window's history (CarDetailForm).
///
/// Each card says when, who, where and on what sheet in two short lines,
/// then what changed from the setup before it, written out whole and
/// wrapped - never cut short with "...", which is what the old grid did to
/// exactly the column worth reading (7 Nov 2026, the car window redesign).
/// A click opens the card to show every value on the setup, and closes it
/// again. Its own Edit, Duplicate and Delete are on the "⋯" in its corner
/// and on a right-click, so no Delete shows until one is asked for.
///
/// Drawn rather than built from labels, as a CarCard is: one control, one
/// scrollbar, and nothing for the layout check to find cut short, however
/// long a change list or a driver's name.
/// </summary>
internal sealed class SetupTimeline : ScrollableControl
{
    /// <summary>One setup as the timeline shows it - worked out by the window, which knows the stores.</summary>
    internal sealed record Item(
        string Id,
        string When,
        string Facts,
        string Changes,
        bool ChangesAreQuiet,
        IReadOnlyList<string> Values);

    private sealed record Placed(Item Item, Rectangle Card, int DotY, Rectangle Menu);

    private const TextFormatFlags Wrap = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
    private const TextFormatFlags OneLine = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

    private IReadOnlyList<Item> _items = Array.Empty<Item>();
    private readonly List<Placed> _placed = new();
    private readonly HashSet<string> _open = new(StringComparer.Ordinal);

    private string? _selected;
    private string _empty = string.Empty;

    /// <summary>Raised on a double-click or Enter: the window edits the selected setup.</summary>
    public event Action? Activated;

    /// <summary>Raised on the Delete key: the window asks, then deletes the selected setup.</summary>
    public event Action? DeletePressed;

    /// <summary>The menu the "⋯" and a right-click open, for the selected setup. The window builds it.</summary>
    public ContextMenuStrip? CardMenu { get; set; }

    /// <summary>Raised when the selection moves, so the window can say what its menu will act on.</summary>
    public event Action? SelectionChanged;

    public SetupTimeline()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
                 | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);

        AutoScroll = true;
        TabStop = true;
        BackColor = AppTheme.Background;
        Tag = AppTheme.KeepBackColor;
        AccessibleRole = AccessibleRole.List;
        AccessibleName = "Setups logged for this car";
    }

    public string? SelectedId => _selected;

    /// <summary>The line shown when there are no setups.</summary>
    public string EmptyText
    {
        get => _empty;
        set
        {
            _empty = value;
            Invalidate();
        }
    }

    /// <summary>Shows these setups, keeping the selection and the open cards where they still exist.</summary>
    public void SetItems(IReadOnlyList<Item> items)
    {
        _items = items;
        _open.IntersectWith(items.Select(i => i.Id));

        if (_selected is null || items.All(i => i.Id != _selected))
        {
            _selected = items.Count > 0 ? items[0].Id : null;
        }

        Relayout();
        SelectionChanged?.Invoke();
    }

    /// <summary>The least height worth giving it: one card's first lines, for the window to keep before it scrolls instead.</summary>
    public static int LeastHeight => AppTheme.BoldFont.Height * 2 + AppTheme.SmallFont.Height * 2 + UiLayout.AtTextSize(40);

    // ---------------------------------------------------------- measures

    private static int Pad => UiLayout.AtTextSize(12);

    private static int Gutter => UiLayout.AtTextSize(24);

    private static int CardGap => UiLayout.AtTextSize(10);

    private static int LineGap => UiLayout.AtTextSize(4);

    private static int MenuWidth => TextRenderer.MeasureText("⋯", AppTheme.HeadingFont).Width + UiLayout.AtTextSize(10);

    private static int Measure(string text, Font font, int width) =>
        text.Length == 0 ? 0 : TextRenderer.MeasureText(text, font, new Size(Math.Max(1, width), int.MaxValue), Wrap).Height;

    private static string Opener(Item item, bool open) =>
        open ? "▾  Whole setup" : $"▸  Whole setup - {Plural.Of(item.Values.Count, "value")}";

    private void Relayout()
    {
        // Twice at most: a scrollbar arriving or going changes the width
        // every card wraps to, and so the height that asked for it.
        for (var pass = 0; pass < 2; pass++)
        {
            var before = ClientSize.Width;

            Place(before);
            AutoScrollMinSize = new Size(0, _placed.Count == 0 ? 0 : _placed[^1].Card.Bottom + 1);

            if (ClientSize.Width == before)
            {
                break;
            }
        }

        Invalidate();
    }

    private void Place(int width)
    {
        _placed.Clear();

        var left = Gutter;
        var cardWidth = Math.Max(UiLayout.AtTextSize(120), width - left - 1);
        var inner = cardWidth - Pad * 2;
        var top = 0;

        foreach (var item in _items)
        {
            var open = _open.Contains(item.Id);
            var y = Pad;

            var whenHeight = AppTheme.BoldFont.Height;
            y += whenHeight + LineGap;
            y += Measure(item.Facts, AppTheme.SmallFont, inner) + LineGap * 2;
            y += Measure(item.Changes, AppTheme.BodyFont, inner);

            if (item.Values.Count > 0)
            {
                y += LineGap * 2 + AppTheme.SmallFont.Height;

                if (open)
                {
                    y += LineGap;

                    foreach (var value in item.Values)
                    {
                        y += Measure(value, AppTheme.BodyFont, inner - Pad) + LineGap;
                    }
                }
            }

            y += Pad;

            var card = new Rectangle(left, top, cardWidth, y);
            var menuSide = Math.Max(MenuWidth, whenHeight + LineGap);
            var menu = new Rectangle(card.Right - Pad / 2 - menuSide, card.Top + Pad / 2, menuSide, whenHeight + LineGap);

            _placed.Add(new Placed(item, card, card.Top + Pad + whenHeight / 2, menu));
            top = card.Bottom + CardGap;
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Relayout();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        Relayout();
    }

    // ---------------------------------------------------------- drawing

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(AppTheme.Background);

        if (_placed.Count == 0)
        {
            TextRenderer.DrawText(g, _empty, AppTheme.BodyFont,
                new Rectangle(Pad, Pad, Math.Max(1, ClientSize.Width - Pad * 2), Math.Max(1, ClientSize.Height - Pad * 2)),
                AppTheme.TextMuted, Wrap);
            return;
        }

        var dy = AutoScrollPosition.Y;
        var lineX = Gutter / 2;

        // The line the dots hang on, from the newest to the oldest.
        using (var line = new Pen(AppTheme.Border, Math.Max(1, UiLayout.AtDpi(2))))
        {
            g.DrawLine(line, lineX, _placed[0].DotY + dy, lineX, _placed[^1].DotY + dy);
        }

        foreach (var placed in _placed)
        {
            var card = placed.Card with { Y = placed.Card.Y + dy };

            if (card.Bottom < 0 || card.Top > ClientSize.Height)
            {
                continue;
            }

            PaintCard(g, placed, card, lineX, dy);
        }
    }

    private void PaintCard(Graphics g, Placed placed, Rectangle card, int lineX, int dy)
    {
        var item = placed.Item;
        var selected = item.Id == _selected;
        var open = _open.Contains(item.Id);

        AppTheme.DrawCard(g, card, selected ? AppTheme.Primary : null);

        if (selected)
        {
            using var path = AppTheme.RoundedRect(RectangleF.Inflate(card, -0.5f, -0.5f), AppTheme.CornerRadius);
            using var pen = new Pen(AppTheme.Primary, Focused ? 2f : 1f);
            g.DrawPath(pen, path);
        }

        // The dot, on the line beside the card's date.
        var dot = UiLayout.AtTextSize(10);
        using (var brush = new SolidBrush(selected ? AppTheme.Primary : AppTheme.TextMuted))
        {
            g.FillEllipse(brush, lineX - dot / 2, placed.DotY + dy - dot / 2, dot, dot);
        }

        var menu = placed.Menu with { Y = placed.Menu.Y + dy };
        var inner = card.Width - Pad * 2;
        var x = card.Left + Pad;
        var y = card.Top + Pad;

        TextRenderer.DrawText(g, item.When, AppTheme.BoldFont,
            new Rectangle(x, y, Math.Max(1, menu.Left - x), AppTheme.BoldFont.Height), AppTheme.TextDark, OneLine);
        TextRenderer.DrawText(g, "⋯", AppTheme.HeadingFont, menu, AppTheme.TextMuted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding);
        y += AppTheme.BoldFont.Height + LineGap;

        var factsHeight = Measure(item.Facts, AppTheme.SmallFont, inner);
        TextRenderer.DrawText(g, item.Facts, AppTheme.SmallFont, new Rectangle(x, y, inner, factsHeight), AppTheme.TextMuted, Wrap);
        y += factsHeight + LineGap * 2;

        var changesHeight = Measure(item.Changes, AppTheme.BodyFont, inner);
        TextRenderer.DrawText(g, item.Changes, AppTheme.BodyFont, new Rectangle(x, y, inner, changesHeight),
            item.ChangesAreQuiet ? AppTheme.TextMuted : AppTheme.TextDark, Wrap);
        y += changesHeight;

        if (item.Values.Count == 0)
        {
            return;
        }

        y += LineGap * 2;
        TextRenderer.DrawText(g, Opener(item, open), AppTheme.SmallFont,
            new Rectangle(x, y, inner, AppTheme.SmallFont.Height), AppTheme.Primary, OneLine);
        y += AppTheme.SmallFont.Height;

        if (!open)
        {
            return;
        }

        y += LineGap;

        foreach (var value in item.Values)
        {
            var height = Measure(value, AppTheme.BodyFont, inner - Pad);
            TextRenderer.DrawText(g, value, AppTheme.BodyFont, new Rectangle(x + Pad, y, inner - Pad, height), AppTheme.TextDark, Wrap);
            y += height + LineGap;
        }
    }

    // ---------------------------------------------------------- input

    private Placed? At(Point client)
    {
        var p = client with { Y = client.Y - AutoScrollPosition.Y };
        return _placed.FirstOrDefault(c => c.Card.Contains(p));
    }

    private void Select(string? id)
    {
        if (_selected == id)
        {
            return;
        }

        _selected = id;
        Invalidate();
        SelectionChanged?.Invoke();
    }

    private void Toggle(string id)
    {
        if (!_open.Remove(id))
        {
            _open.Add(id);
        }

        Relayout();
    }

    private void ShowMenu(Point client)
    {
        if (CardMenu is { } menu && _selected is not null)
        {
            menu.Show(this, client);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();

        if (At(e.Location) is not { } hit)
        {
            return;
        }

        Select(hit.Item.Id);

        var menu = hit.Menu with { Y = hit.Menu.Y + AutoScrollPosition.Y };

        if (e.Button == MouseButtons.Right)
        {
            ShowMenu(e.Location);
        }
        else if (e.Button == MouseButtons.Left && menu.Contains(e.Location))
        {
            ShowMenu(new Point(menu.Left, menu.Bottom));
        }
        else if (e.Button == MouseButtons.Left && e.Clicks == 1 && hit.Item.Values.Count > 0)
        {
            Toggle(hit.Item.Id);
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);

        if (e.Button == MouseButtons.Left && At(e.Location) is { } hit
            && !(hit.Menu with { Y = hit.Menu.Y + AutoScrollPosition.Y }).Contains(e.Location))
        {
            Activated?.Invoke();
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = At(e.Location) is not null ? Cursors.Hand : Cursors.Default;
    }

    protected override bool IsInputKey(Keys keyData) =>
        (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Home or Keys.End || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        var index = _placed.FindIndex(p => p.Item.Id == _selected);

        switch (e.KeyCode)
        {
            case Keys.Up when index > 0:
                Choose(index - 1);
                break;
            case Keys.Down when index >= 0 && index < _placed.Count - 1:
                Choose(index + 1);
                break;
            case Keys.Home when _placed.Count > 0:
                Choose(0);
                break;
            case Keys.End when _placed.Count > 0:
                Choose(_placed.Count - 1);
                break;
            case Keys.Space when index >= 0 && _placed[index].Item.Values.Count > 0:
                Toggle(_placed[index].Item.Id);
                break;
            case Keys.Enter when index >= 0:
                Activated?.Invoke();
                break;
            case Keys.Delete when index >= 0:
                DeletePressed?.Invoke();
                break;
            case Keys.Apps when index >= 0:
                var menu = _placed[index].Menu;
                ShowMenu(new Point(menu.Left, menu.Bottom + AutoScrollPosition.Y));
                break;
            default:
                return;
        }

        e.Handled = true;
    }

    private void Choose(int index)
    {
        var placed = _placed[index];
        Select(placed.Item.Id);

        // Into view, the whole card where it fits and its top where not.
        var top = placed.Card.Top + AutoScrollPosition.Y;
        var bottom = placed.Card.Bottom + AutoScrollPosition.Y;

        if (top < 0 || placed.Card.Height > ClientSize.Height)
        {
            AutoScrollPosition = new Point(0, placed.Card.Top);
        }
        else if (bottom > ClientSize.Height)
        {
            AutoScrollPosition = new Point(0, placed.Card.Bottom - ClientSize.Height);
        }

        Invalidate();
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
