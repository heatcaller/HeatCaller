using System.Drawing.Drawing2D;

namespace HeatCaller;

/// <summary>One line of a <see cref="ColumnChecklist"/>: a column, shown or not - or, with no column, the "Hidden" divider.</summary>
public readonly record struct ColumnRow(KpiDefinition? Column, bool Shown)
{
    public bool IsDivider => Column is null;
}

/// <summary>
/// Which of a leaderboard's columns are shown, and in what order (15 Nov
/// 2026). The shown ones come first, in board order; the rest follow in the
/// catalogue's order, so the list reads as the board left to right and then
/// what else there is. Kept apart from the drawn list so it can be tested
/// without a window.
/// </summary>
public sealed class ColumnChoices
{
    private readonly List<KpiDefinition> _offered;
    private readonly List<KpiDefinition> _shown = new();

    /// <summary>Raised after every change - a tick, a move, a reset.</summary>
    public event EventHandler? Changed;

    public ColumnChoices(IEnumerable<string> shownKeys, IEnumerable<KpiDefinition> offered)
    {
        _offered = offered.ToList();
        Fill(shownKeys);
    }

    public IReadOnlyList<KpiDefinition> Shown => _shown;

    public List<string> ShownKeys => _shown.Select(d => d.Key).ToList();

    public IEnumerable<KpiDefinition> Hidden => _offered.Where(d => !IsShown(d.Key));

    /// <summary>
    /// The list as drawn: the shown columns, then - when there are both - a
    /// divider, then the hidden ones.
    /// </summary>
    public IReadOnlyList<ColumnRow> Rows
    {
        get
        {
            var rows = _shown.Select(d => new ColumnRow(d, true)).ToList();
            var hidden = Hidden.ToList();

            if (rows.Count > 0 && hidden.Count > 0)
            {
                rows.Add(new ColumnRow(null, false));
            }

            rows.AddRange(hidden.Select(d => new ColumnRow(d, false)));
            return rows;
        }
    }

    public bool IsShown(string key) => IndexOf(key) >= 0;

    /// <summary>A hidden column can always be shown; a shown one can go unless it is the last - a board with no columns is no board.</summary>
    public bool CanToggle(string key) => !IsShown(key) || _shown.Count > 1;

    /// <summary>Shows a hidden column (at the right-hand end of the board) or hides a shown one.</summary>
    public bool Toggle(string key)
    {
        if (!CanToggle(key))
        {
            return false;
        }

        var index = IndexOf(key);
        if (index >= 0)
        {
            _shown.RemoveAt(index);
        }
        else if (_offered.FirstOrDefault(d => Same(d.Key, key)) is { } column)
        {
            _shown.Add(column);
        }
        else
        {
            return false;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Whether a shown column can go one place left (-1) or right (+1).</summary>
    public bool CanMove(string key, int direction)
    {
        var index = IndexOf(key);
        var target = index + Math.Sign(direction);
        return index >= 0 && direction != 0 && target >= 0 && target < _shown.Count;
    }

    public bool Move(string key, int direction)
    {
        if (!CanMove(key, direction))
        {
            return false;
        }

        var index = IndexOf(key);
        var target = index + Math.Sign(direction);
        (_shown[index], _shown[target]) = (_shown[target], _shown[index]);

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>Starts over from these columns - Restore Defaults. Says nothing when that is what is already shown.</summary>
    public void Set(IEnumerable<string> keys)
    {
        var before = ShownKeys;
        Fill(keys);

        if (!before.SequenceEqual(ShownKeys, StringComparer.OrdinalIgnoreCase))
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Fill(IEnumerable<string> keys)
    {
        _shown.Clear();

        foreach (var key in keys)
        {
            if (!IsShown(key) && _offered.FirstOrDefault(d => Same(d.Key, key)) is { } column)
            {
                _shown.Add(column);
            }
        }
    }

    private int IndexOf(string key) => _shown.FindIndex(d => Same(d.Key, key));

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// The leaderboard's columns as a list to tick and reorder (15 Nov 2026):
/// a tick box on every row, the "Hidden" divider between the board's columns
/// and the rest, and up and down arrows on the row under the mouse or picked.
/// Drawn by hand in the theme's colours. Used by the column headings'
/// right-click (ColumnsPopover) and by the full picker (KpiPickerForm), so
/// the two always look and behave alike.
///
/// Keys: Space ticks, Ctrl+Up/Ctrl+Down moves the picked column.
/// </summary>
internal sealed class ColumnChecklist : ListBox
{
    private enum Part
    {
        None,
        Tick,
        Label,
        Up,
        Down,
    }

    private IReadOnlyList<ColumnRow> _rows = Array.Empty<ColumnRow>();
    private int _hover = -1;
    private Part _hoverPart;
    private int _lastIndex = -1;
    private bool _reloading;

    private ToolTip? _tip;
    private int _tipRow = -1;

    public ColumnChecklist(ColumnChoices choices)
    {
        Choices = choices;

        DrawMode = DrawMode.OwnerDrawFixed;
        IntegralHeight = false;
        BackColor = AppTheme.Surface;
        ForeColor = AppTheme.TextDark;
        Font = AppTheme.BodyFont;

        // The row follows the font, so a taller text setting gets taller
        // rows rather than clipped ones.
        ItemHeight = Math.Max(26, AppTheme.BodyFont.Height + 10);

        Choices.Changed += (_, _) => Reload();
        Reload();
    }

    public ColumnChoices Choices { get; }

    /// <summary>A column's description as a themed tip beside the row under the mouse - for the popover, which has no room for a description line.</summary>
    public bool ShowDescriptionTips { get; set; }

    /// <summary>The picked row's column, or null for none (or the divider).</summary>
    public KpiDefinition? SelectedColumn =>
        SelectedIndex >= 0 && SelectedIndex < _rows.Count ? _rows[SelectedIndex].Column : null;

    public int RowCount => _rows.Count;

    /// <summary>Picks the row for this column, if it is in the list.</summary>
    public void SelectColumn(string? key)
    {
        if (key is null)
        {
            return;
        }

        for (var i = 0; i < _rows.Count; i++)
        {
            if (_rows[i].Column is { } column && string.Equals(column.Key, key, StringComparison.OrdinalIgnoreCase))
            {
                SelectedIndex = i;
                return;
            }
        }
    }

    /// <summary>Moves the picked column one place: -1 up the list (left on the board), +1 down.</summary>
    public void MoveSelected(int direction)
    {
        if (SelectedColumn is { } column)
        {
            Choices.Move(column.Key, direction);
        }
    }

    public void ToggleSelected()
    {
        if (SelectedColumn is { } column)
        {
            Choices.Toggle(column.Key);
        }
    }

    /// <summary>Wide enough for the longest column name, its tick box and the two arrows, at the text size in use.</summary>
    public int WidthToFit()
    {
        var widest = Choices.Shown.Concat(Choices.Hidden)
            .Select(d => TextRenderer.MeasureText(d.Label, AppTheme.BodyFont).Width)
            .DefaultIfEmpty(80)
            .Max();

        var tick = TickBoxIn(new Rectangle(0, 0, 100, ItemHeight));
        return tick.Right + UiLayout.AtTextSize(10) + widest + UiLayout.AtTextSize(12) + 2 * ArrowWidth() + EdgeInset() + SystemInformation.VerticalScrollBarWidth;
    }

    private void Reload()
    {
        var keep = SelectedColumn?.Key;

        _reloading = true;
        BeginUpdate();

        try
        {
            _rows = Choices.Rows;
            Items.Clear();

            foreach (var row in _rows)
            {
                Items.Add(row.Column?.Label ?? "Hidden");
            }

            SelectColumn(keep);
        }
        finally
        {
            EndUpdate();
            _reloading = false;
        }

        _lastIndex = SelectedIndex;
        Invalidate();
    }

    protected override void OnSelectedIndexChanged(EventArgs e)
    {
        // The divider can't be picked: the pick steps past it, the way the
        // keys were going.
        if (!_reloading && SelectedIndex >= 0 && SelectedIndex < _rows.Count && _rows[SelectedIndex].IsDivider)
        {
            var step = SelectedIndex < _lastIndex ? -1 : 1;
            var next = SelectedIndex + step;

            if (next < 0 || next >= _rows.Count)
            {
                next = SelectedIndex - step;
            }

            if (next >= 0 && next < _rows.Count)
            {
                SelectedIndex = next;
                return;
            }
        }

        // The arrows follow the pick, so the row left behind is redrawn too.
        if (_lastIndex >= 0 && _lastIndex < Items.Count)
        {
            Invalidate(GetItemRectangle(_lastIndex));
        }

        _lastIndex = SelectedIndex;
        base.OnSelectedIndexChanged(e);
    }

    protected override bool IsInputKey(Keys keyData)
    {
        // Ctrl+Up/Down too, so they reach OnKeyDown rather than the menu
        // around the list.
        var key = keyData & Keys.KeyCode;
        return (key is Keys.Up or Keys.Down && (keyData & Keys.Alt) == 0) || base.IsInputKey(keyData);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Space)
        {
            ToggleSelected();
            e.Handled = e.SuppressKeyPress = true;
            return;
        }

        // Ctrl, not Alt: in a drop-down, Alt on its own is the menu key and
        // closes the menu before the arrow ever arrives.
        if (e.Control && !e.Alt && e.KeyCode is Keys.Up or Keys.Down)
        {
            MoveSelected(e.KeyCode == Keys.Up ? -1 : 1);
            e.Handled = e.SuppressKeyPress = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        var index = IndexFromPoint(e.Location);
        if (index < 0 || index >= _rows.Count || _rows[index].Column is not { } column)
        {
            return;
        }

        switch (PartAt(index, e.Location))
        {
            case Part.Tick:
                Choices.Toggle(column.Key);
                break;

            case Part.Up:
                Choices.Move(column.Key, -1);
                break;

            case Part.Down:
                Choices.Move(column.Key, 1);
                break;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var index = IndexFromPoint(e.Location);
        if (index >= _rows.Count)
        {
            index = -1;
        }

        var part = index >= 0 ? PartAt(index, e.Location) : Part.None;

        if (index == _hover && part == _hoverPart)
        {
            return;
        }

        var previous = _hover;
        _hover = index;
        _hoverPart = part;

        InvalidateRow(previous);
        InvalidateRow(index);

        ShowTip(index);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);

        var previous = _hover;
        _hover = -1;
        _hoverPart = Part.None;
        InvalidateRow(previous);

        _tipRow = -1;
        _tip?.Hide(this);
    }

    private void ShowTip(int index)
    {
        if (!ShowDescriptionTips || index == _tipRow)
        {
            return;
        }

        _tipRow = index;

        if (_tip is null)
        {
            _tip = new ToolTip();
            AppTheme.StyleToolTip(_tip, keep: true);
        }

        _tip.Hide(this);

        if (index >= 0 && _rows[index].Column is { } column && !string.IsNullOrWhiteSpace(column.Description))
        {
            // Beside the list rather than under the mouse, so it covers none
            // of the rows.
            var row = GetItemRectangle(index);
            AppTheme.ShowToolTip(_tip, column.Description, this, Width + UiLayout.AtTextSize(10), row.Top);
        }
    }

    private void InvalidateRow(int index)
    {
        if (index >= 0 && index < Items.Count)
        {
            Invalidate(GetItemRectangle(index));
        }
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _rows.Count)
        {
            return;
        }

        var g = e.Graphics;
        var bounds = e.Bounds;
        var row = _rows[e.Index];

        if (row.Column is not { } column)
        {
            DrawDivider(g, bounds);
            return;
        }

        var picked = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
        var hovered = e.Index == _hover;

        using (var back = new SolidBrush(picked ? AppTheme.PrimaryTint : hovered ? AppTheme.RowAlt : AppTheme.Surface))
        {
            g.FillRectangle(back, bounds);
        }

        var box = TickBoxIn(bounds);
        TickBox.Draw(g, box, row.Shown ? CheckState.Checked : CheckState.Unchecked,
            Enabled && Choices.CanToggle(column.Key), hot: hovered && _hoverPart == Part.Tick);

        var withArrows = row.Shown && (picked || hovered);
        var textRight = withArrows ? UpArrowIn(bounds).Left : bounds.Right - EdgeInset();
        var textLeft = box.Right + UiLayout.AtTextSize(10);
        var textRect = new Rectangle(textLeft, bounds.Top, Math.Max(0, textRight - textLeft), bounds.Height);

        TextRenderer.DrawText(g, column.Label, AppTheme.BodyFont, textRect,
            row.Shown ? AppTheme.TextDark : AppTheme.TextMuted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        if (withArrows)
        {
            DrawArrow(g, UpArrowIn(bounds), up: true, Choices.CanMove(column.Key, -1), hovered && _hoverPart == Part.Up);
            DrawArrow(g, DownArrowIn(bounds), up: false, Choices.CanMove(column.Key, 1), hovered && _hoverPart == Part.Down);
        }
    }

    /// <summary>"Hidden" in the small muted face, with a hairline running on from it to the right-hand edge.</summary>
    private void DrawDivider(Graphics g, Rectangle bounds)
    {
        using (var back = new SolidBrush(AppTheme.Surface))
        {
            g.FillRectangle(back, bounds);
        }

        const string text = "Hidden";
        var left = bounds.Left + TickBoxInset();
        var size = TextRenderer.MeasureText(g, text, AppTheme.SmallFont, Size.Empty, TextFormatFlags.NoPadding);
        var textRect = new Rectangle(left, bounds.Top, size.Width, bounds.Height);

        TextRenderer.DrawText(g, text, AppTheme.SmallFont, textRect, AppTheme.TextMuted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPadding);

        var y = bounds.Top + bounds.Height / 2;
        var lineLeft = textRect.Right + UiLayout.AtTextSize(8);
        var lineRight = bounds.Right - EdgeInset();

        if (lineRight > lineLeft)
        {
            using var pen = new Pen(AppTheme.Border);
            g.DrawLine(pen, lineLeft, y, lineRight, y);
        }
    }

    private static void DrawArrow(Graphics g, Rectangle cell, bool up, bool enabled, bool hot)
    {
        var half = Math.Max(3, UiLayout.AtTextSize(5));
        var height = Math.Max(3, UiLayout.AtTextSize(5));
        var cx = cell.Left + cell.Width / 2f;
        var cy = cell.Top + cell.Height / 2f;

        var points = up
            ? new[] { new PointF(cx - half, cy + height / 2f), new PointF(cx + half, cy + height / 2f), new PointF(cx, cy - height / 2f) }
            : new[] { new PointF(cx - half, cy - height / 2f), new PointF(cx + half, cy - height / 2f), new PointF(cx, cy + height / 2f) };

        var color = !enabled ? AppTheme.Border : hot ? AppTheme.Primary : AppTheme.TextMuted;

        var saved = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;

        using (var fill = new SolidBrush(color))
        {
            g.FillPolygon(fill, points);
        }

        g.SmoothingMode = saved;
    }

    private Part PartAt(int index, Point at)
    {
        if (index < 0 || index >= _rows.Count || _rows[index].IsDivider)
        {
            return Part.None;
        }

        var bounds = GetItemRectangle(index);

        if (at.X <= TickBoxIn(bounds).Right + TickBoxInset())
        {
            return Part.Tick;
        }

        if (_rows[index].Shown)
        {
            if (UpArrowIn(bounds).Contains(at))
            {
                return Part.Up;
            }

            if (DownArrowIn(bounds).Contains(at))
            {
                return Part.Down;
            }
        }

        return Part.Label;
    }

    private static int ArrowWidth() => UiLayout.AtTextSize(24);

    private static int EdgeInset() => UiLayout.AtTextSize(4);

    private static Rectangle DownArrowIn(Rectangle row) =>
        new(row.Right - EdgeInset() - ArrowWidth(), row.Top, ArrowWidth(), row.Height);

    private static Rectangle UpArrowIn(Rectangle row) =>
        new(row.Right - EdgeInset() - 2 * ArrowWidth(), row.Top, ArrowWidth(), row.Height);

    /// <summary>How far in from the row's left edge the tick box sits, and the room after it a click still counts for: 6px at the normal text size.</summary>
    private static int TickBoxInset() => UiLayout.AtTextSize(6);

    /// <summary>
    /// Where a row's tick box is drawn: 13 to 18px square at the normal text
    /// size - as much as the row leaves it - at the size in use, centred up
    /// and down in the row. One place for the drawing and the click that
    /// toggles it, so the part that answers a click is the box that is drawn.
    /// </summary>
    private static Rectangle TickBoxIn(Rectangle row)
    {
        var size = Math.Max(
            UiLayout.AtTextSize(13),
            Math.Min(UiLayout.AtTextSize(18), row.Height - UiLayout.AtTextSize(8)));

        return new Rectangle(row.Left + TickBoxInset(), row.Top + (row.Height - size) / 2, size, size);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tip?.Dispose();
            _tip = null;
        }

        base.Dispose(disposing);
    }
}
