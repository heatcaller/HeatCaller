namespace HeatCaller;

/// <summary>Lets the user check/uncheck which nav items are visible and reorder them with Move Up/Down. "Connection" is never listed - it can't be hidden, so there's always somewhere to land - and it keeps its place in the sidebar.</summary>
public sealed class TabVisibilityForm : Form
{
    private readonly ListBox _list = new();
    private readonly List<NavEntry> _entries;

    public HashSet<string> HiddenTabs { get; private set; }

    /// <summary>
    /// Whether the sidebar should lead with Race Day and Race Control while a
    /// race day is open. Read back by the caller on OK.
    /// </summary>
    public bool RaceNightFocus { get; private set; }

    /// <summary>The full set of toggleable names in their final chosen order (visible and hidden alike) - used to persist NavOrder.</summary>
    public List<string> OrderedNames => _entries.Select(e => e.Name).ToList();

    public TabVisibilityForm(
        IReadOnlyList<string> toggleableTabNames,
        IEnumerable<string> currentlyHidden,
        IReadOnlyList<string>? defaultOrder = null,
        bool raceNightFocus = true)
    {
        HiddenTabs = new HashSet<string>(currentlyHidden);
        RaceNightFocus = raceNightFocus;
        _entries = toggleableTabNames
            .Select(n => new NavEntry(n) { Visible = !HiddenTabs.Contains(n) })
            .ToList();

        Text = "Manage Navigation";
        StartPosition = FormStartPosition.CenterParent;
        // Taller than it was, for the race-night row added below. This dialog
        // is laid out at fixed coordinates, so the row went in with everything
        // under it moved down rather than squeezed into the gap that happened
        // to be free - and with room to spare, because the checkbox wraps to a
        // second line at the larger text sizes and a themed button is ten
        // pixels taller than the WinForms default this arithmetic was
        // originally done with.
        Size = new Size(400, 600);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        Font = AppTheme.BodyFont;

        var lblIntro = new Label
        {
            Text = "Uncheck pages you don't use; Move Up/Down reorders. Connection can't be hidden, so it isn't listed and keeps its place.",
            Location = new Point(12, 12),
            AutoSize = true,
            MaximumSize = new Size(370, 0),
            ForeColor = AppTheme.TextMuted,
        };

        _list.Location = new Point(12, 60);
        _list.Size = new Size(270, 350);
        _list.BackColor = AppTheme.Surface;
        _list.Font = AppTheme.BodyFont;
        _list.DrawMode = DrawMode.OwnerDrawFixed;
        _list.ItemHeight = 26;
        _list.DrawItem += List_DrawItem;
        RefreshListItems();

        var btnMoveUp = new Button { Text = "Move Up", Location = new Point(292, 60), Width = 90 };
        btnMoveUp.Click += (_, _) => MoveSelected(-1);

        var btnMoveDown = new Button { Text = "Move Down", Location = new Point(292, 96), Width = 90 };
        btnMoveDown.Click += (_, _) => MoveSelected(1);

        // One line, and the whole explanation in it. A separate hint label
        // under a checkbox is a third thing to keep from colliding with the
        // buttons below at every text size, in a dialog that has no measured
        // layout to fall back on.
        var chkRaceNight = new CheckBox
        {
            Text = "While a race day is open, bring Race Day, Race Control and Championship to the top",
            Location = new Point(12, 418),
            AutoSize = true,
            MaximumSize = new Size(370, 0),
            Checked = RaceNightFocus,
            Font = AppTheme.BodyFont,
        };

        var btnDefaults = new Button { Text = "Restore Defaults", Location = new Point(12, 470), Width = 130 };
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

        var btnShowAll = new Button { Text = "Show All", Location = new Point(150, 470), Width = 100 };
        btnShowAll.Click += (_, _) =>
        {
            foreach (var entry in _entries)
            {
                entry.Visible = true;
            }
            _list.Invalidate();
        };

        var btnOk = new Button { Text = "OK", Location = new Point(208, 508), Width = 80, DialogResult = DialogResult.OK };
        btnOk.Click += (_, _) =>
        {
            HiddenTabs = new HashSet<string>(_entries.Where(e => !e.Visible).Select(e => e.Name));
            RaceNightFocus = chkRaceNight.Checked;
        };

        var btnCancel = new Button { Text = "Cancel", Location = new Point(292, 508), Width = 90, DialogResult = DialogResult.Cancel };

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
            var itemRect = _list.GetItemRectangle(index);
            if (e.X - itemRect.Left <= 24)
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

        Controls.Add(lblIntro);
        Controls.Add(_list);
        Controls.Add(btnMoveUp);
        Controls.Add(btnMoveDown);
        Controls.Add(chkRaceNight);
        Controls.Add(btnDefaults);
        Controls.Add(btnShowAll);
        Controls.Add(btnOk);
        Controls.Add(btnCancel);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        BackColor = AppTheme.Background;
        AppTheme.ApplyRecursively(this);
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

        var checkboxRect = new Rectangle(e.Bounds.Left + 4, e.Bounds.Top + 5, 16, 16);

        // Disposed rather than leaked: this runs on every paint of every
        // row, and an undisposed Pen holds a GDI handle. Windows caps
        // handles per process, so a leak here degrades and eventually
        // crashes the app on a long-running machine - the kind of failure
        // that only shows up hours into a race day.
        using (var boxPen = new Pen(AppTheme.TextMuted))
        {
            e.Graphics.DrawRectangle(boxPen, checkboxRect);
        }
        if (entry.Visible)
        {
            using var checkBrush = new SolidBrush(AppTheme.Primary);
            e.Graphics.FillRectangle(checkBrush, checkboxRect.X + 3, checkboxRect.Y + 3, 10, 10);
        }

        var textColor = entry.Visible ? AppTheme.TextDark : AppTheme.TextMuted;
        var textRect = new Rectangle(e.Bounds.Left + 28, e.Bounds.Top, e.Bounds.Width - 32, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, entry.Name, AppTheme.BodyFont, textRect, textColor,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
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
