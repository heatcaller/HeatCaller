using System.Reflection;
using System.Runtime.InteropServices;

namespace HeatCaller.Tests;

/// <summary>
/// A drop-down's long list in the app's own drop-down, with the slim bar
/// (24 Nov 2026, Asher: "on the cast tab I see native scroll bars when
/// selecting voices. go through each scroll menu app wide and make sure you
/// can't see them"). See ComboListPopup.
/// </summary>
public sealed class ComboListPopupTests
{
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct COMBOBOXINFO
    {
        public int cbSize; public RECT rcItem; public RECT rcButton; public int stateButton;
        public IntPtr hwndCombo; public IntPtr hwndItem; public IntPtr hwndList;
    }

    [DllImport("user32.dll")] private static extern bool GetComboBoxInfo(IntPtr hWnd, ref COMBOBOXINFO info);
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    /// <summary>Windows' own list for this combo, on screen with its scroll bar showing.</summary>
    private static bool NativeListScrolls(ComboBox combo)
    {
        var info = new COMBOBOXINFO { cbSize = Marshal.SizeOf<COMBOBOXINFO>() };

        return GetComboBoxInfo(combo.Handle, ref info)
            && IsWindowVisible(info.hwndList)
            && (GetWindowLong(info.hwndList, -16) & 0x00200000) != 0;
    }

    private static (Form Form, ComboBox Combo) Window(int choices)
    {
        var form = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(200, 200), Size = new Size(400, 300), ShowInTaskbar = false };
        var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(10, 10), Width = 250 };

        for (var i = 0; i < choices; i++)
        {
            combo.Items.Add("Voice " + i);
        }

        combo.SelectedIndex = 2;
        form.Controls.Add(combo);
        AppTheme.ApplyRecursively(form);
        form.Show();
        StaThread.Settle();
        return (form, combo);
    }

    [Fact]
    public void A_long_list_opens_in_the_apps_own_drop_down_with_the_slim_bar_and_Enter_picks()
    {
        var seen = StaThread.Run(() =>
        {
            var (form, combo) = Window(31);

            using (form)
            {
                var committed = 0;
                combo.SelectionChangeCommitted += (_, _) => committed++;

                combo.DroppedDown = true;
                StaThread.Settle();

                var list = ComboListPopup.OpenList(combo);
                var own = ComboListPopup.IsOpen(combo);
                var droppedReads = combo.DroppedDown;
                var native = NativeListScrolls(combo);
                var slimBar = list?.Parent?.Controls.Cast<Control>().Any(c => c.GetType().Name == "ThinScrollBar" && c.Visible) == true;
                var startsOn = list?.SelectedIndex;

                SendMessage(list!.Handle, 0x0100, (IntPtr)(int)Keys.Down, IntPtr.Zero);
                SendMessage(list.Handle, 0x0100, (IntPtr)(int)Keys.Enter, IntPtr.Zero);
                StaThread.Settle();

                return (own, droppedReads, native, slimBar, startsOn, picked: combo.SelectedIndex, committed, closed: !ComboListPopup.IsOpen(combo) && !combo.DroppedDown);
            }
        }, TimeSpan.FromSeconds(60), () => "a long drop-down list");

        Assert.True(seen.own, "the app's own list");
        Assert.True(seen.droppedReads, "DroppedDown reads true while it is open");
        Assert.False(seen.native, "Windows' own list never shows its scroll bar");
        Assert.True(seen.slimBar, "the slim bar beside the list");
        Assert.Equal(2, seen.startsOn);
        Assert.Equal(3, seen.picked);
        Assert.Equal(1, seen.committed);
        Assert.True(seen.closed);
    }

    [Fact]
    public void Esc_leaves_the_choice_as_it_was_and_a_click_on_a_row_picks_it()
    {
        var seen = StaThread.Run(() =>
        {
            var (form, combo) = Window(31);

            using (form)
            {
                var committed = 0;
                combo.SelectionChangeCommitted += (_, _) => committed++;

                combo.DroppedDown = true;
                StaThread.Settle();
                var list = ComboListPopup.OpenList(combo)!;
                SendMessage(list.Handle, 0x0100, (IntPtr)(int)Keys.Down, IntPtr.Zero);
                SendMessage(list.Handle, 0x0100, (IntPtr)(int)Keys.Escape, IntPtr.Zero);
                StaThread.Settle();
                var afterEsc = (combo.SelectedIndex, committed, open: ComboListPopup.IsOpen(combo));

                combo.DroppedDown = true;
                StaThread.Settle();
                list = ComboListPopup.OpenList(combo)!;
                var row = list.GetItemRectangle(list.TopIndex + 1);
                var lParam = (IntPtr)(((row.Top + row.Height / 2) << 16) | (row.Left + 5));
                var rowIndex = list.TopIndex + 1;
                SendMessage(list.Handle, 0x0200, IntPtr.Zero, lParam);
                SendMessage(list.Handle, 0x0201, (IntPtr)1, lParam);
                SendMessage(list.Handle, 0x0202, IntPtr.Zero, lParam);
                StaThread.Settle();

                return (afterEsc, rowIndex, clicked: combo.SelectedIndex, committed, open: ComboListPopup.IsOpen(combo));
            }
        }, TimeSpan.FromSeconds(60), () => "Esc and a click");

        Assert.Equal((2, 0, false), seen.afterEsc);
        Assert.Equal(seen.rowIndex, seen.clicked);
        Assert.Equal(1, seen.committed);
        Assert.False(seen.open);
    }

    /// <summary>A table's drop-down cell (the CSV import's column pickers): the pick lands in the cell, and the table's edit survives the list taking the keys.</summary>
    [Fact]
    public void A_long_list_in_a_tables_drop_down_cell_puts_the_pick_in_the_cell()
    {
        var seen = StaThread.Run(() =>
        {
            using var form = new Form { StartPosition = FormStartPosition.Manual, Location = new Point(200, 200), Size = new Size(500, 300), ShowInTaskbar = false };
            var grid = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false };
            var column = new DataGridViewComboBoxColumn { Name = "pick", Width = 200 };

            for (var i = 0; i < 20; i++)
            {
                column.Items.Add("Column " + i);
            }

            grid.Columns.Add(column);
            grid.Rows.Add("Column 3");
            form.Controls.Add(grid);
            AppTheme.ApplyRecursively(form);
            form.Show();
            StaThread.Settle();

            grid.CurrentCell = grid.Rows[0].Cells[0];
            grid.BeginEdit(selectAll: true);
            StaThread.Settle();

            var combo = (ComboBox)grid.EditingControl!;
            combo.DroppedDown = true;
            StaThread.Settle();

            var list = ComboListPopup.OpenList(combo);
            var own = list is not null;

            if (list is not null)
            {
                SendMessage(list.Handle, 0x0100, (IntPtr)(int)Keys.Down, IntPtr.Zero);
                SendMessage(list.Handle, 0x0100, (IntPtr)(int)Keys.Enter, IntPtr.Zero);
            }

            StaThread.Settle();
            grid.EndEdit();
            StaThread.Settle();

            return (own, value: grid.Rows[0].Cells[0].Value as string);
        }, TimeSpan.FromSeconds(60), () => "a long list in a table's cell");

        Assert.True(seen.own);
        Assert.Equal("Column 4", seen.value);
    }

    [Fact]
    public void A_short_list_opens_as_it_always_has()
    {
        var seen = StaThread.Run(() =>
        {
            var (form, combo) = Window(5);

            using (form)
            {
                combo.DroppedDown = true;
                StaThread.Settle();
                var own = ComboListPopup.IsOpen(combo);
                var dropped = combo.DroppedDown;
                combo.DroppedDown = false;
                return (own, dropped);
            }
        }, TimeSpan.FromSeconds(60), () => "a short drop-down list");

        Assert.False(seen.own, "Windows' own list: it never scrolls");
        Assert.True(seen.dropped);
    }

    /// <summary>
    /// Every drop-down on every page and tab of the main window, opened: none
    /// of Windows' lists shows its scroll bar. Run on Asher's data
    /// (HC_FLASH_DATA) it found nine before: the six voice pickers on Cast,
    /// Lap Times' and Pace's race pickers, and Club Website's time zone.
    /// </summary>
    [Fact]
    public void No_drop_down_anywhere_opens_with_Windows_scroll_bar()
    {
        using var data = new TempAppData();

        if (Environment.GetEnvironmentVariable("HC_FLASH_DATA") is { Length: > 0 } from && Directory.Exists(from))
        {
            foreach (var file in Directory.GetFiles(from, "*.json"))
            {
                File.Copy(file, data.PathOf(Path.GetFileName(file)), overwrite: true);
            }
        }

        var settings = AppSettings.Load();
        settings.StartMaximized = false;
        settings.AutoBackupEnabled = false;
        settings.ConnectDecodersAtStartup = false;
        settings.HasCompletedFirstRun = true;
        settings.HasSeenRaceDaySetup = true;
        settings.Save();

        var found = StaThread.Run(() =>
        {
            var bad = new List<string>();
            var window = new MainForm { LayoutCheckMode = true, StartPosition = FormStartPosition.Manual, Location = new Point(-20000, -20000) };
            var seenCombos = new HashSet<ComboBox>();
            static IEnumerable<Control> All(Control root) => root.Controls.Cast<Control>().SelectMany(c => All(c).Prepend(c));

            void OpenEach(string where)
            {
                foreach (var combo in All(window).OfType<ComboBox>().Where(c => c.Visible && c.Enabled && c.Items.Count > 0 && seenCombos.Add(c)).ToList())
                {
                    try
                    {
                        combo.DroppedDown = true;
                        StaThread.Settle();

                        if (NativeListScrolls(combo))
                        {
                            bad.Add($"{where}: a drop-down of {combo.Items.Count} choices, the first \"{combo.GetItemText(combo.Items[0])}\"");
                        }
                    }
                    finally
                    {
                        combo.DroppedDown = false;
                        ComboListPopup.Close(combo);
                        StaThread.Settle();
                    }
                }
            }

            try
            {
                window.Show();
                window.ClientSize = new Size(1280, 800);
                StaThread.Settle();

                var go = typeof(MainForm).GetMethod("GoToPage", BindingFlags.Instance | BindingFlags.NonPublic)!;
                var navs = (System.Collections.IEnumerable)typeof(MainForm).GetField("_navItems", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;

                foreach (var nav in navs.Cast<object>().ToList())
                {
                    var name = (string)nav.GetType().GetProperty("Name")!.GetValue(nav)!;
                    var page = (Control)nav.GetType().GetProperty("Page")!.GetValue(nav)!;
                    go.Invoke(window, new object[] { name });
                    StaThread.Settle();
                    OpenEach(name);

                    foreach (var tabs in All(page).OfType<TabControl>().Where(t => t.Visible).ToList())
                    {
                        foreach (TabPage tab in tabs.TabPages)
                        {
                            tabs.SelectedTab = tab;
                            StaThread.Settle();
                            OpenEach(name + " > " + tab.Text);
                        }
                    }
                }

                return bad;
            }
            finally
            {
                window.Close();
                window.Dispose();
                StaThread.Settle();
            }
        }, TimeSpan.FromMinutes(8), () => "opening every drop-down");

        Assert.True(found.Count == 0, "Windows' own list showed its scroll bar:\n" + string.Join("\n", found));
    }
}
