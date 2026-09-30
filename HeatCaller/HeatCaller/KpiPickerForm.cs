namespace HeatCaller;

/// <summary>
/// Lets the user pick which KPI columns a given leaderboard shows, and in
/// what order. Used by Race Control, Practice, the Big Screen and the web
/// pages - each keeps its own selection, since what's useful on a laptop
/// mid-race is not what's readable on a screen across the room.
///
/// On the layout pieces (DialogFit): the explanation across the top, the
/// columns taking the height with their buttons in a column beside them
/// (Beside), the selected column's description under them and OK and Cancel
/// at the foot. It used to be placed in pixels, though every number in it
/// was measured from the captions and the font: a 330px list fourteen rows
/// tall, the buttons stepped down beside it, three lines of the small font
/// kept for the description, and a window sized round all of it that could
/// not be resized.
/// </summary>
public sealed class KpiPickerForm : Form
{
    /// <summary>The columns show at least this many before the window scrolls instead - or as many as the buttons beside them are tall, when that is more.</summary>
    private const int LeastRows = 4;

    private sealed class Entry
    {
        public required KpiDefinition Definition { get; init; }
        public bool Selected { get; set; }
    }

    private readonly List<Entry> _entries = new();
    private readonly ListBox _list = new();
    private readonly SteadyLabel _lblDescription = new();

    /// <summary>Chosen KPI keys, in display order.</summary>
    public List<string> SelectedKeys { get; private set; } = new();

    public KpiPickerForm(string surfaceName, IEnumerable<string> currentKeys, IEnumerable<string> defaults)
    {
        var current = currentKeys.ToList();

        // Selected ones first in their saved order, then everything else -
        // so the list doubles as the column order.
        foreach (var key in current)
        {
            var def = KpiCatalog.Find(key);
            if (def is not null)
            {
                _entries.Add(new Entry { Definition = def, Selected = true });
            }
        }
        foreach (var def in KpiCatalog.All.Where(d => !_entries.Any(e => e.Definition.Key == d.Key)))
        {
            _entries.Add(new Entry { Definition = def, Selected = false });
        }

        Text = $"Choose columns - {surfaceName}";
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        var btnUp = new Button { Text = "Move Up" };
        btnUp.Click += (_, _) => MoveSelected(-1);

        var btnDown = new Button { Text = "Move Down" };
        btnDown.Click += (_, _) => MoveSelected(1);

        var btnDefaults = new Button { Text = "Restore Defaults" };
        btnDefaults.Click += (_, _) =>
        {
            var defaultKeys = defaults.ToList();
            _entries.Clear();
            foreach (var key in defaultKeys)
            {
                var def = KpiCatalog.Find(key);
                if (def is not null)
                {
                    _entries.Add(new Entry { Definition = def, Selected = true });
                }
            }
            foreach (var def in KpiCatalog.All.Where(d => !defaultKeys.Contains(d.Key)))
            {
                _entries.Add(new Entry { Definition = def, Selected = false });
            }
            RefreshList();
        };

        var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK };
        btnOk.Click += (_, _) =>
            SelectedKeys = _entries.Where(e => e.Selected).Select(e => e.Definition.Key).ToList();

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        var lblIntro = new Label
        {
            Text = "Click the left edge of a row to show or hide that column. Use Move Up/Down to set the order they appear in.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.BodyFont,
        };

        // The row follows the font, so a taller text setting gets taller
        // rows rather than clipped ones - and the tick box below is drawn
        // from the same number.
        _list.ItemHeight = Math.Max(26, AppTheme.BodyFont.Height + 10);

        // Given the height the window has, not snapped to whole rows - see FillPage.AddFill.
        _list.IntegralHeight = false;
        _list.BackColor = AppTheme.Surface;
        _list.DrawMode = DrawMode.OwnerDrawFixed;
        _list.DrawItem += List_DrawItem;
        _list.SelectedIndexChanged += (_, _) => ShowDescription();
        _list.MouseDown += List_MouseDown;
        RefreshList();

        // As tall as the longest description in the catalogue at the width
        // it has, whichever row is selected - so picking a row never moves
        // OK and Cancel under it. It used to keep three lines of the small
        // font, whatever the width.
        _lblDescription.ForeColor = AppTheme.TextMuted;
        _lblDescription.Font = AppTheme.SmallFont;
        _lblDescription.Wordings = KpiCatalog.All.Select(definition => definition.Description).ToList();

        // --- Layout ---
        //
        // The buttons in one column down the right of the list, all as wide
        // as the widest caption needs, so they read as a column rather than
        // as three chips: a button's gap between the two that reorder and a
        // section's before Restore Defaults. The list takes the width the
        // column leaves and the height down to the description.
        var columns = new Beside();
        columns.AddFill(_list, _ => LeastRows * _list.ItemHeight + 4);
        columns.AddSide(btnUp, gapBelow: UiLayout.ButtonGap);
        columns.AddSide(btnDown, gapBelow: UiLayout.SectionGap);
        columns.AddSide(btnDefaults, gapBelow: 0);

        var page = new FillPage();
        page.Add(lblIntro, gapBelow: UiLayout.RowGap);
        page.AddFill(columns, columns.LeastHeight, gapBelow: UiLayout.RowGap);
        page.Add(_lblDescription, gapBelow: UiLayout.RowGap);

        // Against the right-hand edge, Cancel furthest right, as they were.
        // The bar is handed Cancel first, as its first button goes furthest
        // right; Tab still reaches OK before Cancel, as it did, from the
        // order set here.
        page.Add(new ButtonBar(alignRight: true).With(btnCancel, btnOk), gapBelow: 0);
        btnOk.TabIndex = 0;
        btnCancel.TabIndex = 1;

        Controls.Add(page);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);

        // Re-styled AFTER the recursive pass, which infers a button's kind
        // from its caption and reads "Restore Defaults" as the important
        // one. It is not: OK is. Two pink buttons in a dialog is two
        // buttons claiming to be the answer.
        AppTheme.StyleButton(btnDefaults, AppTheme.ButtonKind.Secondary);

        ShowDescription();

        // The width and height it measured itself to at the normal text
        // size - 541 by about 596, the list fourteen rows tall. It can be
        // made bigger, for every column at once, or shorter down to a few
        // rows.
        DialogFit.Keep(this, page, width: 540, height: 600);
    }

    private void RefreshList()
    {
        var selectedIndex = _list.SelectedIndex;
        _list.Items.Clear();
        foreach (var entry in _entries)
        {
            _list.Items.Add(entry.Definition.Label);
        }
        if (selectedIndex >= 0 && selectedIndex < _list.Items.Count)
        {
            _list.SelectedIndex = selectedIndex;
        }
    }

    private void ShowDescription()
    {
        _lblDescription.Text = _list.SelectedIndex >= 0 && _list.SelectedIndex < _entries.Count
            ? _entries[_list.SelectedIndex].Definition.Description
            : string.Empty;
    }

    private void List_MouseDown(object? sender, MouseEventArgs e)
    {
        var index = _list.IndexFromPoint(e.Location);
        if (index < 0 || index >= _entries.Count)
        {
            return;
        }

        var rect = _list.GetItemRectangle(index);

        // Matches where the box is actually drawn - a fixed 24 was a hit
        // target for a box that has since moved. The row's left edge to just
        // past the box, as TickBoxIn draws it at the text size in use: the
        // 30px it was at the normal size, and the box's own growth past that
        // at 150% and 200%, which a fixed 30 missed.
        var box = TickBoxIn(rect);
        if (e.X <= box.Right + TickBoxInset())
        {
            _entries[index].Selected = !_entries[index].Selected;
            _list.Invalidate();
        }
    }

    /// <summary>Named MoveSelected rather than Move because Control already has a Move event, and shadowing it produces a compiler warning.</summary>
    private void MoveSelected(int direction)
    {
        var index = _list.SelectedIndex;
        var target = index + direction;
        if (index < 0 || target < 0 || target >= _entries.Count)
        {
            return;
        }

        (_entries[index], _entries[target]) = (_entries[target], _entries[index]);
        RefreshList();
        _list.SelectedIndex = target;
    }

    private void List_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _entries.Count)
        {
            return;
        }

        var entry = _entries[e.Index];
        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

        using (var back = new SolidBrush(selected ? AppTheme.PrimaryTint : AppTheme.Surface))
        {
            e.Graphics.FillRectangle(back, e.Bounds);
        }

        // Centred in whatever the row height turned out to be, rather than
        // five pixels down from the top of a 26px row that no longer
        // exists at every text size.
        var box = TickBoxIn(e.Bounds);

        // The app's tick box (TickBox), as every check box in the app is
        // drawn, where TickBoxIn puts it - where a click ticks it.
        TickBox.Draw(e.Graphics, box, entry.Selected ? CheckState.Checked : CheckState.Unchecked, Enabled);

        // The label clear of the box, and short of the row's right edge, at
        // the text size in use - 10px after the box and 4px short at the
        // normal size.
        var textGap = UiLayout.AtTextSize(10);
        var textRect = new Rectangle(box.Right + textGap, e.Bounds.Top, e.Bounds.Width - (box.Right + UiLayout.AtTextSize(14)), e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, entry.Definition.Label, AppTheme.BodyFont, textRect,
            entry.Selected ? AppTheme.TextDark : AppTheme.TextMuted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }

    /// <summary>
    /// How far in from the row's left edge the tick box sits, and the room
    /// kept after it that a click still counts for: 6px at the normal text
    /// size, at the size in use.
    /// </summary>
    private static int TickBoxInset() => UiLayout.AtTextSize(6);

    /// <summary>
    /// Where a row's tick box is drawn: 13 to 18px square at the normal text
    /// size - as much as the row leaves it - at the size in use, centred up
    /// and down in the row. A fixed 13 to 18 stayed the size it was at the
    /// normal text size beside a label twice as tall at 200%. One place for
    /// the drawing and the click that toggles it, so the part that answers a
    /// click is the box that is drawn at every text size. Worked out when
    /// asked, never kept - the text size can change between two paints.
    /// </summary>
    private static Rectangle TickBoxIn(Rectangle row)
    {
        var size = Math.Max(
            UiLayout.AtTextSize(13),
            Math.Min(UiLayout.AtTextSize(18), row.Height - UiLayout.AtTextSize(8)));

        return new Rectangle(row.Left + TickBoxInset(), row.Top + (row.Height - size) / 2, size, size);
    }
}
