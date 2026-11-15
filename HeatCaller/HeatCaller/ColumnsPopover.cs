namespace HeatCaller;

/// <summary>
/// A leaderboard's columns as a drop-down, the way a column heading's
/// right-click works in Windows' own file lists (15 Nov 2026): every column
/// with a tick, the board's ones first in board order, arrows to move them,
/// and the board changing as you go. It is a themed menu (AppTheme's
/// StyleContextMenu) holding a <see cref="ColumnChecklist"/>, with Restore
/// Defaults and the full picker at its foot.
///
/// Clicking inside the list keeps it open; Esc or a click away closes it.
/// </summary>
internal static class ColumnsPopover
{
    /// <summary>The list shows this many rows before it scrolls instead.</summary>
    private const int MostRows = 14;

    /// <param name="owner">What the menu opens over; <paramref name="at"/> is in its client coordinates.</param>
    /// <param name="focusKey">The column to start on - the heading that was right-clicked.</param>
    /// <param name="defaults">What Restore Defaults puts back.</param>
    /// <param name="closed">Once it has closed, whatever was changed.</param>
    /// <param name="more">The full picker, after the menu has gone.</param>
    public static void Show(Control owner, Point at, ColumnChoices choices, string? focusKey,
        IReadOnlyList<string> defaults, Action closed, Action more)
    {
        var list = new ColumnChecklist(choices)
        {
            BorderStyle = BorderStyle.None,
            ShowDescriptionTips = true,
        };

        list.Size = new Size(list.WidthToFit(), Math.Min(MostRows, Math.Max(1, list.RowCount)) * list.ItemHeight);
        list.SelectColumn(focusKey);

        // In a panel of its own rather than hosted bare: the app's thin
        // themed scrollbar parks itself beside its list, in the list's
        // parent, and a menu takes no controls of its own beyond its items.
        // Without it the list scrolls with Windows' own pale bar.
        var panel = new Panel { Size = list.Size, BackColor = AppTheme.Surface, Margin = Padding.Empty };
        list.Dock = DockStyle.Fill;
        panel.Controls.Add(list);
        ThinScrollBar.Attach(list);

        var host = new ToolStripControlHost(panel)
        {
            AutoSize = false,
            Size = panel.Size,
            Margin = new Padding(UiLayout.AtTextSize(4), 0, UiLayout.AtTextSize(4), 0),
            Padding = Padding.Empty,
        };

        var restore = new ToolStripMenuItem("Restore default columns");
        var openPicker = new ToolStripMenuItem("More column options...");

        var menu = new ContextMenuStrip();
        menu.Items.Add(host);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(restore);
        menu.Items.Add(openPicker);

        AppTheme.StyleContextMenu(menu);

        // No tick gutter: nothing in this menu is a checkable item - the
        // ticks are the list's own - and the gutter would push the list in
        // from the menu's left edge.
        menu.ShowImageMargin = false;
        host.BackColor = AppTheme.Surface;

        // Restore Defaults is a change like a tick, so it leaves the menu
        // open to show what it did; only the picker closes it.
        var keepOpen = false;
        var morePicked = false;

        menu.ItemClicked += (_, e) =>
        {
            keepOpen = e.ClickedItem == restore;
            morePicked = e.ClickedItem == openPicker;
        };

        restore.Click += (_, _) => choices.Set(defaults);

        menu.Closing += (_, e) =>
        {
            if (e.CloseReason == ToolStripDropDownCloseReason.ItemClicked && keepOpen)
            {
                keepOpen = false;
                e.Cancel = true;
            }
        };

        menu.Opened += (_, _) => list.Focus();

        menu.Closed += (_, _) =>
        {
            closed();

            if (morePicked && !owner.IsDisposed)
            {
                owner.BeginInvoke(more);
            }

            if (!owner.IsDisposed)
            {
                owner.BeginInvoke(new Action(menu.Dispose));
            }
            else
            {
                menu.Dispose();
            }
        };

        menu.Show(owner, at);
    }
}
