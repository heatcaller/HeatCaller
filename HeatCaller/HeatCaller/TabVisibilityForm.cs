namespace HeatCaller;

/// <summary>
/// Lets the user check/uncheck which nav items are visible and reorder them with Move Up/Down. "Connection" is never listed - it can't be hidden, so there's always somewhere to land - and it keeps its place in the sidebar.
///
/// On the layout pieces (DialogFit): the explanation across the top, the
/// pages taking the height with Move Up and Move Down in a column beside
/// them (Beside), the race-night tick under them, and at the foot Restore
/// Defaults and Show All on the left and OK and Cancel against the right -
/// under the other two when the window is too narrow for all four on one
/// line, as they always were at the normal text size (SplitBar). It used to
/// be placed in pixels in a 400x600 window whatever the text size: the list
/// a fixed 270x350, the buttons at fixed places and widths beside and under
/// it, and the tick at a fixed height under the list, with room left by
/// hand for it to wrap onto a second line.
/// </summary>
public sealed class TabVisibilityForm : Form
{
    /// <summary>The pages show at least this many before the window scrolls instead.</summary>
    private const int LeastRows = 4;

    private readonly ListBox _list = new();
    private readonly List<NavEntry> _entries;

    public HashSet<string> HiddenTabs { get; private set; }

    /// <summary>
    /// Whether the sidebar should lead with Race Day and Race Control while a
    /// race day is open. Read back by the caller on OK.
    /// </summary>
    public bool RaceNightFocus { get; private set; }

    /// <summary>
    /// Whether the pages not yet tested at a real event are kept out of the
    /// sidebar (AppSettings.HideUntestedPages). Read back by the caller on OK.
    /// </summary>
    public bool HideUntested { get; private set; }

    /// <summary>The full set of toggleable names in their final chosen order (visible and hidden alike) - used to persist NavOrder.</summary>
    public List<string> OrderedNames => _entries.Select(e => e.Name).ToList();

    public TabVisibilityForm(
        IReadOnlyList<string> toggleableTabNames,
        IEnumerable<string> currentlyHidden,
        IReadOnlyList<string>? defaultOrder = null,
        bool raceNightFocus = true,
        bool hideUntested = false)
    {
        HiddenTabs = new HashSet<string>(currentlyHidden);
        RaceNightFocus = raceNightFocus;
        HideUntested = hideUntested;
        _entries = toggleableTabNames
            .Select(n => new NavEntry(n) { Visible = !HiddenTabs.Contains(n) })
            .ToList();

        Text = "Manage Navigation";
        Font = AppTheme.BodyFont;

        var lblIntro = new Label
        {
            Text = "Uncheck pages you don't use; Move Up/Down reorders. Connection can't be hidden, so it isn't listed and keeps its place.",
            AutoSize = true,
            ForeColor = AppTheme.TextMuted,
        };

        _list.BackColor = AppTheme.Surface;
        _list.Font = AppTheme.BodyFont;
        _list.DrawMode = DrawMode.OwnerDrawFixed;

        // A row the 26px it was at the normal text size, and taller with the
        // text: a fixed 26 cut the names off at 150% and 200%, where a line
        // of the list's font is taller than that. The tick box grows with the
        // text as well, centred in the row (TickBoxIn).
        _list.ItemHeight = UiLayout.AtTextSize(26);

        // Given the height the window has, not snapped to whole rows - see FillPage.AddFill.
        _list.IntegralHeight = false;
        _list.DrawItem += List_DrawItem;
        RefreshListItems();

        var btnMoveUp = new Button { Text = "Move Up" };
        btnMoveUp.Click += (_, _) => MoveSelected(-1);

        var btnMoveDown = new Button { Text = "Move Down" };
        btnMoveDown.Click += (_, _) => MoveSelected(1);

        // The whole explanation in the tick's own text, wrapping to the
        // window's width, rather than a hint label under it - one thing
        // under the list rather than two.
        var chkRaceNight = new ThemedCheckBox
        {
            Text = "While a race day is open, bring Race Day, Race Control and Championship to the top",
            AutoSize = true,
            Checked = RaceNightFocus,
            Font = AppTheme.BodyFont,
        };

        // THE BETA TIER (22 Oct 2026). Its own tick rather than the pages'
        // ticks above: those are the club's choice of what it uses, this is
        // the app's own word on what has been tried on a night, and the list
        // changes as rehearsals happen (NavTiers.UntestedPages). Restore
        // Defaults leaves it alone - it is about order and the club's own
        // ticks, and a beta club pressing it should not get five untested
        // pages back by surprise.
        var chkUntested = new ThemedCheckBox
        {
            Text = "Keep pages not yet tested at a real event out of the sidebar ("
                + string.Join(", ", NavTiers.Untested.OrderBy(n => n, StringComparer.CurrentCulture))
                + ") - a search still finds them",
            AutoSize = true,
            Checked = HideUntested,
            Font = AppTheme.BodyFont,
        };

        var btnDefaults = new Button { Text = "Restore Defaults" };
        btnDefaults.Click += (_, _) =>
        {
            // Back to the app's own order with everything visible, and the
            // race-night grouping back on - the state a fresh install starts
            // in.
            chkRaceNight.Checked = true;
            var ordered = defaultOrder ?? toggleableTabNames;
            _entries.Clear();
            foreach (var name in ordered.Where(toggleableTabNames.Contains))
            {
                _entries.Add(new NavEntry(name) { Visible = true });
            }
            foreach (var name in toggleableTabNames.Where(n => !_entries.Any(e => e.Name == n)))
            {
                _entries.Add(new NavEntry(name) { Visible = true });
            }
            RefreshListItems();
            _list.Invalidate();
        };

        var btnShowAll = new Button { Text = "Show All" };
        btnShowAll.Click += (_, _) =>
        {
            foreach (var entry in _entries)
            {
                entry.Visible = true;
            }
            _list.Invalidate();
        };

        var btnOk = new Button { Text = "OK", DialogResult = DialogResult.OK };
        btnOk.Click += (_, _) =>
        {
            HiddenTabs = new HashSet<string>(_entries.Where(e => !e.Visible).Select(e => e.Name));
            RaceNightFocus = chkRaceNight.Checked;
            HideUntested = chkUntested.Checked;
        };

        var btnCancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel };

        // Toggle visibility by clicking directly on an item (space bar or
        // click near the checkbox area) - simplest reliable interaction
        // for an owner-drawn list without needing real CheckedListBox
        // support alongside custom Move Up/Down buttons.
        _list.MouseDown += (_, e) =>
        {
            var index = _list.IndexFromPoint(e.Location);
            if (index < 0 || index >= _entries.Count)
            {
                return;
            }
            // The row's left edge to just past its tick box, as drawn at the
            // text size in use - the box and its inset either side, which is
            // the 24px it was at the normal text size. A fixed 24 missed most
            // of the box once it grew at 150% and 200%.
            var itemRect = _list.GetItemRectangle(index);
            var box = TickBoxIn(itemRect);
            if (e.X <= box.Right + TickBoxInset())
            {
                _entries[index].Visible = !_entries[index].Visible;
                _list.Invalidate();
            }
        };

        // The space bar the note above promises. It was never wired, so a
        // page could only be hidden or shown with the mouse.
        _list.KeyDown += (_, e) =>
        {
            var index = _list.SelectedIndex;

            if (e.KeyCode != Keys.Space || index < 0 || index >= _entries.Count)
            {
                return;
            }

            _entries[index].Visible = !_entries[index].Visible;
            _list.Invalidate();
            e.Handled = true;
            e.SuppressKeyPress = true;
        };

        // --- Layout ---
        //
        // Move Up and Move Down in a column down the right of the list, as
        // wide as the wider caption needs; the list takes the width the
        // column leaves and the height down to the tick.
        var band = new Beside();
        band.AddFill(_list, _ => LeastRows * _list.ItemHeight + 4);
        band.AddSide(btnMoveUp, gapBelow: UiLayout.ButtonGap);
        band.AddSide(btnMoveDown, gapBelow: 0);

        // Restore Defaults and Show All on the left, OK and Cancel against
        // the right-hand edge, Cancel furthest right - on one line when the
        // window has room for all four, and OK and Cancel under the other
        // two when it has not (SplitBar), as they were placed. The bar is
        // handed Cancel first, as its first button goes furthest right; Tab
        // still reaches OK before Cancel, as it did, from the order set here.
        var footer = new SplitBar(
            new ButtonBar().With(btnDefaults, btnShowAll),
            new ButtonBar(alignRight: true).With(btnCancel, btnOk));

        btnOk.TabIndex = 0;
        btnCancel.TabIndex = 1;

        var page = new FillPage();
        page.Add(lblIntro, gapBelow: UiLayout.RowGap);
        page.AddFill(band, band.LeastHeight, gapBelow: UiLayout.RowGap);
        page.Add(chkRaceNight, gapBelow: UiLayout.RowGap);
        page.Add(chkUntested);
        page.Add(footer, gapBelow: 0);

        Controls.Add(page);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        BackColor = AppTheme.Background;
        AppTheme.ApplyRecursively(this);

        // Wider than the 400 it was made at: Move Up and Move Down are as
        // wide as a standard button now, 148px rather than 90, and this
        // keeps the list the 270px it had. The height it had inside its
        // frame. It can be made bigger, for every page at once.
        DialogFit.Keep(this, page, width: 460, height: 560);
    }

    private void RefreshListItems()
    {
        var selected = _list.SelectedIndex;
        _list.Items.Clear();
        foreach (var entry in _entries)
        {
            _list.Items.Add(entry.Name);
        }
        if (selected >= 0 && selected < _list.Items.Count)
        {
            _list.SelectedIndex = selected;
        }
    }

    private void MoveSelected(int direction)
    {
        var index = _list.SelectedIndex;
        var newIndex = index + direction;
        if (index < 0 || newIndex < 0 || newIndex >= _entries.Count)
        {
            return;
        }

        (_entries[index], _entries[newIndex]) = (_entries[newIndex], _entries[index]);
        RefreshListItems();
        _list.SelectedIndex = newIndex;
    }

    private void List_DrawItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0)
        {
            return;
        }

        e.DrawBackground();
        var entry = _entries[e.Index];
        var selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;

        using (var backBrush = new SolidBrush(selected ? AppTheme.PrimaryTint : AppTheme.Surface))
        {
            e.Graphics.FillRectangle(backBrush, e.Bounds);
        }

        // The app's tick box (TickBox), as every check box in the app is
        // drawn, in the place TickBoxIn gives it - where a click ticks it.
        TickBox.Draw(e.Graphics, TickBoxIn(e.Bounds), entry.Visible ? CheckState.Checked : CheckState.Unchecked, Enabled);

        // The name clear of the box, and short of the row's right edge, at
        // the text size in use - 28px in and 4px short at the normal size.
        var textLeft = UiLayout.AtTextSize(28);
        var textColor = entry.Visible ? AppTheme.TextDark : AppTheme.TextMuted;
        var textRect = new Rectangle(e.Bounds.Left + textLeft, e.Bounds.Top, e.Bounds.Width - UiLayout.AtTextSize(32), e.Bounds.Height);
        // A page not yet tested at a real event says so after its name, in
        // the muted colour, so the tick under the list has something to point
        // at. Cut short with an ellipsis rather than wrapped when the window
        // is narrow - the name comes first and is never the part cut.
        var name = NavTiers.IsUntested(entry.Name) ? entry.Name + "  (not yet tested at an event)" : entry.Name;
        TextRenderer.DrawText(e.Graphics, name, AppTheme.BodyFont, textRect, textColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    /// <summary>
    /// How far in from the row's left edge the tick box sits, and the room
    /// kept after it that a click still counts for: 4px at the normal text
    /// size, at the size in use.
    /// </summary>
    private static int TickBoxInset() => UiLayout.AtTextSize(4);

    /// <summary>
    /// Where a row's tick box is drawn: 16px square at the normal text size,
    /// at the size in use, centred up and down in the row. One place for the
    /// drawing and the click that toggles it, so the part that answers a
    /// click is the box that is drawn at every text size. Worked out when
    /// asked, never kept - the text size can change between two paints.
    /// </summary>
    private static Rectangle TickBoxIn(Rectangle row)
    {
        var size = UiLayout.AtTextSize(16);

        return new Rectangle(row.Left + TickBoxInset(), row.Top + (row.Height - size) / 2, size, size);
    }

    private sealed class NavEntry
    {
        public string Name { get; }
        public bool Visible { get; set; } = true;

        public NavEntry(string name)
        {
            Name = name;
        }
    }
}
