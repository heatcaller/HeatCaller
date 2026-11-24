using System.Reflection;
using System.Runtime.CompilerServices;

namespace HeatCaller;

/// <summary>
/// A DROP-DOWN'S LIST WITH THE APP'S OWN SCROLL BAR (24 Nov 2026, Asher: "on
/// the cast tab I see native scroll bars when selecting voices. go through
/// each scroll menu app wide and make sure you can't see them").
///
/// WHY: a drop-down's open list is a window of Windows' own, a pop-up on the
/// desktop rather than a control on the page, so the slim bar that covers
/// every other list's scroll bar (ThinScrollBar) has nowhere to sit beside
/// it. Every list longer than it shows drew Windows' bright white bar: the
/// six voice pickers on Voice → Cast (31 voices), the race pickers on Race
/// Analytics' Lap Times and Pace, and Club Website's time zone. Windows' own
/// dark bar was tried: it is still a black strip down the list.
///
/// So a list with more choices than a drop-down shows at once
/// (MaxDropDownItems) opens here instead: the same rows, drawn by the
/// combo's own DrawItem, in a list in a drop-down of the app's own, with the
/// slim bar beside it - as Race Control's columns list is (ColumnsPopover).
/// A short list still opens as it always has; it never scrolls. ComboBoxSkin
/// sends every way of opening a list here (a click, F4, Alt+Down,
/// DroppedDown = true), and answers DroppedDown while it is open.
///
/// Picking works as Windows' list does: the highlight follows the pointer,
/// a click or Enter or Tab picks, and Esc or a click away leaves the choice
/// as it was. A pick sets SelectedIndex and raises SelectionChangeCommitted,
/// and DropDown and DropDownClosed are raised as the list opens and closes.
/// </summary>
internal static class ComboListPopup
{
    /// <summary>The most rows shown at once - more scroll.</summary>
    internal const int MostRows = 20;

    private static readonly ConditionalWeakTable<ComboBox, ContextMenuStrip> Shown = new();

    /// <summary>When each combo's list last closed - a click on the combo that closed it must not open it straight back up.</summary>
    private static readonly ConditionalWeakTable<ComboBox, StrongBox<long>> ClosedAt = new();

    private static readonly MethodInfo? RaiseDropDown = typeof(ComboBox).GetMethod("OnDropDown", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? RaiseDropDownClosed = typeof(ComboBox).GetMethod("OnDropDownClosed", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? RaiseCommitted = typeof(ComboBox).GetMethod("OnSelectionChangeCommitted", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? RaiseDrawItem = typeof(ComboBox).GetMethod("OnDrawItem", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>Whether <paramref name="combo"/>'s list opens here: a pick-from-list drop-down with more choices than it shows at once.</summary>
    internal static bool Wanted(ComboBox combo) =>
        combo is { IsDisposed: false, Enabled: true, DropDownStyle: ComboBoxStyle.DropDownList } && combo.Items.Count > combo.MaxDropDownItems;

    /// <summary>Whether its list is open here now.</summary>
    internal static bool IsOpen(ComboBox combo) => Shown.TryGetValue(combo, out var menu) && !menu.IsDisposed && menu.Visible;

    /// <summary>The list, while it is open - for the tests.</summary>
    internal static ListBox? OpenList(ComboBox combo) =>
        Shown.TryGetValue(combo, out var menu) && !menu.IsDisposed
            ? menu.Items.OfType<ToolStripControlHost>().Select(h => h.Control.Controls.OfType<ListBox>().FirstOrDefault()).FirstOrDefault()
            : null;

    /// <summary>A press on the combo: open, unless it is the press that just closed it.</summary>
    internal static void Toggle(ComboBox combo)
    {
        if (IsOpen(combo))
        {
            Close(combo);
            return;
        }

        if (ClosedAt.TryGetValue(combo, out var at) && Environment.TickCount64 - at.Value < 250)
        {
            return;
        }

        Open(combo);
    }

    internal static void Close(ComboBox combo)
    {
        if (Shown.TryGetValue(combo, out var menu) && !menu.IsDisposed)
        {
            menu.Close(ToolStripDropDownCloseReason.CloseCalled);
        }
    }

    internal static void Open(ComboBox combo)
    {
        if (IsOpen(combo) || !combo.IsHandleCreated || !combo.Visible)
        {
            return;
        }

        RaiseDropDown?.Invoke(combo, new object[] { EventArgs.Empty });

        var rowHeight = Math.Max(1, combo.ItemHeight);
        var screen = Screen.FromControl(combo).WorkingArea;
        var roomRows = Math.Max(3, (Math.Max(screen.Bottom - combo.PointToScreen(new Point(0, combo.Height)).Y, combo.PointToScreen(Point.Empty).Y - screen.Top) - 8) / rowHeight);
        var rows = Math.Max(1, Math.Min(combo.Items.Count, Math.Min(MostRows, roomRows)));
        var margin = UiLayout.AtTextSize(4);
        var width = Math.Max(combo.Width, combo.DropDownWidth) - margin * 2;

        var list = new PopupList
        {
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = rowHeight,
            IntegralHeight = false,
            BorderStyle = BorderStyle.None,
            Font = combo.Font,
            BackColor = AppTheme.Surface,
            ForeColor = AppTheme.TextDark,
            Size = new Size(Math.Max(1, width), rows * rowHeight),
            Dock = DockStyle.Fill,
        };

        list.Items.AddRange(combo.Items.Cast<object>().ToArray());

        // The rows drawn by the combo itself, as its own list draws them.
        list.DrawItem += (_, e) =>
        {
            if (e.Index < 0 || e.Index >= combo.Items.Count)
            {
                e.DrawBackground();
                return;
            }

            var state = e.Index == list.SelectedIndex ? DrawItemState.Selected : DrawItemState.None;
            var args = new DrawItemEventArgs(e.Graphics, e.Font ?? combo.Font, e.Bounds, e.Index, state, combo.ForeColor, combo.BackColor);

            if (RaiseDrawItem is not null)
            {
                RaiseDrawItem.Invoke(combo, new object[] { args });
            }
            else
            {
                e.DrawBackground();
                TextRenderer.DrawText(e.Graphics, combo.GetItemText(combo.Items[e.Index]), combo.Font, e.Bounds, combo.ForeColor, TextFormatFlags.VerticalCenter);
            }
        };

        var start = Math.Clamp(combo.SelectedIndex, -1, list.Items.Count - 1);
        list.SelectedIndex = start;
        list.TopIndex = Math.Max(0, start - rows / 2);

        // In a panel of its own, as in ColumnsPopover: the slim bar parks
        // beside its list, in the list's parent, and a menu takes no
        // controls beyond its items.
        var panel = new Panel { Size = list.Size, BackColor = AppTheme.Surface, Margin = Padding.Empty };
        panel.Controls.Add(list);
        ThinScrollBar.Attach(list);

        var host = new ToolStripControlHost(panel)
        {
            AutoSize = false,
            Size = panel.Size,
            Margin = new Padding(margin, 0, margin, 0),
            Padding = Padding.Empty,
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add(host);
        AppTheme.StyleContextMenu(menu);
        menu.ShowImageMargin = false;
        host.BackColor = AppTheme.Surface;

        var picked = -1;

        void Pick(int index)
        {
            picked = index;
            menu.Close(ToolStripDropDownCloseReason.ItemClicked);
        }

        // The highlight follows the pointer, as on Windows' own list.
        list.MouseMove += (_, e) =>
        {
            var at = list.IndexFromPoint(e.Location);

            if (at >= 0 && at != list.SelectedIndex)
            {
                list.SelectedIndex = at;
            }
        };

        list.MouseUp += (_, e) =>
        {
            var at = list.IndexFromPoint(e.Location);

            if (e.Button == MouseButtons.Left && at >= 0)
            {
                Pick(at);
            }
        };

        list.Picked += () => Pick(list.SelectedIndex);
        list.Escaped += () => menu.Close(ToolStripDropDownCloseReason.Keyboard);

        menu.Opened += (_, _) =>
        {
            // The choice in the middle of the rows shown - set again now the
            // list has its window, which put it on the last row.
            list.TopIndex = Math.Max(0, start - rows / 2);
            list.Focus();
        };

        menu.Closed += (_, _) =>
        {
            Shown.Remove(combo);
            ClosedAt.AddOrUpdate(combo, new StrongBox<long>(Environment.TickCount64));

            if (!combo.IsDisposed)
            {
                if (picked >= 0 && picked < combo.Items.Count && picked != combo.SelectedIndex)
                {
                    combo.SelectedIndex = picked;
                    RaiseCommitted?.Invoke(combo, new object[] { EventArgs.Empty });
                }

                RaiseDropDownClosed?.Invoke(combo, new object[] { EventArgs.Empty });

                if (combo.CanFocus)
                {
                    combo.Focus();
                }

                combo.BeginInvoke(new Action(menu.Dispose));
            }
            else
            {
                menu.Dispose();
            }
        };

        Shown.AddOrUpdate(combo, menu);
        menu.Show(combo, new Point(0, combo.Height));
    }

    /// <summary>The list in the drop-down: Enter and Tab pick, Esc leaves - keys a list box would otherwise keep or pass on.</summary>
    private sealed class PopupList : ListBox
    {
        public event Action? Picked;

        public event Action? Escaped;

        protected override bool IsInputKey(Keys keyData) =>
            keyData is Keys.Enter or Keys.Tab or Keys.Escape || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.Enter or Keys.Tab or Keys.F4 when SelectedIndex >= 0:
                    e.Handled = true;
                    Picked?.Invoke();
                    return;

                case Keys.Up or Keys.Down when e.Alt && SelectedIndex >= 0:
                    e.Handled = true;
                    Picked?.Invoke();
                    return;

                case Keys.Escape:
                    e.Handled = true;
                    Escaped?.Invoke();
                    return;
            }

            base.OnKeyDown(e);
        }
    }
}
