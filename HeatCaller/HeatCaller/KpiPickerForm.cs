namespace HeatCaller;

/// <summary>
/// Lets the user pick which KPI columns a given leaderboard shows, and in
/// what order. Used by Race Control, Practice, the Big Screen and the web
/// pages - each keeps its own selection, since what's useful on a laptop
/// mid-race is not what's readable on a screen across the room.
/// </summary>
public sealed class KpiPickerForm : Form
{
    private sealed class Entry
    {
        public required KpiDefinition Definition { get; init; }
        public bool Selected { get; set; }
    }

    private readonly List<Entry> _entries = new();
    private readonly ListBox _list = new();
    private readonly Label _lblDescription = new();

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
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        Font = AppTheme.BodyFont;
        BackColor = AppTheme.Background;

        // --- Measured, not typed in ---
        //
        // Every number in this dialog used to be a literal chosen against a
        // smaller font: a 106px side column holding a button captioned
        // "Restore Defaults", a 26px row for a font that is now taller than
        // that, and a 470px dialog that the side column then hung off the
        // edge of. All of it is now measured from the captions and the font
        // actually in use, and the dialog is sized to what it contains.
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

        foreach (var button in new[] { btnUp, btnDown, btnDefaults, btnOk, btnCancel })
        {
            button.Size = new Size(UiLayout.WidthFor(button), UiLayout.ButtonHeight);
        }

        // One width for the side column, taken from its widest caption, so
        // the three buttons read as a column rather than as three chips.
        var sideWidth = Math.Max(btnUp.Width, Math.Max(btnDown.Width, btnDefaults.Width));

        btnUp.Width = sideWidth;
        btnDown.Width = sideWidth;
        btnDefaults.Width = sideWidth;

        const int left = 16;
        const int listWidth = 330;

        var sideLeft = left + listWidth + UiLayout.SectionGap;
        var contentRight = sideLeft + sideWidth;

        var lblIntro = new Label
        {
            Text = "Click the left edge of a row to show or hide that column. Use Move Up/Down to set the order they appear in.",
            Location = new Point(left, 12),
            AutoSize = true,
            MaximumSize = new Size(contentRight - left, 0),
            ForeColor = AppTheme.TextMuted,
            Font = AppTheme.BodyFont,
        };

        var listTop = UiLayout.Below(lblIntro, UiLayout.RowGap);

        _list.Location = new Point(left, listTop);

        // The row follows the font, so a taller text setting gets taller
        // rows rather than clipped ones - and the tick box below is drawn
        // from the same number.
        _list.ItemHeight = Math.Max(26, AppTheme.BodyFont.Height + 10);
        _list.IntegralHeight = false;
        _list.Size = new Size(listWidth, _list.ItemHeight * 14);
        _list.BackColor = AppTheme.Surface;
        _list.DrawMode = DrawMode.OwnerDrawFixed;
        _list.DrawItem += List_DrawItem;
        _list.SelectedIndexChanged += (_, _) => ShowDescription();
        _list.MouseDown += List_MouseDown;
        RefreshList();

        btnUp.Location = new Point(sideLeft, listTop);
        btnDown.Location = new Point(sideLeft, btnUp.Bottom + UiLayout.ButtonGap);
        btnDefaults.Location = new Point(sideLeft, btnDown.Bottom + UiLayout.SectionGap);

        // Three lines of description, measured - the descriptions in the
        // catalogue are a sentence and a half now, and 44 pixels was one
        // line and a half of the old font.
        _lblDescription.Location = new Point(left, UiLayout.Below(_list, UiLayout.RowGap));
        _lblDescription.Size = new Size(contentRight - left, AppTheme.SmallFont.Height * 3 + 6);
        _lblDescription.ForeColor = AppTheme.TextMuted;
        _lblDescription.Font = AppTheme.SmallFont;

        var exitTop = UiLayout.Below(_lblDescription, UiLayout.RowGap);

        btnCancel.Location = new Point(contentRight - btnCancel.Width, exitTop);
        btnOk.Location = new Point(btnCancel.Left - UiLayout.ButtonGap - btnOk.Width, exitTop);

        ClientSize = new Size(contentRight + left, exitTop + UiLayout.ButtonHeight + left);

        Controls.Add(lblIntro);
        Controls.Add(_list);
        Controls.Add(btnUp);
        Controls.Add(btnDown);
        Controls.Add(btnDefaults);
        Controls.Add(_lblDescription);
        Controls.Add(btnOk);
        Controls.Add(btnCancel);

        AcceptButton = btnOk;
        CancelButton = btnCancel;

        AppTheme.ApplyRecursively(this);

        // Re-styled AFTER the recursive pass, which infers a button's kind
        // from its caption and reads "Restore Defaults" as the important
        // one. It is not: OK is. Two pink buttons in a dialog is two
        // buttons claiming to be the answer.
        AppTheme.StyleButton(btnDefaults, AppTheme.ButtonKind.Secondary);

        ShowDescription();
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
        // target for a box that has since moved.
        if (e.X - rect.Left <= 30)
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
        var boxSize = Math.Max(13, Math.Min(18, e.Bounds.Height - 8));
        var box = new Rectangle(
            e.Bounds.Left + 6,
            e.Bounds.Top + (e.Bounds.Height - boxSize) / 2,
            boxSize,
            boxSize);
        using (var pen = new Pen(AppTheme.TextMuted))
        {
            e.Graphics.DrawRectangle(pen, box);
        }
        if (entry.Selected)
        {
            using var fill = new SolidBrush(AppTheme.Primary);
            e.Graphics.FillRectangle(fill, box.X + 3, box.Y + 3, box.Width - 5, box.Height - 5);
        }

        var textRect = new Rectangle(box.Right + 10, e.Bounds.Top, e.Bounds.Width - (box.Right + 14), e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, entry.Definition.Label, AppTheme.BodyFont, textRect,
            entry.Selected ? AppTheme.TextDark : AppTheme.TextMuted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left);
    }
}
